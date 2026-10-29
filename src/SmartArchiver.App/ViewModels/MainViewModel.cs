namespace SmartArchiver.App.ViewModels;

/// <summary>The window: one tab per screen.</summary>
public sealed class MainViewModel : ViewModelBase
{
    public PackViewModel Pack { get; } = new();

    public UnpackViewModel Unpack { get; } = new();

    public AnalyzeViewModel Analyze { get; } = new();

    public HistoryViewModel History { get; } = new();

    public ExperimentsViewModel Experiments { get; } = new();
}
