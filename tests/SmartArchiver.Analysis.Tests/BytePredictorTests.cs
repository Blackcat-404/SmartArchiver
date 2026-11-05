using System.Text;

namespace SmartArchiver.Analysis.Tests;

public class BytePredictorTests
{
    private static readonly PredictorOptions Small = new(ContextBytes: 2, HiddenNeurons: 16, Epochs: 4, LearningRate: 0.05, Seed: 1, MaxPositionsPerSample: 4096);

    private static readonly byte[] EnglishTraining = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(
        "the quick brown fox jumps over the lazy dog and then the fox sleeps in the shade of the old tree. ", 30)));

    private static readonly byte[] EnglishOther = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(
        "the dog sleeps and the fox jumps over the tree in the shade, then the quick fox runs. ", 20)));

    private static readonly Lazy<BytePredictor> TrainedOnText = new(() => BytePredictor.Train([EnglishTraining], Small));

    private static byte[] Noise(int length, int seed = 5)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static double Entropy(byte[] data) => FeatureExtractor.Compute(data).Entropy;

    public static TheoryData<byte[]> Inputs => new()
    {
        Array.Empty<byte>(),
        new byte[] { 42 },
        EnglishOther,
        Noise(3000),
        new byte[2000],
    };

    [Theory]
    [MemberData(nameof(Inputs))]
    public void EncodeThenDecode_RestoresExactly(byte[] data)
    {
        BytePredictor predictor = TrainedOnText.Value;

        Assert.Equal(data, predictor.Decode(predictor.Encode(data)));
    }

    [Fact]
    public void Decode_NewInstanceLoadedOnlyFromSavedWeights_RestoresFile()
    {
        // D1: the unpacker has nothing but the weights and the residuals.
        BytePredictor packer = TrainedOnText.Value;
        byte[] encoded = packer.Encode(EnglishOther);
        using var weights = new MemoryStream();
        packer.Save(weights);

        weights.Position = 0;
        BytePredictor unpacker = BytePredictor.Load(weights);

        Assert.Equal(EnglishOther, unpacker.Decode(encoded));
        Assert.Equal(packer.ModelId, unpacker.ModelId);
    }

    [Fact]
    public void Decode_WithAnotherModel_IsRejected()
    {
        // Other weights would predict other bytes and silently produce garbage, so the model id is checked first.
        BytePredictor other = BytePredictor.Train([EnglishTraining], Small with { Seed = 2 });
        byte[] encoded = TrainedOnText.Value.Encode(EnglishOther);

        Assert.NotEqual(TrainedOnText.Value.ModelId, other.ModelId);
        Assert.Throws<InvalidDataException>(() => other.Decode(encoded));
    }

    [Fact]
    public void Decode_ShorterThanModelId_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => TrainedOnText.Value.Decode([1, 2, 3]));
    }

    [Fact]
    public void Encode_StartsWithModelIdAndHasOneResidualPerByte()
    {
        byte[] encoded = TrainedOnText.Value.Encode(EnglishOther);

        Assert.Equal(BytePredictor.ModelIdSize + EnglishOther.Length, encoded.Length);
    }

    [Fact]
    public void Residuals_OfAPatternTheModelLearned_AreMostlyZero()
    {
        byte[] pattern = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("abcd", 500)));
        BytePredictor predictor = BytePredictor.Train([pattern], Small);

        byte[] residuals = predictor.Encode(pattern)[BytePredictor.ModelIdSize..];

        Assert.True(residuals.Count(r => r == 0) >= 0.95 * residuals.Length);
    }

    [Fact]
    public void Residuals_OfSimilarText_HaveLowerEntropy()
    {
        byte[] residuals = TrainedOnText.Value.Encode(EnglishOther)[BytePredictor.ModelIdSize..];

        Assert.True(Entropy(residuals) < Entropy(EnglishOther), $"{Entropy(residuals)} vs {Entropy(EnglishOther)}");
    }

    [Fact]
    public void Residuals_OfNoise_DoNotLowerEntropy()
    {
        // Nothing to predict in random bytes: the residuals are as random as the data.
        byte[] noise = Noise(64 * 1024);
        byte[] residuals = TrainedOnText.Value.Encode(noise)[BytePredictor.ModelIdSize..];

        Assert.True(Entropy(residuals) > Entropy(noise) - 0.01);
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Encode_GivesTheResidualOfPredictingEachByteOnItsOwn(byte[] data)
    {
        // Encode remembers the guess for each context it has seen; the guesses must stay exactly those of Predict.
        BytePredictor predictor = TrainedOnText.Value;

        byte[] residuals = predictor.Encode(data)[BytePredictor.ModelIdSize..];

        Assert.Equal(
            Enumerable.Range(0, data.Length).Select(i => (byte)(data[i] - predictor.Predict(data.AsSpan(0, i)))),
            residuals);
    }

    [Fact]
    public void Encode_NoiseWithAlmostEveryContext_GivesTheResidualsOfPredict()
    {
        BytePredictor predictor = TrainedOnText.Value;
        byte[] data = Noise(200_000, seed: 9);

        byte[] residuals = predictor.Encode(data)[BytePredictor.ModelIdSize..];

        Assert.Equal(
            Enumerable.Range(0, data.Length).Select(i => (byte)(data[i] - predictor.Predict(data.AsSpan(0, i)))),
            residuals);
        Assert.Equal(data, predictor.Decode(predictor.Encode(data)));
    }

    [Fact]
    public void Predict_LooksOnlyAtTheLastContextBytes()
    {
        BytePredictor predictor = TrainedOnText.Value;

        Assert.Equal(predictor.Predict("xyzth"u8), predictor.Predict("th"u8));
    }

    [Fact]
    public void Constructor_NetworkOfWrongShapeOrActivation_Throws()
    {
        Assert.Throws<ArgumentException>(() => new BytePredictor(new Perceptron([512, 8, 256], seed: 1)));
        Assert.Throws<ArgumentException>(() => new BytePredictor(new Perceptron([500, 8, 256], seed: 1, HiddenActivation.Softsign)));
        Assert.Throws<ArgumentException>(() => new BytePredictor(new Perceptron([512, 8, 7], seed: 1, HiddenActivation.Softsign)));
    }
}
