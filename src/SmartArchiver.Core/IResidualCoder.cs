namespace SmartArchiver.Core;

/// <summary>
/// Contract between next-byte prediction and the archive: data is replaced by prediction residuals before
/// compression and restored after decompression. The archive does not need to know how the guesses are made.
/// </summary>
public interface IResidualCoder
{
    /// <summary>Residuals of the data, with whatever header the coder needs to decode them.</summary>
    byte[] Encode(ReadOnlySpan<byte> data);

    /// <exception cref="InvalidDataException">The data was not produced by this coder.</exception>
    byte[] Decode(ReadOnlySpan<byte> encoded);
}
