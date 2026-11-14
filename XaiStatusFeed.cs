using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AiBurgerClock;

// One instance per status client, called serially by StatusMonitor. Only unresolved
// public incident identities are retained; missing items expire after 24 hours, which
// restores the feed baseline without claiming an explicit resolution.
internal sealed class XaiStatusFeed(Action<string>? diagnostic = null)
{
    internal const string RssUrl = "https://status.x.ai/feed.xml";
    internal const string SummaryUrl = "https://data.x.ai/status/summary.json";
    internal const string IncidentsUrl = "https://data.x.ai/status/incidents.json";
    internal const string HealthyReason = "정상 · 진행 중 장애 없음(공식 사건 피드 기준)";
    internal const string ExpiryNote = "해결 표시 없이 피드에서 사라짐(24시간 경과로 해제)";
    internal static readonly TimeSpan MissingGracePeriod = TimeSpan.FromHours(24);
    private const int MaximumTrackedIncidents = 256;
    private readonly Dictionary<string, string> unresolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> missingSince = new(StringComparer.Ordinal);
    private bool incompleteTracking;
    private string expiryNote = "";
    private bool recoveryUnconfirmed;
    private string[]? lastIgnoredComponents;

    internal void Restore(ProviderStatus cached)
    {
        if (cached.Provider != ProviderKind.Xai) return;
        expiryNote = cached.XaiIncidentExpiryNote;
        recoveryUnconfirmed = cached.XaiRecoveryUnconfirmed;
        if (cached.PendingXaiIncidentIds == "?") { incompleteTracking = true; return; }
        string ids = cached.PendingXaiIncidentIds;
        if (ids.Length == 0 && cached.Status != OfficialStatus.Operational)
        {
            ids = cached.IncidentId;
            if (cached.AssessmentIssue.Contains("해결 표시 없이 소실", StringComparison.Ordinal)) ids += "," + cached.UncertainIncidentId;
        }
        foreach (string id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaximumTrackedIncidents))
            unresolved[id] = cached.IncidentTitle.Length > 0 ? cached.IncidentTitle :
                cached.UncertainIncidentTitle.Length > 0 ? cached.UncertainIncidentTitle : "이전 xAI 미해결 사건";
        foreach (var missing in cached.XaiMissingIncidents)
            if (unresolved.ContainsKey(missing.Id) && missing.MissingSinceUtc <= cached.CheckedAtUtc)
                missingSince[missing.Id] = missing.MissingSinceUtc;
    }

    // Public service paths and native summary slugs checked on 2026-11-14.
    // In particular Cursor CLI is not Grok Build, and BugBot is not Grok Bot.
    private static readonly Dictionary<string, string> Services = new(StringComparer.Ordinal)
    {
        ["grok-com"] = "Grok · grok.com", ["ios-app"] = "Grok · iOS", ["android-app"] = "Grok · Android",
        ["grok-in-x"] = "Grok · X", ["grok-build"] = "Grok Build", ["voice"] = "Grok · Voice",
        ["office-workspace-plugins"] = "Grok · Office/Workspace Plugins",
        ["api-global"] = "Grok API · Global", ["api-us"] = "Grok API · US",
        ["cursor-desktop"] = "Cursor · Desktop", ["cursor-ios"] = "Cursor · iOS",
        ["cursor-cli"] = "Cursor · CLI", ["cloud-agents"] = "Cursor · Cloud Agents",
        ["automations"] = "Cursor · Automations", ["bugbot"] = "Cursor · Bugbot",
        ["third-party-models"] = "Cursor · Third-party models",
        ["review-agents"] = "Cursor · Review Agents", ["cursor-com"] = "Cursor · cursor.com",
        ["agent-sdk"] = "Cursor · Agent SDK",
        ["origin"] = "Cursor · Origin", ["enterprise-dashboard"] = "Cursor · Enterprise Dashboard",
        ["grok-bot"] = "Grok Bot"
    };

    // Recognized products outside coding/work recommendations, including incident tracking.
    private static readonly HashSet<string> ExcludedRssServices = new(StringComparer.Ordinal)
    { "imagine", "api-eu-west-1", "admin-apis", "api-console", "api-docs", "single-sign-on", "x-ai" };

    // Current in-scope summary IDs are product slugs, not the retired compatibility IDs.
    // RSS-only legacy services remain recognized without becoming required JSON entries.
    private static readonly HashSet<string> RequiredComponents = new(StringComparer.Ordinal)
    { "agent-sdk", "android-app", "api-global", "api-us", "automations", "cloud-agents", "cursor-cli", "cursor-com",
      "cursor-desktop", "cursor-ios", "enterprise-dashboard", "grok-bot", "grok-com", "grok-in-x", "ios-app", "origin",
      "review-agents", "third-party-models", "voice" };

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
        internal readonly HashSet<string> Seen = new(StringComparer.Ordinal);
        internal readonly HashSet<string> ComponentIds = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, OfficialStatus> ComponentStates = new(StringComparer.Ordinal);
        internal readonly HashSet<string> IgnoredComponents = new(StringComparer.Ordinal);
    }

    internal ProviderStatus Parse(string? rss, string? summary, string? incidents, DateTimeOffset now)
    {
        var reading = new Reading();
        Read(rss, "Grok RSS", value => ReadRss(value, reading));
        bool summaryRead = Read(summary, "xAI summary", value => ReadJson(value, reading, catalog: true));
        Read(incidents, "xAI incidents", value => ReadJson(value, reading, catalog: false));
        if (RequiredComponents.Any(slug => !reading.ComponentIds.Contains(slug)))
            reading.Issues.Add("xAI 필수 구성 요소 누락");
        string[] ignored = reading.IgnoredComponents.Order(StringComparer.Ordinal).ToArray();
        if (summaryRead && (lastIgnoredComponents is null || !ignored.SequenceEqual(lastIgnoredComponents)))
        {
            lastIgnoredComponents = ignored;
            try { diagnostic?.Invoke("xAI 판정 제외 구성 요소: " + (ignored.Length == 0 ? "없음" :
                string.Join(",", ignored.Take(32).Select(slug => Clean(slug, 80))) + (ignored.Length > 32 ? " [truncated]" : ""))); }
            catch (Exception) { /* Optional diagnostics cannot change a public-status assessment. */ }
        }

        foreach (string id in reading.Active.Keys.Intersect(reading.Resolved))
            reading.Issues.Add("동일 사건의 진행/해결 상태가 출처 간 상충");
        foreach (string id in reading.Resolved.Except(reading.Active.Keys))
        {
            unresolved.Remove(id);
            missingSince.Remove(id);
        }
        // Seeing the same identity, even with an uncertain shape, cancels absence.
        foreach (string id in reading.Seen) missingSince.Remove(id);
        bool absenceConfirmed = reading.Issues.Count == 0;
        var expired = new List<string>();
        foreach (var previous in unresolved)
        {
            if (reading.Seen.Contains(previous.Key)) continue;
            if (absenceConfirmed)
            {
                if (!missingSince.TryGetValue(previous.Key, out var since) || now < since)
                    missingSince[previous.Key] = now;
                else if (now - since >= MissingGracePeriod)
                    expired.Add(previous.Key);
            }
        }
        if (expired.Count > 0)
        {
            expiryNote = ExpiryNote;
            recoveryUnconfirmed = true;
            foreach (string id in expired) { unresolved.Remove(id); missingSince.Remove(id); }
        }
        foreach (var previous in unresolved)
            if (!reading.Active.ContainsKey(previous.Key) && !reading.Uncertain.ContainsKey(previous.Key))
            {
                reading.Issues.Add("이전 미해결 사건이 해결 표시 없이 소실됨");
                reading.Uncertain[previous.Key] = previous.Value;
            }
        foreach (var current in reading.Active)
        {
            if (unresolved.Count >= MaximumTrackedIncidents && !unresolved.ContainsKey(current.Key))
                incompleteTracking = true;
            else unresolved[current.Key] = current.Value;
        }
        if (incompleteTracking) reading.Issues.Add("미해결 사건 추적 범위를 확인할 수 없음");
        if (reading.Problems.Count > 0) recoveryUnconfirmed = false;
        string pending = string.Join(",", unresolved.Keys.Order(StringComparer.Ordinal));
        if (pending.Length > 2048 || incompleteTracking) pending = "?";

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
            string.Join(" + ", new[] { (RssUrl, rss), (SummaryUrl, summary), (IncidentsUrl, incidents) }
                .Select(item => item.Item1 + (item.Item2 is null ? " [수신 실패]" : " [응답 수신]"))))
        {
            PendingXaiIncidentIds = pending,
            XaiMissingIncidents = missingSince.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new XaiMissingIncident(item.Key, item.Value)).ToArray(),
            XaiIncidentExpiryNote = expiryNote,
            XaiRecoveryUnconfirmed = recoveryUnconfirmed,
            AssessmentIssue = issue,
            UncertainIncidentId = Clean(string.Join(",", reading.Uncertain.Keys.Order(StringComparer.Ordinal)), 500),
            UncertainIncidentTitle = Clean(string.Join(" | ", reading.Uncertain.Values.Distinct()), 500)
        };

        bool Read(string? value, string label, Action<string> parse)
        {
            if (value is null) { reading.Issues.Add(label + " 응답 수신 실패"); return false; }
            try { parse(value); return true; }
            catch (Exception error) when (error is JsonException or XmlException or InvalidDataException)
            { reading.Issues.Add(label + " 응답 형식 이상"); return false; }
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
            bool trustedLink = Uri.TryCreate((string?)item.Element("link"), UriKind.Absolute, out var link) &&
                link.Scheme == "https" && link.Host == "status.x.ai" && link.IsDefaultPort &&
                link.UserInfo.Length == 0 && link.Query.Length == 0 && link.Fragment.Length == 0;
            string[] segments = trustedLink ? link!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) : [];
            bool identifiedPath = segments.Length == 2 && segments[1] == id;
            // Ignore before Seen: an Imagine-only entry must not pin a work incident's expiry.
            if (id.Length is > 0 and <= 128 && identifiedPath && ExcludedRssServices.Contains(segments[0])) continue;
            if (id.Length is > 0 and <= 128) reading.Seen.Add(id);
            string title = Clean((string?)item.Element("title") ?? "", 500);
            string description = Clean(Tags.Replace(WebUtility.HtmlDecode((string?)item.Element("description") ?? ""), " "), 100_000);
            var header = Header.Match(description);
            var categories = item.Elements("category").Select(value => value.Value.Trim().ToLowerInvariant()).ToArray();
            string state = header.Success ? header.Groups[1].Value.ToLowerInvariant() : "";
            string severity = header.Success ? header.Groups[2].Value.ToLowerInvariant() : "";
            if (id.Length is 0 or > 128 || title.Length == 0 || !header.Success ||
                !categories.Contains(state) || !categories.Contains(severity))
            { reading.Issues.Add("RSS 사건 필수 필드/상태 형식 이상"); continue; }
            if (!trustedLink)
            { Uncertain(reading, id, title, "RSS 사건의 제품 경로를 확인할 수 없음"); continue; }
            if (!identifiedPath || !Services.TryGetValue(segments[0], out var service))
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

    private static void ReadJson(string text, Reading reading, bool catalog)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        if (catalog && (!root.TryGetProperty("status", out var overall) ||
            !StringField(overall, "indicator") || !StringField(overall, "description")))
            reading.Issues.Add("xAI summary 전체 상태 형식 이상");
        if (catalog)
        {
            var items = Array(root, "components");
            foreach (var item in items.EnumerateArray())
            {
                string id = Text(item, "id");
                if (id.Length == 0) { reading.Issues.Add("xAI 구성 요소 필수 필드 누락"); continue; }
                if (ExcludedRssServices.Contains(id)) continue;
                if (!RequiredComponents.Contains(id)) { reading.IgnoredComponents.Add(id); continue; }
                string service = Services[id];
                string name = Text(item, "name"), value = Text(item, "status");
                if (name.Length == 0 || value.Length == 0)
                { reading.Issues.Add("xAI 구성 요소 필수 필드 누락"); continue; }
                reading.ComponentIds.Add(id);
                var status = value switch
                {
                    "available" => OfficialStatus.Operational,
                    "info" => OfficialStatus.Degraded,
                    "disruption" => OfficialStatus.PartialOutage,
                    "outage" => OfficialStatus.MajorOutage,
                    _ => OfficialStatus.Unknown
                };
                if (reading.ComponentStates.TryGetValue(id, out var previous) && previous != status)
                    reading.Issues.Add("동일 xAI 구성 요소 상태가 상충");
                reading.ComponentStates[id] = status;
                if (status == OfficialStatus.Unknown)
                    reading.Issues.Add(value == "no_data" ? "xAI 구성 요소 데이터 없음" : "xAI 구성 요소 상태 형식 이상");
                else if (status != OfficialStatus.Operational) reading.Problems.Add(new(status, service, "", ""));
            }
            return; // summary.incidents is outside the page's summary schema.
        }
        foreach (var item in Array(root, "incidents").EnumerateArray())
        {
            string id = Text(item, "id"), title = Clean(Text(item, "name"), 500), state = Text(item, "status");
            bool addedSeen = id.Length is > 0 and <= 128 && reading.Seen.Add(id);
            var links = Array(item, "components");
            var services = new List<string>();
            bool unknownScope = links.GetArrayLength() == 0;
            foreach (var link in links.EnumerateArray())
            {
                string slug = Text(link, "slug");
                if (slug.Length == 0 || !StringField(link, "name")) { unknownScope = true; continue; }
                if (ExcludedRssServices.Contains(slug)) continue;
                if (Services.TryGetValue(slug, out var service)) services.Add(service);
                else unknownScope = true;
            }
            // Known out-of-scope products never pin or resolve an in-scope episode.
            if (services.Count == 0 && !unknownScope)
            { if (addedSeen) reading.Seen.Remove(id); continue; }
            if (id.Length is 0 or > 128 || state.Length == 0)
            { reading.Issues.Add("xAI 사건 필수 필드 누락"); continue; }
            if (!StringField(item, "name") || state is not ("investigating" or "monitoring" or "resolved") ||
                !TimestampField(item, "created_at") || !TimestampField(item, "updated_at") || !TimestampField(item, "resolved_at", nullable: true))
            { Uncertain(reading, id, title, "xAI 사건 필수 필드/상태 형식 이상"); continue; }
            foreach (var update in Array(item, "incident_updates").EnumerateArray())
                if (!StringField(update, "id") || !StringField(update, "body") ||
                    Text(update, "status") is not ("investigating" or "monitoring" or "resolved") || !TimestampField(update, "created_at"))
                    throw new InvalidDataException();
            var status = Text(item, "impact") switch
            {
                "none" => OfficialStatus.Degraded,
                "minor" or "major" => OfficialStatus.PartialOutage,
                "critical" => OfficialStatus.MajorOutage,
                _ => OfficialStatus.Unknown
            };
            if (status == OfficialStatus.Unknown) { Uncertain(reading, id, title, "진행 사건 심각도 형식 이상"); continue; }
            // Current top-level status/scope wins over old update text and affected_components.
            if (state == "resolved") { if (services.Count > 0) reading.Resolved.Add(id); continue; }
            if (title.Length == 0) { Uncertain(reading, id, title, "xAI 진행 사건 제목 없음"); continue; }
            if (unknownScope) Uncertain(reading, id, title, "xAI 진행 사건의 제품 범위를 확인할 수 없음");
            foreach (string service in services) Add(reading, status, service, id, title);
        }
    }

    private static bool StringField(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String;

    private static bool TimestampField(JsonElement value, string name, bool nullable = false) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && (nullable && field.ValueKind == JsonValueKind.Null ||
            field.ValueKind == JsonValueKind.String && (field.GetString()?.EndsWith('Z') ?? false) && field.TryGetDateTimeOffset(out _));

    private static void Add(Reading reading, OfficialStatus status, string service, string id, string title)
    {
        if (status == OfficialStatus.Unknown) { Uncertain(reading, id, title, "진행 사건 심각도 형식 이상"); return; }
        reading.Problems.Add(new(status, service, id, title));
        reading.Active[id] = title;
    }

    private static void Uncertain(Reading reading, string id, string title, string issue)
    { reading.Issues.Add(issue); reading.Uncertain[id] = title; }

    private static JsonElement Array(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array ? items : throw new InvalidDataException();
    private static string Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
    private static string Clean(string value, int length)
    { string clean = Space.Replace(value, " ").Trim(); return clean.Length <= length ? clean : clean[..length]; }
}
