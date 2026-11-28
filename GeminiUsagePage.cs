using System.Globalization;
using System.Text.RegularExpressions;

namespace AiBurgerClock;

// Each host owns a single hidden WebView per load, including timeout and disposal.
// For a redirect away from the usage page, return only the final URL. A load
// failure has no URL. A loaded usage page may lack the expected element.
internal interface IGeminiUsagePageReader
{
    Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken);
}

internal readonly record struct GeminiUsagePageLoad(string? FinalUrl, string? Text)
{
    public static GeminiUsagePageLoad LoadFailed => default;

    // Neither the navigation destination nor the document belongs in diagnostics.
    public override string ToString() => nameof(GeminiUsagePageLoad);
}

internal enum GeminiUsageOutcome { Ok, LoginRequired, FormatChanged, LoadFailed }

internal enum GeminiUsageFailureRule
{
    PageLoad, Destination, MissingElement, MissingSection, DuplicateSection,
    UsageOutsideSection, InvalidUsage, DuplicateUsage, MissingUsage,
    InvalidReset, DuplicateReset, MissingReset, ResetOutsideWindow
}

internal sealed record GeminiUsageDiagnostic(GeminiUsageFailureRule Rule);
internal sealed record GeminiUsageResult(GeminiUsageOutcome Outcome, QuotaReading? Reading = null,
    GeminiUsageDiagnostic? Diagnostic = null)
{
    internal QuotaFailure? Failure => GeminiUsagePage.ClassifyFailure(Outcome, Diagnostic);
}

internal static partial class GeminiUsagePage
{
    public const string Url = "https://gemini.google.com/usage?hl=en";
    public const string ReadScript = "JSON.stringify({ url: location.href, text: (document.querySelector('declared-route-island-page')||{}).innerText ?? null })";
    public const string FiveHourId = "gemini-web:five-hour";
    public const string WeeklyId = "gemini-web:weekly";
    public const int PollMinutes = 15;

    private const string FiveHourLabel = "5시간";
    private const string WeeklyLabel = "주간";
    private const int FiveHourMinutes = 300;
    private const int WeekMinutes = 10080;
    private const int ResetToleranceMinutes = 15;
    private const string CurrentUsage = "Current usage";
    private const string WeeklyLimit = "Weekly limit";
    private const string ResetPrefix = "Resets";

    internal static QuotaFailure? ClassifyFailure(GeminiUsageOutcome? outcome, GeminiUsageDiagnostic? diagnostic) => outcome switch
    {
        GeminiUsageOutcome.LoginRequired => new(QuotaFailureKind.LoginRequired, "gemini.destination"),
        GeminiUsageOutcome.LoadFailed => new(QuotaFailureKind.Unreachable, "gemini.load"),
        GeminiUsageOutcome.FormatChanged => new(QuotaFailureKind.FormatChanged, diagnostic is { } reason
            ? "gemini." + System.Text.Json.JsonNamingPolicy.KebabCaseLower.ConvertName(reason.Rule.ToString()) : "gemini.format"),
        _ => null
    };

    public static GeminiUsageResult Parse(GeminiUsagePageLoad load, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        GeminiUsageResult Fail(GeminiUsageOutcome outcome, GeminiUsageFailureRule rule) =>
            new(outcome, Diagnostic: new(rule));
        GeminiUsageResult Changed(GeminiUsageFailureRule rule) => Fail(GeminiUsageOutcome.FormatChanged, rule);

        if (load.FinalUrl is null)
            return Fail(GeminiUsageOutcome.LoadFailed, GeminiUsageFailureRule.PageLoad);
        if (!Uri.TryCreate(load.FinalUrl, UriKind.Absolute, out var final) ||
            final.Scheme != Uri.UriSchemeHttps || !final.IsDefaultPort || final.UserInfo.Length != 0 ||
            final.Host != "gemini.google.com" || final.AbsolutePath != "/usage")
            return Fail(GeminiUsageOutcome.LoginRequired, GeminiUsageFailureRule.Destination);
        if (load.Text is null) return Changed(GeminiUsageFailureRule.MissingElement);

        string[] lines = load.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var fiveHour = new Fields();
        var weekly = new Fields();
        Fields? current = null;

        foreach (string line in lines)
        {
            if (line is CurrentUsage or WeeklyLimit)
            {
                current = line == CurrentUsage ? fiveHour : weekly;
                if (current.Seen) return Changed(GeminiUsageFailureRule.DuplicateSection);
                current.Seen = true;
                continue;
            }
            if (line.StartsWith("Get ", StringComparison.Ordinal) || line == "Upgrade")
            {
                current = null;
                continue;
            }

            // Other page text (including plan labels, pricing and upgrade copy)
            // has no bearing on the two fields that make up a verified reading.
            if (line.EndsWith("% used", StringComparison.OrdinalIgnoreCase) || LooksLikeUsage().IsMatch(line))
            {
                if (current is null) return Changed(GeminiUsageFailureRule.UsageOutsideSection);
                var match = UsageLine().Match(line);
                if (!match.Success || !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int used) || used > 100)
                    return Changed(GeminiUsageFailureRule.InvalidUsage);
                if (current.Used is not null) return Changed(GeminiUsageFailureRule.DuplicateUsage);
                current.Used = used;
            }
            else if (current is not null && line.StartsWith(ResetPrefix, StringComparison.OrdinalIgnoreCase) &&
                (line.Length == ResetPrefix.Length || char.IsWhiteSpace(line[ResetPrefix.Length])))
            {
                var match = ResetLine().Match(line);
                if (!match.Success) return Changed(GeminiUsageFailureRule.InvalidReset);
                if (current.Reset is not null) return Changed(GeminiUsageFailureRule.DuplicateReset);
                current.Reset = match.Groups[1].Value;
            }
        }

        if (!fiveHour.Seen || !weekly.Seen) return Changed(GeminiUsageFailureRule.MissingSection);
        if (fiveHour.Used is not int fiveUsed || weekly.Used is not int weekUsed)
            return Changed(GeminiUsageFailureRule.MissingUsage);
        if (fiveHour.Reset is null || weekly.Reset is null) return Changed(GeminiUsageFailureRule.MissingReset);

        var fiveFailure = ConvertReset(fiveHour.Reset, now, timeZone, FiveHourMinutes, out var fiveReset);
        if (fiveFailure is { } invalidFive) return Changed(invalidFive);
        var weekFailure = ConvertReset(weekly.Reset, now, timeZone, WeekMinutes, out var weekReset);
        if (weekFailure is { } invalidWeek) return Changed(invalidWeek);
        return new(GeminiUsageOutcome.Ok, new(QuotaProvider.Gemini,
            Array.AsReadOnly(new QuotaWindow[]
            {
                new(FiveHourId, FiveHourLabel, fiveUsed, fiveReset, FiveHourMinutes),
                new(WeeklyId, WeeklyLabel, weekUsed, weekReset, WeekMinutes)
            })));
    }

    internal static bool IsWebReading(QuotaReading? reading) =>
        reading is { Provider: QuotaProvider.Gemini, Windows.Count: 2 } &&
        IsWindow(reading.Windows[0], FiveHourId, FiveHourLabel, FiveHourMinutes) &&
        IsWindow(reading.Windows[1], WeeklyId, WeeklyLabel, WeekMinutes);

    private static bool IsWindow(QuotaWindow? window, string id, string label, int minutes) =>
        window is { ResetsAtUtc: { Offset.Ticks: 0 }, UsedPercent: >= 0 and <= 100 } &&
        window.Id == id && window.Label == label && window.WindowMinutes == minutes &&
        Math.Truncate(window.UsedPercent) == window.UsedPercent;

    private static GeminiUsageFailureRule? ConvertReset(string source, DateTimeOffset now, TimeZoneInfo zone,
        int windowMinutes, out DateTimeOffset reset)
    {
        reset = default;
        DateTimeOffset earliest = now.AddMinutes(-ResetToleranceMinutes);
        DateTimeOffset latest = now.AddMinutes(windowMinutes + ResetToleranceMinutes);
        DateTime localDay = TimeZoneInfo.ConvertTime(now, zone).Date;

        if (source.StartsWith("at ", StringComparison.Ordinal))
        {
            if (!TimeOnly.TryParseExact(source.AsSpan(3), "h:mm tt", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
                return GeminiUsageFailureRule.InvalidReset;
            DateTime local = localDay.Add(time.ToTimeSpan());
            var first = UtcInstants(local, zone).Concat(UtcInstants(local.AddDays(1), zone))
                .Where(candidate => candidate >= earliest).Min(candidate => (DateTimeOffset?)candidate);
            if (first is not { } occurrence || occurrence > latest) return GeminiUsageFailureRule.ResetOutsideWindow;
            reset = occurrence;
            return null;
        }

        var matches = new List<DateTimeOffset>();
        bool validDate = false;
        foreach (int year in new[] { localDay.Year - 1, localDay.Year, localDay.Year + 1 })
        {
            if (!DateTime.TryParseExact(source + " " + year.ToString("D4", CultureInfo.InvariantCulture),
                "MMM d 'at' h:mm tt yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                continue;
            validDate = true;
            matches.AddRange(UtcInstants(local, zone).Where(candidate => candidate >= earliest && candidate <= latest));
        }
        if (!validDate) return GeminiUsageFailureRule.InvalidReset;
        // A dated reset must identify one instant, even in a DST fold.
        if (matches.Count != 1) return GeminiUsageFailureRule.ResetOutsideWindow;
        reset = matches[0];
        return null;
    }

    private static IEnumerable<DateTimeOffset> UtcInstants(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local)) yield break;
        TimeSpan[] offsets = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local) : [zone.GetUtcOffset(local)];
        foreach (var offset in offsets)
            yield return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    [GeneratedRegex(@"^([0-9]{1,3})% used$", RegexOptions.CultureInvariant)]
    private static partial Regex UsageLine();

    [GeneratedRegex(@"^[<≤>≥~≈]?\s*\d+(?:[.,]\d+)?\s*[%％](?:\s*(?:used\b|사용됨)|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LooksLikeUsage();

    [GeneratedRegex(@"^Resets ((?:[A-Z][a-z]{2} (?:[1-9]|[12][0-9]|3[01]) )?at (?:[1-9]|1[0-2]):[0-5][0-9] (?:AM|PM))$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResetLine();

    private sealed class Fields
    {
        public bool Seen;
        public int? Used;
        public string? Reset;
    }
}
