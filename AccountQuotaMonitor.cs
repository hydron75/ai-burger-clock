using System.Text.Json.Serialization;

namespace AiBurgerClock;

internal sealed record QuotaState(QuotaProvider Provider, QuotaReading? Reading = null,
    DateTimeOffset? CheckedAtUtc = null, DateTimeOffset? LastSuccessfulCheckUtc = null,
    DateTimeOffset? NextCheckUtc = null, bool IsRefreshing = false, bool IsPrevious = false,
    string CacheError = "", long ContinuityId = 0, ClaudeAuthCheck? ClaudeAuth = null,
    QuotaFailure? Failure = null, QuotaPresentationPolicy? Presentation = null,
    QuotaSourceMode Mode = QuotaSourceMode.Active, bool HasResultSinceModeChange = false);

// Only normalized quota numbers/timestamps are cached, never CLI output or credentials.
internal sealed record QuotaCache(int Version, QuotaReading Reading, DateTimeOffset SuccessfulAtUtc,
    IReadOnlyList<DateTimeOffset> ResetAnchors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ResetAnchorPolicyVersion = 0);

internal sealed partial class AccountQuotaMonitor : IDisposable
{
    private sealed class SourceRun(IQuotaSource source, QuotaSourceMode mode)
    {
        internal readonly IQuotaSource Source = source;
        internal QuotaState State = InitialState(source,
            source is UnavailableQuotaSource ? QuotaSourceMode.Unavailable : mode);
        internal readonly SemaphoreSlim Wake = new(0, 1);
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal DateTimeOffset[] Anchors = [];
        internal int Failures;
        internal long Sequence;
        internal long ReadingContinuity = 1;
        internal long Generation;
        internal bool ManualRequested;
        internal DateTimeOffset? LastReadStartedUtc;
        internal DateTimeOffset? DeferredUntilUtc;
        internal CancellationTokenSource? ActiveRead;
        internal Task? ReadCancellation;
    }

    private readonly Func<DateTimeOffset> utcNow;
    private readonly TimeZoneInfo zone;
    private readonly QuotaPollingDiagnosticLog? pollingLog;
    private readonly QuotaEarlyResetDiagnosticLog? earlyResetLog;
    private readonly object sync = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<QuotaProvider, SourceRun> sources;
    private readonly bool cancelLifetimeAsynchronously;
    private Task? runner;
    private Task? lifetimeCancellation;
    private bool disposed;
    public event Action? Changed;
    // Invoked once per accepted live observation, outside the state lock and
    // before releasing the provider gate. Handlers must not wait on shutdown.
    public event Action<QuotaObservation>? Succeeded;

    public AccountQuotaMonitor(IAccountQuotaClient client, UsageStore? store = null, Func<DateTimeOffset>? utcNow = null,
        QuotaPollingDiagnosticLog? diagnosticLog = null, QuotaEarlyResetDiagnosticLog? earlyResetDiagnosticLog = null,
        IGeminiUsagePageReader? geminiWebReader = null, bool geminiWebEnabled = false, TimeZoneInfo? geminiWebTimeZone = null)
        : this(CreateSources(client, store, geminiWebReader, geminiWebEnabled), store, utcNow, diagnosticLog,
            earlyResetDiagnosticLog, client as IClaudeAuthClient, geminiWebTimeZone)
    { }

    internal AccountQuotaMonitor(IEnumerable<(IQuotaSource Source, QuotaSourceMode Mode)> registration, UsageStore? store = null,
        Func<DateTimeOffset>? utcNow = null, QuotaPollingDiagnosticLog? diagnosticLog = null,
        QuotaEarlyResetDiagnosticLog? earlyResetDiagnosticLog = null, IClaudeAuthClient? claudeAuthClient = null,
        TimeZoneInfo? zone = null)
    {
        sources = registration.ToDictionary(item => item.Source.Provider, item => new SourceRun(item.Source, item.Mode));
        this.claudeAuthClient = claudeAuthClient;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.zone = zone ?? TimeZoneInfo.Local;
        pollingLog = diagnosticLog ?? CreatePollingLog(store);
        earlyResetLog = earlyResetDiagnosticLog ?? CreateEarlyResetLog(store);
        // A WebView cancellation callback can dispatch to a UI thread.
        cancelLifetimeAsynchronously = sources.Values.Any(source => source.Source.Traits.CancelLifetimeAsynchronously);
    }

    internal static IEnumerable<(IQuotaSource Source, QuotaSourceMode Mode)> CreateSources(IAccountQuotaClient client,
        UsageStore? store, IGeminiUsagePageReader? web, bool webEnabled,
        IAntigravityUsagePageReader? antigravity = null, bool antigravityEnabled = false,
        Func<QuotaReadiness>? antigravityReadiness = null)
    {
        yield return (new CliQuotaSource(QuotaProvider.Codex, client, store), QuotaSourceMode.Active);
        yield return (new CliQuotaSource(QuotaProvider.Claude, client, store), QuotaSourceMode.Active);
        yield return web is null
            ? (new UnavailableQuotaSource(QuotaProvider.Gemini), QuotaSourceMode.Unavailable)
            : (new GeminiWebQuotaSource(web, store), webEnabled ? QuotaSourceMode.Active : QuotaSourceMode.Off);
        yield return (new CliQuotaSource(QuotaProvider.Xai, client, store), QuotaSourceMode.Active);
        yield return antigravity is null
            ? (new UnavailableQuotaSource(QuotaProvider.Antigravity), QuotaSourceMode.Unavailable)
            : (new AntigravityWebQuotaSource(antigravity, store, antigravityReadiness),
                web is not null && webEnabled && antigravityEnabled ? QuotaSourceMode.Active : QuotaSourceMode.Off);
    }

    private static QuotaState InitialState(IQuotaSource source, QuotaSourceMode mode) =>
        new(source.Provider, ContinuityId: 1, Mode: mode, Presentation: source.Traits.Presentation);

    internal bool EmitsObservations(QuotaProvider provider) => sources[provider].Source.Traits.EmitsObservations;

    internal void RecordEarlyResetDecision(QuotaObservation observation, string outcome, string reason)
    {
        if (observation.DiagnosticAttemptId is { } attemptId)
            earlyResetLog?.TryUpdate(attemptId, outcome, reason);
    }

    public IReadOnlyList<QuotaState> Snapshot()
    {
        lock (sync)
        {
            var now = utcNow();
            return QuotaProviders.All.Where(sources.ContainsKey).Select(provider => sources[provider].State).Select(s => s with
            {
                IsPrevious = s.IsPrevious || (s.NextCheckUtc is { } next && now > next.AddMinutes(5)) ||
                    (s.LastSuccessfulCheckUtc is { } success && now < success.AddMinutes(-5)),
                ClaudeAuth = s.Provider == QuotaProvider.Claude ? LatestClaudeAuth : null
            }).ToArray();
        }
    }

    public void Start()
    {
        lock (sync)
        {
            if (disposed) return;
            runner ??= Task.WhenAll(sources.Values.Where(entry => entry.State.Mode != QuotaSourceMode.Unavailable)
                .Select(entry => Task.Run(() => RunAsync(entry)))
                .Concat(claudeAuthClient is null ? [] : [Task.Run(RunClaudeAuthAsync)]));
        }
    }

    public void RequestRefresh(bool queueWhileRefreshing = false)
    {
        lock (sync)
        {
            if (disposed || lifetime.IsCancellationRequested) return;
            if (!queueWhileRefreshing) QueueClaudeAuthCheck(ClaudeAuthRequest.Immediate);
            foreach (var entry in sources.Values)
                if (entry.State.Mode == QuotaSourceMode.Active && entry.Source.Traits.RefreshOnRecovery &&
                    (queueWhileRefreshing || !entry.State.IsRefreshing))
                    Signal(entry);
        }
    }

    internal void RequestSourceRefresh(QuotaProvider provider, bool afterSignIn = false)
    {
        lock (sync)
        {
            var entry = sources[provider];
            if (disposed || lifetime.IsCancellationRequested || entry.State.Mode != QuotaSourceMode.Active) return;
            if (!afterSignIn && entry.Source.Traits.ManualMinimumInterval is { } interval &&
                entry.LastReadStartedUtc is { } started && utcNow() - started < interval) return;
            if (afterSignIn) entry.DeferredUntilUtc = null;
            if (entry.Source.Traits.StickyManualRefresh) entry.ManualRequested = true;
            else if (entry.State.IsRefreshing) return;
            Signal(entry);
        }
    }

    internal void SetMode(QuotaProvider provider, QuotaSourceMode mode)
    {
        if (mode is not (QuotaSourceMode.Active or QuotaSourceMode.Off))
            throw new ArgumentOutOfRangeException(nameof(mode));
        lock (sync)
        {
            var entry = sources[provider];
            if (!entry.Source.Traits.CanSetMode)
                throw new InvalidOperationException("This quota source does not support changing its mode.");
            if (disposed || lifetime.IsCancellationRequested) return;
            entry.Generation++;
            entry.State = InitialState(entry.Source, mode);
            entry.Anchors = [];
            entry.Failures = 0;
            entry.ReadingContinuity = 1;
            entry.LastReadStartedUtc = null;
            entry.DeferredUntilUtc = null;
            entry.ManualRequested = mode == QuotaSourceMode.Active && entry.Source.Traits.StickyManualRefresh;
            entry.ReadCancellation ??= entry.ActiveRead?.CancelAsync();
            Signal(entry);
        }
        RaiseChanged();
    }

    internal Task ClearCacheAsync(QuotaProvider provider, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entry = sources[provider];
            if (entry.State.Mode != QuotaSourceMode.Off || entry.Generation == 0)
                throw new InvalidOperationException("Disable the quota source before clearing its cache.");
            return entry.Source.Cache.DeleteAsync(cancellationToken);
        }
    }

    private static void Signal(SourceRun entry)
    {
        if (entry.Wake.CurrentCount == 0) entry.Wake.Release();
    }

    private TimeSpan? Delay(SourceRun entry)
    {
        var state = entry.State;
        if (state.Mode != QuotaSourceMode.Active) return null;
        if (entry.DeferredUntilUtc is { } recheck)
        {
            TimeSpan wait = recheck - utcNow();
            if (wait > TimeSpan.Zero) return wait;
        }
        if (entry.ManualRequested) return TimeSpan.Zero;
        if (state.NextCheckUtc is { } next) return next - utcNow();
        if (!entry.Source.Traits.DueCheckBeforeRead) return AccountQuotaPolicy.DefaultInterval;
        return state.Failure?.Kind == QuotaFailureKind.LoginRequired ? null : TimeSpan.Zero;
    }

    private async Task RestoreCacheAsync(SourceRun entry)
    {
        lock (sync)
            if (entry.Generation != 0 || !entry.Source.Cache.RestoreOnStart(entry.State.Mode)) return;

        QuotaCache? cache = null;
        string error = "";
        try { cache = await entry.Source.Cache.ReadAsync(lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
        catch { error = "저장된 한도 정보 확인 실패"; }
        lock (sync)
        {
            var state = entry.State;
            if (entry.Generation != 0 || state.IsRefreshing || state.CheckedAtUtc is not null) return;
            if (cache is not null)
            {
                var reading = cache.Reading with { Windows = Array.AsReadOnly(cache.Reading.Windows.ToArray()) };
                entry.State = state with
                {
                    Reading = reading, LastSuccessfulCheckUtc = cache.SuccessfulAtUtc, IsPrevious = true, CacheError = error
                };
                entry.Anchors = cache.ResetAnchors.ToArray();
                entry.ReadingContinuity = state.ContinuityId;
            }
            else if (error.Length != 0) entry.State = state with { CacheError = error };
        }
        RaiseChanged();
    }

    private async Task RunAsync(SourceRun entry)
    {
        try
        {
            await RestoreCacheAsync(entry).ConfigureAwait(false);
            while (!lifetime.IsCancellationRequested)
            {
                await RefreshSourceIfDueAsync(entry.Source.Provider, lifetime.Token).ConfigureAwait(false);
                TimeSpan? delay;
                lock (sync) delay = Delay(entry);
                // Coalesce wakes and avoid catch-up bursts after time changes.
                if (delay is { } due)
                    await entry.Wake.WaitAsync(due > TimeSpan.Zero ? due : TimeSpan.FromSeconds(1), lifetime.Token).ConfigureAwait(false);
                else
                    await entry.Wake.WaitAsync(lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    internal async Task RefreshSourceIfDueAsync(QuotaProvider provider, CancellationToken cancellationToken = default)
    {
        var entry = sources[provider];
        var traits = entry.Source.Traits;
        lock (sync)
            if (entry.State.Mode != QuotaSourceMode.Active) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        if (!await entry.Gate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        QuotaObservation? observation = null;
        QuotaPollingDiagnostic? diagnostic = null;
        bool started = false, readStarted = false;
        long generation = 0;
        string? attemptId = null;
        DateTimeOffset attempted = default;
        try
        {
            lock (sync)
            {
                if (disposed || entry.State.Mode != QuotaSourceMode.Active ||
                    traits.DueCheckBeforeRead && (Delay(entry) is not { } due || due > TimeSpan.Zero)) return;
                bool dependencyNeedsLogin = traits.DependsOn is { } dependency && sources.TryGetValue(dependency, out var required) &&
                    required.State.Failure?.Kind == QuotaFailureKind.LoginRequired;
                if (dependencyNeedsLogin || entry.Source.CheckReadiness() == QuotaReadiness.Deferred)
                {
                    entry.DeferredUntilUtc = utcNow().AddMinutes(5);
                    return;
                }
                entry.DeferredUntilUtc = null;
                if (traits.ManualMinimumInterval is not null) entry.LastReadStartedUtc = utcNow();
                if (traits.EmitsObservations)
                {
                    attemptId = Guid.NewGuid().ToString("N");
                    attempted = utcNow().ToUniversalTime();
                }
                generation = entry.Generation;
                entry.ActiveRead = linked;
                entry.ManualRequested = false;
                entry.State = entry.State with { IsRefreshing = true, NextCheckUtc = null };
                started = true;
            }
            RaiseChanged();
            linked.Token.ThrowIfCancellationRequested();
            readStarted = true;
            DateTimeOffset? sourceTime = null;
            var result = await ReadAsync(entry.Source, () => sourceTime ??= utcNow().ToUniversalTime(),
                linked.Token).ConfigureAwait(false);
            var now = sourceTime ?? utcNow().ToUniversalTime();
            Task<string>? cacheWrite = null;
            lock (sync)
            {
                if (generation != entry.Generation) return;
                linked.Token.ThrowIfCancellationRequested();
                var old = entry.State;
                var reading = result.Reading;
                var failure = result.Failure;
                bool clear = failure?.Kind == QuotaFailureKind.LoginRequired && traits.Presentation.ClearSuccessOnLoginRequired;
                var known = clear ? null : reading ?? old.Reading;
                entry.Anchors = traits.Schedule.UpdateAnchors(now, old.Reading, known, entry.Anchors);
                entry.Failures = reading is null ? Math.Min(entry.Failures + 1, 100) : 0;
                var plan = traits.Schedule.Next(now, known, failure, entry.Failures, entry.Anchors);
                entry.State = old with
                {
                    Reading = known, CheckedAtUtc = now,
                    LastSuccessfulCheckUtc = clear ? null : reading is not null ? now : old.LastSuccessfulCheckUtc,
                    NextCheckUtc = plan?.NextCheckUtc, IsPrevious = reading is null && (traits.EmitsObservations || known is not null),
                    Failure = failure, HasResultSinceModeChange = true
                };
                if (traits.EmitsObservations && plan is not null)
                    diagnostic = new(provider.ToString(), attempted, now, plan.NextCheckUtc, plan.Reason.ToString(),
                        plan.SelectedResetUtc, reading is not null, entry.Failures, entry.Anchors.Length);
                if (reading is not null)
                {
                    // Queue the write on the store before allowing a concurrent off/delete.
                    // The SQLite work and the wait both run outside this lock.
                    cacheWrite = WriteCacheAsync(entry, reading, now, linked.Token);
                    if (traits.EmitsObservations)
                    {
                        QuotaPreviousSuccess? previous = old.Reading is not null && old.LastSuccessfulCheckUtc is { } previousAt
                            ? new(old.Reading, previousAt, entry.ReadingContinuity) : null;
                        observation = new(provider, ++entry.Sequence, now, reading, old.ContinuityId, previous, attemptId);
                        entry.ReadingContinuity = old.ContinuityId;
                    }
                }
            }
            if (cacheWrite is not null)
            {
                string cacheError = await cacheWrite.ConfigureAwait(false);
                lock (sync)
                    if (generation == entry.Generation) entry.State = entry.State with { CacheError = cacheError };
            }
            if (diagnostic is not null && !linked.IsCancellationRequested) pollingLog?.TryWrite(diagnostic);
        }
        catch (OperationCanceledException) when (!traits.EmitsObservations && linked.IsCancellationRequested) { }
        finally
        {
            bool publish = false;
            try
            {
                if (started)
                    publish = await FinishAsync(entry, generation, linked.Token, readStarted, attempted,
                        attemptId, observation).ConfigureAwait(false);
            }
            finally
            {
                entry.Gate.Release();
                if (provider == QuotaProvider.Claude && readStarted && !linked.IsCancellationRequested)
                    RequestClaudeAuthAfterQuota(publish ? observation : null);
                if (started || traits.EmitsObservations) RaiseChanged();
            }
        }
    }

    private async Task<QuotaReadResult> ReadAsync(IQuotaSource source, Func<DateTimeOffset> sourceClock, CancellationToken token)
    {
        using var timeout = source.Traits.Timeout is not null ? CancellationTokenSource.CreateLinkedTokenSource(token) : null;
        if (source.Traits.Timeout is { } limit) timeout!.CancelAfter(limit);
        var readToken = timeout?.Token ?? token;
        try
        {
            var result = await source.ReadAsync(sourceClock, zone, readToken).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.Reading is not { } reading) return result;
            // Preserve a classified CLI failure even if process cleanup ran past the outer deadline.
            readToken.ThrowIfCancellationRequested();
            return QuotaReadResult.Success(ValidateReading(reading, source.Provider, utcNow(), source.Traits.Presentation));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (readToken.IsCancellationRequested)
        { return QuotaReadResult.Failed(new(QuotaFailureKind.Timeout, "timeout")); }
        catch (QuotaReadException error) { return QuotaReadResult.Failed(error.Failure); }
    }

    internal static QuotaReading ValidateReading(QuotaReading candidate, QuotaProvider provider, DateTimeOffset now,
        QuotaPresentationPolicy policy)
    {
        try
        {
            var snapshot = candidate with
            {
                Windows = candidate.Windows is { } windows ? Array.AsReadOnly(windows.ToArray()) : throw new InvalidDataException()
            };
            UsageStore.ValidateQuotaCache(new(1, snapshot, now.ToUniversalTime(), []), provider);
            if (policy.AcceptForDisplay?.Invoke(snapshot) == false) throw new InvalidDataException();
            return snapshot;
        }
        catch (InvalidDataException) { throw new QuotaReadException(QuotaFailureKind.FormatChanged, "validate.reading"); }
    }

    private static async Task<string> WriteCacheAsync(SourceRun entry, QuotaReading reading, DateTimeOffset now, CancellationToken token)
    {
        try
        {
            await entry.Source.Cache.WriteAsync(reading, now, entry.Anchors, token).ConfigureAwait(false);
            return "";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return "한도 캐시 저장 실패 (화면 조회값은 유지)"; }
    }

    private async Task<bool> FinishAsync(SourceRun entry, long generation, CancellationToken token, bool readStarted,
        DateTimeOffset attempted, string? attemptId, QuotaObservation? observation)
    {
        bool publish;
        long continuity;
        lock (sync)
        {
            bool current = generation == entry.Generation;
            publish = current && observation is not null && !token.IsCancellationRequested;
            if (current && entry.Source.Traits.EmitsObservations && readStarted && !publish)
                entry.State = entry.State with { ContinuityId = entry.State.ContinuityId + 1 };
            continuity = entry.State.ContinuityId;
        }
        Task? cancellation = null;
        try
        {
            // Keep the gate and the refreshing state through the diagnostic flush.
            if (entry.Source.Traits.EmitsObservations && readStarted)
                earlyResetLog?.TryAppend(new(attemptId!, entry.Source.Provider.ToString(), attempted,
                    utcNow().ToUniversalTime(), publish, publish ? observation!.Sequence : null,
                    continuity, "Excluded", publish ? "NotEvaluated" : token.IsCancellationRequested ? "ReadCancelled" : "ReadFailed"));
        }
        finally
        {
            lock (sync)
            {
                entry.ActiveRead = null;
                cancellation = entry.ReadCancellation;
                entry.ReadCancellation = null;
                if (generation == entry.Generation) entry.State = entry.State with { IsRefreshing = false };
            }
            if (cancellation is not null) await ObserveCancellationAsync(cancellation).ConfigureAwait(false);
        }
        lock (sync) publish &= generation == entry.Generation && !token.IsCancellationRequested;
        if (publish) RaiseSucceeded(observation!);
        return publish;
    }

    private Task CancelLifetimeAsync() => lifetimeCancellation ??= ObserveCancellationAsync(lifetime.CancelAsync());

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (AggregateException) { /* A host cancellation callback cannot prevent shutdown. */ }
    }

    private static QuotaPollingDiagnosticLog? CreatePollingLog(UsageStore? store)
    {
        if (store is null) return null;
        try
        {
            // Derive from the actual store, not the user's global data path:
            // tests and isolated hosts must keep diagnostics beside their temp DB.
            string directory = Path.GetDirectoryName(Path.GetFullPath(store.DatabasePath))!;
            return new(Path.Combine(directory, "logs"));
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException
            or NotSupportedException or System.Security.SecurityException)
        {
            return null; // Optional diagnostics must not prevent monitor startup.
        }
    }

    private static QuotaEarlyResetDiagnosticLog? CreateEarlyResetLog(UsageStore? store)
    {
        if (store is null) return null;
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(store.DatabasePath))!;
            return new(Path.Combine(directory, "logs"));
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException
            or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private void RaiseSucceeded(QuotaObservation observation)
    {
        bool delivered = false;
        if (Succeeded is { } handlers)
            foreach (Action<QuotaObservation> handler in handlers.GetInvocationList().Cast<Action<QuotaObservation>>())
            {
                if (lifetime.IsCancellationRequested) break;
                try { handler(observation); delivered = true; } catch { /* Notification failures cannot stop polling. */ }
            }
        if (!delivered)
            RecordEarlyResetDecision(observation, "Excluded", "HostUnavailable");
    }

    private void RaiseChanged()
    {
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
            try { handler(); } catch { /* Display failures cannot stop polling. */ }
    }

    public async Task StopAsync()
    {
        Task? running;
        Task? cancellation = null;
        lock (sync)
        {
            if (disposed) return;
            if (cancelLifetimeAsynchronously) cancellation = CancelLifetimeAsync();
            else lifetime.Cancel();
            running = runner;
        }
        if (cancellation is not null) await cancellation.ConfigureAwait(false);
        if (running is not null) await running.ConfigureAwait(false);
        foreach (var entry in sources.Values) { await entry.Gate.WaitAsync().ConfigureAwait(false); entry.Gate.Release(); }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (cancelLifetimeAsynchronously) _ = CancelLifetimeAsync();
            else lifetime.Cancel();
            // Non-awaited forced shutdown may still have a callback using these primitives.
            // Normal ExitApplication awaits StopAsync; no wait handles are allocated here.
        }
    }
}
