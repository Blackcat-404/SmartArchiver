using SmartArchiver.Compression;

namespace SmartArchiver.Archive;

/// <summary>Файл, відновлений з архіву.</summary>
public sealed record ExtractedFile(string Name, byte[] Data, FileKind Kind, CompressionStrategy Strategy, uint Crc32);

/// <summary>
/// Читання архіву: одного тому (<see cref="Read"/>) або набору томів (<see cref="ReadVolumes"/>).
/// Блоки з NoCompression читаються як є, блоки з Lz77Huffman розпаковуються.
/// Будь-яке пошкодження дає ArchiveException з кодом помилки, інших винятків для некоректного входу бути не повинно.
/// </summary>
public static class ArchiveReader
{
    private sealed class FileState(FileTableEntry entry)
    {
        public FileTableEntry Entry { get; } = entry;
        public List<(uint Sequence, byte[] Data)> Chunks { get; } = [];
        public long Accumulated { get; set; }
    }

    private sealed record ParsedVolume(VolumeHeader Header, List<FileState> Files);

    /// <summary>Читає архів з одного тому. Багатотомний архів читається через <see cref="ReadVolumes"/>.</summary>
    public static IReadOnlyList<ExtractedFile> Read(byte[] archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        return Assemble([ParseVolume(archive, requireSingleVolume: true)]);
    }

    /// <summary>
    /// Читає набір томів у будь-якому порядку. Набір має бути повним і несуперечливим, інакше E_VOLUMES:
    /// кількість томів дорівнює volume_count, номери не повторюються, усі томи заявляють однакове volume_count.
    /// Блоки кожного файлу склеюються за block_seq, CRC32 перевіряється для файлу цілком.
    /// </summary>
    public static IReadOnlyList<ExtractedFile> ReadVolumes(IReadOnlyList<byte[]> volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        if (volumes.Count == 0)
            throw new ArchiveException(ErrorCode.E_VOLUMES, "Не передано жодного тому.");

        // Спочатку лише заголовки: набір томів перевіряється до розбору вмісту.
        var headers = new List<VolumeHeader>(volumes.Count);
        foreach (byte[] volume in volumes)
        {
            headers.Add(VolumeHeader.Parse(volume));
        }

        ushort count = headers[0].VolumeCount;
        var numbers = new HashSet<ushort>();
        foreach (VolumeHeader header in headers)
        {
            if (header.VolumeCount != count)
                throw new ArchiveException(ErrorCode.E_VOLUMES, "Томи належать різним архівам: різна кількість томів у заголовках.");
            if (!numbers.Add(header.VolumeIndex))
                throw new ArchiveException(ErrorCode.E_VOLUMES, $"Том з volume_index {header.VolumeIndex} передано двічі.");
        }
        if (volumes.Count != count)
            throw new ArchiveException(ErrorCode.E_VOLUMES, $"Архів має {count} том(ів), передано {volumes.Count}.");

        var parsed = new List<ParsedVolume>(volumes.Count);
        foreach (byte[] volume in volumes)
        {
            parsed.Add(ParseVolume(volume, requireSingleVolume: false));
        }
        parsed.Sort((a, b) => a.Header.VolumeIndex.CompareTo(b.Header.VolumeIndex));
        return Assemble(parsed);
    }

    private static ParsedVolume ParseVolume(byte[] archive, bool requireSingleVolume)
    {
        // 1. Заголовок і довжини, до будь-якого розбору payload.
        VolumeHeader header = VolumeHeader.Parse(archive);
        long expectedLength = FormatConstants.HeaderSize + (long)header.CiphertextLength;
        if (archive.LongLength != expectedLength)
            throw new ArchiveException(ErrorCode.E_LENGTH,
                $"Довжина архіву {archive.LongLength} не збігається з заголовком ({expectedLength}).");
        if (requireSingleVolume && header.VolumeCount != 1)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Багатотомні архіви читаються через ReadVolumes.");

        var reader = new ByteReader(archive, FormatConstants.HeaderSize, (int)header.CiphertextLength);

        // 2. Поля перед таблицею файлів: flags і reserved мають бути нульові (PREDICTION — розширення, яке тут не читається).
        int count = reader.ReadU16();
        if (reader.ReadU8() != 0)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Невідомі біти flags: цей розархіватор читає лише базовий формат.");
        if (reader.ReadU8() != 0)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Ненульове поле reserved.");
        if ((long)count * FormatConstants.MinFileEntrySize > reader.Remaining)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Кількість файлів не відповідає розміру payload.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var states = new List<FileState>(count);
        for (int i = 0; i < count; i++)
        {
            FileTableEntry entry = FileTableEntry.ReadFrom(reader);
            if (!names.Add(entry.Name))
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Два файли з однаковим іменем в архіві.");
            states.Add(new FileState(entry));
        }

        // 3. Таблиця блоків: спершу всі записи, потім дані блоків підряд у тому самому порядку.
        int blockCount = reader.ReadU16();
        if ((long)blockCount * FormatConstants.BlockHeaderSize > reader.Remaining)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Кількість блоків не відповідає розміру payload.");
        var records = new List<(FileState File, uint Sequence, uint RawLength, uint CodedLength, bool Stored)>(blockCount);
        long dataLength = 0;
        for (int i = 0; i < blockCount; i++)
        {
            ushort fileIndex = reader.ReadU16();
            uint sequence = reader.ReadU32();
            uint rawLength = reader.ReadU32();
            uint codedLength = reader.ReadU32();
            byte flags = reader.ReadU8();

            if (fileIndex >= states.Count)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блок посилається на неіснуючий файл.");
            if ((flags & ~FormatConstants.NoCompressionFlag) != 0)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, $"Невідомі біти block_flags: 0x{flags:X2}.");
            bool stored = (flags & FormatConstants.NoCompressionFlag) != 0;
            FileState state = states[fileIndex];
            if (state.Entry.Strategy == CompressionStrategy.NoCompression && !stored)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Файл позначено як нестиснений, а блок стиснуто.");
            if (rawLength == 0 || rawLength > FormatConstants.MaxBlockRawLength)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Некоректний raw_len блока.");
            if (stored && codedLength != rawLength)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блок NO_COMPRESSION має coded_len, відмінний від raw_len.");
            if ((ulong)(state.Accumulated + rawLength) > state.Entry.Size)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блоки файлу більші за заявлений розмір.");
            state.Accumulated += rawLength;
            dataLength += codedLength;
            records.Add((state, sequence, rawLength, codedLength, stored));
        }

        // Довжини секцій мають точно збігатися: зайві байти після даних блоків неприпустимі.
        if (dataLength != reader.Remaining)
            throw new ArchiveException(ErrorCode.E_PAYLOAD,
                $"Дані блоків займають {dataLength} байт, а після таблиці блоків лишилося {reader.Remaining}.");

        // 4. Дані блоків.
        foreach ((FileState file, uint sequence, uint rawLength, uint codedLength, bool stored) in records)
        {
            ReadOnlySpan<byte> coded = reader.ReadBytes((int)codedLength);
            file.Chunks.Add((sequence, stored ? coded.ToArray() : Decode(coded, (int)rawLength)));
        }
        return new ParsedVolume(header, states);
    }

    /// <summary>Розпаковує блок. Будь-який збій декодера стає контрольованою помилкою E_CODE.</summary>
    private static byte[] Decode(ReadOnlySpan<byte> coded, int rawLength)
    {
        try
        {
            return Lz77CompressionService.DecodeBlock(coded, rawLength);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException
                                       or ArithmeticException or InvalidDataException or KeyNotFoundException)
        {
            throw new ArchiveException(ErrorCode.E_CODE, $"Блок не вдалося розпакувати: {ex.Message}");
        }
    }

    /// <summary>Склеює блоки файлів з усіх томів у порядку block_seq і перевіряє розмір та CRC32 кожного файлу.</summary>
    private static List<ExtractedFile> Assemble(List<ParsedVolume> volumes)
    {
        var order = new List<FileTableEntry>();
        var chunks = new Dictionary<string, List<(uint Sequence, byte[] Data)>>(StringComparer.Ordinal);
        var entries = new Dictionary<string, FileTableEntry>(StringComparer.Ordinal);

        foreach (ParsedVolume volume in volumes)
        {
            foreach (FileState state in volume.Files)
            {
                FileTableEntry entry = state.Entry;
                if (entries.TryGetValue(entry.Name, out FileTableEntry? known))
                {
                    // Той самий файл у кількох томах: розмір, CRC, тип і стратегія мають збігатися.
                    if (known.Size != entry.Size || known.Crc32 != entry.Crc32 || known.Kind != entry.Kind || known.Strategy != entry.Strategy)
                        throw new ArchiveException(ErrorCode.E_PAYLOAD, "Томи по-різному описують один і той самий файл.");
                }
                else
                {
                    entries[entry.Name] = entry;
                    chunks[entry.Name] = [];
                    order.Add(entry);
                }
                chunks[entry.Name].AddRange(state.Chunks);
            }
        }

        var result = new List<ExtractedFile>(order.Count);
        foreach (FileTableEntry entry in order)
        {
            List<(uint Sequence, byte[] Data)> parts = chunks[entry.Name];
            parts.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

            // block_seq: суцільний ряд від 0 без пропусків і повторів.
            long total = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Sequence != (uint)i)
                    throw new ArchiveException(ErrorCode.E_PAYLOAD, "Номери блоків файлу не утворюють суцільного ряду від 0.");
                total += parts[i].Data.Length;
            }
            if ((ulong)total != entry.Size)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Сума блоків не дорівнює розміру файлу в таблиці.");

            // Виділяємо пам'ять лише тепер: total уже збігся з розміром із таблиці й складений із реально прочитаних блоків.
            var data = new byte[total];
            long position = 0;
            foreach ((uint _, byte[] part) in parts)
            {
                part.CopyTo(data, position);
                position += part.Length;
            }

            if (Crc32.Compute(data) != entry.Crc32)
                throw new ArchiveException(ErrorCode.E_CRC, "CRC32 файлу не збігається.");

            result.Add(new ExtractedFile(entry.Name, data, entry.Kind, entry.Strategy, entry.Crc32));
        }
        return result;
    }
}
