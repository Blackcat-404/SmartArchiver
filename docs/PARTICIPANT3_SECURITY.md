# Учасник 3 — Захист, CP1

Реалізація у `src/SmartArchiver.Core/ArchiveSecurity.cs`, тести — `tests/SmartArchiver.Core.Tests/ArchiveSecurityTests.cs`.

- PBKDF2-HMAC-SHA256: 100 000 ітерацій, 16-байтна випадкова сіль, 64 байти результату (32 AES + 32 HMAC).
- AES-256-CTR: 12-байтовий випадковий nonce, далі 32-бітний big-endian лічильник від 0.
- HMAC-SHA256: 32-байтовий тег від `header(41) || ciphertext`; перевірка до розшифрування через constant-time порівняння.
- Помилки перевірки автентичності: `E_HMAC`. Паролі й ключі в БД не записуються.
- Два способи виклику: `ArchiveSecurity.BuildProtected(files, password)` / `ReadProtected(archive, password)` або `Protect(plainArchive, password)` / `Unprotect(protectedArchive, password)`.
- Наявні `ArchiveWriter` і `ArchiveReader` залишені без змін, щоб не порушити попередні тести та API.

## Запуск тестів

```powershell
dotnet test tests/SmartArchiver.Core.Tests/SmartArchiver.Core.Tests.csproj
```

## Увага щодо специфікації

`FORMAT_SPEC_P2.md` не було в наданих файлах. Наразі погоджена в модулі розкладка: `41-byte header | ciphertext | 32-byte HMAC`. Лічильник CTR стартує з нуля. Перед інтеграцією еталонних архівів потрібно звірити точні параметри з методичкою. **Сумісність із еталонами поки не підтверджена.**

## Передача інтегратору

`ArchiveSecurity.BuildProtected` повертає готові байти захищеного архіву, `ArchiveSecurity.ReadProtected` відновлює список файлів. Для CLI або Avalonia потрібно додати окремий ввід пароля й виклик цих методів. Незахищені архіви та старі команди не змінено.
