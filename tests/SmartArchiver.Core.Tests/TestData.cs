using System.Text;
using SmartArchiver.Core;

namespace SmartArchiver.Core.Tests;

internal static class TestData
{
    public static byte[] RandomBytes(int length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static ArchiveInput File(string name, byte[] data, FileKind kind = FileKind.Unknown) => new(name, data, kind);

    /// <summary>Очікуваний розмір тому з одним файлом: заголовок + file_count + запис таблиці + блоки з 15-байтовими заголовками + дані.</summary>
    public static int ExpectedLength(string name, int size, int blockSize)
    {
        int nameBytes = Encoding.UTF8.GetByteCount(name);
        int blocks = (size + blockSize - 1) / blockSize;
        int entry = 2 + nameBytes + 8 + 4 + 1 + 1 + 8;
        return FormatConstants.HeaderSize + 4 + entry + blocks * FormatConstants.BlockHeaderSize + size;
    }

    public static ArchiveException AssertCode(ErrorCode expected, Action action)
    {
        var ex = Assert.Throws<ArchiveException>(action);
        Assert.Equal(expected, ex.Code);
        return ex;
    }
}
