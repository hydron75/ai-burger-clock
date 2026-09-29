namespace AiBurgerClock;

internal static class ScheduleTests
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string description)
        {
            assertions++;
            if (!condition)
                throw new InvalidOperationException("Schedule: " + description);
        }

        static DateTimeOffset Kst(int year, int month, int day, int hour = 0, int minute = 0)
            => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));

        void CheckAt(DateTimeOffset input, AgentState state, DateTimeOffset next, bool weekend)
        {
            ScheduleSnapshot actual = AgentSchedule.GetSnapshot(input);
            Check(actual.State == state, $"State at {input:O}");
            Check(actual.NextTransitionUtc == next.ToUniversalTime(), $"UTC transition at {input:O}");
            Check(actual.NextTransitionKst == next && actual.NextTransitionKst.Offset == TimeSpan.FromHours(9),
                $"KST transition at {input:O}");
            Check(actual.NowUtc.Offset == TimeSpan.Zero && actual.NowUtc == input, $"UTC instant at {input:O}");
            Check(actual.NowKst == input && actual.NowKst.Offset == TimeSpan.FromHours(9), $"KST instant at {input:O}");
            Check(actual.Remaining == next - input && actual.Remaining > TimeSpan.Zero, $"Countdown at {input:O}");
            Check(actual.IsWeekendExtendedFullThrottle == weekend, $"Weekend classification at {input:O}");
            Check(actual.NextState != actual.State, $"Next state at {input:O}");
            Check(actual.SchedulePolicyVersion == AgentSchedule.PolicyVersion, $"Policy version at {input:O}");
        }

        // Independent expected KST windows are test-only; production has no KST boundaries.
        void CheckWeek(DateTimeOffset monday, int fullStartHour, int burgerStartHour, int expectedEastern, int expectedPacific)
        {
            var burgerIntervals = Enumerable.Range(-7, 15)
                .Select(day => monday.AddDays(day))
                .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                .Select(date => (Start: date.AddHours(burgerStartHour), End: date.AddDays(1).AddHours(fullStartHour)))
                .ToArray();

            for (int minute = 0; minute < 7 * 24 * 60; minute++)
            {
                DateTimeOffset now = monday.AddMinutes(minute);
                var candidate = burgerIntervals.First(interval => now < interval.End);
                bool burger = now >= candidate.Start;
                bool weekend = !burger && candidate.Start.DayOfWeek == DayOfWeek.Monday;
                DateTimeOffset next = burger ? candidate.End : candidate.Start;
                CheckAt(now, burger ? AgentState.BurgerTime : AgentState.FullThrottle, next, weekend);
                ScheduleSnapshot actual = AgentSchedule.GetSnapshot(now.ToOffset(TimeSpan.FromHours(-3)));
                Check(actual.State == (burger ? AgentState.BurgerTime : AgentState.FullThrottle)
                    && actual.NextTransitionKst == next, $"Input offset independence at {now:O}");
                Check(actual.EasternUtcOffsetMinutes == expectedEastern && actual.PacificUtcOffsetMinutes == expectedPacific,
                    $"US offsets at {now:O}");
                Check(actual.EasternIsDst == (expectedEastern == -240) && actual.PacificIsDst == (expectedPacific == -420),
                    $"US DST flags at {now:O}");
            }

            foreach (DateTimeOffset boundary in burgerIntervals.SelectMany(interval => new[] { interval.Start, interval.End })
                .Where(value => value >= monday && value < monday.AddDays(7)))
            {
                ScheduleSnapshot before = AgentSchedule.GetSnapshot(boundary.AddTicks(-1));
                ScheduleSnapshot at = AgentSchedule.GetSnapshot(boundary);
                Check(before.Remaining == TimeSpan.FromTicks(1), $"Last tick countdown at {boundary:O}");
                Check(before.NextTransitionKst == boundary, $"Last tick transition at {boundary:O}");
                Check(at.State != before.State, $"Half-open exact boundary at {boundary:O}");
                Check(at.Remaining > TimeSpan.Zero, $"New countdown at {boundary:O}");
            }
        }

        CheckWeek(Kst(2026, 9, 14), 10, 22, -240, -420);
        CheckWeek(Kst(2026, 1, 5), 11, 23, -300, -480);

        // US spring-forward weekend is 59 real hours; fall-back weekend is 61.
        foreach (var weekend in new[]
        {
            (Start: Kst(2026, 3, 7, 11), End: Kst(2026, 3, 9, 22), Hours: 59),
            (Start: Kst(2026, 10, 31, 10), End: Kst(2026, 11, 2, 23), Hours: 61),
            (Start: Kst(2027, 3, 13, 11), End: Kst(2027, 3, 15, 22), Hours: 59),
            (Start: Kst(2027, 11, 6, 10), End: Kst(2027, 11, 8, 23), Hours: 61),
            (Start: Kst(2026, 9, 19, 10), End: Kst(2026, 9, 21, 22), Hours: 60),
            (Start: Kst(2027, 1, 2, 11), End: Kst(2027, 1, 4, 23), Hours: 60),
            (Start: Kst(2022, 12, 31, 11), End: Kst(2023, 1, 2, 23), Hours: 60),
            (Start: Kst(2028, 2, 26, 11), End: Kst(2028, 2, 28, 23), Hours: 60)
        })
        {
            CheckAt(weekend.Start.AddTicks(-1), AgentState.BurgerTime, weekend.Start, false);
            CheckAt(weekend.Start, AgentState.FullThrottle, weekend.End, true);
            CheckAt(weekend.Start.AddHours(30), AgentState.FullThrottle, weekend.End, true);
            CheckAt(weekend.End.AddTicks(-1), AgentState.FullThrottle, weekend.End, true);
            Check(AgentSchedule.GetSnapshot(weekend.Start).Remaining.TotalHours == weekend.Hours,
                $"Real weekend length at {weekend.Start:O}");
            Check(!AgentSchedule.GetSnapshot(weekend.End).IsWeekendExtendedFullThrottle,
                $"Weekend flag clears at Monday business start {weekend.End:O}");
            Check(AgentSchedule.GetSnapshot(weekend.End).State == AgentState.BurgerTime,
                $"Monday business start {weekend.End:O}");
        }

        CheckAt(Kst(2026, 12, 31, 23), AgentState.BurgerTime, Kst(2027, 1, 1, 11), false);
        CheckAt(Kst(2028, 2, 29, 11), AgentState.FullThrottle, Kst(2028, 2, 29, 23), false);
        // Holidays intentionally retain the weekday policy.
        CheckAt(Kst(2026, 12, 25, 23), AgentState.BurgerTime, Kst(2026, 12, 26, 11), false);

        // Eastern and Pacific switch three hours apart in UTC. Capture each zone's
        // actual offset/DST independently even while the schedule stays weekend FULL.
        foreach (var point in new[]
        {
            (Utc: new DateTimeOffset(2026, 3, 8, 6, 59, 59, TimeSpan.Zero), Et: -300, Pt: -480, EtDst: false, PtDst: false),
            (Utc: new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), Et: -240, Pt: -480, EtDst: true, PtDst: false),
            (Utc: new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero), Et: -240, Pt: -420, EtDst: true, PtDst: true),
            (Utc: new DateTimeOffset(2026, 11, 1, 5, 59, 59, TimeSpan.Zero), Et: -240, Pt: -420, EtDst: true, PtDst: true),
            (Utc: new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), Et: -300, Pt: -420, EtDst: false, PtDst: true),
            (Utc: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), Et: -300, Pt: -480, EtDst: false, PtDst: false)
        })
        {
            ScheduleSnapshot actual = AgentSchedule.GetSnapshot(point.Utc);
            Check(actual.EasternUtcOffsetMinutes == point.Et && actual.PacificUtcOffsetMinutes == point.Pt,
                $"Transition UTC offsets at {point.Utc:O}");
            Check(actual.EasternIsDst == point.EtDst && actual.PacificIsDst == point.PtDst,
                $"Transition DST flags at {point.Utc:O}");
            Check(actual.State == AgentState.FullThrottle && actual.IsWeekendExtendedFullThrottle,
                $"Uninterrupted transition weekend at {point.Utc:O}");
            Check(actual.EasternLocalTime == point.Utc && actual.PacificLocalTime == point.Utc,
                $"US local times represent same instant at {point.Utc:O}");
        }

        return assertions;
    }
}
