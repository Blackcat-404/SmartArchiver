using SmartArchiver.Compression;

namespace SmartArchiver.Compression.Tests;

public sealed class TunableSearchTests
{
    [Fact]
    public void WindowAndMinimumMatchAreAppliedWithoutBreakingRoundTrip()
    {
        byte[] original = Enumerable.Repeat((byte)'A', 4_000).ToArray();
        var options = new Lz77SearchOptions
        {
            SearchWindow = 1_024,
            MinMatchLength = 8,
            SearchDepth = 4,
        };

        Lz77PreparedBlock block = Assert.Single(
            Lz77CompressionService.PrepareFixedBlocks(original, Lz77ParseMode.Lazy, 4_000, searchOptions: options));

        Assert.Equal(original, Lz77CompressionService.RestorePreparedBlock(block));
        Assert.False(block.IsStored);
    }

    [Theory]
    [InlineData(Lz77ParseMode.Greedy)]
    [InlineData(Lz77ParseMode.Lazy)]
    [InlineData(Lz77ParseMode.Dp)]
    [InlineData(Lz77ParseMode.Iterative)]
    public void LiteralsOnly_FindsNoMatches_SoOnlyHuffmanShrinksTheData(Lz77ParseMode mode)
    {
        // 4000 bytes of two letters: with matches LZ77 needs a few bytes; literals alone cost at least 1 bit each.
        byte[] original = [.. Enumerable.Range(0, 4_000).Select(i => (byte)(i % 2 == 0 ? 'A' : 'B'))];

        byte[] withMatches = Lz77CompressionService.EncodeBlock(original, mode);
        byte[] literalsOnly = Lz77CompressionService.EncodeBlock(original, mode, new Lz77SearchOptions { LiteralsOnly = true });

        Assert.True(withMatches.Length < 100);
        Assert.True(literalsOnly.Length >= 4_000 / 8);
        Assert.True(literalsOnly.Length < original.Length / 4);
        Assert.Equal(original, Lz77CompressionService.DecodeBlock(literalsOnly, original.Length));
    }

    [Fact]
    public void AbandonmentThresholdCanPreferRawBlock()
    {
        byte[] original = Enumerable.Repeat((byte)'B', 4_000).ToArray();
        Lz77PreparedBlock block = Assert.Single(
            Lz77CompressionService.PrepareFixedBlocks(
                original,
                Lz77ParseMode.Greedy,
                4_000,
                searchOptions: new Lz77SearchOptions { AbandonThreshold = 1.0 }));

        Assert.True(block.IsStored);
        Assert.Equal(original.Length, block.CodedLength);
    }
}
