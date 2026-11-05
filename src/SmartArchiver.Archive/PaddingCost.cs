namespace SmartArchiver.Archive;

public sealed record PaddingCostResult(long OriginalBits, long PaddedBits, long ExtraBits, double OverheadPercent);

public static class PaddingCost
{
    public static PaddingCostResult Measure(IReadOnlyList<byte[]> original, IReadOnlyList<byte[]> padded)
    {
        long a = original.Sum(v => (long)v.Length * 8);
        long b = padded.Sum(v => (long)v.Length * 8);
        return new PaddingCostResult(a, b, b - a, a == 0 ? 0 : (b - a) * 100.0 / a);
    }
}
