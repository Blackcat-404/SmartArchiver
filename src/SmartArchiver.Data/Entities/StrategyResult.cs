namespace SmartArchiver.Data.Entities;

/// <summary>How the files were chosen to be compressed in the strategy experiment.</summary>
public enum StrategyVariant
{
    /// <summary>Nothing compressed: the lower bound.</summary>
    Stored,

    /// <summary>Every file compressed with LZ77 + Huffman, without analysis.</summary>
    Fixed,

    /// <summary>The file analyzer (classifier) decides per file.</summary>
    Classifier,

    /// <summary>The file analyzer decides for every block of a file separately.</summary>
    ClassifierPerBlock,
}

/// <summary>One corpus group packed into an archive with one strategy variant.</summary>
public sealed class StrategyResult
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    /// <summary>Corpus group, file_type 0..6 from FORMAT_SPEC_P2.</summary>
    public byte Group { get; set; }

    public StrategyVariant Variant { get; set; }

    public int FileCount { get; set; }

    /// <summary>Files stored with strategy LZ77 + Huffman after packing.</summary>
    public int CompressedFiles { get; set; }

    public long OriginalBytes { get; set; }

    public long ArchiveBytes { get; set; }

    public double PackSeconds { get; set; }

    public double UnpackSeconds { get; set; }

    /// <summary>Every file came back byte for byte.</summary>
    public bool AllRestored { get; set; }
}
