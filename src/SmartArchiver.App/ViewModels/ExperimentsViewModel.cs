using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartArchiver.App.Services;
using SmartArchiver.Data;

namespace SmartArchiver.App.ViewModels;

/// <summary>One report table: its title, the experiment it came from and the rows as monospace text.</summary>
public sealed record ReportTableView(string Title, string Source, string Text);

/// <summary>The five mandatory report tables, each from the newest experiment of its kind in the database.</summary>
public sealed partial class ExperimentsViewModel : ViewModelBase
{
    public ObservableCollection<ReportTableView> Tables { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "Натисніть «Оновити», щоб прочитати результати експериментів з бази даних.";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Status = "Читання бази даних…";
        try
        {
            ReportData? data = await Task.Run(() =>
            {
                IMeasurementStore store = ArchiveFiles.OpenStore();
                return store.IsEnabled ? ReportData.Load(store) : null;
            });
            Tables.Clear();
            if (data is null)
            {
                Status = "База даних вимкнена: рядок підключення порожній (див. README).";
                return;
            }
            foreach (ReportTableView table in Build(data))
                Tables.Add(table);
            Status = "Таблиці побудовано з бази даних.";
        }
        catch (DatabaseException ex)
        {
            Status = $"База даних: {ex.Message}";
        }
    }

    private static IEnumerable<ReportTableView> Build(ReportData data)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        yield return View("1. Розбір потоку: розмір і час по групах", data.Parsing.Source,
            $"{"Група",5} {"Метод",-8} {"Було, Б",10} {"Стало, Б",10} {"Коеф.",7} {"Час, с",8}",
            data.Parsing.Rows.Select(r => string.Format(c, "{0,5} {1,-8} {2,10} {3,10} {4,7:F2} {5,8:F4}",
                r.Group, r.Method, r.OriginalBytes, r.EncodedBytes, r.Ratio, r.Seconds)));
        yield return View("2. Вплив кожного блоку тракту на Q", data.BlockImpact.Source,
            $"{"Вимкнено",-12} {"Архів, Б",10} {"Час, с",8} {"S",7} {"T",7} {"Q",7} {"ΔQ",8}",
            data.BlockImpact.Rows.Select(r => string.Format(c, "{0,-12} {1,10} {2,8:F3} {3,7:F3} {4,7:F3} {5,7:F3} {6,8:+0.000;-0.000;0}",
                r.DisabledBlock, r.ArchiveBytes, r.Seconds, r.S, r.T, r.Q, r.QChange ?? 0)));
        yield return View("3. ДП проти жадібного пакування першого тому", data.Packing.Source,
            $"{"Метод",-8} {"Місткість",10} {"Використано",12} {"Заповн.",8} {"Цінність",9} {"Блоків",7} {"Час, с",8}",
            data.Packing.Rows.Select(r => string.Format(c, "{0,-8} {1,10} {2,12} {3,7:F1}% {4,9:F0} {5,7} {6,8:F4}",
                r.Method, r.CapacityBytes, r.UsedBytes, r.FillPercent, r.TotalValue, r.BlockCount, r.Seconds)));
        yield return View("4. «Стиснути → зашифрувати» проти зворотного порядку", data.EncryptionOrder.Source,
            $"{"Група",5} {"Було, Б",10} {"Стисн.→шифр.",13} {"Шифр.→стисн.",13} {"Коеф. 1",8} {"Коеф. 2",8}",
            data.EncryptionOrder.Rows.Select(r => string.Format(c, "{0,5} {1,10} {2,13} {3,13} {4,8:F2} {5,8:F2}",
                r.Group, r.OriginalBytes, r.CompressThenEncryptBytes, r.EncryptThenCompressBytes,
                r.CompressThenEncryptRatio, r.EncryptThenCompressRatio)));
        yield return View("5. Збіжність ГА і випадкового пошуку", data.Convergence.Source,
            $"{"Алгоритм",-12} {"Покоління",9} {"Обчислень",9} {"Найкраще",9} {"Середнє",9} {"Краще досі",10}",
            data.Convergence.Rows.Select(r => string.Format(c, "{0,-12} {1,9} {2,9} {3,9:F4} {4,9:F4} {5,10:F4}",
                r.Algorithm, r.Generation, r.Evaluations, r.Best, r.Mean, r.BestSoFar)));
    }

    private static ReportTableView View(string title, ReportSource? source, string header, IEnumerable<string> rows)
    {
        if (source is null)
            return new ReportTableView(title, "У базі ще немає цього експерименту.", "");
        string from = string.Create(CultureInfo.InvariantCulture,
            $"Експеримент №{source.ExperimentId}, {source.StartedAtUtc:yyyy-MM-dd HH:mm} UTC, коміт {source.CommitHash ?? "—"}, дані {source.Dataset}");
        return new ReportTableView(title, from, string.Join(Environment.NewLine, rows.Prepend(header)));
    }
}
