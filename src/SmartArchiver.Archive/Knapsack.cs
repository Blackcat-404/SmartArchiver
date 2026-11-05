namespace SmartArchiver.Archive;

/// <summary>
/// Задача про рюкзак 0/1 для одного тому: вибрати блоки з максимальною сумарною цінністю,
/// сумарна вага яких не перевищує місткості. Власна реалізація, без бібліотек.
/// </summary>
internal static class Knapsack
{
    /// <summary>
    /// Найбільше клітинок таблиці ДП (блоків × (місткість + 1)). Таблиця рішень займає 1 біт на клітинку,
    /// тож 200 млн клітинок це 25 МБ. Для більших задач ваги й місткість грубшають, див. <see cref="SolveDp"/>.
    /// </summary>
    public const long DefaultMaxCells = 200_000_000;

    /// <summary>
    /// Точне ДП за вагою: best[j] = найбільша цінність при вазі не більше j.
    /// Якщо блоків × місткість більше за <paramref name="maxCells"/>, ваги округлюються вгору до кроку unit,
    /// а місткість вниз: вибір завжди влізе в справжню місткість, але може бути трохи гіршим за оптимум.
    /// Для задач, що вміщуються в maxCells (усі експерименти проєкту), результат точно оптимальний.
    /// </summary>
    /// <returns>chosen[i] = true, якщо блок i потрапив у рюкзак.</returns>
    public static bool[] SolveDp(long[] weights, long[] values, long capacity, long maxCells = DefaultMaxCells)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(values);
        if (weights.Length != values.Length)
            throw new ArgumentException("Ваг і цінностей має бути порівну.", nameof(values));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCells);

        int n = weights.Length;
        var chosen = new bool[n];
        if (n == 0 || capacity <= 0)
            return chosen;

        long cells = (long)n * (capacity + 1);
        long unit = cells <= maxCells ? 1 : (cells + maxCells - 1) / maxCells;
        long scaledCapacity = capacity / unit;
        if (scaledCapacity == 0)
            return chosen;

        int width = checked((int)scaledCapacity + 1);
        var scaledWeights = new int[n];
        for (int i = 0; i < n; i++)
        {
            long scaled = (weights[i] + unit - 1) / unit;
            // -1: блок не влазить навіть у порожній рюкзак.
            scaledWeights[i] = scaled > scaledCapacity ? -1 : (int)Math.Max(scaled, 1);
        }

        var best = new long[width];
        var keep = new ulong[((long)n * width + 63) / 64];
        for (int i = 0; i < n; i++)
        {
            int weight = scaledWeights[i];
            if (weight < 0)
                continue;

            long value = values[i];
            long rowStart = (long)i * width;
            for (int j = width - 1; j >= weight; j--)
            {
                long candidate = best[j - weight] + value;
                if (candidate > best[j])
                {
                    best[j] = candidate;
                    long bit = rowStart + j;
                    keep[bit >> 6] |= 1UL << (int)(bit & 63);
                }
            }
        }

        // Відновлення: іде з кінця; блок взято, якщо на його рядку best[j] покращився.
        int column = width - 1;
        for (int i = n - 1; i >= 0; i--)
        {
            if (scaledWeights[i] < 0)
                continue;

            long bit = (long)i * width + column;
            if ((keep[bit >> 6] & (1UL << (int)(bit & 63))) != 0)
            {
                chosen[i] = true;
                column -= scaledWeights[i];
            }
        }
        return chosen;
    }

    /// <summary>
    /// Жадібний вибір: блоки за спаданням цінності на байт ваги, кожен береться, якщо ще влазить.
    /// Рівні щільності розв'язуються меншою вагою, потім порядком у списку, тож результат детермінований.
    /// </summary>
    public static bool[] SolveGreedy(long[] weights, long[] values, long capacity)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(values);
        if (weights.Length != values.Length)
            throw new ArgumentException("Ваг і цінностей має бути порівну.", nameof(values));

        int n = weights.Length;
        var chosen = new bool[n];
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            // values[a] / weights[a] > values[b] / weights[b] без ділення.
            int byDensity = (values[b] * weights[a]).CompareTo(values[a] * weights[b]);
            if (byDensity != 0)
                return byDensity;
            int byWeight = weights[a].CompareTo(weights[b]);
            return byWeight != 0 ? byWeight : a.CompareTo(b);
        });

        long left = capacity;
        foreach (int i in order)
        {
            if (weights[i] <= left)
            {
                chosen[i] = true;
                left -= weights[i];
            }
        }
        return chosen;
    }
}
