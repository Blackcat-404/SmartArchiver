using System.Text;

namespace SmartArchiver.Analysis.Tests;

public class FeatureExtractorTests
{
    private const double Tolerance = 1e-9;

    private static readonly byte[] EnglishText = Encoding.ASCII.GetBytes(
        "The quick brown fox jumps over the lazy dog.\n" +
        "Pack my box with five dozen liquor jugs.\n" +
        "How vexingly quick daft zebras jump!\n");

    private static byte[] Noise(int length, int seed = 7)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public void Compute_Empty_AllFeaturesAreZero()
    {
        FileFeatures f = FeatureExtractor.Compute([]);

        Assert.Equal(0, f.Entropy);
        Assert.Equal(0, f.PrintableAsciiFraction);
        Assert.Equal(0, f.AverageLineLength);
        Assert.Equal(0, f.BigramEntropy);
        Assert.All(f.Histogram, p => Assert.Equal(0, p));
        Assert.All(f.Autocorrelation, r => Assert.Equal(0, r));
    }

    [Fact]
    public void Compute_Always_Returns256HistogramBinsAnd16AutocorrelationLags()
    {
        FileFeatures f = FeatureExtractor.Compute(EnglishText);

        Assert.Equal(256, f.Histogram.Count);
        Assert.Equal(FeatureExtractor.AutocorrelationLags, f.Autocorrelation.Count);
        Assert.Equal(16, FeatureExtractor.AutocorrelationLags);
    }

    [Fact]
    public void Entropy_Zeros_IsZero()
    {
        Assert.Equal(0, FeatureExtractor.Compute(new byte[4096]).Entropy, Tolerance);
    }

    [Fact]
    public void Entropy_EveryByteValueEquallyOften_IsEightBits()
    {
        byte[] data = Enumerable.Range(0, 256 * 4).Select(i => (byte)i).ToArray();

        Assert.Equal(8, FeatureExtractor.Compute(data).Entropy, Tolerance);
    }

    [Fact]
    public void Entropy_TwoValuesEquallyOften_IsOneBit()
    {
        Assert.Equal(1, FeatureExtractor.Compute("abababab"u8.ToArray()).Entropy, Tolerance);
    }

    [Fact]
    public void Entropy_Noise_IsHigherThanText()
    {
        double noise = FeatureExtractor.Compute(Noise(64 * 1024)).Entropy;
        double text = FeatureExtractor.Compute(EnglishText).Entropy;

        Assert.True(noise > 7.9, $"noise entropy {noise}");
        Assert.True(text < 5, $"text entropy {text}");
    }

    [Fact]
    public void Histogram_IsFrequencyOfEachByte()
    {
        FileFeatures f = FeatureExtractor.Compute("aab"u8.ToArray());

        Assert.Equal(2.0 / 3, f.Histogram['a'], Tolerance);
        Assert.Equal(1.0 / 3, f.Histogram['b'], Tolerance);
        Assert.Equal(1, f.Histogram.Sum(), Tolerance);
    }

    [Fact]
    public void PrintableAsciiFraction_Text_IsOne()
    {
        Assert.Equal(1, FeatureExtractor.Compute(EnglishText).PrintableAsciiFraction, Tolerance);
    }

    [Fact]
    public void PrintableAsciiFraction_Zeros_IsZero()
    {
        Assert.Equal(0, FeatureExtractor.Compute(new byte[100]).PrintableAsciiFraction, Tolerance);
    }

    [Fact]
    public void PrintableAsciiFraction_Noise_IsAboutShareOfPrintableValues()
    {
        // 95 visible characters plus tab, LF and CR out of 256 byte values.
        double fraction = FeatureExtractor.Compute(Noise(64 * 1024)).PrintableAsciiFraction;

        Assert.InRange(fraction, 98.0 / 256 - 0.02, 98.0 / 256 + 0.02);
    }

    [Theory]
    [InlineData("ab\ncd\nef", 2.0)]
    [InlineData("ab\n", 2.0)]
    [InlineData("abcd", 4.0)]
    [InlineData("ab\r\ncdef\r\n", 3.0)]
    [InlineData("ab\n\n", 1.0)]
    public void AverageLineLength_CountsCharactersBetweenLineBreaks(string text, double expected)
    {
        Assert.Equal(expected, FeatureExtractor.Compute(Encoding.ASCII.GetBytes(text)).AverageLineLength, Tolerance);
    }

    [Fact]
    public void BigramEntropy_Zeros_IsZero()
    {
        Assert.Equal(0, FeatureExtractor.Compute(new byte[1000]).BigramEntropy, Tolerance);
    }

    [Fact]
    public void BigramEntropy_SingleByte_IsZero()
    {
        Assert.Equal(0, FeatureExtractor.Compute([42]).BigramEntropy, Tolerance);
    }

    [Fact]
    public void BigramEntropy_AlternatingPair_IsAboutOneBit()
    {
        // Pairs "ab" and "ba" alternate: 5 of one and 4 of the other.
        double expected = -(5.0 / 9 * Math.Log2(5.0 / 9) + 4.0 / 9 * Math.Log2(4.0 / 9));

        Assert.Equal(expected, FeatureExtractor.Compute("ababababab"u8.ToArray()).BigramEntropy, Tolerance);
    }

    [Fact]
    public void BigramEntropy_Noise_IsMuchHigherThanText()
    {
        double noise = FeatureExtractor.Compute(Noise(64 * 1024)).BigramEntropy;
        double text = FeatureExtractor.Compute(EnglishText).BigramEntropy;

        Assert.True(noise > 14, $"noise bigram entropy {noise}");
        Assert.True(text < noise / 2, $"text bigram entropy {text}");
    }

    [Fact]
    public void Autocorrelation_Constant_IsZero()
    {
        Assert.All(FeatureExtractor.Compute(new byte[500]).Autocorrelation, r => Assert.Equal(0, r));
    }

    [Fact]
    public void Autocorrelation_Alternating_IsNegativeAtOddLagsAndPositiveAtEven()
    {
        byte[] data = Enumerable.Range(0, 1000).Select(i => (byte)(i % 2 == 0 ? 0 : 255)).ToArray();

        IReadOnlyList<double> r = FeatureExtractor.Compute(data).Autocorrelation;

        Assert.True(r[0] < -0.95, $"lag 1: {r[0]}");
        Assert.True(r[1] > 0.95, $"lag 2: {r[1]}");
    }

    [Fact]
    public void Autocorrelation_Noise_IsNearZero()
    {
        Assert.All(FeatureExtractor.Compute(Noise(64 * 1024)).Autocorrelation, r => Assert.InRange(r, -0.05, 0.05));
    }

    [Fact]
    public void Autocorrelation_ShorterThanLag_IsZeroForThatLag()
    {
        IReadOnlyList<double> r = FeatureExtractor.Compute([1, 200, 3]).Autocorrelation;

        Assert.All(r.Skip(2), value => Assert.Equal(0, value));
    }
}
