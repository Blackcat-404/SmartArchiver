using System.Buffers.Binary;
using System.Security.Cryptography;
using SmartArchiver.Security;

namespace SmartArchiver.Archive;

/// <summary>
/// Glue between the SARC volume format and the primitives in <see cref="ArchiveSecurity"/>.
/// Moved without changes from ArchiveSecurity (author: Oleksandr) so that Security does not depend on the format.
/// </summary>
public static class ProtectedArchive
{
    /// <summary>
    /// Ключі з пароля й солі. PBKDF2 (100 000 ітерацій) дорогий, а FORMAT_SPEC_P2 (4.1) вимагає виконувати його
    /// один раз на архів: сіль однакова для всіх томів, тож ключі теж. Інакше час t і компонента T метрики Q спотворюються.
    /// </summary>
    private sealed class KeyCache(string password) : IDisposable
    {
        private readonly Dictionary<string, (byte[] Enc, byte[] Mac)> _keys = new();

        public (byte[] Enc, byte[] Mac) For(byte[] salt)
        {
            string id = Convert.ToHexString(salt);
            if (!_keys.TryGetValue(id, out (byte[] Enc, byte[] Mac) keys))
            {
                var (enc, mac) = ArchiveSecurity.DeriveKeys(password, salt);
                _keys[id] = keys = (enc, mac);
            }
            return keys;
        }

        public void Dispose()
        {
            foreach ((byte[] enc, byte[] mac) in _keys.Values)
            {
                CryptographicOperations.ZeroMemory(enc);
                CryptographicOperations.ZeroMemory(mac);
            }
        }
    }

    /// <summary>Шифрує payload архіву, зберігаючи 41-байтовий заголовок у відкритому вигляді.</summary>
    public static byte[] Protect(byte[] plainArchive, string password)
    {
        ArgumentNullException.ThrowIfNull(plainArchive);
        ArgumentNullException.ThrowIfNull(password);
        byte[] salt = RandomNumberGenerator.GetBytes(FormatConstants.SaltSize);
        using var keys = new KeyCache(password);
        return Seal(plainArchive, salt, RandomNumberGenerator.GetBytes(FormatConstants.NonceSize), keys.For(salt));
    }

    /// <summary>
    /// Шифрує payload тому заданими ключами й додає HMAC: заголовок(41) | шифротекст | тег(32).
    /// <paramref name="padTo"/> &gt; 0 вмикає командне розширення «вирівнювання»: payload з префіксом довжини
    /// доповнюється до кратного розміру. Такий том читається лише з явним <c>padded: true</c>.
    /// </summary>
    private static byte[] Seal(byte[] plainArchive, byte[] salt, byte[] nonce, (byte[] Enc, byte[] Mac) keys, int padTo = 0)
    {
        VolumeHeader original = VolumeHeader.Parse(plainArchive);
        if (plainArchive.LongLength != FormatConstants.HeaderSize + (long)original.CiphertextLength)
            throw new ArchiveException(ErrorCode.E_LENGTH, "Некоректна довжина вихідного архіву.");
        byte[] payload = plainArchive.AsSpan(FormatConstants.HeaderSize).ToArray();
        if (padTo > 0)
        {
            if (padTo < 16 || padTo > 1_048_576)
                throw new ArgumentOutOfRangeException(nameof(padTo));
            // The original length goes inside the ciphertext, so the padded size reveals nothing.
            byte[] padded = new byte[checked(((payload.Length + 4 + padTo - 1) / padTo) * padTo)];
            BinaryPrimitives.WriteInt32BigEndian(padded, payload.Length);
            payload.CopyTo(padded, 4);
            RandomNumberGenerator.Fill(padded.AsSpan(payload.Length + 4));
            CryptographicOperations.ZeroMemory(payload);
            payload = padded;
        }
        byte[] cipher = ArchiveSecurity.TransformCtr(payload, keys.Enc, nonce);
        CryptographicOperations.ZeroMemory(payload);
        var header = new VolumeHeader
        {
            VolumeIndex = original.VolumeIndex,
            VolumeCount = original.VolumeCount,
            Salt = salt,
            Nonce = nonce,
            CiphertextLength = checked((uint)cipher.Length)
        };
        byte[] result = new byte[checked(FormatConstants.HeaderSize + cipher.Length + ArchiveSecurity.TagSize)];
        header.WriteTo(result);
        cipher.CopyTo(result, FormatConstants.HeaderSize);
        byte[] tag = ArchiveSecurity.ComputeTag(result.AsSpan(0, result.Length - ArchiveSecurity.TagSize), keys.Mac);
        tag.CopyTo(result, result.Length - ArchiveSecurity.TagSize);
        return result;
    }

    /// <summary>Перевіряє HMAC до дешифрування та повертає архів звичайного формату для ArchiveReader.</summary>
    public static byte[] Unprotect(byte[] protectedArchive, string password, bool padded = false)
    {
        ArgumentNullException.ThrowIfNull(protectedArchive);
        ArgumentNullException.ThrowIfNull(password);
        using var keys = new KeyCache(password);
        return Open(protectedArchive, keys, padded);
    }

    /// <summary>Кроки 1–3 розділу 4.3: довжина не менша за 73 байти, потім magic і версія.</summary>
    private static VolumeHeader ParseHeader(byte[] volume)
    {
        if (volume.Length < FormatConstants.MinVolumeSize)
            throw new ArchiveException(ErrorCode.E_SHORT, $"Том коротший за {FormatConstants.MinVolumeSize} байти.");
        return VolumeHeader.Parse(volume);
    }

    private static byte[] Open(byte[] protectedArchive, KeyCache keyCache, bool padded)
    {
        VolumeHeader header = ParseHeader(protectedArchive);
        long expectedLength = FormatConstants.HeaderSize + (long)header.CiphertextLength + ArchiveSecurity.TagSize;
        if (protectedArchive.LongLength != expectedLength)
            throw new ArchiveException(ErrorCode.E_LENGTH, "Невірна довжина захищеного архіву.");
        var (encKey, macKey) = keyCache.For(header.Salt);
        int authenticatedLength = protectedArchive.Length - ArchiveSecurity.TagSize;
        if (!ArchiveSecurity.VerifyTag(protectedArchive.AsSpan(0, authenticatedLength),
                protectedArchive.AsSpan(authenticatedLength, ArchiveSecurity.TagSize), macKey))
            throw new ArchiveException(ErrorCode.E_HMAC, "Неправильний пароль або пошкоджений архів.");
        byte[] plain = ArchiveSecurity.TransformCtr(protectedArchive.AsSpan(FormatConstants.HeaderSize,
            checked((int)header.CiphertextLength)), encKey, header.Nonce);
        if (padded)
        {
            if (plain.Length < 4) throw new ArchiveException(ErrorCode.E_PAYLOAD, "Пошкоджений padding.");
            int originalLength = BinaryPrimitives.ReadInt32BigEndian(plain);
            if (originalLength < 0 || originalLength > plain.Length - 4)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Некоректна довжина padded payload.");
            byte[] unpadded = plain.AsSpan(4, originalLength).ToArray();
            CryptographicOperations.ZeroMemory(plain);
            plain = unpadded;
        }
        var originalHeader = new VolumeHeader
        {
            VolumeIndex = header.VolumeIndex,
            VolumeCount = header.VolumeCount,
            CiphertextLength = checked((uint)plain.Length)
        };
        byte[] result = new byte[FormatConstants.HeaderSize + plain.Length];
        originalHeader.WriteTo(result);
        plain.CopyTo(result, FormatConstants.HeaderSize);
        return result;
    }

    /// <summary>Будує і шифрує архів. З аналізатором файли стискаються за його стратегією (див. <see cref="ArchiveWriter"/>).</summary>
    public static byte[] BuildProtected(IReadOnlyList<ArchiveInput> files, string password,
        int blockSize = FormatConstants.DefaultBlockSize, IFileAnalyzer? analyzer = null) =>
        Protect(new ArchiveWriter(blockSize, analyzer).Build(files), password);

    /// <summary>
    /// Шифрує томи одного архіву: одна сіль і один PBKDF2 на всі томи, а nonce = 8 випадкових байтів архіву
    /// || номер тому (uint32 big-endian), як рекомендує FORMAT_SPEC_P2 (4.2). Nonce тому не повторюється за побудовою.
    /// </summary>
    public static IReadOnlyList<byte[]> ProtectVolumes(IReadOnlyList<byte[]> volumes, string password,
        int padTo = 0) =>
        ProtectVolumes(volumes, password, RandomNumberGenerator.GetBytes(FormatConstants.SaltSize),
            RandomNumberGenerator.GetBytes(FormatConstants.NonceSize - sizeof(uint)), padTo);

    /// <summary>
    /// Те саме із заданими сіллю й префіксом nonce. Фіксовані значення допустимі лише в контрольних прикладах
    /// (FORMAT_SPEC_P2, 4.2), тому метод не публічний.
    /// </summary>
    internal static IReadOnlyList<byte[]> ProtectVolumes(IReadOnlyList<byte[]> volumes, string password,
        byte[] salt, byte[] noncePrefix, int padTo = 0)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(password);
        if (volumes.Count == 0 || volumes.Count > ushort.MaxValue)
            throw new ArchiveException(ErrorCode.E_VOLUMES, "Некоректна кількість томів.");
        if (salt.Length != FormatConstants.SaltSize || noncePrefix.Length != FormatConstants.NonceSize - sizeof(uint))
            throw new ArgumentException("Сіль має 16 байт, префікс nonce 8 байт.", nameof(salt));
        using var keys = new KeyCache(password);
        var result = new List<byte[]>(volumes.Count);
        for (int i = 0; i < volumes.Count; i++)
        {
            VolumeHeader original = VolumeHeader.Parse(volumes[i]);
            if (original.VolumeIndex != i || original.VolumeCount != volumes.Count)
                throw new ArchiveException(ErrorCode.E_VOLUMES, "Невірна послідовність томів.");
            byte[] nonce = new byte[FormatConstants.NonceSize];
            noncePrefix.CopyTo(nonce, 0);
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(noncePrefix.Length), (uint)i);
            result.Add(Seal(volumes[i], salt, nonce, keys.For(salt), padTo));
        }
        return result;
    }

    public static IReadOnlyList<byte[]> BuildProtectedVolumes(IReadOnlyList<ArchiveInput> files,
        string password, VolumeOptions options, int blockSize = FormatConstants.DefaultBlockSize,
        IFileAnalyzer? analyzer = null, int padTo = 0) =>
        ProtectVolumes(new ArchiveWriter(blockSize, analyzer).BuildVolumes(files, options), password, padTo);

    /// <summary>Розділ 4.3: довжина, magic, HMAC до розшифрування, потім розбір і склеювання томів.</summary>
    /// <param name="padded">Томи записано з командним розширенням «вирівнювання» (<c>padTo</c>).</param>
    public static IReadOnlyList<ExtractedFile> ReadProtectedVolumes(IReadOnlyList<byte[]> volumes, string password,
        bool padded = false)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(password);
        if (volumes.Count == 0)
            throw new ArchiveException(ErrorCode.E_VOLUMES, "Немає томів.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (byte[] volume in volumes)
        {
            VolumeHeader header = ParseHeader(volume);
            string identity = Convert.ToHexString(header.Salt) + Convert.ToHexString(header.Nonce);
            if (!seen.Add(identity))
                throw new ArchiveException(ErrorCode.E_VOLUMES, "Повторне використання salt та nonce.");
        }
        using var keys = new KeyCache(password);
        return ArchiveReader.ReadVolumes(volumes.Select(v => Open(v, keys, padded)).ToArray());
    }

    public static IReadOnlyList<ExtractedFile> ReadProtected(byte[] archive, string password, bool padded = false) =>
        ArchiveReader.Read(Unprotect(archive, password, padded));
}
