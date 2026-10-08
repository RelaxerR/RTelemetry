using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using RTelemetry.Server.Storage;

namespace RTelemetry.Server.Pages;

public sealed class IndexModel(IEventStore store, TimeProvider time) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Project { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? AppVersion { get; set; }
    [BindProperty(SupportsGet = true)] public string? ContentVersion { get; set; }
    [BindProperty(SupportsGet = true)] public string? Screen { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Session { get; set; }
    public List<StoredEvent> Events { get; } = [];
    public IEnumerable<IGrouping<string, StoredEvent>> ByProject => Events.GroupBy(e => e.Project).OrderBy(g => g.Key);
    public IEnumerable<string> Screens => Events.Select(e => Prop(e, "page")).Where(p => p != "").Distinct().Order();
    public IEnumerable<StoredEvent> Touches => Events.Where(e => e.Name is "ui.click" or "ui.miss")
        .Where(e => string.IsNullOrEmpty(Screen) || Prop(e, "page") == Screen)
        .Where(e => Coordinate(e, "x") is not null && Coordinate(e, "y") is not null);
    public string FilterUrl(string? handler = null, Guid? session = null, string? project = null) => QueryHelpers.AddQueryString("/",
        new Dictionary<string, string?>
        {
            ["handler"] = handler, ["project"] = project ?? Project,
            ["from"] = From?.ToString("yyyy-MM-dd"), ["to"] = To?.ToString("yyyy-MM-dd"),
            ["appVersion"] = AppVersion, ["contentVersion"] = ContentVersion,
            ["screen"] = Screen, ["session"] = session?.ToString()
        });

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var error = await LoadAsync(cancellationToken);
        return error ?? Page();
    }

    public async Task<IActionResult> OnGetCsvAsync(CancellationToken cancellationToken)
    {
        var error = await LoadAsync(cancellationToken);
        if (error is not null) return error;
        var csv = new StringBuilder("project,id,installId,sessionId,sequence,name,timestampUtc,receivedAtUtc,appVersion,contentVersion,platform,props\r\n");
        foreach (var e in Events.OrderBy(e => e.Project).ThenBy(e => e.SessionId).ThenBy(e => e.Sequence))
        {
            string[] fields = [e.Project, e.Id.ToString(), e.InstallId.ToString(), e.SessionId.ToString(),
                e.Sequence.ToString(CultureInfo.InvariantCulture), e.Name, e.TimestampUtc.ToString("O"),
                e.ReceivedAtUtc.ToString("O"), e.AppVersion, e.ContentVersion ?? "", e.Platform,
                JsonSerializer.Serialize(e.Props)];
            csv.AppendLine(string.Join(",", fields.Select(CsvCell)));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "rtelemetry.csv");
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid filters");
        To ??= DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        From ??= DateOnly.FromDayNumber(Math.Max(0, To.Value.DayNumber - 6));
        if (From > To || To.Value.DayNumber - From.Value.DayNumber > 365 || To == DateOnly.MaxValue)
            return BadRequest("Period must be ordered and at most 366 days");
        if (!string.IsNullOrWhiteSpace(Project) && !RTelemetry.Contracts.TelemetrySchema.IsValidName(Project))
            return BadRequest("Invalid project");
        await foreach (var e in store.ReadAsync(string.IsNullOrWhiteSpace(Project) ? null : Project, From.Value, To.Value, cancellationToken))
        {
            if (!string.IsNullOrEmpty(AppVersion) && e.AppVersion != AppVersion) continue;
            if (!string.IsNullOrEmpty(ContentVersion) && e.ContentVersion != ContentVersion) continue;
            if (Session.HasValue && e.SessionId != Session) continue;
            Events.Add(e);
            // Ограничение памяти явно видно пользователю; экспорт не обрезается молча.
            if (Events.Count > 100_000) return StatusCode(413, "Too many events: narrow the date/project/version filters");
        }
        Screen ??= Screens.FirstOrDefault();
        return null;
    }

    public static string Prop(StoredEvent e, string key) => e.Props?.TryGetValue(key, out var v) == true ? v.ToString() : "";
    public static double? Coordinate(StoredEvent e, string key) =>
        e.Props?.TryGetValue(key, out var value) == true && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var n) && double.IsFinite(n) && n >= 0 && n <= 1 ? n : null;
    public static string Svg(StoredEvent e, string key) => ((Coordinate(e, key) ?? 0) * 1000).ToString("0.###", CultureInfo.InvariantCulture);
    private static string CsvCell(string value)
    {
        // Prevent spreadsheet formula execution, including leading whitespace/control characters.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
