# SmartArchiver

Розумний захищений архіватор файлів (проєкт 2). Зараз готово те, що потрібно для чекпоінта 1: формат архіву SARC (запис і читання томів, таблиця файлів, блоки, CRC32, перевірка імен), канонічний Гаффман, шифрування (PBKDF2, AES-256-CTR, HMAC-SHA256) і база даних для результатів вимірювань. Гаффман і шифрування поки працюють окремо: у запис архіву їх ще не підключено.

## Структура

| Шлях | Що там | Зона |
| --- | --- | --- |
| `src/SmartArchiver.Core` | спільні типи: коди помилок, `ArchiveException`, типи файлів | усі |
| `src/SmartArchiver.Compression` | канонічний Гаффман (далі LZ77) | Стиснення |
| `src/SmartArchiver.Security` | PBKDF2, AES-256-CTR, HMAC-SHA256 | Захист |
| `src/SmartArchiver.Analysis` | аналіз файлу: ознаки, перцептрон, класифікатор типу, вибір стратегії стиснення | Аналіз файлу |
| `src/SmartArchiver.Archive` | формат SARC: томи, таблиця файлів, блоки, захищений архів | Інтеграція |
| `src/SmartArchiver.Data` | база даних результатів (EF Core + SQL Server) | База даних |
| `src/SmartArchiver.Cli` | консольна програма `smartarchiver` | Інтеграція |
| `src/SmartArchiver.App` | графічний інтерфейс на Avalonia, поки шаблон | Аналіз файлу + БД |
| `tests/` | автотести (xUnit), окремий проєкт на кожен проєкт з `src/` | |
| `docs/RUN.md` | меню й команди програми | |
| `docs/decisions.md` | рішення команди: проблема → варіанти → рішення → чому | |

Залежності йдуть в один бік: Compression і Analysis залежать лише від Core, Security ні від кого, Archive — від Core і Security, CLI — від Analysis, Archive і Data. Зони не посилаються одна на одну, тож поломка в одній не зупиняє збірку й тести інших.

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
| `Experiments` | один запуск експерименту CP2 | вид (`Parsing`, `Packing`, `Training`), дата (UTC), коміт, seed, набір даних, параметри (JSON) |
| `ParsingResults` | розбір потоку: одна група × один метод | група 0–6, метод (`Greedy`, `Lazy`, `Dp`), байтів до і після, час |
| `PackingResults` | пакування томів: один том × один метод | метод (`Dp`, `Greedy`), номер тому, місткість W, використано байтів, сумарна цінність, кількість блоків, час |
| `TrainingRuns` | навчання класифікатора: одна ініціалізація | seed, розміри шарів, епохи, швидкість навчання, втрата, macro-F1 на train і valid, матриця помилок (JSON), чи обрана модель |

Результати експериментів залежать від `Experiments` через зовнішній ключ із каскадним видаленням. Неможливі значення відхиляє сама база (обмеження CHECK): група понад 6, від'ємні розміри й час, використано більше за місткість тому, macro-F1 поза межами 0…1, нуль епох тощо. Журнал ГА та історія архівацій з'являться на CP3 окремими міграціями.

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
