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

        // Перевіряємо довжину без ризику переповнення int.
        if (streamLen != (uint)(coded.Length - pos))
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
                // Перевіряємо, чи не перевищено очікуваний розмір блока.
                if (output.Count >= rawLength)
                    throw new ArchiveException(ErrorCode.E_PAYLOAD,
                        "Кількість відновлених байтів перевищує raw_len.");

                output.Add((byte)litSym);
                continue;
            }

            // Посилання: LEN-символ.
            int lenSym = lenDecoder.ReadSymbol(ref reader);
            int length = lenSym + Lz77Decoder.MinMatch;

            // DIST-символ + extra біти (розділ 3.2).
            int distSym = distDecoder.ReadSymbol(ref reader);
            int distance = DecodeDistance(ref reader, distSym);

            // Перевіряємо, чи не виходить посилання за межі відновлених даних.
            if (distance > output.Count)
                throw new ArchiveException(ErrorCode.E_CODE,
                    "Посилання виходить за межі відновленого буфера.");

            // Перевіряємо, чи не перевищить збіг очікуваний розмір блока.
            if (length > rawLength - output.Count)
                throw new ArchiveException(ErrorCode.E_PAYLOAD,
                    "Довжина збігу перевищує межі вихідного блока.");

            // Використовуємо спільний метод копіювання LZ77 з перевірками.
            Lz77Decoder.AppendMatch(output, distance, length);
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

}