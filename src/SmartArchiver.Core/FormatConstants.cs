namespace SmartArchiver.Core;

/// <summary>
/// Константи формату тому. Усі числа в архіві big-endian.
/// УВАГА: розкладку полів звірено лише зі стислим описом у методичці (FORMAT_SPEC_P2.md в мене не було).
/// Якщо специфікація відрізняється, міняти треба тільки цей файл і VolumeHeader.cs.
/// </summary>
public static class FormatConstants
{
    /// <summary>Сигнатура на початку кожного тому.</summary>
    public static ReadOnlySpan<byte> Magic => "SARC"u8;

    /// <summary>Версія формату.</summary>
    public const byte Version = 1;

    public const int MagicSize = 4;
    public const int SaltSize = 16;
    public const int NonceSize = 12;

    /// <summary>magic(4) + версія(1) + номер тому(2) + кількість томів(2) + сіль(16) + nonce(12) + ciphertext_len(4).</summary>
    public const int HeaderSize = 41;

    /// <summary>file_index(2) + block_index(4) + raw_len(4) + coded_len(4) + стратегія(1). Це «+15 байт» з ваги блока в рюкзаку.</summary>
    public const int BlockHeaderSize = 15;

    /// <summary>Найменший можливий запис таблиці файлів: name_len(2) + ім'я(0) + size(8) + crc(4) + тип(1) + стратегія(1) + зміщення(8).</summary>
    public const int MinFileEntrySize = 24;

    /// <summary>Розмір блока за замовчуванням, поки немає team_config.json.</summary>
    public const int DefaultBlockSize = 64 * 1024;

    /// <summary>Максимальна довжина імені в байтах UTF-8.</summary>
    public const int MaxNameBytes = 255;

    /// <summary>file_index у заголовку блока має 2 байти, тож файлів не більше 65536.</summary>
    public const int MaxFileCount = ushort.MaxValue + 1;
}
