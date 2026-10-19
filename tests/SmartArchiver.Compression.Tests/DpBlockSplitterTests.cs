using System.Text;
using Xunit;

namespace SmartArchiver.Compression.Tests;

public class DpBlockSplitterTests
{
    [Fact]
    public void EmptyInput_ReturnsNoBlocks()
    {
        // Порожній потік не повинен утворювати блоків.
        Assert.Empty(DpBlockSplitter.Split([]));
        Assert.Empty(DpBlockSplitter.SplitFixed([]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(4096)]
    [InlineData(12_000)]
    public void RepeatedBytes_DpNotWorseThanFixed(int length)
    {
        // Перевіряємо D1 №14 на повторюваних даних різної довжини.
        byte[] input = Enumerable.Repeat((byte)'A', length).ToArray();
        AssertNotWorseAndRoundTrip(input, 4096, 1024);
    }

    [Fact]
    public void RandomData_DpNotWorseThanFixed()
    {
        // Уже малостискувані дані мають відновитися без втрат.
        byte[] input = new byte[8193];
        new Random(123).NextBytes(input);
        AssertNotWorseAndRoundTrip(input, 4096, 1024);
    }

    [Fact]
    public void MixedData_DpNotWorseThanFixed()
    {
        // Чергуємо повтори, текст та випадкові дані.
        byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abcabcXYZ", 400)));
        byte[] random = new byte[2500];
        new Random(42).NextBytes(random);
        byte[] input = text.Concat(random).Concat(new byte[4500]).ToArray();
        AssertNotWorseAndRoundTrip(input, 4096, 1024);
    }

    [Fact]
    public void InvalidBoundaryStep_IsRejected()
    {
        // Крок повинен ділити фіксований розмір блока без остачі.
        Assert.Throws<ArgumentException>(() => DpBlockSplitter.Split(new byte[100], 4096, 1500));
    }

    private static void AssertNotWorseAndRoundTrip(byte[] input, int blockSize, int step)
    {
        List<EncodedLz77Block> baseline = DpBlockSplitter.SplitFixed(input, blockSize);
        List<EncodedLz77Block> optimized = DpBlockSplitter.Split(input, blockSize, step);

        long fixedBytes = DpBlockSplitter.TotalBytes(baseline);
        long dpBytes = DpBlockSplitter.TotalBytes(optimized);
        Assert.True(dpBytes <= fixedBytes,
            $"DP має бути не гіршим за фіксовані блоки: DP={dpBytes}, fixed={fixedBytes}.");

        // Кожен блок має незалежний Huffman-потік, який можна декодувати.
        var restored = new List<byte>();
        int expectedOffset = 0;
        foreach (EncodedLz77Block block in optimized)
        {
            Assert.Equal(expectedOffset, block.Offset);
            Assert.InRange(block.RawLength, 1, blockSize);
            restored.AddRange(Lz77BlockDecoder.Decode(block.CodedBytes, block.RawLength));
            expectedOffset += block.RawLength;
        }
        Assert.Equal(input.Length, expectedOffset);
        Assert.Equal(input, restored.ToArray());
    }
}
