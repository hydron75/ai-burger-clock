namespace AiBurgerClock;

// Status-card hover/input lifetime. The existing quota popup owns fixed-width drawing and scrolling.
internal sealed class StatusDetailController : IMessageFilter, IDisposable
{
    private readonly Form owner;
    private readonly Func<Point> pointer;
    private readonly Dictionary<Control, ProviderKind> targets = new();
    private readonly Dictionary<ProviderKind, string> texts = new();
    private readonly System.Windows.Forms.Timer hover = new() { Interval = SystemInformation.MouseHoverTime };
    private readonly System.Windows.Forms.Timer leave = new() { Interval = SystemInformation.MouseHoverTime };
    private readonly System.Windows.Forms.Timer lifetime = new() { Interval = 25000 };
    private readonly System.Windows.Forms.Timer watch = new() { Interval = 100 };
    private ProviderKind? pending, dismissed;
    private bool filtering, disposed;
    internal QuotaDetailPopup Popup { get; } = new() { AccessibleName = "공식 상태 상세" };
    internal bool HasInputFilter => filtering;

    internal StatusDetailController(Form owner, Func<Point> pointer)
    {
        this.owner = owner;
        this.pointer = pointer;
        hover.Tick += (_, _) => { hover.Stop(); if (pending is {} provider && ProviderAt(pointer()) == provider) Show(provider, pointer()); else Hide(); };
        leave.Tick += (_, _) => FinishLeave(pointer());
        lifetime.Tick += (_, _) => Dismiss();
        watch.Tick += (_, _) => CheckPointer(pointer());
        Popup.MouseLeave += (_, _) => CheckPointer(pointer());
        owner.VisibleChanged += OwnerVisibilityChanged;
        owner.LocationChanged += OwnerLayoutChanged;
        owner.Resize += OwnerLayoutChanged;
        owner.Scroll += OwnerScrolled;
    }

    internal void Bind(Control control, ProviderKind provider)
    {
        targets.Add(control, provider);
        void Hover()
        {
            CheckPointer(pointer());
            if (dismissed == provider) return;
            leave.Stop();
            if (pending == provider)
            {
                if (Popup.Visible && !Popup.VerticalScroll.Visible) Show(provider, pointer());
                return;
            }
            Hide(); pending = provider; hover.Start(); Track();
        }
        control.MouseEnter += (_, _) => Hover();
        control.MouseMove += (_, _) => Hover();
        control.MouseLeave += (_, _) => CheckPointer(pointer());
        control.MouseDown += (_, _) => Dismiss(); // The original click and recording menu handlers still receive input.
    }

    internal bool IsBound(Control control) => targets.ContainsKey(control);
    internal string DetailFor(Control control) => targets.TryGetValue(control, out var provider) ? texts.GetValueOrDefault(provider, "") : "";

    internal void Update(ProviderKind provider, string text)
    {
        if (texts.GetValueOrDefault(provider) == text) return;
        texts[provider] = text;
        if (Popup.Visible && pending == provider)
            Popup.Display(owner, text, Popup.AnchorPoint, owner.DeviceDpi);
    }

    internal void Show(ProviderKind provider, Point point)
    {
        if (dismissed == provider || !owner.Visible || !texts.TryGetValue(provider, out var text) || text.Length == 0) return;
        if (pending != provider) lifetime.Stop();
        pending = provider; hover.Stop();
        Popup.Display(owner, text, point, owner.DeviceDpi);
        if (!lifetime.Enabled) lifetime.Start();
        Track();
    }

    private ProviderKind? ProviderAt(Point point)
    {
        if (!owner.Visible || !owner.RectangleToScreen(owner.ClientRectangle).Contains(point)) return null;
        return targets.Where(pair => pair.Key.Visible && pair.Key is Panel && pair.Key.RectangleToScreen(pair.Key.ClientRectangle).Contains(point))
            .Select(pair => (ProviderKind?)pair.Value).FirstOrDefault();
    }

    internal void CheckPointer(Point point)
    {
        if (!owner.Visible) { dismissed = null; Hide(); return; }
        var provider = ProviderAt(point);
        if (dismissed is not null && provider != dismissed)
        {
            dismissed = null;
            if (pending is null) StopTracking();
        }
        if (pending is null) return;
        if (provider == pending || Popup.CanInteractAt(point)) { leave.Stop(); return; }
        if (Popup.Visible && Popup.VerticalScroll.Visible)
        {
            if (!leave.Enabled) leave.Start();
        }
        else Hide();
    }

    internal void FinishLeave(Point point)
    {
        leave.Stop();
        if (ProviderAt(point) != pending && !Popup.CanInteractAt(point)) Hide();
    }

    private void Dismiss() { dismissed = pending ?? dismissed; Hide(); }

    internal void Hide()
    {
        hover.Stop(); leave.Stop(); lifetime.Stop(); pending = null; Popup.Hide();
        if (dismissed is not null) Track(); else StopTracking();
    }

    private void Track()
    {
        if (!filtering) { Application.AddMessageFilter(this); filtering = true; }
        watch.Start();
    }

    private void StopTracking()
    {
        watch.Stop();
        if (filtering) Application.RemoveMessageFilter(this);
        filtering = false;
    }

    bool IMessageFilter.PreFilterMessage(ref Message message)
    {
        if (pending is null) return false;
        // Do not consume Escape or clicks: the status window and its recording menus keep their existing behavior.
        if (message.Msg == 0x0100 && (Keys)message.WParam.ToInt32() == Keys.Escape) Dismiss();
        if (message.Msg is 0x0201 or 0x0203 or 0x0204 or 0x0206 or 0x0207 or 0x0209 or 0x020B or 0x020D
            or 0x00A1 or 0x00A3 or 0x00A4 or 0x00A6 or 0x00A7 or 0x00A9 or 0x00AB or 0x00AD)
            if (!Popup.CanInteractAt(pointer())) Dismiss();
        return false;
    }

    private void OwnerVisibilityChanged(object? sender, EventArgs e) { if (!owner.Visible) { dismissed = null; Hide(); } }
    private void OwnerLayoutChanged(object? sender, EventArgs e) { dismissed = null; Hide(); }
    private void OwnerScrolled(object? sender, ScrollEventArgs e) { dismissed = null; Hide(); }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        dismissed = null; Hide();
        owner.VisibleChanged -= OwnerVisibilityChanged;
        owner.LocationChanged -= OwnerLayoutChanged;
        owner.Resize -= OwnerLayoutChanged;
        owner.Scroll -= OwnerScrolled;
        hover.Dispose(); leave.Dispose(); lifetime.Dispose(); watch.Dispose(); Popup.Dispose();
    }
}
