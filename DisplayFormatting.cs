namespace AiBurgerClock;

// Text formatting only; shared by WinForms and the native macOS host.
internal static class DisplayFormatting
{
    internal static string FormatRemaining(TimeSpan remaining) =>
        $"{Math.Max(0, (int)remaining.TotalHours):00}:{Math.Max(0, remaining.Minutes):00}:{Math.Max(0, remaining.Seconds):00}";

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
}
