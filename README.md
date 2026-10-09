# SmartArchiver

Розумний захищений архіватор файлів (проєкт 2). Зараз готово те, що потрібно для чекпоінта 1: формат архіву SARC без стиснення й шифрування (запис і читання томів, таблиця файлів, блоки, CRC32, перевірка імен) і база даних для результатів вимірювань.

## Структура

| Шлях | Що там |
| --- | --- |
| `src/SmartArchiver.Core` | формат архіву: запис і читання томів |
| `src/SmartArchiver.Cli` | консольна програма `smartarchiver` |
| `src/SmartArchiver.Data` | база даних результатів (EF Core + SQL Server) |
| `SmartArchiver.App` | графічний інтерфейс на Avalonia, поки шаблон |
| `tests/` | автотести (xUnit) |
| `docs/RUN.md` | меню й команди програми |
| `docs/decisions.md` | рішення команди: проблема → варіанти → рішення → чому |

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
| `MeasurementFileResults` | один файл прогону | прогін (зовнішній ключ, каскадне видалення), група корпусу, розмір до і в архіві, чи відновлено |

Нові таблиці (експерименти, журнал ГА, навчання класифікатора, історія архівацій) з'являтимуться на наступних чекпоінтах окремими міграціями.

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
