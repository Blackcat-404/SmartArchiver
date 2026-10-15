namespace SmartArchiver.Compression;

/// <summary>
/// Канонічні коди Гаффмана з масиву довжин. Кодер і декодер використовують одну функцію,
/// тому їхні коди гарантовано збігаються. Некоректна таблиця дає ArchiveException(E_CODE).
/// Це ключовий крок між побудованим деревом Гаффмана і реальним записом бітів у архів.
/// Автор: Артем.
/// </summary>
internal static class CanonicalHuffman
{
    public const int MaxCodeLength = 15;

    /// <summary>
    /// Формує канонічний код для кожного символу на основі таблиці довжин.
    /// </summary>
    /// <param name="lengths">Довжина коду для кожного символу. 0 означає, що символ не використовується.</param>
    /// <returns>Код для кожного символу. Для символів з довжиною 0 код не має значення і дорівнює 0.</returns>
    public static uint[] AssignCodes(ReadOnlySpan<byte> lengths)
    {
        var count = new int[MaxCodeLength + 1];
        int used = 0;
        int onlyLength = 0;

        for (int symbol = 0; symbol < lengths.Length; symbol++)
        {
            int len = lengths[symbol];
            if (len > MaxCodeLength)
                throw new ArchiveException(ErrorCode.E_CODE,
                    $"Довжина коду {len} для символу {symbol} перевищує {MaxCodeLength}.");
            if (len == 0)
                continue;

            count[len]++;
            used++;
            onlyLength = len;
        }

        if (used == 0)
            throw new ArchiveException(ErrorCode.E_CODE, "Таблиця кодів порожня.");

        if (used == 1)
        {
            if (onlyLength != 1)
                throw new ArchiveException(ErrorCode.E_CODE, "Для одного символу довжина коду має бути 1.");
        }
        else
        {
            long kraft = 0;
            for (int len = 1; len <= MaxCodeLength; len++)
                kraft += (long)count[len] << (MaxCodeLength - len);

            if (kraft != 1L << MaxCodeLength)
                throw new ArchiveException(ErrorCode.E_CODE, "Порушена рівність Крафта.");
        }

        var nextCode = new uint[MaxCodeLength + 1];
        uint code = 0;
        for (int bits = 1; bits <= MaxCodeLength; bits++)
        {
            code = (code + (uint)count[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        var codes = new uint[lengths.Length];
        for (int symbol = 0; symbol < lengths.Length; symbol++)
        {
            int len = lengths[symbol];
            if (len != 0)
                codes[symbol] = nextCode[len]++;
        }

        return codes;
    }
}