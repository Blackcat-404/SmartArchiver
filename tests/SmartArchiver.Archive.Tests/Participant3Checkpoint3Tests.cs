using System.Security.Cryptography;
using System.Text;

namespace SmartArchiver.Archive.Tests;

public sealed class Participant3Checkpoint3Tests
{
    [Fact]
    public void HmacTamperingIsRejectedBeforePlaintextParsing()
    {
        byte[] volume = ProtectedArchive.BuildProtected(
            [new ArchiveInput("private.txt", Encoding.UTF8.GetBytes("secret"))], "password");
        volume[FormatConstants.HeaderSize + 1] ^= 0x40;
        var error = Assert.Throws<ArchiveException>(() => ProtectedArchive.ReadProtected(volume, "password"));
        Assert.Equal(ErrorCode.E_HMAC, error.Code);
        Assert.DoesNotContain("password", error.Message);
    }

    [Fact]
    public void EncryptedDataCompressesWorse()
    {
        var sample = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("AAAAABBBBBCCCCCDDDDD", 5000)));
        var trial = EncryptionCompressionExperiment.Run("repetitive", sample);
        Assert.True(trial.NormalRatio > trial.EncryptedRatio * 5);
    }

    [Fact]
    public void RsaOaepSharesSameSecretAndRestoresArchive()
    {
        var (publicKey, privateKey) = RecipientKeyExchange.GenerateRecipientKeys();
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        Assert.Equal(secret, RecipientKeyExchange.UnwrapSecret(
            RecipientKeyExchange.WrapSecret(secret, publicKey), privateKey));
        var sent = RecipientKeyExchange.PackForRecipient(
            [new ArchiveInput("colleague.txt", Encoding.UTF8.GetBytes("hello"))], publicKey);
        var restored = RecipientKeyExchange.UnpackFromSender(sent.Volumes, sent.WrappedSecret, privateKey);
        Assert.Equal("hello", Encoding.UTF8.GetString(Assert.Single(restored).Data));
    }

    [Fact]
    public void PaddingConcealsExactLengthAndReportsBitCost()
    {
        var plain = new ArchiveWriter().Build([new ArchiveInput("a.txt", new byte[127])]);
        var normal = ProtectedArchive.ProtectVolumes([plain], "password");
        var padded = ProtectedArchive.ProtectVolumes([plain], "password", padTo: 1024);
        Assert.True(VolumeHeader.Parse(padded[0]).CiphertextLength % 1024u == 0u);
        Assert.True(PaddingCost.Measure(normal, padded).ExtraBits > 0);
        Assert.Equal(127, Assert.Single(ProtectedArchive.ReadProtectedVolumes(padded, "password", padded: true)).Data.Length);
        byte[] corrupt = (byte[])padded[0].Clone();
        corrupt[FormatConstants.HeaderSize + 2] ^= 1;
        Assert.Equal(ErrorCode.E_HMAC,
            Assert.Throws<ArchiveException>(() => ProtectedArchive.ReadProtectedVolumes([corrupt], "password", padded: true)).Code);
    }
}
