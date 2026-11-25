using System.Text.Json.Serialization;

namespace AiBurgerClock;

internal sealed record QuotaState(QuotaProvider Provider, QuotaReading? Reading = null,
    DateTimeOffset? CheckedAtUtc = null, DateTimeOffset? LastSuccessfulCheckUtc = null,
    DateTimeOffset? NextCheckUtc = null, bool IsRefreshing = false, bool IsPrevious = false,
    string Error = "", string CacheError = "", long ContinuityId = 0, GeminiWebQuotaState? GeminiWeb = null,
    ClaudeAuthCheck? ClaudeAuth = null, QuotaFailure? Failure = null)
{
    // Keep the old initializer for native smoke fixtures until P2. Production
    // states get their compatibility text from a classified failure.
    private readonly string legacyError = Error;
    internal QuotaFailure? ReportedFailure => Failure ?? GeminiUsagePage.ClassifyFailure(GeminiWeb?.Outcome, GeminiWeb?.Diagnostic);
    public string Error
    {
        get => ReportedFailure is { } failure ? QuotaPanelModel.FailureMessage(Provider, failure.Kind) : legacyError;
        init => legacyError = value;
    }
}

// Only normalized quota numbers/timestamps are cached, never CLI output or credentials.
internal sealed record QuotaCache(int Version, QuotaReading Reading, DateTimeOffset SuccessfulAtUtc,
    IReadOnlyList<DateTimeOffset> ResetAnchors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ResetAnchorPolicyVersion = 0);

internal sealed partial class AccountQuotaMonitor : IDisposable
{
    private readonly IAccountQuotaClient client;
    private readonly UsageStore? store;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly QuotaPollingDiagnosticLog? pollingLog;
    private readonly QuotaEarlyResetDiagnosticLog? earlyResetLog;
    private readonly object sync = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<QuotaProvider, QuotaState> states = Enum.GetValues<QuotaProvider>()
        .ToDictionary(p => p, p => new QuotaState(p, ContinuityId: 1));
    private readonly Dictionary<QuotaProvider, SemaphoreSlim> wakes = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => new SemaphoreSlim(0, 1));
    private readonly Dictionary<QuotaProvider, SemaphoreSlim> gates = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => new SemaphoreSlim(1, 1));
    private readonly Dictionary<QuotaProvider, DateTimeOffset[]> anchors = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => Array.Empty<DateTimeOffset>());
    private readonly Dictionary<QuotaProvider, int> failures = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => 0);
    private readonly Dictionary<QuotaProvider, long> sequences = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => 0L);
    // Generation of each last successful reading, not an account identity. Failed
    // attempts advance the state generation without re-stamping the previous read.
    private readonly Dictionary<QuotaProvider, long> readingContinuities = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => 1L);
    private Task? runner;
    private bool disposed;
    public event Action? Changed;
    // Unlike Changed, this is emitted exactly once for each accepted live response,
    // never for cached values, refresh starts, failures or cancelled requests.
    // Invoked synchronously by the refresh task, outside the state lock but while
    // holding the provider gate. Dispatch UI work asynchronously and return promptly;
    // slow callbacks delay this provider's next refresh. Never synchronously wait for
    // StopAsync or shutdown/disposal that awaits it here: shutdown waits for this callback.
    // RefreshOnceAsync for the same provider is skipped while this callback holds the gate.
    public event Action<QuotaObservation>? Succeeded;

    public AccountQuotaMonitor(IAccountQuotaClient client, UsageStore? store = null, Func<DateTimeOffset>? utcNow = null,
        QuotaPollingDiagnosticLog? diagnosticLog = null, QuotaEarlyResetDiagnosticLog? earlyResetDiagnosticLog = null,
        IGeminiUsagePageReader? geminiWebReader = null, bool geminiWebEnabled = false, TimeZoneInfo? geminiWebTimeZone = null)
    {
        this.client = client;
        claudeAuthClient = client as IClaudeAuthClient;
        this.store = store;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        pollingLog = diagnosticLog ?? CreatePollingLog(store);
        earlyResetLog = earlyResetDiagnosticLog ?? CreateEarlyResetLog(store);
        this.geminiWebReader = geminiWebReader;
        this.geminiWebTimeZone = geminiWebTimeZone;
        if (geminiWebReader is not null)
            states[QuotaProvider.Gemini] = NewGeminiWebState(geminiWebEnabled);
    }

    // Hosts finish the existing per-query row after dispatching to their UI thread.
    // This records a decision, not proof that the OS displayed an alert.
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
            return states.Values.OrderBy(s => s.Provider).Select(s => s with
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
            runner ??= Task.WhenAll(Enum.GetValues<QuotaProvider>().Where(QuotaNames.IsQueryEnabled)
                .Select(p => Task.Run(() => RunAsync(p)))
                .Concat(geminiWebReader is null ? [] : [Task.Run(RunGeminiWebAsync)])
                .Concat(claudeAuthClient is null ? [] : [Task.Run(RunClaudeAuthAsync)]));
        }
    }

    public void RequestRefresh(bool queueWhileRefreshing = false)
    {
        lock (sync)
        {
            if (disposed || lifetime.IsCancellationRequested) return;
            // Both hosts queue quota refreshes on resume/network recovery and use
            // the default for the manual Refresh command.
            if (!queueWhileRefreshing) QueueClaudeAuthCheck(ClaudeAuthRequest.Immediate);
            foreach (var provider in states.Keys)
                if (QuotaNames.IsQueryEnabled(provider) && (queueWhileRefreshing || !states[provider].IsRefreshing) && wakes[provider].CurrentCount == 0)
                    wakes[provider].Release();
        }
    }

    private async Task RunAsync(QuotaProvider provider)
    {
        try
        {
            if (store is not null)
            {
                try
                {
                    var cached = await store.ReadQuotaAsync(provider, lifetime.Token).ConfigureAwait(false);
                    if (cached is not null)
                        lock (sync)
                        {
                            var reading = cached.Reading with { Windows = Array.AsReadOnly(cached.Reading.Windows.ToArray()) };
                            states[provider] = new(provider, reading, LastSuccessfulCheckUtc: cached.SuccessfulAtUtc,
                                IsPrevious: true, ContinuityId: states[provider].ContinuityId);
                            // Single-account-per-provider assumption: cache v1 has no
                            // account identity or persisted failure generation. Rebind
                            // only its early-reset baseline to this monitor's generation.
                            readingContinuities[provider] = states[provider].ContinuityId;
                            // Legacy Gemini anchors have no window provenance and may
                            // contain a long chain from unused moving resets. Rebuild
                            // only that legacy cache's anchors from verified used windows.
                            // Marked caches retain genuine old reset +15m bands even
                            // after a newly successful reading reports zero usage.
                            anchors[provider] = provider == QuotaProvider.Gemini && cached.ResetAnchorPolicyVersion == 0
                                ? AccountQuotaPolicy.CurrentResetAnchors(cached.Reading.Windows, provider).ToArray()
                                : cached.ResetAnchors.ToArray();
                        }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch { lock (sync) states[provider] = states[provider] with { CacheError = "저장된 한도 정보 확인 실패" }; }
                RaiseChanged();
            }
            while (!lifetime.IsCancellationRequested)
            {
                await RefreshOnceAsync(provider, lifetime.Token).ConfigureAwait(false);
                DateTimeOffset next;
                lock (sync) next = states[provider].NextCheckUtc ?? utcNow() + AccountQuotaPolicy.DefaultInterval;
                TimeSpan delay = next - utcNow();
                // No catch-up bursts after resume/clock changes. A wake is coalesced to one pass.
                await wakes[provider].WaitAsync(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    internal async Task RefreshOnceAsync(QuotaProvider provider, CancellationToken cancellationToken = default)
    {
        if (!QuotaNames.IsQueryEnabled(provider)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        if (!await gates[provider].WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        QuotaObservation? observation = null;
        QuotaPollingDiagnostic? diagnostic = null;
        bool readStarted = false;
        string attemptId = "";
        DateTimeOffset attemptedAtUtc = default;
        try
        {
            // Even an injected clock failure must release the acquired provider gate.
            attemptId = Guid.NewGuid().ToString("N");
            attemptedAtUtc = utcNow().ToUniversalTime();
            lock (sync) states[provider] = states[provider] with { IsRefreshing = true, NextCheckUtc = null };
            RaiseChanged();
            QuotaReading? reading = null;
            QuotaFailure? failure = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                timeout.CancelAfter(provider == QuotaProvider.Xai ? AccountQuotaClient.GrokTimeout : TimeSpan.FromSeconds(40));
                linked.Token.ThrowIfCancellationRequested();
                readStarted = true;
                var candidate = await client.ReadAsync(provider, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                reading = AccountQuotaClient.ValidateReading(candidate, provider, utcNow());
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                reading = null; // Invalid responses must never replace the last verified reading.
                failure = AccountQuotaClient.FailureFromRead(ex);
            }
            var now = utcNow().ToUniversalTime();
            QuotaCache? cache = null;
            lock (sync)
            {
                linked.Token.ThrowIfCancellationRequested();
                var old = states[provider];
                var known = reading ?? old.Reading;
                // Keep the old reset's +15-minute band even when the server returns the next reset.
                anchors[provider] = AccountQuotaPolicy.CoalesceResetAnchors(anchors[provider]
                    .Concat(AccountQuotaPolicy.CurrentResetAnchors(old.Reading?.Windows ?? [], provider))
                    .Concat(AccountQuotaPolicy.CurrentResetAnchors(known?.Windows ?? [], provider))
                    .Where(r => r.AddMinutes(15) >= now), maximumCount: 64);
                var plan = AccountQuotaPolicy.GetNextCheckPlan(now, now, known?.Windows ?? [], anchors[provider], provider);
                failures[provider] = reading is null ? Math.Min(failures[provider] + 1, 100) : 0;
                if (reading is null)
                {
                    var retry = now + AccountQuotaPolicy.GetFailureRetryDelay(failures[provider], known?.Windows ?? []);
                    if (retry <= plan.NextCheckUtc) plan = new(retry, QuotaCheckReason.FailureRetry);
                }
                states[provider] = old with
                {
                    Reading = known, CheckedAtUtc = now, LastSuccessfulCheckUtc = reading is not null ? now : old.LastSuccessfulCheckUtc,
                    NextCheckUtc = plan.NextCheckUtc, IsPrevious = reading is null, Failure = failure
                };
                diagnostic = new(provider.ToString(), attemptedAtUtc, now, plan.NextCheckUtc, plan.Reason.ToString(),
                    plan.SelectedResetUtc, reading is not null, failures[provider], anchors[provider].Length);
                if (reading is not null)
                {
                    cache = new(1, reading, now, anchors[provider], provider == QuotaProvider.Gemini
                        ? AccountQuotaPolicy.ResetAnchorPolicyVersion : 0);
                    QuotaPreviousSuccess? previous = old.Reading is not null && old.LastSuccessfulCheckUtc is { } previousAt
                        ? new(old.Reading, previousAt, readingContinuities[provider]) : null;
                    observation = new(provider, ++sequences[provider], now, reading, old.ContinuityId, previous, attemptId);
                    readingContinuities[provider] = old.ContinuityId;
                }
            }
            if (store is not null && cache is not null)
            {
                try
                {
                    await store.SaveQuotaAsync(cache, linked.Token).ConfigureAwait(false);
                    lock (sync) states[provider] = states[provider] with { CacheError = "" };
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch { lock (sync) states[provider] = states[provider] with { CacheError = "한도 캐시 저장 실패 (화면 조회값은 유지)" }; }
            }
            if (diagnostic is not null && !linked.IsCancellationRequested)
                pollingLog?.TryWrite(diagnostic);
        }
        finally
        {
            bool publishSuccess;
            long continuityId;
            lock (sync)
            {
                // Commit completion once. A valid response whose cache write was
                // cancelled keeps its verified values/time, but emits no success and
                // cannot bridge a later early-reset comparison or a queued UI alert.
                // Cache/diagnostic write failures alone do not invalidate a live read.
                publishSuccess = observation is not null && !linked.IsCancellationRequested;
                var state = states[provider];
                states[provider] = state with
                {
                    ContinuityId = readStarted && !publishSuccess ? state.ContinuityId + 1 : state.ContinuityId
                };
                continuityId = states[provider].ContinuityId;
            }
            try
            {
                // Separate versioned file: never extend the older strict polling
                // schema. Append before callbacks so a fast host can update this row.
                try
                {
                    if (readStarted)
                        earlyResetLog?.TryAppend(new(attemptId, provider.ToString(), attemptedAtUtc,
                            utcNow().ToUniversalTime(), publishSuccess, publishSuccess ? observation!.Sequence : null,
                            continuityId, "Excluded", publishSuccess ? "NotEvaluated" : linked.IsCancellationRequested ? "ReadCancelled" : "ReadFailed"));
                }
                finally
                {
                    // Logging is part of completion. Do not expose an idle state
                    // throughout a flush while this provider's gate is still busy.
                    lock (sync) states[provider] = states[provider] with { IsRefreshing = false };
                }
                // Outside the state lock, but before releasing the provider gate:
                // fast host callbacks see a completed refresh and cannot receive
                // this provider's later response before the current one.
                if (publishSuccess)
                    RaiseSucceeded(observation!);
            }
            finally
            {
                gates[provider].Release();
                if (provider == QuotaProvider.Claude && readStarted && !linked.IsCancellationRequested)
                    RequestClaudeAuthAfterQuota(publishSuccess ? observation : null);
                RaiseChanged();
            }
        }
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
            if (geminiWebReader is null) lifetime.Cancel();
            else cancellation = CancelGeminiWebLifetimeAsync();
            running = runner;
        }
        if (cancellation is not null) await cancellation.ConfigureAwait(false);
        if (running is not null) await running.ConfigureAwait(false);
        foreach (var gate in gates.Values) { await gate.WaitAsync().ConfigureAwait(false); gate.Release(); }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (geminiWebReader is null) lifetime.Cancel();
            else _ = CancelGeminiWebLifetimeAsync();
            // Non-awaited forced shutdown may still have a callback using these primitives.
            // Normal ExitApplication awaits StopAsync; no wait handles are allocated here.
        }
    }
}
