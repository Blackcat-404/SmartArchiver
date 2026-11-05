namespace SmartArchiver.Optimization;

/// <summary>Operators needed by all three searches.</summary>
public interface IChromosomeSpace<T>
{
    T CreateRandom(Random random);

    (T First, T Second) Crossover(T first, T second, Random random);

    T Mutate(T chromosome, Random random, double rate);

    IReadOnlyList<T> Neighbours(T chromosome);

    string Serialize(T chromosome);
}

/// <summary>Operators for the three-gene chromosome.</summary>
public sealed class BasicChromosomeSpace : IChromosomeSpace<BasicChromosome>
{
    private static readonly int[] Windows = [256, 512, 1_024, 2_048, 4_096, 8_192, 16_384, 32_768];

    public BasicChromosome CreateRandom(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return new BasicChromosome(
            Windows[random.Next(Windows.Length)],
            random.Next(BasicChromosome.MinMatch, BasicChromosome.MaxMatch + 1),
            random.Next(0, 26) / 100.0);
    }

    public (BasicChromosome First, BasicChromosome Second) Crossover(
        BasicChromosome first,
        BasicChromosome second,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(random);
        first.Validate();
        second.Validate();

        // Uniform crossover is appropriate for independent numeric genes.
        return (
            new BasicChromosome(
                random.Next(2) == 0 ? first.SearchWindow : second.SearchWindow,
                random.Next(2) == 0 ? first.MinMatchLength : second.MinMatchLength,
                random.Next(2) == 0 ? first.AbandonThreshold : second.AbandonThreshold),
            new BasicChromosome(
                random.Next(2) == 0 ? second.SearchWindow : first.SearchWindow,
                random.Next(2) == 0 ? second.MinMatchLength : first.MinMatchLength,
                random.Next(2) == 0 ? second.AbandonThreshold : first.AbandonThreshold));
    }

    public BasicChromosome Mutate(BasicChromosome chromosome, Random random, double rate)
    {
        ArgumentNullException.ThrowIfNull(chromosome);
        ArgumentNullException.ThrowIfNull(random);
        ValidateRate(rate);
        chromosome.Validate();

        int window = chromosome.SearchWindow;
        int minMatch = chromosome.MinMatchLength;
        double threshold = chromosome.AbandonThreshold;
        if (random.NextDouble() < rate)
            window = Windows[random.Next(Windows.Length)];
        if (random.NextDouble() < rate)
            minMatch = random.Next(BasicChromosome.MinMatch, BasicChromosome.MaxMatch + 1);
        if (random.NextDouble() < rate)
            threshold = random.Next(0, 26) / 100.0;
        return new BasicChromosome(window, minMatch, threshold);
    }

    public IReadOnlyList<BasicChromosome> Neighbours(BasicChromosome chromosome)
    {
        ArgumentNullException.ThrowIfNull(chromosome);
        chromosome.Validate();
        var neighbours = new List<BasicChromosome>();
        int windowIndex = Array.IndexOf(Windows, chromosome.SearchWindow);
        if (windowIndex > 0)
            neighbours.Add(chromosome with { SearchWindow = Windows[windowIndex - 1] });
        if (windowIndex >= 0 && windowIndex + 1 < Windows.Length)
            neighbours.Add(chromosome with { SearchWindow = Windows[windowIndex + 1] });
        if (chromosome.MinMatchLength > BasicChromosome.MinMatch)
            neighbours.Add(chromosome with { MinMatchLength = chromosome.MinMatchLength - 1 });
        if (chromosome.MinMatchLength < BasicChromosome.MaxMatch)
            neighbours.Add(chromosome with { MinMatchLength = chromosome.MinMatchLength + 1 });
        if (chromosome.AbandonThreshold >= 0.01)
            neighbours.Add(chromosome with { AbandonThreshold = chromosome.AbandonThreshold - 0.01 });
        if (chromosome.AbandonThreshold <= 0.24)
            neighbours.Add(chromosome with { AbandonThreshold = chromosome.AbandonThreshold + 0.01 });
        return neighbours;
    }

    public string Serialize(BasicChromosome chromosome) => ChromosomeJson.Serialize(chromosome);

    internal static void ValidateRate(double rate)
    {
        if (double.IsNaN(rate) || double.IsInfinity(rate) || rate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(rate));
    }
}

/// <summary>Operators for the advanced chromosome. Its crossover is two-point.</summary>
public sealed class FullChromosomeSpace : IChromosomeSpace<FullChromosome>
{
    private static readonly int[] Windows = [256, 512, 1_024, 2_048, 4_096, 8_192, 16_384, 32_768];
    private static readonly int[] BlockSizes = [4_096, 8_192, 16_384, 32_768, 65_536];
    private const StrategyFlags AllFlags = StrategyFlags.UseClassifier | StrategyFlags.UsePrediction |
        StrategyFlags.PerBlockStrategy | StrategyFlags.UseDpParser | StrategyFlags.UseIterativeParser |
        StrategyFlags.UseDpPacking | StrategyFlags.AllowStoredBlocks;

    public FullChromosome CreateRandom(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var flags = StrategyFlags.None;
        foreach (StrategyFlags flag in Enum.GetValues<StrategyFlags>())
        {
            if (flag != StrategyFlags.None && random.Next(2) == 1)
                flags |= flag;
        }
        return new FullChromosome(
            Windows[random.Next(Windows.Length)],
            random.Next(BasicChromosome.MinMatch, BasicChromosome.MaxMatch + 1),
            random.Next(0, 26) / 100.0,
            random.Next(1, FullChromosome.MaxSearchDepth + 1),
            BlockSizes[random.Next(BlockSizes.Length)],
            random.Next(FullChromosome.MinHistoryDepth, FullChromosome.MaxHistoryDepth + 1),
            (PerceptronArchitecture)random.Next(Enum.GetValues<PerceptronArchitecture>().Length),
            flags);
    }

    public (FullChromosome First, FullChromosome Second) Crossover(
        FullChromosome first,
        FullChromosome second,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(random);
        first.Validate();
        second.Validate();

        int left = random.Next(0, 8);
        int right = random.Next(left + 1, 9);
        bool TakeFirst(int index) => index < left || index >= right;
        return (Build(first, second, TakeFirst), Build(first, second, index => !TakeFirst(index)));
    }

    public FullChromosome Mutate(FullChromosome chromosome, Random random, double rate)
    {
        ArgumentNullException.ThrowIfNull(chromosome);
        ArgumentNullException.ThrowIfNull(random);
        BasicChromosomeSpace.ValidateRate(rate);
        chromosome.Validate();

        int window = chromosome.SearchWindow;
        int minMatch = chromosome.MinMatchLength;
        double threshold = chromosome.AbandonThreshold;
        int depth = chromosome.SearchDepth;
        int blockSize = chromosome.HuffmanBlockSize;
        int history = chromosome.HistoryDepth;
        PerceptronArchitecture architecture = chromosome.PerceptronArchitecture;
        StrategyFlags flags = chromosome.Strategies;

        if (random.NextDouble() < rate) window = Windows[random.Next(Windows.Length)];
        if (random.NextDouble() < rate) minMatch = random.Next(BasicChromosome.MinMatch, BasicChromosome.MaxMatch + 1);
        if (random.NextDouble() < rate) threshold = random.Next(0, 26) / 100.0;
        if (random.NextDouble() < rate) depth = random.Next(1, FullChromosome.MaxSearchDepth + 1);
        if (random.NextDouble() < rate) blockSize = BlockSizes[random.Next(BlockSizes.Length)];
        if (random.NextDouble() < rate) history = random.Next(FullChromosome.MinHistoryDepth, FullChromosome.MaxHistoryDepth + 1);
        if (random.NextDouble() < rate)
            architecture = (PerceptronArchitecture)random.Next(Enum.GetValues<PerceptronArchitecture>().Length);
        if (random.NextDouble() < rate)
            flags = RandomFlags(random);

        return new FullChromosome(window, minMatch, threshold, depth, blockSize, history, architecture, flags);
    }

    public IReadOnlyList<FullChromosome> Neighbours(FullChromosome chromosome)
    {
        ArgumentNullException.ThrowIfNull(chromosome);
        chromosome.Validate();
        var result = new List<FullChromosome>();
        int windowIndex = Array.IndexOf(Windows, chromosome.SearchWindow);
        if (windowIndex > 0)
            result.Add(chromosome with { SearchWindow = Windows[windowIndex - 1] });
        if (windowIndex >= 0 && windowIndex + 1 < Windows.Length)
            result.Add(chromosome with { SearchWindow = Windows[windowIndex + 1] });
        if (chromosome.MinMatchLength > BasicChromosome.MinMatch)
            result.Add(chromosome with { MinMatchLength = chromosome.MinMatchLength - 1 });
        if (chromosome.MinMatchLength < BasicChromosome.MaxMatch)
            result.Add(chromosome with { MinMatchLength = chromosome.MinMatchLength + 1 });
        if (chromosome.SearchDepth > 1)
            result.Add(chromosome with { SearchDepth = Math.Max(1, chromosome.SearchDepth / 2) });
        if (chromosome.SearchDepth < FullChromosome.MaxSearchDepth)
            result.Add(chromosome with { SearchDepth = Math.Min(FullChromosome.MaxSearchDepth, chromosome.SearchDepth * 2) });
        int blockIndex = Array.IndexOf(BlockSizes, chromosome.HuffmanBlockSize);
        if (blockIndex > 0)
            result.Add(chromosome with { HuffmanBlockSize = BlockSizes[blockIndex - 1] });
        if (blockIndex >= 0 && blockIndex + 1 < BlockSizes.Length)
            result.Add(chromosome with { HuffmanBlockSize = BlockSizes[blockIndex + 1] });
        if (chromosome.HistoryDepth > FullChromosome.MinHistoryDepth)
            result.Add(chromosome with { HistoryDepth = chromosome.HistoryDepth - 1 });
        if (chromosome.HistoryDepth < FullChromosome.MaxHistoryDepth)
            result.Add(chromosome with { HistoryDepth = chromosome.HistoryDepth + 1 });
        result.Add(chromosome with { PerceptronArchitecture = NextArchitecture(chromosome.PerceptronArchitecture, -1) });
        result.Add(chromosome with { PerceptronArchitecture = NextArchitecture(chromosome.PerceptronArchitecture, 1) });
        result.Add(chromosome with { Strategies = chromosome.Strategies ^ StrategyFlags.UseClassifier });
        result.Add(chromosome with { Strategies = chromosome.Strategies ^ StrategyFlags.UsePrediction });
        return result;
    }

    public string Serialize(FullChromosome chromosome) => ChromosomeJson.Serialize(chromosome);

    private static FullChromosome Build(FullChromosome first, FullChromosome second, Func<int, bool> takeFirst) =>
        new(
            takeFirst(0) ? first.SearchWindow : second.SearchWindow,
            takeFirst(1) ? first.MinMatchLength : second.MinMatchLength,
            takeFirst(2) ? first.AbandonThreshold : second.AbandonThreshold,
            takeFirst(3) ? first.SearchDepth : second.SearchDepth,
            takeFirst(4) ? first.HuffmanBlockSize : second.HuffmanBlockSize,
            takeFirst(5) ? first.HistoryDepth : second.HistoryDepth,
            takeFirst(6) ? first.PerceptronArchitecture : second.PerceptronArchitecture,
            takeFirst(7) ? first.Strategies : second.Strategies);

    private static StrategyFlags RandomFlags(Random random)
    {
        StrategyFlags result = StrategyFlags.None;
        foreach (StrategyFlags flag in Enum.GetValues<StrategyFlags>())
        {
            if (flag != StrategyFlags.None && random.Next(2) == 1)
                result |= flag;
        }
        return result & AllFlags;
    }

    private static PerceptronArchitecture NextArchitecture(PerceptronArchitecture value, int delta)
    {
        int count = Enum.GetValues<PerceptronArchitecture>().Length;
        return (PerceptronArchitecture)(((int)value + delta + count) % count);
    }
}
