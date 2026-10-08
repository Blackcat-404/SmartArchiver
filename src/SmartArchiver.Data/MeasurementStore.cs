using Microsoft.Data.SqlClient;

namespace SmartArchiver.Data;

public static class MeasurementStore
{
    /// <summary>
    /// Opens the store for a connection string. An empty string turns the database off, so the archiver
    /// works without SQL Server. Nothing connects to the server until the first call on the store.
    /// </summary>
    /// <exception cref="DatabaseException">The connection string is malformed or does not name a database.</exception>
    public static IMeasurementStore Open(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return DisabledMeasurementStore.Instance;

        SqlConnectionStringBuilder connection;
        try
        {
            connection = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            // The inner message is left out: it may quote a part of the string.
            throw new DatabaseException("The database connection string is malformed.", ex);
        }

        if (string.IsNullOrWhiteSpace(connection.InitialCatalog))
            throw new DatabaseException("The database connection string must name the database (Database=...).");

        return new SqlMeasurementStore(connection);
    }
}
