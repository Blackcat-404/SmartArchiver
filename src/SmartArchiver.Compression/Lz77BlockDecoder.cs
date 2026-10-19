namespace SmartArchiver.Compression;

/// <summary>
/// Декодер блока LZ77+Huffman за FORMAT_SPEC_P2, розділ 3.
/// Вхід: coded_len байтів блока. Вихід: raw_len байтів.
/// </summary>
internal static class Lz77BlockDecoder
{
    public const int LitAlphabetSize = 257;  // 0…256
    public const int LenAlphabetSize = 256;  // 0…255
    public const int DistAlphabetSize = 16;   // 0…15
    public const int MatchMarker = 256;
    private const int MinMatch = 3;

    public static byte[] Decode(ReadOnlySpan<byte> coded, int rawLength)
    {
        if (rawLength < 0)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Некоректна довжина блока.");

        int pos = 0;

        // 1. Три таблиці (розділ 3.3).
        byte[] litLengths = HuffmanTableReader.Read(coded, ref pos, LitAlphabetSize);
        byte[] lenLengths = HuffmanTableReader.Read(coded, ref pos, LenAlphabetSize);
        byte[] distLengths = HuffmanTableReader.Read(coded, ref pos, DistAlphabetSize);

        // 2. token_count і stream_len (розділ 3.1).
        if (pos + 8 > coded.Length)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Блок обірвано перед token_count.");

        uint tokenCount = (uint)(coded[pos] << 24 | coded[pos + 1] << 16 |
                                 coded[pos + 2] << 8 | coded[pos + 3]);
        uint streamLen = (uint)(coded[pos + 4] << 24 | coded[pos + 5] << 16 |
                                 coded[pos + 6] << 8 | coded[pos + 7]);
        pos += 8;

        if (pos + (int)streamLen != coded.Length)
            throw new ArchiveException(ErrorCode.E_PAYLOAD,
                "stream_len не збігається з реальним розміром даних.");

        // 3. Декодери символів.
        var litDecoder = new HuffmanSymbolDecoder(litLengths);
        var lenDecoder = new HuffmanSymbolDecoder(lenLengths);
        var distDecoder = new HuffmanSymbolDecoder(distLengths);

        // 4. Бітовий потік.
        var reader = new BitReader(coded, pos);
        var output = new List<byte>(rawLength > 0 ? rawLength : 0);

        for (uint t = 0; t < tokenCount; t++)
        {
            int litSym = litDecoder.ReadSymbol(ref reader);

            if (litSym != MatchMarker)
            {
                output.Add((byte)litSym);
                continue;
            }

            // Посилання: LEN-символ.
            int lenSym = lenDecoder.ReadSymbol(ref reader);
            int length = lenSym + Lz77Decoder.MinMatch;

            // DIST-символ + extra біти (розділ 3.2).
            int distSym = distDecoder.ReadSymbol(ref reader);
            int distance = DecodeDistance(ref reader, distSym);

            // Перевірка меж. E_CODE за специфікацією розділу 5.
            if (distance > output.Count)
                throw new ArchiveException(ErrorCode.E_CODE,
                    "Посилання виходить за межі відновленого буфера.");

            AppendMatch(output, distance, length);
        }

        reader.EnsureOnlyPaddingLeft();

        if (output.Count != rawLength)
            throw new ArchiveException(ErrorCode.E_PAYLOAD,
                $"Розмір відновленого блока {output.Count} не збігається з raw_len {rawLength}.");

        return output.ToArray();
    }

    // Зворотне перетворення відстані за розділом 3.2.
    private static int DecodeDistance(ref BitReader reader, int code)
    {
        if (code == 0) return 1;
        if (code == 1) return 2;

        int extra = code - 1;
        uint extraBits = reader.ReadBits(extra);
        return (int)(((1u << (code - 1)) | extraBits) + 1);
    }

    // Побайтове копіювання (обов'язково: distance може бути менше length).
    private static void AppendMatch(List<byte> output, int distance, int length)
    {
        int start = output.Count - distance;
        for (int i = 0; i < length; i++)
            output.Add(output[start + i]);
    }
}