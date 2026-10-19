namespace SmartArchiver.Compression;

/// <summary>
/// Стиснутий блок і його межі у вихідних даних.
/// Кожен блок має власні три таблиці Гаффмана всередині CodedBytes.
/// </summary>
internal sealed record EncodedLz77Block(int Offset, int RawLength, byte[] CodedBytes);

/// <summary>
/// ДП-розбиття потоку на блоки за фактичною вартістю LZ77 + Huffman.
/// Порівняння виконується з фіксованими блоками за однакових налаштувань.
/// </summary>
internal static class DpBlockSplitter
{
    // У таблиці блоків архіву запис одного блока займає 15 байтів.
    public const int DefaultBlockRecordBytes = 15;

    /// <summary>
    /// Знаходить найменшу сумарну вартість серед дозволених меж.
    /// Межі лежать на сітці boundaryStep; остання межа завжди дорівнює довжині даних.
    /// fixedBlockSize має бути кратним boundaryStep, щоб фіксоване розбиття
    /// гарантовано входило до множини допустимих варіантів.
    /// </summary>
    public static List<EncodedLz77Block> Split(
        byte[] data,
        int fixedBlockSize = 65_536,
        int boundaryStep = 16_384,
        int blockRecordBytes = DefaultBlockRecordBytes)
    {
        Validate(data, fixedBlockSize, boundaryStep, blockRecordBytes);
        if (data.Length == 0)
            return [];

        List<int> boundaries = GetBoundaries(data.Length, boundaryStep);
        int count = boundaries.Count;
        var cost = new long[count];
        var previous = new int[count];
        Array.Fill(cost, long.MaxValue);
        Array.Fill(previous, -1);
        cost[0] = 0;

        // F[j] = min_i (F[i] + T(i,j) + H(i,j)).
        // Encode вже містить бітовий потік, власні таблиці Huffman та службові поля.
        for (int j = 1; j < count; j++)
        {
            for (int i = j - 1; i >= 0; i--)
            {
                int length = boundaries[j] - boundaries[i];
                if (length > fixedBlockSize)
                    break;
                if (cost[i] == long.MaxValue)
                    continue;

                byte[] coded = EncodeRange(data, boundaries[i], length);
                long candidate = checked(cost[i] + coded.Length + blockRecordBytes);
                if (candidate < cost[j])
                {
                    cost[j] = candidate;
                    previous[j] = i;
                }
            }
        }

        if (previous[^1] < 0)
            throw new InvalidOperationException("Не вдалося знайти допустиме розбиття.");

        // Відновлюємо блоки; кожний кодуємо заново з власними таблицями.
        var result = new List<EncodedLz77Block>();
        for (int j = count - 1; j > 0; j = previous[j])
        {
            int i = previous[j];
            int offset = boundaries[i];
            int length = boundaries[j] - offset;
            result.Add(new EncodedLz77Block(offset, length, EncodeRange(data, offset, length)));
        }
        result.Reverse();
        return result;
    }

    /// <summary>Створює базове розбиття на фіксовані блоки для порівняння.</summary>
    public static List<EncodedLz77Block> SplitFixed(
        byte[] data, int fixedBlockSize = 65_536)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fixedBlockSize);

        var result = new List<EncodedLz77Block>();
        for (int offset = 0; offset < data.Length;)
        {
            int length = Math.Min(fixedBlockSize, data.Length - offset);
            result.Add(new EncodedLz77Block(offset, length, EncodeRange(data, offset, length)));
            offset += length;
        }
        return result;
    }

    /// <summary>Повний розмір закодованих блоків разом із записами таблиці блоків.</summary>
    public static long TotalBytes(
        IReadOnlyList<EncodedLz77Block> blocks,
        int blockRecordBytes = DefaultBlockRecordBytes)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentOutOfRangeException.ThrowIfNegative(blockRecordBytes);
        long total = 0;
        foreach (EncodedLz77Block block in blocks)
            total = checked(total + block.CodedBytes.Length + blockRecordBytes);
        return total;
    }

    private static byte[] EncodeRange(byte[] data, int offset, int length)
    {
        // Парсер працює з окремим блоком: посилання не переходять через його межі.
        byte[] block = data.AsSpan(offset, length).ToArray();
        return Lz77BlockEncoder.Encode(GreedyParser.Parse(block));
    }

    private static List<int> GetBoundaries(int length, int step)
    {
        var boundaries = new List<int> { 0 };
        for (long pos = step; pos < length; pos += step)
            boundaries.Add((int)pos);
        boundaries.Add(length);
        return boundaries;
    }

    private static void Validate(byte[] data, int blockSize, int step, int blockRecordBytes)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        ArgumentOutOfRangeException.ThrowIfNegative(blockRecordBytes);
        if (step > blockSize || blockSize % step != 0)
            throw new ArgumentException(
                "Розмір фіксованого блока має бути кратним кроку меж.", nameof(step));
    }
}
