using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartArchiver.Analysis;
using SmartArchiver.App.Services;
using SmartArchiver.Archive;
using SmartArchiver.Core;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.App.ViewModels;

/// <summary>
/// Packing through the full pipeline: analysis, blocks, LZ77 + Huffman, packing into volumes, AES and HMAC.
/// The archive is read back and compared with the files right after it is written.
/// </summary>
public sealed partial class PackViewModel : ViewModelBase
{
    public ObservableCollection<string> Files { get; } = [];

    /// <summary>What the analyzer decided for every file and how big it became.</summary>
    public ObservableCollection<string> Result { get; } = [];

    [ObservableProperty]
    public partial string? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string PasswordAgain { get; set; } = "";

    /// <summary>Largest volume in bytes; 0 writes one volume of any size.</summary>
    [ObservableProperty]
    public partial string VolumeSize { get; set; } = "0";

    [ObservableProperty]
    public partial string ArchivePath { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "Додайте файли, задайте пароль і шлях до архіву.";

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (Files.Any(f => string.Equals(Path.GetFileName(f), Path.GetFileName(path), StringComparison.Ordinal)))
            {
                Status = $"Файл з іменем {Path.GetFileName(path)} уже додано: імена в архіві мають бути унікальні.";
                continue;
            }
            Files.Add(path);
        }
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedFile is not null)
            Files.Remove(SelectedFile);
    }

    [RelayCommand]
    private void ClearFiles() => Files.Clear();

    [RelayCommand]
    private async Task PackAsync()
    {
        if (Password.Length == 0 || Password != PasswordAgain)
        {
            Status = "Пароль порожній або не збігається з повтором.";
            return;
        }
        if (!long.TryParse(VolumeSize, NumberStyles.None, CultureInfo.InvariantCulture, out long volumeSize))
        {
            Status = "Розмір тому має бути цілим числом байтів (0 = один том).";
            return;
        }
        if (string.IsNullOrWhiteSpace(ArchivePath))
        {
            Status = "Оберіть, куди зберегти архів.";
            return;
        }

        Status = "Пакування…";
        Result.Clear();
        string[] paths = [.. Files];
        string password = Password, archivePath = ArchivePath;
        try
        {
            (IReadOnlyList<string> lines, string status) = await Task.Run(() => Pack(paths, password, archivePath, volumeSize));
            foreach (string line in lines)
                Result.Add(line);
            Status = status;
        }
        catch (ArchiveException ex)
        {
            Status = ArchiveFiles.Explain(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Проблема з файлами: {ex.Message}";
        }
    }

    private static (IReadOnlyList<string> Lines, string Status) Pack(string[] paths, string password, string archivePath, long volumeSize)
    {
        List<ArchiveInput> inputs = [.. paths.Select(p => new ArchiveInput(Path.GetFileName(p), File.ReadAllBytes(p)))];
        var options = new PackOptions(password, new FileAnalyzer(), MaxVolumeBytes: volumeSize);
        var history = new ArchiveOperation
        {
            StartedAtUtc = DateTime.UtcNow,
            Operation = ArchiveOperationKind.Pack,
            FileCount = inputs.Count,
            OriginalBytes = inputs.Sum(i => (long)i.Data.Length),
        };
        var watch = Stopwatch.StartNew();
        IReadOnlyList<PreparedFile> prepared;
        IReadOnlyList<byte[]> volumes;
        try
        {
            prepared = ArchivePipeline.Prepare(inputs, options);
            volumes = ArchivePipeline.Seal(prepared, options);
        }
        catch (ArchiveException ex)
        {
            Finish(history, watch, ex.Code.ToString());
            throw;
        }
        watch.Stop();

        IReadOnlyList<string> outputs = ArchiveFiles.VolumePaths(archivePath, volumeSize > 0 ? volumes.Count : 1);
        for (int i = 0; i < volumes.Count; i++)
            File.WriteAllBytes(outputs[i], volumes[i]);

        // Read back with HMAC and CRC32 checks and compare with the originals.
        ArchiveResult<IReadOnlyList<ExtractedFile>> check = ArchivePipeline.TryUnpack(volumes, password);
        Dictionary<string, byte[]> restored = check.Ok ? check.Value!.ToDictionary(f => f.Name, f => f.Data, StringComparer.Ordinal) : [];
        bool identical = check.Ok && inputs.All(i => restored.TryGetValue(i.Name, out byte[]? data) && data.AsSpan().SequenceEqual(i.Data));
        history.CompressedFiles = prepared.Count(f => f.Strategy == CompressionStrategy.Lz77Huffman);
        history.ArchiveBytes = volumes.Sum(v => (long)v.Length);
        history.VolumeCount = volumes.Count;
        string? warning = Finish(history, watch, identical ? null : check.Error?.ToString() ?? "E_VERIFY");

        var lines = new List<string>
        {
            $"{ArchiveFiles.Fit("Файл", 28)} {"Розмір, Б",11} {"В архіві, Б",12}  {ArchiveFiles.Fit("Тип", 20)} Стратегія",
        };
        lines.AddRange(prepared.Select(f => string.Create(CultureInfo.InvariantCulture,
            $"{ArchiveFiles.Fit(f.Name, 28)} {f.Size,11} {f.CodedSize,12}  {ArchiveFiles.Fit(ArchiveFiles.KindName(f.Kind), 20)} {ArchiveFiles.StrategyName(f.Strategy)}")));
        lines.Add("");
        lines.AddRange(outputs.Take(volumes.Count).Select(o => "Записано: " + o));

        string status = string.Create(CultureInfo.InvariantCulture,
            $"{(identical ? "Готово" : "ПОМИЛКА: файли після читання назад не збігаються")}: {history.OriginalBytes} Б → {history.ArchiveBytes} Б у {volumes.Count} том(ах).");
        return (lines, warning is null ? status : status + " " + warning);
    }

    private static string? Finish(ArchiveOperation history, Stopwatch watch, string? errorCode)
    {
        history.ElapsedSeconds = watch.Elapsed.TotalSeconds;
        history.Succeeded = errorCode is null;
        history.ErrorCode = errorCode;
        return ArchiveFiles.RecordHistory(history);
    }
}
