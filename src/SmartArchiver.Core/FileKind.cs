namespace SmartArchiver.Core;

/// <summary>
/// Тип файлу: значення поля file_type з FORMAT_SPEC_P2 (розділ 2.2), 0…6.
/// Визначає його аналізатор (IFileAnalyzer, Blackcat-404), рішення 27 у docs/decisions.md.
/// </summary>
public enum FileKind : byte
{
    Text = 0,
    SourceCode = 1,
    Structured = 2,
    Raster = 3,
    Executable = 4,
    AlreadyCompressed = 5,
    Artificial = 6,

    /// <summary>Ще не класифіковано. Поза діапазоном специфікації, тож в архів за FORMAT_SPEC_P2 записатися не може.</summary>
    Unknown = 255,
}

/// <summary>
/// Стратегія стиснення файлу (поле strategy у таблиці файлів).
/// За FORMAT_SPEC_P2 стиснений блок завжди кодується як LZ77 із трьома деревами Гаффмана;
/// у контрольному архіві ref_01 такий файл має strategy = 1.
/// </summary>
public enum CompressionStrategy : byte
{
    NoCompression = 0,
    Lz77Huffman = 1,
}
