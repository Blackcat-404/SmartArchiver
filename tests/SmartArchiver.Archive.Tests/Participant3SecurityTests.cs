using System.Security.Cryptography;
using System.Text;

namespace SmartArchiver.Archive.Tests;

public sealed class Participant3SecurityTests
{
    private static IReadOnlyList<ArchiveInput> Files() =>
    [new ArchiveInput("private-name-abc.txt", RandomNumberGenerator.GetBytes(10000)),
     new ArchiveInput("secret-second.bin", RandomNumberGenerator.GetBytes(10000))];

    [Fact]
    public void EveryVolume_HasUniqueNonce_AndRestoresExactly()
    {
        var files = Files();
        var volumes = ProtectedArchive.BuildProtectedVolumes(files, "one password", new VolumeOptions(4000), 512);
        Assert.True(volumes.Count > 1);
        var nonces = volumes.Select(v => Convert.ToHexString(VolumeHeader.Parse(v).Nonce)).ToArray();
        Assert.Equal(nonces.Length, nonces.Distinct().Count());
        var restored = ProtectedArchive.ReadProtectedVolumes(volumes, "one password");
        foreach (var file in files)
            Assert.Equal(file.Data, Assert.Single(restored, x => x.Name == file.Name).Data);
    }

    [Fact]
    public void PrivateMetadata_NotVisibleOutsideCiphertext()
    {
        var volumes = ProtectedArchive.BuildProtectedVolumes(Files(), "secret", new VolumeOptions(4000), 512);
        foreach (byte[] volume in volumes)
        {
            var header = VolumeHeader.Parse(volume);
            Assert.Equal(volume.Length, 41L + header.CiphertextLength + 32);
            Assert.DoesNotContain("private-name-abc.txt", Encoding.UTF8.GetString(volume));
            Assert.DoesNotContain("secret-second.bin", Encoding.UTF8.GetString(volume));
        }
    }

    [Fact]
    public void LeakageReport_ExposesOnlyPublicVolumeStatistics()
    {
        var volumes = ProtectedArchive.BuildProtectedVolumes(Files(), "secret", new VolumeOptions(4000), 512);
        var report = MetadataLeakageAnalyzer.Analyze(volumes);
        Assert.Equal(volumes.Count, report.VolumeCount);
        Assert.True(report.RevealsVolumeOrder);
        Assert.False(report.RevealsExactFileNames);
        Assert.False(report.RevealsFileCrc32);
        Assert.False(report.RevealsFileTypes);
    }
}
