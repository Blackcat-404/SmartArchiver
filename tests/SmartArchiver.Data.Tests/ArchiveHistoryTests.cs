using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data.Tests;

public class ArchiveHistoryTests
{
    private static ArchiveOperation Pack(DateTime startedAtUtc) => new()
    {
        StartedAtUtc = startedAtUtc,
        Operation = ArchiveOperationKind.Pack,
        FileCount = 3,
        CompressedFiles = 2,
        OriginalBytes = 150_000,
        ArchiveBytes = 48_000,
        VolumeCount = 2,
        ElapsedSeconds = 0.4,
        Succeeded = true,
    };

    private static ArchiveOperation FailedUnpack(DateTime startedAtUtc) => new()
    {
        StartedAtUtc = startedAtUtc,
        Operation = ArchiveOperationKind.Unpack,
        ArchiveBytes = 48_000,
        VolumeCount = 2,
        ElapsedSeconds = 0.05,
        Succeeded = false,
        ErrorCode = "E_HMAC",
    };

    private static readonly DateTime Noon = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ArchiveOperation_HasNoTextBesidesTheErrorCode()
    {
        // History must not keep file names, paths, passwords or keys: there is simply no column that could hold them.
        Assert.Equal(
            [nameof(ArchiveOperation.ErrorCode)],
            typeof(ArchiveOperation).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => p.Name));
    }

    [Fact]
    public void DisabledStore_AcceptsHistoryAndReturnsNothing()
    {
        IMeasurementStore store = MeasurementStore.Open("");

        store.SaveArchiveOperation(Pack(Noon));

        Assert.Empty(store.ListArchiveOperations(10));
    }

    [DatabaseFact]
    public void SaveArchiveOperation_IsReadBackUnchanged()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        ArchiveOperation pack = Pack(Noon), unpack = FailedUnpack(Noon.AddMinutes(1));

        store.SaveArchiveOperation(pack);
        store.SaveArchiveOperation(unpack);

        Assert.Equal(
            new[] { unpack, pack }.Select(Fields),
            store.ListArchiveOperations(10).Select(Fields));
    }

    [DatabaseFact]
    public void ListArchiveOperations_NewestFirst_AtMostTheGivenCount()
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        foreach (int minutes in new[] { 5, 1, 9, 3 })
            store.SaveArchiveOperation(Pack(Noon.AddMinutes(minutes)));

        Assert.Equal(
            [Noon.AddMinutes(9), Noon.AddMinutes(5), Noon.AddMinutes(3)],
            store.ListArchiveOperations(3).Select(o => o.StartedAtUtc));
    }

    [Fact]
    public void ListArchiveOperations_CountBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementStore.Open("").ListArchiveOperations(0));
    }

    [DatabaseTheory]
    [InlineData("negative original size")]
    [InlineData("negative archive size")]
    [InlineData("negative file count")]
    [InlineData("more compressed files than files")]
    [InlineData("negative volume count")]
    [InlineData("negative time")]
    [InlineData("success with error code")]
    [InlineData("failure without error code")]
    [InlineData("unknown operation")]
    public void SaveArchiveOperation_ValueOutsideConstraint_IsRejected(string violation)
    {
        using var database = new TestDatabase();
        var store = MeasurementStore.Open(database.ConnectionString);
        ArchiveOperation operation = Pack(Noon);
        switch (violation)
        {
            case "negative original size": operation.OriginalBytes = -1; break;
            case "negative archive size": operation.ArchiveBytes = -1; break;
            case "negative file count": operation.FileCount = -1; break;
            case "more compressed files than files": operation.CompressedFiles = 4; break;
            case "negative volume count": operation.VolumeCount = -1; break;
            case "negative time": operation.ElapsedSeconds = -0.1; break;
            case "success with error code": operation.ErrorCode = "E_HMAC"; break;
            case "failure without error code": operation.Succeeded = false; break;
            case "unknown operation": operation.Operation = (ArchiveOperationKind)7; break;
            default: throw new ArgumentOutOfRangeException(nameof(violation));
        }

        Assert.Throws<DatabaseException>(() => store.SaveArchiveOperation(operation));
        Assert.Empty(store.ListArchiveOperations(10));
    }

    private static (DateTime, ArchiveOperationKind, int, int, long, long, int, double, bool, string?) Fields(ArchiveOperation o) =>
        (o.StartedAtUtc, o.Operation, o.FileCount, o.CompressedFiles, o.OriginalBytes, o.ArchiveBytes, o.VolumeCount,
            o.ElapsedSeconds, o.Succeeded, o.ErrorCode);
}
