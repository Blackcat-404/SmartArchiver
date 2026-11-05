using System.Text;
using System.Text.Json;
using SmartArchiver.Analysis;
using SmartArchiver.Compression;

namespace SmartArchiver.Archive.Tests;

/// <summary>
/// The twelve basic acceptance cases C1 of the course, each through the full pipeline (analysis, LZ77 + Huffman,
/// packing, AES-256-CTR, HMAC) on the team corpus Datasets/. Run them alone with
/// <c>dotnet test --filter "FullyQualifiedName~AcceptanceC1"</c>.
/// </summary>
public class AcceptanceC1Tests
{
    private const string Password = "c1-acceptance";

    private static readonly PackOptions Options = new(Password, new FileAnalyzer());

    private static string Corpus(params string[] parts) => Path.Combine([FindCorpus(), .. parts]);

    private static byte[] CorpusFile(string group, string name) => File.ReadAllBytes(Corpus(group, name));

    private static IReadOnlyList<ExtractedFile> RoundTrip(IReadOnlyList<ArchiveInput> files, PackOptions? options = null)
    {
        options ??= Options;
        ArchiveResult<IReadOnlyList<ExtractedFile>> result = ArchivePipeline.TryUnpack(ArchivePipeline.Pack(files, options), options.Password);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(files.Select(f => f.Name), result.Value!.Select(f => f.Name));
        Assert.Equal(files.Select(f => f.Data), result.Value!.Select(f => f.Data));
        return result.Value!;
    }

    private static PreparedFile Prepared(byte[] data) => ArchivePipeline.Prepare([new ArchiveInput("f.bin", data)], Options)[0];

    [Fact]
    public void C1_01_TextFile_RoundTrips()
    {
        RoundTrip([new ArchiveInput("notes_en.txt", CorpusFile("0", "notes_en.txt"))]);
    }

    [Fact]
    public void C1_02_EmptyAndOneByteFiles_RoundTrip()
    {
        IReadOnlyList<ExtractedFile> restored = RoundTrip([new ArchiveInput("empty.bin", []), new ArchiveInput("one.bin", [0x42])]);

        Assert.Equal(0u, restored[0].Crc32);
    }

    [Fact]
    public void C1_03_AllSevenCorpusGroups_RoundTrip_WithMatchingCrc32()
    {
        List<ArchiveInput> files = [];
        for (int group = 0; group <= 6; group++)
        {
            files.AddRange(Directory.GetFiles(Corpus(group.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .Order(StringComparer.Ordinal)
                .Select(p => new ArchiveInput($"{group}_{Path.GetFileName(p)}", File.ReadAllBytes(p))));
        }

        IReadOnlyList<ExtractedFile> restored = RoundTrip(files);

        Assert.Equal(7, restored.Select(f => f.Name[0]).Distinct().Count());
        Assert.All(restored, f => Assert.Equal(Crc32.Compute(f.Data), f.Crc32));
    }

    [Fact]
    public void C1_04_IdenticalBytes_CompressManyTimes()
    {
        byte[] same = CorpusFile("6", "same_byte.bin");

        PreparedFile file = Prepared(same);

        Assert.Equal(CompressionStrategy.Lz77Huffman, file.Strategy);
        Assert.True(file.CodedSize * 50 < same.Length, $"{same.Length} B → {file.CodedSize} B");
        RoundTrip([new ArchiveInput("same_byte.bin", same)]);
    }

    [Fact]
    public void C1_05_Noise_IsStoredWithoutCompression_CodedLengthEqualsRawLength()
    {
        var noise = new byte[100_000];
        new Random(2026).NextBytes(noise);

        PreparedFile file = Prepared(noise);

        Assert.Equal(CompressionStrategy.NoCompression, file.Strategy);
        Assert.All(file.Blocks, b => Assert.True(b.IsStored && b.CodedLength == b.RawLength));
        RoundTrip([new ArchiveInput("noise.bin", noise)]);
    }

    [Theory]
    [InlineData("already_compressed.zip")]
    [InlineData("noise.png")]
    [InlineData("deflate_data.bin")]
    public void C1_06_AlreadyCompressedData_IsStoredWithoutCompression(string name)
    {
        PreparedFile file = Prepared(CorpusFile("5", name));

        Assert.Equal(CompressionStrategy.NoCompression, file.Strategy);
        Assert.Equal((long)file.Size, file.CodedSize);
    }

    [Fact]
    public void C1_07_HuffmanCodesAreRebuiltFromCanonicalLengths()
    {
        // The 57-byte block of the control example ref_01 (FORMAT_SPEC_P2, section 8): only code lengths are stored.
        byte[] block = Convert.FromHexString(
            "0009" + "002003" + "006404" + "006504" + "006804" + "006C02" + "006F03" + "007204" + "007703" + "010003"
            + "0002" + "00CF01" + "00FF01" + "0001" + "000401" + "0000000E" + "00000007" + "ED06A3F3159D18");

        byte[] decoded = Lz77CompressionService.DecodeBlock(block, 480);

        Assert.Equal(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("hello world ", 40))), decoded);
    }

    [Fact]
    public void C1_08_DegenerateAlphabet_OneSymbol_RoundTrips()
    {
        byte[] letters = Encoding.ASCII.GetBytes(new string('A', 5000));

        byte[] literalsOnly = Lz77CompressionService.EncodeBlock(letters, Lz77ParseMode.Lazy, new Lz77SearchOptions { LiteralsOnly = true });

        // One symbol: its code has length 1, so 5000 literals take 625 bytes of stream.
        Assert.Equal(letters, Lz77CompressionService.DecodeBlock(literalsOnly, letters.Length));
        RoundTrip([new ArchiveInput("degenerate.txt", letters)]);
    }

    [Fact]
    public void C1_09_GreedyAndLazyParsing_GiveSizesForEveryGroup_AndRoundTrip()
    {
        IReadOnlyList<ParsingGroupMeasurement> rows = ParsingComparisonRunner.RunDirectory(FindCorpus());

        Assert.Equal(14, rows.Count);
        Assert.All(rows, r => Assert.True(r.OriginalBytes > 0 && r.EncodedBytes > 0 && r.ElapsedSeconds >= 0));
        byte[] text = CorpusFile("2", "records.json");
        foreach (Lz77ParseMode mode in new[] { Lz77ParseMode.Greedy, Lz77ParseMode.Lazy })
            RoundTrip([new ArchiveInput("records.json", text)], Options with { ParseMode = mode });
    }

    [Fact]
    public void C1_10_DpKnapsack_IsNotWorseThanGreedy_OnTheFirstVolume()
    {
        // Classic instance: greedy by value per byte takes 60, DP takes 50 + 50 = 90.
        PackingPlan dp = VolumePacker.Pack([new PackFile(0, 30)], [new(0, 0, 60, 60), new(0, 1, 50, 45), new(0, 2, 50, 45)],
            VolumePacker.FixedOverhead + 30 + 100, PackingAlgorithm.Dp);
        PackingPlan greedy = VolumePacker.Pack([new PackFile(0, 30)], [new(0, 0, 60, 60), new(0, 1, 50, 45), new(0, 2, 50, 45)],
            VolumePacker.FixedOverhead + 30 + 100, PackingAlgorithm.Greedy);
        Assert.Equal((90L, 60L), (dp.Volumes[0].TotalValue, greedy.Volumes[0].TotalValue));

        // The corpus: the same blocks packed both ways.
        List<ArchiveInput> files = [.. Directory.GetFiles(Corpus("0")).Concat(Directory.GetFiles(Corpus("3")))
            .Select(p => new ArchiveInput(Path.GetFileName(p), File.ReadAllBytes(p)))];
        PackingComparison corpus = VolumePacker.Compare(ArchivePipeline.Prepare(files, Options), 65_536);
        Assert.True(corpus.Dp.Volumes[0].TotalValue >= corpus.Greedy.Volumes[0].TotalValue);
    }

    [Fact]
    public void C1_11_ChangedByte_WrongPassword_TruncatedArchive_AreRejected()
    {
        byte[] volume = ArchivePipeline.Pack([new ArchiveInput("notes.txt", CorpusFile("0", "notes_en.txt"))], Options)[0];
        ErrorCode? Code(byte[] v, string password = Password) => ArchivePipeline.TryUnpack([v], password).Error;

        byte[] changed = [.. volume];
        changed[FormatConstants.HeaderSize + 10] ^= 0x01;

        Assert.Equal(ErrorCode.E_HMAC, Code(changed));
        Assert.Equal(ErrorCode.E_HMAC, Code(volume, "wrong password"));
        Assert.Equal(ErrorCode.E_LENGTH, Code(volume[..^1]));
        Assert.Equal(ErrorCode.E_SHORT, Code(volume[..72]));
    }

    [Fact]
    public void C1_12_Volumes_AnyOrder_MissingOrDuplicateRejected_BigFileSplitAcrossVolumes()
    {
        byte[] big = CorpusFile("3", "stripes.bmp");
        var noise = new byte[150_000];
        new Random(7).NextBytes(noise);
        ArchiveInput[] files = [new("stripes.bmp", big), new("noise.bin", noise)];
        IReadOnlyList<byte[]> volumes = ArchivePipeline.Pack(files, Options with { BlockSize = 16_384, MaxVolumeBytes = 65_536 });

        Assert.True(volumes.Count >= 3, $"{volumes.Count} volumes");
        Assert.Equal(files.Select(f => f.Data), ArchivePipeline.Unpack([.. volumes.Reverse()], Password).Select(f => f.Data));
        Assert.Equal(ErrorCode.E_VOLUMES, ArchivePipeline.TryUnpack(volumes.Skip(1).ToArray(), Password).Error);
        Assert.Equal(ErrorCode.E_VOLUMES, ArchivePipeline.TryUnpack([.. volumes, volumes[0]], Password).Error);
    }

    /// <summary>
    /// C1 №12, second half: the teacher's reference archives. Put ref_*.arc (and ref_*.json) into Reference/ next to
    /// this file; without them the test checks nothing. The reader itself verifies HMAC and CRC32 of every file.
    /// </summary>
    [Fact]
    public void C1_12_ReferenceArchives_FromTheTeacher_Unpack()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Reference");
        if (!Directory.Exists(folder))
            return;
        string password = Environment.GetEnvironmentVariable("TEST_ARCHIVE_PASSWORD") ?? "algo2026";

        foreach (IGrouping<string, string> archive in Directory.GetFiles(folder, "ref_*.arc")
            .GroupBy(p => System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(p), @"\.part\d+$", "")))
        {
            ArchiveResult<IReadOnlyList<ExtractedFile>> result = ArchivePipeline.TryUnpack([.. archive.Select(File.ReadAllBytes)], password);
            Assert.True(result.Ok, $"{archive.Key}: {result.Error} {result.Message}");

            string json = Path.Combine(folder, archive.Key + ".json");
            if (File.Exists(json))
                AssertMatchesJson(result.Value!, File.ReadAllText(json), archive.Key);
        }
    }

    /// <summary>The json format is not specified beyond "file list with sizes and CRC32": names found in it must match.</summary>
    private static void AssertMatchesJson(IReadOnlyList<ExtractedFile> files, string json, string archive)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var named = new List<JsonElement>();
        Collect(document.RootElement, named);
        foreach (JsonElement entry in named)
        {
            string name = entry.GetProperty("name").GetString()!;
            ExtractedFile? file = files.FirstOrDefault(f => f.Name == name);
            Assert.True(file is not null, $"{archive}: no file {name}");
            foreach (string key in new[] { "size", "orig_size" })
            {
                if (entry.TryGetProperty(key, out JsonElement size) && size.ValueKind == JsonValueKind.Number)
                    Assert.Equal(size.GetInt64(), file!.Data.LongLength);
            }
        }
    }

    private static void Collect(JsonElement element, List<JsonElement> named)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
                named.Add(element);
            foreach (JsonProperty property in element.EnumerateObject())
                Collect(property.Value, named);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                Collect(item, named);
        }
    }

    private static string FindCorpus()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, "Datasets");
            if (Directory.Exists(Path.Combine(candidate, "0")) && Directory.Exists(Path.Combine(candidate, "6")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Datasets/ not found above the test folder.");
    }
}
