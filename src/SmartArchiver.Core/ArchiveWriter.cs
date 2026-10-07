namespace SmartArchiver.Core;

/// <summary>Вхідний файл для архівації: безпечне ім'я (без шляху) і вміст.</summary>
public sealed record ArchiveInput(string Name, byte[] Data, FileKind Kind = FileKind.Unknown);

/// <summary>
/// Запис одного тому без стиснення.
/// Том = заголовок(41) | payload. Payload = file_count(u32) | таблиця файлів | секція даних.
/// Секція даних = послідовність блоків: заголовок блока (15 байт) + дані.
/// Заголовок блока: file_index(u16) | block_index(u32) | raw_len(u32) | coded_len(u32) | стратегія(u8).
/// </summary>
public sealed class ArchiveWriter
{
    public int BlockSize { get; }

    public ArchiveWriter(int blockSize = FormatConstants.DefaultBlockSize)
    {
        if (blockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize), "Розмір блока має бути додатним.");
        BlockSize = blockSize;
    }

    public byte[] Build(IReadOnlyList<ArchiveInput> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count > FormatConstants.MaxFileCount)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Забагато файлів в одному архіві (максимум 65536).");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<FileTableEntry>(files.Count);
        using var data = new MemoryStream();

        for (int i = 0; i < files.Count; i++)
        {
            ArchiveInput file = files[i];
            SafeNames.EnsureSafe(file.Name);
            if (!names.Add(file.Name))
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Два файли з однаковим іменем в одному архіві.");
            if (!Enum.IsDefined(file.Kind))
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Невідомий тип файлу.");

            ulong offset = file.Data.Length == 0 ? 0UL : (ulong)data.Position;
            WriteBlocks(data, (ushort)i, file.Data);

            entries.Add(new FileTableEntry(
                file.Name,
                (ulong)file.Data.Length,
                Crc32.Compute(file.Data),
                file.Kind,
                CompressionStrategy.NoCompression,
                offset));
        }

        using var payload = new MemoryStream();
        BinaryIo.WriteU32(payload, (uint)entries.Count);
        foreach (FileTableEntry entry in entries)
        {
            entry.WriteTo(payload);
        }
        data.WriteTo(payload);

        if (payload.Length > uint.MaxValue)
            throw new ArchiveException(ErrorCode.E_LENGTH, "Payload завеликий для одного тому.");

        var header = new VolumeHeader { CiphertextLength = (uint)payload.Length };
        byte[] payloadBytes = payload.ToArray();
        var result = new byte[FormatConstants.HeaderSize + payloadBytes.Length];
        header.WriteTo(result);
        payloadBytes.CopyTo(result, FormatConstants.HeaderSize);
        return result;
    }

    /// <summary>Ріже вміст файлу на блоки по BlockSize. Порожній файл блоків не має.</summary>
    private void WriteBlocks(Stream data, ushort fileIndex, byte[] content)
    {
        uint blockIndex = 0;
        for (long pos = 0; pos < content.Length; pos += BlockSize)
        {
            int length = (int)Math.Min(BlockSize, content.Length - pos);

            BinaryIo.WriteU16(data, fileIndex);
            BinaryIo.WriteU32(data, blockIndex);
            BinaryIo.WriteU32(data, (uint)length);   // raw_len
            BinaryIo.WriteU32(data, (uint)length);   // coded_len: без стиснення збігається з raw_len
            data.WriteByte((byte)CompressionStrategy.NoCompression);
            data.Write(content, (int)pos, length);

            blockIndex++;
        }
    }
}
