using SmartArchiver.Analysis;
using SmartArchiver.Compression;

namespace SmartArchiver.Archive.Tests;

/// <summary>Аналіз і стиснення, підключені до запису архіву.</summary>
public class CompressedArchiveTests
{
    private static ArchiveWriter CompressingWriter(int blockSize = 1024, Lz77ParseMode mode = Lz77ParseMode.Lazy) =>
        new(blockSize, new FixedAnalyzer(FileKind.Text, CompressionStrategy.Lz77Huffman), mode);

    [Fact]
    public void CompressibleFile_ShrinksTheArchive_AndRoundTrips()
    {
        byte[] data = TestData.Repetitive(6000);

        byte[] archive = CompressingWriter().Build([TestData.File("a.txt", data)]);

        Assert.True(archive.Length < data.Length / 2, $"архів {archive.Length} байт, вихідний файл {data.Length}");
        var file = Assert.Single(ArchiveReader.Read(archive));
        Assert.Equal(data, file.Data);
        Assert.Equal(CompressionStrategy.Lz77Huffman, file.Strategy);
        Assert.Equal(FileKind.Text, file.Kind);
    }

    [Fact]
    public void Noise_WithRealAnalyzer_IsStored_AndCodedSizeEqualsOriginal()
    {
        byte[] noise = TestData.RandomBytes(8192);
        var writer = new ArchiveWriter(1024, new FileAnalyzer());

        PreparedFile file = Assert.Single(writer.Prepare([TestData.File("noise.bin", noise)]));

        Assert.Equal(CompressionStrategy.NoCompression, file.Strategy);
        Assert.Equal(noise.Length, file.CodedSize);
        Assert.All(file.Blocks, b => Assert.True(b.IsStored));
        byte[] archive = ArchiveWriter.BuildFromPrepared([file]);
        Assert.Equal(TestData.ExpectedLength("noise.bin", noise.Length, 1024), archive.Length);
        Assert.Equal(noise, Assert.Single(ArchiveReader.Read(archive)).Data);
    }

    [Fact]
    public void AnalyzerWantsCompression_ButNothingShrinks_FileIsMarkedUncompressed()
    {
        byte[] noise = TestData.RandomBytes(3000, 3);

        PreparedFile file = Assert.Single(CompressingWriter().Prepare([TestData.File("n.bin", noise)]));

        Assert.Equal(CompressionStrategy.NoCompression, file.Strategy);
        Assert.Equal(noise.Length, file.CodedSize);
    }

    [Fact]
    public void MixedBlocks_StoredAndCompressed_RoundTrip()
    {
        byte[] data = [.. TestData.Repetitive(1024), .. TestData.RandomBytes(1024, 9)];

        PreparedFile file = Assert.Single(CompressingWriter().Prepare([TestData.File("m.bin", data)]));
        byte[] archive = ArchiveWriter.BuildFromPrepared([file]);

        Assert.Equal(2, file.Blocks.Count);
        Assert.False(file.Blocks[0].IsStored);
        Assert.True(file.Blocks[1].IsStored);
        Assert.Equal(CompressionStrategy.Lz77Huffman, file.Strategy);
        var restored = Assert.Single(ArchiveReader.Read(archive));
        Assert.Equal(data, restored.Data);
        Assert.Equal(CompressionStrategy.Lz77Huffman, restored.Strategy);
    }

    [Fact]
    public void AnalyzerKind_IsUsedOnlyWhenInputKindIsUnknown()
    {
        var writer = new ArchiveWriter(1024, new FixedAnalyzer(FileKind.Executable, CompressionStrategy.NoCompression));

        IReadOnlyList<PreparedFile> files = writer.Prepare(
        [
            TestData.File("unknown.bin", [1, 2, 3]),
            TestData.File("given.txt", [1, 2, 3], FileKind.Text),
        ]);

        Assert.Equal(FileKind.Executable, files[0].Kind);
        Assert.Equal(FileKind.Text, files[1].Kind);
    }

    [Fact]
    public void EmptyFile_WithAnalyzer_HasNoBlocks()
    {
        PreparedFile file = Assert.Single(CompressingWriter().Prepare([TestData.File("e.txt", [])]));

        Assert.Empty(file.Blocks);
        Assert.Equal(CompressionStrategy.NoCompression, file.Strategy);
        Assert.Empty(Assert.Single(ArchiveReader.Read(ArchiveWriter.BuildFromPrepared([file]))).Data);
    }

    [Theory]
    [InlineData(Lz77ParseMode.Greedy)]
    [InlineData(Lz77ParseMode.Lazy)]
    [InlineData(Lz77ParseMode.Dp)]
    public void EveryParseMode_RoundTrips(Lz77ParseMode mode)
    {
        byte[] data = [.. TestData.Repetitive(3000), .. TestData.RandomBytes(300, 4), .. TestData.Repetitive(2000)];

        byte[] archive = CompressingWriter(700, mode).Build([TestData.File("x.bin", data)]);

        Assert.Equal(data, Assert.Single(ArchiveReader.Read(archive)).Data);
    }

    [Fact]
    public void DpBlockSplitting_RoundTrips()
    {
        byte[] data = [.. TestData.Repetitive(30_000), .. TestData.RandomBytes(5000, 6)];
        var writer = new ArchiveWriter(16_384, new FixedAnalyzer(FileKind.Text, CompressionStrategy.Lz77Huffman), splitMode: BlockSplitMode.Dp);

        byte[] archive = writer.Build([TestData.File("d.bin", data)]);

        Assert.Equal(data, Assert.Single(ArchiveReader.Read(archive)).Data);
    }

    [Fact]
    public void DpBlockSplitting_RequiresBlockSizeMultipleOfGrid()
    {
        Assert.Throws<ArgumentException>(() => new ArchiveWriter(1000, null, Lz77ParseMode.Lazy, BlockSplitMode.Dp));
    }

    [Fact]
    public void BlockSizeAboveLimit_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveWriter(FormatConstants.MaxBlockRawLength + 1));
    }

    [Fact]
    public void ProtectedArchive_WithAnalyzer_RoundTripsAndIsSmaller()
    {
        byte[] data = TestData.Repetitive(6000);
        var analyzer = new FixedAnalyzer(FileKind.Text, CompressionStrategy.Lz77Huffman);

        byte[] encrypted = ProtectedArchive.BuildProtected([TestData.File("a.txt", data)], "пароль", 1024, analyzer);

        Assert.True(encrypted.Length < data.Length / 2);
        Assert.Equal(data, Assert.Single(ProtectedArchive.ReadProtected(encrypted, "пароль")).Data);
    }

    // ---------- Читач: пошкоджені стиснені дані ----------

    private static byte[] SmallCompressedArchive() =>
        CompressingWriter(600).Build([TestData.File("a.txt", TestData.Repetitive(600))]);

    [Fact]
    public void Reader_AnyChangedByteInCompressedArchive_OnlyArchiveException()
    {
        byte[] original = SmallCompressedArchive();

        for (int i = 0; i < original.Length; i++)
        {
            foreach (byte mask in new byte[] { 0x01, 0x80, 0xFF })
            {
                byte[] mutated = (byte[])original.Clone();
                mutated[i] ^= mask;
                try
                {
                    ArchiveReader.Read(mutated);
                }
                catch (ArchiveException)
                {
                    // очікувана контрольована відмова
                }
            }
        }
    }

    [Fact]
    public void Reader_EveryTruncatedPrefixOfCompressedArchive_E_LENGTH()
    {
        byte[] archive = SmallCompressedArchive();

        for (int length = 0; length < archive.Length; length++)
        {
            byte[] truncated = archive.AsSpan(0, length).ToArray();
            TestData.AssertCode(ErrorCode.E_LENGTH, () => ArchiveReader.Read(truncated));
        }
    }

    [Fact]
    public void Reader_FileMarkedUncompressed_ButBlockCompressed_E_PAYLOAD()
    {
        // Розкладка для імені "a.txt": стратегія файлу на зсуві 65.
        byte[] archive = SmallCompressedArchive();
        archive[65] = (byte)CompressionStrategy.NoCompression;

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_HugeBlockRawLength_FailsWithoutHugeAllocation()
    {
        // Заголовок першого блока на зсуві 74, raw_len на 80..83.
        byte[] archive = SmallCompressedArchive();
        archive[80] = 0x7F;

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_UnknownBlockStrategy_E_CODE()
    {
        // Стратегія першого блока на зсуві 88.
        byte[] archive = SmallCompressedArchive();
        archive[88] = 200;

        TestData.AssertCode(ErrorCode.E_CODE, () => ArchiveReader.Read(archive));
    }
}
