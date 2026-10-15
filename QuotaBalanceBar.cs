using System.Drawing.Drawing2D;

namespace AiBurgerClock;

// Native Windows painting only: the value and previous/current tone are supplied by the shared model.
internal sealed class QuotaBalanceBar : Control
{
    private (Color Fill, Color Background, bool HighContrast, Color Track, Color Highlight)? paletteKey;
    private QuotaBarPalette? cachedPalette;
    internal double UsedPercent { get; private set; }
    internal double? ElapsedPercent { get; private set; }
    internal Color FillColor { get; private set; }
    internal int FilledWidth => (int)Math.Round(ClientSize.Width * UsedPercent / 100.0);
    internal int ElapsedWidth => ElapsedPercent is { } elapsed ? (int)Math.Round(ClientSize.Width * elapsed / 100.0) : 0;

    internal int MarkerWidth(int dpi) => Math.Min(ClientSize.Width,
        Math.Max(1, (int)Math.Round(dpi / 96.0, MidpointRounding.AwayFromZero)));
    internal int MarkerLeft(int dpi) => Math.Clamp(ElapsedWidth - MarkerWidth(dpi) / 2, 0,
        Math.Max(0, ClientSize.Width - MarkerWidth(dpi)));

    internal QuotaBarPalette Palette(bool highContrast = false, Color? highContrastTrack = null, Color? highContrastFill = null)
    {
        var key = (FillColor, BackColor, highContrast, highContrastTrack ?? SystemColors.Window,
            highContrastFill ?? SystemColors.Highlight);
        if (cachedPalette is null || paletteKey != key)
        {
            cachedPalette = QuotaBarPalette.Create(FillColor, BackColor, highContrast, key.Item4, key.Item5);
            paletteKey = key;
        }
        return cachedPalette;
    }

    internal static Color ProviderColor(QuotaProvider provider) => provider switch
    {
        QuotaProvider.Codex => Color.FromArgb(16, 163, 127),
        QuotaProvider.Claude => Color.FromArgb(217, 119, 87),
        _ => Color.FromArgb(66, 133, 244)
    };

    public QuotaBalanceBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
        AccessibleRole = AccessibleRole.ProgressBar;
        TabStop = false;
        BackColor = Color.White;
    }

    internal void UpdateBalance(QuotaProvider provider, double used, PanelTone tone, string name, string detail,
        double? elapsedPercent = null)
    {
        UsedPercent = double.IsFinite(used) ? Math.Clamp(used, 0, 100) : 0;
        ElapsedPercent = tone != PanelTone.Muted && elapsedPercent is { } elapsed && double.IsFinite(elapsed) &&
            elapsed >= 0 && elapsed <= 100 ? elapsed : null;
        FillColor = tone == PanelTone.Muted ? Color.Gray : ProviderColor(provider);
        AccessibleName = name;
        AccessibleDescription = detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawBar(e.Graphics, DeviceDpi, SystemInformation.HighContrast);
    }

    // The production painter and bitmap checks use the same path. DPI is explicit for off-screen
    // checks; unlike a new message loop, this does not create or display a native window.
    internal void DrawBar(Graphics graphics, int dpi, bool highContrast = false,
        Color? highContrastTrack = null, Color? highContrastFill = null)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        var palette = Palette(highContrast, highContrastTrack, highContrastFill);
        var original = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float diameter = Math.Min(ClientSize.Height, ClientSize.Width);
        using var track = new GraphicsPath();
        track.AddArc(0, 0, diameter, diameter, 90, 180);
        track.AddArc(ClientSize.Width - diameter, 0, diameter, diameter, 270, 180);
        track.CloseFigure();
        using var background = new SolidBrush(palette.Track);
        graphics.FillPath(background, track);
        graphics.SetClip(track);
        if (ElapsedPercent is not null && palette.HasDistinctTint && ElapsedWidth > 0)
        {
            using var elapsed = new SolidBrush(palette.ElapsedFill);
            graphics.FillRectangle(elapsed, 0, 0, ElapsedWidth, ClientSize.Height);
        }
        if (FilledWidth > 0)
        {
            using var fill = new SolidBrush(palette.UsedFill);
            graphics.FillRectangle(fill, 0, 0, FilledWidth, ClientSize.Height);
        }
        if (ElapsedPercent is not null)
        {
            // Draw last, including when E < U or U == 0. Integer rectangles align the 1-DIP core
            // to physical pixels. Clamp 0/100% inside the rounded path instead of clipping it away.
            graphics.SmoothingMode = SmoothingMode.None;
            int x = MarkerLeft(dpi), width = MarkerWidth(dpi);
            if (palette.MarkerHalo is { } haloColor)
            {
                using var halo = new SolidBrush(haloColor);
                graphics.FillRectangle(halo, Math.Max(0, x - 1), 0,
                    Math.Min(ClientSize.Width, x + width + 1) - Math.Max(0, x - 1), ClientSize.Height);
            }
            using var marker = new SolidBrush(palette.Marker);
            graphics.FillRectangle(marker, x, 0, width, ClientSize.Height);
        }
        graphics.Restore(original);
    }
}
