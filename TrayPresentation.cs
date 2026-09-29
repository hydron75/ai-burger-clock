namespace AiBurgerClock;

internal enum TrayAttention { Green, Orange, Red, Gray }
internal readonly record struct TrayAppearance(AgentState Schedule, TrayAttention Attention)
{
    public string Glyph => Schedule == AgentState.FullThrottle ? "F" : "B";
    public Color Color => Attention switch
    {
        TrayAttention.Green => Color.FromArgb(26, 169, 92),
        TrayAttention.Orange => Color.FromArgb(205, 120, 20),
        TrayAttention.Red => Color.FromArgb(224, 69, 62),
        _ => Color.FromArgb(112, 117, 124)
    };
}

// Presentation only: never writes back to an individual provider's recommendation.
internal static class TrayPresentation
{
    public static TrayAppearance Calculate(AgentState schedule, IReadOnlyList<ProviderStatus> states)
    {
        TrayAttention attention;
        if (states.Any(s => s.Status is OfficialStatus.PartialOutage or OfficialStatus.MajorOutage))
            attention = TrayAttention.Red;
        else if (states.Any(s => s.Status == OfficialStatus.Degraded))
            attention = TrayAttention.Orange;
        else if (Enum.GetValues<ProviderKind>().Any(p => !states.Any(s => s.Provider == p)) ||
            states.Any(s => s.Status != OfficialStatus.Operational))
            attention = TrayAttention.Gray;
        else
            attention = schedule == AgentState.FullThrottle ? TrayAttention.Green : TrayAttention.Orange;
        return new(schedule, attention);
    }

    public static string Tooltip(ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> states)
    {
        string state = schedule.State == AgentState.FullThrottle ? "FULL THROTTLE" : "BURGER TIME";
        string holiday = schedule.IsHolidayExtendedFullThrottle ? " · 공휴일" : "";
        string providers = string.Join("\n", Enum.GetValues<ProviderKind>().Select(provider =>
        {
            var official = states.FirstOrDefault(s => s.Provider == provider)?.Status ?? OfficialStatus.Unknown;
            string label = RecommendationPolicy.Label(RecommendationPolicy.Calculate(schedule.State, official))
                .Replace("BURGER TIME", "BURGER", StringComparison.Ordinal)
                .Replace(" ", "", StringComparison.Ordinal);
            if (official == OfficialStatus.Stale) label += "/STALE";
            return provider + " " + label;
        }));
        // Fixed provider names and bounded labels fit NotifyIcon's 127-character limit.
        return $"AI Burger Clock · {state}{holiday}\n전환까지 {StatusWindow.FormatRemaining(schedule.Remaining)}\n{providers}";
    }
}
