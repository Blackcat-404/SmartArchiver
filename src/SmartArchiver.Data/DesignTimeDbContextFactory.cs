using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartArchiver.Data;

/// <summary>
/// Used only by "dotnet ef migrations add": builds the model without connecting to a server.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmartArchiverDbContext>
{
    public SmartArchiverDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SmartArchiverDbContext>()
            .UseSqlServer("Server=localhost;Database=SmartArchiver;Integrated Security=true")
            .Options);
}
