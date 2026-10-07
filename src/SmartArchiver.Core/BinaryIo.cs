using System.Buffers.Binary;

namespace SmartArchiver.Core;

/// <summary>Запис big-endian чисел у потік.</summary>
internal static class BinaryIo
{
    public static void WriteU16(Stream s, ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        s.Write(b);
    }

    public static void WriteU32(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
    }

    public static void WriteU64(Stream s, ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, value);
        s.Write(b);
    }
}

/// <summary>
/// Читач payload з перевіркою меж. Вихід за межі означає структурно некоректний payload,
/// тому кидає E_PAYLOAD, а не IndexOutOfRange чи ArgumentOutOfRange.
/// </summary>
internal sealed class ByteReader
{
    private readonly byte[] _buffer;
    private readonly int _end;
    private int _position;

    public ByteReader(byte[] buffer, int start, int length)
    {
        _buffer = buffer;
        _position = start;
        _end = start + length;
    }

    public int Position => _position;
    public int Remaining => _end - _position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Payload обірвано посеред запису.");
        var span = new ReadOnlySpan<byte>(_buffer, _position, count);
        _position += count;
        return span;
    }

    public byte ReadU8() => Take(1)[0];
    public ushort ReadU16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
    public uint ReadU32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
    public ulong ReadU64() => BinaryPrimitives.ReadUInt64BigEndian(Take(8));
    public ReadOnlySpan<byte> ReadBytes(int count) => Take(count);
}
