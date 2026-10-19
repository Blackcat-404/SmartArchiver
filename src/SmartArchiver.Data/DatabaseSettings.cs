using Microsoft.Extensions.Configuration;

namespace SmartArchiver.Data;

/// <summary>
/// Reads the connection string from appsettings.json (in git, empty) and appsettings.Local.json
/// (ignored by git, personal). The local file wins.
/// </summary>
public static class DatabaseSettings
{
    public const string ConnectionStringName = "SmartArchiver";

    /// <exception cref="DatabaseException">A settings file is not valid JSON.</exception>
    public static string? ReadConnectionString(string directory)
    {
        try
        {
            return new ConfigurationBuilder()
                .SetBasePath(directory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Local.json", optional: true)
                .Build()
                .GetConnectionString(ConnectionStringName);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException)
        {
            // The message names the file; the JSON parser does not quote its content.
            throw new DatabaseException($"Cannot read the database settings: {ex.Message}", ex);
        }
    }
}
