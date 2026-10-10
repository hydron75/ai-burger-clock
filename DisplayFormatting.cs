namespace AiBurgerClock;

// Text formatting only; shared by WinForms and the native macOS host.
internal static class DisplayFormatting
{
    internal static string FormatRemaining(TimeSpan remaining) =>
        $"{Math.Max(0, (int)remaining.TotalHours):00}:{Math.Max(0, remaining.Minutes):00}:{Math.Max(0, remaining.Seconds):00}";

    // Minute precision for text that must not change every second (a tooltip is closed when reassigned).
    internal static string FormatRemainingMinutes(TimeSpan remaining) =>
        $"{Math.Max(0, (int)remaining.TotalHours):00}:{Math.Max(0, remaining.Minutes):00}";

    internal static string Offset(int minutes) => $"UTC{(minutes >= 0 ? "+" : "-")}{Math.Abs(minutes) / 60}" +
        (minutes % 60 == 0 ? "" : $":{Math.Abs(minutes) % 60:00}");

    internal static string ResetCountdown(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "리셋 미제공";
        var remaining = reset.Value - now;
        if (remaining <= TimeSpan.Zero) return "갱신 대기";
        return remaining.TotalDays >= 1 ? $"{(int)remaining.TotalDays}일 {remaining.Hours:00}:{remaining.Minutes:00}" :
            $"{remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    // Quota rows use approximate durations; the Provider detail carries the exact KST instant.
    internal static string CompactResetCountdown(DateTimeOffset? reset, DateTimeOffset now, int? windowMinutes = null)
    {
        if (reset is null) return "리셋 미제공";
        var remaining = reset.Value - now;
        if (remaining <= TimeSpan.Zero) return "갱신 대기";
        if (remaining.TotalMinutes < 1) return "곧 리셋 예정";
        // The window selects the rounding unit; omit either zero unit after rounding/carry.
        bool daysAndHours = windowMinutes == 10080 || (windowMinutes != 300 && remaining.TotalDays >= 1);
        if (daysAndHours)
        {
            long hours = (long)Math.Ceiling(remaining.TotalHours);
            if (hours < 24) return $"약 {hours}시간 후 리셋";
            return hours % 24 == 0 ? $"약 {hours / 24}일 후 리셋" : $"약 {hours / 24}일 {hours % 24}시간 후 리셋";
        }
        long minutes = (long)Math.Ceiling(remaining.TotalMinutes);
        if (minutes < 60) return $"약 {minutes}분 후 리셋";
        return minutes % 60 == 0 ? $"약 {minutes / 60}시간 후 리셋" : $"약 {minutes / 60}시간 {minutes % 60}분 후 리셋";
    }
}
