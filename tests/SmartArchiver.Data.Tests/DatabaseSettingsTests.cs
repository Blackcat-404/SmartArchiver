using System.Text.Json;

namespace SmartArchiver.Data.Tests;

public sealed class DatabaseSettingsTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("smartarchiver-settings-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void ReadConnectionString_NoSettingsFiles_ReturnsNull()
    {
        Assert.Null(DatabaseSettings.ReadConnectionString(directory));
    }

    [Fact]
    public void ReadConnectionString_OnlySharedFile_ReturnsItsValue()
    {
        WriteSettings("appsettings.json", "Server=shared;Database=A");

        Assert.Equal("Server=shared;Database=A", DatabaseSettings.ReadConnectionString(directory));
    }

    [Fact]
    public void ReadConnectionString_LocalFile_OverridesSharedFile()
    {
        WriteSettings("appsettings.json", "");
        WriteSettings("appsettings.Local.json", "Server=local;Database=B");

        Assert.Equal("Server=local;Database=B", DatabaseSettings.ReadConnectionString(directory));
    }

    [Fact]
    public void ReadConnectionString_MalformedJson_ThrowsDatabaseExceptionNamingTheFile()
    {
        File.WriteAllText(Path.Combine(directory, "appsettings.Local.json"), "{ not json");

        var ex = Assert.Throws<DatabaseException>(() => DatabaseSettings.ReadConnectionString(directory));

        Assert.Contains("appsettings.Local.json", ex.Message);
    }

    private void WriteSettings(string fileName, string connectionString) =>
        File.WriteAllText(
            Path.Combine(directory, fileName),
            JsonSerializer.Serialize(new { ConnectionStrings = new { SmartArchiver = connectionString } }));
}
