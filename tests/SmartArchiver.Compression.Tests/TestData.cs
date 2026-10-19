using SmartArchiver.Core;

namespace SmartArchiver.Compression.Tests;

internal static class TestData
{
    public static byte[] RandomBytes(int length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static ArchiveException AssertCode(ErrorCode expected, Action action)
    {
        var ex = Assert.Throws<ArchiveException>(action);
        Assert.Equal(expected, ex.Code);
        return ex;
    }
}
