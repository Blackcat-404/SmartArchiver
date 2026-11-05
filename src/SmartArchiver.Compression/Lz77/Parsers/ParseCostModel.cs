using System.Numerics;

namespace SmartArchiver.Compression;

/// <summary>
/// Адитивна оцінка бітової вартості токенів LZ77 для DP.
/// Ця оцінка не містить заголовка, Huffman-таблиць і вирівнювання бітів.
/// </summary>
internal sealed class ParseCostModel
{
    private readonly int[] _lit;
    private readonly int[] _len;
    private readonly int[] _dist;

    /// <summary>Для CP2 обидва коди відстані 1 і 2 належать одному класу вартості.</summary>
    internal bool IsFixed { get; }

    private ParseCostModel(int[] lit, int[] len, int[] dist, bool isFixed)
    {
        _lit = lit;
        _len = len;
        _dist = dist;
        IsFixed = isFixed;
    }

    /// <summary>Базова модель CP2: літерал 9 біт, match 17 + extra(distance).</summary>
    internal static ParseCostModel Fixed()
    {
        var lit = new int[Lz77BlockDecoder.LitAlphabetSize];
        Array.Fill(lit, DpParser.LiteralCost);
        lit[Lz77BlockDecoder.MatchMarker] = DpParser.MatchBaseCost;

        var len = new int[Lz77BlockDecoder.LenAlphabetSize];
        var dist = new int[Lz77BlockDecoder.DistAlphabetSize];
        return new ParseCostModel(lit, len, dist, isFixed: true);
    }

    /// <summary>
    /// Модель із реальних довжин канонічних кодів.
    /// Для символу з кодовою довжиною 0 задається ненульова штрафна вартість,
    /// інакше DP міг би вважати неіснуючі коди безкоштовними.
    /// </summary>
    internal static ParseCostModel FromLengths(byte[] lit, byte[] len, byte[] dist)
    {
        ArgumentNullException.ThrowIfNull(lit);
        ArgumentNullException.ThrowIfNull(len);
        ArgumentNullException.ThrowIfNull(dist);
        if (lit.Length != Lz77BlockDecoder.LitAlphabetSize ||
            len.Length != Lz77BlockDecoder.LenAlphabetSize ||
            dist.Length != Lz77BlockDecoder.DistAlphabetSize)
            throw new ArgumentException("Некоректні розміри таблиць LIT/LEN/DIST.");

        static int[] MakeCosts(byte[] lengths)
        {
            int max = lengths.Max(x => (int)x);
            if (max == 0)
                throw new ArgumentException("Таблиця Huffman не може бути порожньою.");
            int penalty = Math.Max(9, max + 1);
            return lengths.Select(x => x == 0 ? penalty : (int)x).ToArray();
        }

        return new ParseCostModel(MakeCosts(lit), MakeCosts(len), MakeCosts(dist), isFixed: false);
    }

    /// <summary>
    /// Модель із поточного потоку токенів. Для наступного DP всі частоти
    /// згладжені на +1; це НЕ змінює реальні Huffman-таблиці енкодера.
    /// smoothUnused=false відтворює справжні довжини кодів для використаних символів.
    /// </summary>
    internal static ParseCostModel FromTokens(IReadOnlyList<Lz77Token> tokens, bool smoothUnused = true)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
            throw new ArgumentException("Порожній потік не має таблиць Huffman.", nameof(tokens));
        var lit = new long[Lz77BlockDecoder.LitAlphabetSize];
        var len = new long[Lz77BlockDecoder.LenAlphabetSize];
        var dist = new long[Lz77BlockDecoder.DistAlphabetSize];
        foreach (Lz77Token t in tokens)
        {
            if (!t.IsMatch)
            {
                lit[t.Literal]++;
            }
            else
            {
                lit[Lz77BlockDecoder.MatchMarker]++;
                len[t.Length - Lz77Limits.MinMatch]++;
                dist[DistanceCode(t.Distance)]++;
            }
        }

        if (smoothUnused)
        {
            for (int i = 0; i < lit.Length; i++) lit[i]++;
            for (int i = 0; i < len.Length; i++) len[i]++;
            for (int i = 0; i < dist.Length; i++) dist[i]++;
        }
        else
        {
            if (Array.TrueForAll(len, value => value == 0)) len[0] = 1;
            if (Array.TrueForAll(dist, value => value == 0)) dist[0] = 1;
        }

        return FromLengths(HuffmanLengths.Build(lit), HuffmanLengths.Build(len), HuffmanLengths.Build(dist));
    }

    internal int Literal(byte value) => _lit[value];
    internal int Length(int length) => _len[length - Lz77Limits.MinMatch];
    internal int DistancePart(int code) => _dist[code];
    internal int MatchMarker => _lit[Lz77BlockDecoder.MatchMarker];

    internal int Match(int length, int distance)
    {
        int code = DistanceCode(distance);
        return MatchMarker + Length(length) + DistancePart(code) + Math.Max(0, code - 1);
    }

    internal long CalculateCost(IEnumerable<Lz77Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        long bits = 0;
        foreach (Lz77Token token in tokens)
            bits += token.IsMatch ? Match(token.Length, token.Distance) : Literal(token.Literal);
        return bits;
    }

    internal static int DistanceCode(int distance)
    {
        if (distance < 1 || distance > Lz77Limits.Window)
            throw new ArgumentOutOfRangeException(nameof(distance));
        return distance == 1 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)(distance - 1));
    }
}
