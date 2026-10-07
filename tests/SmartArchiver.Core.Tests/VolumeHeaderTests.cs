using SmartArchiver.Core;

namespace SmartArchiver.Core.Tests;

public class VolumeHeaderTests
{
    [Fact]
    public void HeaderSize_Is41()
    {
        Assert.Equal(41, FormatConstants.HeaderSize);
        Assert.Equal(FormatConstants.HeaderSize,
            FormatConstants.MagicSize + 1 + 2 + 2 + FormatConstants.SaltSize + FormatConstants.NonceSize + 4);
        Assert.Equal(41, new VolumeHeader().ToBytes().Length);
    }

    [Fact]
    public void Magic_IsSARC()
    {
        Assert.Equal("SARC"u8.ToArray(), FormatConstants.Magic.ToArray());
    }

    [Fact]
    public void Write_ProducesExactBytes()
    {
        byte[] salt = Enumerable.Range(0x10, 16).Select(i => (byte)i).ToArray();
        byte[] nonce = Enumerable.Range(0x20, 12).Select(i => (byte)i).ToArray();
        var header = new VolumeHeader
        {
            VolumeNumber = 2,
            VolumeCount = 3,
            Salt = salt,
            Nonce = nonce,
            CiphertextLength = 0x00010203,
        };

        var expected = new List<byte> { 0x53, 0x41, 0x52, 0x43, 0x01, 0x00, 0x02, 0x00, 0x03 };
        expected.AddRange(salt);
        expected.AddRange(nonce);
        expected.AddRange(new byte[] { 0x00, 0x01, 0x02, 0x03 });

        Assert.Equal(expected.ToArray(), header.ToBytes());
    }

    [Fact]
    public void WriteThenParse_RoundTrips()
    {
        var header = new VolumeHeader
        {
            VolumeNumber = 1,
            VolumeCount = 4,
            Salt = TestData.RandomBytes(16, 1),
            Nonce = TestData.RandomBytes(12, 2),
            CiphertextLength = uint.MaxValue,
        };

        VolumeHeader parsed = VolumeHeader.Parse(header.ToBytes());

        Assert.Equal(header.Version, parsed.Version);
        Assert.Equal(header.VolumeNumber, parsed.VolumeNumber);
        Assert.Equal(header.VolumeCount, parsed.VolumeCount);
        Assert.Equal(header.Salt, parsed.Salt);
        Assert.Equal(header.Nonce, parsed.Nonce);
        Assert.Equal(header.CiphertextLength, parsed.CiphertextLength);
    }

    [Fact]
    public void Parse_BadMagic_E_MAGIC()
    {
        byte[] bytes = new VolumeHeader().ToBytes();
        bytes[0] = (byte)'X';
        TestData.AssertCode(ErrorCode.E_MAGIC, () => VolumeHeader.Parse(bytes));
    }

    [Fact]
    public void Parse_UnknownVersion_E_MAGIC()
    {
        byte[] bytes = new VolumeHeader().ToBytes();
        bytes[4] = 99;
        TestData.AssertCode(ErrorCode.E_MAGIC, () => VolumeHeader.Parse(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(40)]
    public void Parse_TooShort_E_LENGTH(int length)
    {
        byte[] bytes = new VolumeHeader().ToBytes().AsSpan(0, length).ToArray();
        TestData.AssertCode(ErrorCode.E_LENGTH, () => VolumeHeader.Parse(bytes));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    public void Parse_InconsistentVolumeNumbers_E_PAYLOAD(int number, int count)
    {
        byte[] bytes = new VolumeHeader { VolumeNumber = (ushort)number, VolumeCount = (ushort)count }.ToBytes();
        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => VolumeHeader.Parse(bytes));
    }
}
