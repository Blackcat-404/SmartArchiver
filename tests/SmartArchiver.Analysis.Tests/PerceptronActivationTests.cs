namespace SmartArchiver.Analysis.Tests;

public class PerceptronActivationTests
{
    [Fact]
    public void Default_HiddenActivationIsTanh()
    {
        Assert.Equal(HiddenActivation.Tanh, new Perceptron([2, 3, 2], seed: 1).Activation);
    }

    [Fact]
    public void Softsign_ForwardOnKnownWeights_IsXOverOnePlusAbsX()
    {
        var net = new Perceptron([1, 1, 2], [[2], [1, -1]], [[0], [0, 0]], HiddenActivation.Softsign);

        // Hidden: 2*3 = 6 -> 6 / (1 + 6). Output logits: (h, -h).
        double h = 6.0 / 7;

        Assert.Equal([h, -h], net.Logits([3]));
    }

    [Fact]
    public void Logits_AreTheValuesBeforeSoftmax()
    {
        var net = new Perceptron([3, 4, 3], seed: 2);
        double[] z = net.Logits([0.1, -0.2, 0.3]);
        double[] p = net.Forward([0.1, -0.2, 0.3]);

        double sum = z.Sum(Math.Exp);
        for (int i = 0; i < z.Length; i++)
            Assert.Equal(Math.Exp(z[i]) / sum, p[i], 1e-12);
    }

    [Fact]
    public void Softsign_GradientsMatchNumericalDerivative()
    {
        var net = new Perceptron([3, 4, 3], seed: 5, HiddenActivation.Softsign);
        double[] input = [0.5, -1, 2];
        const int target = 1;
        const double h = 1e-5;

        (double[][] weightGrads, double[][] biasGrads) = net.Gradients(input, target);

        for (int layer = 0; layer < net.Weights.Length; layer++)
        {
            foreach ((double[] parameters, double[] analytic) in new[] { (net.Weights[layer], weightGrads[layer]), (net.Biases[layer], biasGrads[layer]) })
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
                    Assert.True(Math.Abs(numerical - analytic[i]) <= 1e-6 * Math.Max(1, Math.Abs(numerical) + Math.Abs(analytic[i])),
                        $"layer {layer}, parameter {i}: numerical {numerical}, backpropagation {analytic[i]}");
                }
            }
        }
    }

    [Fact]
    public void SaveThenLoad_KeepsTheActivation()
    {
        var net = new Perceptron([2, 3, 2], seed: 3, HiddenActivation.Softsign);
        using var stream = new MemoryStream();

        net.Save(stream);
        stream.Position = 0;
        Perceptron loaded = Perceptron.Load(stream);

        Assert.Equal(HiddenActivation.Softsign, loaded.Activation);
        Assert.Equal(net.Logits([0.4, 0.6]), loaded.Logits([0.4, 0.6]));
    }

    [Fact]
    public void Load_FileWithoutActivation_UsesTanh()
    {
        // Models saved before the activation was configurable (the shipped classifier) must still load.
        const string json = """{"layerSizes":[1,1,2],"weights":[[1],[1,1]],"biases":[[0],[0,0]]}""";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(HiddenActivation.Tanh, Perceptron.Load(stream).Activation);
    }
}
