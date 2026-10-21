using System.Diagnostics;

namespace SmartArchiver.Archive;

/// <summary>How the files of an archive are chosen to be compressed.</summary>
public enum StrategyVariant
{
    /// <summary>No analysis, nothing compressed: the lower bound.</summary>
    Stored,

    /// <summary>Every file is compressed with LZ77 + Huffman, without analysis.</summary>
    Fixed,

    /// <summary>The analyzer (classifier) decides per file.</summary>
    Classifier,
}

/// <summary>A file of a dataset and its group (file_type 0..6).</summary>
public sealed record GroupedFile(int Group, ArchiveInput File);

/// <summary>One group packed with one variant.</summary>
/// <param name="CompressedFiles">Files whose file table entry says LZ77 + Huffman after packing.</param>
/// <param name="ArchiveBytes">Size of the single-volume archive of the group.</param>
public sealed record StrategyMeasurement(
    int Group,
    StrategyVariant Variant,
    int FileCount,
    int CompressedFiles,
    long OriginalBytes,
    long ArchiveBytes,
    double PackSeconds,
    double UnpackSeconds,
    bool AllRestored);

/// <summary>
/// Experiment "fixed strategy against the classifier's strategy": every group is packed into an archive three ways
/// (<see cref="StrategyVariant"/>), unpacked and compared byte for byte; size and both times are measured.
/// </summary>
/// <remarks>
/// The times are milliseconds, so one run is mostly noise: the first call also pays for JIT compilation.
/// Every group and variant is therefore run once as a warm-up, then <c>repeats</c> times, and the median is kept.
/// </remarks>
public static class StrategyComparison
{
    public static IReadOnlyList<StrategyMeasurement> Run(
        IReadOnlyList<GroupedFile> files, IFileAnalyzer classifier, int blockSize = FormatConstants.DefaultBlockSize,
        int repeats = 5)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentOutOfRangeException.ThrowIfLessThan(repeats, 1);

        var results = new List<StrategyMeasurement>();
        foreach (IGrouping<int, GroupedFile> group in files.GroupBy(f => f.Group).OrderBy(g => g.Key))
        {
            ArchiveInput[] groupFiles = [.. group.Select(f => f.File)];
            foreach (StrategyVariant variant in Enum.GetValues<StrategyVariant>())
            {
                Measure(group.Key, groupFiles, variant, classifier, blockSize);
                StrategyMeasurement[] runs = [.. Enumerable.Range(0, repeats)
                    .Select(_ => Measure(group.Key, groupFiles, variant, classifier, blockSize))];
                results.Add(runs[0] with
                {
                    PackSeconds = Median([.. runs.Select(r => r.PackSeconds)]),
                    UnpackSeconds = Median([.. runs.Select(r => r.UnpackSeconds)]),
                    AllRestored = runs.All(r => r.AllRestored),
                });
            }
        }
        return results;
    }

    /// <summary>Middle value; for an even count, the mean of the two middle values.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("No values.", nameof(values));
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static StrategyMeasurement Measure(
        int group, ArchiveInput[] files, StrategyVariant variant, IFileAnalyzer classifier, int blockSize)
    {
        // The true group is written as file_type unless the classifier is the one deciding.
        ArchiveInput[] inputs = variant == StrategyVariant.Classifier
            ? [.. files.Select(f => f with { Kind = FileKind.Unknown })]
            : [.. files.Select(f => f with { Kind = (FileKind)group })];
        IFileAnalyzer? analyzer = variant switch
        {
            StrategyVariant.Stored => null,
            StrategyVariant.Fixed => new AlwaysCompress(),
            _ => classifier,
        };

        var watch = Stopwatch.StartNew();
        byte[] archive = new ArchiveWriter(blockSize, analyzer).Build(inputs);
        double packSeconds = watch.Elapsed.TotalSeconds;

        watch.Restart();
        IReadOnlyList<ExtractedFile> restored = ArchiveReader.Read(archive);
        double unpackSeconds = watch.Elapsed.TotalSeconds;

        bool allRestored = restored.Count == files.Length
            && files.Zip(restored).All(pair => pair.First.Data.AsSpan().SequenceEqual(pair.Second.Data));
        return new StrategyMeasurement(
            group,
            variant,
            files.Length,
            restored.Count(f => f.Strategy == CompressionStrategy.Lz77Huffman),
            files.Sum(f => (long)f.Data.Length),
            archive.Length,
            packSeconds,
            unpackSeconds,
            allRestored);
    }

    /// <summary>The fixed strategy: compress every file, whatever it is.</summary>
    private sealed class AlwaysCompress : IFileAnalyzer
    {
        public FileAnalysis Analyze(ReadOnlySpan<byte> data) =>
            new(FileKind.Unknown, 1.0, CompressionStrategy.Lz77Huffman);
    }
}
