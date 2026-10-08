namespace AiBurgerClock;

internal static class PortablePlatformTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Shared platform: " + label);
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Check(AppPaths.DataDirectoryFor(false, home, local) == Path.Combine(local, "AIBurgerClock"),
            "Windows data directory is unchanged");
        Check(AppPaths.DataDirectoryFor(true, home, local) == Path.Combine(home, "Library", "Application Support", "AIBurgerClock"),
            "macOS uses Application Support");
        Check(new UsageStore().DatabasePath == Path.Combine(AppPaths.DataDirectory, "burgerclock.db"),
            "store default uses shared path (no database opened)");

        Check(DisplayFormatting.FormatRemaining(TimeSpan.FromHours(60)) == "60:00:00", "extended weekend countdown");
        Check(DisplayFormatting.FormatRemaining(TimeSpan.FromSeconds(-1)) == "00:00:00", "elapsed countdown clamped");
        Check(DisplayFormatting.Offset(-240) == "UTC-4" && DisplayFormatting.Offset(330) == "UTC+5:30", "UTC offsets");
        var now = DateTimeOffset.Parse("2026-10-03T06:00:00Z");
        Check(DisplayFormatting.ResetCountdown(null, now) == "리셋 미제공", "missing reset");
        Check(DisplayFormatting.ResetCountdown(now, now) == "갱신 대기", "elapsed reset is not inferred recovery");
        Check(DisplayFormatting.ResetCountdown(now.AddMinutes(15), now) == "00:15:00", "reset countdown");
        Check(DisplayFormatting.ResetCountdown(now.AddHours(26), now) == "1일 02:00", "weekly reset countdown");

        // Exercise native IANA identifiers even on Windows; actual Mac execution
        // must still run this suite against that machine's OS timezone data.
        var eastern = TimeZoneInfo.FindSystemTimeZoneById(AgentSchedule.EasternIanaTimeZoneId);
        var pacific = TimeZoneInfo.FindSystemTimeZoneById(AgentSchedule.PacificIanaTimeZoneId);
        var korea = TimeZoneInfo.FindSystemTimeZoneById(AgentSchedule.KoreaIanaTimeZoneId);
        foreach ((DateTime date, int startHour, int endHour, bool dst) in new[]
        {
            (new DateTime(2026, 1, 12), 23, 11, false),
            (new DateTime(2026, 7, 13), 22, 10, true),
            (new DateTime(2026, 3, 6), 23, 11, false),
            (new DateTime(2026, 3, 9), 22, 10, true),
            (new DateTime(2026, 10, 30), 22, 10, true),
            (new DateTime(2026, 11, 2), 23, 11, false)
        })
        {
            DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.AddHours(9), DateTimeKind.Unspecified), eastern);
            DateTime endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.AddHours(18), DateTimeKind.Unspecified), pacific);
            DateTime startKst = TimeZoneInfo.ConvertTimeFromUtc(startUtc, korea);
            DateTime endKst = TimeZoneInfo.ConvertTimeFromUtc(endUtc, korea);
            Check(startKst.Date == date.Date && startKst.Hour == startHour, "IANA Eastern start " + date.ToString("yyyy-MM-dd"));
            Check(endKst.Date == date.AddDays(1).Date && endKst.Hour == endHour, "IANA Pacific end " + date.ToString("yyyy-MM-dd"));
            Check(eastern.IsDaylightSavingTime(startUtc) == dst && pacific.IsDaylightSavingTime(endUtc) == dst,
                "IANA transition-week DST " + date.ToString("yyyy-MM-dd"));
        }

        string[] paths = ["", ".", "relative", "/", "/Users/mac/project", "/Users/mac/project/.",
            "/Users/mac/../bin", "\"/custom tools/bin\"", "/usr/local/bin/"];
        var candidates = MacCliPaths.Candidates(QuotaProvider.Codex, paths, "/Users/mac", "/Users/mac/project");
        Check(candidates.Count == 4, "absolute installation directories only and duplicates removed");
        Check(candidates.Contains("/custom tools/bin/codex"), "spaces preserved without shell escaping");
        Check(candidates.Contains("/Users/mac/.local/bin/codex") && candidates.Contains("/opt/homebrew/bin/codex"),
            "native and Homebrew fallbacks");
        Check(!candidates.Any(path => path.Contains("project", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal)),
            "no current directory or dot-segment lookup");
        Check(MacCliPaths.Candidates(QuotaProvider.Claude, [], "/Users/mac", "/")
            .All(path => path.EndsWith("/claude", StringComparison.Ordinal)), "separate Claude executable");
        Check(MacCliPaths.Candidates(QuotaProvider.Gemini, [], "/Users/mac", "/")
            .Contains("/Users/mac/.local/bin/agy"), "native agy fallback without shell lookup");
        Check(MacCliPaths.Candidates(QuotaProvider.Gemini, paths, "/Users/mac", "/Users/mac/project")
            .All(path => path.EndsWith("/agy", StringComparison.Ordinal) && !path.Contains("project", StringComparison.Ordinal)),
            "Gemini uses agy, never the deprecated gemini binary or current directory");
        // Hosts may relabel providers in the tray tooltip; the default stays the enum name (Windows).
        var tooltipAt = AgentSchedule.GetSnapshot(DateTimeOffset.Parse("2026-10-03T06:00:00Z"), true);
        ProviderStatus[] healthy = Enum.GetValues<ProviderKind>().Select(provider =>
            new ProviderStatus(provider, OfficialStatus.Operational, tooltipAt.NowUtc, tooltipAt.NowUtc, "정상")).ToArray();
        string defaultTip = TrayPresentation.Tooltip(tooltipAt, healthy);
        string renamedTip = TrayPresentation.Tooltip(tooltipAt, healthy,
            provider => provider == ProviderKind.OpenAI ? "ChatGPT" : provider.ToString());
        Check(defaultTip.Contains("\nOpenAI GO", StringComparison.Ordinal) && !defaultTip.Contains("ChatGPT", StringComparison.Ordinal),
            "tray tooltip keeps enum provider names by default");
        Check(renamedTip.Contains("\nChatGPT GO", StringComparison.Ordinal) && renamedTip.Contains("\nClaude GO", StringComparison.Ordinal) &&
            !renamedTip.Contains("OpenAI", StringComparison.Ordinal), "tray tooltip uses a host display name");
        Check(DisplayFormatting.FormatRemainingMinutes(TimeSpan.FromSeconds(3_659)) == "01:00" &&
            DisplayFormatting.FormatRemainingMinutes(TimeSpan.FromHours(60)) == "60:00" &&
            DisplayFormatting.FormatRemainingMinutes(TimeSpan.FromSeconds(-1)) == "00:00", "minute-precision countdown");
        var withinMinute = AgentSchedule.GetSnapshot(DateTimeOffset.Parse("2026-10-03T06:00:20Z"), true);
        var tenSecondsLater = AgentSchedule.GetSnapshot(DateTimeOffset.Parse("2026-10-03T06:00:30Z"), true);
        Check(TrayPresentation.Tooltip(withinMinute, healthy, minutePrecision: true) ==
                TrayPresentation.Tooltip(tenSecondsLater, healthy, minutePrecision: true) &&
            TrayPresentation.Tooltip(withinMinute, healthy) != TrayPresentation.Tooltip(tenSecondsLater, healthy),
            "minute-precision tooltip stays the same within a minute; the default still shows seconds");
        return count;
    }
}
