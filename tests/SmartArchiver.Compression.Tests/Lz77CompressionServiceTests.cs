using System.Text;
using Xunit;

namespace SmartArchiver.Compression.Tests;

public sealed class Lz77CompressionServiceTests
{
    [Theory]
    [InlineData(Lz77ParseMode.Greedy)]
    [InlineData(Lz77ParseMode.Lazy)]
    [InlineData(Lz77ParseMode.Dp)]
    public void EncodeBlock_RoundTrip_AllParsers(Lz77ParseMode mode)
    {
        // Кожен парсер створює блок, який читає спільний декодер.
        byte[] original = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abcabcXYZ", 100)));
        byte[] coded = Lz77CompressionService.EncodeBlock(original, mode);
        Assert.Equal(original, Lz77CompressionService.DecodeBlock(coded, original.Length));
    }

    [Theory]
    [InlineData(Lz77ParseMode.Greedy)]
    [InlineData(Lz77ParseMode.Lazy)]
    [InlineData(Lz77ParseMode.Dp)]
    public void FixedBlocks_RoundTrip_AllParsers(Lz77ParseMode mode)
    {
        // Блоки відновлюються без втрат та мають правильні зміщення.
        byte[] original = new byte[6500];
        new Random(123).NextBytes(original);
        AssertRoundTrip(original, Lz77CompressionService.PrepareFixedBlocks(original, mode, 2048));
    }

    [Fact]
    public void StoredBlock_IsChosen_WhenCompressionIsBigger()
    {
        // Службові таблиці збільшують крихітний блок, тому зберігаємо сирі дані.
        byte[] original = [42];
        Lz77PreparedBlock block = Assert.Single(Lz77CompressionService.PrepareFixedBlocks(original));
        Assert.True(block.IsStored);
        Assert.Equal(original, block.CodedBytes);
    }

    [Fact]
    public void DpBlocks_RoundTrip()
    {
        // Перевіряємо інтеграційний контракт із DP-розбиттям.
        byte[] original = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("repeated patterns", 150)));
        AssertRoundTrip(original, Lz77CompressionService.PrepareDpBlocks(original, 1024, 256));
    }

    [Fact]
    public void EmptyData_ProducesNoBlocks()
    {
        Assert.Empty(Lz77CompressionService.PrepareFixedBlocks([]));
        Assert.Empty(Lz77CompressionService.PrepareDpBlocks([]));
    }

    private static void AssertRoundTrip(byte[] expected, IReadOnlyList<Lz77PreparedBlock> blocks)
    {
        var restored = new List<byte>();
        int offset = 0;
        foreach (Lz77PreparedBlock block in blocks)
        {
            Assert.Equal(offset, block.Offset);
            Assert.Equal(block.CodedBytes.Length, block.CodedLength);
            restored.AddRange(Lz77CompressionService.RestorePreparedBlock(block));
            offset += block.RawLength;
        }
        Assert.Equal(expected.Length, offset);
        Assert.Equal(expected, restored.ToArray());
    }
}
