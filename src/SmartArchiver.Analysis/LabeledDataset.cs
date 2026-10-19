using System.Globalization;

namespace SmartArchiver.Analysis;

/// <summary>One training example: the classifier input for a piece of a file and its file type.</summary>
/// <param name="Source">File the piece came from, relative to the dataset folder; train and valid never share a source.</param>
/// <param name="Label">file_type 0..6.</param>
/// <param name="Input">Feature vector of the piece (<see cref="FeatureVector"/>).</param>
public sealed record LabeledSample(string Source, int Label, double[] Input);

public sealed record DatasetSplit(IReadOnlyList<LabeledSample> Train, IReadOnlyList<LabeledSample> Valid);

/// <summary>
/// Reads a dataset laid out as one folder per file type: <c>0/</c> … <c>6/</c>, the folder name being the label.
/// Each file is cut into pieces, so a handful of files still gives enough examples to train on.
/// </summary>
public static class LabeledDataset
{
    public const int ClassCount = 7;

    /// <summary>Piece size used for the shipped model (decision 26).</summary>
    public const int DefaultChunkSize = 4096;

    public const int DefaultMaxChunksPerFile = 24;

    /// <param name="chunkSize">Bytes per piece. A file not longer than this is one piece.</param>
    /// <param name="maxChunksPerFile">Cap, so that one big file does not outweigh a whole group.</param>
    public static IReadOnlyList<LabeledSample> Load(string root, int chunkSize, int maxChunksPerFile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunksPerFile, 1);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Dataset folder not found: {root}");

        var samples = new List<LabeledSample>();
        foreach (string folder in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
        {
            if (!int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out int label)
                || label >= ClassCount)
                continue;

            foreach (string file in Directory.GetFiles(folder).Order(StringComparer.Ordinal))
            {
                string source = Path.GetRelativePath(root, file);
                foreach (ReadOnlyMemory<byte> chunk in Chunks(File.ReadAllBytes(file), chunkSize, maxChunksPerFile))
                    samples.Add(new LabeledSample(source, label, FeatureVector.From(FeatureExtractor.Compute(chunk.Span))));
            }
        }
        return samples;
    }

    /// <summary>
    /// Every third file of each group (by name) goes to valid, the rest to train. Splitting by file rather than by
    /// piece keeps pieces of the same file out of both sets, otherwise valid would partly repeat train.
    /// </summary>
    public static DatasetSplit SplitByFile(IReadOnlyList<LabeledSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var validSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<int, LabeledSample> group in samples.GroupBy(s => s.Label))
        {
            string[] sources = [.. group.Select(s => s.Source).Distinct().Order(StringComparer.Ordinal)];
            for (int i = 2; i < sources.Length; i += 3)
                validSources.Add(sources[i]);
        }
        return new DatasetSplit(
            [.. samples.Where(s => !validSources.Contains(s.Source))],
            [.. samples.Where(s => validSources.Contains(s.Source))]);
    }

    private static IEnumerable<ReadOnlyMemory<byte>> Chunks(byte[] data, int chunkSize, int maxChunks)
    {
        if (data.Length <= chunkSize)
        {
            yield return data;
            yield break;
        }
        int count = 0;
        for (int offset = 0; offset < data.Length && count < maxChunks; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, data.Length - offset);
            if (length < chunkSize / 2)
                yield break;
            yield return data.AsMemory(offset, length);
            count++;
        }
    }
}
