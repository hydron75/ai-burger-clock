namespace AiBurgerClock;

internal sealed class StatusWindow : Form
{
    private readonly Label stateLabel;
    private readonly Label countdownLabel;
    private readonly Label nextLabel;
    private readonly Label timeZoneLabel;
    private readonly Label checkedLabel;
    private readonly Label feedbackLabel;
    private readonly Button refreshButton;
    private readonly CheckBox autoStartCheckBox;
    private readonly CheckBox holidayCheckBox;
    private readonly ToolTip details = new() { AutoPopDelay = 25000 };
    private readonly Dictionary<ProviderKind, (Label Heading, Label Official, Label Reason)> rows = new();
    private readonly List<ContextMenuStrip> recordingMenus = new();
    private bool updatingAutoStart;
    private bool updatingHoliday;

    public event EventHandler? AutoStartChanged;
    public event EventHandler? HolidayAdjustmentChanged;
    public event EventHandler? RefreshRequested;
    public event EventHandler? StatisticsRequested;
    public event Action<ProviderKind, UsageEventType, bool>? RecordRequested;
    public event Action<ProviderKind>? StatusPageRequested;

    public StatusWindow()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "AI Burger Clock";
        ClientSize = new Size(374, 518);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(248, 249, 250);
        Font = new Font("Segoe UI", 9F);

        AddLabel("AI AGENT TRAFFIC", 16, 10, 342, 18, 9F, FontStyle.Bold);
        stateLabel = AddLabel("", 14, 31, 345, 36, 18F, FontStyle.Bold);
        countdownLabel = AddLabel("", 16, 73, 342, 24, 12F);
        nextLabel = AddLabel("", 17, 101, 342, 19, 9F);
        timeZoneLabel = AddLabel("", 17, 122, 342, 18, 8.5F);
        int index = 0;
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var panel = new Panel { Location = new Point(16, 146 + index++ * 72), Size = new Size(342, 66), BackColor = Color.White };
            var heading = new Label { Location = new Point(8, 4), Size = new Size(326, 20), Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            var official = new Label { Location = new Point(8, 25), Size = new Size(326, 17), Font = new Font("Segoe UI", 8.5F), AutoEllipsis = true };
            var reason = new Label { Location = new Point(8, 44), Size = new Size(326, 17), Font = new Font("Segoe UI", 8.5F), AutoEllipsis = true, ForeColor = Color.DimGray };
            var menu = CreateRecordingMenu(provider, (p, e, note) => RecordRequested?.Invoke(p, e, note));
            recordingMenus.Add(menu);
            panel.ContextMenuStrip = menu;
            ConfigureStatusLink(panel, provider);
            foreach (var label in new[] { heading, official, reason })
            {
                label.ContextMenuStrip = menu;
                ConfigureStatusLink(label, provider);
                panel.Controls.Add(label);
            }
            Controls.Add(panel);
            rows.Add(provider, (heading, official, reason));
        }
        checkedLabel = AddLabel("공식 상태 갱신 대기", 17, 366, 342, 35, 8.5F);
        refreshButton = new Button { Text = "Refresh", Location = new Point(16, 407), Size = new Size(106, 28) };
        refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        var statisticsButton = new Button { Text = "Statistics", Location = new Point(130, 407), Size = new Size(106, 28) };
        statisticsButton.Click += (_, _) => StatisticsRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(refreshButton);
        Controls.Add(statisticsButton);
        feedbackLabel = AddLabel("AI 상태 클릭: 공식 페이지 · 우클릭: 기록", 17, 442, 342, 19, 8.5F);
        feedbackLabel.AutoEllipsis = true;
        autoStartCheckBox = new CheckBox { AutoSize = true, Text = "Windows 시작 시 자동 실행", Location = new Point(17, 466) };
        autoStartCheckBox.CheckedChanged += (_, _) =>
        {
            if (!updatingAutoStart) AutoStartChanged?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(autoStartCheckBox);
        holidayCheckBox = new CheckBox { AutoSize = true, Text = "미국 연방 공휴일 보정", Location = new Point(17, 492), Enabled = false };
        holidayCheckBox.CheckedChanged += (_, _) =>
        {
            if (!updatingHoliday) HolidayAdjustmentChanged?.Invoke(this, EventArgs.Empty);
        };
        details.SetToolTip(holidayCheckBox, "미국 연방 정기 공휴일·대체휴일의 업무 구간을 제외합니다.\n기업 휴무나 실제 서비스 품질을 보장하지 않는 시간표 정책입니다.");
        Controls.Add(holidayCheckBox);
        Deactivate += (_, _) => Hide();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        ResumeLayout(false);
        PerformLayout();
    }

    private void ConfigureStatusLink(Control control, ProviderKind provider)
    {
        control.Cursor = Cursors.Hand;
        control.AccessibleDescription = "클릭하면 " + provider + " 공식 상태 페이지를 기본 브라우저로 엽니다. 우클릭하면 사용 경험을 기록합니다.";
        control.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) StatusPageRequested?.Invoke(provider);
        };
        details.SetToolTip(control, control.AccessibleDescription);
    }

    private Label AddLabel(string text, int x, int y, int width, int height, float size, FontStyle style = FontStyle.Regular)
    {
        var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font("Segoe UI", size, style), ForeColor = Color.FromArgb(70, 70, 70) };
        Controls.Add(label);
        return label;
    }

    internal static ContextMenuStrip CreateRecordingMenu(ProviderKind provider, Action<ProviderKind, UsageEventType, bool> record)
    {
        var menu = new ContextMenuStrip();
        foreach (var type in Enum.GetValues<UsageEventType>())
        {
            var item = new ToolStripMenuItem(type.ToString());
            item.Click += (_, _) => record(provider, type, false);
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var withNote = new ToolStripMenuItem("메모와 함께 기록…");
        withNote.Click += (_, _) => record(provider, UsageEventType.Success, true);
        menu.Items.Add(withNote);
        return menu;
    }

    public bool AutoStartChecked => autoStartCheckBox.Checked;
    internal bool HolidayAdjustmentChecked => holidayCheckBox.Checked;
    internal CheckBox HolidayCheckBox => holidayCheckBox;
    internal CheckBox AutoStartCheckBox => autoStartCheckBox;

    internal void SetHolidayAdjustment(bool enabled, bool ready)
    {
        updatingHoliday = true;
        try { holidayCheckBox.Checked = enabled; holidayCheckBox.Enabled = ready; }
        finally { updatingHoliday = false; }
    }

    public void SetAutoStartStatus(AutoStartStatus status)
    {
        updatingAutoStart = true;
        try
        {
            autoStartCheckBox.Checked = status.IsEnabled;
            autoStartCheckBox.Text = status.Label;
            autoStartCheckBox.AccessibleDescription = status.Detail;
            details.SetToolTip(autoStartCheckBox, status.Detail);
        }
        finally { updatingAutoStart = false; }
    }

    public void UpdateStatus(ScheduleSnapshot snapshot)
    {
        bool full = snapshot.State == AgentState.FullThrottle;
        stateLabel.Text = full ? "●  FULL THROTTLE" : "●  BURGER TIME";
        stateLabel.ForeColor = full ? Color.FromArgb(25, 145, 78) : Color.FromArgb(211, 61, 55);
        countdownLabel.Text = "전환까지  " + FormatRemaining(snapshot.Remaining);
        string extended = (snapshot.IsWeekendExtendedFullThrottle, snapshot.IsHolidayExtendedFullThrottle) switch
        {
            (true, true) => " · 주말+공휴일",
            (true, false) => " · Weekend",
            (false, true) => " · 공휴일",
            _ => ""
        };
        nextLabel.Text = $"다음: {snapshot.NextTransitionKst:ddd HH:mm} KST" + extended;
        details.SetToolTip(nextLabel, $"다음 전환: {snapshot.NextTransitionKst:yyyy-MM-dd HH:mm:ss} KST\n공휴일 보정: {(snapshot.HolidayAdjustmentEnabled ? "켜짐" : "꺼짐")}\n연장에 반영된 공휴일: {snapshot.HolidayNames}\n주말 여부와 공휴일 연장은 독립적으로 기록됩니다.");
        string mode = snapshot.EasternIsDst == snapshot.PacificIsDst ? (snapshot.EasternIsDst ? "DST" : "Standard") : "Mixed DST";
        timeZoneLabel.Text = $"US: {mode} · ET {Offset(snapshot.EasternUtcOffsetMinutes)} / PT {Offset(snapshot.PacificUtcOffsetMinutes)}";
        details.SetToolTip(timeZoneLabel, $"Eastern: {snapshot.EasternLocalTime:yyyy-MM-dd HH:mm zzz}\nPacific: {snapshot.PacificLocalTime:yyyy-MM-dd HH:mm zzz}\n정책: {snapshot.SchedulePolicyVersion}");
    }

    public void UpdateProviders(IReadOnlyList<ProviderStatus> states, ScheduleSnapshot schedule, bool refreshing,
        DateTimeOffset? nextRefreshUtc, string storageError = "")
    {
        foreach (var status in states)
        {
            var row = rows[status.Provider];
            var recommendation = RecommendationPolicy.Calculate(schedule.State, status.Status);
            row.Heading.Text = status.Provider + "   " + RecommendationPolicy.Label(recommendation);
            row.Heading.ForeColor = recommendation switch
            {
                Recommendation.Go => Color.FromArgb(25, 145, 78),
                Recommendation.Stop or Recommendation.BurgerServiceIssue => Color.FromArgb(195, 50, 45),
                Recommendation.Hold or Recommendation.BurgerTime => Color.FromArgb(160, 99, 20),
                _ => Color.DimGray
            };
            row.Official.Text = "Official: " + RecommendationPolicy.OfficialLabel(status.Status);
            row.Reason.Text = status.Reason;
            string success = status.LastSuccessfulCheckUtc is { } time ? ToKst(time).ToString("MM-dd HH:mm:ss") + " KST" : "없음";
            string attempted = status.CheckedAtUtc == DateTimeOffset.MinValue ? "없음" : ToKst(status.CheckedAtUtc).ToString("MM-dd HH:mm:ss") + " KST";
            string detail = $"클릭: 공식 상태 페이지 열기 · 우클릭: 사용 경험 기록\n{status.Reason}\n최근 조회 시도: {attempted}\n마지막 상태 확인 성공: {success}\n관련: {status.RelevantComponent}\n사건: {status.IncidentTitle}\n사건 ID: {status.IncidentId}\n마지막 알려진 상태: {status.LastKnownStatus}\n{status.Source}";
            foreach (var label in new[] { row.Heading, row.Official, row.Reason }) details.SetToolTip(label, detail);
        }
        var last = states.Select(s => s.CheckedAtUtc).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
        checkedLabel.Text = (last == DateTimeOffset.MinValue ? "최근 조회 시도: —" : $"최근 조회 시도: {ToKst(last):HH:mm:ss} KST") +
            "\n" + (refreshing ? "공식 상태 확인 중…" : nextRefreshUtc is { } next ? $"다음 조회: {ToKst(next):HH:mm:ss} KST" : "다음 조회: —");
        refreshButton.Enabled = !refreshing;
        if (!string.IsNullOrEmpty(storageError)) SetFeedback(storageError, true);
    }

    public void SetFeedback(string text, bool error = false)
    {
        feedbackLabel.Text = text;
        feedbackLabel.ForeColor = error ? Color.Firebrick : Color.DimGray;
        details.SetToolTip(feedbackLabel, text);
    }

    public void ShowNearTray()
    {
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(Math.Max(area.Left, area.Right - Width - 12), Math.Max(area.Top, area.Bottom - Height - 12));
        Show();
        BringToFront();
        Activate();
    }

    private static string Offset(int minutes) => $"UTC{(minutes >= 0 ? "+" : "-")}{Math.Abs(minutes) / 60}" +
        (minutes % 60 == 0 ? "" : $":{Math.Abs(minutes) % 60:00}");
    private static DateTimeOffset ToKst(DateTimeOffset utc) => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "Korea Standard Time");
    internal static string FormatRemaining(TimeSpan remaining) =>
        $"{Math.Max(0, (int)remaining.TotalHours):00}:{Math.Max(0, remaining.Minutes):00}:{Math.Max(0, remaining.Seconds):00}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            details.Dispose();
            foreach (var menu in recordingMenus) menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
