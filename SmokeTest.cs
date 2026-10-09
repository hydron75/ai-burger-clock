using Timer = System.Windows.Forms.Timer;
using Microsoft.Win32;

namespace AiBurgerClock;

// Explicit developer-only check; no Windows clock changes. Registry checking is separately opt-in.
internal static class SmokeTest
{
    public static int Run(bool verifyAutoStart, string? reportDirectory)
    {
        var now = new DateTimeOffset(2026, 9, 19, 9, 59, 58, TimeSpan.FromHours(9));
        string testDirectory = Directory.CreateTempSubdirectory("AiBurgerClock-ui-tests-").FullName;
        var testStore = new UsageStore(Path.Combine(testDirectory, "ui-test.db"));
        var activity = new SmokeQueryActivity();
        var statusHandler = new TestStatusHttpHandler();
        using var statusHttp = new HttpClient(new SmokeStatusHttpHandler(activity, statusHandler));
        var openedPages = new List<Uri>();
        var quotaClient = new TestAccountQuotaClient(() => now, activity);
        using var context = new TrayApplicationContext(true, () => now, usageStore: testStore, statusHttpClient: statusHttp, openStatusPage: openedPages.Add, accountQuotaClient: quotaClient);
        using var diagnostics = new SmokeDiagnostics(context, activity);
        using var timer = new Timer { Interval = 1500 };
        var notifications = new List<AgentState>();
        var transitionApplyOrder = new List<bool>();
        int balloonEvents = 0;
        int step = 0;
        int exitCode = 0;
        int callbackSequence = 0;
        int callbacksInFlight = 0;
        context.TransitionNotificationRequested += notifications.Add;
        context.TransitionNotificationRequested += state => transitionApplyOrder.Add(
            context.StatusWindow.Controls.OfType<Label>().Any(label => label.Text == "●  " + TrayPresentation.StateName(state)) &&
            context.CurrentAppearance?.Schedule != state &&
            context.TrayIcon.BalloonTipTitle == (state == AgentState.FullThrottle ? "FULL THROTTLE 시작" : "BURGER TIME 시작") &&
            context.TrayIcon.BalloonTipIcon == (state == AgentState.FullThrottle ? ToolTipIcon.Info : ToolTipIcon.Warning));
        context.TrayIcon.BalloonTipShown += (_, _) => balloonEvents++;

        void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException(label);
            Console.WriteLine("PASS: " + label);
        }

        timer.Tick += async (_, _) =>
        {
            int callbackId = ++callbackSequence;
            callbacksInFlight++;
            diagnostics.Record("smoke.timer-enter", new { callbackId, callbacksInFlight, step });
            try
            {
                switch (step++)
                {
                    case 0:
                        await context.Initialization;
                        Check(!context.StatusWindow.Visible && context.TrayIcon.Visible, "Autostart opens only the native tray icon");
                        Check(context.TrayIcon.Text.Contains("BURGER TIME"), "Initial BURGER TIME tooltip");
                        Check(notifications.Count == 0, "No startup notification");
                        var oldIcon = context.TrayIcon.Icon;
                        now = now.AddSeconds(2);
                        context.ShowWindow(); // Must not consume the transition before the timer can notify.
                        Check(context.StatusWindow.Visible, "Status window opens");
                        Check(context.TrayIcon.Icon != oldIcon && context.TrayIcon.Text.Contains("FULL THROTTLE"), "Icon and tooltip change at Saturday 10:00");
                        Check(context.TrayIcon.Text.Contains("60:00:00"), "Weekend countdown is 60 hours");
                        Check(notifications.SequenceEqual(new[] { AgentState.FullThrottle }), "Open-at-transition requests exactly one FULL notification");
                        Check(transitionApplyOrder[0], "FULL event follows schedule UI and native notification, before icon update");
                        Check(context.TrayIcon.BalloonTipTitle == "FULL THROTTLE 시작", "Native FULL notification title");
                        RenderAndCheckLayout(context.StatusWindow, "full-throttle.png", reportDirectory);
                        context.RefreshStatus(true);
                        Check(notifications.Count == 1, "Refresh does not duplicate notification");
                        context.StatusWindow.Close();
                        Check(!context.StatusWindow.Visible && !context.StatusWindow.IsDisposed && context.TrayIcon.Visible, "Close hides the window and preserves the tray");
                        timer.Interval = 8000;
                        break;
                    case 1:
                        now = new DateTimeOffset(2026, 9, 21, 22, 0, 0, TimeSpan.FromHours(9));
                        context.RefreshStatus(true); // Equivalent to a timer tick after sleep/resume.
                        Check(notifications.SequenceEqual(new[] { AgentState.FullThrottle, AgentState.BurgerTime }), "Resume across boundary requests one BURGER notification");
                        Check(transitionApplyOrder[1], "BURGER event follows schedule UI and native notification, before icon update");
                        Check(context.TrayIcon.Text.Contains("BURGER TIME") && context.TrayIcon.Text.Contains("12:00:00"), "Monday 22:00 switches to BURGER with 12-hour countdown");
                        Check(context.TrayIcon.BalloonTipTitle == "BURGER TIME 시작", "Native BURGER notification title");
                        context.ShowWindow();
                        Check(context.StatusWindow.Visible && notifications.Count == 2, "Hidden window reopens without duplicate notification");
                        RenderAndCheckLayout(context.StatusWindow, "burger-time.png", reportDirectory);
                        break;
                    default:
                        timer.Stop();
                        now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(9));
                        context.RefreshStatus(false);
                        diagnostics.Phase = "ui.regression";
                        await UIRegressionChecks.RunAsync(context, testStore, statusHandler, reportDirectory, openedPages, now, value => now = value, diagnostics);
                        diagnostics.Phase = "holiday";
                        await HolidayUiChecks.RunAsync(context, testStore, statusHandler, value => now = value, reportDirectory);
                        diagnostics.Phase = "quota";
                        await AccountQuotaUiChecks.RunAsync(context, quotaClient, () => now, value => now = value, reportDirectory, diagnostics);
                        if (verifyAutoStart)
                            VerifyAutoStart(context, reportDirectory);
                        Console.WriteLine($"Windows BalloonTipShown events: {balloonEvents} (visual delivery depends on Windows notification settings).");
                        timer.Stop();
                        context.ExitApplication();
                        break;
                }
            }
            catch (Exception error)
            {
                exitCode = 1;
                diagnostics.Record("smoke.failure", new { error = error.ToString() });
                Console.Error.WriteLine("FAIL: " + error);
                timer.Stop();
                context.ExitApplication();
            }
            finally
            {
                callbacksInFlight--;
                diagnostics.Record("smoke.timer-exit", new { callbackId, callbacksInFlight, step });
            }
        };

        timer.Start();
        Application.Run(context);
        diagnostics.Dispose();
        context.Dispose();
        if (!context.StatusWindow.IsDisposed)
            return 1;
        Console.WriteLine("PASS: Clean message-loop exit and resource disposal");
        try { Directory.Delete(testDirectory, recursive: true); }
        catch (IOException) { Console.WriteLine("Temporary UI test data retained: " + testDirectory); }
        return exitCode;
    }

    internal static void RenderAndCheckLayout(StatusWindow window, string name, string? reportDirectory)
    {
        window.PerformLayout();
        foreach (Control control in window.Controls)
        {
            if (!window.ClientRectangle.Contains(control.Bounds))
                throw new InvalidOperationException($"Control clipped: {control.Text} at {control.Bounds}");
            if (control is Label label && !label.AutoSize)
            {
                Size required = TextRenderer.MeasureText(label.Text, label.Font);
                if (required.Width > label.Width || required.Height > label.Height)
                    throw new InvalidOperationException($"Label text clipped: {label.Text}, required {required}, available {label.Size}");
            }
        }
        if (reportDirectory is null)
            return;
        Directory.CreateDirectory(reportDirectory);
        using var bitmap = new Bitmap(window.Width, window.Height);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(reportDirectory, name), System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("PASS: Form layout and render " + name);
    }

    private static void VerifyAutoStart(TrayApplicationContext context, string? reportDirectory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AutoStartManager.RunKeyPath, true);
        using var approval = Registry.CurrentUser.CreateSubKey(AutoStartManager.ApprovalKeyPath, true);
        string valueName = AutoStartManager.ValueName;
        var previousRun = RegistryValueSnapshot.Read(key);
        var previousApproval = RegistryValueSnapshot.Read(approval);
        var menuItem = context.AutoStartMenuItem;
        var checkBox = context.StatusWindow.AutoStartCheckBox;
        string expected = AutoStartManager.CommandFor(Application.ExecutablePath);
        byte[] disabled = new byte[12];
        disabled[0] = 3;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(disabled, 4);
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Console.WriteLine("PASS: " + message);
        }
        try
        {
            key.SetValue(valueName, expected, RegistryValueKind.String);
            approval.SetValue(valueName, disabled, RegistryValueKind.Binary);
            context.RefreshAutoStartChecks();
            Check(!checkBox.Checked && !menuItem.Checked && checkBox.Text.Contains("차단됨") &&
                AutoStartManager.GetStatus().State == AutoStartState.DisabledByWindows,
                "Windows-disabled registration is unchecked with an explanation on both UI surfaces");
            var blockedRun = RegistryValueSnapshot.Read(key);
            var blockedApproval = RegistryValueSnapshot.Read(approval);
            context.ShowWindow();
            var opening = typeof(ToolStripDropDown).GetMethod("OnOpening",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            opening.Invoke(context.TrayIcon.ContextMenuStrip, [new System.ComponentModel.CancelEventArgs()]);
            Check(blockedRun.Matches(RegistryValueSnapshot.Read(key)) && blockedApproval.Matches(RegistryValueSnapshot.Read(approval)),
                "Window/menu opening never rewrites registration or silently re-enables Windows startup");
            RenderAndCheckLayout(context.StatusWindow, "autostart-disabled.png", reportDirectory);
            checkBox.Checked = true;
            Check(key.GetValue(valueName) as string == expected && approval.GetValue(valueName) is null &&
                checkBox.Checked && menuItem.Checked && AutoStartManager.IsEnabled(),
                "Explicit checkbox enable repairs the current EXE and clears only its Windows disable override");

            string oldExecutable = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath)!, "previous-version", "AI Burger Clock.exe");
            key.SetValue(valueName, AutoStartManager.CommandFor(oldExecutable), RegistryValueKind.String);
            approval.SetValue(valueName, disabled, RegistryValueKind.Binary);
            context.RefreshAutoStartChecks();
            Check(!checkBox.Checked && !menuItem.Checked && checkBox.Text.Contains("다른 경로") &&
                checkBox.AccessibleDescription!.Contains(oldExecutable), "Old executable registration is identified with both paths");
            RenderAndCheckLayout(context.StatusWindow, "autostart-other-path.png", reportDirectory);
            menuItem.PerformClick();
            Check(key.GetValue(valueName) as string == expected && approval.GetValue(valueName) is null &&
                checkBox.Checked && menuItem.Checked && AutoStartManager.IsEnabled(),
                "Explicit tray enable replaces the old path and synchronizes the checkbox");

            approval.SetValue(valueName, disabled, RegistryValueKind.Binary);
            opening.Invoke(context.TrayIcon.ContextMenuStrip, [new System.ComponentModel.CancelEventArgs()]);
            Check(!checkBox.Checked && !menuItem.Checked, "Tray menu opening detects an external Windows disable");
            byte[] unknown = new byte[12];
            unknown[0] = 55;
            approval.SetValue(valueName, unknown, RegistryValueKind.Binary);
            context.RefreshAutoStartChecks();
            var beforeUnknown = RegistryValueSnapshot.Read(approval);
            bool rejected = false;
            try { AutoStartManager.SetEnabled(true); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && beforeUnknown.Matches(RegistryValueSnapshot.Read(approval)) &&
                key.GetValue(valueName) as string == expected && !checkBox.Checked && checkBox.Text.Contains("확인 필요"),
                "Unknown approval format is not presented as enabled or silently overwritten");
            RenderAndCheckLayout(context.StatusWindow, "autostart-unknown.png", reportDirectory);
            approval.DeleteValue(valueName, false);
            context.RefreshAutoStartChecks();
            checkBox.Checked = false;
            Check(key.GetValue(valueName) is null && !menuItem.Checked && !AutoStartManager.IsEnabled(),
                "Checkbox disable removes this app's startup registration and synchronizes menu");
            menuItem.PerformClick();
            using var reopened = Registry.CurrentUser.OpenSubKey(AutoStartManager.RunKeyPath, false);
            Check(reopened?.GetValue(valueName) as string == expected && AutoStartManager.IsEnabled(),
                "Fresh registry handle reads back the persisted current EXE registration");
        }
        finally
        {
            try { previousRun.Restore(key); }
            finally { previousApproval.Restore(approval); }
            context.RefreshAutoStartChecks();
        }
        Check(previousRun.Matches(RegistryValueSnapshot.Read(key)) && previousApproval.Matches(RegistryValueSnapshot.Read(approval)),
            "Both original startup registry values and their exact types/bytes restored");
    }
}
