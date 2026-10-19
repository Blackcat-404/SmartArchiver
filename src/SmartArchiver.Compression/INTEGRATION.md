# Підготовка SmartArchiver.Compression до інтеграції CP2

Додати `Lz77CompressionService.cs` до `src/SmartArchiver.Compression/Lz77/` (або іншої папки всередині того ж проєкту) та `Lz77CompressionServiceTests.cs` до `tests/SmartArchiver.Compression.Tests/`.

## Контракт для учасника Archive

`Lz77CompressionService.PrepareFixedBlocks(data, mode, blockSize)` повертає незалежні блоки.
`PrepareDpBlocks(data, blockSize, boundaryStep)` обирає межі через DP, але **всередині блока використовує GreedyParser** (як поточний DpBlockSplitter).

Для кожного блока:
- `raw_len = block.RawLength`;
- `coded_len = block.CodedLength`;
- `block_flags = block.IsStored ? 1 : 0` (`NO_COMPRESSION` — біт 0);
- записувати рівно `block.CodedBytes` байтів;
- `block_seq` — номер блока в межах файла, починаючи з нуля.
- `strategy` у записі **файла** необхідно встановлювати згідно зі значеннями `CompressionStrategy` з поточного Core; не ототожнювати його з `block_flags`.

Читання: після валідації довжин та прапорців, коли `NO_COMPRESSION=1`, читати сирі байти (та перевірити `coded_len==raw_len`); інакше викликати `Lz77CompressionService.DecodeBlock(codedBytes, checked((int)raw_len))`.

Важливо: ця поставка **не** змінює `ArchiveWriter`, `ArchiveReader`, `ConsoleApp` та `Core`. Для інтеграції в реальний файл архіву потрібна окрема робота власника Archive: формування таблиці блоків та файлів, CRC, сумісність із форматом, обробка пошкоджених архівів.

## Перевірка

`dotnet build`

`dotnet test tests/SmartArchiver.Compression.Tests/SmartArchiver.Compression.Tests.csproj --filter FullyQualifiedName~Lz77CompressionServiceTests`

## Обмеження

- Публічний API приймає `byte[]`, розрахований на блоки/файли, що поміщаються у пам'ять.
- `EncodeBlock` не кодує порожній блок; для порожнього файла список блоків порожній.
- Поточний `DpBlockSplitter` дорого перебирає кандидати; для великих файлів варто виміряти час.
- Перш ніж дозволяти неперевірені архіви, перевірити обмеження декодера: зокрема `stream_len` (переповнення `int`) та заборону росту відновленого блока понад `raw_len`. Це окреме посилення перевірок безпеки.
- Не слід робити висновок, що `DP` токенів гарантує менший *реальний* Huffman-блок: гарантія пункту 5 стосується фіксованої моделі вартості.
