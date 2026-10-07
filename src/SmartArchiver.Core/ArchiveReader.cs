namespace SmartArchiver.Core;

/// <summary>Файл, відновлений з архіву.</summary>
public sealed record ExtractedFile(string Name, byte[] Data, FileKind Kind, CompressionStrategy Strategy, uint Crc32);

/// <summary>
/// Читання одного тому без стиснення. Будь-яке пошкодження дає ArchiveException з кодом помилки,
/// інших винятків для некоректного входу бути не повинно.
/// </summary>
public static class ArchiveReader
{
    private sealed class FileState
    {
        public FileState(FileTableEntry entry) => Entry = entry;

        public FileTableEntry Entry { get; }
        public MemoryStream? Content { get; set; }
        public uint NextBlock { get; set; }
        public long Accumulated { get; set; }
        public long FirstBlockOffset { get; set; }
    }

    public static IReadOnlyList<ExtractedFile> Read(byte[] archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        // 1. Заголовок і довжини, до будь-якого розбору payload.
        VolumeHeader header = VolumeHeader.Parse(archive);
        long expectedLength = FormatConstants.HeaderSize + (long)header.CiphertextLength;
        if (archive.LongLength != expectedLength)
            throw new ArchiveException(ErrorCode.E_LENGTH,
                $"Довжина архіву {archive.LongLength} не збігається з заголовком ({expectedLength}).");
        if (header.VolumeCount != 1)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Багатотомні архіви читаються окремо (задача «Багатотомність»).");

        var reader = new ByteReader(archive, FormatConstants.HeaderSize, (int)header.CiphertextLength);

        // 2. Таблиця файлів.
        uint count = reader.ReadU32();
        if (count > FormatConstants.MaxFileCount || (long)count * FormatConstants.MinFileEntrySize > reader.Remaining)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Кількість файлів не відповідає розміру payload.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var states = new List<FileState>((int)count);
        for (uint i = 0; i < count; i++)
        {
            FileTableEntry entry = FileTableEntry.ReadFrom(reader);
            if (!names.Add(entry.Name))
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Два файли з однаковим іменем в архіві.");
            states.Add(new FileState(entry));
        }

        // 3. Секція даних: блоки до кінця payload.
        long dataStart = reader.Position;
        while (reader.Remaining > 0)
        {
            long blockOffset = reader.Position - dataStart;
            ushort fileIndex = reader.ReadU16();
            uint blockIndex = reader.ReadU32();
            uint rawLength = reader.ReadU32();
            uint codedLength = reader.ReadU32();
            byte strategy = reader.ReadU8();

            if (fileIndex >= states.Count)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блок посилається на неіснуючий файл.");
            if (!Enum.IsDefined((CompressionStrategy)strategy))
                throw new ArchiveException(ErrorCode.E_CODE, $"Невідома стратегія блока: {strategy}.");
            if ((CompressionStrategy)strategy != CompressionStrategy.NoCompression)
                throw new ArchiveException(ErrorCode.E_CODE, $"Стратегія блока {strategy} ще не підтримується.");
            if (rawLength == 0 || codedLength != rawLength)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Некоректні розміри блока.");

            FileState state = states[fileIndex];
            if (blockIndex != state.NextBlock)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блоки файлу йдуть не по порядку.");
            if (rawLength > reader.Remaining)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блок виходить за межі payload.");
            if ((ulong)(state.Accumulated + rawLength) > state.Entry.Size)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блоки файлу більші за заявлений розмір.");

            if (blockIndex == 0)
                state.FirstBlockOffset = blockOffset;
            state.Content ??= new MemoryStream();
            state.Content.Write(reader.ReadBytes((int)rawLength));
            state.Accumulated += rawLength;
            state.NextBlock++;
        }

        // 4. Зведення: розмір, зміщення, CRC32.
        var result = new List<ExtractedFile>(states.Count);
        foreach (FileState state in states)
        {
            FileTableEntry entry = state.Entry;
            if ((ulong)state.Accumulated != entry.Size)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Сума блоків не дорівнює розміру файлу в таблиці.");
            if (entry.Size > 0 && entry.Offset != (ulong)state.FirstBlockOffset)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Зміщення в таблиці не вказує на перший блок файлу.");

            byte[] data = state.Content?.ToArray() ?? Array.Empty<byte>();
            if (Crc32.Compute(data) != entry.Crc32)
                throw new ArchiveException(ErrorCode.E_CRC, "CRC32 файлу не збігається.");

            result.Add(new ExtractedFile(entry.Name, data, entry.Kind, entry.Strategy, entry.Crc32));
        }
        return result;
    }
}
