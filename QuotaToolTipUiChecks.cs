using System.Drawing.Imaging;

namespace AiBurgerClock;

// Windows-only pointer anchoring, rendering and compact usage presentation; synthetic data only.
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

        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        check(AccountQuotaView.CompactReset(null, now) == "리셋 미제공", "Missing reset is explicit, not guessed");
        check(AccountQuotaView.CompactReset(now, now) == "갱신 대기", "Elapsed reset does not claim restored balance");
        check(AccountQuotaView.CompactReset(now.AddSeconds(59), now) == "곧 리셋 예정", "Sub-minute reset is compact without a running seconds counter");
        check(AccountQuotaView.CompactReset(now.AddMinutes(59), now, 300) == "약 0시간 59분 후 리셋", "Session countdown keeps hours and minutes below one hour");
        check(AccountQuotaView.CompactReset(now.AddHours(1), now, 300) == "약 1시간 0분 후 리셋", "Session hour boundary keeps a zero-minute component");
        check(AccountQuotaView.CompactReset(now.AddHours(2).AddMinutes(37), now, 300) == "약 2시간 37분 후 리셋", "Session countdown includes both hours and minutes");
        check(AccountQuotaView.CompactReset(now.AddDays(1), now, 10080) == "약 1일 0시간 후 리셋", "Weekly day boundary keeps a zero-hour component");
        check(AccountQuotaView.CompactReset(now.AddDays(3).AddHours(4), now, 10080) == "약 3일 4시간 후 리셋", "Weekly countdown includes both days and hours");
        check(AccountQuotaView.CompactReset(now.AddHours(7), now, 10080) == "약 0일 7시간 후 리셋", "Weekly countdown stays in days and hours below one day");
        check(AccountQuotaView.CompactReset(now.AddMinutes(25), now, 10080) == "약 0일 1시간 후 리셋", "Short weekly countdown rounds up the displayed hour instead of claiming zero");
        check(AccountQuotaView.CompactReset(now.AddMinutes(119).AddSeconds(1), now, 300) == "약 2시간 0분 후 리셋", "Rounded session minute carries cleanly into the next hour");
        check(AccountQuotaView.CompactReset(now.AddHours(23).AddSeconds(1), now, 10080) == "약 1일 0시간 후 리셋", "Rounded weekly hour carries cleanly into the next day");
        check(AccountQuotaView.CompactReset(now.AddHours(25), now, 300) == "약 25시간 0분 후 리셋", "Session formatting follows the window kind, not the remaining duration");
        check(AccountQuotaView.CompactReset(now.AddDays(2).AddHours(3), now) == "약 2일 3시간 후 리셋", "Unknown window duration retains automatic day/hour formatting for long resets");

        using var testBar = new QuotaBalanceBar { Size = new Size(200, 6) };
        foreach (var (used, expected) in new[] { (0.0, 0), (100.0, 200), (25.0, 50), (-5.0, 0), (150.0, 200), (double.NaN, 0) })
        {
            testBar.UpdateBalance(QuotaProvider.Claude, used, PanelTone.Good, "test", "test");
            check(testBar.FilledWidth == expected, "Bar fill respects used percent and valid bounds: " + used);
        }

        await CheckProviderAsync(view, QuotaProvider.Codex, "quota-tooltip-chatgpt.png", reportDirectory, check);
        await CheckProviderAsync(view, QuotaProvider.Claude, "quota-tooltip-claude.png", reportDirectory, check);
        await CheckOverflowAsync(view, reportDirectory, check);
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
        check(text.Split("기본 6시간", StringSplitOptions.None).Length == 2 && !text.Contains("사용 18% / 잔여 82%", StringComparison.Ordinal),
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
                label.AccessibleDescription?.Contains("리셋:", StringComparison.Ordinal) == true),
                "Screen-reader row descriptions retain their original precise reset data: " + provider);
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
            check(lifetime.Enabled && lifetime.Interval == 30000 && watchdog.Enabled && watchdog.Interval == 100,
                "Tooltip lifetime is bounded and the independent pointer watchdog is active: " + provider);
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(lifetime, [EventArgs.Empty]);
            check(!popup.Visible && !lifetime.Enabled && !watchdog.Enabled,
                "Tooltip expiry dismisses the detail and stops both timers: " + provider);
            // Show/DrawToBitmap complete synchronously. A screen-coordinate fixture does not move
            // the user's real pointer, so queued MouseLeave may correctly hide the popup afterwards.
            // Drain those messages separately and still ensure the non-activating popup never hides its owner.
            await Task.Yield();
            check(owner.Visible == ownerVisible, "Provider popup preserves its owner after queued pointer-leave messages: " + provider);
            Console.WriteLine($"INFO: {file} popup bounds={popup.Bounds}, pointer={pointer}, DPI={view.DeviceDpi}.");
        }
        finally { owner.Deactivate -= deactivated; view.Details.Activated -= activated; view.HideDetail(); }
    }
}
