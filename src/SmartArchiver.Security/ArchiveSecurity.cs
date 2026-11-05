using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SmartArchiver.Security;

/// <summary>
/// Захист SARC для CP1: PBKDF2-HMAC-SHA256, AES-256-CTR, HMAC-SHA256.
/// Поточне узгодження: 12-байтовий nonce || 32-бітний big-endian лічильник (починається з 0);
/// тег HMAC(заголовок || ciphertext) додається після ciphertext.
/// Остаточну розкладку слід звірити з FORMAT_SPEC_P2.md перед читанням еталонів.
/// </summary>
public static class ArchiveSecurity
{
    public const int Iterations = 100_000;
    public const int KeySize = 32;
    public const int TagSize = 32;
    public const int SaltSize = 16;
    public const int NonceSize = 12;

    public static (byte[] EncryptionKey, byte[] AuthenticationKey) DeriveKeys(
        string password, ReadOnlySpan<byte> salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (salt.Length != SaltSize)
            throw new ArgumentException("Сіль має бути 16 байт.", nameof(salt));
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations,
            HashAlgorithmName.SHA256, 2 * KeySize);
        try
        {
            return (derived[..KeySize], derived[KeySize..]);
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    /// <summary>
    /// AES-CTR — однакова операція для шифрування та розшифрування.
    /// Лічильний блок i: nonce (12 байтів) || uint32_BE(i), i від 1 (FORMAT_SPEC_P2, 4.2).
    /// </summary>
    public static byte[] TransformCtr(ReadOnlySpan<byte> input, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce)
    {
        if (key.Length != KeySize) throw new ArgumentException("AES-256 потребує 32-байтовий ключ.", nameof(key));
        if (nonce.Length != NonceSize)
            throw new ArgumentException("Nonce має бути 12 байт.", nameof(nonce));
        long blocks = ((long)input.Length + 15) / 16;
        if (blocks > uint.MaxValue) throw new ArgumentException("CTR лічильник переповниться.", nameof(input));

        byte[] result = new byte[input.Length];
        Span<byte> counter = stackalloc byte[16];
        Span<byte> streamBlock = stackalloc byte[16];
        nonce.CopyTo(counter);
        byte[] keyCopy = key.ToArray();
        using var aes = Aes.Create();
        try
        {
            aes.Key = keyCopy;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            for (long block = 0; block < blocks; block++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(counter[12..], (uint)(block + 1));
                aes.EncryptEcb(counter, streamBlock, PaddingMode.None);
                int offset = checked((int)(block * 16));
                int take = Math.Min(16, input.Length - offset);
                for (int i = 0; i < take; i++) result[offset + i] = (byte)(input[offset + i] ^ streamBlock[i]);
            }
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
            CryptographicOperations.ZeroMemory(counter);
            CryptographicOperations.ZeroMemory(streamBlock);
        }
    }

    public static byte[] ComputeTag(ReadOnlySpan<byte> message, ReadOnlySpan<byte> authenticationKey)
    {
        if (authenticationKey.Length != KeySize)
            throw new ArgumentException("HMAC ключ має бути 32 байти.", nameof(authenticationKey));
        return HMACSHA256.HashData(authenticationKey, message);
    }

    public static bool VerifyTag(ReadOnlySpan<byte> message, ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> authenticationKey)
    {
        if (tag.Length != TagSize) return false;
        byte[] expected = ComputeTag(message, authenticationKey);
        try { return CryptographicOperations.FixedTimeEquals(expected, tag); }
        finally { CryptographicOperations.ZeroMemory(expected); }
    }
}
