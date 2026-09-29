using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiBurgerClock;

/// <summary>Reads only public, structured official feeds. Owns neither HttpClient nor its lifetime.</summary>
internal sealed class ProviderStatusClient(HttpClient client, Func<DateTimeOffset>? utcNow = null)
{
    // Incident begin/end comparisons use the same injected clock as the rest of the app.
    private readonly Func<DateTimeOffset> clock = utcNow ?? (() => DateTimeOffset.UtcNow);

    internal const string OpenAiSummaryUrl = "https://status.openai.com/api/v2/summary.json";
    internal const string OpenAiIncidentsUrl = "https://status.openai.com/api/v2/incidents.json";
    internal const string ClaudeSummaryUrl = "https://status.claude.com/api/v2/summary.json";
    internal const string GoogleProductsUrl = "https://www.google.com/appsstatus/dashboard/products.json";
    internal const string GoogleIncidentsUrl = "https://www.google.com/appsstatus/dashboard/incidents.json";
    internal const int MaxResponseBytes = 4 * 1024 * 1024;

    // Verified on 2026-09-19. IDs survive marketing-name changes; semantic matches allow new IDs.
    private static readonly HashSet<string> OpenAiIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "01JSM5RTJWHRWDTS6Q604VEW3B", "01JMXBNJXG1S2D9V65P1ZZTD94", // Login
        "01JMXBRMFESJCBGJR10PDD3WCQ", "01JMXBNJXG1YMQPPCPCQX3MPA2", // Files / uploads
        "01JMXBNJXGKKP51D4DEJ2HZJ8Q", "01JSG1XMJ9RVJJQ0E85NVSJ2AZ", // Search / Agent
        "01JSYVYQSWMJ9QG35XHP08BHA7", "01JVCV8YSWZFRSM1G5CVP253SK", // Research / Codex Web
        "01K6TVGGGDCP0PPGCHXAG3AQX8", "01KMKFAMWKQ81YWSE1Z18R6VHR", // Apps / Codex Desktop
        "01KMP3KP5MGE23B80K1EK4S8PV", "01KMP3KP5M8X0EBTVW6KN327EE", // Codex API / VS Code
        "01KMKFAMWKNQ84Z1766MV08ZDE" // CLI
    };
    private static readonly HashSet<string> ClaudeIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "rwppv331jlwc", "k8w3r06qmzrp", "yyzkbfz2thpt", "bpp5gb3hpjcl"
    };
    private const string GeminiProductId = "npdyhgECDJ6tB66MxXyo";
    // Confirmed OpenAI incident wording (2026-09-22); do not infer scope from generic Pro/Plus words.
    private static readonly Regex OpenAiPlanUsers = new(@"\bPlus\s+and\s+Pro\s+users\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<ProviderStatus> FetchAsync(ProviderKind provider, CancellationToken token)
    {
        if (provider == ProviderKind.Gemini)
        {
            var catalog = GetJsonAsync(GoogleProductsUrl, token);
            var incidents = GetJsonAsync(GoogleIncidentsUrl, token);
            await Task.WhenAll(catalog, incidents).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return ParseGoogle(catalog.Result, incidents.Result, clock());
        }

        var url = provider switch
        {
            ProviderKind.OpenAI => OpenAiSummaryUrl,
            ProviderKind.Claude => ClaudeSummaryUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        var json = await GetJsonAsync(url, token).ConfigureAwait(false);
        bool supplemented = false;
        // OpenAI sometimes omits incidents entirely from summary. Validate a separate official
        // history feed in that case; absence is not evidence of an incident-free service.
        if (provider == ProviderKind.OpenAI && NeedsIncidentHistory(json))
        {
            var history = await GetJsonAsync(OpenAiIncidentsUrl, token).ConfigureAwait(false);
            json = SupplementIncidents(json, history);
            supplemented = true;
        }
        token.ThrowIfCancellationRequested();
        var result = ParseStatuspage(provider, json, clock());
        return supplemented ? result with { Source = url + " + " + OpenAiIncidentsUrl } : result;
    }

    private static bool NeedsIncidentHistory(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                !document.RootElement.TryGetProperty("incidents", out _);
        }
        catch (JsonException) { return false; } // The normal parser reports UNKNOWN.
    }

    private static string SupplementIncidents(string summary, string history)
    {
        try
        {
            using var document = JsonDocument.Parse(summary);
            using var incidents = JsonDocument.Parse(history);
            if (!Array(incidents.RootElement, "incidents", out var items)) return summary;
            using var data = new MemoryStream();
            using (var writer = new Utf8JsonWriter(data))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject()) property.WriteTo(writer);
                writer.WritePropertyName("incidents");
                items.WriteTo(writer);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(data.ToArray());
        }
        catch (JsonException) { return summary; } // Required incidents remain missing => UNKNOWN.
    }

    private async Task<string> GetJsonAsync(string url, CancellationToken token)
    {
        // A linked deadline also covers streamed body reads (HttpClient.Timeout alone ends at headers
        // with ResponseHeadersRead). The owner chooses the timeout and can cancel on shutdown.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (client.Timeout != Timeout.InfiniteTimeSpan) deadline.CancelAfter(client.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("AIBurgerClock/2.0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new InvalidDataException("공식 상태 응답이 크기 제한을 초과했습니다.");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var data = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
        {
            if (data.Length + read > MaxResponseBytes)
                throw new InvalidDataException("공식 상태 응답이 크기 제한을 초과했습니다.");
            data.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(data.GetBuffer(), 0, checked((int)data.Length));
    }

    internal static ProviderStatus ParseStatuspage(ProviderKind provider, string json, DateTimeOffset now)
    {
        string source = provider == ProviderKind.OpenAI ? OpenAiSummaryUrl : ClaudeSummaryUrl;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Array(root, "components", out var components) || !Array(root, "incidents", out var incidents) ||
                !Object(root, "status", out var overall) ||
                Text(overall, "indicator") is not ("none" or "minor" or "major" or "critical" or "maintenance"))
                return Unknown(provider, now, source, "공식 상태 응답 구조를 확인할 수 없음");

            var componentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relevantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var problems = new List<Problem>();
            var uncertainDetails = new List<Problem>();
            int relevantCount = 0;
            bool uncertain = false;
            foreach (var item in components.EnumerateArray())
            {
                var id = Text(item, "id");
                var name = Text(item, "name");
                if (id.Length == 0 || name.Length == 0)
                    return Unknown(provider, now, source, "공식 component 구조가 변경됨");
                componentNames[id] = name;
                if (!Relevant(provider, id, name)) continue;
                relevantCount++;
                relevantIds.Add(id);
                var status = ComponentStatus(Text(item, "status"));
                if (status == OfficialStatus.Unknown)
                {
                    uncertain = true;
                    uncertainDetails.Add(new(status, name, "", ""));
                }
                else if (status != OfficialStatus.Operational) problems.Add(new(status, name, "", ""));
            }
            if (relevantCount == 0)
                return Unknown(provider, now, source, "관련 component를 찾을 수 없음");

            foreach (var item in incidents.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    return Unknown(provider, now, source, "공식 incident 구조가 변경됨");
                var state = Text(item, "status");
                if (state is "resolved" or "postmortem" or "completed") continue;
                var title = Text(item, "name");
                // A broad ChatGPT link must not turn a voice/images-only issue into an Agent outage.
                if (ExplicitlyUnrelated(provider, title)) continue;
                var linked = new List<string>();
                bool hasKnownUnrelatedLink = false;
                bool hasUnknownLink = false;
                if (Array(item, "components", out var affected))
                {
                    foreach (var component in affected.EnumerateArray())
                    {
                        var id = component.ValueKind == JsonValueKind.String ? component.GetString() ?? "" : Text(component, "id");
                        var name = Text(component, "name");
                        if (name.Length == 0 && componentNames.TryGetValue(id, out var existing)) name = existing;
                        if (relevantIds.Contains(id) || Relevant(provider, id, name))
                            linked.Add(name.Length == 0 ? id : name);
                        else if (componentNames.ContainsKey(id) || ExplicitlyUnrelated(provider, name)) hasKnownUnrelatedLink = true;
                        else hasUnknownLink = true;
                    }
                }
                bool titleRelevant = IncidentTitleRelevant(provider, title);
                if (linked.Count == 0 && (!titleRelevant || hasKnownUnrelatedLink))
                {
                    // An unscoped incident is not evidence that all Agent components are down.
                    // Unknown is safer than making it green when severity/scope cannot be resolved.
                    if (hasUnknownLink || !hasKnownUnrelatedLink)
                    {
                        uncertain = true;
                        uncertainDetails.Add(new(OfficialStatus.Unknown, "", Text(item, "id"), title));
                    }
                    continue;
                }
                string scope = linked.Count > 0 ? string.Join(", ", linked)
                    : IsOpenAiPlanIncident(provider, title) ? "ChatGPT Plus/Pro" : "관련 서비스";
                if (state is not ("investigating" or "identified" or "monitoring"))
                {
                    uncertain = true;
                    uncertainDetails.Add(new(OfficialStatus.Unknown, scope, Text(item, "id"), title));
                    continue;
                }
                var impact = Text(item, "impact") switch
                {
                    "none" or "minor" => OfficialStatus.Degraded,
                    "major" => OfficialStatus.PartialOutage,
                    "critical" => OfficialStatus.MajorOutage,
                    _ => OfficialStatus.Unknown
                };
                if (impact == OfficialStatus.Unknown)
                {
                    uncertain = true;
                    uncertainDetails.Add(new(impact, scope, Text(item, "id"), title));
                }
                else problems.Add(new(impact, scope, Text(item, "id"), title));
            }

            if (problems.Count > 0) return FromProblems(provider, now, source, problems);
            if (uncertain) return Unknown(provider, now, source, "일부 상태 또는 incident 범위를 확인할 수 없음", uncertainDetails);
            // Overall roll-up deliberately does NOT override verified relevant component health.
            var reason = Text(overall, "indicator") == "none" ? "관련 서비스 정상" : "관련 서비스 정상 · 전체 공지는 범위 외";
            return new(provider, OfficialStatus.Operational, now, now, reason, Source: source);
        }
        catch (JsonException)
        {
            return Unknown(provider, now, source, "공식 JSON 응답을 해석할 수 없음");
        }
    }

    internal static ProviderStatus ParseGoogle(string catalogJson, string historyJson, DateTimeOffset now)
    {
        const ProviderKind provider = ProviderKind.Gemini;
        const string source = GoogleIncidentsUrl;
        try
        {
            using var catalog = JsonDocument.Parse(catalogJson);
            using var history = JsonDocument.Parse(historyJson);
            if (!Array(catalog.RootElement, "products", out var products) || history.RootElement.ValueKind != JsonValueKind.Array)
                return Unknown(provider, now, source, "Google 공식 feed 구조를 확인할 수 없음");
            var geminiIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var product in products.EnumerateArray())
            {
                var id = Text(product, "id");
                if (id == GeminiProductId || IsGeminiTitle(Text(product, "title")) || IsGeminiTitle(Text(product, "current_title")))
                    if (id.Length > 0) geminiIds.Add(id);
            }
            if (geminiIds.Count == 0) return Unknown(provider, now, source, "공식 catalog에서 Gemini를 찾을 수 없음");

            var problems = new List<Problem>();
            bool uncertain = false;
            foreach (var incident in history.RootElement.EnumerateArray())
            {
                if (incident.ValueKind != JsonValueKind.Object)
                    return Unknown(provider, now, source, "Google incident 구조가 변경됨");
                bool related = false;
                bool scoped = false;
                if (Array(incident, "affected_products", out var affected))
                {
                    foreach (var product in affected.EnumerateArray())
                    {
                        var id = Text(product, "id");
                        scoped |= id.Length > 0;
                        related |= geminiIds.Contains(id);
                    }
                }
                if (!scoped)
                {
                    var legacyId = Text(incident, "service_key");
                    scoped = legacyId.Length > 0;
                    related = geminiIds.Contains(legacyId);
                }
                if (!scoped)
                    return Unknown(provider, now, source, "Google incident의 product 범위를 확인할 수 없음");
                if (!related) continue;

                var begin = Text(incident, "begin");
                if (!DateTimeOffset.TryParse(begin, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var began))
                {
                    uncertain = true;
                    continue;
                }
                if (began > now) continue;
                var end = Text(incident, "end");
                if (end.Length > 0)
                {
                    if (!DateTimeOffset.TryParse(end, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var ended)) uncertain = true;
                    else if (ended <= now) continue;
                    else uncertain = true; // A future end is not proof that the incident has recovered.
                }
                var state = Object(incident, "most_recent_update", out var latest) ? Text(latest, "status") : "";
                if (state.Length == 0) state = Text(incident, "status_impact");
                var status = state switch
                {
                    "AVAILABLE" => OfficialStatus.Operational,
                    "SERVICE_INFORMATION" => OfficialStatus.Degraded,
                    "SERVICE_DISRUPTION" => OfficialStatus.PartialOutage,
                    "SERVICE_OUTAGE" => OfficialStatus.MajorOutage,
                    _ => OfficialStatus.Unknown
                };
                if (status == OfficialStatus.Unknown) uncertain = true;
                else if (status != OfficialStatus.Operational)
                {
                    var title = Text(incident, "external_desc");
                    problems.Add(new(status, "Gemini", Text(incident, "id"), Clean(title, 180)));
                }
            }
            if (problems.Count > 0) return FromProblems(provider, now, source, problems);
            if (uncertain) return Unknown(provider, now, source, "Gemini incident 상태를 확인할 수 없음");
            return new(provider, OfficialStatus.Operational, now, now, "Gemini 진행 중 장애 없음", "Gemini", Source: source);
        }
        catch (JsonException)
        {
            return Unknown(provider, now, source, "Google 공식 JSON 응답을 해석할 수 없음");
        }
    }

    private static bool IsGeminiTitle(string name) => name.Equals("Gemini", StringComparison.OrdinalIgnoreCase);

    private static bool Relevant(ProviderKind provider, string id, string name)
    {
        if (ExplicitlyUnrelated(provider, name)) return false;
        if (provider == ProviderKind.Claude)
            return ClaudeIds.Contains(id) || ContainsAny(name, "claude.ai", "claude code", "claude cowork", "claude api", "api.anthropic.com");
        return OpenAiIds.Contains(id) || ContainsAny(name, "codex", "chatgpt", "conversation", "deep research", "connector", "file upload") ||
            name.Equals("Agent", StringComparison.OrdinalIgnoreCase) || name.Equals("Files", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Login", StringComparison.OrdinalIgnoreCase) || name.Equals("Search", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CLI", StringComparison.OrdinalIgnoreCase) || name.Equals("VS Code extension", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExplicitlyUnrelated(ProviderKind provider, string name)
    {
        if (provider == ProviderKind.Claude)
            return ContainsAny(name, "console", "government") && !ContainsAny(name, "claude code", "claude.ai", "claude api", "cowork");
        // A specific Agent/Codex capability mentioned alongside an excluded product remains relevant.
        if (ContainsAny(name, "codex", "deep research", "chatgpt work", "conversation", "file upload", "connector")) return false;
        return ContainsAny(name, "sora", "voice", "image", "realtime", "embedding", "fine-tun", "fedramp", "ads ", "billing", "overbilling");
    }

    private static bool IncidentTitleRelevant(ProviderKind provider, string title) =>
        provider == ProviderKind.Claude
            ? ContainsAny(title, "claude.ai", "claude code", "claude api", "api.anthropic.com", "cowork")
            : ContainsAny(title, "chatgpt", "codex", "deep research", "conversation", "file upload", "connector", "agent mode") ||
                IsOpenAiPlanIncident(provider, title);

    private static bool IsOpenAiPlanIncident(ProviderKind provider, string title) =>
        provider == ProviderKind.OpenAI && OpenAiPlanUsers.IsMatch(title);

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static OfficialStatus ComponentStatus(string value) => value switch
    {
        "operational" => OfficialStatus.Operational,
        "degraded_performance" or "under_maintenance" => OfficialStatus.Degraded,
        "partial_outage" => OfficialStatus.PartialOutage,
        "major_outage" => OfficialStatus.MajorOutage,
        _ => OfficialStatus.Unknown
    };

    private static ProviderStatus FromProblems(ProviderKind provider, DateTimeOffset now, string source, List<Problem> problems)
    {
        var worst = problems.OrderByDescending(p => (int)p.Status).ThenBy(p => p.IncidentId, StringComparer.Ordinal).First();
        var components = string.Join(", ", problems.Select(p => p.Component).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var ids = string.Join(",", problems.Where(p => p.IncidentId.Length > 0).Select(p => p.IncidentId).Distinct().Order(StringComparer.Ordinal));
        var title = string.Join(" | ", problems.Where(p => p.Title.Length > 0).Select(p => p.Title).Distinct().Order(StringComparer.Ordinal));
        return new(provider, worst.Status, now, now, Clean($"{worst.Component} · {RecommendationPolicy.OfficialLabel(worst.Status)}", 180),
            Clean(components, 500), Clean(ids, 500), Clean(title, 500), source);
    }

    private static ProviderStatus Unknown(ProviderKind provider, DateTimeOffset now, string source, string reason,
        List<Problem>? details = null) =>
        new(provider, OfficialStatus.Unknown, now, null, reason,
            Clean(string.Join(", ", (details ?? []).Select(p => p.Component).Where(value => value.Length > 0).Distinct().Order(StringComparer.Ordinal)), 500),
            Clean(string.Join(",", (details ?? []).Select(p => p.IncidentId).Where(value => value.Length > 0).Distinct().Order(StringComparer.Ordinal)), 500),
            Clean(string.Join(" | ", (details ?? []).Select(p => p.Title).Where(value => value.Length > 0).Distinct().Order(StringComparer.Ordinal)), 500),
            source);

    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static bool Array(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out value) && value.ValueKind == JsonValueKind.Array;
    }

    private static bool Object(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out value) && value.ValueKind == JsonValueKind.Object;
    }

    private static string Clean(string value, int length)
    {
        var text = new string(value.Select(c => char.IsControl(c) ? ' ' : c).Take(length).ToArray());
        return text.Trim();
    }

    private sealed record Problem(OfficialStatus Status, string Component, string IncidentId, string Title);
}
