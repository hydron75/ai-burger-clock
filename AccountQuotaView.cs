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
        BackColor = Color.FromArgb(248, 249, 250);
        AccessibleName = QuotaPanelModel.AccessibleName;
    }

    public void UpdateQuotas(IReadOnlyList<QuotaState> states, DateTimeOffset now)
    {
        var sections = QuotaPanelModel.Sections(states, now);
        string key = DeviceDpi + "|" + string.Join('|', states.Select(s => s.Provider + ":" + string.Join(',', s.Reading?.Windows.Select(w => w.Id) ?? [])));
        if (key != layoutKey)
        {
            layoutKey = key;
            AutoScrollPosition = Point.Empty;
            SuspendLayout();
            details.RemoveAll();
            foreach (Control child in Controls.Cast<Control>().ToArray()) child.Dispose();
            headings.Clear(); metadata.Clear(); rows.Clear();
            int y = 0;
            foreach (var section in sections)
            {
                var box = new Panel
                {
                    Name = section.Provider + "QuotaBox",
                    Location = new Point(0, Scale(y)),
                    Width = ClientSize.Width,
                    Margin = Padding.Empty,
                    BackColor = Color.White,
                    Cursor = Cursors.Default,
                    AccessibleName = section.Heading.Text
                };
                Controls.Add(box);
                int rowY = 4;
                headings[section.Provider] = AddRow(box, rowY, headingFont); rowY += 20;
                if (section.Scope is { } scopeText)
                {
                    var scope = AddRow(box, rowY, rowFont); rowY += 20;
                    ApplyLine(scope, scopeText);
                }
                foreach (var line in section.Rows) { rows[(section.Provider, line.WindowId)] = AddRow(box, rowY, rowFont); rowY += 20; }
                metadata[section.Provider] = AddRow(box, rowY, metaFont); rowY += 20;
                box.Height = Scale(rowY);
                y += rowY + 6;
            }
            AutoScrollMinSize = new Size(0, Scale(Math.Max(0, y - 6)));
            ResumeLayout();
            PerformLayout(); // Recalculate scroll ranges after OnResize narrows the boxes.
        }
        foreach (var section in sections)
        {
            headings[section.Provider].Parent!.AccessibleName = section.Heading.Text;
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        foreach (var box in Controls.OfType<Panel>()) box.Width = ClientSize.Width;
    }

    private Label AddRow(Panel box, int y, Font font)
    {
        var label = new Label
        {
            Location = new Point(Scale(8), Scale(y)),
            Size = new Size(box.Width - Scale(16), Scale(19)),
            Font = font,
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        box.Controls.Add(label);
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
