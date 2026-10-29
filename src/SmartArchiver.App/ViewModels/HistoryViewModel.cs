using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartArchiver.App.Services;
using SmartArchiver.Data;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.App.ViewModels;

/// <summary>The latest packs and unpacks from the database. The history has no file names, paths or passwords.</summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private const int Count = 100;

    public ObservableCollection<string> Rows { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "Натисніть «Оновити», щоб прочитати історію з бази даних.";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Status = "Читання бази даних…";
        try
        {
            IReadOnlyList<ArchiveOperation>? operations = await Task.Run(() =>
            {
                IMeasurementStore store = ArchiveFiles.OpenStore();
                return store.IsEnabled ? store.ListArchiveOperations(Count) : null;
            });
            Rows.Clear();
            if (operations is null)
            {
                Status = "База даних вимкнена: рядок підключення порожній (див. README).";
                return;
            }
            Rows.Add($"{"Час (UTC)",-19}  {"Операція",-8} {"Файлів",6} {"Стиснено",8} {"Розмір, Б",12} {"Архів, Б",12} {"Томів",5} {"Час, с",8}  Результат");
            foreach (ArchiveOperation o in operations)
            {
                Rows.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{o.StartedAtUtc:yyyy-MM-dd HH:mm:ss}  {o.Operation,-8} {o.FileCount,6} {o.CompressedFiles,8} {o.OriginalBytes,12} {o.ArchiveBytes,12} {o.VolumeCount,5} {o.ElapsedSeconds,8:F3}  {(o.Succeeded ? "OK" : o.ErrorCode)}"));
            }
            Status = $"Останні операції: {operations.Count}.";
        }
        catch (DatabaseException ex)
        {
            Status = $"База даних: {ex.Message}";
        }
    }
}
