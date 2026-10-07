namespace SmartArchiver.Core;

/// <summary>
/// Тип файлу за аналізатором. Сім груп корпусу + Unknown.
/// Числові значення тимчасові: остаточно їх визначає контракт IFileAnalyzer (Frostouch).
/// </summary>
public enum FileKind : byte
{
    Unknown = 0,
    Text = 1,
    SourceCode = 2,
    Structured = 3,
    Raster = 4,
    Executable = 5,
    AlreadyCompressed = 6,
    Artificial = 7,
}

/// <summary>Стратегія обробки файлу/блока. Зараз реалізовано лише NoCompression.</summary>
public enum CompressionStrategy : byte
{
    NoCompression = 0,
    Huffman = 1,
    Lz77Huffman = 2,
}
