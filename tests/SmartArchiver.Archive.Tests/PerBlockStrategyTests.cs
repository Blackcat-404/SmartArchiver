using System.Text;

namespace SmartArchiver.Archive.Tests;

public class PerBlockStrategyTests
{
    private const int Block = 16_384;

    /// <summary>Classifier stand-in: high entropy is "already compressed", the rest is text. Counts its calls.</summary>
    private sealed class EntropyAnalyzer : IFileAnalyzer
    {
        public List<int> CallLengths { get; } = [];

        public FileAnalysis Analyze(ReadOnlySpan<byte> data)
        {
            CallLengths.Add(data.Length);
            return Entropy(data) > 7.0
                ? new FileAnalysis(FileKind.AlreadyCompressed, 0.9, CompressionStrategy.NoCompression)
                : new FileAnalysis(FileKind.Text, 0.9, CompressionStrategy.Lz77Huffman);
        }

        private static double Entropy(ReadOnlySpan<byte> data)
        {
            var counts = new int[256];
            foreach (byte b in data)
                counts[b]++;
            int length = data.Length;
            return -counts.Where(c => c > 0).Sum(c => (double)c / length * Math.Log2((double)c / length));
        }
    }

    private static byte[] Text(int length) =>
        Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("a line of text that repeats over and over\n", length / 40 + 1)))[..length];

    private static byte[] Noise(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>A document with an embedded picture: text, compressed data, text.</summary>
    private static byte[] Mixed() => [.. Text(Block), .. Noise(Block, 1), .. Text(Block)];

    /// <summary>Mostly compressed data with one block of text, so the whole file looks compressed.</summary>
    private static byte[] MostlyCompressed() => [.. Noise(Block, 2), .. Text(Block), .. Noise(Block, 3), .. Noise(Block, 4)];

    private static PreparedFile Prepare(byte[] data, StrategyScope scope, IFileAnalyzer analyzer, FileKind kind = FileKind.Unknown) =>
        new ArchiveWriter(Block, analyzer, scope: scope).Prepare([new ArchiveInput("file.bin", data, kind)])[0];

    [Fact]
    public void FileScope_IsTheDefault()
    {
        Assert.Equal(StrategyScope.File, new ArchiveWriter().Scope);
        Assert.Equal(StrategyScope.File, new PackOptions("p", new EntropyAnalyzer()).Scope);
    }

    [Fact]
    public void BlockScope_EveryBlockGetsItsOwnStrategy()
    {
        PreparedFile file = Prepare(Mixed(), StrategyScope.Block, new EntropyAnalyzer());

        Assert.Equal([false, true, false], file.Blocks.Select(b => b.IsStored));
        Assert.Equal(CompressionStrategy.Lz77Huffman, file.Strategy);
    }

    [Fact]
    public void BlockScope_AnalyzesTheWholeFileForItsTypeAndEachBlockForItsStrategy()
    {
        var analyzer = new EntropyAnalyzer();

        PreparedFile file = Prepare(MostlyCompressed(), StrategyScope.Block, analyzer);

        Assert.Equal([4 * Block, Block, Block, Block, Block], analyzer.CallLengths);
        Assert.Equal(FileKind.AlreadyCompressed, file.Kind);
    }

    [Fact]
    public void BlockScope_KindGivenByTheCaller_IsKept()
    {
        Assert.Equal(FileKind.Executable, Prepare(Mixed(), StrategyScope.Block, new EntropyAnalyzer(), FileKind.Executable).Kind);
    }

    [Fact]
    public void BlockScope_FileThatLooksCompressed_StillCompressesItsTextBlock()
    {
        // Per file the whole file is stored; per block the text inside it is found and compressed.
        PreparedFile perFile = Prepare(MostlyCompressed(), StrategyScope.File, new EntropyAnalyzer());
        PreparedFile perBlock = Prepare(MostlyCompressed(), StrategyScope.Block, new EntropyAnalyzer());

        Assert.Equal(CompressionStrategy.NoCompression, perFile.Strategy);
        Assert.True(perFile.Blocks.All(b => b.IsStored));
        Assert.Equal([true, false, true, true], perBlock.Blocks.Select(b => b.IsStored));
        Assert.True(perBlock.CodedSize < perFile.CodedSize - Block / 2);
    }

    /// <summary>Says "do not compress" about everything, even text that would shrink well.</summary>
    private sealed class NeverCompress : IFileAnalyzer
    {
        public FileAnalysis Analyze(ReadOnlySpan<byte> data) => new(FileKind.Text, 0.9, CompressionStrategy.NoCompression);
    }

    [Fact]
    public void BlockScope_BlockTheAnalyzerRejects_IsNotCompressedEvenIfItWouldShrink()
    {
        PreparedFile file = Prepare(Text(3 * Block), StrategyScope.Block, new NeverCompress());

        Assert.True(file.Blocks.All(b => b.IsStored));
        Assert.Equal(3 * Block, file.CodedSize);
    }

    [Fact]
    public void BlockScope_NoBlockCompressed_FileIsMarkedNoCompression()
    {
        PreparedFile file = Prepare(Noise(3 * Block, 5), StrategyScope.Block, new EntropyAnalyzer());

        Assert.Equal(CompressionStrategy.NoCompression, file.Strategy);
        Assert.True(file.Blocks.All(b => b.IsStored));
    }

    [Fact]
    public void BlockScope_EmptyFile_HasNoBlocks()
    {
        PreparedFile file = Prepare([], StrategyScope.Block, new EntropyAnalyzer());

        Assert.Empty(file.Blocks);
        Assert.Equal(CompressionStrategy.NoCompression, file.Strategy);
    }

    [Fact]
    public void BlockScope_WithoutAnalyzer_StoresEverything()
    {
        PreparedFile file = new ArchiveWriter(Block, analyzer: null, scope: StrategyScope.Block)
            .Prepare([new ArchiveInput("file.bin", Mixed())])[0];

        Assert.True(file.Blocks.All(b => b.IsStored));
    }

    [Fact]
    public void BlockScope_WithDpSplit_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new ArchiveWriter(Block, new EntropyAnalyzer(), splitMode: BlockSplitMode.Dp, scope: StrategyScope.Block));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveWriter(Block, scope: (StrategyScope)9));
    }

    [Fact]
    public void BlockScope_ThroughTheFullPipeline_RestoresEveryFile()
    {
        var options = new PackOptions("test-password", new EntropyAnalyzer(), BlockSize: Block, Scope: StrategyScope.Block);
        ArchiveInput[] files = [new("mixed.bin", Mixed()), new("mostly.bin", MostlyCompressed()), new("empty.bin", [])];

        IReadOnlyList<ExtractedFile> restored = ArchivePipeline.Unpack(ArchivePipeline.Pack(files, options), options.Password);

        Assert.Equal(files.Select(f => f.Data), restored.Select(f => f.Data));
        Assert.Equal(
            [CompressionStrategy.Lz77Huffman, CompressionStrategy.Lz77Huffman, CompressionStrategy.NoCompression],
            restored.Select(f => f.Strategy));
    }
}
