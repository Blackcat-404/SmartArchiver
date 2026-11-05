using System.Diagnostics;

namespace SmartArchiver.Compression;

/// <summary>
/// CP3 №2: DP → Huffman → DP. Кожного разу перевіряється фактичний
/// Lz77BlockEncoder.Encode, включно з таблицями. Формат блока не змінюється.
/// </summary>
internal static class IterativeParser
{
    internal sealed record Step(
        int Iteration,
        int CandidateBytes,
        int AcceptedBytes,
        int GainBytes,
        long EstimatedBits,
        int TokenCount,
        bool Accepted,
        double ElapsedSeconds);

    internal sealed record Result(
        IReadOnlyList<Lz77Token> Tokens,
        byte[] Encoded,
        IReadOnlyList<Step> History);

    /// <summary>
    /// Ітерація 0 — звичайний DP. Наступні ітерації приймаються тільки якщо
    /// фактичний розмір строго зменшився. Список History містить і відхилений
    /// останній кандидат, якщо він був.
    /// </summary>
    internal static Result Optimize(
        byte[] block,
        int maxIterations = 3,
        int maxChain = MatchFinder.DefaultMaxChain)
        => Optimize(block, maxIterations, new Lz77SearchOptions { SearchDepth = maxChain });

    internal static Result Optimize(
        byte[] block,
        int maxIterations,
        Lz77SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(maxIterations);
        if (block.Length == 0)
            throw new ArgumentException("Порожній блок не кодується Huffman.", nameof(block));

        var watch = Stopwatch.StartNew();
        List<Lz77Token> bestTokens = DpParser.Parse(block, ParseCostModel.Fixed(), options);
        byte[] bestBytes = Lz77BlockEncoder.Encode(bestTokens);
        watch.Stop();

        var history = new List<Step>
        {
            new(0, bestBytes.Length, bestBytes.Length, 0,
                ParseCostModel.Fixed().CalculateCost(bestTokens),
                bestTokens.Count, true, watch.Elapsed.TotalSeconds)
        };

        for (int iteration = 1; iteration <= maxIterations; iteration++)
        {
            watch.Restart();
            // Згладжені частоти тільки для оцінювання нових токенів.
            ParseCostModel model = ParseCostModel.FromTokens(bestTokens);
            List<Lz77Token> candidate = DpParser.Parse(block, model, options);
            long estimatedBits = model.CalculateCost(candidate);

            // Навіть якщо токени не змінилися, зберігаємо крок у журналі.
            bool identical = candidate.SequenceEqual(bestTokens);
            byte[] coded = identical ? bestBytes : Lz77BlockEncoder.Encode(candidate);
            bool accepted = !identical && coded.Length < bestBytes.Length;
            int previousBest = bestBytes.Length;
            if (accepted)
            {
                bestTokens = candidate;
                bestBytes = coded;
            }
            watch.Stop();
            history.Add(new Step(
                iteration, coded.Length, bestBytes.Length,
                previousBest - bestBytes.Length, estimatedBits,
                candidate.Count, accepted, watch.Elapsed.TotalSeconds));

            // Фіксована точка, відсутність покращення або maxIterations.
            if (identical || !accepted)
                break;
        }

        return new Result(bestTokens, bestBytes, history);
    }
}
