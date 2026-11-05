using SmartArchiver.Compression;
using Xunit;
using SmartArchiver.Core;

namespace SmartArchiver.Compression.Tests;

public sealed class ArithmeticBlockCodecTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("ABABABABABABABABABABABABABABABAB")]
    [InlineData("hello world! hello world! hello world!")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Arithmetic_RoundTrip_WithSameLz77Tokens(string text)
    {
        byte[] data = System.Text.Encoding.UTF8.GetBytes(text);
        var tokens = GreedyParser.Parse(data);
        var encoded = ArithmeticBlockCodec.Encode(tokens);
        Assert.Equal(data, ArithmeticBlockCodec.Decode(encoded.Bytes, data.Length));
        Assert.True(encoded.HeaderBytes > 0);
        Assert.Equal(encoded.HeaderBytes + encoded.PayloadBytes, encoded.Bytes.Length);
    }

    [Fact]
    public void Arithmetic_RoundTrip_RandomBytes()
    {
        var random = new Random(2026);
        foreach (int size in new[] { 1, 2, 7, 32, 257, 8192, 20000 })
        {
            byte[] data = new byte[size];
            random.NextBytes(data);
            var tokens = GreedyParser.Parse(data);
            var encoded = ArithmeticBlockCodec.Encode(tokens);
            Assert.Equal(data, ArithmeticBlockCodec.Decode(encoded.Bytes, data.Length));
        }
    }

    [Fact]
    public void Arithmetic_RoundTrip_OverlappingMatches()
    {
        byte[] data = Enumerable.Repeat((byte)42, 16384).ToArray();
        var tokens = GreedyParser.Parse(data);
        Assert.Contains(tokens, t => t.IsMatch && t.Distance < t.Length);
        var encoded = ArithmeticBlockCodec.Encode(tokens);
        Assert.Equal(data, ArithmeticBlockCodec.Decode(encoded.Bytes, data.Length));
    }

    [Fact]
    public void Arithmetic_RoundTrip_IterativeDpTokens()
    {
        byte[] data = System.Text.Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("abcabcabc---some text---", 20)));
        var tokens = IterativeParser.Optimize(data, maxIterations: 2, maxChain: 32).Tokens;
        var encoded = ArithmeticBlockCodec.Encode(tokens);
        Assert.Equal(data, ArithmeticBlockCodec.Decode(encoded.Bytes, data.Length));
    }

    [Fact]
    public void Arithmetic_IsDeterministic()
    {
        byte[] data = System.Text.Encoding.ASCII.GetBytes("aabbaabbaabbaaabbaabbaaa");
        var tokens = GreedyParser.Parse(data);
        Assert.Equal(ArithmeticBlockCodec.Encode(tokens).Bytes,
            ArithmeticBlockCodec.Encode(tokens).Bytes);
    }

    [Fact]
    public void Arithmetic_RejectsEmptyTokensAndTruncatedBlock()
    {
        Assert.Throws<ArgumentException>(() => ArithmeticBlockCodec.Encode(Array.Empty<Lz77Token>()));
        var encoded = ArithmeticBlockCodec.Encode([Lz77Token.Lit((byte)'A')]);
        Assert.ThrowsAny<Exception>(() => ArithmeticBlockCodec.Decode(encoded.Bytes.AsSpan(0, 5), 1));
    }

    [Fact]
    public void Arithmetic_NoFourByteZeroTrailer_ForSingleLiteral()
    {
        var encoded = ArithmeticBlockCodec.Encode([Lz77Token.Lit((byte)'A')]);
        Assert.InRange(encoded.PayloadBytes, 1, 4);
        Assert.Equal(new byte[] { (byte)'A' }, ArithmeticBlockCodec.Decode(encoded.Bytes, 1));
    }

    [Fact]
    public void BitReader_ZeroExtension_DoesNotChangeStrictReadBit()
    {
        var virtualReader = new BitReader(new byte[] { 0b10100000 }, 0);
        Assert.Equal(1, virtualReader.ReadBitOrZero());
        Assert.Equal(0, virtualReader.ReadBitOrZero());
        Assert.Equal(1, virtualReader.ReadBitOrZero());
        for (int i = 0; i < 12; i++)
            Assert.Equal(0, virtualReader.ReadBitOrZero());

        var strictReader = new BitReader(new byte[] { 0 }, 0);
        for (int i = 0; i < 8; i++)
            strictReader.ReadBit();
        bool threw = false;
        try
        {
            strictReader.ReadBit();
        }
        catch (ArchiveException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact]
    public void Arithmetic_RejectsPayloadThatIsPhysicallyTruncated()
    {
        var encoded = ArithmeticBlockCodec.Encode([Lz77Token.Lit((byte)'A')]);
        byte[] truncated = encoded.Bytes.AsSpan(0, encoded.Bytes.Length - 1).ToArray();
        Assert.Throws<InvalidDataException>(() => ArithmeticBlockCodec.Decode(truncated, 1));
    }

    [Fact]
    public void Benchmark_ShortCorpus_SevenGroups_NoCsv()
    {
        string folder = Path.Combine(Path.GetTempPath(), "smartarchiver-arithmetic-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (int g = 0; g < 7; g++)
            {
                string group = Path.Combine(folder, g.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(group);
                File.WriteAllBytes(Path.Combine(group, "test.bin"),
                    Enumerable.Repeat((byte)(65 + g), 80).ToArray());
            }
            var result = ArithmeticBenchmark.RunDirectory(folder,
                blockSize: 128, maxIterations: 1, maxChain: 8);
            Assert.Equal(7, result.Files.Count);
            Assert.True(result.HuffmanBytes > 0);
            Assert.True(result.ArithmeticBytes > 0);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
