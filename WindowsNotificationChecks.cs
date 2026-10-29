using System.Xml.Linq;

namespace AiBurgerClock;

// Windows-only fixtures: XML, injected senders and temporary image/log files.
internal static class WindowsNotificationChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Windows notification: " + label);
        }
        string directory = Directory.CreateTempSubdirectory("AIBurgerClock-notifications-").FullName;
        try
        {
            const string title = "합성 <title> & \"표시\"";
            const string body = "합성 본문 & <markup>\n둘째 줄 🍔";
            var requests = new List<WindowsNotificationRequest>();
            foreach (var item in new[]
            {
                (WindowsQuotaNotificationKind.ScheduleFull, "sched-full", ToolTipIcon.Info),
                (WindowsQuotaNotificationKind.ScheduleBurger, "sched-burger", ToolTipIcon.Warning),
                (WindowsQuotaNotificationKind.HolidayOn, "holiday", ToolTipIcon.Info),
                (WindowsQuotaNotificationKind.HolidayOff, "holiday", ToolTipIcon.Info)
            })
            {
                var request = new WindowsNotificationRequest(item.Item1, WindowsNotificationProvider.None, title, body);
                Check(request.Tag == item.Item2 && request.Icon == item.Item3, "fixed schedule/holiday tag and information/warning distinction");
                Check(request.BalloonTimeout == (item.Item1 is WindowsQuotaNotificationKind.HolidayOn or WindowsQuotaNotificationKind.HolidayOff ? 4000 : 6000),
                    "fallback retains the original duration argument");
                requests.Add(request);
            }
            foreach (var item in new[]
            {
                (ProviderKind.OpenAI, WindowsNotificationProvider.OpenAI, "openai"),
                (ProviderKind.Claude, WindowsNotificationProvider.Claude, "claude"),
                (ProviderKind.Gemini, WindowsNotificationProvider.Gemini, "gemini"),
                (ProviderKind.Xai, WindowsNotificationProvider.Xai, "xai")
            })
            {
                Check(WindowsNotificationRequest.ForProvider(item.Item1) == item.Item2, "official Provider maps without changing the shared enum");
                var issue = new WindowsNotificationRequest(WindowsQuotaNotificationKind.ProviderIssue, item.Item2, title, body);
                var recovered = new WindowsNotificationRequest(WindowsQuotaNotificationKind.ProviderRecovered, item.Item2, title, body);
                Check(issue.Tag == "svc-" + item.Item3 + "-issue" && recovered.Tag == "svc-" + item.Item3 + "-recovered",
                    "issue and recovery retain independent fixed slots");
                Check(issue.Tag != recovered.Tag && issue.Icon == ToolTipIcon.Warning && recovered.Icon == ToolTipIcon.Info,
                    "normalization never replaces the issue and keeps its information classification");
                requests.AddRange([issue, recovered]);
            }
            var quota = WindowsNotificationRequest.FromQuota(new(QuotaProvider.Codex, 1, title, body,
                new(QuotaProvider.Codex, 1, title, body, ["synthetic-weekly"]), null));
            var anotherQuota = WindowsNotificationRequest.FromQuota(new(QuotaProvider.Codex, 1, title, body,
                new(QuotaProvider.Codex, 1, title, body, ["synthetic-weekly"]), null));
            Check(quota.Group == "quota" && quota.Header == "한도" && quota.Icon == ToolTipIcon.Info && quota.Tag != anotherQuota.Tag,
                "quota notifications preserve individual retention and existing image-free composition");
            requests.Add(quota);
            Check(requests.Select(request => request.Id).Distinct().Count() == requests.Count, "every dispatch has an immutable distinct completion identity");
            Check(requests.Where(request => !request.IsQuota).Select(request => request.Tag).Distinct().Count() == 11,
                "status notifications have two schedule, eight Provider and one holiday slot");

            var images = new WindowsNotificationImages(Path.Combine(directory, "images"));
            string? info = images.Get(ToolTipIcon.Info), warning = images.Get(ToolTipIcon.Warning);
            Check(info is not null && warning is not null && info != warning, "fixed information and warning assets have separate local URIs");
            Check(Directory.GetFiles(Path.Combine(directory, "images")).Length == 2, "image storage is bounded to two fixed files");
            byte[] infoBytes = File.ReadAllBytes(new Uri(info!).LocalPath), warningBytes = File.ReadAllBytes(new Uri(warning!).LocalPath);
            Check(!infoBytes.SequenceEqual(warningBytes), "information and warning pixels differ");
            using (var a = Image.FromFile(new Uri(info!).LocalPath))
            using (var b = Image.FromFile(new Uri(warning!).LocalPath))
                Check(a.Size == new Size(96, 96) && b.Size == new Size(96, 96), "assets include pixels for 200 percent scaling");
            Check(images.Get(ToolTipIcon.Info) == info && File.ReadAllBytes(new Uri(info!).LocalPath).SequenceEqual(infoBytes),
                "later dispatches and process restarts reuse the persisted asset");
            Check(new WindowsNotificationImages(Path.Combine(directory, "images")).Get(ToolTipIcon.Warning) == warning,
                "a new sender can reuse a warning image after the old sender exits");
            string blockedImages = Path.Combine(directory, "blocked-images");
            File.WriteAllText(blockedImages, "preserve");
            Check(new WindowsNotificationImages(blockedImages).Get(ToolTipIcon.Info) is null && File.ReadAllText(blockedImages) == "preserve",
                "optional image failure preserves the blocking file and allows a text-only toast");

            foreach (var request in requests)
            {
                var document = XDocument.Parse(WindowsQuotaToastSender.Content(request, request.Icon == ToolTipIcon.Warning ? warning : info));
                var header = document.Root!.Element("header")!;
                Check(header.Attribute("id")?.Value == request.Group && header.Attribute("title")?.Value == request.Header &&
                    header.Attribute("arguments")?.Value == request.Group && header.Attribute("activationType")?.Value == "foreground",
                    "status/quota headers have fixed no-action COM arguments");
                Check(document.Descendants("text").Select(value => value.Value).SequenceEqual(new[] { title, body }),
                    "title and body are preserved through XML escaping");
                Check(document.Descendants("image").Count() == (request.IsQuota ? 0 : 1), "only status messages gain the fixed appLogoOverride image");
                Check(!document.Descendants("actions").Any() && document.Root.Attribute("scenario") is null &&
                    !document.Descendants("audio").Any(), "no button, alarm or extra sound can bypass the original notification policy");
                Check(request.Tag.Length <= 64 && request.Group.Length <= 64, "fixed identity strings fit supported Windows limits");
                int acknowledged = 0;
                var callback = new WindowsQuotaToastCallback(WindowsQuotaToastIdentity.Test.Aumid, () => acknowledged++);
                Check(callback.Activate(WindowsQuotaToastIdentity.Test.Aumid, header.Attribute("arguments")!.Value, IntPtr.Zero, 0) == 0 &&
                    callback.Activate(WindowsQuotaToastIdentity.Test.Aumid, "", IntPtr.Zero, 0) == 0 && acknowledged == 2,
                    "header and body activation only acknowledge the existing callback without starting a tray app");
                int submitted = 0, balloons = 0;
                var entries = new List<WindowsQuotaNotificationDeliveryEntry>();
                var delivery = new WindowsQuotaNotificationDelivery(_ => submitted++, _ => balloons++, entries.Add);
                delivery.Send(request);
                Check(submitted == 1 && balloons == 0 && entries.Single().Kind == request.Kind && entries.Single().Provider == request.Provider,
                    "every notification kind uses the same injected submission and metadata path");
                delivery.RecordToastFailure(request, unchecked((int)0x80004005));
                Check(submitted == 1 && balloons == 0 && entries[^1].Reason == WindowsQuotaNotificationReason.AsynchronousFailure,
                    "asynchronous failure is recorded without a second submission or balloon");
                foreach (var reason in Enum.GetValues<WindowsToastBlockReason>())
                {
                    new WindowsQuotaNotificationDelivery(_ => throw new WindowsToastBlockedException(reason), _ => balloons++, entries.Add).Send(request);
                    Check(balloons == 0 && entries[^1].Result == WindowsQuotaNotificationResult.Blocked, "all kinds respect user and policy blocks");
                }
                new WindowsQuotaNotificationDelivery(_ => throw new InvalidOperationException("synthetic registration/submission failure"),
                    _ => balloons++, entries.Add).Send(request);
                Check(balloons == 1 && entries[^2].Transport == WindowsQuotaNotificationTransport.Toast &&
                    entries[^1].Transport == WindowsQuotaNotificationTransport.Balloon, "only synchronous submission failure has one fallback");
            }

            string logDirectory = Path.Combine(directory, "logs");
            Directory.CreateDirectory(logDirectory);
            string legacy = Path.Combine(logDirectory, "quota-notification-delivery-v1.jsonl");
            byte[] legacyBytes = System.Text.Encoding.UTF8.GetBytes("synthetic old log: preserve even when unreadable\n");
            File.WriteAllBytes(legacy, legacyBytes);
            var log = new WindowsQuotaNotificationDeliveryLog(logDirectory);
            foreach (var request in requests)
                new WindowsQuotaNotificationDelivery(_ => { }, _ => throw new InvalidOperationException("Unexpected fallback"), log.Write).Send(request);
            Check(File.ReadAllBytes(legacy).SequenceEqual(legacyBytes), "v1 is never read, rewritten, appended or removed");
            Check(Path.GetFileName(log.LogPath) == "notification-delivery-v2.jsonl" && File.ReadAllLines(log.LogPath).Length == requests.Count,
                "all kinds are appended only to v2");
            string json = File.ReadAllText(log.LogPath);
            Check(!json.Contains(title, StringComparison.Ordinal) && !json.Contains(body, StringComparison.Ordinal) && !json.Contains("synthetic-weekly", StringComparison.Ordinal),
                "banner text and window identifiers do not enter delivery diagnostics");
            Check(json.Contains("\"Provider\":\"xAI\"", StringComparison.Ordinal) && json.Contains("\"Provider\":\"None\"", StringComparison.Ordinal),
                "Windows diagnostics distinguish xAI and notifications without a Provider");
            Check(log.TryWrite(new(DateTimeOffset.UtcNow, WindowsQuotaNotificationKind.ProviderIssue, WindowsNotificationProvider.Xai,
                WindowsQuotaNotificationTransport.Toast, WindowsQuotaNotificationResult.Submitted, WindowsQuotaNotificationReason.None, null)),
                "v2 rows reload and validate all allowed Provider values");
            Check(WindowsQuotaToastSender.NativeSubmissionAttempts == 0, "all checks use fake senders; native toast submission attempts are zero");
        }
        finally { Directory.Delete(directory, recursive: true); }
        return count;
    }

    internal static async Task RunUiAsync()
    {
        string directory = Directory.CreateTempSubdirectory("AIBurgerClock-notification-ui-").FullName;
        try
        {
            var now = new DateTimeOffset(2026, 9, 19, 5, 0, 0, TimeSpan.Zero);
            long milliseconds = 0;
            var requests = new List<WindowsNotificationRequest>();
            int fallbackCalls = 0;
            using var context = new TrayApplicationContext(true, () => now, enableServices: false,
                usageStore: new UsageStore(Path.Combine(directory, "synthetic.db")), notificationMilliseconds: () => milliseconds,
                quotaNotificationSink: _ => throw new InvalidOperationException("Unexpected quota request"),
                balloonNotificationSink: (_, _) => fallbackCalls++, notificationSink: requests.Add);
            await context.Initialization;
            // Services are disabled in this fixture. Model completed settings load,
            // as the existing holiday fixture does, without starting any monitors.
            typeof(TrayApplicationContext).GetField("holidaySettingsReady",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, true);
            _ = context.StatusWindow.Handle;
            var timer = (System.Windows.Forms.Timer)typeof(TrayApplicationContext).GetField("timer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(context)!;
            timer.Stop();
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException("Notification timing: " + message);
                Console.WriteLine("PASS: " + message);
            }
            var ticker = new StatusTicker(WindowsProviderNames.Provider);
            var schedule = AgentSchedule.GetSnapshot(now, false);
            ProviderStatus[] States(OfficialStatus first) => Enum.GetValues<ProviderKind>()
                .Select(provider => new ProviderStatus(provider, provider == ProviderKind.OpenAI ? first : OfficialStatus.Operational,
                    now, now, "Synthetic service state", Source: "synthetic")).ToArray();
            var apply = typeof(TrayApplicationContext).GetMethod("UpdateProviderDisplay",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            void Apply(ProviderStatus[] states) => apply.Invoke(context, [ticker.Tick(schedule, states, TickReason.Timer)]);
            async Task CompleteOnWorker(WindowsNotificationRequest request)
            {
                await Task.Run(() => context.CompleteNotification(request));
                var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.StatusWindow.BeginInvoke((Action)(() => delivered.SetResult()));
                await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            _ = ticker.Tick(schedule, States(OfficialStatus.Operational), TickReason.Initial);
            await context.SetHolidayAdjustmentAsync(false);
            Check(requests.Count == 1 && requests[0].Kind == WindowsQuotaNotificationKind.HolidayOff && requests[0].BalloonTimeout == 4000,
                "Holiday OFF submits its unchanged information request through the toast sink");
            milliseconds = 4499;
            Apply(States(OfficialStatus.Degraded));
            Check(requests.Count == 1, "Holiday setting retains the 4.5-second defer boundary");
            milliseconds = 4500;
            Apply(States(OfficialStatus.Degraded));
            Check(requests.Count == 2 && requests[^1].Kind == WindowsQuotaNotificationKind.ProviderIssue,
                "Official issue dispatch is eligible at exactly 4.5 seconds after the holiday notice");
            var first = requests[^1];
            Apply(States(OfficialStatus.Operational));
            milliseconds = 34499;
            Apply(States(OfficialStatus.Operational));
            Check(requests.Count == 2, "An uncompleted banner still holds the Provider queue until the 30-second safety boundary");
            milliseconds = 34500;
            Apply(States(OfficialStatus.Operational));
            Check(requests.Count == 3 && requests[^1].Kind == WindowsQuotaNotificationKind.ProviderRecovered,
                "The 30-second safety boundary releases the pending recovery without extending the timer");
            var second = requests[^1];
            await CompleteOnWorker(first);
            Apply(States(OfficialStatus.Degraded));
            milliseconds = 41000;
            Apply(States(OfficialStatus.Degraded));
            Check(requests.Count == 3, "A late worker completion for the old submission cannot release the newer banner");
            await CompleteOnWorker(second);
            Apply(States(OfficialStatus.Degraded));
            Check(requests.Count == 4 && requests[^1].Kind == WindowsQuotaNotificationKind.ProviderIssue,
                "Completion for the active submission releases the next request after the unchanged 6.5-second minimum");
            context.CompleteNotification(requests[^1]);
            Apply(States(OfficialStatus.Operational));
            milliseconds = 47499;
            Apply(States(OfficialStatus.Operational));
            Check(requests.Count == 4, "Early banner completion cannot shorten the 6.5-second spacing");
            milliseconds = 47500;
            Apply(States(OfficialStatus.Operational));
            Check(requests.Count == 5, "The next pending request submits at exactly 6.5 seconds after an early completion");
            Check(fallbackCalls == 0, "Normal status dispatch makes zero balloon calls");
            var fallback = typeof(TrayApplicationContext).GetMethod("ShowFallbackNotification",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            fallback.Invoke(context, [first]);
            ProviderBalloonChecks.CompleteNativeBalloon(context.TrayIcon);
            Apply(States(OfficialStatus.Degraded));
            milliseconds = 54000;
            Apply(States(OfficialStatus.Degraded));
            Check(requests.Count == 5, "A late native fallback completion cannot release a newer toast's banner wait");
            fallback.Invoke(context, [first]);
            fallback.Invoke(context, [requests[^1]]);
            ProviderBalloonChecks.CompleteNativeBalloon(context.TrayIcon);
            Apply(States(OfficialStatus.Degraded));
            Check(requests.Count == 5 && fallbackCalls == 3, "Overlapping fallback events without a submission ID are ignored instead of releasing the wrong request");
            milliseconds = 77500;
            Apply(States(OfficialStatus.Degraded));
            Check(requests.Count == 6, "An ambiguous fallback completion still respects the unchanged 30-second safety upper bound");
            Check(WindowsQuotaToastSender.NativeSubmissionAttempts == 0, "UI notification fixtures make zero native toast API attempts");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
