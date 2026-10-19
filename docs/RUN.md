# Як запустити

Потрібен .NET SDK 10 (`dotnet --version`).

```
dotnet test                                    # автотести (тести БД без SQL Server пропускаються, див. README)
dotnet run --project src/SmartArchiver.Cli     # інтерактивне меню
```

## Меню

| Пункт | Що робить |
|---|---|
| 1 | Запакувати файли: вводите шляхи (можна перетягнути файл у вікно), розмір блока й ім'я архіву. Програма одразу читає архів назад і порівнює з оригіналом |
| 2 | Розпакувати архів у теку (наявні файли не перезаписує) |
| 3 | Показати заголовок тому та таблицю файлів |
| 4 | Демонстрація: порожній файл, файл з 1 байта, великий файл у кількох блоках, небезпечні імена, пошкоджені архіви |
| 5 | База даних: показує, чи вона ввімкнена, створює схему, якщо її ще немає, і рахує збережені прогони вимірювання Q (налаштування в README) |
| 6 | Аналіз файлу: ознаки (ентропія, частка друкованих символів, довжина рядків, найчастіші байти, автокореляція), тип файлу за класифікатором і стратегія стиснення |
| 7 | Експерименти: 1 — порівняння жадібного й лінивого LZ77, 2 — навчання класифікатора типу файлу, 3 — вплив передбачення наступного байта на ентропію і розмір (результати пишуться в базу, якщо вона ввімкнена) |

## Команди без меню

```
dotnet run --project src/SmartArchiver.Cli -- pack test.arc a.txt b.png
dotnet run --project src/SmartArchiver.Cli -- info test.arc
dotnet run --project src/SmartArchiver.Cli -- unpack test.arc out
dotnet run --project src/SmartArchiver.Cli -- demo
dotnet run --project src/SmartArchiver.Cli -- db
dotnet run --project src/SmartArchiver.Cli -- analyze README.md
dotnet run --project src/SmartArchiver.Cli -- train-classifier Datasets classifier.json
dotnet run --project src/SmartArchiver.Cli -- train-predictor Datasets predictor.json
dotnet run --project src/SmartArchiver.Cli -- prediction-experiment Datasets
```
