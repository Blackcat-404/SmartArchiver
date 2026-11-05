using System.Text;
using Xunit;

namespace SmartArchiver.Compression.Tests;

public sealed class DpParserTests
{
    [Fact]
    public void DpParser_Cost_IsAlwaysLessThanOrEqual_ToGreedy()
    {
        byte[] data = TestData.RandomBytes(10000, seed: 42);

        var greedyTokens = GreedyParser.Parse(data);
        var dpTokens = DpParser.Parse(data);

        long greedyCost = DpParser.CalculateCost(greedyTokens);
        long dpCost = DpParser.CalculateCost(dpTokens);

        Assert.True(dpCost <= greedyCost, $"DP Cost ({dpCost}) має бути <= Greedy Cost ({greedyCost})");
    }
    [Fact]
    public void EmptyBlock_RoundTrip()
    {
        Assert.Empty(DpParser.Parse([]));
    }

    [Theory]
    [InlineData(1, 17)]
    [InlineData(2, 17)]
    [InlineData(3, 18)]
    [InlineData(4, 18)]
    [InlineData(5, 19)]
    [InlineData(32768, 31)]
    public void MatchCost_UsesFixedModel(int distance, int expected)
    {
        Assert.Equal(expected, DpParser.MatchCost(distance));
    }

    [Fact]
    public void MatchCost_RejectsInvalidDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DpParser.MatchCost(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DpParser.MatchCost(32769));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abracadabra abracadabra abracadabra")]
    [InlineData("abcQbcdefRabcdef")]
    public void Text_RoundTrip_AndNotWorseThanGreedy(string text)
    {
        byte[] block = Encoding.UTF8.GetBytes(text);
        Verify(block);
    }

    [Fact]
    public void RepeatedBytes_RoundTrip_AndNotWorseThanGreedy()
    {
        Verify(Enumerable.Repeat((byte)'A', 4096).ToArray());
    }

    [Fact]
    public void RandomBytes_RoundTrip_AndNotWorseThanGreedy()
    {
        var random = new Random(12345);
        byte[] block = new byte[8192];
        random.NextBytes(block);
        Verify(block);
    }

    [Fact]
    public void MixedPatterns_RoundTrip_AndNotWorseThanGreedy()
    {
        var random = new Random(2026);
        byte[] block = new byte[8192];
        random.NextBytes(block);
        for (int i = 700; i < 4500; i++)
            block[i] = block[i - 500];
        Verify(block);
    }

    [Fact]
    public void NullAndInvalidMaxChain_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => DpParser.Parse(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => DpParser.Parse([], 0));
    }

    private static void Verify(byte[] input)
    {
        List<Lz77Token> dp = DpParser.Parse(input);
        List<Lz77Token> greedy = GreedyParser.Parse(input);

        // Результат має збігатися з початковими даними побайтово.
        Assert.Equal(input, Lz77Decoder.Decode(dp));

        // Порівнюємо саме фіксовану модель вартості, а не розмір Huffman-блока.
        Assert.True(DpParser.CalculateCost(dp) <= DpParser.CalculateCost(greedy),
            $"DP: {DpParser.CalculateCost(dp)}, Greedy: {DpParser.CalculateCost(greedy)}");
    }
}
