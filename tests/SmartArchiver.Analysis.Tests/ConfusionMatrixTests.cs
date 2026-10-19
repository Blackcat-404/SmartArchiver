namespace SmartArchiver.Analysis.Tests;

public class ConfusionMatrixTests
{
    private const double Tolerance = 1e-12;

    // Worked by hand:
    //   class 0: TP 2, FP 0, FN 1 -> P 1,   R 2/3, F1 0.8
    //   class 1: TP 1, FP 1, FN 1 -> P 1/2, R 1/2, F1 0.5
    //   class 2: TP 1, FP 1, FN 0 -> P 1/2, R 1,   F1 2/3
    //   macro-F1 = (0.8 + 0.5 + 2/3) / 3
    private static readonly int[] Actual = [0, 0, 0, 1, 1, 2];
    private static readonly int[] Predicted = [0, 0, 1, 1, 2, 2];

    [Fact]
    public void From_CountsEveryActualPredictedPair()
    {
        ConfusionMatrix m = ConfusionMatrix.From(Actual, Predicted, classCount: 3);

        Assert.Equal([[2, 1, 0], [0, 1, 1], [0, 0, 1]], m.ToArray());
    }

    [Fact]
    public void PrecisionRecallF1_MatchHandComputedValues()
    {
        ConfusionMatrix m = ConfusionMatrix.From(Actual, Predicted, classCount: 3);

        Assert.Equal(1, m.Precision(0), Tolerance);
        Assert.Equal(2.0 / 3, m.Recall(0), Tolerance);
        Assert.Equal(0.8, m.F1(0), Tolerance);
        Assert.Equal(0.5, m.F1(1), Tolerance);
        Assert.Equal(2.0 / 3, m.F1(2), Tolerance);
    }

    [Fact]
    public void MacroF1_IsMeanOfPerClassF1()
    {
        ConfusionMatrix m = ConfusionMatrix.From(Actual, Predicted, classCount: 3);

        Assert.Equal((0.8 + 0.5 + 2.0 / 3) / 3, m.MacroF1, Tolerance);
        Assert.Equal(4.0 / 6, m.Accuracy, Tolerance);
    }

    [Fact]
    public void MacroF1_IgnoresClassesThatNeverAppear()
    {
        // Seven possible file types, but this sample only has three of them.
        ConfusionMatrix m = ConfusionMatrix.From(Actual, Predicted, classCount: 7);

        Assert.Equal((0.8 + 0.5 + 2.0 / 3) / 3, m.MacroF1, Tolerance);
    }

    [Fact]
    public void MacroF1_ClassNeverPredicted_CountsAsZero()
    {
        // Class 1 exists but is always predicted as 0: its F1 is 0 and pulls the average down,
        // which is exactly why macro-F1 is used instead of accuracy.
        ConfusionMatrix m = ConfusionMatrix.From([0, 0, 0, 1], [0, 0, 0, 0], classCount: 2);

        Assert.Equal(0, m.F1(1), Tolerance);
        Assert.Equal((6.0 / 7 + 0) / 2, m.MacroF1, Tolerance);
        Assert.Equal(0.75, m.Accuracy, Tolerance);
    }

    [Fact]
    public void MacroF1_PerfectPredictions_IsOne()
    {
        Assert.Equal(1, ConfusionMatrix.From([0, 1, 2, 2], [0, 1, 2, 2], classCount: 3).MacroF1, Tolerance);
    }

    [Fact]
    public void From_LabelOutsideClasses_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfusionMatrix.From([0, 3], [0, 1], classCount: 3));
    }

    [Fact]
    public void From_DifferentLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() => ConfusionMatrix.From([0, 1], [0], classCount: 2));
    }
}
