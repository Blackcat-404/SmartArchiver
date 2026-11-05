using System.Security.Cryptography;
using System.Text;
using SmartArchiver.Security;

namespace SmartArchiver.Archive.Tests;

/// <summary>The container byte for byte as FORMAT_SPEC_P2 describes it, checked on the control example ref_01 (section 8).</summary>
public class SpecConformanceTests
{
    /// <summary>hello.txt: 40 repetitions of "hello world ", 480 bytes.</summary>
    private static readonly byte[] Hello = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("hello world ", 40)));

    /// <summary>The 99-byte payload of ref_01_single_file.arc, copied from section 8 of the specification.</summary>
    private static readonly byte[] Ref01Payload = Convert.FromHexString(
        "0001" + "00" + "00"                                   // file_count = 1, flags = 0, reserved = 0
        + "0009" + "68656C6C6F2E747874"                       // name_len = 9, "hello.txt"
        + "000001E0" + "70208846" + "00" + "01"               // orig_size = 480, crc32, file_type = 0, strategy = 1
        + "0001"                                               // block_count = 1
        + "0000" + "00000000" + "000001E0" + "00000039" + "00" // file_index, block_seq, raw_len, coded_len = 57, flags
        + "0009" + "002003" + "006404" + "006504" + "006804" + "006C02" + "006F03" + "007204" + "007703" + "010003" // LIT
        + "0002" + "00CF01" + "00FF01"                         // LEN: 207 and 255
        + "0001" + "000401"                                    // DIST: 4
        + "0000000E" + "00000007" + "ED06A3F3159D18");          // token_count = 14, stream_len = 7, stream

    private static readonly byte[] Salt = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");

    private static readonly byte[] Nonce = Convert.FromHexString("010203040506070800000000");

    private sealed class Compress : IFileAnalyzer
    {
        public FileAnalysis Analyze(ReadOnlySpan<byte> data) => new(FileKind.Text, 1.0, CompressionStrategy.Lz77Huffman);
    }

    private static byte[] Plain(byte[] payload, ushort index = 0, ushort count = 1) =>
        [.. new VolumeHeader { VolumeIndex = index, VolumeCount = count, CiphertextLength = (uint)payload.Length }.ToBytes(), .. payload];

    private static ErrorCode Rejection(byte[] payload) =>
        Assert.Throws<ArchiveException>(() => ArchiveReader.Read(Plain(payload))).Code;

    [Fact]
    public void Ref01_PayloadIsReproducedByteForByte()
    {
        byte[] volume = new ArchiveWriter(analyzer: new Compress()).Build([new ArchiveInput("hello.txt", Hello)]);

        Assert.Equal(Convert.ToHexString(Ref01Payload), Convert.ToHexString(volume.AsSpan(FormatConstants.HeaderSize)));
    }

    [Fact]
    public void Ref01_PayloadFromTheSpecification_IsRead()
    {
        ExtractedFile file = Assert.Single(ArchiveReader.Read(Plain(Ref01Payload)));

        Assert.Equal("hello.txt", file.Name);
        Assert.Equal(Hello, file.Data);
        Assert.Equal((FileKind.Text, CompressionStrategy.Lz77Huffman, 0x70208846u), (file.Kind, file.Strategy, file.Crc32));
    }

    [Fact]
    public void Ref01_ProtectedVolume_HasTheHeaderOfTheSpecification_AndIs172Bytes()
    {
        byte[] plain = new ArchiveWriter(analyzer: new Compress()).Build([new ArchiveInput("hello.txt", Hello)]);

        byte[] volume = Assert.Single(ProtectedArchive.ProtectVolumes([plain], "algo2026", Salt, Nonce[..8]));

        Assert.Equal(172, volume.Length);
        Assert.Equal("534152430100000001" + "000102030405060708090A0B0C0D0E0F" + "010203040506070800000000" + "00000063",
            Convert.ToHexString(volume.AsSpan(0, FormatConstants.HeaderSize)));
        Assert.Equal(Hello, Assert.Single(ProtectedArchive.ReadProtectedVolumes([volume], "algo2026")).Data);
    }

    [Fact]
    public void Ctr_FirstCounterBlockIsOne()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        using var aes = Aes.Create();
        aes.Key = key;

        byte[] keystream = ArchiveSecurity.TransformCtr(new byte[16], key, Nonce);

        Assert.Equal(aes.EncryptEcb([.. Nonce, 0, 0, 0, 1], PaddingMode.None), keystream);
    }

    [Fact]
    public void Header_VolumeIndexStartsAtZero_AndIndexNotBelowCountIsRejected()
    {
        byte[] header = new VolumeHeader { VolumeIndex = 0, VolumeCount = 2 }.ToBytes();
        Assert.Equal("00000002", Convert.ToHexString(header.AsSpan(5, 4)));

        byte[] wrong = new VolumeHeader { VolumeIndex = 2, VolumeCount = 2 }.ToBytes();
        Assert.Equal(ErrorCode.E_VOLUMES, Assert.Throws<ArchiveException>(() => VolumeHeader.Parse(wrong)).Code);
    }

    [Theory]
    [InlineData(0, ErrorCode.E_SHORT)]
    [InlineData(72, ErrorCode.E_SHORT)]
    [InlineData(73, ErrorCode.E_MAGIC)]
    public void ProtectedVolume_ShorterThan73Bytes_IsShort_BeforeMagicIsChecked(int length, ErrorCode expected)
    {
        Assert.Equal(expected,
            Assert.Throws<ArchiveException>(() => ProtectedArchive.ReadProtectedVolumes([new byte[length]], "x")).Code);
    }

    [Theory]
    [InlineData(2, 0x01)]   // flags: PREDICTION is a team extension this archiver does not write
    [InlineData(2, 0x80)]   // flags: unknown bit
    [InlineData(3, 0x01)]   // reserved
    [InlineData(23, 0x07)]  // file_type above 6
    [InlineData(41, 0x02)]  // block_flags: bits 1-7 must be 0
    public void Payload_ReservedValuesAndUnknownBits_AreRejected(int offset, byte value)
    {
        byte[] payload = [.. Ref01Payload];
        payload[offset] = value;

        Assert.Equal(ErrorCode.E_PAYLOAD, Rejection(payload));
    }

    [Fact]
    public void Payload_BytesAfterTheLastBlock_AreRejected()
    {
        Assert.Equal(ErrorCode.E_PAYLOAD, Rejection([.. Ref01Payload, 0]));
    }

    [Fact]
    public void Payload_StoredBlockWhoseCodedLengthDiffersFromRawLength_IsRejected()
    {
        byte[] payload = [.. Ref01Payload];
        payload[41] = 0x01;   // NO_COMPRESSION, but coded_len = 57 and raw_len = 480

        Assert.Equal(ErrorCode.E_PAYLOAD, Rejection(payload));
    }

    [Fact]
    public void StoredBlock_HasNoCompressionFlag_AndRawBytes()
    {
        byte[] volume = new ArchiveWriter().Build([new ArchiveInput("a.bin", [1, 2, 3], FileKind.Raster)]);
        byte[] payload = volume[FormatConstants.HeaderSize..];

        // file_count, flags, reserved | name_len, "a.bin", size, crc, type, strategy | block_count | block record | data
        Assert.Equal("0001" + "0000", Convert.ToHexString(payload, 0, 4));
        int record = 4 + 2 + 5 + 4 + 4 + 1 + 1 + 2;
        Assert.Equal("0000" + "00000000" + "00000003" + "00000003" + "01", Convert.ToHexString(payload, record, 15));
        Assert.Equal("010203", Convert.ToHexString(payload, record + 15, 3));
    }

    [Fact]
    public void FileWithoutType_IsWrittenAsArtificialData_WhenThereIsNoAnalyzer()
    {
        byte[] volume = new ArchiveWriter().Build([new ArchiveInput("a.bin", [1, 2, 3])]);

        Assert.Equal(FileKind.Artificial, Assert.Single(ArchiveReader.Read(volume)).Kind);
    }

    [Fact]
    public void Padding_IsAnExplicitExtension_OnBothSides()
    {
        byte[] plain = new ArchiveWriter(analyzer: new Compress()).Build([new ArchiveInput("hello.txt", Hello)]);
        IReadOnlyList<byte[]> padded = ProtectedArchive.ProtectVolumes([plain], "pw", padTo: 256);

        Assert.Equal(0, (padded[0].Length - FormatConstants.HeaderSize - FormatConstants.TagSize) % 256);
        Assert.Equal(Hello, Assert.Single(ProtectedArchive.ReadProtectedVolumes(padded, "pw", padded: true)).Data);
        // Read as a plain archive by the specification, the padded payload is not a valid payload.
        Assert.Equal(ErrorCode.E_PAYLOAD,
            Assert.Throws<ArchiveException>(() => ProtectedArchive.ReadProtectedVolumes(padded, "pw")).Code);
    }
}
