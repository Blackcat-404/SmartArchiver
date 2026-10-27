using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data.Tests;

/// <summary>The database built from the migrations must be exactly the model: every table, CHECK, key and unique index.</summary>
public class SchemaTests
{
    [Fact]
    public void Model_HasNoChangesMissingFromTheMigrations()
    {
        // Runs without a server: a change to the model without "dotnet ef migrations add" fails here, not at first start.
        using SmartArchiverDbContext db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [DatabaseFact]
    public void EnsureCreated_NewDatabase_HasEveryTableCheckAndUniqueIndexOfTheModel()
    {
        using var database = new TestDatabase();
        MeasurementStore.Open(database.ConnectionString).EnsureCreated();
        using var db = database.CreateContext();
        // CHECK constraints are kept only in the design-time model, not in the one used at run time.
        IEntityType[] entities = [.. db.GetService<IDesignTimeModel>().Model.GetEntityTypes()];

        Assert.Equal(
            entities.Select(e => e.GetTableName()!).Order(StringComparer.Ordinal),
            Names(db, "SELECT name AS Value FROM sys.tables WHERE name <> '__EFMigrationsHistory'"));
        Assert.Equal(
            entities.SelectMany(e => e.GetCheckConstraints()).Select(c => c.Name!).Order(StringComparer.Ordinal),
            Names(db, "SELECT name AS Value FROM sys.check_constraints"));
        Assert.Equal(
            entities.SelectMany(e => e.GetForeignKeys()).Select(k => k.GetConstraintName()!).Order(StringComparer.Ordinal),
            Names(db, "SELECT name AS Value FROM sys.foreign_keys"));
        Assert.Equal(
            entities.SelectMany(e => e.GetIndexes()).Where(i => i.IsUnique).Select(i => i.GetDatabaseName()!).Order(StringComparer.Ordinal),
            Names(db, "SELECT i.name AS Value FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id "
                + "WHERE i.is_unique = 1 AND i.is_primary_key = 0"));
    }

    [DatabaseFact]
    public void Migrations_CanBeRevertedToAnEmptyDatabaseAndAppliedAgain()
    {
        // Every Down() runs, so a migration can be undone without leaving half a schema behind.
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        store.EnsureCreated();
        using var db = database.CreateContext();
        IMigrator migrator = db.GetService<IMigrator>();

        migrator.Migrate(Migration.InitialDatabase);
        Assert.Empty(Names(db, "SELECT name AS Value FROM sys.tables WHERE name <> '__EFMigrationsHistory'"));
        Assert.Empty(db.Database.GetAppliedMigrations());

        migrator.Migrate();
        Assert.Equal(db.Database.GetMigrations(), db.Database.GetAppliedMigrations());
        MeasurementStore.Open(database.ConnectionString).SaveArchiveOperation(new ArchiveOperation
        {
            StartedAtUtc = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc),
            Operation = ArchiveOperationKind.Pack,
            Succeeded = true,
        });
    }

    [DatabaseFact]
    public void DeletingAnExperiment_DeletesItsResults()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        var kept = new Experiment { Kind = ExperimentKind.Genetic, StartedAtUtc = DateTime.UtcNow, Dataset = "corpus", GeneticIndividuals = [Individual(0)] };
        var deleted = new Experiment { Kind = ExperimentKind.Genetic, StartedAtUtc = DateTime.UtcNow, Dataset = "corpus", GeneticIndividuals = [Individual(0), Individual(1)] };
        store.SaveExperiment(kept);
        store.SaveExperiment(deleted);

        using (var db = database.CreateContext())
        {
            db.Experiments.Remove(db.Experiments.Single(e => e.Id == deleted.Id));
            db.SaveChanges();
        }

        using var check = database.CreateContext();
        Assert.Equal([kept.Id], check.GeneticIndividuals.Select(i => i.ExperimentId));
    }

    [DatabaseFact]
    public void ResultWithoutExperiment_IsRejected()
    {
        using var database = new TestDatabase();
        MeasurementStore.Open(database.ConnectionString).EnsureCreated();
        using var db = database.CreateContext();
        GeneticIndividual orphan = Individual(0);
        orphan.ExperimentId = 12345;
        db.GeneticIndividuals.Add(orphan);

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    private static GeneticIndividual Individual(int position) => new()
    {
        Algorithm = SearchAlgorithm.Genetic,
        Generation = 0,
        Individual = position,
        GenesJson = """{"window":4096}""",
        Fitness = 0.5,
        S = 0.5,
        T = 0.5,
    };

    private static List<string> Names(SmartArchiverDbContext db, string sql) =>
        [.. db.Database.SqlQueryRaw<string>(sql).AsEnumerable().Order(StringComparer.Ordinal)];
}
