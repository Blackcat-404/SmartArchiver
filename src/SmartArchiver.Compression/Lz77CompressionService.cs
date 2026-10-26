namespace SmartArchiver.Compression;

/// <summary>Метод розбору LZ77 перед кодуванням Гаффмана.</summary>
public enum Lz77ParseMode
{
    Greedy,
    Lazy,
    Dp,
    Iterative,
    // Тимчасовий псевдонім для сумісності з кодом, де вже було IterativeDp.
    IterativeDp = Iterative
}

/// <summary>
/// Готовий блок для запису в архів. IsStored відповідає прапорцю NO_COMPRESSION.
/// Offset — зміщення в початковому файлі, RawLength — розмір до стиснення.
/// CodedBytes — байти, які потрібно записати у секцію даних блоку.
/// </summary>
public sealed record Lz77PreparedBlock(int Offset, int RawLength, byte[] CodedBytes, bool IsStored)
{
    public int CodedLength => CodedBytes.Length;
}

/// <summary>
/// Публічна межа між бібліотекою Compression та записувачем/читачем архіву.
/// Не залежить від CLI, БД та формату заголовка архіву.
/// </summary>
public static class Lz77CompressionService
{
    /// <summary>
    /// Кодує один непорожній блок у форматі LZ77 + три таблиці Гаффмана.
    /// Не додає заголовок блока архіву.
    /// </summary>
    public static byte[] EncodeBlock(byte[] data, Lz77ParseMode mode = Lz77ParseMode.Greedy)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            throw new ArgumentException("Порожній блок не кодується.", nameof(data));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        // IterativeParser вже повертає готові стиснені байти;
        // не повторюємо Huffman-кодування зайвий раз.
        if (mode == Lz77ParseMode.Iterative)
            return IterativeParser.Optimize(data).Encoded;

        IReadOnlyList<Lz77Token> tokens = mode switch
        {
            Lz77ParseMode.Greedy => GreedyParser.Parse(data),
            Lz77ParseMode.Lazy => LazyParser.Parse(data),
            Lz77ParseMode.Dp => DpParser.Parse(data),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return Lz77BlockEncoder.Encode(tokens);
    }

    /// <summary>Відновлює стиснутий блок до точної довжини rawLength.</summary>
    public static byte[] DecodeBlock(ReadOnlySpan<byte> coded, int rawLength) =>
        Lz77BlockDecoder.Decode(coded, rawLength);

    /// <summary>
    /// Створює незалежні блоки не більші за blockSize байтів.
    /// Якщо стиснення не вигідне, записує сирі байти без змін.
    /// </summary>
    public static IReadOnlyList<Lz77PreparedBlock> PrepareFixedBlocks(
        byte[] data,
        Lz77ParseMode mode = Lz77ParseMode.Greedy,
        int blockSize = 65_536,
        bool allowStored = true)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        var result = new List<Lz77PreparedBlock>();
        for (int offset = 0; offset < data.Length; offset += Math.Min(blockSize, data.Length - offset))
        {
            int length = Math.Min(blockSize, data.Length - offset);
            byte[] raw = data.AsSpan(offset, length).ToArray();
            byte[] coded = EncodeBlock(raw, mode);
            bool stored = allowStored && coded.Length >= raw.Length;
            result.Add(new Lz77PreparedBlock(offset, length, stored ? raw : coded, stored));
        }
        return result;
    }

    /// <summary>
    /// Підбирає межі методом DP. Поточний DpBlockSplitter використовує Greedy
    /// усередині кожного кандидата — це окремо від DP-розбору токенів.
    /// </summary>
    public static IReadOnlyList<Lz77PreparedBlock> PrepareDpBlocks(
        byte[] data,
        int blockSize = 65_536,
        int boundaryStep = 16_384,
        bool allowStored = true)
    {
        ArgumentNullException.ThrowIfNull(data);
        List<EncodedLz77Block> blocks = DpBlockSplitter.Split(data, blockSize, boundaryStep);
        var result = new List<Lz77PreparedBlock>(blocks.Count);
        foreach (EncodedLz77Block block in blocks)
        {
            byte[] coded = block.CodedBytes;
            bool stored = allowStored && coded.Length >= block.RawLength;
            if (stored)
                coded = data.AsSpan(block.Offset, block.RawLength).ToArray();
            result.Add(new Lz77PreparedBlock(block.Offset, block.RawLength, coded, stored));
        }
        return result;
    }

    /// <summary>Відновлює один підготовлений блок для перевірки інтеграції.</summary>
    public static byte[] RestorePreparedBlock(Lz77PreparedBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (block.RawLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(block));
        if (!block.IsStored)
            return DecodeBlock(block.CodedBytes, block.RawLength);
        if (block.CodedBytes.Length != block.RawLength)
            throw new ArgumentException("Розмір несжатого блока має дорівнювати rawLength.", nameof(block));
        return (byte[])block.CodedBytes.Clone();
    }
}
