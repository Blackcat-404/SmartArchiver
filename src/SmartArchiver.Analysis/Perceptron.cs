using System.Text.Json;

namespace SmartArchiver.Analysis;

/// <summary>
/// Multilayer perceptron for classification, written from scratch.
/// Hidden layers use tanh, the output layer uses softmax, the loss is cross-entropy,
/// and training is plain stochastic gradient descent with backpropagation.
/// </summary>
public sealed class Perceptron
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly int[] layerSizes;

    /// <summary>Creates a network with random weights (Glorot uniform) and zero biases.</summary>
    /// <param name="layerSizes">Neurons per layer: inputs first, classes last, for example [2, 4, 2].</param>
    /// <param name="seed">Seed of the weight initialisation, so that a run can be repeated.</param>
    public Perceptron(IReadOnlyList<int> layerSizes, int seed)
    {
        this.layerSizes = ValidateSizes(layerSizes);
        var random = new Random(seed);
        Weights = new double[this.layerSizes.Length - 1][];
        Biases = new double[this.layerSizes.Length - 1][];
        for (int layer = 0; layer < Weights.Length; layer++)
        {
            int inputs = this.layerSizes[layer], outputs = this.layerSizes[layer + 1];
            double limit = Math.Sqrt(6.0 / (inputs + outputs));
            Weights[layer] = new double[inputs * outputs];
            for (int i = 0; i < Weights[layer].Length; i++)
                Weights[layer][i] = (random.NextDouble() * 2 - 1) * limit;
            Biases[layer] = new double[outputs];
        }
    }

    /// <summary>Creates a network with the given weights; used by tests and by <see cref="Load"/>.</summary>
    internal Perceptron(IReadOnlyList<int> layerSizes, double[][] weights, double[][] biases)
    {
        this.layerSizes = ValidateSizes(layerSizes);
        int layers = this.layerSizes.Length - 1;
        if (weights.Length != layers || biases.Length != layers)
            throw new ArgumentException("Weights and biases must have one entry per layer.");
        for (int layer = 0; layer < layers; layer++)
        {
            if (weights[layer].Length != this.layerSizes[layer] * this.layerSizes[layer + 1])
                throw new ArgumentException($"Layer {layer} needs {this.layerSizes[layer] * this.layerSizes[layer + 1]} weights.");
            if (biases[layer].Length != this.layerSizes[layer + 1])
                throw new ArgumentException($"Layer {layer} needs {this.layerSizes[layer + 1]} biases.");
        }
        Weights = weights;
        Biases = biases;
    }

    public IReadOnlyList<int> LayerSizes => layerSizes;

    /// <summary>Weights of each layer, row-major: Weights[layer][output * inputs + input].</summary>
    internal double[][] Weights { get; }

    internal double[][] Biases { get; }

    /// <summary>Probability of each class for the input.</summary>
    public double[] Forward(ReadOnlySpan<double> input) => Activations(input)[^1];

    /// <summary>The most probable class.</summary>
    public int Classify(ReadOnlySpan<double> input)
    {
        double[] p = Forward(input);
        int best = 0;
        for (int i = 1; i < p.Length; i++)
        {
            if (p[i] > p[best])
                best = i;
        }
        return best;
    }

    /// <summary>Cross-entropy: minus the natural logarithm of the probability given to the right class.</summary>
    public double Loss(ReadOnlySpan<double> input, int target)
    {
        ValidateTarget(target);
        return CrossEntropy(Forward(input), target);
    }

    /// <summary>One pass over all samples, updating the weights after each sample.</summary>
    /// <returns>Average loss over the samples, each measured just before its update.</returns>
    public double TrainEpoch(IReadOnlyList<double[]> inputs, IReadOnlyList<int> targets, double learningRate)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(targets);
        if (inputs.Count != targets.Count)
            throw new ArgumentException("Every input needs exactly one target.", nameof(targets));
        if (inputs.Count == 0)
            return 0;

        double totalLoss = 0;
        for (int sample = 0; sample < inputs.Count; sample++)
        {
            ValidateTarget(targets[sample]);
            double[][] activations = Activations(inputs[sample]);
            totalLoss += CrossEntropy(activations[^1], targets[sample]);
            (double[][] weightGrads, double[][] biasGrads) = Backpropagate(activations, targets[sample]);
            for (int layer = 0; layer < Weights.Length; layer++)
            {
                for (int i = 0; i < Weights[layer].Length; i++)
                    Weights[layer][i] -= learningRate * weightGrads[layer][i];
                for (int i = 0; i < Biases[layer].Length; i++)
                    Biases[layer][i] -= learningRate * biasGrads[layer][i];
            }
        }
        return totalLoss / inputs.Count;
    }

    /// <summary>Derivatives of <see cref="Loss"/> with respect to every weight and bias.</summary>
    internal (double[][] WeightGrads, double[][] BiasGrads) Gradients(ReadOnlySpan<double> input, int target)
    {
        ValidateTarget(target);
        return Backpropagate(Activations(input), target);
    }

    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        JsonSerializer.Serialize(stream, new PerceptronFile(layerSizes, Weights, Biases), JsonOptions);
    }

    /// <exception cref="InvalidDataException">The stream does not hold a valid saved network.</exception>
    public static Perceptron Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PerceptronFile? file;
        try
        {
            file = JsonSerializer.Deserialize<PerceptronFile>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The weights file is not valid JSON.", ex);
        }
        if (file?.LayerSizes is null || file.Weights is null || file.Biases is null)
            throw new InvalidDataException("The weights file must contain layerSizes, weights and biases.");
        try
        {
            return new Perceptron(file.LayerSizes, file.Weights, file.Biases);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"The weights file is inconsistent: {ex.Message}", ex);
        }
    }

    /// <summary>Outputs of every layer: [0] is the input, the last one is the softmax.</summary>
    private double[][] Activations(ReadOnlySpan<double> input)
    {
        if (input.Length != layerSizes[0])
            throw new ArgumentException($"The network expects {layerSizes[0]} inputs, got {input.Length}.", nameof(input));

        var activations = new double[layerSizes.Length][];
        activations[0] = input.ToArray();
        for (int layer = 0; layer < Weights.Length; layer++)
        {
            double[] previous = activations[layer];
            int outputs = layerSizes[layer + 1];
            var z = new double[outputs];
            for (int o = 0; o < outputs; o++)
            {
                double sum = Biases[layer][o];
                for (int i = 0; i < previous.Length; i++)
                    sum += Weights[layer][o * previous.Length + i] * previous[i];
                z[o] = sum;
            }
            bool isOutput = layer == Weights.Length - 1;
            activations[layer + 1] = isOutput ? Softmax(z) : Array.ConvertAll(z, Math.Tanh);
        }
        return activations;
    }

    /// <summary>
    /// For softmax with cross-entropy the error at the output is p - onehot(target).
    /// Going back, each hidden error is the weighted sum of the next layer's errors times tanh' = 1 - a^2.
    /// </summary>
    private (double[][] WeightGrads, double[][] BiasGrads) Backpropagate(double[][] activations, int target)
    {
        var weightGrads = new double[Weights.Length][];
        var biasGrads = new double[Weights.Length][];

        double[] delta = (double[])activations[^1].Clone();
        delta[target] -= 1;

        for (int layer = Weights.Length - 1; layer >= 0; layer--)
        {
            double[] input = activations[layer];
            weightGrads[layer] = new double[Weights[layer].Length];
            for (int o = 0; o < delta.Length; o++)
            {
                for (int i = 0; i < input.Length; i++)
                    weightGrads[layer][o * input.Length + i] = delta[o] * input[i];
            }
            biasGrads[layer] = (double[])delta.Clone();

            if (layer == 0)
                break;
            var previousDelta = new double[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                double sum = 0;
                for (int o = 0; o < delta.Length; o++)
                    sum += Weights[layer][o * input.Length + i] * delta[o];
                previousDelta[i] = sum * (1 - input[i] * input[i]);
            }
            delta = previousDelta;
        }
        return (weightGrads, biasGrads);
    }

    private static double[] Softmax(double[] z)
    {
        double max = z.Max();
        double[] exp = Array.ConvertAll(z, v => Math.Exp(v - max));
        double sum = exp.Sum();
        return Array.ConvertAll(exp, v => v / sum);
    }

    private static double CrossEntropy(double[] probabilities, int target) =>
        -Math.Log(Math.Max(probabilities[target], double.Epsilon));

    private void ValidateTarget(int target)
    {
        if (target < 0 || target >= layerSizes[^1])
            throw new ArgumentOutOfRangeException(nameof(target), $"Class must be from 0 to {layerSizes[^1] - 1}.");
    }

    private static int[] ValidateSizes(IReadOnlyList<int> layerSizes)
    {
        ArgumentNullException.ThrowIfNull(layerSizes);
        if (layerSizes.Count < 2)
            throw new ArgumentException("A network needs at least an input and an output layer.", nameof(layerSizes));
        if (layerSizes.Any(size => size < 1))
            throw new ArgumentException("Every layer needs at least one neuron.", nameof(layerSizes));
        if (layerSizes[^1] < 2)
            throw new ArgumentException("The output layer needs at least two classes.", nameof(layerSizes));
        return [.. layerSizes];
    }

    private sealed record PerceptronFile(int[]? LayerSizes, double[][]? Weights, double[][]? Biases);
}
