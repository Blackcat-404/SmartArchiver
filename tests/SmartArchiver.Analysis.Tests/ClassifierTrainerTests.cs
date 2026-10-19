namespace SmartArchiver.Analysis.Tests;

public class ClassifierTrainerTests
{
    private static readonly TrainingOptions FastOptions = new(HiddenNeurons: 4, Epochs: 60, LearningRate: 0.1, Seeds: [1, 2, 3]);

    /// <summary>Class 0 has the first input near 0.9, class 1 near 0.1; everything else is small noise.</summary>
    private static List<LabeledSample> Separable(int perClass, int seed)
    {
        var random = new Random(seed);
        var samples = new List<LabeledSample>();
        for (int i = 0; i < perClass; i++)
        {
            foreach (int label in new[] { 0, 1 })
            {
                var input = new double[FeatureVector.Length];
                for (int k = 0; k < input.Length; k++)
                    input[k] = random.NextDouble() * 0.05;
                input[0] = (label == 0 ? 0.9 : 0.1) + random.NextDouble() * 0.05;
                samples.Add(new LabeledSample($"{label}/file{i}", label, input));
            }
        }
        return samples;
    }

    private static DatasetSplit SeparableSplit() => new(Separable(20, seed: 1), Separable(8, seed: 2));

    [Fact]
    public void Train_RunsOncePerSeed_WithTheSevenClassNetwork()
    {
        TrainingReport report = ClassifierTrainer.Train(SeparableSplit(), FastOptions);

        Assert.Equal([1, 2, 3], report.Runs.Select(r => r.Seed));
        Assert.All(report.Runs, r => Assert.Equal([FeatureVector.Length, 4, LabeledDataset.ClassCount], r.Network.LayerSizes));
    }

    [Fact]
    public void Train_SelectsTheRunWithBestValidMacroF1()
    {
        TrainingReport report = ClassifierTrainer.Train(SeparableSplit(), FastOptions);

        Assert.Equal(report.Runs.Max(r => r.Valid.MacroF1), report.Selected.Valid.MacroF1);
        Assert.Equal(1, report.Selected.Valid.MacroF1);
    }

    [Fact]
    public void Train_LossFalls()
    {
        TrainingReport report = ClassifierTrainer.Train(SeparableSplit(), FastOptions);

        Assert.All(report.Runs, r => Assert.True(r.FinalLoss < r.FirstEpochLoss / 2, $"seed {r.Seed}: {r.FirstEpochLoss} -> {r.FinalLoss}"));
    }

    [Fact]
    public void Train_ConfusionMatricesCoverEverySample()
    {
        DatasetSplit split = SeparableSplit();

        TrainingRunResult run = ClassifierTrainer.Train(split, FastOptions).Runs[0];

        Assert.Equal(split.Train.Count, run.Train.ToArray().Sum(row => row.Sum()));
        Assert.Equal(split.Valid.Count, run.Valid.ToArray().Sum(row => row.Sum()));
    }

    [Fact]
    public void Train_SameOptions_GiveSameResult()
    {
        TrainingReport a = ClassifierTrainer.Train(SeparableSplit(), FastOptions);
        TrainingReport b = ClassifierTrainer.Train(SeparableSplit(), FastOptions);

        Assert.Equal(a.Runs.Select(r => r.FinalLoss), b.Runs.Select(r => r.FinalLoss));
    }

    [Fact]
    public void Train_NoValidSamples_SelectsByTrainMacroF1()
    {
        var split = new DatasetSplit(Separable(20, seed: 1), []);

        TrainingReport report = ClassifierTrainer.Train(split, FastOptions);

        Assert.Equal(report.Runs.Max(r => r.Train.MacroF1), report.Selected.Train.MacroF1);
    }

    [Fact]
    public void Train_NoSeedsOrNoTrainSamples_Throws()
    {
        Assert.Throws<ArgumentException>(() => ClassifierTrainer.Train(SeparableSplit(), FastOptions with { Seeds = [] }));
        Assert.Throws<ArgumentException>(() => ClassifierTrainer.Train(new DatasetSplit([], Separable(2, 1)), FastOptions));
    }
}
