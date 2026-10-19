namespace SmartArchiver.Data.Entities;

/// <summary>
/// Result for one file of a measurement run. The file name is not stored on purpose.
/// </summary>
public sealed class MeasurementFileResult
{
    public int Id { get; set; }

    public int RunId { get; set; }

    public MeasurementRun Run { get; set; } = null!;

    /// <summary>Corpus group of the file (numeric file kind).</summary>
    public byte Group { get; set; }

    public long OriginalBytes { get; set; }

    /// <summary>Bytes the file takes in the archive: its block headers plus coded data.</summary>
    public long ArchiveBytes { get; set; }

    public bool Restored { get; set; }
}
