using System.Globalization;

namespace AiBurgerClock;

// Synthetic agy 1.3.1 command.data fixtures only. No CLI, network, credentials or persisted account data.
internal static class GeminiQuotaTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Gemini quota: " + label);
        }

        Parser(Check);
        RejectMalformed(Check);
        return count;
    }

    private static void Parser(Action<bool, string> check)
    {
        const string data = """
            {"description":"Synthetic grouped development quotas","groups":[
              {"name":"Claude and GPT models","buckets":[
                {"id":"3p-weekly","window":"weekly","remaining_fraction":0,"reset_time":"2027-02-17T12:00:00Z"},
                {"id":"3p-5h","window":"5h","remaining_fraction":0.05,"reset_time":"2027-02-10T17:00:00Z"}
              ]},
              {"name":"Gemini Models","description":"Gemini Flash, Gemini Pro","buckets":[
                {"id":"gemini-weekly","name":"Weekly Limit Remaining","window":"weekly","remaining_fraction":0.99609375,"reset_time":"2027-02-17T11:05:00Z"},
                {"id":"gemini-5h","name":"Five Hour Limit Remaining","window":"5h","remaining_fraction":0.984375,"reset_time":"2027-02-10T16:05:00Z"}
              ]}
            ],"credits":{"remaining":500},"extra_usage":{"reset_vouchers":7}}
            """;
        QuotaReading reading = AccountQuotaParsers.ParseGemini(data);
        check(reading.Provider == QuotaProvider.Gemini, "Antigravity Gemini is a distinct provider");
        check(reading.Windows.Count == 2, "only the Gemini group is adopted");
        check(reading.Windows[0].Id == "gemini-5h" && reading.Windows[1].Id == "gemini-weekly",
            "five-hour window precedes weekly regardless of backend order");
        check(reading.Windows[0].Label == "5시간" && reading.Windows[1].Label == "주간",
            "window labels are stable, not backend presentation names");
        check(reading.Windows[0].WindowMinutes == 300 && reading.Windows[1].WindowMinutes == 10080,
            "duration is the confirmed five-hour and weekly scope");
        check(Math.Abs(reading.Windows[0].UsedPercent - 1.5625) < 1e-12,
            "remaining fraction converts to used percent without display rounding");
        check(Math.Abs(reading.Windows[0].RemainingPercent - 98.4375) < 1e-12,
            "fractional five-hour remaining is preserved precisely");
        check(Math.Abs(reading.Windows[1].RemainingPercent - 99.609375) < 1e-12,
            "near-full weekly remaining must not become 100 percent");
        check(reading.Windows[0].ResetsAtUtc == At("2027-02-10T16:05:00Z"), "five-hour UTC reset is retained");
        check(reading.Windows[1].ResetsAtUtc == At("2027-02-17T11:05:00Z"), "weekly UTC reset is retained");
        check(reading.Windows.All(window => window.ResetsAtUtc!.Value.Offset == TimeSpan.Zero),
            "all reset values are normalized to UTC");
        check(reading.Windows.All(window => !window.Id.StartsWith("3p-", StringComparison.Ordinal)),
            "Antigravity third-party models are not Claude or Codex subscription quotas");
        check(reading.Windows.All(window => !window.Label.Contains("credit", StringComparison.OrdinalIgnoreCase)),
            "credits and reset vouchers are not adopted as quota windows");

        QuotaReading boundaries = AccountQuotaParsers.ParseGemini(GeminiRows(
            """
            {"id":"gemini-5h","window":"5h","remaining_fraction":0,"reset_time":null},
            {"id":"gemini-weekly","window":"weekly","remaining_fraction":1}
            """));
        check(boundaries.Windows[0].UsedPercent == 100 && boundaries.Windows[0].RemainingPercent == 0,
            "exact zero fraction is exhausted");
        check(boundaries.Windows[1].UsedPercent == 0 && boundaries.Windows[1].RemainingPercent == 100,
            "exact one fraction is fully available");
        check(boundaries.Windows.All(window => window.ResetsAtUtc is null),
            "null and absent reset timestamps stay unknown");

        QuotaReading weeklyOnly = AccountQuotaParsers.ParseGemini(GeminiRows(
            """{"id":"gemini-weekly","window":"weekly","remaining_fraction":0.25}"""));
        check(weeklyOnly.Windows.Count == 1 && weeklyOnly.Windows[0].Id == "gemini-weekly",
            "an absent five-hour window is not fabricated as full remaining");
        check(weeklyOnly.Windows[0].RemainingPercent == 25 && weeklyOnly.Windows[0].ResetsAtUtc is null,
            "single-window reading has no inferred reset");

        QuotaReading offsetReset = AccountQuotaParsers.ParseGemini(GeminiRows(
            """{"id":"gemini-5h","window":"5h","remaining_fraction":0.5,"reset_time":"2027-02-11T01:05:00+09:00"}"""));
        check(offsetReset.Windows.Count == 1 && offsetReset.Windows[0].WindowMinutes == 300,
            "an absent weekly window is not fabricated");
        check(offsetReset.Windows[0].ResetsAtUtc == At("2027-02-10T16:05:00Z"),
            "explicit KST offset normalizes to the same UTC instant");

        QuotaReading isolated = AccountQuotaParsers.ParseGemini("""
            {"groups":[{"name":"Claude and GPT models","buckets":null},
              {"name":"Credits","balance":123},
              {"name":"Gemini Models","buckets":[
                {"id":"gemini-5h","window":"5h","remaining_fraction":0.8}
              ]}]}
            """);
        check(isolated.Windows.Count == 1 && isolated.Windows[0].RemainingPercent == 80,
            "unrelated groups cannot invent limits or block valid Gemini parsing");

        DateTimeOffset now = At("2027-02-10T10:00:00Z");
        check(AccountQuotaPolicy.GetInterval(now, boundaries.Windows) == TimeSpan.FromMinutes(15),
            "a Gemini exhausted window uses the shared fifteen-minute policy");
        QuotaReading low = AccountQuotaParsers.ParseGemini(GeminiRows(
            """{"id":"gemini-5h","window":"5h","remaining_fraction":0.05}"""));
        check(AccountQuotaPolicy.GetInterval(now, low.Windows) == TimeSpan.FromHours(1),
            "Gemini low remaining uses the shared one-hour policy");
        check(AccountQuotaPolicy.GetInterval(now, reading.Windows) == TimeSpan.FromHours(6),
            "third-party exhausted windows do not shorten Gemini polling");
        QuotaReading nearZero = AccountQuotaParsers.ParseGemini(GeminiRows(
            """{"id":"gemini-5h","window":"5h","remaining_fraction":0.0004}"""));
        check(nearZero.Windows[0].RemainingPercent > 0
            && AccountQuotaPolicy.GetInterval(now, nearZero.Windows) == TimeSpan.FromHours(1),
            "rounded display zero is not exact exhaustion");
        QuotaReading tinyPositive = AccountQuotaParsers.ParseGemini(GeminiRows(
            """{"id":"gemini-5h","window":"5h","remaining_fraction":1e-50}"""));
        check(tinyPositive.Windows[0].RemainingPercent > 0, "positive fraction cannot become exhausted during double subtraction");
        check(AccountQuotaPolicy.GetInterval(now, tinyPositive.Windows) == TimeSpan.FromHours(1),
            "floating-point conversion never enables the exact-zero fifteen-minute rule");
    }

    private static void RejectMalformed(Action<bool, string> check)
    {
        string[] bad =
        [
            "", " ", "null", "[]", "{", "{}",
            "{\"groups\":null}", "{\"groups\":{}}", "{\"groups\":[]}",
            "{\"groups\":[null]}", "{\"groups\":[[]]}", "{\"groups\":[{}]}",
            "{\"groups\":[{\"name\":null}]}", "{\"groups\":[{\"name\":3}]}",
            "{\"groups\":[{\"name\":\"Gemini\",\"buckets\":[]}]}",
            "{\"groups\":[{\"name\":\"gemini Models\",\"buckets\":[]}]}",
            "{\"groups\":[{\"name\":\"Claude and GPT models\",\"buckets\":[]}]}",
            "{\"groups\":[{\"name\":\"Gemini Models\"}]}",
            "{\"groups\":[{\"name\":\"Gemini Models\",\"buckets\":null}]}",
            "{\"groups\":[{\"name\":\"Gemini Models\",\"buckets\":{}}]}",
            GeminiRows(""), GeminiRows("null"), GeminiRows("[]"), GeminiRows("{}"),
            GeminiRows("""{"id":"gemini-5h","remaining_fraction":0.5}"""),
            GeminiRows("""{"window":"5h","remaining_fraction":0.5}"""),
            GeminiRows("""{"id":null,"window":"5h","remaining_fraction":0.5}"""),
            GeminiRows("""{"id":3,"window":"5h","remaining_fraction":0.5}"""),
            GeminiRows("""{"id":"gemini-5h","window":null,"remaining_fraction":0.5}"""),
            GeminiRows("""{"id":"gemini-5h","window":5,"remaining_fraction":0.5}"""),
            GeminiRows("""{"id":"gemini-5h","window":"weekly","remaining_fraction":0.5}"""),
            GeminiRows("""{"id":"gemini-weekly","window":"5h","remaining_fraction":0.5}"""),
            GeminiRows("""{"id":"gemini-5h","window":"five_hour","remaining_fraction":0.5}"""),
            GeminiRows("""{"id":"new-gemini-monthly","window":"monthly","remaining_fraction":0.5}"""),
            GeminiRows("""{"id":"3p-5h","window":"5h","remaining_fraction":0.5}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h"}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":null}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":"0.5"}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":true}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":{}}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":[]}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":-0.001}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":1.001}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":1e999}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":NaN}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":0.5,"reset_time":3}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":0.5,"reset_time":""}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":0.5,"reset_time":"2027-02-10T16:05:00"}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":0.5,"reset_time":"not a date"}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":0.5,"reset_time":"1999-02-10T16:05:00Z"}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":0.5,"reset_time":"2201-02-10T16:05:00Z"}"""),
            GeminiRows("""{"id":"gemini-5h","window":"5h","remaining_fraction":0.5},{"id":"gemini-5h","window":"5h","remaining_fraction":0.3}"""),
            GeminiRows("""{"id":"gemini-weekly","window":"weekly","remaining_fraction":0.5},{"id":"gemini-weekly","window":"weekly","remaining_fraction":0.3}"""),
            """
            {"groups":[
              {"name":"Gemini Models","buckets":[{"id":"gemini-5h","window":"5h","remaining_fraction":0.5}]},
              {"name":"Gemini Models","buckets":[{"id":"gemini-weekly","window":"weekly","remaining_fraction":0.5}]}
            ]}
            """
        ];
        foreach (string value in bad)
            Reject(value, check, "malformed, missing or ambiguous data is rejected");

        string tooManyBuckets = string.Join(",", Enumerable.Repeat(
            """{"id":"gemini-5h","window":"5h","remaining_fraction":0.5}""", 65));
        Reject(GeminiRows(tooManyBuckets), check, "bucket array is bounded");
        string tooManyGroups = string.Join(",", Enumerable.Repeat("{\"name\":\"Credits\"}", 64));
        Reject("{\"groups\":[" + tooManyGroups + "," + GeminiGroup(
            """{"id":"gemini-5h","window":"5h","remaining_fraction":0.5}""") + "]}",
            check, "group array is bounded");
        string validGroup = GeminiGroup("""{"id":"gemini-5h","window":"5h","remaining_fraction":0.5}""");
        Reject("{\"description\":\"" + new string('x', 2_000_001) + "\",\"groups\":[" + validGroup + "]}",
            check, "otherwise valid payload size is bounded");
        Reject("{\"unused\":" + new string('[', 33) + "0" + new string(']', 33)
            + ",\"groups\":[" + validGroup + "]}", check, "otherwise valid JSON depth is bounded");
    }

    private static string GeminiGroup(string rows) => "{\"name\":\"Gemini Models\",\"buckets\":[" + rows + "]}";
    private static string GeminiRows(string rows) => "{\"groups\":[" + GeminiGroup(rows) + "]}";
    private static DateTimeOffset At(string utc) => DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);

    private static void Reject(string value, Action<bool, string> check, string label)
    {
        try
        {
            AccountQuotaParsers.ParseGemini(value);
            check(false, label);
        }
        catch (InvalidDataException error)
        {
            check(error.Message == "CLI에서 유효한 구독 한도 정보를 받지 못했습니다.",
                label + "; failure never includes CLI payload or metadata");
        }
    }
}
