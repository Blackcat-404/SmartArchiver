namespace SmartArchiver.Compression;

/// <summary>
/// Tunable parameters of the LZ77 search. The archive format stays unchanged: the
/// parameters only decide which valid tokens are produced by the encoder.
/// </summary>
public sealed record Lz77SearchOptions
{
    public const int MinWindow = 256;
    public const int MaxWindow = 32_768;
    public const int MinMatch = 3;
    public const int MaxMatch = 258;
    // The legacy parser accepted every positive maxChain; keep that API range.
    public const int MaxSearchDepth = int.MaxValue;

    /// <summary>Maximum distance to look backwards in the current block.</summary>
    public int SearchWindow { get; init; } = MaxWindow;

    /// <summary>Minimum length at which a match is emitted instead of literals.</summary>
    public int MinMatchLength { get; init; } = MinMatch;

    /// <summary>Maximum number of hash-chain candidates inspected at one position.</summary>
    public int SearchDepth { get; init; } = 256;

    /// <summary>
    /// Minimum fractional saving required to keep a coded block. For example, 0.05
    /// means that a block must be at least five percent smaller than its raw form.
    /// </summary>
    public double AbandonThreshold { get; init; }

    /// <summary>Whether a block may be stored raw when compression is not worthwhile.</summary>
    public bool AllowStoredBlocks { get; init; } = true;

    public void Validate()
    {
        if (SearchWindow is < MinWindow or > MaxWindow)
            throw new ArgumentOutOfRangeException(nameof(SearchWindow), $"Вікно має бути від {MinWindow} до {MaxWindow}.");
        if (MinMatchLength is < MinMatch or > MaxMatch)
            throw new ArgumentOutOfRangeException(nameof(MinMatchLength), $"Мінімальний збіг має бути від {MinMatch} до {MaxMatch}.");
        if (SearchDepth is < 1 or > MaxSearchDepth)
            throw new ArgumentOutOfRangeException(nameof(SearchDepth), $"Глибина пошуку має бути від 1 до {MaxSearchDepth}.");
        if (double.IsNaN(AbandonThreshold) || double.IsInfinity(AbandonThreshold) || AbandonThreshold is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(AbandonThreshold), "Поріг має бути числом від 0 до 1.");
    }
}
