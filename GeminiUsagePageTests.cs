namespace AiBurgerClock;

internal static class GeminiUsagePageTests
{
    internal static readonly DateTimeOffset Now = new(2035, 4, 5, 10, 0, 0, TimeSpan.Zero);
    internal const string English = """
        Current usage
        info
        37% used
        Resets at 12:00 PM
        Weekly limit
        Resets Apr 9 at 9:25 AM
        61% used
        """;

    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string test)
        {
            if (!condition) throw new InvalidOperationException("Gemini usage parser: " + test);
            count++;
        }
        GeminiUsageResult Parse(string? text, string url = GeminiUsagePage.Url) =>
            GeminiUsagePage.Parse(new(url, text), Now, TimeZoneInfo.Utc);
        void Changed(string? text, GeminiUsageFailureRule expected)
        {
            var result = Parse(text);
            Check(result.Outcome == GeminiUsageOutcome.FormatChanged && result.Reading is null &&
                  result.Diagnostic == new GeminiUsageDiagnostic(expected),
                expected.ToString());
        }

        var normal = Parse(English);
        Check(normal is { Outcome: GeminiUsageOutcome.Ok, Diagnostic: null } &&
            GeminiUsagePage.IsWebReading(normal.Reading), "accept observed English shape");
        var rows = normal.Reading!.Windows;
        Check(rows[0] == new QuotaWindow(GeminiUsagePage.FiveHourId, "5시간", 37, Utc(2035, 4, 5, 12, 0), 300),
            "five-hour row uses its web-only ID and instant");
        Check(rows[1] == new QuotaWindow(GeminiUsagePage.WeeklyId, "주간", 61, Utc(2035, 4, 9, 9, 25), 10080),
            "weekly row uses its web-only ID and instant");
        const string reordered = """

          SYNTHETIC-PLAN
          Updated a few minutes ago
          Weekly limit
          61% used
          Resets Apr 9 at 9:25 AM
          Description for the weekly limit

          Current usage
          Resets at 12:00 PM
          info
          The % used value can take a few minutes to update.
          37% used
          Get SYNTHETIC-UPGRADE
          USD 42 / month
          Upgrade
          """;
        Check(Parse(reordered.Replace("\n", "\r\n")).Reading?.Windows.SequenceEqual(rows) == true,
            "labels and field order are independent; surrounding page copy is ignored");
        Check(Parse(English.Replace("37%", "0%").Replace("61%", "100%")).Reading?.Windows
            .Select(row => row.UsedPercent).SequenceEqual([0.0, 100.0]) == true, "usage endpoints are inclusive");
        Check(Parse(English.Replace("at 12:00 PM", "Apr 5 at 12:00 PM").Replace("Apr 9 at 9:25 AM", "at 1:15 PM"))
            .Reading?.Windows[1].ResetsAtUtc == Utc(2035, 4, 5, 13, 15), "both sections accept both reset formats");

        Changed("""
            현재 사용량
            42% 사용됨
            오후 2:00에 초기화
            주간 한도
            58% 사용됨
            """, GeminiUsageFailureRule.UsageOutsideSection);
        Changed(null, GeminiUsageFailureRule.MissingElement);
        Changed(" \n  ", GeminiUsageFailureRule.MissingSection);
        Changed(English[..English.IndexOf("Weekly limit", StringComparison.Ordinal)], GeminiUsageFailureRule.MissingSection);
        Changed(English + "\nWeekly limit", GeminiUsageFailureRule.DuplicateSection);
        Changed(English.Replace("37% used", "37% used\n37% used"), GeminiUsageFailureRule.DuplicateUsage);
        Changed(English.Replace("61% used", "No quota value"), GeminiUsageFailureRule.MissingUsage);
        foreach (string invalid in new[] { "101% used", "<1% used", "11.5% used", "2910% used", "5% Used", "1％ used" })
            Changed(English.Replace("37% used", invalid), GeminiUsageFailureRule.InvalidUsage);
        Changed("Another limit\n19% used\n" + English, GeminiUsageFailureRule.UsageOutsideSection);
        Changed(English + "\nAnother limit\n19% used", GeminiUsageFailureRule.DuplicateUsage);
        Changed(English.Replace("37% used", "Get SYNTHETIC-UPGRADE\n37% used"), GeminiUsageFailureRule.UsageOutsideSection);
        Changed(English + "\nUpgrade\n19% used", GeminiUsageFailureRule.UsageOutsideSection);
        Changed(English.Replace("Resets at 12:00 PM", "Unavailable"), GeminiUsageFailureRule.MissingReset);
        Changed(English.Replace("Resets at 12:00 PM", "Resets"), GeminiUsageFailureRule.InvalidReset);
        Changed(English.Replace("Resets at 12:00 PM", "Resets at 12:00 PM\nResets at 12:00 PM"), GeminiUsageFailureRule.DuplicateReset);
        foreach (string space in new[] { "\t", "\u00a0" })
            Changed(English.Replace("Resets at 12:00 PM", "Resets at 12:00 PM\nResets" + space + "at 1:00 PM"),
                GeminiUsageFailureRule.InvalidReset);
        foreach (string invalid in new[] { "at 15:00", "at 12:70 PM", "Apr 31 at 12:00 PM", "tomorrow at 12:00 PM", "at 12:00 PM UTC" })
            Changed(English.Replace("at 12:00 PM", invalid), GeminiUsageFailureRule.InvalidReset);

        foreach (string redirect in new[] { "https://gemini.google.com/app", "https://accounts.google.com/signin",
            "https://example.test/usage", "https://gemini.google.com.example.test/usage", "http://gemini.google.com/usage",
            "https://gemini.google.com:444/usage", "https://gemini.google.com/usage-other", "not an absolute URL" })
        {
            Check(Parse(English, redirect) == new GeminiUsageResult(GeminiUsageOutcome.LoginRequired,
                Diagnostic: new(GeminiUsageFailureRule.Destination)), "redirect is login required");
        }
        Check(GeminiUsagePage.Parse(GeminiUsagePageLoad.LoadFailed, Now, TimeZoneInfo.Utc) ==
            new GeminiUsageResult(GeminiUsageOutcome.LoadFailed, Diagnostic: new(GeminiUsageFailureRule.PageLoad)),
            "navigation error is separate from login and format");
        var sensitive = new GeminiUsagePageLoad(GeminiUsagePage.Url + "&fixture=PRIVATE-QUERY",
            "PRIVATE-PLAN-LABEL\nCurrent usage\n<1% used");
        var diagnostic = GeminiUsagePage.Parse(sensitive, Now, TimeZoneInfo.Utc);
        Check(diagnostic == new GeminiUsageResult(GeminiUsageOutcome.FormatChanged, Diagnostic: new(GeminiUsageFailureRule.InvalidUsage))
              && !diagnostic.ToString().Contains("PRIVATE-", StringComparison.Ordinal) &&
              !sensitive.ToString().Contains("PRIVATE-", StringComparison.Ordinal),
            "diagnostic and load summary cannot expose URL queries or text");

        foreach (var (five, week, expectedFive, expectedWeek, label) in new[]
        {
            ("at 9:45 AM", "Apr 5 at 9:45 AM", Utc(2035, 4, 5, 9, 45), Utc(2035, 4, 5, 9, 45), "minus fifteen-minute boundary"),
            ("at 3:15 PM", "Apr 12 at 10:15 AM", Utc(2035, 4, 5, 15, 15), Utc(2035, 4, 12, 10, 15), "upper boundaries")
        })
        {
            var result = Parse(English.Replace("at 12:00 PM", five).Replace("Apr 9 at 9:25 AM", week));
            Check(result.Reading?.Windows.Select(row => row.ResetsAtUtc).SequenceEqual([expectedFive, expectedWeek]) == true, label);
        }
        foreach (string outside in new[] { "at 9:44 AM", "at 3:16 PM" })
            Changed(English.Replace("at 12:00 PM", outside), GeminiUsageFailureRule.ResetOutsideWindow);
        foreach (string outside in new[] { "Apr 5 at 9:44 AM", "Apr 12 at 10:16 AM" })
            Changed(English.Replace("Apr 9 at 9:25 AM", outside), GeminiUsageFailureRule.ResetOutsideWindow);
        Check(Parse(English.Replace("Apr 9 at 9:25 AM", "at 9:25 PM"))
            .Reading?.Windows[1].ResetsAtUtc == Utc(2035, 4, 5, 21, 25),
            "a time-only weekly reset may be more than five hours away on the same day");
        Check(Parse(English.Replace("at 12:00 PM", "Apr 5 at 3:15 PM"))
            .Reading?.Windows[0].ResetsAtUtc == Utc(2035, 4, 5, 15, 15),
            "a dated five-hour reset includes the upper boundary");
        Changed(English.Replace("at 12:00 PM", "Apr 5 at 3:16 PM"), GeminiUsageFailureRule.ResetOutsideWindow);

        var korea = TimeZoneInfo.CreateCustomTimeZone("synthetic plus nine", TimeSpan.FromHours(9), "plus nine", "plus nine");
        var minusFour = TimeZoneInfo.CreateCustomTimeZone("synthetic minus four", TimeSpan.FromHours(-4), "minus four", "minus four");
        var crossed = GeminiUsagePage.Parse(new(GeminiUsagePage.Url,
            English.Replace("at 12:00 PM", "at 12:10 AM").Replace("Apr 9 at 9:25 AM", "Jan 3 at 9:25 AM")),
            Utc(2035, 12, 31, 14, 55), korea);
        Check(crossed.Reading?.Windows[0].ResetsAtUtc == Utc(2035, 12, 31, 15, 10), "local midnight rolls to the next day");
        Check(crossed.Reading?.Windows[1].ResetsAtUtc == Utc(2036, 1, 3, 0, 25), "December selects only next year's January within range");
        var datedFiveHour = GeminiUsagePage.Parse(new(GeminiUsagePage.Url,
            English.Replace("at 12:00 PM", "Jan 1 at 12:10 AM").Replace("Apr 9 at 9:25 AM", "Jan 3 at 9:25 AM")),
            Utc(2035, 12, 31, 14, 55), korea);
        Check(datedFiveHour.Reading?.Windows[0].ResetsAtUtc == Utc(2035, 12, 31, 15, 10),
            "a dated five-hour reset can cross local midnight and the year boundary");
        var previousYear = GeminiUsagePage.Parse(new(GeminiUsagePage.Url,
            English.Replace("at 12:00 PM", "at 1:30 AM").Replace("Apr 9 at 9:25 AM", "Dec 31 at 11:57 PM")),
            Utc(2036, 1, 1, 4, 5), minusFour);
        Check(previousYear.Reading?.Windows[0].ResetsAtUtc == Utc(2036, 1, 1, 5, 30), "local clock uses the injected offset");
        Check(previousYear.Reading?.Windows[1].ResetsAtUtc == Utc(2036, 1, 1, 3, 57), "only the previous local year is in range");

        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        string foldText = English.Replace("at 12:00 PM", "at 1:30 AM").Replace("Apr 9 at 9:25 AM", "Nov 5 at 9:25 AM");
        var fold = GeminiUsagePage.Parse(new(GeminiUsagePage.Url, foldText), Utc(2035, 11, 4, 4, 30), eastern);
        Check(fold.Reading?.Windows[0].ResetsAtUtc == Utc(2035, 11, 4, 5, 30),
            "a time-only reset selects the first occurrence of a repeated local time");
        var laterFold = GeminiUsagePage.Parse(new(GeminiUsagePage.Url, foldText), Utc(2035, 11, 4, 6, 0), eastern);
        Check(laterFold.Reading?.Windows[0].ResetsAtUtc == Utc(2035, 11, 4, 6, 30),
            "a time-only reset skips the occurrence before the lower boundary");
        var datedFold = GeminiUsagePage.Parse(new(GeminiUsagePage.Url, foldText.Replace("at 1:30 AM", "Nov 4 at 1:30 AM")),
            Utc(2035, 11, 4, 4, 30), eastern);
        Check(datedFold is { Outcome: GeminiUsageOutcome.FormatChanged, Diagnostic.Rule: GeminiUsageFailureRule.ResetOutsideWindow },
            "a dated reset must still identify a single instant");
        var gapEarlierToday = GeminiUsagePage.Parse(new(GeminiUsagePage.Url,
            English.Replace("at 12:00 PM", "at 2:30 AM").Replace("Apr 9 at 9:25 AM", "Mar 13 at 9:25 AM")),
            Utc(2035, 3, 12, 3, 30), eastern);
        Check(gapEarlierToday.Reading?.Windows[0].ResetsAtUtc == Utc(2035, 3, 12, 6, 30),
            "a time already passed on a spring-forward day can refer to tomorrow");
        return count;
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
