namespace SmartArchiver.Data.Entities;

/// <summary>One initialisation of the classifier: how it trained and how it did on train and valid.</summary>
public sealed class TrainingRun
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    /// <summary>Seed of the weight initialisation.</summary>
    public int InitSeed { get; set; }

    /// <summary>Neurons per layer, for example "54,32,7".</summary>
    public string LayerSizes { get; set; } = "";

    public int Epochs { get; set; }

    public double LearningRate { get; set; }

    public double TrainLoss { get; set; }

    public double TrainMacroF1 { get; set; }

    public double ValidMacroF1 { get; set; }

    /// <summary>Confusion matrix on valid as a JSON array of rows, if it was computed.</summary>
    public string? ConfusionMatrixJson { get; set; }

    /// <summary>True for the run chosen by macro-F1 on valid.</summary>
    public bool IsSelected { get; set; }
}
