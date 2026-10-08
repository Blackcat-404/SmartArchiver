using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data;

/// <summary>Used when no connection string is configured.</summary>
internal sealed class DisabledMeasurementStore : IMeasurementStore
{
    public static readonly DisabledMeasurementStore Instance = new();

    private DisabledMeasurementStore()
    {
    }

    public bool IsEnabled => false;

    public string Description => "no database (the connection string is empty)";

    public void EnsureCreated()
    {
    }

    public void SaveRun(MeasurementRun run) => ArgumentNullException.ThrowIfNull(run);

    public int CountRuns() => 0;
}
