using System.Security.Cryptography;

namespace SmartArchiver.Archive;

/// <summary>RSA-OAEP-SHA256 envelope for sharing a random archive password without disclosing it.</summary>
public static class RecipientKeyExchange
{
    public static (string PublicKey, string PrivateKey) GenerateRecipientKeys()
    {
        using RSA rsa = RSA.Create(3072);
        return (rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    public static byte[] WrapSecret(ReadOnlySpan<byte> secret, string recipientPublicKey)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(recipientPublicKey);
        return rsa.Encrypt(secret.ToArray(), RSAEncryptionPadding.OaepSHA256);
    }

    public static byte[] UnwrapSecret(byte[] wrapped, string recipientPrivateKey)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(recipientPrivateKey);
        return rsa.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256);
    }

    public static (IReadOnlyList<byte[]> Volumes, byte[] WrappedSecret) PackForRecipient(
        IReadOnlyList<ArchiveInput> files, string recipientPublicKey, int blockSize = FormatConstants.DefaultBlockSize)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            string password = Convert.ToHexString(secret);
            return ([ProtectedArchive.BuildProtected(files, password, blockSize)], WrapSecret(secret, recipientPublicKey));
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public static IReadOnlyList<ExtractedFile> UnpackFromSender(
        IReadOnlyList<byte[]> volumes, byte[] wrappedSecret, string recipientPrivateKey)
    {
        byte[] secret = UnwrapSecret(wrappedSecret, recipientPrivateKey);
        try { return ProtectedArchive.ReadProtectedVolumes(volumes, Convert.ToHexString(secret)); }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
}
