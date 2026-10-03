using System.Globalization;

namespace AiBurgerClock;

// Uses the existing popup's middle area; extra model-scoped windows can scroll.
internal sealed class AccountQuotaView : Panel
{
    private readonly ToolTip details = new() { AutoPopDelay = 30000 };
    private readonly Font headingFont = new("Segoe UI", 9F, FontStyle.Bold);
    private readonly Font rowFont = new("Segoe UI", 8.5F);
    private readonly Font metaFont = new("Segoe UI", 8F);
    private readonly Dictionary<QuotaProvider, Label> headings = new();
    private readonly Dictionary<QuotaProvider, Label> metadata = new();
    private readonly Dictionary<(QuotaProvider, string), Label> rows = new();
    private string layoutKey = "";

    public AccountQuotaView()
    {
        Name = "AccountQuotaView";
        AutoScroll = true;
        BackColor = Color.White;
        AccessibleName = "개인 계정 잔여 한도";
    }

    public void UpdateQuotas(IReadOnlyList<QuotaState> states, DateTimeOffset now)
    {
        string key = DeviceDpi + "|" + string.Join('|', states.Select(s => s.Provider + ":" + string.Join(',', s.Reading?.Windows.Select(w => w.Id) ?? [])));
        if (key != layoutKey)
        {
            layoutKey = key;
            SuspendLayout();
            details.RemoveAll();
            foreach (Control child in Controls.Cast<Control>().ToArray()) child.Dispose();
            headings.Clear(); metadata.Clear(); rows.Clear();
            int y = 2;
            foreach (var state in states)
            {
                headings[state.Provider] = AddRow(y, headingFont); y += 20;
                if (state.Provider == QuotaProvider.Codex)
                {
                    var scope = AddRow(y, rowFont); y += 20;
                    scope.Text = "Work/Codex";
                    scope.ForeColor = Color.DimGray;
                    Detail(scope, "ChatGPT 계정의 Work/Codex 한도입니다. 일반 채팅의 모든 모델 한도를 뜻하지 않습니다.");
                }
                if (state.Reading is { } reading)
                    foreach (var window in reading.Windows) { rows[(state.Provider, window.Id)] = AddRow(y, rowFont); y += 20; }
                else { rows[(state.Provider, "")] = AddRow(y, rowFont); y += 20; }
                metadata[state.Provider] = AddRow(y, metaFont); y += 26;
            }
            AutoScrollMinSize = new Size(0, Scale(y));
            ResumeLayout();
        }
        foreach (var state in states)
        {
            bool previous = state.IsPrevious;
            string name = state.Provider == QuotaProvider.Codex ? "ChatGPT" : QuotaNames.For(state.Provider);
            headings[state.Provider].Text = name + (state.IsRefreshing ? " · 확인 중" : previous && state.Reading is not null ? " · 이전 조회값" : "");
            var succeeded = state.LastSuccessfulCheckUtc is { } success ? AgentSchedule.ToKst(success).ToString("MM-dd HH:mm") : "—";
            var next = state.NextCheckUtc is { } due ? AgentSchedule.ToKst(due).ToString("MM-dd HH:mm") : "—";
            var metadataLabel = metadata[state.Provider];
            metadataLabel.Text = $"성공 {succeeded} · 다음 {next}";
            string note = $"{name}{(state.Provider == QuotaProvider.Codex ? " (Work/Codex)" : "")} · 모든 시각 KST\n마지막 성공: {succeeded}\n다음 조회: {next}\n최근 시도: {(state.CheckedAtUtc is { } attempt ? AgentSchedule.ToKst(attempt).ToString("MM-dd HH:mm:ss") : "—")}\n" +
                $"{state.Error}\n{state.CacheError}\n기본 6시간 · 잔여 0% 초과~10% 미만 1시간 · 잔여 0%는 15분 · 리셋 전후 15분은 5분 · 실패 시 15분부터 재시도\n공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다.";
            Detail(headings[state.Provider], note);
            Detail(metadataLabel, note);
            metadataLabel.ForeColor = state.CacheError.Length > 0 ? Color.Firebrick : Color.DimGray;
            if (state.Reading is null)
            {
                var row = rows[(state.Provider, "")];
                row.Text = state.Error.Length > 0 ? state.Error : "한도 조회 대기 · 공식 CLI 로그인 필요";
                row.ForeColor = Color.DimGray;
                Detail(row, note);
                continue;
            }
            foreach (var window in state.Reading.Windows)
            {
                var row = rows[(state.Provider, window.Id)];
                bool elapsed = window.ResetsAtUtc is { } reset && reset <= now;
                string percent = window.RemainingPercent.ToString("0.#", CultureInfo.InvariantCulture);
                row.Text = $"{window.Label}  {percent}% {(previous || elapsed ? "(이전)" : "남음")} · {ResetCountdown(window.ResetsAtUtc, now)}";
                row.ForeColor = previous || elapsed ? Color.DimGray : window.RemainingPercent <= 0 ? Color.Firebrick :
                    window.RemainingPercent < 10 ? Color.DarkOrange : Color.FromArgb(25, 115, 75);
                string resetText = window.ResetsAtUtc is { } at ? AgentSchedule.ToKst(at).ToString("yyyy-MM-dd HH:mm:ss") + " KST" : "제공되지 않음";
                Detail(row, $"{window.Label}\n사용 {window.UsedPercent:0.#}% / 잔여 {percent}%\n리셋: {resetText}\n{(elapsed ? "리셋 예정 시각 경과 · 새 조회로 회복 확인 필요\n" : "")}" + note);
            }
        }
    }

    internal static string ResetCountdown(DateTimeOffset? reset, DateTimeOffset now) =>
        DisplayFormatting.ResetCountdown(reset, now);

    private Label AddRow(int y, Font font)
    {
        var label = new Label { Location = new Point(Scale(8), Scale(y)), Size = new Size(Scale(310), Scale(19)), Font = font, AutoEllipsis = true };
        Controls.Add(label);
        return label;
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private void Detail(Control control, string text)
    {
        control.AccessibleDescription = text;
        if (details.GetToolTip(control) != text) details.SetToolTip(control, text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) details.Dispose();
        base.Dispose(disposing);
        if (disposing) { headingFont.Dispose(); rowFont.Dispose(); metaFont.Dispose(); }
    }
}
