namespace AiBurgerClock;

// Statistics window text shared by every host; wording follows the Windows 2.2.3 window.
// How a user records an experience (row/tray menu on Windows, card right-click on macOS) is a
// click action, so each host passes that sentence; layout, columns and colors stay in the host.
internal static class StatisticsText
{
    public const string Loading = "불러오는 중…";
    public const int SmallSampleLimit = 30;
    public const string SmallSampleNote = "소표본: 이 비율만으로 시간대의 우열을 판단하지 마세요.";

    // Index order matches StatisticsAnalysis.SinceUtc.
    public static IReadOnlyList<string> Periods { get; } = ["최근 7일", "최근 30일", "전체"];

    // Index order matches Rows(report, index).
    public static IReadOnlyList<string> Sections { get; } =
        ["Provider 비교", "KST 시간대", "Schedule / DST", "공식 상태 × 체감", "공식 정상 시간대"];

    public const string Explanation =
        "각 비율의 분모는 해당 행의 n입니다. 문제 체감 = Slow + Error + Interrupted. n < 30은 소표본입니다.\n" +
        "직접 선택해 남긴 체감 기록이며 전체 사용의 장애율·인과관계를 뜻하지 않습니다. 공식 상태와 체감은 별개입니다.\n" +
        "Schedule 행은 겹치는 집단입니다. 정책별·공휴일 ON/OFF별 행으로 비교하세요. 이전 기록은 재분류하지 않습니다.";

    public static IReadOnlyList<StatisticsRow> Rows(StatisticsReport report, int section) => section switch
    {
        0 => report.Providers,
        1 => report.Hours,
        2 => report.Schedules,
        3 => report.Official,
        _ => report.OperationalHours
    };

    public static string Summary(int count, string policies, int skipped) =>
        $"직접 기록한 표본 n = {count:N0} · 정책: {policies}" + (skipped > 0 ? $" · 읽을 수 없는 기록 {skipped:N0}건 제외" : "");

    public static string Empty(string howToRecord) => "No data · " + howToRecord;

    public static string ReadFailed(string error) => "기록을 읽을 수 없습니다: " + error;

    public static string SampleSize(EventCounts counts) => counts.Total == 0 ? "n=0" : $"n={counts.Total:N0}";

    public static bool IsSmallSample(EventCounts counts) => counts.Total is > 0 and < SmallSampleLimit;
}
