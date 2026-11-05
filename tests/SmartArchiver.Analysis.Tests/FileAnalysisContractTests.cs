using SmartArchiver.Core;

namespace SmartArchiver.Analysis.Tests;

/// <summary>The numbers written to the archive must be exactly the ones FORMAT_SPEC_P2 defines.</summary>
public class FileAnalysisContractTests
{
    [Theory]
    [InlineData(FileKind.Text, 0)]
    [InlineData(FileKind.SourceCode, 1)]
    [InlineData(FileKind.Structured, 2)]
    [InlineData(FileKind.Raster, 3)]
    [InlineData(FileKind.Executable, 4)]
    [InlineData(FileKind.AlreadyCompressed, 5)]
    [InlineData(FileKind.Artificial, 6)]
    public void FileKind_EqualsFileTypeFromSpec(FileKind kind, byte fileType)
    {
        Assert.Equal(fileType, (byte)kind);
    }

    [Fact]
    public void FileKind_UnknownIsOutsideTheSpecRange()
    {
        // file_type > 6 is invalid in an archive, so "not classified yet" can never be written by mistake.
        Assert.True((byte)FileKind.Unknown > 6);
    }

    [Fact]
    public void CompressionStrategy_MatchesReferenceArchive()
    {
        // ref_01 in FORMAT_SPEC_P2 section 8 stores an LZ77 + Huffman file with strategy = 1.
        Assert.Equal(0, (byte)CompressionStrategy.NoCompression);
        Assert.Equal(1, (byte)CompressionStrategy.Lz77Huffman);
        Assert.Equal(2, Enum.GetValues<CompressionStrategy>().Length);
    }
}
