using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SmartArchiver.Data.Tests;

/// <summary>
/// A database with a unique name on the test server. It does not exist until a store creates it
/// and is dropped on dispose, so tests never share data.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public string ConnectionString { get; }

    public TestDatabase()
    {
        var server = Environment.GetEnvironmentVariable(DatabaseFactAttribute.VariableName)
            ?? throw new InvalidOperationException($"{DatabaseFactAttribute.VariableName} is not set.");
        ConnectionString = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = $"SmartArchiverTests_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    public SmartArchiverDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SmartArchiverDbContext>().UseSqlServer(ConnectionString).Options);

    public void Dispose()
    {
        using var db = CreateContext();
        db.Database.EnsureDeleted();
    }
}
