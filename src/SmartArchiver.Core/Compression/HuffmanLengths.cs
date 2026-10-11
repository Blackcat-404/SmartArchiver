namespace SmartArchiver.Core;

/// <summary>
/// Побудова довжин кодів Гаффмана з частот. Довжини обмежені 15 бітами, бо формат архіву
/// не дозволяє використовувати більші коди без втрати сумісності.
/// Автор: Артем.
/// </summary>
internal static class HuffmanLengths
{
    /// <summary>
    /// Вибудовує масив довжин коду для 256 символів на основі частот зустрічань.
    /// Якщо побудований код перевищує ліміт 15 біт, частоти «вирівнюються» і процедура повторюється.
    /// </summary>
    public static byte[] Build(long[] freq)
    {
        if (freq.Length != HuffmanCodec.TableSize)
            throw new ArgumentException($"Очікується {HuffmanCodec.TableSize} частот.", nameof(freq));

        long[] work = (long[])freq.Clone();
        while (true)
        {
            byte[] lengths = BuildUnlimited(work);

            int maxLength = 0;
            foreach (byte len in lengths)
                if (len > maxLength) maxLength = len;

            if (maxLength <= CanonicalHuffman.MaxCodeLength)
                return lengths;

            // Довжина перевищила ліміт. Вирівнюємо частоти (ділимо навпіл, але не до нуля)
            // і будуємо дерево знову. Частоти сходяться до 1, тому цикл завершується.
            for (int i = 0; i < work.Length; i++)
                if (work[i] > 0)
                    work[i] = (work[i] + 1) / 2;
        }
    }

    /// <summary>
    /// Будує дерева Гаффмана без обмеження довжини, але з детермінованим порядком для однакових ваг.
    /// </summary>
    private static byte[] BuildUnlimited(long[] freq)
    {
        var lengths = new byte[freq.Length];

        var used = new List<int>();
        for (int s = 0; s < freq.Length; s++)
            if (freq[s] > 0) used.Add(s);

        int n = used.Count;
        if (n == 0)
            throw new ArgumentException("Немає символів для кодування.");
        if (n == 1)
        {
            lengths[used[0]] = 1;
            return lengths;
        }

        // Вузли: 0..n-1 листки, далі внутрішні. Сумарно 2n-1 вузлів.
        int total = 2 * n - 1;
        var weight = new long[total];
        var parent = new int[total];

        var queue = new PriorityQueue<int, long>();
        for (int i = 0; i < n; i++)
        {
            weight[i] = freq[used[i]];
            queue.Enqueue(i, Key(weight[i], i));
        }

        int next = n;
        while (queue.Count > 1)
        {
            queue.TryDequeue(out int a, out _);
            queue.TryDequeue(out int b, out _);

            weight[next] = weight[a] + weight[b];
            parent[a] = next;
            parent[b] = next;
            queue.Enqueue(next, Key(weight[next], next));
            next++;
        }

        int root = next - 1;
        for (int i = 0; i < n; i++)
        {
            int depth = 0;
            for (int x = i; x != root; x = parent[x])
                depth++;
            lengths[used[i]] = (byte)depth;
        }

        return lengths;
    }

    /// <summary>
    /// Вага в старших бітах, індекс у молодших (індекс вузла менше 2^20), щоб результат був детермінованим.
    /// </summary>
    private static long Key(long weight, int index) => (weight << 20) | (uint)index;
}