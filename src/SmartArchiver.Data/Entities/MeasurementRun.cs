namespace SmartArchiver.Data.Entities;

/// <summary>
/// One measurement of Q over a dataset: Q = α·S + β·T, where S = B_orig / (B_orig + B_arc)
/// and T = t_ref / (t_ref + t). Q is 0 when at least one file was not restored byte for byte.
/// </summary>
public sealed class MeasurementRun
{
    public int Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary>Git commit of the code that was measured, when known.</summary>
    public string? CommitHash { get; set; }

    public int? Seed { get; set; }

    /// <summary>Dataset name, for example "corpus".</summary>
    public string Dataset { get; set; } = "";

    public double Alpha { get; set; }

    public double Beta { get; set; }

    public double TRefSeconds { get; set; }

    /// <summary>Remaining run parameters (block size, strategy, ...) as JSON.</summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>B_orig: total size of the original files.</summary>
    public long OriginalBytes { get; set; }

    /// <summary>B_arc: total size of the archive volumes.</summary>
    public long ArchiveBytes { get; set; }

    /// <summary>t: archiving and extraction time.</summary>
    public double ElapsedSeconds { get; set; }

    public bool AllRestored { get; set; }

    public double Q { get; set; }

    public List<MeasurementFileResult> Files { get; set; } = [];
}
