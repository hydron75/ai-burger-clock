namespace AiBurgerClock;

internal static class HolidayScheduleTests
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string description)
        {
            assertions++;
            if (!condition)
                throw new InvalidOperationException("Holiday schedule: " + description);
        }

        static DateTimeOffset Kst(int year, int month, int day, int hour = 0, int minute = 0)
            => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));

        // Independent fixtures from OPM's published 2026 calendar. Production uses
        // recurring rules rather than a per-year holiday or DST table.
        (DateOnly Date, string Name)[] expected2026 =
        [
            (new(2026, 1, 1), "New Year's Day"),
            (new(2026, 1, 19), "Birthday of Martin Luther King, Jr."),
            (new(2026, 2, 16), "Washington's Birthday"),
            (new(2026, 5, 25), "Memorial Day"),
            (new(2026, 6, 19), "Juneteenth National Independence Day"),
            (new(2026, 7, 3), "Independence Day"),
            (new(2026, 9, 7), "Labor Day"),
            (new(2026, 10, 12), "Columbus Day"),
            (new(2026, 11, 11), "Veterans Day"),
            (new(2026, 11, 26), "Thanksgiving Day"),
            (new(2026, 12, 25), "Christmas Day")
        ];
        IReadOnlyDictionary<DateOnly, string> calendar = UsFederalHolidays.GetObservedHolidays(2026);
        Check(calendar.Count == expected2026.Length, "All 11 regular federal holidays in 2026, without extras");
        foreach ((DateOnly date, string name) in expected2026)
        {
            Check(calendar.TryGetValue(date, out string? actualName) && actualName == name, $"Observed date/name {date}");
            DateTimeOffset instant = Kst(date.Year, date.Month, date.Day, 23, 30);
            ScheduleSnapshot enabled = AgentSchedule.GetSnapshot(instant, true);
            ScheduleSnapshot disabled = AgentSchedule.GetSnapshot(instant, false);
            Check(enabled.State == AgentState.FullThrottle && enabled.IsHolidayExtendedFullThrottle,
                $"Holiday skip {date}");
            Check(enabled.HolidayNames.Contains(name, StringComparison.Ordinal), $"Holiday metadata {date}");
            Check(disabled.State == AgentState.BurgerTime && !disabled.IsHolidayExtendedFullThrottle
                && disabled.HolidayNames.Length == 0 && !disabled.HolidayAdjustmentEnabled,
                $"Opt-out restores original business day {date}");
            Check(enabled.HolidayAdjustmentEnabled && enabled.SchedulePolicyVersion == AgentSchedule.HolidayPolicyVersion
                && disabled.SchedulePolicyVersion == AgentSchedule.PolicyVersion, $"Policy snapshot {date}");
        }

        Check(UsFederalHolidays.GetObservedHolidays(2027)[new DateOnly(2027, 12, 31)] == "New Year's Day",
            "Next year's Saturday New Year observed in previous calendar year");
        Check(!UsFederalHolidays.GetObservedHolidays(2028).ContainsKey(new DateOnly(2028, 1, 1)),
            "Saturday New Year not duplicated on nominal date");
        Check(UsFederalHolidays.GetObservedHolidays(2023)[new DateOnly(2023, 1, 2)] == "New Year's Day",
            "Sunday New Year observed Monday");
        Check(UsFederalHolidays.GetObservedHolidays(2027)[new DateOnly(2027, 6, 18)] == "Juneteenth National Independence Day",
            "Saturday Juneteenth observed Friday");
        Check(UsFederalHolidays.GetObservedHolidays(2027)[new DateOnly(2027, 7, 5)] == "Independence Day",
            "Sunday Independence Day observed Monday");
        Check(UsFederalHolidays.GetObservedHolidays(2023)[new DateOnly(2023, 11, 10)] == "Veterans Day",
            "Saturday Veterans Day observed Friday");
        Check(UsFederalHolidays.GetObservedHolidays(2022)[new DateOnly(2022, 12, 26)] == "Christmas Day",
            "Sunday Christmas observed Monday");
        Check(!UsFederalHolidays.GetObservedHolidays(2020).Values.Contains("Juneteenth National Independence Day")
            && UsFederalHolidays.GetObservedHolidays(2021)[new DateOnly(2021, 6, 18)] == "Juneteenth National Independence Day",
            "Juneteenth introduced in 2021, not retroactively before then");
        Check(!UsFederalHolidays.GetObservedHolidays(2021).ContainsKey(new DateOnly(2021, 1, 20)),
            "Regional Inauguration Day excluded");
        Check(!calendar.ContainsKey(new DateOnly(2026, 4, 3)) && !calendar.ContainsKey(new DateOnly(2026, 11, 27)),
            "Good Friday and company Thanksgiving Friday closures excluded");

        // Holiday names describe the whole enclosing FULL gap, including its normal
        // overnight portion. Weekend and holiday labels are independent dimensions.
        void CheckGap(DateTimeOffset start, DateTimeOffset end, bool weekend, string holiday)
        {
            ScheduleSnapshot before = AgentSchedule.GetSnapshot(start.AddTicks(-1), true);
            Check(before.State == AgentState.BurgerTime && before.NextTransitionUtc == start.ToUniversalTime()
                && before.Remaining == TimeSpan.FromTicks(1), $"Last BURGER tick before {start:O}");
            Check(!before.IsWeekendExtendedFullThrottle && !before.IsHolidayExtendedFullThrottle
                && before.HolidayNames == "", $"No FULL labels before {start:O}");
            foreach (DateTimeOffset instant in new[] { start, start.AddTicks((end - start).Ticks / 2), end.AddTicks(-1) })
            {
                ScheduleSnapshot actual = AgentSchedule.GetSnapshot(instant, true);
                Check(actual.State == AgentState.FullThrottle && actual.NextTransitionUtc == end.ToUniversalTime(),
                    $"FULL state and exact UTC end {instant:O}");
                Check(actual.NextTransitionKst == end && actual.Remaining == end - instant && actual.Remaining > TimeSpan.Zero,
                    $"Holiday countdown {instant:O}");
                Check(actual.IsWeekendExtendedFullThrottle == weekend, $"Independent weekend flag {instant:O}");
                Check(actual.IsHolidayExtendedFullThrottle == (holiday.Length > 0) && actual.HolidayNames == holiday,
                    $"Independent holiday flag/name {instant:O}");
                Check(actual.HolidayAdjustmentEnabled && actual.SchedulePolicyVersion == AgentSchedule.HolidayPolicyVersion,
                    $"Captured holiday policy {instant:O}");
                ScheduleSnapshot otherOffset = AgentSchedule.GetSnapshot(instant.ToOffset(TimeSpan.FromHours(-3)), true);
                Check(otherOffset.State == actual.State && otherOffset.NextTransitionUtc == actual.NextTransitionUtc
                    && otherOffset.HolidayNames == actual.HolidayNames, $"Input offset independence {instant:O}");
            }
            ScheduleSnapshot atEnd = AgentSchedule.GetSnapshot(end, true);
            Check(atEnd.State == AgentState.BurgerTime && !atEnd.IsWeekendExtendedFullThrottle
                && !atEnd.IsHolidayExtendedFullThrottle && atEnd.HolidayNames == "", $"Labels clear exactly at {end:O}");
            Check(atEnd.NextState == AgentState.FullThrottle && atEnd.Remaining.TotalHours == 12,
                $"Next business interval is still ET09 to PT18 at {end:O}");
        }

        CheckGap(Kst(2026, 1, 17, 11), Kst(2026, 1, 20, 23), true, "Birthday of Martin Luther King, Jr.");
        CheckGap(Kst(2026, 9, 5, 10), Kst(2026, 9, 8, 22), true, "Labor Day");
        CheckGap(Kst(2026, 7, 3, 10), Kst(2026, 7, 6, 22), true, "Independence Day");
        CheckGap(Kst(2026, 12, 25, 11), Kst(2026, 12, 28, 23), true, "Christmas Day");
        CheckGap(Kst(2026, 11, 11, 11), Kst(2026, 11, 12, 23), false, "Veterans Day");
        CheckGap(Kst(2026, 11, 26, 11), Kst(2026, 11, 27, 23), false, "Thanksgiving Day");
        CheckGap(Kst(2027, 12, 31, 11), Kst(2028, 1, 3, 23), true, "New Year's Day");
        CheckGap(Kst(2022, 12, 24, 11), Kst(2022, 12, 27, 23), true, "Christmas Day");

        // No regular federal holiday falls on these DST-change weekends. They must
        // remain uninterrupted FULL with a 59/61-hour next-transition calculation,
        // even with the holiday option enabled.
        CheckGap(Kst(2026, 3, 7, 11), Kst(2026, 3, 9, 22), true, "");
        CheckGap(Kst(2026, 10, 31, 10), Kst(2026, 11, 2, 23), true, "");
        foreach (var point in new[]
        {
            (Utc: new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), Et: -240, Pt: -480),
            (Utc: new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero), Et: -240, Pt: -420),
            (Utc: new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), Et: -300, Pt: -420),
            (Utc: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), Et: -300, Pt: -480)
        })
        {
            ScheduleSnapshot actual = AgentSchedule.GetSnapshot(point.Utc, true);
            Check(actual.EasternUtcOffsetMinutes == point.Et && actual.PacificUtcOffsetMinutes == point.Pt,
                $"DST remains per-zone OS calculation {point.Utc:O}");
            Check(actual.State == AgentState.FullThrottle && actual.IsWeekendExtendedFullThrottle
                && !actual.IsHolidayExtendedFullThrottle, $"DST weekend is not falsely marked holiday {point.Utc:O}");
        }

        // Omitting an American business interval must not erase the preceding day's
        // tail merely because Korea has already reached the holiday's date.
        Check(AgentSchedule.GetSnapshot(Kst(2026, 7, 3, 9, 59), true).State == AgentState.BurgerTime,
            "US Thursday's business tail remains BURGER on Friday morning in Korea");
        Check(AgentSchedule.GetSnapshot(Kst(2026, 11, 27, 23), true).State == AgentState.BurgerTime,
            "Day after Thanksgiving remains a business day");

        DateTimeOffset toggleInstant = Kst(2026, 9, 7, 22, 30);
        ScheduleSnapshot capturedOn = AgentSchedule.GetSnapshot(toggleInstant, true);
        for (int toggle = 0; toggle < 6; toggle++)
        {
            bool enabled = toggle % 2 == 0;
            ScheduleSnapshot actual = AgentSchedule.GetSnapshot(toggleInstant, enabled);
            Check(actual.State == (enabled ? AgentState.FullThrottle : AgentState.BurgerTime)
                && actual.HolidayAdjustmentEnabled == enabled, "Cache includes holiday policy on same Eastern date");
        }
        Check(capturedOn.HolidayAdjustmentEnabled && capturedOn.IsHolidayExtendedFullThrottle
            && capturedOn.SchedulePolicyVersion == AgentSchedule.HolidayPolicyVersion,
            "Existing snapshot remains immutable after policy toggles");
        Check(AgentSchedule.GetSnapshot(toggleInstant).State == AgentState.BurgerTime,
            "Legacy call defaults to unchanged v1 policy");

        // A full-year daily sweep checks the small finite holiday set and ensures
        // the opt-out path remains weekday-only across both DST seasons.
        var holidayDates = expected2026.Select(value => value.Date).ToHashSet();
        for (var date = new DateOnly(2026, 1, 1); date.Year == 2026; date = date.AddDays(1))
        {
            bool weekday = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            DateTimeOffset instant = Kst(date.Year, date.Month, date.Day, 23, 30);
            ScheduleSnapshot disabled = AgentSchedule.GetSnapshot(instant, false);
            ScheduleSnapshot enabled = AgentSchedule.GetSnapshot(instant, true);
            Check(disabled.State == (weekday ? AgentState.BurgerTime : AgentState.FullThrottle),
                $"Legacy weekday policy {date}");
            Check(enabled.State == (weekday && !holidayDates.Contains(date) ? AgentState.BurgerTime : AgentState.FullThrottle),
                $"Holiday weekday policy {date}");
            Check(enabled.NextTransitionUtc > instant && disabled.NextTransitionUtc > instant,
                $"Finite future transition {date}");
        }

        return assertions;
    }
}
