using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AiBurgerClock;

internal static class StatusToolTipUiChecks
{
    internal static int CheckWrapping()
    {
        int count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new InvalidOperationException("Detail wrapping: " + message); }
        float Measure(string value) => StringInfo.ParseCombiningCharacters(value).Length;
        var words = AccountQuotaView.WrapDetailText("구독 사용 한도", 5, Measure);
        Check(words.SequenceEqual(["구독 사용", "한도"]), "Korean words move to the next line intact");
        string url = "https://synthetic.example.invalid/a/very/long/path";
        var urls = AccountQuotaView.WrapDetailText(url, 10, Measure);
        Check(string.Concat(urls) == url && urls.All(line => Measure(line) <= 10), "long URL wraps without losing characters");
        Check(AccountQuotaView.WrapDetailText("e\u0301👩‍💻🇰🇷한글", 1, Measure).SequenceEqual(["e\u0301", "👩‍💻", "🇰🇷", "한", "글"]),
            "fallback preserves combining characters and emoji clusters");
        Check(AccountQuotaView.WrapDetailText("첫 줄\n\n다음 줄", 20, Measure).SequenceEqual(["첫 줄", "", "다음 줄"]), "explicit blank lines remain");
        Check(AccountQuotaView.WrapDetailText("", 20, Measure).SequenceEqual([""]), "empty detail has one layout line");
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using var bitmap = new Bitmap(1, 1);
            bitmap.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(bitmap);
            float width = 304 * dpi / 96f;
            var lines = AccountQuotaView.DetailLines(graphics, "합성 구독 사용 한도 · " + url, width);
            using var format = AccountQuotaView.DetailFormat();
            format.FormatFlags |= StringFormatFlags.NoWrap;
            Check(lines.All(line => graphics.MeasureString(line, AccountQuotaView.DetailFont, int.MaxValue, format).Width <= width + .01f),
                dpi + " DPI real font lines fit the shared painter's content width");
        }
        return count;
    }

    internal static async Task RunAsync(StatusWindow owner, string? directory, Action<bool, string> check)
    {
        Point pointer = Point.Empty;
        using var host = new StatusWindow(() => pointer) { Bounds = owner.Bounds };
        var now = new DateTimeOffset(2032, 3, 4, 0, 0, 0, TimeSpan.Zero);
        var fixtures = StatusDetailTemplateTests.Fixtures(now).ToArray();
        var states = fixtures.Where(f => f.Scenario == "normal").Select(f => f.State).ToArray();
        var panels = host.Controls.OfType<Panel>().Where(panel => panel != host.QuotaView).ToArray();
        var controller = host.StatusDetails;
        var legacy = (ToolTip)typeof(StatusWindow).GetField("details", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var measurements = new List<object>();
        var opened = new List<ProviderKind>();
        int records = 0;
        host.StatusPageRequested += opened.Add;
        host.RecordRequested += (_, _, _) => records++;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var mouseMove = typeof(Control).GetMethod("OnMouseMove", flags)!;
        var mouseDown = typeof(Control).GetMethod("OnMouseDown", flags)!;
        var mouseClick = typeof(Control).GetMethod("OnMouseClick", flags)!;
        bool wasVisible = owner.Visible;
        await CheckStandaloneNativeAsync(owner, directory, check);
        host.Show();
        try
        {
            await SmokeUiProbe.SettleAsync(host);
            controller.Hide();
            states = fixtures.Where(f => f.Scenario == "normal").Select(f => f.State).ToArray();
            foreach (var provider in Enum.GetValues<ProviderKind>())
            {
                var panel = panels[(int)provider];
                host.ScrollControlIntoView(panel);
                var label = panel.Controls.OfType<Label>().First();
                check(new[] { (Control)panel }.Concat(panel.Controls.Cast<Control>()).All(control => string.IsNullOrEmpty(legacy.GetToolTip(control))),
                    provider + " status surfaces no longer register an auto-width native ToolTip");
                pointer = label.PointToScreen(new Point(4, 4));
                host.UpdateProviders(states, AgentSchedule.GetSnapshot(now), false, now.AddMinutes(5));
                mouseMove.Invoke(label, [new MouseEventArgs(MouseButtons.None, 0, 4, 4, 0)]);
                await WaitAsync(() => controller.Popup.Visible);
                foreach (var (scenario, state) in fixtures.Where(f => f.State.Provider == provider))
                {
                    states[(int)provider] = state;
                    host.UpdateProviders(states, AgentSchedule.GetSnapshot(now), false, now.AddMinutes(5));
                    var popup = controller.Popup;
                    string text = StatusPanelModel.Card(state, AgentState.FullThrottle).Detail;
                    var area = Screen.FromPoint(pointer).WorkingArea;
                    using var graphics = host.CreateGraphics();
                    int width = AccountQuotaView.MeasureDetail(graphics, text, host.DeviceDpi, area.Width).Width;
                    check(popup.Visible && popup.DetailText == text && popup.Width == width && area.Contains(popup.Bounds),
                        provider + "/" + scenario + " actual status popup uses the same fixed width and full shared detail");
                    check(host.Visible && new[] { (Control)panel }.Concat(panel.Controls.Cast<Control>()).All(control =>
                        controller.IsBound(control) && controller.DetailFor(control) == text && control.ContextMenuStrip is not null),
                        provider + "/" + scenario + " every card surface shares one non-activating popup and keeps the recording menu");
                    measurements.Add(new { Provider = provider.ToString(), Scenario = scenario, Dpi = host.DeviceDpi,
                        popup.Width, popup.Height, Scrollbar = popup.VerticalScroll.Visible });
                    if (scenario == "normal" && directory is not null)
                    {
                        using var bitmap = new Bitmap(popup.Width, popup.Height);
                        popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(directory, $"status-tooltip-{provider}-normal.png"), ImageFormat.Png);
                    }
                }
                mouseDown.Invoke(label, [new MouseEventArgs(MouseButtons.Left, 1, 4, 4, 0)]);
                mouseClick.Invoke(label, [new MouseEventArgs(MouseButtons.Left, 1, 4, 4, 0)]);
                check(!controller.Popup.Visible && opened.Count == (int)provider + 1 && opened[^1] == provider,
                    provider + " left click dismisses only the popup and reaches the original official-page event once");
                mouseClick.Invoke(label, [new MouseEventArgs(MouseButtons.Right, 1, 4, 4, 0)]);
                panel.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().First().PerformClick();
                check(opened.Count == (int)provider + 1 && records == (int)provider + 1,
                    provider + " right-click recording route remains separate from official-page clicks");
                pointer = new Point(host.Left - 100, host.Top - 100);
                controller.CheckPointer(pointer);
            }
            var target = panels[0];
            host.ScrollControlIntoView(target);
            pointer = target.PointToScreen(new Point(4, 4));
            string longText = StatusPanelModel.Card(states[0] with
            {
                Reason = string.Concat(Enumerable.Repeat("합성 상태 조회 오류의 긴 설명입니다. ", 80)),
                AssessmentIssue = "합성 범위 확인 오류",
                UncertainIncidentId = string.Join(", ", Enumerable.Range(1, 64).Select(i => "synthetic-uncertain-" + i))
            }, AgentState.FullThrottle).Detail;
            controller.Update(ProviderKind.OpenAI, longText);
            controller.Show(ProviderKind.OpenAI, pointer);
            check(controller.Popup.VerticalScroll.Visible && host.Visible, "Long status detail scrolls without hiding its owner");
            pointer = controller.Popup.Bounds.Location + new Size(12, 12);
            controller.CheckPointer(pointer);
            // The native input contract runs in the non-auto-hiding fixture above.
            // This real StatusWindow path keeps the controller/lifetime/accessibility checks.
            controller.Popup.AutoScrollPosition = new Point(0, controller.Popup.VerticalScroll.Maximum);
            check(controller.Popup.Visible && controller.Popup.AutoScrollPosition.Y < 0 &&
                controller.Popup.AccessibleDescription == longText, "Status detail reaches its last lines and retains complete accessible text");
            pointer = new Point(host.Left - 100, host.Top - 100);
            controller.CheckPointer(pointer);
            check(controller.Popup.Visible, "A scrollable status detail permits the pointer-to-popup gap until leave grace expires");
            controller.FinishLeave(pointer);
            check(!controller.Popup.Visible && !controller.HasInputFilter, "Leave-grace expiry removes the stale status popup without a MouseLeave event");
            pointer = target.PointToScreen(new Point(4, 4));
            controller.Show(ProviderKind.OpenAI, pointer);
            var lifetime = (System.Windows.Forms.Timer)typeof(StatusDetailController).GetField("lifetime", flags)!.GetValue(controller)!;
            check(lifetime.Enabled && lifetime.Interval == 25000, "Status popup retains the native tooltip's 25-second lifetime");
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(lifetime, [EventArgs.Empty]);
            check(!controller.Popup.Visible && !lifetime.Enabled, "Status lifetime expiry dismisses the popup without shortening production timeouts");
            controller.Show(ProviderKind.OpenAI, pointer);
            check(!controller.Popup.Visible, "An expired hover stays dismissed until the pointer leaves its Provider");
            pointer = new Point(host.Left - 100, host.Top - 100);
            controller.CheckPointer(pointer);
            pointer = target.PointToScreen(new Point(4, 4));
            controller.Show(ProviderKind.OpenAI, pointer);
            var escape = Message.Create(host.Handle, 0x0100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
            check(!((IMessageFilter)controller).PreFilterMessage(ref escape) && !controller.Popup.Visible,
                "Popup Escape is not consumed and preserves the status window's existing Escape route");
            controller.Update(ProviderKind.OpenAI, longText + "\n합성 갱신");
            check(!controller.Popup.Visible, "A refreshed status cannot immediately rearm a dismissed hover");
            pointer = new Point(host.Left - 100, host.Top - 100);
            controller.CheckPointer(pointer);
            pointer = target.PointToScreen(new Point(4, 4));
            controller.Show(ProviderKind.OpenAI, pointer);
            var altEscape = Message.Create(host.Handle, 0x0104, new IntPtr((int)Keys.Escape), IntPtr.Zero);
            check(!((IMessageFilter)controller).PreFilterMessage(ref altEscape) && controller.Popup.Visible,
                "Alt+Escape remains an operating-system shortcut, not a popup-close command");
            states = fixtures.Where(f => f.Scenario == "normal").Select(f => f.State).ToArray();
            controller.Hide();
            host.UpdateProviders(states, AgentSchedule.GetSnapshot(now), false, now.AddMinutes(5));
            pointer = new Point(host.Left - 100, host.Top - 100);
            controller.CheckPointer(pointer);
            pointer = target.PointToScreen(new Point(4, 4));
            controller.Show(ProviderKind.OpenAI, pointer);
            host.QuotaButton.PerformClick();
            check(host.QuotaView.Visible && !controller.Popup.Visible, "Switching to quota mode hides the active status detail immediately");
            host.QuotaButton.PerformClick();
            pointer = target.PointToScreen(new Point(4, 4));
            controller.Show(ProviderKind.OpenAI, pointer);
            host.Hide();
            check(!controller.Popup.Visible && !controller.HasInputFilter, "Hiding the owner dismisses status detail and removes its input filter");
            if (directory is not null)
                File.WriteAllText(Path.Combine(directory, "status-tooltip-layout.json"), System.Text.Json.JsonSerializer.Serialize(measurements));
            host.Show();
            pointer = target.PointToScreen(new Point(4, 4));
            controller.Show(ProviderKind.OpenAI, pointer);
            var disposedPopup = controller.Popup;
            host.Dispose();
            check(disposedPopup.IsDisposed && !controller.HasInputFilter, "Disposing a visible status detail releases its owned popup and input filter");
        }
        catch (Exception error)
        { SmokeUiProbe.Failure("status-card-fixture", host, null, controller.Popup, pointer, error); throw; }
        finally { if (!host.IsDisposed) host.Hide(); if (wasVisible) owner.Show(); }
    }

    private static async Task CheckStandaloneNativeAsync(StatusWindow reference, string? directory, Action<bool, string> check)
    {
        Point pointer = Point.Empty;
        using var host = new SmokeUiProbe.FixtureForm { AutoScroll = true };
        using var controller = new StatusDetailController(host, () => pointer);
        var providers = new[] { ProviderKind.OpenAI, ProviderKind.Claude, ProviderKind.Gemini };
        var panels = providers.Select(_ => new Panel()).ToArray();
        var now = new DateTimeOffset(2032, 3, 4, 0, 0, 0, TimeSpan.Zero);
        var normal = StatusDetailTemplateTests.Fixtures(now).Where(f => f.Scenario == "normal").Select(f => f.State).ToArray();
        Control? activeTarget = null;
        for (int i = 0; i < panels.Length; i++) { host.Controls.Add(panels[i]); controller.Bind(panels[i], providers[i]); }
        try
        {
            await SmokeUiProbe.ShowFixtureAsync(host, reference);
            int scale(int value) => (int)Math.Round(value * host.DeviceDpi / 96d);
            for (int i = 0; i < panels.Length; i++) panels[i].Bounds = new Rectangle(scale(8), scale(8 + i * 88), scale(330), scale(80));
            await SmokeUiProbe.SettleAsync(host);
            foreach (var state in normal) controller.Update(state.Provider, StatusPanelModel.Card(state, AgentState.FullThrottle).Detail);
            foreach (var provider in new[] { ProviderKind.Gemini, ProviderKind.OpenAI })
            {
                var panel = panels[Array.IndexOf(providers, provider)];
                activeTarget = panel;
                host.ScrollControlIntoView(panel);
                string text = StatusPanelModel.Card(normal[(int)provider] with
                {
                    Reason = string.Concat(Enumerable.Repeat("합성 상태 조회 오류의 긴 설명입니다. ", 80)),
                    AssessmentIssue = "합성 범위 확인 오류",
                    UncertainIncidentId = string.Join(", ", Enumerable.Range(1, 64).Select(i => "synthetic-incident-" + i))
                }, AgentState.FullThrottle).Detail;
                controller.Update(provider, text);
                pointer = panel.PointToScreen(new Point(4, 4));
                controller.Show(provider, pointer);
                pointer = controller.Popup.Bounds.Location + new Size(12, 12);
                controller.CheckPointer(pointer);
                await SmokeUiProbe.SettleAsync(controller.Popup);
                await CheckNativeWheelAsync(controller.Popup, host, pointer, "status/" + provider + "-long-error", check);
                if (directory is not null)
                {
                    using var bitmap = new Bitmap(controller.Popup.Width, controller.Popup.Height);
                    controller.Popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(directory, provider == ProviderKind.Gemini ? "status-tooltip-first-native-wheel-bottom.png" : "status-tooltip-overflow-bottom.png"), ImageFormat.Png);
                }
                controller.Hide();
                controller.Update(provider, StatusPanelModel.Card(normal[(int)provider], AgentState.FullThrottle).Detail);
            }
            host.ScrollControlIntoView(panels[1]);
            activeTarget = panels[1];
            pointer = panels[0].PointToScreen(new Point(4, 4));
            controller.Show(ProviderKind.OpenAI, pointer);
            pointer = panels[1].PointToScreen(new Point(4, 4));
            controller.Popup.Location = pointer;
            host.Text = "Synthetic title must never enter a native failure snapshot";
            check(SmokeUiProbe.ScopedTarget(host, new Point(host.Left - 1, host.Top - 1)) is null,
                "Parent-scoped native probe rejects a point outside its fixture");
            check(SmokeUiProbe.ScopedTarget(host, panels[0].PointToScreen(new Point(4, 4))) != panels[1],
                "Parent-scoped native probe distinguishes the neighboring card");
            string snapshot = SmokeUiProbe.Capture("synthetic-pre-cleanup", host, panels[1], controller.Popup, pointer, "SyntheticFailure");
            using (var captured = System.Text.Json.JsonDocument.Parse(snapshot))
                check(captured.RootElement.GetProperty("capturedBeforeFixtureCleanup").GetBoolean() &&
                    captured.RootElement.GetProperty("host").GetProperty("Visible").GetBoolean() &&
                    captured.RootElement.GetProperty("popup").GetProperty("Visible").GetBoolean(),
                    "Native failure capture retains the visible fixture and popup before cleanup");
            check(!snapshot.Contains(host.Text, StringComparison.Ordinal),
                "Native failure snapshot records window metadata without reading its title");
            await CheckNativePassThroughAsync(controller.Popup, panels[1],
                () => controller.Popup.Visible && controller.Popup.DetailText == StatusPanelModel.Card(normal[1], AgentState.FullThrottle).Detail,
                "status", check);
        }
        catch (Exception error)
        { SmokeUiProbe.Failure("status-native-fixture", host, activeTarget, controller.Popup, pointer, error); throw; }
    }

    internal static async Task CheckNativeWheelAsync(QuotaDetailPopup popup, Form owner, Point point, string kind, Action<bool, string> check)
    {
        var area = Screen.FromPoint(popup.AnchorPoint).WorkingArea;
        check(popup.ContentSize.Height > area.Height && popup.VerticalScroll.Visible && area.Contains(popup.Bounds),
            kind + " overflowing detail is height-limited to the work area with a scrollbar");
        using (var graphics = popup.CreateGraphics())
        {
            var required = AccountQuotaView.MeasureDetail(graphics, popup.DetailText, owner.DeviceDpi, area.Width, popup.ClientSize.Width);
            Console.WriteLine("INFO: native detail content fit " + System.Text.Json.JsonSerializer.Serialize(new
            {
                Kind = kind, Synthetic = true, StoredWidth = popup.ContentSize.Width, ClientWidth = popup.ClientSize.Width,
                StoredHeight = popup.ContentSize.Height, RequiredHeight = required.Height
            }));
            check(popup.ContentSize.Height >= required.Height,
                kind + " scroll range includes the final line at the actual scrollbar-reduced client width");
        }
        check(popup.CanInteractAt(point) && SendMessage(popup.Handle, 0x0084, IntPtr.Zero, Coordinates(point)).ToInt64() != -1,
            kind + " scrollable popup accepts native hit-testing without activation");
        int before = -popup.AutoScrollPosition.Y, messages = 0;
        using var queue = new SmokeUiProbe.QueueBarrier(popup);
        async Task Wheel(int delta)
        {
            bool delivered = false;
            MouseEventHandler received = (_, e) => { if (e.Delta == delta) delivered = true; };
            popup.MouseWheel += received;
            try
            {
                queue.Post(0x020A, new IntPtr((long)delta << 16), Coordinates(point));
                messages++;
                await queue.DrainAsync();
                if (!delivered) throw new InvalidOperationException(kind + " queued wheel did not reach the actual MouseWheel handler.");
            }
            finally { popup.MouseWheel -= received; }
        }
        await Wheel(-120);
        check(popup.Visible && -popup.AutoScrollPosition.Y > before,
            kind + " actual WM_MOUSEWHEEL scrolls the non-activating popup");
        int end = popup.VerticalScroll.Maximum - popup.VerticalScroll.LargeChange + 1;
        while (popup.Visible && -popup.AutoScrollPosition.Y < end && messages < 512) await Wheel(-120);
        check(popup.Visible && -popup.AutoScrollPosition.Y >= end &&
            -popup.AutoScrollPosition.Y + popup.ClientSize.Height >= popup.ContentSize.Height,
            kind + " wheel messages alone reach the final detail line");
        int bottom = -popup.AutoScrollPosition.Y;
        await Wheel(-120);
        check(-popup.AutoScrollPosition.Y == bottom, kind + " wheel is clamped at the bottom");
        await Wheel(120);
        check(-popup.AutoScrollPosition.Y < bottom, kind + " reverse wheel moves back up");
        await Wheel(-120);
        check(owner.Visible && popup.Visible && Form.ActiveForm != popup,
            kind + " wheel keeps the owner and popup open without activating the popup");
        Console.WriteLine("INFO: native detail wheel " + System.Text.Json.JsonSerializer.Serialize(new
        {
            Kind = kind, Synthetic = true, MessageCount = messages, ContentHeight = popup.ContentSize.Height,
            PopupHeight = popup.Height, WorkAreaHeight = area.Height, BottomOffset = bottom,
            Input = "queued WM_MOUSEWHEEL", PhysicalPointerMoved = false
        }));
    }

    internal static async Task CheckNativePassThroughAsync(QuotaDetailPopup popup, Control below, Func<bool> hoverReady,
        string kind, Action<bool, string> check)
    {
        var point = below.PointToScreen(new Point(4, 4));
        check(popup.Visible && !popup.VerticalScroll.Visible && popup.Bounds.Contains(point) && !popup.CanInteractAt(point) &&
            SendMessage(popup.Handle, 0x0084, IntPtr.Zero, Coordinates(point)).ToInt64() == -1,
            kind + " non-scrollable popup returns native HTTRANSPARENT over the card below");
        var root = below.FindForm() ?? throw new InvalidOperationException("Synthetic target has no fixture form.");
        var scoped = SmokeUiProbe.ScopedTarget(root, point);
        check(scoped == below || (scoped is not null && below.Contains(scoped)),
            kind + " parent-scoped hit-test finds the underlying card; popup WM_NCHITTEST is checked separately");
        int moves = 0, downs = 0, ups = 0;
        using var queue = new SmokeUiProbe.QueueBarrier(below);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler visible = (_, _) => { if (hoverReady()) shown.TrySetResult(); };
        MouseEventHandler moved = (_, _) => moves++;
        MouseEventHandler down = (_, _) => downs++;
        MouseEventHandler up = (_, _) => ups++;
        below.MouseMove += moved;
        below.MouseDown += down;
        below.MouseUp += up;
        popup.VisibleChanged += visible;
        try
        {
            void Post(uint message, int buttons = 0)
            {
                queue.Post(message, new IntPtr(buttons), Coordinates(new Point(4, 4)));
            }
            Post(0x0200); // WM_MOUSEMOVE, client coordinates; production hover handlers run.
            await queue.DrainAsync();
            if (!hoverReady()) await shown.Task.WaitAsync(TimeSpan.FromSeconds(4));
            check(moves > 0, kind + " native mouse move reaches the underlying card and changes its hover detail");
            Post(0x0201, 1); // WM_LBUTTONDOWN / MK_LBUTTON
            Post(0x0202); // WM_LBUTTONUP
            await queue.DrainAsync();
            // Control.WmMouseUp consults global WindowFromPoint before raising MouseClick.
            // Keep native delivery scoped; the real card's managed click/page route is tested separately above.
            check(downs == 1 && ups == 1, kind + " queued native button down/up reach the scoped card exactly once");
        }
        finally { below.MouseMove -= moved; below.MouseDown -= down; below.MouseUp -= up; popup.VisibleChanged -= visible; }
    }

    private static IntPtr Coordinates(Point point) => new(unchecked((int)((uint)(ushort)point.X | ((uint)(ushort)point.Y << 16))));
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static async Task WaitAsync(Func<bool> done, string timeout = "Synthetic status hover did not open its popup.")
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (started.Elapsed > TimeSpan.FromSeconds(4)) throw new InvalidOperationException(timeout);
            await Task.Delay(20);
        }
    }
}
