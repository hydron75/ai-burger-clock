using System.Collections.Concurrent;
using System.Text.Json;

namespace AiBurgerClock;

internal static class WeeklyEarlyResetMonitorTests
{
    public static async Task<int> RunAsync(string directory)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Weekly early-reset monitor: " + message);
        }

        await CachedRestartAsync(directory, Check);
        await LiveComparisonAsync(directory, Check);
        await CancelledReadAsync(directory, Check);
        await UnavailableDiagnosticsAsync(directory, Check);
        return assertions;
    }

    private static readonly DateTimeOffset Now = new(2032, 3, 10, 0, 0, 0, TimeSpan.Zero);

    private static QuotaReading Weekly(QuotaProvider provider, double used, DateTimeOffset reset) => new(provider,
        [new("synthetic:weekly", "주간", used, reset, 10080)]);

    private static QuotaEarlyResetDiagnosticEntry[] Entries(QuotaEarlyResetDiagnosticLog log) =>
        File.ReadAllLines(log.LogPath).Select(line => JsonSerializer.Deserialize(line,
            QuotaJsonContext.Default.QuotaEarlyResetDiagnosticEntry)!).ToArray();

    private static async Task CachedRestartAsync(string directory, Action<bool, string> check)
    {
        var store = new UsageStore(Path.Combine(directory, "weekly-restart.db"));
        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
            await store.SaveQuotaAsync(new(1, Weekly(provider, 17, Now.AddDays(4.6)), Now.AddDays(-2), []));
        var log = new QuotaEarlyResetDiagnosticLog(Path.Combine(directory, "restart-decisions"));
        var client = new Client((provider, _) => Task.FromResult(Weekly(provider, 0, Now.AddDays(4.6))));
        using (var monitor = new AccountQuotaMonitor(client, store, () => Now, earlyResetDiagnosticLog: log))
        {
            var engine = new QuotaEarlyResetNotifications();
            var recovery = new QuotaRecoveryNotifications();
            var observations = new ConcurrentQueue<(QuotaObservation Observation, QuotaEarlyResetEvaluation Evaluation, bool Recovery)>();
            monitor.Succeeded += observation =>
            {
                var result = engine.Evaluate(observation, true);
                bool restoredRecovery = recovery.Observe(observation, true) is not null;
                monitor.RecordEarlyResetDecision(observation, result.Alert is null ? "Excluded" : "Alert", result.Reason);
                observations.Enqueue((observation, result, restoredRecovery));
            };
            monitor.Start();
            await MonitorTests.WaitUntilAsync(() => observations.Count == 3 && monitor.Snapshot().All(state => !state.IsRefreshing), 5000);
            await monitor.StopAsync();
            foreach (var item in observations)
            {
                check(item.Observation is { Sequence: 1, ContinuityId: 1, PreviousSuccess.ContinuityId: 1 }
                    && item.Observation.PreviousSuccess.CheckedAtUtc == Now.AddDays(-2),
                    "First live response carries the last successful cached snapshot, without an age limit");
                check(item.Observation.PreviousSuccess!.Reading.Windows.Single().UsedPercent == 17
                    && item.Evaluation.Alert?.Windows.Single().UsedPercent == 0 && item.Evaluation.Reason == "Alert",
                    "Synthetic 17-to-zero drop, 4.6 days before the old reset, alerts after a two-day shutdown");
                check(!item.Recovery, "Loading an early-reset baseline cannot arm a recovery alert");
                check(item.Observation.PreviousSuccess.Reading.Windows is not QuotaWindow[],
                    "The previous cache snapshot owns a read-only window collection");
            }
            check(Entries(log) is { Length: 3 } rows && rows.All(row => row is { Succeeded: true, Outcome: "Alert", Reason: "Alert" }),
                "Each live read has one completed decision row, not a second row from the UI");
        }

        using (var restarted = new AccountQuotaMonitor(client, store, () => Now.AddHours(3), earlyResetDiagnosticLog: log))
        {
            var engine = new QuotaEarlyResetNotifications();
            var results = new ConcurrentQueue<QuotaEarlyResetEvaluation>();
            restarted.Succeeded += observation =>
            {
                var result = engine.Evaluate(observation, true);
                restarted.RecordEarlyResetDecision(observation, result.Alert is null ? "Excluded" : "Alert", result.Reason);
                results.Enqueue(result);
            };
            restarted.Start();
            await MonitorTests.WaitUntilAsync(() => results.Count == 3 && restarted.Snapshot().All(state => !state.IsRefreshing), 5000);
            await restarted.StopAsync();
            check(results.All(result => result.Alert is null && result.Reason == "UsageNotDecreased"),
                "Successful cache persistence prevents the same zero reading alerting again after restart");
            check(Entries(log).Length == 6 && Entries(log).Count(row => row.Outcome == "Alert") == 3,
                "Restart adds new excluded query decisions without duplicating the original alert rows");
        }
        var saved = await store.ReadQuotaAsync(QuotaProvider.Codex);
        string cacheJson = JsonSerializer.Serialize(saved!, QuotaJsonContext.Default.QuotaCache);
        check(saved?.Version == 1 && !cacheJson.Contains("PreviousSuccess", StringComparison.Ordinal)
            && !cacheJson.Contains("ContinuityId", StringComparison.Ordinal) && !cacheJson.Contains("DiagnosticAttemptId", StringComparison.Ordinal),
            "Quota cache v1 shape is unchanged: snapshots and query generations are not new persisted fields");
    }

    private static async Task LiveComparisonAsync(string directory, Action<bool, string> check)
    {
        DateTimeOffset now = Now;
        double used = 17;
        bool fail = false;
        var log = new QuotaEarlyResetDiagnosticLog(Path.Combine(directory, "live-decisions"));
        var client = new Client((provider, _) => fail
            ? Task.FromException<QuotaReading>(new IOException("Synthetic failure, never copy to diagnostics"))
            : Task.FromResult(Weekly(provider, used, Now.AddDays(4.6))));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now, earlyResetDiagnosticLog: log);
        var engine = new QuotaEarlyResetNotifications();
        var results = new List<(QuotaObservation Observation, QuotaEarlyResetEvaluation Evaluation)>();
        monitor.Succeeded += observation =>
        {
            var result = engine.Evaluate(observation, true);
            monitor.RecordEarlyResetDecision(observation, result.Alert is null ? "Excluded" : "Alert", result.Reason);
            results.Add((observation, result));
        };
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(results[0].Observation.PreviousSuccess is null && results[0].Evaluation.Reason == "NoPreviousSuccess",
            "No cache or live baseline means a silent first read");
        now = now.AddHours(8);
        used = 16.999;
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(results[1].Evaluation.Alert is not null && results[1].Observation.PreviousSuccess?.Reading.Windows[0].UsedPercent == 17,
            "A raw fractional decrease after eight hours is eligible without a reset-time change or old thresholds");
        now = now.AddMinutes(10);
        fail = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(results.Count == 2 && Entries(log)[2] is { Succeeded: false, ContinuityId: 2, Sequence: null, Reason: "ReadFailed" },
            "A failed attempt gets one excluded row and breaks the positive generation without a success event");
        fail = false;
        now = now.AddMinutes(15);
        used = 5;
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(results[2].Observation is { ContinuityId: 2, PreviousSuccess.ContinuityId: 1 }
            && results[2].Evaluation.Reason == "ContinuityChanged" && results[2].Evaluation.Alert is null,
            "The previous successful reading retains its old generation across an intervening failed attempt");
        now = now.AddMinutes(10);
        used = 4;
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(results[3].Observation.PreviousSuccess?.Reading.Windows[0].UsedPercent == 5
            && results[3].Observation.PreviousSuccess?.ContinuityId == 2 && results[3].Evaluation.Alert is not null,
            "The next successful comparison uses the immediately preceding success in the new generation");
        now = now.AddMinutes(10);
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(results[4].Evaluation.Reason == "UsageNotDecreased" && results[4].Evaluation.Alert is null,
            "An unchanged repeated response cannot rediscover a previous drop");
        check(Entries(log).Length == 6 && Entries(log).All(row => row.Reason != "NotEvaluated"),
            "All completed live and failed queries retain exactly one final reason");
        string text = File.ReadAllText(log.LogPath);
        check(!text.Contains("16.999", StringComparison.Ordinal) && !text.Contains("synthetic:weekly", StringComparison.Ordinal)
            && !text.Contains("failure, never", StringComparison.Ordinal) && !text.Contains("ResetsAt", StringComparison.Ordinal),
            "Decision diagnostics do not contain raw quota values, window IDs, resets or exception text");
        await monitor.StopAsync();
    }

    private static async Task CancelledReadAsync(string directory, Action<bool, string> check)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new QuotaEarlyResetDiagnosticLog(Path.Combine(directory, "cancel-decisions"));
        var client = new Client(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable synthetic read");
        });
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => Now, earlyResetDiagnosticLog: log);
        using var cancel = new CancellationTokenSource();
        Task active = monitor.RefreshOnceAsync(QuotaProvider.Gemini, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        try { await active; } catch (OperationCanceledException) { }
        check(Entries(log) is [{ Succeeded: false, Reason: "ReadCancelled", ContinuityId: 2, Sequence: null }],
            "Cancelling a started query records one exclusion without inventing a successful observation");
        await monitor.StopAsync();
    }

    private static async Task UnavailableDiagnosticsAsync(string directory, Action<bool, string> check)
    {
        var log = new QuotaEarlyResetDiagnosticLog(Path.Combine(directory, "blocked-decisions"));
        Directory.CreateDirectory(log.LogPath);
        using var monitor = new AccountQuotaMonitor(new Client((provider, _) => Task.FromResult(Weekly(provider, 17, Now.AddDays(4)))),
            utcNow: () => Now, earlyResetDiagnosticLog: log);
        int successes = 0;
        monitor.Succeeded += observation =>
        {
            monitor.RecordEarlyResetDecision(observation, "Excluded", "NoPreviousSuccess");
            successes++;
        };
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(successes == 1 && monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Codex) is
            { IsRefreshing: false, Error: "", ContinuityId: 1 }, "Optional diagnostic failure cannot change polling, continuity or success delivery");
        check(Directory.Exists(log.LogPath), "An incompatible diagnostic target is preserved rather than overwritten");
        await monitor.StopAsync();
    }

    private sealed class Client(Func<QuotaProvider, CancellationToken, Task<QuotaReading>> handler) : IAccountQuotaClient
    {
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) => handler(provider, cancellationToken);
    }
}
