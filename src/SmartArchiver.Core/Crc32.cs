namespace SmartArchiver.Core;

/// <summary>CRC32 (IEEE 802.3, поліном 0xEDB88320). Власна реалізація: зовнішні бібліотеки в тракті не потрібні.</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Update(0, data);

    /// <summary>Продовжує обчислення: Update(Update(0, a), b) == Compute(a + b).</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = ~crc;
        foreach (byte b in data)
        {
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        }
        return ~c;
    }
}
