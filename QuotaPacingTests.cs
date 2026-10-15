using System.Globalization;

namespace AiBurgerClock;

// Pure synthetic fixtures: no clocks, native UI, account access or file operations.
internal static class QuotaPacingTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota pacing: " + label);
        }
        void Same(string actual, string expected, string label) =>
            Check(actual == expected, $"{label}\n  expected: {expected}\n  actual:   {actual}");
        void Pace(QuotaPace pace, double elapsed, double difference, string detail, string label)
        {
            Check(pace.ElapsedPercent is { } time && Math.Abs(time - elapsed) < 0.0000001, label + " elapsed");
            Check(pace.DifferencePercentagePoints is { } delta && Math.Abs(delta - difference) < 0.0000001,
                label + " difference");
            Same(pace.Detail, detail, label + " detail");
        }
        void Unavailable(QuotaPace pace, string reason, string label)
        {
            Check(pace.ElapsedPercent is null, label + " no time fill");
            Check(pace.DifferencePercentagePoints is null, label + " no difference");
            Same(pace.Detail, "시간 비교: " + reason, label + " reason");
        }

        DateTimeOffset success = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var fiveHours = new QuotaWindow("synthetic-5h", "합성 5시간", 55, success.AddHours(3), 300);
        var weekly = new QuotaWindow("synthetic-weekly", "합성 주간", 10, success.AddHours(126), 10080);

        Pace(QuotaPacing.Calculate(fiveHours, success, false, success), 40, 15,
            "시간 경과 40% · 균등 사용 기준보다 15%p 빠름", "five-hour faster");
        Pace(QuotaPacing.Calculate(weekly, success, false, success), 25, -15,
            "시간 경과 25% · 균등 사용 기준보다 15%p 느림", "weekly slower");
        Pace(QuotaPacing.Calculate(fiveHours with { UsedPercent = 40 }, success, false, success), 40, 0,
            "시간 경과 40% · 균등 사용 기준과 비슷함", "equal");
        Pace(QuotaPacing.Calculate(fiveHours with { UsedPercent = 100 }, success, false, success), 40, 60,
            "시간 경과 40% · 균등 사용 기준보다 60%p 빠름", "exhausted is still a comparison");
        Pace(QuotaPacing.Calculate(weekly with { UsedPercent = 0 }, success, false, success), 25, -25,
            "시간 경과 25% · 균등 사용 기준보다 25%p 느림", "zero weekly has a reference");
        Pace(QuotaPacing.Calculate(fiveHours with { ResetsAtUtc = success.AddHours(5) }, success, false, success), 0, 55,
            "시간 경과 0% · 균등 사용 기준보다 55%p 빠름", "exact inferred start");
        var day = new QuotaWindow("synthetic-day", "주기명으로 추정하지 않음", 30, success.AddHours(18), 1440);
        Pace(QuotaPacing.Calculate(day, success, false, success), 25, 5,
            "시간 경과 25% · 균등 사용 기준보다 5%p 빠름", "duration metadata controls calculation");
        Pace(QuotaPacing.Calculate(fiveHours with { WindowMinutes = 600 }, success, false, success), 70, -15,
            "시간 경과 70% · 균등 사용 기준보다 15%p 느림", "metadata overrides five-hour label");
        Pace(QuotaPacing.Calculate(fiveHours with { WindowMinutes = 1, ResetsAtUtc = success.AddSeconds(36) }, success, false, success), 40, 15,
            "시간 경과 40% · 균등 사용 기준보다 15%p 빠름", "one-minute metadata is supported");
        Pace(QuotaPacing.Calculate(fiveHours with { WindowMinutes = 5_256_000, ResetsAtUtc = success.AddDays(2190) }, success, false, success), 40, 15,
            "시간 경과 40% · 균등 사용 기준보다 15%p 빠름", "maximum normalized duration is supported");
        Pace(QuotaPacing.Calculate(fiveHours with { ResetsAtUtc = DateTimeOffset.MinValue.AddHours(3) }, DateTimeOffset.MinValue,
            false, DateTimeOffset.MinValue), 40, 15,
            "시간 경과 40% · 균등 사용 기준보다 15%p 빠름", "minimum date needs no overflowing inferred-start subtraction");
        Pace(QuotaPacing.Calculate(fiveHours with { ResetsAtUtc = DateTimeOffset.MaxValue }, DateTimeOffset.MaxValue.AddHours(-3),
            false, DateTimeOffset.MaxValue.AddHours(-3)), 40, 15,
            "시간 경과 40% · 균등 사용 기준보다 15%p 빠름", "maximum date needs no inferred-end addition");

        // Provider identities and row labels do not contribute to the calculation.
        foreach (string id in new[] { "codex:primary", "session", "gemini-5h" })
            Pace(QuotaPacing.Calculate(fiveHours with { Id = id }, success, false, success), 40, 15,
                "시간 경과 40% · 균등 사용 기준보다 15%p 빠름", id);
        foreach (string id in new[] { "codex:secondary", "weekly_all", "weekly_scoped:synthetic", "gemini-weekly" })
            Pace(QuotaPacing.Calculate(weekly with { Id = id }, success, false, success), 25, -15,
                "시간 경과 25% · 균등 사용 기준보다 15%p 느림", id);

        // Availability uses now, but the two ratios remain pinned to the same receipt.
        QuotaPace original = QuotaPacing.Calculate(fiveHours, success, false, success);
        Check(QuotaPacing.Calculate(fiveHours, success, false, success.AddHours(2)) == original,
            "local time alone does not move the reference or difference");
        Pace(QuotaPacing.Calculate(fiveHours, success.AddHours(1), false, success.AddHours(1)), 60, -5,
            "시간 경과 60% · 균등 사용 기준보다 5%p 느림", "new successful receipt updates reference");
        Check(fiveHours.UsedPercent == 55 && fiveHours.ResetsAtUtc == success.AddHours(3), "source window is unchanged");

        Unavailable(QuotaPacing.Calculate(fiveHours, success, true, success), "이전 조회값", "previous reading");
        Unavailable(QuotaPacing.Calculate(fiveHours, null, false, success), "마지막 성공 조회 시각 없음", "missing receipt");
        foreach (double used in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.1, 100.1 })
            Unavailable(QuotaPacing.Calculate(fiveHours with { UsedPercent = used }, success, false, success),
                "사용 비율 확인 불가", "invalid use percentage");
        Unavailable(QuotaPacing.Calculate(fiveHours with { ResetsAtUtc = null }, success, false, success),
            "리셋 시각 미제공", "missing reset");
        foreach (int? minutes in new int?[] { null, 0, -1, 5_256_001, int.MaxValue })
            Unavailable(QuotaPacing.Calculate(fiveHours with { WindowMinutes = minutes }, success, false, success),
                "창 길이 미확인", "missing or unsupported duration");
        Unavailable(QuotaPacing.Calculate(fiveHours, success.AddTicks(1), false, success),
            "조회 시각 확인 필요", "future receipt");
        Unavailable(QuotaPacing.Calculate(fiveHours, success, false, success.AddHours(3)),
            "리셋 예정 시각 경과", "reset reached now");
        Unavailable(QuotaPacing.Calculate(fiveHours, success, false, success.AddHours(4)),
            "리셋 예정 시각 경과", "reset passed now");
        Unavailable(QuotaPacing.Calculate(fiveHours with { ResetsAtUtc = success }, success, false, success),
            "리셋 예정 시각 경과", "receipt at reset");
        Unavailable(QuotaPacing.Calculate(fiveHours with { ResetsAtUtc = success.AddTicks(-1) }, success, false, success),
            "리셋 예정 시각 경과", "receipt after reset");
        Unavailable(QuotaPacing.Calculate(fiveHours with { ResetsAtUtc = success.AddHours(5).AddTicks(1) }, success, false, success),
            "조회 시각과 한도 기간 불일치", "receipt before inferred start");
        Unavailable(QuotaPacing.Calculate(fiveHours with { UsedPercent = 0 }, success, false, success),
            "5시간 창 시작 확인 불가", "zero five-hour reading");
        Unavailable(QuotaPacing.Calculate(fiveHours with { Id = "weekly", Label = "주간", UsedPercent = 0 }, success, false, success),
            "5시간 창 시작 확인 불가", "five-hour guard follows metadata, not label");

        // Ratios at a non-round instant retain precision; only shared wording rounds.
        var fractional = fiveHours with { UsedPercent = 42.36, ResetsAtUtc = success.AddMinutes(180.5) };
        Pace(QuotaPacing.Calculate(fractional, success, false, success), 39.83333333333333, 2.52666666666667,
            "시간 경과 39.8% · 균등 사용 기준보다 2.5%p 빠름", "fractional reference");
        var halfway = fiveHours with { ResetsAtUtc = success.AddMinutes(150), UsedPercent = 50.25 };
        Pace(QuotaPacing.Calculate(halfway, success, false, success), 50, 0.25,
            "시간 경과 50% · 균등 사용 기준보다 0.3%p 빠름", "positive midpoint rounds away from zero");
        Pace(QuotaPacing.Calculate(halfway with { UsedPercent = 49.75 }, success, false, success), 50, -0.25,
            "시간 경과 50% · 균등 사용 기준보다 0.3%p 느림", "negative midpoint rounds away from zero");
        foreach (double used in new[] { 49.96, 50.04 })
            Same(QuotaPacing.Calculate(halfway with { UsedPercent = used }, success, false, success).Detail,
                "시간 경과 50% · 균등 사용 기준과 비슷함", "displayed zero has no negative zero or direction");

        Check(QuotaPacing.Calculate(fiveHours with { ResetsAtUtc = fiveHours.ResetsAtUtc!.Value.ToOffset(TimeSpan.FromHours(9)) },
            success.ToOffset(TimeSpan.FromHours(-7)), false, success.ToOffset(TimeSpan.FromHours(1))) == original,
            "different offsets identify the same instants");
        DateTimeOffset spring = new(2026, 3, 8, 1, 30, 0, TimeSpan.FromHours(-5));
        var springWindow = fiveHours with { ResetsAtUtc = new DateTimeOffset(2026, 3, 8, 5, 30, 0, TimeSpan.FromHours(-4)) };
        Pace(QuotaPacing.Calculate(springWindow, spring, false, spring), 40, 15,
            "시간 경과 40% · 균등 사용 기준보다 15%p 빠름", "spring DST uses actual elapsed hours");
        DateTimeOffset fall = new(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-4));
        var fallWindow = fiveHours with { ResetsAtUtc = new DateTimeOffset(2026, 11, 1, 3, 30, 0, TimeSpan.FromHours(-5)) };
        Pace(QuotaPacing.Calculate(fallWindow, fall, false, fall), 40, 15,
            "시간 경과 40% · 균등 사용 기준보다 15%p 빠름", "fall DST uses actual elapsed hours");

        foreach ((TimeSpan age, string text) in new[]
        {
            (TimeSpan.Zero, "방금 전"),
            (TimeSpan.FromSeconds(59.999), "방금 전"),
            (TimeSpan.FromMinutes(1), "1분 전"),
            (TimeSpan.FromMinutes(59).Add(TimeSpan.FromSeconds(59)), "59분 전"),
            (TimeSpan.FromHours(1), "1시간 전"),
            (TimeSpan.FromMinutes(61), "1시간 1분 전"),
            (TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)), "23시간 59분 전"),
            (TimeSpan.FromDays(1), "1일 전"),
            (TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(59)), "1일 전"),
            (TimeSpan.FromDays(1).Add(TimeSpan.FromHours(7)), "1일 7시간 전"),
            (TimeSpan.FromDays(2), "2일 전")
        })
            Same(QuotaPacing.ReferenceAge(success, success + age), "마지막 조회 기준, " + text, "reference age " + text);
        Same(QuotaPacing.ReferenceAge(success, success.AddTicks(-1)), "마지막 조회 기준, 시각 확인 필요",
            "future age is not misrepresented as just now");
        Same(QuotaPacing.ReferenceAge(success.ToOffset(TimeSpan.FromHours(9)), success.AddHours(2).ToOffset(TimeSpan.FromHours(-5))),
            "마지막 조회 기준, 2시간 전", "age uses UTC instants");
        Same(QuotaPacing.ReferenceAge(spring, springWindow.ResetsAtUtc!.Value), "마지막 조회 기준, 3시간 전", "age across spring DST");
        Same(QuotaPacing.ReferenceAge(fall, fallWindow.ResetsAtUtc!.Value), "마지막 조회 기준, 3시간 전", "age across fall DST");

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "ko-KR", "en-US", "de-DE", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Same(QuotaPacing.Calculate(halfway, success, false, success).Detail,
                    "시간 경과 50% · 균등 사용 기준보다 0.3%p 빠름", name + " decimal separator");
                Same(QuotaPacing.ReferenceAge(success, success.AddMinutes(61)), "마지막 조회 기준, 1시간 1분 전", name + " reference age");
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        return count;
    }
}
