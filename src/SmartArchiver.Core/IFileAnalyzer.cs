namespace SmartArchiver.Core;

/// <summary>What the analyzer decided about a file before it is archived.</summary>
/// <param name="Kind">File type written to the archive as file_type.</param>
/// <param name="Confidence">Probability the classifier gave to <paramref name="Kind"/>, from 0 to 1.</param>
/// <param name="Strategy">How the archive should compress the file.</param>
public sealed record FileAnalysis(FileKind Kind, double Confidence, CompressionStrategy Strategy);

/// <summary>
/// Contract between file analysis and the archive: the archive asks for every file it packs,
/// writes <see cref="FileAnalysis.Kind"/> as file_type and compresses according to <see cref="FileAnalysis.Strategy"/>.
/// </summary>
public interface IFileAnalyzer
{
    FileAnalysis Analyze(ReadOnlySpan<byte> data);
}
