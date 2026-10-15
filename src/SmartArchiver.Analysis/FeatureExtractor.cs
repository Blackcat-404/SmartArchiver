namespace SmartArchiver.Analysis;

/// <summary>Computes <see cref="FileFeatures"/> from the bytes of a file.</summary>
public static class FeatureExtractor
{
    public const int AutocorrelationLags = 16;

    public static FileFeatures Compute(ReadOnlySpan<byte> data)
    {
        var counts = new long[256];
        foreach (byte b in data)
            counts[b]++;

        var histogram = new double[256];
        if (!data.IsEmpty)
        {
            for (int i = 0; i < 256; i++)
                histogram[i] = (double)counts[i] / data.Length;
        }

        return new FileFeatures(
            Entropy(counts, data.Length),
            PrintableAsciiFraction(counts, data.Length),
            AverageLineLength(counts, data),
            BigramEntropy(data),
            histogram,
            Autocorrelation(data));
    }

    /// <summary>H = -sum(p * log2 p) over the counted symbols.</summary>
    private static double Entropy(ReadOnlySpan<long> counts, long total)
    {
        if (total == 0)
            return 0;
        double entropy = 0;
        foreach (long count in counts)
        {
            if (count == 0)
                continue;
            double p = (double)count / total;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    private static double PrintableAsciiFraction(long[] counts, int total)
    {
        if (total == 0)
            return 0;
        long printable = counts['\t'] + counts['\n'] + counts['\r'];
        for (int b = 0x20; b <= 0x7E; b++)
            printable += counts[b];
        return (double)printable / total;
    }

    private static double AverageLineLength(long[] counts, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return 0;
        long lineFeeds = counts['\n'];
        long lines = lineFeeds + (data[^1] == '\n' ? 0 : 1);
        long characters = data.Length - lineFeeds - counts['\r'];
        return (double)characters / lines;
    }

    private static double BigramEntropy(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return 0;
        var counts = new long[256 * 256];
        for (int i = 1; i < data.Length; i++)
            counts[(data[i - 1] << 8) | data[i]]++;
        return Entropy(counts, data.Length - 1);
    }

    /// <summary>
    /// Sample autocorrelation r(k) = sum((x[i] - m) * (x[i+k] - m)) / sum((x[i] - m)^2) for k = 1..16.
    /// A constant sequence or a lag not shorter than the data gives 0.
    /// </summary>
    private static double[] Autocorrelation(ReadOnlySpan<byte> data)
    {
        var result = new double[AutocorrelationLags];
        if (data.Length < 2)
            return result;

        double mean = 0;
        foreach (byte b in data)
            mean += b;
        mean /= data.Length;

        double variance = 0;
        foreach (byte b in data)
            variance += (b - mean) * (b - mean);
        if (variance == 0)
            return result;

        for (int lag = 1; lag <= AutocorrelationLags && lag < data.Length; lag++)
        {
            double sum = 0;
            for (int i = 0; i + lag < data.Length; i++)
                sum += (data[i] - mean) * (data[i + lag] - mean);
            result[lag - 1] = sum / variance;
        }
        return result;
    }
}
