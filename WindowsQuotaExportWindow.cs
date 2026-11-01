namespace AiBurgerClock;

internal sealed class WindowsQuotaExportWindow : Form
{
    internal const string MenuLabel = "모바일 사이트 연동…";
    private readonly WindowsQuotaExportService service;
    private readonly CheckBox enabled = new() { Text = "모바일 사이트로 한도 스냅샷 보내기", AutoSize = true };
    private readonly TextBox site = new() { Dock = DockStyle.Fill, MaxLength = 2048, AccessibleName = "사이트 주소" };
    private readonly Label credential = new() { AutoSize = true, Text = "토큰: 확인 중…" };
    private readonly Button replace = new() { Text = "토큰 교체…", AutoSize = true };
    private readonly TextBox token = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 2560,
        AccessibleName = "새 서비스 토큰", Visible = false };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(440, 0), AccessibleName = "전송 상태" };
    private readonly Label feedback = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly Button save = new() { Text = "저장", AutoSize = true };
    private readonly Button test = new() { Text = "테스트 전송", AutoSize = true };
    private bool loading = true, busy, dirty;
    private bool? stored;

    internal WindowsQuotaExportWindow(WindowsQuotaExportService service)
    {
        this.service = service;
        Text = "모바일 사이트 연동";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(490, 390);
        MinimumSize = new Size(440, 390);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1,
            AutoScroll = true, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (Control control in new Control[] { enabled, new Label { Text = "사이트 주소 (https://…)", AutoSize = true }, site })
            layout.Controls.Add(control);
        var tokenRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        tokenRow.Controls.Add(credential); tokenRow.Controls.Add(replace);
        layout.Controls.Add(tokenRow); layout.Controls.Add(token);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(440, 0),
            Text = "토큰은 Windows 자격 증명 저장소에 보관합니다. 한도 값은 저장한 사이트 주소에만 보냅니다.\n기본 꺼짐 · 조회 완료와 15분 하트비트 · 알림 이벤트는 보내지 않습니다." });
        layout.Controls.Add(status); layout.Controls.Add(feedback);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var close = new Button { Text = "닫기", AutoSize = true };
        buttons.Controls.Add(save); buttons.Controls.Add(test); buttons.Controls.Add(close); layout.Controls.Add(buttons);
        foreach (RowStyle row in layout.RowStyles) row.SizeType = SizeType.AutoSize;
        Controls.Add(layout);
        AcceptButton = save; CancelButton = close;
        close.Click += (_, _) => Close();
        enabled.CheckedChanged += (_, _) => Edited();
        site.TextChanged += (_, _) => Edited();
        token.TextChanged += (_, _) => Edited();
        replace.Click += (_, _) => { token.Visible = true; token.Clear(); token.Focus(); Edited(); };
        save.Click += async (_, _) => await SaveAsync();
        test.Click += async (_, _) =>
        {
            busy = true; UpdateButtons();
            try { bool queued = await service.TestAsync(); if (!IsDisposed) feedback.Text = queued ? "테스트 전송을 요청했습니다. 전송 상태를 확인하세요." : "저장한 주소와 토큰을 확인하세요."; }
            finally { if (!IsDisposed) { busy = false; UpdateButtons(); UpdateStatus(); } }
        };
        Shown += async (_, _) =>
        {
            await service.Initialization;
            if (IsDisposed) return;
            enabled.Checked = service.Status.Enabled; site.Text = service.Status.SiteUrl;
            stored = await service.CredentialStoredAsync();
            if (IsDisposed) return;
            loading = false; dirty = false; UpdateCredential(); UpdateStatus(); UpdateButtons();
        };
        service.Changed += OnServiceChanged;
        UpdateButtons(); UpdateStatus();
    }

    private void Edited() { if (!loading) { dirty = true; feedback.Text = "변경한 내용은 저장 후 적용됩니다. 테스트 전송도 먼저 저장해 주세요."; UpdateButtons(); } }
    private void UpdateCredential() => credential.Text = stored switch { true => "토큰: 저장됨 (값은 표시하지 않음)", false => "토큰: 저장 안 됨", _ => "토큰: 확인 실패" };
    private void UpdateButtons()
    {
        bool editable = !loading && !busy && service.Status.Ready;
        enabled.Enabled = site.Enabled = replace.Enabled = token.Enabled = editable;
        save.Enabled = editable;
        test.Enabled = editable && !dirty && stored == true && service.Status.SiteUrl.Length > 0;
    }

    private async Task SaveAsync()
    {
        busy = true; UpdateButtons();
        string? replacement = token.Visible && token.TextLength > 0 ? token.Text : null;
        try
        {
            bool saved = await service.SaveAsync(enabled.Checked, site.Text, replacement);
            if (IsDisposed) return;
            token.Clear(); replacement = null;
            if (saved) { token.Visible = false; dirty = false; enabled.Checked = service.Status.Enabled; site.Text = service.Status.SiteUrl; dirty = false; }
            feedback.Text = saved ? "저장했습니다. 테스트는 저장한 설정으로 현재 조회값 1건을 보냅니다(꺼짐 상태에서도 가능)." : "저장하지 못했습니다. 주소·토큰·설정 파일과 현재 전송 상태를 확인하세요.";
            stored = await service.CredentialStoredAsync();
        }
        finally
        {
            replacement = null;
            if (!IsDisposed) { busy = false; UpdateCredential(); UpdateStatus(); UpdateButtons(); }
        }
    }

    private void OnServiceChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!IsDisposed) { UpdateStatus(); UpdateButtons(); } }); }
        catch (InvalidOperationException) { }
    }

    private void UpdateStatus()
    {
        var state = service.Status;
        static string Stamp(DateTimeOffset? at) => at is { } value ? AgentSchedule.ToKst(value).ToString("MM-dd HH:mm:ss") + " KST" : "—";
        string reason = state.Failure switch
        {
            WindowsQuotaExportFailure.None => "없음",
            WindowsQuotaExportFailure.Settings => "Windows 전용 설정 파일을 읽거나 저장하지 못함",
            WindowsQuotaExportFailure.Credential => "서비스 토큰 확인 필요",
            WindowsQuotaExportFailure.Payload => "전송 자료 형식 또는 크기 확인 필요",
            WindowsQuotaExportFailure.Address => "HTTPS 사이트 기본 주소 확인 필요",
            WindowsQuotaExportFailure.Redirect => "리다이렉트 응답 거부",
            WindowsQuotaExportFailure.Rejected => "서버가 요청을 거부함 (주소·토큰·형식 확인)",
            WindowsQuotaExportFailure.Server => "서버 일시 오류",
            WindowsQuotaExportFailure.Timeout => "요청 시간 초과",
            _ => "네트워크 또는 인증서 연결 실패"
        };
        status.Text = $"연동: {(state.Sending ? "전송 중" : state.Enabled ? "켜짐" : "꺼짐")}\n마지막 성공: {Stamp(state.LastSuccessUtc)}\n재시도 예정: {Stamp(state.RetryAtUtc)}\n최근 실패 사유: {reason}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { service.Changed -= OnServiceChanged; token.Clear(); }
        base.Dispose(disposing);
    }

    internal CheckBox EnabledCheck => enabled;
    internal TextBox SiteInput => site;
    internal TextBox TokenInput => token;
    internal Button SaveButton => save;
    internal Button TestButton => test;
    internal Button ReplaceButton => replace;
    internal Label CredentialLabel => credential;
    internal Label StatusLabel => status;
}
