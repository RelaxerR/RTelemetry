using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RTelemetry.Server.Storage;

public sealed class TelemetryDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("RTelemetry__Storage__ConnectionString")
            ?? "Host=localhost;Database=rtelemetry";
        return new(new DbContextOptionsBuilder<TelemetryDbContext>().UseNpgsql(connection).Options);
    }
}
