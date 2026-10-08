using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using RTelemetry.Contracts;
using RTelemetry.Server;
using RTelemetry.Server.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.AddOptions<TelemetryServerOptions>()
    .Bind(builder.Configuration.GetSection(TelemetryServerOptions.SectionName))
    .Validate(o => o.RetentionDays > 0 && o.RateLimit.RequestsPerMinutePerIp > 0 && o.RateLimit.RequestsPerMinutePerProject > 0,
        "Retention and rate limits must be positive").ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
var storage = builder.Configuration.GetSection("RTelemetry:Storage").Get<StorageOptions>() ?? new();
if (storage.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContextFactory<TelemetryDbContext>(o => o.UseNpgsql(storage.ConnectionString));
    builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
}
else if (storage.Provider.Equals("JsonLines", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IEventStore, JsonLinesEventStore>();
else throw new InvalidOperationException("Unknown RTelemetry:Storage:Provider");
builder.Services.AddHostedService<RetentionService>();
builder.Services.AddSingleton<IngestRateLimiter>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, _) => { context.HttpContext.Response.Headers.RetryAfter = "60"; return ValueTask.CompletedTask; };
    o.AddPolicy("ingest", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IOptions<TelemetryServerOptions>>().Value.RateLimit.RequestsPerMinutePerIp,
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Только доверенные loopback-прокси из настроек ASP.NET по умолчанию.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
builder.Services.AddRazorPages();
var app = builder.Build();
if (storage.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    await using var db = await app.Services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
}
app.UseForwardedHeaders();
app.UseMiddleware<DashboardAuthMiddleware>();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/v1/batches" && context.Request.ContentLength > 1024 * 1024)
    { context.Response.StatusCode = 413; return; }
    await next(context);
});
app.MapPost("/" + TelemetryProtocol.BatchesPath, IngestEndpoint.HandleAsync).RequireRateLimiting("ingest");
app.MapDelete("/v1/admin/installs/{installId:guid}", async (Guid installId, HttpContext context,
    IOptions<TelemetryServerOptions> options, IEventStore store, CancellationToken cancellationToken) =>
{
    if (!IngestEndpoint.KeyMatches(context.Request.Headers["X-RTelemetry-Admin-Key"], options.Value.AdminKey))
        return Results.Unauthorized();
    var deleted = await store.DeleteInstallAsync(installId, cancellationToken);
    return Results.Ok(new { deleted });
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRazorPages();
app.Run();
public partial class Program;
