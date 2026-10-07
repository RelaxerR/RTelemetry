using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RTelemetry.Server.Storage;

namespace RTelemetry.Server.Pages;

/// <summary>Сводка за период: события по проектам с числом установок и сессий.</summary>
public sealed class IndexModel(IEventStore store, TimeProvider time) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Days { get; set; } = 7;

    [BindProperty(SupportsGet = true)]
    public string? Project { get; set; }

    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public IReadOnlyList<ProjectSummary> Projects { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Days = Math.Clamp(Days, 1, 90);
        To = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        From = To.AddDays(-(Days - 1));

        var projects = new Dictionary<string, ProjectAccumulator>(StringComparer.Ordinal);
        await foreach (var e in store.ReadAsync(string.IsNullOrWhiteSpace(Project) ? null : Project, From, To, cancellationToken))
        {
            if (!projects.TryGetValue(e.Project, out var acc))
            {
                projects[e.Project] = acc = new ProjectAccumulator();
            }

            acc.Add(e);
        }

        Projects = projects
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value.ToSummary(p.Key))
            .ToList();
    }

    public sealed record EventSummary(string Name, int Count, int Installs, int Sessions, DateTimeOffset LastSeen);

    public sealed record ProjectSummary(string Project, int Events, int Installs, int Sessions, IReadOnlyList<EventSummary> ByEvent);

    private sealed class ProjectAccumulator
    {
        private readonly HashSet<Guid> _installs = [];
        private readonly HashSet<Guid> _sessions = [];
        private readonly Dictionary<string, (int Count, HashSet<Guid> Installs, HashSet<Guid> Sessions, DateTimeOffset Last)> _events = new();
        private int _total;

        public void Add(StoredEvent e)
        {
            _total++;
            _installs.Add(e.InstallId);
            _sessions.Add(e.SessionId);

            if (!_events.TryGetValue(e.Name, out var s))
            {
                s = (0, new HashSet<Guid>(), new HashSet<Guid>(), DateTimeOffset.MinValue);
            }

            s.Installs.Add(e.InstallId);
            s.Sessions.Add(e.SessionId);
            _events[e.Name] = (s.Count + 1, s.Installs, s.Sessions, e.TimestampUtc > s.Last ? e.TimestampUtc : s.Last);
        }

        public ProjectSummary ToSummary(string project) => new(
            project,
            _total,
            _installs.Count,
            _sessions.Count,
            _events
                .OrderByDescending(x => x.Value.Count)
                .Select(x => new EventSummary(x.Key, x.Value.Count, x.Value.Installs.Count, x.Value.Sessions.Count, x.Value.Last))
                .ToList());
    }
}
