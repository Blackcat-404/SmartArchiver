namespace SmartArchiver.Analysis.Tests;

public sealed class LabeledDatasetTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("smartarchiver-dataset-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private void AddFile(string group, string name, int length)
    {
        Directory.CreateDirectory(Path.Combine(root, group));
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(root, group, name), bytes);
    }

    [Fact]
    public void Load_LabelIsTheGroupFolder()
    {
        AddFile("0", "a.txt", 100);
        AddFile("5", "b.zip", 100);

        IReadOnlyList<LabeledSample> samples = LabeledDataset.Load(root, chunkSize: 4096, maxChunksPerFile: 10);

        Assert.Equal([0, 5], samples.Select(s => s.Label).Order());
        Assert.All(samples, s => Assert.Equal(FeatureVector.Length, s.Input.Length));
    }

    [Fact]
    public void Load_CutsFilesIntoChunks_AndDropsShortTail()
    {
        // 2 full chunks of 1000 bytes and a 300-byte tail, shorter than half a chunk.
        AddFile("2", "big.json", 2300);

        IReadOnlyList<LabeledSample> samples = LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 10);

        Assert.Equal(2, samples.Count);
    }

    [Fact]
    public void Load_KeepsTailOfAtLeastHalfAChunk()
    {
        AddFile("2", "big.json", 2600);

        Assert.Equal(3, LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 10).Count);
    }

    [Fact]
    public void Load_FileSmallerThanChunk_IsOneSample()
    {
        AddFile("5", "tiny.zip", 120);

        Assert.Single(LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 10));
    }

    [Fact]
    public void Load_LimitsChunksPerFile()
    {
        AddFile("2", "huge.json", 50_000);

        Assert.Equal(4, LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 4).Count);
    }

    [Fact]
    public void Load_IgnoresFoldersThatAreNotFileTypes()
    {
        AddFile("0", "a.txt", 100);
        AddFile("7", "x.bin", 100);
        AddFile("notes", "y.txt", 100);
        File.WriteAllText(Path.Combine(root, "README.md"), "dataset");

        Assert.Equal([0], LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 10).Select(s => s.Label));
    }

    [Fact]
    public void Load_MissingFolder_ThrowsDirectoryNotFound()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            LabeledDataset.Load(Path.Combine(root, "nope"), chunkSize: 1000, maxChunksPerFile: 10));
    }

    [Fact]
    public void SplitByFile_EveryThirdFileOfAGroupGoesToValid()
    {
        foreach (string name in new[] { "a", "b", "c" })
            AddFile("1", name + ".cs", 3000);

        DatasetSplit split = LabeledDataset.SplitByFile(LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 10));

        Assert.Equal(6, split.Train.Count);
        Assert.Equal(3, split.Valid.Count);
        Assert.All(split.Valid, s => Assert.EndsWith("c.cs", s.Source));
    }

    [Fact]
    public void SplitByFile_NoFileIsInBothSets()
    {
        foreach (string group in new[] { "0", "3", "6" })
        {
            foreach (string name in new[] { "a", "b", "c", "d" })
                AddFile(group, name + ".bin", 2500);
        }

        DatasetSplit split = LabeledDataset.SplitByFile(LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 10));

        Assert.Empty(split.Train.Select(s => s.Source).Intersect(split.Valid.Select(s => s.Source)));
        Assert.Equal([0, 3, 6], split.Valid.Select(s => s.Label).Distinct().Order());
    }

    [Fact]
    public void SplitFiles_UsesTheSameRuleAsSplitByFile()
    {
        foreach (string group in new[] { "0", "5" })
        {
            foreach (string name in new[] { "a", "b", "c", "d", "e" })
                AddFile(group, name + ".bin", 2500);
        }

        DatasetSplit pieces = LabeledDataset.SplitByFile(LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 10));
        (IReadOnlyList<LabeledFile> train, IReadOnlyList<LabeledFile> valid) = LabeledDataset.SplitFiles(LabeledDataset.ListFiles(root));

        Assert.Equal(pieces.Valid.Select(s => s.Source).Distinct().Order(), valid.Select(f => f.Source).Order());
        Assert.Equal(pieces.Train.Select(s => s.Source).Distinct().Order(), train.Select(f => f.Source).Order());
        Assert.All(valid.Concat(train), f => Assert.True(File.Exists(f.FullPath)));
    }

    [Fact]
    public void SplitByFile_GroupWithOneFile_StaysInTrain()
    {
        AddFile("4", "only.bin", 2000);

        DatasetSplit split = LabeledDataset.SplitByFile(LabeledDataset.Load(root, chunkSize: 1000, maxChunksPerFile: 10));

        Assert.Equal(2, split.Train.Count);
        Assert.Empty(split.Valid);
    }
}
