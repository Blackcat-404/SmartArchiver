using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data;

/// <summary>
/// Storage for measurement results. Get one from <see cref="MeasurementStore.Open"/>.
/// </summary>
public interface IMeasurementStore
{
    /// <summary>False when no connection string is configured: the store accepts calls and writes nothing.</summary>
    bool IsEnabled { get; }

    /// <summary>Database and server for messages. Never contains the password.</summary>
    string Description { get; }

    /// <summary>Creates the database and applies pending migrations. Other methods call it on first use.</summary>
    void EnsureCreated();

    /// <summary>Saves the run together with its file results in one transaction.</summary>
    void SaveRun(MeasurementRun run);

    int CountRuns();
}
