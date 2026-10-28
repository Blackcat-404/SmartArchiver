using System.Security.Cryptography;
using SmartArchiver.Compression;
using SmartArchiver.Security;

namespace SmartArchiver.Archive;

/// <param name="CompressThenEncryptBytes">Compressed blocks (LZ77 + Huffman, blocks without gain stored), then AES-256-CTR.</param>
/// <param name="EncryptThenCompressBytes">AES-256-CTR, then the same compression with the same fallback to stored blocks.</param>
/// <param name="EncryptThenCompressCodedBytes">The same, but every block kept as the coder produced it, without the fallback.</param>
/// <param name="BothRestored">Both orders decrypt and decompress back to the original bytes.</param>
public sealed record EncryptionOrderMeasurement(
    int Group,
    long OriginalBytes,
    long CompressThenEncryptBytes,
    long EncryptThenCompressBytes,
    long EncryptThenCompressCodedBytes,
    bool BothRestored);

/// <summary>
/// Experiment "compress then encrypt against the reverse order" with the archive's own compression (LZ77 + Huffman,
/// fixed blocks, lazy parsing). Ciphertext looks random, so compressing it should gain nothing.
/// </summary>
/// <remarks>
/// Sizes are of the block data only, without volume headers, block records and HMAC: those are the same in both
/// orders. CTR does not change the length, so the result of the first order is the compressed size.
/// </remarks>
public static class EncryptionOrderComparison
{
    public static IReadOnlyList<EncryptionOrderMeasurement> Run(
        IReadOnlyList<GroupedFile> files, int blockSize = FormatConstants.DefaultBlockSize)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);

        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        try
        {
            return [.. files.Select(f => Measure(f, key, nonce, blockSize))];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static EncryptionOrderMeasurement Measure(GroupedFile file, byte[] key, byte[] nonce, int blockSize)
    {
        byte[] data = file.File.Data;

        // Compress, then encrypt; back: decrypt, then decompress.
        IReadOnlyList<Lz77PreparedBlock> compressed = Lz77CompressionService.PrepareFixedBlocks(data, Lz77ParseMode.Lazy, blockSize);
        byte[] sealedData = ArchiveSecurity.TransformCtr(Concat(compressed), key, nonce);
        byte[] firstBack = Restore(compressed, ArchiveSecurity.TransformCtr(sealedData, key, nonce));

        // Encrypt, then compress; back: decompress, then decrypt.
        byte[] ciphertext = ArchiveSecurity.TransformCtr(data, key, nonce);
        IReadOnlyList<Lz77PreparedBlock> encryptedFirst = Lz77CompressionService.PrepareFixedBlocks(ciphertext, Lz77ParseMode.Lazy, blockSize);
        IReadOnlyList<Lz77PreparedBlock> coderOnly =
            Lz77CompressionService.PrepareFixedBlocks(ciphertext, Lz77ParseMode.Lazy, blockSize, allowStored: false);
        byte[] secondBack = ArchiveSecurity.TransformCtr(Restore(encryptedFirst, Concat(encryptedFirst)), key, nonce);

        return new EncryptionOrderMeasurement(
            file.Group,
            data.Length,
            sealedData.Length,
            encryptedFirst.Sum(b => (long)b.CodedLength),
            coderOnly.Sum(b => (long)b.CodedLength),
            firstBack.AsSpan().SequenceEqual(data) && secondBack.AsSpan().SequenceEqual(data));
    }

    private static byte[] Concat(IReadOnlyList<Lz77PreparedBlock> blocks) => [.. blocks.SelectMany(b => b.CodedBytes)];

    /// <summary>Splits <paramref name="coded"/> by the block lengths and decodes every block.</summary>
    private static byte[] Restore(IReadOnlyList<Lz77PreparedBlock> blocks, byte[] coded)
    {
        var restored = new List<byte>();
        int offset = 0;
        foreach (Lz77PreparedBlock block in blocks)
        {
            byte[] part = coded.AsSpan(offset, block.CodedLength).ToArray();
            restored.AddRange(Lz77CompressionService.RestorePreparedBlock(block with { CodedBytes = part }));
            offset += block.CodedLength;
        }
        return [.. restored];
    }
}
