using System.Text;

namespace SmartArchiver.Archive.Tests;

public class AblationExperimentsTests
{
    private const double Alpha = 0.5, Beta = 0.5, TRef = 1.0;

    private static readonly byte[] Text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("plain text line that repeats\n", 1_500)));

    private static byte[] Noise(int length)
    {
        var bytes = new byte[length];
        new Random(8).NextBytes(bytes);
        return bytes;
    }

    /// <summary>Classifier stand-in: text is compressed, everything else is stored.</summary>
    private sealed class TextOnlyAnalyzer : IFileAnalyzer
    {
        public FileAnalysis Analyze(ReadOnlySpan<byte> data) =>
            data.IndexOf((byte)'\n') >= 0
                ? new FileAnalysis(FileKind.Text, 0.9, CompressionStrategy.Lz77Huffman)
                : new FileAnalysis(FileKind.AlreadyCompressed, 0.9, CompressionStrategy.NoCompression);
    }

    private static IReadOnlyList<GroupedFile> Files() =>
    [
        new(0, new ArchiveInput("notes.txt", Text)),
        new(0, new ArchiveInput("short.txt", Text[..3_000])),
        new(5, new ArchiveInput("noise.bin", Noise(30_000))),
        new(0, new ArchiveInput("empty.txt", [])),
    ];

    private static readonly PackOptions Options = new("test-password", new TextOnlyAnalyzer(), BlockSize: 16_384, MaxVolumeBytes: 24_576);

    private static IReadOnlyList<BlockImpactMeasurement> Impact() =>
        BlockImpactComparison.Run(Files(), Options, Alpha, Beta, TRef, repeats: 1);

    // ------------------------------------------------------------------ block impact

    [Fact]
    public void BlockImpact_FullPipelineFirst_ThenEachBlockOffOnce()
    {
        Assert.Equal(
            [PipelineStage.None, PipelineStage.Analysis, PipelineStage.Lz77, PipelineStage.Compression,
             PipelineStage.Packing, PipelineStage.Encryption],
            Impact().Select(m => m.DisabledStage));
    }

    [Fact]
    public void BlockImpact_EveryVariantRestoresEveryFile_AndScoreIsQOfTheMeasurement()
    {
        Assert.All(Impact(), m =>
        {
            Assert.True(m.AllRestored);
            Assert.Equal(Text.Length + 3_000 + 30_000, m.OriginalBytes);
            Assert.Equal(QMetric.Compute(Alpha, Beta, TRef, m.OriginalBytes, m.ArchiveBytes, m.Seconds, true), m.Score);
        });
    }

    [Fact]
    public void BlockImpact_WithoutLz77_HuffmanStillShrinks_ButLessThanTheFullPipeline()
    {
        IReadOnlyList<BlockImpactMeasurement> results = Impact();
        long Size(PipelineStage stage) => results.Single(m => m.DisabledStage == stage).ArchiveBytes;

        Assert.True(Size(PipelineStage.None) < Size(PipelineStage.Lz77));
        Assert.True(Size(PipelineStage.Lz77) < Size(PipelineStage.Compression));
        Assert.True(Size(PipelineStage.Compression) > Text.Length + 3_000 + 30_000);
    }

    [Fact]
    public void BlockImpact_WithoutEncryption_VolumesHaveNoHmac()
    {
        IReadOnlyList<BlockImpactMeasurement> results = Impact();
        BlockImpactMeasurement full = results.Single(m => m.DisabledStage == PipelineStage.None);
        BlockImpactMeasurement plain = results.Single(m => m.DisabledStage == PipelineStage.Encryption);

        Assert.Equal(full.Volumes, plain.Volumes);
        Assert.True(plain.ArchiveBytes < full.ArchiveBytes);
    }

    [Fact]
    public void BlockImpact_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => BlockImpactComparison.Run(null!, Options, Alpha, Beta, TRef));
        Assert.Throws<ArgumentNullException>(() => BlockImpactComparison.Run(Files(), null!, Alpha, Beta, TRef));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockImpactComparison.Run(Files(), Options, Alpha, Beta, TRef, repeats: 0));
    }

    // ------------------------------------------------------------------ encryption order

    [Fact]
    public void EncryptionOrder_OneRowPerFile_WithItsGroup_BothOrdersRestore()
    {
        IReadOnlyList<EncryptionOrderMeasurement> results = EncryptionOrderComparison.Run(Files(), blockSize: 16_384);

        Assert.Equal([0, 0, 5, 0], results.Select(r => r.Group));
        Assert.Equal(Files().Select(f => (long)f.File.Data.Length), results.Select(r => r.OriginalBytes));
        Assert.All(results, r => Assert.True(r.BothRestored));
    }

    [Fact]
    public void EncryptionOrder_CompressingCiphertextGainsNothing()
    {
        EncryptionOrderMeasurement text = EncryptionOrderComparison.Run(Files(), blockSize: 16_384)[0];

        Assert.True(text.CompressThenEncryptBytes < text.OriginalBytes / 10);
        // Blocks without gain are stored as they are, so encrypt-then-compress is exactly the original size;
        // the coder alone, without that fallback, makes ciphertext bigger.
        Assert.Equal(text.OriginalBytes, text.EncryptThenCompressBytes);
        Assert.True(text.EncryptThenCompressCodedBytes > text.OriginalBytes);
    }

    [Fact]
    public void EncryptionOrder_EmptyFile_IsZeroInBothOrders()
    {
        EncryptionOrderMeasurement empty = EncryptionOrderComparison.Run(Files(), blockSize: 16_384)[3];

        Assert.Equal((0L, 0L, 0L), (empty.CompressThenEncryptBytes, empty.EncryptThenCompressBytes, empty.EncryptThenCompressCodedBytes));
    }
}
