using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AiBurgerClock;

// One instance per status client, called serially by StatusMonitor. Only unresolved
// public incident identities are retained; a missing item is not an explicit resolution.
internal sealed class XaiStatusFeed
{
    internal const string RssUrl = "https://status.x.ai/feed.xml";
    internal const string SummaryUrl = "https://status.x.ai/api/compat/summary.json";
    internal const string ComponentsUrl = "https://status.x.ai/api/compat/components.json";
    internal const string IncidentsUrl = "https://status.x.ai/api/compat/incidents.json";
    internal const string HealthyReason = "정상 · 진행 중 장애 없음(공식 사건 피드 기준)";
    private const int MaximumTrackedIncidents = 256;
    private readonly Dictionary<string, string> unresolved = new(StringComparer.Ordinal);

    internal void Restore(ProviderStatus cached)
    {
        if (cached.Provider != ProviderKind.Xai || cached.Status == OfficialStatus.Operational) return;
        string ids = cached.IncidentId;
        if (cached.AssessmentIssue.Contains("해결 표시 없이 소실", StringComparison.Ordinal)) ids += "," + cached.UncertainIncidentId;
        foreach (string id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaximumTrackedIncidents))
            unresolved[id] = cached.IncidentTitle.Length > 0 ? cached.IncidentTitle : cached.UncertainIncidentTitle;
    }

    // Public service paths and Cursor compatibility IDs checked on 2026-10-27.
    // In particular Cursor CLI is not Grok Build, and BugBot is not Grok Bot.
    private static readonly Dictionary<string, string> Services = new(StringComparer.Ordinal)
    {
        ["grok-com"] = "Grok · grok.com", ["ios-app"] = "Grok · iOS", ["android-app"] = "Grok · Android",
        ["grok-in-x"] = "Grok · X", ["grok-build"] = "Grok Build", ["voice"] = "Grok · Voice",
        ["office-workspace-plugins"] = "Grok · Office/Workspace Plugins",
        ["api-global"] = "Grok API · Global", ["api-us"] = "Grok API · US",
        ["cursor-desktop"] = "Cursor · Desktop", ["cursor-ios"] = "Cursor · iOS",
        ["cursor-cli"] = "Cursor · CLI", ["cloud-agents"] = "Cursor · Cloud Agents",
        ["automations"] = "Cursor · Automations", ["bugbot"] = "Cursor · BugBot",
        ["review-agents"] = "Cursor · Review Agents", ["cursor-com"] = "Cursor · cursor.com",
        ["origin"] = "Cursor · Origin", ["enterprise-dashboard"] = "Cursor · Enterprise Dashboard",
        ["grok-bot"] = "Grok Bot"
    };

    private static readonly Dictionary<string, string> Components = new(StringComparer.Ordinal)
    {
        ["cursor-ios"] = "cursor-ios", ["rflc60xp5jp2"] = "cursor-desktop",
        ["mwv1g9sc7kdh"] = "cloud-agents", ["vsny1qv7v86c"] = "cursor-cli",
        ["k0trcq273dr6"] = "automations", ["bugbot"] = "bugbot", ["2x2chyqwmkzl"] = "review-agents",
        ["sm5wkcnqkvr9"] = "grok-bot", ["jh0714rgjgt4"] = "cursor-com", ["xwjpvdf81qh9"] = "origin",
        ["enterprise-dashboard"] = "enterprise-dashboard"
    };

    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Header = new(@"^Status:\s*([A-Za-z_]+)\s+Severity:\s*([A-Za-z_]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private sealed record Problem(OfficialStatus Status, string Service, string Id, string Title);
    private sealed class Reading
    {
        internal readonly List<Problem> Problems = [];
        internal readonly HashSet<string> Issues = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> Uncertain = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> Active = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Resolved = new(StringComparer.Ordinal);
        internal readonly HashSet<string> ComponentIds = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, OfficialStatus> ComponentStates = new(StringComparer.Ordinal);
    }

    internal ProviderStatus Parse(string? rss, string? summary, string? components, string? incidents, DateTimeOffset now)
    {
        var reading = new Reading();
        Read(rss, "Grok RSS", value => ReadRss(value, reading));
        Read(summary, "Cursor summary", value => ReadJson(value, reading, catalog: true, history: true, summary: true));
        Read(components, "Cursor components", value => ReadJson(value, reading, catalog: true, history: false));
        Read(incidents, "Cursor incidents", value => ReadJson(value, reading, catalog: false, history: true));
        if (Components.Keys.Any(id => !reading.ComponentIds.Contains(id)))
            reading.Issues.Add("Cursor/Grok Bot 필수 구성 요소 누락");

        foreach (string id in reading.Active.Keys.Intersect(reading.Resolved))
            reading.Issues.Add("동일 사건의 진행/해결 상태가 출처 간 상충");
        foreach (string id in reading.Resolved.Except(reading.Active.Keys)) unresolved.Remove(id);
        foreach (var previous in unresolved)
            if (!reading.Active.ContainsKey(previous.Key))
            {
                reading.Issues.Add("이전 미해결 사건이 해결 표시 없이 소실됨");
                reading.Uncertain[previous.Key] = previous.Value;
            }
        foreach (var current in reading.Active)
        {
            if (unresolved.Count >= MaximumTrackedIncidents && !unresolved.ContainsKey(current.Key))
                reading.Issues.Add("미해결 사건 추적 한도 초과");
            else unresolved[current.Key] = current.Value;
        }

        string issue = Clean(string.Join(" · ", reading.Issues), 1000);
        // Only confirmed statuses enter this severity ordering; Unknown is never a fabricated outage.
        var problems = reading.Problems.Distinct().OrderByDescending(item => item.Status).ThenBy(item => item.Service, StringComparer.Ordinal).ToArray();
        string scope = Clean(string.Join(", ", problems.Select(item => item.Service).Distinct()), 500);
        string ids = Clean(string.Join(",", problems.Select(item => item.Id).Where(id => id.Length > 0).Distinct().Order(StringComparer.Ordinal)), 500);
        string titles = Clean(string.Join(" | ", problems.Select(item => item.Title).Where(title => title.Length > 0).Distinct()), 500);
        OfficialStatus status = problems.Length > 0 ? problems[0].Status : issue.Length > 0 ? OfficialStatus.Unknown : OfficialStatus.Operational;
        string reason = problems.Length > 0 ? scope + " · " + RecommendationPolicy.OfficialLabel(status)
            : issue.Length > 0 ? issue : HealthyReason;
        return new(ProviderKind.Xai, status, now, now, Clean(reason, 180), scope, ids, titles,
            string.Join(" + ", new[] { (RssUrl, rss), (SummaryUrl, summary), (ComponentsUrl, components), (IncidentsUrl, incidents) }
                .Select(item => item.Item1 + (item.Item2 is null ? " [수신 실패]" : " [응답 수신]"))))
        {
            AssessmentIssue = issue,
            UncertainIncidentId = Clean(string.Join(",", reading.Uncertain.Keys.Order(StringComparer.Ordinal)), 500),
            UncertainIncidentTitle = Clean(string.Join(" | ", reading.Uncertain.Values.Distinct()), 500)
        };

        void Read(string? value, string label, Action<string> parse)
        {
            if (value is null) { reading.Issues.Add(label + " 응답 수신 실패"); return; }
            try { parse(value); }
            catch (Exception error) when (error is JsonException or XmlException or InvalidDataException)
            { reading.Issues.Add(label + " 응답 형식 이상"); }
        }
    }

    private static void ReadRss(string text, Reading reading)
    {
        using var input = new StringReader(text);
        using var xml = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = ProviderStatusClient.MaxResponseBytes
        });
        var root = XDocument.Load(xml).Root;
        var channel = root?.Element("channel");
        if (root?.Name != "rss" || (string?)root.Attribute("version") != "2.0" || channel is null ||
            string.IsNullOrWhiteSpace((string?)channel.Element("title"))) throw new InvalidDataException();
        foreach (var item in channel.Elements("item"))
        {
            string id = ((string?)item.Element("guid") ?? "").Trim();
            string title = Clean((string?)item.Element("title") ?? "", 500);
            string description = Clean(Tags.Replace(WebUtility.HtmlDecode((string?)item.Element("description") ?? ""), " "), 100_000);
            var header = Header.Match(description);
            var categories = item.Elements("category").Select(value => value.Value.Trim().ToLowerInvariant()).ToArray();
            string state = header.Success ? header.Groups[1].Value.ToLowerInvariant() : "";
            string severity = header.Success ? header.Groups[2].Value.ToLowerInvariant() : "";
            if (id.Length is 0 or > 128 || title.Length == 0 || !header.Success ||
                !categories.Contains(state) || !categories.Contains(severity))
            { reading.Issues.Add("RSS 사건 필수 필드/상태 형식 이상"); continue; }
            if (!Uri.TryCreate((string?)item.Element("link"), UriKind.Absolute, out var link) ||
                link.Scheme != "https" || link.Host != "status.x.ai" || !link.IsDefaultPort ||
                link.UserInfo.Length > 0 || link.Query.Length > 0 || link.Fragment.Length > 0)
            { Uncertain(reading, id, title, "RSS 사건의 제품 경로를 확인할 수 없음"); continue; }
            string[] segments = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 2 || segments[1] != id || !Services.TryGetValue(segments[0], out var service))
            {
                if (state != "resolved") Uncertain(reading, id, title, "RSS 진행 사건의 제품 범위를 확인할 수 없음");
                continue;
            }
            if (state == "resolved") { reading.Resolved.Add(id); continue; }
            if (state is not ("investigating" or "identified" or "monitoring" or "ongoing"))
            { Uncertain(reading, id, title, "RSS 진행 상태 형식 이상"); continue; }
            var status = severity switch
            {
                "info" => OfficialStatus.Degraded,
                "disruption" => OfficialStatus.PartialOutage,
                "outage" => OfficialStatus.MajorOutage,
                _ => OfficialStatus.Unknown
            };
            Add(reading, status, service, id, title);
        }
    }

    private static void ReadJson(string text, Reading reading, bool catalog, bool history, bool summary = false)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        if (summary && (!root.TryGetProperty("status", out var overall) ||
            Text(overall, "indicator") is not ("none" or "minor" or "major" or "critical" or "maintenance")))
            reading.Issues.Add("Cursor summary 전체 상태 형식 이상");
        if (catalog)
        {
            var items = Array(root, "components");
            foreach (var item in items.EnumerateArray())
            {
                string id = Text(item, "id"), name = Text(item, "name"), value = Text(item, "status");
                if (id.Length == 0 || name.Length == 0 || value.Length == 0)
                { reading.Issues.Add("Cursor 구성 요소 필수 필드 누락"); continue; }
                if (!Components.TryGetValue(id, out var path))
                {
                    if (value != "operational") reading.Issues.Add("상태가 정상으로 확인되지 않은 Cursor 구성 요소의 범위 미확인");
                    continue;
                }
                reading.ComponentIds.Add(id);
                var status = value switch
                {
                    "operational" => OfficialStatus.Operational,
                    "degraded_performance" or "under_maintenance" => OfficialStatus.Degraded,
                    "partial_outage" => OfficialStatus.PartialOutage,
                    "major_outage" => OfficialStatus.MajorOutage,
                    _ => OfficialStatus.Unknown
                };
                if (reading.ComponentStates.TryGetValue(id, out var previous) && previous != status)
                    reading.Issues.Add("Cursor 구성 요소 상태가 출처 간 상충");
                reading.ComponentStates[id] = status;
                if (status == OfficialStatus.Unknown) reading.Issues.Add("Cursor 구성 요소 상태 형식 이상");
                else if (status != OfficialStatus.Operational) reading.Problems.Add(new(status, Services[path], "", ""));
            }
        }
        if (!history) return;
        foreach (var item in Array(root, "incidents").EnumerateArray())
        {
            string id = Text(item, "id"), title = Clean(Text(item, "name"), 500), state = Text(item, "status");
            if (id.Length is 0 or > 128 || state.Length == 0)
            { reading.Issues.Add("Cursor 사건 필수 필드 누락"); continue; }
            if (state is "resolved" or "postmortem" or "completed") { reading.Resolved.Add(id); continue; }
            if (state is not ("investigating" or "identified" or "monitoring") || title.Length == 0)
            { Uncertain(reading, id, title, "Cursor 진행 사건 상태 형식 이상"); continue; }
            if (!item.TryGetProperty("components", out var links) || links.ValueKind != JsonValueKind.Array || links.GetArrayLength() == 0)
            { Uncertain(reading, id, title, "Cursor 진행 사건의 제품 범위를 확인할 수 없음"); continue; }
            var status = Text(item, "impact") switch
            {
                "none" or "minor" => OfficialStatus.Degraded,
                "major" => OfficialStatus.PartialOutage,
                "critical" => OfficialStatus.MajorOutage,
                _ => OfficialStatus.Unknown
            };
            foreach (var link in links.EnumerateArray())
            {
                string component = link.ValueKind == JsonValueKind.String ? link.GetString() ?? "" : Text(link, "id");
                if (Components.TryGetValue(component, out var path)) Add(reading, status, Services[path], id, title);
                else Uncertain(reading, id, title, "Cursor 진행 사건에 범위 미확인 구성 요소가 있음");
            }
        }
    }

    private static void Add(Reading reading, OfficialStatus status, string service, string id, string title)
    {
        if (status == OfficialStatus.Unknown) { Uncertain(reading, id, title, "진행 사건 심각도 형식 이상"); return; }
        reading.Problems.Add(new(status, service, id, title));
        reading.Active[id] = title;
    }

    private static void Uncertain(Reading reading, string id, string title, string issue)
    { reading.Issues.Add(issue); reading.Uncertain[id] = title; }

    private static JsonElement Array(JsonElement value, string name) =>
        value.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array ? items : throw new InvalidDataException();
    private static string Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
    private static string Clean(string value, int length)
    { string clean = Space.Replace(value, " ").Trim(); return clean.Length <= length ? clean : clean[..length]; }
}
