using SmartArchiver.Core;

namespace SmartArchiver.Core.Tests;

public class EdgeCaseTests
{
    [Fact]
    public void EmptyArchive_HasOnlyHeaderAndZeroCount()
    {
        byte[] archive = new ArchiveWriter().Build(Array.Empty<ArchiveInput>());

        // 41 байт заголовка + 4 байти file_count = 0
        Assert.Equal(FormatConstants.HeaderSize + 4, archive.Length);
        Assert.Empty(ArchiveReader.Read(archive));
    }

    [Fact]
    public void EmptyFile_RoundTrips_WithZeroSizeAndZeroCrc()
    {
        byte[] archive = new ArchiveWriter().Build(new[] { TestData.File("empty.txt", Array.Empty<byte>()) });

        var file = Assert.Single(ArchiveReader.Read(archive));

        Assert.Equal("empty.txt", file.Name);
        Assert.Empty(file.Data);
        Assert.Equal(0u, file.Crc32);
        // Блоків немає, тож розмір = заголовок + count + запис таблиці.
        Assert.Equal(TestData.ExpectedLength("empty.txt", 0, FormatConstants.DefaultBlockSize), archive.Length);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x41)]
    [InlineData(0xFF)]
    public void OneByteFile_RoundTrips(int value)
    {
        byte[] data = { (byte)value };
        byte[] archive = new ArchiveWriter().Build(new[] { TestData.File("one.bin", data) });

        Assert.Equal(data, Assert.Single(ArchiveReader.Read(archive)).Data);
        Assert.Equal(TestData.ExpectedLength("one.bin", 1, FormatConstants.DefaultBlockSize), archive.Length);
    }

    [Fact]
    public void EmptyAndOneByteAndNormalFiles_Together()
    {
        var inputs = new[]
        {
            TestData.File("empty1", Array.Empty<byte>()),
            TestData.File("one", new byte[] { 7 }),
            TestData.File("empty2", Array.Empty<byte>()),
            TestData.File("normal", TestData.RandomBytes(300)),
            TestData.File("empty3", Array.Empty<byte>()),
        };

        var files = ArchiveReader.Read(new ArchiveWriter(100).Build(inputs));

        Assert.Equal(inputs.Length, files.Count);
        for (int i = 0; i < inputs.Length; i++)
        {
            Assert.Equal(inputs[i].Name, files[i].Name);
            Assert.Equal(inputs[i].Data, files[i].Data);
        }
    }

    [Fact]
    public void LongestAllowedName_RoundTrips()
    {
        string name = new string('x', 255);
        var file = Assert.Single(ArchiveReader.Read(new ArchiveWriter().Build(new[] { TestData.File(name, new byte[] { 1 }) })));

        Assert.Equal(name, file.Name);
    }

    [Fact]
    public void UnicodeName_RoundTrips()
    {
        string name = "звіт за жовтень №3 😀.txt";
        var file = Assert.Single(ArchiveReader.Read(new ArchiveWriter().Build(new[] { TestData.File(name, new byte[] { 1 }) })));

        Assert.Equal(name, file.Name);
    }
}
