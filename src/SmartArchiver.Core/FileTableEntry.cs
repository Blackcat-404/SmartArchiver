using System.Text;

namespace SmartArchiver.Core;

/// <summary>
/// Запис таблиці файлів:
/// name_len(u16) | ім'я UTF-8 | size(u64) | crc32(u32) | тип(u8) | стратегія(u8) | зміщення(u64).
/// Зміщення це позиція заголовка першого блока файлу, відлічена від початку секції даних тому.
/// Для порожнього файлу блоків немає, і зміщення не перевіряється (пишемо 0).
/// </summary>
public sealed record FileTableEntry(
    string Name,
    ulong Size,
    uint Crc32,
    FileKind Kind,
    CompressionStrategy Strategy,
    ulong Offset)
{
    internal void WriteTo(Stream s)
    {
        byte[] nameBytes = SafeNames.StrictUtf8.GetBytes(Name);
        BinaryIo.WriteU16(s, (ushort)nameBytes.Length);
        s.Write(nameBytes, 0, nameBytes.Length);
        BinaryIo.WriteU64(s, Size);
        BinaryIo.WriteU32(s, Crc32);
        s.WriteByte((byte)Kind);
        s.WriteByte((byte)Strategy);
        BinaryIo.WriteU64(s, Offset);
    }

    /// <summary>Читає запис і перевіряє: ім'я безпечне, тип і стратегія відомі, стратегія реалізована.</summary>
    internal static FileTableEntry ReadFrom(ByteReader r)
    {
        int nameLength = r.ReadU16();
        if (nameLength == 0 || nameLength > FormatConstants.MaxNameBytes)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Некоректна довжина імені файлу.");

        string name;
        try
        {
            name = SafeNames.StrictUtf8.GetString(r.ReadBytes(nameLength));
        }
        catch (DecoderFallbackException)
        {
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Ім'я файлу не є коректним UTF-8.");
        }
        SafeNames.EnsureSafe(name);

        ulong size = r.ReadU64();
        uint crc = r.ReadU32();
        byte kind = r.ReadU8();
        byte strategy = r.ReadU8();
        ulong offset = r.ReadU64();

        if (!Enum.IsDefined((FileKind)kind))
            throw new ArchiveException(ErrorCode.E_PAYLOAD, $"Невідомий тип файлу: {kind}.");
        if (!Enum.IsDefined((CompressionStrategy)strategy))
            throw new ArchiveException(ErrorCode.E_CODE, $"Невідома стратегія: {strategy}.");
        if ((CompressionStrategy)strategy != CompressionStrategy.NoCompression)
            throw new ArchiveException(ErrorCode.E_CODE, $"Стратегія {strategy} ще не підтримується.");

        return new FileTableEntry(name, size, crc, (FileKind)kind, (CompressionStrategy)strategy, offset);
    }
}
