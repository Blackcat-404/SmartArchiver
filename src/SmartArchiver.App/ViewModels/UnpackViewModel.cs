using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartArchiver.App.Services;
using SmartArchiver.Archive;
using SmartArchiver.Core;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.App.ViewModels;

/// <summary>
/// Atomic unpacking: the archive is checked in memory first (HMAC, structure, CRC32) and existing files are never
/// overwritten, so a bad archive or a wrong password leaves nothing on disk.
/// </summary>
public sealed partial class UnpackViewModel : ViewModelBase
{
    public ObservableCollection<string> Result { get; } = [];

    /// <summary>The archive or any of its volumes name.partN.arc: the neighbours are found by name.</summary>
    [ObservableProperty]
    public partial string ArchivePath { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string OutputFolder { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "Оберіть архів, введіть пароль і теку для файлів.";

    [RelayCommand]
    private async Task UnpackAsync()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath) || string.IsNullOrWhiteSpace(OutputFolder))
        {
            Status = "Оберіть архів і теку для файлів.";
            return;
        }
        if (!File.Exists(ArchivePath))
        {
            Status = $"Файл не знайдено: {ArchivePath}";
            return;
        }

        Status = "Розпакування…";
        Result.Clear();
        string archivePath = ArchivePath, password = Password, outputFolder = OutputFolder;
        try
        {
            (IReadOnlyList<string> lines, string status) = await Task.Run(() => Unpack(archivePath, password, outputFolder));
            foreach (string line in lines)
                Result.Add(line);
            Status = status;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Проблема з файлами: {ex.Message}";
        }
    }

    private static (IReadOnlyList<string> Lines, string Status) Unpack(string archivePath, string password, string outputFolder)
    {
        List<byte[]> volumes = ArchiveFiles.LoadVolumes(archivePath);
        var history = new ArchiveOperation
        {
            StartedAtUtc = DateTime.UtcNow,
            Operation = ArchiveOperationKind.Unpack,
            ArchiveBytes = volumes.Sum(v => (long)v.Length),
            VolumeCount = volumes.Count,
        };
        var watch = Stopwatch.StartNew();
        ArchiveResult<IReadOnlyList<ExtractedFile>> result;
        try
        {
            result = ArchivePipeline.TryExtract(volumes, password, outputFolder);
        }
        catch (IOException)
        {
            Finish(history, watch, "E_IO");
            throw;
        }
        if (!result.Ok)
        {
            Finish(history, watch, result.Error.ToString());
            return ([], ArchiveFiles.Explain(new ArchiveException(result.Error!.Value, result.Message)));
        }

        IReadOnlyList<ExtractedFile> files = result.Value!;
        history.FileCount = files.Count;
        history.CompressedFiles = files.Count(f => f.Strategy == CompressionStrategy.Lz77Huffman);
        history.OriginalBytes = files.Sum(f => (long)f.Data.Length);
        string? warning = Finish(history, watch, null);

        var lines = new List<string> { $"{ArchiveFiles.Fit("Файл", 28)} {"Розмір, Б",11}  {ArchiveFiles.Fit("Тип", 20)} Стратегія" };
        lines.AddRange(files.Select(f => string.Create(CultureInfo.InvariantCulture,
            $"{ArchiveFiles.Fit(f.Name, 28)} {f.Data.Length,11}  {ArchiveFiles.Fit(ArchiveFiles.KindName(f.Kind), 20)} {ArchiveFiles.StrategyName(f.Strategy)}")));
        string status = $"Готово: {files.Count} файл(ів) у {Path.GetFullPath(outputFolder)}. HMAC і CRC32 кожного файлу збігаються.";
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
