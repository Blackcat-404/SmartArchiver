namespace SmartArchiver.Archive.Tests;

public class PredictionQComparisonTests
{
    private const double Alpha = 0.6, Beta = 0.4, TRef = 1.0;

    /// <summary>Predicts "the previous byte": a ramp 0, 1, 2… gives residuals that are all 1.</summary>
    private sealed class DeltaCoder : IResidualCoder
    {
        public byte[] Encode(ReadOnlySpan<byte> data)
        {
            var encoded = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                encoded[i] = (byte)(data[i] - (i == 0 ? 0 : data[i - 1]));
            return encoded;
        }

        public byte[] Decode(ReadOnlySpan<byte> encoded)
        {
            var data = new byte[encoded.Length];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(encoded[i] + (i == 0 ? 0 : data[i - 1]));
            return data;
        }
    }

    /// <summary>A coder whose decoding loses the data.</summary>
    private sealed class BrokenCoder : IResidualCoder
    {
        public byte[] Encode(ReadOnlySpan<byte> data) => data.ToArray();

        public byte[] Decode(ReadOnlySpan<byte> encoded) => new byte[encoded.Length];
    }

    private sealed class AlwaysCompress : IFileAnalyzer
    {
        public FileAnalysis Analyze(ReadOnlySpan<byte> data) => new(FileKind.Unknown, 1.0, CompressionStrategy.Lz77Huffman);
    }

    private static readonly PackOptions Options = new("test-password", new AlwaysCompress());

    /// <summary>Bytes 0, 7, 14…: LZ77 finds no repeats inside 256 bytes, the deltas are all 7.</summary>
    private static byte[] Ramp(int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(i * 7))];

    private static byte[] Noise(int length)
    {
        var bytes = new byte[length];
        new Random(3).NextBytes(bytes);
        return bytes;
    }

    private static IReadOnlyList<GroupedFile> Files() =>
    [
        new(6, new ArchiveInput("ramp.bin", Ramp(40_000))),
        new(5, new ArchiveInput("noise.bin", Noise(20_000))),
        new(6, new ArchiveInput("ramp2.bin", Ramp(10_000))),
    ];

    private static IReadOnlyList<PredictionQMeasurement> Run(IResidualCoder coder) =>
        PredictionQComparison.Run(Files(), coder, Options, Alpha, Beta, TRef, repeats: 1);

    [Fact]
    public void Run_GivesPlainThenPredictedForEveryGroup()
    {
        Assert.Equal(
            [(5, false), (5, true), (6, false), (6, true)],
            Run(new DeltaCoder()).Select(m => (m.Group, m.Predicted)));
    }

    [Fact]
    public void Run_CountsFilesAndOriginalBytesPerGroup()
    {
        PredictionQMeasurement ramps = Run(new DeltaCoder()).First(m => m.Group == 6);

        Assert.Equal(2, ramps.FileCount);
        Assert.Equal(50_000, ramps.OriginalBytes);
    }

    [Fact]
    public void Run_BothVariantsRestoreEveryFile_AndScoreIsTheQOfTheMeasurement()
    {
        Assert.All(Run(new DeltaCoder()), m =>
        {
            Assert.True(m.AllRestored);
            Assert.Equal(QMetric.Compute(Alpha, Beta, TRef, m.OriginalBytes, m.ArchiveBytes, m.Seconds, true), m.Score);
        });
    }

    [Fact]
    public void Run_PredictionThatFitsTheData_MakesTheArchiveSmaller()
    {
        IReadOnlyList<PredictionQMeasurement> results = Run(new DeltaCoder());
        PredictionQMeasurement plain = results.Single(m => m.Group == 6 && !m.Predicted);
        PredictionQMeasurement predicted = results.Single(m => m.Group == 6 && m.Predicted);

        Assert.True(predicted.ArchiveBytes < plain.ArchiveBytes / 2);
        Assert.True(predicted.Score.S > plain.Score.S);
    }

    [Fact]
    public void Run_CoderThatLosesData_GivesQZeroForPredictedOnly()
    {
        IReadOnlyList<PredictionQMeasurement> results = Run(new BrokenCoder());

        Assert.All(results.Where(m => m.Predicted), m =>
        {
            Assert.False(m.AllRestored);
            Assert.Equal(0, m.Score.Q);
        });
        Assert.All(results.Where(m => !m.Predicted), m => Assert.True(m.AllRestored));
    }

    [Fact]
    public void Run_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => PredictionQComparison.Run(null!, new DeltaCoder(), Options, Alpha, Beta, TRef));
        Assert.Throws<ArgumentNullException>(() => PredictionQComparison.Run(Files(), null!, Options, Alpha, Beta, TRef));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PredictionQComparison.Run(Files(), new DeltaCoder(), Options, Alpha, Beta, TRef, repeats: 0));
    }
}
