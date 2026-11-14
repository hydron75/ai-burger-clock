using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

// Synthetic public-feed fixtures only. No account, browser, or network access.
internal static class XaiStatusTests
{
    internal const string EmptyRss = "<rss version=\"2.0\"><channel><title>xAI Status</title></channel></rss>";
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 1, 0, 0, TimeSpan.Zero);
    private static readonly (string Id, string Name)[] Catalog =
    [
        ("agent-sdk", "Agent SDK"), ("android-app", "Grok Android"), ("api-global", "Global API"), ("api-us", "US API"),
        ("automations", "Automations"), ("cloud-agents", "Cloud Agents"), ("cursor-cli", "CLI"), ("cursor-com", "cursor.com"),
        ("cursor-desktop", "Desktop"), ("cursor-ios", "Cursor iOS"), ("enterprise-dashboard", "Enterprise Dashboard"),
        ("grok-bot", "Grok Bot"), ("grok-com", "grok.com"), ("grok-in-x", "Grok X"), ("ios-app", "Grok iOS"),
        ("origin", "Origin"), ("review-agents", "Review Agents"), ("third-party-models", "Third-party models"), ("voice", "Voice")
    ];
    internal static string SummaryJson => Summary();
    internal const string IncidentsJson = "{\"incidents\":[]}";

    private static object[] ComponentItems(string? omit = null, string? changed = null, string value = "available") =>
        Catalog.Where(item => item.Id != omit).Select(item => (object)new
            { id = item.Id, name = item.Name, status = item.Id == changed ? value : "available" }).ToArray();
    private static string Summary(string? omit = null, string? changed = null, string value = "available") =>
        JsonSerializer.Serialize(new { status = new { indicator = "none", description = "Synthetic overall status" }, components = ComponentItems(omit, changed, value) });
    private static string History(params object[] incidents) => JsonSerializer.Serialize(new { incidents });
    private static object Incident(string id = "synthetic-incident", string state = "investigating", string impact = "major", params string[] components) =>
        new { id, name = "Synthetic issue", status = state, impact, created_at = "2026-01-01T00:00:00Z",
            updated_at = "2026-01-01T00:00:00Z", resolved_at = state == "resolved" ? "2026-01-01T00:00:00Z" : null,
            incident_updates = System.Array.Empty<object>(), components = components.Select(slug => new { slug, name = "Synthetic component" }).ToArray() };
    internal static string Rss(params XElement[] items) => new XElement("rss", new XAttribute("version", "2.0"),
        new XElement("channel", new XElement("title", "xAI Status"), items)).ToString();
    internal static XElement Item(string service = "grok-com", string id = "synthetic-incident", string state = "investigating", string severity = "disruption") =>
        new("item", new XElement("title", "[Synthetic product] Investigating synthetic issue"),
            new XElement("link", "https://status.x.ai/" + service + "/" + id), new XElement("guid", new XAttribute("isPermaLink", "false"), id),
            new XElement("description", new XCData($"<p>Status: {state.ToUpperInvariant()}</p><p>Severity: {severity}</p><p>Updates: Investigating old text; contains &amp; HTML.</p>")),
            new XElement("pubDate", "Fri, 01 Jan 2010 00:00:00 GMT"), new XElement("category", severity), new XElement("category", state));

    internal static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string label)
        { count++; if (!value) throw new InvalidOperationException("xAI status: " + label); }
        Check(new XaiStatusFeed().Parse(EmptyRss, SummaryJson, IncidentsJson, Now).Status == OfficialStatus.Operational,
            "native data.x.ai available summary is healthy with the complete required slug catalog");
        ProviderStatus Parse(string rss = EmptyRss, string? summary = null, string? history = null) =>
            new XaiStatusFeed().Parse(rss, summary ?? SummaryJson, history ?? IncidentsJson, Now);

        var healthy = Parse();
        Check(healthy.Status == OfficialStatus.Operational && healthy.Reason == XaiStatusFeed.HealthyReason,
            "empty valid RSS plus operational Cursor catalog is healthy under the approved incident-feed rule");
        Check(RecommendationPolicy.Calculate(AgentState.FullThrottle, healthy.Status) == Recommendation.Go,
            "incident-free xAI is GO, not permanently gray");
        Check(healthy.LastSuccessfulCheckUtc == Now && healthy.AssessmentIssue.Length == 0, "normal receipt uses the injected clock");
        var november05Catalog = JsonNode.Parse(SummaryJson)!;
        var november05Component = november05Catalog["components"]!.AsArray().Single(item => (string?)item!["id"] == "review-agents")!;
        november05Component["id"] = "bugbot"; november05Component["name"] = "Bugbot";
        var november05Summary = JsonNode.Parse(SummaryJson)!;
        november05Summary["components"] = november05Catalog["components"]!.DeepClone();
        var november05 = Parse(summary: november05Summary.ToJsonString());
        Check(november05.Status == OfficialStatus.Unknown && november05.AssessmentIssue.Contains("필수 구성 요소 누락", StringComparison.Ordinal),
            "November 05 Bugbot catalog cannot replace the required Review Agents component");
        var legacyCatalog = november05Catalog.DeepClone();
        var legacyComponent = legacyCatalog["components"]!.AsArray().Single(item => (string?)item!["id"] == "third-party-models")!;
        legacyComponent["id"] = "review-agents"; legacyComponent["name"] = "Review Agents";
        var legacySummary = JsonNode.Parse(SummaryJson)!;
        legacySummary["components"] = legacyCatalog["components"]!.DeepClone();
        var legacy = Parse(summary: legacySummary.ToJsonString());
        Check(legacy.Status == OfficialStatus.Unknown && legacy.AssessmentIssue.Contains("필수 구성 요소 누락", StringComparison.Ordinal),
            "legacy Review Agents catalog cannot replace the required Third-party models component");
        var thirdPartyIncident = Parse(history: JsonSerializer.Serialize(new { incidents = new[] { Incident(components: ["third-party-models"]) } }));
        Check(thirdPartyIncident.Status == OfficialStatus.PartialOutage && thirdPartyIncident.RelevantComponent == "Cursor · Third-party models",
            "Third-party models JSON incident contributes to Cursor work status");
        var reviewAgentsIncident = Parse(history: JsonSerializer.Serialize(new { incidents = new[] { Incident(components: ["review-agents"]) } }));
        Check(reviewAgentsIncident.Status == OfficialStatus.PartialOutage && reviewAgentsIncident.RelevantComponent == "Cursor · Review Agents",
            "Review Agents JSON incident contributes to Cursor work status with the public display name");
        Check(Enum.GetValues<QuotaProvider>().SequenceEqual([QuotaProvider.Codex, QuotaProvider.Claude, QuotaProvider.Gemini, QuotaProvider.Xai])
            && ProviderNames.Provider(ProviderKind.Xai) == "xAI" && ProviderNames.Quota(QuotaProvider.Xai) == "xAI",
            "xAI weekly quota is appended without changing the existing official status provider");
        Check(ProviderStatusPages.For(ProviderKind.Xai).AbsoluteUri == "https://status.x.ai/",
            "xAI retains the fixed official status destination");

        (string Path, string Label)[] mappings =
        [
            ("grok-com", "Grok · grok.com"), ("ios-app", "Grok · iOS"), ("android-app", "Grok · Android"),
            ("grok-in-x", "Grok · X"), ("grok-build", "Grok Build"), ("voice", "Grok · Voice"),
            ("office-workspace-plugins", "Grok · Office/Workspace Plugins"), ("api-global", "Grok API · Global"), ("api-us", "Grok API · US"),
            ("cursor-desktop", "Cursor · Desktop"), ("cursor-ios", "Cursor · iOS"), ("cursor-cli", "Cursor · CLI"),
            ("cloud-agents", "Cursor · Cloud Agents"), ("automations", "Cursor · Automations"), ("bugbot", "Cursor · Bugbot"),
            ("third-party-models", "Cursor · Third-party models"),
            ("review-agents", "Cursor · Review Agents"), ("agent-sdk", "Cursor · Agent SDK"), ("cursor-com", "Cursor · cursor.com"), ("origin", "Cursor · Origin"),
            ("enterprise-dashboard", "Cursor · Enterprise Dashboard"), ("grok-bot", "Grok Bot")
        ];
        foreach (var (path, label) in mappings)
        {
            var status = Parse(Rss(Item(path)));
            Check(status.Status == OfficialStatus.PartialOutage && status.RelevantComponent == label, "RSS product mapping: " + path);
            Check(status.IncidentId == "synthetic-incident", "public incident ID preserved: " + path);
        }
        foreach (string state in new[] { "investigating", "identified", "monitoring", "ongoing", "resolved" })
        foreach (string severity in new[] { "info", "disruption", "outage" })
        {
            var ignored = Parse(Rss(Item("imagine", state: state, severity: severity)));
            Check(ignored.Status == OfficialStatus.Operational && ignored.AssessmentIssue.Length == 0 &&
                ignored.IncidentId.Length == 0 && ignored.PendingXaiIncidentIds.Length == 0 && ignored.XaiMissingIncidents.Count == 0,
                "Imagine is outside work status and tracking: " + state + "/" + severity);
        }
        foreach (bool imagineFirst in new[] { false, true })
        foreach (string state in new[] { "investigating", "resolved" })
        {
            var items = new[] { Item("grok-bot"), Item("imagine", state: state, severity: "outage") };
            if (imagineFirst) Array.Reverse(items);
            var mixed = Parse(Rss(items));
            Check(mixed.Status == OfficialStatus.PartialOutage && mixed.RelevantComponent == "Grok Bot" && mixed.AssessmentIssue.Length == 0,
                "ignored Imagine observation neither escalates nor resolves another product sharing the ID");
        }
        var ignoredTracker = new XaiStatusFeed();
        _ = ignoredTracker.Parse(Rss(Item("grok-bot")), SummaryJson, IncidentsJson, Now);
        var ignoredOnly = ignoredTracker.Parse(Rss(Item("imagine")), SummaryJson, IncidentsJson, Now.AddHours(1));
        Check(ignoredOnly.Status == OfficialStatus.Unknown && ignoredOnly.XaiMissingIncidents.Count == 1,
            "Imagine alone does not hide disappearance of a previously affected work product");
        var ignoredRestart = new XaiStatusFeed(); ignoredRestart.Restore(ignoredOnly);
        var ignoredExpired = ignoredRestart.Parse(Rss(Item("imagine")), SummaryJson, IncidentsJson, Now.AddHours(25));
        Check(ignoredExpired.Status == OfficialStatus.Operational && ignoredExpired.XaiRecoveryUnconfirmed && ignoredExpired.PendingXaiIncidentIds.Length == 0,
            "Imagine does not pin a work incident ID past its persisted 24-hour disappearance period");
        var unsafeImagine = Item("imagine");
        unsafeImagine.Element("link")!.Value = "https://untrusted.example/imagine/synthetic-incident";
        Check(Parse(Rss(unsafeImagine)).Status == OfficialStatus.Unknown, "Imagine exclusion never bypasses the trusted-link boundary");
        foreach (var item in Catalog)
        {
            var status = Parse(summary: Summary(changed: item.Id, value: "info"));
            Check(status.Status == OfficialStatus.Degraded && status.RelevantComponent.Length > 0, "each Cursor/Bot component contributes: " + item.Name);
        }
        foreach (var (severity, expected) in new[] { ("info", OfficialStatus.Degraded), ("disruption", OfficialStatus.PartialOutage), ("outage", OfficialStatus.MajorOutage) })
            Check(Parse(Rss(Item(severity: severity))).Status == expected, "RSS severity mapping: " + severity);
        foreach (string phase in new[] { "investigating", "identified", "monitoring", "ongoing" })
            Check(Parse(Rss(Item(state: phase))).Status == OfficialStatus.PartialOutage, "recognized open RSS phase: " + phase);
        foreach (string path in mappings.Select(item => item.Path).Append("future-product"))
            Check(Parse(Rss(Item(path, state: "resolved", severity: "outage"))).Status == OfficialStatus.Operational,
                "resolved old Investigating title/body never asserts a current outage: " + path);
        var distantDates = Parse(Rss(Item(state: "resolved")));
        Check(distantDates.CheckedAtUtc == Now && distantDates.Status == OfficialStatus.Operational, "old publication dates are not transport staleness");

        var duplicate = Parse(Rss(Item("grok-bot"), Item("cursor-cli"), Item("cursor-cli")),
            history: History(Incident(components: ["grok-bot", "cursor-cli"])));
        Check(duplicate.IncidentId == "synthetic-incident" && duplicate.IncidentTitle == "[Synthetic product] Investigating synthetic issue | Synthetic issue",
            "same GUID in repeated products and JSON is deduplicated by episode without discarding titles");
        Check(duplicate.RelevantComponent.Contains("Grok Bot", StringComparison.Ordinal) && duplicate.RelevantComponent.Contains("Cursor · CLI", StringComparison.Ordinal),
            "duplicate episode retains all affected products");
        var worst = Parse(Rss(Item("grok-build", severity: "outage"), Item("grok-bot", id: "synthetic-second")),
            summary: Summary(changed: "cursor-cli", value: "info"));
        Check(worst.Status == OfficialStatus.MajorOutage && worst.RelevantComponent.Contains("Grok Build", StringComparison.Ordinal) &&
            worst.RelevantComponent.Contains("Grok Bot", StringComparison.Ordinal) && worst.RelevantComponent.Contains("Cursor · CLI", StringComparison.Ordinal), "worst status with all product causes");
        var titleOnly = Parse(history: History([Incident(components: [])]));
        Check(titleOnly.Status == OfficialStatus.Unknown && titleOnly.IncidentId.Length == 0 && titleOnly.UncertainIncidentId.Length > 0,
            "unscoped ongoing event is CHECK, not a fabricated all-product outage");
        var partlyScoped = Parse(history: History([Incident(components: ["grok-bot", "future-component"])]));
        Check(partlyScoped.Status == OfficialStatus.PartialOutage && partlyScoped.AssessmentIssue.Length > 0, "known affected product wins alongside uncertain scope");
        var resolvedJson = Parse(history: JsonSerializer.Serialize(new { incidents = new[] { Incident(state: "resolved", impact: "critical") } }));
        Check(resolvedJson.Status == OfficialStatus.Operational, "resolved JSON event without a scope is historical only");
        var conflicting = Parse(Rss(Item()), history: JsonSerializer.Serialize(new { incidents = new[] { Incident(state: "resolved", components: ["grok-com"]) } }));
        Check(conflicting.Status == OfficialStatus.PartialOutage && conflicting.AssessmentIssue.Contains("상충", StringComparison.Ordinal),
            "resolved observation cannot erase a still-open observation from another source");
        var oppositeConflict = Parse(Rss(Item(state: "resolved")), history: History([Incident(components: ["cursor-cli"])]));
        Check(oppositeConflict.Status == OfficialStatus.PartialOutage && oppositeConflict.AssessmentIssue.Length > 0, "cross-source conflict is symmetric");
        Check(Parse(summary: Summary(changed: "cursor-cli", value: "outage")).Status == OfficialStatus.MajorOutage,
            "healthy complete catalog cannot erase a summary component outage");
        Check(Parse(summary: Summary(changed: "cursor-cli", value: "outage")).Status == OfficialStatus.MajorOutage,
            "healthy summary cannot erase a complete-catalog component outage");

        foreach (string id in Catalog.Select(item => item.Id))
        {
            Check(Parse(summary: Summary(omit: id)).Status == OfficialStatus.Unknown, "missing expected component is CHECK: " + id);
            Check(Parse(summary: Summary(omit: id), history: History(Incident(state: "resolved", components: [id]))).Status == OfficialStatus.Unknown, "history cannot replace a missing current catalog entry: " + id);
        }
        foreach (string bad in new[] { "", "<html>Forbidden</html>", "<rss>", "<rss version=\"2.0\"/>",
            "<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///must-not-read'>]><rss version=\"2.0\"><channel><title>&x;</title></channel></rss>" })
            Check(Parse(bad).Status == OfficialStatus.Unknown, "malformed/DTD feed cannot produce GO");
        foreach (var bad in new[] { Item("future-product"), Item(state: "future-phase"), Item(severity: "future-impact") })
            Check(Parse(Rss(bad)).Status == OfficialStatus.Unknown, "unknown ongoing scope/state/severity is CHECK");
        foreach (string link in new[] { "https://untrusted.example/grok-com/synthetic-incident", "file:///grok-com/synthetic-incident", "https://status.x.ai/grok-com/synthetic-incident?redirect=bad" })
        {
            var item = Item(); item.Element("link")!.Value = link;
            Check(Parse(Rss(item)).Status == OfficialStatus.Unknown, "untrusted or changed RSS product link is not executed or inferred");
        }
        foreach (string bad in new[] { "", "{}", "[]", "{bad", "{\"components\":null}" })
        {
            Check(Parse(summary: bad).Status == OfficialStatus.Unknown, "bad catalog receipt is CHECK");
            Check(Parse(Rss(Item(severity: "outage")), summary: bad).Status == OfficialStatus.MajorOutage, "confirmed RSS outage survives a malformed catalog");
        }
        Check(Parse(summary: Summary(changed: "grok-bot", value: "new-state")).Status == OfficialStatus.Unknown, "unknown expected component state cannot be green");

        var tracker = new XaiStatusFeed();
        var notifications = new RecommendationNotifications();
        bool Notice(ProviderStatus status) => notifications.Observe(ProviderKind.Xai, AgentState.FullThrottle,
            RecommendationPolicy.Calculate(AgentState.FullThrottle, status.Status), true);
        var baseline = tracker.Parse(EmptyRss, SummaryJson, IncidentsJson, Now);
        Check(!Notice(baseline), "first normal observation has no alert");
        var open = tracker.Parse(Rss(Item("grok-bot")), SummaryJson, IncidentsJson, Now.AddMinutes(5));
        Check(Notice(open), "confirmed issue after GO produces one work recommendation");
        for (int poll = 2; poll <= 4; poll++)
        {
            var missing = tracker.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddMinutes(poll * 5));
            Check(missing.Status == OfficialStatus.Unknown && missing.UncertainIncidentId == "synthetic-incident", "unresolved disappearance stays uncertain on every later poll");
            Check(!Notice(missing), "disappearance cannot send a false recovery");
        }
        var closed = tracker.Parse(Rss(Item("grok-bot", state: "resolved")), SummaryJson, IncidentsJson, Now.AddMinutes(25));
        Check(closed.Status == OfficialStatus.Operational && Notice(closed), "explicit resolution releases the tracked incident and sends one recovery");
        Check(!Notice(tracker.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddMinutes(30))), "resolved episode can age out without duplicate recovery");
        var restarted = new XaiStatusFeed();
        restarted.Restore(open);
        var missingAfterRestart = restarted.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddHours(2));
        Check(missingAfterRestart.Status == OfficialStatus.Unknown && missingAfterRestart.UncertainIncidentId == open.IncidentId,
            "cached unresolved identity is retained across restart instead of a fabricated recovery");
        var secondRestart = new XaiStatusFeed();
        secondRestart.Restore(missingAfterRestart);
        Check(secondRestart.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddHours(3)).Status == OfficialStatus.Unknown,
            "cached disappearance diagnostics also retain the unresolved identity across another restart");
        Check(restarted.Parse(Rss(Item("grok-bot", state: "resolved")), SummaryJson, IncidentsJson, Now.AddHours(3)).Status == OfficialStatus.Operational,
            "explicit resolution releases an identity restored from cache");
        var longIds = Enumerable.Range(0, 4).Select(index => new string((char)('a' + index), 128)).ToArray();
        var longTracker = new XaiStatusFeed();
        _ = longTracker.Parse(Rss(longIds.Select(id => Item(id: id)).ToArray()), SummaryJson, IncidentsJson, Now);
        var longMissing = longTracker.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddMinutes(5));
        Check(longMissing.PendingXaiIncidentIds.Length == 515 && longMissing.UncertainIncidentId.Length == 500,
            "full pending identities remain distinct from truncated visible diagnostics");
        var longRestart = new XaiStatusFeed(); longRestart.Restore(longMissing);
        Check(longRestart.Parse(Rss(longIds.Select(id => Item(id: id, state: "resolved")).ToArray()), SummaryJson,             IncidentsJson, Now.AddMinutes(10)).Status == OfficialStatus.Operational,
            "truncated display ID never creates a phantom event after restart and explicit resolution");
        CheckMissingIncidentRelease(Check);
        CheckMissingIncidentExpiry(Check);
        var detail = StatusPanelModel.Card(baseline, AgentState.FullThrottle);
        Check(detail.Detail.Contains("공식 사건 피드 기준", StringComparison.Ordinal) &&
            !detail.Detail.Contains("사용량 보기", StringComparison.Ordinal) &&
            !detail.Detail.Contains("grok.com/dashboard", StringComparison.Ordinal) &&
            !detail.Detail.Contains("공유 주간 한도 값은 조회하지 않습니다.", StringComparison.Ordinal),
            "shared xAI detail retains incident-feed scope without a usage link or obsolete quota exclusion");
        Check(TrayPresentation.ProviderNotification(ProviderKind.Xai, Recommendation.Stop, open.Reason, ProviderNames.Provider).Body.Contains("Grok Bot", StringComparison.Ordinal), "problem notification includes affected product");
        Check(TrayPresentation.ProviderNotification(ProviderKind.Xai, Recommendation.Go, closed.Reason, ProviderNames.Provider).Body.Contains("Grok·Cursor·Grok Bot", StringComparison.Ordinal), "recovery notification names the product scope without claiming runtime availability");
        CheckNativeJsonSchema(Check);
        CheckIgnoredComponentDiagnostics(Check);

        for (int missing = 0; missing < 3; missing++)
        {
            string?[] inputs = [EmptyRss, SummaryJson, IncidentsJson]; inputs[missing] = null;
            var partial = new XaiStatusFeed().Parse(inputs[0], inputs[1], inputs[2], Now);
            Check(partial.Status == OfficialStatus.Unknown && partial.AssessmentIssue.Contains("수신 실패", StringComparison.Ordinal), "each required source failure is CHECK");
            Check(partial.LastSuccessfulCheckUtc == Now && partial.Source.Contains("[수신 실패]", StringComparison.Ordinal), "partial receipt is distinct from total transport failure");
        }
        var partialOutage = new XaiStatusFeed().Parse(Rss(Item()), null, IncidentsJson, Now);
        Check(partialOutage.Status == OfficialStatus.PartialOutage && partialOutage.AssessmentIssue.Length > 0, "fresh confirmed RSS problem wins over partial transport failure");
        using var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        var client = new ProviderStatusClient(http, () => Now);
        var fetched = await client.FetchAsync(ProviderKind.Xai, CancellationToken.None);
        Check(fetched.Status == OfficialStatus.Operational && handler.Requests.Count == 3 && handler.Requests.Distinct().Count() == 3, "one xAI poll reads only three fixed official public URLs");
        Check(handler.AcceptsXml && handler.HasUserAgent, "RSS Accept and versioned User-Agent are set without cookies or auth");
        handler.Failed.Add(XaiStatusFeed.SummaryUrl);
        Check((await client.FetchAsync(ProviderKind.Xai, CancellationToken.None)).Status == OfficialStatus.Unknown, "client retains partial successful receipts");
        handler.Failed.UnionWith([XaiStatusFeed.RssUrl, XaiStatusFeed.SummaryUrl, XaiStatusFeed.IncidentsUrl]);
        bool threw = false;
        try { await client.FetchAsync(ProviderKind.Xai, CancellationToken.None); }
        catch (HttpRequestException) { threw = true; }
        Check(threw, "total transport failure propagates to the existing monitor STALE policy");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped = false;
        try { await client.FetchAsync(ProviderKind.Xai, cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "shutdown cancellation is never converted to a successful receipt");
        using (var partialHandler = new FakeHandler { RssOverride = Rss(Item("grok-bot")), OversizedUrl = XaiStatusFeed.SummaryUrl })
        using (var partialHttp = new HttpClient(partialHandler))
        {
            var partial = await new ProviderStatusClient(partialHttp, () => Now).FetchAsync(ProviderKind.Xai, CancellationToken.None);
            Check(partial.Status == OfficialStatus.PartialOutage && partial.AssessmentIssue.Contains("수신 실패", StringComparison.Ordinal),
                "one oversized body never erases a completed RSS outage");
        }
        using (var partialHandler = new FakeHandler { RssOverride = Rss(Item("grok-bot")), BlockedUrl = XaiStatusFeed.SummaryUrl })
        using (var partialHttp = new HttpClient(partialHandler))
        using (var deadline = new CancellationTokenSource())
        {
            var partialClient = new ProviderStatusClient(partialHttp, () => Now);
            var pending = partialClient.FetchAsync(ProviderKind.Xai, deadline.Token, CancellationToken.None);
            await partialHandler.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            deadline.Cancel(); // Same distinction as StatusMonitor: request deadline, not lifetime.
            var partial = await pending;
            Check(partial.Status == OfficialStatus.PartialOutage && partial.AssessmentIssue.Length > 0,
                "monitor request deadline retains already received source evidence");
        }
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-xai-restart-").FullName;
        try
        {
            var store = new UsageStore(Path.Combine(directory, "synthetic.db"));
            var schedule = AgentSchedule.GetSnapshot(Now);
            await store.SaveProviderAsync(open, schedule, Recommendation.Stop);
            using var offlineHandler = new TestStatusHttpHandler { Offline = true };
            using var offlineHttp = new HttpClient(offlineHandler);
            using (var monitor = new StatusMonitor(new ProviderStatusClient(offlineHttp, () => Now.AddHours(1)), store,
                () => Now.AddHours(1), pollInterval: TimeSpan.FromHours(1)))
            {
                monitor.Start();
                await MonitorTests.WaitUntilAsync(() => !monitor.IsRefreshing && monitor.NextRefreshUtc.HasValue &&
                    monitor.Snapshot().Single(state => state.Provider == ProviderKind.Xai).CheckedAtUtc == Now.AddHours(1));
                await monitor.StopAsync();
            }
            var cache = (await store.ReadLatestStatusesAsync()).Single(state => state.Provider == ProviderKind.Xai);
            Check(cache.Status == OfficialStatus.Stale && cache.IncidentId.Length == 0 && cache.PendingXaiIncidentIds == "synthetic-incident",
                "complete failure clears current outage display but preserves independent pending ID in SQLite");
            using var restartHandler = new TestStatusHttpHandler();
            using var restartHttp = new HttpClient(restartHandler);
            using var restartedMonitor = new StatusMonitor(new ProviderStatusClient(restartHttp, () => Now.AddHours(2)), store,
                () => Now.AddHours(2), pollInterval: TimeSpan.FromHours(1));
            restartedMonitor.Start();
            await MonitorTests.WaitUntilAsync(() => !restartedMonitor.IsRefreshing && restartedMonitor.NextRefreshUtc.HasValue &&
                restartedMonitor.Snapshot().Single(state => state.Provider == ProviderKind.Xai).CheckedAtUtc == Now.AddHours(2));
            await restartedMonitor.StopAsync();
            Check(restartedMonitor.Snapshot().Single(state => state.Provider == ProviderKind.Xai).Status == OfficialStatus.Unknown,
                "open event then total failure then restart cannot produce a false GO from an empty feed");
            var overflow = open with { AssessmentIssue = new string('한', 9000) };
            await store.SaveProviderAsync(overflow, schedule, Recommendation.Stop);
            Check((await store.ReadLatestStatusesAsync()).Single(state => state.Provider == ProviderKind.Xai).PendingXaiIncidentIds == "synthetic-incident",
                "oversized optional diagnostics cannot delete the separate bounded restart state");
        }
        finally { Directory.Delete(directory, recursive: true); }
        await CheckMissingIncidentStorageAsync(Check);
        return count;
    }

    private static void CheckIgnoredComponentDiagnostics(Action<bool, string> check)
    {
        string SummaryWith(params string[] ids) => JsonSerializer.Serialize(new
        {
            status = new { indicator = "none", description = "Synthetic status" },
            components = ComponentItems().Concat(ids.Select(id => (object)new { id, name = "Synthetic ignored", status = "outage" }))
        });
        var diagnostics = new List<string>();
        var feed = new XaiStatusFeed(diagnostics.Add);
        void Read(string? summary) => feed.Parse(EmptyRss, summary, IncidentsJson, Now);
        Read(SummaryWith("synthetic-b", "synthetic-a"));
        Read(SummaryWith("synthetic-a", "synthetic-b", "synthetic-a"));
        check(diagnostics.Count == 1 && diagnostics[0] == "xAI 판정 제외 구성 요소: synthetic-a,synthetic-b",
            "same ignored summary set is logged once, regardless of order or duplicates");
        Read(SummaryWith("synthetic-a", "synthetic-c"));
        check(diagnostics.Count == 2 && diagnostics[1].EndsWith("synthetic-a,synthetic-c", StringComparison.Ordinal),
            "changed ignored summary set is logged again");
        Read(null);
        Read("{");
        Read(SummaryWith("synthetic-a", "synthetic-c"));
        check(diagnostics.Count == 2, "failed summary reads do not reset the last confirmed ignored set");
        Read(SummaryJson);
        Read(SummaryJson);
        check(diagnostics.Count == 3 && diagnostics[2] == "xAI 판정 제외 구성 요소: 없음",
            "an emptied ignored summary set is logged once");
        Read(SummaryWith("synthetic-a", "synthetic-c"));
        check(diagnostics.Count == 4, "an ignored summary set returning after disappearance is logged again");
        new XaiStatusFeed(diagnostics.Add).Parse(EmptyRss, SummaryWith("synthetic-a", "synthetic-c"), IncidentsJson, Now);
        check(diagnostics.Count == 5, "a fresh status client logs its first ignored set again");

        string[] many = Enumerable.Range(0, 33).Select(index => "synthetic-" + index.ToString("D2")).ToArray();
        Read(SummaryWith(many));
        many[^1] = "synthetic-99";
        Read(SummaryWith(many));
        check(diagnostics.Count == 7 && diagnostics[^1] == diagnostics[^2],
            "ignored set comparison uses the full set, even beyond the bounded log output");
        var emptyDiagnostics = new List<string>();
        var emptyFeed = new XaiStatusFeed(emptyDiagnostics.Add);
        emptyFeed.Parse(EmptyRss, SummaryJson, IncidentsJson, Now);
        emptyFeed.Parse(EmptyRss, SummaryJson, IncidentsJson, Now);
        check(emptyDiagnostics.Count == 1 && emptyDiagnostics[0] == "xAI 판정 제외 구성 요소: 없음",
            "the first confirmed empty set is logged once per client");
        int attempts = 0;
        var failingFeed = new XaiStatusFeed(_ => { attempts++; throw new IOException("Synthetic diagnostic failure"); });
        var first = failingFeed.Parse(EmptyRss, SummaryWith("synthetic-a"), IncidentsJson, Now);
        var repeated = failingFeed.Parse(EmptyRss, SummaryWith("synthetic-a"), IncidentsJson, Now);
        check(attempts == 1 && first.Status == OfficialStatus.Operational && repeated.Status == OfficialStatus.Operational,
            "a throwing diagnostic callback is still called only once for an unchanged set and cannot change status");
    }

    private static void CheckNativeJsonSchema(Action<bool, string> check)
    {
        ProviderStatus Read(string? summary = null, string? history = null, Action<string>? log = null) =>
            new XaiStatusFeed(log).Parse(EmptyRss, summary ?? SummaryJson, history ?? IncidentsJson, Now);
        JsonObject Event() => JsonSerializer.SerializeToNode(Incident(components: ["grok-bot"]))!.AsObject();
        string Events(JsonObject item) => History(item);
        string[] excluded = ["imagine", "api-eu-west-1", "admin-apis", "api-console", "api-docs", "single-sign-on", "x-ai"];
        foreach (var (state, expected) in new[] { ("available", OfficialStatus.Operational), ("info", OfficialStatus.Degraded),
            ("disruption", OfficialStatus.PartialOutage), ("outage", OfficialStatus.MajorOutage), ("no_data", OfficialStatus.Unknown) })
            foreach (var component in Catalog)
                check(Read(Summary(changed: component.Id, value: state)).Status == expected,
                    "native component vocabulary / " + component.Id + " / " + state);

        foreach (string slug in excluded)
        {
            var summary = JsonNode.Parse(SummaryJson)!;
            summary["components"]!.AsArray().Add(new JsonObject { ["id"] = slug, ["name"] = "Synthetic excluded", ["status"] = "outage" });
            check(Read(summary.ToJsonString()).Status == OfficialStatus.Operational, "excluded summary component: " + slug);
            check(Read(history: History(Incident(components: [slug]))).Status == OfficialStatus.Operational, "excluded-only JSON incident: " + slug);
            check(new XaiStatusFeed().Parse(Rss(Item(slug)), SummaryJson, IncidentsJson, Now).Status == OfficialStatus.Operational,
                "excluded-only trusted RSS incident: " + slug);
            var combined = Read(history: History(Incident(components: [slug, "grok-bot"])));
            check(combined.Status == OfficialStatus.PartialOutage && combined.RelevantComponent == "Grok Bot" && combined.AssessmentIssue.Length == 0,
                "excluded slug cannot obscure an in-scope JSON incident: " + slug);
        }
        foreach (string value in new[] { "available", "outage", "future-state" })
        {
            var summary = JsonNode.Parse(SummaryJson)!;
            summary["components"]!.AsArray().Add(new JsonObject { ["id"] = "synthetic-new-service", ["name"] = "must-not-log-name", ["status"] = value, ["extra"] = "must-not-log-body" });
            var diagnostics = new List<string>();
            var current = Read(summary.ToJsonString(), log: diagnostics.Add);
            check(current.Status == OfficialStatus.Operational && current.AssessmentIssue.Length == 0 && current.RelevantComponent.Length == 0,
                "a new summary slug changes neither assessment nor card scope: " + value);
            check(diagnostics.Count == 1 && diagnostics[0].Contains("synthetic-new-service", StringComparison.Ordinal) &&
                !diagnostics[0].Contains("must-not-log", StringComparison.Ordinal), "diagnostics contain only the new slug");
            check(Read(summary.ToJsonString(), log: _ => throw new IOException("must-not-log-exception")).Status == OfficialStatus.Operational,
                "diagnostic write failure is isolated from public status");
        }
        check(Read(history: History(Incident(components: ["synthetic-new-service"]))).Status == OfficialStatus.Unknown,
            "unknown-only ongoing JSON scope is CHECK, unlike a new summary component");
        check(Read(history: History(Incident(components: ["synthetic-new-service", "imagine"]))).Status == OfficialStatus.Unknown,
            "excluded plus unknown ongoing JSON scope is still CHECK");
        var partlyKnown = Read(history: History(Incident(components: ["synthetic-new-service", "grok-bot"])));
        check(partlyKnown.Status == OfficialStatus.PartialOutage && partlyKnown.AssessmentIssue.Length > 0,
            "known JSON outage takes priority while unknown affected scope remains explicit");
        check(Read(history: History(Incident(components: ["agent-sdk"]))).RelevantComponent == "Cursor · Agent SDK",
            "Agent SDK uses the Cursor display scope");
        foreach (string legacy in new[] { "grok-build", "office-workspace-plugins", "bugbot" })
        {
            check(Read(history: History(Incident(components: [legacy]))).Status == OfficialStatus.PartialOutage,
                "recognized legacy service remains assessed without a required catalog row: " + legacy);
            var summary = JsonNode.Parse(SummaryJson)!;
            summary["components"]!.AsArray().Add(new JsonObject { ["id"] = legacy, ["name"] = "Synthetic legacy", ["status"] = "outage" });
            var diagnostics = new List<string>();
            check(Read(summary.ToJsonString(), log: diagnostics.Add).Status == OfficialStatus.Operational &&
                diagnostics.Count == 1 && diagnostics[0].Contains(legacy, StringComparison.Ordinal),
                "RSS-only legacy slug appearing in summary is still diagnostic-only: " + legacy);
        }

        foreach (var (impact, expected) in new[] { ("none", OfficialStatus.Degraded), ("minor", OfficialStatus.PartialOutage),
            ("major", OfficialStatus.PartialOutage), ("critical", OfficialStatus.MajorOutage) })
            foreach (string phase in new[] { "investigating", "monitoring", "resolved" })
                check(Read(history: History(Incident(state: phase, impact: impact, components: ["grok-bot"]))).Status ==
                    (phase == "resolved" ? OfficialStatus.Operational : expected), "native incident impact / " + phase + " / " + impact);

        foreach (string phase in new[] { "identified", "postmortem", "completed", "future-phase", "" })
            check(Read(history: History(Incident(state: phase, components: ["grok-bot"]))).Status == OfficialStatus.Unknown,
                "phase outside the page JSON enum is CHECK: " + phase);
        var wrongIdentifier = Event();
        wrongIdentifier["components"] = new JsonArray(new JsonObject { ["id"] = "grok-bot", ["name"] = "Synthetic" });
        check(Read(history: Events(wrongIdentifier)).Status == OfficialStatus.Unknown, "incident components require slug, never id");
        foreach (string name in new[] { "id", "name", "status", "impact", "created_at", "updated_at", "resolved_at", "incident_updates", "components" })
        {
            var item = Event(); item.Remove(name);
            check(Read(history: Events(item)).Status == OfficialStatus.Unknown, "missing page-schema incident field: " + name);
        }
        foreach (string name in new[] { "created_at", "updated_at", "resolved_at" })
        {
            var item = Event(); item[name] = "not-a-time";
            check(Read(history: Events(item)).Status == OfficialStatus.Unknown, "malformed ISO timestamp: " + name);
        }
        foreach (string name in new[] { "id", "name", "status" })
        {
            var summary = JsonNode.Parse(SummaryJson)!; summary["components"]![0]!.AsObject().Remove(name);
            check(Read(summary.ToJsonString()).Status == OfficialStatus.Unknown, "missing page-schema summary component field: " + name);
        }
        foreach (string indicator in new[] { "none", "unobserved-global-indicator", "" })
        {
            var summary = JsonNode.Parse(SummaryJson)!; summary["status"]!["indicator"] = indicator;
            summary["incidents"] = "outside-summary-schema";
            check(Read(summary.ToJsonString()).Status == OfficialStatus.Operational,
                "overall indicator is a string, not a product-scope enum; extra summary fields are ignored");
        }
        foreach (string name in new[] { "indicator", "description" })
        {
            var summary = JsonNode.Parse(SummaryJson)!; summary["status"]!.AsObject().Remove(name);
            check(Read(summary.ToJsonString()).Status == OfficialStatus.Unknown, "missing summary status field: " + name);
        }
        var withUpdates = Event();
        var update = new JsonObject { ["id"] = "synthetic-update", ["status"] = "investigating", ["body"] = "Resolved old text is not current state",
            ["created_at"] = "2026-01-01T00:00:00Z", ["affected_components"] = new JsonArray("imagine") };
        withUpdates["incident_updates"] = new JsonArray(update.DeepClone());
        check(Read(history: Events(withUpdates)).Status == OfficialStatus.PartialOutage, "update body and affected_components never override current root scope/state");
        foreach (string name in new[] { "id", "status", "body", "created_at" })
        {
            var item = withUpdates.DeepClone().AsObject(); item["incident_updates"]![0]!.AsObject().Remove(name);
            check(Read(history: Events(item)).Status == OfficialStatus.Unknown, "missing page-schema update field: " + name);
        }
        foreach (string body in new[] { "null", "[]", "{bad", "{\"incidents\":null}", "{\"incidents\":[3]}" })
            check(Read(history: body).Status == OfficialStatus.Unknown, "malformed native incidents root remains CHECK");
    }

    private static void CheckMissingIncidentRelease(Action<bool, string> check)
    {
        foreach (var (source, phase) in new[] { ("RSS", "resolved"), ("incidents", "resolved"), ("both", "resolved") })
        foreach (bool restart in new[] { false, true })
        {
            string label = $"{source}/{phase}, restart={restart}: ";
            var tracker = new XaiStatusFeed();
            _ = tracker.Parse(Rss(Item()), SummaryJson, IncidentsJson, Now);
            var missing = tracker.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddMinutes(5));
            check(missing.Status == OfficialStatus.Unknown && missing.PendingXaiIncidentIds == "synthetic-incident", label + "disappearance enters CHECK");
            if (restart) { tracker = new XaiStatusFeed(); tracker.Restore(missing); }
            var late = tracker.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddHours(23));
            check(late.Status == OfficialStatus.Unknown && late.PendingXaiIncidentIds == "synthetic-incident", label + "elapsed time before 24 hours and restart retain the missing identity");
            var unrelatedResolution = tracker.Parse(Rss(Item(id: "synthetic-other", state: "resolved")), SummaryJson, IncidentsJson, Now.AddHours(23).AddMinutes(5));
            check(unrelatedResolution.Status == OfficialStatus.Unknown && unrelatedResolution.PendingXaiIncidentIds == "synthetic-incident", label + "another incident resolution cannot clear CHECK");
            var next = tracker.Parse(Rss(Item("cursor-cli", id: "synthetic-next")), SummaryJson, IncidentsJson, Now.AddHours(23).AddMinutes(10));
            check(next.Status == OfficialStatus.PartialOutage && next.UncertainIncidentId == "synthetic-incident" && next.PendingXaiIncidentIds.Contains("synthetic-next", StringComparison.Ordinal), label + "new issue takes severity priority without forgetting the missing incident");
            var nextResolved = tracker.Parse(Rss(Item("cursor-cli", id: "synthetic-next", state: "resolved")), SummaryJson, IncidentsJson, Now.AddHours(23).AddMinutes(15));
            check(nextResolved.Status == OfficialStatus.Unknown && nextResolved.PendingXaiIncidentIds == "synthetic-incident", label + "resolving the new incident returns to the original CHECK");
            var reappeared = tracker.Parse(Rss(Item()), SummaryJson, IncidentsJson, Now.AddHours(23).AddMinutes(20));
            check(reappeared.Status == OfficialStatus.PartialOutage && reappeared.PendingXaiIncidentIds == "synthetic-incident", label + "same incident reappearing as active is an outage rather than a recovery");
            string resolved = JsonSerializer.Serialize(new { incidents = new[] { Incident(state: phase, components: ["grok-com"]) } });
            var closed = tracker.Parse(source is "RSS" or "both" ? Rss(Item(state: "resolved")) : EmptyRss,
                SummaryJson, source is "incidents" or "both" ? resolved : IncidentsJson, Now.AddHours(23).AddMinutes(25));
            check(closed.Status == OfficialStatus.Operational && closed.PendingXaiIncidentIds.Length == 0 && closed.AssessmentIssue.Length == 0, label + "matching explicit resolution releases CHECK on that poll");
            var after = tracker.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddHours(23).AddMinutes(30));
            check(after.Status == OfficialStatus.Operational && after.PendingXaiIncidentIds.Length == 0, label + "a resolved incident may leave the feed without recreating CHECK");
        }

        var conflict = new XaiStatusFeed();
        _ = conflict.Parse(Rss(Item()), SummaryJson, IncidentsJson, Now);
        var opposed = conflict.Parse(Rss(Item(state: "resolved")), SummaryJson, History(Incident(components: ["grok-bot"])), Now.AddMinutes(5));
        check(opposed.Status == OfficialStatus.PartialOutage && opposed.PendingXaiIncidentIds == "synthetic-incident", "active observation blocks matching resolution from another source");
        check(conflict.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddMinutes(10)).Status == OfficialStatus.Unknown, "a conflicted resolution cannot silently expire when both observations disappear");
        check(conflict.Parse(Rss(Item(state: "resolved")), SummaryJson, IncidentsJson, Now.AddMinutes(15)).Status == OfficialStatus.Operational, "later unopposed matching resolution clears the conflict");

        var multiple = new XaiStatusFeed();
        _ = multiple.Parse(Rss(Item(), Item("grok-bot", id: "synthetic-second")), SummaryJson, IncidentsJson, Now);
        var one = multiple.Parse(Rss(Item(state: "resolved")), SummaryJson, IncidentsJson, Now.AddMinutes(5));
        check(one.Status == OfficialStatus.Unknown && one.PendingXaiIncidentIds == "synthetic-second", "each missing incident must be resolved independently");
        check(multiple.Parse(Rss(Item("grok-bot", id: "synthetic-second", state: "resolved")), SummaryJson, IncidentsJson, Now.AddMinutes(10)).Status == OfficialStatus.Operational, "resolving the final missing incident releases CHECK");

        var partial = new XaiStatusFeed();
        _ = partial.Parse(Rss(Item()), SummaryJson, IncidentsJson, Now);
        var receivedResolution = partial.Parse(Rss(Item(state: "resolved")), null, IncidentsJson, Now.AddMinutes(5));
        check(receivedResolution.Status == OfficialStatus.Unknown && receivedResolution.PendingXaiIncidentIds.Length == 0 && receivedResolution.AssessmentIssue.Contains("수신 실패", StringComparison.Ordinal), "matching resolution clears the incident but a separate source failure still requires CHECK");
        check(partial.Parse(EmptyRss, SummaryJson, IncidentsJson, Now.AddMinutes(10)).Status == OfficialStatus.Operational, "healthy receipt clears the remaining source failure without reviving a resolved incident");
    }

    private const string ExpiryNote = "해결 표시 없이 피드에서 사라짐(24시간 경과로 해제)";

    private static ProviderStatus ReadAt(XaiStatusFeed tracker, DateTimeOffset at, string rss = EmptyRss) =>
        tracker.Parse(rss, SummaryJson, IncidentsJson, at);

    private static void CheckMissingIncidentExpiry(Action<bool, string> check)
    {
        DateTimeOffset firstMissing = Now.AddMinutes(5);
        var tracker = new XaiStatusFeed();
        _ = ReadAt(tracker, Now, Rss(Item()));
        var missing = ReadAt(tracker, firstMissing);
        check(missing.Status == OfficialStatus.Unknown && missing.XaiMissingIncidents.Single() == new XaiMissingIncident("synthetic-incident", firstMissing),
            "first valid absence starts the per-ID 24-hour CHECK clock");
        var repeated = ReadAt(tracker, firstMissing.AddHours(12));
        check(repeated.XaiMissingIncidents.Single().MissingSinceUtc == firstMissing, "repeated absence preserves the first missing time");
        var before = ReadAt(tracker, firstMissing.AddHours(24).AddMilliseconds(-1));
        check(before.Status == OfficialStatus.Unknown && before.PendingXaiIncidentIds == "synthetic-incident" && before.XaiIncidentExpiryNote.Length == 0,
            "23:59:59.999 since first absence is still CHECK without an expiry note");
        var expired = ReadAt(tracker, firstMissing.AddHours(24));
        check(expired.Status == OfficialStatus.Operational && expired.PendingXaiIncidentIds.Length == 0 && expired.XaiMissingIncidents.Count == 0,
            "a valid empty receipt at exactly 24 hours releases the missing identity");
        check(expired.XaiIncidentExpiryNote == ExpiryNote && expired.XaiRecoveryUnconfirmed,
            "time expiry records the exact explanation and never claims confirmed recovery");
        check(expired.AssessmentIssue.Length == 0 && expired.UncertainIncidentId.Length == 0 && expired.IncidentId.Length == 0,
            "expired disappearance leaves no current assessment or incident diagnostics");
        var after = ReadAt(tracker, firstMissing.AddHours(24).AddMilliseconds(1));
        check(after.Status == OfficialStatus.Operational && after.XaiIncidentExpiryNote == expired.XaiIncidentExpiryNote && after.XaiRecoveryUnconfirmed,
            "the next valid receipt retains the latest expiry note and unconfirmed-recovery flag");
        var justAfterTracker = new XaiStatusFeed();
        _ = ReadAt(justAfterTracker, Now, Rss(Item()));
        _ = ReadAt(justAfterTracker, firstMissing);
        check(ReadAt(justAfterTracker, firstMissing.AddHours(24).AddMilliseconds(1)).Status == OfficialStatus.Operational,
            "first receipt just after 24 hours also releases CHECK");
        var card = StatusPanelModel.Card(expired, AgentState.FullThrottle);
        check(card.Recommendation == Recommendation.Go && card.HeadingTone == PanelTone.Good && card.Detail.Split('\n').Contains("과거 사건 해제: " + ExpiryNote),
            "expiry detail shows the exact note and a GO card instead of gray CHECK");
        var allHealthy = Enum.GetValues<ProviderKind>().Select(provider => provider == ProviderKind.Xai ? expired :
            new ProviderStatus(provider, OfficialStatus.Operational, Now, Now, "Synthetic healthy")).ToArray();
        check(TrayPresentation.Calculate(AgentState.FullThrottle, allHealthy).Attention == TrayAttention.Green,
            "expired xAI no longer keeps an otherwise healthy tray gray");
        var restored = new XaiStatusFeed(); restored.Restore(expired);
        var restoredHealthy = ReadAt(restored, firstMissing.AddDays(2));
        check(restoredHealthy.Status == OfficialStatus.Operational && restoredHealthy.XaiIncidentExpiryNote == expired.XaiIncidentExpiryNote && restoredHealthy.XaiRecoveryUnconfirmed,
            "feed restoration retains an expired episode note without recreating its identity");

        foreach (bool uncertain in new[] { false, true })
        {
            var reappearing = new XaiStatusFeed();
            _ = ReadAt(reappearing, Now, Rss(Item()));
            _ = ReadAt(reappearing, firstMissing);
            var visible = ReadAt(reappearing, firstMissing.AddHours(24), Rss(Item(state: uncertain ? "future_phase" : "investigating")));
            check(visible.Status == (uncertain ? OfficialStatus.Unknown : OfficialStatus.PartialOutage) && visible.XaiMissingIncidents.Count == 0 &&
                visible.PendingXaiIncidentIds == "synthetic-incident" && visible.XaiIncidentExpiryNote.Length == 0,
                "same-ID reappearance cancels expiry even with an unrecognized state: " + uncertain);
            DateTimeOffset againAt = firstMissing.AddHours(25);
            var again = ReadAt(reappearing, againAt);
            check(again.Status == OfficialStatus.Unknown && again.XaiMissingIncidents.Single().MissingSinceUtc == againAt,
                "a disappearance after reappearance starts a fresh 24 hours: " + uncertain);
            check(ReadAt(reappearing, againAt.AddHours(24).AddMilliseconds(-1)).Status == OfficialStatus.Unknown,
                "reappearance prevents the old missing clock from releasing the episode early: " + uncertain);
            check(ReadAt(reappearing, againAt.AddHours(24)).Status == OfficialStatus.Operational,
                "the restarted missing clock releases at its own 24-hour boundary: " + uncertain);
        }
        CheckMalformedReappearance(check, firstMissing);

        var multiple = new XaiStatusFeed();
        _ = ReadAt(multiple, Now, Rss(Item(), Item(id: "synthetic-second")));
        _ = ReadAt(multiple, firstMissing, Rss(Item(id: "synthetic-second")));
        DateTimeOffset secondMissing = firstMissing.AddHours(1);
        var both = ReadAt(multiple, secondMissing);
        check(both.XaiMissingIncidents.Single(item => item.Id == "synthetic-incident").MissingSinceUtc == firstMissing &&
            both.XaiMissingIncidents.Single(item => item.Id == "synthetic-second").MissingSinceUtc == secondMissing,
            "multiple incident IDs keep independent missing times");
        var firstExpired = ReadAt(multiple, firstMissing.AddHours(24));
        check(firstExpired.Status == OfficialStatus.Unknown && firstExpired.PendingXaiIncidentIds == "synthetic-second" &&
            firstExpired.XaiMissingIncidents.Single().MissingSinceUtc == secondMissing && firstExpired.XaiIncidentExpiryNote == ExpiryNote,
            "expiring one ID leaves the younger missing ID in CHECK");
        check(ReadAt(multiple, secondMissing.AddHours(24)).Status == OfficialStatus.Operational, "the final missing ID expires on its own boundary");

        var old = new XaiStatusFeed();
        _ = ReadAt(old, Now.AddDays(-30), Rss(Item()));
        var oldMissing = ReadAt(old, Now);
        check(oldMissing.Status == OfficialStatus.Unknown && oldMissing.XaiMissingIncidents.Single().MissingSinceUtc == Now,
            "old incident publication and first-seen time never replace its first absence time");
        check(ReadAt(old, Now.AddHours(24).AddMilliseconds(-1)).Status == OfficialStatus.Unknown, "a long-lived incident receives a complete 24-hour missing interval");

        var rewind = new XaiStatusFeed();
        _ = ReadAt(rewind, Now, Rss(Item()));
        _ = ReadAt(rewind, firstMissing);
        DateTimeOffset rewoundAt = firstMissing.AddHours(-1);
        var rewound = ReadAt(rewind, rewoundAt);
        check(rewound.Status == OfficialStatus.Unknown && rewound.XaiMissingIncidents.Single().MissingSinceUtc == rewoundAt,
            "clock rollback resets the missing baseline to the new observation");
        check(ReadAt(rewind, rewoundAt.AddHours(24).AddMilliseconds(-1)).Status == OfficialStatus.Unknown, "a rolled-back clock still requires a complete new 24-hour interval");
        check(ReadAt(rewind, rewoundAt.AddHours(24)).Status == OfficialStatus.Operational, "the rebased clock expires at its own boundary");

        var explicitResolution = new XaiStatusFeed();
        _ = ReadAt(explicitResolution, Now, Rss(Item()));
        _ = ReadAt(explicitResolution, firstMissing);
        var resolvedAtBoundary = ReadAt(explicitResolution, firstMissing.AddHours(24), Rss(Item(state: "resolved")));
        check(resolvedAtBoundary.Status == OfficialStatus.Operational && resolvedAtBoundary.PendingXaiIncidentIds.Length == 0 &&
            resolvedAtBoundary.XaiMissingIncidents.Count == 0 && resolvedAtBoundary.XaiIncidentExpiryNote.Length == 0 && !resolvedAtBoundary.XaiRecoveryUnconfirmed,
            "matching explicit resolution wins over time expiry on the same boundary poll");
        CheckInvalidMissingReceipts(check);
        CheckExpiryNotifications(check, tracker, expired, firstMissing.AddHours(25));
    }

    private static void CheckMalformedReappearance(Action<bool, string> check, DateTimeOffset firstMissing)
    {
        var mismatched = Item();
        mismatched.Elements("category").First(item => item.Value == "investigating").Value = "monitoring";
        var untitled = Item(); untitled.Element("title")!.Remove();
        (string Label, string Rss, string History)[] readings =
        [
            ("RSS category/header mismatch", Rss(mismatched), IncidentsJson),
            ("RSS missing title", Rss(untitled), IncidentsJson),
            ("JSON empty status", EmptyRss, History([Incident(state: "", components: ["grok-bot"])]))
        ];
        foreach (var reading in readings)
        {
            var tracker = new XaiStatusFeed();
            _ = ReadAt(tracker, Now, Rss(Item()));
            _ = ReadAt(tracker, firstMissing);
            var malformed = tracker.Parse(reading.Rss, SummaryJson, reading.History, firstMissing.AddHours(12));
            check(malformed.Status == OfficialStatus.Unknown && malformed.PendingXaiIncidentIds == "synthetic-incident" && malformed.XaiMissingIncidents.Count == 0,
                reading.Label + ": seeing the same valid ID cancels the missing clock before field validation");
            DateTimeOffset newMissing = firstMissing.AddHours(24);
            var again = ReadAt(tracker, newMissing);
            check(again.Status == OfficialStatus.Unknown && again.XaiMissingIncidents.Single().MissingSinceUtc == newMissing && again.XaiIncidentExpiryNote.Length == 0,
                reading.Label + ": a valid empty receipt at the old boundary begins a new 24-hour CHECK");
            check(ReadAt(tracker, newMissing.AddHours(24).AddMilliseconds(-1)).Status == OfficialStatus.Unknown,
                reading.Label + ": malformed reappearance prevents expiry before the new 24-hour boundary");
            var expired = ReadAt(tracker, newMissing.AddHours(24));
            check(expired.Status == OfficialStatus.Operational && expired.XaiIncidentExpiryNote == ExpiryNote,
                reading.Label + ": valid absence releases only after the new complete 24-hour interval");
        }
    }

    private static void CheckInvalidMissingReceipts(Action<bool, string> check)
    {
        var invalid = new List<(string Label, string? Rss, string? Summary, string? History)>();
        for (int index = 0; index < 3; index++)
        {
            string?[] inputs = [EmptyRss, SummaryJson, IncidentsJson]; inputs[index] = null;
            invalid.Add(("missing source " + index, inputs[0], inputs[1], inputs[2]));
            inputs = [EmptyRss, SummaryJson, IncidentsJson]; inputs[index] = index == 0 ? "<rss>" : "{bad";
            invalid.Add(("malformed source " + index, inputs[0], inputs[1], inputs[2]));
        }
        invalid.Add(("missing product scope", EmptyRss, Summary(omit: "grok-bot"), IncidentsJson));
        invalid.Add(("uncertain unrelated episode", Rss(Item("future-product", id: "synthetic-other")), SummaryJson, IncidentsJson));
        invalid.Add(("unknown component state", EmptyRss, Summary(changed: "grok-bot", value: "future-state"), IncidentsJson));
        foreach (var input in invalid)
        {
            var tracker = new XaiStatusFeed();
            _ = ReadAt(tracker, Now, Rss(Item()));
            var invalidFirst = tracker.Parse(input.Rss, input.Summary, input.History, Now.AddHours(1));
            check(invalidFirst.Status == OfficialStatus.Unknown && invalidFirst.PendingXaiIncidentIds == "synthetic-incident" && invalidFirst.XaiMissingIncidents.Count == 0,
                input.Label + ": invalid evidence cannot start a missing interval");
            DateTimeOffset firstMissing = Now.AddDays(2);
            var validFirst = ReadAt(tracker, firstMissing);
            check(validFirst.Status == OfficialStatus.Unknown && validFirst.XaiMissingIncidents.Single().MissingSinceUtc == firstMissing,
                input.Label + ": the next complete valid absence starts its full 24 hours");
            var invalidBoundary = tracker.Parse(input.Rss, input.Summary, input.History, firstMissing.AddHours(24));
            check(invalidBoundary.Status == OfficialStatus.Unknown && invalidBoundary.PendingXaiIncidentIds == "synthetic-incident" &&
                invalidBoundary.XaiMissingIncidents.Single().MissingSinceUtc == firstMissing && invalidBoundary.XaiIncidentExpiryNote.Length == 0,
                input.Label + ": invalid evidence at 24 hours preserves the first time without expiring");
            check(ReadAt(tracker, firstMissing.AddHours(24).AddMinutes(5)).Status == OfficialStatus.Operational,
                input.Label + ": the next complete valid empty receipt may expire the preserved interval");
        }
    }

    private static void CheckExpiryNotifications(Action<bool, string> check, XaiStatusFeed tracker, ProviderStatus expired, DateTimeOffset nextAt)
    {
        var schedule = AgentSchedule.GetSnapshot(Now);
        check(schedule.State == AgentState.FullThrottle, "expiry notification fixture is FULL THROTTLE");
        var ticker = new StatusTicker(strictInput: true);
        ticker.Tick(schedule, [expired with { Status = OfficialStatus.Operational, XaiRecoveryUnconfirmed = false }], TickReason.Initial);
        var original = expired with { Status = OfficialStatus.PartialOutage, XaiRecoveryUnconfirmed = false };
        check(ticker.Tick(schedule, [original], TickReason.ProviderChanged).ProviderAlerts.Count == 1, "confirmed synthetic outage creates an initial work alert");
        ticker.Tick(schedule, [original with { Status = OfficialStatus.Unknown }], TickReason.ProviderChanged);
        check(ticker.Tick(schedule, [expired], TickReason.ProviderChanged).ProviderAlerts.Count == 0,
            "time-expiry GO consumes the recommendation baseline without a recovery alert");
        check(ticker.Tick(schedule, [ReadAt(tracker, nextAt)], TickReason.Timer).ProviderAlerts.Count == 0, "subsequent healthy polls never leak a deferred time-expiry recovery alert");
        var next = ReadAt(tracker, nextAt.AddMinutes(5), Rss(Item(id: "synthetic-new")));
        check(!next.XaiRecoveryUnconfirmed && next.Status == OfficialStatus.PartialOutage,
            "a newly confirmed outage restores the normal recovery flow");
        var nextAlert = ticker.Tick(schedule, [next], TickReason.ProviderChanged).ProviderAlerts;
        check(nextAlert.Count == 1 && !nextAlert[0].Recovered, "a new outage after suppressed expiry still announces its work recommendation");
        var resolved = ReadAt(tracker, nextAt.AddMinutes(10), Rss(Item(id: "synthetic-new", state: "resolved")));
        var recovery = ticker.Tick(schedule, [resolved], TickReason.ProviderChanged).ProviderAlerts;
        check(!resolved.XaiRecoveryUnconfirmed && recovery.Count == 1 && recovery[0].Recovered,
            "explicit resolution of the new outage sends its normal recovery alert");
        check(ticker.Tick(schedule, [ReadAt(tracker, nextAt.AddMinutes(15))], TickReason.Timer).ProviderAlerts.Count == 0,
            "explicit recovery is delivered once even while the historical expiry note remains");
    }

    private static async Task CheckMissingIncidentStorageAsync(Action<bool, string> check)
    {
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-xai-expiry-").FullName;
        try
        {
            string database = Path.Combine(directory, "synthetic.db");
            DateTimeOffset firstMissing = Now.AddMinutes(5);
            var tracker = new XaiStatusFeed();
            _ = ReadAt(tracker, Now, Rss(Item()));
            var missing = ReadAt(tracker, firstMissing);
            var store = new UsageStore(database);
            await store.SaveProviderAsync(missing, AgentSchedule.GetSnapshot(firstMissing), Recommendation.Check);
            var reloaded = (await new UsageStore(database).ReadLatestStatusesAsync()).Single(state => state.Provider == ProviderKind.Xai);
            check(reloaded.XaiMissingIncidents.SequenceEqual(missing.XaiMissingIncidents) && reloaded.PendingXaiIncidentIds == missing.PendingXaiIncidentIds,
                "temporary SQLite preserves missing ID and first absence time across a fresh store instance");
            var failed = await RestartPollAsync(firstMissing.AddHours(24), offline: true);
            check(failed.Status == OfficialStatus.Stale && failed.XaiMissingIncidents.Single().MissingSinceUtc == firstMissing && failed.PendingXaiIncidentIds == "synthetic-incident",
                "restart plus total fake transport failure preserves the missing interval beyond 24 hours");
            check(failed.XaiIncidentExpiryNote.Length == 0 && !failed.XaiRecoveryUnconfirmed, "total failure cannot manufacture an expiry or unconfirmed recovery");
            var expired = await RestartPollAsync(firstMissing.AddHours(24).AddMinutes(5), offline: false);
            check(expired.Status == OfficialStatus.Operational && expired.PendingXaiIncidentIds.Length == 0 && expired.XaiMissingIncidents.Count == 0 &&
                expired.XaiIncidentExpiryNote == ExpiryNote && expired.XaiRecoveryUnconfirmed,
                "first valid receipt after restart and total failure expires from the original persisted time");
            var noteReload = (await new UsageStore(database).ReadLatestStatusesAsync()).Single(state => state.Provider == ProviderKind.Xai);
            check(noteReload.XaiIncidentExpiryNote == expired.XaiIncidentExpiryNote && noteReload.XaiRecoveryUnconfirmed,
                "SQLite persists the exact expiry note and unconfirmed-recovery flag");
            var afterRestart = await RestartPollAsync(firstMissing.AddHours(25), offline: false);
            check(afterRestart.Status == OfficialStatus.Operational && afterRestart.XaiIncidentExpiryNote == expired.XaiIncidentExpiryNote && afterRestart.XaiRecoveryUnconfirmed,
                "later restart and healthy receipt retain the latest historical expiry note");
            var laterFailure = await RestartPollAsync(firstMissing.AddHours(26), offline: true);
            check(laterFailure.Status == OfficialStatus.Stale && laterFailure.XaiIncidentExpiryNote == expired.XaiIncidentExpiryNote && laterFailure.XaiRecoveryUnconfirmed,
                "a later total failure preserves the saved expiry explanation and flag");
            var legacy = missing with { XaiMissingIncidents = [], CheckedAtUtc = Now.AddDays(-30), LastSuccessfulCheckUtc = Now.AddDays(-30) };
            await store.SaveProviderAsync(legacy, AgentSchedule.GetSnapshot(Now), Recommendation.Check);
            SetXaiTrackingValue(database, null);
            check(ReadXaiTrackingValue(database) is null, "legacy fixture contains the v1 ID entry with the v2 tracking key removed");
            var legacyFirst = await RestartPollAsync(Now.AddDays(3), offline: false);
            check(legacyFirst.Status == OfficialStatus.Unknown && legacyFirst.XaiMissingIncidents.Single().MissingSinceUtc == Now.AddDays(3),
                "IDs-only legacy cache starts 24 hours at the next valid absence rather than the old cache time");
            check((await RestartPollAsync(Now.AddDays(4).AddMilliseconds(-1), offline: false)).Status == OfficialStatus.Unknown,
                "IDs-only legacy migration preserves CHECK until its new 24-hour boundary");
            check((await RestartPollAsync(Now.AddDays(4), offline: false)).Status == OfficialStatus.Operational,
                "IDs-only legacy migration expires at exactly 24 hours from its first valid absence");
            foreach (string invalidKind in new[] { "string Version", "duplicate Missing ID", "future missing time", "oversized v2" })
            {
                await store.SaveProviderAsync(missing, AgentSchedule.GetSnapshot(firstMissing), Recommendation.Check);
                var invalid = JsonNode.Parse(ReadXaiTrackingValue(database)!)!.AsObject();
                switch (invalidKind)
                {
                    case "string Version": invalid["Version"] = "2"; break;
                    case "duplicate Missing ID":
                        invalid["Missing"]!.AsArray().Add(invalid["Missing"]![0]!.DeepClone()); break;
                    case "future missing time":
                        invalid["Missing"]![0]!["MissingSinceUtc"] = firstMissing.AddHours(1).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture); break;
                    case "oversized v2": invalid["XaiIncidentExpiryNote"] = new string('x', 32 * 1024 + 1); break;
                }
                SetXaiTrackingValue(database, invalid.ToJsonString());
                var fallback = (await new UsageStore(database).ReadLatestStatusesAsync()).Single(state => state.Provider == ProviderKind.Xai);
                check(fallback.PendingXaiIncidentIds == "synthetic-incident" && fallback.XaiMissingIncidents.Count == 0 && fallback.XaiIncidentExpiryNote.Length == 0,
                    invalidKind + ": invalid v2 falls back to the v1 identity without throwing or retaining its clock");
                var fallbackTracker = new XaiStatusFeed(); fallbackTracker.Restore(fallback);
                DateTimeOffset newMissing = Now.AddDays(10);
                var first = ReadAt(fallbackTracker, newMissing);
                check(first.Status == OfficialStatus.Unknown && first.XaiMissingIncidents.Single().MissingSinceUtc == newMissing,
                    invalidKind + ": the next valid absence begins a fresh 24-hour interval instead of early expiry");
                check(ReadAt(fallbackTracker, newMissing.AddHours(24).AddMilliseconds(-1)).Status == OfficialStatus.Unknown,
                    invalidKind + ": the fallback identity remains in CHECK for the full new interval");
                var released = ReadAt(fallbackTracker, newMissing.AddHours(24));
                check(released.Status == OfficialStatus.Operational && released.XaiIncidentExpiryNote == ExpiryNote,
                    invalidKind + ": fallback releases normally at the new 24-hour boundary");
            }

            async Task<ProviderStatus> RestartPollAsync(DateTimeOffset at, bool offline)
            {
                using var handler = new TestStatusHttpHandler { Offline = offline };
                using var http = new HttpClient(handler);
                using var monitor = new StatusMonitor(new ProviderStatusClient(http, () => at), new UsageStore(database), () => at,
                    pollInterval: TimeSpan.FromHours(1));
                monitor.Start();
                await MonitorTests.WaitUntilAsync(() => !monitor.IsRefreshing && monitor.NextRefreshUtc.HasValue &&
                    monitor.Snapshot().Single(state => state.Provider == ProviderKind.Xai).CheckedAtUtc == at);
                await monitor.StopAsync();
                check(monitor.StorageError.Length == 0, "synthetic expiry restart poll saves without storage errors");
                return monitor.Snapshot().Single(state => state.Provider == ProviderKind.Xai);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string? ReadXaiTrackingValue(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppMetadata WHERE Key='XaiPendingIncidents.v2';";
        return command.ExecuteScalar() as string;
    }

    private static void SetXaiTrackingValue(string database, string? value)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = value is null ? "DELETE FROM AppMetadata WHERE Key='XaiPendingIncidents.v2';" :
            "INSERT INTO AppMetadata(Key,Value) VALUES('XaiPendingIncidents.v2',$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
        if (value is not null) command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        internal readonly HashSet<string> Failed = [];
        internal bool AcceptsXml, HasUserAgent;
        internal string? RssOverride, OversizedUrl, BlockedUrl;
        internal readonly TaskCompletionSource Blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            lock (Requests) Requests.Add(url);
            if (Failed.Contains(url)) throw new HttpRequestException("Synthetic source failure");
            if (url == BlockedUrl)
            {
                Blocked.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            if (url == OversizedUrl)
            {
                var content = new ByteArrayContent([]);
                content.Headers.ContentLength = ProviderStatusClient.MaxResponseBytes + 1L;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }
            if (url == XaiStatusFeed.RssUrl) AcceptsXml = request.Headers.Accept.Any(item => item.MediaType == "application/rss+xml");
            HasUserAgent = request.Headers.UserAgent.ToString().Contains("AIBurgerClock/", StringComparison.Ordinal) && request.Headers.Authorization is null;
            string body = url switch
            {
                XaiStatusFeed.RssUrl => RssOverride ?? EmptyRss, XaiStatusFeed.SummaryUrl => SummaryJson,
                XaiStatusFeed.IncidentsUrl => IncidentsJson,
                _ => throw new InvalidOperationException("Unexpected URL")
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
        }
    }
}
