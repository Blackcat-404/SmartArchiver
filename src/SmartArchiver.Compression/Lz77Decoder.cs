namespace SmartArchiver.Compression;

internal static class Lz77Decoder
{
    public const int MinMatch = 3;
    public const int MaxMatch = 258;
    public const int Window = 32768;

    public static byte[] Decode(IEnumerable<Lz77Token> tokens)
    {
        var output = new List<byte>();

        foreach (Lz77Token token in tokens)
        {
            if (!token.IsMatch)
            {
                output.Add(token.Literal);
                continue;
            }

            AppendMatch(output, token.Distance, token.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Перевіряє й копіює одне посилання. Винесено окремо, щоб той самий код
    /// використовували і токен-рівневий Decode, і Lz77BlockDecoder.
    /// </summary>
    internal static void AppendMatch(List<byte> output, int distance, int length)
    {
        if (distance <= 0 || distance > Window)
            throw new ArchiveException(ErrorCode.E_PAYLOAD,
                $"Некоректна відстань посилання: {distance}.");

        if (distance > output.Count)
            throw new ArchiveException(ErrorCode.E_PAYLOAD,
                "Посилання виходить за межі вже відновлених даних.");

        if (length < MinMatch || length > MaxMatch)
            throw new ArchiveException(ErrorCode.E_PAYLOAD,
                $"Некоректна довжина посилання: {length}.");

        int start = output.Count - distance;
        for (int i = 0; i < length; i++)
        {
            output.Add(output[start + i]);
        }
    }
}