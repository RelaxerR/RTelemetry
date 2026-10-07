using RTelemetry.Contracts;
using RTelemetry.Server;
using RTelemetry.Server.Storage;

var builder = WebApplication.CreateBuilder(args);

// Пачка до 500 событий по 32 коротких свойства укладывается в мегабайт с запасом.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1024 * 1024);

builder.Services.Configure<TelemetryServerOptions>(builder.Configuration.GetSection(TelemetryServerOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEventStore, JsonLinesEventStore>();
builder.Services.AddRazorPages();

var app = builder.Build();

app.UseMiddleware<DashboardAuthMiddleware>();

app.MapPost("/" + TelemetryProtocol.BatchesPath, IngestEndpoint.HandleAsync);
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRazorPages();

app.Run();

/// <summary>Точка входа, доступная интеграционным тестам (WebApplicationFactory).</summary>
public partial class Program;
