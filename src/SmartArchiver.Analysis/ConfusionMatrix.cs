namespace SmartArchiver.Analysis;

/// <summary>
/// Counts of (actual class, predicted class) pairs and the metrics derived from them.
/// Row = actual class, column = predicted class.
/// </summary>
public sealed class ConfusionMatrix
{
    private readonly int[,] counts;

    public ConfusionMatrix(int classCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(classCount, 1);
        counts = new int[classCount, classCount];
    }

    public int ClassCount => counts.GetLength(0);

    public int this[int actual, int predicted] => counts[actual, predicted];

    public static ConfusionMatrix From(IReadOnlyList<int> actual, IReadOnlyList<int> predicted, int classCount)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(predicted);
        if (actual.Count != predicted.Count)
            throw new ArgumentException("Every actual label needs exactly one prediction.", nameof(predicted));

        var matrix = new ConfusionMatrix(classCount);
        for (int i = 0; i < actual.Count; i++)
            matrix.Add(actual[i], predicted[i]);
        return matrix;
    }

    public void Add(int actual, int predicted)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(actual);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(actual, ClassCount);
        ArgumentOutOfRangeException.ThrowIfNegative(predicted);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(predicted, ClassCount);
        counts[actual, predicted]++;
    }

    /// <summary>Of everything predicted as the class, the share that really is the class.</summary>
    public double Precision(int c)
    {
        int predicted = ColumnSum(c);
        return predicted == 0 ? 0 : (double)counts[c, c] / predicted;
    }

    /// <summary>Of everything that really is the class, the share that was found.</summary>
    public double Recall(int c)
    {
        int actual = RowSum(c);
        return actual == 0 ? 0 : (double)counts[c, c] / actual;
    }

    /// <summary>Harmonic mean of precision and recall; 0 when both are 0.</summary>
    public double F1(int c)
    {
        double p = Precision(c), r = Recall(c);
        return p + r == 0 ? 0 : 2 * p * r / (p + r);
    }

    /// <summary>
    /// Mean F1 over the classes that occur as an actual label or as a prediction.
    /// Classes absent from both are left out, so a sample without some file types is not penalised for them.
    /// </summary>
    public double MacroF1
    {
        get
        {
            double sum = 0;
            int classes = 0;
            for (int c = 0; c < ClassCount; c++)
            {
                if (RowSum(c) == 0 && ColumnSum(c) == 0)
                    continue;
                sum += F1(c);
                classes++;
            }
            return classes == 0 ? 0 : sum / classes;
        }
    }

    public double Accuracy
    {
        get
        {
            int correct = 0, total = 0;
            for (int a = 0; a < ClassCount; a++)
            {
                for (int p = 0; p < ClassCount; p++)
                {
                    total += counts[a, p];
                    if (a == p)
                        correct += counts[a, p];
                }
            }
            return total == 0 ? 0 : (double)correct / total;
        }
    }

    /// <summary>Rows as arrays, for printing and for saving as JSON.</summary>
    public int[][] ToArray()
    {
        var rows = new int[ClassCount][];
        for (int a = 0; a < ClassCount; a++)
        {
            rows[a] = new int[ClassCount];
            for (int p = 0; p < ClassCount; p++)
                rows[a][p] = counts[a, p];
        }
        return rows;
    }

    private int RowSum(int c)
    {
        int sum = 0;
        for (int p = 0; p < ClassCount; p++)
            sum += counts[c, p];
        return sum;
    }

    private int ColumnSum(int c)
    {
        int sum = 0;
        for (int a = 0; a < ClassCount; a++)
            sum += counts[a, c];
        return sum;
    }
}
