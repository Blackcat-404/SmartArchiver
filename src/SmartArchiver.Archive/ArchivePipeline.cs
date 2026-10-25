namespace SmartArchiver.Archive;

/// <summary>Параметри повного тракту. Аналізатор обов'язковий: без нього не відомо, що стискати.</summary>
/// <param name="MaxVolumeBytes">Найбільший розмір тому разом з HMAC. 0 = один том без обмеження.</param>
public sealed record PackOptions(
    string Password,
    IFileAnalyzer Analyzer,
    int BlockSize = FormatConstants.DefaultBlockSize,
    long MaxVolumeBytes = 0,
    PackingAlgorithm Algorithm = PackingAlgorithm.Dp,
    PackingPriorities? Priorities = null);

/// <summary>Результат без винятку: значення або код помилки (E_MAGIC, E_LENGTH, E_HMAC, E_PAYLOAD, E_CODE, E_CRC, ...).</summary>
public sealed record ArchiveResult<T>(T? Value, ErrorCode? Error = null, string Message = "")
{
    public bool Ok => Error is null;
}

/// <summary>
/// Повний тракт: аналіз → блоки → LZ77 → Гаффман → пакування по томах → AES-256-CTR → HMAC-SHA256 → томи,
/// і назад: HMAC → розшифрування → розпаковка → CRC32. Кожен крок уже є в своєму класі, тут вони зведені в одне місце.
/// </summary>
public static class ArchivePipeline
{
    /// <summary>Аналіз, розбиття на блоки, стиснення. Після цього ваги блоків (coded_len + 15) відомі.</summary>
    public static IReadOnlyList<PreparedFile> Prepare(IReadOnlyList<ArchiveInput> files, PackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ArchiveWriter(options.BlockSize, options.Analyzer).Prepare(files);
    }

    /// <summary>Пакування по томах (рюкзак), шифрування і HMAC. Блок, що не влазить у том, дає E_CAPACITY.</summary>
    public static IReadOnlyList<byte[]> Seal(IReadOnlyList<PreparedFile> prepared, PackOptions options)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(options);
        IReadOnlyList<byte[]> plain = options.MaxVolumeBytes > 0
            ? ArchiveWriter.BuildVolumes(prepared, VolumePacker.Pack(prepared, options.MaxVolumeBytes, options.Algorithm, options.Priorities))
            : [ArchiveWriter.BuildFromPrepared(prepared)];
        return ProtectedArchive.ProtectVolumes(plain, options.Password);
    }

    public static IReadOnlyList<byte[]> Pack(IReadOnlyList<ArchiveInput> files, PackOptions options) =>
        Seal(Prepare(files, options), options);

    /// <summary>Томи в будь-якому порядку → файли. Будь-яка вада архіву дає ArchiveException з кодом.</summary>
    public static IReadOnlyList<ExtractedFile> Unpack(IReadOnlyList<byte[]> volumes, string password) =>
        ProtectedArchive.ReadProtectedVolumes(volumes, password);

    public static ArchiveResult<IReadOnlyList<ExtractedFile>> TryUnpack(IReadOnlyList<byte[]> volumes, string password) =>
        Try(() => Unpack(volumes, password));

    /// <summary>
    /// Атомарне розпакування. Спочатку все перевіряється в пам'яті (HMAC, структура, CRC32): якщо архів погано, на диск
    /// не потрапляє нічого. Далі файли пишуться в тимчасову теку всередині <paramref name="outputDir"/> і переносяться на місце.
    /// Наявний файл ніколи не перезаписується: при конфлікті (у тому числі такому, що виник посеред розпаковування)
    /// те, що вже перенесено, видаляється, і кидається IOException.
    /// </summary>
    public static IReadOnlyList<ExtractedFile> Extract(IReadOnlyList<byte[]> volumes, string password, string outputDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputDir);
        IReadOnlyList<ExtractedFile> files = Unpack(volumes, password);
        string[] targets = [.. files.Select(f => Path.Combine(outputDir, f.Name))];
        foreach (string target in targets)
        {
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException($"Файл уже існує: {Path.GetFileName(target)}. Нічого не записано.");
        }

        bool createdDir = !Directory.Exists(outputDir);
        Directory.CreateDirectory(outputDir);
        string staging = Path.Combine(outputDir, $".extract-{Guid.NewGuid():N}");
        var moved = new List<string>();
        bool done = false;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (ExtractedFile file in files)
            {
                File.WriteAllBytes(Path.Combine(staging, file.Name), file.Data);
            }
            for (int i = 0; i < files.Count; i++)
            {
                File.Move(Path.Combine(staging, files[i].Name), targets[i]);   // без перезапису: наявний файл дає IOException
                moved.Add(targets[i]);
            }
            done = true;
            return files;
        }
        finally
        {
            if (!done)
            {
                foreach (string path in moved)
                {
                    File.Delete(path);
                }
            }
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            if (!done && createdDir && Directory.GetFileSystemEntries(outputDir).Length == 0)
                Directory.Delete(outputDir);
        }
    }

    public static ArchiveResult<IReadOnlyList<ExtractedFile>> TryExtract(IReadOnlyList<byte[]> volumes, string password, string outputDir) =>
        Try(() => Extract(volumes, password, outputDir));

    private static ArchiveResult<T> Try<T>(Func<T> action)
    {
        try
        {
            return new ArchiveResult<T>(action());
        }
        catch (ArchiveException ex)
        {
            return new ArchiveResult<T>(default, ex.Code, ex.Message);
        }
    }
}
