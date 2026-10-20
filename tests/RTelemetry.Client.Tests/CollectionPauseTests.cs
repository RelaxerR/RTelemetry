using RTelemetry.Client.Storage;
using RTelemetry.Client.Transport;
using RTelemetry.Contracts;

namespace RTelemetry.Client.Tests;

public sealed class CollectionPauseTests
{
    private static TelemetryClientOptions Options() => new()
    {
        Project = "test", Endpoint = new Uri("https://localhost/"), AppVersion = "0.1.0+1.1234567",
        MaxBatchSize = 100, MaxQueuedEvents = 1000
    };

    [Fact]
    public async Task PersistedConsentAndQueueDoNotBypassInitialPause()
    {
        var storage = new InMemoryTelemetryStorage();
        var transport = new CaptureTransport { Result = SendResult.RetryLater };
        await using (var old = new TelemetryClient(Options(), transport, storage))
        {
            old.SetConsent(ConsentState.Granted);
            old.Track("before.restart");
            await old.FlushAsync();
        }
        transport.Batches.Clear();
        transport.Result = SendResult.Accepted;
        var options = Options();
        options.InitialCollectionEnabled = false;
        await using var restarted = new TelemetryClient(options, transport, storage);
        var install = restarted.InstallId;
        restarted.OnForeground();
        restarted.Track("must.not.exist");
        await restarted.FlushAsync();
        Assert.Empty(transport.Batches);
        Assert.Equal(ConsentState.Granted, restarted.Consent);
        Assert.Equal("before.restart", Assert.Single(storage.LoadQueue()).Name);
        restarted.SetCollectionEnabled(true);
        await restarted.FlushAsync();
        Assert.Equal(install, restarted.InstallId);
        Assert.Equal(new[] { "before.restart", "session.start" }, transport.Batches.SelectMany(b => b.Events).Select(e => e.Name));
    }

    [Fact]
    public async Task PauseCancelsInflightDeliveryAndRetainsQueueUntilResume()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new List<TelemetryEvent>();
        var first = true;
        var transport = new DelegateTransport(async (batch, token) =>
        {
            if (first)
            {
                first = false;
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            captured.AddRange(batch.Events);
            return SendResult.Accepted;
        });
        var storage = new InMemoryTelemetryStorage();
        await using var client = new TelemetryClient(Options(), transport, storage);
        client.SetConsent(ConsentState.Granted);
        client.Track("retained");
        var flush = client.FlushAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.SetCollectionEnabled(false);
        await flush.WaitAsync(TimeSpan.FromSeconds(5));
        client.Track("blocked");
        await client.FlushAsync();
        Assert.Empty(captured);
        Assert.Equal("retained", Assert.Single(storage.LoadQueue()).Name);
        client.SetCollectionEnabled(true);
        await client.FlushAsync();
        Assert.Equal("retained", Assert.Single(captured).Name);
    }

    [Fact]
    public async Task DenialStillErasesPausedQueueAndResumeDoesNotGrantConsent()
    {
        var transport = new CaptureTransport();
        var storage = new InMemoryTelemetryStorage();
        await using var client = new TelemetryClient(Options(), transport, storage);
        client.SetConsent(ConsentState.Granted);
        client.Track("old");
        client.SetCollectionEnabled(false);
        client.SetConsent(ConsentState.Denied);
        client.SetCollectionEnabled(true);
        client.Track("blocked");
        await client.FlushAsync();
        Assert.Empty(transport.Batches);
        Assert.Empty(storage.LoadQueue());
        Assert.Equal(ConsentState.Denied, client.Consent);
    }

    [Fact]
    public async Task BackgroundRequiresApprovalAndTimeoutRotationWaitsForIt()
    {
        var options = Options();
        options.PauseOnBackground = true;
        var clock = new Clock();
        var transport = new CaptureTransport();
        await using var client = new TelemetryClient(options, transport, new InMemoryTelemetryStorage(), clock);
        client.SetConsent(ConsentState.Granted);
        client.OnForeground();
        var firstSession = client.SessionId;
        await client.OnBackgroundAsync();
        Assert.False(client.IsCollectionEnabled);
        Assert.Empty(transport.Batches);
        clock.Now += TimeSpan.FromMinutes(1);
        client.OnForeground();
        await client.FlushAsync();
        Assert.Empty(transport.Batches);
        client.SetCollectionEnabled(true);
        Assert.Equal(firstSession, client.SessionId);
        await client.OnBackgroundAsync();
        clock.Now += TimeSpan.FromMinutes(31);
        client.OnForeground();
        Assert.Equal(firstSession, client.SessionId);
        client.SetCollectionEnabled(true);
        Assert.NotEqual(firstSession, client.SessionId);
        await client.FlushAsync();
        Assert.Equal(2, transport.Batches.SelectMany(b => b.Events).Count(e => e.Name == "session.start"));
    }

    private sealed class CaptureTransport : ITelemetryTransport
    {
        public SendResult Result = SendResult.Accepted;
        public List<TelemetryBatch> Batches { get; } = [];
        public Task<SendResult> SendAsync(TelemetryBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.FromResult(Result);
        }
    }

    private sealed class DelegateTransport(Func<TelemetryBatch, CancellationToken, Task<SendResult>> send) : ITelemetryTransport
    {
        public Task<SendResult> SendAsync(TelemetryBatch batch, CancellationToken cancellationToken) => send(batch, cancellationToken);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
