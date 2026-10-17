namespace SmartArchiver.Data.Tests;

/// <summary>A parameterised test that needs SQL Server; skipped like <see cref="DatabaseFactAttribute"/>.</summary>
public sealed class DatabaseTheoryAttribute : TheoryAttribute
{
    public DatabaseTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DatabaseFactAttribute.VariableName)))
            Skip = $"Set {DatabaseFactAttribute.VariableName} to a SQL Server connection string to run database tests.";
    }
}
