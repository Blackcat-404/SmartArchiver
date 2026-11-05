namespace SmartArchiver.Archive.Tests;

public class QMeasurementTests
{
    [Fact]
    public void QMetric_FollowsFormula()
    {
        QScore q = QMetric.Compute(alpha: 0.6, beta: 0.4, tRefSeconds: 1, originalBytes: 300, archiveBytes: 100, seconds: 1, allRestored: true);

        Assert.Equal(0.75, q.S, 12);
        Assert.Equal(0.5, q.T, 12);
        Assert.Equal(0.6 * 0.75 + 0.4 * 0.5, q.Q, 12);
    }

    [Fact]
    public void QMetric_IsZero_WhenFileNotRestored()
    {
        Assert.Equal(0, QMetric.Compute(0.5, 0.5, 1, 100, 50, 0.1, allRestored: false).Q);
    }

    [Fact]
    public void Measure_WholeCorpus_RestoresEveryFile_AndFillsAllFields()
    {
        var corpus = new List<(ArchiveInput, byte)>
        {
            (TestData.File("t.txt", TestData.Repetitive(30_000)), 1),
            (TestData.File("r.bin", TestData.RandomBytes(4_000, 5)), 6),
        };
        var options = new PackOptions("k", new FixedAnalyzer(FileKind.Text, CompressionStrategy.Lz77Huffman), BlockSize: 4096, MaxVolumeBytes: 6000);

        QMeasurement m = CorpusMeasurer.Measure(corpus, options, 0.5, 0.5, 1.0);

        Assert.True(m.AllRestored);
        Assert.Equal(34_000, m.OriginalBytes);
        Assert.True(m.Volumes >= 1);
        Assert.Equal(m.OriginalBytes, m.Files.Sum(f => f.OriginalBytes));
        Assert.True(m.Files[0].ArchiveBytes < m.Files[0].OriginalBytes);
        Assert.InRange(m.Score.Q, 0.0001, 1);
        Assert.Equal([1, 6], m.Files.Select(f => (int)f.Group));
    }
}
