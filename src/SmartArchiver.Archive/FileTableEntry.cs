using System.Text;

namespace SmartArchiver.Archive;

/// <summary>
/// Запис таблиці файлів (FORMAT_SPEC_P2, 2.2):
/// name_len(u16) | ім'я UTF-8 | orig_size(u32) | crc32(u32) | file_type(u8) | strategy(u8).
/// orig_size і crc32 стосуються цілого файлу, навіть якщо його блоки лежать у різних томах.
/// </summary>
public sealed record FileTableEntry(
    string Name,
    ulong Size,
    uint Crc32,
    FileKind Kind,
    CompressionStrategy Strategy)
{
    internal void WriteTo(Stream s)
    {
        if (Size > FormatConstants.MaxFileSize)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Файл більший за 4 ГіБ: orig_size має лише 4 байти.");
        if ((byte)Kind > FormatConstants.MaxFileType)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, $"file_type має бути від 0 до 6, а не {(byte)Kind}.");
        byte[] nameBytes = SafeNames.StrictUtf8.GetBytes(Name);
        BinaryIo.WriteU16(s, (ushort)nameBytes.Length);
        s.Write(nameBytes, 0, nameBytes.Length);
        BinaryIo.WriteU32(s, (uint)Size);
        BinaryIo.WriteU32(s, Crc32);
        s.WriteByte((byte)Kind);
        s.WriteByte((byte)Strategy);
    }

    /// <summary>Читає запис і перевіряє: ім'я безпечне, file_type від 0 до 6, стратегія відома.</summary>
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

        uint size = r.ReadU32();
        uint crc = r.ReadU32();
        byte kind = r.ReadU8();
        byte strategy = r.ReadU8();

        if (kind > FormatConstants.MaxFileType)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, $"Невідомий тип файлу: {kind}.");
        if (!Enum.IsDefined((CompressionStrategy)strategy))
            throw new ArchiveException(ErrorCode.E_CODE, $"Невідома стратегія: {strategy}.");

        return new FileTableEntry(name, size, crc, (FileKind)kind, (CompressionStrategy)strategy);
    }
}
