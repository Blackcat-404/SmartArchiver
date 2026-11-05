using System.Buffers.Binary;

namespace SmartArchiver.Archive;

/// <summary>
/// 41-байтовий відкритий заголовок тому (big-endian):
/// 0..4 magic "SARC" | 4 version | 5..7 volume_index (з 0) | 7..9 volume_count |
/// 9..25 salt | 25..37 nonce | 37..41 ciphertext_len (FORMAT_SPEC_P2, 1.1).
/// У незашифрованому томі (всередині тракту, до шифрування) сіль і nonce нульові, а ciphertext_len це довжина payload.
/// </summary>
public sealed class VolumeHeader
{
    private const int OffVersion = 4;
    private const int OffIndex = 5;
    private const int OffCount = 7;
    private const int OffSalt = 9;
    private const int OffNonce = 25;
    private const int OffLength = 37;

    public byte Version { get; init; } = FormatConstants.Version;
    /// <summary>volume_index: номер тому від 0.</summary>
    public ushort VolumeIndex { get; init; }
    public ushort VolumeCount { get; init; } = 1;
    public byte[] Salt { get; init; } = new byte[FormatConstants.SaltSize];
    public byte[] Nonce { get; init; } = new byte[FormatConstants.NonceSize];
    public uint CiphertextLength { get; init; }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < FormatConstants.HeaderSize)
            throw new ArgumentException("Буфер менший за заголовок.", nameof(destination));
        if (Salt.Length != FormatConstants.SaltSize || Nonce.Length != FormatConstants.NonceSize)
            throw new InvalidOperationException("Сіль має бути 16 байт, nonce 12 байт.");

        FormatConstants.Magic.CopyTo(destination);
        destination[OffVersion] = Version;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(OffIndex, 2), VolumeIndex);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(OffCount, 2), VolumeCount);
        Salt.AsSpan().CopyTo(destination.Slice(OffSalt, FormatConstants.SaltSize));
        Nonce.AsSpan().CopyTo(destination.Slice(OffNonce, FormatConstants.NonceSize));
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(OffLength, 4), CiphertextLength);
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[FormatConstants.HeaderSize];
        WriteTo(bytes);
        return bytes;
    }

    /// <summary>Читає й перевіряє заголовок. Додаткові байти після перших 41 ігноруються (їх перевіряє виклик).</summary>
    public static VolumeHeader Parse(ReadOnlySpan<byte> source)
    {
        if (source.Length < FormatConstants.HeaderSize)
            throw new ArchiveException(ErrorCode.E_LENGTH, "Архів коротший за 41-байтовий заголовок.");

        if (!source[..FormatConstants.MagicSize].SequenceEqual(FormatConstants.Magic))
            throw new ArchiveException(ErrorCode.E_MAGIC, "Невірний MAGIC.");

        byte version = source[OffVersion];
        if (version != FormatConstants.Version)
            throw new ArchiveException(ErrorCode.E_MAGIC, $"Непідтримувана версія формату: {version}.");

        ushort index = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(OffIndex, 2));
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(OffCount, 2));
        if (index >= count)
            throw new ArchiveException(ErrorCode.E_VOLUMES, $"volume_index {index} не менший за volume_count {count}.");

        return new VolumeHeader
        {
            Version = version,
            VolumeIndex = index,
            VolumeCount = count,
            Salt = source.Slice(OffSalt, FormatConstants.SaltSize).ToArray(),
            Nonce = source.Slice(OffNonce, FormatConstants.NonceSize).ToArray(),
            CiphertextLength = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(OffLength, 4)),
        };
    }
}
