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
            _ => throw new ArgumentOutOfRangeException(nameof(violation)),
        };

        Assert.Throws<DatabaseException>(() => store.SaveExperiment(experiment));
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
