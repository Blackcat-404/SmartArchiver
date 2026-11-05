using System.Text;
using Xunit;

namespace SmartArchiver.Compression.Tests;

public sealed class IterativeParserTests
{
    [Fact]
    public void FixedModel_MatchesOriginalCp2Costs_AndOldApi()
    {
        var model = ParseCostModel.Fixed();
        byte[] input = Encoding.ASCII.GetBytes("ABABABABAABABAABABA");
        var oldApi = DpParser.Parse(input, maxChain: 256);
        var newApi = DpParser.Parse(input, model, maxChain: 256);
        Assert.Equal(oldApi, newApi);
        Assert.Equal(DpParser.CalculateCost(oldApi), model.CalculateCost(newApi));
        foreach (int d in new[] { 1, 2, 3, 4, 5, 32768 })
            Assert.Equal(DpParser.MatchCost(d), model.Match(3, d));
    }

    [Fact]
    public void AdaptiveModel_SeparatesDistanceOneAndTwo()
    {
        byte[] lit = Enumerable.Repeat((byte)8, 257).ToArray();
        byte[] len = Enumerable.Repeat((byte)6, 256).ToArray();
        byte[] dist = Enumerable.Repeat((byte)6, 16).ToArray();
        dist[0] = 2;
        dist[1] = 9;
        ParseCostModel model = ParseCostModel.FromLengths(lit, len, dist);
        Assert.NotEqual(model.Match(3, 1), model.Match(3, 2));
    }

    [Fact]
    public void AdaptiveDp_CostEqualsBruteForce_OnShortRandomBlocks()
    {
        var rng = new Random(20261026);
        for (int trial = 0; trial < 100; trial++)
        {
            byte[] input = new byte[rng.Next(0, 12)];
            for (int i = 0; i < input.Length; i++) input[i] = (byte)rng.Next(0, 3);
            byte[] lit = new byte[257];
            byte[] len = new byte[256];
            byte[] dist = new byte[16];
            for (int i = 0; i < lit.Length; i++) lit[i] = (byte)rng.Next(1, 13);
            for (int i = 0; i < len.Length; i++) len[i] = (byte)rng.Next(1, 13);
            for (int i = 0; i < dist.Length; i++) dist[i] = (byte)rng.Next(1, 13);
            ParseCostModel model = ParseCostModel.FromLengths(lit, len, dist);
            var actual = DpParser.Parse(input, model, maxChain: 256);
            Assert.Equal(input, Lz77Decoder.Decode(actual));
            Assert.Equal(BruteForce(input, model), model.CalculateCost(actual));
        }
    }

    private static long BruteForce(byte[] data, ParseCostModel model)
    {
        var best = new long[data.Length + 1];
        for (int pos = data.Length - 1; pos >= 0; pos--)
        {
            best[pos] = model.Literal(data[pos]) + best[pos + 1];
            for (int distance = 1; distance <= pos && distance <= Lz77Limits.Window; distance++)
            {
                int maxLen = Math.Min(Lz77Limits.MaxMatch, data.Length - pos);
                int length = 0;
                while (length < maxLen && data[pos + length] == data[pos + length - distance])
                {
                    length++;
                    if (length >= Lz77Limits.MinMatch)
                        best[pos] = Math.Min(best[pos], model.Match(length, distance) + best[pos + length]);
                }
            }
        }
        return best[0];
    }

    [Fact]
    public void ExactHuffmanCost_AccountsForHeaderAndBytePadding()
    {
        byte[] block = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("ABACABCABCABAB", 20)));
        var tokens = DpParser.Parse(block);
        byte[] coded = Lz77BlockEncoder.Encode(tokens);
        ParseCostModel model = ParseCostModel.FromTokens(tokens, smoothUnused: false);

        int litSymbols = tokens.Select(t => t.IsMatch ? Lz77BlockDecoder.MatchMarker : (int)t.Literal)
            .Distinct().Count();
        int lenSymbols = tokens.Where(t => t.IsMatch).Select(t => t.Length - Lz77Limits.MinMatch)
            .Distinct().Count();
        int distSymbols = tokens.Where(t => t.IsMatch)
            .Select(t => ParseCostModel.DistanceCode(t.Distance)).Distinct().Count();
        // Енкодер записує одну фіктивну довжину/відстань, якщо Match немає.
        if (lenSymbols == 0) lenSymbols = 1;
        if (distSymbols == 0) distSymbols = 1;
        int header = 8 + 3 * 2 + 3 * (litSymbols + lenSymbols + distSymbols);
        long bits = model.CalculateCost(tokens);
        Assert.Equal((long)coded.Length, header + (bits + 7) / 8);
    }

    [Fact]
    public void RoundTrip_NeverIncreasesBestSize_AndDeterministic()
    {
        var rng = new Random(77);
        byte[] noise = new byte[700];
        rng.NextBytes(noise);
        foreach (byte[] block in new byte[][]
        {
            [42],
            Enumerable.Repeat((byte)'X', 1500).ToArray(),
            Encoding.UTF8.GetBytes("ABABABABABCABCABCABCXYZXYZXYZ"),
            noise
        })
        {
            var first = IterativeParser.Optimize(block, maxIterations: 3, maxChain: 32);
            var second = IterativeParser.Optimize(block, maxIterations: 3, maxChain: 32);
            Assert.Equal(block, Lz77BlockDecoder.Decode(first.Encoded, block.Length));
            Assert.Equal(first.Encoded, second.Encoded);
            Assert.Equal(first.Tokens.ToArray(), second.Tokens.ToArray());
            Assert.True(first.Encoded.Length <=
                Lz77BlockEncoder.Encode(DpParser.Parse(block, maxChain: 32)).Length);
            Assert.Equal(first.Encoded.Length, first.History[^1].AcceptedBytes);
            int last = int.MaxValue;
            foreach (var step in first.History)
            {
                Assert.True(step.AcceptedBytes <= last);
                Assert.True(step.GainBytes >= 0);
                if (step.Iteration > 0 && step.Accepted)
                    Assert.True(step.AcceptedBytes < last);
                last = step.AcceptedBytes;
            }
        }
    }

    [Fact]
    public void EmptyBlockAndInvalidParameters()
    {
        Assert.Empty(DpParser.Parse([], ParseCostModel.Fixed()));
        Assert.Throws<ArgumentException>(() => IterativeParser.Optimize([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => IterativeParser.Optimize([65], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => IterativeParser.Optimize([65], 1, 0));
    }

    [Fact]
    public void PublicService_IterativeMode_RoundTrips_AndAliasesAgree()
    {
        byte[] data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("abcabcXYZ", 45)));
        byte[] compressed = Lz77CompressionService.EncodeBlock(data, Lz77ParseMode.Iterative);
        Assert.Equal(data, Lz77CompressionService.DecodeBlock(compressed, data.Length));
        Assert.Equal(compressed, Lz77CompressionService.EncodeBlock(data, Lz77ParseMode.IterativeDp));
        var blocks = Lz77CompressionService.PrepareFixedBlocks(data, Lz77ParseMode.Iterative, 256);
        Assert.Equal(data, blocks.SelectMany(Lz77CompressionService.RestorePreparedBlock).ToArray());
    }
}
