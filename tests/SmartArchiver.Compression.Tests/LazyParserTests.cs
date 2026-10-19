using System;
using System.Linq;

namespace SmartArchiver.Compression.Tests;

public class LazyParserTests
{
    [Fact]
    public void EmptyBlock_ProducesNoTokens()
    {
        Assert.Empty(LazyParser.Parse(Array.Empty<byte>()));
    }

    [Fact]
    public void ShortBlock_ProducesOnlyLiterals()
    {
        byte[] input = { 10, 20 };
        var tokens = LazyParser.Parse(input);

        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, token => Assert.False(token.IsMatch));
        Assert.Equal(input, Lz77Decoder.Decode(tokens));
    }

    [Fact]
    public void RepeatedBytes_RoundTripAndUsesMatches()
    {
        byte[] input = Enumerable.Repeat((byte)'A', 1000).ToArray();
        var tokens = LazyParser.Parse(input);

        Assert.Contains(tokens, token => token.IsMatch);
        Assert.Equal(input, Lz77Decoder.Decode(tokens));
    }

    [Fact]
    public void Text_RoundTrips()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(
            "abcabcabc--abcabcabc--abcabcabc");

        Assert.Equal(input, Lz77Decoder.Decode(LazyParser.Parse(input)));
    }

    [Fact]
    public void RandomData_RoundTrips()
    {
        byte[] input = new byte[8192];
        new Random(12345).NextBytes(input);

        Assert.Equal(input, Lz77Decoder.Decode(LazyParser.Parse(input)));
    }

    [Fact]
    public void LongerNextMatch_ChoosesLiteral()
    {
        // На позиції 10 знайдено збіг "abc" довжиною 3.
        // На позиції 11 знайдено збіг "bcdef" довжиною 5.
        // Оскільки наступний збіг довший на 2 байти,
        // Lazy має записати літерал 'a', а потім посилання.
        byte[] input = System.Text.Encoding.ASCII.GetBytes(
            "abcQbcdefRabcdef");

        var tokens = LazyParser.Parse(input, minLazyGain: 2);

        // Перевіряємо правильність відновлення початкових даних.
        Assert.Equal(input, Lz77Decoder.Decode(tokens));

        int offset = 0;
        bool deferred = false;

        foreach (var token in tokens)
        {
            if (offset == 10)
            {
                // На позиції 10 очікуємо літерал замість збігу.
                Assert.False(token.IsMatch);
                Assert.Equal((byte)'a', token.Literal);
                deferred = true;
            }

            if (offset == 11)
            {
                // На наступній позиції очікуємо довший збіг.
                Assert.True(token.IsMatch);
                Assert.Equal(5, token.Length);
            }

            offset += token.IsMatch ? token.Length : 1;
        }

        Assert.True(deferred, "Lazy має відкласти збіг у позиції 10.");
    }


    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => LazyParser.Parse(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => LazyParser.Parse(Array.Empty<byte>(), maxChain: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LazyParser.Parse(Array.Empty<byte>(), minLazyGain: 0));
    }

    [Fact]
    public void EveryMatch_RespectsLz77Limits()
    {
        byte[] input = Enumerable.Repeat((byte)'Z', 2000).ToArray();
        var tokens = LazyParser.Parse(input);

        foreach (var token in tokens.Where(token => token.IsMatch))
        {
            Assert.InRange(token.Distance, 1, Lz77Limits.Window);
            Assert.InRange(token.Length, Lz77Limits.MinMatch, Lz77Limits.MaxMatch);
        }
    }
}
