using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SmartArchiver.App.ViewModels;

namespace SmartArchiver.App.Views;

/// <summary>The window only opens file dialogs; everything else is in the view models.</summary>
public partial class MainWindow : Window
{
    private static readonly FilePickerFileType Archives = new("Архіви SmartArchiver") { Patterns = ["*.arc"] };

    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Model => (MainViewModel)DataContext!;

    private async void AddPackFiles(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = "Файли для архіву", AllowMultiple = true });
        Model.Pack.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private async void ChooseArchiveToWrite(object? sender, RoutedEventArgs e)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Куди зберегти архів",
            SuggestedFileName = "archive.arc",
            DefaultExtension = "arc",
            FileTypeChoices = [Archives],
        });
        if (file?.TryGetLocalPath() is string path)
            Model.Pack.ArchivePath = path;
    }

    private async void ChooseArchiveToRead(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Архів або будь-який його том", [Archives, FilePickerFileTypes.All]) is string path)
            Model.Unpack.ArchivePath = path;
    }

    private async void ChooseOutputFolder(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Тека для розпакованих файлів" });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is string path)
            Model.Unpack.OutputFolder = path;
    }

    private async void ChooseFileToAnalyze(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Файл для аналізу", null) is string path)
            Model.Analyze.FilePath = path;
    }

    private async Task<string?> PickFile(string title, IReadOnlyList<FilePickerFileType>? types)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = title, FileTypeFilter = types });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
