using System.Globalization;

namespace SmartArchiver.Compression;

/// <summary>Підсумок порівняння одного алгоритму в межах групи файлів.</summary>
public sealed record ParsingGroupMeasurement(
    byte Group,
    Lz77ParsingMethod Method,
    long OriginalBytes,
    long EncodedBytes,
    double ElapsedSeconds);

/// <summary>
/// Порівнює Greedy та Lazy на файлах каталогів 0..6.
/// Не залежить від CLI, бази даних або формату архіву.
/// </summary>
public static class ParsingComparisonRunner
{
    public const int BlockSize = 64 * 1024;

    public static IReadOnlyList<ParsingGroupMeasurement> RunDirectory(
        string datasetPath,
        int maxChain = 256,
        int minLazyGain = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minLazyGain);

        string fullPath = Path.GetFullPath(datasetPath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Каталог з даними не знайдено: {fullPath}");

        var results = new List<ParsingGroupMeasurement>();

        // Кожна папка від 0 до 6 позначає окрему групу корпусу.
        for (byte group = 0; group <= 6; group++)
        {
            string groupPath = Path.Combine(fullPath, group.ToString(CultureInfo.InvariantCulture));
            if (!Directory.Exists(groupPath))
                continue;

            long[] originalBytes = new long[2];
            long[] encodedBytes = new long[2];
            double[] elapsedSeconds = new double[2];
            bool hasData = false;

            foreach (string file in Directory.EnumerateFiles(groupPath, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                using var stream = File.OpenRead(file);
                byte[] buffer = new byte[BlockSize];
                int count;

                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasData = true;
                    // Обидва методи отримують однаковий блок для чесного порівняння.
                    byte[] block = buffer.AsSpan(0, count).ToArray();
                    foreach (Lz77ParsingMethod method in Enum.GetValues<Lz77ParsingMethod>())
                    {
                        Lz77ParsingMeasurement measurement =
                            Lz77ParsingBenchmark.Measure(block, method, maxChain, minLazyGain);

                        int index = (int)method;
                        originalBytes[index] += measurement.OriginalBytes;
                        encodedBytes[index] += measurement.EncodedBytes;
                        elapsedSeconds[index] += measurement.ElapsedSeconds;
                    }
                }
            }

            if (!hasData)
                continue;

            foreach (Lz77ParsingMethod method in Enum.GetValues<Lz77ParsingMethod>())
            {
                int index = (int)method;
                results.Add(new ParsingGroupMeasurement(
                    group, method, originalBytes[index], encodedBytes[index], elapsedSeconds[index]));
            }
        }

        if (results.Count == 0)
            throw new ArgumentException(
                "У папках 0..6 немає непорожніх файлів для порівняння.", nameof(datasetPath));

        return results;
    }
}
