using System.Collections.Concurrent;

namespace AiBurgerClock;

// Fixed policy regression: no agy discovery, process setup, cache use or notification.
internal static class GeminiQuotaClientTests
{
    internal static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string label) { count++; if (!condition) throw new InvalidOperationException("Gemini disabled: " + label); }
        var now = new DateTimeOffset(2032, 4, 5, 6, 0, 0, TimeSpan.Zero);
        var reading = new QuotaReading(QuotaProvider.Gemini, [new("synthetic-week", "Synthetic model", 100, now.AddDays(3), 10080)]);
        string directory = Directory.CreateTempSubdirectory("gemini-disabled-").FullName;
        try
        {
            string unusedWorking = Path.Combine(directory, "must-not-be-created");
            try { await new AccountQuotaClient(workingDirectory: unusedWorking).ReadAsync(QuotaProvider.Gemini, CancellationToken.None); Check(false, "direct client must refuse"); }
            catch (NotSupportedException error) { Check(error.Message == QuotaNames.GeminiUnavailableText, "client refuses before filesystem or process work"); }
            Check(!Directory.Exists(unusedWorking), "no CLI working directory created");
            try { AccountQuotaClient.CreateStartInfo(QuotaProvider.Gemini, "/synthetic/agy", directory); Check(false, "no process start info"); }
            catch (NotSupportedException) { Check(true, "no Gemini process command can be constructed"); }
            try { AccountQuotaClient.FindExecutable(QuotaProvider.Gemini); Check(false, "no discovery"); }
            catch (NotSupportedException) { Check(true, "no agy discovery"); }
            var store = new UsageStore(Path.Combine(directory, "synthetic.db"));
            await store.SaveQuotaAsync(new(1, reading, now, []));
            var fake = new CountingClient(now);
            using var monitor = new AccountQuotaMonitor(fake, store, () => now);
            int geminiSuccesses = 0;
            monitor.Succeeded += observation => { if (observation.Provider == QuotaProvider.Gemini) Interlocked.Increment(ref geminiSuccesses); };
            monitor.Start();
            try
            {
                await fake.FirstThree.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // These all enter the same public wake path used by Refresh, resume and network recovery.
                monitor.RequestRefresh();
                monitor.RequestRefresh(queueWhileRefreshing: true);
                foreach (string trigger in new[] { "startup", "scheduled", "Refresh", "resume", "network-recovery" })
                {
                    await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
                    Check(fake.Calls.GetValueOrDefault(QuotaProvider.Gemini) == 0, trigger + " invokes no Gemini client/process");
                }
            }
            finally { await monitor.StopAsync(); }
            var state = monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini);
            Check(state.Reading is null && state.CheckedAtUtc is null && state.LastSuccessfulCheckUtc is null && state.NextCheckUtc is null && !state.IsRefreshing, "cached Gemini is never loaded or scheduled");
            Check(geminiSuccesses == 0, "no Gemini observation or alert callback");
            Check(new[] { QuotaProvider.Codex, QuotaProvider.Claude, QuotaProvider.Xai }.All(p => fake.Calls.GetValueOrDefault(p) > 0), "other three providers still run");
            var preserved = await store.ReadQuotaAsync(QuotaProvider.Gemini);
            Check(preserved is not null && preserved.Reading.Windows.SequenceEqual(reading.Windows) && preserved.SuccessfulAtUtc == now, "legacy cache preserved unchanged for rollback; never displayed");
            foreach (bool previous in new[] { false, true })
            foreach (bool refreshing in new[] { false, true })
            {
                var old = new QuotaState(QuotaProvider.Gemini, reading, now, now, now.AddHours(1), refreshing, previous, "private synthetic error", "private synthetic cache error");
                var section = QuotaPanelModel.Section(old, now);
                Check(section.Rows.Count == 1 && section.Rows[0].Text == QuotaNames.GeminiUnavailableText && section.Rows[0].UsedPercent is null && section.Rows[0].ElapsedPercent is null, "all old states have one fixed row without usage/pacing");
                Check(!section.Detail.Contains("Synthetic model") && !section.Detail.Contains("private synthetic") && section.Metadata.Text == "", "detail/metadata never reveal old values, errors or dates");
                Check(section.Detail.StartsWith("Gemini 한도 · 조회 안 함\n", StringComparison.Ordinal), "disabled title has no stale timestamps");
                Check(section.Detail.Contains("Antigravity 약관상 앱에서 자동 조회하지 않습니다.", StringComparison.Ordinal) && section.Detail.Contains("https://antigravity.google/docs/faq.md", StringComparison.Ordinal), "reason includes the FAQ basis");
                Check(section.Detail.EndsWith("공식 서비스 상태는 상태 카드에서 확인하세요.", StringComparison.Ordinal), "official status remains on its card");
                Check(section.Detail.Split('\n').Length == 4 && !section.Detail.Contains("조회 주기") && !section.Detail.Contains("응답 수신 시각"), "disabled detail has only four explanatory lines");
            }
            var recovery = new QuotaRecoveryNotifications();
            var early = new QuotaEarlyResetNotifications();
            var oldObservation = new QuotaObservation(QuotaProvider.Gemini, 1, now, reading, 1);
            var current = reading with { Windows = [reading.Windows[0] with { UsedPercent = 0 }] };
            Check(recovery.Observe(oldObservation, true) is null && recovery.Observe(oldObservation with { Sequence = 2, Reading = current }, true) is null, "no recovery alert from old/exhausted Gemini values");
            Check(early.Evaluate(oldObservation, true).Reason == "QueryDisabled" && early.Evaluate(oldObservation with { Sequence = 2, Reading = current }, true).Alert is null, "no early reset alert");
            var recoveryAlert = new QuotaRecoveryAlert(QuotaProvider.Gemini, 2, "synthetic", "synthetic", ["synthetic-week"]);
            var earlyAlert = new QuotaEarlyResetAlert(QuotaProvider.Gemini, 2, 1, now, []);
            Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(recoveryAlert, reading, current, now), "pending Gemini recovery is discarded");
            Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(earlyAlert, reading, current, now, 1), "pending Gemini early reset is discarded");
            return count;
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, recursive: true); }
    }

    private sealed class CountingClient(DateTimeOffset now) : IAccountQuotaClient
    {
        internal readonly ConcurrentDictionary<QuotaProvider, int> Calls = new();
        internal readonly TaskCompletionSource FirstThree = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken token)
        {
            Calls.AddOrUpdate(provider, 1, (_, count) => count + 1);
            if (Calls.Count >= 3) FirstThree.TrySetResult();
            return Task.FromResult(new QuotaReading(provider, [new("synthetic-week", "Synthetic", 25, now.AddDays(2), 10080)]));
        }
    }
}
