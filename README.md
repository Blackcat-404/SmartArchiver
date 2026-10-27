# SmartArchiver

Розумний захищений архіватор файлів (проєкт 2). Зараз готово те, що потрібно для чекпоінта 1: формат архіву SARC (запис і читання томів, таблиця файлів, блоки, CRC32, перевірка імен), канонічний Гаффман, шифрування (PBKDF2, AES-256-CTR, HMAC-SHA256) і база даних для результатів вимірювань. Стиснення (LZ77 + Гаффман) і аналіз файлу підключено до запису архіву, томи пакуються ДП-рюкзаком; шифрування працює окремо, у запис томів його ще не підключено.

## Структура

| Шлях | Що там | Зона |
| --- | --- | --- |
| `src/SmartArchiver.Core` | спільні типи: коди помилок, `ArchiveException`, типи файлів | усі |
| `src/SmartArchiver.Compression` | канонічний Гаффман (далі LZ77) | Стиснення |
| `src/SmartArchiver.Security` | PBKDF2, AES-256-CTR, HMAC-SHA256 | Захист |
| `src/SmartArchiver.Analysis` | аналіз файлу: ознаки, перцептрон, класифікатор типу, вибір стратегії стиснення, передбачення наступного байта | Аналіз файлу |
| `src/SmartArchiver.Archive` | формат SARC: томи, таблиця файлів, блоки, захищений архів | Інтеграція |
| `src/SmartArchiver.Data` | база даних результатів (EF Core + SQL Server) | База даних |
| `src/SmartArchiver.Cli` | консольна програма `smartarchiver` | Інтеграція |
| `src/SmartArchiver.App` | графічний інтерфейс на Avalonia, поки шаблон | Аналіз файлу + БД |
| `tests/` | автотести (xUnit), окремий проєкт на кожен проєкт з `src/` | |
| `docs/RUN.md` | меню й команди програми | |
| `docs/decisions.md` | рішення команди: проблема → варіанти → рішення → чому | |

Залежності йдуть в один бік: Compression і Analysis залежать лише від Core, Security ні від кого, Archive — від Core, Compression і Security (аналізатор приходить як `IFileAnalyzer` з Core), CLI — від Analysis, Archive і Data. Зони не посилаються одна на одну, тож поломка в одній не зупиняє збірку й тести інших.

Спільні налаштування всіх проєктів — у `Directory.Build.props`, версії пакетів — у `Directory.Packages.props` (у `.csproj` версій немає), версія SDK — у `global.json`.

## Що потрібно

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0): `dotnet --version` має показати `10.x`.
- SQL Server, лише для бази даних. Найпростіше підняти його в Docker (див. нижче). Без нього архіватор працює повністю, просто результати вимірювань нікуди не записуються.

## Збірка, тести, запуск

```bash
dotnet build
dotnet test
dotnet run --project src/SmartArchiver.Cli
```

Без SQL Server 3 тести бази даних позначаються як пропущені (`Skipped`), це нормально. Як запустити і їх, описано в розділі «Тести бази даних». Пункти меню й команди описано в [docs/RUN.md](docs/RUN.md).

## Стиснення, томи і рюкзак

`ArchiveWriter` з аналізатором стискає кожен файл за його стратегією: шум і вже стиснене зберігаються як є (стиснений розмір дорівнює початковому), решта кодується LZ77 + Гаффманом, а блок без виграшу все одно зберігається як є.

```bash
dotnet run --project src/SmartArchiver.Cli -- pack test.arc a.txt b.png
dotnet run --project src/SmartArchiver.Cli -- pack test.arc --volume-size 65536 a.txt b.png   # томи test.part1.arc, test.part2.arc...
dotnet run --project src/SmartArchiver.Cli -- unpack test.part1.arc out                        # сусідні томи підхоплюються самі
dotnet run --project src/SmartArchiver.Cli -- pack-experiment Datasets 65536                   # ДП-рюкзак проти жадібного
dotnet run --project src/SmartArchiver.Cli -- measure Datasets [team_config.json]              # Q на всьому корпусі, результат у БД
```

Чекпоінт 3: `pack` і `unpack` проходять повний тракт **аналіз → блоки → LZ77 → Гаффман → пакування → AES-256-CTR → HMAC-SHA256 → томи** (`ArchivePipeline`). Пароль береться зі змінної `SMARTARCHIVER_PASSWORD` або вводиться без показу. Пошкоджений архів дає код `E_MAGIC`, `E_LENGTH`, `E_HMAC`, `E_PAYLOAD`, `E_CODE` або `E_CRC` (`ArchivePipeline.TryUnpack`, без винятків). `unpack` атомарний: усе перевіряється в пам'яті, наявні файли не перезаписуються, часткового результату на диску немає. `measure` проганяє весь корпус через тракт і рахує `Q = α·S + β·T`; α, β, `t_ref` і місткість тому беруться з `team_config.json` (`alpha`, `beta`, `t_ref`, `max_volume_bytes`), а без нього це заглушки. Рішення 35–38 у `docs/decisions.md`.

З `--volume-size` блоки розкладає по томах ДП-рюкзак: вага блока `coded_len + 15`, місткість тому `max_volume_bytes − 41 − 32 − 4 − таблиця файлів`, цінність за типом файлу. Блок, що не влазить у том, дає помилку `E_CAPACITY`. Пріоритети типів поки стандартні (заглушка), справжні читаються з `team_config.json` (`TeamConfig.Load`). Деталі і відмінності від специфікації — `docs/decisions.md`, рішення 29–33.

## Аналіз файлу

Перед архівацією аналізатор (`FileAnalyzer`, контракт `IFileAnalyzer` у Core) визначає тип файлу `file_type` 0–6 і стратегію стиснення: шум і вже стиснені дані зберігаються як є, решта стискається LZ77 + Гаффманом.

```bash
dotnet run --project src/SmartArchiver.Cli -- analyze README.md
```

Тип визначає перцептрон, навчений на `Datasets/` (теки `0`…`6` за типами). Модель вбудована в збірку (`src/SmartArchiver.Analysis/Model/classifier.json`). Перенавчити її, наприклад на `train/` від викладача, розкладеному так само по теках:

```bash
dotnet run --project src/SmartArchiver.Cli -- train-classifier Datasets src/SmartArchiver.Analysis/Model/classifier.json
```

Команда навчає мережу з п'яти seed, обирає модель за macro-F1 на valid (кожен третій файл групи), друкує матрицю помилок і, якщо база ввімкнена, записує прогони в `TrainingRuns`. Чому саме так і які результати — `docs/decisions.md`, рішення 26 і 27.

### Передбачення наступного байта

`BytePredictor` за кількома попередніми байтами вгадує наступний і записує залишок `(справжній − передбачений) mod 256`; розпакування повторює ті самі прогнози й відновлює файл побайтово. Модель навчена на train-файлах `Datasets/` і вбудована в збірку (`src/SmartArchiver.Analysis/Model/predictor.json`); закодовані дані починаються з ідентифікатора моделі, тож дані від іншої моделі відхиляються.

```bash
dotnet run --project src/SmartArchiver.Cli -- train-predictor Datasets src/SmartArchiver.Analysis/Model/predictor.json
dotnet run --project src/SmartArchiver.Cli -- prediction-experiment Datasets
```

### Фіксована стратегія проти класифікатора

```bash
dotnet run --project src/SmartArchiver.Cli -- strategy-experiment Datasets
```

Кожна група `Datasets/` пакується в архів трьома способами: без стиснення, завжди LZ77 + Гаффман і як вирішить класифікатор. Програма розпаковує архіви, перевіряє файли побайтово, друкує розміри й час (медіана з 5 прогонів після розігріву) і записує результати в `StrategyResults`. Результати й пояснення — `docs/decisions.md`, рішення 34.

Експеримент для кожного valid-файлу (модель їх не бачила) порівнює ентропію і розмір після LZ77 + Гаффмана для самого файлу і для залишків, перевіряє відновлення і пише результати в `PredictionResults`. Результати й пояснення — `docs/decisions.md`, рішення 28.

## База даних

Архів базу даних не використовує. Вона живе поруч і зберігає результати вимірювань Q. Паролі, ключі та імена файлів у ній не зберігаються.

### 1. Підняти SQL Server

Через Docker (Windows, Linux, macOS):

```bash
docker run -d --name smart-archiver -p 1433:1433 \
  -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<пароль>" \
  mcr.microsoft.com/mssql/server:2025-latest
```

Пароль має містити щонайменше 8 символів і символи хоча б трьох видів із чотирьох: великі літери, малі літери, цифри, інші символи. Інакше контейнер одразу зупиниться (причину покаже `docker logs smart-archiver`). Після перезавантаження комп'ютера контейнер запускається знову командою `docker start smart-archiver`.

Підійде і звичайно встановлений SQL Server (Express або Developer).

### 2. Вказати рядок підключення

Створіть файл `src/SmartArchiver.Cli/appsettings.Local.json`:

```json
{
  "ConnectionStrings": {
    "SmartArchiver": "Server=localhost,1433;Database=SmartArchiver;User Id=sa;Password=<пароль>;TrustServerCertificate=true"
  }
}
```

Для SQL Server, встановленого на Windows, зі входом під обліковим записом Windows: `Server=localhost\SQLEXPRESS;Database=SmartArchiver;Integrated Security=true;TrustServerCertificate=true`.

Цей файл в `.gitignore`, тож пароль не потрапить у репозиторій. Поруч лежить `appsettings.json` з порожнім рядком. Його не змінюйте: він спільний, а порожній рядок означає, що база вимкнена.

### 3. Створити базу

```bash
dotnet run --project src/SmartArchiver.Cli -- db
```

Перший запуск сам створює базу й таблиці (застосовує міграції EF Core), окремі SQL-скрипти не потрібні. Те саме робить пункт 5 меню. Очікуваний вивід:

```text
[OK] База даних готова: database 'SmartArchiver' on 'localhost,1433'
Збережено прогонів вимірювання Q: 0
```

Якщо сервер недоступний, пароль хибний або `appsettings.Local.json` не є коректним JSON, програма пояснює причину і завершується з кодом 4.

### Схема

| Таблиця | Рядок | Поля |
| --- | --- | --- |
| `MeasurementRuns` | один прогін вимірювання Q | дата (UTC), коміт, seed, набір даних, α, β, t_ref, інші параметри (JSON), B_orig, B_arc, час t, чи всі файли відновлено, Q |
| `MeasurementFileResults` | один файл прогону | прогін (зовнішній ключ, каскадне видалення), група корпусу 0–6, розмір до і в архіві, чи відновлено |
| `Experiments` | один запуск експерименту | вид (`Parsing`, `Packing`, `Training`, `Prediction`, `Strategy`, `Genetic`), дата (UTC), коміт, seed, набір даних, параметри (JSON) |
| `ParsingResults` | розбір потоку: одна група × один метод | група 0–6, метод (`Greedy`, `Lazy`, `Dp`), байтів до і після, час |
| `PackingResults` | пакування томів: один том × один метод | метод (`Dp`, `Greedy`), номер тому, місткість W, використано байтів, сумарна цінність, кількість блоків, час |
| `StrategyResults` | стратегія стиснення: одна група × один варіант | група 0–6, варіант (`Stored`, `Fixed`, `Classifier`), файлів і скільки стиснуто, розмір до і в архіві, час запакування й розпакування, чи все відновлено |
| `PredictionResults` | передбачення байта: один файл | група 0–6, розмір, ентропія файлу й залишків, розмір після LZ77 + Гаффмана для файлу й залишків, час |
| `TrainingRuns` | навчання класифікатора: одна ініціалізація | seed, розміри шарів, епохи, швидкість навчання, втрата, macro-F1 на train і valid, матриця помилок (JSON), чи обрана модель |
| `ArchiveOperations` | історія архівацій: один `pack` або `unpack` | дата (UTC), операція (`Pack`, `Unpack`), файлів і скільки стиснуто, розмір файлів і архіву, томів, час, успіх або код помилки (`E_HMAC`, `E_CAPACITY`, `E_IO`…) |
| `GeneticIndividuals` | журнал еволюції ГА: одна хромосома | алгоритм (`Genetic`, `Random`, `LocalSearch`), покоління, номер особини, гени (JSON-об'єкт), пристосованість Q, окремо S і T |

Результати експериментів залежать від `Experiments` через зовнішній ключ із каскадним видаленням. Неможливі значення відхиляє сама база (обмеження CHECK): група понад 6, від'ємні розміри й час, використано більше за місткість тому, macro-F1 поза межами 0…1, нуль епох, гени не JSON-об'єкт, та сама особина двічі тощо.

Експеримент записується разом з усіма результатами однією транзакцією: якщо хоч один результат порушує обмеження, не зберігається нічого.

```csharp
store.SaveExperiment(new Experiment
{
    Kind = ExperimentKind.Parsing,
    StartedAtUtc = DateTime.UtcNow,
    Dataset = "corpus",
    ParsingResults = [new ParsingResult { Group = 0, Method = ParsingMethod.Greedy, OriginalBytes = 10_000, EncodedBytes = 4_200, ElapsedSeconds = 0.12 }],
});
IReadOnlyList<Experiment> runs = store.ListExperiments(ExperimentKind.Parsing);
Experiment? full = store.GetExperiment(runs[0].Id);   // разом із результатами
```

### Історія архівацій

Кожен `pack` і `unpack` (і в меню) додає рядок в `ArchiveOperations`, якщо базу ввімкнено. Імен файлів, шляхів, пароля й ключів у таблиці немає: для них просто немає колонок. Без бази або з недоступною базою архівація працює як завжди, лише з попередженням.

```bash
dotnet run --project src/SmartArchiver.Cli -- history 20
```

### Журнал еволюції ГА

Кожна перевірена хромосома ГА і випадкового пошуку записується в `GeneticIndividuals`, а копія журналу для репозиторію вивантажується в CSV:

```bash
dotnet run --project src/SmartArchiver.Cli -- export-ga <id експерименту> docs/ga/ga-vs-random.csv
```

Колонки: `algorithm, generation, individual, fitness, s, t`, далі по одній на кожен ген. Рішення 45 у `docs/decisions.md`.

### Запис результатів з коду

```csharp
using SmartArchiver.Data;
using SmartArchiver.Data.Entities;

IMeasurementStore store = MeasurementStore.Open(DatabaseSettings.ReadConnectionString(AppContext.BaseDirectory));
store.SaveRun(new MeasurementRun
{
    StartedAtUtc = DateTime.UtcNow,
    Dataset = "corpus",
    // α, β, t_ref, B_orig, B_arc, t, Q ...
    Files = [new MeasurementFileResult { Group = 0, OriginalBytes = 1000, ArchiveBytes = 640, Restored = true }],
});
```

Прогін і його файли записуються однією транзакцією. Якщо база вимкнена, `SaveRun` нічого не робить, а `store.IsEnabled` дорівнює `false`.

### Тести бази даних

Тести, яким потрібен сервер, виконуються лише тоді, коли задано змінну `SMARTARCHIVER_TEST_SQLSERVER`. Назву бази в рядку не вказуйте: кожен тест створює власну тимчасову базу `SmartArchiverTests_<guid>` і видаляє її після себе, а робочої бази не торкається.

```bash
# bash
SMARTARCHIVER_TEST_SQLSERVER='Server=localhost,1433;User Id=sa;Password=<пароль>;TrustServerCertificate=true' dotnet test
```

```powershell
# PowerShell
$env:SMARTARCHIVER_TEST_SQLSERVER = 'Server=localhost,1433;User Id=sa;Password=<пароль>;TrustServerCertificate=true'
dotnet test
```

У CI ці тести виконуються в Linux-задачі, де SQL Server піднімається в контейнері. Windows-задача їх пропускає.

### Зміна схеми

Змініть класи в `src/SmartArchiver.Data/Entities` або `SmartArchiverDbContext`, потім створіть міграцію:

```bash
dotnet tool restore
dotnet ef migrations add <НазваЗміни> --project src/SmartArchiver.Data
```

`dotnet tool restore` встановлює `dotnet-ef` тієї версії, що вказана в `dotnet-tools.json`. Згенеровані файли в `src/SmartArchiver.Data/Migrations` комітяться разом зі зміною. У кожного учасника програма застосує нову міграцію сама при наступному зверненні до бази.
