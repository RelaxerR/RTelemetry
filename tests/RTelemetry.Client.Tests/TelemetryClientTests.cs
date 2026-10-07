using RTelemetry.Client;
using RTelemetry.Client.Storage;
using RTelemetry.Client.Transport;
using RTelemetry.Contracts;
using RTelemetry.Server;

namespace RTelemetry.Client.Tests;

public sealed class TelemetryClientTests
{
    private static TelemetryClientOptions Options(List<string>? diagnostics = null) => new()
    {
        Project = "test",
        Endpoint = new Uri("https://telemetry.invalid/"),
        ApiKey = "key",
        AppVersion = "1.2.3+45",
        ContentVersion = "story-abc",
        StorageDirectory = "unused",
        Platform = "test 1.0",
        MaxBatchSize = 3,
        MaxQueuedEvents = 10,
        OnDiagnostic = diagnostics is null ? null : new Action<string>(diagnostics.Add),
    };

    [Fact]
    public async Task Without_consent_nothing_is_recorded_or_sent()
    {
        var transport = new FakeTransport();
        var storage = new InMemoryTelemetryStorage();
        await using var client = new TelemetryClient(Options(), transport, storage);

        client.Track("ui.click");
        await client.FlushAsync();

        Assert.Equal(ConsentState.Unknown, client.Consent);
        Assert.Empty(transport.Batches);
        Assert.Empty(storage.LoadQueue());
    }

    [Fact]
    public async Task Granted_events_are_sent_in_batches_with_versions_and_order()
    {
        var transport = new FakeTransport();
        await using var client = new TelemetryClient(Options(), transport, new InMemoryTelemetryStorage());
        client.SetConsent(ConsentState.Granted);

        client.StartSession();
        for (var i = 0; i < 4; i++)
        {
            client.Track("ui.click", new Dictionary<string, object?> { ["element"] = $"button_{i}" });
        }

        await client.FlushAsync();

        Assert.Equal(2, transport.Batches.Count); // 5 событий при MaxBatchSize = 3
        var events = transport.Batches.SelectMany(b => b.Events).ToList();
        Assert.Equal(5, events.Count);
        Assert.Equal(TelemetryEventNames.SessionStart, events[0].Name);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, events.Select(e => e.Sequence));
        Assert.All(events, e =>
        {
            Assert.Equal("1.2.3+45", e.AppVersion);
            Assert.Equal("story-abc", e.ContentVersion);
            Assert.Equal(client.SessionId, e.SessionId);
        });
        Assert.All(transport.Batches, b => Assert.Null(BatchValidator.Validate(b)));
    }

    [Fact]
    public async Task Denied_clears_queue_and_stops_recording()
    {
        var transport = new FakeTransport();
        var storage = new InMemoryTelemetryStorage();
        await using var client = new TelemetryClient(Options(), transport, storage);
        client.SetConsent(ConsentState.Granted);
        client.Track("ui.click");

        client.SetConsent(ConsentState.Denied);
        client.Track("ui.click");
        await client.FlushAsync();

        Assert.Empty(transport.Batches);
        Assert.Empty(storage.LoadQueue());
    }

    [Fact]
    public async Task Retry_later_keeps_events_on_disk_and_sends_them_next_time()
    {
        var transport = new FakeTransport { Result = SendResult.RetryLater };
        var storage = new InMemoryTelemetryStorage();
        var client = new TelemetryClient(Options(), transport, storage);
        client.SetConsent(ConsentState.Granted);
        client.Track("ui.click");
        await client.FlushAsync();
        await client.DisposeAsync();

        Assert.Single(storage.LoadQueue());

        // «Перезапуск приложения»: тот же storage, сеть появилась.
        transport.Result = SendResult.Accepted;
        transport.Batches.Clear();
        await using var restarted = new TelemetryClient(Options(), transport, storage);
        await restarted.FlushAsync();

        Assert.Single(transport.Batches);
        Assert.Empty(storage.LoadQueue());
        Assert.Equal(ConsentState.Granted, restarted.Consent);
        Assert.Equal(client.InstallId, restarted.InstallId);
    }

    [Fact]
    public async Task Invalid_names_and_values_are_dropped_and_long_strings_truncated()
    {
        var diagnostics = new List<string>();
        var transport = new FakeTransport();
        await using var client = new TelemetryClient(Options(diagnostics), transport, new InMemoryTelemetryStorage());
        client.SetConsent(ConsentState.Granted);

        client.Track("UI Click");
        client.Track("scene.shown", new Dictionary<string, object?>
        {
            ["scene_id"] = new string('x', 1000),
            ["Bad Key"] = 1,
            ["obj"] = new object(),
            ["index"] = 2,
        });
        await client.FlushAsync();

        var e = Assert.Single(transport.Batches.SelectMany(b => b.Events));
        Assert.Equal("scene.shown", e.Name);
        Assert.Equal(TelemetrySchema.MaxStringValueLength, e.Props!["scene_id"].GetString()!.Length);
        Assert.Equal(2, e.Props["index"].GetInt64());
        Assert.Equal(2, e.Props.Count);
        Assert.Equal(3, diagnostics.Count);
    }

    [Fact]
    public async Task Queue_overflow_drops_oldest_events()
    {
        var transport = new FakeTransport();
        await using var client = new TelemetryClient(Options(), transport, new InMemoryTelemetryStorage());
        client.SetConsent(ConsentState.Granted);

        for (var i = 0; i < 15; i++)
        {
            client.Track("ui.click", new Dictionary<string, object?> { ["i"] = i });
        }

        await client.FlushAsync();

        var values = transport.Batches.SelectMany(b => b.Events).Select(e => e.Props!["i"].GetInt64()).ToList();
        Assert.Equal(Enumerable.Range(5, 10).Select(i => (long)i), values);
    }

    private sealed class FakeTransport : ITelemetryTransport
    {
        public SendResult Result { get; set; } = SendResult.Accepted;
        public List<TelemetryBatch> Batches { get; } = [];

        public Task<SendResult> SendAsync(TelemetryBatch batch, CancellationToken cancellationToken)
        {
            if (Result == SendResult.Accepted)
            {
                Batches.Add(batch);
            }

            return Task.FromResult(Result);
        }
    }
}
