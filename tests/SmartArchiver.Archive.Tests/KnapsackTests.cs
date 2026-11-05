namespace SmartArchiver.Archive.Tests;

public class KnapsackTests
{
    private static long BruteForce(long[] weights, long[] values, long capacity)
    {
        long best = 0;
        for (int mask = 0; mask < 1 << weights.Length; mask++)
        {
            long weight = 0;
            long value = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    weight += weights[i];
                    value += values[i];
                }
            }
            if (weight <= capacity && value > best)
                best = value;
        }
        return best;
    }

    private static long Total(bool[] chosen, long[] numbers)
    {
        long sum = 0;
        for (int i = 0; i < chosen.Length; i++)
        {
            if (chosen[i])
                sum += numbers[i];
        }
        return sum;
    }

    private static (long[] Weights, long[] Values, long Capacity) RandomInstance(Random rng, int maxItems)
    {
        int n = rng.Next(0, maxItems + 1);
        var weights = new long[n];
        var values = new long[n];
        for (int i = 0; i < n; i++)
        {
            weights[i] = rng.Next(1, 31);
            values[i] = rng.Next(0, 21);
        }
        return (weights, values, rng.Next(0, 81));
    }

    [Fact]
    public void Dp_MatchesBruteForce_OnRandomInstances()
    {
        var rng = new Random(2026);
        for (int round = 0; round < 300; round++)
        {
            (long[] weights, long[] values, long capacity) = RandomInstance(rng, 12);

            bool[] chosen = Knapsack.SolveDp(weights, values, capacity);

            Assert.True(Total(chosen, weights) <= capacity, $"раунд {round}: вага перевищує місткість");
            Assert.Equal(BruteForce(weights, values, capacity), Total(chosen, values));
        }
    }

    [Fact]
    public void Dp_IsNeverWorseThanGreedy_AndBothFit()
    {
        var rng = new Random(7);
        for (int round = 0; round < 300; round++)
        {
            (long[] weights, long[] values, long capacity) = RandomInstance(rng, 25);

            bool[] dp = Knapsack.SolveDp(weights, values, capacity);
            bool[] greedy = Knapsack.SolveGreedy(weights, values, capacity);

            Assert.True(Total(greedy, weights) <= capacity);
            Assert.True(Total(dp, weights) <= capacity);
            Assert.True(Total(dp, values) >= Total(greedy, values), $"раунд {round}: ДП гірший за жадібний");
        }
    }

    [Fact]
    public void Greedy_ClassicCounterexample_DpWins()
    {
        long[] weights = [60, 50, 50];
        long[] values = [60, 45, 45];

        bool[] greedy = Knapsack.SolveGreedy(weights, values, 100);
        bool[] dp = Knapsack.SolveDp(weights, values, 100);

        // Жадібний бере найщільніший блок (60) і далі нічого не влазить; ДП бере два по 50.
        Assert.Equal(60, Total(greedy, values));
        Assert.Equal(90, Total(dp, values));
    }

    [Fact]
    public void Greedy_IsDeterministic_OnEqualDensity()
    {
        long[] weights = [10, 10, 10, 10];
        long[] values = [5, 5, 5, 5];

        bool[] first = Knapsack.SolveGreedy(weights, values, 25);
        bool[] second = Knapsack.SolveGreedy(weights, values, 25);

        Assert.Equal(first, second);
        Assert.Equal(2, first.Count(x => x));
        Assert.True(first[0] && first[1]);   // рівні щільність і вага: за порядком у списку
    }

    [Fact]
    public void ZeroCapacity_ChoosesNothing()
    {
        long[] weights = [1, 2];
        long[] values = [5, 5];

        Assert.DoesNotContain(true, Knapsack.SolveDp(weights, values, 0));
        Assert.DoesNotContain(true, Knapsack.SolveGreedy(weights, values, 0));
    }

    [Fact]
    public void NoItems_ChoosesNothing()
    {
        Assert.Empty(Knapsack.SolveDp([], [], 100));
        Assert.Empty(Knapsack.SolveGreedy([], [], 100));
    }

    [Fact]
    public void ItemHeavierThanCapacity_IsSkipped()
    {
        long[] weights = [500, 10];
        long[] values = [1000, 1];

        bool[] chosen = Knapsack.SolveDp(weights, values, 100);

        Assert.False(chosen[0]);
        Assert.True(chosen[1]);
    }

    [Fact]
    public void Dp_WithScaledWeights_StillFitsRealCapacity()
    {
        var rng = new Random(11);
        var weights = new long[30];
        var values = new long[30];
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = rng.Next(100, 501);
            values[i] = rng.Next(1, 10);
        }
        const long capacity = 3000;

        // Дуже мала межа клітинок змушує ваги округлюватися: вибір усе одно не має перевищити місткість.
        bool[] chosen = Knapsack.SolveDp(weights, values, capacity, maxCells: 5000);

        Assert.True(Total(chosen, weights) <= capacity);
        Assert.True(Total(chosen, values) > 0);
    }

    [Fact]
    public void LengthMismatch_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Knapsack.SolveDp([1, 2], [1], 10));
        Assert.Throws<ArgumentException>(() => Knapsack.SolveGreedy([1, 2], [1], 10));
    }
}
