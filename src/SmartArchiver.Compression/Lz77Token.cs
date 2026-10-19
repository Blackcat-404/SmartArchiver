namespace SmartArchiver.Compression;

/// <summary>
/// Один елемент потоку LZ77: або літерал (байт як є), або посилання
/// на вже відновлені дані (distance назад, length байтів копіювати).
/// </summary>
internal readonly struct Lz77Token
{
    public bool IsMatch { get; }
    public byte Literal { get; }
    public int Distance { get; }
    public int Length { get; }

    private Lz77Token(bool isMatch, byte literal, int distance, int length)
    {
        IsMatch = isMatch;
        Literal = literal;
        Distance = distance;
        Length = length;
    }

    public static Lz77Token Lit(byte value) => new(false, value, 0, 0);

    public static Lz77Token Match(int distance, int length) => new(true, 0, distance, length);
}