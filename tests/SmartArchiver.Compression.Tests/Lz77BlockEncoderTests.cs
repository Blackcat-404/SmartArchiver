using System.Text;
using Xunit;

namespace SmartArchiver.Compression.Tests;

public class Lz77BlockEncoderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Greedy_RoundTrip_RepeatedBytes(int count)
    {
        // Перевіряємо відновлення блока з однакових байтів.
        byte[] input = Enumerable.Repeat((byte)'A', count).ToArray();
        AssertRoundTrip(input, GreedyParser.Parse(input));
    }

    [Fact]
    public void Greedy_RoundTrip_TextWithMatches()
    {
        // Перевіряємо літерали та повторні посилання в одному блоці.
        byte[] input = Encoding.UTF8.GetBytes(
            "abcabcabcXYZabcabcabcXYZabcabcabcXYZ");
        AssertRoundTrip(input, GreedyParser.Parse(input));
    }

    [Fact]
    public void Lazy_RoundTrip_TextWithMatches()
    {
        // Перевіряємо сумісність енкодера з лінивим розбором.
        byte[] input = Encoding.UTF8.GetBytes(
            "abcQbcdefRabcdef abcQbcdefRabcdef abcQbcdefRabcdef");
        AssertRoundTrip(input, LazyParser.Parse(input));
    }

    [Fact]
    public void Greedy_RoundTrip_RandomData()
    {
        // Випадкові дані перевіряють велику кількість різних літералів.
        byte[] input = new byte[4096];
        new Random(12345).NextBytes(input);
        AssertRoundTrip(input, GreedyParser.Parse(input));
    }

    [Fact]
    public void Encoder_RoundTrip_OverlappingMatch()
    {
        // Distance=1 і Length=258 перевіряють перекривне копіювання.
        var tokens = new List<Lz77Token>
        {
            Lz77Token.Lit((byte)'A'),
            Lz77Token.Match(1, 258)
        };
        byte[] coded = Lz77BlockEncoder.Encode(tokens);
        byte[] decoded = Lz77BlockDecoder.Decode(coded, 259);
        Assert.Equal(Enumerable.Repeat((byte)'A', 259), decoded);
    }

    [Fact]
    public void Encoder_RoundTrip_VariousDistances()
    {
        // Перевіряємо коди відстаней біля меж степенів двійки.
        foreach (int distance in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33 })
        {
            var tokens = new List<Lz77Token>();
            for (int i = 0; i < distance; i++)
                tokens.Add(Lz77Token.Lit((byte)('a' + i % 26)));
            tokens.Add(Lz77Token.Match(distance, 3));

            byte[] coded = Lz77BlockEncoder.Encode(tokens);
            byte[] decoded = Lz77BlockDecoder.Decode(coded, distance + 3);
            for (int i = 0; i < 3; i++)
                Assert.Equal((byte)('a' + (i % distance) % 26), decoded[distance + i]);
        }
    }

    [Fact]
    public void Encoder_CompressesRepeatedBytesManyTimes()
    {
        // За умовою пункту 4 повторювані байти мають стискатися в рази.
        byte[] input = Enumerable.Repeat((byte)'A', 10_000).ToArray();
        byte[] coded = Lz77BlockEncoder.Encode(GreedyParser.Parse(input));

        Assert.True(coded.Length < input.Length / 3,
            $"Очікувалося стиснення щонайменше в 3 рази: {input.Length} -> {coded.Length} байтів.");
        Assert.Equal(input, Lz77BlockDecoder.Decode(coded, input.Length));
    }

    [Fact]
    public void Encoder_RejectsEmptyTokenBlock()
    {
        // Порожній блок не є коректним вхідним блоком енкодера.
        Assert.Throws<ArgumentException>(() =>
            Lz77BlockEncoder.Encode(Array.Empty<Lz77Token>()));
    }

    private static void AssertRoundTrip(byte[] input, IReadOnlyList<Lz77Token> tokens)
    {
        // Перевіряємо весь шлях: токени -> бітовий потік -> байти.
        byte[] coded = Lz77BlockEncoder.Encode(tokens);
        byte[] decoded = Lz77BlockDecoder.Decode(coded, input.Length);
        Assert.Equal(input, decoded);
    }
}
