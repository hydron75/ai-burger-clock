namespace AiBurgerClock;

internal static class AccountQuotaPolicy
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan LowRemainingInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan ExhaustedInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResetInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResetMargin = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(15);

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

    // After consecutive failed reads: 15m, 30m, 1h, ... never later than the regular interval.
    // A transient CLI/server failure then does not leave the previous value for up to 6h.
    public static TimeSpan GetFailureRetryDelay(int consecutiveFailures, IReadOnlyList<QuotaWindow> windows)
    {
        TimeSpan regular = RegularInterval(windows);
        TimeSpan delay = FailureRetryInterval * Math.Pow(2, Math.Clamp(consecutiveFailures - 1, 0, 10));
        return delay < regular ? delay : regular;
    }

    private static TimeSpan RegularInterval(IReadOnlyList<QuotaWindow> windows)
    {
        // Use the verified raw percentage, not a rounded "0%" display label.
        if (windows.Any(window => window.UsedPercent == 100)) return ExhaustedInterval;
        return windows.Any(window => double.IsFinite(window.UsedPercent)
            && window.UsedPercent is > 90 and < 100) ? LowRemainingInterval : DefaultInterval;
    }

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
