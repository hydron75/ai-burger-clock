using System.Reflection;

namespace AiBurgerClock;

// Windows-only smoke: synthetic status decisions and fake balloon transport.
internal static class ProviderBalloonChecks
{
    // Fake transports explicitly complete their native lifecycle instead of using
    // the requested ShowBalloonTip timeout as proof of a Closed event.
    internal static void CompleteNativeBalloon(NotifyIcon icon, bool clicked = false) => typeof(NotifyIcon)
        .GetMethod(clicked ? "OnBalloonTipClicked" : "OnBalloonTipClosed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(icon, null);

    internal static async Task RunAsync()
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Provider balloon queue: " + label);
            Console.WriteLine("PASS: " + label);
        }
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-provider-balloon-").FullName;
        var now = new DateTimeOffset(2026, 9, 19, 5, 0, 0, TimeSpan.Zero);
        long milliseconds = 0;
        var sent = new List<(long At, string Title, string Body, ToolTipIcon Icon, int Timeout)>();
        var events = new List<ProviderKind>();
        try
        {
            using var context = new TrayApplicationContext(true, () => now, enableServices: false,
                usageStore: new UsageStore(Path.Combine(directory, "synthetic.db")),
                notificationMilliseconds: () => milliseconds, quotaNotificationSink: _ => throw new InvalidOperationException("Unexpected quota send"),
                balloonNotificationSink: (icon, timeout) => sent.Add((milliseconds, icon.BalloonTipTitle,
                    icon.BalloonTipText, icon.BalloonTipIcon, timeout)));
            await context.Initialization;
            // Own the test clock and decisions; the product timer must not inject UNKNOWN snapshots.
            var timer = (System.Windows.Forms.Timer)typeof(TrayApplicationContext)
                .GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
            timer.Stop();
            context.ProviderNotificationRequested += (provider, _) => events.Add(provider);
            var apply = typeof(TrayApplicationContext).GetMethod("UpdateProviderDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void CloseBalloon() => CompleteNativeBalloon(context.TrayIcon);
            var ticker = new StatusTicker(WindowsProviderNames.Provider);
            var schedule = AgentSchedule.GetSnapshot(now, false);
            ProviderStatus[] States(params OfficialStatus[] statuses) => Enum.GetValues<ProviderKind>()
                .Select((provider, index) => new ProviderStatus(provider, statuses[index], now, now,
                    "Synthetic service state", Source: "synthetic")).ToArray();
            void Apply(ProviderStatus[] states) => apply.Invoke(context, [ticker.Tick(schedule, states, TickReason.Timer)]);
            var healthy = States(OfficialStatus.Operational, OfficialStatus.Operational, OfficialStatus.Operational, OfficialStatus.Operational);
            _ = ticker.Tick(schedule, healthy, TickReason.Initial);
            var outages = States(OfficialStatus.Degraded, OfficialStatus.PartialOutage, OfficialStatus.MajorOutage, OfficialStatus.Degraded);
            Apply(outages);
            Check(sent.Count == 1 && events.SequenceEqual([ProviderKind.OpenAI]),
                "Simultaneous four-Provider changes submit only one balloon instead of a same-tick burst");
            var first = TrayPresentation.ProviderNotification(ProviderKind.OpenAI, Recommendation.Hold,
                "Synthetic service state", WindowsProviderNames.Provider);
            Check(sent[0].Title == first.Title && sent[0].Body == first.Body && sent[0].Icon == ToolTipIcon.Warning && sent[0].Timeout == 6000,
                "Queued official alert keeps its original title, body, icon and native timeout argument");
            milliseconds = 6500;
            Apply(outages);
            Check(sent.Count == 1, "The timeout argument is not treated as evidence that the native balloon closed");
            CloseBalloon();
            Apply(outages);
            Check(sent.Count == 2 && events.SequenceEqual([ProviderKind.OpenAI, ProviderKind.Claude]),
                "Native balloon completion releases the next Provider in the original order");
            CompleteNativeBalloon(context.TrayIcon, clicked: true);
            milliseconds = 12999;
            Apply(outages);
            Check(sent.Count == 2, "Early completion still preserves the explicit 6.5-second minimum spacing");
            milliseconds = 13000;
            Apply(outages);
            Check(sent.Count == 3 && events[^1] == ProviderKind.Gemini, "Third Provider retains its original queue position");
            CloseBalloon();
            milliseconds = 19500;
            Apply(outages);
            Check(sent.Count == 4 && events.SequenceEqual(Enum.GetValues<ProviderKind>()) && sent[^1].Title == "xAI 작업 권고 변경",
                "All four Provider alerts including xAI are delivered in order without an unbounded queue");
            Apply(outages);
            Check(sent.Count == 4, "Unchanged snapshots never enqueue or resend consumed Provider changes");

            // A later change replaces the pending summary for that Provider; UNKNOWN drops it.
            Apply(healthy);
            milliseconds = 26000;
            Apply(healthy);
            Check(sent.Count == 4, "A pending recovery waits while the prior native balloon is still active");
            milliseconds = 49500;
            Apply(healthy);
            Check(sent.Count == 5 && sent[^1].Title == "ChatGPT 정상화" && sent[^1].Icon == ToolTipIcon.Info,
                "A missing native close event has a bounded 30-second fallback instead of blocking the queue forever");
            var newer = States(OfficialStatus.Operational, OfficialStatus.Unknown, OfficialStatus.Degraded, OfficialStatus.Unknown);
            Apply(newer);
            CloseBalloon();
            milliseconds += 6500;
            Apply(newer);
            Check(sent.Count == 6 && events[^1] == ProviderKind.Gemini && sent[^1].Icon == ToolTipIcon.Warning,
                "UNKNOWN invalidates a queued recovery and the latest Provider change replaces its older pending summary");

            // A FULL-to-BURGER boundary invalidates work recommendations before dispatch.
            Apply(healthy);
            schedule = AgentSchedule.GetSnapshot(new DateTimeOffset(2026, 9, 21, 15, 0, 0, TimeSpan.Zero), false);
            CloseBalloon();
            milliseconds += 30000;
            Apply(healthy);
            Check(sent.Count == 6, "Leaving FULL drops pending official work recommendations rather than showing stale alerts");

            schedule = AgentSchedule.GetSnapshot(now, false);
            _ = ticker.Tick(schedule, healthy, TickReason.Initial);
            milliseconds += 30000;
            Apply(outages);
            int beforeHoliday = sent.Count;
            // Services are disabled in this fixture; model the completed settings
            // load without starting any background HTTP or CLI monitors.
            typeof(TrayApplicationContext).GetField("holidaySettingsReady", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(context, true);
            await context.SetHolidayAdjustmentAsync(false);
            Check(sent.Count == beforeHoliday,
                "A holiday-policy confirmation never replaces a still-active official Provider balloon");
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { Console.WriteLine("Temporary Provider balloon fixture retained after cleanup failure"); }
        }
    }
}
