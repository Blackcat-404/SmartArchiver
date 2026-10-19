using SmartArchiver.Core;

namespace SmartArchiver.Analysis;

/// <summary>
/// Decides the file type and the compression strategy for a file before it is archived.
/// The type comes from the classifier; the strategy follows from the type, with entropy as a safety net.
/// </summary>
public sealed class FileAnalyzer(FileClassifier classifier) : IFileAnalyzer
{
    /// <summary>
    /// Bits per byte above which data is treated as noise. In the team dataset deflate data and PNG give 7.99,
    /// text, code and structured data 4.0–4.7, while the raw gradient BMP reaches 7.75 and still compresses well;
    /// so the threshold sits just below compressed data, and the classifier, not entropy, makes the main decision.
    /// </summary>
    public const double NoiseEntropy = 7.9;

    /// <summary>
    /// "Already compressed" from the classifier is trusted only if the data agrees: compressed data has high entropy.
    /// A wrong answer here would leave a text file uncompressed, which costs far more than compressing a zip again.
    /// </summary>
    public const double CompressedEntropy = 7.0;

    /// <summary>Uses the model shipped with the assembly.</summary>
    public FileAnalyzer()
        : this(FileClassifier.BuiltIn)
    {
    }

    public FileAnalysis Analyze(ReadOnlySpan<byte> data)
    {
        FileFeatures features = FeatureExtractor.Compute(data);
        (FileKind kind, double confidence) = classifier.Classify(features);
        return new FileAnalysis(kind, confidence, ChooseStrategy(kind, features, data.Length));
    }

    /// <summary>
    /// Noise and already compressed data are stored as they are: LZ77 finds no repeats there and the Huffman
    /// tables only add bytes. Everything else, including artificial data with repeats, is compressed.
    /// </summary>
    private static CompressionStrategy ChooseStrategy(FileKind kind, FileFeatures features, int length)
    {
        if (length == 0 || features.Entropy >= NoiseEntropy)
            return CompressionStrategy.NoCompression;
        if (kind == FileKind.AlreadyCompressed && features.Entropy >= CompressedEntropy)
            return CompressionStrategy.NoCompression;
        return CompressionStrategy.Lz77Huffman;
    }
}
