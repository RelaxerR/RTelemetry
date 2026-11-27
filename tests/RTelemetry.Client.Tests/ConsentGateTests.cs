using RTelemetry.Client.Storage;
using RTelemetry.Client.Transport;
using RTelemetry.Contracts;

namespace RTelemetry.Client.Tests;

public sealed class ConsentGateTests
{
    [Fact]
    public async Task OfflineEventsStayLocalUntilEachBatchPassesAStandaloneCheck()
    {
        var clock = new Clock();
        var store = new InMemoryTelemetryStorage();
        var sent = new Transport();
        await using var client = new TelemetryClient(Options(), sent, store, clock);
        client.SetConsent(ConsentState.Granted);
        client.Track("before.deadline");
        client.SetCollectionDeadline(clock.Now.AddDays(7));
        var checks = 0;
        var active = false;
        client.Track("offline.first");
        await client.FlushAsync();
        Assert.Empty(sent.Batches);
        Assert.Equal("offline.first", Assert.Single(store.LoadQueue()).Name);

        client.ConfigureConsentGate((id, _) =>
        {
            Assert.Equal(client.InstallId, id);
            checks++;
            return Task.FromResult(active);
        });
        client.Track("offline.second");
        await client.FlushAsync();
        Assert.Equal(1, checks);
        Assert.Empty(sent.Batches);
        Assert.Equal(2, store.LoadQueue().Count);

        active = true;
        sent.Result = SendResult.RetryLater;
        clock.Advance(TimeSpan.FromSeconds(2));
        await client.FlushAsync();
        Assert.Equal(2, checks);
        Assert.Single(sent.Batches);
        await client.FlushAsync();
        Assert.Equal(2, checks);

        sent.Result = SendResult.Accepted;
        clock.Advance(TimeSpan.FromSeconds(2));
        await client.FlushAsync();
        Assert.Equal(4, checks);
        Assert.Equal(["offline.first", "offline.first", "offline.second"],
            sent.Batches.SelectMany(batch => batch.Events).Select(e => e.Name).ToArray());
        Assert.Empty(store.LoadQueue());
    }

    [Fact]
    public async Task ReopeningOrShorteningDeadlineCannotExtendMonotonicBudgetAfterRollback()
    {
        var clock = new Clock();
        var store = new InMemoryTelemetryStorage();
        var sent = new Transport();
        await using var client = new TelemetryClient(Options(), sent, store, clock);
        client.SetConsent(ConsentState.Granted);
        client.ConfigureConsentGate((_, _) => Task.FromResult(true));
        var expiry = clock.Now.AddDays(7);
        client.SetCollectionDeadline(expiry);
        clock.Advance(TimeSpan.FromDays(6));
        clock.Now = clock.Now.AddDays(-5);
        client.SetCollectionDeadline(null);
        client.SetCollectionDeadline(expiry.AddHours(-1));
        Assert.True(client.IsCollectionEnabled);

        clock.Advance(TimeSpan.FromHours(23));
        Assert.False(client.IsCollectionEnabled);
        client.Track("after.deadline");
        await client.FlushAsync();
        Assert.Empty(store.LoadQueue());
        Assert.Empty(sent.Batches);
    }

    [Fact]
    public async Task RestartAndFlushDiscardExpiredEventsBeforeNewConfirmation()
    {
        var clock = new Clock();
        var store = new InMemoryTelemetryStorage();
        var sent = new Transport();
        Guid installId;
        await using (var client = new TelemetryClient(Options(), sent, store, clock))
        {
            installId = client.InstallId;
            client.SetConsent(ConsentState.Granted);
            client.SetCollectionDeadline(clock.Now.AddDays(7));
            client.Track("expired");
            clock.Advance(TimeSpan.FromDays(1));
            client.Track("retained");
        }
        clock.Advance(TimeSpan.FromDays(6));
        await using var restarted = new TelemetryClient(Options(), sent, store, clock);
        Assert.False(restarted.IsCollectionEnabled);
        await restarted.FlushAsync();
        Assert.Equal("retained", Assert.Single(store.LoadQueue()).Name);
        restarted.SetInstallId(installId);
        restarted.ConfigureConsentGate((_, _) =>
        {
            restarted.SetCollectionDeadline(clock.Now.AddDays(7));
            return Task.FromResult(true);
        });

        await restarted.FlushAsync();
        Assert.Equal("retained", Assert.Single(Assert.Single(sent.Batches).Events).Name);
    }

    [Fact]
    public async Task WithdrawalCancelsValidationEvenWhenItsLateResultIsActive()
    {
        var clock = new Clock();
        var store = new InMemoryTelemetryStorage();
        var sent = new Transport();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var validator = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new TelemetryClient(Options(), sent, store, clock);
        client.SetConsent(ConsentState.Granted);
        client.SetCollectionDeadline(clock.Now.AddDays(7));
        client.ConfigureConsentGate((_, token) =>
        {
            started.SetResult(token);
            return validator.Task;
        });
        client.Track("before.withdrawal");

        var flush = client.FlushAsync();
        var validationToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.SetConsent(ConsentState.Denied);
        var newInstallId = Guid.NewGuid();
        client.SetInstallId(newInstallId);
        validator.SetResult(true);
        await flush;

        Assert.True(validationToken.IsCancellationRequested);
        Assert.Empty(sent.Batches);
        Assert.Empty(store.LoadQueue());
        Assert.Equal(newInstallId, store.LoadState()!.InstallId);
        Assert.Equal(ConsentState.Unknown, client.Consent);
        Assert.False(client.IsCollectionEnabled);
    }

    private static TelemetryClientOptions Options() => new()
    {
        Project = "test",
        Endpoint = new Uri("https://localhost"),
        AppVersion = "1",
        RequireUploadValidation = true,
        MaxPendingEventAge = TimeSpan.FromDays(7),
        MaxBatchSize = 1,
        RetryInitialDelay = TimeSpan.FromSeconds(1),
        RetryMaxDelay = TimeSpan.FromSeconds(2),
    };

    private sealed class Clock : TimeProvider
    {
        private long _elapsed;
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _elapsed;
        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan elapsed)
        {
            Now += elapsed;
            _elapsed += elapsed.Ticks;
        }
    }

    private sealed class Transport : ITelemetryTransport
    {
        public SendResult Result = SendResult.Accepted;
        public List<TelemetryBatch> Batches { get; } = [];

        public Task<SendResult> SendAsync(TelemetryBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.FromResult(Result);
        }
    }
}
