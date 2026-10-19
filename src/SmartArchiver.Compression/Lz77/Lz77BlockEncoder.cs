using System.Numerics;

namespace SmartArchiver.Compression;

/// <summary>
/// Кодує токени LZ77 трьома окремими канонічними таблицями Гаффмана
/// відповідно до розділу 3 FORMAT_SPEC_P2_2.
/// </summary>
internal static class Lz77BlockEncoder
{
    public static byte[] Encode(IReadOnlyList<Lz77Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
            throw new ArgumentException("Порожній блок не кодується.", nameof(tokens));

        var litFreq = new long[Lz77BlockDecoder.LitAlphabetSize];
        var lenFreq = new long[Lz77BlockDecoder.LenAlphabetSize];
        var distFreq = new long[Lz77BlockDecoder.DistAlphabetSize];

        // Підраховуємо частоти символів окремо для кожного алфавіту.
        long decodedLength = 0;
        foreach (Lz77Token token in tokens)
        {
            if (!token.IsMatch)
            {
                litFreq[token.Literal]++;
                decodedLength++;
                continue;
            }

            ValidateMatch(token, decodedLength);
            litFreq[Lz77BlockDecoder.MatchMarker]++;
            lenFreq[token.Length - Lz77Limits.MinMatch]++;
            distFreq[DistanceCode(token.Distance)]++;
            decodedLength += token.Length;
        }

        // Формат вимагає три непорожні таблиці навіть без збігів.
        if (Array.TrueForAll(lenFreq, x => x == 0)) lenFreq[0] = 1;
        if (Array.TrueForAll(distFreq, x => x == 0)) distFreq[0] = 1;

        byte[] litLengths = HuffmanLengths.Build(litFreq);
        byte[] lenLengths = HuffmanLengths.Build(lenFreq);
        byte[] distLengths = HuffmanLengths.Build(distFreq);
        uint[] litCodes = CanonicalHuffman.AssignCodes(litLengths);
        uint[] lenCodes = CanonicalHuffman.AssignCodes(lenLengths);
        uint[] distCodes = CanonicalHuffman.AssignCodes(distLengths);

        // Спочатку створюємо бітовий потік, щоб знати його довжину.
        using var bitStream = new MemoryStream();
        var writer = new BitWriter(bitStream);
        foreach (Lz77Token token in tokens)
        {
            if (!token.IsMatch)
            {
                writer.WriteBits(litCodes[token.Literal], litLengths[token.Literal]);
                continue;
            }

            WriteSymbol(writer, litCodes, litLengths, Lz77BlockDecoder.MatchMarker);
            WriteSymbol(writer, lenCodes, lenLengths, token.Length - Lz77Limits.MinMatch);

            int code = DistanceCode(token.Distance);
            WriteSymbol(writer, distCodes, distLengths, code);
            int extra = Math.Max(0, code - 1);
            if (extra > 0)
            {
                uint d = (uint)(token.Distance - 1);
                uint lowBits = d & ((1u << extra) - 1);
                writer.WriteBits(lowBits, extra);
            }
        }
        writer.Flush();

        using var output = new MemoryStream();
        WriteTable(output, litLengths);
        WriteTable(output, lenLengths);
        WriteTable(output, distLengths);
        WriteUInt32(output, checked((uint)tokens.Count));
        WriteUInt32(output, checked((uint)bitStream.Length));
        bitStream.Position = 0;
        bitStream.CopyTo(output);
        return output.ToArray();
    }

    private static void ValidateMatch(Lz77Token token, long decodedLength)
    {
        if (token.Length < Lz77Limits.MinMatch || token.Length > Lz77Limits.MaxMatch)
            throw new ArgumentOutOfRangeException(nameof(token), "Некоректна довжина збігу.");
        if (token.Distance < 1 || token.Distance > Lz77Limits.Window || token.Distance > decodedLength)
            throw new ArgumentOutOfRangeException(nameof(token), "Некоректна відстань збігу.");
    }

    private static int DistanceCode(int distance) =>
        distance == 1 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)(distance - 1));

    private static void WriteSymbol(BitWriter writer, uint[] codes, byte[] lengths, int symbol) =>
        writer.WriteBits(codes[symbol], lengths[symbol]);

    private static void WriteTable(Stream output, byte[] lengths)
    {
        int count = 0;
        foreach (byte length in lengths)
            if (length != 0) count++;
        WriteUInt16(output, checked((ushort)count));

        // Записи таблиці обов'язково йдуть за зростанням символу.
        for (int symbol = 0; symbol < lengths.Length; symbol++)
        {
            if (lengths[symbol] == 0) continue;
            WriteUInt16(output, checked((ushort)symbol));
            output.WriteByte(lengths[symbol]);
        }
    }

    private static void WriteUInt16(Stream output, ushort value)
    {
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }
}
