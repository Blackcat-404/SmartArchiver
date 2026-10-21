namespace SmartArchiver.Data.Entities;

/// <summary>Which CP2 experiment a row belongs to. Stored as text, so the table stays readable.</summary>
public enum ExperimentKind
{
    /// <summary>Greedy, lazy and DP parsing of the stream (LZ77).</summary>
    Parsing,

    /// <summary>DP knapsack against greedy packing of volumes.</summary>
    Packing,

    /// <summary>Training the file type classifier.</summary>
    Training,

    /// <summary>Next-byte prediction: entropy and compressed size of residuals against the original.</summary>
    Prediction,

    /// <summary>Fixed compression strategy against the strategy chosen by the classifier.</summary>
    Strategy,
}

/// <summary>
/// One run of an experiment with its run-log fields (date, commit, seed, parameters, dataset).
/// The results are in the collection that matches <see cref="Kind"/>.
/// </summary>
public sealed class Experiment
{
    public int Id { get; set; }

    public ExperimentKind Kind { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public string? CommitHash { get; set; }

    public int? Seed { get; set; }

    /// <summary>Dataset name, for example "corpus" or "train/valid".</summary>
    public string Dataset { get; set; } = "";

    public string ParametersJson { get; set; } = "{}";

    public List<ParsingResult> ParsingResults { get; set; } = [];

    public List<PackingResult> PackingResults { get; set; } = [];

    public List<TrainingRun> TrainingRuns { get; set; } = [];

    public List<PredictionResult> PredictionResults { get; set; } = [];

    public List<StrategyResult> StrategyResults { get; set; } = [];
}
