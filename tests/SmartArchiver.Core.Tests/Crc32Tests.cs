using SmartArchiver.Core;

namespace SmartArchiver.Core.Tests;

public class Crc32Tests
{
    [Fact]
    public void KnownVector_123456789()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }

    [Fact]
    public void Empty_IsZero()
    {
        Assert.Equal(0u, Crc32.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Incremental_EqualsOneShot()
    {
        byte[] data = TestData.RandomBytes(1000, seed: 7);
        uint part = Crc32.Update(Crc32.Update(0, data.AsSpan(0, 400)), data.AsSpan(400));
        Assert.Equal(Crc32.Compute(data), part);
    }
}
