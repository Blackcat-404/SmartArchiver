using System.Text;
using SmartArchiver.Compression;

namespace SmartArchiver.Compression.Tests;

public class GreedyParserTests
{
    private static void AssertRoundTrip(byte[] input)
    {
        // Перевіряємо, що декодер відновлює всі байти без змін.
        var tokens = GreedyParser.Parse(input);
        Assert.Equal(input, Lz77Decoder.Decode(tokens));
    }

    [Fact]
    public void EmptyInput_ProducesNoTokens()
    {
        Assert.Empty(GreedyParser.Parse(Array.Empty<byte>()));
    }

    [Fact]
    public void ShortInput_ProducesOnlyLiterals()
    {
        var tokens = GreedyParser.Parse(Encoding.ASCII.GetBytes("AB"));
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, token => Assert.False(token.IsMatch));
    }

    [Fact]
    public void RepeatedBytes_ProducesMatch()
    {
        var input = Enumerable.Repeat((byte)'A', 1000).ToArray();
        var tokens = GreedyParser.Parse(input);

        Assert.Contains(tokens, token => token.IsMatch);
        AssertRoundTrip(input);
    }

    [Fact]
    public void RepeatedText_RoundTrips()
    {
        AssertRoundTrip(Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("hello world ", 100))));
    }

    [Fact]
    public void RandomBytes_RoundTrip()
    {
        var input = new byte[10000];
        new Random(12345).NextBytes(input);
        AssertRoundTrip(input);
    }

    [Fact]
    public void Matches_StayWithinFormatLimits()
    {
        var input = Enumerable.Repeat((byte)'X', 2000).ToArray();
        var tokens = GreedyParser.Parse(input);

        foreach (var token in tokens.Where(t => t.IsMatch))
        {
            // Усі посилання мають відповідати обмеженням LZ77.
            Assert.InRange(token.Distance, 1, Lz77Limits.Window);
            Assert.InRange(token.Length, Lz77Limits.MinMatch, Lz77Limits.MaxMatch);
        }
    }

    [Fact]
    public void InvalidMaxChain_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GreedyParser.Parse(new byte[] { 1, 2, 3 }, 0));
    }

    [Fact]
    public void NullInput_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => GreedyParser.Parse(null!));
    }
}
