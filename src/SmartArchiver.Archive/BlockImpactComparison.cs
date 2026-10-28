using System.Diagnostics;
using SmartArchiver.Compression;

namespace SmartArchiver.Archive;

/// <summary>A block of the pipeline switched off in the block impact experiment.</summary>
public enum PipelineStage
{
    /// <summary>Nothing switched off: the full pipeline, the baseline.</summary>
    None,

    /// <summary>The classifier is replaced by the fixed strategy "always compress".</summary>
    Analysis,

    /// <summary>LZ77 finds no matches: every byte is a literal and only Huffman shrinks the data.</summary>
    Lz77,

    /// <summary>LZ77 and Huffman together: every file is stored as it is.</summary>
    Compression,

    /// <summary>The DP knapsack is replaced by greedy packing of volumes.</summary>
    Packing,

    /// <summary>No AES and no HMAC: plain volumes are written and read.</summary>
    Encryption,
}

/// <param name="ArchiveBytes">Size of all volumes.</param>
/// <param name="Seconds">Pack and unpack, median of the repeats.</param>
public sealed record BlockImpactMeasurement(
    PipelineStage DisabledStage,
    long OriginalBytes,
    long ArchiveBytes,
    int Volumes,
    double Seconds,
    bool AllRestored,
    QScore Score);

/// <summary>
/// Experiment "impact of each block": the whole corpus goes through the pipeline with one block switched off at a
/// time, and Q is compared with the full pipeline (methodology 8.23). Huffman cannot be switched off on its own,
/// because the format requires Huffman tables in every compressed block; next-byte prediction is not part of the
/// pipeline at all (decision 49).
/// </summary>
/// <remarks>Every variant runs once as a warm-up, then <c>repeats</c> times; the median time is kept.</remarks>
public static class BlockImpactComparison
{
    public static IReadOnlyList<BlockImpactMeasurement> Run(
        IReadOnlyList<GroupedFile> corpus, PackOptions options, double alpha, double beta, double tRefSeconds, int repeats = 3)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(repeats, 1);

        // Without the classifier the true group is written as file_type, as in the strategy experiment.
        ArchiveInput[] asIs = [.. corpus.Select(f => f.File)];
        ArchiveInput[] withGroup = [.. corpus.Select(f => f.File with { Kind = (FileKind)f.Group })];
        long original = asIs.Sum(f => (long)f.Data.Length);

        var results = new List<BlockImpactMeasurement>();
        foreach (PipelineStage stage in Enum.GetValues<PipelineStage>())
        {
            (ArchiveInput[] inputs, PackOptions variant) = stage switch
            {
                PipelineStage.None => (asIs, options),
                PipelineStage.Analysis => (withGroup, options with { Analyzer = new FixedStrategy(CompressionStrategy.Lz77Huffman) }),
                PipelineStage.Lz77 => (asIs, options with { Compression = (options.Compression ?? new Lz77SearchOptions()) with { LiteralsOnly = true } }),
                PipelineStage.Compression => (withGroup, options with { Analyzer = new FixedStrategy(CompressionStrategy.NoCompression) }),
                PipelineStage.Packing => (asIs, options with { Algorithm = PackingAlgorithm.Greedy }),
                _ => (asIs, options),
            };
            bool encrypted = stage != PipelineStage.Encryption;

            Measure(inputs, variant, encrypted);
            (long ArchiveBytes, int Volumes, double Seconds, bool AllRestored)[] runs =
                [.. Enumerable.Range(0, repeats).Select(_ => Measure(inputs, variant, encrypted))];
            double seconds = StrategyComparison.Median([.. runs.Select(r => r.Seconds)]);
            bool restored = runs.All(r => r.AllRestored);
            results.Add(new BlockImpactMeasurement(stage, original, runs[0].ArchiveBytes, runs[0].Volumes, seconds, restored,
                QMetric.Compute(alpha, beta, tRefSeconds, original, runs[0].ArchiveBytes, seconds, restored)));
        }
        return results;
    }

    private static (long ArchiveBytes, int Volumes, double Seconds, bool AllRestored) Measure(
        ArchiveInput[] files, PackOptions options, bool encrypted)
    {
        var watch = Stopwatch.StartNew();
        IReadOnlyList<PreparedFile> prepared = ArchivePipeline.Prepare(files, options);
        IReadOnlyList<byte[]> volumes;
        IReadOnlyList<ExtractedFile>? restored = null;
        if (encrypted)
        {
            volumes = ArchivePipeline.Seal(prepared, options);
            ArchiveResult<IReadOnlyList<ExtractedFile>> unpacked = ArchivePipeline.TryUnpack(volumes, options.Password);
            restored = unpacked.Value;
        }
        else
        {
            volumes = options.MaxVolumeBytes > 0
                ? ArchiveWriter.BuildVolumes(prepared, VolumePacker.Pack(prepared, options.MaxVolumeBytes, options.Algorithm, options.Priorities))
                : [ArchiveWriter.BuildFromPrepared(prepared)];
            try
            {
                restored = ArchiveReader.ReadVolumes(volumes);
            }
            catch (ArchiveException)
            {
                // A plain archive that cannot be read counts as files that were not restored.
            }
        }
        double seconds = watch.Elapsed.TotalSeconds;

        Dictionary<string, byte[]> byName = restored?.ToDictionary(f => f.Name, f => f.Data, StringComparer.Ordinal) ?? [];
        bool all = restored is not null && files.All(f => byName.TryGetValue(f.Name, out byte[]? data) && data.AsSpan().SequenceEqual(f.Data));
        return (volumes.Sum(v => (long)v.Length), volumes.Count, seconds, all);
    }

    /// <summary>The same strategy for every file, whatever it is.</summary>
    private sealed class FixedStrategy(CompressionStrategy strategy) : IFileAnalyzer
    {
        public FileAnalysis Analyze(ReadOnlySpan<byte> data) => new(FileKind.Unknown, 1.0, strategy);
    }
}
