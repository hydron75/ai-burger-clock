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
            check(QuotaPanelModel.Section(new(provider), Now).Detail.Contains(
                "Gemini 사용 0% 창의 리셋은 빠른 조회 기준에서 제외합니다.", StringComparison.Ordinal)
                == (provider == QuotaProvider.Gemini), "unused-reset exception is explained only in Gemini's Provider detail");
    }

    private static async Task MovingResetTestsAsync(Action<bool, string> check)
    {
        foreach (int offset in new[] { 300, 17, 10 })
        {
            DateTimeOffset now = Now;
            var client = new FakeClient(provider => new(provider,
                [Window(0, now.AddMinutes(offset)), Window(0, now.AddDays(7), "gemini-weekly", 10080),
                 Window(0, now.AddMinutes(offset), "gemini-model:synthetic-weekly", 10080)]));
            using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
            int calls = 0;
            for (; now <= Now.AddDays(7); now = State(monitor).NextCheckUtc!.Value)
            {
                await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
                check(State(monitor).NextCheckUtc == now.AddHours(1),
                    "seven virtual days of moving unused resets never create two/five-minute polling");
                calls++;
            }
            check(calls == 169 && client.Calls == 169, "seven inclusive virtual days perform 169 hourly checks without reset-driven extra queries");
            await monitor.StopAsync();
        }
    }

    private static async Task FailureTestsAsync(Action<bool, string> check)
    {
        DateTimeOffset now = Now;
        bool fail = false;
        double used = 0;
        var client = new FakeClient(provider => fail ? throw new IOException("Synthetic read failure")
            : new(provider, [Window(used, used == 0 ? now.AddMinutes(10) : null)]));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        fail = true;
        foreach (int minutes in new[] { 15, 30, 60, 60, 60, 60, 60 })
        {
            await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
            QuotaState state = State(monitor);
            check(state.NextCheckUtc == now.AddMinutes(minutes), "unused Gemini reset does not override exponential failure backoff");
            check(state.IsPrevious && state.LastSuccessfulCheckUtc == Now,
                "failed reads retain verified data and its original successful time");
            now = state.NextCheckUtc!.Value;
        }
        fail = false;
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        check(State(monitor).NextCheckUtc == now.AddHours(1), "success clears failures and unused reset polling remains hourly");
        fail = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        check(State(monitor).NextCheckUtc == now.AddMinutes(15), "a new failure episode starts at fifteen minutes");
        fail = false;
        used = 100;
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        fail = true;
        for (int index = 0; index < 4; index++)
        {
            await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
            check(State(monitor).NextCheckUtc == now.AddMinutes(15), "exhausted Gemini failure retries remain capped at fifteen minutes");
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
            check(State(monitor).NextCheckUtc == now.AddMinutes(15),
                "legacy Gemini's sixty-four unattributed anchors cannot shorten the first failed retry");
            check(State(monitor).IsPrevious && State(monitor).LastSuccessfulCheckUtc == now.AddHours(-1),
                "legacy anchor repair neither discards reading values nor invents a new successful observation");
            check((await store.ReadQuotaAsync(QuotaProvider.Gemini))?.ResetAnchorPolicyVersion == 0,
                "failed reads do not rewrite the legacy cache as a new successful cache");
            client.Handler = provider => provider == QuotaProvider.Gemini ? reading : new(provider, [Window(0, null)]);
            now = now.AddMinutes(1);
            await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
            var repaired = await store.ReadQuotaAsync(QuotaProvider.Gemini);
            check(repaired?.ResetAnchorPolicyVersion == 1 && repaired.ResetAnchors.SequenceEqual([Now.AddDays(3)]),
                "the next success saves only eligible legacy reading anchors with the new provenance marker");
            check(State(monitor).NextCheckUtc == now.AddHours(1), "mixed zero/positive legacy readings do not reintroduce discarded anchors");
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
        DateTimeOffset now = reset.AddMinutes(-20);
        double used = 20;
        DateTimeOffset reportedReset = reset;
        var store = new UsageStore(Path.Combine(directory, "gemini-polling-trusted.db"));
        await store.InitializeAsync();
        var client = new FakeClient(provider => new(provider,
            [Window(provider == QuotaProvider.Gemini ? used : 0, provider == QuotaProvider.Gemini ? reportedReset : null)]));
        using (var monitor = new AccountQuotaMonitor(client, store, () => now))
        {
            await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
            check(State(monitor).NextCheckUtc == reset.AddMinutes(-15), "positive Gemini usage enters its genuine reset band");
            now = reset.AddMinutes(-10);
            used = 0;
            reportedReset = reset.AddHours(5);
            await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
            check(State(monitor).NextCheckUtc == now.AddMinutes(5),
                "a positive window's old anchor survives an early rollover to zero use");
            now = reset.AddMinutes(-5);
            await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
            check(State(monitor).NextCheckUtc == reset, "the retained future old anchor survives another zero-use response");
            now = reset.AddMinutes(1);
            await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
            var cached = await store.ReadQuotaAsync(QuotaProvider.Gemini);
            check(cached?.ResetAnchorPolicyVersion == 1 && cached.ResetAnchors.SequenceEqual([reset]),
                "trusted cache retains the genuine old reset but never adds the new zero-use reset");
            await monitor.StopAsync();
        }

        now = reset.AddMinutes(6);
        using (var restarted = new AccountQuotaMonitor(client, store, () => now))
        {
            restarted.Start();
            await WaitForInitialReadsAsync(restarted);
            check(State(restarted).NextCheckUtc == reset.AddMinutes(11),
                "restart keeps the trusted old plus-fifteen-minute band even with zero current usage");
            now = reset.AddMinutes(15);
            await restarted.RefreshOnceAsync(QuotaProvider.Gemini);
            check(State(restarted).NextCheckUtc == now.AddHours(1), "the inclusive band's end does not schedule a trailing fast check");
            now = now.AddTicks(1);
            await restarted.RefreshOnceAsync(QuotaProvider.Gemini);
            var cached = await store.ReadQuotaAsync(QuotaProvider.Gemini);
            check(cached is { ResetAnchorPolicyVersion: 1 } && cached.ResetAnchors.Count == 0,
                "expired trusted anchors are removed from the new cache");
            check(State(restarted).NextCheckUtc == now.AddHours(1), "zero-use reading returns to ordinary cadence after the genuine band");
            await restarted.StopAsync();
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

    private static QuotaWindow Window(double used, DateTimeOffset? reset, string id = "gemini-5h", int minutes = 300) =>
        new(id, id == "gemini-5h" ? "5시간" : "주간 합성", used, reset, minutes);
    private static QuotaState State(AccountQuotaMonitor monitor) => monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini);
    private static Task WaitForInitialReadsAsync(AccountQuotaMonitor monitor) => MonitorTests.WaitUntilAsync(
        () => monitor.Snapshot().All(s => s.CheckedAtUtc.HasValue && !s.IsRefreshing), milliseconds: 5000);
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
