namespace AiBurgerClock;

internal sealed record EventCounts(int Success, int Slow, int Error, int Interrupted)
{
    public int Total => Success + Slow + Error + Interrupted;
    public int Adverse => Slow + Error + Interrupted;
    public string Cell(int value) => Total == 0 ? "No data" : $"{value:N0} ({100d * value / Total:0.0}%)";
    public static EventCounts From(IEnumerable<UsageMeasurement> source)
    {
        int success = 0, slow = 0, error = 0, interrupted = 0;
        foreach (UsageMeasurement item in source)
        {
            switch (item.EventType)
            {
                case UsageEventType.Success: success++; break;
                case UsageEventType.Slow: slow++; break;
                case UsageEventType.Error: error++; break;
                case UsageEventType.Interrupted: interrupted++; break;
            }
        }
        return new(success, slow, error, interrupted);
    }
}

internal sealed record StatisticsRow(string Provider, string Group, EventCounts Counts);
internal sealed record StatisticsReport(IReadOnlyList<StatisticsRow> Providers,
    IReadOnlyList<StatisticsRow> Hours, IReadOnlyList<StatisticsRow> Schedules,
    IReadOnlyList<StatisticsRow> Official, IReadOnlyList<StatisticsRow> OperationalHours, string Policies);

internal static class StatisticsAnalysis
{
    // Periods are rolling UTC intervals, while day/hour labels use KST.
    public static DateTimeOffset? SinceUtc(int periodIndex, DateTimeOffset nowUtc) => periodIndex switch
    {
        0 => nowUtc.ToUniversalTime().AddDays(-7),
        1 => nowUtc.ToUniversalTime().AddDays(-30),
        _ => null
    };

    public static StatisticsReport Build(IReadOnlyList<UsageMeasurement> items, CancellationToken cancellationToken = default)
    {
        List<StatisticsRow> providers = [], hours = [], schedules = [], official = [], operationalHours = [];
        // Every provider gets rows for every recorded policy, including policies it has no data for.
        string[] policyVersions = items.Select(item => item.SchedulePolicyVersion).Distinct().Order().ToArray();
        foreach (ProviderKind provider in Enum.GetValues<ProviderKind>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            UsageMeasurement[] rows = items.Where(item => item.Provider == provider).ToArray();
            ILookup<int, UsageMeasurement> byHour = rows.ToLookup(item => item.KstHour);
            string name = provider.ToString();
            providers.Add(new(name, "전체", EventCounts.From(rows)));
            for (int hour = 0; hour < 24; hour++)
            {
                hours.Add(new(name, $"{hour:00}:00–{hour:00}:59 KST", EventCounts.From(byHour[hour])));
                operationalHours.Add(new(name, $"{hour:00}:00–{hour:00}:59 KST", EventCounts.From(byHour[hour].Where(item => item.OfficialStatus == OfficialStatus.Operational))));
            }
            schedules.Add(new(name, "FULL THROTTLE", EventCounts.From(rows.Where(item => item.ScheduleState == AgentState.FullThrottle))));
            schedules.Add(new(name, "BURGER TIME", EventCounts.From(rows.Where(item => item.ScheduleState == AgentState.BurgerTime))));
            schedules.Add(new(name, "Weekend / Extended FULL (공휴일 제외)", EventCounts.From(rows.Where(item => item.WeekendExtendedFullThrottle && !item.HolidayExtendedFullThrottle))));
            schedules.Add(new(name, "공휴일 연장 FULL (주말 중복 포함)", EventCounts.From(rows.Where(item => item.HolidayExtendedFullThrottle))));
            schedules.Add(new(name, "일반 FULL", EventCounts.From(rows.Where(item => item.ScheduleState == AgentState.FullThrottle && !item.WeekendExtendedFullThrottle && !item.HolidayExtendedFullThrottle))));
            schedules.Add(new(name, "공휴일 보정 ON", EventCounts.From(rows.Where(item => item.HolidayAdjustmentEnabled == true))));
            schedules.Add(new(name, "공휴일 보정 OFF", EventCounts.From(rows.Where(item => item.HolidayAdjustmentEnabled == false))));
            schedules.Add(new(name, "공휴일 보정 미기록 (이전 버전)", EventCounts.From(rows.Where(item => item.HolidayAdjustmentEnabled is null))));
            schedules.Add(new(name, "US DST (ET + PT)", EventCounts.From(rows.Where(item => item.EasternIsDst && item.PacificIsDst))));
            schedules.Add(new(name, "US Standard (ET + PT)", EventCounts.From(rows.Where(item => !item.EasternIsDst && !item.PacificIsDst))));
            schedules.Add(new(name, "US Mixed (ET ≠ PT)", EventCounts.From(rows.Where(item => item.EasternIsDst != item.PacificIsDst))));
            schedules.Add(new(name, "DST × FULL", EventCounts.From(rows.Where(item => item.EasternIsDst && item.PacificIsDst && item.ScheduleState == AgentState.FullThrottle))));
            schedules.Add(new(name, "DST × BURGER", EventCounts.From(rows.Where(item => item.EasternIsDst && item.PacificIsDst && item.ScheduleState == AgentState.BurgerTime))));
            schedules.Add(new(name, "Standard × FULL", EventCounts.From(rows.Where(item => !item.EasternIsDst && !item.PacificIsDst && item.ScheduleState == AgentState.FullThrottle))));
            schedules.Add(new(name, "Standard × BURGER", EventCounts.From(rows.Where(item => !item.EasternIsDst && !item.PacificIsDst && item.ScheduleState == AgentState.BurgerTime))));
            foreach (string policy in policyVersions)
            {
                UsageMeasurement[] policyRows = rows.Where(item => item.SchedulePolicyVersion == policy).ToArray();
                schedules.Add(new(name, "Policy: " + policy, EventCounts.From(policyRows)));
                foreach (AgentState state in Enum.GetValues<AgentState>())
                {
                    string stateName = state == AgentState.FullThrottle ? "FULL" : "BURGER";
                    schedules.Add(new(name, $"Policy: {policy} × {stateName}",
                        EventCounts.From(policyRows.Where(item => item.ScheduleState == state))));
                    foreach (bool? enabled in new bool?[] { true, false, null })
                    {
                        string setting = enabled switch { true => "공휴일 ON", false => "공휴일 OFF", _ => "공휴일 미기록" };
                        schedules.Add(new(name, $"Policy: {policy} × {setting} × {stateName}",
                            EventCounts.From(policyRows.Where(item => item.ScheduleState == state && item.HolidayAdjustmentEnabled == enabled))));
                    }
                }
            }
            foreach (OfficialStatus status in Enum.GetValues<OfficialStatus>())
                official.Add(new(name, RecommendationPolicy.OfficialLabel(status), EventCounts.From(rows.Where(item => item.OfficialStatus == status))));
        }
        string policies = string.Join(", ", policyVersions);
        return new(providers, hours, schedules, official, operationalHours, policies.Length == 0 ? "No data" : policies);
    }
}
