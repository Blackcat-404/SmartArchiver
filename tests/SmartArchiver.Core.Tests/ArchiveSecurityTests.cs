using System.Security.Cryptography;
using System.Text;
using SmartArchiver.Core;

namespace SmartArchiver.Core.Tests;

public sealed class ArchiveSecurityTests
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
        byte[] encrypted = ArchiveSecurity.BuildProtected(files, "сильний пароль для тесту", 127);
        var restored = ArchiveSecurity.ReadProtected(encrypted, "сильний пароль для тесту");
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
        byte[] data = ArchiveSecurity.BuildProtected([new ArchiveInput("test", [1, 2, 3])], "correct");
        Assert.Equal(ErrorCode.E_HMAC, Assert.Throws<ArchiveException>(() =>
            ArchiveSecurity.ReadProtected(data, "wrong")).Code);
    }

    [Theory]
    [InlineData(9)]   // salt
    [InlineData(25)]  // nonce
    [InlineData(41)]  // ciphertext
    [InlineData(-1)]  // authentication tag
    public void ModifiedAuthenticatedData_RejectsWithHmacError(int index)
    {
        byte[] data = ArchiveSecurity.BuildProtected([new ArchiveInput("test", [1, 2, 3])], "secret");
        data[index < 0 ? data.Length - 1 : index] ^= 1;
        Assert.Equal(ErrorCode.E_HMAC, Assert.Throws<ArchiveException>(() =>
            ArchiveSecurity.ReadProtected(data, "secret")).Code);
    }

    [Fact]
    public void TruncatedProtectedArchive_IsRejected()
    {
        byte[] data = ArchiveSecurity.BuildProtected([new ArchiveInput("x", [5])], "secret");
        Assert.Equal(ErrorCode.E_LENGTH, Assert.Throws<ArchiveException>(() =>
            ArchiveSecurity.ReadProtected(data[..^1], "secret")).Code);
    }

    [Fact]
    public void Ctr_EncryptDecryptSymmetric()
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] nonce = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        byte[] source = RandomNumberGenerator.GetBytes(1003);
        byte[] cipher = ArchiveSecurity.TransformCtr(source, key, nonce);
        Assert.NotEqual(source, cipher);
        Assert.Equal(source, ArchiveSecurity.TransformCtr(cipher, key, nonce));
    }

    [Fact]
    public void KeyDerivation_IsDeterministic_AndSeparated()
    {
        byte[] salt = new byte[16];
        var first = ArchiveSecurity.DeriveKeys("password", salt);
        var second = ArchiveSecurity.DeriveKeys("password", salt);
        Assert.Equal(32, first.EncryptionKey.Length);
        Assert.Equal(32, first.AuthenticationKey.Length);
        Assert.Equal(first.EncryptionKey, second.EncryptionKey);
        Assert.Equal(first.AuthenticationKey, second.AuthenticationKey);
        Assert.NotEqual(first.EncryptionKey, first.AuthenticationKey);
    }
}
