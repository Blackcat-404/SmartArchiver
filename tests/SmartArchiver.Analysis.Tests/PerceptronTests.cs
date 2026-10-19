namespace SmartArchiver.Analysis.Tests;

public class PerceptronTests
{
    private const double Tolerance = 1e-12;

    private static readonly double[][] XorInputs = [[0, 0], [0, 1], [1, 0], [1, 1]];
    private static readonly int[] XorTargets = [0, 1, 1, 0];

    /// <summary>Two inputs straight into two outputs: z = W x + b, then softmax.</summary>
    private static Perceptron SingleLayer() => new(
        [2, 2],
        [[1, 2, 3, 4]],     // row-major: output 0 = (1, 2), output 1 = (3, 4)
        [[0.5, -0.5]]);

    [Fact]
    public void Constructor_SameSeed_GivesSameWeights()
    {
        var a = new Perceptron([3, 5, 2], seed: 11);
        var b = new Perceptron([3, 5, 2], seed: 11);

        Assert.Equal(a.Weights, b.Weights);
        Assert.Equal(a.Biases, b.Biases);
    }

    [Theory]
    [InlineData(new[] { 3 })]
    [InlineData(new[] { 3, 0, 2 })]
    [InlineData(new[] { 3, 1 })]
    public void Constructor_InvalidLayerSizes_Throws(int[] sizes)
    {
        Assert.Throws<ArgumentException>(() => new Perceptron(sizes, seed: 1));
    }

    [Fact]
    public void Forward_KnownWeightsWithoutHiddenLayer_IsSoftmaxOfWeightedSum()
    {
        // z = (1 + 2 + 0.5, 3 + 4 - 0.5) = (3.5, 6.5); softmax gives p0 = 1 / (1 + e^3).
        double[] p = SingleLayer().Forward([1, 1]);

        Assert.Equal(1 / (1 + Math.Exp(3)), p[0], Tolerance);
        Assert.Equal(1 - 1 / (1 + Math.Exp(3)), p[1], Tolerance);
    }

    [Fact]
    public void Forward_KnownWeightsWithHiddenLayer_AppliesTanhThenSoftmax()
    {
        var net = new Perceptron(
            [2, 2, 2],
            [[1, -1, 0.5, 0.5], [1, 0, 0, 1]],
            [[0, 0], [0, 0]]);

        // Hidden: tanh(1*1 + -1*0) = tanh 1, tanh(0.5*1 + 0.5*0) = tanh 0.5; the output layer copies them.
        double h0 = Math.Tanh(1), h1 = Math.Tanh(0.5);
        double expected0 = Math.Exp(h0) / (Math.Exp(h0) + Math.Exp(h1));

        double[] p = net.Forward([1, 0]);

        Assert.Equal(expected0, p[0], Tolerance);
        Assert.Equal(1 - expected0, p[1], Tolerance);
    }

    [Fact]
    public void Forward_WrongInputLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => SingleLayer().Forward([1, 2, 3]));
    }

    [Fact]
    public void Loss_IsMinusLogOfTargetProbability()
    {
        double p1 = 1 - 1 / (1 + Math.Exp(3));

        Assert.Equal(-Math.Log(p1), SingleLayer().Loss([1, 1], target: 1), Tolerance);
    }

    [Fact]
    public void Loss_TargetOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SingleLayer().Loss([1, 1], target: 2));
    }

    [Fact]
    public void Gradients_MatchNumericalDerivativeOfLoss()
    {
        var net = new Perceptron([3, 4, 3], seed: 5);
        double[] input = [0.5, -1, 2];
        const int target = 2;
        const double h = 1e-5;

        (double[][] weightGrads, double[][] biasGrads) = net.Gradients(input, target);

        for (int layer = 0; layer < net.Weights.Length; layer++)
        {
            AssertNumericalMatch(net, net.Weights[layer], weightGrads[layer], input, target, h);
            AssertNumericalMatch(net, net.Biases[layer], biasGrads[layer], input, target, h);
        }
    }

    private static void AssertNumericalMatch(
        Perceptron net, double[] parameters, double[] analytic, double[] input, int target, double h)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            double original = parameters[i];
            parameters[i] = original + h;
            double plus = net.Loss(input, target);
            parameters[i] = original - h;
            double minus = net.Loss(input, target);
            parameters[i] = original;

            double numerical = (plus - minus) / (2 * h);
            Assert.True(
                Math.Abs(numerical - analytic[i]) <= 1e-6 * Math.Max(1, Math.Abs(numerical) + Math.Abs(analytic[i])),
                $"parameter {i}: numerical {numerical}, backpropagation {analytic[i]}");
        }
    }

    // With learning rate 0.1 and 4 hidden neurons all of seeds 1..30 learn XOR in 2000 epochs;
    // with 0.5 about half of them get stuck, so 0.5 is too large for per-sample updates.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TrainEpoch_Xor_LossFallsAndAllFourCasesAreLearned(int seed)
    {
        var net = new Perceptron([2, 4, 2], seed);

        double firstLoss = net.TrainEpoch(XorInputs, XorTargets, learningRate: 0.1);
        double lastLoss = firstLoss;
        for (int epoch = 1; epoch < 2000; epoch++)
            lastLoss = net.TrainEpoch(XorInputs, XorTargets, learningRate: 0.1);

        Assert.True(lastLoss < firstLoss / 10, $"loss went from {firstLoss} to {lastLoss}");
        for (int i = 0; i < XorInputs.Length; i++)
            Assert.Equal(XorTargets[i], net.Classify(XorInputs[i]));
    }

    [Fact]
    public void TrainEpoch_InputsAndTargetsOfDifferentLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => SingleLayer().TrainEpoch(XorInputs, [0, 1], learningRate: 0.1));
    }

    [Fact]
    public void SaveThenLoad_RestoresTheSameNetwork()
    {
        var net = new Perceptron([4, 6, 3, 2], seed: 3);
        using var stream = new MemoryStream();

        net.Save(stream);
        stream.Position = 0;
        Perceptron loaded = Perceptron.Load(stream);

        Assert.Equal(net.LayerSizes, loaded.LayerSizes);
        Assert.Equal(net.Weights, loaded.Weights);
        Assert.Equal(net.Biases, loaded.Biases);
        Assert.Equal(net.Forward([0.1, -0.2, 0.3, 0.9]), loaded.Forward([0.1, -0.2, 0.3, 0.9]));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"layerSizes":[2,2],"weights":[[1,2,3]],"biases":[[0,0]]}""")]
    [InlineData("""{"layerSizes":[2,2],"weights":[[1,2,3,4]],"biases":[[0]]}""")]
    [InlineData("""{"layerSizes":[2,2,2],"weights":[[1,2,3,4]],"biases":[[0,0]]}""")]
    public void Load_MalformedOrInconsistentFile_ThrowsInvalidData(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Throws<InvalidDataException>(() => Perceptron.Load(stream));
    }
}
