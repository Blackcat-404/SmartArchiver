namespace SmartArchiver.Data.Entities;

public enum ParsingMethod
{
    Greedy,
    Lazy,
    Dp,
}

/// <summary>Size and time of one parsing method on one corpus group.</summary>
public sealed class ParsingResult
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    /// <summary>Corpus group, file_type 0..6 from FORMAT_SPEC_P2.</summary>
    public byte Group { get; set; }

    public ParsingMethod Method { get; set; }

    public long OriginalBytes { get; set; }

    public long EncodedBytes { get; set; }

    public double ElapsedSeconds { get; set; }
}
