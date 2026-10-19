using System.Security.Cryptography;

namespace SmartArchiver.Security.Tests;

public sealed class ArchiveSecurityTests
{
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
