using System.Globalization;

namespace AiBurgerClock;

// WindowId is the quota window's Id, or "" for the placeholder row shown before any reading.
internal sealed record QuotaLine(string Text, PanelTone Tone, string Detail, string WindowId = "");

internal sealed record QuotaSectionText(
    QuotaProvider Provider, QuotaLine Heading, QuotaLine? Scope, IReadOnlyList<QuotaLine> Rows, QuotaLine Metadata);

// Account quota text shared by every host; wording follows the Windows 2.2.3 quota view.
internal static class QuotaPanelModel
{
    public const string AccessibleName = "개인 계정 잔여 한도";
    public const string ShowQuotas = "한도 보기";
    public const string ShowStatus = "상태 보기";
    public const string ToggleDetail = "ChatGPT (Work/Codex) · Claude 개인 계정 한도. Gemini의 공식 서비스 상태는 그대로 유지합니다.";
    public const string Caption = "잔여량 = 100 − 사용률 · 시각은 KST\n마우스 올리기: 상세 · Refresh: 다시 조회";
    public const string CodexScope = "Work/Codex";
    public const string CodexScopeDetail = "ChatGPT 계정의 Work/Codex 한도입니다. 일반 채팅의 모든 모델 한도를 뜻하지 않습니다.";

    // A previous value, or one whose reset time has passed, is never shown as current.
    public static PanelTone Tone(QuotaWindow window, bool previous, DateTimeOffset now)
    {
        bool elapsed = window.ResetsAtUtc is { } reset && reset <= now;
        return previous || elapsed ? PanelTone.Muted : window.RemainingPercent <= 0 ? PanelTone.Danger :
            window.RemainingPercent < 10 ? PanelTone.Caution : PanelTone.Good;
    }

    public static IReadOnlyList<QuotaSectionText> Sections(IReadOnlyList<QuotaState> states, DateTimeOffset now) =>
        states.Select(state => Section(state, now)).ToArray();

    public static QuotaSectionText Section(QuotaState state, DateTimeOffset now)
    {
        bool previous = state.IsPrevious;
        bool codex = state.Provider == QuotaProvider.Codex;
        string name = ProviderNames.Quota(state.Provider);
        var succeeded = state.LastSuccessfulCheckUtc is { } success ? AgentSchedule.ToKst(success).ToString("MM-dd HH:mm") : "—";
        var next = state.NextCheckUtc is { } due ? AgentSchedule.ToKst(due).ToString("MM-dd HH:mm") : "—";
        string note = $"{name}{(codex ? " (Work/Codex)" : "")} · 모든 시각 KST\n마지막 성공: {succeeded}\n다음 조회: {next}\n최근 시도: {(state.CheckedAtUtc is { } attempt ? AgentSchedule.ToKst(attempt).ToString("MM-dd HH:mm:ss") : "—")}\n" +
            $"{state.Error}\n{state.CacheError}\n기본 6시간 · 잔여 0% 초과~10% 미만 1시간 · 잔여 0%는 15분 · 리셋 전후 15분은 5분 · 실패 시 15분부터 재시도\n공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다.";

        var heading = new QuotaLine(
            name + (state.IsRefreshing ? " · 확인 중" : previous && state.Reading is not null ? " · 이전 조회값" : ""),
            PanelTone.Normal, note);
        var scope = codex ? new QuotaLine(CodexScope, PanelTone.Muted, CodexScopeDetail) : null;
        var metadata = new QuotaLine($"성공 {succeeded} · 다음 {next}",
            state.CacheError.Length > 0 ? PanelTone.Danger : PanelTone.Muted, note);

        QuotaLine[] rows = state.Reading is null
            ? [new(state.Error.Length > 0 ? state.Error : "한도 조회 대기 · 공식 CLI 로그인 필요", PanelTone.Muted, note)]
            : state.Reading.Windows.Select(window =>
            {
                bool elapsed = window.ResetsAtUtc is { } reset && reset <= now;
                string percent = window.RemainingPercent.ToString("0.#", CultureInfo.InvariantCulture);
                string used = window.UsedPercent.ToString("0.#", CultureInfo.InvariantCulture);
                string resetText = window.ResetsAtUtc is { } at ? AgentSchedule.ToKst(at).ToString("yyyy-MM-dd HH:mm:ss") + " KST" : "제공되지 않음";
                return new QuotaLine(
                    $"{window.Label}  {percent}% {(previous || elapsed ? "(이전)" : "남음")} · {DisplayFormatting.ResetCountdown(window.ResetsAtUtc, now)}",
                    Tone(window, previous, now),
                    $"{window.Label}\n사용 {used}% / 잔여 {percent}%\n리셋: {resetText}\n{(elapsed ? "리셋 예정 시각 경과 · 새 조회로 회복 확인 필요\n" : "")}" + note,
                    window.Id);
            }).ToArray();
        return new(state.Provider, heading, scope, rows, metadata);
    }
}
