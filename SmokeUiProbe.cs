using System.Runtime.InteropServices;
using System.Text.Json;

namespace AiBurgerClock;

// Explicit --self-test/--smoke-test only. Never reads another window's title or text.
internal static class SmokeUiProbe
{
    internal static async Task SettleAsync(Form host)
    {
        host.PerformLayout();
        using var queue = new QueueBarrier(host);
        await queue.DrainAsync();
        host.PerformLayout();
    }

    internal static async Task ShowFixtureAsync(Form host, Form reference)
    {
        var area = Screen.FromControl(reference).WorkingArea;
        host.StartPosition = FormStartPosition.Manual;
        var physicalBounds = new Rectangle(area.Left + Math.Min(16, area.Width / 4), area.Top + Math.Min(16, area.Height / 4),
            Math.Max(1, Math.Min(reference.Width, area.Width - Math.Min(32, area.Width / 2))),
            Math.Max(1, Math.Min(reference.Height, area.Height - Math.Min(32, area.Height / 2))));
        host.Location = physicalBounds.Location;
        host.Show(); // Independent non-activating fixture; the real status owner may auto-hide.
        host.Bounds = physicalBounds; // Physical geometry is assigned once, after initial handle/DPI creation.
        await SettleAsync(host);
        if (!area.Contains(host.Bounds)) throw new InvalidOperationException("Synthetic fixture exceeds its selected working area after layout.");
        Console.WriteLine("INFO: native fixture layout " + JsonSerializer.Serialize(new
            { host.DeviceDpi, host.AutoScaleMode, host.Bounds, host.ClientRectangle, selectedWorkingArea = area }));
    }

    internal static Control? ScopedTarget(Control root, Point screenPoint)
    {
        if (!root.Visible || !root.RectangleToScreen(root.ClientRectangle).Contains(screenPoint)) return null;
        Control current = root;
        while (current.GetChildAtPoint(current.PointToClient(screenPoint),
            GetChildAtPointSkip.Invisible | GetChildAtPointSkip.Disabled) is { } child)
            current = child;
        return current;
    }

    internal static Rectangle LayoutArea(ScrollableControl control) =>
        control.AutoScroll ? control.DisplayRectangle : control.ClientRectangle;

    internal static void Failure(string kind, Form host, Control? target, QuotaDetailPopup? popup, Point? point, Exception error)
    {
        try { Console.Error.WriteLine("UI-FAILURE: " + Capture(kind, host, target, popup, point, error.GetType().Name)); }
        catch (Exception captureError)
        { Console.Error.WriteLine("UI-FAILURE: " + JsonSerializer.Serialize(new { kind, captureUnavailable = captureError.GetType().Name })); }
    }

    internal static string Capture(string kind, Form host, Control? target, QuotaDetailPopup? popup, Point? point, string failure)
    {
        IntPtr hostHandle = host.IsHandleCreated ? host.Handle : IntPtr.Zero;
        IntPtr popupHandle = popup?.IsHandleCreated == true ? popup.Handle : IntPtr.Zero;
        IntPtr targetHandle = target?.IsHandleCreated == true ? target.Handle : IntPtr.Zero;
        var observed = point is { } p ? WindowFromPoint(p) : IntPtr.Zero;
        return JsonSerializer.Serialize(new
        {
            kind, synthetic = true, failure, capturedBeforeFixtureCleanup = true,
            utc = DateTimeOffset.UtcNow, screenPoint = point,
            host = new { host.Visible, host.Enabled, host.Bounds, host.ClientRectangle, host.DisplayRectangle,
                host.DeviceDpi, host.AutoScaleMode, host.AutoScaleDimensions, host.CurrentAutoScaleDimensions,
                host.AutoScroll, host.AutoScrollPosition, host.AutoScrollMinSize },
            target = target is null ? null : new { target.Visible, target.Enabled, target.Bounds,
                screenBounds = target.RectangleToScreen(target.ClientRectangle), target.DeviceDpi },
            popup = popup is null ? null : new { popup.Visible, popup.Bounds, popup.ClientSize, popup.ContentSize,
                popup.DeviceDpi, popup.AnchorPoint, popup.AutoScrollPosition, popup.AutoScrollMinSize,
                vertical = new { popup.VerticalScroll.Visible, popup.VerticalScroll.Value, popup.VerticalScroll.Maximum, popup.VerticalScroll.LargeChange } },
            observedWindow = Describe(observed, hostHandle, targetHandle, popupHandle),
            foregroundWindow = Describe(GetForegroundWindow(), hostHandle, targetHandle, popupHandle),
            expectedWindows = new
            {
                host = Describe(hostHandle, hostHandle, targetHandle, popupHandle),
                target = Describe(targetHandle, hostHandle, targetHandle, popupHandle),
                popup = Describe(popupHandle, hostHandle, targetHandle, popupHandle)
            },
            dpiAwareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()),
            screens = Screen.AllScreens.Select((screen, index) => new { index, screen.Primary, screen.Bounds, screen.WorkingArea }).ToArray()
        });
    }

    // Allowlisted external metadata only: no GetWindowText, class names, process names or body APIs.
    private sealed record WindowMetadata(long Hwnd, uint ProcessId, uint ThreadId, long RootHwnd, long OwnerHwnd,
        string Relation, bool Visible, Rectangle Bounds);

    private static WindowMetadata Describe(IntPtr window, IntPtr host, IntPtr target, IntPtr popup)
    {
        uint thread = GetWindowThreadProcessId(window, out uint process);
        GetWindowRect(window, out var bounds);
        string relation = window == IntPtr.Zero ? "none" : window == target ? "target" : window == popup ? "popup" :
            window == host ? "fixture" : IsChild(host, window) ? "fixture-child" :
            process == Environment.ProcessId ? "same-process-other" : "external";
        return new(window.ToInt64(), process, thread, GetAncestor(window, 2).ToInt64(), GetWindow(window, 4).ToInt64(), relation, IsWindowVisible(window),
            Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
    }

    internal static int CheckPrivacyContract()
    {
        string json = JsonSerializer.Serialize(new WindowMetadata(17, 23, 29, 17, 0, "external", true, new Rectangle(1, 2, 3, 4)));
        using var document = JsonDocument.Parse(json);
        string[] names = document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(new[] { "Bounds", "Hwnd", "OwnerHwnd", "ProcessId", "Relation", "RootHwnd", "ThreadId", "Visible" }))
            throw new InvalidOperationException("External window diagnostics must retain only the allowlisted geometry/identity fields.");
        if (json.Contains("Title", StringComparison.OrdinalIgnoreCase) || json.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("Body", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("External window content must not enter smoke diagnostics.");
        return 2;
    }

    internal sealed class FixtureForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        internal FixtureForm()
        {
            Text = "AI Burger Clock 합성 검사";
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
        }
    }

    // A posted sentinel follows the input message in this same HWND's UI queue.
    // Each input is sent once; this waits for completion rather than retrying the input or assertion.
    internal sealed class QueueBarrier : NativeWindow, IDisposable
    {
        private const int BarrierMessage = 0x8000 + 0x456;
        private static long nextId;
        private TaskCompletionSource? pending;
        private long id;

        internal QueueBarrier(Control control) => AssignHandle(control.Handle);

        internal void Post(uint message, IntPtr wParam, IntPtr lParam)
        {
            if (!PostMessage(Handle, message, wParam, lParam))
                throw new InvalidOperationException("Synthetic native message submission failed.");
        }

        internal async Task DrainAsync()
        {
            if (pending is not null) throw new InvalidOperationException("Overlapping synthetic queue barriers are not allowed.");
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = completion;
            id = Interlocked.Increment(ref nextId);
            try
            {
                Post(BarrierMessage, new IntPtr(id), IntPtr.Zero);
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(4));
            }
            finally { pending = null; }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == BarrierMessage && message.WParam.ToInt64() == id && pending is { } completion)
            { completion.TrySetResult(); message.Result = IntPtr.Zero; return; }
            base.WndProc(ref message);
        }

        public void Dispose() => ReleaseHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
