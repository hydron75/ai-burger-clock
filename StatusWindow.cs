namespace AiBurgerClock;

internal sealed class StatusWindow : Form
{
    private readonly Label titleLabel;
    private readonly Label stateLabel;
    private readonly Label countdownLabel;
    private readonly Label nextLabel;
    private readonly Label timeZoneLabel;
    private readonly Label checkedLabel;
    private readonly Label feedbackLabel;
    private readonly Button refreshButton;
    private readonly Button quotaButton;
    private readonly AccountQuotaView quotaView;
    private readonly List<Panel> statusPanels = new();
    private string statusCaption = StatusPanelModel.WaitingCaption;
    private readonly CheckBox autoStartCheckBox;
    private readonly CheckBox holidayCheckBox;
    private readonly ToolTip details = new() { AutoPopDelay = 25000 };
    private readonly Dictionary<ProviderKind, (Label Heading, Label Official, Label Reason)> rows = new();
    private readonly List<ContextMenuStrip> recordingMenus = new();
    private readonly List<Font> fonts = new();
    private const string DefaultFeedback = "AI 상태 클릭: 공식 페이지 · 우클릭: 기록";
    private string shownStorageError = "";
    private bool updatingAutoStart;
    private bool updatingHoliday;
    private int autoHideSuppressed;

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
        Font = OwnedFont(9F);

        titleLabel = AddLabel(StatusPanelModel.Title, 16, 10, 342, 18, 9F, FontStyle.Bold);
        stateLabel = AddLabel("", 14, 31, 345, 36, 18F, FontStyle.Bold);
        countdownLabel = AddLabel("", 16, 73, 342, 24, 12F);
        nextLabel = AddLabel("", 17, 101, 342, 19, 9F);
        timeZoneLabel = AddLabel("", 17, 122, 342, 18, 8.5F);
        int index = 0;
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var panel = new Panel { Location = new Point(16, 146 + index++ * 72), Size = new Size(342, 66), BackColor = Color.White };
            var heading = new Label { Location = new Point(8, 4), Size = new Size(326, 20), Font = OwnedFont(10F, FontStyle.Bold) };
            var official = new Label { Location = new Point(8, 25), Size = new Size(326, 17), Font = OwnedFont(8.5F), AutoEllipsis = true };
            var reason = new Label { Location = new Point(8, 44), Size = new Size(326, 17), Font = OwnedFont(8.5F), AutoEllipsis = true, ForeColor = Color.DimGray };
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
            statusPanels.Add(panel);
            rows.Add(provider, (heading, official, reason));
        }
        quotaView = new AccountQuotaView { Location = new Point(16, 146), Size = new Size(342, 214), Visible = false };
        Controls.Add(quotaView);
        checkedLabel = AddLabel(StatusPanelModel.WaitingCaption, 17, 366, 342, 35, 8.5F);
        refreshButton = new Button { Text = "Refresh", Location = new Point(16, 407), Size = new Size(106, 28) };
        refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        var statisticsButton = new Button { Text = "Statistics", Location = new Point(130, 407), Size = new Size(106, 28) };
        statisticsButton.Click += (_, _) => StatisticsRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(refreshButton);
        Controls.Add(statisticsButton);
        quotaButton = new Button { Text = QuotaPanelModel.ShowQuotas, Location = new Point(244, 407), Size = new Size(114, 28) };
        quotaButton.Click += (_, _) =>
        {
            quotaView.Visible = !quotaView.Visible;
            foreach (var panel in statusPanels) panel.Visible = !quotaView.Visible;
            titleLabel.Text = quotaView.Visible ? AccountQuotaView.UsageTitle : StatusPanelModel.Title;
            quotaButton.Text = quotaView.Visible ? QuotaPanelModel.ShowStatus : QuotaPanelModel.ShowQuotas;
            SetQuotaCaption();
        };
        Controls.Add(quotaButton);
        details.SetToolTip(quotaButton, QuotaPanelModel.ToggleDetail);
        feedbackLabel = AddLabel(DefaultFeedback, 17, 442, 342, 19, 8.5F);
        feedbackLabel.AutoEllipsis = true;
        autoStartCheckBox = new CheckBox { AutoSize = true, Text = "Windows 시작 시 자동 실행", Location = new Point(17, 466) };
        autoStartCheckBox.CheckedChanged += (_, _) =>
        {
            if (!updatingAutoStart) AutoStartChanged?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(autoStartCheckBox);
        holidayCheckBox = new CheckBox { AutoSize = true, Text = StatusPanelModel.HolidayOption, Location = new Point(17, 492), Enabled = false };
        holidayCheckBox.CheckedChanged += (_, _) =>
        {
            if (!updatingHoliday) HolidayAdjustmentChanged?.Invoke(this, EventArgs.Empty);
        };
        details.SetToolTip(holidayCheckBox, StatusPanelModel.HolidayOptionDetail);
        Controls.Add(holidayCheckBox);
        Deactivate += (_, _) =>
        {
            if (autoHideSuppressed == 0) Hide();
        };
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
        control.AccessibleDescription = "클릭하면 " + WindowsProviderNames.Provider(provider) + " 공식 상태 페이지를 기본 브라우저로 엽니다. 우클릭하면 사용 경험을 기록합니다.";
        control.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) StatusPageRequested?.Invoke(provider);
        };
        details.SetToolTip(control, control.AccessibleDescription);
    }

    // Controls do not dispose fonts assigned to them; this window releases its own.
    private Font OwnedFont(float size, FontStyle style = FontStyle.Regular)
    {
        var font = new Font("Segoe UI", size, style);
        fonts.Add(font);
        return font;
    }

    private Label AddLabel(string text, int x, int y, int width, int height, float size, FontStyle style = FontStyle.Regular)
    {
        var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = OwnedFont(size, style), ForeColor = Color.FromArgb(70, 70, 70) };
        Controls.Add(label);
        return label;
    }

    internal static ContextMenuStrip CreateRecordingMenu(ProviderKind provider, Action<ProviderKind, UsageEventType, bool> record)
    {
        var menu = new ContextMenuStrip();
        foreach (var descriptor in UsageMeasurementFactory.MenuItems)
        {
            if (descriptor is null)
            {
                menu.Items.Add(new ToolStripSeparator());
                continue;
            }
            var item = new ToolStripMenuItem(descriptor.Text);
            item.Click += (_, _) => record(provider, descriptor.Type, descriptor.WithNote);
            menu.Items.Add(item);
        }
        return menu;
    }

    public bool AutoStartChecked => autoStartCheckBox.Checked;
    internal bool HolidayAdjustmentChecked => holidayCheckBox.Checked;
    internal CheckBox HolidayCheckBox => holidayCheckBox;
    internal CheckBox AutoStartCheckBox => autoStartCheckBox;
    internal Label FeedbackLabel => feedbackLabel;

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
            SetDetail(autoStartCheckBox, status.Detail);
        }
        finally { updatingAutoStart = false; }
    }

    public void UpdateStatus(ScheduleSnapshot snapshot)
    {
        var text = StatusPanelModel.Schedule(snapshot);
        stateLabel.Text = text.State;
        stateLabel.ForeColor = text.StateTone == PanelTone.Good ? Color.FromArgb(25, 145, 78) : Color.FromArgb(211, 61, 55);
        countdownLabel.Text = text.Countdown;
        nextLabel.Text = text.Next;
        SetDetail(nextLabel, text.NextDetail);
        timeZoneLabel.Text = text.TimeZone;
        SetDetail(timeZoneLabel, text.TimeZoneDetail);
    }

    public void UpdateProviders(IReadOnlyList<ProviderStatus> states, ScheduleSnapshot schedule, bool refreshing,
        DateTimeOffset? nextRefreshUtc, string storageError = "")
    {
        foreach (var card in StatusPanelModel.Cards(states, schedule.State))
        {
            var row = rows[card.Provider];
            row.Heading.Text = card.Heading;
            row.Heading.ForeColor = card.HeadingTone switch
            {
                PanelTone.Good => Color.FromArgb(25, 145, 78),
                PanelTone.Danger => Color.FromArgb(195, 50, 45),
                PanelTone.Caution => Color.FromArgb(160, 99, 20),
                _ => Color.DimGray
            };
            row.Official.Text = card.Official;
            row.Reason.Text = card.Reason;
            foreach (var label in new[] { row.Heading, row.Official, row.Reason }) SetDetail(label, card.Detail);
        }
        statusCaption = StatusPanelModel.CheckedCaption(states, refreshing, nextRefreshUtc);
        refreshButton.Enabled = !refreshing;
        SetQuotaCaption();
        // Called every second: show a storage error only when it changes, so it does not
        // overwrite later save/setting feedback, and clear it once storage recovers.
        if (storageError != shownStorageError)
        {
            if (storageError.Length > 0) SetFeedback(storageError, true);
            else if (feedbackLabel.Text == shownStorageError) SetFeedback(DefaultFeedback);
            shownStorageError = storageError;
        }
    }

    internal AccountQuotaView QuotaView => quotaView;
    internal Button QuotaButton => quotaButton;
    internal void UpdateQuotas(IReadOnlyList<QuotaState> quotas, DateTimeOffset now)
    {
        // Refresh stays tied to the official status poll: a quota CLI read can take up to
        // 30-40s, and a click while it runs only skips the provider already refreshing.
        quotaView.UpdateQuotas(quotas, now);
        SetQuotaCaption();
    }

    private void SetQuotaCaption()
    {
        checkedLabel.Text = quotaButton.Text == QuotaPanelModel.ShowStatus ? AccountQuotaView.UsageCaption : statusCaption;
    }

    // A modal message owned by this window deactivates it; keep it visible meanwhile.
    internal T WithoutAutoHide<T>(Func<T> action)
    {
        autoHideSuppressed++;
        try { return action(); }
        finally { autoHideSuppressed--; }
    }

    // UpdateStatus/UpdateProviders run every second; replace a tooltip only when its text changes.
    private void SetDetail(Control control, string text)
    {
        if (details.GetToolTip(control) != text) details.SetToolTip(control, text);
    }

    public void SetFeedback(string text, bool error = false)
    {
        feedbackLabel.Text = text;
        feedbackLabel.ForeColor = error ? Color.Firebrick : Color.DimGray;
        SetDetail(feedbackLabel, text);
    }

    public void ShowNearTray()
    {
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(Math.Max(area.Left, area.Right - Width - 12), Math.Max(area.Top, area.Bottom - Height - 12));
        Show();
        BringToFront();
        Activate();
    }

    internal static string FormatRemaining(TimeSpan remaining) => DisplayFormatting.FormatRemaining(remaining);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            details.Dispose();
            foreach (var menu in recordingMenus) menu.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
            foreach (var font in fonts) font.Dispose(); // After the controls using them.
    }
}
