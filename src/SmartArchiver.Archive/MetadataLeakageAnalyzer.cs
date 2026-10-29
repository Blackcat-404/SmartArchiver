using System.Text;

namespace SmartArchiver.Archive;

/// <summary>Показує лише інформацію, доступну без пароля з відкритого формату.</summary>
public sealed record MetadataLeakageReport(int VolumeCount, long TotalEncryptedBytes,
    IReadOnlyList<uint> CiphertextLengths, bool RevealsVolumeOrder,
    bool RevealsExactFileNames, bool RevealsFileCrc32, bool RevealsFileTypes);

public static class MetadataLeakageAnalyzer
{
    public static MetadataLeakageReport Analyze(IReadOnlyList<byte[]> volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        if (volumes.Count == 0) throw new ArgumentException("Потрібен хоча б один том.", nameof(volumes));
        var lengths = new List<uint>();
        var numbers = new HashSet<ushort>();
        ushort expected = VolumeHeader.Parse(volumes[0]).VolumeCount;
        long total = 0;
        foreach (byte[] volume in volumes)
        {
            VolumeHeader header = VolumeHeader.Parse(volume);
            if (header.VolumeCount != expected || !numbers.Add(header.VolumeIndex) ||
                volume.LongLength != FormatConstants.HeaderSize + (long)header.CiphertextLength + FormatConstants.TagSize)
                throw new ArgumentException("Некоректний набір зашифрованих томів.", nameof(volumes));
            lengths.Add(header.CiphertextLength);
            total += volume.LongLength;
        }
        if (numbers.Count != expected || Enumerable.Range(0, expected).Any(i => !numbers.Contains((ushort)i)))
            throw new ArgumentException("Набір томів неповний.", nameof(volumes));
        return new MetadataLeakageReport(expected, total, lengths.AsReadOnly(), true, false, false, false);
    }
}
