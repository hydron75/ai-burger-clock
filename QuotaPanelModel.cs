using System.Globalization;
using System.Text;

namespace AiBurgerClock;

// WindowId is the quota window's Id, or "" for the placeholder row shown before any reading.
// ComparisonText is row-specific accessibility text; null when its time comparison is hidden.
// Real quota rows split their existing text into NameText and ValueText for host-owned layout.
// ReadingText keeps the unsqueezed Text and optional comparison together for accessibility.
// These fields do not restore names already normalized by a parser or change Text/Detail.
// Detail is the whole-Provider tooltip; neither host parses it.
internal sealed record QuotaLine(string Text, PanelTone Tone, string Detail, string WindowId = "", double? UsedPercent = null,
    double? ElapsedPercent = null, string? ComparisonText = null, string? NameText = null, string? ValueText = null,
    string? ReadingText = null);

internal sealed record QuotaSectionText(
    QuotaProvider Provider, QuotaLine Heading, QuotaLine? Scope, IReadOnlyList<QuotaLine> Rows, QuotaLine Metadata,
    IReadOnlyList<QuotaSectionPart>? Parts = null)
{
    public string Detail => Heading.Detail;
}

internal sealed record QuotaSectionPart(QuotaProvider Source, QuotaLine Title, IReadOnlyList<QuotaLine> Rows, QuotaLine Metadata);

// Shared quota presentation. Hosts own bars, palette, layout and tooltip behavior, not wording.
internal static class QuotaPanelModel
{
    public const string AccessibleName = "개인 계정 사용량";
    public const string ShowQuotas = "한도 보기";
    public const string ShowStatus = "상태 보기";
    public const string ToggleDetail = "ChatGPT (Work/Codex) · Claude · xAI (Grok 주간 크레딧) 계정 한도. Gemini는 자동 조회하지 않습니다. 일반 채팅의 모든 모델 한도를 뜻하지 않습니다. 공식 서비스 상태는 그대로 유지합니다.";
    public const string Caption = "진함: 사용 · 연함/선: 조회 당시 시간\n마우스 올리기: 상세 · Refresh: 다시 조회";
    public const string CodexScope = "Work/Codex";
    public const string CodexScopeDetail = "ChatGPT 계정의 Work/Codex 한도입니다. 일반 채팅의 모든 모델 한도를 뜻하지 않습니다.";
    public const string ClaudeScopeDetail = "Claude Code CLI의 /usage 응답으로 받은 구독 사용 한도입니다.";
    public const string GeminiUnavailableDetail = "Gemini 한도 · 조회 안 함\n" +
        "Antigravity 약관상 앱에서 자동 조회하지 않습니다.\n" +
        "근거: https://antigravity.google/docs/faq.md\n" +
        "공식 서비스 상태는 상태 카드에서 확인하세요.";
    private const string GeminiWebScopeDetail = "gemini.google.com/usage에서 읽은 Gemini 앱 한도입니다. Antigravity 한도와 별개입니다.";
    private const string GeminiWebOffText = "꺼짐 · 로그인하면 Gemini 앱 사용량 조회";
    private const string GeminiWebOffDetail = "Gemini 한도 · 꺼짐\n" + GeminiWebScopeDetail + "\n" +
        "Gemini에 로그인하면 웹 사용량 조회가 켜집니다.";
    public const string XaiScopeDetail = "Grok CLI의 ACP _x.ai/billing 응답으로 받은 주간 크레딧 한도입니다.";
    private const string AntigravityScopeDetail = "Antigravity 설정의 Gemini Models 5시간·주간 한도입니다. Gemini 앱 한도와 별개입니다.";

    internal static string FailureMessage(QuotaProvider provider, QuotaFailureKind kind) => kind switch
    {
        QuotaFailureKind.LoginRequired => provider switch
        {
            QuotaProvider.Gemini => "로그인 필요 · Gemini 앱 사용량",
            QuotaProvider.Antigravity => "로그인 필요 · Antigravity 사용량",
            QuotaProvider.Claude => ClaudeAuthCheck.LoginRequiredText,
            _ => $"{QuotaNames.For(provider)} CLI 로그인 필요"
        },
        QuotaFailureKind.NotInstalled => "공식 CLI를 찾지 못했습니다. 설치 경로를 확인하세요.",
        QuotaFailureKind.NotPermitted => "앱 작업 폴더 권한을 확인하세요.",
        QuotaFailureKind.Timeout => "한도 조회 시간 초과",
        QuotaFailureKind.Unreachable => provider == QuotaProvider.Gemini
            ? "한도 조회 실패 · 네트워크와 로그인을 확인하세요."
            : provider == QuotaProvider.Antigravity ? "한도 조회 실패 · Antigravity 연결과 Gemini 로그인을 확인하세요."
            : "한도 조회 실패 · 네트워크와 CLI 상태를 확인하세요.",
        QuotaFailureKind.Rejected => "한도 조회 거부 · CLI 로그인과 버전을 확인하세요.",
        QuotaFailureKind.FormatChanged => "형식 변경 · 조회 불가",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // A previous value, or one whose reset time has passed, is never shown as current.
    private static bool ResetElapsed(DateTimeOffset? reset, DateTimeOffset now, bool approximate) =>
        reset is { } value && now - value >= (approximate ? TimeSpan.FromMinutes(30) : TimeSpan.Zero);

    private static string ApproximateResetCountdown(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "리셋 미제공";
        var remaining = reset.Value - now;
        if (remaining <= TimeSpan.Zero) return "갱신 대기";
        var rounded = TimeSpan.FromMinutes(Math.Ceiling(remaining.TotalMinutes));
        string[] units = [rounded.Days > 0 ? $"{rounded.Days}일" : "",
            rounded.Hours > 0 ? $"{rounded.Hours}시간" : "", rounded.Minutes > 0 ? $"{rounded.Minutes}분" : ""];
        return "약 " + string.Join(' ', units.Where(unit => unit.Length > 0)) + " 후 리셋";
    }

    public static PanelTone Tone(QuotaWindow window, bool previous, DateTimeOffset now, bool approximate = false)
    {
        bool elapsed = ResetElapsed(window.ResetsAtUtc, now, approximate);
        return previous || elapsed ? PanelTone.Muted : window.RemainingPercent <= 0 ? PanelTone.Danger :
            window.RemainingPercent < 10 ? PanelTone.Caution : PanelTone.Good;
    }

    public static IReadOnlyList<QuotaSectionText> Sections(IReadOnlyList<QuotaState> states, DateTimeOffset now,
        IReadOnlyDictionary<QuotaProvider, QuotaEarlyResetAlert>? recentEarlyResets = null)
    {
        var antigravity = states.FirstOrDefault(state => state.Provider == QuotaProvider.Antigravity);
        return states.Where(state => QuotaProviders.Boxes.Contains(state.Provider)).Select(state =>
        {
            var section = Section(state, now, recentEarlyResets?.GetValueOrDefault(state.Provider));
            if (state.Provider != QuotaProvider.Gemini || antigravity is not { Mode: QuotaSourceMode.Active })
                return section;

            bool geminiLoginRequired = state.Failure?.Kind == QuotaFailureKind.LoginRequired;
            var other = Section(geminiLoginRequired ? antigravity with { Reading = null } : antigravity, now);
            if (geminiLoginRequired)
            {
                string detail = other.Detail + "\n" + AntigravityWebText.GeminiLoginRequired;
                other = other with { Heading = other.Heading with { Detail = detail },
                    Rows = [new(AntigravityWebText.GeminiLoginRequired, PanelTone.Danger, detail)] };
            }
            static QuotaSectionPart Part(QuotaSectionText source, string title) =>
                new(source.Provider, source.Heading with { Text = title }, source.Rows
                    .Select(row => row.WindowId.Length == 0 ? row with { WindowId = source.Provider + ":placeholder" } : row)
                    .ToArray(), source.Metadata);
            var geminiPart = Part(section, section.Heading.Text.Insert("Gemini".Length, " 앱"));
            var antigravityPart = Part(other, other.Heading.Text);
            return section with
            {
                Heading = section.Heading with { Text = "Gemini", Detail = section.Detail + "\n\n" + other.Detail },
                Rows = geminiPart.Rows,
                Parts = [geminiPart, antigravityPart]
            };
        }).ToArray();
    }

    // Both hosts cache native controls by section/row identity. IDs are opaque and
    // may contain any delimiter; counts and UTF-16 length prefixes keep this exact,
    // ordered layout signature unambiguous without changing the IDs themselves.
    // Values/text/tone do not affect control structure. Hosts add their own DPI key.
    internal static string LayoutKey(IReadOnlyList<QuotaSectionText> sections)
    {
        var key = new StringBuilder();
        void Number(int value) => key.Append(value.ToString(CultureInfo.InvariantCulture)).Append(':');
        Number(sections.Count);
        foreach (var section in sections)
        {
            Number((int)section.Provider);
            Number(section.Scope is null ? 0 : 1);
            Number(section.Rows.Count);
            foreach (var row in section.Rows)
            {
                Number(row.WindowId.Length);
                key.Append(row.WindowId);
            }
            if (section.Parts is { Count: > 0 } parts)
            {
                key.Append('|');
                Number(parts.Count);
                foreach (var part in parts)
                {
                    Number((int)part.Source);
                    Number(part.Rows.Count);
                    foreach (var row in part.Rows)
                    {
                        Number(row.WindowId.Length);
                        key.Append(row.WindowId);
                    }
                }
            }
        }
        return key.ToString();
    }

    public static QuotaSectionText Section(QuotaState state, DateTimeOffset now, QuotaEarlyResetAlert? recentEarlyReset = null)
    {
        if (state.Mode is QuotaSourceMode.Off or QuotaSourceMode.Unavailable)
        {
            if (state.Provider == QuotaProvider.Antigravity)
                return new(state.Provider, new("Antigravity", PanelTone.Normal, AntigravityScopeDetail), null,
                    [new(AntigravityWebText.EnableGemini, PanelTone.Muted, AntigravityScopeDetail)], new("", PanelTone.Muted, AntigravityScopeDetail));
            bool webOff = state.Mode == QuotaSourceMode.Off;
            string detail = webOff ? GeminiWebOffDetail : GeminiUnavailableDetail;
            string notice = webOff ? GeminiWebOffText : QuotaNames.GeminiUnavailableText;
            return new(state.Provider, new(ProviderNames.Quota(state.Provider), PanelTone.Normal, detail), null,
                [new(notice, PanelTone.Muted, detail)], new("", PanelTone.Muted, detail));
        }
        var presentation = state.Presentation ?? (state.Provider == QuotaProvider.Antigravity
            ? QuotaPresentationPolicy.AntigravityWeb : QuotaPresentationPolicy.Cli);
        var failure = state.Failure;
        bool hideReading = failure is not null && presentation.HideReadingInPanel.Contains(failure.Kind);
        if (hideReading || (state.Reading is { } candidate && presentation.AcceptForDisplay?.Invoke(candidate) == false))
            state = state with { Reading = null };
        if (!presentation.ShowEarlyResetDetail) recentEarlyReset = null;
        var auth = state.Provider == QuotaProvider.Claude ? state.ClaudeAuth : null;
        bool loginRequired = auth is not null && auth.RequiresLogin(now) &&
            (state.LastSuccessfulCheckUtc is not { } successAfterAuth || auth.CheckedAtUtc >= successAfterAuth);
        string error = loginRequired ? ClaudeAuthCheck.LoginRequiredText :
            failure is null ? "" : FailureMessage(state.Provider, failure.Kind);
        bool previous = state.IsPrevious || loginRequired;
        bool codex = state.Provider == QuotaProvider.Codex;
        string name = ProviderNames.Quota(state.Provider);
        var succeeded = state.LastSuccessfulCheckUtc is { } success ? AgentSchedule.ToKst(success).ToString("MM-dd HH:mm") : "—";
        var next = state.NextCheckUtc is { } due ? AgentSchedule.ToKst(due).ToString("MM-dd HH:mm") : "—";
        QuotaPace[] paces = state.Reading?.Windows.Select(window =>
            QuotaPacing.Calculate(window, state.LastSuccessfulCheckUtc, previous, now)).ToArray() ?? [];
        string note = ProviderDetail(state, now, paces, recentEarlyReset, error, presentation.ApproximateReset);

        var heading = new QuotaLine(
            name + (state.IsRefreshing ? " · 확인 중" : previous && state.Reading is not null ? " · 이전 조회값" : ""),
            PanelTone.Normal, note);
        QuotaLine? scope = codex ? new(CodexScope, PanelTone.Muted, note) :
            loginRequired && state.Reading is not null ? new(ClaudeAuthCheck.LoginRequiredText, PanelTone.Danger, note) :
            auth?.WarningExpiry(now) is not null ? new(ClaudeAuthCheck.ExpiringText, PanelTone.Caution, note) : null;
        var metadata = new QuotaLine($"성공 {succeeded} · 다음 {next}",
            state.CacheError.Length > 0 ? PanelTone.Danger : PanelTone.Muted, note);

        QuotaLine[] rows = state.Reading is null
            ? [new(error.Length > 0 ? error : state.Provider == QuotaProvider.Gemini ? "한도 조회 대기 · Gemini 앱 사용량" : "한도 조회 대기",
                loginRequired || failure?.Kind == QuotaFailureKind.LoginRequired ? PanelTone.Danger : PanelTone.Muted, note)]
            : state.Reading.Windows.Select((window, index) =>
            {
                bool elapsed = ResetElapsed(window.ResetsAtUtc, now, presentation.ApproximateReset);
                string used = window.UsedPercent.ToString("0.#", CultureInfo.InvariantCulture);
                string countdown = presentation.ApproximateReset ? ApproximateResetCountdown(window.ResetsAtUtc, now) :
                    DisplayFormatting.CompactResetCountdown(window.ResetsAtUtc, now, window.WindowMinutes);
                string value = $"{used}% 사용{(previous || elapsed ? " (이전)" : "")} · {countdown}";
                string text = $"{window.Label}  {value}";
                string? comparison = QuotaPacing.RowComparison(paces[index], state.LastSuccessfulCheckUtc, now);
                return new QuotaLine(
                    text,
                    Tone(window, previous, now, presentation.ApproximateReset), note, window.Id, window.UsedPercent, paces[index].ElapsedPercent,
                    comparison, window.Label, value, comparison is null ? text : text + ". " + comparison);
            }).ToArray();
        return new(state.Provider, heading, scope, rows, metadata);
    }

    private static string ProviderDetail(QuotaState state, DateTimeOffset now, IReadOnlyList<QuotaPace> paces,
        QuotaEarlyResetAlert? recentEarlyReset, string error, bool approximate)
    {
        string scope = state.Provider switch
        {
            QuotaProvider.Codex => CodexScopeDetail,
            QuotaProvider.Claude => ClaudeScopeDetail,
            QuotaProvider.Gemini => GeminiWebScopeDetail,
            QuotaProvider.Xai => XaiScopeDetail,
            QuotaProvider.Antigravity => AntigravityScopeDetail,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        var lines = new List<string>
        {
            $"{ProviderNames.Quota(state.Provider)} 한도 · 모든 시각 KST",
            "범위: " + scope,
            "",
            "리셋·한도 복원 예정"
        };
        if (state.Reading is { Windows.Count: > 0 } reading)
        {
            foreach (var window in reading.Windows)
            {
                string reset = window.ResetsAtUtc is { } at
                    ? (approximate ? "약 " : "") + AgentSchedule.ToKst(at)
                        .ToString(approximate ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " KST"
                    : "제공되지 않음";
                lines.Add($"{window.Label}: {reset}" + (ResetElapsed(window.ResetsAtUtc, now, approximate) ? " · 예정 시각 경과, 새 조회 필요" : ""));
            }
        }
        else lines.Add("조회 값 없음");
        lines.Add("실제 복원 여부는 새 조회로 확인합니다.\n");
        lines.Add("시간·사용량 비교");
        lines.Add(state.LastSuccessfulCheckUtc is { } reference && reference <= now
            ? QuotaPacing.ReferenceAge(reference, now) : "마지막 조회 기준: 확인 불가");
        if (state.Reading is { Windows.Count: > 0 } compared)
            for (int index = 0; index < compared.Windows.Count; index++)
                lines.Add($"{compared.Windows[index].Label}: {paces[index].Detail}");
        else lines.Add("조회 값 없음");
        lines.Add("리셋 예정에서 역산한 단순 시간 기준입니다. 소진 예측이나 실제 창 시작·활성 확인은 아닙니다.\n");
        if (recentEarlyReset is { } recent && recent.Provider == state.Provider)
            lines.Add(QuotaEarlyResetText.Detail(recent, now) + "\n");
        string Stamp(DateTimeOffset? at, string format) => at is { } value
            ? AgentSchedule.ToKst(value).ToString(format, CultureInfo.InvariantCulture) : "—";
        lines.Add("조회 상태");
        lines.Add("마지막 성공: " + Stamp(state.LastSuccessfulCheckUtc, "MM-dd HH:mm"));
        lines.Add("다음 조회: " + Stamp(state.NextCheckUtc, "MM-dd HH:mm"));
        lines.Add("최근 시도: " + Stamp(state.CheckedAtUtc, "MM-dd HH:mm:ss"));
        if (!string.IsNullOrWhiteSpace(error)) lines.Add(error);
        if (state.Failure is { } failure) lines.Add("진단 코드: " + failure.Code);
        if (!string.IsNullOrWhiteSpace(state.CacheError)) lines.Add(state.CacheError);
        if (state.Provider == QuotaProvider.Claude && state.ClaudeAuth is { } auth)
        {
            lines.Add("");
            lines.Add("Claude CLI 로그인 상태");
            var status = auth.Status;
            if (status.Outcome == ClaudeAuthOutcome.Unknown) lines.Add(ClaudeAuthCheck.UnknownText);
            else if (status.Outcome == ClaudeAuthOutcome.SignedIn)
            {
                if (status.AuthMethod != "claude.ai")
                    lines.Add($"구독 로그인이 아님(인증 방식: {status.AuthMethod})");
                lines.Add(status.LoginExpiresAtUtc is { } expiry
                    ? $"로그인 종료: 늦어도 {Stamp(expiry, "MM-dd HH:mm")} KST(더 일찍 끝날 수 있음)"
                    : "종료 시점 미제공");
            }
            lines.Add("로그인 상태 마지막 확인: " + Stamp(auth.CheckedAtUtc, "MM-dd HH:mm") + " KST");
        }
        lines.Add("");
        lines.Add("조회 주기");
        lines.Add(state.Provider switch
        {
            QuotaProvider.Gemini => $"자동 {GeminiUsagePage.PollMinutes}분 · 수동 Refresh · 실패 시 {GeminiUsagePage.PollMinutes}분 뒤 다시 조회",
            QuotaProvider.Xai => "기본 1시간 · 사용 90% 초과~100% 미만은 1시간 · 사용 100%는 15분 · 리셋 전후 15분은 10분 · 실패 시 15분부터 재시도",
            QuotaProvider.Antigravity => "자동 1시간 · 수동 Refresh 최소 10분 · 실패 시 1시간 뒤 다시 조회",
            _ => "기본 1시간 · 사용 90% 초과~100% 미만은 1시간 · 사용 100%는 15분 · 리셋 전후 15분은 5분 · 실패 시 15분부터 재시도"
        });
        lines.Add("참고: " + (state.Provider switch
        {
            QuotaProvider.Gemini => "사용량 페이지 자체 갱신에 수 분이 걸릴 수 있습니다. 세션이 만료되면 로그인 필요로 표시하고 자동 조회를 멈춥니다.",
            QuotaProvider.Xai => "xAI 예약 조회는 최소 10분입니다. 시작·Refresh·복귀·네트워크 조회에는 적용하지 않습니다.",
            QuotaProvider.Antigravity => "남은 기간을 바탕으로 초기화 시각을 추정합니다. Gemini 로그인 필요 또는 세션 잠금 상태에서는 조회를 미룹니다.",
            _ => "—"
        }));
        lines.Add(state.Provider is QuotaProvider.Gemini or QuotaProvider.Antigravity
            ? "사용량 페이지를 읽은 시각이며 서버 데이터 생성 시각을 보장하지 않습니다."
            : "공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다.");
        return string.Join('\n', lines);
    }
}
