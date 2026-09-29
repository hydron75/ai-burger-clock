namespace AiBurgerClock;

internal static class AccountQuotaTests
{
    public static void Run(Action<bool, string> check)
    {
        ParserTests(check);
        PolicyTests(check);
    }

    private static void ParserTests(Action<bool, string> check)
    {
        const string claude = """
            {"type":"assistant","usage_report":{"session":{"total_cost_usd":0},"rate_limits":{"limits":[
              {"kind":"session","group":"session","percent":0,"resets_at":"2026-09-29T21:00:00.620528+00:00","scope":null,"is_active":false},
              {"kind":"weekly_all","group":"weekly","percent":45,"resets_at":"2026-09-30T05:00:00Z","scope":null,"is_active":true},
              {"kind":"weekly_scoped","group":"weekly","percent":1,"resets_at":"2026-09-30T13:59:59+09:00","scope":{"model":{"display_name":"Fable"},"surface":null},"is_active":false}
            ],"extra_usage":{"is_enabled":false,"used_credits":123}}}}
            """;
        QuotaReading reading = AccountQuotaParsers.ParseClaude(claude);
        check(reading.Provider == QuotaProvider.Claude && reading.Windows.Count == 3, "Claude parses real structured report shape");
        check(reading.Windows[0].UsedPercent == 0 && reading.Windows[0].RemainingPercent == 100, "Claude inactive 0% is present, not missing");
        check(reading.Windows[1].RemainingPercent == 55, "Claude percent is used, not remaining");
        check(reading.Windows[2].RemainingPercent == 99 && reading.Windows[2].Label.Contains("Fable"), "Claude inactive model-scoped window retained");
        check(reading.Windows[2].ResetsAtUtc == Utc(2026, 9, 30, 4, 59, 59), "Claude explicit KST timestamp converts to UTC");
        check(reading.Windows[0].ResetsAtUtc!.Value.Offset == TimeSpan.Zero, "Claude reset stored as UTC");
        check(reading.Windows.All(window => !window.Label.Contains("credit", StringComparison.OrdinalIgnoreCase)), "Extra usage credits are not reset vouchers");

        QuotaReading scoped = AccountQuotaParsers.ParseClaude("""
            {"usage_report":{"rate_limits":{"limits":[
              {"kind":"weekly_scoped","percent":100,"resets_at":null,"scope":{"model":{"display_name":"Fable"},"surface":"claude_code"}},
              {"kind":"weekly_scoped","percent":5.5,"scope":{"model":{"display_name":"Other"},"surface":"claude_ai"}}
            ]}}}
            """);
        check(scoped.Windows.Count == 2 && scoped.Windows[0].RemainingPercent == 0, "Claude retains exhausted and distinct scopes");
        check(scoped.Windows[0].Label.Contains("claude_code") && scoped.Windows[1].UsedPercent == 5.5, "Claude scope surface and fractional usage retained");
        check(scoped.Windows.All(window => window.ResetsAtUtc is null), "Missing reset is unknown, not epoch or local estimate");
        QuotaReading longLabel = AccountQuotaParsers.ParseClaude(ClaudeRow(
            "{\"kind\":\"weekly_scoped\",\"percent\":1,\"scope\":{\"model\":{\"display_name\":\""
            + new string('A', 160) + "\\n\\t\"}}}"));
        check(longLabel.Windows[0].Label.Length <= 80 && !longLabel.Windows[0].Label.Any(char.IsControl),
            "Quota labels are bounded and strip control characters");

        const string codex = """
            {"rateLimitsByLimitId":{
              "codex":{"primary":{"usedPercent":45,"windowDurationMins":10080,"resetsAt":1790744400},
                       "secondary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1790715600}},
              "review":{"limitName":"Code review","primary":{"usedPercent":100,"windowDurationMins":10080,"resetsAt":null},"secondary":null}
            },"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":300}}}
            """;
        QuotaReading codexReading = AccountQuotaParsers.ParseCodex(codex);
        check(codexReading.Provider == QuotaProvider.Codex && codexReading.Windows.Count == 3, "Codex uses every distinct bucket, not duplicate legacy rollup");
        check(codexReading.Windows[0].Label == "주간" && codexReading.Windows[0].WindowMinutes == 10080,
            "Codex primary is weekly when duration says weekly");
        check(codexReading.Windows[1].Label == "5시간" && codexReading.Windows[1].RemainingPercent == 100,
            "Codex secondary may be the 5h window");
        check(codexReading.Windows[2].Label.Contains("Code review") && codexReading.Windows[2].RemainingPercent == 0,
            "Codex named bucket and 100% use retained");
        check(codexReading.Windows[0].ResetsAtUtc == DateTimeOffset.FromUnixTimeSeconds(1790744400), "Codex reset is Unix seconds");

        QuotaReading legacy = AccountQuotaParsers.ParseCodex("""
            {"rateLimitsByLimitId":null,"rateLimits":{"primary":null,"secondary":{"usedPercent":12.5,"windowDurationMins":10080}}}
            """);
        check(legacy.Windows.Count == 1 && legacy.Windows[0].UsedPercent == 12.5, "Codex null map allows legacy fallback with absent window");
        QuotaReading unknownDuration = AccountQuotaParsers.ParseCodex("""
            {"rateLimits":{"primary":{"usedPercent":30,"windowDurationMins":null,"resetsAt":null}}}
            """);
        check(unknownDuration.Windows[0].WindowMinutes is null && unknownDuration.Windows[0].Label.Contains("미확인"),
            "Missing duration never invents 5h slot semantics");

        string[] badClaude =
        [
            "", "null", "[]", "{", "{}", "{\"usage_report\":null}",
            "{\"usage_report\":{\"rate_limits\":null}}",
            "{\"usage_report\":{\"rate_limits\":{\"limits\":[]}}}",
            "{\"usage_report\":{\"rate_limits\":{\"limits\":{}}}}",
            ClaudeRow("{\"kind\":\"session\"}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":null}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":\"45\"}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":-0.1}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":100.1}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":1e999}"),
            ClaudeRow("{\"kind\":\"new_kind\",\"percent\":50}"),
            ClaudeRow("{\"kind\":\"weekly_scoped\",\"percent\":50,\"scope\":null}"),
            ClaudeRow("{\"kind\":\"weekly_scoped\",\"percent\":50,\"scope\":{}}"),
            ClaudeRow("{\"kind\":\"weekly_scoped\",\"percent\":50,\"scope\":{\"surface\":{\"unknown\":true}}}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":1,\"resets_at\":\"2026-09-30T05:00:00\"}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":1,\"resets_at\":\"not a date\"}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":1,\"resets_at\":1790744400}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":1,\"resets_at\":\"9999-09-30T05:00:00Z\"}"),
            ClaudeRow("{\"kind\":\"session\",\"percent\":1},{\"kind\":\"session\",\"percent\":2}")
        ];
        foreach (string value in badClaude) Reject(() => AccountQuotaParsers.ParseClaude(value), check, "Claude rejects missing/malformed/ambiguous data");

        string[] badCodex =
        [
            "{}", "null", "{\"rateLimits\":null}", "{\"rateLimits\":{}}",
            "{\"rateLimitsByLimitId\":{},\"rateLimits\":{\"primary\":{\"usedPercent\":3}}}",
            "{\"rateLimitsByLimitId\":[],\"rateLimits\":{\"primary\":{\"usedPercent\":3}}}",
            "{\"rateLimitsByLimitId\":{\"codex\":null}}",
            CodexWindow("{}"), CodexWindow("{\"usedPercent\":null}"),
            CodexWindow("{\"usedPercent\":\"45\"}"), CodexWindow("{\"usedPercent\":101}"),
            CodexWindow("{\"usedPercent\":-1}"),
            CodexWindow("{\"usedPercent\":1,\"windowDurationMins\":0}"),
            CodexWindow("{\"usedPercent\":1,\"windowDurationMins\":1.5}"),
            CodexWindow("{\"usedPercent\":1,\"resetsAt\":1790744400000}"),
            CodexWindow("{\"usedPercent\":1,\"resetsAt\":\"2026-09-30T05:00:00Z\"}"),
            CodexWindow("{\"usedPercent\":1,\"resetsAt\":-1}"),
            CodexWindow("{\"usedPercent\":1,\"resetsAt\":1.5}")
        ];
        foreach (string value in badCodex) Reject(() => AccountQuotaParsers.ParseCodex(value), check, "Codex rejects malformed data without legacy masking");

        string tooMany = string.Join(",", Enumerable.Range(0, 65).Select(index =>
            "{\"kind\":\"weekly_scoped\",\"percent\":0,\"scope\":{\"model\":{\"display_name\":\"model" + index + "\"}}}"));
        Reject(() => AccountQuotaParsers.ParseClaude(ClaudeRow(tooMany)), check, "Window count bounded");
        Reject(() => AccountQuotaParsers.ParseCodex(new string(' ', 2_000_001)), check, "Payload size bounded");
        check(QuotaNames.For(QuotaProvider.Codex) == "Work / Codex" && QuotaNames.For(QuotaProvider.Claude) == "Claude", "Display names match requested scope");
    }

    private static void PolicyTests(Action<bool, string> check)
    {
        DateTimeOffset now = Utc(2026, 9, 30, 0, 0, 0);
        QuotaWindow normal = new("normal", "normal", 30, null);
        QuotaWindow low = new("low", "low", 90.01, null);
        check(AccountQuotaPolicy.GetInterval(now, [normal]) == TimeSpan.FromHours(6), "Normal polling is 6h");
        check(AccountQuotaPolicy.GetInterval(now, [normal with { UsedPercent = 90 }]) == TimeSpan.FromHours(6), "Exactly 10% remaining is not below 10%");
        check(AccountQuotaPolicy.GetInterval(now, [low]) == TimeSpan.FromHours(1), "Below 10% remaining polls hourly");
        check(AccountQuotaPolicy.GetInterval(now, [normal, low]) == TimeSpan.FromHours(1), "Any applicable window enables hourly polling");
        check(AccountQuotaPolicy.GetInterval(now, [normal with { UsedPercent = 100 }]) == TimeSpan.FromHours(1), "Exhausted quota polls hourly");
        check(AccountQuotaPolicy.GetInterval(now, []) == TimeSpan.FromHours(6), "Unknown quota does not invent low remaining");
        check(AccountQuotaPolicy.GetInterval(now, [normal]) == TimeSpan.FromHours(6), "Another provider's low quota cannot affect this provider");
        check(AccountQuotaPolicy.GetNextCheckUtc(now, now, [normal]) == now.AddHours(6), "Normal next check");
        check(AccountQuotaPolicy.GetNextCheckUtc(now, now, [low]) == now.AddHours(1), "Low remaining next check");

        DateTimeOffset reset = now.AddHours(2);
        QuotaWindow resetting = normal with { ResetsAtUtc = reset };
        check(AccountQuotaPolicy.GetNextCheckUtc(now, now, [resetting]) == reset.AddMinutes(-15), "Long wait enters reset-15m band on time");
        check(AccountQuotaPolicy.GetInterval(reset.AddMinutes(-15), [resetting]) == TimeSpan.FromMinutes(5), "Reset fast band includes start boundary");
        check(AccountQuotaPolicy.GetInterval(reset, [resetting]) == TimeSpan.FromMinutes(5), "Reset instant polls every 5m");
        check(AccountQuotaPolicy.GetInterval(reset.AddMinutes(15), [resetting]) == TimeSpan.FromMinutes(5), "Reset fast band includes end boundary");
        check(AccountQuotaPolicy.GetInterval(reset.AddMinutes(-15).AddTicks(-1), [resetting]) == TimeSpan.FromHours(6), "Before fast band normal cadence");
        check(AccountQuotaPolicy.GetInterval(reset.AddMinutes(15).AddTicks(1), [resetting]) == TimeSpan.FromHours(6), "After fast band normal cadence");
        check(AccountQuotaPolicy.GetInterval(reset, [low, resetting]) == TimeSpan.FromMinutes(5), "Shortest polling condition wins");
        check(AccountQuotaPolicy.GetInterval(reset.AddMinutes(16), [low, resetting]) == TimeSpan.FromHours(1), "After reset band return to low-remaining cadence");
        check(AccountQuotaPolicy.GetNextCheckUtc(reset, reset, [resetting]) == reset.AddMinutes(5), "Fast next check advances by 5m");
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(10), reset.AddMinutes(10), [resetting]) == reset.AddMinutes(15),
            "Last 5m candidate on the inclusive reset+15m boundary is allowed");
        foreach (int minute in new[] { 14, 15, 16 })
        {
            DateTimeOffset boundaryNow = reset.AddMinutes(minute);
            check(AccountQuotaPolicy.GetNextCheckUtc(boundaryNow, boundaryNow, [resetting]) == boundaryNow.AddHours(6),
                $"Reset+{minute}m does not schedule trailing fast poll outside the band");
            check(AccountQuotaPolicy.GetNextCheckUtc(boundaryNow, boundaryNow, [resetting, low]) == boundaryNow.AddHours(1),
                $"Reset+{minute}m resumes hourly cadence when remaining is below10%");
            check(AccountQuotaPolicy.GetNextCheckUtc(boundaryNow, boundaryNow,
                [resetting with { UsedPercent = 90 }]) == boundaryNow.AddHours(6),
                $"Reset+{minute}m keeps exact10% remaining on normal cadence");
        }
        QuotaWindow overlapping = normal with { Id = "overlap", ResetsAtUtc = reset.AddMinutes(10) };
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(14), reset.AddMinutes(14), [resetting, overlapping])
            == reset.AddMinutes(19), "Overlapping reset band keeps candidate inside its union");
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(20), reset.AddMinutes(20), [resetting, overlapping])
            == reset.AddMinutes(25), "Overlapping band's inclusive end still permits a5m candidate");
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(25), reset.AddMinutes(25), [resetting, overlapping])
            == reset.AddMinutes(25).AddHours(6), "Overlapping bands stop fast polling after their final end");
        QuotaWindow touching = normal with { Id = "touching", ResetsAtUtc = reset.AddMinutes(30) };
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(15), reset.AddMinutes(15), [resetting, touching])
            == reset.AddMinutes(20), "Touching reset bands remain continuous at the boundary");
        QuotaWindow later = normal with { Id = "later", ResetsAtUtc = reset.AddMinutes(45) };
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(14), reset.AddMinutes(14), [resetting, later])
            == reset.AddMinutes(30), "Future reset entry still wakes before resumed6h interval");
        QuotaWindow soon = normal with { Id = "soon", ResetsAtUtc = reset.AddMinutes(31) };
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(14), reset.AddMinutes(14), [resetting, soon])
            == reset.AddMinutes(16), "Upcoming band entry wins even when5m candidate lies in that band");
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(14), reset.AddMinutes(8), [resetting])
            == reset.AddMinutes(14), "Overdue fast check is immediate before considering band exit");
        check(AccountQuotaPolicy.GetNextCheckUtc(reset.AddMinutes(16), reset.AddHours(-7), [resetting])
            == reset.AddMinutes(16), "Overdue regular check remains immediate after band exit");

        QuotaWindow advanced = normal with { ResetsAtUtc = reset.AddDays(7) };
        check(AccountQuotaPolicy.GetInterval(reset.AddMinutes(10), [advanced], [reset]) == TimeSpan.FromMinutes(5), "Old reset anchor retained after server rolls reset forward");
        check(AccountQuotaPolicy.GetInterval(reset.AddMinutes(16), [advanced], [reset]) == TimeSpan.FromHours(6), "Old anchor cannot cause indefinite fast polling");
        check(AccountQuotaPolicy.GetInterval(now, [normal with { ResetsAtUtc = now.AddDays(-7) }]) == TimeSpan.FromHours(6), "Long-past reset is not a live reset band");
        check(AccountQuotaPolicy.GetNextCheckUtc(now, now, [normal], [now.AddHours(1)]) == now.AddMinutes(45), "Retained future anchor can wake scheduler");
        check(AccountQuotaPolicy.GetNextCheckUtc(now, now.AddDays(-1), [normal]) == now, "Resume or overdue schedule queries once immediately");
        check(AccountQuotaPolicy.GetNextCheckUtc(reset, reset.AddHours(-1), [resetting]) == reset, "Entering fast band catches overdue poll immediately");
        check(AccountQuotaPolicy.GetNextCheckUtc(now, now, [normal with { ResetsAtUtc = now.AddMinutes(10) }, low]) == now.AddMinutes(5), "Fast band overrides hourly low quota");

        DateTimeOffset kstNow = now.ToOffset(TimeSpan.FromHours(9));
        check(AccountQuotaPolicy.GetNextCheckUtc(kstNow, kstNow, [resetting]) == reset.AddMinutes(-15), "Polling compares instants, not displayed timezone");
        foreach (DateTimeOffset dstDate in new[] { Utc(2026, 3, 8, 7, 0, 0), Utc(2026, 11, 1, 6, 0, 0) })
        {
            check(AccountQuotaPolicy.GetNextCheckUtc(dstDate, dstDate, [normal]) == dstDate.AddHours(6), "DST transition does not change UTC quota cadence");
        }
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

    private static string ClaudeRow(string rows) => "{\"usage_report\":{\"rate_limits\":{\"limits\":[" + rows + "]}}}";
    private static string CodexWindow(string window) => "{\"rateLimits\":{\"primary\":" + window + "}}";

    private static void Reject(Action action, Action<bool, string> check, string message)
    {
        try { action(); check(false, message); }
        catch (InvalidDataException error)
        {
            check(error.Message == "CLI에서 유효한 구독 한도 정보를 받지 못했습니다.", message);
        }
    }
}
