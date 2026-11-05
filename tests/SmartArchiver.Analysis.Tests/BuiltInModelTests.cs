using SmartArchiver.Core;

namespace SmartArchiver.Analysis.Tests;

/// <summary>Checks the model shipped in the assembly against the team dataset it was trained on.</summary>
public class BuiltInModelTests
{
    private static string DatasetFolder()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartArchiver.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."), "Datasets");
    }

    [Fact]
    public void BuiltIn_LoadsWithTheClassifierShape()
    {
        Assert.Equal(TrainingOptions.Default.LayerSizes, FileClassifier.BuiltIn.Network.LayerSizes);
    }

    [Fact]
    public void BuiltIn_ReachesExpectedMacroF1OnTeamDataset()
    {
        DatasetSplit split = LabeledDataset.SplitByFile(
            LabeledDataset.Load(DatasetFolder(), LabeledDataset.DefaultChunkSize, LabeledDataset.DefaultMaxChunksPerFile));
        Perceptron network = FileClassifier.BuiltIn.Network;

        // Recorded in decision 26: train 1.0, valid 0.714 (code and CSV mixed up, see the confusion matrix there).
        Assert.True(ClassifierTrainer.Evaluate(network, split.Train).MacroF1 >= 0.99);
        Assert.True(ClassifierTrainer.Evaluate(network, split.Valid).MacroF1 >= 0.7);
    }

    [Fact]
    public void BuiltInPredictor_IsDeterministicSoftsignNetwork()
    {
        BytePredictor predictor = BytePredictor.BuiltIn;

        Assert.Equal(HiddenActivation.Softsign, predictor.Network.Activation);
        Assert.Equal(PredictorOptions.Default.ContextBytes, predictor.ContextBytes);
        Assert.Equal(256, predictor.Network.LayerSizes[^1]);
    }

    [Fact]
    public void BuiltInPredictor_RestoresEveryValidFileOfTheDataset()
    {
        (_, IReadOnlyList<LabeledFile> valid) = LabeledDataset.SplitFiles(LabeledDataset.ListFiles(DatasetFolder()));
        BytePredictor predictor = BytePredictor.BuiltIn;

        Assert.Equal(7, valid.Count);
        foreach (LabeledFile file in valid)
        {
            byte[] data = File.ReadAllBytes(file.FullPath);
            Assert.Equal(data, predictor.Decode(predictor.Encode(data)));
        }
    }

    [Fact]
    public void DefaultAnalyzer_UsesBuiltInModel()
    {
        FileAnalysis analysis = new FileAnalyzer().Analyze(File.ReadAllBytes(Path.Combine(DatasetFolder(), "5", "noise.png")));

        Assert.Equal(FileKind.AlreadyCompressed, analysis.Kind);
        Assert.Equal(CompressionStrategy.NoCompression, analysis.Strategy);
    }
}
