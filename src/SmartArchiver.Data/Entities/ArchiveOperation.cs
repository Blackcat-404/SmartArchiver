namespace SmartArchiver.Data.Entities;

public enum ArchiveOperationKind
{
    Pack,
    Unpack,
}

/// <summary>
/// One pack or unpack in the archive history. There are deliberately no file names, paths, passwords or keys:
/// the history tells what happened and how well, not what was archived.
/// </summary>
public sealed class ArchiveOperation
{
    public int Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public ArchiveOperationKind Operation { get; set; }

    public int FileCount { get; set; }

    /// <summary>Files with strategy LZ77 + Huffman.</summary>
    public int CompressedFiles { get; set; }

    /// <summary>Total size of the files; 0 when unpacking failed before any file was read.</summary>
    public long OriginalBytes { get; set; }

    /// <summary>Total size of all volumes.</summary>
    public long ArchiveBytes { get; set; }

    public int VolumeCount { get; set; }

    public double ElapsedSeconds { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>Why the operation failed, for example E_HMAC or E_CAPACITY; null exactly when it succeeded.</summary>
    public string? ErrorCode { get; set; }
}
