namespace AiBurgerClock;

internal sealed record QuotaState(QuotaProvider Provider, QuotaReading? Reading = null,
    DateTimeOffset? CheckedAtUtc = null, DateTimeOffset? LastSuccessfulCheckUtc = null,
    DateTimeOffset? NextCheckUtc = null, bool IsRefreshing = false, bool IsPrevious = false,
    string Error = "", string CacheError = "");

// Only normalized quota numbers/timestamps are cached, never CLI output or credentials.
internal sealed record QuotaCache(int Version, QuotaReading Reading, DateTimeOffset SuccessfulAtUtc,
    IReadOnlyList<DateTimeOffset> ResetAnchors);

internal sealed class AccountQuotaMonitor : IDisposable
{
    private readonly IAccountQuotaClient client;
    private readonly UsageStore? store;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly object sync = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<QuotaProvider, QuotaState> states = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, p => new QuotaState(p));
    private readonly Dictionary<QuotaProvider, SemaphoreSlim> wakes = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => new SemaphoreSlim(0, 1));
    private readonly Dictionary<QuotaProvider, SemaphoreSlim> gates = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => new SemaphoreSlim(1, 1));
    private readonly Dictionary<QuotaProvider, DateTimeOffset[]> anchors = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => Array.Empty<DateTimeOffset>());
    private readonly Dictionary<QuotaProvider, int> failures = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, _ => 0);
    private Task? runner;
    private bool disposed;
    public event Action? Changed;

    public AccountQuotaMonitor(IAccountQuotaClient client, UsageStore? store = null, Func<DateTimeOffset>? utcNow = null)
    {
        this.client = client;
        this.store = store;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
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
                            states[provider] = new(provider, cached.Reading, LastSuccessfulCheckUtc: cached.SuccessfulAtUtc, IsPrevious: true);
                            anchors[provider] = cached.ResetAnchors.ToArray();
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
                lock (sync) next = states[provider].NextCheckUtc ?? utcNow().AddHours(6);
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
                var candidate = await client.ReadAsync(provider, timeout.Token).ConfigureAwait(false);
                UsageStore.ValidateQuotaCache(new(1, candidate, utcNow().ToUniversalTime(), []), provider);
                reading = candidate;
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
                var old = states[provider];
                var known = reading ?? old.Reading;
                // Keep the old reset's +15-minute band even when the server returns the next reset.
                anchors[provider] = anchors[provider].Concat(old.Reading?.Windows.Where(w => w.ResetsAtUtc.HasValue).Select(w => w.ResetsAtUtc!.Value) ?? [])
                    .Concat(known?.Windows.Where(w => w.ResetsAtUtc.HasValue).Select(w => w.ResetsAtUtc!.Value) ?? [])
                    .Where(r => r.AddMinutes(15) >= now).Distinct().OrderBy(r => r).Take(64).ToArray();
                var next = AccountQuotaPolicy.GetNextCheckUtc(now, now, known?.Windows ?? [], anchors[provider]);
                failures[provider] = reading is null ? Math.Min(failures[provider] + 1, 100) : 0;
                if (reading is null)
                {
                    var retry = now + AccountQuotaPolicy.GetFailureRetryDelay(failures[provider], known?.Windows ?? []);
                    if (retry < next) next = retry;
                }
                states[provider] = old with
                {
                    Reading = known, CheckedAtUtc = now, LastSuccessfulCheckUtc = reading is not null ? now : old.LastSuccessfulCheckUtc,
                    NextCheckUtc = next, IsPrevious = reading is null, Error = error
                };
                if (reading is not null) cache = new(1, reading, now, anchors[provider]);
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
        }
        finally
        {
            lock (sync) states[provider] = states[provider] with { IsRefreshing = false };
            gates[provider].Release();
            RaiseChanged();
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
