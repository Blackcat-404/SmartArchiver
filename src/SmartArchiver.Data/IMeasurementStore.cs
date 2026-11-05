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

    /// <summary>Saves the experiment together with all its results in one transaction.</summary>
    void SaveExperiment(Experiment experiment);

    /// <summary>The experiment with all its results, or null if there is none with this id.</summary>
    Experiment? GetExperiment(int id);

    /// <summary>Experiments of one kind without their results, oldest first.</summary>
    IReadOnlyList<Experiment> ListExperiments(ExperimentKind kind);

    /// <summary>Adds one pack or unpack to the archive history.</summary>
    void SaveArchiveOperation(ArchiveOperation operation);

    /// <summary>The last <paramref name="count"/> operations of the archive history, newest first.</summary>
    IReadOnlyList<ArchiveOperation> ListArchiveOperations(int count);
}
