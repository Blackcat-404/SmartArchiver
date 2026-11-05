namespace SmartArchiver.Data.Entities;

/// <summary>Effect of next-byte prediction on one file: entropy and compressed size with and without residuals.</summary>
public sealed class PredictionResult
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    /// <summary>Corpus group, file_type 0..6 from FORMAT_SPEC_P2.</summary>
    public byte Group { get; set; }

    public long OriginalBytes { get; set; }

    /// <summary>Entropy of the file bytes, bits per byte.</summary>
    public double OriginalEntropy { get; set; }

    /// <summary>Entropy of the residuals (actual − predicted) mod 256, bits per byte.</summary>
    public double ResidualEntropy { get; set; }

    /// <summary>Size after LZ77 + Huffman of the file itself.</summary>
    public long EncodedBytes { get; set; }

    /// <summary>Size after LZ77 + Huffman of the model id and the residuals.</summary>
    public long PredictedEncodedBytes { get; set; }

    /// <summary>Time to compute the residuals.</summary>
    public double ElapsedSeconds { get; set; }
}
