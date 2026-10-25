using SmartArchiver.Compression;

namespace SmartArchiver.Archive.Tests;

/// <summary>
/// CP3, пункт 1: наскрізна перевірка всіх семи груп корпусу.
/// Кожен файл проходить ArchiveWriter -> ArchiveReader; байти й CRC32 мають збігатися.
/// </summary>
public class CorpusRoundTripTests
{
    [Theory]
    [InlineData(BlockSplitMode.Fixed)]
    [InlineData(BlockSplitMode.Dp)]
    public void AllSevenCorpusGroups_RoundTrip_WithMatchingCrc32(BlockSplitMode splitMode)
    {
        string corpusPath = FindCorpusDirectory();
        int filesChecked = 0;
        int groupsChecked = 0;

        for (int group = 0; group < 7; group++)
        {
            string groupPath = Path.Combine(corpusPath, group.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(Directory.Exists(groupPath), $"Відсутня група корпусу: {groupPath}");

            string[] files = Directory.GetFiles(groupPath, "*", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            groupsChecked++;

            foreach (string path in files.OrderBy(p => p, StringComparer.Ordinal))
            {
                byte[] original = File.ReadAllBytes(path);
                string fileName = Path.GetFileName(path);

                // Примусово вмикаємо LZ77 + Huffman: тест має пройти реальний тракт
                // стиснення, але ArchiveWriter усе одно може зберегти невигідний блок сирим.
                var analyzer = new FixedAnalyzer((FileKind)group, CompressionStrategy.Lz77Huffman);
                var writer = new ArchiveWriter(
                    blockSize: 32_768,
                    analyzer: analyzer,
                    parseMode: Lz77ParseMode.Lazy,
                    splitMode: splitMode);

                byte[] archive = writer.Build([new ArchiveInput(fileName, original)]);
                ExtractedFile restored = Assert.Single(ArchiveReader.Read(archive));

                Assert.Equal(fileName, restored.Name);
                Assert.Equal(original, restored.Data);

                uint expectedCrc = Crc32.Compute(original);
                uint actualCrc = Crc32.Compute(restored.Data);
                Assert.Equal(expectedCrc, actualCrc);
                Assert.Equal(expectedCrc, restored.Crc32);
                filesChecked++;
            }
        }

        Assert.Equal(7, groupsChecked);
        Assert.Equal(22, filesChecked);
    }

    private static string FindCorpusDirectory()
    {
        // Піднімаємося від bin/Debug/... до кореня репозиторію.
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "Datasets");
            if (Directory.Exists(candidate) &&
                Directory.Exists(Path.Combine(candidate, "0")) &&
                Directory.Exists(Path.Combine(candidate, "6")))
            {
                return candidate;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Не знайдено Datasets/0...6. Запусти dotnet test із повної копії репозиторію SmartArchiver.");
    }
}
