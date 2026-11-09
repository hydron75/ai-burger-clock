using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;

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
        host.Show();
        try
        {
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
            string longText = string.Join('\n', Enumerable.Repeat(StatusPanelModel.Card(states[0], AgentState.FullThrottle).Detail, 10));
            controller.Update(ProviderKind.OpenAI, longText);
            controller.Show(ProviderKind.OpenAI, pointer);
            check(controller.Popup.VerticalScroll.Visible && host.Visible, "Long status detail scrolls without hiding its owner");
            pointer = controller.Popup.Bounds.Location + new Size(12, 12);
            controller.CheckPointer(pointer);
            controller.Popup.AutoScrollPosition = new Point(0, controller.Popup.ContentSize.Height);
            check(controller.Popup.Visible && controller.Popup.AutoScrollPosition.Y < 0 &&
                controller.Popup.AccessibleDescription == longText, "Status detail reaches its last lines and retains complete accessible text");
            if (directory is not null)
            {
                using var bitmap = new Bitmap(controller.Popup.Width, controller.Popup.Height);
                controller.Popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, "status-tooltip-overflow-bottom.png"), ImageFormat.Png);
            }
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
        finally { if (!host.IsDisposed) host.Hide(); if (wasVisible) owner.Show(); }
    }

    private static async Task WaitAsync(Func<bool> done)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (started.Elapsed > TimeSpan.FromSeconds(4)) throw new InvalidOperationException("Synthetic status hover did not open its popup.");
            await Task.Delay(20);
        }
    }
}
