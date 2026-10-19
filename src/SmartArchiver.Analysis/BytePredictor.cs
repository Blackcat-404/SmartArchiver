using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SmartArchiver.Analysis;

/// <summary>Hyperparameters of predictor training.</summary>
/// <param name="ContextBytes">How many previous bytes the network sees (k).</param>
/// <param name="MaxPositionsPerSample">Positions taken from the start of each training file, so one file cannot dominate.</param>
public sealed record PredictorOptions(
    int ContextBytes, int HiddenNeurons, int Epochs, double LearningRate, int Seed, int MaxPositionsPerSample)
{
    /// <summary>Values used for the shipped model (decision 28 in docs/decisions.md).</summary>
    public static PredictorOptions Default { get; } =
        new(ContextBytes: 2, HiddenNeurons: 32, Epochs: 3, LearningRate: 0.05, Seed: 1, MaxPositionsPerSample: 2048);
}

/// <summary>
/// Predicts the next byte from the previous k bytes with a perceptron and codes the residual
/// <c>(actual − predicted) mod 256</c>. Where the guess is often right the residuals are mostly zero,
/// so they compress better than the bytes themselves; decoding repeats the same guesses and adds them back.
/// </summary>
/// <remarks>
/// The unpacker must make exactly the same guesses. The hidden layer therefore uses softsign and the guess is the
/// largest output before softmax: only addition, multiplication, division and absolute value, which IEEE 754 rounds
/// identically on every machine. Encoded data starts with <see cref="ModelId"/>, so data packed with other weights
/// is rejected instead of being decoded into garbage.
/// </remarks>
public sealed class BytePredictor
{
    public const int ModelIdSize = 8;

    private const string BuiltInResource = "SmartArchiver.Analysis.predictor.json";
    private const int TrainingChunk = 1024;

    private static readonly Lazy<BytePredictor> BuiltInModel = new(LoadBuiltIn);

    private readonly double[] hiddenWeights;
    private readonly double[] hiddenBiases;
    private readonly double[] outputWeights;
    private readonly double[] outputBiases;
    private readonly int hidden;

    /// <param name="network">Softsign perceptron with 256·k inputs, one hidden layer and 256 outputs.</param>
    public BytePredictor(Perceptron network)
    {
        ArgumentNullException.ThrowIfNull(network);
        if (network.Activation != HiddenActivation.Softsign)
            throw new ArgumentException("The predictor needs softsign, which gives the same result on every machine.", nameof(network));
        if (network.LayerSizes.Count != 3 || network.LayerSizes[0] % 256 != 0 || network.LayerSizes[0] == 0)
            throw new ArgumentException("The predictor needs 256·k inputs and exactly one hidden layer.", nameof(network));
        if (network.LayerSizes[2] != 256)
            throw new ArgumentException("The predictor needs 256 outputs, one per byte value.", nameof(network));

        Network = network;
        ContextBytes = network.LayerSizes[0] / 256;
        hidden = network.LayerSizes[1];
        hiddenWeights = network.Weights[0];
        hiddenBiases = network.Biases[0];
        outputWeights = network.Weights[1];
        outputBiases = network.Biases[1];
        ModelId = ComputeModelId(network);
    }

    /// <summary>The model trained on the team dataset and shipped inside this assembly.</summary>
    public static BytePredictor BuiltIn => BuiltInModel.Value;

    public Perceptron Network { get; }

    public int ContextBytes { get; }

    /// <summary>First 8 bytes of SHA-256 over the shape and every weight: identifies the exact model.</summary>
    public ulong ModelId { get; }

    public static BytePredictor Train(IEnumerable<ReadOnlyMemory<byte>> samples, PredictorOptions options)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ContextBytes, 1);

        // Each position is (sample, index); its input is built only when its chunk is trained, to keep memory small.
        var positions = new List<(ReadOnlyMemory<byte> Sample, int Index)>();
        foreach (ReadOnlyMemory<byte> sample in samples)
        {
            for (int i = 0; i < Math.Min(sample.Length, options.MaxPositionsPerSample); i++)
                positions.Add((sample, i));
        }

        var network = new Perceptron(
            [256 * options.ContextBytes, options.HiddenNeurons, 256], options.Seed, HiddenActivation.Softsign);
        var shuffle = new Random(options.Seed);
        (ReadOnlyMemory<byte> Sample, int Index)[] order = [.. positions];
        for (int epoch = 0; epoch < options.Epochs; epoch++)
        {
            shuffle.Shuffle(order);
            for (int start = 0; start < order.Length; start += TrainingChunk)
            {
                int count = Math.Min(TrainingChunk, order.Length - start);
                var inputs = new double[count][];
                var targets = new int[count];
                for (int i = 0; i < count; i++)
                {
                    (ReadOnlyMemory<byte> sample, int index) = order[start + i];
                    inputs[i] = OneHot(sample.Span[..index], options.ContextBytes);
                    targets[i] = sample.Span[index];
                }
                network.TrainEpoch(inputs, targets, options.LearningRate);
            }
        }
        return new BytePredictor(network);
    }

    /// <summary>The guess for the byte that follows <paramref name="history"/>; only its last k bytes matter.</summary>
    public byte Predict(ReadOnlySpan<byte> history)
    {
        // Hidden layer: the input is one-hot, so each neuron adds its bias and the weights of the k active inputs.
        Span<double> h = hidden <= 256 ? stackalloc double[hidden] : new double[hidden];
        int inputs = 256 * ContextBytes;
        for (int j = 0; j < hidden; j++)
        {
            double sum = hiddenBiases[j];
            for (int back = 0; back < ContextBytes && back < history.Length; back++)
                sum += hiddenWeights[j * inputs + back * 256 + history[history.Length - 1 - back]];
            h[j] = sum / (1 + Math.Abs(sum));
        }

        int best = 0;
        double bestValue = double.NegativeInfinity;
        for (int o = 0; o < 256; o++)
        {
            double sum = outputBiases[o];
            for (int j = 0; j < hidden; j++)
                sum += outputWeights[o * hidden + j] * h[j];
            if (sum > bestValue)
            {
                bestValue = sum;
                best = o;
            }
        }
        return (byte)best;
    }

    /// <summary>Model id (8 bytes, big-endian), then one residual per byte.</summary>
    public byte[] Encode(ReadOnlySpan<byte> data)
    {
        var encoded = new byte[ModelIdSize + data.Length];
        BinaryPrimitives.WriteUInt64BigEndian(encoded, ModelId);
        for (int i = 0; i < data.Length; i++)
            encoded[ModelIdSize + i] = (byte)(data[i] - Predict(data[..i]));
        return encoded;
    }

    /// <exception cref="InvalidDataException">The data is too short or was encoded with another model.</exception>
    public byte[] Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < ModelIdSize)
            throw new InvalidDataException("Predicted data is shorter than the model id.");
        ulong id = BinaryPrimitives.ReadUInt64BigEndian(encoded);
        if (id != ModelId)
            throw new InvalidDataException($"Data was encoded with predictor model {id:X16}, this one is {ModelId:X16}.");

        ReadOnlySpan<byte> residuals = encoded[ModelIdSize..];
        var data = new byte[residuals.Length];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(residuals[i] + Predict(data.AsSpan(0, i)));
        return data;
    }

    public void Save(Stream stream) => Network.Save(stream);

    /// <exception cref="InvalidDataException">The stream does not hold a valid predictor model.</exception>
    public static BytePredictor Load(Stream stream)
    {
        Perceptron network = Perceptron.Load(stream);
        try
        {
            return new BytePredictor(network);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"The model does not fit the predictor: {ex.Message}", ex);
        }
    }

    private static double[] OneHot(ReadOnlySpan<byte> history, int contextBytes)
    {
        var input = new double[256 * contextBytes];
        for (int back = 0; back < contextBytes && back < history.Length; back++)
            input[back * 256 + history[history.Length - 1 - back]] = 1;
        return input;
    }

    private static ulong ComputeModelId(Perceptron network)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[8];
        foreach (int size in network.LayerSizes)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer, size);
            hash.AppendData(buffer[..4]);
        }
        for (int layer = 0; layer < network.Weights.Length; layer++)
        {
            foreach (double value in network.Weights[layer].Concat(network.Biases[layer]))
            {
                BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
                hash.AppendData(buffer);
            }
        }
        return BinaryPrimitives.ReadUInt64BigEndian(hash.GetHashAndReset());
    }

    private static BytePredictor LoadBuiltIn()
    {
        using Stream stream = typeof(BytePredictor).Assembly.GetManifestResourceStream(BuiltInResource)
            ?? throw new InvalidOperationException($"The built-in model {BuiltInResource} is missing from the assembly.");
        return Load(stream);
    }
}
