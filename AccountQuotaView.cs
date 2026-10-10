namespace AiBurgerClock;

// Uses the existing popup's middle area; extra model-scoped windows can scroll.
internal sealed class AccountQuotaView : Panel
{
    private readonly QuotaDetailPopup details = new();
    private readonly System.Windows.Forms.Timer detailDelay = new() { Interval = SystemInformation.MouseHoverTime };
    private readonly System.Windows.Forms.Timer detailLifetime = new() { Interval = 30000 };
    private readonly System.Windows.Forms.Timer detailLeaveDelay = new() { Interval = SystemInformation.MouseHoverTime };
    private readonly System.Windows.Forms.Timer detailPointerMonitor = new() { Interval = 100 };
    internal const int DetailWidth = 320; // Whole tooltip width at 96 DPI, including padding.
    private const int DetailPadding = 8;
    private readonly Font headingFont = new("Segoe UI", 9F, FontStyle.Bold);
    private readonly Font rowFont = new("Segoe UI", 8.5F);
    private readonly Font metaFont = new("Segoe UI", 8F);
    private readonly Dictionary<QuotaProvider, Label> headings = new();
    private readonly Dictionary<QuotaProvider, Label> scopes = new();
    private readonly Dictionary<QuotaProvider, Label> metadata = new();
    private readonly Dictionary<(QuotaProvider, string), Label> rows = new();
    private readonly Dictionary<(QuotaProvider, string), QuotaBalanceBar> bars = new();
    private readonly Dictionary<Control, QuotaProvider> detailOwners = new();
    private readonly Dictionary<QuotaProvider, string> providerDetails = new();
    private QuotaProvider? pendingDetail;
    private string layoutKey = "";
    private bool sizingBoxes;

    public AccountQuotaView()
    {
        Name = "AccountQuotaView";
        AutoScroll = true;
        BackColor = Color.FromArgb(248, 249, 250);
        AccessibleName = QuotaPanelModel.AccessibleName;
        detailDelay.Tick += (_, _) =>
        {
            detailDelay.Stop();
            if (pendingDetail is { } provider && ProviderAt(Cursor.Position) == provider)
                ShowDetail(provider, Cursor.Position);
            else HideDetail();
        };
        detailPointerMonitor.Tick += (_, _) => CheckDetailPointer(Cursor.Position);
        detailLifetime.Tick += (_, _) => HideDetail();
        detailLeaveDelay.Tick += (_, _) => FinishDetailLeave(Cursor.Position);
        details.MouseLeave += (_, _) => DeferDetailLeave();
        details.MouseEnter += (_, _) => detailLeaveDelay.Stop();
        details.Scroll += (_, _) => { detailLifetime.Stop(); detailLifetime.Start(); };
        VisibleChanged += (_, _) => { if (!Visible) HideDetail(); };
        Scroll += (_, _) => HideDetail();
    }

    internal QuotaDetailPopup Details => details;
    internal string DetailFor(Control control) => detailOwners.TryGetValue(control, out var provider)
        ? providerDetails.GetValueOrDefault(provider, "") : "";
    internal static Font DetailFont => SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

    internal static Size MeasureDetail(Graphics graphics, string text, int dpi, int workingAreaWidth, int? contentWidth = null)
    {
        int padding = ScaleDetail(DetailPadding, dpi);
        int width = Math.Min(ScaleDetail(DetailWidth, dpi), Math.Max(2 * padding + 1, workingAreaWidth - 2 * padding));
        width = Math.Min(width, contentWidth ?? width);
        using var format = DetailFormat();
        // GDI+ also wraps words without spaces, such as long CLI paths or URLs.
        var content = graphics.MeasureString(text, DetailFont,
            new SizeF(width - 2 * padding, int.MaxValue), format);
        return new Size(width, (int)Math.Ceiling(content.Height) + 2 * padding);
    }

    internal static void DrawDetail(Graphics graphics, Rectangle bounds, string text, int dpi)
    {
        graphics.Clear(Color.White);
        using var border = new Pen(Color.FromArgb(200, 200, 200));
        graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        int padding = ScaleDetail(DetailPadding, dpi);
        var content = Rectangle.Inflate(bounds, -padding, -padding);
        using var format = DetailFormat();
        graphics.DrawString(text, DetailFont, Brushes.Black, content, format);
    }

    internal static StringFormat DetailFormat() => new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.MeasureTrailingSpaces,
        Trimming = StringTrimming.None
    };

    private static int ScaleDetail(int value, int dpi) => Math.Max(1, (int)Math.Round(value * dpi / 96.0));

    private void BindDetail(Control control, QuotaProvider provider)
    {
        detailOwners[control] = provider;
        control.MouseMove += (_, _) =>
        {
            detailLeaveDelay.Stop();
            if (details.Visible && pendingDetail == provider)
            {
                // A scrollable detail must not chase the pointer while it approaches the scrollbar.
                if (!details.VerticalScroll.Visible) ShowDetail(provider, Cursor.Position);
            }
            else if (pendingDetail != provider)
            {
                HideDetail();
                pendingDetail = provider;
                detailDelay.Start();
                detailPointerMonitor.Start();
            }
        };
        control.MouseLeave += (_, _) => DeferDetailLeave();
    }

    private void DeferDetailLeave()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke((Action)(() =>
        {
            if (!IsDisposed) CheckDetailPointer(Cursor.Position);
        }));
    }

    // MouseLeave can be missed by child controls or a non-activating popup. Check independently
    // while a hover is pending/visible; never extend the lifetime or repeatedly reset the leave grace.
    internal void CheckDetailPointer(Point screenPoint)
    {
        if (pendingDetail is null) return;
        if (!Visible || FindForm() is not { Visible: true }) { HideDetail(); return; }
        if (ProviderAt(screenPoint) == pendingDetail || details.CanInteractAt(screenPoint))
        {
            detailLeaveDelay.Stop();
            return;
        }
        // Let the pointer cross the small gap to a scrollable popup, but do not leave it pinned.
        if (details.Visible && details.VerticalScroll.Visible)
        {
            if (!detailLeaveDelay.Enabled) detailLeaveDelay.Start();
        }
        else HideDetail();
    }

    internal void FinishDetailLeave(Point screenPoint)
    {
        detailLeaveDelay.Stop();
        if (ProviderAt(screenPoint) != pendingDetail && !details.CanInteractAt(screenPoint)) HideDetail();
    }

    private QuotaProvider? ProviderAt(Point screenPoint)
    {
        if (!Visible || !RectangleToScreen(ClientRectangle).Contains(screenPoint)) return null;
        return Controls.OfType<Panel>().Where(box => box.RectangleToScreen(box.ClientRectangle).Contains(screenPoint))
            .Select(box => (QuotaProvider?)detailOwners[box]).FirstOrDefault();
    }

    internal void ShowDetail(QuotaProvider provider, Point screenPoint)
    {
        if (!providerDetails.TryGetValue(provider, out var text) || text.Length == 0 || FindForm() is not { } owner) return;
        pendingDetail = provider;
        details.Display(owner, text, screenPoint, DeviceDpi);
        if (!detailLifetime.Enabled) detailLifetime.Start();
        detailPointerMonitor.Start();
    }

    internal void HideDetail()
    {
        detailDelay.Stop();
        detailLifetime.Stop();
        detailLeaveDelay.Stop();
        detailPointerMonitor.Stop();
        pendingDetail = null;
        details.Hide();
    }

    public void UpdateQuotas(IReadOnlyList<QuotaState> states, DateTimeOffset now)
    {
        var sections = QuotaPanelModel.Sections(states, now);
        string key = DeviceDpi + "|" + string.Join('|', sections.Select(s => s.Provider + ":" + (s.Scope is not null) + ":" + string.Join(',', s.Rows.Select(row => row.WindowId))));
        if (key != layoutKey)
        {
            HideDetail();
            layoutKey = key;
            AutoScrollPosition = Point.Empty;
            SuspendLayout();
            foreach (Control child in Controls.Cast<Control>().ToArray()) child.Dispose();
            headings.Clear(); scopes.Clear(); metadata.Clear(); rows.Clear(); bars.Clear(); detailOwners.Clear(); providerDetails.Clear();
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
                BindDetail(box, section.Provider);
                int rowY = 4;
                headings[section.Provider] = AddRow(box, rowY, headingFont); rowY += 20;
                if (section.Scope is { } scopeText)
                {
                    scopes[section.Provider] = AddRow(box, rowY, rowFont); rowY += 20;
                }
                foreach (var line in section.Rows)
                {
                    rows[(section.Provider, line.WindowId)] = AddRow(box, rowY, rowFont); rowY += 20;
                    if (line.WindowId.Length == 0) continue;
                    var bar = new QuotaBalanceBar { Location = new Point(Scale(8), Scale(rowY)),
                        Size = new Size(box.Width - Scale(16), Scale(6)), Name = section.Provider + "-" + line.WindowId + "BalanceBar" };
                    box.Controls.Add(bar);
                    BindDetail(bar, section.Provider);
                    bars[(section.Provider, line.WindowId)] = bar;
                    rowY += 14;
                }
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
            providerDetails[section.Provider] = section.Detail;
            headings[section.Provider].Parent!.AccessibleName = section.Heading.Text;
            headings[section.Provider].Parent!.AccessibleDescription = providerDetails[section.Provider];
            ApplyLine(headings[section.Provider], section.Heading);
            if (section.Scope is { } scope) ApplyLine(scopes[section.Provider], scope);
            ApplyLine(metadata[section.Provider], section.Metadata);
            foreach (var line in section.Rows)
            {
                var label = rows[(section.Provider, line.WindowId)];
                ApplyLine(label, line);
                if (line.UsedPercent is not { } used) continue;
                bars[(section.Provider, line.WindowId)].UpdateBalance(section.Provider, used, line.Tone,
                    section.Heading.Text + " · " + label.Text, line.Detail);
            }
        }
        if (details.Visible && pendingDetail is { } shown && ProviderAt(Cursor.Position) == shown)
            ShowDetail(shown, details.VerticalScroll.Visible ? details.AnchorPoint : Cursor.Position);
        else if (details.Visible && pendingDetail is { } scrolling && details.CanInteractAt(Cursor.Position))
            ShowDetail(scrolling, details.AnchorPoint);
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
        label.AccessibleDescription = line.Detail;
    }

    internal static string ResetCountdown(DateTimeOffset? reset, DateTimeOffset now) =>
        DisplayFormatting.ResetCountdown(reset, now);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        foreach (var box in Controls.OfType<Panel>()) box.Width = ClientSize.Width;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (sizingBoxes) return;
        sizingBoxes = true;
        try
        {
            foreach (var box in Controls.OfType<Panel>())
            {
                box.Width = ClientSize.Width;
                foreach (Control child in box.Controls)
                    child.Width = Math.Max(0, box.ClientSize.Width - Scale(16));
            }
        }
        finally { sizingBoxes = false; }
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
        BindDetail(label, detailOwners[box]);
        return label;
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { detailDelay.Dispose(); detailLifetime.Dispose(); detailLeaveDelay.Dispose(); detailPointerMonitor.Dispose(); details.Dispose(); }
        base.Dispose(disposing);
        if (disposing) { headingFont.Dispose(); rowFont.Dispose(); metaFont.Dispose(); }
    }
}
