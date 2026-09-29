namespace AiBurgerClock;

internal sealed class MeasurementDialog : Form
{
    private readonly ComboBox eventType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Font formFont = new("Segoe UI", 9F); // Not disposed by the form itself.
    private readonly TextBox note = new() { Multiline = true, MaxLength = 1000, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    public UsageEventType EventType => (UsageEventType)(eventType.SelectedItem ?? UsageEventType.Success);
    public string UserNote => note.Text.Trim();

    public MeasurementDialog(ProviderKind provider, UsageEventType initial)
    {
        SuspendLayout();
        Text = provider + " · 사용 경험";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(360, 245);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = formFont;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        // Items (not DataSource) so the initial type is selected before the form is shown.
        eventType.Items.AddRange(Enum.GetValues<UsageEventType>().Cast<object>().ToArray());
        eventType.SelectedItem = initial;
        layout.Controls.Add(eventType, 0, 0);
        layout.Controls.Add(new Label { Text = "선택 메모 · 프롬프트/대화/계정 정보는 입력하지 마세요.", Dock = DockStyle.Fill, AutoSize = false }, 0, 1);
        layout.Controls.Add(note, 0, 2);
        layout.Controls.Add(new Label { Text = "이 PC의 로컬 DB에만 저장됩니다.", Dock = DockStyle.Fill }, 0, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "저장", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) formFont.Dispose();
    }
}
