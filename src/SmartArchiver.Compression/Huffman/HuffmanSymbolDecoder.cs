namespace SmartArchiver.Compression;

/// <summary>
/// Декодер символів Гаффмана для довільного алфавіту. Потрібен для LIT (257), LEN (256), DIST (16).
/// На відміну від HuffmanCodec, не прив'язаний до 256 символів.
/// </summary>
internal sealed class HuffmanSymbolDecoder
{
    private readonly int[] _count;
    private readonly int[] _symbols;

    public HuffmanSymbolDecoder(ReadOnlySpan<byte> lengths)
    {
        // Перевірка через AssignCodes кидає E_CODE на некоректній таблиці.
        CanonicalHuffman.AssignCodes(lengths);

        _count = new int[CanonicalHuffman.MaxCodeLength + 1];
        foreach (byte len in lengths)
            _count[len]++;
        _count[0] = 0;

        var offset = new int[CanonicalHuffman.MaxCodeLength + 2];
        for (int len = 1; len <= CanonicalHuffman.MaxCodeLength; len++)
            offset[len + 1] = offset[len] + _count[len];

        _symbols = new int[lengths.Length];
        var next = (int[])offset.Clone();
        for (int s = 0; s < lengths.Length; s++)
        {
            int len = lengths[s];
            if (len != 0)
                _symbols[next[len]++] = s;
        }
    }

    /// <summary>
    /// Читає один символ з бітового потоку. Некоректний код дає E_CODE (специфікація, розділ 5).
    /// </summary>
    public int ReadSymbol(ref BitReader reader)
    {
        int code = 0, first = 0, index = 0;
        for (int len = 1; len <= CanonicalHuffman.MaxCodeLength; len++)
        {
            code |= reader.ReadBit();
            int c = _count[len];
            if (code < first + c)
                return _symbols[index + (code - first)];
            index += c;
            first = (first + c) << 1;
            code <<= 1;
        }
        throw new ArchiveException(ErrorCode.E_CODE, "Некоректний код у потоці.");
    }
}