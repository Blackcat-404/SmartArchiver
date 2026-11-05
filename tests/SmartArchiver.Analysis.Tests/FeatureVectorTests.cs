using System.Text;

namespace SmartArchiver.Analysis.Tests;

public class FeatureVectorTests
{
    private const double Tolerance = 1e-12;

    private static double[] VectorOf(byte[] data) => FeatureVector.From(FeatureExtractor.Compute(data));

    private static byte[] Noise(int length)
    {
        var bytes = new byte[length];
        new Random(3).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public void From_HasFixedLength()
    {
        Assert.Equal(38, FeatureVector.Length);
        Assert.Equal(FeatureVector.Length, VectorOf("hello"u8.ToArray()).Length);
    }

    [Fact]
    public void From_Empty_IsAllZeros()
    {
        Assert.All(VectorOf([]), v => Assert.Equal(0, v));
    }

    [Fact]
    public void From_EveryValueIsBetweenMinusOneAndOne()
    {
        byte[][] samples = [Noise(10_000), new byte[5000], Encoding.ASCII.GetBytes(new string('x', 50_000)),
            Encoding.UTF8.GetBytes("Привіт\nсвіт\n"), [0, 255, 0, 255, 0, 255]];

        foreach (byte[] sample in samples)
            Assert.All(VectorOf(sample), v => Assert.InRange(v, -1, 1));
    }

    [Fact]
    public void From_ScalesEntropiesToZeroOne()
    {
        double[] v = VectorOf(Enumerable.Range(0, 256 * 4).Select(i => (byte)i).ToArray());

        Assert.Equal(1, v[FeatureVector.EntropyIndex], Tolerance);
    }

    [Fact]
    public void From_CoarseHistogramSumsToOne()
    {
        double[] v = VectorOf(Noise(4096));

        Assert.Equal(1, v.Skip(FeatureVector.HistogramIndex).Take(16).Sum(), Tolerance);
    }

    [Fact]
    public void From_CoarseHistogramPutsByteIntoItsSixteenth()
    {
        // 'A' = 0x41 lies in bin 4 (0x40..0x4F).
        double[] v = VectorOf("AAAA"u8.ToArray());

        Assert.Equal(1, v[FeatureVector.HistogramIndex + 4], Tolerance);
    }

    [Fact]
    public void From_ZeroAndFfSharesAreSeparateInputs()
    {
        double[] v = VectorOf([0, 0, 0, 255]);

        Assert.Equal(0.75, v[FeatureVector.ZeroByteIndex], Tolerance);
        Assert.Equal(0.25, v[FeatureVector.FfByteIndex], Tolerance);
    }

    [Fact]
    public void From_VeryLongLines_AreCappedAtOne()
    {
        Assert.Equal(1, VectorOf(Encoding.ASCII.GetBytes(new string('x', 50_000)))[FeatureVector.LineLengthIndex], Tolerance);
    }

    [Fact]
    public void From_LongerLines_GiveLargerValue()
    {
        double shortLines = VectorOf("ab\ncd\nef\n"u8.ToArray())[FeatureVector.LineLengthIndex];
        double longLines = VectorOf(Encoding.ASCII.GetBytes(new string('x', 80) + "\n"))[FeatureVector.LineLengthIndex];

        Assert.True(longLines > shortLines);
    }

    [Fact]
    public void From_KeepsAutocorrelationAsIs()
    {
        FileFeatures f = FeatureExtractor.Compute(Enumerable.Range(0, 1000).Select(i => (byte)(i % 2 == 0 ? 0 : 255)).ToArray());

        Assert.Equal(f.Autocorrelation, FeatureVector.From(f).Skip(FeatureVector.AutocorrelationIndex));
    }
}
