namespace SmartArchiver.Archive;

/// <summary>Константи формату тому за FORMAT_SPEC_P2 (розділи 1, 2, 7). Усі числа в архіві big-endian.</summary>
public static class FormatConstants
{
    /// <summary>Сигнатура на початку кожного тому.</summary>
    public static ReadOnlySpan<byte> Magic => "SARC"u8;

    /// <summary>Версія формату.</summary>
    public const byte Version = 1;

    public const int MagicSize = 4;

    public const int SaltSize = 16;

    public const int NonceSize = 12;

    /// <summary>magic(4) + version(1) + volume_index(2) + volume_count(2) + salt(16) + nonce(12) + ciphertext_len(4).</summary>
    public const int HeaderSize = 41;

    /// <summary>Запис таблиці блоків: file_index(2) + block_seq(4) + raw_len(4) + coded_len(4) + block_flags(1). Це «+15» у вазі блока.</summary>
    public const int BlockHeaderSize = 15;

    /// <summary>Найменший запис таблиці файлів: name_len(2) + ім'я(0) + orig_size(4) + crc32(4) + file_type(1) + strategy(1).</summary>
    public const int MinFileEntrySize = 12;

    /// <summary>Розмір блока за замовчуванням, поки немає team_config.json.</summary>
    public const int DefaultBlockSize = 64 * 1024;

    /// <summary>file_count(2) + flags(1) + reserved(1) + block_count(2): «6» у формулі місткості тому (розділ 7).</summary>
    public const int PayloadFixedSize = 6;

    /// <summary>HMAC-SHA256 у кінці захищеного тому. Місткість тому в пакувальнику завжди враховує його.</summary>
    public const int TagSize = 32;

    /// <summary>Найкоротший можливий том: заголовок, порожній шифротекст і HMAC. Коротший дає E_SHORT.</summary>
    public const int MinVolumeSize = HeaderSize + TagSize;

    /// <summary>block_flags, біт 0: дані блоку є сирими байтами, coded_len = raw_len. Біти 1–7 мають бути 0.</summary>
    public const byte NoCompressionFlag = 0x01;

    /// <summary>
    /// Найбільший raw_len одного блока. Читач відхиляє більші значення до виділення пам'яті,
    /// інакше підроблений запис блока змусив би декодер виділити гігабайти.
    /// </summary>
    public const int MaxBlockRawLength = 16 * 1024 * 1024;

    /// <summary>Максимальна довжина імені в байтах UTF-8.</summary>
    public const int MaxNameBytes = 255;

    /// <summary>file_count і file_index мають 2 байти.</summary>
    public const int MaxFileCount = ushort.MaxValue;

    /// <summary>block_count має 2 байти.</summary>
    public const int MaxBlocksPerVolume = ushort.MaxValue;

    /// <summary>orig_size має 4 байти, тож файл не більший за 4 ГіБ − 1.</summary>
    public const long MaxFileSize = uint.MaxValue;

    /// <summary>Найбільше значення file_type (розділ 2.2).</summary>
    public const byte MaxFileType = 6;
}
