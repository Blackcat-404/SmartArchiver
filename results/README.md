# Результати

Сирі результати, згенеровані програмою (методичка, 8.30–8.31). Отримано на корпусі `Datasets/`, Release-збірка,
α = β = 0.5 і t_ref = 1 с (заглушки до `team_config.json`).

| Файл | Що це | Якою командою отримано |
|---|---|---|
| `demo-2026-11-05.log` | повний прогін показу: збірка з `-warnaserror`, усі тести, сценарії архіватора, ГА, класифікатор | `bash scripts/defense-demo.sh` |
| `acceptance-c1.txt` | 12 приймальних кейсів C1 (15 перевірок) | `dotnet test tests/SmartArchiver.Archive.Tests -c Release --filter "FullyQualifiedName~AcceptanceC1"` |
| `coverage-summary.txt` | покриття рядків і гілок по класах | `dotnet test --collect:"XPlat Code Coverage"` + `reportgenerator`, як у CI |
| `ga-vs-random.csv` | журнал еволюції ГА: кожна хромосома ГА й випадкового пошуку з Q, S, T і генами | `genetic-experiment Datasets`, потім `export-ga <id> results/ga-vs-random.csv` |
| `ga-vs-random-summary.txt` | підсумок ГА проти випадкового пошуку з однаковим бюджетом і фронт Парето | `genetic-experiment Datasets` |
| `classifier-training.txt` | macro-F1 на train і valid для 5 ініціалізацій і матриця помилок обраної моделі | `train-classifier Datasets <модель.json>` |

Розміри відтворюються точно; час і, отже, T та Q ГА залежать від машини.
