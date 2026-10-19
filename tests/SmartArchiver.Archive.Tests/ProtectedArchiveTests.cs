using System.Text;
using SmartArchiver.Core;

namespace SmartArchiver.Archive.Tests;

public sealed class ProtectedArchiveTests
{
    [Fact]
    public void ProtectedArchive_RoundTrip_IsByteExact()
    {
        var files = new[]
        {
            new ArchiveInput("a.txt", Encoding.UTF8.GetBytes("Привіт, SmartArchiver!")),
            new ArchiveInput("empty.bin", Array.Empty<byte>()),
            new ArchiveInput("binary.bin", Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray())
        };
        byte[] encrypted = ProtectedArchive.BuildProtected(files, "сильний пароль для тесту", 127);
        var restored = ProtectedArchive.ReadProtected(encrypted, "сильний пароль для тесту");
        Assert.Equal(files.Length, restored.Count);
        for (int i = 0; i < files.Length; i++)
        {
            Assert.Equal(files[i].Name, restored[i].Name);
            Assert.Equal(files[i].Data, restored[i].Data);
        }
    }

    [Fact]
    public void WrongPassword_RejectsWithHmacError()
    {
        byte[] data = ProtectedArchive.BuildProtected([new ArchiveInput("test", [1, 2, 3])], "correct");
        Assert.Equal(ErrorCode.E_HMAC, Assert.Throws<ArchiveException>(() =>
            ProtectedArchive.ReadProtected(data, "wrong")).Code);
    }

    [Theory]
    [InlineData(9)]   // salt
    [InlineData(25)]  // nonce
    [InlineData(41)]  // ciphertext
    [InlineData(-1)]  // authentication tag
    public void ModifiedAuthenticatedData_RejectsWithHmacError(int index)
    {
        byte[] data = ProtectedArchive.BuildProtected([new ArchiveInput("test", [1, 2, 3])], "secret");
        data[index < 0 ? data.Length - 1 : index] ^= 1;
        Assert.Equal(ErrorCode.E_HMAC, Assert.Throws<ArchiveException>(() =>
            ProtectedArchive.ReadProtected(data, "secret")).Code);
    }

    [Fact]
    public void TruncatedProtectedArchive_IsRejected()
    {
        byte[] data = ProtectedArchive.BuildProtected([new ArchiveInput("x", [5])], "secret");
        Assert.Equal(ErrorCode.E_LENGTH, Assert.Throws<ArchiveException>(() =>
            ProtectedArchive.ReadProtected(data[..^1], "secret")).Code);
    }
}
