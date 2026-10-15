namespace SmartArchiver.Analysis;

/// <summary>
/// Numbers that describe what a file looks like. The classifier turns them into a file kind.
/// </summary>
/// <param name="Entropy">Shannon entropy of single bytes, in bits: 0 for one repeated byte, 8 for uniform noise.</param>
/// <param name="PrintableAsciiFraction">Share of bytes that are visible ASCII characters, tab, LF or CR.</param>
/// <param name="AverageLineLength">Average number of characters between line breaks (LF and CR are not counted).</param>
/// <param name="BigramEntropy">Shannon entropy of pairs of neighbouring bytes, in bits (0 to 16).</param>
/// <param name="Histogram">Frequency of each byte value 0..255; sums to 1 for a non-empty file.</param>
/// <param name="Autocorrelation">Correlation of the byte sequence with itself shifted by 1..16 bytes, each from -1 to 1.</param>
public sealed record FileFeatures(
    double Entropy,
    double PrintableAsciiFraction,
    double AverageLineLength,
    double BigramEntropy,
    IReadOnlyList<double> Histogram,
    IReadOnlyList<double> Autocorrelation);
