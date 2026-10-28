# Як запустити

Потрібен .NET SDK 10 (`dotnet --version`).

```
dotnet test                                    # автотести (тести БД без SQL Server пропускаються, див. README)
dotnet run --project src/SmartArchiver.Cli     # інтерактивне меню
```

## Меню

| Пункт | Що робить |
|---|---|
| 1 | Запакувати файли: вводите шляхи (можна перетягнути файл у вікно), розмір блока, найбільший розмір тому (0 = один том) і ім'я архіву. Файли аналізуються і стискаються; програма одразу читає архів назад і порівнює з оригіналом |
| 2 | Розпакувати архів у теку (наявні файли не перезаписує) |
| 3 | Показати заголовок тому та таблицю файлів |
| 4 | Демонстрація: порожній файл, файл з 1 байта, великий файл у кількох блоках, небезпечні імена, пошкоджені архіви |
| 5 | База даних: показує, чи вона ввімкнена, створює схему, якщо її ще немає, і рахує збережені прогони вимірювання Q (налаштування в README) |
| 6 | Аналіз файлу: ознаки (ентропія, частка друкованих символів, довжина рядків, найчастіші байти, автокореляція), тип файлу за класифікатором і стратегія стиснення |
| 7 | Експерименти: 1 — порівняння жадібного й лінивого LZ77, 2 — навчання класифікатора типу файлу, 3 — вплив передбачення наступного байта на ентропію і розмір, 4 — ДП-рюкзак проти жадібного пакування томів, 5 — фіксована стратегія стиснення проти класифікатора, 6 — вимір Q на всьому корпусі (результати пишуться в базу, якщо вона ввімкнена) |

## Команди без меню

```
dotnet run --project src/SmartArchiver.Cli -- pack test.arc a.txt b.png
dotnet run --project src/SmartArchiver.Cli -- pack test.arc --volume-size 65536 a.txt b.png
dotnet run --project src/SmartArchiver.Cli -- info test.arc
dotnet run --project src/SmartArchiver.Cli -- unpack test.arc out
dotnet run --project src/SmartArchiver.Cli -- demo
dotnet run --project src/SmartArchiver.Cli -- db
dotnet run --project src/SmartArchiver.Cli -- analyze README.md
dotnet run --project src/SmartArchiver.Cli -- train-classifier Datasets classifier.json
dotnet run --project src/SmartArchiver.Cli -- train-predictor Datasets predictor.json
dotnet run --project src/SmartArchiver.Cli -- prediction-experiment Datasets
dotnet run --project src/SmartArchiver.Cli -- prediction-q Datasets [team_config.json]
dotnet run --project src/SmartArchiver.Cli -- strategy-experiment Datasets
dotnet run --project src/SmartArchiver.Cli -- block-impact Datasets [team_config.json]
dotnet run --project src/SmartArchiver.Cli -- encryption-order Datasets
dotnet run --project src/SmartArchiver.Cli -- pack-experiment Datasets 65536 [team_config.json]
dotnet run --project src/SmartArchiver.Cli -- measure Datasets [team_config.json]
dotnet run --project src/SmartArchiver.Cli -- genetic-experiment Datasets [team_config.json] [full]
dotnet run --project src/SmartArchiver.Cli -- history 20
dotnet run --project src/SmartArchiver.Cli -- export-ga <id експерименту> ga.csv
```

`pack`, `unpack` і `info` питають пароль (без показу на екрані) або беруть його зі змінної `SMARTARCHIVER_PASSWORD`. `measure` зберігає прогін у БД, якщо рядок підключення задано (інакше лише виводить таблицю). Пароль виміру не секрет і в БД не потрапляє.
