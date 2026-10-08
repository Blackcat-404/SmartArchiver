using Microsoft.EntityFrameworkCore;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data;

/// <summary>
/// Database of measurement results. It lives next to the archiver and never touches the archive format.
/// </summary>
public sealed class SmartArchiverDbContext(DbContextOptions<SmartArchiverDbContext> options) : DbContext(options)
{
    public DbSet<MeasurementRun> MeasurementRuns => Set<MeasurementRun>();

    public DbSet<MeasurementFileResult> MeasurementFileResults => Set<MeasurementFileResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MeasurementRun>(run =>
        {
            run.Property(r => r.CommitHash).HasMaxLength(40).IsUnicode(false);
            run.Property(r => r.Dataset).HasMaxLength(100);
            run.HasMany(r => r.Files)
                .WithOne(f => f.Run)
                .HasForeignKey(f => f.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
