namespace AiBurgerClock;

internal sealed class StatusMonitor : IDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly ProviderStatusClient client;
    private readonly UsageStore? store;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<DateTimeOffset, ScheduleSnapshot> scheduleAt;
    private readonly TimeSpan interval;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly object sync = new();
    private readonly Dictionary<ProviderKind, ProviderStatus> states =
        Enum.GetValues<ProviderKind>().ToDictionary(p => p, p => ProviderStatus.Unknown(p));
    private Task? runner;
    private bool disposed;
    private bool refreshing;
    private DateTimeOffset? nextRefresh;
    // Provider saves run concurrently; one provider's success must not hide another's failure.
    private string cacheLoadError = "";
    private readonly Dictionary<ProviderKind, string> saveErrors = new();

    public event Action? Changed;
    public bool IsRefreshing { get { lock (sync) return refreshing; } }
    public DateTimeOffset? NextRefreshUtc { get { lock (sync) return nextRefresh; } }
    public string StorageError
    {
        get
        {
            lock (sync)
                return cacheLoadError.Length > 0 ? cacheLoadError
                    : saveErrors.OrderBy(e => e.Key).Select(e => e.Value).FirstOrDefault() ?? "";
        }
    }

    public StatusMonitor(ProviderStatusClient client, UsageStore? store = null,
        Func<DateTimeOffset>? utcNow = null, TimeSpan? pollInterval = null,
        Func<DateTimeOffset, ScheduleSnapshot>? scheduleAt = null)
    {
        this.client = client;
        this.store = store;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.scheduleAt = scheduleAt ?? (instant => AgentSchedule.GetSnapshot(instant));
        interval = pollInterval ?? PollInterval;
    }

    public IReadOnlyList<ProviderStatus> Snapshot()
    {
        lock (sync)
            return states.Values.OrderBy(s => s.Provider).Select(s => WithFreshness(s, utcNow())).ToArray();
    }

    internal static ProviderStatus WithFreshness(ProviderStatus state, DateTimeOffset now)
    {
        // A pre-upgrade assessed cache supplies an evidenced timestamp; do not infer one
        // for an already uncertain/stale cache whose receipt time may be much newer.
        if (state.Status is not (OfficialStatus.Unknown or OfficialStatus.Stale) && state.LastKnownStatusUtc is null &&
            (state.LastKnownStatus is null || state.LastKnownStatus == state.Status))
            state = state with { LastKnownStatus = state.Status, LastKnownStatusUtc = state.LastSuccessfulCheckUtc };
        if (state.Status == OfficialStatus.Stale) return state;
        if (state.LastSuccessfulCheckUtc is { } success && now - success >= StaleAfter)
            return state with
            {
                Status = OfficialStatus.Stale,
                LastKnownStatus = state.LastKnownStatus ??
                    (state.Status == OfficialStatus.Unknown ? null : state.Status),
                LastKnownStatusUtc = state.LastKnownStatusUtc ??
                    (state.Status == OfficialStatus.Unknown ? null : state.LastSuccessfulCheckUtc),
                // A cached outage is history, not evidence that the outage is still active.
                RelevantComponent = "",
                IncidentId = "",
                IncidentTitle = "",
                Reason = "공식 응답 수신 성공 후 15분 이상 경과" +
                    (state.CheckedAtUtc == state.LastSuccessfulCheckUtc ? "" : " · " + state.Reason)
            };
        return state;
    }

    public void Start() => runner ??= RunAsync();

    // Clicks during a refresh are ignored. A request that must not be lost (resume from
    // sleep while a pre-suspend pass is still in flight) stays pending and runs one
    // fresh pass right after the current one.
    public void RequestRefresh(bool queueWhileRefreshing = false)
    {
        if (!lifetime.IsCancellationRequested && (queueWhileRefreshing || !IsRefreshing) && wake.CurrentCount == 0)
        {
            try { wake.Release(); }
            catch (SemaphoreFullException) { /* coalesce repeated clicks */ }
        }
    }

    private async Task RunAsync()
    {
        try
        {
            if (store is not null)
            {
                try
                {
                    await store.InitializeAsync(lifetime.Token).ConfigureAwait(false);
                    var cached = await store.ReadLatestStatusesAsync(lifetime.Token).ConfigureAwait(false);
                    lock (sync)
                        foreach (var state in cached)
                            states[state.Provider] = WithFreshness(state, utcNow());
                    RaiseChanged();
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception error) { lock (sync) cacheLoadError = StorageMessage(error); }
            }
            while (!lifetime.IsCancellationRequested)
            {
                await RefreshOnceAsync(lifetime.Token).ConfigureAwait(false);
                lock (sync) nextRefresh = utcNow() + interval;
                RaiseChanged();
                await wake.WaitAsync(interval, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    internal async Task RefreshOnceAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        if (!await refreshGate.WaitAsync(0, linked.Token).ConfigureAwait(false))
            return;
        try
        {
            lock (sync) { refreshing = true; nextRefresh = null; }
            RaiseChanged();
            await Task.WhenAll(Enum.GetValues<ProviderKind>().Select(p => FetchOneAsync(p, linked.Token))).ConfigureAwait(false);
        }
        finally
        {
            lock (sync) refreshing = false;
            refreshGate.Release();
            RaiseChanged();
        }
    }

    private async Task FetchOneAsync(ProviderKind provider, CancellationToken token)
    {
        ProviderStatus previous;
        lock (sync) previous = states[provider];
        ProviderStatus result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(RequestTimeout);
            var fetched = await client.FetchAsync(provider, timeout.Token).ConfigureAwait(false);
            var now = utcNow();
            bool assessed = fetched.Status is not (OfficialStatus.Unknown or OfficialStatus.Stale);
            result = fetched with
            {
                CheckedAtUtc = now,
                LastSuccessfulCheckUtc = now,
                LastKnownStatus = assessed ? fetched.Status : previous.LastKnownStatus,
                LastKnownStatusUtc = assessed ? now : previous.LastKnownStatusUtc
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            result = Failure(previous, utcNow(), "조회 시간 초과");
        }
        catch (Exception error)
        {
            result = Failure(previous, utcNow(), "공식 상태 응답 수신 실패: " + Short(error.Message));
        }
        lock (sync) states[provider] = result;
        if (store is not null)
        {
            try
            {
                var schedule = scheduleAt(result.CheckedAtUtc);
                await store.SaveProviderAsync(result, schedule, RecommendationPolicy.Calculate(schedule.State, result.Status), token).ConfigureAwait(false);
                lock (sync) { saveErrors.Remove(provider); cacheLoadError = ""; }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { lock (sync) saveErrors[provider] = StorageMessage(error); }
        }
        RaiseChanged();
    }

    private static ProviderStatus Failure(ProviderStatus previous, DateTimeOffset now, string reason) =>
        WithFreshness(previous with
        {
            Status = OfficialStatus.Unknown,
            CheckedAtUtc = now,
            Reason = reason,
            RelevantComponent = "",
            IncidentId = "",
            IncidentTitle = "",
            AssessmentIssue = "",
            UncertainIncidentId = "",
            UncertainIncidentTitle = "",
            LastKnownStatus = previous.LastKnownStatus ??
                (previous.Status is OfficialStatus.Unknown or OfficialStatus.Stale ? null : previous.Status)
        }, now);

    // A failing display subscriber must not end background polling or make shutdown
    // throw; the UI timer redraws from Snapshot() on its next tick.
    private void RaiseChanged()
    {
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch (Exception) { /* Isolated per subscriber. */ }
        }
    }

    private static string StorageMessage(Exception error) => "로컬 저장 확인 필요: " + Short(error.Message);

    private static string Short(string text) => text.Length <= 180 ? text : text[..180];

    public async Task StopAsync()
    {
        lifetime.Cancel();
        if (runner is not null)
            await runner.ConfigureAwait(false);
        await refreshGate.WaitAsync().ConfigureAwait(false);
        refreshGate.Release();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        // Normal shutdown has awaited StopAsync before disposing owned HTTP resources.
        // A still-running runner keeps using these; they hold no OS handles here (no
        // CancelAfter, no AvailableWaitHandle), so leaving them to the GC is safe.
        if (runner is null || runner.IsCompleted)
        {
            lifetime.Dispose();
            refreshGate.Dispose();
            wake.Dispose();
        }
    }
}
