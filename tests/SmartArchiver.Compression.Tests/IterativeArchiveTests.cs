using SmartArchiver.Analysis;
using SmartArchiver.Compression;
using Xunit;

namespace SmartArchiver.Archive.Tests;

public sealed class IterativeArchiveTests
{
    [Fact]
    public void ArchiveWriter_IterativeParser_RoundTrips_AndCrcMatches()
    {
        byte[] original = TestData.Repetitive(2_500);
        var writer = new ArchiveWriter(
            1024,
            new FixedAnalyzer(FileKind.Text, CompressionStrategy.Lz77Huffman),
            parseMode: Lz77ParseMode.Iterative);

        byte[] archive = writer.Build([TestData.File("iterative.txt", original)]);
        var restored = Assert.Single(ArchiveReader.Read(archive));

        Assert.Equal("iterative.txt", restored.Name);
        Assert.Equal(original, restored.Data);
        Assert.Equal(Crc32.Compute(original), restored.Crc32);
    }
}
