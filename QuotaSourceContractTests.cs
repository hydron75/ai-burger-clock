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

        AccountQuotaMonitor? duringCompletion = null;
        int timestamps = 0, published = 0;
        using var cli = duringCompletion = new AccountQuotaMonitor(
            [(new CliQuotaSource(QuotaProvider.Codex, new Client(now), null), QuotaSourceMode.Active)],
            utcNow: () =>
            {
                // Attempt start, validation, result time, then diagnostic time.
                if (++timestamps == 4) duringCompletion!.SetMode(QuotaProvider.Codex, QuotaSourceMode.Off);
                return now;
            },
            earlyResetDiagnosticLog: new(Path.Combine(directory, "quota-source-contract", "completion-log")));
        cli.Succeeded += _ => published++;
        await cli.RefreshSourceIfDueAsync(QuotaProvider.Codex);
        Check(published == 0 && cli.Snapshot().Single() is { Mode: QuotaSourceMode.Off, Reading: null },
            "switching mode during completion cannot publish a discarded generation");
        return count;
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
