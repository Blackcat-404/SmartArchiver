# META-USAGE: Blackcat-404

Зони: Аналіз файлу, База даних.

## Використані інструменти

- Claude Opus 5.5

## 1. Що таке CI/CD на GitHub і як воно працює

**Задача.** Розібратися, як працює CI, перш ніж додавати в наш `.github/workflows/ci.yml` тести бази даних.

**Промпт:**

> А можеш розказати, що таке CI/CD на GitHub? І як воно все працює?

**Відповідь:**

**CI** (Continuous Integration, безперервна інтеграція) — це практика, коли кожна зміна коду одразу автоматично збирається й тестується на чистій машині. Мета в тому, щоб дізнатися про поламку за кілька хвилин після пушу, а не через тиждень, коли зміни кількох людей уже змішалися. Головне правило: зламаний CI лагодять першочергово, а в основну гілку потрапляє лише те, що його пройшло.

**CD** має два значення:

- **Continuous Delivery** (безперервна доставка): після CI програма автоматично пакується в готовий до випуску вигляд (архів, інсталятор, Docker-образ), а випускає її людина однією дією.
- **Continuous Deployment** (безперервне розгортання): випуск теж автоматичний, і кожна зміна, що пройшла перевірки, одразу йде до користувачів.

Для навчального проєкту зазвичай вистачає CI: збірки й тестів на кожен PR.

### GitHub Actions

На GitHub CI/CD робиться через GitHub Actions. Усе описується YAML-файлами в теці `.github/workflows/` самого репозиторію, тож налаштування CI зберігається разом з кодом і проходить рев'ю, як будь-яка інша зміна.

| Поняття       | Що це                                                                                                                                                             |
| ------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| workflow      | один YAML-файл: коли запускатися і що робити                                                                                                                      |
| подія (`on:`) | що запускає workflow: `push`, `pull_request`, ручний запуск `workflow_dispatch`, розклад `schedule`                                                               |
| job           | набір кроків, який виконується на одній машині. Різні job за замовчуванням ідуть паралельно, порядок задається через `needs:`                                     |
| runner        | машина, на якій виконується job. GitHub на кожен job дає нову чисту віртуальну машину (`ubuntu-latest`, `windows-latest`, `macos-latest`) і після нього її знищує |
| step          | один крок: або команда оболонки (`run:`), або готова дія (`uses:`)                                                                                                |
| action        | готовий крок для повторного використання, наприклад `actions/checkout` (завантажити код) чи `actions/setup-dotnet` (встановити .NET)                              |
| matrix        | запуск того самого job з різними параметрами, наприклад на Linux і на Windows                                                                                     |
| artifact      | файли, які job зберігає після завершення: звіти тестів, зібрані програми                                                                                          |
| secret        | пароль чи токен у налаштуваннях репозиторію. У workflow він доступний як `${{ secrets.NAME }}` і маскується в логах                                               |

### Що відбувається після пушу

1. GitHub бачить подію (наприклад, відкрито PR) і знаходить усі workflow, у яких ця подія вказана в `on:`.
2. Для кожного job, а при matrix для кожної комбінації параметрів, ставить у чергу окремий runner.
3. На runner кроки виконуються по черзі: завантажити код, встановити SDK, відновити залежності, зібрати, запустити тести. Кроки одного job працюють зі спільною файловою системою, а змінні середовища між кроками передаються через файл `$GITHUB_ENV`.
4. Якщо будь-яка команда завершилась з ненульовим кодом, крок і весь job вважаються проваленими, а наступні кроки пропускаються. Виняток — кроки з умовою `if: always()` або `if: failure()`.
5. Результат з'являється біля коміту і в PR як ✓ або ✗ з посиланням на повний лог кожного кроку.

### Як зробити, щоб перевірку не можна було обійти

Червоний ✗ сам по собі нічого не блокує. Щоб у `main` не можна було злити PR з проваленими перевірками, у Settings → Branches (або Settings → Rules) для `main` вмикають правило «Require status checks to pass» і вибирають потрібні job. Тоді злити PR можна лише після зеленого CI.

### Мінімальний приклад для .NET

```yaml
name: CI
on:
    pull_request:
    push:
        branches: [main]

jobs:
    build-test:
        runs-on: ubuntu-latest
        steps:
            - uses: actions/checkout@v4
            - uses: actions/setup-dotnet@v4
              with:
                  dotnet-version: 10.0.x
            - run: dotnet build --configuration Release
            - run: dotnet test --configuration Release --no-build
```

Цього достатньо, щоб кожен PR збирався й тестувався. Далі зазвичай додають:

- matrix з `windows-latest`, бо шляхи, роздільники рядків і регістр імен файлів на різних ОС поводяться по-різному;
- кеш пакетів NuGet (`actions/cache`), щоб не завантажувати їх щоразу;
- збереження звітів тестів і покриття як artifact;
- `concurrency`, щоб новий пуш скасовував застарілий прогін тієї ж гілки.

### Що варто пам'ятати

- Runner щоразу чистий: від попереднього прогону на ньому нічого немає, крім того, що явно закешовано. Тому CI і ловить ситуацію «а в мене працює»: забутий файл чи незафіксована залежність одразу проявляються.
- Для публічних репозиторіїв стандартні runner-и безкоштовні, для приватних діє місячний ліміт хвилин.
- Паролі не пишуть у YAML. Для справжніх секретів є secrets, а тимчасові значення, наприклад пароль до тестової бази, можна згенерувати прямо під час прогону.
- Linux-контейнери (`services:` або `docker run`) доступні лише на Linux-runner-ах.

**Що зроблено мною.** Пояснення використав, щоб розібратися в наявному `ci.yml` перед тим, як змінювати його (запис 3). Код з цієї відповіді в репозиторій не потрапив.

## 2. Структура проєкту з EF Core

**Задача.** Зробити базу даних для результатів вимірювань на EF Core і MS SQL Server і зрозуміти, як правильно розкласти таку частину проєкту.

**Промпт:**

> Я роблю базу даних для проєкту на C# (.NET 10) з EF Core і MS SQL Server. Якою має бути структура проєкту з EF Core: де що лежить, які файли потрібні і навіщо? Наведи, будь ласка, приклади кількох основних файлів, від яких можна відштовхуватися.

**Відповідь:**

Головна ідея: усе, що стосується бази, живе в окремому проєкті-бібліотеці, а решта програми бачить лише невеликий інтерфейс доступу до даних.

### Структура

```text
src/
  SmartArchiver.Core/              формат архіву, про БД нічого не знає
  SmartArchiver.Cli/               консоль: читає рядок підключення і викликає шар даних
  SmartArchiver.Data/              усе, що стосується БД
    Entities/                      класи-сутності: один клас відповідає одній таблиці
    Migrations/                    міграції, згенеровані dotnet ef (комітяться в git)
    SmartArchiverDbContext.cs      модель: таблиці, ключі, зв'язки, обмеження
    DesignTimeDbContextFactory.cs  як створити контекст для dotnet ef
    IMeasurementStore.cs           інтерфейс, через який решта програми пише й читає дані
    ...                            його реалізації та читання налаштувань
tests/
  SmartArchiver.Data.Tests/        тести шару даних на справжньому SQL Server
dotnet-tools.json                  закріплена версія dotnet-ef
```

Чому окремий проєкт:

- Залежність іде в один бік: Cli посилається на Data, а Core на Data не посилається. Формат архіву і стиснення не залежать від того, чи є база, а пакети EF Core не потрапляють туди, де вони не потрібні.
- Шар даних тестується окремо, а пізніше той самий проєкт підключить і графічний інтерфейс.

### Пакети

- `Microsoft.EntityFrameworkCore.SqlServer`: сам EF Core і провайдер SQL Server.
- `Microsoft.EntityFrameworkCore.Design` з `PrivateAssets="all"`: потрібен лише інструменту `dotnet ef` під час розробки і не потрапляє в залежності проєктів, які посилаються на Data.
- `dotnet-ef` ставиться як локальний інструмент (`dotnet new tool-manifest`, потім `dotnet tool install dotnet-ef`). Його версія записується в `dotnet-tools.json`, і кожен учасник отримує ту саму командою `dotnet tool restore`.

### Основні частини

**Сутність** — це звичайний клас із властивостями. Багато чого EF Core виводить сам за домовленостями:

- `Id` стає первинним ключем з автоінкрементом;
- `string?` стає колонкою, що допускає NULL, а `string` і `int` стають NOT NULL (коли в проєкті ввімкнено nullable);
- пара `RunId` і `Run` стає зовнішнім ключем;
- `List<...>` стає зв'язком «один до багатьох».

Те, що домовленостями не виразити, задають у `DbContext`.

**DbContext** описує модель: по одному `DbSet<T>` на таблицю. В `OnModelCreating` через Fluent API уточнюють решту: довжини рядків, зв'язки, що робити з дочірніми рядками при видаленні. Конфігурацію краще тримати тут, а не атрибутами на сутностях, тоді всі правила схеми лежать в одному місці.

**Міграції.** Це підхід code-first:

1. Спершу змінюєш класи.
2. Команда `dotnet ef migrations add <Назва>` порівнює модель з останнім знімком (`...ModelSnapshot.cs`) і генерує клас міграції з методами `Up` (застосувати) і `Down` (відкотити). Міграції комітяться разом з кодом.
3. Під час роботи програма викликає `Database.Migrate()`. Він створює базу, якщо її немає, і застосовує ті міграції, яких ще немає в таблиці `__EFMigrationsHistory`.

`Database.EnsureCreated()` тут не підходить: він створює схему один раз і не вміє її змінювати, тож з міграціями несумісний.

**Фабрика для design time.** `dotnet ef` має створити `DbContext`, не запускаючи саму програму. Найпростіше дати йому клас, що реалізує `IDesignTimeDbContextFactory`. Рядок підключення в ньому потрібен лише для вибору провайдера: `migrations add` до сервера не підключається.

**Рядок підключення** не пишуть у код і не комітять з паролем. Типовий варіант: `appsettings.json` у git з порожнім значенням плюс особистий `appsettings.Local.json`, доданий у `.gitignore`.

**Час життя контексту.** `DbContext` легкий і не потокобезпечний. Його створюють на одну операцію (`using var db = ...`) і одразу звільняють, а не тримають один на всю програму. Один виклик `SaveChanges()` виконується в одній транзакції: записується або все, або нічого.

**Шар доступу.** Решті програми краще не працювати з `DbContext` напряму. Дай їй невеликий інтерфейс з операціями предметної області (наприклад, `SaveRun` і `CountRuns`), а EF Core сховай у реалізації. Тоді легко зробити реалізацію-заглушку для режиму без бази, а помилки EF Core і SQL Server перетворювати на одне зрозуміле виключення.

### Приклади, від яких можна відштовхуватися

`src/SmartArchiver.Data/SmartArchiver.Data.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SmartArchiver.Data</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.12" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.12" />
  </ItemGroup>

</Project>
```

`Microsoft.Extensions.Configuration.Json` тут потрібен для читання `appsettings.json`.

`src/SmartArchiver.Data/Entities/MeasurementRun.cs` — сутність для одного прогону вимірювання. Дочірня таблиця з результатами по файлах описується так само:

```csharp
namespace SmartArchiver.Data.Entities;

/// <summary>
/// One measurement of Q over a dataset: Q = α·S + β·T, where S = B_orig / (B_orig + B_arc)
/// and T = t_ref / (t_ref + t). Q is 0 when at least one file was not restored byte for byte.
/// </summary>
public sealed class MeasurementRun
{
    public int Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary>Git commit of the code that was measured, when known.</summary>
    public string? CommitHash { get; set; }

    public int? Seed { get; set; }

    /// <summary>Dataset name, for example "corpus".</summary>
    public string Dataset { get; set; } = "";

    public double Alpha { get; set; }

    public double Beta { get; set; }

    public double TRefSeconds { get; set; }

    /// <summary>Remaining run parameters (block size, strategy, ...) as JSON.</summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>B_orig: total size of the original files.</summary>
    public long OriginalBytes { get; set; }

    /// <summary>B_arc: total size of the archive volumes.</summary>
    public long ArchiveBytes { get; set; }

    /// <summary>t: archiving and extraction time.</summary>
    public double ElapsedSeconds { get; set; }

    public bool AllRestored { get; set; }

    public double Q { get; set; }

    public List<MeasurementFileResult> Files { get; set; } = [];
}
```

`src/SmartArchiver.Data/SmartArchiverDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data;

/// <summary>
/// Database of measurement results. It lives next to the archiver and never touches the archive format.
/// </summary>
public sealed class SmartArchiverDbContext(DbContextOptions<SmartArchiverDbContext> options) : DbContext(options)
{
    public DbSet<MeasurementRun> MeasurementRuns => Set<MeasurementRun>();

    public DbSet<MeasurementFileResult> MeasurementFileResults => Set<MeasurementFileResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MeasurementRun>(run =>
        {
            run.Property(r => r.CommitHash).HasMaxLength(40).IsUnicode(false);
            run.Property(r => r.Dataset).HasMaxLength(100);
            run.HasMany(r => r.Files)
                .WithOne(f => f.Run)
                .HasForeignKey(f => f.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

`src/SmartArchiver.Data/DesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartArchiver.Data;

/// <summary>
/// Used only by "dotnet ef migrations add": builds the model without connecting to a server.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmartArchiverDbContext>
{
    public SmartArchiverDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SmartArchiverDbContext>()
            .UseSqlServer("Server=localhost;Database=SmartArchiver;Integrated Security=true")
            .Options);
}
```

Перша міграція:

```bash
dotnet new tool-manifest
dotnet tool install dotnet-ef
dotnet ef migrations add InitialCreate --project src/SmartArchiver.Data
```

У `Migrations/` з'являться три файли: сама міграція, її `.Designer.cs` і знімок моделі. Варто відкрити згенерований метод `Up` і перевірити, що типи колонок, NULL і NOT NULL та зовнішні ключі саме такі, як задумано.

**Що потрапило в репозиторій.** За цією структурою асистент згенерував проєкт `SmartArchiver.Data`, тести до нього і підключення до CLI. Приклади вище — це ті самі файли, що лежать у репозиторії. Я переглядав зміни й комітив по кроках:

- `2e91da6`: проєкт Data, сутності, `SmartArchiverDbContext`, фабрика для design time, міграція `InitialCreate`, `dotnet-tools.json`;
- `37bdca6`: сховище результатів (`IMeasurementStore`, реалізації для SQL Server і для режиму без бази, `DatabaseException`) і тести до нього;
- `58ee43f`: читання налаштувань (`DatabaseSettings`), `appsettings.json`, команда `db` у CLI.

**Як перевірено** (разом з асистентом). 14 автотестів `SmartArchiver.Data.Tests` прогнано на SQL Server 2025 у Docker. Вони перевіряють:

- створення схеми з нуля;
- повторне застосування міграцій без втрати даних;
- запис і читання прогону разом з файлами;
- вимкнену базу при порожньому рядку підключення;
- некоректний рядок і рядок без назви бази;
- що пароль не потрапляє в повідомлення про помилку;
- читання налаштувань і пріоритет `appsettings.Local.json`.

Тести прибирають за собою: після прогону на сервері не лишається тестових баз. Окремо вручну перевірено, що команда `db` з першого запуску створює базу й таблиці, при хибному паролі завершується з кодом 4, а `pack` працює навіть зі зламаними налаштуваннями бази.

## 3. ci.yml під тести бази даних

**Задача.** У проєкті з'явилися тести бази даних (SQL Server). Без змінної `SMARTARCHIVER_TEST_SQLSERVER` вони пропускаються, тож у CI їх треба запускати з сервером.

**Промпт:**

> Ось наш ci.yml. У проєкті з'явилися тести бази даних на SQL Server, які без змінної SMARTARCHIVER_TEST_SQLSERVER пропускаються. Підправ workflow так, щоб у CI вони теж запускалися.

<details>
<summary>ci.yml, доданий до промпту</summary>

```yaml
name: CI

# Збірка й тести на кожен PR і на кожен пуш у main.
on:
    pull_request:
    push:
        branches: [main]
    workflow_dispatch:

# Новий пуш у ту саму гілку скасовує попередній ще не завершений прогін.
concurrency:
    group: ci-${{ github.workflow }}-${{ github.ref }}
    cancel-in-progress: true

permissions:
    contents: read

env:
    SOLUTION: SmartArchiver.slnx
    DOTNET_NOLOGO: true
    DOTNET_CLI_TELEMETRY_OPTOUT: true

defaults:
    run:
        shell: bash # однаковий синтаксис на Linux і Windows

jobs:
    build-test:
        name: build & test (${{ matrix.os }})
        runs-on: ${{ matrix.os }}
        timeout-minutes: 15
        strategy:
            fail-fast: false
            matrix:
                os: [ubuntu-latest, windows-latest]

        steps:
            - name: Checkout
              uses: actions/checkout@v4

            - name: Setup .NET
              uses: actions/setup-dotnet@v4
              with:
                  dotnet-version: 10.0.x # має збігатися з TargetFramework у .csproj

            - name: Cache NuGet packages
              uses: actions/cache@v4
              with:
                  path: ~/.nuget/packages
                  key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
                  restore-keys: ${{ runner.os }}-nuget-

            - name: Restore
              run: dotnet restore ${{ env.SOLUTION }}

            - name: Build
              run: dotnet build ${{ env.SOLUTION }} --configuration Release --no-restore

            - name: Test
              run: >
                  dotnet test ${{ env.SOLUTION }}
                  --configuration Release --no-build
                  --logger "trx;LogFileName=test-results.trx"
                  --collect:"XPlat Code Coverage"
                  --results-directory ./TestResults

            - name: Upload test results and coverage
              if: always()
              uses: actions/upload-artifact@v4
              with:
                  name: test-results-${{ matrix.os }}
                  path: TestResults/**
                  if-no-files-found: warn
                  retention-days: 14
```

</details>

**Відповідь:**

Так. На Linux-runner-і перед тестами запускаємо SQL Server у Docker-контейнері й передаємо тестам рядок підключення через змінну `SMARTARCHIVER_TEST_SQLSERVER`. На Windows тести БД і далі пропускатимуться.

**Чому не `services:`.** Для баз даних у GitHub Actions зазвичай пишуть блок `services:`, але він задається для всього job. Наш job запускається матрицею і на Windows, де Linux-контейнери не підтримуються, тож там job падав би. Тому контейнер запускається звичайним кроком `docker run` з умовою `if: runner.os == 'Linux'`.

Що змінюється:

1. **Крок Start SQL Server (Linux)** іде одразу після checkout. Контейнер стартує, поки йдуть restore і build, тож на очікування сервера майже не витрачається час. Пароль `sa` генерується на кожен прогін (`openssl rand`), реєструється як маска (`::add-mask::`), щоб не з'явитися в логах, і передається далі через `$GITHUB_ENV`. Префікс `Ci_` гарантує, що пароль відповідає вимогам SQL Server до складності: є великі й малі літери, цифри і символ.
2. **Крок Wait for SQL Server (Linux)** іде перед тестами. До 60 разів з паузою 2 с він пробує виконати `SELECT 1` через `sqlcmd` усередині контейнера. Щойно сервер приймає вхід, крок записує рядок підключення в `SMARTARCHIVER_TEST_SQLSERVER`. Якщо за 2 хвилини сервер так і не піднявся, крок виводить `docker logs` і падає, тож тести БД не можуть тихо пропуститися через збій сервера.
3. **У кроці Test `LogFileName` замінено на `LogFilePrefix`.** Тестових проєктів тепер два, і з однаковою назвою файлу звіт другого затирав звіт першого (`WARNING: Overwriting results file`). З префіксом кожен проєкт пише свій `.trx`.

Оновлений файл повністю:

```yaml
name: CI

# Збірка й тести на кожен PR і на кожен пуш у main.
on:
    pull_request:
    push:
        branches: [main]
    workflow_dispatch:

# Новий пуш у ту саму гілку скасовує попередній ще не завершений прогін.
concurrency:
    group: ci-${{ github.workflow }}-${{ github.ref }}
    cancel-in-progress: true

permissions:
    contents: read

env:
    SOLUTION: SmartArchiver.slnx
    DOTNET_NOLOGO: true
    DOTNET_CLI_TELEMETRY_OPTOUT: true

defaults:
    run:
        shell: bash # однаковий синтаксис на Linux і Windows

jobs:
    build-test:
        name: build & test (${{ matrix.os }})
        runs-on: ${{ matrix.os }}
        timeout-minutes: 15
        strategy:
            fail-fast: false
            matrix:
                os: [ubuntu-latest, windows-latest]

        steps:
            - name: Checkout
              uses: actions/checkout@v4

            # Тестам БД потрібен SQL Server. Контейнер є лише на Linux: Windows-раннери GitHub не запускають
            # Linux-контейнери, тож там ці тести пропускаються (див. DatabaseFactAttribute).
            # Стартуємо до збірки, щоб сервер піднімався паралельно. Пароль новий на кожен прогін і замаскований у логах.
            - name: Start SQL Server (Linux)
              if: runner.os == 'Linux'
              run: |
                  password="Ci_$(openssl rand -hex 16)"
                  echo "::add-mask::$password"
                  echo "SQL_SA_PASSWORD=$password" >> "$GITHUB_ENV"
                  docker run -d --name sql -p 1433:1433 \
                    -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=$password" \
                    mcr.microsoft.com/mssql/server:2025-latest

            - name: Setup .NET
              uses: actions/setup-dotnet@v4
              with:
                  dotnet-version: 10.0.x # має збігатися з TargetFramework у .csproj

            - name: Cache NuGet packages
              uses: actions/cache@v4
              with:
                  path: ~/.nuget/packages
                  key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
                  restore-keys: ${{ runner.os }}-nuget-

            - name: Restore
              run: dotnet restore ${{ env.SOLUTION }}

            - name: Build
              run: dotnet build ${{ env.SOLUTION }} --configuration Release --no-restore

            # Чекаємо, поки сервер прийме вхід, і лише тоді вмикаємо тести БД.
            - name: Wait for SQL Server (Linux)
              if: runner.os == 'Linux'
              run: |
                  for attempt in $(seq 1 60); do
                    if docker exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SQL_SA_PASSWORD" -C -Q "SELECT 1" > /dev/null 2>&1; then
                      echo "SMARTARCHIVER_TEST_SQLSERVER=Server=localhost,1433;User Id=sa;Password=$SQL_SA_PASSWORD;TrustServerCertificate=true" >> "$GITHUB_ENV"
                      exit 0
                    fi
                    sleep 2
                  done
                  docker logs sql
                  exit 1

            # LogFilePrefix, а не LogFileName: тестових проєктів кілька, і з однаковою назвою звіти затирали б один одного.
            - name: Test
              run: >
                  dotnet test ${{ env.SOLUTION }}
                  --configuration Release --no-build
                  --logger "trx;LogFilePrefix=test-results"
                  --collect:"XPlat Code Coverage"
                  --results-directory ./TestResults

            - name: Upload test results and coverage
              if: always()
              uses: actions/upload-artifact@v4
              with:
                  name: test-results-${{ matrix.os }}
                  path: TestResults/**
                  if-no-files-found: warn
                  retention-days: 14
```

Після пушу перевірте лог кроку Test: на ubuntu в `SmartArchiver.Data.Tests` має бути `Skipped: 0`, на windows `Skipped: 3`.

**Що змінено в репозиторії.** Коміт `f8fd2ab` (`ci: run database tests against SQL Server on Linux`): `ci.yml` з відповіді без змін.

**Як перевірено** (разом з асистентом). Кроки Start і Wait прогнали локально тими самими командами, лише з окремим контейнером на іншому порту. Сервер прийняв вхід приблизно за 8 с, усі 14 тестів `SmartArchiver.Data.Tests` виконалися (жоден не пропущено) і пройшли, звітів `.trx` два. Перевірено також, що без SQL Server 3 тести БД позначаються як пропущені, а не падають.
