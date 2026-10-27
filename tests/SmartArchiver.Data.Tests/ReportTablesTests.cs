using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data.Tests;

public class ReportTablesTests
{
    private static Experiment New(ExperimentKind kind, DateTime? startedAtUtc = null) => new()
    {
        Kind = kind,
        StartedAtUtc = startedAtUtc ?? new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc),
        CommitHash = "3d7ca94",
        Dataset = "corpus",
    };

    private static GeneticIndividual Chromosome(SearchAlgorithm algorithm, int generation, int individual, double fitness) => new()
    {
        Algorithm = algorithm,
        Generation = generation,
        Individual = individual,
        GenesJson = "{}",
        Fitness = fitness,
        S = 0.5,
        T = 0.5,
    };

    [Fact]
    public void Parsing_OrderedByGroupThenMethod_WithRatio()
    {
        Experiment e = New(ExperimentKind.Parsing);
        e.ParsingResults =
        [
            new() { Group = 1, Method = ParsingMethod.Lazy, OriginalBytes = 1000, EncodedBytes = 400, ElapsedSeconds = 0.2 },
            new() { Group = 0, Method = ParsingMethod.Lazy, OriginalBytes = 1000, EncodedBytes = 250, ElapsedSeconds = 0.3 },
            new() { Group = 0, Method = ParsingMethod.Greedy, OriginalBytes = 1000, EncodedBytes = 500, ElapsedSeconds = 0.1 },
        ];

        Assert.Equal(
            [new ParsingRow(0, ParsingMethod.Greedy, 1000, 500, 2.0, 0.1),
             new ParsingRow(0, ParsingMethod.Lazy, 1000, 250, 4.0, 0.3),
             new ParsingRow(1, ParsingMethod.Lazy, 1000, 400, 2.5, 0.2)],
            ReportTables.Parsing(e));
    }

    [Fact]
    public void BlockImpact_BaselineFirst_ChangeOfQIsAgainstTheFullPipeline()
    {
        Experiment e = New(ExperimentKind.BlockImpact);
        e.BlockImpactResults =
        [
            new() { DisabledBlock = PipelineBlock.Encryption, AllRestored = true, S = 0.8, T = 0.7, Q = 0.75 },
            new() { DisabledBlock = PipelineBlock.None, AllRestored = true, S = 0.8, T = 0.4, Q = 0.6 },
            new() { DisabledBlock = PipelineBlock.Lz77, AllRestored = true, S = 0.5, T = 0.5, Q = 0.5 },
        ];

        IReadOnlyList<BlockImpactRow> rows = ReportTables.BlockImpact(e);

        Assert.Equal([PipelineBlock.None, PipelineBlock.Lz77, PipelineBlock.Encryption], rows.Select(r => r.DisabledBlock));
        Assert.Equal([0.0, -0.1, 0.15], rows.Select(r => Math.Round(r.QChange!.Value, 10)));
    }

    [Fact]
    public void BlockImpact_NoBaseline_LeavesTheChangeEmpty()
    {
        Experiment e = New(ExperimentKind.BlockImpact);
        e.BlockImpactResults = [new() { DisabledBlock = PipelineBlock.Lz77, AllRestored = true, S = 0.5, T = 0.5, Q = 0.5 }];

        Assert.Null(Assert.Single(ReportTables.BlockImpact(e)).QChange);
    }

    [Fact]
    public void FirstVolumePacking_KeepsOnlyVolumeZero_WithFillPercent()
    {
        Experiment e = New(ExperimentKind.Packing);
        e.PackingResults =
        [
            new() { Method = PackingMethod.Greedy, VolumeIndex = 0, CapacityBytes = 1000, UsedBytes = 900, TotalValue = 30, BlockCount = 9, ElapsedSeconds = 0.01 },
            new() { Method = PackingMethod.Dp, VolumeIndex = 1, CapacityBytes = 1000, UsedBytes = 100, TotalValue = 5, BlockCount = 1, ElapsedSeconds = 0.1 },
            new() { Method = PackingMethod.Dp, VolumeIndex = 0, CapacityBytes = 1000, UsedBytes = 995, TotalValue = 34, BlockCount = 10, ElapsedSeconds = 0.5 },
        ];

        Assert.Equal(
            [new PackingRow(PackingMethod.Dp, 1000, 995, 99.5, 34, 10, 0.5),
             new PackingRow(PackingMethod.Greedy, 1000, 900, 90.0, 30, 9, 0.01)],
            ReportTables.FirstVolumePacking(e));
    }

    [Fact]
    public void EncryptionOrder_SumsFilesOfAGroup_WithBothRatios()
    {
        Experiment e = New(ExperimentKind.EncryptionOrder);
        e.EncryptionOrderResults =
        [
            new() { Group = 5, OriginalBytes = 100, CompressThenEncryptBytes = 100, EncryptThenCompressBytes = 100 },
            new() { Group = 0, OriginalBytes = 600, CompressThenEncryptBytes = 100, EncryptThenCompressBytes = 610 },
            new() { Group = 0, OriginalBytes = 400, CompressThenEncryptBytes = 100, EncryptThenCompressBytes = 390 },
        ];

        Assert.Equal(
            [new EncryptionOrderRow(0, 1000, 200, 1000, 5.0, 1.0),
             new EncryptionOrderRow(5, 100, 100, 100, 1.0, 1.0)],
            ReportTables.EncryptionOrder(e));
    }

    [Fact]
    public void Convergence_BestMeanAndBestSoFarPerGeneration_WithCumulativeEvaluations()
    {
        Experiment e = New(ExperimentKind.Genetic);
        e.GeneticIndividuals =
        [
            Chromosome(SearchAlgorithm.Genetic, 0, 0, 0.4),
            Chromosome(SearchAlgorithm.Genetic, 0, 1, 0.6),
            Chromosome(SearchAlgorithm.Genetic, 1, 0, 0.5),
            Chromosome(SearchAlgorithm.Genetic, 1, 1, 0.5),
            Chromosome(SearchAlgorithm.Random, 0, 0, 0.3),
            Chromosome(SearchAlgorithm.Random, 0, 1, 0.1),
        ];

        Assert.Equal(
            [new ConvergenceRow(SearchAlgorithm.Genetic, 0, 2, 0.6, 0.5, 0.6),
             new ConvergenceRow(SearchAlgorithm.Genetic, 1, 4, 0.5, 0.5, 0.6),
             new ConvergenceRow(SearchAlgorithm.Random, 0, 2, 0.3, 0.2, 0.3)],
            ReportTables.Convergence(e).Select(r => r with { Mean = Math.Round(r.Mean, 10) }));
    }

    [Fact]
    public void Load_NoDatabase_GivesEmptyTablesWithoutSource()
    {
        ReportData data = ReportData.Load(MeasurementStore.Open(""));

        Assert.All(
            new[] { data.Parsing.Source, data.BlockImpact.Source, data.Packing.Source, data.EncryptionOrder.Source, data.Convergence.Source },
            Assert.Null);
        Assert.Empty(data.Parsing.Rows);
        Assert.Empty(data.Convergence.Rows);
    }

    [DatabaseFact]
    public void Load_TakesTheNewestExperimentThatHasRows()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        DateTime noon = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

        Experiment old = New(ExperimentKind.Parsing, noon);
        old.ParsingResults = [new() { Group = 0, Method = ParsingMethod.Greedy, OriginalBytes = 10, EncodedBytes = 5, ElapsedSeconds = 1 }];
        Experiment newer = New(ExperimentKind.Parsing, noon.AddHours(1));
        newer.ParsingResults = [new() { Group = 0, Method = ParsingMethod.Lazy, OriginalBytes = 10, EncodedBytes = 4, ElapsedSeconds = 1 }];
        // Iterative DP and other CP3 runs of kind Parsing keep their results in JSON only: they are skipped.
        Experiment jsonOnly = New(ExperimentKind.Parsing, noon.AddHours(2));
        store.SaveExperiment(old);
        store.SaveExperiment(newer);
        store.SaveExperiment(jsonOnly);

        ReportData data = ReportData.Load(store);

        Assert.Equal(newer.Id, data.Parsing.Source!.ExperimentId);
        Assert.Equal("3d7ca94", data.Parsing.Source.CommitHash);
        Assert.Equal(ParsingMethod.Lazy, Assert.Single(data.Parsing.Rows).Method);
        Assert.Null(data.Convergence.Source);
    }
}
