using System.Text;
using SmartArchiver.Core;

namespace SmartArchiver.Core.Tests;

public class RoundTripTests
{
    [Fact]
    public void SingleTextFile_ComesBackByteExact()
    {
        byte[] text = Encoding.UTF8.GetBytes("Привіт, архіваторе! Hello, archiver!\r\n\tтаб і нульовий байт: \0 кінець.");
        byte[] archive = new ArchiveWriter().Build(new[] { TestData.File("note.txt", text, FileKind.Text) });

        var files = ArchiveReader.Read(archive);

        var file = Assert.Single(files);
        Assert.Equal("note.txt", file.Name);
        Assert.Equal(text, file.Data);
        Assert.Equal(FileKind.Text, file.Kind);
        Assert.Equal(CompressionStrategy.NoCompression, file.Strategy);
        Assert.Equal(Crc32.Compute(text), file.Crc32);
    }

    [Fact]
    public void SingleBinaryFile_AllByteValues_ComesBackByteExact()
    {
        byte[] data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        byte[] archive = new ArchiveWriter().Build(new[] { TestData.File("all.bin", data) });

        Assert.Equal(data, Assert.Single(ArchiveReader.Read(archive)).Data);
    }

    [Fact]
    public void Archive_StartsWithValidHeader_AndLengthMatches()
    {
        byte[] data = TestData.RandomBytes(500);
        byte[] archive = new ArchiveWriter().Build(new[] { TestData.File("a.bin", data) });

        VolumeHeader header = VolumeHeader.Parse(archive);

        Assert.Equal(1, header.VolumeNumber);
        Assert.Equal(1, header.VolumeCount);
        Assert.Equal((uint)(archive.Length - FormatConstants.HeaderSize), header.CiphertextLength);
        Assert.Equal(TestData.ExpectedLength("a.bin", 500, FormatConstants.DefaultBlockSize), archive.Length);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var input = new[] { TestData.File("x.bin", TestData.RandomBytes(300)), TestData.File("y.bin", TestData.RandomBytes(10, 2)) };
        var writer = new ArchiveWriter(128);

        Assert.Equal(writer.Build(input), writer.Build(input));
    }

    [Fact]
    public void MultipleFiles_KeepOrderNamesKindsAndContent()
    {
        var inputs = new List<ArchiveInput>
        {
            TestData.File("one.txt", Encoding.UTF8.GetBytes("перший"), FileKind.Text),
            TestData.File("two.bin", TestData.RandomBytes(1000, 2), FileKind.AlreadyCompressed),
            TestData.File("три.csv", Encoding.UTF8.GetBytes("a,b\n1,2\n"), FileKind.Structured),
            TestData.File("four.dat", new byte[5000], FileKind.Artificial),
        };

        var files = ArchiveReader.Read(new ArchiveWriter(256).Build(inputs));

        Assert.Equal(inputs.Count, files.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            Assert.Equal(inputs[i].Name, files[i].Name);
            Assert.Equal(inputs[i].Data, files[i].Data);
            Assert.Equal(inputs[i].Kind, files[i].Kind);
            Assert.Equal(Crc32.Compute(inputs[i].Data), files[i].Crc32);
        }
    }

    [Fact]
    public void ManyFiles_InOneVolume()
    {
        var inputs = Enumerable.Range(0, 300)
            .Select(i => TestData.File($"f{i}.bin", TestData.RandomBytes(i % 50, i)))
            .ToList();

        var files = ArchiveReader.Read(new ArchiveWriter(16).Build(inputs));

        Assert.Equal(300, files.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            Assert.Equal(inputs[i].Data, files[i].Data);
        }
    }

    [Theory]
    [InlineData(2500, 1000, 3)]   // три блоки, останній неповний
    [InlineData(2000, 1000, 2)]   // кратно блоку: порожнього хвостового блока немає
    [InlineData(1000, 1000, 1)]   // рівно один блок
    [InlineData(1001, 1000, 2)]   // на байт більше блока
    [InlineData(999, 1000, 1)]    // менше блока
    [InlineData(200_000, 65536, 4)]
    public void LargeFile_IsSplitIntoBlocks_AndRestored(int size, int blockSize, int expectedBlocks)
    {
        byte[] data = TestData.RandomBytes(size, seed: size);

        byte[] archive = new ArchiveWriter(blockSize).Build(new[] { TestData.File("big.bin", data) });

        // Кожен блок додає рівно 15 байт службових даних: за цим видно кількість блоків.
        int expectedLength = TestData.ExpectedLength("big.bin", size, blockSize);
        Assert.Equal(expectedLength, archive.Length);
        Assert.Equal(expectedBlocks, (size + blockSize - 1) / blockSize);
        Assert.Equal(data, Assert.Single(ArchiveReader.Read(archive)).Data);
    }

    [Fact]
    public void BlockSizeOne_StillRoundTrips()
    {
        byte[] data = TestData.RandomBytes(50);
        var archive = new ArchiveWriter(1).Build(new[] { TestData.File("tiny-blocks", data) });

        Assert.Equal(data, Assert.Single(ArchiveReader.Read(archive)).Data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Writer_RejectsNonPositiveBlockSize(int blockSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveWriter(blockSize));
    }
}
