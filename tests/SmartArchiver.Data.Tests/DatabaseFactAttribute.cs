namespace SmartArchiver.Data.Tests;

/// <summary>
/// A test that needs SQL Server. It is skipped unless SMARTARCHIVER_TEST_SQLSERVER holds a connection string
/// to a server where the tests may create and drop their own databases.
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public const string VariableName = "SMARTARCHIVER_TEST_SQLSERVER";

    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariableName)))
            Skip = $"Set {VariableName} to a SQL Server connection string to run database tests.";
    }
}
