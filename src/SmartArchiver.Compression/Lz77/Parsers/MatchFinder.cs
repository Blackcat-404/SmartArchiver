namespace SmartArchiver.Compression;

/// <summary>
/// Пошук збігів через хеш-ланцюжки за трьома байтами (методичка 4.10).
/// Ланцюжок іде від найновішої позиції до найстарішої, тож відстані зростають.
/// Вікно обмежене самим блоком: позицій поза блоком не існує.
/// Автор: Артем.
/// </summary>
internal sealed class MatchFinder
{
    /// <summary>Скільки кандидатів перевіряти. Обмежує час на патологічних даних.</summary>
    public const int DefaultMaxChain = 256;

    private const int HashBits = 16;

    private readonly byte[] _data;
    private readonly int[] _head = new int[1 << HashBits];
    private readonly int[] _prev;
    private readonly int _maxChain;
    private readonly int _window;
    private readonly int _minMatch;

    public MatchFinder(byte[] data, int maxChain = DefaultMaxChain)
        : this(data, new Lz77SearchOptions { SearchDepth = maxChain })
    {
    }

    public MatchFinder(byte[] data, Lz77SearchOptions options)
    {
        // Перевіряємо коректність вхідних параметрів.
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _data = data;
        _maxChain = options.SearchDepth;
        _window = options.SearchWindow;
        _minMatch = options.MinMatchLength;
        _prev = new int[data.Length];

        Array.Fill(_head, -1);
        Array.Fill(_prev, -1);
    }

    private int Hash(int pos)
    {
        int key = _data[pos] << 16 | _data[pos + 1] << 8 | _data[pos + 2];
        return (int)(((uint)key * 2654435761u) >> (32 - HashBits));
    }

    /// <summary>Додає позицію в ланцюжок. Позиції мають додаватися у зростаючому порядку.</summary>
    public void Insert(int pos)
    {
        if (pos + 2 >= _data.Length)
            return;

        int h = Hash(pos);
        _prev[pos] = _head[h];
        _head[h] = pos;
    }

    /// <summary>
    /// Знаходить найдовший збіг для позиції <paramref name="pos"/> серед уже доданих позицій.
    /// При однаковій довжині лишається найближчий (менша відстань).
    /// Повертає false, якщо збіг коротший за MinMatch.
    /// </summary>
    public bool TryLongest(int pos, out int distance, out int length)
    {
        distance = 0;
        length = 0;

        if (pos + _minMatch > _data.Length)
            return false;

        int maxLen = Math.Min(Lz77Limits.MaxMatch, _data.Length - pos);
        int candidate = _head[Hash(pos)];
        int checkedCount = 0;

        while (candidate >= 0 && checkedCount++ < _maxChain)
        {
            int dist = pos - candidate;

            // Ланцюжок упорядкований від найближчих до найдальших: далі будуть лише більші відстані.
            if (dist > _window)
                break;

            int l = CommonPrefix(candidate, pos, maxLen);

            // Строго більше: при рівній довжині зберігаємо вже знайдений (він ближчий).
            if (l > length)
            {
                length = l;
                distance = dist;
                if (length == maxLen)
                    break;
            }

            candidate = _prev[candidate];
        }

        return length >= _minMatch;
    }

    private int CommonPrefix(int source, int pos, int maxLen)
    {
        // source < pos, тому перекриття (distance < length) тут коректне: байти вже є у вхідних даних.
        int l = 0;
        while (l < maxLen && _data[source + l] == _data[pos + l])
            l++;
        return l;
    }
}
