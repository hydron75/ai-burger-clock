namespace AiBurgerClock;

// Feedback-line and notification wording for recording and the holiday option, shared by every host.
// Wording follows Windows 2.2.3; where and how long it is shown stays in each host.
internal static class FeedbackText
{
    public static string RecordSaved(ProviderKind provider, UsageEventType type, ScheduleSnapshot schedule) =>
        $"{ProviderNames.Provider(provider)} · {type} 저장됨 ({schedule.NowKst:HH:mm} KST)";

    public static string RecordFailed(string error) => "기록 저장 실패: " + error;

    public static string RecordStartupFailed(string error) => "초기화 실패로 기록하지 못했습니다: " + error;

    // Only a host whose note field cannot enforce the limit itself needs this.
    public static string NoteTruncated(int savedLength) =>
        $"메모가 {UsageMeasurementFactory.MaximumNoteLength:N0}자를 넘어 앞부분 {savedLength:N0}자만 저장했습니다.";

    public static string HolidayReadFailed(string error) => "공휴일 설정 확인 실패 · 보정 OFF: " + error;

    public static string HolidaySaved(bool enabled) => "공휴일 보정 " + (enabled ? "ON" : "OFF") + " · 시간표에 반영됨";

    public static string HolidaySaveFailed(string error) => "공휴일 설정 저장 실패 · 기존 정책 유지: " + error;

    // Same wording for the Windows balloon and the macOS notification.
    public static (string Title, string Body) HolidayNotification(bool enabled, ScheduleSnapshot snapshot) => (
        "공휴일 보정 " + (enabled ? "켜짐" : "꺼짐"),
        $"시간표 정책이 변경되었습니다. 다음 전환: {snapshot.NextTransitionKst:MM-dd HH:mm} KST.\nProvider 공식 상태는 별도로 확인하세요.");
}
