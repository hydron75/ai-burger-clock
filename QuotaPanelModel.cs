using System.Globalization;

namespace AiBurgerClock;

// WindowId is the quota window's Id, or "" for the placeholder row shown before any reading.
internal sealed record QuotaLine(string Text, PanelTone Tone, string Detail, string WindowId = "", double? UsedPercent = null);

internal sealed record QuotaSectionText(
    QuotaProvider Provider, QuotaLine Heading, QuotaLine? Scope, IReadOnlyList<QuotaLine> Rows, QuotaLine Metadata)
{
    public string Detail => Heading.Detail;
}

// Shared quota presentation. Hosts own bars, palette, layout and tooltip behavior, not wording.
internal static class QuotaPanelModel
{
    public const string AccessibleName = "개인 계정 사용량";
    public const string ShowQuotas = "한도 보기";
    public const string ShowStatus = "상태 보기";
    public const string ToggleDetail = "ChatGPT (Work/Codex) · Claude · Gemini (Antigravity) 계정 한도. 일반 채팅의 모든 모델 한도를 뜻하지 않습니다. 공식 서비스 상태는 그대로 유지합니다.";
    public const string Caption = "색 막대 = 사용한 비율 · 시각은 KST\n마우스 올리기: 상세 · Refresh: 다시 조회";
    public const string CodexScope = "Work/Codex";
    public const string CodexScopeDetail = "ChatGPT 계정의 Work/Codex 한도입니다. 일반 채팅의 모든 모델 한도를 뜻하지 않습니다.";
    public const string GeminiScope = "Antigravity · Gemini 모델";
    public const string GeminiScopeDetail = "agy CLI의 Antigravity Gemini 모델 그룹 한도입니다. Gemini Apps 웹·모바일의 전체 한도가 아니며, Antigravity의 Claude/GPT 모델 그룹과 크레딧은 포함하지 않습니다.";

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
        bool gemini = state.Provider == QuotaProvider.Gemini;
        string name = ProviderNames.Quota(state.Provider);
        var succeeded = state.LastSuccessfulCheckUtc is { } success ? AgentSchedule.ToKst(success).ToString("MM-dd HH:mm") : "—";
        var next = state.NextCheckUtc is { } due ? AgentSchedule.ToKst(due).ToString("MM-dd HH:mm") : "—";
        string note = ProviderDetail(state, now);

        var heading = new QuotaLine(
            name + (state.IsRefreshing ? " · 확인 중" : previous && state.Reading is not null ? " · 이전 조회값" : ""),
            PanelTone.Normal, note);
        var scope = codex ? new QuotaLine(CodexScope, PanelTone.Muted, note) :
            gemini ? new QuotaLine(GeminiScope, PanelTone.Muted, note) : null;
        var metadata = new QuotaLine($"성공 {succeeded} · 다음 {next}",
            state.CacheError.Length > 0 ? PanelTone.Danger : PanelTone.Muted, note);

        QuotaLine[] rows = state.Reading is null
            ? [new(state.Error.Length > 0 ? state.Error : gemini ? "한도 조회 대기 · agy CLI 로그인 필요" : "한도 조회 대기 · 공식 CLI 로그인 필요", PanelTone.Muted, note)]
            : state.Reading.Windows.Select(window =>
            {
                bool elapsed = window.ResetsAtUtc is { } reset && reset <= now;
                string used = window.UsedPercent.ToString("0.#", CultureInfo.InvariantCulture);
                return new QuotaLine(
                    $"{window.Label}  {used}% 사용{(previous || elapsed ? " (이전)" : "")} · {DisplayFormatting.CompactResetCountdown(window.ResetsAtUtc, now, window.WindowMinutes)}",
                    Tone(window, previous, now), note, window.Id, window.UsedPercent);
            }).ToArray();
        return new(state.Provider, heading, scope, rows, metadata);
    }

    private static string ProviderDetail(QuotaState state, DateTimeOffset now)
    {
        bool codex = state.Provider == QuotaProvider.Codex;
        bool gemini = state.Provider == QuotaProvider.Gemini;
        var lines = new List<string>
        {
            $"{ProviderNames.Quota(state.Provider)}{(codex ? " (Work/Codex)" : gemini ? " (Antigravity)" : "")} · 모든 시각 KST"
        };
        if (codex) lines.Add(CodexScopeDetail);
        if (gemini) lines.Add(GeminiScopeDetail);
        if (state.Reading is { Windows.Count: > 0 } reading)
        {
            lines.Add("리셋·한도 복원 예정");
            foreach (var window in reading.Windows)
            {
                string reset = window.ResetsAtUtc is { } at
                    ? AgentSchedule.ToKst(at).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " KST"
                    : "제공되지 않음";
                lines.Add($"{window.Label}: {reset}" + (window.ResetsAtUtc <= now ? " · 예정 시각 경과, 새 조회 필요" : ""));
            }
            lines.Add("실제 복원 여부는 새 조회로 확인합니다.\n");
        }
        string Stamp(DateTimeOffset? at, string format) => at is { } value
            ? AgentSchedule.ToKst(value).ToString(format, CultureInfo.InvariantCulture) : "—";
        lines.Add("마지막 성공: " + Stamp(state.LastSuccessfulCheckUtc, "MM-dd HH:mm"));
        lines.Add("다음 조회: " + Stamp(state.NextCheckUtc, "MM-dd HH:mm"));
        lines.Add("최근 시도: " + Stamp(state.CheckedAtUtc, "MM-dd HH:mm:ss"));
        if (!string.IsNullOrWhiteSpace(state.Error)) lines.Add(state.Error);
        if (!string.IsNullOrWhiteSpace(state.CacheError)) lines.Add(state.CacheError);
        lines.Add("기본 6시간 · 잔여 0% 초과~10% 미만 1시간 · 잔여 0%는 15분 · 리셋 전후 15분은 5분 · 실패 시 15분부터 재시도");
        lines.Add("공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다.");
        return string.Join('\n', lines);
    }
}
