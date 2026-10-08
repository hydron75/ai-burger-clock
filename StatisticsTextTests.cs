using System.Globalization;

namespace AiBurgerClock;

// Golden checks for the shared statistics text. Expected strings are the Windows 2.2.3
// StatisticsWindow text; no UI, database or user data.
internal static class StatisticsTextTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Statistics text: " + label);
        }
        void Same(string actual, string expected, string label) =>
            Check(actual == expected, $"{label}\n  expected: {expected}\n  actual:   {actual}");

        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Same(StatisticsText.Loading, "불러오는 중…", "loading");
            Same(string.Join("|", StatisticsText.Periods), "최근 7일|최근 30일|전체", "period choices");
            Same(string.Join("|", StatisticsText.Sections), "Provider 비교|KST 시간대|Schedule / DST|공식 상태 × 체감|공식 정상 시간대",
                "section choices");
            Same(StatisticsText.Explanation,
                "각 비율의 분모는 해당 행의 n입니다. 문제 체감 = Slow + Error + Interrupted. n < 30은 소표본입니다.\n" +
                "직접 선택해 남긴 체감 기록이며 전체 사용의 장애율·인과관계를 뜻하지 않습니다. 공식 상태와 체감은 별개입니다.\n" +
                "Schedule 행은 겹치는 집단입니다. 정책별·공휴일 ON/OFF별 행으로 비교하세요. 이전 기록은 재분류하지 않습니다.",
                "explanation");

            Same(StatisticsText.Summary(0, "No data", 0), "직접 기록한 표본 n = 0 · 정책: No data", "empty summary");
            Same(StatisticsText.Summary(1234, "us-business-et09-pt18-v1, us-business-et09-pt18-holidays-v2", 3),
                "직접 기록한 표본 n = 1,234 · 정책: us-business-et09-pt18-v1, us-business-et09-pt18-holidays-v2 · 읽을 수 없는 기록 3건 제외",
                "summary with skipped rows");
            Same(StatisticsText.Empty("Provider 행이나 트레이 메뉴에서 사용 경험을 기록하세요."),
                "No data · Provider 행이나 트레이 메뉴에서 사용 경험을 기록하세요.", "Windows empty hint");
            Same(StatisticsText.ReadFailed("locked"), "기록을 읽을 수 없습니다: locked", "read failure");
            Same(StatisticsText.SmallSampleNote, "소표본: 이 비율만으로 시간대의 우열을 판단하지 마세요.", "small-sample note");

            Same(StatisticsText.SampleSize(new EventCounts(0, 0, 0, 0)), "n=0", "no samples");
            Same(StatisticsText.SampleSize(new EventCounts(20, 5, 3, 1)), "n=29", "small sample size");
            Same(StatisticsText.SampleSize(new EventCounts(1000, 200, 30, 4)), "n=1,234", "grouped sample size");
            foreach ((int total, bool small) in new[] { (0, false), (1, true), (29, true), (30, false), (31, false) })
                Check(StatisticsText.IsSmallSample(new EventCounts(total, 0, 0, 0)) == small, $"small sample at n={total}");

            // Choices line up with the period filter and the report sections they select.
            var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z", CultureInfo.InvariantCulture);
            Check(StatisticsText.Periods.Count == 3 && StatisticsAnalysis.SinceUtc(0, now) == now.AddDays(-7) &&
                StatisticsAnalysis.SinceUtc(1, now) == now.AddDays(-30) && StatisticsAnalysis.SinceUtc(2, now) is null,
                "period index matches the filter");
            StatisticsReport report = StatisticsAnalysis.Build([]);
            Check(StatisticsText.Sections.Count == 5 &&
                ReferenceEquals(StatisticsText.Rows(report, 0), report.Providers) &&
                ReferenceEquals(StatisticsText.Rows(report, 1), report.Hours) &&
                ReferenceEquals(StatisticsText.Rows(report, 2), report.Schedules) &&
                ReferenceEquals(StatisticsText.Rows(report, 3), report.Official) &&
                ReferenceEquals(StatisticsText.Rows(report, 4), report.OperationalHours), "section index selects its rows");
            Same(StatisticsText.Summary(0, report.Policies, 0), "직접 기록한 표본 n = 0 · 정책: No data", "summary of an empty report");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        return count;
    }
}
