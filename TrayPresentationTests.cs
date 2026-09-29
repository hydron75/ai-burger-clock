namespace AiBurgerClock;

// Explicit self-test only: no network, registry changes, notifications, or real user data.
internal static class TrayPresentationTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Tray presentation test: " + message);
        }

        var full = AgentSchedule.GetSnapshot(new DateTimeOffset(2026, 6, 16, 5, 0, 0, TimeSpan.Zero));
        var burger = AgentSchedule.GetSnapshot(new DateTimeOffset(2026, 6, 16, 14, 0, 0, TimeSpan.Zero));
        Check(full.State == AgentState.FullThrottle && burger.State == AgentState.BurgerTime, "Representative Schedule fixtures");
        OfficialStatus[] officialStates = Enum.GetValues<OfficialStatus>();
        Check(officialStates.Length == 6, "Six official states are covered by the exhaustive matrix");

        // Independent priority oracle: outage 3 > degraded 2 > uncertain 1 > healthy 0.
        var priorities = new Dictionary<OfficialStatus, int>
        {
            [OfficialStatus.Operational] = 0,
            [OfficialStatus.Unknown] = 1,
            [OfficialStatus.Stale] = 1,
            [OfficialStatus.Degraded] = 2,
            [OfficialStatus.PartialOutage] = 3,
            [OfficialStatus.MajorOutage] = 3
        };
        int combinations = 0;
        foreach (var schedule in new[] { full, burger })
        foreach (var openAi in officialStates)
        foreach (var claude in officialStates)
        foreach (var gemini in officialStates)
        {
            combinations++;
            var statuses = new[]
            {
                Status(ProviderKind.OpenAI, openAi),
                Status(ProviderKind.Claude, claude),
                Status(ProviderKind.Gemini, gemini)
            };
            var original = statuses.ToArray();
            int highest = new[] { priorities[openAi], priorities[claude], priorities[gemini] }.Max();
            TrayAttention expected = highest switch
            {
                3 => TrayAttention.Red,
                2 => TrayAttention.Orange,
                1 => TrayAttention.Gray,
                _ => schedule.State == AgentState.FullThrottle ? TrayAttention.Green : TrayAttention.Orange
            };
            string context = $"{schedule.State}: {openAi}/{claude}/{gemini}";
            TrayAppearance appearance = TrayPresentation.Calculate(schedule.State, statuses);
            Check(appearance.Attention == expected, "Color priority " + context);
            Check(appearance.Schedule == schedule.State && appearance.Glyph ==
                (schedule.State == AgentState.FullThrottle ? "F" : "B"), "Schedule glyph remains independent: " + context);
            Check(TrayPresentation.Calculate(schedule.State, statuses.Reverse().ToArray()) == appearance,
                "Provider ordering has no effect: " + context);
            Check(statuses.SequenceEqual(original), "Aggregation never modifies provider records: " + context);

            string tooltip = TrayPresentation.Tooltip(schedule, statuses);
            Check(tooltip.Length <= 127, "NotifyIcon tooltip length: " + context + " (" + tooltip.Length + ")");
            Check(tooltip.Contains("전환까지 " + StatusWindow.FormatRemaining(schedule.Remaining), StringComparison.Ordinal),
                "Tooltip retains countdown: " + context);
            Check(tooltip.Contains(schedule.State == AgentState.FullThrottle ? "FULL THROTTLE" : "BURGER TIME", StringComparison.Ordinal),
                "Tooltip retains Schedule: " + context);
            string[] lines = tooltip.Split('\n');
            Check(lines.Length == 5, "One Schedule/countdown/provider line each: " + context);
            foreach (var status in statuses)
            {
                Recommendation independent = ExpectedRecommendation(schedule.State, status.Status);
                Check(RecommendationPolicy.Calculate(schedule.State, status.Status) == independent,
                    "Provider recommendation is not elevated/lowered by another provider: " + context + "/" + status.Provider);
                string expectedLabel = independent switch
                {
                    Recommendation.Go => "GO",
                    Recommendation.Hold => "HOLD",
                    Recommendation.Stop => "STOP",
                    Recommendation.Check => "CHECK",
                    Recommendation.BurgerTime => "BURGER",
                    Recommendation.BurgerServiceIssue => "BURGER+ISSUE",
                    _ => "BURGER+CHECK"
                };
                if (status.Status == OfficialStatus.Stale) expectedLabel += "/STALE";
                Check(lines.Contains(status.Provider + " " + expectedLabel), "Independent tooltip recommendation: " + context + "/" + status.Provider);
            }
        }
        Check(combinations == 2 * 6 * 6 * 6, "All 432 Schedule/provider combinations tested");

        foreach (var schedule in new[] { full, burger })
        {
            Check(TrayPresentation.Calculate(schedule.State, Array.Empty<ProviderStatus>()).Attention == TrayAttention.Gray,
                "Empty provider list is unknown, not healthy");
            foreach (var missing in Enum.GetValues<ProviderKind>())
            {
                var available = Enum.GetValues<ProviderKind>().Where(p => p != missing)
                    .Select(p => Status(p, OfficialStatus.Operational)).ToArray();
                Check(TrayPresentation.Calculate(schedule.State, available).Attention == TrayAttention.Gray,
                    "A missing provider keeps aggregation uncertain: " + missing);
                string expected = schedule.State == AgentState.FullThrottle ? "CHECK" : "BURGER+CHECK";
                Check(TrayPresentation.Tooltip(schedule, available).Split('\n').Contains(missing + " " + expected),
                    "Missing provider appears as CHECK in tooltip: " + missing);
                available[0] = available[0] with { Status = OfficialStatus.PartialOutage };
                Check(TrayPresentation.Calculate(schedule.State, available).Attention == TrayAttention.Red,
                    "Confirmed outage takes priority over missing information");
                available[0] = available[0] with { Status = OfficialStatus.Degraded };
                Check(TrayPresentation.Calculate(schedule.State, available).Attention == TrayAttention.Orange,
                    "Confirmed degradation takes priority over missing information");
            }
        }

        var holiday = AgentSchedule.GetSnapshot(new DateTimeOffset(2026, 9, 7, 14, 0, 0, TimeSpan.Zero), true);
        var unadjustedHoliday = AgentSchedule.GetSnapshot(holiday.NowUtc, false);
        var allStale = Enum.GetValues<ProviderKind>().Select(p => Status(p, OfficialStatus.Stale)).ToArray();
        Check(holiday.State == AgentState.FullThrottle && holiday.IsHolidayExtendedFullThrottle,
            "Labor Day fixture uses holiday-aware Schedule");
        Check(unadjustedHoliday.State == AgentState.BurgerTime && !unadjustedHoliday.IsHolidayExtendedFullThrottle,
            "Same instant without holiday adjustment stays Burger");
        string holidayTooltip = TrayPresentation.Tooltip(holiday, allStale);
        Check(holidayTooltip.Contains("공휴일", StringComparison.Ordinal) && holidayTooltip.Length <= 127,
            "Holiday marker remains within tooltip limit with three stale providers");
        Check(!TrayPresentation.Tooltip(unadjustedHoliday, allStale).Contains("공휴일", StringComparison.Ordinal),
            "No holiday marker when policy is disabled");
        Check(TrayPresentation.Tooltip(burger, allStale).Length <= 127,
            "Longest ordinary all-STALE Burger tooltip fits NotifyIcon");

        foreach (var state in Enum.GetValues<AgentState>())
        foreach (var attention in Enum.GetValues<TrayAttention>())
        {
            var appearance = new TrayAppearance(state, attention);
            using var icon = TrayApplicationContext.CreateStatusIcon(appearance);
            Check(icon.Width == 32 && icon.Height == 32, "Native icon dimensions: " + appearance);
            foreach (int size in new[] { 32, 16 })
            {
                using var bitmap = new Bitmap(size, size);
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.DrawIcon(icon, new Rectangle(0, 0, size, size));
                // Left-middle interior avoids both the anti-aliased outer edge and the white F/B glyph.
                Color sample = bitmap.GetPixel(size / 5, size / 2);
                Color expected = appearance.Color;
                Check(sample.A > 240 && Math.Abs(sample.R - expected.R) <= 15 &&
                    Math.Abs(sample.G - expected.G) <= 15 && Math.Abs(sample.B - expected.B) <= 15,
                    "Rendered icon background at " + size + "px: " + appearance);
                bool lightGlyphPresent = false;
                for (int y = size / 4; y < size * 3 / 4; y++)
                for (int x = size / 4; x < size * 3 / 4; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    lightGlyphPresent |= pixel.A > 200 && pixel.R > 220 && pixel.G > 220 && pixel.B > 220;
                }
                Check(lightGlyphPresent, "Light glyph pixels survive " + size + "px rendering: " + appearance);
            }
        }
        return count;
    }

    private static ProviderStatus Status(ProviderKind provider, OfficialStatus status) =>
        new(provider, status, new DateTimeOffset(2026, 6, 16, 5, 0, 0, TimeSpan.Zero), null, "Fixture");

    private static Recommendation ExpectedRecommendation(AgentState schedule, OfficialStatus official) =>
        (schedule, official) switch
        {
            (AgentState.FullThrottle, OfficialStatus.Operational) => Recommendation.Go,
            (AgentState.FullThrottle, OfficialStatus.Degraded) => Recommendation.Hold,
            (AgentState.FullThrottle, OfficialStatus.PartialOutage or OfficialStatus.MajorOutage) => Recommendation.Stop,
            (AgentState.FullThrottle, _) => Recommendation.Check,
            (_, OfficialStatus.Operational) => Recommendation.BurgerTime,
            (_, OfficialStatus.Unknown or OfficialStatus.Stale) => Recommendation.BurgerCheck,
            _ => Recommendation.BurgerServiceIssue
        };
}
