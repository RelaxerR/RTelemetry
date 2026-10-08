using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RTelemetry.Client.Storage;
using RTelemetry.Client.Transport;
using RTelemetry.Contracts;

namespace RTelemetry.Client.Tests;

public sealed class ReliabilityTests
{
    private static TelemetryClientOptions Options() => new()
    {
        Project = "test", AppVersion = "1", ApiKey = "key", Endpoint = new("https://localhost/"),
        MaxBatchSize = 10, MaxQueuedEvents = 1000,
    };

    [Fact]
    public async Task Retry_backoff_has_jitter_grows_and_caps()
    {
        var clock = new Clock();
        var transport = new Transport { Result = SendResult.RetryLater };
        var options = Options();
        options.RetryInitialDelay = TimeSpan.FromSeconds(10);
        options.RetryMaxDelay = TimeSpan.FromSeconds(20);
        await using var client = new TelemetryClient(options, transport, new InMemoryTelemetryStorage(), clock);
        client.SetConsent(ConsentState.Granted);
        client.Track("test");
        await client.FlushAsync();
        await client.FlushAsync();
        Assert.Equal(1, transport.Calls);
        clock.Advance(TimeSpan.FromSeconds(4));
        await client.FlushAsync();
        Assert.Equal(1, transport.Calls);
        clock.Advance(TimeSpan.FromSeconds(6));
        await client.FlushAsync();
        Assert.Equal(2, transport.Calls);
        clock.Advance(TimeSpan.FromSeconds(9));
        await client.FlushAsync();
        Assert.Equal(2, transport.Calls);
        clock.Advance(TimeSpan.FromSeconds(11));
        await client.FlushAsync();
        Assert.Equal(3, transport.Calls);
        clock.Advance(TimeSpan.FromSeconds(20));
        transport.Result = SendResult.Accepted;
        await client.FlushAsync();
        client.Track("next");
        await client.FlushAsync();
        Assert.Equal(5, transport.Calls);
    }

    [Fact]
    public async Task Foreground_rotates_only_after_timeout_and_starts_after_consent()
    {
        var clock = new Clock();
        var transport = new Transport();
        await using var client = new TelemetryClient(Options(), transport, new InMemoryTelemetryStorage(), clock);
        client.OnForeground();
        await client.OnBackgroundAsync();
        Assert.Empty(transport.Events);
        client.OnForeground();
        client.SetConsent(ConsentState.Granted);
        var first = client.SessionId;
        client.OnForeground();
        await client.OnBackgroundAsync();
        clock.Advance(TimeSpan.FromMinutes(29));
        client.OnForeground();
        Assert.Equal(first, client.SessionId);
        client.Track("click");
        await client.OnBackgroundAsync();
        clock.Advance(TimeSpan.FromMinutes(30));
        client.OnForeground();
        Assert.NotEqual(first, client.SessionId);
        await client.FlushAsync();
        Assert.Equal(2, transport.Events.Count(e => e.Name == "session.start"));
        Assert.Equal(2, transport.Events.Count(e => e.Name == "session.end"));
        var ends = transport.Events.Where(e => e.Name == "session.end").ToList();
        Assert.All(ends, e => Assert.Equal(first, e.SessionId));
        Assert.Equal(0, ends[0].Props!["duration_ms"].GetInt64());
        Assert.Equal(29 * 60 * 1000, ends[1].Props!["duration_ms"].GetInt64());
        Assert.Equal(1, transport.Events.Last().Sequence);
    }

    [Fact]
    public async Task Revocation_cancels_inflight_batch_and_does_not_restore_disk_queue()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new InMemoryTelemetryStorage();
        var transport = new DelegateTransport(async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { canceled.SetResult(); throw; }
            return SendResult.Accepted;
        });
        await using var client = new TelemetryClient(Options(), transport, storage);
        client.SetConsent(ConsentState.Granted);
        client.Track("test");
        var flush = client.FlushAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.SetConsent(ConsentState.Denied);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await flush;
        Assert.Empty(storage.LoadQueue());
        Assert.Equal(ConsentState.Denied, storage.LoadState()!.Consent);
    }

    [Fact]
    public async Task Concurrent_track_flush_and_consent_leave_no_events_after_revocation()
    {
        var storage = new InMemoryTelemetryStorage();
        var transport = new DelegateTransport((_, _) => Task.FromResult(SendResult.RetryLater));
        await using var client = new TelemetryClient(Options(), transport, storage);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                if (worker % 3 == 0) client.SetConsent(i % 2 == 0 ? ConsentState.Granted : ConsentState.Denied);
                else if (worker % 3 == 1) client.Track("click");
                else await client.FlushAsync();
            }
        })));
        client.SetConsent(ConsentState.Denied);
        await client.FlushAsync();
        Assert.Empty(storage.LoadQueue());
        Assert.Equal(ConsentState.Denied, storage.LoadState()!.Consent);
    }

    [Fact]
    public async Task Late_response_from_previous_install_does_not_remove_new_events()
    {
        var release = new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var delivered = new List<TelemetryBatch>();
        var transport = new DelegateTransport((batch, _) =>
        {
            delivered.Add(batch);
            return ++calls == 1 ? release.Task : Task.FromResult(SendResult.Accepted);
        });
        await using var client = new TelemetryClient(Options(), transport, new InMemoryTelemetryStorage());
        client.SetConsent(ConsentState.Granted);
        client.Track("old");
        var oldId = client.InstallId;
        var flush = client.FlushAsync();
        client.ResetInstallId();
        client.Track("new");
        release.SetResult(SendResult.Accepted);
        await flush;
        Assert.Equal(2, delivered.Count);
        Assert.Equal(oldId, delivered[0].InstallId);
        Assert.Equal(client.InstallId, delivered[1].InstallId);
        Assert.Equal("new", Assert.Single(delivered[1].Events).Name);
    }

    [Fact]
    public async Task Concurrent_dispose_waits_for_inflight_send_and_is_idempotent()
    {
        var release = new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new DelegateTransport((_, _) => release.Task);
        var storage = new InMemoryTelemetryStorage();
        var client = new TelemetryClient(Options(), transport, storage);
        client.SetConsent(ConsentState.Granted);
        client.Track("event");
        var flush = client.FlushAsync();
        var dispose1 = client.DisposeAsync().AsTask();
        var dispose2 = client.DisposeAsync().AsTask();
        Assert.Same(dispose1, dispose2);
        Assert.False(dispose1.IsCompleted);
        release.SetResult(SendResult.RetryLater);
        await Task.WhenAll(flush, dispose1, dispose2);
        await client.DisposeAsync();
        client.Dispose();
        Assert.Single(storage.LoadQueue());
    }

    [Fact]
    public async Task Throwing_logger_does_not_disable_diagnostic_callback()
    {
        var options = Options();
        var diagnostics = new List<string>();
        options.Logger = new TestLogger { Throw = true };
        options.OnDiagnostic = diagnostics.Add;
        await using var client = new TelemetryClient(options, new Transport(), new InMemoryTelemetryStorage());
        client.SetConsent(ConsentState.Granted);
        client.Track("bad name");
        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task Disposed_client_and_throwing_diagnostics_do_not_throw()
    {
        var options = Options();
        var logger = new TestLogger();
        options.Logger = logger;
        options.OnDiagnostic = _ => throw new InvalidOperationException();
        var client = new TelemetryClient(options, new Transport(), new InMemoryTelemetryStorage());
        client.SetConsent(ConsentState.Granted);
        client.Track("invalid name");
        client.Track("number", new Dictionary<string, object?> { ["value"] = double.NaN });
        Assert.Equal(2, logger.Calls);
        await client.DisposeAsync();
        client.Start().StartSession();
        client.Track("ignored");
        client.SetConsent(ConsentState.Granted);
        await client.FlushAsync();
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }
    private sealed class Transport : ITelemetryTransport
    {
        public int Calls;
        public SendResult Result = SendResult.Accepted;
        public List<TelemetryEvent> Events = [];
        public Task<SendResult> SendAsync(TelemetryBatch batch, CancellationToken token)
        {
            Calls++;
            if (Result == SendResult.Accepted) Events.AddRange(batch.Events);
            return Task.FromResult(Result);
        }
    }
    private sealed class DelegateTransport(Func<TelemetryBatch, CancellationToken, Task<SendResult>> send) : ITelemetryTransport
    {
        public Task<SendResult> SendAsync(TelemetryBatch batch, CancellationToken token) => send(batch, token);
    }
    private sealed class TestLogger : ILogger
    {
        public int Calls;
        public bool Throw;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("logger");
        }
    }
}

public sealed class FileStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rtelemetry-tests-" + Guid.NewGuid());
    private string QueuePath => Path.Combine(_directory, "rtelemetry", "queue.json");
    private static TelemetryEvent Event() => new() { Id = Guid.NewGuid(), Name = "click", AppVersion = "1", SessionId = Guid.NewGuid(), Sequence = 1 };

    [Fact]
    public void Atomic_replacement_preserves_committed_data_and_discards_interrupted_temp()
    {
        var storage = new FileTelemetryStorage(_directory);
        var first = Event();
        storage.SaveQueue([first]);
        File.WriteAllText(QueuePath + ".tmp", "interrupted");
        var reopened = new FileTelemetryStorage(_directory);
        Assert.Equal(first.Id, Assert.Single(reopened.LoadQueue()).Id);
        Assert.False(File.Exists(QueuePath + ".tmp"));
        var next = Event();
        reopened.SaveQueue([next]);
        Assert.Equal(next.Id, Assert.Single(storage.LoadQueue()).Id);
    }

    [Fact]
    public void Corrupt_and_oversized_files_are_removed_and_storage_recovers()
    {
        var storage = new FileTelemetryStorage(_directory, 500);
        File.WriteAllText(QueuePath, "{broken");
        Assert.Empty(storage.LoadQueue());
        Assert.False(File.Exists(QueuePath));
        File.WriteAllText(QueuePath, new string('x', 501));
        Assert.Empty(storage.LoadQueue());
        File.WriteAllText(Path.Combine(_directory, "rtelemetry", "state.json"), "bad");
        Assert.Null(storage.LoadState());
        storage.SaveState(new(Guid.NewGuid(), ConsentState.Granted));
        storage.SaveQueue([Event()]);
        Assert.Single(storage.LoadQueue());
        Assert.NotNull(storage.LoadState());
    }

    [Fact]
    public void Disk_limit_keeps_newest_events_and_clear_removes_temporary_files()
    {
        var events = Enumerable.Range(0, 10).Select(_ => Event()).ToArray();
        var oneSize = JsonSerializer.SerializeToUtf8Bytes(new[] { events[0] }, TelemetryProtocol.JsonOptions).Length;
        var storage = new FileTelemetryStorage(_directory, oneSize + 5);
        storage.SaveQueue(events);
        Assert.Equal(events[^1].Id, Assert.Single(storage.LoadQueue()).Id);
        Assert.True(new FileInfo(QueuePath).Length <= oneSize + 5);
        File.WriteAllText(QueuePath + ".tmp", "old");
        storage.ClearQueue();
        Assert.False(File.Exists(QueuePath));
        Assert.False(File.Exists(QueuePath + ".tmp"));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}

public sealed class HttpTransportTests
{
    [Theory]
    [InlineData(200, SendResult.Accepted)]
    [InlineData(400, SendResult.Rejected)]
    [InlineData(401, SendResult.Rejected)]
    [InlineData(413, SendResult.Rejected)]
    [InlineData(429, SendResult.RetryLater)]
    [InlineData(500, SendResult.RetryLater)]
    public async Task Status_codes_and_protocol_are_mapped(int status, SendResult expected)
    {
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            Assert.Equal("https://localhost/prefix/v1/batches", request.RequestUri!.AbsoluteUri);
            Assert.Equal("secret", Assert.Single(request.Headers.GetValues(TelemetryProtocol.ApiKeyHeader)));
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("\"project\":\"test\"", await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage((HttpStatusCode)status);
        }));
        var transport = new HttpTelemetryTransport(http, new("https://localhost/prefix"), "secret");
        Assert.Equal(expected, await transport.SendAsync(new() { Project = "test" }, CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_is_retry_but_caller_cancellation_propagates()
    {
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })) { Timeout = TimeSpan.FromMilliseconds(30) };
        var transport = new HttpTelemetryTransport(http, new("https://localhost"), "key");
        Assert.Equal(SendResult.RetryLater, await transport.SendAsync(new(), CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendAsync(new(), cancellation.Token));
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
