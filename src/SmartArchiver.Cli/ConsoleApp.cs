using System.Globalization;
using System.Text.Json;
using SmartArchiver.Compression;
using System.Text;
using SmartArchiver.Analysis;
using SmartArchiver.Archive;
using SmartArchiver.Core;
using SmartArchiver.Data;
using SmartArchiver.Data.Entities;
using ArchiveVariant = SmartArchiver.Archive.StrategyVariant;
using DataVariant = SmartArchiver.Data.Entities.StrategyVariant;
using SmartArchiver.Compression.Benchmarks;

namespace SmartArchiver.Cli;

/// <summary>
/// Консольний інтерфейс чекпоінта 1.
/// Без аргументів запускає інтерактивне меню, з аргументами працює як звичайна команда
/// (pack, unpack, info, demo, db, analyze, experiment).
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
                PackCommand(args);
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
            case "analyze" when args.Length == 2:
                AnalyzeFile(args[1]);
                return 0;
            case "train-classifier" when args.Length <= 3:
                TrainClassifier(args.Length > 1 ? args[1] : "Datasets", args.Length > 2 ? args[2] : "classifier.json");
                return 0;
            case "train-predictor" when args.Length <= 3:
                TrainPredictor(args.Length > 1 ? args[1] : "Datasets", args.Length > 2 ? args[2] : "predictor.json");
                return 0;
            case "prediction-experiment" when args.Length <= 2:
                RunPredictionExperiment(args.Length > 1 ? args[1] : "Datasets");
                return 0;
            case "strategy-experiment" when args.Length <= 2:
                RunStrategyExperiment(args.Length > 1 ? args[1] : "Datasets");
                return 0;
            case "experiment" when args.Length == 2:
                RunParsingComparison(args[1]);
                return 0;
            case "pack-experiment" when args.Length <= 4:
                PackExperimentCommand(args);
                return 0;
            case "measure" when args.Length <= 3:
                MeasureQ(args.Length > 1 ? args[1] : "Datasets", args.Length > 2 ? args[2] : null);
                return 0;
            case "export-ga" when args.Length == 3:
                return ExportGeneticJournal(args[1], args[2]);
            default:
                Console.WriteLine("Використання:");
                Console.WriteLine("  smartarchiver                            інтерактивне меню");
                Console.WriteLine("  smartarchiver pack <archive.arc> [--volume-size <байт>] <файл>...  запакувати: аналіз, стиснення, шифрування, томи");
                Console.WriteLine("  smartarchiver unpack <archive.arc> <тека>   розпакувати атомарно (для томів name.partN.arc достатньо вказати будь-який том)");
                Console.WriteLine("  smartarchiver info <archive.arc>            показати вміст");
                Console.WriteLine("  smartarchiver demo                          автоматична демонстрація");
                Console.WriteLine("  smartarchiver db                            стан бази даних (створює її, якщо ще немає)");
                Console.WriteLine("  smartarchiver analyze <файл>                ознаки, тип файлу і стратегія стиснення");
                Console.WriteLine("  smartarchiver train-classifier [датасет] [модель.json]  навчити класифікатор (за замовчуванням Datasets)");
                Console.WriteLine("  smartarchiver train-predictor [датасет] [модель.json]   навчити передбачення наступного байта");
                Console.WriteLine("  smartarchiver prediction-experiment [датасет]          вплив передбачення на ентропію і розмір");
                Console.WriteLine("  smartarchiver strategy-experiment [датасет]            фіксована стратегія проти класифікатора");
                Console.WriteLine("  smartarchiver experiment <тека>            порівняти Greedy та Lazy на Datasets");
                Console.WriteLine("  smartarchiver pack-experiment [датасет] [місткість тому] [team_config.json]  ДП-рюкзак проти жадібного пакування томів");
                Console.WriteLine("  smartarchiver measure [датасет] [team_config.json]  виміряти Q на всьому корпусі, результат у БД");
                Console.WriteLine("  smartarchiver export-ga <id експерименту> <файл.csv>  журнал еволюції ГА з БД у CSV");
                return 1;
        }
    }

    // ------------------------------------------------------------------
    // Інтерактивне меню
    // ------------------------------------------------------------------

    private static int Interactive()
    {
        Write("==============================================", ConsoleColor.Cyan);
        Write("  SmartArchiver, чекпоінт 3: повний тракт зі стисненням і шифруванням", ConsoleColor.Cyan);
        Write("  (пароль береться зі змінної SMARTARCHIVER_PASSWORD або вводиться без показу)", ConsoleColor.DarkCyan);
        Write("==============================================", ConsoleColor.Cyan);

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("  1) Запакувати файли в архів");
            Console.WriteLine("  2) Розпакувати архів");
            Console.WriteLine("  3) Показати вміст архіву (заголовок і таблиця файлів)");
            Console.WriteLine("  4) Демонстрація: усі можливості на прикладі");
            Console.WriteLine("  5) База даних: стан і створення схеми");
            Console.WriteLine("  6) Аналіз файлу: ознаки вмісту");
            Console.WriteLine("  7) Експерименти: порівняння алгоритмів");
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
                case "6": Guard(AnalyzeInteractive); break;
                case "7": Guard(ExperimentsMenu); break;
                case "0":
                    Console.WriteLine("До побачення!");
                    return 0;
                default:
                    Write("Невідомий пункт. Введіть число від 0 до 7.", ConsoleColor.Yellow);
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

        string? sizeText = Prompt("Розмір блока в байтах (великі файли ріжуться на блоки)", FormatConstants.DefaultBlockSize.ToString(CultureInfo.InvariantCulture));
        int blockSize = FormatConstants.DefaultBlockSize;
        if (!int.TryParse(sizeText, out blockSize) || blockSize <= 0)
        {
            blockSize = FormatConstants.DefaultBlockSize;
            Write($"Некоректне значення, беремо {blockSize}.", ConsoleColor.Yellow);
        }

        string? volumeText = Prompt("Найбільший розмір тому в байтах (0 = один том без обмеження)", "0");
        if (!long.TryParse(volumeText, NumberStyles.None, CultureInfo.InvariantCulture, out long volumeSize))
        {
            volumeSize = 0;
            Write("Некоректне значення, пишемо один том.", ConsoleColor.Yellow);
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

        PackFiles(archivePath, paths.ToArray(), blockSize, volumeSize);
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

    private static void AnalyzeInteractive()
    {
        string? path = Prompt("Шлях до файлу");
        if (string.IsNullOrEmpty(path))
        {
            Write("Шлях не вказано.", ConsoleColor.Yellow);
            return;
        }
        AnalyzeFile(path);
    }

    /// <summary>Меню експериментів другого чекпоінту.</summary>
    private static void ExperimentsMenu()
    {
        while (true)
        {
            Console.WriteLine();
            Write("========== Експерименти ==========", ConsoleColor.Cyan);
            Console.WriteLine("  1) Порівняти Greedy та Lazy LZ77");
            Console.WriteLine("  2) Навчити класифікатор типу файлу");
            Console.WriteLine("  3) Вплив передбачення наступного байта");
            Console.WriteLine("  4) ДП-рюкзак проти жадібного пакування томів");
            Console.WriteLine("  5) Фіксована стратегія стиснення проти класифікатора");
            Console.WriteLine("  6) Виміряти Q на всьому корпусі");
            Console.WriteLine("  7) Ітеративний DP + Huffman (D1 №19)");
            Console.WriteLine("  8) Порівняння арифметичного кодування з Гаффманом");
            Console.WriteLine("  9) Бонус CP3 №4: SmartArchiver проти zip -9");
            Console.WriteLine("  0) Повернутися до головного меню");

            string? choice = Prompt("Ваш вибір");
            switch (choice)
            {
                case "1":
                    Guard(ParsingComparisonInteractive);
                    break;
                case "2":
                    Guard(TrainClassifierInteractive);
                    break;
                case "3":
                    Guard(PredictionExperimentInteractive);
                    break;
                case "4":
                    Guard(PackingExperimentInteractive);
                    break;
                case "5":
                    Guard(StrategyExperimentInteractive);
                    break;
                case "6":
                    Guard(MeasureInteractive);
                    break;
                case "7":
                    Guard(IterativeDpExperimentInteractive);
                    break;
                case "8":
                    Guard(ArithmeticExperimentInteractive);
                    break;
                case "9":
                    Guard(Zip9BonusInteractive);
                    break;
                case "0":
                case null:
                    return;
                default:
                    Write("Невідомий пункт. Введіть число від 0 до 6.", ConsoleColor.Yellow);
                    break;
            }
        }
    }

    private static void ParsingComparisonInteractive()
    {
        // Папку з даними можна вказати будь-яку; за замовчуванням Datasets.
        string? path = Prompt("Каталог із групами 0..6 (Натисніть Enter для використання за замовчуванням)", "Datasets");
        if (path is null)
            return;

        RunParsingComparison(path);
    }

    private static void RunParsingComparison(string datasetPath)
    {
        Write("Запускаємо порівняння Greedy та Lazy...", ConsoleColor.Cyan);

        // Бібліотека Compression виконує вимірювання без доступу до БД.
        IReadOnlyList<ParsingGroupMeasurement> measurements =
            ParsingComparisonRunner.RunDirectory(datasetPath);

        Console.WriteLine();
        Console.WriteLine($"  {"Група",-7} {"Метод",-8} {"Було (байт)",14} {"Стало (байт)",15} {"Час (с)",12}");
        foreach (ParsingGroupMeasurement measurement in measurements)
        {
            Console.WriteLine($"  {measurement.Group,-7} {measurement.Method,-8} {measurement.OriginalBytes,14} {measurement.EncodedBytes,15} {measurement.ElapsedSeconds,12:F4}");
        }

        // Створюємо сутності Data лише на рівні застосунку.
        var experiment = new Experiment
        {
            Kind = ExperimentKind.Parsing,
            StartedAtUtc = DateTime.UtcNow,
            Dataset = Path.GetFullPath(datasetPath),
            ParametersJson = JsonSerializer.Serialize(new
            {
                blockSize = ParsingComparisonRunner.BlockSize,
                maxChain = 256,
                minLazyGain = 2,
                timeScope = "LZ77 parsing + Huffman encoding",
                sizeScope = "encoded block without archive container",
                grouping = "subdirectories 0..6"
            })
        };

        foreach (ParsingGroupMeasurement measurement in measurements)
        {
            experiment.ParsingResults.Add(new ParsingResult
            {
                Group = measurement.Group,
                Method = measurement.Method switch
                {
                    Lz77ParsingMethod.Greedy => ParsingMethod.Greedy,
                    Lz77ParsingMethod.Lazy => ParsingMethod.Lazy,

                    // Захист від невідомого значення переліку.
                    _ => throw new InvalidOperationException(
                        $"Непідтримуваний метод розбору LZ77: {measurement.Method}")
                },
                OriginalBytes = measurement.OriginalBytes,
                EncodedBytes = measurement.EncodedBytes,
                ElapsedSeconds = measurement.ElapsedSeconds
            });
        }

        IMeasurementStore store = OpenMeasurementStore();
        if (store.IsEnabled)
        {
            store.SaveExperiment(experiment);
            Write($"[OK] Експеримент №{experiment.Id} збережено в базі даних.", ConsoleColor.Green);
        }
        else
        {
            Write("[УВАГА] База даних вимкнена: результати виведено лише в консоль.", ConsoleColor.Yellow);
        }
    }

    // ------------------------------------------------------------------
    // Експеримент: ДП-рюкзак проти жадібного пакування томів
    // ------------------------------------------------------------------

    /// <summary>Розмір блока в експерименті: щоб блок (до 16 КіБ + 15 байт) влазив у том навіть невеликої місткості.</summary>
    private const int PackingBlockSize = 16_384;

    /// <summary>Місткість тому за замовчуванням, поки немає team_config.json: корпус займає кілька томів.</summary>
    private const long DefaultPackingVolumeBytes = 65_536;

    private static void PackExperimentCommand(string[] args)
    {
        string dataset = args.Length > 1 ? args[1] : "Datasets";
        long volumeBytes = DefaultPackingVolumeBytes;
        if (args.Length > 2 && (!long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out volumeBytes) || volumeBytes <= 0))
        {
            throw new ArgumentException("Місткість тому має бути додатним цілим числом (байт).");
        }
        RunPackingExperiment(dataset, volumeBytes, args.Length > 3 ? args[3] : null);
    }

    private static void PackingExperimentInteractive()
    {
        string? dataset = Prompt("Каталог із групами 0..6", "Datasets");
        if (dataset is null)
            return;

        string? sizeText = Prompt("Місткість тому в байтах (max_volume_bytes)", DefaultPackingVolumeBytes.ToString(CultureInfo.InvariantCulture));
        if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out long volumeBytes) || volumeBytes <= 0)
        {
            volumeBytes = DefaultPackingVolumeBytes;
            Write($"Некоректне значення, беремо {volumeBytes}.", ConsoleColor.Yellow);
        }
        RunPackingExperiment(dataset, volumeBytes, null);
    }

    /// <summary>
    /// Усі файли корпусу проходять аналіз і стиснення, потім їхні блоки розкладаються по томах двома способами:
    /// ДП-рюкзаком і жадібно. Обидва томи-набори записуються, читаються назад і звіряються з оригіналами.
    /// Результати (по рядку на том і метод) пишуться в базу, якщо вона ввімкнена.
    /// </summary>
    private static void RunPackingExperiment(string datasetPath, long maxVolumeBytes, string? configPath)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        PackingPriorities priorities = PackingPriorities.Default;
        if (configPath is not null)
        {
            TeamConfig config = TeamConfig.Load(configPath);
            maxVolumeBytes = config.MaxVolumeBytes;
            priorities = config.Priorities;
        }

        (List<ArchiveInput> inputs, _) = LoadCorpus(datasetPath);

        Console.WriteLine();
        Write("Аналіз і стиснення файлів корпусу...", ConsoleColor.Cyan);
        var writer = new ArchiveWriter(PackingBlockSize, new FileAnalyzer());
        IReadOnlyList<PreparedFile> prepared = writer.Prepare(inputs);
        PackingComparison comparison = VolumePacker.Compare(prepared, maxVolumeBytes, priorities);

        int blockCount = prepared.Sum(f => f.Blocks.Count);
        long codedBytes = prepared.Sum(f => f.CodedSize);
        long originalBytes = prepared.Sum(f => (long)f.Size);
        Console.WriteLine(string.Format(c, "  Файлів: {0}, блоків: {1}, вихідний обсяг: {2} байт, після стиснення: {3} байт, місткість тому: {4} байт",
            prepared.Count, blockCount, originalBytes, codedBytes, maxVolumeBytes));
        Console.WriteLine($"  Вага блока = coded_len + {FormatConstants.BlockHeaderSize} байт, цінність за типом файлу: {string.Join(", ", priorities.Values)} (типи 0..6)");
        if (configPath is null)
        {
            Write("  [УВАГА] Пріоритети типів стандартні (заглушка): справжні задає team_config.json (4-й параметр команди).", ConsoleColor.Yellow);
        }

        Console.WriteLine();
        Console.WriteLine($"  {"Том",4} {"Метод",-7} {"Блоків",7} {"Використано",12} {"Місткість W",12} {"Цінність",9} {"Час, мс",9}");
        int rows = Math.Max(comparison.Dp.Volumes.Count, comparison.Greedy.Volumes.Count);
        for (int volume = 0; volume < rows; volume++)
        {
            PrintPackingRow(volume, "ДП", comparison.Dp);
            PrintPackingRow(volume, "Жадібний", comparison.Greedy);
        }
        Console.WriteLine();
        foreach ((string label, PackingPlan plan) in new[] { ("ДП", comparison.Dp), ("Жадібний", comparison.Greedy) })
        {
            Console.WriteLine(string.Format(c, "  {0,-9} томів {1,3}, блоків {2,4}, використано {3,9} байт, цінність {4,6}, час {5,8:F3} мс",
                label, plan.Volumes.Count, plan.TotalBlocks, plan.TotalUsedBytes, plan.TotalValue, plan.TotalSeconds * 1000));
        }
        VolumePlan firstDp = comparison.Dp.Volumes[0];
        VolumePlan firstGreedy = comparison.Greedy.Volumes[0];
        Console.WriteLine(string.Format(c, "  Перший том: цінність ДП {0}, жадібний {1}. Обидва методи зрештою розкладають усі блоки, різниця в тому, що потрапляє в ранні томи.",
            firstDp.TotalValue, firstGreedy.TotalValue));

        // Перевірка: томи обох розкладок записуються, читаються назад і дають вихідні файли.
        foreach (PackingPlan plan in new[] { comparison.Dp, comparison.Greedy })
        {
            IReadOnlyList<byte[]> volumes = ArchiveWriter.BuildVolumes(prepared, plan);
            IReadOnlyList<ExtractedFile> restored = ArchiveReader.ReadVolumes(volumes);
            if (!SameContent(inputs, restored))
            {
                throw new InvalidOperationException($"Томи розкладки {plan.Algorithm} не відновили вихідні файли.");
            }
        }
        Write("[OK] Томи обох розкладок записано, прочитано назад і звірено з оригіналами побайтово.", ConsoleColor.Green);

        var experiment = new Experiment
        {
            Kind = ExperimentKind.Packing,
            StartedAtUtc = DateTime.UtcNow,
            Dataset = Path.GetFullPath(datasetPath),
            ParametersJson = JsonSerializer.Serialize(new
            {
                maxVolumeBytes,
                blockSize = PackingBlockSize,
                parseMode = "Lazy",
                weight = "coded_len + 15",
                priorities = priorities.Values,
                prioritiesSource = configPath is null ? "default placeholder" : Path.GetFileName(configPath),
                capacity = "max_volume_bytes - 41 - 32 - 4 - file table of the volume",
            }),
        };
        AddPackingResults(experiment, comparison.Dp, PackingMethod.Dp);
        AddPackingResults(experiment, comparison.Greedy, PackingMethod.Greedy);

        IMeasurementStore store = OpenMeasurementStore();
        if (store.IsEnabled)
        {
            store.SaveExperiment(experiment);
            Write($"[OK] Результати пакування збережено в базі даних (експеримент №{experiment.Id}).", ConsoleColor.Green);
        }
        else
        {
            Write("[УВАГА] База даних вимкнена: результати виведено лише в консоль.", ConsoleColor.Yellow);
        }
    }

    private static void PrintPackingRow(int volume, string label, PackingPlan plan)
    {
        if (volume >= plan.Volumes.Count)
            return;

        VolumePlan v = plan.Volumes[volume];
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,4} {1,-8} {2,6} {3,12} {4,12} {5,9} {6,9:F3}",
            volume, label, v.BlockCount, v.UsedBytes, v.CapacityBytes, v.TotalValue, v.ElapsedSeconds * 1000));
    }

    private static void AddPackingResults(Experiment experiment, PackingPlan plan, PackingMethod method)
    {
        foreach (VolumePlan volume in plan.Volumes)
        {
            experiment.PackingResults.Add(new PackingResult
            {
                Method = method,
                VolumeIndex = volume.VolumeIndex,
                CapacityBytes = volume.CapacityBytes,
                UsedBytes = volume.UsedBytes,
                TotalValue = volume.TotalValue,
                BlockCount = volume.BlockCount,
                ElapsedSeconds = volume.ElapsedSeconds,
            });
        }
    }

    // ------------------------------------------------------------------
    // Вимір Q на всьому корпусі
    // ------------------------------------------------------------------

    /// <summary>Місткість тому для виміру, поки в team_config.json немає max_volume_bytes: корпус займає кілька томів.</summary>
    private const long DefaultMeasureVolumeBytes = 262_144;

    /// <summary>Пароль виміру. Це не секрет: вимір не зберігає архіви, а пароль не потрапляє ні в базу, ні в логи.</summary>
    private const string MeasurementPassword = "algo2026";

    private static void MeasureInteractive()
    {
        string? dataset = Prompt("Каталог із групами 0..6", "Datasets");
        if (dataset is null)
            return;
        string? config = Prompt("team_config.json (Enter = стандартні α, β, t_ref і місткість)", "");
        MeasureQ(dataset, string.IsNullOrEmpty(config) ? null : config);
    }

    /// <summary>
    /// Одна команда на весь корпус: кожен файл проходить повний тракт з шифруванням і назад, Q = α·S + β·T,
    /// прогін і результати по файлах пишуться в MeasurementRuns і MeasurementFileResults, якщо база ввімкнена.
    /// </summary>
    private static void MeasureQ(string datasetPath, string? configPath)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        TeamConfig config = configPath is null
            ? new TeamConfig(DefaultMeasureVolumeBytes, PackingPriorities.Default)
            : TeamConfig.Load(configPath);
        (List<ArchiveInput> inputs, List<byte> groups) = LoadCorpus(datasetPath);
        var options = new PackOptions(MeasurementPassword, new FileAnalyzer(), MaxVolumeBytes: config.MaxVolumeBytes, Priorities: config.Priorities);

        DateTime started = DateTime.UtcNow;
        QMeasurement m = CorpusMeasurer.Measure([.. inputs.Select((input, i) => (input, groups[i]))], options, config.Alpha, config.Beta, config.TRefSeconds);

        Console.WriteLine();
        Console.WriteLine($"  {"Група",5} {"Файлів",7} {"Було, Б",10} {"В архіві, Б",12} {"Відновлено",11}");
        foreach (IGrouping<byte, FileMeasurement> group in m.Files.GroupBy(f => f.Group).OrderBy(g => g.Key))
        {
            Console.WriteLine(string.Format(c, "  {0,5} {1,7} {2,10} {3,12} {4,11}", group.Key, group.Count(),
                group.Sum(f => f.OriginalBytes), group.Sum(f => f.ArchiveBytes), group.All(f => f.Restored) ? "так" : "НІ"));
        }
        Console.WriteLine();
        Console.WriteLine(string.Format(c, "  B_orig {0} Б, B_arc {1} Б у {2} том(ах), t {3:F3} с", m.OriginalBytes, m.ArchiveBytes, m.Volumes, m.Seconds));
        Console.WriteLine(string.Format(c, "  S = {0:F4}, T = {1:F4}, Q = {2:F4}  (α = {3}, β = {4}, t_ref = {5} с)",
            m.Score.S, m.Score.T, m.Score.Q, config.Alpha, config.Beta, config.TRefSeconds));
        if (configPath is null)
        {
            Write("  [УВАГА] α, β, t_ref і місткість тому стандартні (заглушка): справжні задає team_config.json (2-й параметр команди).", ConsoleColor.Yellow);
        }
        if (m.AllRestored)
        {
            Write("[OK] Усі файли відновлено побайтово.", ConsoleColor.Green);
        }
        else
        {
            Write("[ПОМИЛКА] Є файли, що не відновилися: Q = 0.", ConsoleColor.Red);
        }

        IMeasurementStore store = OpenMeasurementStore();
        if (!store.IsEnabled)
        {
            Write("[УВАГА] База даних вимкнена: результати виведено лише в консоль.", ConsoleColor.Yellow);
            return;
        }

        var run = new MeasurementRun
        {
            StartedAtUtc = started,
            CommitHash = GitCommit(),
            Dataset = DatasetName(datasetPath),
            Alpha = config.Alpha,
            Beta = config.Beta,
            TRefSeconds = config.TRefSeconds,
            ParametersJson = JsonSerializer.Serialize(new
            {
                blockSize = options.BlockSize,
                maxVolumeBytes = config.MaxVolumeBytes,
                volumes = m.Volumes,
                packing = "DP knapsack",
                parseMode = "Lazy",
                encryption = "AES-256-CTR + HMAC-SHA256, PBKDF2 once per archive",
                config = configPath is null ? "defaults (placeholder)" : Path.GetFileName(configPath),
            }),
            OriginalBytes = m.OriginalBytes,
            ArchiveBytes = m.ArchiveBytes,
            ElapsedSeconds = m.Seconds,
            AllRestored = m.AllRestored,
            Q = m.Score.Q,
            Files = [.. m.Files.Select(f => new MeasurementFileResult
            {
                Group = f.Group,
                OriginalBytes = f.OriginalBytes,
                ArchiveBytes = f.ArchiveBytes,
                Restored = f.Restored,
            })],
        };
        store.SaveRun(run);
        Write($"[OK] Прогін №{run.Id} збережено в базі даних (файли без імен, пароль не зберігається).", ConsoleColor.Green);
    }

    /// <summary>Файли корпусу (теки 0..6) з їхніми групами. Однакові імена з різних груп отримують префікс групи.</summary>
    private static (List<ArchiveInput> Inputs, List<byte> Groups) LoadCorpus(string datasetPath)
    {
        IReadOnlyList<LabeledFile> corpus = LabeledDataset.ListFiles(datasetPath);
        if (corpus.Count == 0)
        {
            throw new ArgumentException($"У каталозі {datasetPath} немає файлів корпусу (теки 0..6).");
        }

        var inputs = new List<ArchiveInput>();
        var groups = new List<byte>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (LabeledFile file in corpus)
        {
            string name = Path.GetFileName(file.Source);
            if (!names.Add(name))
            {
                name = $"{file.Label}_{name}";
                names.Add(name);
            }
            inputs.Add(new ArchiveInput(name, File.ReadAllBytes(file.FullPath)));
            groups.Add((byte)file.Label);
        }
        return (inputs, groups);
    }

    private static string DatasetName(string datasetPath)
    {
        string name = Path.GetFileName(Path.GetFullPath(datasetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        name = name.Length == 0 ? "corpus" : name;
        return name.Length > 100 ? name[..100] : name;
    }

    /// <summary>Коротка назва поточного коміту для журналу прогону; null, якщо git недоступний.</summary>
    private static string? GitCommit()
    {
        try
        {
            using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short=12 HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            string? line = git?.StandardOutput.ReadLine();
            git?.WaitForExit(2000);
            return git is { ExitCode: 0 } && !string.IsNullOrWhiteSpace(line) ? line.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Пароль береться зі змінної SMARTARCHIVER_PASSWORD (для скриптів) або вводиться без показу на екрані.
    /// У командний рядок його не передають: він залишився б в історії оболонки.
    /// </summary>
    private static string ReadPassword(bool confirm)
    {
        string? password = Environment.GetEnvironmentVariable("SMARTARCHIVER_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            password = ReadHidden("Пароль");
            if (confirm && !Console.IsInputRedirected && ReadHidden("Повторіть пароль") != password)
            {
                throw new ArgumentException("Паролі не збігаються.");
            }
        }
        return string.IsNullOrEmpty(password) ? throw new ArgumentException("Пароль не може бути порожнім.") : password;
    }

    private static string ReadHidden(string label)
    {
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        Console.Write($"{label}: ");
        var typed = new StringBuilder();
        for (ConsoleKeyInfo key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(intercept: true))
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                typed.Length = Math.Max(0, typed.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return typed.ToString();
    }

    // ------------------------------------------------------------------
    // Дії
    // ------------------------------------------------------------------

    /// <summary>pack &lt;archive&gt; [--volume-size N] файл... : розбирає параметри командного рядка.</summary>
    private static void PackCommand(string[] args)
    {
        long volumeSize = 0;
        var paths = new List<string>();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--volume-size" && i + 1 < args.Length)
            {
                i++;
                if (!long.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out volumeSize) || volumeSize <= 0)
                {
                    throw new ArgumentException("--volume-size має бути додатним цілим числом (байт).");
                }
            }
            else
            {
                paths.Add(args[i]);
            }
        }
        if (paths.Count == 0)
        {
            throw new ArgumentException("Не вказано жодного файлу для архіву.");
        }
        PackFiles(args[1], [.. paths], FormatConstants.DefaultBlockSize, volumeSize);
    }

    /// <summary>
    /// Запаковує файли: аналізатор обирає стратегію для кожного, шум і стиснене йдуть як є, решта кодується LZ77 + Гаффманом.
    /// Якщо задано розмір тому, блоки розкладає по томах ДП-рюкзак; томи називаються назва.partN.arc.
    /// </summary>
    private static void PackFiles(string archivePath, string[] paths, int blockSize, long volumeSize = 0)
    {
        var inputs = new List<ArchiveInput>();
        foreach (string path in paths)
        {
            inputs.Add(new ArchiveInput(Path.GetFileName(path), File.ReadAllBytes(path)));
        }

        if (Directory.Exists(archivePath))
        {
            archivePath = Path.Combine(archivePath, "archive.arc");
        }

        // Повний тракт: аналіз → блоки → LZ77 → Гаффман → пакування → AES → HMAC → томи.
        var options = new PackOptions(ReadPassword(confirm: true), new FileAnalyzer(), blockSize, volumeSize);
        IReadOnlyList<PreparedFile> prepared = ArchivePipeline.Prepare(inputs, options);
        IReadOnlyList<byte[]> volumes = ArchivePipeline.Seal(prepared, options);
        var outputs = new List<string>();
        if (volumeSize > 0)
        {
            string stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".", Path.GetFileNameWithoutExtension(archivePath));
            string extension = Path.GetExtension(archivePath).Length == 0 ? ".arc" : Path.GetExtension(archivePath);
            for (int i = 0; i < volumes.Count; i++)
            {
                outputs.Add($"{stem}.part{i + 1}{extension}");
            }
        }
        else
        {
            outputs.Add(archivePath);
        }
        for (int i = 0; i < volumes.Count; i++)
        {
            File.WriteAllBytes(outputs[i], volumes[i]);
        }

        // Одразу читаємо назад (з перевіркою HMAC і CRC32) і порівнюємо з оригіналом.
        ArchiveResult<IReadOnlyList<ExtractedFile>> check = ArchivePipeline.TryUnpack(volumes, options.Password);
        bool identical = check.Ok && SameContent(inputs, check.Value!);

        long originalSize = inputs.Sum(i => (long)i.Data.Length);
        long archiveSize = volumes.Sum(v => (long)v.Length);
        Console.WriteLine();
        foreach (string output in outputs)
        {
            Write($"[OK] Архів записано: {Path.GetFullPath(output)}", ConsoleColor.Green);
        }
        PrintPreparedTable(prepared);
        Console.WriteLine($"Вихідний розмір: {originalSize} байт, архів (зашифрований): {archiveSize} байт у {volumes.Count} том(ах), різниця: {originalSize - archiveSize} байт.");
        if (identical)
        {
            Write("[OK] Перевірка: після читання назад усі файли збігаються побайтово.", ConsoleColor.Green);
        }
        else
        {
            Write("[ПОМИЛКА] Після читання назад файли не збігаються з оригіналом!", ConsoleColor.Red);
        }
    }

    /// <summary>Що вирішив аналізатор для кожного файлу і скільки він займає після стиснення (без 15-байтових заголовків блоків).</summary>
    private static void PrintPreparedTable(IReadOnlyList<PreparedFile> files)
    {
        Console.WriteLine($"  {"№",3}  {"Ім'я",-26} {"Розмір",10} {"Стиснено",10}  {"Тип",-17} {"Стратегія",-13} {"Блоків (як є / усього)"}");
        for (int i = 0; i < files.Count; i++)
        {
            PreparedFile f = files[i];
            int stored = f.Blocks.Count(b => b.IsStored);
            Console.WriteLine($"  {i + 1,3}  {Fit(f.Name, 26),-26} {f.Size,10} {f.CodedSize,10}  {f.Kind,-17} {f.Strategy,-13} {stored} / {f.Blocks.Count}");
        }
        if (files.Count == 0)
        {
            Console.WriteLine("  (архів порожній)");
        }
    }

    /// <summary>
    /// Томи name.partN.arc: якщо вказано один з них, підхоплюються всі сусідні томи з тією самою назвою.
    /// Звичайний файл читається як один том.
    /// </summary>
    private static List<byte[]> LoadVolumes(string archivePath)
    {
        if (!TryParsePartName(Path.GetFileName(archivePath), out string stem, out _, out string extension))
        {
            return [File.ReadAllBytes(archivePath)];
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        var parts = new List<(int Number, string FullPath)>();
        foreach (string candidate in Directory.GetFiles(directory))
        {
            if (TryParsePartName(Path.GetFileName(candidate), out string candidateStem, out int number, out string candidateExtension)
                && string.Equals(candidateStem, stem, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidateExtension, extension, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add((number, candidate));
            }
        }
        parts.Sort((a, b) => a.Number.CompareTo(b.Number));
        return [.. parts.Select(p => File.ReadAllBytes(p.FullPath))];
    }

    private static bool TryParsePartName(string fileName, out string stem, out int number, out string extension)
    {
        stem = string.Empty;
        number = 0;
        extension = string.Empty;
        int dot = fileName.LastIndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        extension = fileName[dot..];
        string withoutExtension = fileName[..dot];
        int part = withoutExtension.LastIndexOf(".part", StringComparison.OrdinalIgnoreCase);
        if (part < 0)
        {
            return false;
        }

        string digits = withoutExtension[(part + ".part".Length)..];
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }
        stem = withoutExtension[..part];
        return true;
    }

    private static void UnpackFile(string archivePath, string outputDir)
    {
        // Атомарно: або всі файли, або жодного; наявні файли не перезаписуються (див. ArchivePipeline.Extract).
        IReadOnlyList<ExtractedFile> files = ArchivePipeline.Extract(LoadVolumes(archivePath), ReadPassword(confirm: false), outputDir);

        Console.WriteLine();
        Write($"[OK] Розпаковано {files.Count} файл(ів) у {Path.GetFullPath(outputDir)}", ConsoleColor.Green);
        PrintFileTable(files, 0);
        Write("[OK] HMAC і CRC32 кожного файлу збігаються.", ConsoleColor.Green);
    }

    private static void ShowInfo(string archivePath)
    {
        List<byte[]> volumes = LoadVolumes(archivePath);
        IReadOnlyList<ExtractedFile> files = ArchivePipeline.Unpack(volumes, ReadPassword(confirm: false));

        foreach (byte[] bytes in volumes)
        {
            VolumeHeader header = VolumeHeader.Parse(bytes);
            Console.WriteLine();
            Write("Заголовок тому (41 байт)", ConsoleColor.Cyan);
            Console.WriteLine($"  Magic:            {Encoding.ASCII.GetString(FormatConstants.Magic)}");
            Console.WriteLine($"  Версія формату:   {header.Version}");
            Console.WriteLine($"  Том:              {header.VolumeNumber} з {header.VolumeCount}");
            Console.WriteLine($"  Довжина payload:  {header.CiphertextLength} байт");
            Console.WriteLine($"  Сіль:             {Convert.ToHexString(header.Salt)}");
            Console.WriteLine($"  Nonce:            {Convert.ToHexString(header.Nonce)}");
            Console.WriteLine($"  Розмір файлу:     {bytes.Length} байт");
        }
        Console.WriteLine();
        Write("Таблиця файлів", ConsoleColor.Cyan);
        PrintFileTable(files, 0);
        Write("[OK] HMAC вірний, структура коректна, CRC32 усіх файлів збігається.", ConsoleColor.Green);
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

    /// <summary>Журнал еволюції ГА (усі хромосоми й пристосованість) з БД у CSV, який зберігається в репозиторії.</summary>
    private static int ExportGeneticJournal(string experimentId, string csvPath)
    {
        if (!int.TryParse(experimentId, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
        {
            Write($"[ПОМИЛКА] Номер експерименту має бути цілим числом: {experimentId}", ConsoleColor.Red);
            return 1;
        }
        IMeasurementStore store = OpenMeasurementStore();
        if (!store.IsEnabled)
        {
            Write("База даних вимкнена: журнал ГА зберігається лише в ній.", ConsoleColor.Yellow);
            return 1;
        }

        Experiment? experiment = store.GetExperiment(id);
        if (experiment is not { Kind: ExperimentKind.Genetic })
        {
            Write($"[ПОМИЛКА] Експерименту ГА з номером {id} немає в базі.", ConsoleColor.Red);
            return 1;
        }

        using (var writer = new StreamWriter(csvPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            GeneticJournalCsv.Write(experiment.GeneticIndividuals, writer);
        Write($"[OK] {experiment.GeneticIndividuals.Count} хромосом записано в {Path.GetFullPath(csvPath)}", ConsoleColor.Green);
        return 0;
    }

    /// <summary>Показує ознаки, за якими класифікатор розрізнятиме типи файлів.</summary>
    private static void AnalyzeFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        FileFeatures f = FeatureExtractor.Compute(data);
        CultureInfo c = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Write($"Ознаки файлу {Path.GetFileName(path)} ({data.Length} байт)", ConsoleColor.Cyan);
        Console.WriteLine(string.Format(c, "  Ентропія байтів:          {0,6:F3} біт із 8    (0 = один байт повторюється, 8 = шум або стиснені дані)", f.Entropy));
        Console.WriteLine(string.Format(c, "  Ентропія пар байтів:      {0,6:F3} біт із 16", f.BigramEntropy));
        Console.WriteLine(string.Format(c, "  Друковані символи ASCII:  {0,6:P1}        (англійський текст і код близько 100%, кирилиця в UTF-8 менше)", f.PrintableAsciiFraction));
        Console.WriteLine(string.Format(c, "  Середня довжина рядка:    {0,6:F1} символів", f.AverageLineLength));

        IEnumerable<string> top = f.Histogram
            .Select((p, b) => (p, b))
            .Where(x => x.p > 0)
            .OrderByDescending(x => x.p)
            .Take(5)
            .Select(x => string.Format(c, "{0} {1:P1}", ByteLabel((byte)x.b), x.p));
        Console.WriteLine($"  Найчастіші байти:         {string.Join(", ", top)}");

        string lags = string.Join(" ", f.Autocorrelation.Select(r => r.ToString("+0.00;-0.00; 0.00", c)));
        Console.WriteLine($"  Автокореляція, лаги 1-16: {lags}");

        FileAnalysis analysis = new FileAnalyzer().Analyze(data);
        Console.WriteLine(string.Format(c, "  Тип за класифікатором:    {0} = {1}, впевненість {2:P0}", (byte)analysis.Kind, KindName(analysis.Kind), analysis.Confidence));
        Console.WriteLine($"  Стратегія стиснення:      {StrategyName(analysis.Strategy)}");
    }

    private static string KindName(FileKind kind) => kind switch
    {
        FileKind.Text => "природний текст",
        FileKind.SourceCode => "вихідний код",
        FileKind.Structured => "структуровані дані",
        FileKind.Raster => "растр без стиснення",
        FileKind.Executable => "виконуваний файл",
        FileKind.AlreadyCompressed => "вже стиснуте",
        FileKind.Artificial => "штучні дані",
        _ => "невідомо",
    };

    private static string StrategyName(CompressionStrategy strategy) => strategy switch
    {
        CompressionStrategy.NoCompression => "без стиснення (зберігається як є)",
        CompressionStrategy.Lz77Huffman => "LZ77 + Гаффман",
        _ => strategy.ToString(),
    };

    private static void StrategyExperimentInteractive()
    {
        string? dataset = Prompt("Каталог із групами 0..6", "Datasets");
        if (dataset is null)
            return;
        RunStrategyExperiment(dataset);
    }

    /// <summary>
    /// Пакує кожну групу датасету в архів трьома способами (без стиснення, завжди LZ77 + Гаффман,
    /// як вирішить класифікатор), розпаковує, порівнює й пише розміри та час у базу.
    /// </summary>
    private static void RunStrategyExperiment(string datasetPath)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        IReadOnlyList<GroupedFile> files = [.. LabeledDataset.ListFiles(datasetPath)
            .Select(f => new GroupedFile(f.Label, new ArchiveInput(Path.GetFileName(f.FullPath), File.ReadAllBytes(f.FullPath))))];

        Console.WriteLine();
        Write($"Фіксована стратегія проти класифікатора: {files.Count} файлів, архів на кожну групу", ConsoleColor.Cyan);
        IReadOnlyList<StrategyMeasurement> results = StrategyComparison.Run(files, new FileAnalyzer());

        Console.WriteLine($"  {"Група",5} {"Варіант",-12} {"Стиснуто",9} {"Було, Б",9} {"Архів, Б",9} {"Запак., с",10} {"Розпак., с",11} {"Відновлено",11}");
        foreach (StrategyMeasurement r in results)
        {
            Console.WriteLine(string.Format(c, "  {0,5} {1,-12} {2,4} з {3,-2} {4,9} {5,9} {6,10:F3} {7,11:F3} {8,11}",
                r.Group, VariantName(r.Variant), r.CompressedFiles, r.FileCount, r.OriginalBytes, r.ArchiveBytes,
                r.PackSeconds, r.UnpackSeconds, r.AllRestored ? "так" : "НІ"));
        }

        Console.WriteLine();
        foreach (ArchiveVariant variant in Enum.GetValues<ArchiveVariant>())
        {
            StrategyMeasurement[] rows = [.. results.Where(r => r.Variant == variant)];
            Console.WriteLine(string.Format(c, "  Разом {0,-12} архів {1,9} Б, запакування {2,7:F3} с, розпакування {3,7:F3} с",
                VariantName(variant), rows.Sum(r => r.ArchiveBytes), rows.Sum(r => r.PackSeconds), rows.Sum(r => r.UnpackSeconds)));
        }
        if (results.All(r => r.AllRestored))
            Write("[OK] Усі файли відновлено побайтово в усіх варіантах.", ConsoleColor.Green);
        else
            Write("[ПОМИЛКА] Не всі файли відновлено побайтово.", ConsoleColor.Red);

        var experiment = new Experiment
        {
            Kind = ExperimentKind.Strategy,
            StartedAtUtc = DateTime.UtcNow,
            Dataset = Path.GetFullPath(datasetPath),
            ParametersJson = JsonSerializer.Serialize(new
            {
                blockSize = FormatConstants.DefaultBlockSize,
                parseMode = "Lazy",
                archive = "one single-volume archive per group, unencrypted",
                classifier = "built-in model; trained on two of three files of each group",
            }),
        };
        foreach (StrategyMeasurement r in results)
        {
            experiment.StrategyResults.Add(new StrategyResult
            {
                Group = (byte)r.Group,
                Variant = Enum.Parse<DataVariant>(r.Variant.ToString()),
                FileCount = r.FileCount,
                CompressedFiles = r.CompressedFiles,
                OriginalBytes = r.OriginalBytes,
                ArchiveBytes = r.ArchiveBytes,
                PackSeconds = r.PackSeconds,
                UnpackSeconds = r.UnpackSeconds,
                AllRestored = r.AllRestored,
            });
        }

        IMeasurementStore store = OpenMeasurementStore();
        if (store.IsEnabled)
        {
            store.SaveExperiment(experiment);
            Write($"[OK] Результати збережено в базі даних (експеримент №{experiment.Id}).", ConsoleColor.Green);
        }
        else
        {
            Write("[УВАГА] База даних вимкнена: результати виведено лише в консоль.", ConsoleColor.Yellow);
        }
    }

    private static string VariantName(ArchiveVariant variant) => variant switch
    {
        ArchiveVariant.Stored => "без стиснення",
        ArchiveVariant.Fixed => "фіксована",
        ArchiveVariant.Classifier => "класифікатор",
        _ => variant.ToString(),
    };

    private static void PredictionExperimentInteractive()
    {
        string? dataset = Prompt("Каталог із групами 0..6", "Datasets");
        if (dataset is null)
            return;
        RunPredictionExperiment(dataset);
    }

    /// <summary>Навчає передбачення наступного байта на train-файлах датасету і зберігає модель.</summary>
    private static void TrainPredictor(string datasetPath, string modelPath)
    {
        (IReadOnlyList<LabeledFile> train, _) = LabeledDataset.SplitFiles(LabeledDataset.ListFiles(datasetPath));
        PredictorOptions options = PredictorOptions.Default;

        Console.WriteLine();
        Write($"Навчання передбачення: {train.Count} файлів, до {options.MaxPositionsPerSample} позицій з кожного", ConsoleColor.Cyan);
        Console.WriteLine($"  Контекст {options.ContextBytes} байти, мережа {256 * options.ContextBytes}-{options.HiddenNeurons}-256 (softsign), епох {options.Epochs}, швидкість {options.LearningRate.ToString(CultureInfo.InvariantCulture)}, seed {options.Seed}");

        var watch = System.Diagnostics.Stopwatch.StartNew();
        BytePredictor predictor = BytePredictor.Train([.. train.Select(f => (ReadOnlyMemory<byte>)File.ReadAllBytes(f.FullPath))], options);
        using (FileStream file = File.Create(modelPath))
            predictor.Save(file);

        Write($"[OK] Модель {predictor.ModelId:X16} збережено за {watch.Elapsed.TotalSeconds:F1} с: {Path.GetFullPath(modelPath)}", ConsoleColor.Green);
    }

    /// <summary>
    /// Для кожного valid-файлу (на них модель не навчалася) порівнює ентропію і розмір після LZ77 + Гаффмана
    /// для самого файлу та для залишків передбачення, перевіряє відновлення і пише результати в базу.
    /// </summary>
    private static void RunPredictionExperiment(string datasetPath)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        (_, IReadOnlyList<LabeledFile> valid) = LabeledDataset.SplitFiles(LabeledDataset.ListFiles(datasetPath));
        BytePredictor predictor = BytePredictor.BuiltIn;
        var experiment = new Experiment
        {
            Kind = ExperimentKind.Prediction,
            StartedAtUtc = DateTime.UtcNow,
            Dataset = Path.GetFullPath(datasetPath),
            ParametersJson = JsonSerializer.Serialize(new
            {
                model = predictor.ModelId.ToString("X16", c),
                contextBytes = predictor.ContextBytes,
                layers = string.Join("-", predictor.Network.LayerSizes),
                files = "valid files: every third file of each group, not used for training",
                encoder = "LZ77 greedy + Huffman, 64 KB blocks, stored blocks allowed",
            }),
        };

        Console.WriteLine();
        Write($"Вплив передбачення (модель {predictor.ModelId:X16}) на файлах, яких модель не бачила", ConsoleColor.Cyan);
        Console.WriteLine($"  {"Група",5} {"Файл",-24} {"Байтів",8} {"H файлу",8} {"H залишків",11} {"LZ77 файлу",11} {"LZ77 залишків",14} {"Час, с",7}");
        foreach (LabeledFile file in valid)
        {
            byte[] data = File.ReadAllBytes(file.FullPath);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            byte[] encoded = predictor.Encode(data);
            double seconds = watch.Elapsed.TotalSeconds;
            if (!predictor.Decode(encoded).AsSpan().SequenceEqual(data))
                throw new InvalidOperationException($"Передбачення не відновило {file.Source} побайтово.");

            byte[] residuals = encoded[BytePredictor.ModelIdSize..];
            var result = new PredictionResult
            {
                Group = (byte)file.Label,
                OriginalBytes = data.Length,
                OriginalEntropy = FeatureExtractor.Compute(data).Entropy,
                ResidualEntropy = FeatureExtractor.Compute(residuals).Entropy,
                EncodedBytes = EncodedSize(data),
                PredictedEncodedBytes = EncodedSize(encoded),
                ElapsedSeconds = seconds,
            };
            experiment.PredictionResults.Add(result);
            Console.WriteLine(string.Format(c, "  {0,5} {1,-24} {2,8} {3,8:F3} {4,11:F3} {5,11} {6,14} {7,7:F2}",
                file.Label, Fit(Path.GetFileName(file.Source), 24), result.OriginalBytes, result.OriginalEntropy,
                result.ResidualEntropy, result.EncodedBytes, result.PredictedEncodedBytes, result.ElapsedSeconds));
        }
        Write("[OK] Усі файли відновлено із залишків побайтово.", ConsoleColor.Green);

        IMeasurementStore store = OpenMeasurementStore();
        if (store.IsEnabled)
        {
            store.SaveExperiment(experiment);
            Write($"[OK] Результати збережено в базі даних (експеримент №{experiment.Id}).", ConsoleColor.Green);
        }
        else
        {
            Write("[УВАГА] База даних вимкнена: результати виведено лише в консоль.", ConsoleColor.Yellow);
        }
    }

    /// <summary>Розмір після LZ77 + Гаффмана блоками по 64 КБ; блок без виграшу зберігається як є.</summary>
    private static long EncodedSize(byte[] data) =>
        data.Length == 0 ? 0 : Lz77CompressionService.PrepareFixedBlocks(data).Sum(b => (long)b.CodedLength);

    private static void TrainClassifierInteractive()
    {
        string? dataset = Prompt("Каталог із групами 0..6", "Datasets");
        string? model = Prompt("Куди зберегти модель", "classifier.json");
        if (dataset is null || model is null)
            return;
        TrainClassifier(dataset, model);
    }

    /// <summary>
    /// Навчає класифікатор з кількох seed, обирає модель за macro-F1 на valid,
    /// зберігає її і пише прогони в базу (експеримент Training).
    /// </summary>
    private static void TrainClassifier(string datasetPath, string modelPath)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        DatasetSplit split = LabeledDataset.SplitByFile(
            LabeledDataset.Load(datasetPath, LabeledDataset.DefaultChunkSize, LabeledDataset.DefaultMaxChunksPerFile));
        TrainingOptions options = TrainingOptions.Default;

        Console.WriteLine();
        Write($"Навчання класифікатора: {split.Train.Count} прикладів у train, {split.Valid.Count} у valid", ConsoleColor.Cyan);
        Console.WriteLine($"  Мережа {string.Join("-", options.LayerSizes)}, епох {options.Epochs}, швидкість навчання {options.LearningRate.ToString(c)}, seed {string.Join(", ", options.Seeds)}");

        TrainingReport report = ClassifierTrainer.Train(split, options);

        Console.WriteLine();
        Console.WriteLine($"  {"Seed",4} {"Втрата 1",9} {"Втрата N",9} {"F1 train",9} {"F1 valid",9}");
        for (int i = 0; i < report.Runs.Count; i++)
        {
            TrainingRunResult run = report.Runs[i];
            Console.WriteLine(string.Format(c, "  {0,4} {1,9:F4} {2,9:F4} {3,9:F3} {4,9:F3}{5}",
                run.Seed, run.FirstEpochLoss, run.FinalLoss, run.Train.MacroF1, run.Valid.MacroF1,
                i == report.SelectedIndex ? "  <- обрано" : ""));
        }

        Console.WriteLine();
        Console.WriteLine("  Матриця помилок обраної моделі на valid (рядок = справжній тип, стовпець = передбачений):");
        Console.WriteLine("        " + string.Concat(Enumerable.Range(0, LabeledDataset.ClassCount).Select(k => $"{k,5}")));
        int[][] matrix = report.Selected.Valid.ToArray();
        for (int row = 0; row < matrix.Length; row++)
            Console.WriteLine($"  {row,4}  " + string.Concat(matrix[row].Select(v => $"{v,5}")));

        using (FileStream file = File.Create(modelPath))
            new FileClassifier(report.Selected.Network).Save(file);
        Write($"[OK] Модель збережено: {Path.GetFullPath(modelPath)}", ConsoleColor.Green);

        var experiment = new Experiment
        {
            Kind = ExperimentKind.Training,
            StartedAtUtc = DateTime.UtcNow,
            Dataset = Path.GetFullPath(datasetPath),
            ParametersJson = JsonSerializer.Serialize(new
            {
                chunkSize = LabeledDataset.DefaultChunkSize,
                maxChunksPerFile = LabeledDataset.DefaultMaxChunksPerFile,
                split = "every third file of each group goes to valid",
                features = FeatureVector.Length,
            }),
        };
        for (int i = 0; i < report.Runs.Count; i++)
        {
            TrainingRunResult run = report.Runs[i];
            experiment.TrainingRuns.Add(new TrainingRun
            {
                InitSeed = run.Seed,
                LayerSizes = string.Join(",", options.LayerSizes),
                Epochs = options.Epochs,
                LearningRate = options.LearningRate,
                TrainLoss = run.FinalLoss,
                TrainMacroF1 = run.Train.MacroF1,
                ValidMacroF1 = run.Valid.MacroF1,
                ConfusionMatrixJson = JsonSerializer.Serialize(run.Valid.ToArray()),
                IsSelected = i == report.SelectedIndex,
            });
        }

        IMeasurementStore store = OpenMeasurementStore();
        if (store.IsEnabled)
        {
            store.SaveExperiment(experiment);
            Write($"[OK] Прогони навчання збережено в базі даних (експеримент №{experiment.Id}).", ConsoleColor.Green);
        }
        else
        {
            Write("[УВАГА] База даних вимкнена: результати виведено лише в консоль.", ConsoleColor.Yellow);
        }
    }

    /// <summary>
    /// Пакує кожен файл у датасеті ітеративним DP + Гаффманом, розпаковує, порівнює й пише розміри та час у базу.
    /// </summary>
    private static void IterativeDpExperimentInteractive()
    {
        string? dataset = Prompt(
            "Каталог корпусу 0..6",
            "Datasets");

        if (dataset is null)
            return;

        const int blockSize = 8192;
        const int maxIterations = 3;
        const int maxChain = 32;

        DateTime startedAtUtc = DateTime.UtcNow;

        Write(
            "Запускаємо ітеративний DP + Huffman...",
            ConsoleColor.Cyan);

        var report = IterativeDpBenchmark.RunDirectory(
            dataset,
            blockSize: blockSize,
            maxIterations: maxIterations,
            maxChain: maxChain,
            progress: p => Console.Write(
                $"\rГрупа {p.Group}, {p.File}: " +
                $"блок {p.BlocksDone}/{p.BlocksTotal}     "));

        Console.WriteLine();

        long baseline = report.Files.Sum(f => f.BaselineBytes);
        long optimized = report.Files.Sum(f => f.OptimizedBytes);
        long gain = baseline - optimized;

        double percent = baseline == 0
            ? 0
            : 100.0 * gain / baseline;

        Write("=== Результати D1 №19 ===", ConsoleColor.Cyan);

        Console.WriteLine($"Файлів: {report.Files.Count}");
        Console.WriteLine($"Початковий DP: {baseline} байт");
        Console.WriteLine($"Ітеративний DP: {optimized} байт");
        Console.WriteLine($"Економія: {gain} байт ({percent:F2}%)");

        var iterations = report.Iterations
            .GroupBy(r => r.Iteration)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                iteration = g.Key,
                blocks = g.Count(),
                gainBytes = g.Sum(r => (long)r.GainBytes),
                elapsedSeconds = g.Sum(r => r.ElapsedSeconds)
            })
            .ToArray();

        Console.WriteLine();
        Write("=== Виграш за ітераціями ===", ConsoleColor.Cyan);

        foreach (var iteration in iterations)
        {
            Console.WriteLine(
                $"Ітерація {iteration.iteration}: " +
                $"виграш {iteration.gainBytes} байт, " +
                $"час {iteration.elapsedSeconds:F4} с");
        }

        // Зберігаємо результати через наявну систему БД.
        var experiment = new Experiment
        {
            Kind = ExperimentKind.Parsing,
            StartedAtUtc = startedAtUtc,
            Dataset = Path.GetFullPath(dataset),

            ParametersJson = JsonSerializer.Serialize(new
            {
                experimentType = "IterativeDp",

                blockSize,
                maxIterations,
                maxChain,

                filesCount = report.Files.Count,
                blocksCount = report.Files.Sum(f => f.Blocks),

                baselineBytes = baseline,
                optimizedBytes = optimized,
                gainBytes = gain,
                gainPercent = percent,

                iterations,

                files = report.Files.Select(f => new
                {
                    group = f.Group,
                    file = f.File,
                    rawBytes = f.RawBytes,
                    blocks = f.Blocks,
                    baselineBytes = f.BaselineBytes,
                    optimizedBytes = f.OptimizedBytes,
                    gainBytes = f.GainBytes
                }).ToArray()
            })
        };

        IMeasurementStore store = OpenMeasurementStore();

        if (store.IsEnabled)
        {
            store.SaveExperiment(experiment);

            Write(
                $"[OK] Експеримент №{experiment.Id} " +
                "збережено в базі даних.",
                ConsoleColor.Green);
        }
        else
        {
            Write(
                "[УВАГА] База даних вимкнена. " +
                "Результати показано в консолі.",
                ConsoleColor.Yellow);
        }
    }
    private static void Zip9BonusInteractive()
{
    string? dataset = Prompt("Каталог корпусу 0..6", "Datasets");
    if (dataset is null)
        return;

    RunZip9Bonus(dataset);
}

private static void RunZip9Bonus(string dataset)
{
    DateTime startedAtUtc = DateTime.UtcNow;

    Write("=== Бонус CP3 №4: SmartArchiver проти ZIP (.NET SmallestSize) ===",
        ConsoleColor.Cyan);
    Write("ZIP створюється засобами .NET; це НЕ точне вимірювання Info-ZIP zip -9.",
        ConsoleColor.DarkCyan);

    Zip9Benchmark.Report report = Zip9Benchmark.RunDirectory(
        dataset,
        BuildAndVerifyZip9Sarc,
        progress: message => Console.WriteLine(message));

    Console.WriteLine();
    Console.WriteLine($"Файлів: {report.FilesCount}");
    Console.WriteLine($"Вхідних байтів: {report.RawBytes}");
    Console.WriteLine($"ZIP (.NET SmallestSize): {report.ZipBytes} Б");

    Console.WriteLine();
    Write("=== Варіанти реального SARC ===", ConsoleColor.Cyan);
    foreach (var c in report.Configurations)
    {
        Console.WriteLine($"{c.Name}: {c.SarcBytes} Б, " +
            $"виграш відносно ZIP: {c.DeltaVsZipBytes:+#;-#;0} Б, " +
            $"час {c.Seconds:F3} с, stored-блоків: {c.StoredBlocks}");
    }

    Console.WriteLine();
    string verdict = report.GainBytes > 0
        ? "[OK] SmartArchiver обігнав .NET ZIP SmallestSize на цьому корпусі!"
        : report.GainBytes == 0
            ? "[=] Однаковий розмір із .NET ZIP."
            : "[INFO] .NET ZIP поки що менший за SARC.";
    Write(verdict, report.GainBytes > 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
    Console.WriteLine($"Найкращий режим: {report.Best.Name}");
    Console.WriteLine($"SARC: {report.Best.SarcBytes} Б; " +
        $"ZIP .NET: {report.ZipBytes} Б; " +
        $"виграш SARC: {report.GainBytes:+#;-#;0} Б " +
        $"({report.GainPercent:+0.00;-0.00;0.00}% від ZIP)");

    Console.WriteLine();
    Write("=== За групами (окремий архів для кожної групи) ===", ConsoleColor.Cyan);
    foreach (var group in report.Groups)
    {
        Console.WriteLine($"Група {group.Group}: вхід {group.RawBytes} Б; " +
            $"ZIP .NET = {group.ZipBytes} Б; SARC = {group.SarcBytes} Б; " +
            $"виграш SARC = {group.GainBytes:+#;-#;0} Б");
    }

    Console.WriteLine("Усі ZIP та SARC архіви перевірено побайтовим round-trip.");
    Console.WriteLine("Порівнювали повні архіви з усіма заголовками, без шифрування.");
    Console.WriteLine("Розміри окремих груп не слід підсумовувати: заголовки повторюються.");

    var experiment = new Experiment
    {
        Kind = ExperimentKind.Parsing,
        StartedAtUtc = startedAtUtc,
        Dataset = "corpus",
        ParametersJson = JsonSerializer.Serialize(new
        {
            experimentType = "ManagedZipVsSarc",
            datasetPath = Path.GetFullPath(dataset),
            zipImplementation = "System.IO.Compression.ZipArchive, CompressionLevel.SmallestSize",
            fileCount = report.FilesCount,
            rawBytes = report.RawBytes,
            zipBytes = report.ZipBytes,
            bestConfiguration = report.Best.Name,
            bestSarcBytes = report.Best.SarcBytes,
            gainBytes = report.GainBytes,
            gainPercent = report.GainPercent,
            successful = report.GainBytes > 0,
            configurations = report.Configurations,
            groups = report.Groups
        })
    };

    IMeasurementStore store = OpenMeasurementStore();
    if (store.IsEnabled)
    {
        store.SaveExperiment(experiment);
        Write($"[OK] Результати записано в БД: експеримент №{experiment.Id}.",
            ConsoleColor.Green);
    }
    else
    {
        Write("[УВАГА] БД вимкнена: результати показано в консолі.",
            ConsoleColor.Yellow);
    }
}


    private static Zip9Benchmark.SarcResult BuildAndVerifyZip9Sarc(
    IReadOnlyList<Zip9Benchmark.SourceFile> sources,
    Zip9Benchmark.Configuration config)
    {
        List<ArchiveInput> inputs = sources.Select(source =>
        {
            SafeNames.EnsureSafe(source.Name);
            return new ArchiveInput(source.Name, source.Bytes);
        }).ToList();

        BlockSplitMode splitMode = config.SplitMode == Zip9Benchmark.SplitMode.Dp
            ? BlockSplitMode.Dp
            : BlockSplitMode.Fixed;

        var writer = new ArchiveWriter(
            blockSize: config.BlockSize,
            analyzer: new FileAnalyzer(),
            parseMode: config.ParseMode,
            splitMode: splitMode);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<PreparedFile> prepared = writer.Prepare(inputs);
        byte[] archive = ArchiveWriter.BuildFromPrepared(prepared);
        watch.Stop();

        IReadOnlyList<ExtractedFile> restored = ArchiveReader.Read(archive);
        if (restored.Count != inputs.Count)
            throw new InvalidDataException("SARC round-trip: різна кількість файлів.");

        var byName = restored.ToDictionary(
            file => file.Name, file => file.Data, StringComparer.Ordinal);
        foreach (ArchiveInput input in inputs)
        {
            if (!byName.TryGetValue(input.Name, out byte[]? decoded)
                || !decoded.AsSpan().SequenceEqual(input.Data))
                throw new InvalidDataException($"SARC round-trip не збігся: {input.Name}");
        }

        int storedBlocks = prepared.Sum(file => file.Blocks.Count(block => block.IsStored));
        return new Zip9Benchmark.SarcResult(
            archive.LongLength, watch.Elapsed.TotalSeconds, storedBlocks);
    }

    private static void ArithmeticExperimentInteractive()
    {
        string? dataset = Prompt("Каталог корпусу 0..6", "Datasets");
        if (dataset is null)
            return;

        const int blockSize = 65536;
        const int maxIterations = 3;
        const int maxChain = 256;
        DateTime startedAtUtc = DateTime.UtcNow;

        Write("Порівняння Huffman і арифметичного кодування на однакових LZ77-токенах...",
            ConsoleColor.Cyan);

        var report = ArithmeticBenchmark.RunDirectory(
            dataset, blockSize, maxIterations, maxChain,
            progress: p => Console.Write(
                $"\rГрупа {p.Group}, {p.File}: блок {p.BlocksDone}/{p.BlocksTotal}    "));

        Console.WriteLine();
        Console.WriteLine();
        Write("=== CP3 №3: Huffman vs Arithmetic ===", ConsoleColor.Cyan);
        Console.WriteLine($"Файлів: {report.Files.Count}");
        Console.WriteLine($"Початкових байтів: {report.RawBytes}");
        Console.WriteLine($"Huffman повний: {report.HuffmanBytes} байт");
        Console.WriteLine($"Arithmetic повний: {report.ArithmeticBytes} байт");
        Console.WriteLine($"Різниця: {report.TotalSavedBytes:+#;-#;0} байт ({report.TotalSavedPercent:+0.00;-0.00;0.00}%)");
        Console.WriteLine();
        Console.WriteLine($"Huffman payload: {report.HuffmanPayloadBytes} байт");
        Console.WriteLine($"Arithmetic payload: {report.ArithmeticPayloadBytes} байт");
        Console.WriteLine($"Різниця payload: {report.PayloadSavedBytes:+#;-#;0} байт");
        Console.WriteLine("Додатна різниця = арифметичне кодування менше, від'ємна = Huffman менше.");
        Console.WriteLine("Усі блоки перевірено на точний round-trip; архівний формат не змінено.");

        foreach (var group in report.Files.GroupBy(f => f.Group).OrderBy(g => g.Key))
        {
            long h = group.Sum(f => f.HuffmanBytes);
            long a = group.Sum(f => f.ArithmeticBytes);
            long hp = group.Sum(f => f.HuffmanPayloadBytes);
            long ap = group.Sum(f => f.ArithmeticPayloadBytes);
            Console.WriteLine($"Група {group.Key}: Huffman={h} Б, Arithmetic={a} Б, " +
                $"різниця={h - a:+#;-#;0} Б; різниця payload={hp - ap:+#;-#;0} Б");
        }

        // Стара система збереження: один Experiment -> один JSON у ParametersJson.
        // Нові таблиці, EF міграції та CSV НЕ потрібні.
        var experiment = new Experiment
        {
            Kind = ExperimentKind.Parsing,
            StartedAtUtc = startedAtUtc,
            Dataset = "corpus",
            ParametersJson = JsonSerializer.Serialize(new
            {
                experimentType = "ArithmeticVsHuffman",
                datasetPath = Path.GetFullPath(dataset),
                blockSize,
                maxIterations,
                maxChain,
                filesCount = report.Files.Count,
                rawBytes = report.RawBytes,
                huffmanBytes = report.HuffmanBytes,
                arithmeticBytes = report.ArithmeticBytes,
                totalSavedBytes = report.TotalSavedBytes,
                totalSavedPercent = report.TotalSavedPercent,
                huffmanPayloadBytes = report.HuffmanPayloadBytes,
                arithmeticPayloadBytes = report.ArithmeticPayloadBytes,
                payloadSavedBytes = report.PayloadSavedBytes,
                huffmanSeconds = report.Files.Sum(f => f.HuffmanSeconds),
                arithmeticSeconds = report.Files.Sum(f => f.ArithmeticSeconds),
                groups = report.Files.GroupBy(f => f.Group).OrderBy(g => g.Key).Select(g => new
                {
                    group = g.Key,
                    huffmanBytes = g.Sum(f => f.HuffmanBytes),
                    arithmeticBytes = g.Sum(f => f.ArithmeticBytes),
                    payloadSavedBytes = g.Sum(f => f.PayloadSavedBytes)
                }).ToArray(),
                files = report.Files.Select(f => new
                {
                    group = f.Group,
                    file = f.File,
                    rawBytes = f.RawBytes,
                    blocks = f.Blocks,
                    huffmanBytes = f.HuffmanBytes,
                    arithmeticBytes = f.ArithmeticBytes,
                    huffmanPayloadBytes = f.HuffmanPayloadBytes,
                    arithmeticPayloadBytes = f.ArithmeticPayloadBytes,
                    totalSavedBytes = f.TotalSavedBytes,
                    payloadSavedBytes = f.PayloadSavedBytes,
                    huffmanSeconds = f.HuffmanSeconds,
                    arithmeticSeconds = f.ArithmeticSeconds
                }).ToArray()
            })
        };

        IMeasurementStore store = OpenMeasurementStore();
        if (store.IsEnabled)
        {
            store.SaveExperiment(experiment);
            Write($"[OK] Експеримент №{experiment.Id} збережено в БД.", ConsoleColor.Green);
        }
        else
            Write("[УВАГА] БД вимкнена. Результати показано в консолі.", ConsoleColor.Yellow);
    }

    private static string ByteLabel(byte b) => b switch
    {
        (byte)'\n' => "'\\n'",
        (byte)'\r' => "'\\r'",
        (byte)'\t' => "'\\t'",
        (byte)' ' => "' '",
        >= 0x21 and <= 0x7E => $"'{(char)b}'",
        _ => $"0x{b:X2}",
    };

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

    /// <summary>Порядок файлів у томах може відрізнятися від вхідного, тому звірка йде за іменем.</summary>
    private static bool SameContent(List<ArchiveInput> inputs, IReadOnlyList<ExtractedFile> restored)
    {
        if (inputs.Count != restored.Count)
        {
            return false;
        }
        Dictionary<string, byte[]> byName = restored.ToDictionary(f => f.Name, f => f.Data, StringComparer.Ordinal);
        return inputs.TrueForAll(i => byName.TryGetValue(i.Name, out byte[]? data) && data.AsSpan().SequenceEqual(i.Data));
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
        text.Length <= width ? text : string.Concat(text.AsSpan(0, width - 3), "...");

    private static string Explain(ArchiveException ex)
    {
        string meaning = ex.Code switch
        {
            ErrorCode.E_MAGIC => "це не архів SmartArchiver або непідтримувана версія формату",
            ErrorCode.E_LENGTH => "архів обрізаний або має зайві байти",
            ErrorCode.E_HMAC => "перевірка автентичності не пройшла",
            ErrorCode.E_PAYLOAD => "внутрішня структура архіву пошкоджена або містить небезпечні дані",
            ErrorCode.E_CODE => "невідома стратегія стиснення або пошкоджені стиснені дані",
            ErrorCode.E_CRC => "вміст файлу змінено: CRC32 не збігається",
            ErrorCode.E_VOLUMES => "набір томів неповний, містить дублікати або томи з різних архівів",
            ErrorCode.E_CAPACITY => "блок не вміщується в том: збільште розмір тому або зменште розмір блока",
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
