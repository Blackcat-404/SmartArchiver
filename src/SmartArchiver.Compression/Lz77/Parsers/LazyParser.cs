namespace SmartArchiver.Compression;

/// <summary>
/// Лінивий жадібний розбір LZ77: порівнює збіг у поточній позиції
/// зі збігом у наступній позиції та за потреби відкладає посилання.
/// </summary>
internal static class LazyParser
{
    // Мінімальна перевага наступного збігу для вибору літерала.
    public const int DefaultMinLazyGain = 2;

    public static List<Lz77Token> Parse(
        byte[] block,
        int maxChain = MatchFinder.DefaultMaxChain,
        int minLazyGain = DefaultMinLazyGain)
    {
        ArgumentNullException.ThrowIfNull(block);

        // Перевіряємо, що глибина пошуку є додатною.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChain);

        // Перевіряємо, що мінімальний приріст довжини є додатним.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minLazyGain);

        var finder = new MatchFinder(block, maxChain);
        var tokens = new List<Lz77Token>();

        int pos = 0;
        while (pos < block.Length)
        {
            // Шукаємо збіг до додавання поточної позиції в хеш-ланцюжок.
            if (!finder.TryLongest(pos, out int distance, out int length))
            {
                tokens.Add(Lz77Token.Lit(block[pos]));
                finder.Insert(pos);
                pos++;
                continue;
            }

            // Поточний байт стане доступним як історія для пошуку в pos + 1.
            // Додаємо його рівно один раз незалежно від прийнятого рішення.
            finder.Insert(pos);

            bool useLiteral = false;
            if (pos + 1 < block.Length &&
                finder.TryLongest(pos + 1, out _, out int nextLength))
            {
                // Порівнюємо без додавання, щоб уникнути переповнення int.
                useLiteral = nextLength > length &&
                             nextLength - length >= minLazyGain;
            }

            if (useLiteral)
            {
                // Відкладаємо посилання: наступна ітерація обробить pos + 1.
                tokens.Add(Lz77Token.Lit(block[pos]));
                pos++;
                continue;
            }

            tokens.Add(Lz77Token.Match(distance, length));

            // Позиція pos вже додана перед переглядом наступного збігу.
            // Додаємо решту позицій, покритих поточним посиланням.
            for (int k = 1; k < length; k++)
                finder.Insert(pos + k);

            pos += length;
        }

        return tokens;
    }
}
