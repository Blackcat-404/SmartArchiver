#!/usr/bin/env bash
# Демонстрація на захисті: збірка й тести з нуля, потім живі сценарії архіватора.
# Запуск з кореня репозиторію:  bash scripts/defense-demo.sh [--skip-build]
# Увесь вивід дублюється в results/runs/demo-<дата>.log (тека в .gitignore).
#
# Якщо в appsettings.Local.json задано базу даних, команди пишуть у неї історію й результати.
# Перед показом переконайтеся, що на порту 1433 працює саме контейнер smart-archiver.

set -u
cd "$(dirname "$0")/.."

mkdir -p results/runs
LOG="results/runs/demo-$(date +%Y%m%d-%H%M%S).log"
exec > >(tee "$LOG") 2>&1

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
CLI=(dotnet run --project src/SmartArchiver.Cli -c Release --no-build --)
export SMARTARCHIVER_PASSWORD="demo-2026"

step() { printf '\n\033[1;36m==== %s ====\033[0m\n' "$*"; }
expect_code() {   # expect_code E_XXX <command...>: команда має завершитися помилкою з цим кодом
    local code="$1"; shift
    local out; out="$("$@" 2>&1)"
    if grep -q "$code" <<<"$out"; then echo "[OK] очікувано: $(grep -m1 "$code" <<<"$out")"
    else echo "[НЕ ТАК] очікували $code, отримали:"; echo "$out"; fi
}

if [[ "${1:-}" != "--skip-build" ]]; then
    step "0. Збірка Release з -warnaserror і всі тести"
    dotnet build SmartArchiver.slnx -c Release -warnaserror -v q || exit 1
    dotnet test SmartArchiver.slnx -c Release --no-build || exit 1
    step "0a. Приймальні кейси C1 окремо"
    dotnet test tests/SmartArchiver.Archive.Tests -c Release --no-build \
        --filter "FullyQualifiedName~AcceptanceC1" --logger "console;verbosity=normal"
fi

step "1. Сім груп корпусу: запакувати у томи по 64 КіБ і побайтово відновити"
"${CLI[@]}" pack "$WORK/corpus.arc" --volume-size 65536 Datasets/[0-6]/*
ls -l "$WORK"/corpus.part*.arc
"${CLI[@]}" unpack "$WORK/corpus.part1.arc" "$WORK/out"
for f in Datasets/[0-6]/*; do
    cmp -s "$f" "$WORK/out/$(basename "$f")" || { echo "[НЕ ТАК] відрізняється $f"; DIFF=1; }
done
[[ -z "${DIFF:-}" ]] && echo "[OK] усі $(ls Datasets/[0-6]/* | wc -l) файлів збігаються побайтово (cmp)"

step "2. NO_COMPRESSION: шум і вже стиснені дані (ZIP, PNG, deflate) зберігаються як є"
head -c 200000 /dev/urandom > "$WORK/noise.bin"
"${CLI[@]}" pack "$WORK/compressed.arc" "$WORK/noise.bin" Datasets/5/*
echo "У колонці «Стратегія» має бути NoCompression, а «Стиснено» дорівнює «Розмір»."

step "3. Пошкоджений байт шифротексту → E_HMAC"
cp "$WORK/compressed.arc" "$WORK/tampered.arc"
printf '\xff' | dd of="$WORK/tampered.arc" bs=1 seek=100 conv=notrunc status=none
expect_code E_HMAC "${CLI[@]}" unpack "$WORK/tampered.arc" "$WORK/t"

step "4. Неправильний пароль, обрізаний том, відсутній том"
expect_code E_HMAC env SMARTARCHIVER_PASSWORD=wrong "${CLI[@]}" unpack "$WORK/compressed.arc" "$WORK/w"
head -c -1 "$WORK/compressed.arc" > "$WORK/truncated.arc"
expect_code E_LENGTH "${CLI[@]}" unpack "$WORK/truncated.arc" "$WORK/tr"
head -c 50 "$WORK/compressed.arc" > "$WORK/short.arc"
expect_code E_SHORT "${CLI[@]}" unpack "$WORK/short.arc" "$WORK/s"
mkdir "$WORK/missing" && cp "$WORK"/corpus.part*.arc "$WORK/missing/" && rm "$WORK/missing/corpus.part2.arc"
expect_code E_VOLUMES "${CLI[@]}" unpack "$WORK/missing/corpus.part1.arc" "$WORK/m"
[[ ! -e "$WORK/w" && ! -e "$WORK/t" && ! -e "$WORK/m" ]] && echo "[OK] після жодної помилки на диск нічого не записано"

step "5. Еталонні архіви викладача"
REF=tests/SmartArchiver.Archive.Tests/Reference
if compgen -G "$REF/ref_*.arc" > /dev/null; then
    for arc in "$REF"/ref_*.arc; do
        [[ "$arc" == *.part[2-9].arc ]] && continue
        SMARTARCHIVER_PASSWORD="${TEST_ARCHIVE_PASSWORD:-algo2026}" "${CLI[@]}" unpack "$arc" "$WORK/ref/$(basename "$arc" .arc)"
    done
else
    echo "Файлів ref_*.arc у $REF немає: викладач їх ще не надав."
fi

step "6. ГА проти випадкового пошуку з однаковим бюджетом"
"${CLI[@]}" genetic-experiment Datasets

step "7. Класифікатор: macro-F1 з 5 ініціалізацій і матриця помилок на valid"
"${CLI[@]}" train-classifier Datasets "$WORK/classifier.json"

step "Готово"
echo "Повний вивід збережено в $LOG"
