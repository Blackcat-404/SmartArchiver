using System.Globalization;
using System.Text;

namespace SmartArchiver.Compression;

/// <summary>
/// CP3 D1 #19: вимірювання ітеративного DP на корпусі без зміни формату архіву.
/// Розміри включають таблиці Huffman всередині Lz77BlockEncoder.Encode,
/// але НЕ включають зовнішній заголовок архівного блока (15 байтів).
/// </summary>
public static class IterativeDpBenchmark
{
    public sealed record IterationRow(
        int Group, string File, int BlockIndex, int RawBytes, int Iteration,
        int CandidateBytes, int AcceptedBytes, int GainBytes, long EstimatedBits,
        int TokenCount, bool Accepted, double ElapsedSeconds);

    public sealed record FileRow(
    int Group,
    string File,
    long RawBytes,
    int Blocks,
    long BaselineBytes,
    long OptimizedBytes)
{
    public long GainBytes =>
        BaselineBytes - OptimizedBytes;

    public double GainPercent =>
        BaselineBytes == 0
            ? 0
            : 100.0 * (BaselineBytes - OptimizedBytes) / BaselineBytes;
}

    public sealed record Report(IReadOnlyList<IterationRow> Iterations, IReadOnlyList<FileRow> Files);

    /// <summary>Оновлення прогресу після кожного обробленого блока.</summary>
    public sealed record ProgressRow(int Group, string File, int BlocksDone, int BlocksTotal);

    public static Report RunDirectory(string corpusDirectory,
        int blockSize = 8192, int maxIterations = 3, int maxChain = 64,
        Action<ProgressRow>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusDirectory);
        if (!Directory.Exists(corpusDirectory))
            throw new DirectoryNotFoundException(corpusDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maxIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChain);

        var steps = new List<IterationRow>();
        var files = new List<FileRow>();
        for (int group = 0; group < 7; group++)
        {
            string groupPath = Path.Combine(corpusDirectory,
                group.ToString(CultureInfo.InvariantCulture));
            if (!Directory.Exists(groupPath))
                throw new DirectoryNotFoundException($"Відсутня група корпусу {group}: {groupPath}");

            foreach (string path in Directory.EnumerateFiles(groupPath, "*", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                byte[] data = File.ReadAllBytes(path);
                string name = Path.GetRelativePath(groupPath, path).Replace('\\', '/');
                long baselineSum = 0, optimizedSum = 0;
                int blocks = 0;
                int totalBlocks = (int)((data.LongLength + blockSize - 1L) / blockSize);
                for (int offset = 0; offset < data.Length; offset += blockSize)
                {
                    int count = Math.Min(blockSize, data.Length - offset);
                    byte[] block = data.AsSpan(offset, count).ToArray();
                    var result = IterativeParser.Optimize(block, maxIterations, maxChain);

                    // Історія включає відхилений останній кандидат.
                    int acceptedBytes = result.History[0].AcceptedBytes;
                    foreach (var step in result.History)
                    {
                        acceptedBytes = step.AcceptedBytes;
                        steps.Add(new IterationRow(group, name, blocks, count,
                            step.Iteration, step.CandidateBytes, step.AcceptedBytes,
                            step.GainBytes, step.EstimatedBits, step.TokenCount,
                            step.Accepted, step.ElapsedSeconds));
                    }
                    if (acceptedBytes != result.Encoded.Length)
                        throw new InvalidOperationException("Історія не збігається з оптимальним результатом.");
                    if (!Lz77BlockDecoder.Decode(result.Encoded, count).AsSpan().SequenceEqual(block))
                        throw new InvalidOperationException($"Декодування не збіглося: {group}/{name}, блок {blocks}");
                    baselineSum += result.History[0].AcceptedBytes;
                    optimizedSum += acceptedBytes;
                    blocks++;
                    progress?.Invoke(new ProgressRow(group, name, blocks, totalBlocks));
                }
                files.Add(new FileRow(group, name, data.Length, blocks, baselineSum, optimizedSum));
            }
        }
        return new Report(steps, files);
    }

    public static void SaveCsv(Report report, string directory)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var culture = CultureInfo.InvariantCulture;
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        var details = new StringBuilder("group,file,block,raw_bytes,iteration,candidate_bytes,accepted_bytes,gain_bytes,estimated_bits,tokens,accepted,elapsed_seconds\n");
        foreach (var row in report.Iterations)
            details.Append(row.Group).Append(',').Append(Csv(row.File)).Append(',')
                .Append(row.BlockIndex).Append(',').Append(row.RawBytes).Append(',')
                .Append(row.Iteration).Append(',').Append(row.CandidateBytes).Append(',')
                .Append(row.AcceptedBytes).Append(',').Append(row.GainBytes).Append(',')
                .Append(row.EstimatedBits).Append(',').Append(row.TokenCount).Append(',')
                .Append(row.Accepted ? "true" : "false").Append(',')
                .Append(row.ElapsedSeconds.ToString("F6", culture)).Append('\n');

        var summary = new StringBuilder("group,file,raw_bytes,blocks,baseline_bytes,optimized_bytes,gain_bytes,gain_percent\n");
        foreach (var row in report.Files)
            summary.Append(row.Group).Append(',').Append(Csv(row.File)).Append(',')
                .Append(row.RawBytes).Append(',').Append(row.Blocks).Append(',')
                .Append(row.BaselineBytes).Append(',').Append(row.OptimizedBytes).Append(',')
                .Append(row.GainBytes).Append(',').Append(row.GainPercent.ToString("F4", culture)).Append('\n');

        File.WriteAllText(Path.Combine(directory, "iterative-dp-iterations.csv"), details.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "iterative-dp-files.csv"), summary.ToString(), new UTF8Encoding(false));
    }
}
