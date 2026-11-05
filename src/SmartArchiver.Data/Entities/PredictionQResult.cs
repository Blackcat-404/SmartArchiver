namespace SmartArchiver.Data.Entities;

/// <summary>Q of one corpus group through the full pipeline, with or without next-byte prediction.</summary>
public sealed class PredictionQResult
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    /// <summary>Corpus group, file_type 0..6 from FORMAT_SPEC_P2.</summary>
    public byte Group { get; set; }

    /// <summary>True when the files were replaced by prediction residuals before packing.</summary>
    public bool Predicted { get; set; }

    public int FileCount { get; set; }

    public long OriginalBytes { get; set; }

    /// <summary>Size of all volumes.</summary>
    public long ArchiveBytes { get; set; }

    /// <summary>Pack and unpack; with prediction also computing and undoing the residuals.</summary>
    public double ElapsedSeconds { get; set; }

    public bool AllRestored { get; set; }

    public double S { get; set; }

    public double T { get; set; }

    /// <summary>Q = α·S + β·T, and 0 when any file was not restored.</summary>
    public double Q { get; set; }
}
