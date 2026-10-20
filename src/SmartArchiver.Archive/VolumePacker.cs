using System.Diagnostics;

namespace SmartArchiver.Archive;

/// <summary>Як вибирати блоки для тому.</summary>
public enum PackingAlgorithm
{
    /// <summary>Рюкзак 0/1 динамічним програмуванням: максимум сумарної цінності.</summary>
    Dp,

    /// <summary>Жадібно за цінністю на байт ваги.</summary>
    Greedy,
}

/// <summary>Блок як предмет рюкзака: вага = coded_len + 15 байт, цінність за типом файлу.</summary>
public sealed record PackBlock(int FileIndex, int Sequence, long Weight, long Value);

/// <summary>Файл, чий запис треба резервувати в таблиці файлів тому.</summary>
public sealed record PackFile(int Index, int TableEntrySize);

/// <summary>Що потрапило в один том.</summary>
/// <param name="VolumeIndex">Номер тому з нуля (volume_index у БД).</param>
/// <param name="CapacityBytes">Місткість рюкзака W: max_volume_bytes мінус заголовок, HMAC, поля payload і таблиця файлів.</param>
/// <param name="FileIndexes">Файли в таблиці файлів цього тому (зарезервовані до запуску рюкзака).</param>
public sealed record VolumePlan(
    int VolumeIndex,
    long CapacityBytes,
    IReadOnlyList<int> FileIndexes,
    IReadOnlyList<PackBlock> Blocks,
    double ElapsedSeconds)
{
    public long UsedBytes => Blocks.Sum(b => b.Weight);

    public long TotalValue => Blocks.Sum(b => b.Value);

    public int BlockCount => Blocks.Count;
}

/// <summary>Розкладка всіх блоків по томах одним алгоритмом.</summary>
public sealed record PackingPlan(PackingAlgorithm Algorithm, long MaxVolumeBytes, IReadOnlyList<VolumePlan> Volumes)
{
    public long TotalValue => Volumes.Sum(v => v.TotalValue);

    public long TotalUsedBytes => Volumes.Sum(v => v.UsedBytes);

    public int TotalBlocks => Volumes.Sum(v => v.BlockCount);

    public double TotalSeconds => Volumes.Sum(v => v.ElapsedSeconds);
}

/// <summary>Результат порівняння ДП-рюкзака з жадібним пакуванням на однакових блоках.</summary>
public sealed record PackingComparison(PackingPlan Dp, PackingPlan Greedy);

/// <summary>
/// Пакування блоків у томи за FORMAT_SPEC_P2, розділ 7. Блоки вже закодовані, тож їх ваги відомі й не змінюються.
/// Том за томом: фіксується таблиця файлів тому, обчислюється місткість W, з решти блоків вибирається підмножина
/// (рюкзак або жадібно), невибрані переходять до наступного тому за тим самим правилом.
/// </summary>
public static class VolumePacker
{
    /// <summary>
    /// Фіксовані байти тому, що не залежать від файлів: заголовок 41, HMAC 32 і file_count 4.
    /// (Специфікація рахує 41 + 32 + 6 для свого формату payload; цей формат має file_count(4) і не має
    /// окремої таблиці блоків, тож стала інша. Усе зосереджено в FormatConstants.)
    /// </summary>
    public const int FixedOverhead = FormatConstants.HeaderSize + FormatConstants.TagSize + FormatConstants.PayloadFixedSize;

    /// <summary>Пакує підготовлені файли: цінність блока береться з пріоритетів за типом його файлу.</summary>
    public static PackingPlan Pack(
        IReadOnlyList<PreparedFile> files,
        long maxVolumeBytes,
        PackingAlgorithm algorithm,
        PackingPriorities? priorities = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        PackingPriorities values = priorities ?? PackingPriorities.Default;

        var packFiles = new List<PackFile>(files.Count);
        var blocks = new List<PackBlock>();
        foreach (PreparedFile file in files)
        {
            packFiles.Add(new PackFile(file.Index, file.TableEntrySize));
            long value = values.ValueOf(file.Kind);
            foreach (PreparedBlock block in file.Blocks)
            {
                blocks.Add(new PackBlock(file.Index, block.Sequence, block.Weight, value));
            }
        }
        return Pack(packFiles, blocks, maxVolumeBytes, algorithm);
    }

    /// <summary>Один і той самий набір блоків двома алгоритмами, для таблиці порівняння.</summary>
    public static PackingComparison Compare(
        IReadOnlyList<PreparedFile> files,
        long maxVolumeBytes,
        PackingPriorities? priorities = null) =>
        new(Pack(files, maxVolumeBytes, PackingAlgorithm.Dp, priorities),
            Pack(files, maxVolumeBytes, PackingAlgorithm.Greedy, priorities));

    /// <summary>
    /// Розкладає блоки по томах. Кидає ArchiveException(E_CAPACITY), якщо якийсь блок не влазить у том
    /// навіть тоді, коли в таблиці файлів лише його файл.
    /// </summary>
    public static PackingPlan Pack(
        IReadOnlyList<PackFile> files,
        IReadOnlyList<PackBlock> blocks,
        long maxVolumeBytes,
        PackingAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!Enum.IsDefined(algorithm))
            throw new ArgumentOutOfRangeException(nameof(algorithm));
        // Довжина шифротексту (ciphertext_len) має 4 байти, тож том більший за 4 ГБ неможливий.
        if (maxVolumeBytes <= 0 || maxVolumeBytes > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxVolumeBytes), "Місткість тому має бути в межах від 1 до 4 ГБ.");

        var entrySizes = new Dictionary<int, int>();
        foreach (PackFile file in files)
        {
            if (!entrySizes.TryAdd(file.Index, file.TableEntrySize))
                throw new ArgumentException($"Файл {file.Index} вказано двічі.", nameof(files));
        }

        foreach (PackBlock block in blocks)
        {
            if (!entrySizes.TryGetValue(block.FileIndex, out int entrySize))
                throw new ArgumentException($"Блок посилається на невідомий файл {block.FileIndex}.", nameof(blocks));
            if (block.Weight <= 0 || block.Value < 0)
                throw new ArgumentException("Вага блока має бути додатною, цінність не від'ємною.", nameof(blocks));

            long roomForBlocks = maxVolumeBytes - FixedOverhead - entrySize;
            if (block.Weight > roomForBlocks)
            {
                throw new ArchiveException(ErrorCode.E_CAPACITY,
                    $"Блок {block.Sequence} файлу №{block.FileIndex} важить {block.Weight} байт (coded_len + {FormatConstants.BlockHeaderSize}), " +
                    $"а в томі місткістю {maxVolumeBytes} байт для блоків лишається щонайбільше {Math.Max(roomForBlocks, 0)} байт. " +
                    "Збільште місткість тому або зменште розмір блока.");
            }
        }

        var remaining = new List<PackBlock>(blocks);
        remaining.Sort((a, b) => a.FileIndex != b.FileIndex ? a.FileIndex.CompareTo(b.FileIndex) : a.Sequence.CompareTo(b.Sequence));

        // Файли без блоків (порожні) потрапляють лише в перший том.
        var withBlocks = new HashSet<int>();
        foreach (PackBlock block in remaining)
        {
            withBlocks.Add(block.FileIndex);
        }
        var emptyFiles = new List<int>();
        foreach (PackFile file in files)
        {
            if (!withBlocks.Contains(file.Index))
                emptyFiles.Add(file.Index);
        }
        emptyFiles.Sort();

        var volumes = new List<VolumePlan>();
        do
        {
            int volumeIndex = volumes.Count;
            IReadOnlyList<int> extra = volumeIndex == 0 ? emptyFiles : Array.Empty<int>();

            // Таблиця файлів тому: усі файли, що ще мають блоки (плюс порожні в першому томі). Далі вона не змінюється.
            List<int> tableFiles = TableFor(remaining, extra);
            long capacity = CapacityFor(tableFiles, entrySizes, maxVolumeBytes);
            List<PackBlock> chosen = [];
            double seconds = 0;
            if (capacity >= 0)
            {
                (chosen, seconds) = Select(remaining, capacity, algorithm);
            }

            if (chosen.Count == 0 && remaining.Count > 0)
            {
                // Повна таблиця забрала всю місткість. Беремо лише перший файл, що лишився: його блок влазить (перевірено вище).
                int first = remaining[0].FileIndex;
                List<PackBlock> candidates = remaining.FindAll(b => b.FileIndex == first);
                tableFiles = TableFor(candidates, extra);
                capacity = CapacityFor(tableFiles, entrySizes, maxVolumeBytes);
                if (capacity >= 0)
                {
                    (chosen, seconds) = Select(candidates, capacity, algorithm);
                }
            }

            if (capacity < 0 || (chosen.Count == 0 && remaining.Count > 0))
            {
                throw new ArchiveException(ErrorCode.E_CAPACITY,
                    $"У том №{volumeIndex} місткістю {maxVolumeBytes} байт не влазить таблиця файлів разом із жодним блоком. Збільште місткість тому.");
            }

            volumes.Add(new VolumePlan(volumeIndex, capacity, tableFiles, chosen, seconds));
            foreach (PackBlock block in chosen)
            {
                remaining.Remove(block);
            }
        }
        while (remaining.Count > 0);

        return new PackingPlan(algorithm, maxVolumeBytes, volumes);
    }

    private static List<int> TableFor(List<PackBlock> candidates, IReadOnlyList<int> extraFiles)
    {
        var set = new SortedSet<int>(extraFiles);
        foreach (PackBlock block in candidates)
        {
            set.Add(block.FileIndex);
        }
        return [.. set];
    }

    /// <summary>W = max_volume_bytes − заголовок − HMAC − file_count − таблиця файлів. Від'ємне значення означає, що таблиця не вміщується.</summary>
    private static long CapacityFor(List<int> tableFiles, Dictionary<int, int> entrySizes, long maxVolumeBytes)
    {
        long capacity = maxVolumeBytes - FixedOverhead;
        foreach (int file in tableFiles)
        {
            capacity -= entrySizes[file];
        }
        return capacity;
    }

    private static (List<PackBlock> Chosen, double Seconds) Select(List<PackBlock> candidates, long capacity, PackingAlgorithm algorithm)
    {
        var weights = new long[candidates.Count];
        var values = new long[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            weights[i] = candidates[i].Weight;
            values[i] = candidates[i].Value;
        }

        var watch = Stopwatch.StartNew();
        bool[] flags = algorithm == PackingAlgorithm.Dp
            ? Knapsack.SolveDp(weights, values, capacity)
            : Knapsack.SolveGreedy(weights, values, capacity);
        watch.Stop();

        var chosen = new List<PackBlock>();
        for (int i = 0; i < flags.Length; i++)
        {
            if (flags[i])
                chosen.Add(candidates[i]);
        }
        return (chosen, watch.Elapsed.TotalSeconds);
    }
}
