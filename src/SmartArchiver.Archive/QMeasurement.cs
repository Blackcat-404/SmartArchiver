namespace SmartArchiver.Archive;

/// <summary>Q = α·S + β·T, де S = B_orig / (B_orig + B_arc), T = t_ref / (t_ref + t). Q = 0, якщо хоч один файл не відновився.</summary>
public readonly record struct QScore(double S, double T, double Q);

public static class QMetric
{
    public static QScore Compute(double alpha, double beta, double tRefSeconds, long originalBytes, long archiveBytes, double seconds, bool allRestored)
    {
        double s = originalBytes + archiveBytes == 0 ? 0 : (double)originalBytes / (originalBytes + archiveBytes);
        double t = tRefSeconds / (tRefSeconds + seconds);
        return new QScore(s, t, allRestored ? alpha * s + beta * t : 0);
    }
}

/// <summary>Один файл корпусу: група 0…6, розмір, скільки він займає в архіві (заголовки блоків + дані), чи відновився.</summary>
public sealed record FileMeasurement(string Name, byte Group, long OriginalBytes, long ArchiveBytes, bool Restored);

/// <param name="ArchiveBytes">B_arc: розмір усіх томів, разом із шифруванням і HMAC.</param>
/// <param name="Seconds">t: аналіз, стиснення, пакування, шифрування, потім перевірка, розшифрування й розпаковка.</param>
public sealed record QMeasurement(
    long OriginalBytes,
    long ArchiveBytes,
    int Volumes,
    double Seconds,
    bool AllRestored,
    QScore Score,
    IReadOnlyList<FileMeasurement> Files);

public static class CorpusMeasurer
{
    /// <summary>
    /// Весь корпус через повний тракт (запакувати → зашифрувати → розпакувати) за один прогін, звірка кожного файлу
    /// побайтово. Час міряється лише по тракту, звірка в нього не входить. Імена файлів мають бути унікальні.
    /// </summary>
    public static QMeasurement Measure(
        IReadOnlyList<(ArchiveInput Input, byte Group)> corpus,
        PackOptions options,
        double alpha,
        double beta,
        double tRefSeconds)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        List<ArchiveInput> inputs = [.. corpus.Select(c => c.Input)];

        var watch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<PreparedFile> prepared = ArchivePipeline.Prepare(inputs, options);
        IReadOnlyList<byte[]> volumes = ArchivePipeline.Seal(prepared, options);
        ArchiveResult<IReadOnlyList<ExtractedFile>> unpacked = ArchivePipeline.TryUnpack(volumes, options.Password);
        watch.Stop();

        Dictionary<string, byte[]> restored = unpacked.Ok
            ? unpacked.Value!.ToDictionary(f => f.Name, f => f.Data, StringComparer.Ordinal)
            : new();
        var files = new List<FileMeasurement>(inputs.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            ArchiveInput input = inputs[i];
            bool same = restored.TryGetValue(input.Name, out byte[]? data) && data.AsSpan().SequenceEqual(input.Data);
            files.Add(new FileMeasurement(input.Name, corpus[i].Group, input.Data.LongLength, prepared[i].Blocks.Sum(b => (long)b.Weight), same));
        }

        long original = files.Sum(f => f.OriginalBytes);
        long archive = volumes.Sum(v => (long)v.Length);
        bool all = unpacked.Ok && files.TrueForAll(f => f.Restored);
        QScore score = QMetric.Compute(alpha, beta, tRefSeconds, original, archive, watch.Elapsed.TotalSeconds, all);
        return new QMeasurement(original, archive, volumes.Count, watch.Elapsed.TotalSeconds, all, score, files);
    }
}
