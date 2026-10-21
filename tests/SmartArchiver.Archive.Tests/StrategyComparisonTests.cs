using System.Text;

namespace SmartArchiver.Archive.Tests;

public class StrategyComparisonTests
{
    private static readonly byte[] Text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("plain text line that repeats\n", 400)));

    private static byte[] Noise(int length)
    {
        var bytes = new byte[length];
        new Random(4).NextBytes(bytes);
        return bytes;
    }

    private static IReadOnlyList<GroupedFile> Files() =>
    [
        new(0, new ArchiveInput("notes.txt", Text)),
        new(0, new ArchiveInput("more.txt", Text[..5000])),
        new(5, new ArchiveInput("noise.bin", Noise(20_000))),
    ];

    /// <summary>Classifier stand-in: text is compressed, everything else is stored.</summary>
    private sealed class TextOnlyAnalyzer : IFileAnalyzer
    {
        public FileAnalysis Analyze(ReadOnlySpan<byte> data) =>
            data.IndexOf((byte)'\n') >= 0
                ? new FileAnalysis(FileKind.Text, 0.9, CompressionStrategy.Lz77Huffman)
                : new FileAnalysis(FileKind.AlreadyCompressed, 0.9, CompressionStrategy.NoCompression);
    }

    [Fact]
    public void Run_GivesEveryVariantForEveryGroup()
    {
        IReadOnlyList<StrategyMeasurement> results = StrategyComparison.Run(Files(), new TextOnlyAnalyzer());

        Assert.Equal(
            [(0, StrategyVariant.Stored), (0, StrategyVariant.Fixed), (0, StrategyVariant.Classifier),
             (5, StrategyVariant.Stored), (5, StrategyVariant.Fixed), (5, StrategyVariant.Classifier)],
            results.Select(r => (r.Group, r.Variant)));
    }

    [Fact]
    public void Run_EveryVariantRestoresEveryFile()
    {
        Assert.All(StrategyComparison.Run(Files(), new TextOnlyAnalyzer()), r => Assert.True(r.AllRestored));
    }

    [Fact]
    public void Run_CountsFilesAndOriginalBytesPerGroup()
    {
        StrategyMeasurement text = StrategyComparison.Run(Files(), new TextOnlyAnalyzer())
            .First(r => r.Group == 0 && r.Variant == StrategyVariant.Stored);

        Assert.Equal(2, text.FileCount);
        Assert.Equal(Text.Length + 5000, text.OriginalBytes);
    }

    [Fact]
    public void Run_StoredCompressesNothing_FixedAndClassifierCompressText()
    {
        IReadOnlyList<StrategyMeasurement> results = StrategyComparison.Run(Files(), new TextOnlyAnalyzer());
        StrategyMeasurement Get(int group, StrategyVariant variant) => results.Single(r => r.Group == group && r.Variant == variant);

        Assert.Equal(0, Get(0, StrategyVariant.Stored).CompressedFiles);
        Assert.Equal(2, Get(0, StrategyVariant.Fixed).CompressedFiles);
        Assert.Equal(2, Get(0, StrategyVariant.Classifier).CompressedFiles);
        Assert.True(Get(0, StrategyVariant.Fixed).ArchiveBytes < Get(0, StrategyVariant.Stored).ArchiveBytes);
        Assert.Equal(Get(0, StrategyVariant.Fixed).ArchiveBytes, Get(0, StrategyVariant.Classifier).ArchiveBytes);
    }

    [Fact]
    public void Run_NoiseIsNeverCompressed_AndFixedGainsNothing()
    {
        // Fixed tries LZ77 on noise, finds no gain and stores the blocks; the classifier skips the attempt.
        IReadOnlyList<StrategyMeasurement> results = StrategyComparison.Run(Files(), new TextOnlyAnalyzer());

        Assert.All(results.Where(r => r.Group == 5), r => Assert.Equal(0, r.CompressedFiles));
        Assert.Equal(
            results.Single(r => r.Group == 5 && r.Variant == StrategyVariant.Stored).ArchiveBytes,
            results.Single(r => r.Group == 5 && r.Variant == StrategyVariant.Fixed).ArchiveBytes);
    }

    [Fact]
    public void Run_RepeatsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategyComparison.Run(Files(), new TextOnlyAnalyzer(), repeats: 0));
    }

    [Fact]
    public void Median_OfOddAndEvenCounts()
    {
        Assert.Equal(2.0, StrategyComparison.Median([5.0, 1.0, 2.0]));
        Assert.Equal(2.5, StrategyComparison.Median([4.0, 1.0, 3.0, 2.0]));
    }

    [Fact]
    public void Run_TimesAreMeasured()
    {
        Assert.All(StrategyComparison.Run(Files(), new TextOnlyAnalyzer()),
            r => Assert.True(r.PackSeconds > 0 && r.UnpackSeconds > 0));
    }
}
