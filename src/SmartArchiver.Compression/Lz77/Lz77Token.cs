namespace SmartArchiver.Compression;

/// <summary>
/// Один елемент потоку LZ77: або літерал (байт як є), або посилання
/// на вже відновлені дані (distance назад, length байтів копіювати).
/// </summary>
internal readonly record struct Lz77Token(bool IsMatch, byte Literal, int Distance, int Length)
{
    public static Lz77Token Lit(byte value) => new(false, value, 0, 0);

    public static Lz77Token Match(int distance, int length) => new(true, 0, distance, length);
}