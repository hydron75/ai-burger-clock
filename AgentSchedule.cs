namespace AiBurgerClock;

internal enum AgentState
{
    FullThrottle,
    BurgerTime
}

internal sealed class ScheduleSnapshot
{
    internal ScheduleSnapshot(
        AgentState state,
        DateTimeOffset nowUtc,
        DateTimeOffset nextTransitionUtc,
        bool weekendExtended,
        bool holidayAdjustmentEnabled,
        string holidayNames,
        TimeZoneInfo korea,
        TimeZoneInfo eastern,
        TimeZoneInfo pacific)
    {
        State = state;
        NowUtc = nowUtc;
        NextTransitionUtc = nextTransitionUtc;
        NowKst = TimeZoneInfo.ConvertTime(nowUtc, korea);
        NextTransitionKst = TimeZoneInfo.ConvertTime(nextTransitionUtc, korea);
        IsWeekendExtendedFullThrottle = weekendExtended;
        HolidayAdjustmentEnabled = holidayAdjustmentEnabled;
        HolidayNames = holidayNames;
        IsHolidayExtendedFullThrottle = holidayNames.Length > 0;
        SchedulePolicyVersion = holidayAdjustmentEnabled ? AgentSchedule.HolidayPolicyVersion : AgentSchedule.PolicyVersion;
        EasternLocalTime = TimeZoneInfo.ConvertTime(nowUtc, eastern);
        PacificLocalTime = TimeZoneInfo.ConvertTime(nowUtc, pacific);
        EasternUtcOffsetMinutes = (int)EasternLocalTime.Offset.TotalMinutes;
        PacificUtcOffsetMinutes = (int)PacificLocalTime.Offset.TotalMinutes;
        EasternIsDst = eastern.IsDaylightSavingTime(nowUtc);
        PacificIsDst = pacific.IsDaylightSavingTime(nowUtc);
    }

    public AgentState State { get; }
    public DateTimeOffset NowUtc { get; }
    public DateTimeOffset NextTransitionUtc { get; }
    public DateTimeOffset NowKst { get; }
    public DateTimeOffset NextTransitionKst { get; }
    public bool IsWeekendExtendedFullThrottle { get; }
    public bool HolidayAdjustmentEnabled { get; }
    public bool IsHolidayExtendedFullThrottle { get; }
    public string HolidayNames { get; }
    public DateTimeOffset EasternLocalTime { get; }
    public DateTimeOffset PacificLocalTime { get; }
    public int EasternUtcOffsetMinutes { get; }
    public int PacificUtcOffsetMinutes { get; }
    public bool EasternIsDst { get; }
    public bool PacificIsDst { get; }
    public string SchedulePolicyVersion { get; }
    public TimeSpan Remaining => NextTransitionUtc - NowUtc;
    public AgentState NextState => State == AgentState.FullThrottle
        ? AgentState.BurgerTime
        : AgentState.FullThrottle;
}

internal static class AgentSchedule
{
    public const string PolicyVersion = "us-business-et09-pt18-v1";
    public const string HolidayPolicyVersion = "us-business-et09-pt18-holidays-v2";
    public const string EasternTimeZoneId = "Eastern Standard Time";
    public const string PacificTimeZoneId = "Pacific Standard Time";
    public const string KoreaTimeZoneId = "Korea Standard Time";
    internal const string EasternIanaTimeZoneId = "America/New_York";
    internal const string PacificIanaTimeZoneId = "America/Los_Angeles";
    internal const string KoreaIanaTimeZoneId = "Asia/Seoul";
    private const long TimeZoneRefreshMilliseconds = 24L * 60 * 60 * 1000;
    private static readonly object Sync = new();
    private static TimeZoneInfo korea = FindTimeZone(KoreaTimeZoneId, KoreaIanaTimeZoneId);
    private static TimeZoneInfo eastern = FindTimeZone(EasternTimeZoneId, EasternIanaTimeZoneId);
    private static TimeZoneInfo pacific = FindTimeZone(PacificTimeZoneId, PacificIanaTimeZoneId);
    private static long timeZonesLoadedAt = Environment.TickCount64;
    private static DateOnly? cachedEasternDate;
    private static bool cachedHolidayAdjustment;
    private static BusinessInterval[] cachedIntervals = [];

    // Keep Windows' original registry IDs. macOS uses its native IANA zone data;
    // the business policy, UTC comparisons and per-boundary DST conversion are identical.
    private static TimeZoneInfo FindTimeZone(string windowsId, string ianaId) =>
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? windowsId : ianaId);

    // The injected instant is the only clock used for schedule decisions. Neither the
    // PC's local time zone nor today's DST setting is applied to another business date.
    public static ScheduleSnapshot GetSnapshot(DateTimeOffset instant, bool adjustForHolidays = false)
    {
        DateTimeOffset nowUtc = instant.ToUniversalTime();
        lock (Sync)
        {
            RefreshTimeZoneDataIfNeeded();
            DateOnly easternDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, eastern).DateTime);
            if (cachedEasternDate != easternDate || cachedHolidayAdjustment != adjustForHolidays)
            {
                cachedIntervals = BuildIntervals(easternDate, adjustForHolidays);
                cachedEasternDate = easternDate;
                cachedHolidayAdjustment = adjustForHolidays;
            }

            foreach (BusinessInterval interval in cachedIntervals)
            {
                if (nowUtc < interval.StartUtc)
                {
                    return CreateSnapshot(AgentState.FullThrottle, nowUtc, interval.StartUtc,
                        interval.PrecedingWeekend, adjustForHolidays, interval.PrecedingHolidays);
                }

                // Business intervals are [start, end): exactly 18:00 Pacific is FULL.
                if (nowUtc < interval.EndUtc)
                    return CreateSnapshot(AgentState.BurgerTime, nowUtc, interval.EndUtc, false, adjustForHolidays, "");
            }

            throw new InvalidOperationException("No upcoming US business interval was generated.");
        }
    }

    // Display/statistics conversion with the same cached zone data as the schedule.
    // The field is replaced atomically on refresh, so reading it needs no lock.
    public static DateTimeOffset ToKst(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, korea);

    private static ScheduleSnapshot CreateSnapshot(
        AgentState state, DateTimeOffset nowUtc, DateTimeOffset transitionUtc,
        bool weekendExtended, bool holidayAdjustmentEnabled, string holidayNames)
        => new(state, nowUtc, transitionUtc, weekendExtended, holidayAdjustmentEnabled, holidayNames, korea, eastern, pacific);

    private static BusinessInterval[] BuildIntervals(DateOnly centerDate, bool adjustForHolidays)
    {
        var intervals = new List<BusinessInterval>(24);
        var holidayYears = new Dictionary<int, IReadOnlyDictionary<DateOnly, string>>();
        string? HolidayName(DateOnly date)
        {
            if (!adjustForHolidays)
                return null;
            if (!holidayYears.TryGetValue(date.Year, out IReadOnlyDictionary<DateOnly, string>? holidays))
            {
                holidays = UsFederalHolidays.GetObservedHolidays(date.Year);
                holidayYears.Add(date.Year, holidays);
            }
            return holidays.TryGetValue(date, out string? name) ? name : null;
        }

        bool precedingWeekend = false;
        var precedingHolidays = new List<string>();
        // Two weeks on each side include both business boundaries of every regular
        // federal holiday/weekend gap. All time conversion and gap classification
        // runs once per Eastern date/policy, not on every countdown tick.
        for (int day = -14; day <= 15; day++)
        {
            DateOnly date = centerDate.AddDays(day);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                precedingWeekend = true;
                continue;
            }

            if (HolidayName(date) is string holidayName)
            {
                precedingHolidays.Add(holidayName);
                continue;
            }

            intervals.Add(new BusinessInterval(
                date,
                ToUtc(date, new TimeOnly(9, 0), eastern),
                ToUtc(date, new TimeOnly(18, 0), pacific),
                precedingWeekend,
                string.Join("; ", precedingHolidays)));
            precedingWeekend = false;
            precedingHolidays.Clear();
        }
        return intervals.ToArray();
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        // DateTimeKind.Unspecified means this is a wall clock in the supplied zone.
        // The OS converts each boundary independently, including DST transition weeks.
        DateTime wallClock = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wallClock, zone));
    }

    private static void RefreshTimeZoneDataIfNeeded()
    {
        long elapsed = Environment.TickCount64 - timeZonesLoadedAt;
        if (elapsed < TimeZoneRefreshMilliseconds)
            return;

        // Observe installed OS time-zone rule updates without an app update or a
        // separate polling timer. Existing immutable snapshots keep their captured data.
        TimeZoneInfo.ClearCachedData();
        korea = FindTimeZone(KoreaTimeZoneId, KoreaIanaTimeZoneId);
        eastern = FindTimeZone(EasternTimeZoneId, EasternIanaTimeZoneId);
        pacific = FindTimeZone(PacificTimeZoneId, PacificIanaTimeZoneId);
        timeZonesLoadedAt = Environment.TickCount64;
        cachedEasternDate = null;
    }

    private sealed record BusinessInterval(
        DateOnly BusinessDate, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
        bool PrecedingWeekend, string PrecedingHolidays);
}
