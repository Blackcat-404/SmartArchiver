using System.IO.Compression;
using System.Security.Cryptography;

namespace SmartArchiver.Archive;

public sealed record CompressionTrial(string Name, int InputBytes, int CompressFirstBytes,
    int EncryptFirstBytes, double NormalRatio, double EncryptedRatio);

public static class EncryptionCompressionExperiment
{
    public static CompressionTrial Run(string name, byte[] input)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] encrypted = new byte[input.Length];
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        try
        {
            encrypted = SmartArchiver.Security.ArchiveSecurity.TransformCtr(input, key, nonce);
            int normal = Compress(input);
            int encryptedSize = Compress(encrypted);
            return new CompressionTrial(name, input.Length, normal, encryptedSize,
                normal == 0 ? 0 : (double)input.Length / normal,
                encryptedSize == 0 ? 0 : (double)input.Length / encryptedSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    private static int Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return checked((int)output.Length);
    }
}
