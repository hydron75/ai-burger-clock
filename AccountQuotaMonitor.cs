using System.Text.Json.Serialization;

namespace AiBurgerClock;

internal sealed record QuotaState(QuotaProvider Provider, QuotaReading? Reading = null,
    DateTimeOffset? CheckedAtUtc = null, DateTimeOffset? LastSuccessfulCheckUtc = null,
    DateTimeOffset? NextCheckUtc = null, bool IsRefreshing = false, bool IsPrevious = false,
    string Error = "", string CacheError = "", long ContinuityId = 0);

// Only normalized quota numbers/timestamps are cached, never CLI output or credentials.
internal sealed record QuotaCache(int Version, QuotaReading Reading, DateTimeOffset SuccessfulAtUtc,
    IReadOnlyList<DateTimeOffset> ResetAnchors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ResetAnchorPolicyVersion = 0);

internal sealed class AccountQuotaMonitor : IDisposable
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
        QuotaPollingDiagnosticLog? diagnosticLog = null, QuotaEarlyResetDiagnosticLog? earlyResetDiagnosticLog = null)
    {
        this.client = client;
        this.store = store;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        pollingLog = diagnosticLog ?? CreatePollingLog(store);
        earlyResetLog = earlyResetDiagnosticLog ?? CreateEarlyResetLog(store);
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
                    (s.LastSuccessfulCheckUtc is { } success && now < success.AddMinutes(-5))
            }).ToArray();
        }
    }

    public void Start()
    {
        lock (sync)
        {
            if (disposed) return;
            runner ??= Task.WhenAll(Enum.GetValues<QuotaProvider>().Select(p => Task.Run(() => RunAsync(p))));
        }
    }

    public void RequestRefresh(bool queueWhileRefreshing = false)
    {
        lock (sync)
        {
            if (disposed || lifetime.IsCancellationRequested) return;
            foreach (var provider in states.Keys)
                if ((queueWhileRefreshing || !states[provider].IsRefreshing) && wakes[provider].CurrentCount == 0)
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
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        if (!await gates[provider].WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        QuotaObservation? observation = null;
        QuotaPollingDiagnostic? diagnostic = null;
        bool readStarted = false;
        string attemptId = Guid.NewGuid().ToString("N");
        DateTimeOffset attemptedAtUtc = utcNow().ToUniversalTime();
        try
        {
            lock (sync) states[provider] = states[provider] with { IsRefreshing = true, NextCheckUtc = null };
            RaiseChanged();
            QuotaReading? reading = null;
            string error = "";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(40));
                linked.Token.ThrowIfCancellationRequested();
                readStarted = true;
                var candidate = await client.ReadAsync(provider, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                // Own the accepted windows: a client must not be able to mutate a
                // delivered observation or the monitor snapshot after returning.
                var snapshot = candidate with { Windows = Array.AsReadOnly(candidate.Windows.ToArray()) };
                UsageStore.ValidateQuotaCache(new(1, snapshot, utcNow().ToUniversalTime(), []), provider);
                reading = snapshot;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                reading = null; // Invalid responses must never replace the last verified reading.
                error = ex switch
                {
                    OperationCanceledException or TimeoutException => "한도 조회 시간 초과",
                    FileNotFoundException => "공식 CLI를 찾지 못했습니다. 설치 경로를 확인하세요.",
                    NotSupportedException when provider == QuotaProvider.Gemini => "agy CLI 1.3.1 이상이 필요합니다(2.0 미만 안정 버전).",
                    UnauthorizedAccessException => "공식 CLI 실행 권한을 확인하세요.",
                    InvalidDataException => "한도 응답 없음 · CLI 로그인과 사용량 화면을 확인하세요.",
                    _ => "한도 조회 실패 · 네트워크와 CLI 로그인을 확인하세요."
                };
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
                    NextCheckUtc = plan.NextCheckUtc, IsPrevious = reading is null, Error = error
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
        if (Succeeded is not { } handlers) return;
        foreach (Action<QuotaObservation> handler in handlers.GetInvocationList().Cast<Action<QuotaObservation>>())
        {
            if (lifetime.IsCancellationRequested) return;
            try { handler(observation); } catch { /* Notification failures cannot stop polling. */ }
        }
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
        lock (sync) { if (disposed) return; lifetime.Cancel(); running = runner; }
        if (running is not null) await running.ConfigureAwait(false);
        foreach (var gate in gates.Values) { await gate.WaitAsync().ConfigureAwait(false); gate.Release(); }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            // Non-awaited forced shutdown may still have a callback using these primitives.
            // Normal ExitApplication awaits StopAsync; no wait handles are allocated here.
        }
    }
}
