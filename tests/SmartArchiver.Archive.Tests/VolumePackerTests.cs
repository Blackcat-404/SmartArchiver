using SmartArchiver.Security;

namespace SmartArchiver.Archive.Tests;

public class VolumePackerTests
{
    private static readonly int[] BothFiles = [0, 1];
    private static readonly int[] OnlyFirstFile = [0];
    private static readonly int[] OnlySecondFile = [1];

    // Місткість W = max − 77 (заголовок 41 + HMAC 32 + file_count 4) − записи таблиці файлів тому.
    private const int Overhead = 79;

    [Fact]
    public void FixedOverhead_IsHeaderPlusHmacPlusFixedPayloadFields()
    {
        // FORMAT_SPEC_P2, розділ 7: W = max_volume_bytes − 41 − 32 − 6 − таблиця файлів.
        Assert.Equal(41 + 32 + 6, VolumePacker.FixedOverhead);
        Assert.Equal(ArchiveSecurity.TagSize, FormatConstants.TagSize);
    }

    [Fact]
    public void FirstVolume_DpBeatsGreedy_OnClassicInstance()
    {
        PackFile[] files = [new PackFile(0, 30)];
        PackBlock[] blocks = [new(0, 0, 60, 60), new(0, 1, 50, 45), new(0, 2, 50, 45)];
        long max = Overhead + 30 + 100;   // W = 100

        PackingPlan dp = VolumePacker.Pack(files, blocks, max, PackingAlgorithm.Dp);
        PackingPlan greedy = VolumePacker.Pack(files, blocks, max, PackingAlgorithm.Greedy);

        Assert.Equal(100, dp.Volumes[0].CapacityBytes);
        Assert.Equal(90, dp.Volumes[0].TotalValue);
        Assert.Equal(60, greedy.Volumes[0].TotalValue);
        Assert.Equal(3, dp.TotalBlocks);
        Assert.Equal(3, greedy.TotalBlocks);
    }

    [Theory]
    [InlineData(PackingAlgorithm.Dp)]
    [InlineData(PackingAlgorithm.Greedy)]
    public void EveryBlock_IsPlacedExactlyOnce_AndEveryVolumeFits(PackingAlgorithm algorithm)
    {
        var rng = new Random(5);
        var files = new List<PackFile>();
        var blocks = new List<PackBlock>();
        for (int f = 0; f < 8; f++)
        {
            files.Add(new PackFile(f, 24 + 6));
            int count = rng.Next(0, 9);
            for (int b = 0; b < count; b++)
            {
                blocks.Add(new PackBlock(f, b, rng.Next(16, 400), rng.Next(1, 6)));
            }
        }

        PackingPlan plan = VolumePacker.Pack(files, blocks, 1500, algorithm);

        var placed = plan.Volumes.SelectMany(v => v.Blocks).Select(b => (b.FileIndex, b.Sequence)).ToList();
        Assert.Equal(blocks.Count, placed.Count);
        Assert.Equal(blocks.Count, placed.Distinct().Count());
        foreach (VolumePlan volume in plan.Volumes)
        {
            Assert.True(volume.UsedBytes <= volume.CapacityBytes);
            Assert.True(volume.UsedBytes + volume.CapacityBytes <= 2 * 1500);
        }
    }

    [Fact]
    public void Capacity_SubtractsHeaderHmacAndFileTable()
    {
        PackFile[] files = [new PackFile(0, 30), new PackFile(1, 40)];
        PackBlock[] blocks = [new(0, 0, 100, 1), new(1, 0, 100, 1)];

        PackingPlan plan = VolumePacker.Pack(files, blocks, 1000, PackingAlgorithm.Dp);

        Assert.Equal(1000 - Overhead - 30 - 40, plan.Volumes[0].CapacityBytes);
        Assert.Single(plan.Volumes);
        Assert.Equal(BothFiles, plan.Volumes[0].FileIndexes);
    }

    [Fact]
    public void BlockHeavierThanVolume_Is_E_CAPACITY()
    {
        PackFile[] files = [new PackFile(0, 30)];
        PackBlock[] blocks = [new(0, 0, 200, 1)];

        ArchiveException ex = TestData.AssertCode(ErrorCode.E_CAPACITY,
            () => VolumePacker.Pack(files, blocks, Overhead + 30 + 100, PackingAlgorithm.Dp));

        Assert.Contains("200", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockThatFillsVolumeExactly_IsAccepted()
    {
        PackFile[] files = [new PackFile(0, 30)];
        PackBlock[] blocks = [new(0, 0, 100, 1)];

        PackingPlan plan = VolumePacker.Pack(files, blocks, Overhead + 30 + 100, PackingAlgorithm.Greedy);

        Assert.Single(plan.Volumes);
        Assert.Equal(plan.Volumes[0].CapacityBytes, plan.Volumes[0].UsedBytes);
    }

    [Fact]
    public void EmptyFiles_GoToFirstVolumeOnly()
    {
        PackFile[] files = [new PackFile(0, 30), new PackFile(1, 30)];
        PackBlock[] blocks = [new(0, 0, 60, 1), new(0, 1, 60, 1), new(0, 2, 60, 1)];

        // Перший том: таблиця з двох файлів, W = 100, влазить один блок. Далі таблиця лише з файлу 0.
        PackingPlan plan = VolumePacker.Pack(files, blocks, Overhead + 60 + 100, PackingAlgorithm.Dp);

        Assert.Equal(BothFiles, plan.Volumes[0].FileIndexes);
        Assert.Equal(OnlyFirstFile, plan.Volumes[1].FileIndexes);
        Assert.Equal(3, plan.TotalBlocks);
    }

    [Fact]
    public void FullTableLeavesNoRoom_FallsBackToFilesOneByOne()
    {
        PackFile[] files = [new PackFile(0, 30), new PackFile(1, 30)];
        PackBlock[] blocks = [new(0, 0, 90, 1), new(1, 0, 90, 1)];

        // Кожен блок окремо влазить (W = 90 з одним файлом у таблиці), а з обома файлами W = 60.
        PackingPlan plan = VolumePacker.Pack(files, blocks, Overhead + 30 + 90, PackingAlgorithm.Dp);

        Assert.Equal(2, plan.Volumes.Count);
        Assert.Equal(OnlyFirstFile, plan.Volumes[0].FileIndexes);
        Assert.Equal(OnlySecondFile, plan.Volumes[1].FileIndexes);
        Assert.Single(plan.Volumes[0].Blocks);
        Assert.Single(plan.Volumes[1].Blocks);
    }

    [Fact]
    public void OnlyEmptyFiles_ProduceOneVolume()
    {
        PackFile[] files = [new PackFile(0, 30), new PackFile(1, 30)];

        PackingPlan plan = VolumePacker.Pack(files, [], 1000, PackingAlgorithm.Dp);

        var volume = Assert.Single(plan.Volumes);
        Assert.Equal(BothFiles, volume.FileIndexes);
        Assert.Empty(volume.Blocks);
    }

    [Fact]
    public void NoFilesAtAll_ProduceOneEmptyVolume()
    {
        PackingPlan plan = VolumePacker.Pack([], [], 1000, PackingAlgorithm.Greedy);

        var volume = Assert.Single(plan.Volumes);
        Assert.Empty(volume.FileIndexes);
        Assert.Empty(volume.Blocks);
    }

    [Fact]
    public void FileTableThatDoesNotFit_Is_E_CAPACITY()
    {
        PackFile[] files = [new PackFile(0, 500)];

        TestData.AssertCode(ErrorCode.E_CAPACITY, () => VolumePacker.Pack(files, [], 300, PackingAlgorithm.Dp));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(uint.MaxValue + 1L)]
    public void VolumeSizeOutOfRange_IsRejected(long max)
    {
        PackFile[] files = [new PackFile(0, 30)];
        Assert.Throws<ArgumentOutOfRangeException>(() => VolumePacker.Pack(files, [], max, PackingAlgorithm.Dp));
    }

    [Fact]
    public void BlockOfUnknownFile_IsRejected()
    {
        PackBlock[] blocks = [new(7, 0, 10, 1)];
        Assert.Throws<ArgumentException>(() => VolumePacker.Pack([], blocks, 1000, PackingAlgorithm.Dp));
    }

    [Fact]
    public void Value_ComesFromPrioritiesByFileKind()
    {
        var writer = new ArchiveWriter(100);
        var files = new[]
        {
            TestData.File("text.txt", TestData.RandomBytes(150), FileKind.Text),
            TestData.File("noise.bin", TestData.RandomBytes(50, 2), FileKind.AlreadyCompressed),
        };
        IReadOnlyList<PreparedFile> prepared = writer.Prepare(files);
        var priorities = new PackingPriorities([10, 0, 0, 0, 0, 3, 0]);

        PackingPlan plan = VolumePacker.Pack(prepared, 10_000, PackingAlgorithm.Dp, priorities);

        PackBlock[] packed = [.. plan.Volumes.SelectMany(v => v.Blocks)];
        Assert.All(packed.Where(b => b.FileIndex == 0), b => Assert.Equal(10, b.Value));
        Assert.All(packed.Where(b => b.FileIndex == 1), b => Assert.Equal(3, b.Value));
        Assert.Equal(3, packed.Length);
    }

    [Fact]
    public void Compare_RunsBothAlgorithmsOnTheSameBlocks()
    {
        IReadOnlyList<PreparedFile> prepared = new ArchiveWriter(200).Prepare([TestData.File("a.bin", TestData.RandomBytes(900))]);

        PackingComparison comparison = VolumePacker.Compare(prepared, 2000);

        Assert.Equal(PackingAlgorithm.Dp, comparison.Dp.Algorithm);
        Assert.Equal(PackingAlgorithm.Greedy, comparison.Greedy.Algorithm);
        Assert.Equal(comparison.Dp.TotalBlocks, comparison.Greedy.TotalBlocks);
        Assert.Equal(comparison.Dp.TotalUsedBytes, comparison.Greedy.TotalUsedBytes);
    }
}
