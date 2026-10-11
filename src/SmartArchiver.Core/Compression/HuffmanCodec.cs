namespace SmartArchiver.Core;

/// <summary>
/// Кодер і декодер Гаффмана для одного блока.
/// ASSUMPTION: формат блока = таблиця довжин (256 байтів, по одному на символ) + біти коду.
/// Звірити з FORMAT_SPEC_P2.md, коли він з'явиться.
/// Цей клас є точкою входу для стискання та розпакування одного блоку даних перед записом
/// у архів і після читання з нього.
/// Автор: Артем.
/// </summary>
internal static class HuffmanCodec
{
    public const int TableSize = 256;

    /// <summary>
    /// Стискає один блок даних у формат: 256 байтів довжин + бітовий потік Кодування.
    /// Блок повинен бути непорожнім, бо в коді не передбачено способу зберігати порожній вміст.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> raw)
    {
        if (raw.Length == 0)
            throw new ArgumentException("Порожній блок не кодується.", nameof(raw));

        var freq = new long[TableSize];
        foreach (byte b in raw)
            freq[b]++;

        byte[] lengths = HuffmanLengths.Build(freq);
        uint[] codes = CanonicalHuffman.AssignCodes(lengths);

        using var output = new MemoryStream();
        output.Write(lengths, 0, TableSize);

        var bits = new BitWriter(output);
        foreach (byte b in raw)
            bits.WriteBits(codes[b], lengths[b]);
        bits.Flush();

        return output.ToArray();
    }

    /// <summary>
    /// Розкодовує блок, якщо відомо реальну довжину оригінальних даних.
    /// Сама таблиця довжин зберігається на початку, а для перевірки використовуються канонічні коди.
    /// </summary>
    public static byte[] Decode(ReadOnlySpan<byte> coded, int rawLength)
    {
        if (rawLength <= 0)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Некоректна довжина блока.");
        if (coded.Length < TableSize)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блок коротший за таблицю довжин.");

        ReadOnlySpan<byte> lengths = coded[..TableSize];
        CanonicalHuffman.AssignCodes(lengths); // перевірка таблиці, кидає E_CODE

        var count = new int[CanonicalHuffman.MaxCodeLength + 1];
        foreach (byte len in lengths)
            count[len]++;
        count[0] = 0;

        var offset = new int[CanonicalHuffman.MaxCodeLength + 2];
        for (int len = 1; len <= CanonicalHuffman.MaxCodeLength; len++)
            offset[len + 1] = offset[len] + count[len];

        var symbols = new int[TableSize];
        var next = (int[])offset.Clone();
        for (int s = 0; s < TableSize; s++)
        {
            int len = lengths[s];
            if (len != 0)
                symbols[next[len]++] = s;
        }

        var reader = new BitReader(coded, TableSize);
        var output = new byte[rawLength];
        for (int i = 0; i < rawLength; i++)
            output[i] = (byte)DecodeSymbol(ref reader, count, symbols);

        reader.EnsureOnlyPaddingLeft();
        return output;
    }

    /// <summary>
    /// Зчитує один символ з бітового потоку, використовуючи порядковий індекс в таблиці символів.
    /// </summary>
    private static int DecodeSymbol(ref BitReader reader, int[] count, int[] symbols)
    {
        int code = 0;
        int first = 0;
        int index = 0;

        for (int len = 1; len <= CanonicalHuffman.MaxCodeLength; len++)
        {
            code |= reader.ReadBit();
            int c = count[len];
            if (code < first + c)
                return symbols[index + (code - first)];

            index += c;
            first = (first + c) << 1;
            code <<= 1;
        }

        throw new ArchiveException(ErrorCode.E_PAYLOAD, "Некоректний код у потоці.");
    }
}