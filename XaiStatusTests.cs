using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

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
        return count;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        internal readonly HashSet<string> Failed = [];
        internal bool AcceptsXml, HasUserAgent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            lock (Requests) Requests.Add(url);
            if (Failed.Contains(url)) throw new HttpRequestException("Synthetic source failure");
            if (url == XaiStatusFeed.RssUrl) AcceptsXml = request.Headers.Accept.Any(item => item.MediaType == "application/rss+xml");
            HasUserAgent = request.Headers.UserAgent.ToString().Contains("AIBurgerClock/", StringComparison.Ordinal) && request.Headers.Authorization is null;
            string body = url switch
            {
                XaiStatusFeed.RssUrl => EmptyRss, XaiStatusFeed.SummaryUrl => SummaryJson,
                XaiStatusFeed.ComponentsUrl => ComponentsJson, XaiStatusFeed.IncidentsUrl => IncidentsJson,
                _ => throw new InvalidOperationException("Unexpected URL")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }
}
