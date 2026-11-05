namespace SmartArchiver.Data.Entities;

/// <summary>
/// One file compressed before encryption and after it (report table "compress then encrypt against the reverse").
/// Encrypted bytes look random, so compressing them should gain nothing.
/// </summary>
public sealed class EncryptionOrderResult
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    /// <summary>Corpus group, file_type 0..6 from FORMAT_SPEC_P2.</summary>
    public byte Group { get; set; }

    public long OriginalBytes { get; set; }

    public long CompressThenEncryptBytes { get; set; }

    public long EncryptThenCompressBytes { get; set; }
}
