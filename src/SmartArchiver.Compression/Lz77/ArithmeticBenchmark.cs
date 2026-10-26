using System.Diagnostics;
using System.Globalization;

namespace SmartArchiver.Compression;

/// <summary>
/// CP3 №3: Huffman vs arithmetic на одному наборі LZ77-токенів.
/// Кожен результат перевіряється побайтовим round-trip.
/// Не створює CSV і не змінює формат архіву.
/// </summary>
public static class ArithmeticBenchmark
{
    public sealed record FileRow(
        int Group, string File, long RawBytes, int Blocks,
        long HuffmanBytes, long ArithmeticBytes,
        long HuffmanPayloadBytes, long ArithmeticPayloadBytes,
        double HuffmanSeconds, double ArithmeticSeconds)
    {
        /// <summary>Додатне значення: arithmetic менший за Huffman.</summary>
        public long TotalSavedBytes => HuffmanBytes - ArithmeticBytes;
        public long PayloadSavedBytes => HuffmanPayloadBytes - ArithmeticPayloadBytes;
    }

    public sealed record Report(IReadOnlyList<FileRow> Files)
    {
        public long RawBytes => Files.Sum(f => f.RawBytes);
        public long HuffmanBytes => Files.Sum(f => f.HuffmanBytes);
        public long ArithmeticBytes => Files.Sum(f => f.ArithmeticBytes);
        public long HuffmanPayloadBytes => Files.Sum(f => f.HuffmanPayloadBytes);
        public long ArithmeticPayloadBytes => Files.Sum(f => f.ArithmeticPayloadBytes);
        public long TotalSavedBytes => HuffmanBytes - ArithmeticBytes;
        public long PayloadSavedBytes => HuffmanPayloadBytes - ArithmeticPayloadBytes;
        public double TotalSavedPercent => HuffmanBytes == 0 ? 0 : 100.0 * TotalSavedBytes / HuffmanBytes;
    }

    public sealed record ProgressRow(int Group, string File, int BlocksDone, int BlocksTotal);

    /// <summary>
    /// Обидва кодери отримують токени результату IterativeParser.Optimize.
    /// Для Huffman повторно кодуємо токени й перевіряємо еквівалентність
    /// уже отриманому результату IterativeParser, щоб виключити зміну парсингу.
    /// </summary>
    public static Report RunDirectory(
        string corpusDirectory, int blockSize = 65536,
        int maxIterations = 3, int maxChain = 256,
        Action<ProgressRow>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maxIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChain);
        if (!Directory.Exists(corpusDirectory))
            throw new DirectoryNotFoundException(corpusDirectory);

        var files = new List<FileRow>();
        for (int group = 0; group < 7; group++)
        {
            string groupPath = Path.Combine(corpusDirectory,
                group.ToString(CultureInfo.InvariantCulture));
            if (!Directory.Exists(groupPath))
                throw new DirectoryNotFoundException($"Відсутня група корпусу {group}: {groupPath}");

            foreach (string path in Directory.EnumerateFiles(groupPath, "*", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                byte[] input = File.ReadAllBytes(path);
                string name = Path.GetRelativePath(groupPath, path).Replace('\\', '/');
                long huffmanBytes = 0, arithmeticBytes = 0;
                long huffmanPayload = 0, arithmeticPayload = 0;
                double huffmanSeconds = 0, arithmeticSeconds = 0;
                int blocks = 0;
                int countBlocks = (int)((input.LongLength + blockSize - 1L) / blockSize);

                for (int offset = 0; offset < input.Length; offset += blockSize)
                {
                    byte[] block = input.AsSpan(offset, Math.Min(blockSize, input.Length - offset)).ToArray();
                    var optimized = IterativeParser.Optimize(block, maxIterations, maxChain);
                    var stopwatch = Stopwatch.StartNew();
                    byte[] huffman = Lz77BlockEncoder.Encode(optimized.Tokens);
                    stopwatch.Stop();
                    huffmanSeconds += stopwatch.Elapsed.TotalSeconds;
                    if (!huffman.AsSpan().SequenceEqual(optimized.Encoded))
                        throw new InvalidDataException("Huffman-код результату IterativeParser змінився.");

                    stopwatch.Restart();
                    var arithmetic = ArithmeticBlockCodec.Encode(optimized.Tokens);
                    stopwatch.Stop();
                    arithmeticSeconds += stopwatch.Elapsed.TotalSeconds;

                    // Round-trip ОБОХ кодерів. Час декодування не включений у наведені секунди.
                    if (!Lz77BlockDecoder.Decode(huffman, block.Length).AsSpan().SequenceEqual(block) ||
                        !ArithmeticBlockCodec.Decode(arithmetic.Bytes, block.Length).AsSpan().SequenceEqual(block))
                        throw new InvalidDataException($"Round-trip не збігся: {group}/{name}, блок {blocks}");

                    huffmanBytes += huffman.Length;
                    arithmeticBytes += arithmetic.Bytes.Length;
                    huffmanPayload += HuffmanPayloadLength(huffman);
                    arithmeticPayload += arithmetic.PayloadBytes;
                    blocks++;
                    progress?.Invoke(new ProgressRow(group, name, blocks, countBlocks));
                }

                files.Add(new FileRow(group, name, input.LongLength, blocks,
                    huffmanBytes, arithmeticBytes, huffmanPayload, arithmeticPayload,
                    huffmanSeconds, arithmeticSeconds));
            }
        }
        return new Report(files);
    }

    private static int HuffmanPayloadLength(ReadOnlySpan<byte> coded)
    {
        int pos = 0;
        foreach (int alphabetSize in new[] { 257, 256, 16 })
        {
            if (coded.Length - pos < 2) throw new InvalidDataException("Немає Huffman-таблиці.");
            int count = (coded[pos] << 8) | coded[pos + 1];
            if (count > alphabetSize || 2L + 3L * count > coded.Length - pos)
                throw new InvalidDataException("Некоректна Huffman-таблиця.");
            pos += 2 + 3 * count;
        }
        if (coded.Length - pos < 8) throw new InvalidDataException("Немає заголовка блока.");
        return coded.Length - pos - 8;
    }
}
