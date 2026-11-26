using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiBurgerClock;

// Only synthetic readings, an injected clock and temporary databases; never starts a CLI.
internal static class GeminiPollingPolicyTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 10, 10, 0, 0, TimeSpan.Zero);

    internal static async Task<int> RunAsync(string directory)
    {
        int assertions = 0;
        void Check(bool condition, string label)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Gemini polling policy: " + label);
        }

        Check(AccountQuotaPolicy.GetNextCheckUtc(Now, Now, [Window(0, Now.AddMinutes(10))],
            provider: QuotaProvider.Gemini) == Now.AddHours(1),
            "unused Gemini reset near now must not replace the hourly interval with five-minute polling");
        PolicyTests(Check);
        await MovingResetTestsAsync(Check);
        await FailureTestsAsync(Check);
        await CompletionWaitTestsAsync(Check);
        await LegacyCacheTestsAsync(directory, Check);
        await TrustedCacheTestsAsync(directory, Check);
        CacheJsonTests(Check);
        return assertions;
    }

    private static void PolicyTests(Action<bool, string> check)
    {
        QuotaWindow[] kinds = [Window(0, Now), Window(0, Now, "gemini-weekly", 10080),
            Window(0, Now, "gemini-model:synthetic-weekly", 10080)];
        foreach (QuotaWindow window in kinds)
        {
            foreach (int minutes in new[] { -16, -15, -10, 0, 10, 15, 16, 17, 300 })
            {
                var unused = window with { ResetsAtUtc = Now.AddMinutes(minutes) };
                check(AccountQuotaPolicy.GetInterval(Now, [unused], provider: QuotaProvider.Gemini)
                    == TimeSpan.FromHours(1), "each unused Gemini window is excluded from a live reset band");
                check(AccountQuotaPolicy.GetNextCheckUtc(Now, Now, [unused], provider: QuotaProvider.Gemini)
                    == Now.AddHours(1), "each unused Gemini window cannot schedule a future reset-band entry");
            }
            foreach (double invalidOrZero in new[] { 0d, -0d, -1, double.NaN,
                double.PositiveInfinity, double.NegativeInfinity, 100.01 })
                check(!AccountQuotaPolicy.CurrentResetAnchors([window with { UsedPercent = invalidOrZero }],
                    QuotaProvider.Gemini).Any(), "only finite, positive, valid Gemini usage can create an anchor");

            foreach (double positive in new[] { 0.0001, 90, 90.01, 99.96, 100 })
                check(AccountQuotaPolicy.GetInterval(Now, [window with { UsedPercent = positive }],
                    provider: QuotaProvider.Gemini) == TimeSpan.FromMinutes(5),
                    "positive Gemini usage keeps the real reset band, without display rounding");
        }

        foreach (QuotaProvider? provider in new QuotaProvider?[] { null, QuotaProvider.Codex, QuotaProvider.Claude })
        {
            check(AccountQuotaPolicy.GetInterval(Now, [Window(0, Now)], provider: provider) == TimeSpan.FromMinutes(5),
                "legacy callers and other providers retain zero-use reset bands");
            check(AccountQuotaPolicy.GetNextCheckUtc(Now, Now, [Window(0, Now.AddMinutes(17))], provider: provider)
                == Now.AddMinutes(2), "other providers retain their earlier future reset-band entry");
        }

        foreach ((double used, TimeSpan interval) in new[]
        {
            (0d, TimeSpan.FromHours(1)), (90d, TimeSpan.FromHours(1)), (90.01, TimeSpan.FromHours(1)),
            (99.96, TimeSpan.FromHours(1)), (100d, TimeSpan.FromMinutes(15))
        })
            check(AccountQuotaPolicy.GetInterval(Now, [Window(0, Now), Window(used, null, "other")],
                provider: QuotaProvider.Gemini) == interval,
                "ignoring an unused reset does not change another window's ordinary usage interval");

        check(AccountQuotaPolicy.GetInterval(Now, [Window(0, Now.AddHours(5))], [Now], QuotaProvider.Gemini)
            == TimeSpan.FromMinutes(5), "a trusted previous anchor survives a current zero-use reading");
        check(AccountQuotaPolicy.GetNextCheckUtc(Now, Now, [Window(0.0001, Now.AddMinutes(17))],
            provider: QuotaProvider.Gemini) == Now.AddMinutes(2), "used windows can still enter a genuine band in two minutes");
        check(AccountQuotaPolicy.GetNextCheckUtc(Now, Now.AddDays(-1), [Window(0, Now)],
            provider: QuotaProvider.Gemini) == Now, "overdue or resumed unused quotas remain immediately due");

        foreach (int minutes in new[] { -15, 0, 10, 15 })
            check(AccountQuotaPolicy.GetInterval(Now.AddMinutes(minutes), [Window(0.0001, Now)],
                provider: QuotaProvider.Gemini) == TimeSpan.FromMinutes(5), "positive reset bands retain inclusive boundaries");
        check(AccountQuotaPolicy.GetNextCheckUtc(Now.AddMinutes(15), Now.AddMinutes(15), [Window(20, Now)],
            provider: QuotaProvider.Gemini) == Now.AddMinutes(15).AddHours(1), "no trailing five-minute poll follows a genuine band");
        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
            check(QuotaPanelModel.Section(new(provider, Mode: provider == QuotaProvider.Gemini
                ? QuotaSourceMode.Unavailable : QuotaSourceMode.Active), Now).Detail.Contains(
                "Antigravity 약관상", StringComparison.Ordinal)
                == (provider == QuotaProvider.Gemini), "automatic-query stop is explained only in Gemini's Provider detail");
    }

    private static async Task MovingResetTestsAsync(Action<bool, string> check)
    {
        foreach (int offset in new[] { 300, 17, 10 })
        {
            DateTimeOffset now = Now;
            var client = new FakeClient(provider => new(provider,
                [Window(0, now.AddMinutes(offset)), Window(0, now.AddDays(7), "gemini-weekly", 10080)]));
            using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
            for (; now <= Now.AddDays(7); now = now.AddHours(1))
            {
                await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
                check(State(monitor) is { Reading: null, NextCheckUtc: null, CheckedAtUtc: null },
                    "seven virtual days of unused Gemini resets cannot create a query or schedule");
            }
            check(client.Calls == 0, "repeated Gemini refresh requests never reach the client");
            await monitor.StopAsync();
        }
    }

    private static async Task FailureTestsAsync(Action<bool, string> check)
    {
        DateTimeOffset now = Now;
        bool fail = false;
        double used = 0;
        var client = new FakeClient(provider => fail ? throw new IOException("Synthetic read failure")
            : new(provider, [Window(used, null)]));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
        fail = true;
        foreach (int minutes in new[] { 15, 30, 60, 60, 60, 60, 60 })
        {
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
            QuotaState state = State(monitor, QuotaProvider.Claude);
            check(state.NextCheckUtc == now.AddMinutes(minutes), "enabled provider retains exponential failure backoff");
            check(state.IsPrevious && state.LastSuccessfulCheckUtc == Now,
                "failed reads retain verified data and its original successful time");
            now = state.NextCheckUtc!.Value;
        }
        fail = false;
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
        check(State(monitor, QuotaProvider.Claude).NextCheckUtc == now.AddHours(1), "success clears failures and ordinary polling remains hourly");
        fail = true;
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
        check(State(monitor, QuotaProvider.Claude).NextCheckUtc == now.AddMinutes(15), "a new failure episode starts at fifteen minutes");
        fail = false;
        used = 100;
        await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
        fail = true;
        for (int index = 0; index < 4; index++)
        {
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
            check(State(monitor, QuotaProvider.Claude).NextCheckUtc == now.AddMinutes(15), "exhausted enabled-provider failure retries remain capped at fifteen minutes");
        }
        await monitor.StopAsync();
    }

    private static async Task LegacyCacheTestsAsync(string directory, Action<bool, string> check)
    {
        DateTimeOffset now = Now;
        string path = Path.Combine(directory, "gemini-polling-legacy.db");
        var store = new UsageStore(path);
        await store.InitializeAsync();
        var reading = new QuotaReading(QuotaProvider.Gemini,
            [Window(0, now.AddMinutes(10)), Window(20, now.AddDays(3), "gemini-weekly", 10080)]);
        DateTimeOffset[] polluted = Enumerable.Range(0, 64).Select(i => now.AddMinutes(10 + i * 5)).ToArray();
        await store.SaveQuotaAsync(new(1, reading, now.AddHours(-1), polluted));
        var client = new FakeClient(provider => provider == QuotaProvider.Gemini
            ? throw new IOException("Synthetic first read failure") : new(provider, [Window(0, null)]));
        using (var monitor = new AccountQuotaMonitor(client, store, () => now))
        {
            monitor.Start();
            await WaitForInitialReadsAsync(monitor);
            check(State(monitor) is { Reading: null, CheckedAtUtc: null, NextCheckUtc: null },
                "disabled Gemini never loads legacy values or reset anchors into runtime state");
            check((await store.ReadQuotaAsync(QuotaProvider.Gemini)) is { ResetAnchorPolicyVersion: 0 } original
                && original.ResetAnchors.SequenceEqual(polluted) && original.Reading.Windows.SequenceEqual(reading.Windows),
                "automatic query stop preserves the complete legacy Gemini cache");
            client.Handler = provider => provider == QuotaProvider.Gemini ? reading : new(provider, [Window(0, null)]);
            now = now.AddMinutes(1);
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
            var preserved = await store.ReadQuotaAsync(QuotaProvider.Gemini);
            check(preserved?.ResetAnchorPolicyVersion == 0 && preserved.ResetAnchors.SequenceEqual(polluted),
                "explicit disabled refresh cannot migrate or overwrite the legacy cache");
            check(State(monitor).LastSuccessfulCheckUtc is null, "preserved disk cache never invents Gemini runtime success");
            await monitor.StopAsync();
        }
        check(Scalar(path, "PRAGMA user_version;") == 2 &&
            Scalar(path, "SELECT COUNT(*) FROM AppMetadata WHERE Key='AccountQuota.v1.Gemini';") == 1,
            "anchor migration changes neither SQLite schema nor the existing Gemini metadata key");

        foreach (QuotaProvider provider in new[] { QuotaProvider.Codex, QuotaProvider.Claude })
        {
            var otherStore = new UsageStore(Path.Combine(directory, "gemini-polling-legacy-" + provider + ".db"));
            await otherStore.InitializeAsync();
            await otherStore.SaveQuotaAsync(new(1, new(provider, [Window(0, now.AddDays(7))]), now.AddHours(-1), [now]));
            var offline = new FakeClient(p => p == provider ? throw new IOException("Synthetic offline restart")
                : new(p, [Window(0, null)]));
            using var restarted = new AccountQuotaMonitor(offline, otherStore, () => now);
            restarted.Start();
            await WaitForInitialReadsAsync(restarted);
            check(restarted.Snapshot().Single(s => s.Provider == provider).NextCheckUtc == now.AddMinutes(5),
                "other providers' legacy previous reset anchors remain intact on restart and failure");
            await restarted.StopAsync();
        }
    }

    private static async Task TrustedCacheTestsAsync(string directory, Action<bool, string> check)
    {
        DateTimeOffset reset = Now.AddHours(2);
        var store = new UsageStore(Path.Combine(directory, "gemini-polling-trusted.db"));
        await store.InitializeAsync();
        var trusted = new QuotaCache(1, new(QuotaProvider.Gemini, [Window(0, reset.AddHours(5))]),
            Now, [reset], AccountQuotaPolicy.ResetAnchorPolicyVersion);
        await store.SaveQuotaAsync(trusted);
        var client = new FakeClient(provider => new(provider, [Window(20, reset)]));
        foreach (DateTimeOffset now in new[] { reset.AddMinutes(-10), reset.AddMinutes(6), reset.AddMinutes(16) })
        {
            using var monitor = new AccountQuotaMonitor(client, store, () => now);
            monitor.Start();
            await WaitForInitialReadsAsync(monitor);
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Gemini);
            check(State(monitor) is { Reading: null, LastSuccessfulCheckUtc: null, NextCheckUtc: null },
                "disabled Gemini ignores trusted reset bands before, during and after expiry");
            var preserved = await store.ReadQuotaAsync(QuotaProvider.Gemini);
            check(preserved is { ResetAnchorPolicyVersion: 1 } && preserved.ResetAnchors.SequenceEqual([reset])
                && preserved.SuccessfulAtUtc == trusted.SuccessfulAtUtc
                && preserved.Reading.Windows.SequenceEqual(trusted.Reading.Windows),
                "restart and disabled refresh preserve trusted Gemini values, receipt and anchors");
            await monitor.StopAsync();
        }
    }

    private static void CacheJsonTests(Action<bool, string> check)
    {
        var legacy = new QuotaCache(1, new(QuotaProvider.Gemini, [Window(20, Now.AddHours(1))]), Now, [Now.AddHours(1)]);
        string legacyJson = JsonSerializer.Serialize(legacy, QuotaJsonContext.Default.QuotaCache);
        var legacyObject = JsonNode.Parse(legacyJson)!.AsObject();
        check(!legacyObject.ContainsKey("ResetAnchorPolicyVersion"), "default marker is omitted, preserving legacy cache JSON");
        check(JsonSerializer.Deserialize(legacyJson, QuotaJsonContext.Default.QuotaCache)?.ResetAnchorPolicyVersion == 0,
            "source-generated reader treats a missing marker as legacy policy zero");
        var current = legacy with { ResetAnchorPolicyVersion = 1 };
        string currentJson = JsonSerializer.Serialize(current, QuotaJsonContext.Default.QuotaCache);
        var restored = JsonSerializer.Deserialize(currentJson, QuotaJsonContext.Default.QuotaCache);
        check(restored?.ResetAnchorPolicyVersion == 1 && restored.Version == 1 &&
            restored.Reading.Windows.SequenceEqual(legacy.Reading.Windows) && restored.ResetAnchors.SequenceEqual(legacy.ResetAnchors),
            "new cache marker roundtrips without changing the reading, anchors or cache payload version");
        var currentObject = JsonNode.Parse(currentJson)!.AsObject();
        currentObject.Remove("ResetAnchorPolicyVersion");
        check(JsonNode.DeepEquals(legacyObject, currentObject), "all pre-existing JSON fields are unchanged by the optional marker");
        foreach (int invalid in new[] { -1, 2, int.MaxValue })
        {
            bool rejected = false;
            try { UsageStore.ValidateQuotaCache(current with { ResetAnchorPolicyVersion = invalid }, QuotaProvider.Gemini); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "unsupported anchor-policy markers cannot silently make untrusted anchors trusted");
        }
    }

    private static async Task CompletionWaitTestsAsync(Action<bool, string> check)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient(provider => new(provider, [Window(0, null)]));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => Now);
        monitor.Succeeded += observation =>
        {
            if (observation.Provider != QuotaProvider.Claude) return;
            entered.TrySetResult();
            if (!release.Task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic callback release was not signalled.");
        };
        monitor.Start();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await MonitorTests.WaitUntilAsync(() => monitor.Snapshot().Where(state => state.Provider != QuotaProvider.Gemini).All(state => state.CheckedAtUtc.HasValue && !state.IsRefreshing));
            check(client.Calls == 3, "all initial reads have returned while the synthetic success callback still holds its provider gate");
            Task completed = WaitForInitialReadsAsync(monitor);
            check(!completed.IsCompleted, "the fixture must not mistake idle display state for completed success callbacks");
            release.TrySetResult();
            await completed.WaitAsync(TimeSpan.FromSeconds(5));
            check(completed.IsCompletedSuccessfully, "the fixture completes promptly after the provider callback releases its gate");
            int calls = client.Calls;
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude).WaitAsync(TimeSpan.FromSeconds(5));
            check(client.Calls == calls + 1 && State(monitor, QuotaProvider.Claude).NextCheckUtc == Now.AddHours(1),
                "the next direct read executes instead of being skipped by an unfinished initial callback");
        }
        finally
        {
            release.TrySetResult();
            await monitor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static QuotaWindow Window(double used, DateTimeOffset? reset, string id = "gemini-5h", int minutes = 300) =>
        new(id, id == "gemini-5h" ? "5시간" : "주간 합성", used, reset, minutes);
    private static QuotaState State(AccountQuotaMonitor monitor, QuotaProvider provider = QuotaProvider.Gemini) =>
        monitor.Snapshot().Single(s => s.Provider == provider);
    private static async Task WaitForInitialReadsAsync(AccountQuotaMonitor monitor)
    {
        await MonitorTests.WaitUntilAsync(
            () => monitor.Snapshot().Where(s => s.Provider != QuotaProvider.Gemini).All(s => s.CheckedAtUtc.HasValue && !s.IsRefreshing), milliseconds: 5000);
        // IsRefreshing is already false inside Succeeded, before the gate is released.
        // Wait for actual completion in this fixture, without a sleep or another read.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var sources = (System.Collections.IDictionary)typeof(AccountQuotaMonitor).GetField("sources", flags)!.GetValue(monitor)!;
        foreach (var entry in sources.Values)
        {
            var gate = (SemaphoreSlim)entry!.GetType().GetField("Gate", flags)!.GetValue(entry)!;
            if (!await gate.WaitAsync(TimeSpan.FromSeconds(5))) throw new TimeoutException("Initial quota callback did not finish.");
            gate.Release();
        }
    }
    private static long Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private sealed class FakeClient(Func<QuotaProvider, QuotaReading> handler) : IAccountQuotaClient
    {
        private int calls;
        internal Func<QuotaProvider, QuotaReading> Handler { get; set; } = handler;
        internal int Calls => Volatile.Read(ref calls);
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            return Task.FromResult(Handler(provider));
        }
    }
}
