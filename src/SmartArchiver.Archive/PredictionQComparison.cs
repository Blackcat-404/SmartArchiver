using System.Diagnostics;

namespace SmartArchiver.Archive;

/// <summary>One corpus group through the full pipeline, with or without next-byte prediction.</summary>
/// <param name="ArchiveBytes">Size of all volumes, encryption and HMAC included.</param>
/// <param name="Seconds">Pack and unpack; with prediction also computing the residuals and restoring the bytes from them.</param>
/// <param name="AllRestored">Every file came back byte for byte; otherwise Q is 0.</param>
public sealed record PredictionQMeasurement(
    int Group,
    bool Predicted,
    int FileCount,
    long OriginalBytes,
    long ArchiveBytes,
    double Seconds,
    bool AllRestored,
    QScore Score);

/// <summary>
/// Experiment "Q with prediction and without": each group goes through the full pipeline (analysis, LZ77, Huffman,
/// packing, AES, HMAC) twice, as the files are and as prediction residuals. Q is computed per group, so it shows
/// where prediction helps and where it only costs time.
/// </summary>
/// <remarks>
/// The analyzer sees the residuals, because they are what gets compressed; file_type stays the true group.
/// Each group and variant runs once as a warm-up, then <c>repeats</c> times, and the median time is kept.
/// </remarks>
public static class PredictionQComparison
{
    public static IReadOnlyList<PredictionQMeasurement> Run(
        IReadOnlyList<GroupedFile> files, IResidualCoder coder, PackOptions options,
        double alpha, double beta, double tRefSeconds, int repeats = 3)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(coder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(repeats, 1);

        var results = new List<PredictionQMeasurement>();
        foreach (IGrouping<int, GroupedFile> group in files.GroupBy(f => f.Group).OrderBy(g => g.Key))
        {
            ArchiveInput[] groupFiles = [.. group.Select(f => f.File with { Kind = (FileKind)group.Key })];
            foreach (bool predicted in new[] { false, true })
            {
                Measure(groupFiles, predicted ? coder : null, options);
                (long ArchiveBytes, double Seconds, bool AllRestored)[] runs = [.. Enumerable.Range(0, repeats)
                    .Select(_ => Measure(groupFiles, predicted ? coder : null, options))];

                long original = groupFiles.Sum(f => (long)f.Data.Length);
                long archive = runs[0].ArchiveBytes;
                double seconds = StrategyComparison.Median([.. runs.Select(r => r.Seconds)]);
                bool restored = runs.All(r => r.AllRestored);
                results.Add(new PredictionQMeasurement(group.Key, predicted, groupFiles.Length, original, archive, seconds,
                    restored, QMetric.Compute(alpha, beta, tRefSeconds, original, archive, seconds, restored)));
            }
        }
        return results;
    }

    private static (long ArchiveBytes, double Seconds, bool AllRestored) Measure(
        ArchiveInput[] files, IResidualCoder? coder, PackOptions options)
    {
        var watch = Stopwatch.StartNew();
        ArchiveInput[] inputs = coder is null ? files : [.. files.Select(f => f with { Data = coder.Encode(f.Data) })];
        IReadOnlyList<byte[]> volumes = ArchivePipeline.Pack(inputs, options);
        ArchiveResult<IReadOnlyList<ExtractedFile>> unpacked = ArchivePipeline.TryUnpack(volumes, options.Password);
        byte[][]? restored = null;
        if (unpacked.Ok)
        {
            try
            {
                restored = [.. unpacked.Value!.Select(f => coder is null ? f.Data : coder.Decode(f.Data))];
            }
            catch (InvalidDataException)
            {
                // Residuals the coder cannot read count as files that were not restored.
            }
        }
        double seconds = watch.Elapsed.TotalSeconds;

        bool allRestored = restored is not null && restored.Length == files.Length
            && files.Zip(restored).All(pair => pair.First.Data.AsSpan().SequenceEqual(pair.Second));
        return (volumes.Sum(v => (long)v.Length), seconds, allRestored);
    }
}
