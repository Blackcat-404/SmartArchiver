using Microsoft.EntityFrameworkCore;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data.Tests;

public class MeasurementStoreTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_NoConnectionString_ReturnsDisabledStore(string? connectionString)
    {
        var store = MeasurementStore.Open(connectionString);

        Assert.False(store.IsEnabled);
        store.EnsureCreated();
        store.SaveRun(SampleRun());
        Assert.Equal(0, store.CountRuns());
    }

    [Fact]
    public void Open_MalformedConnectionString_ThrowsDatabaseException()
    {
        Assert.Throws<DatabaseException>(() => MeasurementStore.Open("this is not a connection string"));
    }

    [Fact]
    public void Open_ConnectionStringWithoutDatabase_ThrowsDatabaseException()
    {
        // Without Database=... SQL Server would put the tables into master.
        Assert.Throws<DatabaseException>(() => MeasurementStore.Open("Server=localhost;User Id=sa;Password=x"));
    }

    [Fact]
    public void Open_ConnectionString_DescriptionNamesDatabaseAndServerButNotPassword()
    {
        var store = MeasurementStore.Open("Server=db.local,1433;Database=Results;User Id=sa;Password=Secret_Password_42");

        Assert.True(store.IsEnabled);
        Assert.Contains("Results", store.Description);
        Assert.Contains("db.local,1433", store.Description);
        Assert.DoesNotContain("Secret_Password_42", store.Description);
    }

    [Fact]
    public void SaveRun_UnreachableServer_ThrowsDatabaseExceptionWithoutPassword()
    {
        var store = MeasurementStore.Open(
            "Server=127.0.0.1,1;Database=SmartArchiver;User Id=sa;Password=Secret_Password_42;Connect Timeout=2");

        var ex = Assert.Throws<DatabaseException>(() => store.SaveRun(SampleRun()));

        Assert.DoesNotContain("Secret_Password_42", ex.Message);
        Assert.Contains("SmartArchiver", ex.Message);
    }

    [DatabaseFact]
    public void EnsureCreated_NewDatabase_AppliesAllMigrations()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);

        store.EnsureCreated();

        using var db = database.CreateContext();
        Assert.Equal(db.Database.GetMigrations(), db.Database.GetAppliedMigrations());
        Assert.Equal(0, store.CountRuns());
    }

    [DatabaseFact]
    public void EnsureCreated_ExistingDatabase_KeepsSavedRuns()
    {
        using var database = new TestDatabase();
        MeasurementStore.Open(database.ConnectionString).SaveRun(SampleRun());

        var reopened = MeasurementStore.Open(database.ConnectionString);
        reopened.EnsureCreated();

        Assert.Equal(1, reopened.CountRuns());
    }

    [DatabaseFact]
    public void SaveRun_RunWithFiles_IsReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        var run = SampleRun();

        store.SaveRun(run);

        using var db = database.CreateContext();
        var saved = db.MeasurementRuns.Include(r => r.Files).Single();
        Assert.Equal(run.StartedAtUtc, saved.StartedAtUtc);
        Assert.Equal(run.CommitHash, saved.CommitHash);
        Assert.Equal(run.Seed, saved.Seed);
        Assert.Equal(run.Dataset, saved.Dataset);
        Assert.Equal(run.Alpha, saved.Alpha);
        Assert.Equal(run.Beta, saved.Beta);
        Assert.Equal(run.TRefSeconds, saved.TRefSeconds);
        Assert.Equal(run.ParametersJson, saved.ParametersJson);
        Assert.Equal(run.OriginalBytes, saved.OriginalBytes);
        Assert.Equal(run.ArchiveBytes, saved.ArchiveBytes);
        Assert.Equal(run.ElapsedSeconds, saved.ElapsedSeconds);
        Assert.Equal(run.AllRestored, saved.AllRestored);
        Assert.Equal(run.Q, saved.Q);
        Assert.Equal(
            run.Files.Select(f => (f.Group, f.OriginalBytes, f.ArchiveBytes, f.Restored)),
            saved.Files.OrderBy(f => f.Id).Select(f => (f.Group, f.OriginalBytes, f.ArchiveBytes, f.Restored)));
    }

    private static MeasurementRun SampleRun() => new()
    {
        StartedAtUtc = new DateTime(2026, 10, 8, 12, 30, 0, DateTimeKind.Utc),
        CommitHash = "05569e5c0ffee05569e5c0ffee05569e5c0ffee0",
        Seed = 42,
        Dataset = "corpus",
        Alpha = 0.7,
        Beta = 0.3,
        TRefSeconds = 10,
        ParametersJson = """{"blockSize":65536}""",
        OriginalBytes = 3000,
        ArchiveBytes = 3120,
        ElapsedSeconds = 0.25,
        AllRestored = true,
        Q = 0.6123,
        Files =
        [
            new() { Group = 0, OriginalBytes = 1000, ArchiveBytes = 1040, Restored = true },
            new() { Group = 4, OriginalBytes = 2000, ArchiveBytes = 2080, Restored = true },
        ],
    };
}
