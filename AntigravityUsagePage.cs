using System.Globalization;
using System.Text.RegularExpressions;

namespace AiBurgerClock;

internal interface IAntigravityUsagePageReader
{
    Task<AntigravityUsagePageLoad> LoadAntigravityAsync(CancellationToken cancellationToken);
}

internal readonly record struct AntigravityUsagePageLoad(
    Uri? FinalUri, string? Text, string? VisibilityState, string? DocumentLanguage, bool TimedOut)
{
    public override string ToString() => nameof(AntigravityUsagePageLoad);
}

internal static class AntigravityWebText
{
    internal const string MenuLabel = "Antigravity 사용량 조회";
    internal const string EnableGemini = "Gemini 웹 조회를 먼저 켜세요.";
    internal const string GeminiLoginRequired = "Gemini 로그인 필요 · Antigravity 사용량";
}

internal static partial class AntigravityUsagePage
{
    internal const string Url = "https://antigravity.google.com/remote?settingsOpen=true&settingsScreen=Models";
    internal const int PollMinutes = 60;
    internal const int ManualMinimumMinutes = 10;
    internal const string FiveHourId = "antigravity-web:five-hour";
    internal const string WeeklyId = "antigravity-web:weekly";
    private const string Title = "Gemini Models";
    private const string FiveHour = "Five Hour Limit Remaining";
    private const string Weekly = "Weekly Limit Remaining";
    private const string Refresh = "fully refresh in";

    // The closest ancestor with both exact labels limits extraction to this group.
    // Extra headings inside the same ancestor make its scope ambiguous.
    internal const string ReadScript = """
        (() => {
            const visible = document.visibilityState;
            const language = document.documentElement.lang;
            let text = null;
            for (const node of document.querySelectorAll('body *:not(:has(> *))')) {
                if (node.textContent.trim() !== 'Gemini Models' || !node.getClientRects().length) continue;
                for (let parent = node.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
                    const candidate = parent.innerText;
                    const lines = candidate.split('\n').map(line => line.trim());
                    if (lines.includes('Weekly Limit Remaining') && lines.includes('Five Hour Limit Remaining')) {
                        if (!lines.includes('Claude and GPT models')) text = candidate;
                        break;
                    }
                }
                if (text !== null) break;
            }
            return JSON.stringify({ url: location.href, text, visibilityState: visible, documentLanguage: language });
        })()
        """;

    internal static bool IsUsagePage(Uri? uri) => IsHttpsOrigin(uri, "antigravity.google.com") && uri!.AbsolutePath == "/remote";
    internal static bool IsLoginPage(Uri? uri) => IsHttpsOrigin(uri, "accounts.google.com");

    private static bool IsHttpsOrigin(Uri? uri, string host) =>
        uri is { IsAbsoluteUri: true, IsDefaultPort: true, UserInfo.Length: 0 } &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == host;

    internal static QuotaReadResult Parse(AntigravityUsagePageLoad load, DateTimeOffset now)
    {
        static QuotaReadResult Fail(QuotaFailureKind kind, string rule) => QuotaReadResult.Failed(new(kind, "antigravity." + rule));
        static QuotaReadResult Format(string rule) => Fail(QuotaFailureKind.FormatChanged, rule);

        if (IsLoginPage(load.FinalUri))
            return Fail(QuotaFailureKind.LoginRequired, "destination");
        if (load.TimedOut || !IsUsagePage(load.FinalUri))
            return Fail(QuotaFailureKind.Unreachable, "load");
        if (load.Text is null && !string.IsNullOrWhiteSpace(load.DocumentLanguage) &&
            !load.DocumentLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return Format("language");
        if (load.VisibilityState == "hidden")
            return Fail(QuotaFailureKind.Unreachable, "not-visible");
        if (load.Text is null)
            return Fail(QuotaFailureKind.Unreachable, "not-rendered");

        string[] lines = load.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Count(line => line == Title) != 1 || lines.Count(line => line == FiveHour) != 1 ||
            lines.Count(line => line == Weekly) != 1)
            return Format("sections");
        int fiveHour = Array.IndexOf(lines, FiveHour);
        int weekly = Array.IndexOf(lines, Weekly);
        string? issue = ReadWindow(lines.AsSpan(fiveHour + 1, (fiveHour < weekly ? weekly : lines.Length) - fiveHour - 1),
            now, FiveHourId, "5시간", 300, out var shortWindow);
        if (issue is not null) return Format(issue);
        issue = ReadWindow(lines.AsSpan(weekly + 1, (weekly < fiveHour ? fiveHour : lines.Length) - weekly - 1),
            now, WeeklyId, "주간", 10080, out var longWindow);
        return issue is null ? QuotaReadResult.Success(new(QuotaProvider.Antigravity, [shortWindow!, longWindow!])) : Format(issue);
    }

    private static string? ReadWindow(ReadOnlySpan<string> lines, DateTimeOffset now, string id, string label,
        int windowMinutes, out QuotaWindow? window)
    {
        window = null;
        int? remaining = null;
        string? duration = null;
        foreach (var line in lines)
        {
            var percent = Percentage().Match(line);
            if (percent.Success)
            {
                int value = int.Parse(percent.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
                if (remaining is not null || value > 100) return "percent";
                remaining = value;
            }
            int start = line.IndexOf(Refresh, StringComparison.Ordinal);
            if (start < 0) continue;
            if (duration is not null) return "duration";
            duration = line[(start + Refresh.Length)..].Trim().TrimEnd('.');
        }
        if (remaining is null) return "percent";
        DateTimeOffset? reset = null;
        if (duration is not null)
        {
            if (!TryDuration(duration, windowMinutes, out int minutes, out int precision)) return "duration";
            var estimate = now.ToUniversalTime().AddMinutes(minutes + precision / 2d);
            reset = estimate.AddTicks(-(estimate.Ticks % TimeSpan.TicksPerMinute));
        }
        else if (remaining != 100) return "duration";
        window = new(id, label, 100 - remaining.Value, reset, windowMinutes);
        return null;
    }

    private static bool TryDuration(string text, int limit, out int minutes, out int precision)
    {
        minutes = precision = 0;
        var match = Duration().Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out int first)) return false;
        int firstUnit = UnitMinutes(match.Groups[2].Value);
        long total = (long)first * firstUnit;
        precision = firstUnit;
        if (match.Groups[3].Success)
        {
            int secondUnit = UnitMinutes(match.Groups[4].Value);
            if (firstUnit <= secondUnit ||
                !int.TryParse(match.Groups[3].ValueSpan, CultureInfo.InvariantCulture, out int second)) return false;
            total += (long)second * secondUnit;
            precision = secondUnit;
        }
        if (total > limit) return false;
        minutes = (int)total;
        return true;
    }

    private static int UnitMinutes(string unit) => unit[0] switch { 'd' => 1440, 'h' => 60, _ => 1 };

    internal static bool IsWebReading(QuotaReading? reading) =>
        reading is { Provider: QuotaProvider.Antigravity, Windows.Count: 2 } &&
        reading.Windows.Count(window => IsWindow(window, FiveHourId, 300)) == 1 &&
        reading.Windows.Count(window => IsWindow(window, WeeklyId, 10080)) == 1;

    private static bool IsWindow(QuotaWindow? window, string id, int minutes) =>
        window is { UsedPercent: >= 0 and <= 100 } && window.Id == id && window.WindowMinutes == minutes &&
        (window.ResetsAtUtc is { Offset: var offset } ? offset == TimeSpan.Zero : window.UsedPercent == 0);

    [GeneratedRegex(@"^([0-9]{1,3})%$", RegexOptions.CultureInvariant)]
    private static partial Regex Percentage();
    [GeneratedRegex(@"^([0-9]+) (days?|hours?|minutes?)(?:, ([0-9]+) (days?|hours?|minutes?))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Duration();
}
