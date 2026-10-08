using System.Globalization;

namespace AiBurgerClock;

// One entry of a provider's recording menu; a null entry in MenuItems marks the separator.
internal sealed record RecordMenuItem(string Text, UsageEventType Type, bool WithNote);

// Builds the stored usage record for every host. The schedule and official status are
// captured when the user picks a menu item, before any note prompt (the Windows order).
internal static class UsageMeasurementFactory
{
    // Same limit as the Windows note box (UTF-16 units).
    public const int MaximumNoteLength = 1000;

    public static IReadOnlyList<RecordMenuItem?> MenuItems { get; } =
    [
        .. Enum.GetValues<UsageEventType>().Select(type => new RecordMenuItem(type.ToString(), type, false)),
        null,
        new RecordMenuItem("메모와 함께 기록…", UsageEventType.Success, true)
    ];

    public static UsageMeasurement Capture(ProviderKind provider, UsageEventType type, ScheduleSnapshot schedule,
        ProviderStatus status, string appVersion) =>
        new(Guid.NewGuid().ToString("N"), provider, type, schedule.NowUtc,
            schedule.State, schedule.IsWeekendExtendedFullThrottle, schedule.EasternUtcOffsetMinutes,
            schedule.PacificUtcOffsetMinutes, schedule.EasternIsDst, schedule.PacificIsDst,
            schedule.SchedulePolicyVersion, status.Status, RecommendationPolicy.Calculate(schedule.State, status.Status),
            status.RelevantComponent, status.IncidentId, "", appVersion,
            schedule.HolidayAdjustmentEnabled, schedule.IsHolidayExtendedFullThrottle, schedule.HolidayNames);

    // The note prompt may change the event type; the captured moment stays the same.
    public static UsageMeasurement WithNote(UsageMeasurement captured, UsageEventType type, string note) =>
        captured with { EventType = type, UserNote = Note(note) };

    public static string Note(string text) => LimitNote(text.Trim());

    // Never split a character or emoji at the limit.
    public static string LimitNote(string text)
    {
        if (text.Length <= MaximumNoteLength) return text;
        var elements = StringInfo.GetTextElementEnumerator(text);
        int end = 0;
        while (elements.MoveNext() && elements.ElementIndex + elements.GetTextElement().Length <= MaximumNoteLength)
            end = elements.ElementIndex + elements.GetTextElement().Length;
        return text[..end];
    }
}
