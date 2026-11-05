using System.Text;
using SmartArchiver.Core;

namespace SmartArchiver.Analysis.Tests;

public class FileAnalyzerTests
{
    private static readonly byte[] Text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("plain text line\n", 200)));

    /// <summary>A classifier that always answers <paramref name="kind"/>: zero weights, a large bias on that output.</summary>
    private static FileClassifier Always(FileKind kind)
    {
        var biases = new double[LabeledDataset.ClassCount];
        biases[(int)kind] = 10;
        var network = new Perceptron(
            [FeatureVector.Length, LabeledDataset.ClassCount],
            [new double[FeatureVector.Length * LabeledDataset.ClassCount]],
            [biases]);
        return new FileClassifier(network);
    }

    private static byte[] Noise(int length)
    {
        var bytes = new byte[length];
        new Random(9).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public void Analyze_ReturnsClassifierKindAndItsProbability()
    {
        FileAnalysis result = new FileAnalyzer(Always(FileKind.SourceCode)).Analyze(Text);

        Assert.Equal(FileKind.SourceCode, result.Kind);
        // softmax of 10 against six zeros
        Assert.Equal(Math.Exp(10) / (Math.Exp(10) + 6), result.Confidence, 1e-12);
    }

    [Fact]
    public void Analyze_TextLikeFile_IsCompressed()
    {
        Assert.Equal(CompressionStrategy.Lz77Huffman, new FileAnalyzer(Always(FileKind.Text)).Analyze(Text).Strategy);
    }

    /// <summary>Random bytes drawn from 180 values: entropy log2(180) ≈ 7.49, like compressed data.</summary>
    private static byte[] HighEntropy(int length)
    {
        var random = new Random(11);
        return [.. Enumerable.Range(0, length).Select(_ => (byte)random.Next(180))];
    }

    [Fact]
    public void Analyze_AlreadyCompressedWithHighEntropy_IsStoredWithoutCompression()
    {
        Assert.Equal(CompressionStrategy.NoCompression,
            new FileAnalyzer(Always(FileKind.AlreadyCompressed)).Analyze(HighEntropy(64 * 1024)).Strategy);
    }

    [Fact]
    public void Analyze_ClassifiedAsCompressedButLowEntropy_IsStillCompressed()
    {
        // A wrong "already compressed" on text would cost the whole compression gain,
        // while compressing real compressed data only costs time, so the data has to agree with the classifier.
        FileAnalysis analysis = new FileAnalyzer(Always(FileKind.AlreadyCompressed)).Analyze(Text);

        Assert.Equal(FileKind.AlreadyCompressed, analysis.Kind);
        Assert.Equal(CompressionStrategy.Lz77Huffman, analysis.Strategy);
    }

    [Fact]
    public void Analyze_Noise_IsStoredWithoutCompressionWhateverTheClass()
    {
        // Random bytes have nothing to compress; entropy is a safety net next to the classifier.
        Assert.Equal(CompressionStrategy.NoCompression,
            new FileAnalyzer(Always(FileKind.Artificial)).Analyze(Noise(64 * 1024)).Strategy);
    }

    [Fact]
    public void Analyze_RepeatedPattern_IsCompressedEvenThoughArtificial()
    {
        byte[] pattern = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("ABCD", 5000)));

        Assert.Equal(CompressionStrategy.Lz77Huffman, new FileAnalyzer(Always(FileKind.Artificial)).Analyze(pattern).Strategy);
    }

    [Fact]
    public void Analyze_EmptyFile_IsNotCompressed()
    {
        Assert.Equal(CompressionStrategy.NoCompression, new FileAnalyzer(Always(FileKind.Text)).Analyze([]).Strategy);
    }

    [Fact]
    public void FileClassifier_WrongNetworkShape_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FileClassifier(new Perceptron([5, 7], seed: 1)));
        Assert.Throws<ArgumentException>(() => new FileClassifier(new Perceptron([FeatureVector.Length, 3], seed: 1)));
    }

    [Fact]
    public void FileClassifier_SaveThenLoad_GivesSameAnswers()
    {
        var classifier = new FileClassifier(new Perceptron(TrainingOptions.Default.LayerSizes, seed: 4));
        using var stream = new MemoryStream();

        classifier.Save(stream);
        stream.Position = 0;
        FileClassifier loaded = FileClassifier.Load(stream);

        Assert.Equal(classifier.Classify(FeatureExtractor.Compute(Text)), loaded.Classify(FeatureExtractor.Compute(Text)));
    }
}
