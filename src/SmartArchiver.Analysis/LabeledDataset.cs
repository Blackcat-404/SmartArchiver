using System.Globalization;

namespace SmartArchiver.Analysis;

/// <summary>One training example: the classifier input for a piece of a file and its file type.</summary>
/// <param name="Source">File the piece came from, relative to the dataset folder; train and valid never share a source.</param>
/// <param name="Label">file_type 0..6.</param>
/// <param name="Input">Feature vector of the piece (<see cref="FeatureVector"/>).</param>
public sealed record LabeledSample(string Source, int Label, double[] Input);

public sealed record DatasetSplit(IReadOnlyList<LabeledSample> Train, IReadOnlyList<LabeledSample> Valid);

/// <summary>A whole dataset file with its file type.</summary>
/// <param name="Source">Path relative to the dataset folder, as in <see cref="LabeledSample.Source"/>.</param>
public sealed record LabeledFile(string Source, int Label, string FullPath);

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

        var samples = new List<LabeledSample>();
        foreach (LabeledFile file in ListFiles(root))
        {
            foreach (ReadOnlyMemory<byte> chunk in Chunks(File.ReadAllBytes(file.FullPath), chunkSize, maxChunksPerFile))
                samples.Add(new LabeledSample(file.Source, file.Label, FeatureVector.From(FeatureExtractor.Compute(chunk.Span))));
        }
        return samples;
    }

    /// <summary>Every file in folders 0..6, ordered by group and name; other folders and loose files are ignored.</summary>
    public static IReadOnlyList<LabeledFile> ListFiles(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Dataset folder not found: {root}");

        var files = new List<LabeledFile>();
        foreach (string folder in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
        {
            if (!int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out int label)
                || label >= ClassCount)
                continue;
            foreach (string file in Directory.GetFiles(folder).Order(StringComparer.Ordinal))
                files.Add(new LabeledFile(Path.GetRelativePath(root, file), label, file));
        }
        return files;
    }

    /// <summary>Whole files split by the same rule as <see cref="SplitByFile"/>.</summary>
    public static (IReadOnlyList<LabeledFile> Train, IReadOnlyList<LabeledFile> Valid) SplitFiles(IReadOnlyList<LabeledFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        HashSet<string> valid = ValidSources(files.Select(f => (f.Source, f.Label)));
        return ([.. files.Where(f => !valid.Contains(f.Source))], [.. files.Where(f => valid.Contains(f.Source))]);
    }

    /// <summary>
    /// Every third file of each group (by name) goes to valid, the rest to train. Splitting by file rather than by
    /// piece keeps pieces of the same file out of both sets, otherwise valid would partly repeat train.
    /// </summary>
    public static DatasetSplit SplitByFile(IReadOnlyList<LabeledSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        HashSet<string> validSources = ValidSources(samples.Select(s => (s.Source, s.Label)));
        return new DatasetSplit(
            [.. samples.Where(s => !validSources.Contains(s.Source))],
            [.. samples.Where(s => validSources.Contains(s.Source))]);
    }

    /// <summary>The single place of the split rule: every third file of each group, by name, is valid.</summary>
    private static HashSet<string> ValidSources(IEnumerable<(string Source, int Label)> items)
    {
        var valid = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<int, (string Source, int Label)> group in items.GroupBy(i => i.Label))
        {
            string[] sources = [.. group.Select(i => i.Source).Distinct().Order(StringComparer.Ordinal)];
            for (int i = 2; i < sources.Length; i += 3)
                valid.Add(sources[i]);
        }
        return valid;
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
