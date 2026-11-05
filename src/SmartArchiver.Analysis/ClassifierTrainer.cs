namespace SmartArchiver.Analysis;

/// <summary>Hyperparameters of classifier training. One network is trained per seed.</summary>
public sealed record TrainingOptions(int HiddenNeurons, int Epochs, double LearningRate, IReadOnlyList<int> Seeds)
{
    /// <summary>Values chosen on the team dataset (decision 26 in docs/decisions.md).</summary>
    public static TrainingOptions Default { get; } = new(HiddenNeurons: 16, Epochs: 100, LearningRate: 0.05, Seeds: [1, 2, 3, 4, 5]);

    public IReadOnlyList<int> LayerSizes => [FeatureVector.Length, HiddenNeurons, LabeledDataset.ClassCount];
}

/// <summary>One trained network and how it did.</summary>
public sealed record TrainingRunResult(
    int Seed,
    Perceptron Network,
    double FirstEpochLoss,
    double FinalLoss,
    ConfusionMatrix Train,
    ConfusionMatrix Valid);

public sealed record TrainingReport(IReadOnlyList<TrainingRunResult> Runs, int SelectedIndex)
{
    /// <summary>The run with the best macro-F1 on valid (on train if there is no valid data).</summary>
    public TrainingRunResult Selected => Runs[SelectedIndex];
}

/// <summary>
/// Trains the file type classifier from several initialisations and picks the best one by macro-F1 on valid,
/// as the methodology asks (10.42): one random start can land in a poor local minimum.
/// </summary>
public static class ClassifierTrainer
{
    public static TrainingReport Train(DatasetSplit data, TrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Seeds.Count == 0)
            throw new ArgumentException("At least one seed is needed.", nameof(options));
        if (data.Train.Count == 0)
            throw new ArgumentException("There are no training samples.", nameof(data));

        var runs = new List<TrainingRunResult>();
        foreach (int seed in options.Seeds)
            runs.Add(TrainOne(data, options, seed));

        bool useValid = data.Valid.Count > 0;
        int selected = 0;
        for (int i = 1; i < runs.Count; i++)
        {
            double score = useValid ? runs[i].Valid.MacroF1 : runs[i].Train.MacroF1;
            double best = useValid ? runs[selected].Valid.MacroF1 : runs[selected].Train.MacroF1;
            if (score > best)
                selected = i;
        }
        return new TrainingReport(runs, selected);
    }

    private static TrainingRunResult TrainOne(DatasetSplit data, TrainingOptions options, int seed)
    {
        var network = new Perceptron(options.LayerSizes, seed);
        var shuffle = new Random(seed);
        LabeledSample[] order = [.. data.Train];

        double firstLoss = 0, loss = 0;
        for (int epoch = 0; epoch < options.Epochs; epoch++)
        {
            // Samples arrive grouped by class; without shuffling the network would mostly remember the last group.
            shuffle.Shuffle(order);
            loss = network.TrainEpoch(
                [.. order.Select(s => s.Input)], [.. order.Select(s => s.Label)], options.LearningRate);
            if (epoch == 0)
                firstLoss = loss;
        }

        return new TrainingRunResult(seed, network, firstLoss, loss, Evaluate(network, data.Train), Evaluate(network, data.Valid));
    }

    public static ConfusionMatrix Evaluate(Perceptron network, IReadOnlyList<LabeledSample> samples)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(samples);
        var matrix = new ConfusionMatrix(LabeledDataset.ClassCount);
        foreach (LabeledSample sample in samples)
            matrix.Add(sample.Label, network.Classify(sample.Input));
        return matrix;
    }
}
