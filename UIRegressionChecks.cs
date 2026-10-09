using Timer = System.Windows.Forms.Timer;
using Microsoft.Win32;

namespace AiBurgerClock;

// Runs inside the explicit smoke test's real WinForms message loop against an isolated DB.
internal static class UIRegressionChecks
{
    public static async Task RunAsync(TrayApplicationContext context, UsageStore store,
        TestStatusHttpHandler handler, string? reportDirectory, List<Uri> openedPages,
        DateTimeOffset initialNow, Action<DateTimeOffset> setNow)
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("UI integration: " + message);
            Console.WriteLine("PASS: " + message);
        }
        var monitor = context.ProviderMonitor ?? throw new InvalidOperationException("Test monitor missing");
        var mouseClick = typeof(Control).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var panels = context.StatusWindow.Controls.OfType<Panel>().ToArray();
        Check(panels[0].Controls.OfType<Label>().Any(l => l.Text.StartsWith("ChatGPT   ", StringComparison.Ordinal)) &&
            panels[0].AccessibleDescription?.Contains("ChatGPT 공식 상태 페이지", StringComparison.Ordinal) == true,
            "Windows official status heading and accessible link display ChatGPT");
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
        // The popup may auto-hide while this async test yields. Reopen it through
        // the normal tray path before PerformClick, which requires a selectable button.
        context.StatusWindow.Hide();
        context.ShowWindow();
        var refreshButton = context.StatusWindow.Controls.OfType<Button>().Single(b => b.Text == "Refresh");
        Check(context.StatusWindow.Visible && refreshButton.Enabled && refreshButton.CanSelect,
            "Reopening the status window makes Refresh actionable");
        int requests = handler.RequestCount;
        refreshButton.PerformClick();
        await WaitUntilAsync(() => Task.FromResult(handler.RequestCount > requests && !monitor.IsRefreshing && monitor.NextRefreshUtc.HasValue));
        Check(true, "Main Refresh button triggers asynchronous provider update");
        requests = handler.RequestCount;
        context.TrayIcon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "상태·한도 새로 고침").PerformClick();
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
        Check(context.StatusWindow.Controls.OfType<Label>().Single(l => l.Text == "●  FULL THROTTLE").ForeColor == Color.FromArgb(25, 145, 78) &&
            panels.Take(3).All(p => p.Controls.OfType<Label>().Single(l => l.Font.Bold).ForeColor == Color.FromArgb(25, 145, 78)),
            "Shared Good tones retain the Windows schedule and Provider green palette");
        Icon? unchangedIcon = context.TrayIcon.Icon;
        context.RefreshStatus(true);
        Check(ReferenceEquals(unchangedIcon, context.TrayIcon.Icon), "Countdown ticks do not recreate unchanged tray icon");
        var notices = new List<(ProviderKind, Recommendation)>();
        var providerApplyOrder = new List<bool>();
        context.ProviderNotificationRequested += (provider, recommendation) => notices.Add((provider, recommendation));
        context.ProviderNotificationRequested += (provider, recommendation) =>
        {
            var status = monitor.Snapshot().Single(item => item.Provider == provider);
            var expected = TrayPresentation.ProviderNotification(provider, recommendation, status.Reason, WindowsProviderNames.Provider);
            string displayed = WindowsProviderNames.Provider(provider);
            string name = recommendation.ToString().ToUpperInvariant();
            providerApplyOrder.Add(context.CurrentAppearance == TrayPresentation.Calculate(AgentState.FullThrottle, monitor.Snapshot()) &&
                context.TrayIcon.Text.Contains(displayed + " " + name, StringComparison.Ordinal) &&
                context.TrayIcon.BalloonTipTitle == expected.Title && context.TrayIcon.BalloonTipText == expected.Body &&
                context.TrayIcon.BalloonTipIcon == (recommendation == Recommendation.Go ? ToolTipIcon.Info : ToolTipIcon.Warning) &&
                panels[(int)provider].Controls.OfType<Label>().Single(label => label.Font.Bold).Text != displayed + "   " + name);
        };
        handler.OpenAiStatus = OfficialStatus.Degraded;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.SequenceEqual(new[] { (ProviderKind.OpenAI, Recommendation.Hold) }), "GO -> HOLD notification reaches tray handler");
        Check(providerApplyOrder[0], "Provider warning event follows icon/tooltip and balloon, before provider UI");
        Check(panels[0].Controls.OfType<Label>().Any(l => l.Text == "ChatGPT   HOLD" && l.ForeColor == Color.FromArgb(160, 99, 20)),
            "Shared Caution tone retains the Windows Provider HOLD color");
        Check(context.CurrentAppearance == new TrayAppearance(AgentState.FullThrottle, TrayAttention.Orange) &&
            context.TrayIcon.Text.Contains("ChatGPT HOLD") && context.TrayIcon.Text.Contains("Claude GO") &&
            context.TrayIcon.Text.Contains("Gemini GO"), "Degraded provider makes orange F and independent tooltip recommendations");
        Check(context.TrayIcon.BalloonTipTitle == "ChatGPT 작업 권고 변경" &&
            context.TrayIcon.BalloonTipText == TrayPresentation.ProviderNotification(ProviderKind.OpenAI, Recommendation.Hold,
                monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI).Reason).Body,
            "Native Provider warning uses ChatGPT with unchanged body wording");
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Count == 1, "Repeated official status does not repeat notification");
        handler.OpenAiStatus = OfficialStatus.PartialOutage;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(notices.Last() == (ProviderKind.OpenAI, Recommendation.Stop), "HOLD -> STOP notification");
        Check(context.CurrentAppearance?.Attention == TrayAttention.Red, "Partial outage makes red F");
        Check(panels[0].Controls.OfType<Label>().Any(l => l.Text == "ChatGPT   STOP" && l.ForeColor == Color.FromArgb(195, 50, 45)),
            "Shared Danger tone retains the Windows Provider STOP color");
        context.ShowWindow();
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "provider-outage.png", reportDirectory);
        Check(Descendants(context.StatusWindow).OfType<Label>().Any(l => l.Text == "Claude   GO") &&
            Descendants(context.StatusWindow).OfType<Label>().Any(l => l.Text == "Gemini   GO"), "ChatGPT STOP leaves Claude and Gemini GO in UI");
        var deactivate = typeof(Form).GetMethod("OnDeactivate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        context.StatusWindow.WithoutAutoHide(() => deactivate.Invoke(context.StatusWindow, [EventArgs.Empty]));
        Check(context.StatusWindow.Visible, "Owned warning message does not auto-hide the status window");
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
        Check(panels[0].Controls.OfType<Label>().Any(l => l.Text == "ChatGPT   CHECK" && l.ForeColor == Color.DimGray),
            "Shared Muted tone retains the Windows Provider CHECK color");
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
        Check(providerApplyOrder.Count == 8 && providerApplyOrder.All(value => value),
            "All Provider warning/recovery events preserve native application order");
        Check(context.TrayIcon.BalloonTipTitle == "ChatGPT 정상화", "Native Provider recovery title displays ChatGPT");
        Check(context.StatusWindow.Controls.OfType<Label>().Any(l => l.Text.StartsWith("최근 조회 시도:")), "UI labels attempted time explicitly");

        // Synchronous sequence: the 1-second UI timer cannot run between these calls.
        var feedback = context.StatusWindow.FeedbackLabel;
        var feedbackSchedule = AgentSchedule.GetSnapshot(DateTimeOffset.UtcNow);
        void ShowStorage(string storageError) =>
            context.StatusWindow.UpdateProviders(monitor.Snapshot(), feedbackSchedule, false, null, storageError);
        const string storageFailure = "로컬 저장 확인 필요: synthetic";
        ShowStorage(storageFailure);
        Check(feedback.Text == storageFailure && feedback.ForeColor == Color.Firebrick, "Storage error appears in feedback");
        context.StatusWindow.SetFeedback("ChatGPT · Success 저장됨");
        ShowStorage(storageFailure);
        Check(feedback.Text == "ChatGPT · Success 저장됨", "Repeated storage error does not overwrite newer feedback");
        ShowStorage("");
        Check(feedback.Text == "ChatGPT · Success 저장됨", "Storage recovery keeps unrelated feedback");
        ShowStorage(storageFailure);
        ShowStorage("");
        Check(!feedback.Text.Contains("synthetic") && feedback.ForeColor != Color.Firebrick, "Storage recovery clears a still-visible error");

        var rowMenus = context.StatusWindow.Controls.OfType<Panel>().Select(p => p.ContextMenuStrip!).ToArray();
        var trayRoot = context.TrayIcon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "사용 경험 기록");
        void CheckRecordingMenu(ToolStripItemCollection items, string surface)
        {
            Check(items.Count == 6, surface + " has four events, a separator and the note item");
            string[] eventNames = ["Success", "Slow", "Error", "Interrupted"];
            for (int index = 0; index < eventNames.Length; index++)
                Check(items[index] is ToolStripMenuItem item && item.Text == eventNames[index],
                    surface + " event order: " + eventNames[index]);
            Check(items[4] is ToolStripSeparator && items[5] is ToolStripMenuItem noteItem &&
                noteItem.Text == "메모와 함께 기록…", surface + " ends with the separator and note item");
        }
        int total = 0;
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var trayProvider = trayRoot.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == WindowsProviderNames.Provider(provider));
            CheckRecordingMenu(rowMenus[(int)provider].Items, provider + " row menu");
            CheckRecordingMenu(trayProvider.DropDownItems, provider + " tray menu");
            foreach (var type in Enum.GetValues<UsageEventType>())
            {
                var menu = provider == ProviderKind.OpenAI ? rowMenus[(int)provider].Items : trayProvider.DropDownItems;
                menu.OfType<ToolStripMenuItem>().Single(i => i.Text == type.ToString()).PerformClick();
                total++;
                string expectedFeedback = $"{WindowsProviderNames.Provider(provider)} · {type} 저장됨 (12:00 KST)";
                await WaitUntilAsync(async () => (await store.ReadUsageAsync(null)).Count == total && feedback.Text == expectedFeedback);
                Check(feedback.ForeColor == Color.DimGray, provider + " " + type + " save uses the exact success feedback");
            }
        }
        Check(total == 12, "Provider row/tray menu handlers store all 3 providers x 4 event types");
        Check(trayRoot.DropDownItems.OfType<ToolStripMenuItem>().Select(i => i.Text).SequenceEqual(new[] { "ChatGPT", "Claude", "Gemini" }),
            "Windows recording menu shows ChatGPT without changing provider order");
        var events = await store.ReadUsageAsync(null);
        Check(events.All(e => e.SchedulePolicyVersion == AgentSchedule.HolidayPolicyVersion && e.HolidayAdjustmentEnabled == true &&
            !e.HolidayExtendedFullThrottle && e.EasternIsDst &&
            e.PacificIsDst && e.WeekendExtendedFullThrottle && e.OfficialStatus == OfficialStatus.Operational &&
            e.EffectiveRecommendation == Recommendation.Go), "UI-recorded events capture schedule/DST/official/recommendation metadata");

        using (var slowDialog = new MeasurementDialog(ProviderKind.Claude, UsageEventType.Slow))
        {
            Check(slowDialog.EventType == UsageEventType.Slow, "Note dialog preselects the requested event type");
            var noteBox = Descendants(slowDialog).OfType<TextBox>().Single();
            _ = noteBox.Handle;
            Check(noteBox.IsHandleCreated && noteBox.MaxLength == 0, "Native note box preserves input for the shared save-time limit");
            var captured = UsageMeasurementFactory.Capture(ProviderKind.Claude, UsageEventType.Slow,
                AgentSchedule.GetSnapshot(initialNow, context.HolidayAdjustmentEnabled),
                monitor.Snapshot().Single(s => s.Provider == ProviderKind.Claude), "2.2.3");
            (string Input, string Expected, string Label)[] noteCases =
            [
                (new string('a', 1005), new string('a', 1000), "over-limit note"),
                ("  " + new string('b', 1000) + "  ", new string('b', 1000), "whitespace trimmed before the limit"),
                (new string('c', 999) + "🍔", new string('c', 999), "emoji remains whole at the limit"),
                (new string('d', 999) + "e\u0301", new string('d', 999), "combining sequence remains whole at the limit"),
                (string.Concat(Enumerable.Repeat("🇰🇷", 251)), string.Concat(Enumerable.Repeat("🇰🇷", 250)), "flag sequences remain whole at the limit"),
                (new string('e', 999) + " f", new string('e', 999) + " ", "normalization is applied only once")
            ];
            foreach (var noteCase in noteCases)
            {
                noteBox.SelectAll();
                noteBox.SelectedText = noteCase.Input;
                Check(noteBox.Text == noteCase.Input, "Native note input preserves " + noteCase.Label);
                var noted = UsageMeasurementFactory.WithNote(captured, slowDialog.EventType, slowDialog.UserNote);
                Check(noted.UserNote == noteCase.Expected && noted.UserNote.Length <= 1000,
                    "Dialog input and shared save apply " + noteCase.Label);
            }
        }

        string feedbackBeforeCancel = feedback.Text;
        Color feedbackColorBeforeCancel = feedback.ForeColor;
        bool dialogCanceled = false;
        Exception? cancelError = null;
        using (var cancelTimer = new Timer { Interval = 100 })
        {
            cancelTimer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.OfType<MeasurementDialog>().SingleOrDefault();
                if (dialog is null) return;
                cancelTimer.Stop();
                try
                {
                    Descendants(dialog).OfType<ComboBox>().Single().SelectedItem = UsageEventType.Error;
                    Descendants(dialog).OfType<TextBox>().Single().SelectedText = "취소할 메모 · synthetic only";
                    Descendants(dialog).OfType<Button>().Single(b => b.Text == "취소").PerformClick();
                    dialogCanceled = true;
                }
                catch (Exception error)
                {
                    cancelError = error;
                    dialog.DialogResult = DialogResult.Cancel;
                }
            };
            cancelTimer.Start();
            rowMenus[0].Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "메모와 함께 기록…").PerformClick();
        }
        if (cancelError is not null) throw new InvalidOperationException("Cancel dialog action failed", cancelError);
        Check(dialogCanceled && (await store.ReadUsageAsync(null)).Count == 12,
            "Canceling a note dialog creates no usage record");
        Check(feedback.Text == feedbackBeforeCancel && feedback.ForeColor == feedbackColorBeforeCancel,
            "Canceling a note dialog preserves existing feedback");

        bool dialogFilled = false;
        Exception? dialogError = null;
        OfficialStatus originalStatus = handler.OpenAiStatus;
        try
        {
            using (var dialogTimer = new Timer { Interval = 100 })
            {
                dialogTimer.Tick += async (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<MeasurementDialog>().SingleOrDefault();
                    if (dialog is null) return;
                    dialogTimer.Stop();
                    try
                    {
                        Check(dialog.EventType == UsageEventType.Success, "Note menu starts the dialog with Success");
                        Descendants(dialog).OfType<ComboBox>().Single().SelectedItem = UsageEventType.Interrupted;
                        Descendants(dialog).OfType<TextBox>().Single().Text = "검증 메모 · synthetic only";
                        Check(dialog.Text == "ChatGPT · 사용 경험", "Windows note dialog title displays ChatGPT");
                        SaveFormImage(dialog, "measurement-dialog.png", reportDirectory);
                        // Keep the image and stored fixture at 12:00 FULL. An earlier same-day
                        // BURGER instant tests capture timing without advancing quota schedules.
                        setNow(new DateTimeOffset(2026, 9, 19, 9, 59, 59, TimeSpan.FromHours(9)));
                        handler.OpenAiStatus = OfficialStatus.PartialOutage;
                        await monitor.RefreshOnceAsync();
                        context.RefreshStatus(false);
                        Check(context.CurrentAppearance?.Schedule == AgentState.BurgerTime &&
                            monitor.Snapshot().Single(s => s.Provider == ProviderKind.OpenAI).Status == OfficialStatus.PartialOutage,
                            "Clock and official status change while the note dialog is open");
                        dialogFilled = true;
                        Descendants(dialog).OfType<Button>().Single(b => b.Text == "저장").PerformClick();
                    }
                    catch (Exception error)
                    {
                        dialogError = error;
                        if (!dialog.IsDisposed) dialog.DialogResult = DialogResult.Cancel;
                    }
                };
                dialogTimer.Start();
                rowMenus[0].Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "메모와 함께 기록…").PerformClick();
            }
            if (dialogError is not null) throw new InvalidOperationException("Note dialog action failed", dialogError);
            await WaitUntilAsync(async () => (await store.ReadUsageAsync(null)).Count == 13 &&
                feedback.Text == "ChatGPT · Interrupted 저장됨 (12:00 KST)");
            events = await store.ReadUsageAsync(null);
            var noted = events.Single(e => e.UserNote == "검증 메모 · synthetic only");
            Check(dialogFilled && noted.EventType == UsageEventType.Interrupted,
                "Note dialog save handler stores selected event and Unicode note");
            Check(noted.TimestampUtc == initialNow.ToUniversalTime() && noted.TimestampUtc.Offset == TimeSpan.Zero &&
                noted.ScheduleState == AgentState.FullThrottle && noted.WeekendExtendedFullThrottle &&
                noted.OfficialStatus == OfficialStatus.Operational && noted.EffectiveRecommendation == Recommendation.Go,
                "Note save retains the timestamp, schedule, official status and recommendation captured before the prompt");
            Check(events.Count(e => e.Provider == ProviderKind.OpenAI) == 5 &&
                feedback.Text == "ChatGPT · Interrupted 저장됨 (12:00 KST)" && feedback.ForeColor == Color.DimGray,
                "ChatGPT recording feedback uses the captured time and preserves stored OpenAI identities");
        }
        finally
        {
            setNow(initialNow);
            handler.OpenAiStatus = originalStatus;
            await monitor.RefreshOnceAsync();
            context.RefreshStatus(false);
        }
        Check(context.CurrentAppearance == new TrayAppearance(AgentState.FullThrottle, TrayAttention.Green) &&
            monitor.Snapshot().All(s => s.Status == OfficialStatus.Operational && s.CheckedAtUtc == initialNow.ToUniversalTime()),
            "Note capture test restores the original FULL/healthy clock and status fixture");
        Check((await new UsageStore(store.DatabasePath).ReadUsageAsync(null)).Count == 13, "UI input persists across database reopen");

        context.ShowStatistics();
        var statistics = Application.OpenForms.OfType<StatisticsWindow>().Single();
        await WaitUntilAsync(() => Task.FromResult(Descendants(statistics).OfType<Label>().Any(l => l.Text.StartsWith("직접 기록한 표본 n = "))));
        Check(Descendants(statistics).OfType<Label>().Any(l => l.Text.StartsWith("직접 기록한 표본 n = 13 ")),
            "Statistics 7-day period uses the app clock, not the real date");
        var period = Descendants(statistics).OfType<ComboBox>().Single();
        for (int index = 0; index < 3; index++)
        {
            period.SelectedIndex = index;
            await WaitUntilAsync(() => Task.FromResult(period.Enabled));
            Check(period.Text == new[] { "최근 7일", "최근 30일", "전체" }[index], "Statistics selected period text");
            Check(Descendants(statistics).OfType<DataGridView>().Count() == 5, $"Statistics period {index}: all five tables populated");
        }
        Check(Descendants(statistics).OfType<DataGridView>().First().Rows.Count == 3, "Statistics provider counts rendered for all providers");
        foreach (var grid in Descendants(statistics).OfType<DataGridView>())
            Check(grid.Rows.Cast<DataGridViewRow>().Any(r => r.Cells[0].Value as string == "ChatGPT") &&
                grid.Rows.Cast<DataGridViewRow>().All(r => r.Cells[0].Value as string != "OpenAI"),
                "Every Windows statistics tab displays ChatGPT while shared analysis keeps stored names");
        foreach (var label in Descendants(statistics).OfType<Label>())
        {
            var required = TextRenderer.MeasureText(label.Text, label.Font,
                new Size(label.Width - label.Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak);
            Check(label.Height - label.Padding.Vertical >= required.Height, "Statistics label fits current DPI: " + label.Text.Split('\n')[0]);
        }
        SaveFormImage(statistics, "statistics.png", reportDirectory);
        statistics.Close();
        Check(statistics.IsDisposed, "Statistics window closes cleanly");

        // Render the original 13-record statistics fixture first. Then exercise the
        // actual save handler's note limits and feedback without changing the PNG fixture.
        var savedNoteCases = new[]
        {
            (Label: "over 1,000 units", Input: new string('a', 1005), Saved: new string('a', 1000), Truncated: true),
            (Label: "padded over-limit note", Input: "  " + new string('c', 1005) + "  ", Saved: new string('c', 1000), Truncated: true),
            (Label: "padding alone exceeds the limit", Input: "  " + new string('d', 1000) + "  ", Saved: new string('d', 1000), Truncated: false),
            (Label: "trim then limit once", Input: new string('e', 999) + " f", Saved: new string('e', 999) + " ", Truncated: true),
            (Label: "complete emoji at the boundary", Input: new string('f', 999) + "🍔", Saved: new string('f', 999), Truncated: true),
            // End with a normal save so the later holiday PNG keeps its original feedback fixture.
            (Label: "exactly 1,000 units", Input: new string('b', 1000), Saved: new string('b', 1000), Truncated: false)
        };
        int savedNoteCount = 13;
        foreach (var noteCase in savedNoteCases)
        {
            bool combinedQuotaCase = noteCase.Label == "over 1,000 units";
            if (combinedQuotaCase)
            {
                context.ShowWindow();
                context.StatusWindow.Controls.OfType<Button>().Single(b => b.Text == QuotaPanelModel.ShowQuotas).PerformClick();
            }
            Exception? noteError = null;
            bool noteFilled = false;
            using (var noteTimer = new Timer { Interval = 100 })
            {
                noteTimer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<MeasurementDialog>().SingleOrDefault();
                    if (dialog is null) return;
                    noteTimer.Stop();
                    try
                    {
                        Descendants(dialog).OfType<ComboBox>().Single().SelectedItem = UsageEventType.Interrupted;
                        var noteBox = Descendants(dialog).OfType<TextBox>().Single();
                        noteBox.SelectedText = noteCase.Input;
                        Check(noteBox.Text == noteCase.Input, "Note dialog preserves full input: " + noteCase.Label);
                        noteFilled = true;
                        Descendants(dialog).OfType<Button>().Single(b => b.Text == "저장").PerformClick();
                    }
                    catch (Exception error)
                    {
                        noteError = error;
                        if (!dialog.IsDisposed) dialog.DialogResult = DialogResult.Cancel;
                    }
                };
                noteTimer.Start();
                var noteMenu = combinedQuotaCase
                    ? trayRoot.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "ChatGPT").DropDownItems
                    : rowMenus[0].Items;
                noteMenu.OfType<ToolStripMenuItem>().Single(i => i.Text == "메모와 함께 기록…").PerformClick();
            }
            if (noteError is not null) throw new InvalidOperationException("Note limit dialog action failed: " + noteCase.Label, noteError);
            string expectedFeedback = noteCase.Truncated
                ? FeedbackText.NoteTruncated(noteCase.Saved.Length)
                : "ChatGPT · Interrupted 저장됨 (12:00 KST)";
            savedNoteCount++;
            await WaitUntilAsync(async () => (await store.ReadUsageAsync(null)).Count == savedNoteCount &&
                feedback.Text == expectedFeedback);
            Check(noteFilled && (await new UsageStore(store.DatabasePath).ReadUsageAsync(null)).Any(e =>
                e.EventType == UsageEventType.Interrupted && e.UserNote == noteCase.Saved),
                "Actual note save persists the expected complete note: " + noteCase.Label);
            Check(feedback.Text == expectedFeedback && feedback.ForeColor == Color.DimGray,
                "Actual note save uses the expected non-error feedback: " + noteCase.Label);
            if (combinedQuotaCase)
            {
                context.ShowWindow();
                var quotaView = context.StatusWindow.Controls.OfType<AccountQuotaView>().Single();
                Check(quotaView.Visible && quotaView.Controls.OfType<Panel>().Select(p => p.Name)
                    .SequenceEqual(new[] { "CodexQuotaBox", "ClaudeQuotaBox", "GeminiQuotaBox" }) && panels.Take(3).All(p => !p.Visible),
                    "Combined quota/note save keeps all three Provider boxes visible and status cards hidden");
                Check(feedback.Text == expectedFeedback && feedback.ForeColor == Color.DimGray,
                    "Combined quota/note save shows the truncation feedback in quota mode");
                SaveFormImage(context.StatusWindow, "account-quotas-note-truncated.png", reportDirectory);
                context.StatusWindow.Controls.OfType<Button>().Single(b => b.Text == QuotaPanelModel.ShowStatus).PerformClick();
                Check(!quotaView.Visible && panels.Take(3).All(p => p.Visible) && feedback.Text == expectedFeedback,
                    "Combined quota/note save returns to status mode without clearing truncation feedback");
                SaveFormImage(context.StatusWindow, "note-truncated.png", reportDirectory);
            }
        }
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
