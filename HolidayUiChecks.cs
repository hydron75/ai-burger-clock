namespace AiBurgerClock;

// Only the explicit smoke-test entry point invokes these synthetic UI actions.
internal static class HolidayUiChecks
{
    internal static async Task RunAsync(TrayApplicationContext context, UsageStore store,
        TestStatusHttpHandler handler, Action<DateTimeOffset> setNow, string? reportDirectory)
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Holiday UI: " + message);
            Console.WriteLine("PASS: " + message);
        }
        static DateTimeOffset Kst(int month, int day, int hour, int minute = 0, int second = 0) =>
            new(2026, month, day, hour, minute, second, TimeSpan.FromHours(9));
        await context.Initialization;
        Check(context.HolidayAdjustmentEnabled && context.HolidayMenuItem.Checked &&
            context.StatusWindow.HolidayCheckBox.Checked, "Holiday adjustment defaults ON on both UI surfaces");

        var transitions = new List<AgentState>();
        context.TransitionNotificationRequested += transitions.Add;
        setNow(Kst(5, 23, 9, 59, 59));
        context.RefreshStatus(false);
        setNow(Kst(5, 23, 10));
        context.RefreshStatus(true);
        Check(context.TrayIcon.Text.Contains("84:00:00") && context.TrayIcon.Text.Contains("공휴일"),
            "Memorial Day extends FULL countdown to Tuesday 22:00 KST (84 hours)");
        Check(transitions.SequenceEqual(new[] { AgentState.FullThrottle }) && context.TrayIcon.BalloonTipText.Contains("공휴일"),
            "Holiday extended FULL emits one schedule notification with holiday reason");
        context.ShowWindow();
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "holiday-weekend.png", reportDirectory);
        setNow(Kst(5, 25, 22));
        context.RefreshStatus(true);
        Check(transitions.Count == 1 && context.CurrentAppearance?.Schedule == AgentState.FullThrottle,
            "Skipped Monday holiday boundary produces no false BURGER notification");

        int before = (await store.ReadUsageAsync(null)).Count;
        var recording = context.TrayIcon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "사용 경험 기록")
            .DropDownItems.OfType<ToolStripMenuItem>().First().DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Success");
        recording.PerformClick();
        await WaitUntilAsync(async () => (await store.ReadUsageAsync(null)).Count == before + 1);
        var holidayEvent = (await store.ReadUsageAsync(null)).Single(e => e.TimestampUtc == Kst(5, 25, 22));
        Check(holidayEvent.HolidayAdjustmentEnabled == true && holidayEvent.HolidayExtendedFullThrottle &&
            holidayEvent.WeekendExtendedFullThrottle && holidayEvent.HolidayNames.Contains("Memorial") &&
            holidayEvent.SchedulePolicyVersion == AgentSchedule.HolidayPolicyVersion,
            "Actual recording handler captures holiday/weekend/policy metadata independently");
        setNow(Kst(5, 26, 22));
        context.RefreshStatus(true);
        context.RefreshStatus(true);
        Check(transitions.SequenceEqual(new[] { AgentState.FullThrottle, AgentState.BurgerTime }),
            "Extended interval ends Tuesday with exactly one BURGER notification");

        setNow(Kst(11, 26, 23, 5));
        context.RefreshStatus(false);
        var schedule = AgentSchedule.GetSnapshot(Kst(11, 26, 23, 5), true);
        Check(schedule.IsHolidayExtendedFullThrottle && !schedule.IsWeekendExtendedFullThrottle,
            "Midweek Thanksgiving FULL does not count as weekend");
        context.ShowWindow();
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "holiday-midweek.png", reportDirectory);
        var monitor = context.ProviderMonitor!;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(false);
        int requests = handler.RequestCount;
        int noticeCount = transitions.Count;
        context.StatusWindow.HolidayCheckBox.Checked = false;
        await WaitUntilAsync(() => Task.FromResult(context.StatusWindow.HolidayCheckBox.Enabled && !context.HolidayAdjustmentEnabled));
        Check(!context.HolidayMenuItem.Checked && !await store.GetHolidayAdjustmentAsync() &&
            context.CurrentAppearance?.Schedule == AgentState.BurgerTime,
            "Main checkbox disables holiday policy, persists OFF, synchronizes tray and recalculates BURGER");
        Check(transitions.Count == noticeCount && handler.RequestCount == requests,
            "Policy toggle is not a fake time transition and does not add status polling");
        Check(context.TrayIcon.BalloonTipTitle == "공휴일 보정 꺼짐", "Policy toggle uses its own notification");
        Check(context.StatusWindow.FeedbackLabel.Text == "공휴일 보정 OFF · 시간표에 반영됨" &&
            context.StatusWindow.FeedbackLabel.ForeColor == Color.DimGray,
            "Holiday OFF success feedback preserves the Windows wording and color");
        Check(context.TrayIcon.BalloonTipText == "시간표 정책이 변경되었습니다. 다음 전환: 11-27 11:00 KST.\nProvider 공식 상태는 별도로 확인하세요.",
            "Holiday OFF native notification preserves the Windows body");
        before = (await store.ReadUsageAsync(null)).Count;
        recording.PerformClick();
        await WaitUntilAsync(async () => (await store.ReadUsageAsync(null)).Count == before + 1);
        var offEvent = (await store.ReadUsageAsync(null)).Single(e => e.TimestampUtc == Kst(11, 26, 23, 5));
        Check(offEvent.HolidayAdjustmentEnabled == false && !offEvent.HolidayExtendedFullThrottle &&
            offEvent.SchedulePolicyVersion == AgentSchedule.PolicyVersion && offEvent.ScheduleState == AgentState.BurgerTime,
            "OFF event preserves the old policy without modifying prior ON record");

        context.HolidayMenuItem.PerformClick();
        await WaitUntilAsync(() => Task.FromResult(context.HolidayMenuItem.Enabled && context.HolidayAdjustmentEnabled));
        Check(context.StatusWindow.HolidayCheckBox.Checked && await new UsageStore(store.DatabasePath).GetHolidayAdjustmentAsync(),
            "Tray toggle re-enables policy and survives store restart");
        Check(context.StatusWindow.FeedbackLabel.Text == "공휴일 보정 ON · 시간표에 반영됨" &&
            context.StatusWindow.FeedbackLabel.ForeColor == Color.DimGray,
            "Holiday ON success feedback preserves the Windows wording and color");
        Check(context.TrayIcon.BalloonTipTitle == "공휴일 보정 켜짐" &&
            context.TrayIcon.BalloonTipText == "시간표 정책이 변경되었습니다. 다음 전환: 11-27 23:00 KST.\nProvider 공식 상태는 별도로 확인하세요.",
            "Holiday ON native notification preserves the Windows title and body");
        handler.OpenAiStatus = OfficialStatus.PartialOutage;
        await monitor.RefreshOnceAsync();
        context.RefreshStatus(true);
        Check(context.CurrentAppearance == new TrayAppearance(AgentState.FullThrottle, TrayAttention.Red) &&
            context.TrayIcon.Text.Contains("ChatGPT STOP") && context.TrayIcon.Text.Contains("Claude GO"),
            "Holiday FULL never hides provider outage or lowers other providers");
        // Freshness still affects icon without an extra polling timer or HTTP request.
        setNow(Kst(11, 26, 23, 21));
        context.RefreshStatus(true);
        Check(context.CurrentAppearance?.Attention == TrayAttention.Gray && context.TrayIcon.Text.Contains("STALE"),
            "Clock-only stale transition updates tray to gray");
        var stableIcon = context.TrayIcon.Icon;
        for (int i = 0; i < 100; i++) context.RefreshStatus(false);
        Check(ReferenceEquals(stableIcon, context.TrayIcon.Icon), "One hundred unchanged ticks reuse the native icon");
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!await condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Holiday UI test condition not reached");
            await Task.Delay(25);
        }
    }
}
