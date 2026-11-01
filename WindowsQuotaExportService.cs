namespace AiBurgerClock;

internal sealed record WindowsQuotaExportStatus(bool Ready, bool Enabled, string SiteUrl,
    bool Sending, DateTimeOffset? LastSuccessUtc, DateTimeOffset? RetryAtUtc, WindowsQuotaExportFailure Failure);

// Single background sender; no await/file/network operation runs in a monitor callback or UI tick.
// Settings are separate from UsageStore. Pending snapshots and delivery status are session-only.
internal sealed class WindowsQuotaExportService : IDisposable
{
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(15);
    private static readonly int[] RetrySeconds = [30, 120, 300, 900];
    private sealed record Draft(IReadOnlyList<QuotaState> States, DateTimeOffset AtUtc, bool Manual);
    private sealed record Packet(Uri Endpoint, byte[] Body, bool Manual);
    private readonly object sync = new();
    private readonly SemaphoreSlim settingsGate = new(1, 1);
    private readonly IWindowsQuotaExportSettingsStore store;
    private readonly IWindowsQuotaExportCredentials credentials;
    private readonly IWindowsQuotaExportTransport transport;
    private readonly Func<IReadOnlyList<QuotaState>> snapshot;
    private readonly TimeProvider clock;
    private readonly string appVersion;
    private readonly CancellationTokenSource lifetime = new();
    private WindowsQuotaExportSettings? settings;
    private Draft? pending;
    private Packet? retry;
    private CancellationTokenSource? activeRequest;
    private Task worker = Task.CompletedTask;
    private bool ready, running, stopped, saving;
    private long epoch, heartbeatDue, notBefore;
    private bool delayed;
    private int failures;
    private DateTimeOffset? lastSuccess;
    private WindowsQuotaExportFailure failure;
    internal Task Initialization { get; }
    internal event Action? Changed;

    internal WindowsQuotaExportService(IWindowsQuotaExportSettingsStore store,
        IWindowsQuotaExportCredentials credentials, IWindowsQuotaExportTransport transport,
        Func<IReadOnlyList<QuotaState>> snapshot, string appVersion, TimeProvider? clock = null)
    {
        this.store = store; this.credentials = credentials; this.transport = transport;
        this.snapshot = snapshot; this.appVersion = appVersion; this.clock = clock ?? TimeProvider.System;
        Initialization = Task.Run(() =>
        {
            try
            {
                var loaded = store.Load();
                lock (sync)
                {
                    if (stopped) return;
                    settings = loaded; ready = true; heartbeatDue = After(HeartbeatInterval);
                    if (!loaded.Enabled) pending = null;
                }
            }
            catch { lock (sync) { ready = true; failure = WindowsQuotaExportFailure.Settings; pending = null; } }
            Notify(); StartWorker();
        });
    }

    private long After(TimeSpan delay) => checked(clock.GetTimestamp() + (long)(delay.TotalSeconds * clock.TimestampFrequency));

    internal WindowsQuotaExportStatus Status
    {
        get
        {
            lock (sync)
            {
                DateTimeOffset? due = delayed ? clock.GetUtcNow() +
                    TimeSpan.FromSeconds(Math.Max(0, (double)(notBefore - clock.GetTimestamp()) / clock.TimestampFrequency)) : null;
                return new(ready && settings is not null, settings?.Enabled == true && !saving && !stopped, settings?.SiteUrl ?? "",
                    running, lastSuccess, due, failure);
            }
        }
    }

    internal void Completed(IReadOnlyList<QuotaState> states) => Offer(states, manual: false);

    private void Offer(IReadOnlyList<QuotaState> states, bool manual)
    {
        lock (sync)
        {
            if (stopped || saving || (ready && settings?.Enabled != true && !manual)) return;
            pending = new(states.ToArray(), clock.GetUtcNow(), manual || pending?.Manual == true);
        }
        StartWorker();
    }

    // Called by the existing one-second UI timer. Resume catches up once, not in bursts.
    // Network recovery never bypasses backoff or asks the CLI for another reading.
    internal void Tick()
    {
        bool heartbeat;
        lock (sync)
        {
            heartbeat = !stopped && !saving && ready && settings?.Enabled == true && clock.GetTimestamp() >= heartbeatDue;
            if (heartbeat) heartbeatDue = After(HeartbeatInterval);
        }
        if (heartbeat)
        {
            try { Offer(snapshot(), manual: false); }
            catch { lock (sync) failure = WindowsQuotaExportFailure.Payload; Notify(); }
        }
        StartWorker();
    }

    internal async Task<bool?> CredentialStoredAsync()
    {
        await Initialization.ConfigureAwait(false);
        return await Task.Run<bool?>(() => { try { return credentials.Exists(); } catch { return null; } }).ConfigureAwait(false);
    }

    internal async Task<bool> SaveAsync(bool enabled, string siteUrl, string? replacementToken)
    {
        await Initialization.ConfigureAwait(false);
        try
        {
            if (siteUrl.Trim().Length > 0 || enabled) _ = WindowsQuotaExportSettingsStore.Endpoint(siteUrl.Trim());
        }
        catch { lock (sync) failure = WindowsQuotaExportFailure.Address; Notify(); return false; }
        try { if (replacementToken is not null) WindowsQuotaExportCredentials.Validate(replacementToken); }
        catch { lock (sync) failure = WindowsQuotaExportFailure.Credential; Notify(); return false; }
        CancellationTokenSource? cancel;
        lock (sync)
        {
            if (stopped || saving || settings is null) return false;
            saving = true; epoch++; cancel = activeRequest;
            pending = null; retry = null; delayed = false; failures = 0;
        }
        try { cancel?.Cancel(); } catch (ObjectDisposedException) { }
        Notify();
        bool saved = false;
        await Task.Run(async () =>
        {
            await settingsGate.WaitAsync().ConfigureAwait(false);
            var failedPhase = WindowsQuotaExportFailure.Settings;
            try
            {
                // A token/configuration update cannot be atomic across two Windows stores.
                // Persist OFF before replacing the token; a partial save stays safely OFF.
                if (replacementToken is not null)
                {
                    store.Update(false, siteUrl);
                    failedPhase = WindowsQuotaExportFailure.Credential;
                    credentials.Replace(replacementToken);
                    failedPhase = WindowsQuotaExportFailure.Settings;
                }
                var updated = store.Update(enabled, siteUrl);
                lock (sync)
                {
                    if (settings?.SiteUrl != updated.SiteUrl) lastSuccess = null;
                    settings = updated; failure = WindowsQuotaExportFailure.None;
                    heartbeatDue = After(HeartbeatInterval); saved = true;
                }
            }
            catch
            {
                lock (sync)
                {
                    if (settings is not null) settings = settings with { Enabled = false };
                    failure = failedPhase;
                }
            }
            finally { settingsGate.Release(); lock (sync) saving = false; }
        }).ConfigureAwait(false);
        Notify(); return saved;
    }

    // Explicit button only. OFF permits this one request, without automatic retries.
    internal async Task<bool> TestAsync()
    {
        await Initialization.ConfigureAwait(false);
        lock (sync) if (stopped || saving || settings is null || settings.SiteUrl.Length == 0) return false;
        try { Offer(snapshot(), manual: true); return true; }
        catch { lock (sync) failure = WindowsQuotaExportFailure.Payload; Notify(); return false; }
    }

    private void StartWorker()
    {
        lock (sync)
        {
            if (stopped || saving || !ready || settings is null || running || (pending is null && retry is null) ||
                (delayed && clock.GetTimestamp() < notBefore && pending?.Manual != true)) return;
            running = true;
            worker = Task.Run(ProcessAsync);
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (true)
            {
                Draft? draft; Packet? packet; long version;
                lock (sync)
                {
                    if (stopped || saving || settings is null || (delayed && clock.GetTimestamp() < notBefore && pending?.Manual != true)) return;
                    if (!settings.Enabled && pending?.Manual != true && retry?.Manual != true) { pending = null; retry = null; return; }
                    version = epoch; draft = pending; pending = null;
                    packet = draft is null ? retry : null; retry = null; delayed = false;
                    if (draft is null && packet is null) return;
                }
                try
                {
                    if (draft is not null)
                    {
                        await settingsGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                        try
                        {
                            lock (sync) if (stopped || saving || epoch != version) continue;
                            var reserved = store.ReserveSequence();
                            packet = new(WindowsQuotaExportSettingsStore.Endpoint(reserved.SiteUrl),
                                WindowsQuotaExportSnapshot.Serialize(draft.States, reserved, draft.AtUtc, appVersion), draft.Manual);
                            lock (sync) if (epoch == version && !saving) settings = reserved;
                        }
                        finally { settingsGate.Release(); }
                    }
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    lock (sync)
                    {
                        if (stopped || saving || epoch != version) continue;
                        activeRequest = request;
                    }
                    Notify();
                    var result = await transport.SendAsync(packet!.Endpoint, packet.Body, request.Token).ConfigureAwait(false);
                    lock (sync)
                    {
                        if (stopped || saving || epoch != version) continue;
                        failure = result.Failure;
                        if (result.Succeeded) { lastSuccess = clock.GetUtcNow(); failures = 0; }
                        else if (result.Retry && settings?.Enabled == true)
                        {
                            retry = packet; DelayRetry();
                        }
                        else failures = 0;
                    }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (OperationCanceledException) { /* Disabled/reconfigured while a request was running. */ }
                catch (InvalidDataException)
                { lock (sync) if (!stopped && epoch == version) { failure = WindowsQuotaExportFailure.Payload; failures = 0; } }
                catch
                {
                    lock (sync)
                    {
                        if (!stopped && !saving && epoch == version)
                        {
                            failure = WindowsQuotaExportFailure.Settings;
                            if (settings?.Enabled == true)
                            {
                                if (pending is null && draft is not null) pending = draft with { Manual = false };
                                retry = packet; DelayRetry();
                            }
                        }
                    }
                }
                finally { lock (sync) activeRequest = null; Notify(); }
            }
        }
        finally
        {
            lock (sync) running = false;
            Notify(); StartWorker();
        }
    }

    private void DelayRetry()
    {
        notBefore = After(TimeSpan.FromSeconds(RetrySeconds[Math.Min(failures, RetrySeconds.Length - 1)]));
        failures = Math.Min(failures + 1, RetrySeconds.Length); delayed = true;
    }

    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch { /* A settings window cannot affect polling or the sender. */ }
    }

    internal async Task WaitForIdleAsync()
    {
        await Initialization.ConfigureAwait(false);
        while (true)
        {
            Task current; lock (sync) current = worker;
            await current.ConfigureAwait(false);
            lock (sync) if (!running && ReferenceEquals(current, worker)) return;
        }
    }

    internal void Stop()
    {
        lock (sync)
        {
            if (stopped) return;
            stopped = true; epoch++; pending = null; retry = null; delayed = false;
        }
        lifetime.Cancel(); // No flush, site request or wait on shutdown.
    }

    public void Dispose() { Stop(); transport.Dispose(); }
}
