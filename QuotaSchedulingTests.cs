using System.Text.Json;

namespace AiBurgerClock;

// Synthetic clocks/readings and temporary files only; never starts a real CLI.
internal static class QuotaSchedulingTests
{
    private static readonly DateTimeOffset Now = new(2027, 4, 12, 10, 0, 0, TimeSpan.Zero);

    internal static async Task<int> RunAsync(string directory)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Quota scheduling test: " + message);
        }

        CoalescingTests(Check);
        DecisionTests(Check);
        await DiagnosticIntegrationTestsAsync(directory, Check);
        await CanceledReadTestsAsync(directory, Check);
        await UnwritableDiagnosticTestsAsync(directory, Check);
        await RestartTestsAsync(directory, Check);
        return assertions;
    }

    private static void CoalescingTests(Action<bool, string> check)
    {
        DateTimeOffset first = Now.AddHours(2);
        DateTimeOffset second = Now.AddDays(3);
        DateTimeOffset[] jitter = Enumerable.Range(0, 32).SelectMany(index => new[]
            { first.AddMilliseconds(index * 30), second.AddMilliseconds(index * 30) }).ToArray();
        DateTimeOffset[] compact = AccountQuotaPolicy.CoalesceResetAnchors(jitter);
        check(compact.SequenceEqual(new[] { first, first.AddMilliseconds(930), second, second.AddMilliseconds(930) }),
            "sixty-four jittered anchors around two real resets retain only each pair of band endpoints");
        check(AccountQuotaPolicy.CoalesceResetAnchors(compact).SequenceEqual(compact),
            "coalescing already compacted endpoints is idempotent");
        check(AccountQuotaPolicy.CoalesceResetAnchors(jitter.Reverse()).SequenceEqual(compact),
            "input order does not change coalesced reset endpoints");
        check(AccountQuotaPolicy.CoalesceResetAnchors([first, first, first]).SequenceEqual([first]),
            "identical instants need only one endpoint");
        DateTimeOffset[] utc = AccountQuotaPolicy.CoalesceResetAnchors([first.ToOffset(TimeSpan.FromHours(9)), first]);
        check(utc.Length == 1 && utc[0] == first && utc[0].Offset == TimeSpan.Zero,
            "the same instant in different offsets is stored once in UTC");
        check(AccountQuotaPolicy.CoalesceResetAnchors([]).Length == 0, "an empty anchor collection remains empty");

        DateTimeOffset[] boundary = AccountQuotaPolicy.CoalesceResetAnchors([first, first.AddSeconds(30), first.AddMinutes(1)]);
        check(boundary.SequenceEqual([first, first.AddMinutes(1)]), "a group includes exactly a one-minute span");
        DateTimeOffset[] beyond = AccountQuotaPolicy.CoalesceResetAnchors(
            [first, first.AddSeconds(30), first.AddMinutes(1).AddTicks(1)]);
        check(beyond.SequenceEqual([first, first.AddSeconds(30), first.AddMinutes(1).AddTicks(1)]),
            "one minute plus one tick starts a separate group");
        DateTimeOffset[] chain = AccountQuotaPolicy.CoalesceResetAnchors(
            [first, first.AddSeconds(25), first.AddSeconds(50), first.AddSeconds(75), first.AddSeconds(100), first.AddSeconds(125)]);
        check(chain.SequenceEqual([first, first.AddSeconds(50), first.AddSeconds(75), first.AddSeconds(125)]),
            "adjacent sub-minute gaps cannot chain a group beyond a one-minute total span");

        foreach (int maximum in new[] { 0, 1, 2, 3, 4, 64 })
        {
            DateTimeOffset[] capped = AccountQuotaPolicy.CoalesceResetAnchors(jitter, maximum);
            check(capped.Length <= maximum, "the stored endpoint limit is respected");
            check(capped.Length % 2 == 0, "a count limit never retains only half of a two-endpoint jitter group");
        }
        DateTimeOffset[] singlesAndPair = AccountQuotaPolicy.CoalesceResetAnchors(
            [first, first.AddHours(1), first.AddHours(1).AddSeconds(1)], 2);
        check(singlesAndPair.SequenceEqual([first]), "a singleton is retained without cutting the next group's endpoint pair");

        // Independent interval membership, not the implementation's helper: compression
        // must not shift the earliest entry or latest exit of any retained reset band.
        DateTimeOffset[] samples = jitter.Concat(compact).SelectMany(reset => new[]
        {
            reset.AddMinutes(-15).AddTicks(-1), reset.AddMinutes(-15), reset.AddMinutes(-15).AddTicks(1),
            reset, reset.AddMinutes(15).AddTicks(-1), reset.AddMinutes(15), reset.AddMinutes(15).AddTicks(1)
        }).Distinct().Order().ToArray();
        bool unchanged = samples.All(instant => InRawBand(instant, jitter) == InRawBand(instant, compact));
        check(unchanged, "raw and compacted anchors have exactly the same inclusive reset-band union at every synthetic boundary");
        check(compact[0].AddMinutes(-15) == jitter.Min().AddMinutes(-15)
            && compact[^1].AddMinutes(15) == jitter.Max().AddMinutes(15),
            "compression preserves the first entry and final exit instead of rounding reset times");

        DateTimeOffset edge = first.AddMinutes(15).AddTicks(1);
        DateTimeOffset[] surviving = compact.Where(reset => reset.AddMinutes(15) >= edge).ToArray();
        check(InRawBand(edge, surviving) && surviving[0] == first.AddMilliseconds(930),
            "expiring the earlier endpoint retains the later endpoint's entire remaining band");
    }

    private static bool InRawBand(DateTimeOffset instant, IEnumerable<DateTimeOffset> anchors) =>
        anchors.Any(reset => instant >= reset.AddMinutes(-15) && instant <= reset.AddMinutes(15));

    private static void DecisionTests(Action<bool, string> check)
    {
        void Plan(DateTimeOffset now, DateTimeOffset last, QuotaWindow[] windows,
            DateTimeOffset next, QuotaCheckReason reason, DateTimeOffset? selectedReset = null,
            DateTimeOffset[]? previous = null, QuotaProvider? provider = null)
        {
            QuotaCheckPlan plan = AccountQuotaPolicy.GetNextCheckPlan(now, last, windows, previous, provider);
            check(plan.NextCheckUtc == next && plan.Reason == reason && plan.SelectedResetUtc == selectedReset,
                "a scheduling decision reports its actual deadline, reason and selected reset");
            check(AccountQuotaPolicy.GetNextCheckUtc(now, last, windows, previous, provider) == plan.NextCheckUtc,
                "the existing deadline-only API delegates to the same scheduling decision");
            check(plan.NextCheckUtc.Offset == TimeSpan.Zero,
                "scheduling decisions use UTC instants regardless of input display offset");
        }

        Plan(Now, Now, [Window(0)], Now.AddHours(6), QuotaCheckReason.Regular);
        Plan(Now, Now, [Window(90)], Now.AddHours(6), QuotaCheckReason.Regular);
        Plan(Now, Now, [Window(90.0001)], Now.AddHours(1), QuotaCheckReason.LowRemaining);
        Plan(Now, Now, [Window(99.96)], Now.AddHours(1), QuotaCheckReason.LowRemaining);
        Plan(Now, Now, [Window(100)], Now.AddMinutes(15), QuotaCheckReason.Exhausted);
        Plan(Now, Now, [Window(20), Window(100, id: "weekly", minutes: 10080)],
            Now.AddMinutes(15), QuotaCheckReason.Exhausted);
        Plan(Now, Now, [], Now.AddHours(6), QuotaCheckReason.Regular);
        Plan(Now, Now.AddHours(-7), [Window(20)], Now, QuotaCheckReason.Overdue);
        Plan(Now.ToOffset(TimeSpan.FromHours(9)), Now.ToOffset(TimeSpan.FromHours(9)),
            [Window(20)], Now.AddHours(6), QuotaCheckReason.Regular);

        DateTimeOffset reset = Now.AddHours(2);
        Plan(Now, Now, [Window(20, reset)], reset.AddMinutes(-15), QuotaCheckReason.ResetEntry, reset);
        Plan(reset.AddMinutes(-15), reset.AddMinutes(-15), [Window(100, reset)],
            reset.AddMinutes(-10), QuotaCheckReason.ResetPeriod, reset);
        Plan(reset, reset, [Window(20, reset)], reset.AddMinutes(5), QuotaCheckReason.ResetPeriod, reset);
        Plan(reset.AddMinutes(10), reset.AddMinutes(10), [Window(20, reset)],
            reset.AddMinutes(15), QuotaCheckReason.ResetPeriod, reset);
        Plan(reset.AddMinutes(15), reset.AddMinutes(15), [Window(20, reset)],
            reset.AddMinutes(15).AddHours(6), QuotaCheckReason.Regular);
        Plan(reset.AddMinutes(15), reset.AddMinutes(15), [Window(95, reset)],
            reset.AddMinutes(15).AddHours(1), QuotaCheckReason.LowRemaining);
        Plan(reset.AddMinutes(15), reset.AddMinutes(15), [Window(100, reset)],
            reset.AddMinutes(30), QuotaCheckReason.Exhausted);
        Plan(Now, Now, [Window(0, Now.AddMinutes(10))], Now.AddHours(6), QuotaCheckReason.Regular,
            provider: QuotaProvider.Gemini);
        Plan(Now, Now, [Window(0.0001, Now.AddMinutes(17))], Now.AddMinutes(2),
            QuotaCheckReason.ResetEntry, Now.AddMinutes(17), provider: QuotaProvider.Gemini);
        Plan(Now, Now, [Window(0, Now.AddHours(5))], Now.AddMinutes(5), QuotaCheckReason.ResetPeriod,
            Now, [Now], QuotaProvider.Gemini);

        // Deliberate narrowly scoped difference: the intermediate jitter entry is
        // dropped, but the reset-band union and the true initial entry are unchanged.
        DateTimeOffset before = reset.AddMinutes(-15).AddSeconds(10);
        DateTimeOffset[] jitter = [reset, reset.AddSeconds(20), reset.AddSeconds(40)];
        QuotaCheckPlan compressed = AccountQuotaPolicy.GetNextCheckPlan(before, before, [], jitter);
        check(compressed.NextCheckUtc == reset.AddMinutes(-15).AddSeconds(40)
            && compressed.Reason == QuotaCheckReason.ResetEntry && compressed.SelectedResetUtc == reset.AddSeconds(40),
            "coalescing removes only the redundant intermediate entry within a one-minute jitter group");
    }

    private static async Task DiagnosticIntegrationTestsAsync(string directory, Action<bool, string> check)
    {
        string root = Path.Combine(directory, "quota-scheduling-live");
        var log = new QuotaPollingDiagnosticLog(root);
        DateTimeOffset now = Now;
        double used = 20;
        DateTimeOffset? reset = null;
        bool fail = false;
        var client = new FakeClient((provider, _) => fail
            ? Task.FromException<QuotaReading>(new IOException("Synthetic query failure"))
            : Task.FromResult(new QuotaReading(provider, [Window(used, reset)])));
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now, diagnosticLog: log);

        foreach ((QuotaProvider provider, double percent, QuotaCheckReason reason) in new[]
        {
            (QuotaProvider.Codex, 20d, QuotaCheckReason.Regular),
            (QuotaProvider.Claude, 95d, QuotaCheckReason.LowRemaining),
            (QuotaProvider.Gemini, 100d, QuotaCheckReason.Exhausted)
        })
        {
            used = percent;
            await monitor.RefreshOnceAsync(provider);
            QuotaPollingDiagnostic entry = Read(log).Entries[^1];
            check(entry.Provider == provider.ToString() && entry.Reason == reason.ToString(),
                "each Provider's real monitor schedule is logged with the selected usage reason");
            check(entry.NextCheckUtc == State(monitor, provider).NextCheckUtc
                && entry.AttemptedAtUtc == now && entry.SelectedResetUtc is null,
                "the diagnostic deadline is the same one the monitor actually stores");
            check(entry.Succeeded && entry.ConsecutiveFailures == 0 && entry.ResetAnchorCount == 0,
                "successful ordinary reads record a reset failure counter and no invented reset anchor");
        }

        fail = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        QuotaPollingDiagnostic failed = Read(log).Entries[^1];
        check(!failed.Succeeded && failed.ConsecutiveFailures == 1
            && failed.Reason == QuotaCheckReason.FailureRetry.ToString()
            && failed.NextCheckUtc == now.AddMinutes(15) && failed.SelectedResetUtc is null,
            "a retry tied with an exhausted deadline records failure retry without changing the deadline");
        check(State(monitor, QuotaProvider.Gemini).IsPrevious
            && State(monitor, QuotaProvider.Gemini).LastSuccessfulCheckUtc == Now,
            "diagnostics neither convert a failure into success nor replace the previous verified reading");

        fail = false;
        used = 20;
        reset = now.AddMinutes(17);
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        QuotaPollingDiagnostic bandEntry = Read(log).Entries[^1];
        check(bandEntry.Reason == QuotaCheckReason.ResetEntry.ToString()
            && bandEntry.SelectedResetUtc == reset && bandEntry.NextCheckUtc == now.AddMinutes(2),
            "a genuine upcoming band logs the actual reset responsible for its earlier entry");
        fail = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        QuotaPollingDiagnostic earlierBand = Read(log).Entries[^1];
        check(!earlierBand.Succeeded && earlierBand.Reason == QuotaCheckReason.ResetEntry.ToString()
            && earlierBand.NextCheckUtc == now.AddMinutes(2),
            "an earlier genuine reset entry still wins over the first fifteen-minute failed retry");
        fail = false;
        now = now.AddMinutes(2);
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        QuotaPollingDiagnostic inBand = Read(log).Entries[^1];
        check(inBand.Reason == QuotaCheckReason.ResetPeriod.ToString()
            && inBand.SelectedResetUtc == reset && inBand.NextCheckUtc == now.AddMinutes(5)
            && inBand.ConsecutiveFailures == 0,
            "entry into the band records five-minute scheduling and success resets backoff");
        check(Read(log).Version == 1 && Read(log).Entries.Count == 7,
            "one completed accepted or failed refresh writes one versioned scheduling record");
        await monitor.StopAsync();

        // A store-backed monitor selects its private sibling logs directory by default.
        var store = new UsageStore(Path.Combine(directory, "quota-scheduling-default", "temporary.db"));
        using var automatic = new AccountQuotaMonitor(new FakeClient((provider, _) =>
            Task.FromResult(new QuotaReading(provider, [Window(20)]))), store, () => Now);
        await automatic.RefreshOnceAsync(QuotaProvider.Claude);
        var expectedDefault = new QuotaPollingDiagnosticLog(Path.Combine(Path.GetDirectoryName(store.DatabasePath)!, "logs"));
        check(Read(expectedDefault).Entries.Single().Provider == QuotaProvider.Claude.ToString(),
            "store-backed monitoring writes diagnostics beside its own temporary database, not user app data");
        await automatic.StopAsync();
    }

    private static async Task CanceledReadTestsAsync(string directory, Action<bool, string> check)
    {
        var log = new QuotaPollingDiagnosticLog(Path.Combine(directory, "quota-scheduling-canceled"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var client = new FakeClient(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Canceled synthetic read cannot finish.");
        });
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => Now, diagnosticLog: log);
        Task refresh = monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        bool canceled = false;
        try { await refresh; }
        catch (OperationCanceledException) { canceled = true; }
        check(canceled && !File.Exists(log.LogPath), "a canceled in-progress read never invents a next schedule record");
        check(State(monitor, QuotaProvider.Claude).NextCheckUtc is null
            && !State(monitor, QuotaProvider.Claude).IsRefreshing,
            "cancellation leaves no scheduled retry and releases the refreshing state");
        await monitor.StopAsync();
    }

    private static async Task UnwritableDiagnosticTestsAsync(string directory, Action<bool, string> check)
    {
        string blocked = Path.Combine(directory, "quota-scheduling-blocked-parent");
        await File.WriteAllTextAsync(blocked, "Synthetic file blocks a diagnostic directory.");
        var log = new QuotaPollingDiagnosticLog(blocked);
        using var monitor = new AccountQuotaMonitor(new FakeClient((provider, _) =>
            Task.FromResult(new QuotaReading(provider, [Window(20)]))), utcNow: () => Now, diagnosticLog: log);
        int observations = 0;
        monitor.Succeeded += _ => observations++;
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        QuotaState state = State(monitor, QuotaProvider.Gemini);
        check(state.LastSuccessfulCheckUtc == Now && state.NextCheckUtc == Now.AddHours(6)
            && !state.IsPrevious && state.Error.Length == 0 && observations == 1,
            "a diagnostic write failure cannot fail polling, change its deadline or suppress a successful observation");
        check(await File.ReadAllTextAsync(blocked) == "Synthetic file blocks a diagnostic directory.",
            "failed logging does not replace the unrelated blocking fixture");
        await monitor.StopAsync();
    }

    private static async Task RestartTestsAsync(string directory, Action<bool, string> check)
    {
        string root = Path.Combine(directory, "quota-scheduling-restart");
        var store = new UsageStore(Path.Combine(root, "temporary.db"));
        DateTimeOffset first = Now.AddHours(2);
        DateTimeOffset second = Now.AddDays(3);
        DateTimeOffset[] jitter = Enumerable.Range(0, 32).SelectMany(index => new[]
            { first.AddMilliseconds(index * 30), second.AddMilliseconds(index * 30) }).ToArray();
        var reading = new QuotaReading(QuotaProvider.Claude,
            [Window(20, first), Window(30, second, "weekly", 10080)]);
        await store.SaveQuotaAsync(new(1, reading, Now.AddMinutes(-1), jitter));
        var client = new FakeClient((provider, _) => Task.FromResult(provider == QuotaProvider.Claude
            ? reading : new QuotaReading(provider, [Window(20)])));
        using (var restarted = new AccountQuotaMonitor(client, store, () => Now))
        {
            restarted.Start();
            await MonitorTests.WaitUntilAsync(() => restarted.Snapshot().All(state =>
                state.CheckedAtUtc.HasValue && !state.IsRefreshing), milliseconds: 5000);
            await restarted.StopAsync();
            QuotaCache? compact = await store.ReadQuotaAsync(QuotaProvider.Claude);
            check(compact is not null && compact.ResetAnchors.SequenceEqual(
                [first, first.AddMilliseconds(930), second, second.AddMilliseconds(930)]),
                "a legacy sixty-four-anchor Claude cache is compacted and saved without a schema migration");
            check(compact?.Version == 1 && compact.ResetAnchorPolicyVersion == 0
                && State(restarted, QuotaProvider.Claude).NextCheckUtc == first.AddMinutes(-15),
                "Claude cache identity and its true next reset-band entry survive compaction");
        }
        DateTimeOffset inOldBand = first.AddMinutes(14);
        var advanced = new QuotaReading(QuotaProvider.Claude,
            [Window(20, first.AddHours(5)), Window(30, second, "weekly", 10080)]);
        client.Handler = (provider, _) => Task.FromResult(provider == QuotaProvider.Claude
            ? advanced : new QuotaReading(provider, [Window(20)]));
        using var again = new AccountQuotaMonitor(client, store, () => inOldBand);
        again.Start();
        await MonitorTests.WaitUntilAsync(() => again.Snapshot().All(state =>
            state.CheckedAtUtc.HasValue && !state.IsRefreshing), milliseconds: 5000);
        await again.StopAsync();
        QuotaCache? persisted = await store.ReadQuotaAsync(QuotaProvider.Claude);
        check(persisted is not null && persisted.ResetAnchors.Contains(first)
            && persisted.ResetAnchors.Contains(first.AddMilliseconds(930)),
            "reopening compacted cache preserves both old reset endpoints through their remaining fifteen-minute band");
        check(State(again, QuotaProvider.Claude).NextCheckUtc == first.AddHours(5).AddMinutes(-15),
            "the old band's last minute does not invent a trailing fast check after restart");
    }

    private static QuotaPollingDiagnosticFile Read(QuotaPollingDiagnosticLog log) =>
        JsonSerializer.Deserialize(File.ReadAllText(log.LogPath), QuotaJsonContext.Default.QuotaPollingDiagnosticFile)
        ?? throw new InvalidDataException("Missing synthetic scheduling diagnostics.");

    private static QuotaWindow Window(double used, DateTimeOffset? reset = null,
        string id = "synthetic:5h", int minutes = 300) => new(id, minutes == 10080 ? "주간" : "5시간", used, reset, minutes);

    private static QuotaState State(AccountQuotaMonitor monitor, QuotaProvider provider) =>
        monitor.Snapshot().Single(state => state.Provider == provider);

    private sealed class FakeClient(Func<QuotaProvider, CancellationToken, Task<QuotaReading>> handler) : IAccountQuotaClient
    {
        internal Func<QuotaProvider, CancellationToken, Task<QuotaReading>> Handler { get; set; } = handler;
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) =>
            Handler(provider, cancellationToken);
    }
}
