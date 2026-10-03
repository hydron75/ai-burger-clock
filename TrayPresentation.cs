using System.Drawing;

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
    // Shared by the tray menu, tooltip, transition notification and status window.
    public static string StateName(AgentState state) =>
        state == AgentState.FullThrottle ? "FULL THROTTLE" : "BURGER TIME";

    public static Color StateColor(AgentState state) =>
        state == AgentState.FullThrottle ? Color.FromArgb(25, 145, 78) : Color.FromArgb(211, 61, 55);

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
        string state = StateName(schedule.State);
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
        return $"AI Burger Clock · {state}{holiday}\n전환까지 {DisplayFormatting.FormatRemaining(schedule.Remaining)}\n{providers}";
    }
}
