using Timer = System.Windows.Forms.Timer;
using Microsoft.Win32;

namespace AiBurgerClock;

// Runs inside the explicit smoke test's real WinForms message loop against an isolated DB.
internal static class UIRegressionChecks
{
    public static async Task RunAsync(TrayApplicationContext context, UsageStore store,
        TestStatusHttpHandler handler, string? reportDirectory, List<Uri> openedPages)
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("UI integration: " + message);
            Console.WriteLine("PASS: " + message);
        }
        var monitor = context.ProviderMonitor ?? throw new InvalidOperationException("Test monitor missing");
        var mouseClick = typeof(Control).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var panels = context.StatusWindow.Controls.OfType<Panel>().ToArray();
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var panel = panels[(int)provider];
            foreach (var surface in new[] { (Control)panel }.Concat(panel.Controls.Cast<Control>()))
            {
                int before = openedPages.Count;
                mouseClick.Invoke(surface, [new MouseEventArgs(MouseButtons.Left, 1, 4, 4, 0)]);
                Check(openedPages.Count == before + 1 && openedPages.Last() == ProviderStatusPages.For(provider),
                    provider + " row/label left click routes to official HTTPS status page");
                mouseClick.Invoke(surface, [new MouseEventArgs(MouseButtons.Right, 1, 4, 4, 0)]);
                mouseClick.Invoke(surface, [new MouseEventArgs(MouseButtons.Middle, 1, 4, 4, 0)]);
                Check(openedPages.Count == before + 1 && surface.ContextMenuStrip is not null && surface.Cursor == Cursors.Hand,
                    "Right/middle click does not open browser; recording menu and hand cursor preserved");
            }
        }
        Check((await store.ReadUsageAsync(null)).Count == 0, "Opening official pages never creates usage records");
        await WaitUntilAsync(() => Task.FromResult(!monitor.IsRefreshing && monitor.NextRefreshUtc.HasValue));
        int requests = handler.RequestCount;
        context.StatusWindow.Controls.OfType<Button>().Single(b => b.Text == "Refresh").PerformClick();
        await WaitUntilAsync(() => Task.FromResult(handler.RequestCount > requests && !monitor.IsRefreshing && monitor.NextRefreshUtc.HasValue));
        Check(true, "Main Refresh button triggers asynchronous provider update");
        requests = handler.RequestCount;
        context.TrayIcon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "공식 상태 새로 고침").PerformClick();
        await WaitUntilAsync(() => Task.FromResult(handler.RequestCount > requests && !monitor.IsRefreshing && monitor.NextRefreshUtc.HasValue));
        Check(true, "Tray Refresh handler triggers asynchronous provider update");
        requests = handler.RequestCount;
        context.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Suspend));
        await Task.Delay(300);
        Check(handler.RequestCount == requests, "Suspend does not request a provider update");
        context.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));
        await WaitUntilAsync(() => Task.FromResult(handler.RequestCount > requests && !monitor.IsRefreshing && monitor.NextRefreshUtc.HasValue));
        Check(true, "Resume from sleep triggers an immediate provider update");
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(context.CurrentAppearance?.Attention == TrayAttention.Green, "Healthy FULL shows green F");
        Icon? unchangedIcon = context.TrayIcon.Icon;
        context.RefreshStatus(true);
        Check(ReferenceEquals(unchangedIcon, context.TrayIcon.Icon), "Countdown ticks do not recreate unchanged tray icon");
        var notices = new List<(ProviderKind, Recommendation)>();
        context.ProviderNotificationRequested += (provider, recommendation) => notices.Add((provider, recommendation));
        handler.OpenAiStatus = OfficialStatus.Degraded;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.SequenceEqual(new[] { (ProviderKind.OpenAI, Recommendation.Hold) }), "GO -> HOLD notification reaches tray handler");
        Check(context.CurrentAppearance == new TrayAppearance(AgentState.FullThrottle, TrayAttention.Orange) &&
            context.TrayIcon.Text.Contains("OpenAI HOLD") && context.TrayIcon.Text.Contains("Claude GO") &&
            context.TrayIcon.Text.Contains("Gemini GO"), "Degraded provider makes orange F and independent tooltip recommendations");
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Count == 1, "Repeated official status does not repeat notification");
        handler.OpenAiStatus = OfficialStatus.PartialOutage;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Last() == (ProviderKind.OpenAI, Recommendation.Stop), "HOLD -> STOP notification");
        Check(context.CurrentAppearance?.Attention == TrayAttention.Red, "Partial outage makes red F");
        context.ShowWindow();
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "provider-outage.png", reportDirectory);
        Check(Descendants(context.StatusWindow).OfType<Label>().Any(l => l.Text == "Claude   GO") &&
            Descendants(context.StatusWindow).OfType<Label>().Any(l => l.Text == "Gemini   GO"), "OpenAI STOP leaves Claude and Gemini GO in UI");
        handler.OpenAiStatus = OfficialStatus.Degraded;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        handler.OpenAiStatus = OfficialStatus.Operational;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        handler.OpenAiStatus = OfficialStatus.MajorOutage;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        handler.OpenAiStatus = OfficialStatus.Operational;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Select(n => n.Item2).SequenceEqual(new[] { Recommendation.Hold, Recommendation.Stop,
            Recommendation.Hold, Recommendation.Go, Recommendation.Stop, Recommendation.Go }),
            "STOP -> HOLD, HOLD -> GO, GO -> STOP, STOP -> GO notification sequence");

        handler.FailOpenAi = true;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Count == 6, "Unknown gap itself does not send an outage alert");
        Check(context.CurrentAppearance?.Attention == TrayAttention.Gray, "Unknown provider makes gray F without outage alert");
        handler.FailOpenAi = false;
        handler.OpenAiStatus = OfficialStatus.Degraded;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Count == 7 && notices.Last() == (ProviderKind.OpenAI, Recommendation.Hold), "GO -> UNKNOWN -> HOLD still notifies");
        handler.FailOpenAi = true;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        handler.FailOpenAi = false;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Count == 7, "HOLD -> UNKNOWN -> same HOLD does not duplicate alert");
        handler.OpenAiStatus = OfficialStatus.Operational;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Count == 8 && notices.Last() == (ProviderKind.OpenAI, Recommendation.Go), "Recovery after uncertainty notifies once");
        Check(context.StatusWindow.Controls.OfType<Label>().Any(l => l.Text.StartsWith("최근 조회 시도:")), "UI labels attempted time explicitly");

        // Synchronous sequence: the 1-second UI timer cannot run between these calls.
        var feedback = context.StatusWindow.FeedbackLabel;
        var feedbackSchedule = AgentSchedule.GetSnapshot(DateTimeOffset.UtcNow);
        void ShowStorage(string storageError) =>
            context.StatusWindow.UpdateProviders(monitor.Snapshot(), feedbackSchedule, false, null, storageError);
        const string storageFailure = "로컬 저장 확인 필요: synthetic";
        ShowStorage(storageFailure);
        Check(feedback.Text == storageFailure && feedback.ForeColor == Color.Firebrick, "Storage error appears in feedback");
        context.StatusWindow.SetFeedback("OpenAI · Success 저장됨");
        ShowStorage(storageFailure);
        Check(feedback.Text == "OpenAI · Success 저장됨", "Repeated storage error does not overwrite newer feedback");
        ShowStorage("");
        Check(feedback.Text == "OpenAI · Success 저장됨", "Storage recovery keeps unrelated feedback");
        ShowStorage(storageFailure);
        ShowStorage("");
        Check(!feedback.Text.Contains("synthetic") && feedback.ForeColor != Color.Firebrick, "Storage recovery clears a still-visible error");

        var rowMenus = context.StatusWindow.Controls.OfType<Panel>().Select(p => p.ContextMenuStrip!).ToArray();
        var trayRoot = context.TrayIcon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "사용 경험 기록");
        int total = 0;
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var trayProvider = trayRoot.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == provider.ToString());
            foreach (var type in Enum.GetValues<UsageEventType>())
            {
                var menu = provider == ProviderKind.OpenAI ? rowMenus[(int)provider].Items : trayProvider.DropDownItems;
                menu.OfType<ToolStripMenuItem>().Single(i => i.Text == type.ToString()).PerformClick();
                total++;
                await WaitUntilAsync(async () => (await store.ReadUsageAsync(null)).Count == total);
            }
        }
        Check(total == 12, "Provider row/tray menu handlers store all 3 providers x 4 event types");
        var events = await store.ReadUsageAsync(null);
        Check(events.All(e => e.SchedulePolicyVersion == AgentSchedule.HolidayPolicyVersion && e.HolidayAdjustmentEnabled == true &&
            !e.HolidayExtendedFullThrottle && e.EasternIsDst &&
            e.PacificIsDst && e.WeekendExtendedFullThrottle && e.OfficialStatus == OfficialStatus.Operational &&
            e.EffectiveRecommendation == Recommendation.Go), "UI-recorded events capture schedule/DST/official/recommendation metadata");

        using (var slowDialog = new MeasurementDialog(ProviderKind.Claude, UsageEventType.Slow))
            Check(slowDialog.EventType == UsageEventType.Slow, "Note dialog preselects the requested event type");

        bool dialogFilled = false;
        using (var dialogTimer = new Timer { Interval = 100 })
        {
            dialogTimer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.OfType<MeasurementDialog>().SingleOrDefault();
                if (dialog is null) return;
                dialogTimer.Stop();
                Descendants(dialog).OfType<ComboBox>().Single().SelectedItem = UsageEventType.Interrupted;
                Descendants(dialog).OfType<TextBox>().Single().Text = "검증 메모 · synthetic only";
                SaveFormImage(dialog, "measurement-dialog.png", reportDirectory);
                dialogFilled = true;
                Descendants(dialog).OfType<Button>().Single(b => b.Text == "저장").PerformClick();
            };
            dialogTimer.Start();
            rowMenus[0].Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "메모와 함께 기록…").PerformClick();
        }
        await WaitUntilAsync(async () => (await store.ReadUsageAsync(null)).Count == 13);
        events = await store.ReadUsageAsync(null);
        Check(dialogFilled && events.Any(e => e.UserNote == "검증 메모 · synthetic only" && e.EventType == UsageEventType.Interrupted),
            "Note dialog save handler stores selected event and Unicode note");
        Check((await new UsageStore(store.DatabasePath).ReadUsageAsync(null)).Count == 13, "UI input persists across database reopen");

        context.ShowStatistics();
        var statistics = Application.OpenForms.OfType<StatisticsWindow>().Single();
        await WaitUntilAsync(() => Task.FromResult(Descendants(statistics).OfType<Label>().Any(l => l.Text.StartsWith("직접 기록한 표본 n = "))));
        var period = Descendants(statistics).OfType<ComboBox>().Single();
        for (int index = 0; index < 3; index++)
        {
            period.SelectedIndex = index;
            await WaitUntilAsync(() => Task.FromResult(period.Enabled));
            Check(period.Text == new[] { "최근 7일", "최근 30일", "전체" }[index], "Statistics selected period text");
            Check(Descendants(statistics).OfType<DataGridView>().Count() == 5, $"Statistics period {index}: all five tables populated");
        }
        Check(Descendants(statistics).OfType<DataGridView>().First().Rows.Count == 3, "Statistics provider counts rendered for all providers");
        foreach (var label in Descendants(statistics).OfType<Label>())
        {
            var required = TextRenderer.MeasureText(label.Text, label.Font,
                new Size(label.Width - label.Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak);
            Check(label.Height - label.Padding.Vertical >= required.Height, "Statistics label fits current DPI: " + label.Text.Split('\n')[0]);
        }
        SaveFormImage(statistics, "statistics.png", reportDirectory);
        statistics.Close();
        Check(statistics.IsDisposed, "Statistics window closes cleanly");
        Check((await store.ReadLatestStatusesAsync()).Count == 3, "Official cache saved independently of user observations");
    }

    private static void SaveFormImage(Form form, string name, string? directory)
    {
        if (directory is null) return;
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(directory, name), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!await condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("UI test condition not reached");
            await Task.Delay(25);
        }
    }
}
