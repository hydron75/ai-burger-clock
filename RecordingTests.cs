using System.Globalization;

namespace AiBurgerClock;

// Golden checks for the shared recording factory and feedback text. Expected values are the
// Windows 2.2.3 TrayApplicationContext/StatusWindow output; no UI, database or user data.
internal static class RecordingTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Recording: " + label);
        }
        void Same(string actual, string expected, string label) =>
            Check(actual == expected, $"{label}\n  expected: {expected}\n  actual:   {actual}");

        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Menu(Check, Same);
            Capture(Check, Same);
            Notes(Check, Same);
            Feedback(Check, Same);
        }
        finally { CultureInfo.CurrentCulture = culture; }
        return count;
    }

    private static DateTimeOffset At(string utc) => DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);

    private static void Menu(Action<bool, string> check, Action<string, string, string> same)
    {
        var items = UsageMeasurementFactory.MenuItems;
        check(items.Count == 6, "four event types, a separator and the note item");
        check(items.Take(4).Select(i => i!.Type).SequenceEqual(Enum.GetValues<UsageEventType>()), "event types in enum order");
        same(string.Join(",", items.Take(4).Select(i => i!.Text)), "Success,Slow,Error,Interrupted", "event item text");
        check(items.Take(4).All(i => !i!.WithNote), "event items record without a note");
        check(items[4] is null, "separator before the note item");
        check(items[5] is { Text: "메모와 함께 기록…", Type: UsageEventType.Success, WithNote: true }, "note item starts as Success");
    }

    private static void Capture(Action<bool, string> check, Action<string, string, string> same)
    {
        // Thanksgiving FULL THROTTLE with a degraded provider: every captured field is non-default.
        var schedule = AgentSchedule.GetSnapshot(At("2026-11-26T17:00:00Z"), adjustForHolidays: true);
        var status = new ProviderStatus(ProviderKind.OpenAI, OfficialStatus.Degraded, At("2026-11-26T16:59:00Z"), null,
            "reason", "Codex", "inc-7");
        var captured = UsageMeasurementFactory.Capture(ProviderKind.OpenAI, UsageEventType.Slow, schedule, status, "2.2.3");

        // Field-for-field what TrayApplicationContext.RecordMeasurement constructs today.
        var windows = new UsageMeasurement(captured.EventId, ProviderKind.OpenAI, UsageEventType.Slow, schedule.NowUtc,
            schedule.State, schedule.IsWeekendExtendedFullThrottle, schedule.EasternUtcOffsetMinutes,
            schedule.PacificUtcOffsetMinutes, schedule.EasternIsDst, schedule.PacificIsDst,
            schedule.SchedulePolicyVersion, status.Status, RecommendationPolicy.Calculate(schedule.State, status.Status),
            status.RelevantComponent, status.IncidentId, "", "2.2.3",
            schedule.HolidayAdjustmentEnabled, schedule.IsHolidayExtendedFullThrottle, schedule.HolidayNames);
        check(captured == windows, "captured record matches the Windows construction");
        check(captured.EventId.Length == 32 && captured.EventId.All(Uri.IsHexDigit), "event id is a 32-digit hex GUID");
        check(UsageMeasurementFactory.Capture(ProviderKind.OpenAI, UsageEventType.Slow, schedule, status, "2.2.3").EventId != captured.EventId,
            "each capture gets a new event id");
        check(captured.TimestampUtc == At("2026-11-26T17:00:00Z") && captured.ScheduleState == AgentState.FullThrottle,
            "timestamp and state come from the snapshot");
        check(captured.EffectiveRecommendation == Recommendation.Hold && captured.OfficialStatus == OfficialStatus.Degraded,
            "recommendation from schedule and official status");
        check(captured.HolidayAdjustmentEnabled == true && captured.HolidayExtendedFullThrottle && captured.HolidayNames == "Thanksgiving Day" &&
            !captured.WeekendExtendedFullThrottle, "holiday metadata");
        check(captured.EasternUtcOffsetMinutes == -300 && captured.PacificUtcOffsetMinutes == -480 && !captured.EasternIsDst &&
            captured.SchedulePolicyVersion == AgentSchedule.HolidayPolicyVersion, "time zone metadata");
        check(captured.RelevantComponent == "Codex" && captured.IncidentId == "inc-7" && captured.UserNote == "" && captured.AppVersion == "2.2.3",
            "component, incident, empty note and version");

        // The note prompt may change the type; the captured moment and status must not move.
        var noted = UsageMeasurementFactory.WithNote(captured, UsageEventType.Error, "  응답 지연  ");
        check(noted with { EventType = captured.EventType, UserNote = "" } == captured, "note keeps every captured field");
        check(noted.EventType == UsageEventType.Error, "note prompt can change the event type");
        same(noted.UserNote, "응답 지연", "note is trimmed");

        var burger = UsageMeasurementFactory.Capture(ProviderKind.Gemini, UsageEventType.Success,
            AgentSchedule.GetSnapshot(At("2026-06-16T14:00:00Z")), ProviderStatus.Unknown(ProviderKind.Gemini), "2.2.3");
        check(burger.ScheduleState == AgentState.BurgerTime && burger.EffectiveRecommendation == Recommendation.BurgerCheck &&
            burger.HolidayAdjustmentEnabled == false && burger.HolidayNames == "" &&
            burger.SchedulePolicyVersion == AgentSchedule.PolicyVersion, "BURGER TIME capture without holiday adjustment");
    }

    private static void Notes(Action<bool, string> check, Action<string, string, string> same)
    {
        int max = UsageMeasurementFactory.MaximumNoteLength;
        check(max == 1000, "note limit matches the Windows note box");
        same(UsageMeasurementFactory.LimitNote(""), "", "empty note");
        same(UsageMeasurementFactory.LimitNote("짧은 메모"), "짧은 메모", "short note unchanged");
        string exact = new('b', max);
        check(ReferenceEquals(UsageMeasurementFactory.LimitNote(exact), exact), "note at the limit is unchanged");
        check(UsageMeasurementFactory.LimitNote(new string('c', max + 5)).Length == max, "long note cut at the limit");
        string emoji = new string('a', max - 1) + "🍔";
        same(UsageMeasurementFactory.LimitNote(emoji), new string('a', max - 1), "emoji at the limit is not split");
        string combining = new string('a', max - 1) + "é";
        same(UsageMeasurementFactory.LimitNote(combining), new string('a', max - 1), "combining mark stays with its letter");
        string flags = string.Concat(Enumerable.Repeat("🇰🇷", 251));
        check(UsageMeasurementFactory.LimitNote(flags) == string.Concat(Enumerable.Repeat("🇰🇷", 250)), "flag sequences are whole");
        same(UsageMeasurementFactory.Note("  " + new string('d', max) + "  "), new string('d', max), "whitespace trimmed before the limit");
    }

    private static void Feedback(Action<bool, string> check, Action<string, string, string> same)
    {
        var schedule = AgentSchedule.GetSnapshot(At("2026-06-16T05:00:00Z")); // 14:00 KST
        same(FeedbackText.RecordSaved(ProviderKind.OpenAI, UsageEventType.Interrupted, schedule),
            "ChatGPT · Interrupted 저장됨 (14:00 KST)", "record saved");
        same(FeedbackText.RecordSaved(ProviderKind.Claude, UsageEventType.Success, schedule), "Claude · Success 저장됨 (14:00 KST)",
            "record saved for Claude");
        same(FeedbackText.RecordFailed("disk full"), "기록 저장 실패: disk full", "record failure");
        same(FeedbackText.RecordStartupFailed("locked"), "초기화 실패로 기록하지 못했습니다: locked", "record before startup failed");
        same(FeedbackText.NoteTruncated(999), "메모가 1,000자를 넘어 앞부분 999자만 저장했습니다.", "note truncated");
        same(FeedbackText.HolidayReadFailed("locked"), "공휴일 설정 확인 실패 · 보정 OFF: locked", "holiday read failure");
        same(FeedbackText.HolidaySaved(true), "공휴일 보정 ON · 시간표에 반영됨", "holiday on");
        same(FeedbackText.HolidaySaved(false), "공휴일 보정 OFF · 시간표에 반영됨", "holiday off");
        same(FeedbackText.HolidaySaveFailed("locked"), "공휴일 설정 저장 실패 · 기존 정책 유지: locked", "holiday save failure");
        var (title, body) = FeedbackText.HolidayNotification(true,
            AgentSchedule.GetSnapshot(At("2026-11-26T17:00:00Z"), adjustForHolidays: true));
        same(title, "공휴일 보정 켜짐", "holiday notification title");
        same(body, "시간표 정책이 변경되었습니다. 다음 전환: 11-27 23:00 KST.\nProvider 공식 상태는 별도로 확인하세요.", "holiday notification body");
        same(FeedbackText.HolidayNotification(false, schedule).Title, "공휴일 보정 꺼짐", "holiday off notification title");
        check(FeedbackText.HolidayNotification(false, schedule).Body.Contains("다음 전환: 06-16 22:00 KST.", StringComparison.Ordinal),
            "notification uses the snapshot's next transition");
    }
}
