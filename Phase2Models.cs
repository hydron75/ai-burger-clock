namespace AiBurgerClock;

internal enum ProviderKind { OpenAI, Claude, Gemini }
internal enum OfficialStatus { Operational, Degraded, PartialOutage, MajorOutage, Unknown, Stale }
internal enum Recommendation { Go, Hold, Stop, Check, BurgerTime, BurgerServiceIssue, BurgerCheck }
internal enum UsageEventType { Success, Slow, Error, Interrupted }

internal sealed record ProviderStatus(
    ProviderKind Provider,
    OfficialStatus Status,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? LastSuccessfulCheckUtc,
    string Reason,
    string RelevantComponent = "",
    string IncidentId = "",
    string IncidentTitle = "",
    string Source = "",
    OfficialStatus? LastKnownStatus = null)
{
    // Receiving a complete HTTP response and being able to assess its scope are separate.
    // IncidentId/Title describe confirmed relevant incidents only; uncertain ones never
    // become a fabricated outage or a recorded usage-event incident.
    public string AssessmentIssue { get; init; } = "";
    public string UncertainIncidentId { get; init; } = "";
    public string UncertainIncidentTitle { get; init; } = "";
    public DateTimeOffset? LastKnownStatusUtc { get; init; }

    public static ProviderStatus Unknown(ProviderKind provider, string reason = "아직 확인하지 않음") =>
        new(provider, OfficialStatus.Unknown, DateTimeOffset.MinValue, null, reason);
}

internal static class RecommendationPolicy
{
    public static Recommendation Calculate(AgentState schedule, OfficialStatus official)
    {
        if (schedule == AgentState.BurgerTime)
            return official switch
            {
                OfficialStatus.Operational => Recommendation.BurgerTime,
                OfficialStatus.Unknown or OfficialStatus.Stale => Recommendation.BurgerCheck,
                _ => Recommendation.BurgerServiceIssue
            };
        return official switch
        {
            OfficialStatus.Operational => Recommendation.Go,
            OfficialStatus.Degraded => Recommendation.Hold,
            OfficialStatus.PartialOutage or OfficialStatus.MajorOutage => Recommendation.Stop,
            _ => Recommendation.Check
        };
    }

    public static bool ShouldNotify(Recommendation previous, Recommendation current) =>
        previous != current &&
        previous is Recommendation.Go or Recommendation.Hold or Recommendation.Stop &&
        current is Recommendation.Go or Recommendation.Hold or Recommendation.Stop;

    public static string Label(Recommendation value) => value switch
    {
        Recommendation.Go => "GO",
        Recommendation.Hold => "HOLD",
        Recommendation.Stop => "STOP",
        Recommendation.Check => "CHECK",
        Recommendation.BurgerTime => "BURGER TIME",
        Recommendation.BurgerServiceIssue => "BURGER + ISSUE",
        _ => "BURGER + CHECK"
    };

    public static string OfficialLabel(OfficialStatus value) => value switch
    {
        OfficialStatus.Operational => "정상",
        OfficialStatus.Degraded => "성능 저하",
        OfficialStatus.PartialOutage => "일부 장애",
        OfficialStatus.MajorOutage => "주요 장애",
        OfficialStatus.Stale => "STALE · 오래된 정보",
        _ => "UNKNOWN · 확인 불가"
    };
}

internal sealed record UsageMeasurement(
    string EventId,
    ProviderKind Provider,
    UsageEventType EventType,
    DateTimeOffset TimestampUtc,
    AgentState ScheduleState,
    bool WeekendExtendedFullThrottle,
    int EasternUtcOffsetMinutes,
    int PacificUtcOffsetMinutes,
    bool EasternIsDst,
    bool PacificIsDst,
    string SchedulePolicyVersion,
    OfficialStatus OfficialStatus,
    Recommendation EffectiveRecommendation,
    string RelevantComponent,
    string IncidentId,
    string UserNote,
    string AppVersion,
    bool? HolidayAdjustmentEnabled = null,
    bool HolidayExtendedFullThrottle = false,
    string HolidayNames = "")
{
    public DateTimeOffset TimestampKst => AgentSchedule.ToKst(TimestampUtc);
    public int KstHour => TimestampKst.Hour;
    public DayOfWeek KstDay => TimestampKst.DayOfWeek;
}
