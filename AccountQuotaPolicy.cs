namespace AiBurgerClock;

internal enum QuotaCheckReason { Regular, LowRemaining, Exhausted, ResetPeriod, ResetEntry, FailureRetry, Overdue }

internal sealed record QuotaCheckPlan(DateTimeOffset NextCheckUtc, QuotaCheckReason Reason,
    DateTimeOffset? SelectedResetUtc = null);

internal static class AccountQuotaPolicy
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan LowRemainingInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan ExhaustedInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResetInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResetMargin = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan ResetCoalescingMargin = TimeSpan.FromMinutes(1);
    internal const int ResetAnchorPolicyVersion = 1;

    public static TimeSpan GetInterval(DateTimeOffset nowUtc, IReadOnlyList<QuotaWindow> windows,
        IEnumerable<DateTimeOffset>? previousResetAnchors = null, QuotaProvider? provider = null)
    {
        return IsResetPeriod(nowUtc, ResetAnchors(windows, previousResetAnchors, provider))
            ? ResetInterval : RegularSchedule(windows).Interval;
    }

    public static DateTimeOffset GetNextCheckUtc(DateTimeOffset nowUtc, DateTimeOffset lastCheckedAtUtc,
        IReadOnlyList<QuotaWindow> windows, IEnumerable<DateTimeOffset>? previousResetAnchors = null,
        QuotaProvider? provider = null) =>
        GetNextCheckPlan(nowUtc, lastCheckedAtUtc, windows, previousResetAnchors, provider).NextCheckUtc;

    // The monitor uses this same decision for scheduling and diagnostics; a separate
    // explanation calculation could disagree with the actual chosen due time.
    internal static QuotaCheckPlan GetNextCheckPlan(DateTimeOffset nowUtc, DateTimeOffset lastCheckedAtUtc,
        IReadOnlyList<QuotaWindow> windows, IEnumerable<DateTimeOffset>? previousResetAnchors = null,
        QuotaProvider? provider = null)
    {
        // Materialize once: callers may provide a lazy collection shared with monitoring state.
        DateTimeOffset[] anchors = ResetAnchors(windows, previousResetAnchors, provider).ToArray();
        (TimeSpan regular, QuotaCheckReason regularReason) = RegularSchedule(windows);
        bool inResetPeriod = IsResetPeriod(nowUtc, anchors);
        TimeSpan interval = inResetPeriod ? ResetInterval : regular;
        QuotaCheckReason reason = inResetPeriod ? QuotaCheckReason.ResetPeriod : regularReason;
        DateTimeOffset? selected = inResetPeriod
            ? anchors.First(reset => IsResetPeriod(nowUtc, [reset])) : null;
        DateTimeOffset next = lastCheckedAtUtc + interval;
        if (next <= nowUtc) return new(nowUtc.ToUniversalTime(), QuotaCheckReason.Overdue, selected);
        // Selecting an interval at the last in-band check must not create a trailing
        // fast poll outside the +/-15m band. Another overlapping band may keep it fast.
        if (inResetPeriod && !IsResetPeriod(next, anchors))
        {
            next = lastCheckedAtUtc + regular;
            reason = regularReason;
            selected = null;
        }
        else if (inResetPeriod)
        {
            selected = anchors.First(reset => IsResetPeriod(next, [reset]));
        }
        foreach (DateTimeOffset reset in anchors)
        {
            DateTimeOffset entry = reset - ResetMargin;
            if (entry > nowUtc && entry <= next)
            {
                next = entry;
                reason = QuotaCheckReason.ResetEntry;
                selected = reset;
            }
        }
        // A due check is immediate, including clock changes/resume; never return a negative wait.
        return new((next < nowUtc ? nowUtc : next).ToUniversalTime(), reason, selected);
    }

    // After consecutive failed reads: 15m, 30m, then at most 1h; never later than
    // the usage-based interval (15m for an exhausted window).
    public static TimeSpan GetFailureRetryDelay(int consecutiveFailures, IReadOnlyList<QuotaWindow> windows)
    {
        TimeSpan regular = RegularSchedule(windows).Interval;
        TimeSpan delay = FailureRetryInterval * Math.Pow(2, Math.Clamp(consecutiveFailures - 1, 0, 10));
        return delay < regular ? delay : regular;
    }

    private static (TimeSpan Interval, QuotaCheckReason Reason) RegularSchedule(IReadOnlyList<QuotaWindow> windows)
    {
        // Use the verified raw percentage, not a rounded "0%" display label.
        // Default and low-remaining schedules are both hourly: their reason must
        // come from the condition, never by comparing the resulting intervals.
        if (windows.Any(window => window.UsedPercent == 100))
            return (ExhaustedInterval, QuotaCheckReason.Exhausted);
        return windows.Any(window => double.IsFinite(window.UsedPercent)
            && window.UsedPercent is > 90 and < 100)
            ? (LowRemainingInterval, QuotaCheckReason.LowRemaining)
            : (DefaultInterval, QuotaCheckReason.Regular);
    }

    private static bool IsResetPeriod(DateTimeOffset instant, IEnumerable<DateTimeOffset> anchors) =>
        anchors.Any(reset => instant >= reset - ResetMargin && instant <= reset + ResetMargin);

    // Gemini can advance an unused window's reset on every response. Such a
    // timestamp is not an active reset: retaining it can perpetuate fast polling.
    // Keep raw positive usage, even when the displayed percentage rounds to zero.
    internal static IEnumerable<DateTimeOffset> CurrentResetAnchors(IReadOnlyList<QuotaWindow> windows,
        QuotaProvider? provider = null) =>
        windows.Where(window => window.ResetsAtUtc.HasValue && (provider != QuotaProvider.Gemini ||
            double.IsFinite(window.UsedPercent) && window.UsedPercent is > 0 and <= 100))
            .Select(window => window.ResetsAtUtc!.Value);

    private static IEnumerable<DateTimeOffset> ResetAnchors(IReadOnlyList<QuotaWindow> windows,
        IEnumerable<DateTimeOffset>? previousResetAnchors, QuotaProvider? provider)
    {
        return CoalesceResetAnchors(CurrentResetAnchors(windows, provider)
            .Concat(previousResetAnchors ?? []));
    }

    // Merge only a fixed <=1m span, never a chain of individually close values.
    // Both endpoints preserve the exact union of the original +/-15m bands.
    // Keeping one rounded timestamp would lose either the first entry or last exit.
    internal static DateTimeOffset[] CoalesceResetAnchors(IEnumerable<DateTimeOffset> anchors,
        int maximumCount = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        DateTimeOffset[] ordered = anchors.Select(reset => reset.ToUniversalTime()).Distinct().Order().ToArray();
        var result = new List<DateTimeOffset>();
        for (int first = 0; first < ordered.Length;)
        {
            int last = first;
            while (last + 1 < ordered.Length && ordered[last + 1] - ordered[first] <= ResetCoalescingMargin)
                last++;
            int count = last == first ? 1 : 2;
            // A full endpoint pair is one logical group: never truncate it halfway.
            if (result.Count > maximumCount - count) break;
            result.Add(ordered[first]);
            if (last != first) result.Add(ordered[last]);
            first = last + 1;
        }
        return result.ToArray();
    }
}
