using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartArchiver.Analysis;
using SmartArchiver.App.Services;
using SmartArchiver.Core;

namespace SmartArchiver.App.ViewModels;

/// <summary>Features of a file, its type from the classifier and the compression strategy the archive would choose.</summary>
public sealed partial class AnalyzeViewModel : ViewModelBase
{
    public ObservableCollection<string> Features { get; } = [];

    [ObservableProperty]
    public partial string FilePath { get; set; } = "";

    [ObservableProperty]
    public partial string Kind { get; set; } = "";

    [ObservableProperty]
    public partial string Strategy { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "Оберіть файл для аналізу.";

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            Status = "Оберіть файл для аналізу.";
            return;
        }
        if (!File.Exists(FilePath))
        {
            Status = $"Файл не знайдено: {FilePath}";
            return;
        }
        string path = FilePath;
        try
        {
            (byte[] data, FileFeatures features, FileAnalysis analysis) = await Task.Run(() =>
            {
                byte[] bytes = File.ReadAllBytes(path);
                return (bytes, FeatureExtractor.Compute(bytes), new FileAnalyzer().Analyze(bytes));
            });
            CultureInfo c = CultureInfo.InvariantCulture;
            Features.Clear();
            Features.Add(string.Format(c, "Розмір:                   {0} Б", data.Length));
            Features.Add(string.Format(c, "Ентропія байтів:          {0:F3} біт із 8", features.Entropy));
            Features.Add(string.Format(c, "Ентропія пар байтів:      {0:F3} біт із 16", features.BigramEntropy));
            Features.Add(string.Format(c, "Друковані символи ASCII:  {0:P1}", features.PrintableAsciiFraction));
            Features.Add(string.Format(c, "Середня довжина рядка:    {0:F1} символів", features.AverageLineLength));
            Features.Add("Автокореляція, лаги 1-16: " + string.Join(" ", features.Autocorrelation.Select(r => r.ToString("+0.00;-0.00; 0.00", c))));
            Kind = string.Format(c, "{0} = {1}, впевненість {2:P0}", (byte)analysis.Kind, ArchiveFiles.KindName(analysis.Kind), analysis.Confidence);
            Strategy = ArchiveFiles.StrategyName(analysis.Strategy);
            Status = "Готово.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Проблема з файлом: {ex.Message}";
        }
    }
}
