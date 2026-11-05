using Microsoft.EntityFrameworkCore;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data.Tests;

public class ExperimentStoreTests
{
    [Fact]
    public void DisabledStore_AcceptsExperimentsAndReturnsNothing()
    {
        IMeasurementStore store = MeasurementStore.Open("");

        store.SaveExperiment(TrainingExperiment());

        Assert.Null(store.GetExperiment(1));
        Assert.Empty(store.ListExperiments(ExperimentKind.Training));
    }

    [DatabaseFact]
    public void SaveExperiment_Training_IsReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment experiment = TrainingExperiment();

        store.SaveExperiment(experiment);
        Experiment saved = store.GetExperiment(experiment.Id)!;

        Assert.Equal(ExperimentKind.Training, saved.Kind);
        Assert.Equal(experiment.StartedAtUtc, saved.StartedAtUtc);
        Assert.Equal(experiment.CommitHash, saved.CommitHash);
        Assert.Equal(experiment.Seed, saved.Seed);
        Assert.Equal(experiment.Dataset, saved.Dataset);
        Assert.Equal(experiment.ParametersJson, saved.ParametersJson);
        Assert.Equal(
            experiment.TrainingRuns.Select(r => (r.InitSeed, r.LayerSizes, r.Epochs, r.LearningRate, r.TrainLoss,
                r.TrainMacroF1, r.ValidMacroF1, r.ConfusionMatrixJson, r.IsSelected)),
            saved.TrainingRuns.OrderBy(r => r.Id).Select(r => (r.InitSeed, r.LayerSizes, r.Epochs, r.LearningRate,
                r.TrainLoss, r.TrainMacroF1, r.ValidMacroF1, r.ConfusionMatrixJson, r.IsSelected)));
    }

    [DatabaseFact]
    public void SaveExperiment_ParsingAndPacking_AreReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment parsing = ParsingExperiment(), packing = PackingExperiment();

        store.SaveExperiment(parsing);
        store.SaveExperiment(packing);

        Assert.Equal(
            parsing.ParsingResults.Select(r => (r.Group, r.Method, r.OriginalBytes, r.EncodedBytes, r.ElapsedSeconds)),
            store.GetExperiment(parsing.Id)!.ParsingResults.OrderBy(r => r.Id)
                .Select(r => (r.Group, r.Method, r.OriginalBytes, r.EncodedBytes, r.ElapsedSeconds)));
        Assert.Equal(
            packing.PackingResults.Select(r => (r.Method, r.VolumeIndex, r.CapacityBytes, r.UsedBytes, r.TotalValue,
                r.BlockCount, r.ElapsedSeconds)),
            store.GetExperiment(packing.Id)!.PackingResults.OrderBy(r => r.Id).Select(r => (r.Method, r.VolumeIndex,
                r.CapacityBytes, r.UsedBytes, r.TotalValue, r.BlockCount, r.ElapsedSeconds)));
    }

    [DatabaseFact]
    public void SaveExperiment_Prediction_IsReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment prediction = PredictionExperiment();

        store.SaveExperiment(prediction);

        Assert.Equal(
            prediction.PredictionResults.Select(r => (r.Group, r.OriginalBytes, r.OriginalEntropy, r.ResidualEntropy,
                r.EncodedBytes, r.PredictedEncodedBytes, r.ElapsedSeconds)),
            store.GetExperiment(prediction.Id)!.PredictionResults.OrderBy(r => r.Id).Select(r => (r.Group, r.OriginalBytes,
                r.OriginalEntropy, r.ResidualEntropy, r.EncodedBytes, r.PredictedEncodedBytes, r.ElapsedSeconds)));
    }

    [DatabaseFact]
    public void SaveExperiment_Strategy_IsReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment strategy = StrategyExperiment();

        store.SaveExperiment(strategy);

        Assert.Equal(
            strategy.StrategyResults.Select(r => (r.Group, r.Variant, r.FileCount, r.CompressedFiles, r.OriginalBytes,
                r.ArchiveBytes, r.PackSeconds, r.UnpackSeconds, r.AllRestored)),
            store.GetExperiment(strategy.Id)!.StrategyResults.OrderBy(r => r.Id).Select(r => (r.Group, r.Variant,
                r.FileCount, r.CompressedFiles, r.OriginalBytes, r.ArchiveBytes, r.PackSeconds, r.UnpackSeconds, r.AllRestored)));
    }

    [DatabaseFact]
    public void SaveExperiment_GeneticJournal_IsReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment genetic = GeneticExperiment();

        store.SaveExperiment(genetic);

        Assert.Equal(
            genetic.GeneticIndividuals.Select(r => (r.Algorithm, r.Generation, r.Individual, r.GenesJson, r.Fitness, r.S, r.T)),
            store.GetExperiment(genetic.Id)!.GeneticIndividuals.OrderBy(r => r.Id)
                .Select(r => (r.Algorithm, r.Generation, r.Individual, r.GenesJson, r.Fitness, r.S, r.T)));
    }

    [DatabaseFact]
    public void SaveExperiment_BlockImpactAndEncryptionOrder_AreReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment impact = BlockImpactExperiment(), order = EncryptionOrderExperiment();

        store.SaveExperiment(impact);
        store.SaveExperiment(order);

        Assert.Equal(
            impact.BlockImpactResults.Select(r => (r.DisabledBlock, r.OriginalBytes, r.ArchiveBytes, r.ElapsedSeconds,
                r.AllRestored, r.S, r.T, r.Q)),
            store.GetExperiment(impact.Id)!.BlockImpactResults.OrderBy(r => r.Id).Select(r => (r.DisabledBlock,
                r.OriginalBytes, r.ArchiveBytes, r.ElapsedSeconds, r.AllRestored, r.S, r.T, r.Q)));
        Assert.Equal(
            order.EncryptionOrderResults.Select(r => (r.Group, r.OriginalBytes, r.CompressThenEncryptBytes, r.EncryptThenCompressBytes)),
            store.GetExperiment(order.Id)!.EncryptionOrderResults.OrderBy(r => r.Id)
                .Select(r => (r.Group, r.OriginalBytes, r.CompressThenEncryptBytes, r.EncryptThenCompressBytes)));
    }

    [DatabaseFact]
    public void SaveExperiment_PredictionQ_IsReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment experiment = PredictionQExperiment();

        store.SaveExperiment(experiment);

        Assert.Equal(
            experiment.PredictionQResults.Select(r => (r.Group, r.Predicted, r.FileCount, r.OriginalBytes, r.ArchiveBytes,
                r.ElapsedSeconds, r.AllRestored, r.S, r.T, r.Q)),
            store.GetExperiment(experiment.Id)!.PredictionQResults.OrderBy(r => r.Id).Select(r => (r.Group, r.Predicted,
                r.FileCount, r.OriginalBytes, r.ArchiveBytes, r.ElapsedSeconds, r.AllRestored, r.S, r.T, r.Q)));
    }

    [DatabaseFact]
    public void ListExperiments_ReturnsOnlyThatKind_OldestFirst()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment later = TrainingExperiment(), earlier = TrainingExperiment();
        earlier.StartedAtUtc = later.StartedAtUtc.AddHours(-1);

        store.SaveExperiment(later);
        store.SaveExperiment(ParsingExperiment());
        store.SaveExperiment(earlier);

        Assert.Equal([earlier.Id, later.Id], store.ListExperiments(ExperimentKind.Training).Select(e => e.Id));
    }

    [DatabaseFact]
    public void GetExperiment_UnknownId_ReturnsNull()
    {
        using var database = new TestDatabase();

        Assert.Null(MeasurementStore.Open(database.ConnectionString).GetExperiment(12345));
    }

    [DatabaseFact]
    public void SaveExperiment_TextWithSqlSyntax_IsStoredAsPlainText()
    {
        // Values reach SQL Server as parameters, so this string is data and never becomes part of a command.
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment experiment = ParsingExperiment();
        experiment.Dataset = "x'; DROP TABLE Experiments; --";

        store.SaveExperiment(experiment);

        Assert.Equal("x'; DROP TABLE Experiments; --", store.GetExperiment(experiment.Id)!.Dataset);
        Assert.Single(store.ListExperiments(ExperimentKind.Parsing));
    }

    [DatabaseFact]
    public void SaveExperiment_OneResultBreaksConstraint_NothingIsSaved()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment experiment = TrainingExperiment();
        experiment.TrainingRuns[1].ValidMacroF1 = 1.5;

        Assert.Throws<DatabaseException>(() => store.SaveExperiment(experiment));

        Assert.Empty(store.ListExperiments(ExperimentKind.Training));
        using var db = database.CreateContext();
        Assert.Equal(0, db.TrainingRuns.Count());
    }

    [DatabaseTheory]
    [InlineData("parsing group above 6")]
    [InlineData("parsing negative size")]
    [InlineData("packing uses more than capacity")]
    [InlineData("packing negative volume index")]
    [InlineData("training macro-F1 above 1")]
    [InlineData("training zero epochs")]
    [InlineData("training zero learning rate")]
    [InlineData("experiment empty dataset")]
    [InlineData("prediction group above 6")]
    [InlineData("prediction entropy above 8")]
    [InlineData("prediction negative size")]
    [InlineData("strategy group above 6")]
    [InlineData("strategy more compressed files than files")]
    [InlineData("strategy negative time")]
    [InlineData("genetic negative generation")]
    [InlineData("genetic negative individual")]
    [InlineData("genetic S above 1")]
    [InlineData("genetic negative fitness")]
    [InlineData("genetic genes not json")]
    [InlineData("genetic same individual twice")]
    [InlineData("block impact Q above zero without restore")]
    [InlineData("block impact T above 1")]
    [InlineData("block impact same block twice")]
    [InlineData("block impact unknown block")]
    [InlineData("encryption order group above 6")]
    [InlineData("encryption order negative size")]
    [InlineData("prediction Q group above 6")]
    [InlineData("prediction Q above zero without restore")]
    [InlineData("prediction Q S above 1")]
    [InlineData("prediction Q same group and variant twice")]
    public void SaveExperiment_ValueOutsideConstraint_IsRejected(string violation)
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        Experiment experiment = violation switch
        {
            "parsing group above 6" => With(ParsingExperiment(), e => e.ParsingResults[0].Group = 7),
            "parsing negative size" => With(ParsingExperiment(), e => e.ParsingResults[0].EncodedBytes = -1),
            "packing uses more than capacity" => With(PackingExperiment(), e => e.PackingResults[0].UsedBytes = e.PackingResults[0].CapacityBytes + 1),
            "packing negative volume index" => With(PackingExperiment(), e => e.PackingResults[0].VolumeIndex = -1),
            "training macro-F1 above 1" => With(TrainingExperiment(), e => e.TrainingRuns[0].TrainMacroF1 = 1.01),
            "training zero epochs" => With(TrainingExperiment(), e => e.TrainingRuns[0].Epochs = 0),
            "training zero learning rate" => With(TrainingExperiment(), e => e.TrainingRuns[0].LearningRate = 0),
            "experiment empty dataset" => With(TrainingExperiment(), e => e.Dataset = ""),
            "prediction group above 6" => With(PredictionExperiment(), e => e.PredictionResults[0].Group = 7),
            "prediction entropy above 8" => With(PredictionExperiment(), e => e.PredictionResults[0].ResidualEntropy = 8.5),
            "prediction negative size" => With(PredictionExperiment(), e => e.PredictionResults[0].PredictedEncodedBytes = -1),
            "strategy group above 6" => With(StrategyExperiment(), e => e.StrategyResults[0].Group = 7),
            "strategy more compressed files than files" => With(StrategyExperiment(), e => e.StrategyResults[0].CompressedFiles = 4),
            "strategy negative time" => With(StrategyExperiment(), e => e.StrategyResults[0].UnpackSeconds = -0.1),
            "genetic negative generation" => With(GeneticExperiment(), e => e.GeneticIndividuals[0].Generation = -1),
            "genetic negative individual" => With(GeneticExperiment(), e => e.GeneticIndividuals[0].Individual = -1),
            "genetic S above 1" => With(GeneticExperiment(), e => e.GeneticIndividuals[0].S = 1.2),
            "genetic negative fitness" => With(GeneticExperiment(), e => e.GeneticIndividuals[0].Fitness = -0.1),
            "genetic genes not json" => With(GeneticExperiment(), e => e.GeneticIndividuals[0].GenesJson = "window=4096"),
            "genetic same individual twice" => With(GeneticExperiment(), e => e.GeneticIndividuals[1].Individual = 0),
            "block impact Q above zero without restore" => With(BlockImpactExperiment(), e => e.BlockImpactResults[1].AllRestored = false),
            "block impact T above 1" => With(BlockImpactExperiment(), e => e.BlockImpactResults[0].T = 1.5),
            "block impact same block twice" => With(BlockImpactExperiment(), e => e.BlockImpactResults[1].DisabledBlock = PipelineBlock.None),
            "block impact unknown block" => With(BlockImpactExperiment(), e => e.BlockImpactResults[0].DisabledBlock = (PipelineBlock)42),
            "encryption order group above 6" => With(EncryptionOrderExperiment(), e => e.EncryptionOrderResults[0].Group = 7),
            "encryption order negative size" => With(EncryptionOrderExperiment(), e => e.EncryptionOrderResults[0].EncryptThenCompressBytes = -1),
            "prediction Q group above 6" => With(PredictionQExperiment(), e => e.PredictionQResults[0].Group = 7),
            "prediction Q above zero without restore" => With(PredictionQExperiment(), e => e.PredictionQResults[1].AllRestored = false),
            "prediction Q S above 1" => With(PredictionQExperiment(), e => e.PredictionQResults[0].S = 1.1),
            "prediction Q same group and variant twice" => With(PredictionQExperiment(), e => e.PredictionQResults[1].Predicted = false),
            _ => throw new ArgumentOutOfRangeException(nameof(violation)),
        };

        Assert.Throws<DatabaseException>(() => store.SaveExperiment(experiment));

        // One transaction: the experiment and the results that were valid are rolled back too.
        Assert.Empty(store.ListExperiments(experiment.Kind));
        using var db = database.CreateContext();
        Assert.Equal(0, db.ParsingResults.Count() + db.PackingResults.Count() + db.TrainingRuns.Count()
            + db.PredictionResults.Count() + db.StrategyResults.Count() + db.GeneticIndividuals.Count()
            + db.BlockImpactResults.Count() + db.EncryptionOrderResults.Count() + db.PredictionQResults.Count());
    }

    [DatabaseTheory]
    [InlineData("negative Q")]
    [InlineData("zero t_ref")]
    [InlineData("negative archive size")]
    [InlineData("file group above 6")]
    public void SaveRun_ValueOutsideConstraint_IsRejected(string violation)
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        MeasurementRun run = new()
        {
            StartedAtUtc = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
            Dataset = "corpus",
            Alpha = 0.5,
            Beta = 0.5,
            TRefSeconds = 10,
            OriginalBytes = 100,
            ArchiveBytes = 80,
            ElapsedSeconds = 1,
            AllRestored = true,
            Q = 0.6,
            Files = [new() { Group = 0, OriginalBytes = 100, ArchiveBytes = 80, Restored = true }],
        };
        switch (violation)
        {
            case "negative Q": run.Q = -0.1; break;
            case "zero t_ref": run.TRefSeconds = 0; break;
            case "negative archive size": run.ArchiveBytes = -1; break;
            case "file group above 6": run.Files[0].Group = 7; break;
            default: throw new ArgumentOutOfRangeException(nameof(violation));
        }

        Assert.Throws<DatabaseException>(() => store.SaveRun(run));

        // The run and its files are saved together or not at all.
        Assert.Equal(0, store.CountRuns());
        using var db = database.CreateContext();
        Assert.Equal(0, db.MeasurementFileResults.Count());
    }

    private static Experiment With(Experiment experiment, Action<Experiment> change)
    {
        change(experiment);
        return experiment;
    }

    private static Experiment NewExperiment(ExperimentKind kind) => new()
    {
        Kind = kind,
        StartedAtUtc = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc),
        CommitHash = "fe0ac9a",
        Seed = 42,
        Dataset = "corpus",
        ParametersJson = """{"window":32768}""",
    };

    private static Experiment ParsingExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.Parsing);
        e.ParsingResults =
        [
            new() { Group = 0, Method = ParsingMethod.Greedy, OriginalBytes = 10_000, EncodedBytes = 4_200, ElapsedSeconds = 0.12 },
            new() { Group = 0, Method = ParsingMethod.Lazy, OriginalBytes = 10_000, EncodedBytes = 4_050, ElapsedSeconds = 0.15 },
            new() { Group = 5, Method = ParsingMethod.Dp, OriginalBytes = 8_000, EncodedBytes = 8_000, ElapsedSeconds = 0.4 },
        ];
        return e;
    }

    private static Experiment PackingExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.Packing);
        e.PackingResults =
        [
            new() { Method = PackingMethod.Dp, VolumeIndex = 0, CapacityBytes = 65_000, UsedBytes = 64_950, TotalValue = 37.5, BlockCount = 12, ElapsedSeconds = 0.8 },
            new() { Method = PackingMethod.Greedy, VolumeIndex = 0, CapacityBytes = 65_000, UsedBytes = 61_200, TotalValue = 33, BlockCount = 11, ElapsedSeconds = 0.01 },
        ];
        return e;
    }

    private static Experiment StrategyExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.Strategy);
        e.StrategyResults =
        [
            new() { Group = 0, Variant = StrategyVariant.Stored, FileCount = 3, CompressedFiles = 0, OriginalBytes = 150_000, ArchiveBytes = 150_400, PackSeconds = 0.01, UnpackSeconds = 0.01, AllRestored = true },
            new() { Group = 0, Variant = StrategyVariant.Fixed, FileCount = 3, CompressedFiles = 3, OriginalBytes = 150_000, ArchiveBytes = 48_000, PackSeconds = 0.4, UnpackSeconds = 0.05, AllRestored = true },
            new() { Group = 5, Variant = StrategyVariant.Classifier, FileCount = 3, CompressedFiles = 0, OriginalBytes = 76_000, ArchiveBytes = 76_300, PackSeconds = 0.02, UnpackSeconds = 0.01, AllRestored = true },
            new() { Group = 5, Variant = StrategyVariant.ClassifierPerBlock, FileCount = 3, CompressedFiles = 1, OriginalBytes = 76_000, ArchiveBytes = 70_100, PackSeconds = 0.03, UnpackSeconds = 0.01, AllRestored = true },
        ];
        return e;
    }

    private static Experiment GeneticExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.Genetic);
        e.GeneticIndividuals =
        [
            new() { Algorithm = SearchAlgorithm.Genetic, Generation = 0, Individual = 0, GenesJson = """{"window":4096,"min_match":3,"no_compress_threshold":7.5}""", Fitness = 0.61, S = 0.72, T = 0.5 },
            new() { Algorithm = SearchAlgorithm.Genetic, Generation = 0, Individual = 1, GenesJson = """{"window":1024,"min_match":4,"no_compress_threshold":7.9}""", Fitness = 0.58, S = 0.66, T = 0.5 },
            new() { Algorithm = SearchAlgorithm.Random, Generation = 0, Individual = 0, GenesJson = """{"window":256,"min_match":5,"no_compress_threshold":6}""", Fitness = 0.4, S = 0.3, T = 0.5 },
        ];
        return e;
    }

    private static Experiment BlockImpactExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.BlockImpact);
        e.BlockImpactResults =
        [
            new() { DisabledBlock = PipelineBlock.None, OriginalBytes = 930_000, ArchiveBytes = 200_000, ElapsedSeconds = 1.2, AllRestored = true, S = 0.82, T = 0.45, Q = 0.64 },
            new() { DisabledBlock = PipelineBlock.Lz77, OriginalBytes = 930_000, ArchiveBytes = 600_000, ElapsedSeconds = 0.6, AllRestored = true, S = 0.61, T = 0.62, Q = 0.62 },
            new() { DisabledBlock = PipelineBlock.Encryption, OriginalBytes = 930_000, ArchiveBytes = 199_900, ElapsedSeconds = 0.4, AllRestored = true, S = 0.82, T = 0.71, Q = 0.77 },
        ];
        return e;
    }

    private static Experiment EncryptionOrderExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.EncryptionOrder);
        e.EncryptionOrderResults =
        [
            new() { Group = 0, OriginalBytes = 156_000, CompressThenEncryptBytes = 4_200, EncryptThenCompressBytes = 156_400 },
            new() { Group = 5, OriginalBytes = 76_000, CompressThenEncryptBytes = 76_300, EncryptThenCompressBytes = 76_300 },
        ];
        return e;
    }

    private static Experiment PredictionQExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.PredictionQ);
        e.PredictionQResults =
        [
            new() { Group = 0, Predicted = false, FileCount = 3, OriginalBytes = 156_000, ArchiveBytes = 4_400, ElapsedSeconds = 0.3, AllRestored = true, S = 0.97, T = 0.77, Q = 0.87 },
            new() { Group = 0, Predicted = true, FileCount = 3, OriginalBytes = 156_000, ArchiveBytes = 5_600, ElapsedSeconds = 0.35, AllRestored = true, S = 0.97, T = 0.74, Q = 0.85 },
        ];
        return e;
    }

    private static Experiment PredictionExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.Prediction);
        e.PredictionResults =
        [
            new() { Group = 0, OriginalBytes = 40_000, OriginalEntropy = 4.7, ResidualEntropy = 3.9, EncodedBytes = 16_000, PredictedEncodedBytes = 15_200, ElapsedSeconds = 0.3 },
            new() { Group = 5, OriginalBytes = 18_000, OriginalEntropy = 7.99, ResidualEntropy = 7.99, EncodedBytes = 18_000, PredictedEncodedBytes = 18_008, ElapsedSeconds = 0.2 },
        ];
        return e;
    }

    private static Experiment TrainingExperiment()
    {
        Experiment e = NewExperiment(ExperimentKind.Training);
        e.Dataset = "train/valid";
        e.TrainingRuns =
        [
            new() { InitSeed = 1, LayerSizes = "54,32,7", Epochs = 200, LearningRate = 0.05, TrainLoss = 0.41, TrainMacroF1 = 0.88, ValidMacroF1 = 0.81, ConfusionMatrixJson = "[[10,1],[2,9]]", IsSelected = true },
            new() { InitSeed = 2, LayerSizes = "54,32,7", Epochs = 200, LearningRate = 0.05, TrainLoss = 0.47, TrainMacroF1 = 0.86, ValidMacroF1 = 0.77, ConfusionMatrixJson = null, IsSelected = false },
        ];
        return e;
    }
}
