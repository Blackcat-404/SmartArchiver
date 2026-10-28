using System.Text.Json;

namespace SmartArchiver.Optimization;

/// <summary>Architecture choices available to the advanced chromosome.</summary>
public enum PerceptronArchitecture
{
    Compact,
    Standard,
    Wide,
}

/// <summary>
/// Optional parts of the complete archive strategy. The flags are genes, so the
/// advanced search can turn a stage on or off without changing the archive format.
/// </summary>
[Flags]
public enum StrategyFlags
{
    None = 0,
    UseClassifier = 1 << 0,
    UsePrediction = 1 << 1,
    PerBlockStrategy = 1 << 2,
    UseDpParser = 1 << 3,
    UseIterativeParser = 1 << 4,
    UseDpPacking = 1 << 5,
    AllowStoredBlocks = 1 << 6,
}

/// <summary>The three-gene chromosome for the basic task.</summary>
public record BasicChromosome(int SearchWindow, int MinMatchLength, double AbandonThreshold)
{
    public const int MinWindow = 256;
    public const int MaxWindow = 32_768;
    public const int MinMatch = 3;
    public const int MaxMatch = 258;

    public static BasicChromosome Default { get; } = new(32_768, 3, 0.0);

    public void Validate()
    {
        if (SearchWindow is < MinWindow or > MaxWindow)
            throw new ArgumentOutOfRangeException(nameof(SearchWindow));
        if (MinMatchLength is < MinMatch or > MaxMatch)
            throw new ArgumentOutOfRangeException(nameof(MinMatchLength));
        if (double.IsNaN(AbandonThreshold) || double.IsInfinity(AbandonThreshold) || AbandonThreshold is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(AbandonThreshold));
    }
}

/// <summary>
/// Full chromosome for the advanced task. The first three genes are deliberately
/// inherited from <see cref="BasicChromosome"/> so both experiments use the same
/// fitness contract.
/// </summary>
public sealed record FullChromosome(
    int SearchWindow,
    int MinMatchLength,
    double AbandonThreshold,
    int SearchDepth,
    int HuffmanBlockSize,
    int HistoryDepth,
    PerceptronArchitecture PerceptronArchitecture,
    StrategyFlags Strategies)
    : BasicChromosome(SearchWindow, MinMatchLength, AbandonThreshold)
{
    public const int MinSearchDepth = 1;
    public const int MaxSearchDepth = 1_024;
    public const int MinBlockSize = 4_096;
    public const int MaxBlockSize = 65_536;
    public const int MinHistoryDepth = 1;
    public const int MaxHistoryDepth = 4;

    public static FullChromosome Default { get; } = new(
        SearchWindow: 32_768,
        MinMatchLength: 3,
        AbandonThreshold: 0.0,
        SearchDepth: 256,
        HuffmanBlockSize: 65_536,
        HistoryDepth: 2,
        PerceptronArchitecture: PerceptronArchitecture.Standard,
        Strategies: StrategyFlags.UseClassifier | StrategyFlags.UseDpPacking | StrategyFlags.AllowStoredBlocks);

    public new void Validate()
    {
        base.Validate();
        if (SearchDepth is < MinSearchDepth or > MaxSearchDepth)
            throw new ArgumentOutOfRangeException(nameof(SearchDepth));
        if (HuffmanBlockSize is < MinBlockSize or > MaxBlockSize)
            throw new ArgumentOutOfRangeException(nameof(HuffmanBlockSize));
        if (HistoryDepth is < MinHistoryDepth or > MaxHistoryDepth)
            throw new ArgumentOutOfRangeException(nameof(HistoryDepth));
        if (!Enum.IsDefined(PerceptronArchitecture))
            throw new ArgumentOutOfRangeException(nameof(PerceptronArchitecture));
        const StrategyFlags all = StrategyFlags.UseClassifier | StrategyFlags.UsePrediction |
            StrategyFlags.PerBlockStrategy | StrategyFlags.UseDpParser | StrategyFlags.UseIterativeParser |
            StrategyFlags.UseDpPacking | StrategyFlags.AllowStoredBlocks;
        if ((Strategies & ~all) != 0)
            throw new ArgumentOutOfRangeException(nameof(Strategies));
    }

    public static FullChromosome FromBasic(BasicChromosome chromosome) => new(
        chromosome.SearchWindow,
        chromosome.MinMatchLength,
        chromosome.AbandonThreshold,
        SearchDepth: 256,
        HuffmanBlockSize: 65_536,
        HistoryDepth: 2,
        PerceptronArchitecture: PerceptronArchitecture.Standard,
        Strategies: StrategyFlags.UseClassifier | StrategyFlags.UseDpPacking | StrategyFlags.AllowStoredBlocks);
}

/// <summary>Stable JSON representation used in the database journal and CSV export.</summary>
public static class ChromosomeJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
    };

    public static string Serialize(BasicChromosome chromosome)
    {
        ArgumentNullException.ThrowIfNull(chromosome);
        chromosome.Validate();
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["window"] = chromosome.SearchWindow,
            ["min_match"] = chromosome.MinMatchLength,
            ["abandon_threshold"] = chromosome.AbandonThreshold,
        }, Options);
    }

    public static string Serialize(FullChromosome chromosome)
    {
        ArgumentNullException.ThrowIfNull(chromosome);
        chromosome.Validate();
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["window"] = chromosome.SearchWindow,
            ["min_match"] = chromosome.MinMatchLength,
            ["abandon_threshold"] = chromosome.AbandonThreshold,
            ["search_depth"] = chromosome.SearchDepth,
            ["huffman_block_size"] = chromosome.HuffmanBlockSize,
            ["history_k"] = chromosome.HistoryDepth,
            ["perceptron_architecture"] = chromosome.PerceptronArchitecture.ToString(),
            ["strategy_flags"] = chromosome.Strategies.ToString(),
        }, Options);
    }
}
