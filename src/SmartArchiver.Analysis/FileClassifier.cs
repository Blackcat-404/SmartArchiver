using SmartArchiver.Core;

namespace SmartArchiver.Analysis;

/// <summary>A trained perceptron that maps <see cref="FileFeatures"/> to a file type 0..6.</summary>
public sealed class FileClassifier
{
    private const string BuiltInResource = "SmartArchiver.Analysis.classifier.json";

    private static readonly Lazy<FileClassifier> BuiltInModel = new(LoadBuiltIn);

    public FileClassifier(Perceptron network)
    {
        ArgumentNullException.ThrowIfNull(network);
        if (network.LayerSizes[0] != FeatureVector.Length)
            throw new ArgumentException($"The network must take {FeatureVector.Length} inputs.", nameof(network));
        if (network.LayerSizes[^1] != LabeledDataset.ClassCount)
            throw new ArgumentException($"The network must have {LabeledDataset.ClassCount} outputs, one per file type.", nameof(network));
        Network = network;
    }

    /// <summary>The model trained on the team dataset and shipped inside this assembly.</summary>
    public static FileClassifier BuiltIn => BuiltInModel.Value;

    public Perceptron Network { get; }

    /// <summary>The most probable file type and its probability.</summary>
    public (FileKind Kind, double Confidence) Classify(FileFeatures features)
    {
        double[] p = Network.Forward(FeatureVector.From(features));
        int best = 0;
        for (int i = 1; i < p.Length; i++)
        {
            if (p[i] > p[best])
                best = i;
        }
        return ((FileKind)best, p[best]);
    }

    public void Save(Stream stream) => Network.Save(stream);

    /// <exception cref="InvalidDataException">The stream does not hold a valid model.</exception>
    public static FileClassifier Load(Stream stream)
    {
        Perceptron network = Perceptron.Load(stream);
        try
        {
            return new FileClassifier(network);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"The model does not fit the classifier: {ex.Message}", ex);
        }
    }

    private static FileClassifier LoadBuiltIn()
    {
        using Stream stream = typeof(FileClassifier).Assembly.GetManifestResourceStream(BuiltInResource)
            ?? throw new InvalidOperationException($"The built-in model {BuiltInResource} is missing from the assembly.");
        return Load(stream);
    }
}
