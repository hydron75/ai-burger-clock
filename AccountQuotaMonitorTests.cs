using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AiBurgerClock;

internal static class AccountQuotaMonitorTests
{
    public static async Task<int> RunAsync()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Account quota monitor test: " + message);
        }

        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-quota-tests-").FullName;
        try
        {
            await StateTestsAsync(Check);
            await SuccessObservationTestsAsync(directory, Check);
            await SuccessObservationCancellationTestsAsync(Check);
            await SuccessObservationOrderingTestsAsync(Check);
            await ExhaustedPollingTestsAsync(Check);
            await GeminiPollingTestsAsync(Check);
            await OverlapTestsAsync(Check);
            await PollingTestsAsync(Check);
            await CacheTestsAsync(directory, Check);
            await ResetAnchorTestsAsync(directory, Check);
            return assertions;
        }
        finally
        {
            // This generated child belongs only to this invocation, never to the user's app data.
            string target = Path.GetFullPath(directory);
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith("AiBurgerClock-quota-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected quota test cleanup target.");
            try { Directory.Delete(target, recursive: true); }
            catch (IOException) { Console.WriteLine("Temporary quota test data retained: " + target); }
        }
    }

    private static async Task StateTestsAsync(Action<bool, string> check)
    {
        DateTimeOffset now = Now;
        var client = new FakeClient((provider, _) => provider == QuotaProvider.Codex
            ? Task.FromResult(Reading(provider, 45))
            : Task.FromException<QuotaReading>(new IOException("Synthetic sensitive output must not reach the UI")));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
        check(monitor.Snapshot().All(state => state.Reading is null && state.CheckedAtUtc is null), "No initial data is unknown");
        monitor.Changed += () => throw new InvalidOperationException("Synthetic display failure");
        await Task.WhenAll(monitor.RefreshOnceAsync(QuotaProvider.Codex), monitor.RefreshOnceAsync(QuotaProvider.Claude));
        QuotaState codex = State(monitor, QuotaProvider.Codex);
        QuotaState claude = State(monitor, QuotaProvider.Claude);
        check(codex.Reading?.Windows[0].RemainingPercent == 55 && !codex.IsPrevious && codex.Error.Length == 0,
            "One failed provider does not block the other provider's success");
        check(claude.Reading is null && claude.IsPrevious && claude.LastSuccessfulCheckUtc is null && claude.Error.Length > 0,
            "Initial failed provider remains unknown, not zero or full");
        check(!claude.Error.Contains("sensitive", StringComparison.Ordinal), "Exception details never become the visible error");
        check(codex.CheckedAtUtc == now && codex.LastSuccessfulCheckUtc == now && codex.NextCheckUtc == now.AddHours(1),
            "Success sets current timestamps and default schedule");

        now = now.AddHours(1);
        client.Handler = (provider, _) => provider == QuotaProvider.Claude
            ? Task.FromResult(Reading(provider, 93))
            : Task.FromException<QuotaReading>(new TimeoutException("Synthetic timeout"));
        await Task.WhenAll(monitor.RefreshOnceAsync(QuotaProvider.Codex), monitor.RefreshOnceAsync(QuotaProvider.Claude));
        codex = State(monitor, QuotaProvider.Codex);
        claude = State(monitor, QuotaProvider.Claude);
        check(codex.Reading?.Windows[0].UsedPercent == 45 && codex.IsPrevious, "Failure retains last known numbers as previous");
        check(codex.CheckedAtUtc == now && codex.LastSuccessfulCheckUtc == now.AddHours(-1), "Failure advances attempt, not successful retrieval");
        check(codex.Error.Contains("시간 초과") && !codex.IsRefreshing, "Timeout has a bounded friendly message and finishes refresh");
        check(claude.Reading?.Windows[0].UsedPercent == 93 && !claude.IsPrevious && claude.Error.Length == 0,
            "Other provider recovery clears its own previous/error state");
        check(claude.NextCheckUtc == now.AddHours(1) && codex.NextCheckUtc == now.AddMinutes(15),
            "Polling policies are independent per provider; a failed read retries after 15 minutes");

        now = now.AddMinutes(1);
        client.Handler = (_, _) => Task.FromResult(Reading(QuotaProvider.Claude, 11));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        codex = State(monitor, QuotaProvider.Codex);
        check(codex.Reading?.Provider == QuotaProvider.Codex && codex.Reading.Windows[0].UsedPercent == 45 && codex.IsPrevious,
            "Mismatched provider response cannot replace a valid previous reading");
        check(codex.LastSuccessfulCheckUtc == Now, "Invalid response cannot advance success time");
        check(codex.NextCheckUtc == now.AddMinutes(30), "Second consecutive failure retries after 30 minutes");

        client.Handler = (provider, _) => Task.FromResult(new QuotaReading(provider, []));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(State(monitor, QuotaProvider.Codex).Reading?.Windows.Count == 1
            && State(monitor, QuotaProvider.Codex).IsPrevious, "Empty response cannot become a successful reading");
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == now.AddHours(1), "Third consecutive failure retries after 1 hour");
        client.Handler = (provider, _) => Task.FromResult(Reading(provider, 1));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(!State(monitor, QuotaProvider.Codex).IsPrevious && State(monitor, QuotaProvider.Codex).Error.Length == 0,
            "Valid reading recovers after invalid responses");
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == now.AddHours(1), "Success resets failure backoff to the normal schedule");
        now = now.AddHours(1).AddMinutes(6);
        check(State(monitor, QuotaProvider.Codex).IsPrevious, "Overdue refresh presents cached values as previous");
        now = Now.AddHours(-1);
        check(State(monitor, QuotaProvider.Codex).IsPrevious, "Backward clock change cannot make future cached data fresh");
        now = Now.AddHours(2);
        client.Handler = (_, _) => Task.FromException<QuotaReading>(new IOException("Synthetic failure after recovery"));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == now.AddMinutes(15),
            "A new failure after recovery restarts backoff at 15 minutes");
        await monitor.StopAsync();
    }

    private static async Task ExhaustedPollingTestsAsync(Action<bool, string> check)
    {
        DateTimeOffset now = Now;
        double codexUsed = 45;
        double claudeWeeklyUsed = 100;
        QuotaReading CurrentReading(QuotaProvider provider) => provider == QuotaProvider.Codex
            ? Reading(provider, codexUsed)
            : new(provider, [new("session", "5시간", 20, null, 300), new("weekly_all", "주간", claudeWeeklyUsed, null, 10080)]);
        var client = new FakeClient((provider, _) => Task.FromResult(CurrentReading(provider)));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
        await Task.WhenAll(monitor.RefreshOnceAsync(QuotaProvider.Codex), monitor.RefreshOnceAsync(QuotaProvider.Claude));
        check(State(monitor, QuotaProvider.Claude).NextCheckUtc == now.AddMinutes(15),
            "One exhausted weekly window enables 15m polling even while the 5h window has remaining quota");
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == now.AddHours(1),
            "Claude exhaustion cannot shorten Codex's independent normal polling interval");

        client.Handler = (_, _) => Task.FromException<QuotaReading>(new IOException("Synthetic exhausted-query failure"));
        for (int failure = 1; failure <= 3; failure++)
        {
            now = now.AddMinutes(15);
            await monitor.RefreshOnceAsync(QuotaProvider.Claude);
            QuotaState claude = State(monitor, QuotaProvider.Claude);
            check(claude.NextCheckUtc == now.AddMinutes(15),
                $"Exhausted failure {failure} cannot back off beyond 15m");
            check(claude.IsPrevious && claude.Reading?.Windows[1].RemainingPercent == 0 && claude.LastSuccessfulCheckUtc == Now,
                "Failed exhausted read preserves the known zero and its original successful time");
        }
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == Now.AddHours(1) && State(monitor, QuotaProvider.Codex).Error.Length == 0,
            "Repeated Claude failures cannot alter another provider's cadence or error state");

        now = now.AddMinutes(15);
        client.Handler = (provider, _) => Task.FromResult(Reading(provider, 101));
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        check(State(monitor, QuotaProvider.Claude).NextCheckUtc == now.AddMinutes(15)
            && State(monitor, QuotaProvider.Claude).Reading?.Windows[1].RemainingPercent == 0,
            "Invalid fresh usage cannot replace the last exhausted reading or weaken its retry cap");

        now = now.AddMinutes(15);
        claudeWeeklyUsed = 95;
        client.Handler = (provider, _) => Task.FromResult(CurrentReading(provider));
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        QuotaState recovered = State(monitor, QuotaProvider.Claude);
        check(recovered.NextCheckUtc == now.AddHours(1) && !recovered.IsPrevious && recovered.Error.Length == 0
            && recovered.LastSuccessfulCheckUtc == now,
            "A successful partial recovery clears failures and returns to low-remaining hourly checks");

        now = now.AddHours(1);
        claudeWeeklyUsed = 0;
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        check(State(monitor, QuotaProvider.Claude).NextCheckUtc == now.AddHours(1),
            "A successful full recovery returns the provider to normal hourly polling");

        codexUsed = 100;
        await Task.WhenAll(monitor.RefreshOnceAsync(QuotaProvider.Codex), monitor.RefreshOnceAsync(QuotaProvider.Claude));
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == now.AddMinutes(15)
            && State(monitor, QuotaProvider.Claude).NextCheckUtc == now.AddHours(1),
            "The same exhausted rule applies to Codex without changing recovered Claude's cadence");
        await monitor.StopAsync();
    }

    private static async Task SuccessObservationTestsAsync(string directory, Action<bool, string> check)
    {
        DateTimeOffset now = Now;
        List<QuotaWindow> windows = [new("synthetic:5h", "5시간", 100, Now.AddHours(1), 300)];
        var client = new FakeClient((provider, _) => Task.FromResult(new QuotaReading(provider, windows)));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
        List<QuotaObservation> observations = [];
        int changed = 0;
        bool allCallbacksAfterRefresh = true;
        monitor.Changed += () => changed++;
        monitor.Succeeded += _ => throw new InvalidOperationException("Synthetic notification failure");
        monitor.Succeeded += observation =>
        {
            allCallbacksAfterRefresh &= !State(monitor, observation.Provider).IsRefreshing;
            observations.Add(observation);
        };
        check(observations.Count == 0, "Construction and snapshot access emit no successful live observation");
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(observations.Count == 1 && observations[0].Provider == QuotaProvider.Codex
            && observations[0].Sequence == 1 && observations[0].CheckedAtUtc == Now,
            "Accepted live response emits exactly one normalized observation with its own provider sequence");
        check(observations[0].Reading.Provider == QuotaProvider.Codex
            && observations[0].Reading.Windows[0].UsedPercent == 100,
            "The successful observation carries the exact validated reading");
        check(changed == 2 && allCallbacksAfterRefresh,
            "Refresh start and finish remain Changed events; success callback sees IsRefreshing false");
        check(!ReferenceEquals(observations[0].Reading.Windows, windows),
            "Successful observation owns a window collection detached from the client");
        windows[0] = windows[0] with { UsedPercent = 20 };
        windows.Add(new("synthetic:weekly", "주간", 10, null, 10080));
        check(observations[0].Reading.Windows.Count == 1 && observations[0].Reading.Windows[0].UsedPercent == 100
            && State(monitor, QuotaProvider.Codex).Reading?.Windows.Count == 1,
            "Client mutation after returning cannot rewrite delivered observations or displayed state");
        bool immutable = false;
        try { ((IList<QuotaWindow>)observations[0].Reading.Windows)[0] = windows[0]; }
        catch (NotSupportedException) { immutable = true; }
        check(immutable && observations[0].Reading.Windows[0].UsedPercent == 100,
            "Notification subscribers cannot mutate accepted quota windows");
        now = now.AddMinutes(15);
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        check(observations.Count == 3 && observations[1].Sequence == 2 && observations[1].CheckedAtUtc == now
            && observations[2].Provider == QuotaProvider.Claude && observations[2].Sequence == 1,
            "Sequences increase independently per provider even when another subscriber throws");

        client.Handler = (_, _) => Task.FromException<QuotaReading>(new IOException("Synthetic read failure"));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        client.Handler = (_, _) => Task.FromResult(Reading(QuotaProvider.Gemini, 20));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        client.Handler = (provider, _) => Task.FromResult(new QuotaReading(provider, []));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(observations.Count == 3 && State(monitor, QuotaProvider.Codex).IsPrevious,
            "Network failure, mismatched provider and invalid windows never emit a success");
        client.Handler = (provider, _) => Task.FromResult(Reading(provider, 20));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(observations.Count == 4 && observations[^1].Sequence == 3,
            "Rejected responses consume no successful observation sequence");
        await monitor.StopAsync();

        // A directory cannot be opened as a SQLite database on either platform.
        // Cache persistence failure is distinct from a verified live CLI response.
        string blockedPath = Path.Combine(directory, "cache-save-blocked.db");
        Directory.CreateDirectory(blockedPath);
        using var cacheFailure = new AccountQuotaMonitor(client, new UsageStore(blockedPath), () => Now);
        List<QuotaObservation> cacheObservations = [];
        cacheFailure.Succeeded += cacheObservations.Add;
        await cacheFailure.RefreshOnceAsync(QuotaProvider.Codex);
        var failedCacheState = State(cacheFailure, QuotaProvider.Codex);
        check(cacheObservations.Count == 1 && cacheObservations[0].Sequence == 1
            && failedCacheState.LastSuccessfulCheckUtc == Now && !failedCacheState.IsPrevious,
            "Cache save failure does not hide the independently accepted successful live observation");
        check(failedCacheState.CacheError.Length > 0 && failedCacheState.Error.Length == 0
            && failedCacheState.Reading?.Windows[0].UsedPercent == 20,
            "Cache warning remains distinct from a live query failure");
        await cacheFailure.StopAsync();
    }

    private static async Task SuccessObservationCancellationTestsAsync(Action<bool, string> check)
    {
        using CancellationTokenSource canceled = new();
        var client = new FakeClient((provider, _) =>
        {
            canceled.Cancel();
            return Task.FromResult(Reading(provider, 20));
        });
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => Now);
        List<QuotaObservation> observations = [];
        monitor.Succeeded += observations.Add;
        bool cancellationPreserved = false;
        try { await monitor.RefreshOnceAsync(QuotaProvider.Codex, canceled.Token); }
        catch (OperationCanceledException) { cancellationPreserved = true; }
        check(cancellationPreserved && observations.Count == 0
            && State(monitor, QuotaProvider.Codex).LastSuccessfulCheckUtc is null,
            "Client returning after cancellation cannot be accepted or emit a successful observation");
        check(!State(monitor, QuotaProvider.Codex).IsRefreshing,
            "Cancellation clears the refresh flag without manufacturing a failure or success");
        client.Handler = (provider, _) => Task.FromResult(Reading(provider, 20));
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(observations.Count == 1 && observations[0].Sequence == 1,
            "Cancelled response does not consume the next provider success sequence");
        await monitor.StopAsync();

        using var stopped = new AccountQuotaMonitor(client, utcNow: () => Now);
        int afterStop = 0;
        stopped.Succeeded += _ => afterStop++;
        client.Handler = (provider, _) =>
        {
            stopped.Dispose();
            return Task.FromResult(Reading(provider, 20));
        };
        bool stoppedCanceled = false;
        try { await stopped.RefreshOnceAsync(QuotaProvider.Claude); }
        catch (OperationCanceledException) { stoppedCanceled = true; }
        check(stoppedCanceled && afterStop == 0 && State(stopped, QuotaProvider.Claude).LastSuccessfulCheckUtc is null,
            "Lifetime cancellation after client return suppresses success as well as notification callbacks");
    }

    private static async Task SuccessObservationOrderingTestsAsync(Action<bool, string> check)
    {
        var entered = Signal();
        var release = Signal();
        var client = new FakeClient((provider, _) => Task.FromResult(Reading(provider, 20)));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => Now);
        List<long> delivered = [];
        Task? overlapping = null;
        monitor.Succeeded += observation =>
        {
            delivered.Add(observation.Sequence);
            if (observation.Sequence != 1) return;
            // Async UI forwarding normally returns immediately. Hold this synthetic
            // callback to verify that a second caller cannot overtake its response.
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            overlapping = monitor.RefreshOnceAsync(QuotaProvider.Codex);
        };
        Task first = Task.Run(() => monitor.RefreshOnceAsync(QuotaProvider.Codex));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            await monitor.RefreshOnceAsync(QuotaProvider.Codex);
            check(client.Calls(QuotaProvider.Codex) == 1 && delivered.SequenceEqual([1L]),
                "An overlapping refresh cannot overtake the current provider's success delivery");
        }
        finally { release.TrySetResult(); }
        await first;
        if (overlapping is not null) await overlapping;
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(delivered.SequenceEqual([1L, 2L]) && client.Calls(QuotaProvider.Codex) == 2,
            "Success callbacks are ordered outside the state lock while the provider gate still serializes delivery");
        await monitor.StopAsync();
    }

    private static async Task OverlapTestsAsync(Action<bool, string> check)
    {
        var entered = Signal();
        var release = Signal();
        var client = new FakeClient(async (provider, token) =>
        {
            if (provider == QuotaProvider.Codex)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }
            return Reading(provider, 20);
        });
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => Now);
        Task first = monitor.RefreshOnceAsync(QuotaProvider.Codex);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => monitor.RefreshOnceAsync(QuotaProvider.Codex)));
        check(client.Calls(QuotaProvider.Codex) == 1 && State(monitor, QuotaProvider.Codex).IsRefreshing,
            "Overlapping refresh requests do not duplicate the running provider command");
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        check(client.Calls(QuotaProvider.Claude) == 1 && State(monitor, QuotaProvider.Claude).Reading is not null,
            "Blocked Codex command does not block Claude");
        release.TrySetResult();
        await first;
        check(client.MaximumActive(QuotaProvider.Codex) == 1 && !State(monitor, QuotaProvider.Codex).IsRefreshing,
            "Provider gate serializes work and releases after success");
        await monitor.StopAsync();
    }

    private static async Task GeminiPollingTestsAsync(Action<bool, string> check)
    {
        DateTimeOffset now = Now;
        double used = 100;
        DateTimeOffset? reset = null;
        var client = new FakeClient((provider, _) => Task.FromResult(provider == QuotaProvider.Gemini
            ? new QuotaReading(provider, [new("gemini-5h", "5시간", used, reset, 300),
                new("gemini-weekly", "주간", 5, null, 10080)]) : Reading(provider, 20)));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
        await Task.WhenAll(Enum.GetValues<QuotaProvider>().Select(provider => monitor.RefreshOnceAsync(provider)));
        check(State(monitor, QuotaProvider.Gemini).NextCheckUtc == now.AddMinutes(15), "Gemini exhaustion polls every 15m");
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == now.AddHours(1) &&
            State(monitor, QuotaProvider.Claude).NextCheckUtc == now.AddHours(1), "Gemini exhaustion cannot shorten other providers' cadence");
        check(State(monitor, QuotaProvider.Gemini).Reading?.Windows.Count == 2, "Gemini five-hour and weekly windows stay together");
        used = 96;
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        check(State(monitor, QuotaProvider.Gemini).NextCheckUtc == now.AddHours(1), "Verified low Gemini recovery changes to hourly polling");
        used = 20;
        reset = now.AddHours(1);
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        check(State(monitor, QuotaProvider.Gemini).NextCheckUtc == reset.Value.AddMinutes(-15), "Gemini enters the reset band before a long wait ends");
        now = reset.Value.AddMinutes(-10);
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        check(State(monitor, QuotaProvider.Gemini).NextCheckUtc == now.AddMinutes(5), "Gemini reset band polls every 5m");
        reset = reset.Value.AddDays(7);
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        check(State(monitor, QuotaProvider.Gemini).NextCheckUtc == now.AddMinutes(5), "Gemini preserves the old reset anchor after rollover");
        now = now.AddMinutes(26);
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        check(State(monitor, QuotaProvider.Gemini).NextCheckUtc == now.AddHours(1), "Gemini returns to the hourly default outside the reset band");
        DateTimeOffset success = now;
        // Other Providers are already past their new hourly deadline by this
        // synthetic instant. Refresh their baselines so the isolation check below
        // tests Gemini's failure, not unrelated overdue presentation.
        await Task.WhenAll(monitor.RefreshOnceAsync(QuotaProvider.Codex), monitor.RefreshOnceAsync(QuotaProvider.Claude));
        client.Handler = (_, _) => Task.FromException<QuotaReading>(new IOException("Synthetic agy diagnostic secret"));
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        var previous = State(monitor, QuotaProvider.Gemini);
        check(previous.IsPrevious && previous.Reading?.Windows[0].UsedPercent == 20 && previous.LastSuccessfulCheckUtc == success,
            "Gemini failure keeps previous values and original successful time");
        check(previous.NextCheckUtc == now.AddMinutes(15) && !previous.Error.Contains("secret"), "Gemini retries without exposing diagnostics");
        client.Handler = (_, _) => Task.FromException<QuotaReading>(new NotSupportedException("Unknown agy version"));
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        check(State(monitor, QuotaProvider.Gemini).Error == "agy CLI 1.3.1 이상이 필요합니다(2.0 미만 안정 버전).",
            "Unsupported CLI version explains the stable minimum and excluded major versions without agent fallback");
        check(State(monitor, QuotaProvider.Gemini).NextCheckUtc == now.AddMinutes(30), "Gemini failure backoff uses the existing policy");
        check(!State(monitor, QuotaProvider.Codex).IsPrevious && !State(monitor, QuotaProvider.Claude).IsPrevious,
            "Gemini failure does not mark other providers as previous");
        await monitor.StopAsync();
    }

    private static async Task PollingTestsAsync(Action<bool, string> check)
    {
        var release = Signal();
        var client = new FakeClient(async (provider, token) =>
        {
            await release.Task.WaitAsync(token).ConfigureAwait(false);
            return Reading(provider, 40);
        });
        using (var monitor = new AccountQuotaMonitor(client, utcNow: () => Now))
        {
            monitor.Changed += () => throw new InvalidOperationException("Synthetic subscriber failure");
            monitor.Start();
            monitor.Start();
            await WaitAsync(() => Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) == 1));
            for (int index = 0; index < 10; index++) monitor.RequestRefresh();
            check(Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) == 1),
                "Start is idempotent and manual refresh ignores in-flight requests");
            for (int index = 0; index < 10; index++) monitor.RequestRefresh(queueWhileRefreshing: true);
            release.TrySetResult();
            await WaitAsync(() => Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) >= 2)
                && monitor.Snapshot().All(state => !state.IsRefreshing && state.CheckedAtUtc.HasValue));
            await Task.Delay(40);
            check(Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) == 2),
                "Resume/reconnect requests during refresh coalesce into one follow-up");
            check(Enum.GetValues<QuotaProvider>().All(provider => client.MaximumActive(provider) == 1),
                "Background polling never overlaps a provider process");
            monitor.RequestRefresh();
            await WaitAsync(() => Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) == 3)
                && monitor.Snapshot().All(state => !state.IsRefreshing));
            check(monitor.Snapshot().All(state => state.NextCheckUtc == Now.AddHours(1)),
                "Manual refresh re-arms the next scheduled check");
            await monitor.StopAsync();
            monitor.RequestRefresh(queueWhileRefreshing: true);
            await Task.Delay(40);
            check(Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) == 3), "Stopped monitor never polls again");
            monitor.Dispose();
            monitor.Dispose();
            check(true, "Repeated monitor disposal is harmless");
        }

        var blocked = new FakeClient(async (provider, token) =>
        {
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return Reading(provider, 0);
        });
        using var cancelling = new AccountQuotaMonitor(blocked, utcNow: () => Now);
        cancelling.Start();
        await WaitAsync(() => Enum.GetValues<QuotaProvider>().All(provider => blocked.Calls(provider) == 1));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await cancelling.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        check(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "Shutdown cancels both provider calls promptly");
        check(cancelling.Snapshot().All(state => !state.IsRefreshing && state.LastSuccessfulCheckUtc is null),
            "Cancelled query is never a successful retrieval");
    }

    private static async Task CacheTestsAsync(string directory, Action<bool, string> check)
    {
        string path = Path.Combine(directory, "quota-cache.db");
        var store = new UsageStore(path);
        await store.InitializeAsync();
        check(await store.ReadQuotaAsync(QuotaProvider.Codex) is null, "Empty DB has no invented quota cache");
        var client = new FakeClient((provider, _) => Task.FromResult(Reading(provider, provider == QuotaProvider.Codex ? 25 : 75)));
        using (var monitor = new AccountQuotaMonitor(client, store, () => Now))
        {
            await Task.WhenAll(Enum.GetValues<QuotaProvider>().Select(provider => monitor.RefreshOnceAsync(provider)));
            check(monitor.Snapshot().All(state => state.CacheError.Length == 0), "Successful readings saved without cache errors");
            for (int index = 0; index < 5; index++) await monitor.RefreshOnceAsync(QuotaProvider.Codex);
            await monitor.StopAsync();
        }
        var reopened = new UsageStore(path);
        QuotaCache? codex = await reopened.ReadQuotaAsync(QuotaProvider.Codex);
        QuotaCache? claude = await reopened.ReadQuotaAsync(QuotaProvider.Claude);
        check(codex is { Version: 1 } && codex.SuccessfulAtUtc == Now && codex.Reading.Windows[0].UsedPercent == 25,
            "Reopened DB returns exact normalized quota and successful time");
        check(claude?.Reading.Windows[0].UsedPercent == 75, "Provider caches persist independently");
        check(Scalar(path, "SELECT COUNT(*) FROM AppMetadata WHERE Key LIKE 'AccountQuota.v1.%';") == 3,
            "Repeated polling overwrites three bounded metadata cache entries");
        QuotaCache? geminiCache = await reopened.ReadQuotaAsync(QuotaProvider.Gemini);
        check(geminiCache?.Reading.Provider == QuotaProvider.Gemini && geminiCache.Reading.Windows[0].UsedPercent == 75,
            "Gemini cache persists independently under its new key");
        check(Scalar(path, "SELECT COUNT(*) FROM UsageEvents;") == 0 && Scalar(path, "SELECT COUNT(*) FROM ProviderStatusHistory;") == 0,
            "Quota cache does not create measurements or provider-status history");
        check(Scalar(path, "PRAGMA user_version;") == 2, "Quota cache uses metadata without changing schema version");

        string legacyPath = Path.Combine(directory, "quota-legacy-cache.db");
        var legacyStore = new UsageStore(legacyPath);
        await legacyStore.InitializeAsync();
        foreach (var provider in Enum.GetValues<QuotaProvider>())
        {
            var legacy = new QuotaCache(1, new(provider,
                [new("account:5h", "5시간", 100, Now.AddHours(5), 300),
                 new("weekly_all", "주간", 45, null, 10080),
                 new("weekly_scoped:fable", "Fable 주간", 1, Now.AddDays(7))]),
                Now, [Now.AddHours(5), Now.AddDays(7)]);
            string legacyJson = JsonSerializer.Serialize(legacy);
            string generatedJson = JsonSerializer.Serialize(legacy, QuotaJsonContext.Default.QuotaCache);
            check(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(legacyJson), System.Text.Json.Nodes.JsonNode.Parse(generatedJson)),
                "Source-generated quota JSON preserves the legacy cache shape and values");
            SetCache(legacyPath, provider, legacyJson);
            var migrated = await new UsageStore(legacyPath).ReadQuotaAsync(provider);
            check(migrated is not null && SameCache(legacy, migrated),
                "Source-generated reader opens legacy cache including enums, Unicode, nulls and reset anchors");
            var oldReader = JsonSerializer.Deserialize<QuotaCache>(generatedJson);
            check(oldReader is not null && SameCache(legacy, oldReader),
                "Source-generated cache remains readable by the previous serializer");
            await legacyStore.SaveQuotaAsync(legacy);
            var generatedReopened = await new UsageStore(legacyPath).ReadQuotaAsync(provider);
            check(generatedReopened is not null && SameCache(legacy, generatedReopened),
                "Source-generated quota save and restart preserve every normalized field");
        }
        check(Scalar(legacyPath, "PRAGMA user_version;") == 2 &&
              Scalar(legacyPath, "SELECT COUNT(*) FROM AppMetadata WHERE Key LIKE 'AccountQuota.v1.%';") == 3,
            "Source generation changes neither SQLite schema nor quota metadata keys");

        var offline = new FakeClient((_, _) => Task.FromException<QuotaReading>(new IOException("Offline")));
        using (var restarted = new AccountQuotaMonitor(offline, reopened, () => Now.AddHours(1)))
        {
            int liveSuccesses = 0;
            restarted.Succeeded += _ => Interlocked.Increment(ref liveSuccesses);
            restarted.Start();
            await WaitAsync(() => restarted.Snapshot().All(state => state.CheckedAtUtc.HasValue && !state.IsRefreshing));
            check(restarted.Snapshot().All(state => state.IsPrevious && state.Reading is not null && state.LastSuccessfulCheckUtc == Now),
                "Offline restart shows persisted values as previous, with original successful time");
            check(restarted.Snapshot().All(state => state.CheckedAtUtc == Now.AddHours(1) && state.Error.Length > 0),
                "Offline restart distinguishes current attempt from cached observation");
            await restarted.StopAsync();
            check(liveSuccesses == 0, "Cache loading, refresh starts and failed offline reads emit no live successful observations");
        }

        SetCache(path, QuotaProvider.Codex, "{bad-json");
        var corruptClient = new FakeClient((_, _) => Task.FromException<QuotaReading>(new InvalidDataException("No data")));
        using (var corrupted = new AccountQuotaMonitor(corruptClient, new UsageStore(path), () => Now.AddHours(2)))
        {
            corrupted.Start();
            await WaitAsync(() => corrupted.Snapshot().All(state => state.CheckedAtUtc.HasValue && !state.IsRefreshing));
            check(State(corrupted, QuotaProvider.Codex).Reading is null && State(corrupted, QuotaProvider.Codex).CacheError.Length > 0,
                "Malformed persisted cache becomes unknown with a cache warning");
            check(State(corrupted, QuotaProvider.Claude).Reading?.Windows[0].UsedPercent == 75,
                "Malformed provider cache cannot break another provider");
            await corrupted.StopAsync();
        }

        foreach (string invalid in new[]
        {
            "null", new string('x', 512 * 1024 + 1),
            JsonSerializer.Serialize(codex! with { Version = 9 }),
            JsonSerializer.Serialize(codex! with { Reading = Reading(QuotaProvider.Claude, 1) }),
            JsonSerializer.Serialize(codex! with { Reading = new QuotaReading(QuotaProvider.Codex, []) }),
            JsonSerializer.Serialize(codex! with { Reading = Reading(QuotaProvider.Codex, 101) })
        })
        {
            SetCache(path, QuotaProvider.Codex, invalid);
            bool rejected = false;
            try { _ = await reopened.ReadQuotaAsync(QuotaProvider.Codex); }
            catch (Exception error) when (error is JsonException or InvalidDataException) { rejected = true; }
            check(rejected, "Unsupported/invalid cache data is rejected, not shown as current quota");
        }
        await reopened.SaveQuotaAsync(codex!);
        check((await reopened.ReadQuotaAsync(QuotaProvider.Codex))?.Reading.Windows[0].UsedPercent == 25,
            "Successful save repairs malformed cache");
        string unicode = new('한', 80);
        var large = new QuotaCache(1, new(QuotaProvider.Claude,
            Enumerable.Range(0, 64).Select(i => new QuotaWindow("weekly_scoped:" + unicode + i, unicode, i, Now.AddDays(7), 10080)).ToArray()), Now, []);
        check(JsonSerializer.Serialize(large).Length > 65536, "Unicode fixture exceeds former inconsistent cache read limit");
        await reopened.SaveQuotaAsync(large);
        var largeReopened = await new UsageStore(path).ReadQuotaAsync(QuotaProvider.Claude);
        check(largeReopened?.Reading.Windows.Count == 64 && largeReopened.Reading.Windows[63].Label == unicode,
            "Supported maximum Unicode quota windows survive save and restart");
        SetCache(path, QuotaProvider.Codex, JsonSerializer.Serialize(codex! with
        {
            Reading = new(QuotaProvider.Codex, [codex.Reading.Windows[0], codex.Reading.Windows[0]])
        }));
        bool duplicateRejected = false;
        try { _ = await reopened.ReadQuotaAsync(QuotaProvider.Codex); }
        catch (InvalidDataException) { duplicateRejected = true; }
        check(duplicateRejected, "Duplicate cached window identifiers cannot corrupt the quota layout");
    }

    private static async Task ResetAnchorTestsAsync(string directory, Action<bool, string> check)
    {
        DateTimeOffset reset = Now.AddHours(2);
        DateTimeOffset now = reset.AddMinutes(-20);
        DateTimeOffset reportedReset = reset;
        var store = new UsageStore(Path.Combine(directory, "quota-reset.db"));
        var client = new FakeClient((provider, _) => Task.FromResult(Reading(provider, 20, reportedReset)));
        using var monitor = new AccountQuotaMonitor(client, store, () => now);
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == reset.AddMinutes(-15), "Monitor wakes at reset band start instead of waiting an hour");
        now = reset.AddMinutes(1);
        reportedReset = reset.AddDays(7);
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == now.AddMinutes(5), "Rolled-forward server reset retains old reset's fast band");
        QuotaCache? cache = await store.ReadQuotaAsync(QuotaProvider.Codex);
        check(cache?.ResetAnchors.Contains(reset) == true && cache.ResetAnchors.Contains(reportedReset),
            "Both old and current reset anchors survive cache serialization");

        using (var restarted = new AccountQuotaMonitor(client, new UsageStore(store.DatabasePath), () => reset.AddMinutes(6)))
        {
            restarted.Start();
            await WaitAsync(() => restarted.Snapshot().All(state => state.CheckedAtUtc.HasValue && !state.IsRefreshing));
            check(State(restarted, QuotaProvider.Codex).NextCheckUtc == reset.AddMinutes(11),
                "Restart during old reset band restores 5m polling from persisted anchor");
            await restarted.StopAsync();
        }
        now = reset.AddMinutes(16);
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        check(State(monitor, QuotaProvider.Codex).NextCheckUtc == now.AddHours(1), "After old reset+15m monitor returns to ordinary cadence");
        cache = await store.ReadQuotaAsync(QuotaProvider.Codex);
        check(cache is not null && !cache.ResetAnchors.Contains(reset), "Expired anchors are pruned from persisted cache");
        await monitor.StopAsync();
    }

    private static DateTimeOffset Now => new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
    private static bool SameCache(QuotaCache expected, QuotaCache actual) =>
        expected.Version == actual.Version && expected.SuccessfulAtUtc == actual.SuccessfulAtUtc &&
        expected.Reading.Provider == actual.Reading.Provider && expected.Reading.Windows.SequenceEqual(actual.Reading.Windows) &&
        expected.ResetAnchors.SequenceEqual(actual.ResetAnchors);
    private static QuotaState State(AccountQuotaMonitor monitor, QuotaProvider provider) =>
        monitor.Snapshot().Single(state => state.Provider == provider);
    private static QuotaReading Reading(QuotaProvider provider, double used, DateTimeOffset? reset = null) =>
        new(provider, [new("account:5h", "5시간", used, reset, 300)]);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task WaitAsync(Func<bool> predicate) => MonitorTests.WaitUntilAsync(predicate, milliseconds: 5000);

    private static long Scalar(string path, string sql)
    {
        using var connection = Connection(path);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static void SetCache(string path, QuotaProvider provider, string json)
    {
        using var connection = Connection(path);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppMetadata(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
        command.Parameters.AddWithValue("$key", "AccountQuota.v1." + provider);
        command.Parameters.AddWithValue("$value", json);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Connection(string path) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5
    }.ToString());

    private sealed class FakeClient(Func<QuotaProvider, CancellationToken, Task<QuotaReading>> handler) : IAccountQuotaClient
    {
        private readonly int[] calls = new int[Enum.GetValues<QuotaProvider>().Length];
        private readonly int[] active = new int[Enum.GetValues<QuotaProvider>().Length];
        private readonly int[] maximumActive = new int[Enum.GetValues<QuotaProvider>().Length];
        private readonly object sync = new();
        public Func<QuotaProvider, CancellationToken, Task<QuotaReading>> Handler { get; set; } = handler;
        public int Calls(QuotaProvider provider) { lock (sync) return calls[(int)provider]; }
        public int MaximumActive(QuotaProvider provider) { lock (sync) return maximumActive[(int)provider]; }

        public async Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                calls[(int)provider]++;
                active[(int)provider]++;
                maximumActive[(int)provider] = Math.Max(maximumActive[(int)provider], active[(int)provider]);
            }
            try { return await Handler(provider, cancellationToken).ConfigureAwait(false); }
            finally { lock (sync) active[(int)provider]--; }
        }
    }
}
