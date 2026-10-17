using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data;

/// <summary>
/// Database of measurement results. It lives next to the archiver and never touches the archive format.
/// Keys, NOT NULL and CHECK constraints are declared here, so the database itself rejects impossible values.
/// </summary>
public sealed class SmartArchiverDbContext(DbContextOptions<SmartArchiverDbContext> options) : DbContext(options)
{
    /// <summary>file_type in FORMAT_SPEC_P2 is 0..6.</summary>
    private const int MaxFileType = 6;

    public DbSet<MeasurementRun> MeasurementRuns => Set<MeasurementRun>();

    public DbSet<MeasurementFileResult> MeasurementFileResults => Set<MeasurementFileResult>();

    public DbSet<Experiment> Experiments => Set<Experiment>();

    public DbSet<ParsingResult> ParsingResults => Set<ParsingResult>();

    public DbSet<PackingResult> PackingResults => Set<PackingResult>();

    public DbSet<TrainingRun> TrainingRuns => Set<TrainingRun>();

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
            run.ToTable(t =>
            {
                t.HasCheckConstraint("CK_MeasurementRuns_Sizes", "[OriginalBytes] >= 0 AND [ArchiveBytes] >= 0");
                t.HasCheckConstraint("CK_MeasurementRuns_Time", "[ElapsedSeconds] >= 0 AND [TRefSeconds] > 0");
                t.HasCheckConstraint("CK_MeasurementRuns_Weights", "[Alpha] >= 0 AND [Beta] >= 0");
                t.HasCheckConstraint("CK_MeasurementRuns_Q", "[Q] >= 0");
            });
        });

        modelBuilder.Entity<MeasurementFileResult>(file => file.ToTable(t =>
        {
            t.HasCheckConstraint("CK_MeasurementFileResults_Group", $"[Group] <= {MaxFileType}");
            t.HasCheckConstraint("CK_MeasurementFileResults_Sizes", "[OriginalBytes] >= 0 AND [ArchiveBytes] >= 0");
        }));

        modelBuilder.Entity<Experiment>(experiment =>
        {
            experiment.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
            experiment.Property(e => e.CommitHash).HasMaxLength(40).IsUnicode(false);
            experiment.Property(e => e.Dataset).HasMaxLength(100);
            experiment.HasIndex(e => new { e.Kind, e.StartedAtUtc });
            experiment.HasMany(e => e.ParsingResults).WithOne(r => r.Experiment)
                .HasForeignKey(r => r.ExperimentId).OnDelete(DeleteBehavior.Cascade);
            experiment.HasMany(e => e.PackingResults).WithOne(r => r.Experiment)
                .HasForeignKey(r => r.ExperimentId).OnDelete(DeleteBehavior.Cascade);
            experiment.HasMany(e => e.TrainingRuns).WithOne(r => r.Experiment)
                .HasForeignKey(r => r.ExperimentId).OnDelete(DeleteBehavior.Cascade);
            experiment.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Experiments_Kind", OneOf<ExperimentKind>("Kind"));
                t.HasCheckConstraint("CK_Experiments_Dataset", "LEN([Dataset]) > 0");
            });
        });

        modelBuilder.Entity<ParsingResult>(result =>
        {
            result.Property(r => r.Method).HasConversion<string>().HasMaxLength(20);
            result.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ParsingResults_Group", $"[Group] <= {MaxFileType}");
                t.HasCheckConstraint("CK_ParsingResults_Method", OneOf<ParsingMethod>("Method"));
                t.HasCheckConstraint("CK_ParsingResults_Sizes", "[OriginalBytes] >= 0 AND [EncodedBytes] >= 0");
                t.HasCheckConstraint("CK_ParsingResults_Time", "[ElapsedSeconds] >= 0");
            });
        });

        modelBuilder.Entity<PackingResult>(result =>
        {
            result.Property(r => r.Method).HasConversion<string>().HasMaxLength(20);
            result.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PackingResults_Method", OneOf<PackingMethod>("Method"));
                t.HasCheckConstraint("CK_PackingResults_Volume", "[VolumeIndex] >= 0 AND [BlockCount] >= 0");
                t.HasCheckConstraint("CK_PackingResults_Capacity", "[UsedBytes] >= 0 AND [UsedBytes] <= [CapacityBytes]");
                t.HasCheckConstraint("CK_PackingResults_Value", "[TotalValue] >= 0 AND [ElapsedSeconds] >= 0");
            });
        });

        modelBuilder.Entity<TrainingRun>(run =>
        {
            run.Property(r => r.LayerSizes).HasMaxLength(200).IsUnicode(false);
            run.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TrainingRuns_Training", "[Epochs] > 0 AND [LearningRate] > 0 AND [TrainLoss] >= 0");
                t.HasCheckConstraint("CK_TrainingRuns_MacroF1",
                    "[TrainMacroF1] BETWEEN 0 AND 1 AND [ValidMacroF1] BETWEEN 0 AND 1");
                t.HasCheckConstraint("CK_TrainingRuns_LayerSizes", "LEN([LayerSizes]) > 0");
            });
        });
    }

    /// <summary>CHECK that an enum stored as text holds one of its names; built from the enum so they cannot drift apart.</summary>
    private static string OneOf<TEnum>(string column) where TEnum : struct, Enum =>
        $"[{column}] IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"))})";
}
