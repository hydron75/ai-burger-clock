namespace AiBurgerClock;

// A non-activating, pointer-anchored Windows popup avoids native ToolTip's pre-resize positioning.
internal sealed class QuotaDetailPopup : Form
{
    internal string DetailText { get; private set; } = "";
    private int detailDpi = 96;
    private int measuredAreaWidth;
    private int measuredAreaHeight;
    private Size measuredSize;
    internal Point AnchorPoint { get; private set; }
    internal Size ContentSize { get; private set; }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x00000080; return parameters; }
    }

    public QuotaDetailPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        AutoScroll = true;
        BackColor = Color.White;
        // An owned window already stays above its owner. Form.TopMost would focus it during Show,
        // even with ShowWithoutActivation, and trigger the status window's Deactivate/auto-hide.
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.ToolTip;
        AccessibleName = "계정 한도 상세";
        Scroll += (_, _) => Invalidate();
    }

    internal bool CanInteractAt(Point point) => Visible && VerticalScroll.Visible && Bounds.Contains(point);

    internal static Rectangle Place(Point pointer, Size size, Rectangle area, int dpi)
    {
        int gap = Math.Max(1, (int)Math.Round(16 * dpi / 96.0));
        // Overflow stays on-screen; Display enables vertical scrolling rather than discarding detail.
        size = new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, Math.Max(1, area.Height - gap)));
        int x = pointer.X + gap;
        int y = pointer.Y + gap;
        if (x + size.Width > area.Right) x = pointer.X - gap - size.Width;
        if (y + size.Height > area.Bottom) y = pointer.Y - gap - size.Height;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Rectangle(new Point(x, y), size);
    }

    internal void Display(Form owner, string text, Point pointer, int dpi)
    {
        var area = Screen.FromPoint(pointer).WorkingArea;
        bool changed = DetailText != text || detailDpi != dpi;
        bool layoutChanged = changed || measuredAreaWidth != area.Width || measuredAreaHeight != area.Height || measuredSize.IsEmpty;
        if (layoutChanged)
        {
            using var graphics = owner.CreateGraphics();
            measuredSize = AccountQuotaView.MeasureDetail(graphics, text, dpi, area.Width);
            measuredAreaWidth = area.Width;
            measuredAreaHeight = area.Height;
            // Reset before laying out another Provider so a previous long detail cannot narrow it.
            AutoScrollMinSize = Size.Empty;
            AutoScrollPosition = Point.Empty;
            Bounds = Place(pointer, measuredSize, area, dpi);
            ContentSize = measuredSize;
            if (measuredSize.Height > ClientSize.Height)
            {
                AutoScrollMinSize = new Size(0, measuredSize.Height);
                PerformLayout();
                // The native vertical scrollbar reduces the text area but not the popup's fixed outer width.
                ContentSize = AccountQuotaView.MeasureDetail(graphics, text, dpi, area.Width, ClientSize.Width);
                measuredSize = new Size(measuredSize.Width, ContentSize.Height);
                AutoScrollMinSize = new Size(0, ContentSize.Height);
            }
        }
        DetailText = text;
        detailDpi = dpi;
        AccessibleDescription = text;
        AnchorPoint = pointer;
        Bounds = Place(pointer, measuredSize, area, dpi);
        if (layoutChanged) Invalidate();
        if (!Visible) Show(owner);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var content = new Rectangle(0, AutoScrollPosition.Y, ClientSize.Width, ContentSize.Height);
        AccountQuotaView.DrawDetail(e.Graphics, content, DetailText, detailDpi);
        using var border = new Pen(Color.FromArgb(200, 200, 200));
        e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void WndProc(ref Message message)
    {
        // Like a native tooltip, the popup must not intercept hover/clicks on the quota card below it.
        // Only a very long popup accepts pointer input, to make its scrollbar/wheel usable without activation.
        if (message.Msg == 0x0084 && !VerticalScroll.Visible) { message.Result = new IntPtr(-1); return; } // WM_NCHITTEST / HTTRANSPARENT
        base.WndProc(ref message);
    }
}
