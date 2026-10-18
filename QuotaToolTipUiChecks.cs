using System.Drawing.Imaging;

namespace AiBurgerClock;

// Windows-only pointer anchoring and rendering; shared quota wording is checked by portable tests.
internal static class QuotaToolTipUiChecks
{
    internal static async Task RunAsync(AccountQuotaView view, Label[] labels, string? reportDirectory,
        Action<bool, string> check)
    {
        using var graphics = view.CreateGraphics();
        string longText = view.DetailFor(labels.Single(label => label.Text == "ChatGPT"));
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using var bitmap = new Bitmap(1, 1);
            bitmap.SetResolution(dpi, dpi);
            using var dpiGraphics = Graphics.FromImage(bitmap);
            var shortSize = AccountQuotaView.MeasureDetail(dpiGraphics, "리셋: 2026-10-10 22:00 KST", dpi, 1920);
            var longSize = AccountQuotaView.MeasureDetail(dpiGraphics, longText, dpi, 1920);
            check(shortSize.Width == (int)Math.Round(AccountQuotaView.DetailWidth * dpi / 96.0) && longSize.Width == shortSize.Width,
                $"Provider tooltip keeps a fixed 320-DIP width at {dpi} DPI");
            check(longSize.Height > shortSize.Height, $"Provider detail expands downwards, not sideways, at {dpi} DPI");
            var area = new Rectangle(-1920, -300, 1920, 1080);
            foreach (var pointer in new[] { new Point(-1500, 100), new Point(-10, 760), new Point(-1919, -299) })
            {
                var placed = QuotaDetailPopup.Place(pointer, shortSize, area, dpi);
                check(area.Contains(placed) && placed.Size == shortSize,
                    $"Pointer tooltip stays within a negative-origin monitor at {dpi} DPI: {pointer}");
            }
            var normalPointer = new Point(200, 200);
            var oversized = QuotaDetailPopup.Place(normalPointer, new Size(320, 5000), area, dpi);
            check(area.Contains(oversized) && oversized.Height < area.Height,
                $"Long Provider detail is height-limited within the monitor at {dpi} DPI");
            var nearby = QuotaDetailPopup.Place(normalPointer, shortSize, new Rectangle(0, 0, 1920, 1080), dpi);
            int gap = (int)Math.Round(16 * dpi / 96.0);
            check(nearby.Location == new Point(normalPointer.X + gap, normalPointer.Y + gap),
                $"Tooltip anchors beside the pointer instead of a label's old rectangle at {dpi} DPI");
        }
        var narrow = AccountQuotaView.MeasureDetail(graphics, longText, 96, 280);
        check(narrow.Width < 280 && narrow.Height > AccountQuotaView.MeasureDetail(graphics, longText, 96, 1920).Height,
            "Provider detail wraps within a narrow monitor");
        string token = "C:\\" + new string('x', 200) + "\\agy.exe";
        var tokenSize = AccountQuotaView.MeasureDetail(graphics, token, 96, 1920);
        using var format = AccountQuotaView.DetailFormat();
        graphics.MeasureString(token, AccountQuotaView.DetailFont, new SizeF(304, tokenSize.Height - 16), format,
            out int fitted, out int lines);
        check(fitted == token.Length && lines > 1, "Unbroken paths wrap without losing characters");

        using var testBar = new QuotaBalanceBar { Size = new Size(200, 6) };
        foreach (var (used, expected) in new[] { (0.0, 0), (100.0, 200), (25.0, 50), (-5.0, 0), (150.0, 200), (double.NaN, 0) })
        {
            testBar.UpdateBalance(QuotaProvider.Claude, used, PanelTone.Good, "test", "test");
            check(testBar.FilledWidth == expected, "Bar fill respects used percent and valid bounds: " + used);
        }
        QuotaBarUiChecks.Run(check);

        await CheckProviderAsync(view, QuotaProvider.Codex, "quota-tooltip-chatgpt.png", reportDirectory, check);
        await CheckProviderAsync(view, QuotaProvider.Claude, "quota-tooltip-claude.png", reportDirectory, check);
        await CheckOverflowAsync(view, reportDirectory, check);
        await CheckDismissalAsync(view.FindForm()!, check);
    }

    internal static Task CheckGeminiAsync(AccountQuotaView view, string? reportDirectory, Action<bool, string> check) =>
        CheckProviderAsync(view, QuotaProvider.Gemini, "quota-tooltip-gemini.png", reportDirectory, check);

    private static async Task CheckOverflowAsync(AccountQuotaView view, string? reportDirectory, Action<bool, string> check)
    {
        string text = string.Join('\n', Enumerable.Range(1, 100).Select(index =>
            $"모델 한도 {index}: 2026-10-10 22:00:00 KST · 실제 복원은 새 조회 필요"));
        var owner = view.FindForm()!;
        var pointer = view.PointToScreen(new Point(view.Width / 2, view.Height / 2));
        var popup = view.Details;
        try
        {
            view.ShowDetail(QuotaProvider.Codex, pointer); // Enable the real pending-provider/watchdog path.
            popup.Display(owner, text, pointer, view.DeviceDpi);
            // Layout/show are synchronous. This fixture does not move the real Cursor: draining
            // pointer events before evaluating the synthetic points would correctly dismiss it.
            check(Screen.FromPoint(pointer).WorkingArea.Contains(popup.Bounds) && popup.VerticalScroll.Visible,
                "Oversized Provider detail stays on-screen with a native vertical scrollbar");
            using var graphics = owner.CreateGraphics();
            using var format = AccountQuotaView.DetailFormat();
            int padding = (int)Math.Round(8 * view.DeviceDpi / 96.0);
            graphics.MeasureString(text, AccountQuotaView.DetailFont,
                new SizeF(popup.ContentSize.Width - 2 * padding, popup.ContentSize.Height - 2 * padding), format, out int fitted, out _);
            check(fitted == text.Length && popup.DetailText == text,
                "Long detail retains every character, with wrapping accounting for the scrollbar");
            popup.AutoScrollPosition = new Point(0, popup.ContentSize.Height);
            check(popup.AutoScrollPosition.Y < 0 && popup.CanInteractAt(popup.Bounds.Location + new Size(10, 10)),
                "Long detail can scroll to its last lines without activating the owner");
            if (reportDirectory is not null)
            {
                using var bitmap = new Bitmap(popup.ClientSize.Width, popup.ClientSize.Height);
                popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(reportDirectory, "quota-tooltip-overflow-bottom.png"), ImageFormat.Png);
            }
            var inside = popup.Bounds.Location + new Size(10, 10);
            view.CheckDetailPointer(inside);
            check(popup.Visible, "Pointer watchdog keeps a scrollable Provider detail usable");
            var outside = new Point(Math.Min(owner.Left, popup.Left) - 100, Math.Min(owner.Top, popup.Top) - 100);
            view.CheckDetailPointer(outside);
            check(popup.Visible, "Scrollable detail allows crossing the pointer-to-popup gap before dismissal");
            view.FinishDetailLeave(inside);
            check(popup.Visible, "Arriving inside the scrollable popup cancels dismissal");
            view.CheckDetailPointer(outside);
            view.FinishDetailLeave(outside);
            check(!popup.Visible, "Scrollable detail hides after the leave grace expires outside both regions");
            await Task.Yield();
        }
        finally { view.HideDetail(); }
    }

    private static async Task CheckProviderAsync(AccountQuotaView view, QuotaProvider provider, string file,
        string? reportDirectory, Action<bool, string> check)
    {
        var box = view.Controls.OfType<Panel>().Single(panel => panel.Name == provider + "QuotaBox");
        view.ScrollControlIntoView(box);
        string text = view.DetailFor(box);
        check(box.Controls.Cast<Control>().All(control => view.DetailFor(control) == text),
            "Every title, scope, quota row and bar shares one Provider tooltip: " + provider);
        check(text.Contains("리셋·한도 복원 예정", StringComparison.Ordinal) && text.Contains(" KST", StringComparison.Ordinal) &&
            text.Contains("실제 복원 여부는 새 조회로 확인", StringComparison.Ordinal),
            "Provider detail includes exact reset times without promising unverified recovery: " + provider);
        check(text.Split("기본 1시간", StringSplitOptions.None).Length == 2 && !text.Contains("사용 18% / 잔여 82%", StringComparison.Ordinal),
            "Provider detail keeps polling explanation once instead of repeating visible percentages: " + provider);
        Point pointer = view.PointToScreen(new Point(view.Width / 2, view.Height / 2));
        var owner = view.FindForm()!;
        bool ownerVisible = owner.Visible;
        EventHandler deactivated = (_, _) => Console.WriteLine($"INFO: tooltip owner Deactivate, activeForm={Form.ActiveForm?.GetType().Name}, popupVisible={view.Details.Visible}, ownerVisible={owner.Visible}.");
        EventHandler activated = (_, _) => Console.WriteLine($"INFO: tooltip popup Activated, activeForm={Form.ActiveForm?.GetType().Name}.");
        owner.Deactivate += deactivated;
        view.Details.Activated += activated;
        try
        {
            view.ShowDetail(provider, pointer);
            Console.WriteLine($"INFO: {provider} immediately after Show: ownerVisible={owner.Visible} (before={ownerVisible}), popupVisible={view.Details.Visible}, activeForm={Form.ActiveForm?.GetType().Name}.");
            var popup = view.Details;
            var area = Screen.FromPoint(pointer).WorkingArea;
            using var graphics = owner.CreateGraphics();
            var expected = AccountQuotaView.MeasureDetail(graphics, text, view.DeviceDpi, area.Width);
            Console.WriteLine($"INFO: {provider} popup visible={popup.Visible}, bounds={popup.Bounds}, expected={QuotaDetailPopup.Place(pointer, expected, area, view.DeviceDpi)}, client={popup.ClientSize}, content={popup.ContentSize}, ownerVisible={owner.Visible}, ownerDPI={view.DeviceDpi}, popupDPI={popup.DeviceDpi}.");
            check(popup.Visible && popup.Bounds == QuotaDetailPopup.Place(pointer, expected, area, view.DeviceDpi),
                "Native Provider popup uses measured size and screen-coordinate pointer placement: " + provider);
            check(owner.Visible == ownerVisible, "Provider popup does not deactivate and hide the status window: " + provider);
            using var bitmap = new Bitmap(popup.ClientSize.Width, popup.ClientSize.Height);
            popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            check(bitmap.GetPixel(bitmap.Width - 3, bitmap.Height - 3).ToArgb() == Color.White.ToArgb(),
                "Provider tooltip keeps the original white background, not yellow: " + provider);
            int padding = (int)Math.Round(8 * view.DeviceDpi / 96.0);
            using var format = AccountQuotaView.DetailFormat();
            graphics.MeasureString(text, AccountQuotaView.DetailFont,
                new SizeF(expected.Width - 2 * padding, expected.Height - 2 * padding), format, out int fitted, out _);
            check(fitted == text.Length, "Complete Provider detail fits the wrapped popup: " + provider);
            check(box.Controls.OfType<Label>().Where(label => label.Text.Contains('%')).All(label =>
                view.DetailFor(label) == text && label.AccessibleDescription != text) &&
                box.AccessibleDescription == text && text.Contains("리셋·한도 복원 예정", StringComparison.Ordinal) &&
                text.Contains(" KST", StringComparison.Ordinal),
                "Row accessibility is separate from the unchanged Provider tooltip with precise reset data: " + provider);
            if (reportDirectory is not null)
            {
                Directory.CreateDirectory(reportDirectory);
                bitmap.Save(Path.Combine(reportDirectory, file), ImageFormat.Png);
            }
            // Evaluate screen-coordinate fixtures without moving the user's real pointer or
            // sending MouseLeave: the production watchdog must independently clear stale details.
            var cardPoint = box.PointToScreen(new Point(2, 2));
            view.CheckDetailPointer(cardPoint);
            check(popup.Visible, "Pointer stays within its Provider without hiding the detail: " + provider);
            view.CheckDetailPointer(box.Controls.OfType<Label>().First().PointToScreen(new Point(2, 2)));
            check(popup.Visible, "Pointer movement between the same Provider's children keeps one tooltip: " + provider);
            var outside = new Point(Math.Min(owner.Left, popup.Left) - 100, Math.Min(owner.Top, popup.Top) - 100);
            view.CheckDetailPointer(outside);
            check(!popup.Visible, "Pointer watchdog hides detail outside its Provider without any MouseLeave event: " + provider);
            view.ShowDetail(provider, pointer);
            owner.Hide();
            view.CheckDetailPointer(cardPoint);
            check(!popup.Visible, "Pointer watchdog dismisses Provider detail when its owner is hidden: " + provider);
            if (ownerVisible) owner.Show();
            view.ShowDetail(provider, pointer);
            var lifetime = (System.Windows.Forms.Timer)typeof(AccountQuotaView).GetField("detailLifetime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(view)!;
            var watchdog = (System.Windows.Forms.Timer)typeof(AccountQuotaView).GetField("detailPointerMonitor",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(view)!;
            check(lifetime.Enabled && lifetime.Interval == 10000 && watchdog.Enabled && watchdog.Interval == 100,
                "Tooltip lifetime is bounded and the independent pointer watchdog is active: " + provider);
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(lifetime, [EventArgs.Empty]);
            check(!popup.Visible && !lifetime.Enabled && watchdog.Enabled,
                "Tooltip expiry stops its lifetime but watches for Provider exit before rearming: " + provider);
            // Show/DrawToBitmap complete synchronously. A screen-coordinate fixture does not move
            // the user's real pointer, so queued MouseLeave may correctly hide the popup afterwards.
            // Drain those messages separately and still ensure the non-activating popup never hides its owner.
            await Task.Yield();
            check(owner.Visible == ownerVisible, "Provider popup preserves its owner after queued pointer-leave messages: " + provider);
            Console.WriteLine($"INFO: {file} popup bounds={popup.Bounds}, pointer={pointer}, DPI={view.DeviceDpi}.");
        }
        finally { owner.Deactivate -= deactivated; view.Details.Activated -= activated; view.HideDetail(); }
    }

    // Actual WinForms timers/input filters, but synthetic data/coordinates: no user mouse movement,
    // account calls, DB, shortened product timeout, or direct invocation of the expiry callback.
    private static async Task CheckDismissalAsync(Form owner, Action<bool, string> check)
    {
        using var host = new DetailTestHost { Bounds = owner.Bounds, ShowInTaskbar = false };
        Point pointer = Point.Empty;
        using var view = new AccountQuotaView(() => pointer) { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        var now = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
        QuotaState[] states = [new(QuotaProvider.Codex, new(QuotaProvider.Codex,
            [new("session", "5시간", 50, now.AddHours(2), 300)]), now, now, now.AddHours(6))];
        bool updatingFixture = false;
        bool cleaningFixture = false;
        host.FormClosing += (_, args) => TraceLifetime("host-closing", args.CloseReason.ToString());
        host.FormClosed += (_, args) => TraceLifetime("host-closed", args.CloseReason.ToString());
        host.Disposed += (_, _) => TraceLifetime("host-disposed");
        view.Disposed += (_, _) => TraceLifetime("view-disposed");
        view.ControlRemoved += (_, args) => TraceLifetime("control-removed", args.Control?.Name);
        view.ControlAdded += (_, args) => TraceLifetime("control-added", args.Control?.Name);
        bool ownerWasVisible = owner.Visible;
        host.Show(); // Independent fixture: the real status owner's Deactivate policy must not own its lifetime.
        UpdateFixture(now);
        Control Box()
        {
            var boxes = view.Controls.OfType<Panel>().Where(box => box.Name == "CodexQuotaBox").ToArray();
            if (boxes.Length != 1)
            {
                TraceLifetime("box-missing", $"matches={boxes.Length}");
                throw new InvalidOperationException("Tooltip fixture lost CodexQuotaBox; see INFO: tooltip fixture lifetime diagnostics.");
            }
            return boxes[0];
        }
        void UpdateFixture(DateTimeOffset timestamp)
        {
            updatingFixture = true;
            try { view.UpdateQuotas(states, timestamp); }
            finally { updatingFixture = false; }
        }
        void TraceLifetime(string phase, string? detail = null)
        {
            Console.WriteLine("INFO: tooltip fixture lifetime " + System.Text.Json.JsonSerializer.Serialize(new
            {
                phase, detail, updatingFixture, cleaningFixture,
                thread = Environment.CurrentManagedThreadId,
                context = SynchronizationContext.Current?.GetType().Name,
                hostDisposed = host.IsDisposed, hostDisposing = host.Disposing, hostVisible = host.Visible,
                hostHandleCreated = host.IsHandleCreated,
                viewDisposed = view.IsDisposed, viewDisposing = view.Disposing, viewVisible = view.Visible,
                viewHandleCreated = view.IsHandleCreated,
                layoutKey = typeof(AccountQuotaView).GetField("layoutKey", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!.GetValue(view),
                parent = view.Parent?.GetType().Name,
                controls = view.Controls.Cast<Control>().Select(child => new { child.Name, type = child.GetType().Name, child.IsDisposed }).ToArray()
            }));
            if (!updatingFixture && !cleaningFixture && phase is "host-closing" or "view-disposed" or "control-removed" or "box-missing")
                Console.WriteLine("INFO: tooltip fixture lifetime stack " + Environment.StackTrace);
        }
        var onMouseMove = typeof(Control).GetMethod("OnMouseMove",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        void Move(Control control) => onMouseMove.Invoke(control, [new MouseEventArgs(MouseButtons.None, 0, 2, 2, 0)]);
        void Hover()
        {
            pointer = Box().PointToScreen(new Point(2, 2));
            Move(Box());
        }
        void Leave()
        {
            pointer = new Point(Math.Min(host.Left, view.Details.Left) - 100,
                Math.Min(host.Top, view.Details.Top) - 100);
            view.CheckDetailPointer(pointer);
            view.FinishDetailLeave(pointer);
            check(!view.Details.Visible, "Synthetic pointer exit is outside both the Provider and scrollable popup");
        }
        bool Input(int message, Keys key = Keys.None, int lParam = 0)
        {
            var input = Message.Create(host.Handle, message, (IntPtr)(int)key, (IntPtr)lParam);
            return Application.FilterMessage(ref input);
        }
        async Task WaitFor(Func<bool> condition, string label, int timeout = -1)
        {
            int limit = timeout < 0 ? Math.Max(5000, SystemInformation.MouseHoverTime + 2000) : timeout;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (elapsed.ElapsedMilliseconds >= limit)
                    throw new TimeoutException($"{label}: hostVisible={host.Visible}, viewVisible={view.Visible}, popupVisible={view.Details.Visible}");
                await Task.Delay(25);
            }
        }
        async Task Reenter()
        {
            Leave();
            Hover();
            await WaitFor(() => view.Details.Visible, "Provider detail reopens after leave/reentry");
        }
        void CheckSuppressed(string reason)
        {
            Move(Box());
            foreach (Control child in Box().Controls) Move(child);
            view.ShowDetail(QuotaProvider.Codex, pointer);
            UpdateFixture(now.AddMinutes(1));
            check(!view.Details.Visible, "Dismissed detail stays hidden across child hover/show/model refresh: " + reason);
        }
        try
        {
            Hover();
            await WaitFor(() => view.Details.Visible, "Real hover delay opens Provider detail");
            check(view.Details.Visible, "Real hover timer opens Provider detail using the controlled pointer");
            check(!host.ControlBox, "Synthetic tooltip fixture has no user close button");
            host.Close(); // Reproduce the manual close that previously disposed the Provider mid-await.
            check(!host.IsDisposed && !view.IsDisposed && host.Visible && Box().Parent == view && view.Details.Visible,
                "User close request preserves the active tooltip fixture and Provider controls");
            var lifetime = System.Diagnostics.Stopwatch.StartNew();
            bool stayedVisible = true;
            while (lifetime.Elapsed < TimeSpan.FromSeconds(9))
            {
                Move(Box());
                UpdateFixture(now.AddSeconds(lifetime.Elapsed.TotalSeconds));
                stayedVisible &= view.Details.Visible;
                await Task.Delay(100);
            }
            check(stayedVisible, "Detail remains visible for nine real seconds despite hover and model refresh");
            typeof(ScrollableControl).GetMethod("OnScroll", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.Invoke(view.Details,
                [new ScrollEventArgs(ScrollEventType.SmallIncrement, 1, ScrollOrientation.VerticalScroll)]);
            await WaitFor(() => !view.Details.Visible, "Real ten-second tooltip expiry", 6000);
            check(lifetime.Elapsed.TotalSeconds is >= 9 and < 15,
                "Real message-loop timer dismisses detail at ten seconds without restarting on refresh or scroll");
            Console.WriteLine($"INFO: real tooltip expiry after {lifetime.Elapsed.TotalSeconds:F2}s; product interval=10000ms.");
            CheckSuppressed("real timeout");
            // Rebuilding the rows while still inside the same Provider must preserve the guard.
            states = [states[0] with { Reading = new(QuotaProvider.Codex,
                [new("weekly", "주간", 50, now.AddDays(2), 10080)]) }];
            UpdateFixture(now);
            Hover();
            check(!view.Details.Visible, "Layout rebuild does not rearm a dismissed Provider");
            await Task.Delay(SystemInformation.MouseHoverTime + 150);
            check(!view.Details.Visible, "Same-box hover delay cannot resurrect an expired detail");
            await Reenter();
            check(view.Details.Visible, "Leaving the Provider and reentering permits a new tooltip");

            foreach (int message in new[] { 0x0201, 0x0204, 0x0207, 0x020B, 0x00A1 })
            {
                bool consumed = Input(message);
                check(!view.Details.Visible && !consumed,
                    $"Mouse-down 0x{message:X4} dismisses tooltip without consuming the original click");
                CheckSuppressed("click " + message);
                await Reenter();
            }
            check(!Input(0x0100, Keys.Space) && view.Details.Visible,
                "Unrelated keys remain available while a tooltip is visible");
            check(!Input(0x0104, Keys.Escape, 1 << 29) && view.Details.Visible,
                "Alt+Escape passes through the input filter without dismissing a visible tooltip");
            check(Input(0x0100, Keys.Escape) && !view.Details.Visible && host.Visible,
                "Escape reaches the non-focused Provider tooltip and closes only the tooltip");
            CheckSuppressed("Escape");
            host.Close();
            check(!host.IsDisposed && host.Visible && Box().Parent == view && !view.Details.Visible,
                "User close request also preserves an idle fixture without reopening the dismissed tooltip");
            check(!Input(0x0100, Keys.Escape), "Escape is not swallowed when no detail is pending or visible");
            await Reenter();
            check(view.Details.Visible, "Provider can reopen after an Escape dismissal and actual box exit");
            Leave();
            Hover(); // Cancel before the hover delay has fired.
            var pendingHover = (System.Windows.Forms.Timer)typeof(AccountQuotaView).GetField("detailDelay",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(view)!;
            check(!Input(0x0104, Keys.Escape, 1 << 29) && !view.Details.Visible && pendingHover.Enabled,
                "Alt+Escape passes through the input filter without cancelling a pending tooltip hover");
            check(Input(0x0100, Keys.Escape), "Escape cancels a pending hover as well as a visible detail");
            await Task.Delay(SystemInformation.MouseHoverTime + 150);
            CheckSuppressed("pending hover cancelled");
            host.Hide();
            TraceState("host-hidden");
            host.Show();
            TraceState("host-reshown");
            check(host.Visible && view.Visible, "Independent tooltip fixture is visible again after host hide/reopen");
            Hover();
            TraceState("hover-after-reshow");
            await WaitFor(() => view.Details.Visible, "Hidden/reopened owner resets its previous hover session");
            check(view.Details.Visible, "Owner hide/reopen starts a new hover session without a stale dismissal guard");
            Input(0x0100, Keys.Escape);
            states = [states[0], new(QuotaProvider.Claude, new(QuotaProvider.Claude,
                [new("weekly", "주간", 50, now.AddDays(2), 10080)]), now, now, now.AddHours(6))];
            UpdateFixture(now);
            var other = view.Controls.OfType<Panel>().Single(box => box.Name == "ClaudeQuotaBox");
            view.ScrollControlIntoView(other);
            pointer = other.PointToScreen(new Point(2, 2));
            Move(other);
            await WaitFor(() => view.Details.Visible, "Moving directly to another Provider opens its own tooltip");
            check(view.Details.DetailText.StartsWith("Claude", StringComparison.Ordinal),
                "Dismissing one Provider does not block a different Provider");
        }
        finally
        {
            cleaningFixture = true;
            view.HideDetail(); host.Hide();
            if (ownerWasVisible) owner.Show();
        }

        void TraceState(string phase)
        {
            object? Field(string name) => typeof(AccountQuotaView).GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view);
            var delay = (System.Windows.Forms.Timer)Field("detailDelay")!;
            var watchdog = (System.Windows.Forms.Timer)Field("detailPointerMonitor")!;
            var at = typeof(AccountQuotaView).GetMethod("ProviderAt", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.Invoke(view, [pointer]);
            Console.WriteLine($"INFO: tooltip fixture {phase}: host={host.Visible}, view={view.Visible}, popup={view.Details.Visible}, pending={Field("pendingDetail")}, dismissed={Field("dismissedDetail")}, delay={delay.Enabled}, watchdog={watchdog.Enabled}, filter={Field("filteringDetailInput")}, pointerProvider={at}.");
        }
    }

    private sealed class DetailTestHost : Form
    {
        public DetailTestHost()
        {
            Text = "AI Burger Clock 자동 검사";
            ControlBox = false;
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // This synthetic window must outlive awaited checks, even if the user closes it.
            // Normal using/Dispose cleanup and OS/application shutdown are not cancelled.
            if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
