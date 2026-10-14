using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SmartArchiver.Core;

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

    public static (byte[] EncryptionKey, byte[] AuthenticationKey) DeriveKeys(
        string password, ReadOnlySpan<byte> salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (salt.Length != FormatConstants.SaltSize)
            throw new ArgumentException("Сіль має бути 16 байт.", nameof(salt));
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations,
            HashAlgorithmName.SHA256, 2 * KeySize);
        try
        {
            return (derived[..KeySize], derived[KeySize..]);
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    /// <summary>AES-CTR — однакова операція для шифрування та розшифрування.</summary>
    public static byte[] TransformCtr(ReadOnlySpan<byte> input, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce)
    {
        if (key.Length != KeySize) throw new ArgumentException("AES-256 потребує 32-байтовий ключ.", nameof(key));
        if (nonce.Length != FormatConstants.NonceSize)
            throw new ArgumentException("Nonce має бути 12 байт.", nameof(nonce));
        long blocks = ((long)input.Length + 15) / 16;
        if (blocks > (long)uint.MaxValue + 1) throw new ArgumentException("CTR лічильник переповниться.", nameof(input));

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
                BinaryPrimitives.WriteUInt32BigEndian(counter[12..], (uint)block);
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

    /// <summary>Шифрує payload архіву, зберігаючи 41-байтовий заголовок у відкритому вигляді.</summary>
    public static byte[] Protect(byte[] plainArchive, string password)
    {
        ArgumentNullException.ThrowIfNull(plainArchive);
        ArgumentNullException.ThrowIfNull(password);
        VolumeHeader original = VolumeHeader.Parse(plainArchive);
        if (original.VolumeCount != 1)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Захист багатотомності підключається окремо.");
        if (plainArchive.LongLength != FormatConstants.HeaderSize + (long)original.CiphertextLength)
            throw new ArchiveException(ErrorCode.E_LENGTH, "Некоректна довжина вихідного архіву.");
        byte[] salt = RandomNumberGenerator.GetBytes(FormatConstants.SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(FormatConstants.NonceSize);
        var (encKey, macKey) = DeriveKeys(password, salt);
        try
        {
            byte[] cipher = TransformCtr(plainArchive.AsSpan(FormatConstants.HeaderSize), encKey, nonce);
            var header = new VolumeHeader
            {
                VolumeNumber = original.VolumeNumber,
                VolumeCount = original.VolumeCount,
                Salt = salt,
                Nonce = nonce,
                CiphertextLength = checked((uint)cipher.Length)
            };
            byte[] result = new byte[checked(FormatConstants.HeaderSize + cipher.Length + TagSize)];
            header.WriteTo(result);
            cipher.CopyTo(result, FormatConstants.HeaderSize);
            byte[] tag = ComputeTag(result.AsSpan(0, result.Length - TagSize), macKey);
            tag.CopyTo(result, result.Length - TagSize);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    /// <summary>Перевіряє HMAC до дешифрування та повертає архів звичайного формату для ArchiveReader.</summary>
    public static byte[] Unprotect(byte[] protectedArchive, string password)
    {
        ArgumentNullException.ThrowIfNull(protectedArchive);
        ArgumentNullException.ThrowIfNull(password);
        VolumeHeader header = VolumeHeader.Parse(protectedArchive);
        long expectedLength = FormatConstants.HeaderSize + (long)header.CiphertextLength + TagSize;
        if (protectedArchive.LongLength != expectedLength)
            throw new ArchiveException(ErrorCode.E_LENGTH, "Невірна довжина захищеного архіву.");
        var (encKey, macKey) = DeriveKeys(password, header.Salt);
        try
        {
            int authenticatedLength = protectedArchive.Length - TagSize;
            if (!VerifyTag(protectedArchive.AsSpan(0, authenticatedLength),
                    protectedArchive.AsSpan(authenticatedLength, TagSize), macKey))
                throw new ArchiveException(ErrorCode.E_HMAC, "Неправильний пароль або пошкоджений архів.");
            byte[] plain = TransformCtr(protectedArchive.AsSpan(FormatConstants.HeaderSize,
                checked((int)header.CiphertextLength)), encKey, header.Nonce);
            var originalHeader = new VolumeHeader
            {
                VolumeNumber = header.VolumeNumber,
                VolumeCount = header.VolumeCount,
                CiphertextLength = header.CiphertextLength
            };
            byte[] result = new byte[FormatConstants.HeaderSize + plain.Length];
            originalHeader.WriteTo(result);
            plain.CopyTo(result, FormatConstants.HeaderSize);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    public static byte[] BuildProtected(IReadOnlyList<ArchiveInput> files, string password,
        int blockSize = FormatConstants.DefaultBlockSize) =>
        Protect(new ArchiveWriter(blockSize).Build(files), password);

    public static IReadOnlyList<ExtractedFile> ReadProtected(byte[] archive, string password) =>
        ArchiveReader.Read(Unprotect(archive, password));
}
