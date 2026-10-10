using System.Drawing.Drawing2D;

namespace AiBurgerClock;

// Native Windows painting only: the value and previous/current tone are supplied by the shared model.
internal sealed class QuotaBalanceBar : Control
{
    internal double UsedPercent { get; private set; }
    internal Color FillColor { get; private set; }
    internal int FilledWidth => (int)Math.Round(ClientSize.Width * UsedPercent / 100.0);

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

    internal void UpdateBalance(QuotaProvider provider, double used, PanelTone tone, string name, string detail)
    {
        UsedPercent = double.IsFinite(used) ? Math.Clamp(used, 0, 100) : 0;
        FillColor = tone == PanelTone.Muted ? Color.Gray : ProviderColor(provider);
        AccessibleName = name;
        AccessibleDescription = detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float diameter = Math.Min(ClientSize.Height, ClientSize.Width);
        using var track = new GraphicsPath();
        track.AddArc(0, 0, diameter, diameter, 90, 180);
        track.AddArc(ClientSize.Width - diameter, 0, diameter, diameter, 270, 180);
        track.CloseFigure();
        using var background = new SolidBrush(Color.FromArgb(232, 234, 237));
        e.Graphics.FillPath(background, track);
        if (FilledWidth <= 0) return;
        var saved = e.Graphics.Save();
        e.Graphics.SetClip(track);
        using var fill = new SolidBrush(FillColor);
        e.Graphics.FillRectangle(fill, 0, 0, FilledWidth, ClientSize.Height);
        e.Graphics.Restore(saved);
    }
}
