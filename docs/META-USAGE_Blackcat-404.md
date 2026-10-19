# META-USAGE: Blackcat-404

Зони: Аналіз файлу, База даних.

## Використані інструменти

- Claude Opus 5.5

## 1. Що таке CI/CD на GitHub і як воно працює

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

## 2. Структура проєкту з EF Core

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

## 3. ci.yml під тести бази даних

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

## 4. Ознаки файлу для класифікатора

**Промпт:**

> Мені треба зробити аналіз файлу для архіватора: обчислити ознаки — ентропію Шеннона, гістограму байтів, частку друкованих ASCII, середню довжину рядка, автокореляцію на лагах 1–16 і ентропію біграм. Поясни, що означає кожна ознака, як її правильно рахувати і як перевірити тестами, і дай приклад коду на C#, від якого можна відштовхуватися.

**Відповідь:**

Ознаки — це «відбиток» файлу у вигляді кількох чисел. Жодна з них окремо не визначає тип, але разом вони добре розрізняють текст, код, картинки, виконувані файли й уже стиснені дані. Тому їх і подають на вхід класифікатору, а не приймають рішення за одним порогом на кшталт «висока ентропія — не стискати» (методичка, 10.28).

| Ознака                   | Як рахувати                                                   | Що показує                                                                                                        |
| ------------------------ | ------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| Ентропія байтів          | `H = −Σ p·log2 p` по 256 значеннях, де `p` — частота значення | 0 для одного повторюваного байта, ≈8 для шуму і стиснених даних, 4–5 для тексту                                   |
| Гістограма               | частота кожного значення байта, сума 1                        | «склад» файлу: у тексті багато літер і пробілів, у картинках — нулі й 0xFF                                        |
| Частка друкованих ASCII  | байти `0x20…0x7E` плюс `\t`, `\n`, `\r`, поділені на довжину  | близько 1 для англійського тексту й коду, близько 0.38 для шуму                                                   |
| Середня довжина рядка    | символи без `\n` і `\r`, поділені на кількість рядків         | у коді 30–60, у прозі довше, у бінарних файлах «рядки» випадкові                                                  |
| Автокореляція, лаги 1–16 | `r(k) = Σ(x[i]−m)(x[i+k]−m) / Σ(x[i]−m)²`                     | чи пов'язаний байт із байтом через `k` позицій: у шумі ≈0, у растрі піки на кроці пікселя (наприклад, 4 для RGBA) |
| Ентропія біграм          | та сама формула по парах сусідніх байтів, 0…16 біт            | у тексті пари повторюються, тож вона значно нижча, ніж у шуму                                                     |

На що звернути увагу:

- **Крайні випадки.** Порожній файл дає нулі, а не ділення на нуль. Для сталих даних дисперсія нульова, тож автокореляцію визначаємо як 0. Лаг, не менший за довжину даних, теж дає 0.
- **`\r` не рахуємо в довжину рядка**, щоб один і той самий текст з Windows (CRLF) і з Linux (LF) мав однакові ознаки.
- **Кирилиця в UTF-8** займає два байти поза ASCII, тому в українському тексті частка друкованих ASCII близько 40%. Це не помилка: ознака описує саме ASCII, а відрізнити такий текст від бінарних даних допоможуть ентропія й автокореляція.
- **Ентропію біграм** зручно рахувати масивом на 65 536 лічильників, індекс `(попередній << 8) | поточний`.

Перевіряти ознаки варто на даних з відомою відповіддю (методичка, 10.41), ще до того, як щось навчати:

- нулі: ентропія 0, друкованих 0, автокореляція 0;
- байти 0…255 порівну: ентропія рівно 8;
- `"abababab"`: ентропія рівно 1 біт;
- шум: ентропія понад 7.9, автокореляція близька до 0;
- англійський текст: усі символи друковані, ентропія нижча за 5;
- чергування `0, 255, 0, 255`: автокореляція ≈ −1 на непарних лагах і ≈ +1 на парних.

Приклад, від якого можна відштовхуватися: головні обчислення — ентропія, ентропія біграм і автокореляція. Гістограма, частка друкованих символів і довжина рядків рахуються з тих самих лічильників байтів.

```csharp
/// <summary>H = -sum(p * log2 p) over the counted symbols.</summary>
private static double Entropy(ReadOnlySpan<long> counts, long total)
{
    if (total == 0)
        return 0;
    double entropy = 0;
    foreach (long count in counts)
    {
        if (count == 0)
            continue;
        double p = (double)count / total;
        entropy -= p * Math.Log2(p);
    }
    return entropy;
}

private static double BigramEntropy(ReadOnlySpan<byte> data)
{
    if (data.Length < 2)
        return 0;
    var counts = new long[256 * 256];
    for (int i = 1; i < data.Length; i++)
        counts[(data[i - 1] << 8) | data[i]]++;
    return Entropy(counts, data.Length - 1);
}

/// <summary>
/// Sample autocorrelation r(k) = sum((x[i] - m) * (x[i+k] - m)) / sum((x[i] - m)^2) for k = 1..16.
/// A constant sequence or a lag not shorter than the data gives 0.
/// </summary>
private static double[] Autocorrelation(ReadOnlySpan<byte> data)
{
    var result = new double[AutocorrelationLags];
    if (data.Length < 2)
        return result;

    double mean = 0;
    foreach (byte b in data)
        mean += b;
    mean /= data.Length;

    double variance = 0;
    foreach (byte b in data)
        variance += (b - mean) * (b - mean);
    if (variance == 0)
        return result;

    for (int lag = 1; lag <= AutocorrelationLags && lag < data.Length; lag++)
    {
        double sum = 0;
        for (int i = 0; i + lag < data.Length; i++)
            sum += (data[i] - mean) * (data[i + lag] - mean);
        result[lag - 1] = sum / variance;
    }
    return result;
}
```

## 5. Багатошаровий перцептрон без бібліотек

**Промпт:**

> Тепер потрібен багатошаровий перцептрон без готових бібліотек: довільна кількість шарів, навчання зворотним поширенням помилки, збереження і завантаження ваг. Поясни, як це влаштовано, які функції активації й втрат обрати для класифікації на 7 класів, як перевірити, що зворотне поширення правильне, і дай приклад коду, від якого можна відштовхуватися.

**Відповідь:**

**Прямий прохід.** Кожен шар рахує `z = W·a + b` для виходу попереднього шару `a`. У прихованих шарах до `z` застосовують нелінійність, на виході — softmax, щоб отримати ймовірності класів, які в сумі дають 1.

**Вибір активацій і втрат:**

- **Вихід: softmax + cross-entropy.** Це стандарт для кількох класів. Втрата `L = −ln p[правильний клас]` велика, коли мережа впевнено помиляється. Головна перевага — простий градієнт на виході: `∂L/∂z = p − onehot(клас)`.
- **Приховані шари: `tanh`.** Значення центровані навколо нуля, похідна виражається через сам вихід: `tanh' = 1 − a²`. Sigmoid вчиться повільніше через нецентровані значення, а ReLU в маленьких мережах легко дає «мертві» нейрони.

**Зворотне поширення.** Похибку `δ` на виході передаємо назад шар за шаром:

```
δ_вихід          = p − onehot(клас)
∂L/∂W[шар][o,i]  = δ[o] · a_попередній[i]
∂L/∂b[шар][o]    = δ[o]
δ_попередній[i]  = (Σ_o W[шар][o,i] · δ[o]) · (1 − a_попередній[i]²)
```

**Навчання.** Стохастичний градієнтний спуск: після кожного прикладу `W ← W − η·∂L/∂W`.

**Початкові ваги.** Нульові ваги не годяться: усі нейрони шару вчилися б однаково. Беруть рівномірні значення в межах `±√(6/(n_in + n_out))` (ініціалізація Глоро) з генератора із заданим seed. Seed робить запуск відтворюваним, а методичка вимагає порівнювати кілька ініціалізацій і обирати модель за macro-F1 на valid.

**Як перевірити** (методичка, 6.11):

1. **Прямий прохід на вручну заданих вагах.** Відповідь рахується на папері й порівнюється.
2. **Значення втрати.** Для відомої ймовірності `L = −ln p`.
3. **Чисельний градієнт.** Для кожної ваги `(L(w+h) − L(w−h)) / 2h` має збігтися з тим, що дає зворотне поширення. Це найсильніша перевірка: вона ловить будь-яку помилку у формулах.
4. **Навчання на XOR.** XOR не розділяється однією прямою, тож без прихованого шару його не вивчити. Втрата має впасти в рази, і всі 4 випадки мають класифікуватися правильно.
5. **Збереження і завантаження.** Після них мережа дає ті самі відповіді.

**Збереження ваг.** JSON (`System.Text.Json` входить до .NET): розміри шарів, ваги, зсуви. При завантаженні варто перевірити, що кількість ваг відповідає розмірам шарів, інакше пошкоджений файл дасть незрозумілу помилку пізніше.

**Швидкість навчання** підбирають експериментом. Для оновлень після кожного прикладу забагато — це коли мережа «перестрибує» мінімум і застрягає.

Приклад, від якого можна відштовхуватися: softmax, втрата і зворотне поширення. Прямий прохід, навчання й збереження ваг будуються навколо них.

```csharp
private static double[] Softmax(double[] z)
{
    double max = z.Max();
    double[] exp = Array.ConvertAll(z, v => Math.Exp(v - max));
    double sum = exp.Sum();
    return Array.ConvertAll(exp, v => v / sum);
}

private static double CrossEntropy(double[] probabilities, int target) =>
    -Math.Log(Math.Max(probabilities[target], double.Epsilon));

/// <summary>
/// For softmax with cross-entropy the error at the output is p - onehot(target).
/// Going back, each hidden error is the weighted sum of the next layer's errors times tanh' = 1 - a^2.
/// </summary>
private (double[][] WeightGrads, double[][] BiasGrads) Backpropagate(double[][] activations, int target)
{
    var weightGrads = new double[Weights.Length][];
    var biasGrads = new double[Weights.Length][];

    double[] delta = (double[])activations[^1].Clone();
    delta[target] -= 1;

    for (int layer = Weights.Length - 1; layer >= 0; layer--)
    {
        double[] input = activations[layer];
        weightGrads[layer] = new double[Weights[layer].Length];
        for (int o = 0; o < delta.Length; o++)
        {
            for (int i = 0; i < input.Length; i++)
                weightGrads[layer][o * input.Length + i] = delta[o] * input[i];
        }
        biasGrads[layer] = (double[])delta.Clone();

        if (layer == 0)
            break;
        var previousDelta = new double[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            double sum = 0;
            for (int o = 0; o < delta.Length; o++)
                sum += Weights[layer][o * input.Length + i] * delta[o];
            previousDelta[i] = sum * (1 - input[i] * input[i]);
        }
        delta = previousDelta;
    }
    return (weightGrads, biasGrads);
}
```

Перевірка зворотного поширення чисельним градієнтом:

```csharp
[Fact]
public void Gradients_MatchNumericalDerivativeOfLoss()
{
    var net = new Perceptron([3, 4, 3], seed: 5);
    double[] input = [0.5, -1, 2];
    const int target = 2;
    const double h = 1e-5;

    (double[][] weightGrads, double[][] biasGrads) = net.Gradients(input, target);

    for (int layer = 0; layer < net.Weights.Length; layer++)
    {
        AssertNumericalMatch(net, net.Weights[layer], weightGrads[layer], input, target, h);
        AssertNumericalMatch(net, net.Biases[layer], biasGrads[layer], input, target, h);
    }
}

private static void AssertNumericalMatch(
    Perceptron net, double[] parameters, double[] analytic, double[] input, int target, double h)
{
    for (int i = 0; i < parameters.Length; i++)
    {
        double original = parameters[i];
        parameters[i] = original + h;
        double plus = net.Loss(input, target);
        parameters[i] = original - h;
        double minus = net.Loss(input, target);
        parameters[i] = original;

        double numerical = (plus - minus) / (2 * h);
        Assert.True(
            Math.Abs(numerical - analytic[i]) <= 1e-6 * Math.Max(1, Math.Abs(numerical) + Math.Abs(analytic[i])),
            $"parameter {i}: numerical {numerical}, backpropagation {analytic[i]}");
    }
}
```

## 6. Класифікатор типу файлу: навчання й вибір моделі

**Промпт:**

> Маю ознаки файлу і власний перцептрон. Тепер треба класифікатор на 7 типів (`file_type` 0…6): навчити на train, обрати модель за macro-F1 на valid щонайменше з трьох ініціалізацій і показати матрицю помилок. Даних мало — по 3 файли на групу. Як правильно підготувати приклади й поділ, що подавати на вхід мережі, як рахувати macro-F1 і на що звернути увагу? Дай приклади коду, від яких можна відштовхуватися.

**Відповідь:**

**Приклади.** Трьох файлів на групу замало для навчання, тому кожен файл ріжуть на шматки (наприклад, по 4 КБ) і кожен шматок стає окремим прикладом. Щоб один великий файл не переважив цілу групу, кількість шматків з файлу обмежують, а надто короткий хвіст відкидають.

**Поділ train/valid — лише за файлами.** Якщо ділити випадково по шматках, у valid потраплять шматки тих самих файлів, що й у train, і valid покаже майже 1.0, нічого не кажучи про узагальнення. Тому цілий файл іде або в train, або у valid, наприклад кожен третій файл групи.

**Вхід мережі.** Ознаки треба звести до однакового масштабу, інакше величини з більшими одиницями переважать: ентропію поділити на 8, ентропію біграм на 16, довжину рядка взяти в логарифмі з верхньою межею. Повна гістограма — 256 входів, що на малому наборі веде до перенавчання; її краще згрупувати, наприклад у 16 інтервалів.

**Навчання.** На кожній епосі приклади треба перемішувати: дані йдуть згрупованими за класами, і без перемішування мережа запам'ятовує переважно останній клас. Мережу навчають з кількох seed і обирають ту, що має найкращий macro-F1 на valid (методичка, 10.42).

**Macro-F1.** Для кожного класу `F1 = 2·P·R / (P + R)`, де P — точність (precision), R — повнота (recall); macro-F1 — середнє по класах. На відміну від частки правильних відповідей, він карає модель, яка ігнорує рідкісний клас. Класи, яких немає ні серед справжніх, ні серед передбачених міток, з середнього виключають.

**Матриця помилок** показує, які саме класи плутаються: рядок — справжній клас, стовпець — передбачений. Якщо train дає 1.0, а valid помітно менше, саме матриця покаже, чи це перенавчання загалом, чи дві конкретні групи, яким бракує різноманітних прикладів.

Приклад: поділ за файлами.

```csharp
/// <summary>
/// Every third file of each group (by name) goes to valid, the rest to train. Splitting by file rather than by
/// piece keeps pieces of the same file out of both sets, otherwise valid would partly repeat train.
/// </summary>
public static DatasetSplit SplitByFile(IReadOnlyList<LabeledSample> samples)
{
    ArgumentNullException.ThrowIfNull(samples);
    var validSources = new HashSet<string>(StringComparer.Ordinal);
    foreach (IGrouping<int, LabeledSample> group in samples.GroupBy(s => s.Label))
    {
        string[] sources = [.. group.Select(s => s.Source).Distinct().Order(StringComparer.Ordinal)];
        for (int i = 2; i < sources.Length; i += 3)
            validSources.Add(sources[i]);
    }
    return new DatasetSplit(
        [.. samples.Where(s => !validSources.Contains(s.Source))],
        [.. samples.Where(s => validSources.Contains(s.Source))]);
}
```

Приклад: навчання однієї мережі з перемішуванням і оцінкою.

```csharp
private static TrainingRunResult TrainOne(DatasetSplit data, TrainingOptions options, int seed)
{
    var network = new Perceptron(options.LayerSizes, seed);
    var shuffle = new Random(seed);
    LabeledSample[] order = [.. data.Train];

    double firstLoss = 0, loss = 0;
    for (int epoch = 0; epoch < options.Epochs; epoch++)
    {
        // Samples arrive grouped by class; without shuffling the network would mostly remember the last group.
        shuffle.Shuffle(order);
        loss = network.TrainEpoch(
            [.. order.Select(s => s.Input)], [.. order.Select(s => s.Label)], options.LearningRate);
        if (epoch == 0)
            firstLoss = loss;
    }

    return new TrainingRunResult(seed, network, firstLoss, loss, Evaluate(network, data.Train), Evaluate(network, data.Valid));
}
```

Приклад: macro-F1 за матрицею помилок.

```csharp
/// <summary>Harmonic mean of precision and recall; 0 when both are 0.</summary>
public double F1(int c)
{
    double p = Precision(c), r = Recall(c);
    return p + r == 0 ? 0 : 2 * p * r / (p + r);
}

/// <summary>
/// Mean F1 over the classes that occur as an actual label or as a prediction.
/// Classes absent from both are left out, so a sample without some file types is not penalised for them.
/// </summary>
public double MacroF1
{
    get
    {
        double sum = 0;
        int classes = 0;
        for (int c = 0; c < ClassCount; c++)
        {
            if (RowSum(c) == 0 && ColumnSum(c) == 0)
                continue;
            sum += F1(c);
            classes++;
        }
        return classes == 0 ? 0 : sum / classes;
    }
}
```

## 7. Контракт аналізу для архіву і вибір стратегії стиснення

**Промпт:**

> Архів має питати в аналізу тип файлу й стратегію стиснення. Як оформити цей контракт між зонами, як узгодити нумерацію типів і стратегій з FORMAT_SPEC_P2 і як обирати, стискати файл чи ні? Класифікатор іноді помиляється — наприклад, приймає український текст за вже стиснений. Дай приклад коду.

**Відповідь:**

**Контракт.** Інтерфейс кладуть у спільний проєкт Core, щоб від нього залежали і архів, і аналіз, а не одне від одного: `IFileAnalyzer.Analyze(байти)` повертає `FileAnalysis(Kind, Confidence, Strategy)`.

**Нумерація.** Значення, що пишуться в архів, мають збігатися зі специфікацією: `file_type` 0…6 у порядку з таблиці 2.2, а стратегія — як у контрольному архіві розділу 8, де файл, стиснений LZ77 + Гаффманом, має `strategy = 1`. Стан «ще не класифіковано» варто винести за межі 0…6, щоб він не міг випадково потрапити в архів.

**Стратегія — за асиметрією помилок.** Стиснути вже стиснене коштує лише часу: кодер однаково збереже блок як є, якщо виграшу немає. А не стиснути текст — це втрата всього виграшу в розмірі. Тому «не стискати» варто лише тоді, коли згодні класифікатор і самі дані:

- шум і стиснені дані мають ентропію майже 8 біт на байт — їх не стискають за будь-якого класу;
- клас «вже стиснуте» приймають лише за високої ентропії (наприклад, від 7.0); український текст має близько 5, тож його стиснуть, навіть якщо класифікатор помилився;
- одна ентропія рішення не ухвалює: нестиснений растр з плавним градієнтом може мати 7.75 і добре стискатися (методичка, 10.28), тому поріг шуму ставлять ближче до 8.

Приклад контракту:

```csharp
namespace SmartArchiver.Core;

/// <summary>What the analyzer decided about a file before it is archived.</summary>
/// <param name="Kind">File type written to the archive as file_type.</param>
/// <param name="Confidence">Probability the classifier gave to <paramref name="Kind"/>, from 0 to 1.</param>
/// <param name="Strategy">How the archive should compress the file.</param>
public sealed record FileAnalysis(FileKind Kind, double Confidence, CompressionStrategy Strategy);

/// <summary>
/// Contract between file analysis and the archive: the archive asks for every file it packs,
/// writes <see cref="FileAnalysis.Kind"/> as file_type and compresses according to <see cref="FileAnalysis.Strategy"/>.
/// </summary>
public interface IFileAnalyzer
{
    FileAnalysis Analyze(ReadOnlySpan<byte> data);
}
```

Приклад вибору стратегії:

```csharp
/// <summary>
/// Noise and already compressed data are stored as they are: LZ77 finds no repeats there and the Huffman
/// tables only add bytes. Everything else, including artificial data with repeats, is compressed.
/// </summary>
private static CompressionStrategy ChooseStrategy(FileKind kind, FileFeatures features, int length)
{
    if (length == 0 || features.Entropy >= NoiseEntropy)
        return CompressionStrategy.NoCompression;
    if (kind == FileKind.AlreadyCompressed && features.Entropy >= CompressedEntropy)
        return CompressionStrategy.NoCompression;
    return CompressionStrategy.Lz77Huffman;
}
```
