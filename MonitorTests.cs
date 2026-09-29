using System.Net;
using System.Text;

namespace AiBurgerClock;

// Used only by explicit --self-test / --smoke-test. Never sends network requests.
internal sealed class TestStatusHttpHandler : HttpMessageHandler
{
    public OfficialStatus OpenAiStatus { get; set; } = OfficialStatus.Operational;
    public bool Offline { get; set; }
    public bool FailOpenAi { get; set; }
    public bool MalformedOpenAi { get; set; }
    public string? OpenAiJson { get; set; }
    public bool Block { get; set; }
    public int RequestCount;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Interlocked.Increment(ref RequestCount);
        token.ThrowIfCancellationRequested();
        if (Block) await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        string url = request.RequestUri!.AbsoluteUri;
        bool openai = url.Contains("status.openai.com", StringComparison.Ordinal);
        if (Offline || (openai && FailOpenAi)) throw new HttpRequestException("Synthetic offline");
        string status = OpenAiStatus switch
        {
            OfficialStatus.Degraded => "degraded_performance",
            OfficialStatus.PartialOutage => "partial_outage",
            OfficialStatus.MajorOutage => "major_outage",
            _ => "operational"
        };
        string json = url.EndsWith("products.json", StringComparison.Ordinal)
            ? """{"products":[{"id":"test-gemini","title":"Gemini"}]}"""
            : url.EndsWith("incidents.json", StringComparison.Ordinal) ? "[]"
            : openai && OpenAiJson is not null ? OpenAiJson
            : openai && MalformedOpenAi ? "{bad"
            : System.Text.Json.JsonSerializer.Serialize(new
            {
                components = new[] { new { id = "test-component", name = openai ? "ChatGPT Work" : "claude.ai", status = openai ? status : "operational" } },
                incidents = Array.Empty<object>(),
                status = new { indicator = "none" }
            });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

internal static class MonitorTests
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Monitor/recommendation test: " + message);
        }
        var expected = new[]
        {
            (OfficialStatus.Operational, Recommendation.Go, Recommendation.BurgerTime),
            (OfficialStatus.Degraded, Recommendation.Hold, Recommendation.BurgerServiceIssue),
            (OfficialStatus.PartialOutage, Recommendation.Stop, Recommendation.BurgerServiceIssue),
            (OfficialStatus.MajorOutage, Recommendation.Stop, Recommendation.BurgerServiceIssue),
            (OfficialStatus.Unknown, Recommendation.Check, Recommendation.BurgerCheck),
            (OfficialStatus.Stale, Recommendation.Check, Recommendation.BurgerCheck)
        };
        foreach (var (official, full, burger) in expected)
        {
            Check(RecommendationPolicy.Calculate(AgentState.FullThrottle, official) == full, "FULL + " + official);
            Check(RecommendationPolicy.Calculate(AgentState.BurgerTime, official) == burger, "BURGER + " + official);
        }
        foreach (var from in Enum.GetValues<Recommendation>())
            foreach (var to in Enum.GetValues<Recommendation>())
                Check(RecommendationPolicy.ShouldNotify(from, to) ==
                    (from != to && new[] { Recommendation.Go, Recommendation.Hold, Recommendation.Stop }.Contains(from) &&
                        new[] { Recommendation.Go, Recommendation.Hold, Recommendation.Stop }.Contains(to)), $"Notification {from}->{to}");

        var notifications = new RecommendationNotifications();
        bool Observe(Recommendation value, AgentState schedule = AgentState.FullThrottle, ProviderKind provider = ProviderKind.OpenAI, bool enabled = true)
            => notifications.Observe(provider, schedule, value, enabled);
        Check(!Observe(Recommendation.Check) && !Observe(Recommendation.Go), "No startup alert after initial unknown");
        Check(!Observe(Recommendation.Check) && Observe(Recommendation.Hold), "GO -> UNKNOWN/STALE -> HOLD notifies");
        Check(!Observe(Recommendation.Check) && !Observe(Recommendation.Hold), "Same confirmed HOLD across gap is not repeated");
        Check(!Observe(Recommendation.Check) && Observe(Recommendation.Stop), "HOLD -> gap -> STOP notifies");
        Check(!Observe(Recommendation.Check) && Observe(Recommendation.Go), "STOP -> gap -> GO notifies recovery");
        Check(!Observe(Recommendation.Stop, provider: ProviderKind.Claude), "Providers retain independent baselines");
        Check(!Observe(Recommendation.BurgerTime, AgentState.BurgerTime) && !Observe(Recommendation.Check) && !Observe(Recommendation.Hold), "Schedule transition clears notification baseline");
        Check(!Observe(Recommendation.Go, enabled: false) && !Observe(Recommendation.Go), "Suppressed changes establish baseline without later duplicate");

        var destinations = new[] { "https://status.openai.com/", "https://status.claude.com/", "https://www.google.com/appsstatus/dashboard/" };
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var destination = ProviderStatusPages.For(provider);
            var launch = ProviderStatusPages.StartInfo(destination);
            Check(destination.AbsoluteUri == destinations[(int)provider] && destination.Scheme == "https", "Fixed official status URL: " + provider);
            Check(launch.UseShellExecute && launch.FileName == destination.AbsoluteUri && launch.Arguments.Length == 0, "Uses default browser association without command arguments");
        }

        var now = new DateTimeOffset(2026, 9, 19, 5, 0, 0, TimeSpan.Zero);
        var handler = new TestStatusHttpHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(120) };
        using var monitor = new StatusMonitor(new ProviderStatusClient(http), utcNow: () => now);
        Check(monitor.Snapshot().All(s => s.Status == OfficialStatus.Unknown), "No data starts unknown");
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().All(s => s.Status == OfficialStatus.Operational), "All providers fetched independently");
        Check(handler.RequestCount == 4, "One refresh makes four official feed requests");
        handler.OpenAiStatus = OfficialStatus.PartialOutage;
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI).Status == OfficialStatus.PartialOutage, "OpenAI partial outage");
        Check(monitor.Snapshot().Where(s => s.Provider != ProviderKind.OpenAI).All(s => s.Status == OfficialStatus.Operational), "OpenAI issue does not affect other providers");
        handler.FailOpenAi = true;
        now = now.AddMinutes(5);
        await monitor.RefreshOnceAsync();
        var failed = monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI);
        Check(failed.Status == OfficialStatus.Unknown && failed.LastKnownStatus == OfficialStatus.PartialOutage, "Failure is not falsely green, preserves last known");
        Check(failed.LastSuccessfulCheckUtc == now.AddMinutes(-5), "Failure does not advance successful retrieval");
        now = now.AddMinutes(10);
        Check(monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI).Status == OfficialStatus.Stale, "STALE exactly at 15 minutes");
        handler.FailOpenAi = false;
        handler.OpenAiStatus = OfficialStatus.Operational;
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().All(s => s.Status == OfficialStatus.Operational), "Recovery clears stale");
        handler.OpenAiJson = """{"components":[{"id":"chat","name":"ChatGPT","status":"operational"}],"status":{"indicator":"minor"},"incidents":[{"id":"scope-unknown","name":"Elevated error rates","status":"investigating","impact":"minor"}]}""";
        await monitor.RefreshOnceAsync();
        var ambiguous = monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI);
        Check(ambiguous.Status == OfficialStatus.Unknown && ambiguous.IncidentId == "scope-unknown" && ambiguous.IncidentTitle == "Elevated error rates", "Ambiguous current incident metadata survives monitor");
        Check(ambiguous.LastSuccessfulCheckUtc == now, "Ambiguous response does not advance last successful check");
        now = now.AddMinutes(16);
        Check(monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI) is { Status: OfficialStatus.Stale, IncidentId: "scope-unknown" }, "STALE preserves current unknown incident metadata");
        handler.FailOpenAi = true;
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI).IncidentId.Length == 0, "Transport failure never presents prior incident as freshly observed");
        handler.FailOpenAi = false;
        handler.OpenAiJson = null;
        await monitor.RefreshOnceAsync();
        handler.MalformedOpenAi = true;
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI).Status == OfficialStatus.Unknown, "Malformed response is isolated UNKNOWN");
        handler.Offline = true;
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().All(s => s.Status == OfficialStatus.Unknown), "No Internet leaves monitor functional");
        now = now.AddMinutes(16);
        Check(monitor.Snapshot().All(s => s.Status == OfficialStatus.Stale), "Offline with aged cached data becomes stale");
        handler.Offline = handler.MalformedOpenAi = false;
        handler.Block = true;
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().All(s => s.Status == OfficialStatus.Stale && s.Reason.Contains("시간 초과")), "HTTP timeout preserves stale metadata");
        handler.Block = false;
        await monitor.StopAsync();

        var pollingHandler = new TestStatusHttpHandler();
        using var pollingHttp = new HttpClient(pollingHandler);
        using var polling = new StatusMonitor(new ProviderStatusClient(pollingHttp), pollInterval: TimeSpan.FromMilliseconds(100));
        polling.Start();
        await WaitUntilAsync(() => pollingHandler.RequestCount >= 8);
        Check(polling.NextRefreshUtc.HasValue && !polling.IsRefreshing, "Automatic polling and next refresh");
        int before = pollingHandler.RequestCount;
        polling.RequestRefresh();
        await WaitUntilAsync(() => pollingHandler.RequestCount > before);
        Check(pollingHandler.RequestCount > before, "Manual refresh");
        await polling.StopAsync();
        int stopped = pollingHandler.RequestCount;
        await Task.Delay(150);
        Check(pollingHandler.RequestCount == stopped, "No polling after shutdown");
        polling.Dispose();
        polling.Dispose();
        Check(true, "Monitor Dispose is idempotent");

        var faultyHandler = new TestStatusHttpHandler();
        using var faultyHttp = new HttpClient(faultyHandler);
        using var faulty = new StatusMonitor(new ProviderStatusClient(faultyHttp), pollInterval: TimeSpan.FromMilliseconds(100));
        faulty.Changed += () => throw new InvalidOperationException("Synthetic subscriber failure");
        faulty.Start();
        await WaitUntilAsync(() => faultyHandler.RequestCount >= 8);
        await faulty.StopAsync();
        Check(faultyHandler.RequestCount >= 8, "Subscriber failure does not stop polling or shutdown");

        // No scheduled poll within the test: only the queued request can start a second pass.
        var queuedHandler = new TestStatusHttpHandler { Block = true };
        using var queuedHttp = new HttpClient(queuedHandler) { Timeout = TimeSpan.FromSeconds(1) };
        using var queued = new StatusMonitor(new ProviderStatusClient(queuedHttp), pollInterval: TimeSpan.FromHours(1));
        queued.Start();
        await WaitUntilAsync(() => queuedHandler.RequestCount >= 4);
        queued.RequestRefresh(queueWhileRefreshing: true);
        queuedHandler.Block = false;
        await WaitUntilAsync(() => queuedHandler.RequestCount >= 8);
        await queued.StopAsync();
        Check(queuedHandler.RequestCount >= 8, "Refresh requested during an active poll runs right after it");

        var blockedHandler = new TestStatusHttpHandler { Block = true };
        using var blockedHttp = new HttpClient(blockedHandler);
        using var blockedMonitor = new StatusMonitor(new ProviderStatusClient(blockedHttp));
        blockedMonitor.Start();
        await WaitUntilAsync(() => blockedHandler.RequestCount >= 4);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await blockedMonitor.StopAsync();
        Check(watch.Elapsed < TimeSpan.FromSeconds(2), "Shutdown cancels in-flight HTTP without waiting for timeout");
        return count;
    }

    internal static async Task WaitUntilAsync(Func<bool> predicate, int milliseconds = 5000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.ElapsedMilliseconds >= milliseconds) throw new TimeoutException("Test condition not reached");
            await Task.Delay(25);
        }
    }
}
