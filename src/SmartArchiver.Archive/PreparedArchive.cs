namespace SmartArchiver.Archive;

/// <summary>
/// Блок файлу після аналізу й стиснення, готовий до запису в том.
/// <see cref="IsStored"/> означає, що в томі лежать сирі байти (NO_COMPRESSION), а coded_len = raw_len.
/// </summary>
/// <param name="Sequence">block_seq: номер блока в межах файлу, з нуля.</param>
/// <param name="RawLength">raw_len: розмір до стиснення.</param>
/// <param name="CodedBytes">Рівно ті байти, що йдуть у секцію даних тому.</param>
/// <param name="IsStored">Блок записано як є, бо стиснення не дало виграшу.</param>
public sealed record PreparedBlock(int Sequence, int RawLength, byte[] CodedBytes, bool IsStored)
{
    /// <summary>coded_len.</summary>
    public int CodedLength => CodedBytes.Length;

    /// <summary>Стратегія в заголовку блока.</summary>
    public CompressionStrategy Strategy => IsStored ? CompressionStrategy.NoCompression : CompressionStrategy.Lz77Huffman;

    /// <summary>Вага блока в рюкзаку: coded_len + 15 байт заголовка блока.</summary>
    public int Weight => CodedBytes.Length + FormatConstants.BlockHeaderSize;
}

/// <summary>
/// Файл після аналізу: тип і стратегія вирішено, блоки закодовані, розміри відомі.
/// Саме за цими розмірами пакувальник розкладає блоки по томах.
/// </summary>
/// <param name="Index">Позиція файлу у вхідному списку.</param>
/// <param name="Size">Розмір вихідного файлу цілком.</param>
/// <param name="Crc32">CRC32 вихідного файлу цілком.</param>
/// <param name="Strategy">
/// Стратегія файлу. Lz77Huffman лише тоді, коли хоч один блок справді стиснуто; інакше NoCompression.
/// </param>
public sealed record PreparedFile(
    int Index,
    string Name,
    ulong Size,
    uint Crc32,
    FileKind Kind,
    CompressionStrategy Strategy,
    IReadOnlyList<PreparedBlock> Blocks)
{
    /// <summary>Розмір запису цього файлу в таблиці файлів тому.</summary>
    public int TableEntrySize => FormatConstants.MinFileEntrySize + SafeNames.StrictUtf8.GetByteCount(Name);

    /// <summary>Сума coded_len усіх блоків: скільки файл займає в архіві без службових заголовків.</summary>
    public long CodedSize
    {
        get
        {
            long total = 0;
            foreach (PreparedBlock block in Blocks)
            {
                total += block.CodedLength;
            }
            return total;
        }
    }
}
