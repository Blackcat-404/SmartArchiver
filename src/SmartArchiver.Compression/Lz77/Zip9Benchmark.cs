using System.Globalization;
using System.IO.Compression;

namespace SmartArchiver.Compression.Benchmarks;

/// <summary>
/// CP3 bonus #4. Managed .NET ZIP with CompressionLevel.SmallestSize is the reference.
/// No ZIP code or ZIP metadata is introduced into the SARC format.
/// Each comparison uses the same bytes, file names, and complete archive sizes.
/// SARC creation is supplied by caller, avoiding Compression -> Archive dependency.
/// </summary>
public static class Zip9Benchmark
{
    public sealed record Configuration(
        string Name, int BlockSize, Lz77ParseMode ParseMode, SplitMode SplitMode);

    public sealed record ConfigurationResult(
        string Name, int BlockSize, string ParseMode, string SplitMode,
        long SarcBytes, long DeltaVsZipBytes, double Seconds, int StoredBlocks);

    public sealed record GroupResult(
        int Group, int FileCount, long RawBytes, long ZipBytes, long SarcBytes)
    {
        public long GainBytes => ZipBytes - SarcBytes;
    }

    public sealed record Report(
        int FilesCount, long RawBytes, long ZipBytes,
        IReadOnlyList<ConfigurationResult> Configurations,
        ConfigurationResult Best,
        IReadOnlyList<GroupResult> Groups)
    {
        public long GainBytes => ZipBytes - Best.SarcBytes;
        public double GainPercent => ZipBytes == 0 ? 0 : 100.0 * GainBytes / ZipBytes;
    }

    public sealed record SourceFile(int Group, string Name, byte[] Bytes);

    public enum SplitMode { Fixed, Dp }

    public sealed record SarcResult(long Bytes, double Seconds, int StoredBlocks);

    // Keep the production settings as the first baseline. Larger independent Huffman
    // blocks are an EXPERIMENT, not a change to the archive format/defaults.
    private static readonly Configuration[] Candidates =
    [
        new("Product Lazy / 64 KiB", 65_536, Lz77ParseMode.Lazy, SplitMode.Fixed),
        new("Iterative / 64 KiB", 65_536, Lz77ParseMode.Iterative, SplitMode.Fixed),
        new("Iterative / 128 KiB", 131_072, Lz77ParseMode.Iterative, SplitMode.Fixed),
        new("Iterative / 256 KiB", 262_144, Lz77ParseMode.Iterative, SplitMode.Fixed),
        new("DP block split / 64 KiB", 65_536, Lz77ParseMode.Greedy, SplitMode.Dp),
    ];

    public static Report RunDirectory(
        string datasetDirectory,
        Func<IReadOnlyList<SourceFile>, Configuration, SarcResult> buildAndVerifySarc,
        Action<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetDirectory);
        ArgumentNullException.ThrowIfNull(buildAndVerifySarc);

        List<SourceFile> sources = ReadCorpus(datasetDirectory);
        long rawBytes = sources.Sum(x => (long)x.Bytes.Length);

        // This is a managed ZIP reference, NOT byte-identical to Info-ZIP `zip -9`.
        progress?.Invoke("Створюємо та перевіряємо ZIP через .NET (SmallestSize)...");
        long zipBytes = CreateZipAndVerify(sources);

        var options = new List<ConfigurationResult>(Candidates.Length);
        foreach (Configuration config in Candidates)
        {
            progress?.Invoke($"Стискаємо SARC: {config.Name}...");
            SarcResult result = buildAndVerifySarc(sources, config);
            long bytes = result.Bytes;
            double seconds = result.Seconds;
            int storedBlocks = result.StoredBlocks;
            options.Add(new ConfigurationResult(
                config.Name, config.BlockSize, config.ParseMode.ToString(),
                config.SplitMode.ToString(), bytes, zipBytes - bytes,
                seconds, storedBlocks));
        }

        ConfigurationResult best = options.MinBy(x => x.SarcBytes)!;
        Configuration bestConfig = Candidates.Single(x => x.Name == best.Name);
        var groups = new List<GroupResult>(7);

        // Independent per-group archives: do NOT sum these numbers to estimate
        // the whole-corpus archive (each independent archive has its own headers).
        for (int group = 0; group < 7; group++)
        {
            List<SourceFile> subset = sources.Where(x => x.Group == group).ToList();
            progress?.Invoke($"Група {group}: ZIP .NET проти {best.Name}...");
            long groupZip = CreateZipAndVerify(subset);
            long groupSarc = buildAndVerifySarc(subset, bestConfig).Bytes;
            groups.Add(new GroupResult(
                group, subset.Count, subset.Sum(x => (long)x.Bytes.Length), groupZip, groupSarc));
        }

        return new Report(sources.Count, rawBytes, zipBytes, options, best, groups);
    }

    private static List<SourceFile> ReadCorpus(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Не знайдено корпус: {root}");

        var results = new List<SourceFile>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int group = 0; group < 7; group++)
        {
            string dir = Path.Combine(root, group.ToString(CultureInfo.InvariantCulture));
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"Немає групи корпусу: {dir}");

            foreach (string path in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                if (!names.Add(name))
                    throw new IOException(
                        $"Однакове ім'я {name} у різних групах. Для SARC потрібні унікальні імена.");
                results.Add(new SourceFile(group, name, File.ReadAllBytes(path)));
            }
        }

        if (results.Count == 0)
            throw new IOException("Корпус порожній.");
        return results;
    }

    private static long CreateZipAndVerify(List<SourceFile> sources)
    {
        // ZIP never touches the file system; source names and bytes match SARC input.
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (SourceFile source in sources)
            {
                ZipArchiveEntry entry = zip.CreateEntry(
                    source.Name, CompressionLevel.SmallestSize);
                // Fix timestamps to make repeated benchmark output deterministic.
                entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using Stream destination = entry.Open();
                destination.Write(source.Bytes);
            }
        }

        long size = buffer.Length;
        buffer.Position = 0;
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (zip.Entries.Count != sources.Count)
                throw new InvalidDataException("ZIP містить неправильну кількість файлів.");

            var expected = sources.ToDictionary(x => x.Name, x => x.Bytes, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (!expected.TryGetValue(entry.FullName, out byte[]? raw)
                    || !seen.Add(entry.FullName))
                    throw new InvalidDataException($"Зайвий або повторний ZIP-запис: {entry.FullName}");

                using Stream content = entry.Open();
                using var decoded = new MemoryStream();
                content.CopyTo(decoded);
                if (!decoded.GetBuffer().AsSpan(0, checked((int)decoded.Length)).SequenceEqual(raw))
                    throw new InvalidDataException($"ZIP round-trip не збігся: {entry.FullName}");
            }
        }
        return size;
    }
}
