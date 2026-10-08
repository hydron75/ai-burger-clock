namespace AiBurgerClock;

// What a host should emphasise; each OS maps a tone to its own colors (no color values here).
internal enum PanelTone { Normal, Good, Caution, Danger, Muted }

internal sealed record ScheduleText(
    string State, PanelTone StateTone, string Countdown, string Next, string NextDetail, string TimeZone, string TimeZoneDetail);

internal sealed record ProviderCardText(
    ProviderKind Provider, Recommendation Recommendation, string Heading, PanelTone HeadingTone,
    string Official, string Reason, string Detail);

// Status panel body text shared by the WinForms window and the macOS popover.
// Wording follows Windows 2.2.3; layout, fonts, colors and click handling stay in each host.
internal static class StatusPanelModel
{
    public const string Title = "AI AGENT TRAFFIC";
    public const string WaitingCaption = "공식 상태 갱신 대기";
    public const string HolidayOption = "미국 연방 공휴일 보정";
    public const string HolidayOptionDetail =
        "미국 연방 정기 공휴일·대체휴일의 업무 구간을 제외합니다.\n기업 휴무나 실제 서비스 품질을 보장하지 않는 시간표 정책입니다.";

    public static PanelTone Tone(AgentState state) =>
        state == AgentState.FullThrottle ? PanelTone.Good : PanelTone.Danger;

    public static PanelTone Tone(Recommendation recommendation) => recommendation switch
    {
        Recommendation.Go => PanelTone.Good,
        Recommendation.Stop or Recommendation.BurgerServiceIssue => PanelTone.Danger,
        Recommendation.Hold or Recommendation.BurgerTime => PanelTone.Caution,
        _ => PanelTone.Muted
    };

    // Day names follow the current culture, as the Windows window always has.
    public static ScheduleText Schedule(ScheduleSnapshot snapshot)
    {
        string extended = (snapshot.IsWeekendExtendedFullThrottle, snapshot.IsHolidayExtendedFullThrottle) switch
        {
            (true, true) => " · 주말+공휴일",
            (true, false) => " · Weekend",
            (false, true) => " · 공휴일",
            _ => ""
        };
        string mode = snapshot.EasternIsDst == snapshot.PacificIsDst ? (snapshot.EasternIsDst ? "DST" : "Standard") : "Mixed DST";
        return new(
            "●  " + TrayPresentation.StateName(snapshot.State),
            Tone(snapshot.State),
            "전환까지  " + DisplayFormatting.FormatRemaining(snapshot.Remaining),
            $"다음: {snapshot.NextTransitionKst:ddd HH:mm} KST" + extended,
            $"다음 전환: {snapshot.NextTransitionKst:yyyy-MM-dd HH:mm:ss} KST\n공휴일 보정: {(snapshot.HolidayAdjustmentEnabled ? "켜짐" : "꺼짐")}\n연장에 반영된 공휴일: {snapshot.HolidayNames}\n주말 여부와 공휴일 연장은 독립적으로 기록됩니다.",
            $"US: {mode} · ET {DisplayFormatting.Offset(snapshot.EasternUtcOffsetMinutes)} / PT {DisplayFormatting.Offset(snapshot.PacificUtcOffsetMinutes)}",
            $"Eastern: {snapshot.EasternLocalTime:yyyy-MM-dd HH:mm zzz}\nPacific: {snapshot.PacificLocalTime:yyyy-MM-dd HH:mm zzz}\n정책: {snapshot.SchedulePolicyVersion}");
    }

    public static IReadOnlyList<ProviderCardText> Cards(IReadOnlyList<ProviderStatus> states, AgentState schedule) =>
        states.Select(status => Card(status, schedule)).ToArray();

    public static ProviderCardText Card(ProviderStatus status, AgentState schedule)
    {
        var recommendation = RecommendationPolicy.Calculate(schedule, status.Status);
        string success = status.LastSuccessfulCheckUtc is { } time ? AgentSchedule.ToKst(time).ToString("MM-dd HH:mm:ss") + " KST" : "없음";
        string attempted = status.CheckedAtUtc == DateTimeOffset.MinValue ? "없음" : AgentSchedule.ToKst(status.CheckedAtUtc).ToString("MM-dd HH:mm:ss") + " KST";
        return new(
            status.Provider,
            recommendation,
            ProviderNames.Provider(status.Provider) + "   " + RecommendationPolicy.Label(recommendation),
            Tone(recommendation),
            "Official: " + RecommendationPolicy.OfficialLabel(status.Status),
            status.Reason,
            Detail(status, attempted, success));
    }

    // Optional fields appear only when the official source supplied them, so the tooltip never shows
    // bare labels such as "사건:". The two check times always appear ("없음" before the first check).
    private static string Detail(ProviderStatus status, string attempted, string success)
    {
        var lines = new List<string>
        {
            "클릭: 공식 상태 페이지 열기 · 우클릭: 사용 경험 기록",
            status.Reason,
            "최근 조회 시도: " + attempted,
            "마지막 상태 확인 성공: " + success
        };
        void Optional(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) lines.Add(label + value);
        }
        Optional("관련: ", status.RelevantComponent);
        Optional("사건: ", status.IncidentTitle);
        Optional("사건 ID: ", status.IncidentId);
        Optional("마지막 알려진 상태: ", status.LastKnownStatus?.ToString());
        Optional("", status.Source);
        return string.Join("\n", lines);
    }

    // Two lines under the cards: latest attempt, then the refresh in progress or the next scheduled check.
    public static string CheckedCaption(IReadOnlyList<ProviderStatus> states, bool refreshing, DateTimeOffset? nextRefreshUtc)
    {
        var last = states.Select(s => s.CheckedAtUtc).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
        return (last == DateTimeOffset.MinValue ? "최근 조회 시도: —" : $"최근 조회 시도: {AgentSchedule.ToKst(last):HH:mm:ss} KST") +
            "\n" + (refreshing ? "공식 상태 확인 중…" : nextRefreshUtc is { } next ? $"다음 조회: {AgentSchedule.ToKst(next):HH:mm:ss} KST" : "다음 조회: —");
    }
}
