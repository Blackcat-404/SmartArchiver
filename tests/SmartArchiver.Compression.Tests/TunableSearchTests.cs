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
