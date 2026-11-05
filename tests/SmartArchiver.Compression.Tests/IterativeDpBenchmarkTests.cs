using System.Text;
using Xunit;

namespace SmartArchiver.Compression.Tests;

public sealed class IterativeDpBenchmarkTests
{
    [Fact]
    public void SmallCorpus_ExportsCsv_AndNeverIncreasesAcceptedSize()
    {
        string root = Path.Combine(Path.GetTempPath(), "smartarchiver-iterative-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (int group = 0; group < 7; group++)
            {
                string dir = Path.Combine(root, group.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "case.bin"),
                    Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("ABCDABCDEF", 18))));
            }
            var report = IterativeDpBenchmark.RunDirectory(root, blockSize: 256, maxIterations: 2, maxChain: 32);
            Assert.Equal(7, report.Files.Count);
            Assert.All(report.Files, f => Assert.True(f.OptimizedBytes <= f.BaselineBytes));
            Assert.All(report.Iterations, r => Assert.True(r.AcceptedBytes <= r.CandidateBytes));
            foreach (var g in report.Iterations.GroupBy(r => (r.Group, r.File, r.BlockIndex)))
            {
                int previous = int.MaxValue;
                foreach (var step in g.OrderBy(x => x.Iteration))
                {
                    Assert.True(step.AcceptedBytes <= previous);
                    Assert.True(step.ElapsedSeconds >= 0);
                    previous = step.AcceptedBytes;
                }
            }
            string reports = Path.Combine(root, "reports");
            IterativeDpBenchmark.SaveCsv(report, reports);
            Assert.True(File.Exists(Path.Combine(reports, "iterative-dp-iterations.csv")));
            Assert.True(File.Exists(Path.Combine(reports, "iterative-dp-files.csv")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
