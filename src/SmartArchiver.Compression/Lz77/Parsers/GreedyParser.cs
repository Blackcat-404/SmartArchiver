namespace SmartArchiver.Compression;

/// <summary>
/// Жадібний розбір LZ77: на кожній позиції вибирає найдовший
/// знайдений збіг або записує поточний байт як літерал.
/// </summary>
internal static class GreedyParser
{
    public static List<Lz77Token> Parse(
        byte[] block,
        int maxChain = MatchFinder.DefaultMaxChain)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (maxChain <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChain),
                "Глибина пошуку має бути додатною.");

        var finder = new MatchFinder(block, maxChain);
        var tokens = new List<Lz77Token>();
        int pos = 0;

        while (pos < block.Length)
        {
            // Шукаємо серед попередніх позицій, не додаючи поточну.
            if (finder.TryLongest(pos, out int distance, out int length))
            {
                tokens.Add(Lz77Token.Match(distance, length));

                // Пропущені позиції теж потрібні для наступних пошуків.
                for (int k = 0; k < length; k++)
                    finder.Insert(pos + k);

                pos += length;
            }
            else
            {
                // Якщо збіг коротший за 3 байти, записуємо літерал.
                tokens.Add(Lz77Token.Lit(block[pos]));
                finder.Insert(pos);
                pos++;
            }
        }

        return tokens;
    }
}
