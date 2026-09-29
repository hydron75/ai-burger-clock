namespace AiBurgerClock;

internal static class AccountQuotaPolicy
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan LowRemainingInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan ResetInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResetMargin = TimeSpan.FromMinutes(15);

    public static TimeSpan GetInterval(DateTimeOffset nowUtc, IReadOnlyList<QuotaWindow> windows,
        IEnumerable<DateTimeOffset>? previousResetAnchors = null)
    {
        return IsResetPeriod(nowUtc, ResetAnchors(windows, previousResetAnchors))
            ? ResetInterval : RegularInterval(windows);
    }

    public static DateTimeOffset GetNextCheckUtc(DateTimeOffset nowUtc, DateTimeOffset lastCheckedAtUtc,
        IReadOnlyList<QuotaWindow> windows, IEnumerable<DateTimeOffset>? previousResetAnchors = null)
    {
        // Materialize once: callers may provide a lazy collection shared with monitoring state.
        DateTimeOffset[] anchors = ResetAnchors(windows, previousResetAnchors).ToArray();
        TimeSpan interval = GetInterval(nowUtc, windows, anchors);
        DateTimeOffset next = lastCheckedAtUtc + interval;
        if (next <= nowUtc) return nowUtc.ToUniversalTime();
        // Selecting an interval at the last in-band check must not create a trailing
        // fast poll outside the +/-15m band. Another overlapping band may keep it fast.
        if (interval == ResetInterval && !IsResetPeriod(next, anchors))
            next = lastCheckedAtUtc + RegularInterval(windows);
        foreach (DateTimeOffset reset in anchors)
        {
            DateTimeOffset entry = reset - ResetMargin;
            if (entry > nowUtc && entry < next) next = entry;
        }
        // A due check is immediate, including clock changes/resume; never return a negative wait.
        return (next < nowUtc ? nowUtc : next).ToUniversalTime();
    }

    private static TimeSpan RegularInterval(IReadOnlyList<QuotaWindow> windows) =>
        windows.Any(window => double.IsFinite(window.UsedPercent)
            && window.UsedPercent is > 90 and <= 100) ? LowRemainingInterval : DefaultInterval;

    private static bool IsResetPeriod(DateTimeOffset instant, IEnumerable<DateTimeOffset> anchors) =>
        anchors.Any(reset => instant >= reset - ResetMargin && instant <= reset + ResetMargin);

    private static IEnumerable<DateTimeOffset> ResetAnchors(IReadOnlyList<QuotaWindow> windows,
        IEnumerable<DateTimeOffset>? previousResetAnchors)
    {
        return windows.Where(window => window.ResetsAtUtc.HasValue)
            .Select(window => window.ResetsAtUtc!.Value)
            .Concat(previousResetAnchors ?? []).Distinct();
    }
}
