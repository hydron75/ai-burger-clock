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
        AccessibleName = QuotaPanelModel.AccessibleName;
    }

    public void UpdateQuotas(IReadOnlyList<QuotaState> states, DateTimeOffset now)
    {
        var sections = QuotaPanelModel.Sections(states, now);
        string key = DeviceDpi + "|" + string.Join('|', states.Select(s => s.Provider + ":" + string.Join(',', s.Reading?.Windows.Select(w => w.Id) ?? [])));
        if (key != layoutKey)
        {
            layoutKey = key;
            SuspendLayout();
            details.RemoveAll();
            foreach (Control child in Controls.Cast<Control>().ToArray()) child.Dispose();
            headings.Clear(); metadata.Clear(); rows.Clear();
            int y = 2;
            foreach (var section in sections)
            {
                headings[section.Provider] = AddRow(y, headingFont); y += 20;
                if (section.Scope is { } scopeText)
                {
                    var scope = AddRow(y, rowFont); y += 20;
                    ApplyLine(scope, scopeText);
                }
                foreach (var line in section.Rows) { rows[(section.Provider, line.WindowId)] = AddRow(y, rowFont); y += 20; }
                metadata[section.Provider] = AddRow(y, metaFont); y += 26;
            }
            AutoScrollMinSize = new Size(0, Scale(y));
            ResumeLayout();
        }
        foreach (var section in sections)
        {
            ApplyLine(headings[section.Provider], section.Heading);
            ApplyLine(metadata[section.Provider], section.Metadata);
            foreach (var line in section.Rows) ApplyLine(rows[(section.Provider, line.WindowId)], line);
        }
    }

    // Text and tone come from the shared model; the Windows palette stays local.
    private void ApplyLine(Label label, QuotaLine line)
    {
        label.Text = line.Text;
        label.ForeColor = line.Tone switch
        {
            PanelTone.Good => Color.FromArgb(25, 115, 75),
            PanelTone.Caution => Color.DarkOrange,
            PanelTone.Danger => Color.Firebrick,
            PanelTone.Muted => Color.DimGray,
            _ => SystemColors.ControlText
        };
        Detail(label, line.Detail);
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
