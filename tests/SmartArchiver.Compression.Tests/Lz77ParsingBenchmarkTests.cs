using System.Text;
using Xunit;

namespace SmartArchiver.Compression.Tests;

public class Lz77ParsingBenchmarkTests
{
    [Theory]
    [InlineData(Lz77ParsingMethod.Greedy)]
    [InlineData(Lz77ParsingMethod.Lazy)]
    public void Measure_ReturnsValidSizesAndTime(Lz77ParsingMethod method)
    {
        // Обидва методи мають обробити один і той самий блок.
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abcabcXYZ", 100)));
        Lz77ParsingMeasurement result = Lz77ParsingBenchmark.Measure(input, method);

        Assert.Equal(method, result.Method);
        Assert.Equal(input.LongLength, result.OriginalBytes);
        Assert.True(result.EncodedBytes > 0);
        Assert.True(result.ElapsedSeconds >= 0);
    }

    [Fact]
    public void RepeatedBytes_CompressWellInBothMethods()
    {
        // Перевіряємо фактичний розмір блока, а не кількість токенів.
        byte[] input = Enumerable.Repeat((byte)'A', 10_000).ToArray();
        foreach (Lz77ParsingMethod method in Enum.GetValues<Lz77ParsingMethod>())
        {
            var result = Lz77ParsingBenchmark.Measure(input, method);
            Assert.True(result.EncodedBytes < input.LongLength / 3);
        }
    }

    [Fact]
    public void EmptyBlock_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Lz77ParsingBenchmark.Measure([], Lz77ParsingMethod.Greedy));
    }
}
