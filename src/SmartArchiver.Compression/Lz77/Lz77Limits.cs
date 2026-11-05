namespace SmartArchiver.Compression;

/// <summary>Межі LZ77 з методички 10.19 і специфікації. Єдине джерело для кодера і декодера.</summary>
internal static class Lz77Limits
{
    public const int MinMatch = 3;
    public const int MaxMatch = 258;
    public const int Window = 32768;
}