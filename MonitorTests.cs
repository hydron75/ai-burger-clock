using System.Net;
using System.Net.NetworkInformation;
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
    public string? OpenAiComponentsJson { get; set; }
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
        var defaultComponents = new[]
        {
            new { id = "test-component", name = openai ? "ChatGPT Work" : "claude.ai", status = openai ? status : "operational" }
        };
        string json = url.EndsWith("products.json", StringComparison.Ordinal)
            ? """{"products":[{"id":"test-gemini","title":"Gemini"}]}"""
            : url == ProviderStatusClient.OpenAiComponentsUrl ? OpenAiComponentsJson ?? System.Text.Json.JsonSerializer.Serialize(new { components = defaultComponents })
            : url == ProviderStatusClient.OpenAiIncidentsUrl ? """{"incidents":[]}"""
            : url == ProviderStatusClient.GoogleIncidentsUrl ? "[]"
            : openai && OpenAiJson is not null ? OpenAiJson
            : openai && MalformedOpenAi ? "{bad"
            : System.Text.Json.JsonSerializer.Serialize(new
            {
                components = defaultComponents,
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
        var oldOutage = StatusMonitor.WithFreshness(new ProviderStatus(ProviderKind.OpenAI, OfficialStatus.PartialOutage,
            now.AddMinutes(-15), now.AddMinutes(-15), "Synthetic past incident", "CLI", "synthetic-old", "Synthetic past incident"), now);
        Check(oldOutage.Status == OfficialStatus.Stale && oldOutage.IncidentId.Length == 0 && oldOutage.IncidentTitle.Length == 0 &&
            !oldOutage.Reason.Contains("Synthetic past incident", StringComparison.Ordinal),
            "Aging cached outage no longer asserts a current incident on the card");
        Check(oldOutage.LastKnownStatus == OfficialStatus.PartialOutage && oldOutage.LastKnownStatusUtc == now.AddMinutes(-15),
            "Aging cached outage preserves the historical assessed status and time");
        // Synthetic legacy caches have no auxiliary timestamp. Only a current, assessable
        // cache row itself proves that its successful receipt was also an assessed status.
        var legacyOperational = new ProviderStatus(ProviderKind.OpenAI, OfficialStatus.Operational,
            now.AddMinutes(-5), now.AddMinutes(-5), "Synthetic healthy cache", LastKnownStatus: OfficialStatus.Operational);
        var freshLegacy = StatusMonitor.WithFreshness(legacyOperational, now);
        Check(freshLegacy.Status == OfficialStatus.Operational && freshLegacy.LastKnownStatus == OfficialStatus.Operational &&
            freshLegacy.LastKnownStatusUtc == now.AddMinutes(-5),
            "recent assessed legacy cache supplies its evidenced last-known status timestamp");
        var legacyOutage = legacyOperational with
            { Status = OfficialStatus.PartialOutage, LastKnownStatus = OfficialStatus.PartialOutage };
        Check(StatusMonitor.WithFreshness(legacyOutage, now).LastKnownStatusUtc == now.AddMinutes(-5),
            "recent legacy outage supplies its own assessed timestamp before a later failed or ambiguous receipt");
        var legacyUnknown = legacyOperational with { Status = OfficialStatus.Unknown };
        Check(StatusMonitor.WithFreshness(legacyUnknown, now).LastKnownStatusUtc is null,
            "legacy UNKNOWN receipt does not prove when its historical assessed status was confirmed");
        var legacyStale = legacyOutage with
        {
            Status = OfficialStatus.Stale,
            CheckedAtUtc = now.AddMinutes(-20),
            LastSuccessfulCheckUtc = now.AddMinutes(-20)
        };
        Check(StatusMonitor.WithFreshness(legacyStale, now).LastKnownStatusUtc is null,
            "legacy STALE receipt never invents a timestamp for its historical assessed state");
        var olderConfirmed = legacyOperational with { LastKnownStatusUtc = now.AddMinutes(-10) };
        Check(StatusMonitor.WithFreshness(olderConfirmed, now).LastKnownStatusUtc == now.AddMinutes(-10),
            "an existing historical status timestamp is not overwritten by cache receipt time");
        var conflictingKnown = legacyOperational with { LastKnownStatus = OfficialStatus.PartialOutage };
        Check(StatusMonitor.WithFreshness(conflictingKnown, now).LastKnownStatusUtc is null,
            "a receipt cannot date a different historical status when no confirmation timestamp exists");
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().All(s => s.Status == OfficialStatus.Operational), "All providers fetched independently");
        Check(handler.RequestCount == 5, "One refresh makes five official feed requests including the complete OpenAI component catalog");
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
        Check(failed.LastKnownStatusUtc == now.AddMinutes(-5) && failed.AssessmentIssue.Length == 0,
            "transport failure preserves the last assessable status time and is not an assessment issue");
        now = now.AddMinutes(10);
        Check(monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI).Status == OfficialStatus.Stale, "STALE exactly at 15 minutes");
        handler.FailOpenAi = false;
        handler.OpenAiStatus = OfficialStatus.Operational;
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().All(s => s.Status == OfficialStatus.Operational), "Recovery clears stale");
        var lastConfirmed = now;
        now = now.AddMinutes(5);
        handler.OpenAiJson = """{"components":[{"id":"chat","name":"ChatGPT","status":"operational"}],"status":{"indicator":"none"},"incidents":[{"id":"scope-unknown","name":"Delayed administrative metric processing","status":"monitoring","impact":"minor"}]}""";
        await monitor.RefreshOnceAsync();
        var ambiguous = monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI);
        Check(ambiguous.Status == OfficialStatus.Unknown && ambiguous.UncertainIncidentId == "scope-unknown" &&
            ambiguous.UncertainIncidentTitle == "Delayed administrative metric processing" && ambiguous.IncidentId.Length == 0 && ambiguous.IncidentTitle.Length == 0,
            "ambiguous current incident is separate from confirmed outage metadata in the monitor");
        Check(ambiguous.LastSuccessfulCheckUtc == now, "a complete ambiguous response advances the successful receipt time");
        Check(ambiguous.AssessmentIssue.Length > 0 && RecommendationPolicy.Calculate(AgentState.FullThrottle, ambiguous.Status) == Recommendation.Check,
            "receipt success with an uncertain incident has an assessment explanation and CHECK, not GO or STOP");
        Check(ambiguous.LastKnownStatus == OfficialStatus.Operational && ambiguous.LastKnownStatusUtc == lastConfirmed,
            "an ambiguous response preserves the last assessable status and its separate timestamp");
        foreach (int elapsedMinutes in new[] { 5, 5, 6 })
        {
            now = now.AddMinutes(elapsedMinutes);
            await monitor.RefreshOnceAsync();
            var received = monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI);
            Check(received.Status == OfficialStatus.Unknown && received.LastSuccessfulCheckUtc == now &&
                received.LastKnownStatusUtc == lastConfirmed && received.UncertainIncidentId == "scope-unknown",
                "repeated successful unknown-scope responses remain fresh CHECK beyond fifteen minutes");
        }
        handler.FailOpenAi = true;
        now = now.AddMinutes(1);
        await monitor.RefreshOnceAsync();
        var transportFailure = monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI);
        Check(transportFailure.IncidentId.Length == 0 && transportFailure.UncertainIncidentId.Length == 0 &&
            transportFailure.UncertainIncidentTitle.Length == 0, "transport failure never presents prior confirmed or uncertain incidents as freshly observed");
        Check(transportFailure.LastSuccessfulCheckUtc == now.AddMinutes(-1) && transportFailure.AssessmentIssue.Length == 0 &&
            transportFailure.LastKnownStatus == OfficialStatus.Operational && transportFailure.LastKnownStatusUtc == lastConfirmed,
            "transport failure after successful CHECK does not advance receipt or lose the assessable baseline");
        handler.FailOpenAi = false;
        handler.OpenAiJson = null;
        await monitor.RefreshOnceAsync();
        handler.OpenAiComponentsJson = "{malformed-catalog";
        now = now.AddMinutes(16);
        await monitor.RefreshOnceAsync();
        var invalidCatalog = monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI);
        Check(invalidCatalog.Status == OfficialStatus.Unknown && invalidCatalog.LastSuccessfulCheckUtc == now && invalidCatalog.AssessmentIssue.Length > 0,
            "a malformed full catalog is a fresh receipt but cannot certify healthy components");
        handler.OpenAiComponentsJson = null;
        handler.MalformedOpenAi = true;
        now = now.AddMinutes(16);
        await monitor.RefreshOnceAsync();
        Check(monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI).Status == OfficialStatus.Unknown, "Malformed response is isolated UNKNOWN");
        var malformed = monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI);
        Check(malformed.LastSuccessfulCheckUtc == now && malformed.AssessmentIssue.Length > 0,
            "malformed JSON does not masquerade as a failed HTTP receipt or STALE");
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
        await WaitUntilAsync(() => pollingHandler.RequestCount >= 10);
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
        await WaitUntilAsync(() => faultyHandler.RequestCount >= 10);
        await faulty.StopAsync();
        Check(faultyHandler.RequestCount >= 10, "Subscriber failure does not stop polling or shutdown");

        // No scheduled poll within the test: only the queued request can start a second pass.
        var queuedHandler = new TestStatusHttpHandler { Block = true };
        using var queuedHttp = new HttpClient(queuedHandler) { Timeout = TimeSpan.FromSeconds(1) };
        using var queued = new StatusMonitor(new ProviderStatusClient(queuedHttp), pollInterval: TimeSpan.FromHours(1));
        queued.Start();
        await WaitUntilAsync(() => queuedHandler.RequestCount >= 4);
        queued.RequestRefresh(queueWhileRefreshing: true);
        queuedHandler.Block = false;
        await WaitUntilAsync(() => queuedHandler.RequestCount >= 10);
        await queued.StopAsync();
        Check(queuedHandler.RequestCount >= 10, "Refresh requested during an active poll runs right after it");

        var blockedHandler = new TestStatusHttpHandler { Block = true };
        using var blockedHttp = new HttpClient(blockedHandler);
        using var blockedMonitor = new StatusMonitor(new ProviderStatusClient(blockedHttp));
        blockedMonitor.Start();
        await WaitUntilAsync(() => blockedHandler.RequestCount >= 4);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await blockedMonitor.StopAsync();
        Check(watch.Elapsed < TimeSpan.FromSeconds(2), "Shutdown cancels in-flight HTTP without waiting for timeout");

        // Shared HTTP client settings used by both hosts.
        using (var sharedHandler = ProviderStatusClient.CreateHttpHandler())
        {
            Check(!sharedHandler.UseCookies && sharedHandler.PooledConnectionLifetime == TimeSpan.FromMinutes(10) &&
                sharedHandler.ConnectTimeout == TimeSpan.FromSeconds(5) &&
                sharedHandler.AutomaticDecompression == (DecompressionMethods.GZip | DecompressionMethods.Deflate),
                "Shared HTTP handler: no cookies, 10-minute pool lifetime, 5-second connect timeout, gzip/deflate");
        }
        using (var client = ProviderStatusClient.CreateHttpClient())
            Check(client.Timeout == StatusMonitor.RequestTimeout, "Shared HTTP client uses the status request timeout");

        // Shared notification wording used by both hosts.
        var (fullTitle, fullBody) = TrayPresentation.TransitionNotification(
            AgentSchedule.GetSnapshot(DateTimeOffset.Parse("2026-10-03T06:00:00Z"), true));
        Check(fullTitle == "FULL THROTTLE 시작" && fullBody.StartsWith("미국 업무시간 밖입니다. 다음 전환: ", StringComparison.Ordinal) &&
            fullBody.EndsWith("KST. Provider별 공식 상태와 작업 권고도 확인하세요.", StringComparison.Ordinal), "FULL transition wording");
        var (_, holidayBody) = TrayPresentation.TransitionNotification(
            AgentSchedule.GetSnapshot(DateTimeOffset.Parse("2026-09-07T15:00:00Z"), true));
        Check(holidayBody.StartsWith("미국 공휴일이 포함된 연장 FULL 구간입니다. ", StringComparison.Ordinal), "Holiday-extended FULL wording");
        var (burgerTitle, burgerBody) = TrayPresentation.TransitionNotification(
            AgentSchedule.GetSnapshot(DateTimeOffset.Parse("2026-10-05T15:00:00Z"), true));
        Check(burgerTitle == "BURGER TIME 시작" && burgerBody == "새로운 대형 Agent 작업은 다음 FULL THROTTLE까지 미뤄두세요.",
            "BURGER transition wording");
        Check(TrayPresentation.ProviderNotification(ProviderKind.Claude, Recommendation.Go, "ignored") ==
            ("Claude 정상화", "관련 서비스가 정상화되었습니다. 현재 FULL THROTTLE이므로 대규모 작업 재개 가능."), "Provider recovery wording");
        var (stopTitle, stopBody) = TrayPresentation.ProviderNotification(ProviderKind.OpenAI, Recommendation.Stop, "API 장애");
        Check(stopTitle == "OpenAI 작업 권고 변경" && stopBody.StartsWith("현재 FULL THROTTLE이지만 공식 서비스 문제가 있습니다. ", StringComparison.Ordinal) &&
            stopBody.EndsWith("\nAPI 장애", StringComparison.Ordinal), "Provider incident wording keeps the reason");
        Check(TrayPresentation.ProviderNotification(ProviderKind.OpenAI, Recommendation.Go, "",
                provider => provider == ProviderKind.OpenAI ? "ChatGPT" : provider.ToString()).Title == "ChatGPT 정상화",
            "Provider notification title uses a host display name");

        // Network-recovery refresh: settle delay, debounce, one refresh per minute, nothing after dispose.
        long clock = 1_000;
        var refreshedAt = new List<long>();
        Action? duringWait = null;
        var network = new NetworkRefreshScheduler(() => refreshedAt.Add(clock), () => clock, (span, _) =>
        {
            var hook = duringWait;
            duringWait = null;
            hook?.Invoke();
            clock += (long)span.TotalMilliseconds;
            return Task.CompletedTask;
        }, () => true); // Timing checks must not depend on the build machine's real network.
        long settle = (long)NetworkRefreshScheduler.SettleDelay.TotalMilliseconds;
        long minimum = (long)NetworkRefreshScheduler.MinimumInterval.TotalMilliseconds;
        network.OnNetworkAvailable();
        Check(refreshedAt.SequenceEqual([1_000 + settle]), "First network change refreshes after the settle delay");
        clock = refreshedAt[0] + 20_000;
        network.OnNetworkAvailable();
        Check(refreshedAt.Count == 2 && refreshedAt[1] == refreshedAt[0] + minimum,
            "A change within a minute is deferred to the one-minute limit, not dropped");
        clock = refreshedAt[1] + minimum + 10_000;
        long secondChange = clock + 3_000;
        duringWait = () => { clock = secondChange; network.OnNetworkAvailable(); };
        network.OnNetworkAvailable();
        Check(refreshedAt.Count == 3 && refreshedAt[2] == secondChange + settle,
            "Changes during the wait coalesce into one refresh after the last change settles");
        clock += minimum * 2;
        duringWait = network.Dispose;
        network.OnNetworkAvailable();
        network.OnNetworkAvailable();
        Check(refreshedAt.Count == 3, "Dispose cancels a pending refresh and ignores later changes");

        // No network when the wait ends: skip without using the one-minute slot.
        clock = 1_000;
        refreshedAt.Clear();
        bool online = true;
        var offlineAware = new NetworkRefreshScheduler(() => refreshedAt.Add(clock), () => clock, (span, _) =>
        {
            clock += (long)span.TotalMilliseconds;
            return Task.CompletedTask;
        }, () => online);
        offlineAware.OnNetworkAvailable();
        long firstRefresh = refreshedAt.Single();
        clock = firstRefresh + 70_000;
        online = false;
        offlineAware.OnNetworkAvailable();
        Check(refreshedAt.Count == 1, "No refresh when every network is down after the settle delay");
        online = true;
        clock += 3_000;
        long reconnectAt = clock;
        offlineAware.OnNetworkAvailable();
        Check(refreshedAt.Count == 2 && refreshedAt[1] == reconnectAt + settle,
            "A skipped offline refresh does not take the one-minute slot; reconnect refreshes after the settle delay");
        clock = refreshedAt[1] + 10_000;
        online = false;
        offlineAware.OnNetworkAvailable();
        Check(refreshedAt.Count == 2, "An offline change within a minute of a refresh is skipped when its slot arrives");
        online = true;
        clock += 1_000;
        reconnectAt = clock;
        offlineAware.OnNetworkAvailable();
        Check(refreshedAt.Count == 3 && refreshedAt[2] == reconnectAt + settle,
            "Reconnect after that skip refreshes after the settle delay, not another minute later");

        // Usable network: up, not loopback/tunnel, and a non-link-local address.
        static (OperationalStatus, NetworkInterfaceType, IEnumerable<IPAddress>) Nic(
            bool up, NetworkInterfaceType type, params string[] addresses) =>
            (up ? OperationalStatus.Up : OperationalStatus.Down,
             type, addresses.Select(IPAddress.Parse));
        var ethernet = NetworkInterfaceType.Ethernet;
        var unknown = NetworkInterfaceType.Unknown;
        Check(!NetworkRefreshScheduler.HasUsableNetwork([
                Nic(true, NetworkInterfaceType.Loopback, "127.0.0.1", "::1"),
                Nic(true, unknown, "fe80::7575:796:dc16:f688"), Nic(true, unknown, "fe80::17c9:20d7:a1c3:ea37"),
                Nic(false, ethernet, "192.168.50.161")]),
            "macOS utun links with only link-local addresses do not count as a network (observed)");
        Check(!NetworkRefreshScheduler.HasUsableNetwork([Nic(true, ethernet, "169.254.10.20", "fe80::1")]),
            "Self-assigned IPv4 and link-local IPv6 do not count");
        Check(NetworkRefreshScheduler.HasUsableNetwork([Nic(true, unknown, "fe80::1"), Nic(true, ethernet, "fe80::2", "192.168.50.161")]),
            "An up interface with a LAN IPv4 address counts");
        Check(NetworkRefreshScheduler.HasUsableNetwork([Nic(true, NetworkInterfaceType.Wireless80211, "2001:db8::5")]),
            "An up interface with a global IPv6 address counts");
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
