using System.Globalization;
using SmartArchiver.Archive;
using SmartArchiver.Core;
using SmartArchiver.Data;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.App.Services;

/// <summary>Volumes on disk, the database and the texts the screens show; the same rules as the console app.</summary>
internal static class ArchiveFiles
{
    /// <summary>Paths of the volumes: one file as is, several as name.part1.arc, name.part2.arc, …</summary>
    public static IReadOnlyList<string> VolumePaths(string archivePath, int count)
    {
        if (count == 1)
            return [archivePath];
        string directory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        string stem = Path.Combine(directory, Path.GetFileNameWithoutExtension(archivePath));
        string extension = Path.GetExtension(archivePath).Length == 0 ? ".arc" : Path.GetExtension(archivePath);
        return [.. Enumerable.Range(1, count).Select(i => string.Create(CultureInfo.InvariantCulture, $"{stem}.part{i}{extension}"))];
    }

    /// <summary>A volume name.partN.arc brings all its neighbours with the same name; any other file is one volume.</summary>
    public static List<byte[]> LoadVolumes(string archivePath)
    {
        if (!TryParsePartName(Path.GetFileName(archivePath), out string stem, out _, out string extension))
            return [File.ReadAllBytes(archivePath)];

        string directory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        var parts = new List<(int Number, string Path)>();
        foreach (string candidate in Directory.GetFiles(directory))
        {
            if (TryParsePartName(Path.GetFileName(candidate), out string candidateStem, out int number, out string candidateExtension)
                && string.Equals(candidateStem, stem, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidateExtension, extension, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add((number, candidate));
            }
        }
        return [.. parts.OrderBy(p => p.Number).Select(p => File.ReadAllBytes(p.Path))];
    }

    private static bool TryParsePartName(string fileName, out string stem, out int number, out string extension)
    {
        stem = "";
        number = 0;
        extension = "";
        int dot = fileName.LastIndexOf('.');
        if (dot <= 0)
            return false;
        extension = fileName[dot..];
        string withoutExtension = fileName[..dot];
        int part = withoutExtension.LastIndexOf(".part", StringComparison.OrdinalIgnoreCase);
        if (part < 0 || !int.TryParse(withoutExtension[(part + ".part".Length)..], NumberStyles.None, CultureInfo.InvariantCulture, out number))
            return false;
        stem = withoutExtension[..part];
        return true;
    }

    /// <summary>The connection string is read from appsettings.json and appsettings.Local.json next to the program.</summary>
    public static IMeasurementStore OpenStore() =>
        MeasurementStore.Open(DatabaseSettings.ReadConnectionString(AppContext.BaseDirectory));

    /// <summary>
    /// Adds the operation to the archive history (no names, paths or passwords). Archiving never depends on the
    /// database: without one nothing is written, and an unreachable one only gives a warning.
    /// </summary>
    public static string? RecordHistory(ArchiveOperation operation)
    {
        try
        {
            IMeasurementStore store = OpenStore();
            if (store.IsEnabled)
                store.SaveArchiveOperation(operation);
            return null;
        }
        catch (DatabaseException ex)
        {
            return $"Операцію не записано в історію: {ex.Message}";
        }
    }

    /// <summary>What the error code means, then the details without the repeated "E_…:" prefixes.</summary>
    public static string Explain(ArchiveException ex)
    {
        string details = ex.Message;
        string prefix = ex.Code + ":";
        while (details.StartsWith(prefix, StringComparison.Ordinal))
            details = details[prefix.Length..].TrimStart();
        return Meaning(ex.Code) + (details.Length == 0 ? "" : $" Деталі: {details}");
    }

    private static string Meaning(ErrorCode code) => code switch
    {
        ErrorCode.E_MAGIC => "E_MAGIC: це не архів SmartArchiver або непідтримувана версія формату.",
        ErrorCode.E_LENGTH => "E_LENGTH: архів обрізаний або має зайві байти.",
        ErrorCode.E_HMAC => "E_HMAC: неправильний пароль або архів змінено.",
        ErrorCode.E_PAYLOAD => "E_PAYLOAD: внутрішня структура архіву пошкоджена або містить небезпечні дані.",
        ErrorCode.E_CODE => "E_CODE: невідома стратегія або пошкоджені стиснені дані.",
        ErrorCode.E_CRC => "E_CRC: контрольна сума файлу не збіглася.",
        ErrorCode.E_VOLUMES => "E_VOLUMES: набір томів неповний, з дублікатами або з різних архівів.",
        ErrorCode.E_CAPACITY => "E_CAPACITY: блок не вміщується в том, збільште розмір тому.",
        _ => code.ToString(),
    };

    public static string KindName(FileKind kind) => kind switch
    {
        FileKind.Text => "текст",
        FileKind.SourceCode => "вихідний код",
        FileKind.Structured => "структуровані дані",
        FileKind.Raster => "растр без стиснення",
        FileKind.Executable => "виконуваний файл",
        FileKind.AlreadyCompressed => "вже стиснуте",
        FileKind.Artificial => "штучні дані",
        _ => "невідомо",
    };

    public static string StrategyName(CompressionStrategy strategy) => strategy switch
    {
        CompressionStrategy.NoCompression => "без стиснення",
        CompressionStrategy.Lz77Huffman => "LZ77 + Гаффман",
        _ => strategy.ToString(),
    };

    /// <summary>Text cut or padded to an exact width, for the monospace tables of the screens.</summary>
    public static string Fit(string text, int width) =>
        text.Length > width ? text[..(width - 1)] + "…" : text.PadRight(width);
}
