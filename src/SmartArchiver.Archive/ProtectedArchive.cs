using System.Security.Cryptography;
using SmartArchiver.Security;

namespace SmartArchiver.Archive;

/// <summary>
/// Glue between the SARC volume format and the primitives in <see cref="ArchiveSecurity"/>.
/// Moved without changes from ArchiveSecurity (author: Oleksandr) so that Security does not depend on the format.
/// </summary>
public static class ProtectedArchive
{
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
        var (encKey, macKey) = ArchiveSecurity.DeriveKeys(password, salt);
        try
        {
            byte[] cipher = ArchiveSecurity.TransformCtr(plainArchive.AsSpan(FormatConstants.HeaderSize), encKey, nonce);
            var header = new VolumeHeader
            {
                VolumeNumber = original.VolumeNumber,
                VolumeCount = original.VolumeCount,
                Salt = salt,
                Nonce = nonce,
                CiphertextLength = checked((uint)cipher.Length)
            };
            byte[] result = new byte[checked(FormatConstants.HeaderSize + cipher.Length + ArchiveSecurity.TagSize)];
            header.WriteTo(result);
            cipher.CopyTo(result, FormatConstants.HeaderSize);
            byte[] tag = ArchiveSecurity.ComputeTag(result.AsSpan(0, result.Length - ArchiveSecurity.TagSize), macKey);
            tag.CopyTo(result, result.Length - ArchiveSecurity.TagSize);
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
        long expectedLength = FormatConstants.HeaderSize + (long)header.CiphertextLength + ArchiveSecurity.TagSize;
        if (protectedArchive.LongLength != expectedLength)
            throw new ArchiveException(ErrorCode.E_LENGTH, "Невірна довжина захищеного архіву.");
        var (encKey, macKey) = ArchiveSecurity.DeriveKeys(password, header.Salt);
        try
        {
            int authenticatedLength = protectedArchive.Length - ArchiveSecurity.TagSize;
            if (!ArchiveSecurity.VerifyTag(protectedArchive.AsSpan(0, authenticatedLength),
                    protectedArchive.AsSpan(authenticatedLength, ArchiveSecurity.TagSize), macKey))
                throw new ArchiveException(ErrorCode.E_HMAC, "Неправильний пароль або пошкоджений архів.");
            byte[] plain = ArchiveSecurity.TransformCtr(protectedArchive.AsSpan(FormatConstants.HeaderSize,
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
