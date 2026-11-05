using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data;

/// <param name="Ratio">OriginalBytes / EncodedBytes, how many times smaller.</param>
public sealed record ParsingRow(byte Group, ParsingMethod Method, long OriginalBytes, long EncodedBytes, double Ratio, double Seconds);

/// <param name="QChange">Q minus Q of the full pipeline; negative means the block helped. Null without a baseline row.</param>
public sealed record BlockImpactRow(
    PipelineBlock DisabledBlock, long ArchiveBytes, double Seconds, bool AllRestored, double S, double T, double Q, double? QChange);

public sealed record PackingRow(
    PackingMethod Method, long CapacityBytes, long UsedBytes, double FillPercent, double TotalValue, int BlockCount, double Seconds);

/// <param name="CompressThenEncryptRatio">OriginalBytes / CompressThenEncryptBytes.</param>
/// <param name="EncryptThenCompressRatio">OriginalBytes / EncryptThenCompressBytes.</param>
public sealed record EncryptionOrderRow(
    byte Group, long OriginalBytes, long CompressThenEncryptBytes, long EncryptThenCompressBytes,
    double CompressThenEncryptRatio, double EncryptThenCompressRatio);

/// <param name="Evaluations">Fitness evaluations up to and including this generation, the fair axis for GA against random search.</param>
/// <param name="BestSoFar">Best fitness found in this and all earlier generations.</param>
public sealed record ConvergenceRow(SearchAlgorithm Algorithm, int Generation, int Evaluations, double Best, double Mean, double BestSoFar);

/// <summary>A non-dominated chromosome in the compression/speed objective plane.</summary>
public sealed record ParetoRow(
    SearchAlgorithm Algorithm,
    int Generation,
    int Individual,
    string GenesJson,
    double Fitness,
    double S,
    double T);

/// <summary>The run-log fields of the experiment a report table was built from.</summary>
public sealed record ReportSource(int ExperimentId, DateTime StartedAtUtc, string? CommitHash, string Dataset);

/// <param name="Source">Null when the database has no experiment with rows for this table.</param>
public sealed record ReportTable<TRow>(ReportSource? Source, IReadOnlyList<TRow> Rows);

/// <summary>
/// Rows of the five mandatory report tables, computed from experiment results. Only data: formatting the tables
/// (Markdown, CSV) is up to the report generator.
/// </summary>
public static class ReportTables
{
    /// <summary>Table 1: size and time of each parsing method per corpus group.</summary>
    public static IReadOnlyList<ParsingRow> Parsing(Experiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        return [.. experiment.ParsingResults
            .OrderBy(r => r.Group)
            .ThenBy(r => r.Method)
            .Select(r => new ParsingRow(r.Group, r.Method, r.OriginalBytes, r.EncodedBytes,
                Ratio(r.OriginalBytes, r.EncodedBytes), r.ElapsedSeconds))];
    }

    /// <summary>Table 2: Q with each block switched off in turn, the full pipeline first.</summary>
    public static IReadOnlyList<BlockImpactRow> BlockImpact(Experiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        double? baseline = experiment.BlockImpactResults.FirstOrDefault(r => r.DisabledBlock == PipelineBlock.None)?.Q;
        return [.. experiment.BlockImpactResults
            .OrderBy(r => r.DisabledBlock)
            .Select(r => new BlockImpactRow(r.DisabledBlock, r.ArchiveBytes, r.ElapsedSeconds, r.AllRestored, r.S, r.T, r.Q,
                r.Q - baseline))];
    }

    /// <summary>Table 3: how DP and greedy packing filled the first volume.</summary>
    public static IReadOnlyList<PackingRow> FirstVolumePacking(Experiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        return [.. experiment.PackingResults
            .Where(r => r.VolumeIndex == 0)
            .OrderBy(r => r.Method)
            .Select(r => new PackingRow(r.Method, r.CapacityBytes, r.UsedBytes,
                r.CapacityBytes == 0 ? 0 : 100.0 * r.UsedBytes / r.CapacityBytes, r.TotalValue, r.BlockCount, r.ElapsedSeconds))];
    }

    /// <summary>Table 4: both orders of compression and encryption, files summed per group.</summary>
    public static IReadOnlyList<EncryptionOrderRow> EncryptionOrder(Experiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        return [.. experiment.EncryptionOrderResults
            .GroupBy(r => r.Group)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                long original = g.Sum(r => r.OriginalBytes);
                long compressFirst = g.Sum(r => r.CompressThenEncryptBytes);
                long encryptFirst = g.Sum(r => r.EncryptThenCompressBytes);
                return new EncryptionOrderRow(g.Key, original, compressFirst, encryptFirst,
                    Ratio(original, compressFirst), Ratio(original, encryptFirst));
            })];
    }

    /// <summary>Table 5: convergence of the GA against random search, one row per generation.</summary>
    public static IReadOnlyList<ConvergenceRow> Convergence(Experiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        var rows = new List<ConvergenceRow>();
        foreach (IGrouping<SearchAlgorithm, GeneticIndividual> algorithm in experiment.GeneticIndividuals
            .GroupBy(i => i.Algorithm)
            .OrderBy(g => g.Key))
        {
            int evaluations = 0;
            double bestSoFar = double.NegativeInfinity;
            foreach (IGrouping<int, GeneticIndividual> generation in algorithm.GroupBy(i => i.Generation).OrderBy(g => g.Key))
            {
                evaluations += generation.Count();
                double best = generation.Max(i => i.Fitness);
                bestSoFar = Math.Max(bestSoFar, best);
                rows.Add(new ConvergenceRow(algorithm.Key, generation.Key, evaluations, best, generation.Average(i => i.Fitness), bestSoFar));
            }
        }
        return rows;
    }

    /// <summary>Expert table: the Pareto front maximising compression and speed separately.</summary>
    public static IReadOnlyList<ParetoRow> ParetoFront(Experiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        List<GeneticIndividual> all = [.. experiment.GeneticIndividuals];
        var front = new List<ParetoRow>();
        for (int i = 0; i < all.Count; i++)
        {
            GeneticIndividual candidate = all[i];
            bool dominated = false;
            for (int j = 0; j < all.Count; j++)
            {
                if (i == j)
                    continue;
                GeneticIndividual other = all[j];
                if (other.S >= candidate.S && other.T >= candidate.T &&
                    (other.S > candidate.S || other.T > candidate.T))
                {
                    dominated = true;
                    break;
                }
            }
            if (!dominated)
            {
                front.Add(new ParetoRow(
                    candidate.Algorithm,
                    candidate.Generation,
                    candidate.Individual,
                    candidate.GenesJson,
                    candidate.Fitness,
                    candidate.S,
                    candidate.T));
            }
        }
        return [.. front
            .OrderByDescending(row => row.S)
            .ThenByDescending(row => row.T)
            .ThenBy(row => row.Algorithm)
            .ThenBy(row => row.Generation)
            .ThenBy(row => row.Individual)];
    }

    private static double Ratio(long original, long encoded) => encoded == 0 ? 0 : (double)original / encoded;
}

/// <summary>All five report tables, each from the newest experiment of its kind that has rows for it.</summary>
public sealed record ReportData(
    ReportTable<ParsingRow> Parsing,
    ReportTable<BlockImpactRow> BlockImpact,
    ReportTable<PackingRow> Packing,
    ReportTable<EncryptionOrderRow> EncryptionOrder,
    ReportTable<ConvergenceRow> Convergence)
{
    public static ReportData Load(IMeasurementStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new ReportData(
            Newest(store, ExperimentKind.Parsing, ReportTables.Parsing),
            Newest(store, ExperimentKind.BlockImpact, ReportTables.BlockImpact),
            Newest(store, ExperimentKind.Packing, ReportTables.FirstVolumePacking),
            Newest(store, ExperimentKind.EncryptionOrder, ReportTables.EncryptionOrder),
            Newest(store, ExperimentKind.Genetic, ReportTables.Convergence));
    }

    /// <summary>
    /// Newest first, the first experiment that gives rows. Some experiments of a kind keep their results in JSON only
    /// (for example iterative DP of kind Parsing), so the newest one is not always the right one.
    /// </summary>
    private static ReportTable<TRow> Newest<TRow>(
        IMeasurementStore store, ExperimentKind kind, Func<Experiment, IReadOnlyList<TRow>> rows)
    {
        foreach (Experiment summary in store.ListExperiments(kind).Reverse())
        {
            Experiment? experiment = store.GetExperiment(summary.Id);
            IReadOnlyList<TRow> table = experiment is null ? [] : rows(experiment);
            if (table.Count > 0)
                return new ReportTable<TRow>(
                    new ReportSource(experiment!.Id, experiment.StartedAtUtc, experiment.CommitHash, experiment.Dataset), table);
        }
        return new ReportTable<TRow>(null, []);
    }
}
