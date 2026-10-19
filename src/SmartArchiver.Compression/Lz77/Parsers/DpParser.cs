using System.Numerics;

namespace SmartArchiver.Compression;

/// <summary>
/// Оптимальний розбір LZ77 за фіксованою моделлю вартості зі специфікації CP2.
/// Розглядає збіги в тому самому хеш-ланцюжку, що й GreedyParser.
/// </summary>
internal static class DpParser
{
    public const int LiteralCost = 9;
    public const int MatchBaseCost = 17;

    /// <summary>Повертає вартість одного посилання у бітах за фіксованою моделлю.</summary>
    internal static int MatchCost(int distance)
    {
        if (distance is < 1 or > Lz77Limits.Window)
            throw new ArgumentOutOfRangeException(nameof(distance));

        // bit_length(distance - 1) - 1 = floor(log2(distance - 1)) для distance > 1.
        int extra = distance <= 2 ? 0 : BitOperations.Log2((uint)(distance - 1));
        return MatchBaseCost + extra;
    }

    /// <summary>Обчислює модельну вартість готового потоку токенів.</summary>
    internal static long CalculateCost(IEnumerable<Lz77Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        long cost = 0;
        foreach (Lz77Token token in tokens)
            cost += token.IsMatch ? MatchCost(token.Distance) : LiteralCost;
        return cost;
    }

    public static List<Lz77Token> Parse(byte[] block, int maxChain = MatchFinder.DefaultMaxChain)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChain);

        int n = block.Length;
        var result = new List<Lz77Token>();
        if (n == 0)
            return result;

        // Будуємо ті самі хеш-ланцюжки з трьох байтів, що використовує Greedy.
        const int hashBits = 16;
        var heads = new int[1 << hashBits];
        var previous = new int[n];
        Array.Fill(heads, -1);
        Array.Fill(previous, -1);

        for (int pos = 0; pos + 2 < n; pos++)
        {
            int hash = Hash(block, pos, hashBits);
            previous[pos] = heads[hash];
            heads[hash] = pos;
        }

        // cost[i] — мінімальна вартість кодування суфікса, починаючи з i.
        var cost = new long[n + 1];
        var chosenDistance = new int[n];
        var chosenLength = new int[n];

        // Для кожного класу вартості відстані достатньо знати найдовший збіг:
        // він дозволяє вибрати будь-яку коротшу довжину з тією самою вартістю.
        var maxLengthByExtra = new int[16];
        var distanceByExtra = new int[16];
        var minSuffixCost = new long[Lz77Limits.MaxMatch + 1];
        var minSuffixLength = new int[Lz77Limits.MaxMatch + 1];

        for (int pos = n - 1; pos >= 0; pos--)
        {
            long bestCost = LiteralCost + cost[pos + 1];
            int bestDistance = 0;
            int bestLength = 1;
            int longest = Math.Min(Lz77Limits.MaxMatch, n - pos);

            if (longest >= Lz77Limits.MinMatch)
            {
                Array.Clear(maxLengthByExtra);
                Array.Clear(distanceByExtra);

                int hash = Hash(block, pos, hashBits);
                int candidate = previous[pos];
                int checkedCount = 0;

                while (candidate >= 0 && checkedCount++ < maxChain)
                {
                    int distance = pos - candidate;
                    if (distance > Lz77Limits.Window)
                        break;

                    int extra = MatchCost(distance) - MatchBaseCost;
                    if (maxLengthByExtra[extra] < longest)
                    {
                        int length = 0;
                        while (length < longest && block[candidate + length] == block[pos + length])
                            length++;

                        if (length > maxLengthByExtra[extra])
                        {
                            maxLengthByExtra[extra] = length;
                            distanceByExtra[extra] = distance;
                        }
                    }

                    candidate = previous[candidate];
                }

                // Префіксні мінімуми серед cost[pos + length].
                // Вони дозволяють перевіряти всі довжини збігу за O(1) на клас.
                for (int length = Lz77Limits.MinMatch; length <= longest; length++)
                {
                    long current = cost[pos + length];
                    if (length == Lz77Limits.MinMatch || current <= minSuffixCost[length - 1])
                    {
                        minSuffixCost[length] = current;
                        minSuffixLength[length] = length;
                    }
                    else
                    {
                        minSuffixCost[length] = minSuffixCost[length - 1];
                        minSuffixLength[length] = minSuffixLength[length - 1];
                    }
                }

                for (int extra = 0; extra < maxLengthByExtra.Length; extra++)
                {
                    int availableLength = maxLengthByExtra[extra];
                    if (availableLength < Lz77Limits.MinMatch)
                        continue;

                    long candidateCost = MatchBaseCost + extra + minSuffixCost[availableLength];
                    if (candidateCost < bestCost)
                    {
                        bestCost = candidateCost;
                        bestDistance = distanceByExtra[extra];
                        bestLength = minSuffixLength[availableLength];
                    }
                }
            }

            cost[pos] = bestCost;
            chosenDistance[pos] = bestDistance;
            chosenLength[pos] = bestLength;
        }

        // Відновлюємо оптимальну послідовність токенів за обраними переходами.
        for (int pos = 0; pos < n;)
        {
            if (chosenDistance[pos] == 0)
            {
                result.Add(Lz77Token.Lit(block[pos]));
                pos++;
            }
            else
            {
                result.Add(Lz77Token.Match(chosenDistance[pos], chosenLength[pos]));
                pos += chosenLength[pos];
            }
        }

        return result;
    }

    private static int Hash(byte[] data, int pos, int hashBits)
    {
        int key = data[pos] << 16 | data[pos + 1] << 8 | data[pos + 2];
        return (int)(((uint)key * 2654435761u) >> (32 - hashBits));
    }
}
