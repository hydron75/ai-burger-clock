using System.Globalization;

namespace AiBurgerClock;

internal sealed record QuotaPace(double? ElapsedPercent, double? DifferencePercentagePoints, string Detail);

// A display-only reference pinned to the successful response receipt, not a forecast
// or a server-provided window start. It never changes polling or quota alert policy.
internal static class QuotaPacing
{
    public static QuotaPace Calculate(QuotaWindow window, DateTimeOffset? successfulAtUtc,
        bool previous, DateTimeOffset now)
    {
        static QuotaPace Unavailable(string reason) => new(null, null, "시간 비교: " + reason);

        if (previous) return Unavailable("이전 조회값");
        if (successfulAtUtc is not { } success) return Unavailable("마지막 성공 조회 시각 없음");
        if (!double.IsFinite(window.UsedPercent) || window.UsedPercent is < 0 or > 100)
            return Unavailable("사용 비율 확인 불가");
        if (window.ResetsAtUtc is not { } reset) return Unavailable("리셋 시각 미제공");
        // Match the normalized metadata's supported range. Never infer a duration
        // from a provider, row id, display label or the current time until reset.
        if (window.WindowMinutes is not { } minutes || minutes is <= 0 or > 5_256_000)
            return Unavailable("창 길이 미확인");
        if (success > now) return Unavailable("조회 시각 확인 필요");
        if (reset <= now) return Unavailable("리셋 예정 시각 경과");

        TimeSpan duration = TimeSpan.FromMinutes(minutes);
        TimeSpan remainingAtSuccess = reset - success;
        if (remainingAtSuccess <= TimeSpan.Zero || remainingAtSuccess > duration)
            return Unavailable("조회 시각과 한도 기간 불일치");
        // An unused five-hour window may keep moving its reset. A zero reading
        // does not establish that its inferred start is an active quota window.
        if (minutes == 300 && window.UsedPercent == 0)
            return Unavailable("5시간 창 시작 확인 불가");

        double elapsed = 100.0 * (duration.Ticks - remainingAtSuccess.Ticks) / duration.Ticks;
        double difference = window.UsedPercent - elapsed;
        double shownDifference = Math.Round(difference, 1, MidpointRounding.AwayFromZero);
        string comparison = shownDifference == 0
            ? "균등 사용 기준과 비슷함"
            : "균등 사용 기준보다 " + Math.Abs(shownDifference).ToString("0.#", CultureInfo.InvariantCulture) +
                "%p " + (shownDifference > 0 ? "빠름" : "느림");
        string detail = "시간 경과 " + Math.Round(elapsed, 1, MidpointRounding.AwayFromZero)
            .ToString("0.#", CultureInfo.InvariantCulture) + "% · " + comparison;
        return new(elapsed, difference, detail);
    }

    public static string? RowComparison(QuotaPace pace, DateTimeOffset? successfulAtUtc, DateTimeOffset now) =>
        pace.ElapsedPercent is not null && successfulAtUtc is { } success && success <= now
            ? pace.Detail + " · " + AgeText(success, now) + " 기준" : null;

    public static string ReferenceAge(DateTimeOffset successfulAtUtc, DateTimeOffset now) =>
        "마지막 조회 기준, " + AgeText(successfulAtUtc, now);

    private static string AgeText(DateTimeOffset successfulAtUtc, DateTimeOffset now)
    {
        if (successfulAtUtc > now) return "시각 확인 필요";
        TimeSpan age = now - successfulAtUtc;
        // Whole units are rounded down: never claim the receipt is older than it is.
        string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        if (age < TimeSpan.FromMinutes(1)) return "방금 전";
        if (age < TimeSpan.FromHours(1)) return Number(age.Minutes) + "분 전";
        if (age < TimeSpan.FromDays(1))
            return Number(age.Hours) + "시간" + (age.Minutes > 0 ? " " + Number(age.Minutes) + "분" : "") + " 전";
        return Number(age.Days) + "일" + (age.Hours > 0 ? " " + Number(age.Hours) + "시간" : "") + " 전";
    }
}
