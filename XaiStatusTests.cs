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
        ("cursor-ios", "Cursor IDE (iOS)"), ("rflc60xp5jp2", "IDE"), ("mwv1g9sc7kdh", "Cloud Agents"),
        ("vsny1qv7v86c", "CLI"), ("k0trcq273dr6", "Automations"), ("bugbot", "BugBot"),
        ("2x2chyqwmkzl", "Review Agents"), ("sm5wkcnqkvr9", "Grok Bot"), ("jh0714rgjgt4", "cursor.com"),
        ("xwjpvdf81qh9", "Origin"), ("enterprise-dashboard", "Enterprise Dashboard")
    ];
    internal static string ComponentsJson => Components();
    internal static string SummaryJson => Summary();
    internal const string IncidentsJson = "{\"incidents\":[]}";

    private static object[] ComponentItems(string? omit = null, string? changed = null, string value = "operational") =>
        Catalog.Where(item => item.Id != omit).Select(item => (object)new
            { id = item.Id, name = item.Name, status = item.Id == changed ? value : "operational" }).ToArray();
    private static string Components(string? omit = null, string? changed = null, string value = "operational") =>
        JsonSerializer.Serialize(new { components = ComponentItems(omit, changed, value) });
    private static string Summary(string? omit = null, string? changed = null, string value = "operational", object[]? incidents = null) =>
        JsonSerializer.Serialize(new { status = new { indicator = "none" }, components = ComponentItems(omit, changed, value), incidents = incidents ?? [] });
    private static object Incident(string id = "synthetic-incident", string state = "investigating", string impact = "major", params string[] components) =>
        new { id, name = "Synthetic issue", status = state, impact, components = components.Select(component => new { id = component }).ToArray() };
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
        ProviderStatus Parse(string rss = EmptyRss, string? summary = null, string? components = null, string? history = null) =>
            new XaiStatusFeed().Parse(rss, summary ?? SummaryJson, components ?? ComponentsJson, history ?? IncidentsJson, Now);

        var healthy = Parse();
        Check(healthy.Status == OfficialStatus.Operational && healthy.Reason == XaiStatusFeed.HealthyReason,
            "empty valid RSS plus operational Cursor catalog is healthy under the approved incident-feed rule");
        Check(RecommendationPolicy.Calculate(AgentState.FullThrottle, healthy.Status) == Recommendation.Go,
            "incident-free xAI is GO, not permanently gray");
        Check(healthy.LastSuccessfulCheckUtc == Now && healthy.AssessmentIssue.Length == 0, "normal receipt uses the injected clock");
        Check(Enum.GetValues<QuotaProvider>().Length == 3 && ProviderNames.Provider(ProviderKind.Xai) == "xAI", "xAI status adds no quota provider");
        Check(ProviderStatusPages.For(ProviderKind.Xai).AbsoluteUri == "https://status.x.ai/" &&
            ProviderStatusPages.XaiUsage.AbsoluteUri == "https://grok.com/dashboard", "fixed official status and usage destinations");

        (string Path, string Label)[] mappings =
        [
            ("grok-com", "Grok · grok.com"), ("ios-app", "Grok · iOS"), ("android-app", "Grok · Android"),
            ("grok-in-x", "Grok · X"), ("grok-build", "Grok Build"), ("voice", "Grok · Voice"),
            ("office-workspace-plugins", "Grok · Office/Workspace Plugins"), ("api-global", "Grok API · Global"), ("api-us", "Grok API · US"),
            ("cursor-desktop", "Cursor · Desktop"), ("cursor-ios", "Cursor · iOS"), ("cursor-cli", "Cursor · CLI"),
            ("cloud-agents", "Cursor · Cloud Agents"), ("automations", "Cursor · Automations"), ("bugbot", "Cursor · BugBot"),
            ("review-agents", "Cursor · Review Agents"), ("cursor-com", "Cursor · cursor.com"), ("origin", "Cursor · Origin"),
            ("enterprise-dashboard", "Cursor · Enterprise Dashboard"), ("grok-bot", "Grok Bot")
        ];
        foreach (var (path, label) in mappings)
        {
            var status = Parse(Rss(Item(path)));
            Check(status.Status == OfficialStatus.PartialOutage && status.RelevantComponent == label, "RSS product mapping: " + path);
            Check(status.IncidentId == "synthetic-incident", "public incident ID preserved: " + path);
        }
        foreach (var item in Catalog)
        {
            var status = Parse(components: Components(changed: item.Id, value: "degraded_performance"));
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
            Summary(incidents: [Incident(components: ["sm5wkcnqkvr9", "vsny1qv7v86c"])]));
        Check(duplicate.IncidentId == "synthetic-incident" && duplicate.IncidentTitle == "[Synthetic product] Investigating synthetic issue | Synthetic issue",
            "same GUID in repeated products and JSON is deduplicated by episode without discarding titles");
        Check(duplicate.RelevantComponent.Contains("Grok Bot", StringComparison.Ordinal) && duplicate.RelevantComponent.Contains("Cursor · CLI", StringComparison.Ordinal),
            "duplicate episode retains all affected products");
        var worst = Parse(Rss(Item("grok-build", severity: "outage"), Item("grok-bot", id: "synthetic-second")),
            components: Components(changed: "vsny1qv7v86c", value: "degraded_performance"));
        Check(worst.Status == OfficialStatus.MajorOutage && worst.RelevantComponent.Contains("Grok Build", StringComparison.Ordinal) &&
            worst.RelevantComponent.Contains("Grok Bot", StringComparison.Ordinal) && worst.RelevantComponent.Contains("Cursor · CLI", StringComparison.Ordinal), "worst status with all product causes");
        var titleOnly = Parse(summary: Summary(incidents: [Incident(components: [])]));
        Check(titleOnly.Status == OfficialStatus.Unknown && titleOnly.IncidentId.Length == 0 && titleOnly.UncertainIncidentId.Length > 0,
            "unscoped ongoing event is CHECK, not a fabricated all-product outage");
        var partlyScoped = Parse(summary: Summary(incidents: [Incident(components: ["sm5wkcnqkvr9", "future-component"])]));
        Check(partlyScoped.Status == OfficialStatus.PartialOutage && partlyScoped.AssessmentIssue.Length > 0, "known affected product wins alongside uncertain scope");
        var resolvedJson = Parse(history: JsonSerializer.Serialize(new { incidents = new[] { Incident(state: "resolved", impact: "critical") } }));
        Check(resolvedJson.Status == OfficialStatus.Operational, "resolved JSON event without a scope is historical only");
        var conflicting = Parse(Rss(Item()), history: JsonSerializer.Serialize(new { incidents = new[] { Incident(state: "resolved") } }));
        Check(conflicting.Status == OfficialStatus.PartialOutage && conflicting.AssessmentIssue.Contains("상충", StringComparison.Ordinal),
            "resolved observation cannot erase a still-open observation from another source");
        var oppositeConflict = Parse(Rss(Item(state: "resolved")), Summary(incidents: [Incident(components: ["vsny1qv7v86c"])]));
        Check(oppositeConflict.Status == OfficialStatus.PartialOutage && oppositeConflict.AssessmentIssue.Length > 0, "cross-source conflict is symmetric");
        Check(Parse(summary: Summary(changed: "vsny1qv7v86c", value: "major_outage")).Status == OfficialStatus.MajorOutage,
            "healthy complete catalog cannot erase a summary component outage");
        Check(Parse(components: Components(changed: "vsny1qv7v86c", value: "major_outage")).Status == OfficialStatus.MajorOutage,
            "healthy summary cannot erase a complete-catalog component outage");

        foreach (string id in Catalog.Select(item => item.Id))
        {
            Check(Parse(summary: Summary(omit: id), components: Components(omit: id)).Status == OfficialStatus.Unknown, "missing expected component is CHECK: " + id);
            Check(Parse(summary: Summary(omit: id)).Status == OfficialStatus.Operational, "complete catalog supplements summary omission: " + id);
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
            Check(Parse(components: bad).Status == OfficialStatus.Unknown, "bad catalog receipt is CHECK");
            Check(Parse(Rss(Item(severity: "outage")), components: bad).Status == OfficialStatus.MajorOutage, "confirmed RSS outage survives a malformed catalog");
        }
        Check(Parse(components: Components(changed: "sm5wkcnqkvr9", value: "new-state")).Status == OfficialStatus.Unknown, "unknown expected component state cannot be green");

        var tracker = new XaiStatusFeed();
        var notifications = new RecommendationNotifications();
        bool Notice(ProviderStatus status) => notifications.Observe(ProviderKind.Xai, AgentState.FullThrottle,
            RecommendationPolicy.Calculate(AgentState.FullThrottle, status.Status), true);
        var baseline = tracker.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now);
        Check(!Notice(baseline), "first normal observation has no alert");
        var open = tracker.Parse(Rss(Item("grok-bot")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(5));
        Check(Notice(open), "confirmed issue after GO produces one work recommendation");
        for (int poll = 2; poll <= 4; poll++)
        {
            var missing = tracker.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(poll * 5));
            Check(missing.Status == OfficialStatus.Unknown && missing.UncertainIncidentId == "synthetic-incident", "unresolved disappearance stays uncertain on every later poll");
            Check(!Notice(missing), "disappearance cannot send a false recovery");
        }
        var closed = tracker.Parse(Rss(Item("grok-bot", state: "resolved")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(25));
        Check(closed.Status == OfficialStatus.Operational && Notice(closed), "explicit resolution releases the tracked incident and sends one recovery");
        Check(!Notice(tracker.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(30))), "resolved episode can age out without duplicate recovery");
        var restarted = new XaiStatusFeed();
        restarted.Restore(open);
        var missingAfterRestart = restarted.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(2));
        Check(missingAfterRestart.Status == OfficialStatus.Unknown && missingAfterRestart.UncertainIncidentId == open.IncidentId,
            "cached unresolved identity is retained across restart instead of a fabricated recovery");
        var secondRestart = new XaiStatusFeed();
        secondRestart.Restore(missingAfterRestart);
        Check(secondRestart.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(3)).Status == OfficialStatus.Unknown,
            "cached disappearance diagnostics also retain the unresolved identity across another restart");
        Check(restarted.Parse(Rss(Item("grok-bot", state: "resolved")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(3)).Status == OfficialStatus.Operational,
            "explicit resolution releases an identity restored from cache");
        var longIds = Enumerable.Range(0, 4).Select(index => new string((char)('a' + index), 128)).ToArray();
        var longTracker = new XaiStatusFeed();
        _ = longTracker.Parse(Rss(longIds.Select(id => Item(id: id)).ToArray()), SummaryJson, ComponentsJson, IncidentsJson, Now);
        var longMissing = longTracker.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(5));
        Check(longMissing.PendingXaiIncidentIds.Length == 515 && longMissing.UncertainIncidentId.Length == 500,
            "full pending identities remain distinct from truncated visible diagnostics");
        var longRestart = new XaiStatusFeed(); longRestart.Restore(longMissing);
        Check(longRestart.Parse(Rss(longIds.Select(id => Item(id: id, state: "resolved")).ToArray()), SummaryJson, ComponentsJson,
            IncidentsJson, Now.AddMinutes(10)).Status == OfficialStatus.Operational,
            "truncated display ID never creates a phantom event after restart and explicit resolution");
        CheckMissingIncidentRelease(Check);
        CheckMissingIncidentExpiry(Check);
        var detail = StatusPanelModel.Card(baseline, AgentState.FullThrottle);
        Check(detail.Detail.Contains("공식 사건 피드 기준", StringComparison.Ordinal) && detail.Detail.Contains("사용량 보기: https://grok.com/dashboard", StringComparison.Ordinal), "shared detail declares incident-feed scope and usage link only");
        Check(TrayPresentation.ProviderNotification(ProviderKind.Xai, Recommendation.Stop, open.Reason, ProviderNames.Provider).Body.Contains("Grok Bot", StringComparison.Ordinal), "problem notification includes affected product");
        Check(TrayPresentation.ProviderNotification(ProviderKind.Xai, Recommendation.Go, closed.Reason, ProviderNames.Provider).Body.Contains("Grok·Cursor·Grok Bot", StringComparison.Ordinal), "recovery notification names the product scope without claiming runtime availability");

        for (int missing = 0; missing < 4; missing++)
        {
            string?[] inputs = [EmptyRss, SummaryJson, ComponentsJson, IncidentsJson]; inputs[missing] = null;
            var partial = new XaiStatusFeed().Parse(inputs[0], inputs[1], inputs[2], inputs[3], Now);
            Check(partial.Status == OfficialStatus.Unknown && partial.AssessmentIssue.Contains("수신 실패", StringComparison.Ordinal), "each required source failure is CHECK");
            Check(partial.LastSuccessfulCheckUtc == Now && partial.Source.Contains("[수신 실패]", StringComparison.Ordinal), "partial receipt is distinct from total transport failure");
        }
        var partialOutage = new XaiStatusFeed().Parse(Rss(Item()), SummaryJson, null, IncidentsJson, Now);
        Check(partialOutage.Status == OfficialStatus.PartialOutage && partialOutage.AssessmentIssue.Length > 0, "fresh confirmed RSS problem wins over partial transport failure");
        using var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        var client = new ProviderStatusClient(http, () => Now);
        var fetched = await client.FetchAsync(ProviderKind.Xai, CancellationToken.None);
        Check(fetched.Status == OfficialStatus.Operational && handler.Requests.Count == 4 && handler.Requests.Distinct().Count() == 4, "one xAI poll reads only four fixed official public URLs");
        Check(handler.AcceptsXml && handler.HasUserAgent, "RSS Accept and versioned User-Agent are set without cookies or auth");
        handler.Failed.Add(XaiStatusFeed.ComponentsUrl);
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
        using (var partialHandler = new FakeHandler { RssOverride = Rss(Item("grok-bot")), OversizedUrl = XaiStatusFeed.ComponentsUrl })
        using (var partialHttp = new HttpClient(partialHandler))
        {
            var partial = await new ProviderStatusClient(partialHttp, () => Now).FetchAsync(ProviderKind.Xai, CancellationToken.None);
            Check(partial.Status == OfficialStatus.PartialOutage && partial.AssessmentIssue.Contains("수신 실패", StringComparison.Ordinal),
                "one oversized body never erases a completed RSS outage");
        }
        using (var partialHandler = new FakeHandler { RssOverride = Rss(Item("grok-bot")), BlockedUrl = XaiStatusFeed.ComponentsUrl })
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

    private static void CheckMissingIncidentRelease(Action<bool, string> check)
    {
        foreach (var (source, phase) in new[] { ("RSS", "resolved"), ("summary", "resolved"),
            ("incidents", "resolved"), ("incidents", "postmortem"), ("incidents", "completed") })
        foreach (bool restart in new[] { false, true })
        {
            string label = $"{source}/{phase}, restart={restart}: ";
            var tracker = new XaiStatusFeed();
            _ = tracker.Parse(Rss(Item()), SummaryJson, ComponentsJson, IncidentsJson, Now);
            var missing = tracker.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(5));
            check(missing.Status == OfficialStatus.Unknown && missing.PendingXaiIncidentIds == "synthetic-incident", label + "disappearance enters CHECK");
            if (restart) { tracker = new XaiStatusFeed(); tracker.Restore(missing); }
            var late = tracker.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(23));
            check(late.Status == OfficialStatus.Unknown && late.PendingXaiIncidentIds == "synthetic-incident", label + "elapsed time before 24 hours and restart retain the missing identity");
            var unrelatedResolution = tracker.Parse(Rss(Item(id: "synthetic-other", state: "resolved")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(23).AddMinutes(5));
            check(unrelatedResolution.Status == OfficialStatus.Unknown && unrelatedResolution.PendingXaiIncidentIds == "synthetic-incident", label + "another incident resolution cannot clear CHECK");
            var next = tracker.Parse(Rss(Item("cursor-cli", id: "synthetic-next")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(23).AddMinutes(10));
            check(next.Status == OfficialStatus.PartialOutage && next.UncertainIncidentId == "synthetic-incident" && next.PendingXaiIncidentIds.Contains("synthetic-next", StringComparison.Ordinal), label + "new issue takes severity priority without forgetting the missing incident");
            var nextResolved = tracker.Parse(Rss(Item("cursor-cli", id: "synthetic-next", state: "resolved")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(23).AddMinutes(15));
            check(nextResolved.Status == OfficialStatus.Unknown && nextResolved.PendingXaiIncidentIds == "synthetic-incident", label + "resolving the new incident returns to the original CHECK");
            var reappeared = tracker.Parse(Rss(Item()), SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(23).AddMinutes(20));
            check(reappeared.Status == OfficialStatus.PartialOutage && reappeared.PendingXaiIncidentIds == "synthetic-incident", label + "same incident reappearing as active is an outage rather than a recovery");
            string resolved = JsonSerializer.Serialize(new { incidents = new[] { Incident(state: phase) } });
            var closed = tracker.Parse(source == "RSS" ? Rss(Item(state: phase)) : EmptyRss,
                source == "summary" ? Summary(incidents: [Incident(state: phase)]) : SummaryJson, ComponentsJson,
                source == "incidents" ? resolved : IncidentsJson, Now.AddHours(23).AddMinutes(25));
            check(closed.Status == OfficialStatus.Operational && closed.PendingXaiIncidentIds.Length == 0 && closed.AssessmentIssue.Length == 0, label + "matching explicit resolution releases CHECK on that poll");
            var after = tracker.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddHours(23).AddMinutes(30));
            check(after.Status == OfficialStatus.Operational && after.PendingXaiIncidentIds.Length == 0, label + "a resolved incident may leave the feed without recreating CHECK");
        }

        var conflict = new XaiStatusFeed();
        _ = conflict.Parse(Rss(Item()), SummaryJson, ComponentsJson, IncidentsJson, Now);
        var opposed = conflict.Parse(Rss(Item(state: "resolved")), Summary(incidents: [Incident(components: ["sm5wkcnqkvr9"])]), ComponentsJson, IncidentsJson, Now.AddMinutes(5));
        check(opposed.Status == OfficialStatus.PartialOutage && opposed.PendingXaiIncidentIds == "synthetic-incident", "active observation blocks matching resolution from another source");
        check(conflict.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(10)).Status == OfficialStatus.Unknown, "a conflicted resolution cannot silently expire when both observations disappear");
        check(conflict.Parse(Rss(Item(state: "resolved")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(15)).Status == OfficialStatus.Operational, "later unopposed matching resolution clears the conflict");

        var multiple = new XaiStatusFeed();
        _ = multiple.Parse(Rss(Item(), Item("grok-bot", id: "synthetic-second")), SummaryJson, ComponentsJson, IncidentsJson, Now);
        var one = multiple.Parse(Rss(Item(state: "resolved")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(5));
        check(one.Status == OfficialStatus.Unknown && one.PendingXaiIncidentIds == "synthetic-second", "each missing incident must be resolved independently");
        check(multiple.Parse(Rss(Item("grok-bot", id: "synthetic-second", state: "resolved")), SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(10)).Status == OfficialStatus.Operational, "resolving the final missing incident releases CHECK");

        var partial = new XaiStatusFeed();
        _ = partial.Parse(Rss(Item()), SummaryJson, ComponentsJson, IncidentsJson, Now);
        var receivedResolution = partial.Parse(Rss(Item(state: "resolved")), SummaryJson, null, IncidentsJson, Now.AddMinutes(5));
        check(receivedResolution.Status == OfficialStatus.Unknown && receivedResolution.PendingXaiIncidentIds.Length == 0 && receivedResolution.AssessmentIssue.Contains("수신 실패", StringComparison.Ordinal), "matching resolution clears the incident but a separate source failure still requires CHECK");
        check(partial.Parse(EmptyRss, SummaryJson, ComponentsJson, IncidentsJson, Now.AddMinutes(10)).Status == OfficialStatus.Operational, "healthy receipt clears the remaining source failure without reviving a resolved incident");
    }

    private const string ExpiryNote = "해결 표시 없이 피드에서 사라짐(24시간 경과로 해제)";

    private static ProviderStatus ReadAt(XaiStatusFeed tracker, DateTimeOffset at, string rss = EmptyRss) =>
        tracker.Parse(rss, SummaryJson, ComponentsJson, IncidentsJson, at);

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
        (string Label, string Rss, string Summary)[] readings =
        [
            ("RSS category/header mismatch", Rss(mismatched), SummaryJson),
            ("RSS missing title", Rss(untitled), SummaryJson),
            ("JSON empty status", EmptyRss, Summary(incidents: [Incident(state: "", components: ["sm5wkcnqkvr9"])]))
        ];
        foreach (var reading in readings)
        {
            var tracker = new XaiStatusFeed();
            _ = ReadAt(tracker, Now, Rss(Item()));
            _ = ReadAt(tracker, firstMissing);
            var malformed = tracker.Parse(reading.Rss, reading.Summary, ComponentsJson, IncidentsJson, firstMissing.AddHours(12));
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
        var invalid = new List<(string Label, string? Rss, string? Summary, string? Components, string? History)>();
        for (int index = 0; index < 4; index++)
        {
            string?[] inputs = [EmptyRss, SummaryJson, ComponentsJson, IncidentsJson]; inputs[index] = null;
            invalid.Add(("missing source " + index, inputs[0], inputs[1], inputs[2], inputs[3]));
            inputs = [EmptyRss, SummaryJson, ComponentsJson, IncidentsJson]; inputs[index] = index == 0 ? "<rss>" : "{bad";
            invalid.Add(("malformed source " + index, inputs[0], inputs[1], inputs[2], inputs[3]));
        }
        invalid.Add(("missing product scope", EmptyRss, Summary(omit: "sm5wkcnqkvr9"), Components(omit: "sm5wkcnqkvr9"), IncidentsJson));
        invalid.Add(("uncertain unrelated episode", Rss(Item("future-product", id: "synthetic-other")), SummaryJson, ComponentsJson, IncidentsJson));
        invalid.Add(("unknown component state", EmptyRss, SummaryJson, Components(changed: "sm5wkcnqkvr9", value: "future-state"), IncidentsJson));
        foreach (var input in invalid)
        {
            var tracker = new XaiStatusFeed();
            _ = ReadAt(tracker, Now, Rss(Item()));
            var invalidFirst = tracker.Parse(input.Rss, input.Summary, input.Components, input.History, Now.AddHours(1));
            check(invalidFirst.Status == OfficialStatus.Unknown && invalidFirst.PendingXaiIncidentIds == "synthetic-incident" && invalidFirst.XaiMissingIncidents.Count == 0,
                input.Label + ": invalid evidence cannot start a missing interval");
            DateTimeOffset firstMissing = Now.AddDays(2);
            var validFirst = ReadAt(tracker, firstMissing);
            check(validFirst.Status == OfficialStatus.Unknown && validFirst.XaiMissingIncidents.Single().MissingSinceUtc == firstMissing,
                input.Label + ": the next complete valid absence starts its full 24 hours");
            var invalidBoundary = tracker.Parse(input.Rss, input.Summary, input.Components, input.History, firstMissing.AddHours(24));
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
                XaiStatusFeed.ComponentsUrl => ComponentsJson, XaiStatusFeed.IncidentsUrl => IncidentsJson,
                _ => throw new InvalidOperationException("Unexpected URL")
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
        }
    }
}
