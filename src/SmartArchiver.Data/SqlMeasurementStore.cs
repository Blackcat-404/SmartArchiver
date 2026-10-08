using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data;

/// <summary>
/// SQL Server store. Each call uses a short-lived context; the schema is migrated on the first call.
/// </summary>
internal sealed class SqlMeasurementStore : IMeasurementStore
{
    private readonly DbContextOptions<SmartArchiverDbContext> options;
    private bool created;

    public SqlMeasurementStore(SqlConnectionStringBuilder connection)
    {
        Description = $"database '{connection.InitialCatalog}' on '{connection.DataSource}'";
        options = new DbContextOptionsBuilder<SmartArchiverDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .Options;
    }

    public bool IsEnabled => true;

    public string Description { get; }

    public void EnsureCreated()
    {
        if (created)
            return;
        Execute(db => db.Database.Migrate());
        created = true;
    }

    public void SaveRun(MeasurementRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        EnsureCreated();
        // A single SaveChanges runs in one transaction: the run and its files are saved together or not at all.
        Execute(db =>
        {
            db.MeasurementRuns.Add(run);
            db.SaveChanges();
        });
    }

    public int CountRuns()
    {
        EnsureCreated();
        return Execute(db => db.MeasurementRuns.Count());
    }

    private void Execute(Action<SmartArchiverDbContext> action) =>
        Execute(db =>
        {
            action(db);
            return 0;
        });

    private T Execute<T>(Func<SmartArchiverDbContext, T> action)
    {
        try
        {
            using var db = new SmartArchiverDbContext(options);
            return action(db);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            // SQL Server messages do not include the connection string, so the password stays out.
            throw new DatabaseException($"Cannot use {Description}: {ex.GetBaseException().Message}", ex);
        }
    }
}
