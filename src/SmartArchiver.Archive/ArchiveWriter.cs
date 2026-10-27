using SmartArchiver.Compression;

namespace SmartArchiver.Archive;

/// <summary>Вхідний файл для архівації: безпечне ім'я (без шляху) і вміст.</summary>
public sealed record ArchiveInput(string Name, byte[] Data, FileKind Kind = FileKind.Unknown);

/// <summary>Як різати файл на блоки перед стисненням.</summary>
public enum BlockSplitMode
{
    /// <summary>Блоки рівно по BlockSize байт (останній коротший).</summary>
    Fixed,

    /// <summary>Межі блоків обирає ДП за фактичним розміром стиснення (BlockSize має бути кратний 16 КіБ).</summary>
    Dp,
}

/// <summary>Що аналізатор оцінює, обираючи стратегію стиснення.</summary>
public enum StrategyScope
{
    /// <summary>Одна стратегія на весь файл.</summary>
    File,

    /// <summary>
    /// Кожен блок отримує свою стратегію: блок, який аналізатор вважає нестисканим, зберігається без спроби LZ77.
    /// Тип файлу (file_type) і далі визначає аналіз усього файлу. Формат не змінюється: рішення записується
    /// в block_flags кожного блоку.
    /// </summary>
    Block,
}

/// <summary>Обмеження й алгоритм для запису архіву кількома томами.</summary>
/// <param name="MaxVolumeBytes">Найбільший розмір одного тому з урахуванням 32 байтів HMAC.</param>
public sealed record VolumeOptions(
    long MaxVolumeBytes,
    PackingAlgorithm Algorithm = PackingAlgorithm.Dp,
    PackingPriorities? Priorities = null);

/// <summary>
/// Запис архіву. Без аналізатора файли зберігаються як є. З аналізатором кожен файл спочатку аналізується
/// (тип і стратегія), потім або зберігається як є (шум, вже стиснене), або кодується LZ77 + Гаффман блоками,
/// причому блок без виграшу все одно зберігається як є.
/// Том = заголовок(41) | payload. Payload = file_count(u32) | таблиця файлів | секція даних.
/// Секція даних = послідовність блоків: заголовок блока (15 байт) + дані.
/// Заголовок блока: file_index(u16) | block_index(u32) | raw_len(u32) | coded_len(u32) | стратегія(u8).
/// </summary>
public sealed class ArchiveWriter
{
    /// <summary>Крок сітки меж блоків для <see cref="BlockSplitMode.Dp"/>.</summary>
    public const int DpBoundaryStep = 16_384;

    public int BlockSize { get; }

    public IFileAnalyzer? Analyzer { get; }

    public Lz77ParseMode ParseMode { get; }

    public BlockSplitMode SplitMode { get; }

    public StrategyScope Scope { get; }

    public ArchiveWriter(
        int blockSize = FormatConstants.DefaultBlockSize,
        IFileAnalyzer? analyzer = null,
        Lz77ParseMode parseMode = Lz77ParseMode.Lazy,
        BlockSplitMode splitMode = BlockSplitMode.Fixed,
        StrategyScope scope = StrategyScope.File)
    {
        if (blockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize), "Розмір блока має бути додатним.");
        if (blockSize > FormatConstants.MaxBlockRawLength)
            throw new ArgumentOutOfRangeException(nameof(blockSize), $"Розмір блока не може перевищувати {FormatConstants.MaxBlockRawLength} байт.");
        if (!Enum.IsDefined(parseMode))
            throw new ArgumentOutOfRangeException(nameof(parseMode));
        if (!Enum.IsDefined(splitMode))
            throw new ArgumentOutOfRangeException(nameof(splitMode));
        if (splitMode == BlockSplitMode.Dp && blockSize % DpBoundaryStep != 0)
            throw new ArgumentException($"Для ДП-розбиття розмір блока має бути кратним {DpBoundaryStep}.", nameof(blockSize));
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (scope == StrategyScope.Block && splitMode == BlockSplitMode.Dp)
            throw new ArgumentException("Стратегія по блоках потребує фіксованих блоків: ДП обирає межі вже після стиснення.", nameof(scope));

        BlockSize = blockSize;
        Analyzer = analyzer;
        ParseMode = parseMode;
        SplitMode = splitMode;
        Scope = scope;
    }

    /// <summary>
    /// Перевіряє імена, аналізує й кодує кожен файл. Ваги блоків (coded_len + 15) після цього відомі
    /// і не змінюються, тож за ними можна пакувати томи.
    /// </summary>
    public IReadOnlyList<PreparedFile> Prepare(IReadOnlyList<ArchiveInput> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count > FormatConstants.MaxFileCount)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Забагато файлів в одному архіві (максимум 65536).");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PreparedFile>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            ArchiveInput file = files[i];
            SafeNames.EnsureSafe(file.Name);
            if (!names.Add(file.Name))
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Два файли з однаковим іменем в одному архіві.");
            if (!Enum.IsDefined(file.Kind))
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Невідомий тип файлу.");

            result.Add(PrepareFile(i, file));
        }
        return result;
    }

    /// <summary>Один том без обмеження розміру.</summary>
    public byte[] Build(IReadOnlyList<ArchiveInput> files) => BuildFromPrepared(Prepare(files));

    /// <summary>Один том з уже підготовлених файлів (щоб не аналізувати й не стискати вдруге).</summary>
    public static byte[] BuildFromPrepared(IReadOnlyList<PreparedFile> prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var blocks = new List<(int FileIndex, PreparedBlock Block)>();
        foreach (PreparedFile file in prepared)
        {
            foreach (PreparedBlock block in file.Blocks)
            {
                blocks.Add((file.Index, block));
            }
        }
        return WriteVolume(1, 1, prepared, [.. prepared.Select(f => f.Index)], blocks);
    }

    /// <summary>
    /// Кілька томів: блоки розкладає по томах рюкзак (або жадібний алгоритм) за місткістю, кожен том не більший за
    /// <see cref="VolumeOptions.MaxVolumeBytes"/> з урахуванням HMAC. Блок, що не влазить у том, дає E_CAPACITY.
    /// </summary>
    public IReadOnlyList<byte[]> BuildVolumes(IReadOnlyList<ArchiveInput> files, VolumeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        IReadOnlyList<PreparedFile> prepared = Prepare(files);
        PackingPlan plan = VolumePacker.Pack(prepared, options.MaxVolumeBytes, options.Algorithm, options.Priorities);
        return BuildVolumes(prepared, plan);
    }

    /// <summary>Записує томи за готовою розкладкою.</summary>
    public static IReadOnlyList<byte[]> BuildVolumes(IReadOnlyList<PreparedFile> prepared, PackingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Volumes.Count > ushort.MaxValue)
            throw new ArchiveException(ErrorCode.E_VOLUMES, "Забагато томів (максимум 65535).");

        var volumes = new List<byte[]>(plan.Volumes.Count);
        foreach (VolumePlan volume in plan.Volumes)
        {
            var blocks = new List<(int FileIndex, PreparedBlock Block)>(volume.Blocks.Count);
            foreach (PackBlock packed in volume.Blocks)
            {
                blocks.Add((packed.FileIndex, prepared[packed.FileIndex].Blocks[packed.Sequence]));
            }

            byte[] bytes = WriteVolume(
                checked((ushort)(volume.VolumeIndex + 1)),
                (ushort)plan.Volumes.Count,
                prepared,
                volume.FileIndexes,
                blocks);

            // Контроль пакувальника: том разом з HMAC не повинен перевищувати місткість.
            if (bytes.LongLength + FormatConstants.TagSize > plan.MaxVolumeBytes)
                throw new InvalidOperationException($"Том №{volume.VolumeIndex} вийшов більшим за місткість: {bytes.Length} байт.");
            volumes.Add(bytes);
        }
        return volumes;
    }

    private PreparedFile PrepareFile(int index, ArchiveInput file)
    {
        byte[] data = file.Data;
        FileKind kind = file.Kind;
        CompressionStrategy strategy = CompressionStrategy.NoCompression;
        if (Analyzer is not null)
        {
            FileAnalysis analysis = Analyzer.Analyze(data);
            if (!Enum.IsDefined(analysis.Strategy))
                throw new ArchiveException(ErrorCode.E_CODE, $"Аналізатор повернув невідому стратегію: {analysis.Strategy}.");
            if (kind == FileKind.Unknown)
                kind = analysis.Kind;
            strategy = analysis.Strategy;
        }

        List<PreparedBlock> blocks = Analyzer is not null && Scope == StrategyScope.Block
            ? CompressBlocksSeparately(data)
            : strategy == CompressionStrategy.Lz77Huffman && data.Length > 0
                ? CompressBlocks(data)
                : StoreBlocks(data);
        if (Scope == StrategyScope.Block)
            strategy = blocks.TrueForAll(b => b.IsStored) ? CompressionStrategy.NoCompression : CompressionStrategy.Lz77Huffman;

        // Файл, у якому жоден блок не вдалося стиснути, чесно позначається як нестиснений.
        if (strategy == CompressionStrategy.Lz77Huffman && blocks.TrueForAll(b => b.IsStored))
            strategy = CompressionStrategy.NoCompression;

        return new PreparedFile(index, file.Name, (ulong)data.Length, Crc32.Compute(data), kind, strategy, blocks);
    }

    private List<PreparedBlock> CompressBlocks(byte[] data)
    {
        var prepared = SplitMode == BlockSplitMode.Dp
            ? Lz77CompressionService.PrepareDpBlocks(data, BlockSize, DpBoundaryStep)
            : Lz77CompressionService.PrepareFixedBlocks(data, ParseMode, BlockSize);

        var blocks = new List<PreparedBlock>(prepared.Count);
        for (int i = 0; i < prepared.Count; i++)
        {
            Lz77PreparedBlock block = prepared[i];
            blocks.Add(new PreparedBlock(i, block.RawLength, block.CodedBytes, block.IsStored));
        }
        return blocks;
    }

    /// <summary>
    /// Стратегія по блоках: аналізатор оцінює кожен блок окремо. Блок, який він вважає нестисканим, зберігається
    /// без спроби LZ77; решта кодується, і блок без виграшу все одно зберігається як є.
    /// </summary>
    private List<PreparedBlock> CompressBlocksSeparately(byte[] data)
    {
        List<PreparedBlock> blocks = StoreBlocks(data);
        for (int i = 0; i < blocks.Count; i++)
        {
            byte[] raw = blocks[i].CodedBytes;   // блок ще не стиснений, тож це сирі байти
            FileAnalysis analysis = Analyzer!.Analyze(raw);
            if (!Enum.IsDefined(analysis.Strategy))
                throw new ArchiveException(ErrorCode.E_CODE, $"Аналізатор повернув невідому стратегію: {analysis.Strategy}.");
            if (analysis.Strategy == CompressionStrategy.NoCompression)
                continue;

            Lz77PreparedBlock coded = Lz77CompressionService.PrepareFixedBlocks(raw, ParseMode, raw.Length)[0];
            blocks[i] = new PreparedBlock(i, raw.Length, coded.CodedBytes, coded.IsStored);
        }
        return blocks;
    }

    /// <summary>Ріже вміст файлу на блоки по BlockSize без стиснення. Порожній файл блоків не має.</summary>
    private List<PreparedBlock> StoreBlocks(byte[] content)
    {
        var blocks = new List<PreparedBlock>();
        int sequence = 0;
        for (long pos = 0; pos < content.Length; pos += BlockSize)
        {
            int length = (int)Math.Min(BlockSize, content.Length - pos);
            blocks.Add(new PreparedBlock(sequence++, length, content.AsSpan((int)pos, length).ToArray(), IsStored: true));
        }
        return blocks;
    }

    /// <summary>
    /// Записує один том. Таблиця файлів містить файли з <paramref name="tableFiles"/> у цьому порядку;
    /// file_index у заголовках блоків локальний для тому. Offset у таблиці це позиція першого блока файлу
    /// в секції даних цього тому, або 0, якщо в цьому томі блоків файлу немає.
    /// </summary>
    private static byte[] WriteVolume(
        ushort volumeNumber,
        ushort volumeCount,
        IReadOnlyList<PreparedFile> prepared,
        IReadOnlyList<int> tableFiles,
        List<(int FileIndex, PreparedBlock Block)> blocks)
    {
        if (tableFiles.Count > FormatConstants.MaxFileCount)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Забагато файлів в одному томі (максимум 65536).");

        var localIndex = new Dictionary<int, int>(tableFiles.Count);
        for (int i = 0; i < tableFiles.Count; i++)
        {
            localIndex[tableFiles[i]] = i;
        }

        var firstOffset = new ulong?[tableFiles.Count];
        using var data = new MemoryStream();
        foreach ((int fileIndex, PreparedBlock block) in blocks)
        {
            int local = localIndex[fileIndex];
            firstOffset[local] ??= (ulong)data.Position;

            BinaryIo.WriteU16(data, (ushort)local);
            BinaryIo.WriteU32(data, (uint)block.Sequence);
            BinaryIo.WriteU32(data, (uint)block.RawLength);
            BinaryIo.WriteU32(data, (uint)block.CodedLength);
            data.WriteByte((byte)block.Strategy);
            data.Write(block.CodedBytes, 0, block.CodedBytes.Length);
        }

        using var payload = new MemoryStream();
        BinaryIo.WriteU32(payload, (uint)tableFiles.Count);
        for (int i = 0; i < tableFiles.Count; i++)
        {
            PreparedFile file = prepared[tableFiles[i]];
            new FileTableEntry(file.Name, file.Size, file.Crc32, file.Kind, file.Strategy, firstOffset[i] ?? 0UL).WriteTo(payload);
        }
        data.WriteTo(payload);

        if (payload.Length > uint.MaxValue)
            throw new ArchiveException(ErrorCode.E_LENGTH, "Payload завеликий для одного тому.");

        var header = new VolumeHeader
        {
            VolumeNumber = volumeNumber,
            VolumeCount = volumeCount,
            CiphertextLength = (uint)payload.Length,
        };
        byte[] payloadBytes = payload.ToArray();
        var result = new byte[FormatConstants.HeaderSize + payloadBytes.Length];
        header.WriteTo(result);
        payloadBytes.CopyTo(result, FormatConstants.HeaderSize);
        return result;
    }
}
