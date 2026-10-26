using System.Numerics;

namespace SmartArchiver.Compression;

/// <summary>
/// CP3 №3. Експериментальний статичний арифметичний кодер ТИХ САМИХ LZ77-токенів,
/// що й Lz77BlockEncoder. Це НЕ формат архіву і не використовується ArchiveWriter.
/// Вхідні й вихідні дані блока перевіряються зворотним Decode().
///
/// Experimental block layout: 3 sparse frequency tables (LIT=257, LEN=256, DIST=16),
/// token count (u32 BE), arithmetic payload length (u32 BE), payload (MSB first).
/// Each table: nonzero count (u16 BE), then (symbol u16 BE, frequency u16 BE).
/// Frequency totals <= 16384; payload additionally codes DIST extra bits as uniform symbols.
/// </summary>
internal static class ArithmeticBlockCodec
{
    private const int MaxTotal = 16384;
    private const int LitSize = 257;
    private const int LenSize = 256;
    private const int DistSize = 16;
    private const int MatchMarker = 256;

    internal sealed record EncodedBlock(byte[] Bytes, int HeaderBytes, int PayloadBytes);

    internal static EncodedBlock Encode(IReadOnlyList<Lz77Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
            throw new ArgumentException("Порожній блок не кодується.", nameof(tokens));

        var lit = new int[LitSize];
        var len = new int[LenSize];
        var dist = new int[DistSize];
        int rawLength = 0;
        foreach (Lz77Token t in tokens)
        {
            if (!t.IsMatch)
            {
                lit[t.Literal]++;
                rawLength = checked(rawLength + 1);
            }
            else
            {
                ValidateMatch(t, rawLength);
                lit[MatchMarker]++;
                len[t.Length - Lz77Limits.MinMatch]++;
                dist[DistanceCode(t.Distance)]++;
                rawLength = checked(rawLength + t.Length);
            }
        }

        // Частоти обох ентропійних кодерів походять з однакових токенів.
        // Якщо блок великий, нормалізуємо без обнулення реально присутніх символів.
        Normalize(lit);
        Normalize(len);
        Normalize(dist);
        var litModel = new FrequencyModel(lit);
        var lenModel = new FrequencyModel(len);
        var distModel = new FrequencyModel(dist);

        using var payload = new MemoryStream();
        var bits = new BitWriter(payload);
        var encoder = new ArithmeticEncoder(bits);
        foreach (Lz77Token t in tokens)
        {
            if (!t.IsMatch)
            {
                encoder.Encode(litModel, t.Literal);
                continue;
            }

            encoder.Encode(litModel, MatchMarker);
            encoder.Encode(lenModel, t.Length - Lz77Limits.MinMatch);
            int symbol = DistanceCode(t.Distance);
            encoder.Encode(distModel, symbol);
            int extra = Math.Max(0, symbol - 1);
            uint lowBits = (uint)(t.Distance - 1) & ((1u << extra) - 1u);
            for (int bit = extra - 1; bit >= 0; bit--)
                encoder.EncodeUniformBit((int)((lowBits >> bit) & 1u));
        }
        encoder.Finish();

        using var output = new MemoryStream();
        WriteTable(output, lit);
        WriteTable(output, len);
        WriteTable(output, dist);
        WriteU32(output, checked((uint)tokens.Count));
        WriteU32(output, checked((uint)payload.Length));
        int headerSize = checked((int)output.Length);
        payload.Position = 0;
        payload.CopyTo(output);
        return new EncodedBlock(output.ToArray(), headerSize, checked((int)payload.Length));
    }

    internal static byte[] Decode(ReadOnlySpan<byte> coded, int rawLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rawLength);
        int pos = 0;
        int[] lit = ReadTable(coded, ref pos, LitSize);
        int[] len = ReadTable(coded, ref pos, LenSize);
        int[] dist = ReadTable(coded, ref pos, DistSize);
        uint tokenCount = ReadU32(coded, ref pos);
        uint streamLength = ReadU32(coded, ref pos);
        if (tokenCount == 0 || tokenCount > rawLength || streamLength == 0 ||
            streamLength != coded.Length - (long)pos)
            throw new InvalidDataException("Некоректна довжина блока arithmetic.");
        if (lit.All(f => f == 0))
            throw new InvalidDataException("Порожня таблиця LIT.");

        var litModel = new FrequencyModel(lit);
        var lenModel = new FrequencyModel(len);
        var distModel = new FrequencyModel(dist);
        var reader = new BitReader(coded, pos);
        var decoder = new ArithmeticDecoder(ref reader);
        var output = new List<byte>(rawLength);
        for (uint i = 0; i < tokenCount; i++)
        {
            int symbol = decoder.Decode(litModel, ref reader);
            if (symbol != MatchMarker)
            {
                if (output.Count == rawLength)
                    throw new InvalidDataException("Забагато літералів у arithmetic-блоці.");
                output.Add((byte)symbol);
                continue;
            }

            int length = decoder.Decode(lenModel, ref reader) + Lz77Limits.MinMatch;
            int distanceCode = decoder.Decode(distModel, ref reader);
            int extra = Math.Max(0, distanceCode - 1);
            uint lowBits = 0;
            for (int bit = 0; bit < extra; bit++)
                lowBits = (lowBits << 1) | (uint)decoder.DecodeUniformBit(ref reader);
            int distance = distanceCode == 0 ? 1 : distanceCode == 1 ? 2
                : checked((int)((1u << (distanceCode - 1)) + lowBits + 1u));

            if (length is < Lz77Limits.MinMatch or > Lz77Limits.MaxMatch ||
                distance < 1 || distance > Lz77Limits.Window || distance > output.Count ||
                length > rawLength - output.Count)
                throw new InvalidDataException("Некоректний LZ77-збіг у arithmetic-блоці.");

            int start = output.Count - distance;
            for (int j = 0; j < length; j++)
                output.Add(output[start + j]);
        }
        if (output.Count != rawLength)
            throw new InvalidDataException("Відновлена довжина arithmetic-блока не збігається.");
        return output.ToArray();
    }

    private static void ValidateMatch(Lz77Token t, int rawLength)
    {
        if (t.Length is < Lz77Limits.MinMatch or > Lz77Limits.MaxMatch ||
            t.Distance < 1 || t.Distance > Lz77Limits.Window || t.Distance > rawLength)
            throw new ArgumentOutOfRangeException(nameof(t), "Некоректний LZ77-збіг.");
    }

    private static int DistanceCode(int distance) => distance == 1 ? 0
        : 32 - BitOperations.LeadingZeroCount((uint)(distance - 1));

    private static void Normalize(int[] freq)
    {
        long sum = freq.Sum(x => (long)x);
        if (sum <= MaxTotal) return;
        int active = freq.Count(x => x > 0);
        int budget = MaxTotal - active;
        long totalWeight = sum;
        long[] remainder = new long[freq.Length];
        int assigned = 0;
        for (int s = 0; s < freq.Length; s++)
        {
            if (freq[s] == 0) continue;
            long scaled = (long)freq[s] * budget;
            int share = (int)(scaled / totalWeight);
            remainder[s] = scaled % totalWeight;
            freq[s] = share + 1;
            assigned += share;
        }
        // Розподіл залишку за найбільшими дробовими частками,
        // щоб підсумкова частота точно дорівнювала MaxTotal.
        int left = budget - assigned;
        foreach (int index in Enumerable.Range(0, freq.Length)
                     .Where(s => freq[s] > 0)
                     .OrderByDescending(s => remainder[s])
                     .ThenBy(s => s).Take(left))
            freq[index]++;
    }

    private sealed class FrequencyModel
    {
        private readonly int[] _cumulative;
        internal int Total { get; }

        internal FrequencyModel(int[] frequencies)
        {
            _cumulative = new int[frequencies.Length + 1];
            for (int i = 0; i < frequencies.Length; i++)
                _cumulative[i + 1] = checked(_cumulative[i] + frequencies[i]);
            Total = _cumulative[^1];
            if (Total > MaxTotal)
                throw new InvalidDataException("Завелика частотна таблиця.");
        }

        internal (int Low, int High) Interval(int symbol)
        {
            if (Total == 0 || symbol < 0 || symbol + 1 >= _cumulative.Length ||
                _cumulative[symbol] == _cumulative[symbol + 1])
                throw new InvalidDataException("Символ без частоти в арифметичній моделі.");
            return (_cumulative[symbol], _cumulative[symbol + 1]);
        }

        internal (int Symbol, int Low, int High) Lookup(int value)
        {
            if (value < 0 || value >= Total)
                throw new InvalidDataException("Арифметична частота поза межами.");
            int left = 1, right = _cumulative.Length - 1;
            int target = value + 1;
            while (left < right)
            {
                int mid = left + (right - left) / 2;
                if (_cumulative[mid] >= target) right = mid;
                else left = mid + 1;
            }
            int symbol = left - 1;
            if (symbol < 0 || symbol + 1 >= _cumulative.Length ||
                _cumulative[symbol] == _cumulative[symbol + 1])
                throw new InvalidDataException("Некоректний арифметичний символ.");
            return (symbol, _cumulative[symbol], _cumulative[symbol + 1]);
        }
    }

    private sealed class ArithmeticEncoder(BitWriter output)
    {
        private const uint Half = 0x80000000;
        private const uint Quarter = 0x40000000;
        private const uint ThreeQuarters = 0xC0000000;
        private uint _low;
        private uint _high = uint.MaxValue;
        private int _pending;

        internal void Encode(FrequencyModel model, int symbol)
        {
            var (lo, hi) = model.Interval(symbol);
            Update(lo, hi, model.Total);
        }

        internal void EncodeUniformBit(int bit) => Update(bit, bit + 1, 2);

        private void Update(int cumLo, int cumHi, int total)
        {
            ulong range = (ulong)_high - _low + 1;
            _high = (uint)((ulong)_low + range * (uint)cumHi / (uint)total - 1);
            _low = (uint)((ulong)_low + range * (uint)cumLo / (uint)total);
            while (true)
            {
                if (_high < Half) Emit(0);
                else if (_low >= Half) { Emit(1); _low -= Half; _high -= Half; }
                else if (_low >= Quarter && _high < ThreeQuarters)
                {
                    _pending++;
                    _low -= Quarter;
                    _high -= Quarter;
                }
                else break;
                _low <<= 1;
                _high = (_high << 1) | 1u;
            }
        }

        private void Emit(int bit)
        {
            output.WriteBits((uint)bit, 1);
            while (_pending > 0)
            {
                output.WriteBits((uint)(1 - bit), 1);
                _pending--;
            }
        }

        internal void Finish()
        {
            _pending++;
            Emit(_low < Quarter ? 0 : 1);
            // Декодер віртуально доповнює потік нулями після його кінця.
            // Фізичні 32 біти доповнення не потрібні.
            output.Flush();
        }
    }

    private struct ArithmeticDecoder
    {
        private const uint Half = 0x80000000;
        private const uint Quarter = 0x40000000;
        private const uint ThreeQuarters = 0xC0000000;
        private uint _low;
        private uint _high;
        private uint _value;

        internal ArithmeticDecoder(scoped ref BitReader reader)
        {
            _low = 0;
            _high = uint.MaxValue;
            for (int i = 0; i < 32; i++)
                _value = (_value << 1) | (uint)reader.ReadBitOrZero();
        }

        internal int Decode(FrequencyModel model, scoped ref BitReader reader)
        {
            if (model.Total == 0)
                throw new InvalidDataException("Порожня таблиця для збігу.");
            ulong range = (ulong)_high - _low + 1;
            int cumulative = (int)((((ulong)_value - _low + 1) * (uint)model.Total - 1) / range);
            var (symbol, lo, hi) = model.Lookup(cumulative);
            Update(lo, hi, model.Total, ref reader);
            return symbol;
        }

        internal int DecodeUniformBit(scoped ref BitReader reader)
        {
            ulong range = (ulong)_high - _low + 1;
            int bit = (int)((((ulong)_value - _low + 1) * 2 - 1) / range);
            Update(bit, bit + 1, 2, ref reader);
            return bit;
        }

        private void Update(int cumLo, int cumHi, int total, scoped ref BitReader reader)
        {
            ulong range = (ulong)_high - _low + 1;
            _high = (uint)((ulong)_low + range * (uint)cumHi / (uint)total - 1);
            _low = (uint)((ulong)_low + range * (uint)cumLo / (uint)total);
            while (true)
            {
                if (_high < Half) { }
                else if (_low >= Half)
                {
                    _low -= Half; _high -= Half; _value -= Half;
                }
                else if (_low >= Quarter && _high < ThreeQuarters)
                {
                    _low -= Quarter; _high -= Quarter; _value -= Quarter;
                }
                else break;
                _low <<= 1;
                _high = (_high << 1) | 1u;
                _value = (_value << 1) | (uint)reader.ReadBitOrZero();
            }
        }
    }

    private static void WriteTable(Stream output, int[] frequencies)
    {
        WriteU16(output, checked((ushort)frequencies.Count(f => f > 0)));
        for (int i = 0; i < frequencies.Length; i++)
        {
            if (frequencies[i] == 0) continue;
            WriteU16(output, checked((ushort)i));
            WriteU16(output, checked((ushort)frequencies[i]));
        }
    }

    private static int[] ReadTable(ReadOnlySpan<byte> data, ref int pos, int size)
    {
        int count = ReadU16(data, ref pos);
        if (count > size)
            throw new InvalidDataException("Забагато елементів таблиці.");
        var f = new int[size];
        int previous = -1;
        for (int i = 0; i < count; i++)
        {
            int symbol = ReadU16(data, ref pos);
            int value = ReadU16(data, ref pos);
            if (symbol <= previous || symbol >= size || value == 0)
                throw new InvalidDataException("Некоректна частотна таблиця.");
            f[symbol] = value;
            previous = symbol;
        }
        return f;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, ref int pos)
    {
        if (data.Length - pos < 2)
            throw new InvalidDataException("Обрізана таблиця частот.");
        ushort value = (ushort)((data[pos] << 8) | data[pos + 1]);
        pos += 2;
        return value;
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, ref int pos)
    {
        if (data.Length - pos < 4)
            throw new InvalidDataException("Обрізаний заголовок arithmetic-блока.");
        uint value = ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) |
            ((uint)data[pos + 2] << 8) | data[pos + 3];
        pos += 4;
        return value;
    }

    private static void WriteU16(Stream output, ushort value)
    {
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static void WriteU32(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }
}
