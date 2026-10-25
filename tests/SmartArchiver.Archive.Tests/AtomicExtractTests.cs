namespace SmartArchiver.Archive.Tests;

/// <summary>Розпакування або повне, або жодне: наявні файли не перезаписуються, часткового результату на диску немає.</summary>
public sealed class AtomicExtractTests : IDisposable
{
    private const string Password = "k";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sa-extract-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static IReadOnlyList<byte[]> TwoFiles() =>
        ArchivePipeline.Pack([TestData.File("a.txt", [1, 2, 3]), TestData.File("b.txt", [4, 5, 6])],
            new PackOptions(Password, new FixedAnalyzer(FileKind.Unknown, CompressionStrategy.NoCompression)));

    [Fact]
    public void Extract_WritesAllFiles_AndLeavesNoStaging()
    {
        ArchivePipeline.Extract(TwoFiles(), Password, _dir);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(_dir, "a.txt")));
        Assert.Equal([4, 5, 6], File.ReadAllBytes(Path.Combine(_dir, "b.txt")));
        Assert.Equal(2, Directory.GetFileSystemEntries(_dir).Length);
    }

    [Fact]
    public void ExistingFile_IsNeverOverwritten_AndNothingElseIsWritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "мій файл");

        Assert.Throws<IOException>(() => ArchivePipeline.Extract(TwoFiles(), Password, _dir));

        Assert.Equal("мій файл", File.ReadAllText(Path.Combine(_dir, "b.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "a.txt")));
        Assert.Single(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void DamagedArchive_WritesNothing_AndDoesNotCreateFolder()
    {
        byte[] bad = TwoFiles()[0];
        bad[^1] ^= 1;

        ArchiveResult<IReadOnlyList<ExtractedFile>> result = ArchivePipeline.TryExtract([bad], Password, _dir);

        Assert.Equal(ErrorCode.E_HMAC, result.Error);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void WrongPassword_WritesNothing()
    {
        Assert.Equal(ErrorCode.E_HMAC, ArchivePipeline.TryExtract(TwoFiles(), "інший", _dir).Error);
        Assert.False(Directory.Exists(_dir));
    }
}
