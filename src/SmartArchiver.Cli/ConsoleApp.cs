using System.Text;
using SmartArchiver.Core;
using SmartArchiver.Data;

namespace SmartArchiver.Cli;

/// <summary>
/// Консольний інтерфейс чекпоінта 1.
/// Без аргументів запускає інтерактивне меню, з аргументами працює як звичайна команда
/// (pack, unpack, info, demo, db).
/// </summary>
internal static class ConsoleApp
{
    public static int Run(string[] args)
    {
        TrySetUtf8();
        try
        {
            return args.Length == 0 ? Interactive() : CommandLine(args);
        }
        catch (ArchiveException ex)
        {
            Write(Explain(ex), ConsoleColor.Red);
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Write($"[ПОМИЛКА] Проблема з файлами: {ex.Message}", ConsoleColor.Red);
            return 3;
        }
        catch (DatabaseException ex)
        {
            Write($"[ПОМИЛКА] База даних: {ex.Message}", ConsoleColor.Red);
            return 4;
        }
    }

    // ------------------------------------------------------------------
    // Режим команд
    // ------------------------------------------------------------------

    private static int CommandLine(string[] args)
    {
        switch (args[0].ToLowerInvariant())
        {
            case "pack" when args.Length >= 3:
                PackFiles(args[1], args[2..], FormatConstants.DefaultBlockSize);
                return 0;
            case "unpack" when args.Length == 3:
                UnpackFile(args[1], args[2]);
                return 0;
            case "info" when args.Length == 2:
                ShowInfo(args[1]);
                return 0;
            case "demo" when args.Length == 1:
                Demo();
                return 0;
            case "db" when args.Length == 1:
                ShowDatabase();
                return 0;
            default:
                Console.WriteLine("Використання:");
                Console.WriteLine("  smartarchiver                            інтерактивне меню");
                Console.WriteLine("  smartarchiver pack <archive.arc> <файл>...  запакувати файли");
                Console.WriteLine("  smartarchiver unpack <archive.arc> <тека>   розпакувати");
                Console.WriteLine("  smartarchiver info <archive.arc>            показати вміст");
                Console.WriteLine("  smartarchiver demo                          автоматична демонстрація");
                Console.WriteLine("  smartarchiver db                            стан бази даних (створює її, якщо ще немає)");
                return 1;
        }
    }

    // ------------------------------------------------------------------
    // Інтерактивне меню
    // ------------------------------------------------------------------

    private static int Interactive()
    {
        Write("==============================================", ConsoleColor.Cyan);
        Write("  SmartArchiver, чекпоінт 1: формат і томи", ConsoleColor.Cyan);
        Write("  (без стиснення й шифрування, це наступні етапи)", ConsoleColor.DarkCyan);
        Write("==============================================", ConsoleColor.Cyan);

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("  1) Запакувати файли в архів");
            Console.WriteLine("  2) Розпакувати архів");
            Console.WriteLine("  3) Показати вміст архіву (заголовок і таблиця файлів)");
            Console.WriteLine("  4) Демонстрація: усі можливості на прикладі");
            Console.WriteLine("  5) База даних: стан і створення схеми");
            Console.WriteLine("  0) Вихід");

            string? choice = Prompt("Ваш вибір");
            if (choice is null)
            {
                return 0;   // кінець вводу
            }

            switch (choice)
            {
                case "1": Guard(PackInteractive); break;
                case "2": Guard(UnpackInteractive); break;
                case "3": Guard(InfoInteractive); break;
                case "4": Guard(Demo); break;
                case "5": Guard(ShowDatabase); break;
                case "0":
                    Console.WriteLine("До побачення!");
                    return 0;
                default:
                    Write("Невідомий пункт. Введіть число від 0 до 5.", ConsoleColor.Yellow);
                    break;
            }
        }
    }

    /// <summary>Помилка в одній дії не закриває програму: показуємо її й повертаємось у меню.</summary>
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (ArchiveException ex)
        {
            Write(Explain(ex), ConsoleColor.Red);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Write($"[ПОМИЛКА] Проблема з файлами: {ex.Message}", ConsoleColor.Red);
        }
        catch (DatabaseException ex)
        {
            Write($"[ПОМИЛКА] База даних: {ex.Message}", ConsoleColor.Red);
        }
    }

    private static void PackInteractive()
    {
        Console.WriteLine("Вводьте шляхи до файлів по одному в рядку (можна перетягнути файл у вікно).");
        Console.WriteLine("Порожній рядок завершує список.");

        var paths = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            string? path = Prompt($"Файл №{paths.Count + 1}");
            if (string.IsNullOrEmpty(path))
            {
                break;
            }
            if (!File.Exists(path))
            {
                Write($"Файл не знайдено: {path}", ConsoleColor.Yellow);
                continue;
            }

            string name = Path.GetFileName(path);
            if (!SafeNames.IsSafe(name, out string reason))
            {
                Write($"Ім'я \"{name}\" відхилено: {reason}.", ConsoleColor.Yellow);
                continue;
            }
            if (!names.Add(name))
            {
                Write($"Файл з іменем \"{name}\" вже додано (в архіві імена мають бути унікальні).", ConsoleColor.Yellow);
                continue;
            }

            paths.Add(path);
            Write($"Додано: {name} ({new FileInfo(path).Length} байт)", ConsoleColor.Green);
        }

        if (paths.Count == 0 && !Confirm("Файлів не додано. Створити порожній архів?"))
        {
            return;
        }

        string? sizeText = Prompt("Розмір блока в байтах (великі файли ріжуться на блоки)", FormatConstants.DefaultBlockSize.ToString());
        int blockSize = FormatConstants.DefaultBlockSize;
        if (!int.TryParse(sizeText, out blockSize) || blockSize <= 0)
        {
            blockSize = FormatConstants.DefaultBlockSize;
            Write($"Некоректне значення, беремо {blockSize}.", ConsoleColor.Yellow);
        }

        string? archivePath = Prompt("Куди зберегти архів", "archive.arc");
        if (string.IsNullOrEmpty(archivePath))
        {
            Write("Шлях не вказано.", ConsoleColor.Yellow);
            return;
        }
        if (Directory.Exists(archivePath))
        {
            // Вказали теку замість файлу: кладемо архів усередину неї.
            archivePath = Path.Combine(archivePath, "archive.arc");
            Write($"Вказано теку, архів буде збережено як {archivePath}", ConsoleColor.Yellow);
        }
        if (File.Exists(archivePath) && !Confirm($"Файл {archivePath} вже існує. Перезаписати?"))
        {
            return;
        }

        PackFiles(archivePath, paths.ToArray(), blockSize);
    }

    private static void UnpackInteractive()
    {
        string? archivePath = Prompt("Шлях до архіву");
        if (string.IsNullOrEmpty(archivePath))
        {
            Write("Шлях не вказано.", ConsoleColor.Yellow);
            return;
        }
        string? outputDir = Prompt("Тека для результату", "unpacked");
        if (string.IsNullOrEmpty(outputDir))
        {
            return;
        }
        UnpackFile(archivePath, outputDir);
    }

    private static void InfoInteractive()
    {
        string? archivePath = Prompt("Шлях до архіву");
        if (string.IsNullOrEmpty(archivePath))
        {
            Write("Шлях не вказано.", ConsoleColor.Yellow);
            return;
        }
        ShowInfo(archivePath);
    }

    // ------------------------------------------------------------------
    // Дії
    // ------------------------------------------------------------------

    private static void PackFiles(string archivePath, string[] paths, int blockSize)
    {
        var inputs = new List<ArchiveInput>();
        foreach (string path in paths)
        {
            inputs.Add(new ArchiveInput(Path.GetFileName(path), File.ReadAllBytes(path)));
        }

        byte[] archive = new ArchiveWriter(blockSize).Build(inputs);
        if (Directory.Exists(archivePath))
        {
            archivePath = Path.Combine(archivePath, "archive.arc");
        }
        File.WriteAllBytes(archivePath, archive);

        // Одразу читаємо назад і порівнюємо з оригіналом.
        IReadOnlyList<ExtractedFile> restored = ArchiveReader.Read(archive);
        bool identical = SameContent(inputs, restored);

        long originalSize = inputs.Sum(i => (long)i.Data.Length);
        Console.WriteLine();
        Write($"[OK] Архів записано: {Path.GetFullPath(archivePath)}", ConsoleColor.Green);
        PrintFileTable(restored, blockSize);
        Console.WriteLine($"Вихідний розмір: {originalSize} байт, архів: {archive.Length} байт, службові дані: {archive.Length - originalSize} байт.");
        if (identical)
        {
            Write("[OK] Перевірка: після читання назад усі файли збігаються побайтово.", ConsoleColor.Green);
        }
        else
        {
            Write("[ПОМИЛКА] Після читання назад файли не збігаються з оригіналом!", ConsoleColor.Red);
        }
    }

    private static void UnpackFile(string archivePath, string outputDir)
    {
        IReadOnlyList<ExtractedFile> files = ArchiveReader.Read(File.ReadAllBytes(archivePath));

        // Імена вже перевірені читачем (без шляхів). Наявні файли не перезаписуємо.
        foreach (ExtractedFile file in files)
        {
            if (File.Exists(Path.Combine(outputDir, file.Name)))
            {
                throw new IOException($"Файл уже існує: {file.Name}. Оберіть іншу теку.");
            }
        }

        Directory.CreateDirectory(outputDir);
        foreach (ExtractedFile file in files)
        {
            File.WriteAllBytes(Path.Combine(outputDir, file.Name), file.Data);
        }

        Console.WriteLine();
        Write($"[OK] Розпаковано {files.Count} файл(ів) у {Path.GetFullPath(outputDir)}", ConsoleColor.Green);
        PrintFileTable(files, 0);
        Write("[OK] CRC32 кожного файлу збігається.", ConsoleColor.Green);
    }

    private static void ShowInfo(string archivePath)
    {
        byte[] bytes = File.ReadAllBytes(archivePath);
        VolumeHeader header = VolumeHeader.Parse(bytes);
        IReadOnlyList<ExtractedFile> files = ArchiveReader.Read(bytes);

        Console.WriteLine();
        Write("Заголовок тому (41 байт)", ConsoleColor.Cyan);
        Console.WriteLine($"  Magic:            {Encoding.ASCII.GetString(FormatConstants.Magic)}");
        Console.WriteLine($"  Версія формату:   {header.Version}");
        Console.WriteLine($"  Том:              {header.VolumeNumber} з {header.VolumeCount}");
        Console.WriteLine($"  Довжина payload:  {header.CiphertextLength} байт");
        Console.WriteLine($"  Сіль:             {Convert.ToHexString(header.Salt)}");
        Console.WriteLine($"  Nonce:            {Convert.ToHexString(header.Nonce)}");
        Console.WriteLine($"  Розмір файлу:     {bytes.Length} байт");
        Console.WriteLine();
        Write("Таблиця файлів", ConsoleColor.Cyan);
        PrintFileTable(files, 0);
        Write("[OK] Структура коректна, CRC32 усіх файлів збігається.", ConsoleColor.Green);
    }

    private static void ShowDatabase()
    {
        IMeasurementStore store = OpenMeasurementStore();

        Console.WriteLine();
        if (!store.IsEnabled)
        {
            Write("База даних вимкнена: рядок підключення порожній.", ConsoleColor.Yellow);
            Console.WriteLine("Щоб увімкнути, вкажіть його в src/SmartArchiver.Cli/appsettings.Local.json (див. README).");
            return;
        }

        store.EnsureCreated();
        Write($"[OK] База даних готова: {store.Description}", ConsoleColor.Green);
        Console.WriteLine($"Збережено прогонів вимірювання Q: {store.CountRuns()}");
    }

    /// <summary>
    /// Рядок підключення читається з appsettings.json і appsettings.Local.json поруч із програмою.
    /// До сервера звертаються лише команди, яким потрібна БД, тож pack і unpack працюють і без неї.
    /// </summary>
    private static IMeasurementStore OpenMeasurementStore() =>
        MeasurementStore.Open(DatabaseSettings.ReadConnectionString(AppContext.BaseDirectory));

    // ------------------------------------------------------------------
    // Демонстрація
    // ------------------------------------------------------------------

    private static void Demo()
    {
        const int blockSize = 65536;

        Section("Крок 1. Створюємо чотири тестові файли (в пам'яті, на диск нічого не пишемо)");
        var big = new byte[200_000];
        new Random(1).NextBytes(big);
        var inputs = new List<ArchiveInput>
        {
            new ArchiveInput("hello.txt", Encoding.UTF8.GetBytes("Привіт, SmartArchiver!"), FileKind.Text),
            new ArchiveInput("empty.txt", Array.Empty<byte>()),
            new ArchiveInput("one.bin", new byte[] { 42 }),
            new ArchiveInput("big.bin", big),
        };
        foreach (ArchiveInput input in inputs)
        {
            Console.WriteLine($"  {input.Name,-12} {input.Data.Length,8} байт");
        }
        Pause();

        Section($"Крок 2. Пакуємо в один том (блок {blockSize} байт) і читаємо назад");
        byte[] archive = new ArchiveWriter(blockSize).Build(inputs);
        IReadOnlyList<ExtractedFile> restored = ArchiveReader.Read(archive);
        PrintFileTable(restored, blockSize);
        Console.WriteLine($"Розмір архіву: {archive.Length} байт. Файл big.bin розбито на блоки, порожній і однобайтовий файли теж працюють.");
        if (SameContent(inputs, restored))
        {
            Write("[OK] Усі файли відновлено побайтово.", ConsoleColor.Green);
        }
        else
        {
            Write("[ПОМИЛКА] Файли не збігаються!", ConsoleColor.Red);
        }
        Pause();

        Section("Крок 3. Перевірка безпечних імен");
        string[] candidates = { "звіт.txt", "../evil.txt", "dir/file.txt", "C:\\Windows\\x.dll", "..", "a\0b" };
        foreach (string candidate in candidates)
        {
            string shown = candidate.Replace("\0", "\\0");
            if (SafeNames.IsSafe(candidate, out string reason))
            {
                Write($"  [OK]       \"{shown}\" дозволено", ConsoleColor.Green);
            }
            else
            {
                Write($"  [ВІДХИЛЕНО] \"{shown}\": {reason}", ConsoleColor.Yellow);
            }
        }
        Pause();

        Section("Крок 4. Що буде з пошкодженим архівом");
        var flipped = (byte[])archive.Clone();
        flipped[^1] ^= 0xFF;
        ShowRejection("Змінено останній байт даних", flipped);

        byte[] truncated = archive.AsSpan(0, archive.Length - 10).ToArray();
        ShowRejection("Архів обрізано на 10 байт", truncated);

        var badMagic = (byte[])archive.Clone();
        badMagic[0] = (byte)'X';
        ShowRejection("Зіпсовано початок файлу (MAGIC)", badMagic);

        Console.WriteLine();
        Write("Демонстрація завершена. Програма не впала на жодному з пошкоджених архівів.", ConsoleColor.Cyan);
    }

    private static void ShowRejection(string what, byte[] damaged)
    {
        try
        {
            ArchiveReader.Read(damaged);
            Write($"  {what}: архів прийнято (це неочікувано!)", ConsoleColor.Red);
        }
        catch (ArchiveException ex)
        {
            Write($"  {what}: відхилено, код {ex.Code}", ConsoleColor.Green);
        }
    }

    // ------------------------------------------------------------------
    // Допоміжне
    // ------------------------------------------------------------------

    private static bool SameContent(IReadOnlyList<ArchiveInput> inputs, IReadOnlyList<ExtractedFile> restored)
    {
        if (inputs.Count != restored.Count)
        {
            return false;
        }
        for (int i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].Name != restored[i].Name || !inputs[i].Data.AsSpan().SequenceEqual(restored[i].Data.AsSpan()))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Таблиця файлів. Якщо blockSize більший за нуль, додається стовпець із кількістю блоків.</summary>
    private static void PrintFileTable(IReadOnlyList<ExtractedFile> files, int blockSize)
    {
        string blocksHeader = blockSize > 0 ? "  Блоків" : string.Empty;
        Console.WriteLine($"  {"№",3}  {"Ім'я",-26} {"Розмір",10}  {"CRC32",-8}  {"Тип",-17} {"Стратегія",-14}{blocksHeader}");
        for (int i = 0; i < files.Count; i++)
        {
            ExtractedFile f = files[i];
            string blocks = string.Empty;
            if (blockSize > 0)
            {
                long count = f.Data.Length == 0 ? 0 : (f.Data.Length + (long)blockSize - 1) / blockSize;
                blocks = $"  {count}";
            }
            Console.WriteLine($"  {i + 1,3}  {Fit(f.Name, 26),-26} {f.Data.Length,10}  {f.Crc32:X8}  {f.Kind,-17} {f.Strategy,-14}{blocks}");
        }
        if (files.Count == 0)
        {
            Console.WriteLine("  (архів порожній)");
        }
    }

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text.Substring(0, width - 3) + "...";

    private static string Explain(ArchiveException ex)
    {
        string meaning = ex.Code switch
        {
            ErrorCode.E_MAGIC => "це не архів SmartArchiver або непідтримувана версія формату",
            ErrorCode.E_LENGTH => "архів обрізаний або має зайві байти",
            ErrorCode.E_HMAC => "перевірка автентичності не пройшла",
            ErrorCode.E_PAYLOAD => "внутрішня структура архіву пошкоджена або містить небезпечні дані",
            ErrorCode.E_CODE => "невідома або ще не підтримувана стратегія стиснення",
            ErrorCode.E_CRC => "вміст файлу змінено: CRC32 не збігається",
            _ => "помилка архіву",
        };
        return $"[ПОМИЛКА] {ex.Code}: {meaning}.{Environment.NewLine}  Деталі: {ex.Message}";
    }

    private static string? Prompt(string text, string? defaultValue = null)
    {
        Console.Write(defaultValue is null ? $"{text}: " : $"{text} [{defaultValue}]: ");
        string? line = Console.ReadLine();
        if (line is null)
        {
            return null;
        }
        line = line.Trim().Trim('"');   // перетягнутий у вікно файл приходить у лапках
        return line.Length == 0 && defaultValue is not null ? defaultValue : line;
    }

    private static bool Confirm(string question)
    {
        string? answer = Prompt($"{question} (т/н)", "н");
        return answer is not null
            && (answer.StartsWith("т", StringComparison.OrdinalIgnoreCase)
                || answer.StartsWith("y", StringComparison.OrdinalIgnoreCase));
    }

    private static void Pause()
    {
        Console.Write("  Натисніть Enter, щоб продовжити...");
        Console.ReadLine();
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Write(title, ConsoleColor.Cyan);
    }

    private static void Write(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void TrySetUtf8()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            // Консоль не підтримує зміну кодування: працюємо з тим, що є.
        }
    }
}
