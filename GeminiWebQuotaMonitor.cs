namespace AiBurgerClock;

internal sealed partial class AccountQuotaMonitor
{
    private const QuotaProvider Gemini = QuotaProvider.Gemini;
    private readonly IGeminiUsagePageReader? geminiWebReader;
    private readonly TimeZoneInfo? geminiWebTimeZone;
    private bool geminiWebRefreshRequested;
    private long geminiWebGeneration;
    private CancellationTokenSource? geminiWebRead;
    private Task? geminiWebCancellation;
    private Task? geminiWebShutdown;

    // The optional WebView adapter can register UI callbacks on a linked token.
    // Cancel asynchronously, so they never run under the monitor state lock.
    private Task CancelGeminiWebLifetimeAsync() => geminiWebShutdown ??= ObserveCancellationAsync(lifetime.CancelAsync());

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (AggregateException) { /* A host cancellation callback cannot prevent shutdown. */ }
    }

    // A host calls true after its own successful login and false on disable or
    // logout. The default refresh, resume and network wakes remain CLI-only.
    internal void SetGeminiWebEnabled(bool enabled)
    {
        lock (sync)
        {
            if (geminiWebReader is null || disposed || lifetime.IsCancellationRequested) return;
            geminiWebGeneration++;
            states[Gemini] = NewGeminiWebState(enabled);
            geminiWebRefreshRequested = enabled;
            // Cancellation callbacks may dispatch to the host UI thread.
            geminiWebCancellation ??= geminiWebRead?.CancelAsync();
            WakeGeminiWeb();
        }
        RaiseChanged();
    }

    internal void RequestGeminiWebRefresh()
    {
        lock (sync)
        {
            if (disposed || lifetime.IsCancellationRequested || states[Gemini].GeminiWeb is not { IsEnabled: true }) return;
            geminiWebRefreshRequested = true;
            WakeGeminiWeb();
        }
    }

    private void WakeGeminiWeb()
    {
        if (wakes[Gemini].CurrentCount == 0) wakes[Gemini].Release();
    }

    private static QuotaState NewGeminiWebState(bool enabled) =>
        new(Gemini, ContinuityId: 1, GeminiWeb: new(IsEnabled: enabled));

    // Called under sync. Only a manual wake or a fresh login can leave the
    // login-required state. Failures and successes use the same fixed interval.
    private TimeSpan? GeminiWebDelay(DateTimeOffset now)
    {
        var state = states[Gemini];
        if (state.GeminiWeb is not { IsEnabled: true } web) return null;
        if (geminiWebRefreshRequested) return TimeSpan.Zero;
        if (web.Outcome == GeminiUsageOutcome.LoginRequired) return null;
        return state.NextCheckUtc is { } next ? next - now : TimeSpan.Zero;
    }

    private async Task RunGeminiWebAsync()
    {
        try
        {
            await RestoreGeminiWebCacheAsync().ConfigureAwait(false);
            while (!lifetime.IsCancellationRequested)
            {
                await RefreshGeminiWebIfDueAsync(lifetime.Token).ConfigureAwait(false);
                TimeSpan? delay;
                lock (sync) delay = GeminiWebDelay(utcNow());
                if (delay is { } due)
                    await wakes[Gemini].WaitAsync(due > TimeSpan.Zero ? due : TimeSpan.FromSeconds(1), lifetime.Token).ConfigureAwait(false);
                else
                    await wakes[Gemini].WaitAsync(lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task RestoreGeminiWebCacheAsync()
    {
        if (store is null) return;
        lock (sync)
            if (geminiWebGeneration != 0 || states[Gemini].GeminiWeb is not { IsEnabled: true }) return;

        QuotaCache? cache = null;
        string error = "";
        try { cache = await store.ReadGeminiWebQuotaAsync(lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
        catch { error = "저장된 한도 정보 확인 실패"; }
        lock (sync)
        {
            var state = states[Gemini];
            if (geminiWebGeneration != 0 || state.IsRefreshing || state.CheckedAtUtc is not null) return;
            states[Gemini] = state with
            {
                Reading = cache is null ? null : cache.Reading with { Windows = Array.AsReadOnly(cache.Reading.Windows.ToArray()) },
                LastSuccessfulCheckUtc = cache?.SuccessfulAtUtc,
                IsPrevious = cache is not null,
                CacheError = error
            };
        }
        RaiseChanged();
    }

    internal async Task RefreshGeminiWebIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (geminiWebReader is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        if (!await gates[Gemini].WaitAsync(0, linked.Token).ConfigureAwait(false)) return;

        bool started = false;
        long generation = 0;
        try
        {
            lock (sync)
            {
                if (disposed || GeminiWebDelay(utcNow()) is not { } due || due > TimeSpan.Zero) return;
                linked.Token.ThrowIfCancellationRequested();
                generation = geminiWebGeneration;
                geminiWebRead = linked;
                geminiWebRefreshRequested = false;
                states[Gemini] = states[Gemini] with { IsRefreshing = true, NextCheckUtc = null };
                started = true;
            }
            RaiseChanged();

            GeminiUsagePageLoad load;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                load = await geminiWebReader.LoadAsync(GeminiUsagePage.Url, GeminiUsagePage.ReadScript, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
            catch { load = GeminiUsagePageLoad.LoadFailed; }

            linked.Token.ThrowIfCancellationRequested();
            var now = utcNow().ToUniversalTime();
            var result = GeminiUsagePage.Parse(load, now, geminiWebTimeZone ?? TimeZoneInfo.Local);
            lock (sync)
            {
                if (generation != geminiWebGeneration || linked.IsCancellationRequested) return;
                var old = states[Gemini];
                bool loginRequired = result.Outcome == GeminiUsageOutcome.LoginRequired;
                var reading = loginRequired ? null : result.Reading ?? old.Reading;
                states[Gemini] = old with
                {
                    GeminiWeb = new(result.Outcome, result.Diagnostic),
                    Reading = reading,
                    IsPrevious = reading is not null && result.Reading is null,
                    CheckedAtUtc = now,
                    LastSuccessfulCheckUtc = loginRequired ? null : result.Reading is not null ? now : old.LastSuccessfulCheckUtc,
                    NextCheckUtc = loginRequired ? null : now.AddMinutes(GeminiUsagePage.PollMinutes)
                };
            }

            if (store is not null && result.Reading is { } success)
            {
                string cacheError = "";
                try
                {
                    await store.SaveGeminiWebQuotaAsync(new(1, success, now, []), linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch { cacheError = "한도 캐시 저장 실패 (화면 조회값은 유지)"; }
                lock (sync)
                    if (generation == geminiWebGeneration)
                        states[Gemini] = states[Gemini] with { CacheError = cacheError };
            }
            // This path delivers state via Changed. It does not produce CLI polling
            // log entries, early-reset decisions, or a quota Succeeded observation.
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally
        {
            Task? cancellation = null;
            if (started)
                lock (sync)
                {
                    geminiWebRead = null;
                    cancellation = geminiWebCancellation;
                    geminiWebCancellation = null;
                    if (generation == geminiWebGeneration)
                        states[Gemini] = states[Gemini] with { IsRefreshing = false };
                }
            if (cancellation is not null) await ObserveCancellationAsync(cancellation).ConfigureAwait(false);
            gates[Gemini].Release();
            if (started) RaiseChanged();
        }
    }
}
