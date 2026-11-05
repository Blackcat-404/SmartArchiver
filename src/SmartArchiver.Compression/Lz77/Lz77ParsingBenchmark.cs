using System.Diagnostics;

namespace SmartArchiver.Compression;

/// <summary>Спосіб розбору потоку для порівняльного експерименту.</summary>
public enum Lz77ParsingMethod
{
    Greedy,
    Lazy
}

/// <summary>Результат одного вимірювання розбору та стиснення блока.</summary>
public sealed record Lz77ParsingMeasurement(
    Lz77ParsingMethod Method,
    long OriginalBytes,
    long EncodedBytes,
    double ElapsedSeconds);

/// <summary>
/// Вимірює час LZ77 + Huffman і фактичний розмір закодованого блока.
/// Не залежить від бази даних або формату контейнера архіву.
/// </summary>
public static class Lz77ParsingBenchmark
{
    public static Lz77ParsingMeasurement Measure(
        byte[] block,
        Lz77ParsingMethod method,
        int maxChain = 256,
        int minLazyGain = 2)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minLazyGain);

        if (block.Length == 0)
            throw new ArgumentException("Порожній блок не можна закодувати.", nameof(block));

        // Час охоплює і LZ77-розбір, і запис таблиць та потоку Huffman.
        var timer = Stopwatch.StartNew();
        List<Lz77Token> tokens = method switch
        {
            Lz77ParsingMethod.Greedy => GreedyParser.Parse(block, maxChain),
            Lz77ParsingMethod.Lazy => LazyParser.Parse(block, maxChain, minLazyGain),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
        byte[] encoded = Lz77BlockEncoder.Encode(tokens);
        timer.Stop();

        // Розмір включає службові поля та три таблиці Huffman,
        // але не включає заголовок і метадані зовнішнього архіву.
        return new Lz77ParsingMeasurement(
            method, block.LongLength, encoded.LongLength, timer.Elapsed.TotalSeconds);
    }
}
