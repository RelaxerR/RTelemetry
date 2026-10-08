using Microsoft.EntityFrameworkCore;

namespace RTelemetry.Server.Storage;

public sealed class EventRow
{
    public Guid Id { get; set; }
    public string Project { get; set; } = "";
    public Guid InstallId { get; set; }
    public Guid SessionId { get; set; }
    public long Sequence { get; set; }
    public string Name { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string? ContentVersion { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options)
{
    public DbSet<EventRow> Events => Set<EventRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<EventRow>();
        e.ToTable("Events");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Payload).HasColumnType("jsonb");
        e.HasIndex(x => new { x.Project, x.ReceivedAtUtc, x.AppVersion, x.ContentVersion });
        e.HasIndex(x => new { x.Project, x.Name, x.TimestampUtc });
        e.HasIndex(x => new { x.Project, x.SessionId, x.Sequence });
        e.HasIndex(x => x.InstallId);
        e.HasIndex(x => x.ReceivedAtUtc);
    }
}
