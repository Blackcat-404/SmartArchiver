namespace SmartArchiver.Data.Entities;

/// <summary>A block of the pipeline that the block impact experiment switches off.</summary>
public enum PipelineBlock
{
    /// <summary>Nothing is switched off: the full pipeline, the baseline of the table.</summary>
    None,

    /// <summary>File analysis: a fixed strategy instead of the classifier's choice.</summary>
    Analysis,

    /// <summary>LZ77 parsing: only Huffman over the raw bytes.</summary>
    Lz77,

    /// <summary>Huffman coding: LZ77 tokens stored without entropy coding.</summary>
    Huffman,

    /// <summary>DP knapsack packing of volumes: greedy packing instead.</summary>
    Packing,

    /// <summary>AES-256-CTR and HMAC-SHA256.</summary>
    Encryption,

    /// <summary>Next-byte prediction (residual coding).</summary>
    Prediction,
}

/// <summary>Q of the whole corpus with one block of the pipeline switched off (report table "impact of each block").</summary>
public sealed class BlockImpactResult
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    public PipelineBlock DisabledBlock { get; set; }

    public long OriginalBytes { get; set; }

    /// <summary>Size of all volumes.</summary>
    public long ArchiveBytes { get; set; }

    public double ElapsedSeconds { get; set; }

    public bool AllRestored { get; set; }

    public double S { get; set; }

    public double T { get; set; }

    /// <summary>Q = α·S + β·T, and 0 when any file was not restored.</summary>
    public double Q { get; set; }
}
