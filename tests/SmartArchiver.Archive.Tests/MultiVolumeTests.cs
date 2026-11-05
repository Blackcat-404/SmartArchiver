using SmartArchiver.Compression;

namespace SmartArchiver.Archive.Tests;

/// <summary>Запис і читання архіву кількома томами, пакування рюкзаком.</summary>
public class MultiVolumeTests
{
    private static List<ArchiveInput> SixRandomFiles() =>
        [.. Enumerable.Range(0, 6).Select(i => TestData.File($"f{i}.bin", TestData.RandomBytes(3000, i + 1)))];

    private static void AssertSameFiles(IReadOnlyList<ArchiveInput> inputs, IReadOnlyList<ExtractedFile> restored)
    {
        Assert.Equal(inputs.Count, restored.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            Assert.Equal(inputs[i].Name, restored[i].Name);
            Assert.Equal(inputs[i].Data, restored[i].Data);
        }
    }

    [Theory]
    [InlineData(PackingAlgorithm.Dp)]
    [InlineData(PackingAlgorithm.Greedy)]
    public void Volumes_AreSplitByCapacity_AndReadBackInAnyOrder(PackingAlgorithm algorithm)
    {
        List<ArchiveInput> inputs = SixRandomFiles();
        const int max = 5000;

        IReadOnlyList<byte[]> volumes = new ArchiveWriter(1000).BuildVolumes(inputs, new VolumeOptions(max, algorithm));

        Assert.True(volumes.Count > 1);
        Assert.All(volumes, v => Assert.True(v.Length + FormatConstants.TagSize <= max, $"том {v.Length} байт"));
        AssertSameFiles(inputs, ArchiveReader.ReadVolumes(volumes));
        AssertSameFiles(inputs, ArchiveReader.ReadVolumes([.. volumes.Reverse()]));
        AssertSameFiles(inputs, ArchiveReader.ReadVolumes([volumes[1], volumes[^1], volumes[0], .. volumes.Skip(2).Take(volumes.Count - 3)]));
    }

    [Fact]
    public void VolumeHeaders_CarryNumberAndCount()
    {
        IReadOnlyList<byte[]> volumes = new ArchiveWriter(1000).BuildVolumes(SixRandomFiles(), new VolumeOptions(5000));

        for (int i = 0; i < volumes.Count; i++)
        {
            VolumeHeader header = VolumeHeader.Parse(volumes[i]);
            Assert.Equal(i, header.VolumeIndex);
            Assert.Equal(volumes.Count, header.VolumeCount);
        }
    }

    [Fact]
    public void CompressedVolumes_RoundTrip_AndUseCodedSizes()
    {
        var inputs = new List<ArchiveInput>
        {
            TestData.File("text.txt", TestData.Repetitive(20_000), FileKind.Text),
            TestData.File("noise.bin", TestData.RandomBytes(6000, 8), FileKind.AlreadyCompressed),
            TestData.File("empty.txt", [], FileKind.Text),
            TestData.File("more.txt", TestData.Repetitive(9000), FileKind.SourceCode),
        };
        var writer = new ArchiveWriter(2048, new FixedAnalyzer(FileKind.Text, CompressionStrategy.Lz77Huffman));

        IReadOnlyList<byte[]> volumes = writer.BuildVolumes(inputs, new VolumeOptions(4096));

        long total = volumes.Sum(v => (long)v.Length);
        Assert.True(total < 20_000 + 6000 + 9000, $"усі томи разом {total} байт");
        AssertSameFiles(inputs, ArchiveReader.ReadVolumes(volumes));
    }

    [Fact]
    public void EmptyFiles_LiveInFirstVolume_AndSurvive()
    {
        var inputs = new List<ArchiveInput>
        {
            TestData.File("e1", []),
            TestData.File("big.bin", TestData.RandomBytes(4000, 2)),
            TestData.File("e2", []),
        };

        IReadOnlyList<byte[]> volumes = new ArchiveWriter(1000).BuildVolumes(inputs, new VolumeOptions(2500));

        Assert.True(volumes.Count > 1);
        AssertSameFiles(inputs, ArchiveReader.ReadVolumes(volumes));
    }

    [Fact]
    public void NoFiles_GiveOneVolume()
    {
        IReadOnlyList<byte[]> volumes = new ArchiveWriter().BuildVolumes([], new VolumeOptions(1000));

        Assert.Single(volumes);
        Assert.Empty(ArchiveReader.ReadVolumes(volumes));
    }

    [Fact]
    public void BlockThatDoesNotFitAnyVolume_Is_E_CAPACITY()
    {
        var inputs = new[] { TestData.File("big.bin", TestData.RandomBytes(500)) };

        ArchiveException ex = TestData.AssertCode(ErrorCode.E_CAPACITY,
            () => new ArchiveWriter(500).BuildVolumes(inputs, new VolumeOptions(300)));

        Assert.Contains("515", ex.Message, StringComparison.Ordinal);   // 500 + 15 байт заголовка блока
    }

    [Fact]
    public void BlockThatFitsOnlyAfterSmallerBlockSize_ProducesVolumes()
    {
        var inputs = new[] { TestData.File("big.bin", TestData.RandomBytes(500)) };

        TestData.AssertCode(ErrorCode.E_CAPACITY, () => new ArchiveWriter(500).BuildVolumes(inputs, new VolumeOptions(300)));
        IReadOnlyList<byte[]> volumes = new ArchiveWriter(50).BuildVolumes(inputs, new VolumeOptions(300));

        Assert.True(volumes.Count > 1);
        AssertSameFiles(inputs, ArchiveReader.ReadVolumes(volumes));
    }

    // ---------- Читач: набір томів ----------

    private static IReadOnlyList<byte[]> ThreeVolumes() =>
        new ArchiveWriter(1000).BuildVolumes(SixRandomFiles(), new VolumeOptions(8000));

    [Fact]
    public void ReadVolumes_NoVolumes_E_VOLUMES()
    {
        TestData.AssertCode(ErrorCode.E_VOLUMES, () => ArchiveReader.ReadVolumes([]));
    }

    [Fact]
    public void ReadVolumes_MissingVolume_E_VOLUMES()
    {
        IReadOnlyList<byte[]> volumes = ThreeVolumes();
        Assert.True(volumes.Count >= 2);

        TestData.AssertCode(ErrorCode.E_VOLUMES, () => ArchiveReader.ReadVolumes([.. volumes.Skip(1)]));
    }

    [Fact]
    public void ReadVolumes_DuplicateVolume_E_VOLUMES()
    {
        IReadOnlyList<byte[]> volumes = ThreeVolumes();

        TestData.AssertCode(ErrorCode.E_VOLUMES, () => ArchiveReader.ReadVolumes([volumes[0], volumes[0], .. volumes.Skip(2)]));
    }

    [Fact]
    public void ReadVolumes_VolumesOfDifferentArchives_E_VOLUMES()
    {
        IReadOnlyList<byte[]> three = ThreeVolumes();
        IReadOnlyList<byte[]> other = new ArchiveWriter(1000).BuildVolumes(SixRandomFiles(), new VolumeOptions(5000));
        Assert.NotEqual(three.Count, other.Count);

        TestData.AssertCode(ErrorCode.E_VOLUMES, () => ArchiveReader.ReadVolumes([three[0], other[1]]));
    }

    [Fact]
    public void Read_OfOneVolumeFromMultiVolumeArchive_IsRejected()
    {
        IReadOnlyList<byte[]> volumes = ThreeVolumes();

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(volumes[0]));
    }

    [Fact]
    public void ReadVolumes_SingleVolumeArchive_Works()
    {
        List<ArchiveInput> inputs = SixRandomFiles();
        byte[] single = new ArchiveWriter(1000).Build(inputs);

        AssertSameFiles(inputs, ArchiveReader.ReadVolumes([single]));
    }

    [Fact]
    public void Reader_AnyChangedByteInVolumes_OnlyArchiveException()
    {
        var inputs = new List<ArchiveInput>
        {
            TestData.File("a.txt", TestData.Repetitive(400)),
            TestData.File("b.bin", TestData.RandomBytes(150, 5)),
        };
        var writer = new ArchiveWriter(100, new FixedAnalyzer(FileKind.Text, CompressionStrategy.Lz77Huffman));
        IReadOnlyList<byte[]> volumes = writer.BuildVolumes(inputs, new VolumeOptions(400));
        Assert.True(volumes.Count > 1);

        for (int v = 0; v < volumes.Count; v++)
        {
            for (int i = 0; i < volumes[v].Length; i++)
            {
                foreach (byte mask in new byte[] { 0x01, 0xFF })
                {
                    var copy = volumes.Select(x => (byte[])x.Clone()).ToList();
                    copy[v][i] ^= mask;
                    try
                    {
                        ArchiveReader.ReadVolumes(copy);
                    }
                    catch (ArchiveException)
                    {
                        // очікувана контрольована відмова
                    }
                }
            }
        }
    }
}
