namespace SmartArchiver.Archive.Tests;

/// <summary>Повний тракт: аналіз → блоки → LZ77 → Гаффман → пакування → AES → HMAC → томи і назад.</summary>
public class PipelineTests
{
    private static readonly FixedAnalyzer Lz = new(FileKind.Text, CompressionStrategy.Lz77Huffman);

    [Fact]
    public void FullPipeline_RoundTrips_AndCompressesRepetitiveData()
    {
        List<ArchiveInput> files =
        [
            TestData.File("text.txt", TestData.Repetitive(20_000)),
            TestData.File("noise.bin", TestData.RandomBytes(5_000, 3)),
        ];
        var options = new PackOptions("пароль", Lz, BlockSize: 4096);

        IReadOnlyList<byte[]> volumes = ArchivePipeline.Pack(files, options);
        IReadOnlyList<ExtractedFile> restored = ArchivePipeline.Unpack(volumes, "пароль");

        Assert.Single(volumes);
        Assert.True(volumes[0].Length < 20_000, "стиснений і зашифрований архів має бути меншим за вхід");
        Assert.Equal(files[0].Data, restored.Single(f => f.Name == "text.txt").Data);
        Assert.Equal(files[1].Data, restored.Single(f => f.Name == "noise.bin").Data);
    }

    [Fact]
    public void Plaintext_IsNotVisibleInSealedVolume()
    {
        byte[] data = TestData.Repetitive(2_000);
        byte[] volume = ArchivePipeline.Pack([TestData.File("a.txt", data)], new PackOptions("k", new FixedAnalyzer(FileKind.Text, CompressionStrategy.None)))[0];

        Assert.DoesNotContain("hello world", System.Text.Encoding.ASCII.GetString(volume));
    }

    [Fact]
    public void Volumes_AreEncrypted_UnderOneSalt_WithDistinctNonces()
    {
        List<ArchiveInput> files = [.. Enumerable.Range(0, 6).Select(i => TestData.File($"f{i}.bin", TestData.RandomBytes(3000, i + 1)))];
        var options = new PackOptions("k", new FixedAnalyzer(FileKind.Unknown, CompressionStrategy.None), BlockSize: 1000, MaxVolumeBytes: 5000);

        IReadOnlyList<byte[]> volumes = ArchivePipeline.Pack(files, options);
        var headers = volumes.Select(v => VolumeHeader.Parse(v)).ToList();

        Assert.True(volumes.Count > 1);
        Assert.Single(headers.Select(h => Convert.ToHexString(h.Salt)).Distinct());
        Assert.Equal(headers.Count, headers.Select(h => Convert.ToHexString(h.Nonce)).Distinct().Count());
        Assert.All(volumes, v => Assert.True(v.Length <= 5000));
        Assert.Equal(files.Count, ArchivePipeline.Unpack([.. volumes.Reverse()], "k").Count);
    }

    [Fact]
    public void BlockLargerThanVolume_IsCapacityError()
    {
        var options = new PackOptions("k", new FixedAnalyzer(FileKind.Unknown, CompressionStrategy.None), BlockSize: 1000, MaxVolumeBytes: 100);

        Assert.Equal(ErrorCode.E_CAPACITY,
            Assert.Throws<ArchiveException>(() => ArchivePipeline.Pack([TestData.File("a.bin", TestData.RandomBytes(1000))], options)).Code);
    }
}
