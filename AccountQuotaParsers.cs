using System.Globalization;
using System.Text.Json;

namespace AiBurgerClock;

internal static class AccountQuotaParsers
{
    private const int MaximumWindows = 64;
    private const int MaximumPayloadChars = 2_000_000;

    public static QuotaReading ParseClaude(string assistantJson) => Parse(assistantJson, root =>
    {
        JsonElement report = RequiredObject(root, "usage_report");
        JsonElement limits = RequiredObject(report, "rate_limits");
        if (!limits.TryGetProperty("limits", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() is 0 or > MaximumWindows)
            throw Invalid();

        List<QuotaWindow> windows = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement row in rows.EnumerateArray())
        {
            RequireObject(row);
            string kind = RequiredString(row, "kind");
            string label;
            string scope = string.Empty;
            int minutes;
            switch (kind)
            {
                case "session": label = "세션 (5시간)"; minutes = 300; break;
                case "weekly_all": label = "주간 전체"; minutes = 10080; break;
                case "weekly_scoped":
                    scope = ClaudeScope(row);
                    label = "주간 " + scope;
                    minutes = 10080;
                    break;
                default:
                    // A new kind may represent an additional binding quota. Do not silently omit it.
                    throw Invalid();
            }
            string id = kind + (scope.Length > 0 ? ":" + scope : string.Empty);
            if (!ids.Add(id)) throw Invalid();
            // is_active describes the CLI presentation; 0% and inactive scoped limits are still valid.
            windows.Add(new QuotaWindow(id, ShortLabel(label), Percent(row, "percent"),
                IsoReset(row, "resets_at"), minutes));
        }
        return new QuotaReading(QuotaProvider.Claude, windows.AsReadOnly());
    });

    public static QuotaReading ParseCodex(string resultJson) => Parse(resultJson, root =>
    {
        RequireObject(root);
        List<QuotaWindow> windows = [];
        if (root.TryGetProperty("rateLimitsByLimitId", out JsonElement map)
            && map.ValueKind != JsonValueKind.Null)
        {
            RequireObject(map);
            int count = 0;
            foreach (JsonProperty entry in map.EnumerateObject())
            {
                if (++count > MaximumWindows || entry.Name.Length is 0 or > 256) throw Invalid();
                ParseCodexBucket(entry.Value, entry.Name, windows);
            }
        }
        else
        {
            ParseCodexBucket(RequiredObject(root, "rateLimits"), "codex", windows);
        }
        if (windows.Count == 0 || windows.Count > MaximumWindows) throw Invalid();
        if (windows.Select(window => window.Id).Distinct(StringComparer.Ordinal).Count() != windows.Count)
            throw Invalid();
        return new QuotaReading(QuotaProvider.Codex, windows.AsReadOnly());
    });

    private static void ParseCodexBucket(JsonElement bucket, string bucketId, List<QuotaWindow> windows)
    {
        RequireObject(bucket);
        string? bucketLabel = OptionalString(bucket, "limitName");
        if (string.IsNullOrWhiteSpace(bucketLabel))
            bucketLabel = bucketId.Equals("codex", StringComparison.OrdinalIgnoreCase) ? null : bucketId;

        foreach (string slot in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(slot, out JsonElement window) || window.ValueKind == JsonValueKind.Null)
                continue;
            RequireObject(window);
            int? minutes = OptionalMinutes(window);
            string label = minutes switch
            {
                300 => "5시간",
                10080 => "주간",
                1440 => "일간",
                int duration => $"{duration:N0}분",
                _ => "한도 (주기 미확인)"
            };
            if (!string.IsNullOrEmpty(bucketLabel)) label = ShortLabel(bucketLabel) + " · " + label;
            windows.Add(new QuotaWindow(bucketId + ":" + slot, ShortLabel(label),
                Percent(window, "usedPercent"), UnixReset(window, "resetsAt"), minutes));
            if (windows.Count > MaximumWindows) throw Invalid();
        }
    }

    private static string ClaudeScope(JsonElement row)
    {
        JsonElement scope = RequiredObject(row, "scope");
        List<string> parts = [];
        if (scope.TryGetProperty("model", out JsonElement model) && model.ValueKind != JsonValueKind.Null)
        {
            RequireObject(model);
            parts.Add(RequiredString(model, "display_name"));
        }
        if (scope.TryGetProperty("surface", out JsonElement surface) && surface.ValueKind != JsonValueKind.Null)
        {
            if (surface.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(surface.GetString()))
                throw Invalid();
            parts.Add(surface.GetString()!);
        }
        if (parts.Count == 0) throw Invalid();
        return ShortLabel(string.Join(" / ", parts));
    }

    private static QuotaReading Parse(string json, Func<JsonElement, QuotaReading> parse)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumPayloadChars) throw Invalid();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            return parse(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or ArgumentOutOfRangeException or FormatException or OverflowException)
        {
            // Never include the raw CLI output, account metadata or exception payload in UI/logs.
            throw Invalid();
        }
    }

    private static double Percent(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out double percent) || !double.IsFinite(percent) || percent is < 0 or > 100)
            throw Invalid();
        return percent;
    }

    private static int? OptionalMinutes(JsonElement row)
    {
        if (!row.TryGetProperty("windowDurationMins", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int minutes)
            || minutes is <= 0 or > 5256000) throw Invalid();
        return minutes;
    }

    private static DateTimeOffset? IsoReset(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        string text = value.GetString()!;
        bool hasZone = text.EndsWith('Z') || (text.Length >= 6
            && (text[^6] == '+' || text[^6] == '-') && text[^3] == ':');
        if (text.Length is < 20 or > 40 || text[10] != 'T' || !hasZone
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateTimeOffset reset)) throw Invalid();
        return ValidateReset(reset);
    }

    private static DateTimeOffset? UnixReset(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long seconds)) throw Invalid();
        return ValidateReset(DateTimeOffset.FromUnixTimeSeconds(seconds));
    }

    private static DateTimeOffset ValidateReset(DateTimeOffset reset)
    {
        // Avoid accidental millisecond timestamps and impossible values while allowing past resets.
        if (reset.Year is < 2000 or > 2200) throw Invalid();
        return reset.ToUniversalTime();
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        RequireObject(parent);
        if (!parent.TryGetProperty(name, out JsonElement value)) throw Invalid();
        RequireObject(value);
        return value;
    }

    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        string? value = OptionalString(parent, name);
        return string.IsNullOrWhiteSpace(value) ? throw Invalid() : value;
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 256) throw Invalid();
        return value.GetString();
    }

    private static string ShortLabel(string text)
    {
        string cleaned = new(text.Where(character => !char.IsControl(character)).Take(80).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? throw Invalid() : cleaned.Trim();
    }

    private static InvalidDataException Invalid() => new("CLI에서 유효한 구독 한도 정보를 받지 못했습니다.");
}
