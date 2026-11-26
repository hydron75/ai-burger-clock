namespace AiBurgerClock;

internal static class GeminiWebQuotaTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Gemini web quota: " + label);
            count++;
        }
        var now = GeminiUsagePageTests.Now;
        var loaded = new GeminiUsagePageLoad(GeminiUsagePage.Url, GeminiUsagePageTests.English
            .Replace("37%", "100%").Replace("61%", "97%").Replace("at 12:00 PM", "at 10:05 AM"));
        var later = new GeminiUsagePageLoad(GeminiUsagePage.Url, GeminiUsagePageTests.English);
        var signedOut = new GeminiUsagePageLoad("https://gemini.google.com/app", null);
        var reader = new FakeReader(loaded);
        var cli = new NoGeminiCli();
        var store = new UsageStore(Path.Combine(directory, "gemini-web-cache.db"));
        var legacy = new QuotaCache(1, new(QuotaProvider.Gemini,
            [new("synthetic-legacy", "Synthetic legacy", 32, now.AddDays(2), 10080)]), now, []);
        await store.SaveQuotaAsync(legacy);

        using (var missingReader = new AccountQuotaMonitor(cli, store, () => now, geminiWebEnabled: true))
        {
            missingReader.SetMode(QuotaProvider.Gemini, QuotaSourceMode.Active);
            missingReader.RequestSourceRefresh(QuotaProvider.Gemini);
            await missingReader.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
            var state = GeminiState(missingReader);
            Check(state == new QuotaState(QuotaProvider.Gemini, ContinuityId: 1, Mode: QuotaSourceMode.Unavailable, Presentation: QuotaPresentationPolicy.Cli), "saved opt-in alone leaves the old state unchanged");
            var disabled = QuotaPanelModel.Section(state, now);
            Check(disabled.Detail == QuotaPanelModel.GeminiUnavailableDetail && disabled.Rows.Single().Text == QuotaNames.GeminiUnavailableText,
                "missing reader keeps the exact disabled UI");
        }

        using var monitor = new AccountQuotaMonitor(cli, store, () => now, geminiWebReader: reader, geminiWebTimeZone: TimeZoneInfo.Utc);
        int observations = 0;
        monitor.Succeeded += observation => { if (observation.Provider == QuotaProvider.Gemini) observations++; };
        monitor.RequestSourceRefresh(QuotaProvider.Gemini);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        var off = GeminiState(monitor);
        Check(reader.Calls == 0 && off.Mode == QuotaSourceMode.Off && off.NextCheckUtc is null,
            "registration alone neither polls nor schedules");
        var offSection = QuotaPanelModel.Section(off, now);
        Check(offSection.Rows.Single() is { Text: "꺼짐 · 로그인하면 Gemini 앱 사용량 조회", UsedPercent: null, ElapsedPercent: null } &&
            offSection.Metadata.Text.Length == 0, "registered but off has its own notice and no quota or timestamps");
        Check(offSection.Detail.Contains("gemini.google.com/usage", StringComparison.Ordinal) &&
            offSection.Detail.Contains("Antigravity 한도와 별개", StringComparison.Ordinal) &&
            offSection.Detail.Contains("로그인하면 웹 사용량 조회가 켜집니다", StringComparison.Ordinal),
            "off detail names the source and explains how to enable");
        Check(await store.ReadGeminiWebQuotaAsync() is null, "web cache key starts empty despite legacy records");
        monitor.SetMode(QuotaProvider.Gemini, QuotaSourceMode.Active);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        var ok = GeminiState(monitor);
        Check(cli.GeminiCalls == 0 && reader.Calls == 1 && reader.Url == GeminiUsagePage.Url && reader.Script == GeminiUsagePage.ReadScript,
            "login signal goes through just the fixed URL and shared script");
        Check(ok is { HasResultSinceModeChange: true, Failure: null } && ok.LastSuccessfulCheckUtc == now &&
            ok.NextCheckUtc == now.AddMinutes(15) && !ok.IsPrevious, "100%, 97%, and near reset still schedule fifteen minutes");

        var displayed = QuotaPanelModel.Section(ok, now);
        Check(displayed.Rows.Select(row => row.NameText).SequenceEqual(["5시간", "주간"]) &&
            displayed.Rows.All(row => row.UsedPercent is not null && row.ElapsedPercent is not null), "both web windows use the shared rows and time layers");
        Check(displayed.Detail.Contains("gemini.google.com/usage", StringComparison.Ordinal) &&
            displayed.Detail.Contains("Antigravity 한도와 별개", StringComparison.Ordinal) &&
            displayed.Detail.Contains("자동 15분", StringComparison.Ordinal) &&
            displayed.Detail.Contains("수 분", StringComparison.Ordinal) &&
            displayed.Detail.Contains("로그인 필요", StringComparison.Ordinal), "web detail explains its source, delay, and session policy");
        var elapsed = QuotaPanelModel.Section(ok, now.AddMinutes(5));
        Check(elapsed.Rows[0] is { Tone: PanelTone.Muted, ElapsedPercent: null } &&
            elapsed.Rows[0].ValueText == "100% 사용 (이전) · 갱신 대기" &&
            elapsed.Detail.Contains("예정 시각 경과, 새 조회 필요", StringComparison.Ordinal),
            "an elapsed reset keeps the previous number and hides pacing");
        var cached = await store.ReadGeminiWebQuotaAsync();
        Check(cached is { Reading: not null, ResetAnchors.Count: 0 } &&
            cached.SuccessfulAtUtc == now && cached.Reading.Windows.SequenceEqual(ok.Reading!.Windows),
            "only normalized web values enter the separate cache");
        Check((await store.ReadQuotaAsync(QuotaProvider.Gemini))?.Reading.Windows.SequenceEqual(legacy.Reading.Windows) == true,
            "legacy key remains untouched");
        try
        {
            await store.SaveGeminiWebQuotaAsync(legacy);
            Check(false, "web cache must reject legacy IDs");
        }
        catch (InvalidDataException)
        {
            Check(true, "web cache rejects legacy IDs");
        }

        reader.Load = _ => throw new InvalidOperationException("SYNTHETIC-PRIVATE-READER-ERROR");
        now = ok.NextCheckUtc!.Value.AddTicks(-1);
        monitor.RequestRefresh(queueWhileRefreshing: true);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(reader.Calls == 1, "generic wake and a clock just before due cannot poll web");
        now = now.AddTicks(1);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        var failed = GeminiState(monitor);
        Check(reader.Calls == 2 && failed.Failure == new QuotaFailure(QuotaFailureKind.Unreachable, "gemini.load")
            && failed.IsPrevious && failed.NextCheckUtc == now.AddMinutes(15), "deadline is inclusive; load failure keeps the previous web cache and retries in fifteen minutes");
        Check(!QuotaPanelModel.Section(failed, now).Detail.Contains("SYNTHETIC-PRIVATE", StringComparison.Ordinal),
            "host exceptions are never copied into presentation");

        reader.Load = _ => Task.FromResult(later with { Text = null });
        now = failed.NextCheckUtc!.Value;
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        var changed = GeminiState(monitor);
        Check(reader.Calls == 3 && changed.NextCheckUtc == now.AddMinutes(15) && changed.Failure?.Kind == QuotaFailureKind.FormatChanged,
            "format failure also retries in fifteen minutes");
        Check(QuotaPanelModel.Section(changed, now).Rows.Single() is { Text: "형식 변경 · 조회 불가", UsedPercent: null },
            "format change is visible and has no numeric bars");

        reader.Load = _ => Task.FromResult(signedOut);
        monitor.RequestSourceRefresh(QuotaProvider.Gemini);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        var paused = GeminiState(monitor);
        Check(reader.Calls == 4 && paused.Failure?.Kind == QuotaFailureKind.LoginRequired && paused.Reading is null &&
            paused.LastSuccessfulCheckUtc is null && paused.NextCheckUtc is null, "login expiry hides values and has no automatic deadline");
        Check(QuotaPanelModel.Section(paused, now).Rows.Single().Text == "로그인 필요 · Gemini 앱 사용량",
            "login expiry uses the Gemini notice");
        now = now.AddHours(1);
        monitor.RequestRefresh();
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(reader.Calls == 4, "a generic wake or time passing does not resume a signed-out session");
        reader.Load = _ => Task.FromResult(later);
        monitor.RequestSourceRefresh(QuotaProvider.Gemini);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(reader.Calls == 5 && GeminiState(monitor) is { HasResultSinceModeChange: true, Failure: null } &&
            GeminiState(monitor).NextCheckUtc == now.AddMinutes(15), "manual Refresh resumes after verifying a signed-in session");
        reader.Load = _ => Task.FromResult(signedOut);
        monitor.RequestSourceRefresh(QuotaProvider.Gemini);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        reader.Load = _ => Task.FromResult(later);
        monitor.SetMode(QuotaProvider.Gemini, QuotaSourceMode.Active);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(reader.Calls == 7 && GeminiState(monitor) is { HasResultSinceModeChange: true, Failure: null }, "host login completion also resumes the paused schedule");

        var current = GeminiState(monitor).Reading!;
        var stale = new QuotaObservation(QuotaProvider.Gemini, 1, now, ok.Reading!, 1);
        var fresh = new QuotaObservation(QuotaProvider.Gemini, 2, now, current, 1);
        var recovery = new QuotaRecoveryNotifications();
        var early = new QuotaEarlyResetNotifications();
        Check(observations == 0 && recovery.Observe(stale, true, eligible: false) is null && recovery.Observe(fresh, true, eligible: false) is null &&
            early.Evaluate(stale, true, eligible: false).Reason == "QueryDisabled" && early.Evaluate(fresh, true, eligible: false).Alert is null,
            "live web values cannot reach recovery or early-reset callbacks");
        Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(
            new(QuotaProvider.Gemini, 2, "synthetic", "synthetic", [GeminiUsagePage.FiveHourId]), current, current, now, eligible: false) &&
            !QuotaPendingAlertPolicy.EarlyResetStillCurrent(new(QuotaProvider.Gemini, 2, 1, now, []), current, current, now, 1, eligible: false),
            "a pending Gemini alert is discarded even with a valid web cache");

        var completion = new TaskCompletionSource<GeminiUsagePageLoad>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Load = _ => completion.Task;
        monitor.RequestSourceRefresh(QuotaProvider.Gemini);
        Task inFlight = monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(GeminiState(monitor).IsRefreshing && reader.Calls == 8, "one read is in flight");
        monitor.RequestSourceRefresh(QuotaProvider.Gemini);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(reader.Calls == 8, "refresh while busy cannot start a second read");
        monitor.SetMode(QuotaProvider.Gemini, QuotaSourceMode.Off);
        completion.SetResult(later);
        await inFlight.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(reader.Cancellation.IsCancellationRequested && reader.Calls == 8 &&
            GeminiState(monitor) == off,
            "disabling cancels the active read and ignores its late result and pending refresh");
        var retained = await store.ReadGeminiWebQuotaAsync();
        Check(retained is not null && retained.SuccessfulAtUtc == now && retained.Reading.Windows.SequenceEqual(current.Windows),
            "turning off keeps the last valid web cache");

        var blocking = new TaskCompletionSource<QuotaState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new TaskCompletionSource<QuotaState>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var restarted = new AccountQuotaMonitor(cli, store, () => now, geminiWebReader: reader,
            geminiWebEnabled: true, geminiWebTimeZone: TimeZoneInfo.Utc);
        reader.Load = async token =>
        {
            _ = token.Register(() =>
            {
                cancellation.TrySetResult(GeminiState(restarted));
                throw new InvalidOperationException("Synthetic host cancellation error");
            });
            blocking.TrySetResult(GeminiState(restarted));
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return later;
        };
        restarted.Start();
        try
        {
            var startup = await blocking.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(startup.IsRefreshing && startup.IsPrevious && GeminiUsagePage.IsWebReading(startup.Reading) &&
                startup.LastSuccessfulCheckUtc == now, "registered and enabled runner restores only normalized web values as previous");
        }
        finally { await restarted.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        Check(cancellation.Task.IsCompletedSuccessfully && !GeminiState(restarted).IsRefreshing,
            "a host cancellation callback can read state and cannot interrupt cleanup even if it throws");
        Check(reader.Calls == 9 && cli.GeminiCalls == 0 &&
            (await store.ReadQuotaAsync(QuotaProvider.Gemini))?.Reading.Windows.SequenceEqual(legacy.Reading.Windows) == true,
            "start and shutdown preserve the legacy cache and never invoke a Gemini CLI");

        var late = new TaskCompletionSource<GeminiUsagePageLoad>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Load = _ => late.Task;
        monitor.SetMode(QuotaProvider.Gemini, QuotaSourceMode.Active);
        Task logoutRead = monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(reader.Calls == 10 && GeminiState(monitor).IsRefreshing, "simulate logout with a read in progress");
        monitor.SetMode(QuotaProvider.Gemini, QuotaSourceMode.Off);
        try { await monitor.ClearCacheAsync(QuotaProvider.Gemini); }
        finally
        {
            late.TrySetResult(later);
            await logoutRead.WaitAsync(TimeSpan.FromSeconds(5));
        }
        monitor.RequestSourceRefresh(QuotaProvider.Gemini);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
        Check(reader.Cancellation.IsCancellationRequested && reader.Calls == 10 && GeminiState(monitor) == off,
            "logout leaves the registered reader off and ignores refresh");
        Check(await store.ReadGeminiWebQuotaAsync() is null, "a late result cannot restore the deleted web cache");
        await monitor.ClearCacheAsync(QuotaProvider.Gemini);
        var preserved = await store.ReadQuotaAsync(QuotaProvider.Gemini);
        Check(preserved is not null && preserved.SuccessfulAtUtc == legacy.SuccessfulAtUtc &&
            preserved.Reading.Windows.SequenceEqual(legacy.Reading.Windows),
            "repeated logout does not change the legacy cache");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return count;
    }

    private static QuotaState GeminiState(AccountQuotaMonitor monitor) =>
        monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Gemini);

    private sealed class FakeReader(GeminiUsagePageLoad initial) : IGeminiUsagePageReader
    {
        public Func<CancellationToken, Task<GeminiUsagePageLoad>> Load = _ => Task.FromResult(initial);
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        public string? Script { get; private set; }
        public CancellationToken Cancellation { get; private set; }

        public Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken)
        {
            Calls++;
            Url = url;
            Script = script;
            Cancellation = cancellationToken;
            return Load(cancellationToken);
        }
    }

    private sealed class NoGeminiCli : IAccountQuotaClient
    {
        private int geminiCalls;
        public int GeminiCalls => Volatile.Read(ref geminiCalls);

        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            if (provider == QuotaProvider.Gemini) Interlocked.Increment(ref geminiCalls);
            throw new InvalidDataException("Synthetic CLI fixture has no reading");
        }
    }
}
