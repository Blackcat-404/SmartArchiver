namespace SmartArchiver.Compression;

/// <summary>
/// Читач таблиці кодів Гаффмана за розділом 3.3 специфікації FORMAT_SPEC_P2.
/// Формат: nsym(uint16), далі nsym записів (symbol uint16, length uint8).
/// Символи мають бути за зростанням, довжини 1…15.
/// </summary>
internal static class HuffmanTableReader
{
    /// <summary>
    /// Читає таблицю і повертає масив довжин, індекс = символ.
    /// </summary>
    /// <param name="data">Увесь блок: читання починається з <paramref name="pos"/>,
    /// після повернення <paramref name="pos"/> вказує за останній прочитаний байт.</param>
    /// <param name="alphabetSize">Максимальний дозволений символ + 1 (257, 256 або 16).</param>
    public static byte[] Read(ReadOnlySpan<byte> data, ref int pos, int alphabetSize)
    {
        if (pos + 2 > data.Length)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Таблиця кодів обірвана.");

        int nsym = data[pos] << 8 | data[pos + 1];
        pos += 2;

        if (nsym > alphabetSize)
            throw new ArchiveException(ErrorCode.E_CODE,
                $"nsym={nsym} перевищує розмір алфавіту {alphabetSize}.");

        if (pos + nsym * 3 > data.Length)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Таблиця кодів обірвана.");

        var lengths = new byte[alphabetSize];
        int prevSymbol = -1;

        for (int i = 0; i < nsym; i++)
        {
            int symbol = data[pos] << 8 | data[pos + 1];
            byte length = data[pos + 2];
            pos += 3;

            if (symbol >= alphabetSize)
                throw new ArchiveException(ErrorCode.E_CODE,
                    $"Символ {symbol} виходить за межі алфавіту {alphabetSize}.");
            if (symbol <= prevSymbol)
                throw new ArchiveException(ErrorCode.E_CODE,
                    "Символи в таблиці мають зростати суворо.");
            if (length < 1 || length > CanonicalHuffman.MaxCodeLength)
                throw new ArchiveException(ErrorCode.E_CODE,
                    $"Довжина коду {length} для символу {symbol} поза допустимим діапазоном 1…15.");

            lengths[symbol] = length;
            prevSymbol = symbol;
        }

        // Перевірка Крафта (і вироджений один символ). nsym = 0 дає E_CODE через AssignCodes.
        CanonicalHuffman.AssignCodes(lengths);

        return lengths;
    }
}