namespace SmartArchiver.Analysis;

/// <summary>
/// Turns <see cref="FileFeatures"/> into the fixed-length input of the classifier.
/// Every value is scaled to [0, 1] (autocorrelation stays in [-1, 1]), so no feature dominates by its units.
/// </summary>
public static class FeatureVector
{
    public const int Length = 38;

    public const int EntropyIndex = 0;
    public const int BigramEntropyIndex = 1;
    public const int PrintableIndex = 2;
    public const int LineLengthIndex = 3;
    public const int ZeroByteIndex = 4;
    public const int FfByteIndex = 5;
    public const int HistogramIndex = 6;
    public const int AutocorrelationIndex = 22;

    private const int HistogramBins = 16;

    /// <summary>Lines this long or longer all map to 1; beyond it the file is simply "not line-based".</summary>
    private const double MaxLineLength = 1000;

    public static double[] From(FileFeatures features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var v = new double[Length];

        v[EntropyIndex] = features.Entropy / 8;
        v[BigramEntropyIndex] = features.BigramEntropy / 16;
        v[PrintableIndex] = features.PrintableAsciiFraction;
        // Logarithm, because 40 vs 80 characters matters, 4000 vs 8000 does not.
        v[LineLengthIndex] = Math.Min(1, Math.Log(1 + features.AverageLineLength) / Math.Log(1 + MaxLineLength));
        v[ZeroByteIndex] = features.Histogram[0x00];
        v[FfByteIndex] = features.Histogram[0xFF];

        // 256 byte frequencies grouped into 16 ranges of 16 values: enough to tell letters, digits,
        // control bytes and high bytes apart without 256 inputs.
        for (int b = 0; b < 256; b++)
            v[HistogramIndex + b / (256 / HistogramBins)] += features.Histogram[b];

        for (int lag = 0; lag < FeatureExtractor.AutocorrelationLags; lag++)
            v[AutocorrelationIndex + lag] = features.Autocorrelation[lag];

        return v;
    }
}
