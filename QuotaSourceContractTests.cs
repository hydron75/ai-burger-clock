namespace AiBurgerClock;

internal static class QuotaSourceContractTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota source contract: " + label);
        }

        var now = GeminiUsagePageTests.Now;
        const QuotaProvider provider = QuotaProvider.Gemini;
        var reading = GeminiUsagePage.Parse(new(GeminiUsagePage.Url, GeminiUsagePageTests.English), now, TimeZoneInfo.Utc).Reading!;
        var store = new UsageStore(Path.Combine(directory, "quota-source-contract", "test.db"));
        var cache = QuotaCacheAdapter.ForGeminiWeb(store);
        await cache.WriteAsync(reading, now, [], default);
        var source = new GeminiWebQuotaSource(new Reader(), store);

        using var monitor = new AccountQuotaMonitor([(source, QuotaSourceMode.Off)], store, () => now);
        bool rejected = false;
        try { await monitor.ClearCacheAsync(provider); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && await cache.ReadAsync(default) is not null, "a startup off state alone cannot delete a cache");

        monitor.SetMode(provider, QuotaSourceMode.Active);
        rejected = false;
        try { await monitor.ClearCacheAsync(provider); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && await cache.ReadAsync(default) is not null, "an active source cannot delete a cache");

        monitor.SetMode(provider, QuotaSourceMode.Off);
        await monitor.ClearCacheAsync(provider);
        Check(await cache.ReadAsync(default) is null && monitor.Snapshot().Single().Mode == QuotaSourceMode.Off,
            "an explicit off generation clears only its adapter without starting the monitor");

        var retry = new FixedSchedule(TimeSpan.FromMinutes(15), stopOnLoginRequired: false)
            .Next(now, null, new(QuotaFailureKind.LoginRequired, "synthetic.login"), 1, []);
        Check(retry is { Reason: QuotaCheckReason.FailureRetry, SelectedResetUtc: null } &&
            retry.NextCheckUtc == now.AddMinutes(15),
            "a fixed retry without a stop condition keeps its deadline without a reset anchor");

        foreach (var cliProvider in QuotaProviders.All.Where(QuotaNames.HasCliRoute))
        {
            var cliSource = new CliQuotaSource(cliProvider, new Client(now), null);
            using var cli = new AccountQuotaMonitor([(cliSource, QuotaSourceMode.Active)], utcNow: () => now);
            rejected = false;
            try { cli.SetMode(cliProvider, QuotaSourceMode.Off); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && cli.Snapshot().Single().Mode == QuotaSourceMode.Active,
                "the CLI route rejects SetMode without changing its state");
        }

        AccountQuotaMonitor? duringCompletion = null;
        int timestamps = 0, published = 0;
        var codex = new CliQuotaSource(QuotaProvider.Codex, new Client(now), null);
        var switchable = new FixtureSource(codex, codex.Traits with { CanSetMode = true });
        using var completing = duringCompletion = new AccountQuotaMonitor(
            [(switchable, QuotaSourceMode.Active)],
            utcNow: () =>
            {
                // Attempt start, validation, result time, then diagnostic time.
                if (++timestamps == 4) duringCompletion!.SetMode(QuotaProvider.Codex, QuotaSourceMode.Off);
                return now;
            },
            earlyResetDiagnosticLog: new(Path.Combine(directory, "quota-source-contract", "completion-log")));
        completing.Succeeded += _ => published++;
        await completing.RefreshSourceIfDueAsync(QuotaProvider.Codex);
        Check(published == 0 && completing.Snapshot().Single() is { Mode: QuotaSourceMode.Off, Reading: null },
            "switching mode during completion cannot publish a discarded generation");

        var claude = new CliQuotaSource(QuotaProvider.Claude, new Client(now), null);
        var dependency = new FixtureSource(claude, claude.Traits)
        {
            Read = _ => Task.FromResult(QuotaReadResult.Failed(new(QuotaFailureKind.LoginRequired, "synthetic.login")))
        };
        var dependent = new FixtureSource(codex, codex.Traits with { DependsOn = QuotaProvider.Claude, DueCheckBeforeRead = true });
        using var linked = new AccountQuotaMonitor(
            [(dependent, QuotaSourceMode.Active), (dependency, QuotaSourceMode.Active)], utcNow: () => now);
        await linked.RefreshSourceIfDueAsync(QuotaProvider.Claude);
        QuotaState DependentState() => linked.Snapshot().Single(state => state.Provider == QuotaProvider.Codex);
        var initial = DependentState();
        await linked.RefreshSourceIfDueAsync(QuotaProvider.Codex);
        Check(DependentState() == initial, "the configured dependency defers a read without mutating its state");
        dependency.Read = _ => Task.FromResult(QuotaReadResult.Failed(new(QuotaFailureKind.FormatChanged, "synthetic.format")));
        await linked.RefreshSourceIfDueAsync(QuotaProvider.Claude);
        now = now.AddMinutes(5);
        await linked.RefreshSourceIfDueAsync(QuotaProvider.Codex);
        Check(DependentState().Reading is not null, "a dependency failure other than login required does not defer");

        var readingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellable = new FixtureSource(codex, codex.Traits with
        {
            CancelLifetimeAsynchronously = true, StickyManualRefresh = false, Timeout = null, EmitsObservations = false
        })
        {
            Read = async token =>
            {
                using var registration = token.Register(() =>
                {
                    cancellationEntered.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                });
                readingStarted.TrySetResult();
                await release.Task;
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The synthetic read expected cancellation.");
            }
        };
        using var shutdown = new AccountQuotaMonitor([(cancellable, QuotaSourceMode.Active)], utcNow: () => now);
        var read = shutdown.RefreshSourceIfDueAsync(QuotaProvider.Codex);
        var limit = TimeSpan.FromSeconds(5);
        try
        {
            await readingStarted.Task.WaitAsync(limit);
            var dispose = Task.Run(shutdown.Dispose);
            await cancellationEntered.Task.WaitAsync(limit);
            await dispose.WaitAsync(limit);
            Check(!read.IsCompleted, "non-sticky source lifetime cancellation does not block Dispose");
        }
        finally
        {
            release.TrySetResult();
            await read.WaitAsync(limit);
        }
        return count;
    }

    private sealed class FixtureSource(IQuotaSource source, QuotaSourceTraits traits) : IQuotaSource
    {
        public QuotaProvider Provider => source.Provider;
        public QuotaSourceTraits Traits => traits;
        public IQuotaCacheAdapter Cache => source.Cache;
        internal Func<CancellationToken, Task<QuotaReadResult>>? Read { get; set; }
        public Task<QuotaReadResult> ReadAsync(Func<DateTimeOffset> utcNow, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            Read?.Invoke(cancellationToken) ?? source.ReadAsync(utcNow, zone, cancellationToken);
    }

    private sealed class Reader : IGeminiUsagePageReader
    {
        public Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No web read is expected during mode/cache setup.");
    }

    private sealed class Client(DateTimeOffset now) : IAccountQuotaClient
    {
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult(new QuotaReading(provider, [new("test-window", "합성", 15, now.AddHours(2), 300)]));
    }
}
