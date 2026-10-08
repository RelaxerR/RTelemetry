using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RTelemetry.Contracts;
using RTelemetry.Server.Storage;

namespace RTelemetry.Server.Tests;

public sealed class ServerFactory(int ipLimit = 120, int projectLimit = 600) : WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rtelemetry-server-test-" + Guid.NewGuid());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RTelemetry:DataDirectory"] = _directory,
            ["RTelemetry:Storage:Provider"] = "JsonLines",
            ["RTelemetry:Projects:test:ApiKey"] = "test-key",
            ["RTelemetry:AdminKey"] = "admin-test-key",
            ["RTelemetry:Dashboard:User"] = "test",
            ["RTelemetry:Dashboard:Password"] = "test-password",
            ["RTelemetry:RateLimit:RequestsPerMinutePerIp"] = ipLimit.ToString(),
            ["RTelemetry:RateLimit:RequestsPerMinutePerProject"] = projectLimit.ToString()
        }));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
    public HttpClient IngestClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TelemetryProtocol.ApiKeyHeader, "test-key");
        return client;
    }
    public HttpClient DashboardClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("test:test-password")));
        return client;
    }
}

public sealed class ServerTests
{
    internal static TelemetryBatch Batch(string version = "1.0", long sequence = 1) => new()
    {
        Project = "test", InstallId = Guid.NewGuid(), Platform = "test",
        Events = [new() { Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), Name = "ui.miss",
            Sequence = sequence, TimestampUtc = DateTimeOffset.UtcNow, AppVersion = version, ContentVersion = "c1",
            Props = new() { ["page"] = JsonSerializer.SerializeToElement("home"), ["x"] = JsonSerializer.SerializeToElement(0.25),
                ["y"] = JsonSerializer.SerializeToElement(0.5), ["nearest_element"] = JsonSerializer.SerializeToElement("button") } }]
    };

    [Fact]
    public async Task IngestDashboardFiltersCsvAndDelete()
    {
        using var factory = new ServerFactory();
        using var ingest = factory.IngestClient();
        var batch = Batch();
        Assert.Equal(HttpStatusCode.OK, (await ingest.PostAsJsonAsync("/v1/batches", batch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ingest.PostAsJsonAsync("/v1/batches", Batch("2.0"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/")).StatusCode);
        using var dashboard = factory.DashboardClient();
        var html = await dashboard.GetStringAsync("/?project=test&appVersion=1.0&contentVersion=c1");
        Assert.Contains("ui.miss", html);
        Assert.Contains("translate(250 500)", html);
        Assert.Contains("button", html);
        var csv = await dashboard.GetStringAsync("/?handler=Csv&appVersion=1.0");
        Assert.Contains(batch.Events[0].Id.ToString(), csv);
        Assert.DoesNotContain("\"2.0\"", csv);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ingest.DeleteAsync($"/v1/admin/installs/{batch.InstallId}")).StatusCode);
        ingest.DefaultRequestHeaders.Add("X-RTelemetry-Admin-Key", "admin-test-key");
        Assert.Equal(HttpStatusCode.OK, (await ingest.DeleteAsync($"/v1/admin/installs/{batch.InstallId}")).StatusCode);
        Assert.DoesNotContain(batch.Events[0].Id.ToString(), await dashboard.GetStringAsync("/?handler=Csv"));
    }

    [Fact]
    public async Task RejectsInvalidKeyInvalidBodyAndLargeBody()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/batches", Batch())).StatusCode);
        using var ingest = factory.IngestClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await ingest.PostAsJsonAsync("/v1/batches", Batch() with { Project = "../escape" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ingest.PostAsync("/v1/batches", new StringContent("{", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ingest.PostAsync("/v1/batches", new StringContent(new string(' ', 1024 * 1024 + 1), Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(100, 1)]
    public async Task RateLimitsIpAndProjectSeparately(int ipLimit, int projectLimit)
    {
        using var factory = new ServerFactory(ipLimit, projectLimit);
        using var client = factory.IngestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/batches", Batch())).StatusCode);
        var response = await client.PostAsJsonAsync("/v1/batches", Batch());
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task CsvEscapesFormulaAndQuotesAndSessionUsesSequence()
    {
        using var factory = new ServerFactory();
        using var client = factory.IngestClient();
        var batch = Batch("=1+1", 2);
        batch.Events.Add(batch.Events[0] with { Id = Guid.NewGuid(), Sequence = 1, Name = "first.event" });
        await client.PostAsJsonAsync("/v1/batches", batch);
        using var dashboard = factory.DashboardClient();
        var csv = await dashboard.GetStringAsync("/?handler=Csv");
        Assert.Contains("\"'=1+1\"", csv);
        Assert.Contains("\"\"page\"\"", csv);
        var html = await dashboard.GetStringAsync($"/?session={batch.Events[0].SessionId}");
        var timeline = html[html.IndexOf("Лента сессии", StringComparison.Ordinal)..];
        Assert.True(timeline.IndexOf("first.event", StringComparison.Ordinal) < timeline.IndexOf("ui.miss", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, (await dashboard.GetAsync("/?from=2026-10-07&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dashboard.GetAsync("/?to=0001-01-01")).StatusCode);
    }

    [Fact]
    public async Task RetentionUsesReceiptTimeAndKeepsRecentEvents()
    {
        using var factory = new ServerFactory();
        _ = factory.CreateClient();
        var store = factory.Services.GetRequiredService<IEventStore>();
        var batch = Batch();
        var now = DateTimeOffset.UtcNow;
        await store.AppendAsync([StoredEvent.From(batch, batch.Events[0], now.AddDays(-100)),
            StoredEvent.From(batch, batch.Events[0] with { Id = Guid.NewGuid() }, now)], default);
        Assert.Equal(1, await store.DeleteBeforeAsync(now.AddDays(-90), default));
        Assert.Equal(1, await store.DeleteInstallAsync(batch.InstallId, default));
        Assert.Equal(0, await store.DeleteInstallAsync(batch.InstallId, default));
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RTELEMETRY_TEST_POSTGRES")))
            Skip = "Set RTELEMETRY_TEST_POSTGRES to an isolated PostgreSQL database connection string";
    }
}

public sealed class PostgresTests
{
    [PostgresFact]
    public async Task MigrationsConcurrentDedupQueryAndDeletion()
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("RTELEMETRY_TEST_POSTGRES")).Options;
        await using var db = new TelemetryDbContext(options);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var store = new PostgresEventStore(new Factory(options));
        var batch = ServerTests.Batch();
        var now = DateTimeOffset.UtcNow;
        var e = StoredEvent.From(batch, batch.Events[0], now);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => store.AppendAsync([e], default)));
            Assert.Equal(1, await db.Events.CountAsync(x => x.Id == e.Id));
            var rows = new List<StoredEvent>();
            await foreach (var item in store.ReadAsync("test", DateOnly.FromDateTime(now.UtcDateTime), DateOnly.FromDateTime(now.UtcDateTime), default)) rows.Add(item);
            Assert.Contains(rows, x => x.Id == e.Id && x.Props!["page"].GetString() == "home");
            Assert.Equal(1, await store.DeleteInstallAsync(e.InstallId, default));
            var old = e with { Id = Guid.NewGuid(), ReceivedAtUtc = now.AddDays(-100) };
            await store.AppendAsync([old, e], default);
            Assert.True(await store.DeleteBeforeAsync(now.AddDays(-90), default) >= 1);
            Assert.True(await db.Events.AnyAsync(x => x.Id == e.Id));
        }
        finally { await store.DeleteInstallAsync(e.InstallId, default); }
    }
    private sealed class Factory(DbContextOptions<TelemetryDbContext> options) : IDbContextFactory<TelemetryDbContext>
    {
        public TelemetryDbContext CreateDbContext() => new(options);
    }
}
