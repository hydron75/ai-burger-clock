using System.Net.NetworkInformation;
using System.Reflection;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace AiBurgerClock;

[Register("AIBurgerClockMacApplication")]
internal sealed class MacApplication(UsageStore store, bool smoke) : NSApplicationDelegate
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly RecommendationNotifications recommendationNotifications = new();
    private readonly HashSet<Task> pendingWrites = [];
    private Task initialization = Task.CompletedTask;
    private HttpClient? http;
    private StatusMonitor? statusMonitor;
    private AccountQuotaMonitor? quotaMonitor;
    private MacStatusPanel? panel;
    private MacStatisticsWindow? statisticsWindow;
    private MacNotifications? notifications;
    private NSStatusItem? statusItem;
    private NSImage? statusImage;
    // Right-click menu only; left-click opens the status popover.
    private NSMenu? contextMenu;
    private NSTimer? timer;
    private NSObject? wakeObserver;
    private AgentState? lastSchedule;
    private TrayAppearance? lastAppearance;
    // Apply the user's holiday policy only after its stored setting is read successfully.
    private bool holidayEnabled;
    private bool holidayReady;
    private bool savingHoliday;
    // Lets a holiday-policy notification yield to a provider alert raised by the same refresh.
    private int providerNotificationSerial;
    private bool stopping;
    private bool stopped;
    private int uiRefreshQueued;
    private NetworkRefreshScheduler? networkRefresh;
    private int timerTicks;
    // Smoke: official-page requests that reached the host (the browser is not opened in a smoke run).
    private int statusPageRequests;
    internal int ExitCode { get; private set; }

    public override void DidFinishLaunching(NSNotification notification)
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        panel = new(RefreshAll, ShowStatistics, enabled => _ = ChangeHolidayAsync(enabled), ChangeAutoStart,
            OpenStatusPage, (provider, type, note) => _ = RecordAsync(provider, type, note), smoke);
        CreateContextMenu();
        statusItem = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Square);
        if (statusItem.Button is { } button)
        {
            button.ImagePosition = NSCellImagePosition.ImageOnly;
            button.ImageScaling = NSImageScale.None;
            // No statusItem.Menu: it would open on every click. Route left and right clicks here instead.
            button.SendActionOn((NSEventType)(ulong)(NSEventMask.LeftMouseUp | NSEventMask.RightMouseUp));
            button.Activated += (_, _) => StatusItemClicked();
        }
        RefreshDisplay(false);
        // One existing-style countdown timer; Schedule itself caches its date/policy calculations.
        // Common modes keep it running while the menu-bar menu is open (event-tracking mode).
        timer = NSTimer.CreateRepeatingTimer(TimeSpan.FromSeconds(1), _ =>
        {
            timerTicks++;
            RefreshDisplay(true);
        });
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
        initialization = InitializeAsync();
    }

    private void CreateContextMenu()
    {
        contextMenu = new NSMenu { AutoEnablesItems = false };
        contextMenu.AddItem(new NSMenuItem("Refresh", (_, _) => RefreshAll()));
        contextMenu.AddItem(new NSMenuItem("로그인 항목 설정 열기…", (_, _) => MacAutoStart.OpenSettings()) { Enabled = !smoke });
        contextMenu.AddItem(NSMenuItem.SeparatorItem);
        contextMenu.AddItem(new NSMenuItem("종료", "q", (_, _) => NSApplication.SharedApplication.Terminate(null)));
    }

    // Right-click and control-click open the short menu; a left click toggles the popover.
    internal static bool IsContextClick(NSEvent? click) => click is not null &&
        (click.Type is NSEventType.RightMouseUp or NSEventType.RightMouseDown ||
         click.ModifierFlags.HasFlag(NSEventModifierMask.ControlKeyMask));

    private void StatusItemClicked()
    {
        if (stopping || statusItem?.Button is not { } button || contextMenu is null) return;
        if (IsContextClick(NSApplication.SharedApplication.CurrentEvent))
        {
            panel?.Close();
            contextMenu.PopUpMenu(null, new CGPoint(0, button.IsFlipped ? button.Bounds.Height + 4 : -4), button);
        }
        else if (panel?.IsShown == true) panel.Close();
        else ShowPanel();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await store.InitializeAsync(lifetime.Token);
            holidayEnabled = await store.GetHolidayAdjustmentAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (Exception error)
        {
            // Never silently apply an unconfirmed policy; keep official-status and quota polling running.
            holidayEnabled = false;
            panel?.SetFeedback(FeedbackText.HolidayReadFailed(error.Message), error: true);
            if (smoke)
            {
                ExitCode = 1;
                Console.Error.WriteLine("FAIL: native smoke initialization: " + error.Message);
                // Queue instead of calling here: shutdown awaits this initialization task.
                BeginInvokeOnMainThread(() => NSApplication.SharedApplication.Terminate(null));
                return;
            }
        }
        try
        {
            if (stopping) return;
            holidayReady = true;
            RefreshHolidayControls();
            RefreshDisplay(false);
            if (smoke)
            {
                await RunSmokeAsync();
                return;
            }
            http = ProviderStatusClient.CreateHttpClient();
            statusMonitor = new(new ProviderStatusClient(http), store, scheduleAt: Schedule);
            quotaMonitor = new(new AccountQuotaClient(), store);
            statusMonitor.Changed += QueueDisplayRefresh;
            quotaMonitor.Changed += QueueDisplayRefresh;
            // System notification permission is optional and must not stop status/quota polling.
            try
            {
                notifications = new(text => panel?.SetFeedback(text));
                notifications.RequestPermission();
            }
            catch (Exception) { panel?.SetFeedback("알림 초기화 실패 · macOS 알림 설정을 확인하세요.", error: true); }
            RefreshAutoStart();
            wakeObserver = NSWorkspace.SharedWorkspace.NotificationCenter.AddObserver(NSWorkspace.DidWakeNotification,
                _ => RefreshAll(queueWhileRefreshing: true));
            networkRefresh = new NetworkRefreshScheduler(() => RefreshAll(queueWhileRefreshing: true));
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
            statusMonitor.Start();
            quotaMonitor.Start();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            panel?.SetFeedback("초기화 실패: " + error.Message, error: true);
            if (smoke)
            {
                ExitCode = 1;
                Console.Error.WriteLine("FAIL: native smoke initialization: " + error.Message);
                BeginInvokeOnMainThread(() => NSApplication.SharedApplication.Terminate(null));
            }
        }
    }

    internal static string AppVersion { get; } = typeof(MacApplication).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

    private ScheduleSnapshot Schedule(DateTimeOffset at) => AgentSchedule.GetSnapshot(at, holidayEnabled);

    private IReadOnlyList<ProviderStatus> ProviderStates() => statusMonitor?.Snapshot() ??
        Enum.GetValues<ProviderKind>().Select(provider => ProviderStatus.Unknown(provider)).ToArray();

    private void QueueDisplayRefresh()
    {
        if (stopping || Interlocked.Exchange(ref uiRefreshQueued, 1) == 1) return;
        BeginInvokeOnMainThread(() =>
        {
            Volatile.Write(ref uiRefreshQueued, 0);
            if (!stopping) RefreshDisplay(true);
        });
    }

    private void RefreshDisplay(bool notify, bool notifyProviders = false)
    {
        if (stopping) return;
        ScheduleSnapshot snapshot = Schedule(DateTimeOffset.UtcNow);
        var providers = ProviderStates();
        bool changed = lastSchedule.HasValue && lastSchedule != snapshot.State;
        if (notify && changed)
        {
            var (title, body) = TrayPresentation.TransitionNotification(snapshot);
            notifications?.Show(title, body);
        }
        lastSchedule = snapshot.State;
        // A policy-only refresh must not consume a concurrently arrived provider incident.
        foreach (ProviderStatus status in providers)
        {
            var recommendation = RecommendationPolicy.Calculate(snapshot.State, status.Status);
            if (recommendationNotifications.Observe(status.Provider, snapshot.State, recommendation, (notify || notifyProviders) && !changed))
            {
                var (title, body) = TrayPresentation.ProviderNotification(status.Provider, recommendation, status.Reason,
                    ProviderNames.Provider);
                providerNotificationSerial++;
                notifications?.Show(title, body);
            }
        }
        TrayAppearance appearance = TrayPresentation.Calculate(snapshot.State, providers);
        if (statusItem?.Button is { } button)
        {
            if (lastAppearance != appearance)
            {
                NSImage image = MacStatusIcon.Create(appearance);
                NSImage? previous = statusImage;
                button.Image = image;
                button.Title = "";
                statusImage = image;
                previous?.Dispose();
                lastAppearance = appearance;
            }
            // Minute precision: a per-second change would close the tooltip while it is being read.
            string tooltip = TrayPresentation.Tooltip(snapshot, providers, ProviderNames.Provider, minutePrecision: true);
            if (button.ToolTip != tooltip) button.ToolTip = tooltip;
        }
        // The popover is redrawn only while it is open; ShowPanel fills it before showing.
        if (panel?.IsShown == true) UpdatePanel(snapshot, providers);
    }

    private void UpdatePanel(ScheduleSnapshot snapshot, IReadOnlyList<ProviderStatus> providers) =>
        panel?.Update(snapshot, providers, QuotaStates(), statusMonitor?.IsRefreshing ?? false,
            statusMonitor?.NextRefreshUtc, statusMonitor?.StorageError ?? "");

    private IReadOnlyList<QuotaState> QuotaStates() => quotaMonitor?.Snapshot() ??
        Enum.GetValues<QuotaProvider>().Select(provider => new QuotaState(provider)).ToArray();

    private void ShowPanel()
    {
        if (stopping || panel is null || statusItem?.Button is not { } button) return;
        if (!smoke) RefreshAutoStart();
        RefreshDisplay(true);
        UpdatePanel(Schedule(DateTimeOffset.UtcNow), ProviderStates());
        panel.Show(button);
    }

    private void ShowStatistics()
    {
        if (stopping) return;
        statisticsWindow ??= new(store);
        statisticsWindow.Show();
    }

    private void RefreshAll() => RefreshAll(false);

    private void RefreshAll(bool queueWhileRefreshing)
    {
        if (stopping || smoke) return;
        statusMonitor?.RequestRefresh(queueWhileRefreshing);
        quotaMonitor?.RequestRefresh(queueWhileRefreshing);
    }

    // Raised off the main thread; the shared scheduler waits for DHCP/DNS and limits flapping adapters.
    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs args)
    {
        if (args.IsAvailable && !stopping) networkRefresh?.OnNetworkAvailable();
    }

    private async Task ChangeHolidayAsync(bool enabled)
    {
        if (stopping || !holidayReady || savingHoliday || enabled == holidayEnabled) return;
        savingHoliday = true;
        RefreshHolidayControls();
        // User-accepted writes finish before shutdown; only network/CLI work is canceled.
        Task save = store.SetHolidayAdjustmentAsync(enabled);
        pendingWrites.Add(save);
        try
        {
            await save;
            if (stopping) return;
            holidayEnabled = enabled;
            int alertsBeforeRefresh = providerNotificationSerial;
            // A policy change isn't a clock transition, but provider changes arriving now still notify.
            RefreshDisplay(false, notifyProviders: true);
            panel?.SetFeedback(FeedbackText.HolidaySaved(enabled));
            // Do not immediately replace an important concurrent service alert (the Windows rule).
            if (alertsBeforeRefresh == providerNotificationSerial)
            {
                var (title, body) = FeedbackText.HolidayNotification(enabled, Schedule(DateTimeOffset.UtcNow));
                notifications?.Show(title, body);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!stopping) panel?.SetFeedback(FeedbackText.HolidaySaveFailed(error.Message), error: true); }
        finally
        {
            pendingWrites.Remove(save);
            savingHoliday = false;
            if (!stopping) RefreshHolidayControls();
        }
    }

    private void RefreshHolidayControls() =>
        panel?.SetHoliday(holidayEnabled, holidayReady && !savingHoliday && !stopping);

    private void ChangeAutoStart(bool enabled)
    {
        if (stopping || smoke) return;
        try { panel?.SetFeedback(MacAutoStart.Set(enabled)); }
        catch (Exception error) { panel?.SetFeedback("자동 실행 변경 실패: " + error.Message, error: true); }
        RefreshAutoStart();
    }

    private void RefreshAutoStart()
    {
        try
        {
            var status = MacAutoStart.Read();
            panel?.SetAutoStart(status.Enabled, status.Detail);
        }
        catch (Exception error) { panel?.SetFeedback("자동 실행 확인 실패: " + error.Message, error: true); }
    }

    private void OpenStatusPage(ProviderKind provider)
    {
        statusPageRequests++;
        if (stopping || smoke) return;
        using var url = new NSUrl(ProviderStatusPages.For(provider).AbsoluteUri);
        if (!NSWorkspace.SharedWorkspace.OpenUrl(url))
            panel?.SetFeedback("공식 상태 페이지를 열지 못했습니다. 기본 브라우저를 확인하세요.", error: true);
    }

    private async Task RecordAsync(ProviderKind provider, UsageEventType type, bool withNote)
    {
        try { await initialization; }
        catch (Exception error)
        {
            if (!stopping) panel?.SetFeedback(FeedbackText.RecordStartupFailed(error.Message), error: true);
            return;
        }
        if (stopping) return;
        // Capture when the item is picked, before the note prompt (the Windows order).
        ScheduleSnapshot schedule = Schedule(DateTimeOffset.UtcNow);
        UsageMeasurement measurement = UsageMeasurementFactory.Capture(provider, type, schedule,
            ProviderStates().Single(item => item.Provider == provider), AppVersion);
        string? truncated = null;
        bool reopen = false;
        if (withNote)
        {
            // A transient popover closes behind a modal alert: close it first, reopen after the save.
            reopen = panel?.IsShown == true;
            panel?.Close();
            var answer = AskNote(provider, type);
            // Quit may start while the modal alert is open; shutdown has then collected pendingWrites.
            if (stopping) return;
            if (answer is null)
            {
                if (reopen) ShowPanel();
                return;
            }
            measurement = UsageMeasurementFactory.WithNote(measurement, answer.Value.Type, answer.Value.Note);
            // NSTextField has no length limit, unlike the Windows note box; say when a note was cut.
            if (measurement.UserNote.Length < answer.Value.Note.Trim().Length)
                truncated = FeedbackText.NoteTruncated(measurement.UserNote.Length);
        }
        try
        {
            await SaveMeasurementAsync(measurement);
            if (!stopping) panel?.SetFeedback(truncated ?? FeedbackText.RecordSaved(provider, measurement.EventType, schedule));
        }
        catch (Exception error)
        {
            if (!stopping) panel?.SetFeedback(FeedbackText.RecordFailed(error.Message), error: true);
        }
        if (reopen && !stopping) ShowPanel();
    }

    private async Task SaveMeasurementAsync(UsageMeasurement measurement)
    {
        if (stopping) return;
        Task save = store.AddUsageAsync(measurement);
        pendingWrites.Add(save);
        try { await save; }
        finally { pendingWrites.Remove(save); }
    }

    private static (UsageEventType Type, string Note)? AskNote(ProviderKind provider, UsageEventType type)
    {
        using var alert = new NSAlert
        {
            MessageText = ProviderNames.Provider(provider) + " 사용 경험 기록",
            InformativeText = "메모는 선택 사항입니다. Prompt·답변·계정 정보는 기록하지 마세요."
        };
        using var accessory = new NSView(new CGRect(0, 0, 330, 87));
        using var choice = new NSPopUpButton(new CGRect(0, 54, 164, 27), false);
        choice.AddItems(Enum.GetNames<UsageEventType>());
        choice.SelectItem((int)type);
        using var note = new NSTextField(new CGRect(0, 8, 330, 34)) { PlaceholderString = $"선택적 메모 (최대 {UsageMeasurementFactory.MaximumNoteLength:N0}자)" };
        accessory.AddSubview(choice);
        accessory.AddSubview(note);
        alert.AccessoryView = accessory;
        alert.AddButton("저장");
        alert.AddButton("취소");
        NSApplication.SharedApplication.Activate();
        if (alert.RunModal() != 1000) return null;
        return ((UsageEventType)(int)choice.IndexOfSelectedItem, note.StringValue.Trim());
    }

    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
    {
        if (stopped) return NSApplicationTerminateReply.Now;
        if (!stopping)
        {
            stopping = true;
            timer?.Invalidate();
            lifetime.Cancel();
            _ = StopAsync();
        }
        return NSApplicationTerminateReply.Later;
    }

    private async Task StopAsync()
    {
        try
        {
            // A startup or note save cannot outlive HTTP/CLI teardown.
            try { await initialization; } catch { }
            Task statisticsStop = statisticsWindow?.StopAsync() ?? Task.CompletedTask;
            if (statusMonitor is not null) await statusMonitor.StopAsync();
            if (quotaMonitor is not null) await quotaMonitor.StopAsync();
            await statisticsStop;
            try { await Task.WhenAll(pendingWrites.ToArray()); } catch { }
        }
        finally
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
            networkRefresh?.Dispose();
            if (wakeObserver is not null)
            {
                NSWorkspace.SharedWorkspace.NotificationCenter.RemoveObserver(wakeObserver);
                wakeObserver.Dispose();
                wakeObserver = null;
            }
            if (statusMonitor is not null) statusMonitor.Changed -= QueueDisplayRefresh;
            if (quotaMonitor is not null) quotaMonitor.Changed -= QueueDisplayRefresh;
            statusMonitor?.Dispose();
            quotaMonitor?.Dispose();
            http?.Dispose();
            notifications?.Stop();
            notifications?.Dispose();
            statisticsWindow?.Dispose();
            panel?.Dispose();
            if (statusItem is not null) NSStatusBar.SystemStatusBar.RemoveStatusItem(statusItem);
            statusItem?.Dispose();
            statusImage?.Dispose();
            contextMenu?.Dispose();
            timer?.Dispose();
            lifetime.Dispose();
            stopped = true;
            if (smoke)
            {
                // NSApplication's native termination may exit without returning to C# Main.
                // Preserve the smoke result after all async work/resources have stopped.
                string directory = Path.GetDirectoryName(store.DatabasePath)!;
                if (Path.GetFileName(directory).StartsWith("aiburgerclock-mac-smoke-", StringComparison.Ordinal) &&
                    string.Equals(Path.GetFullPath(Path.GetDirectoryName(directory)!).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
                {
                    try { Directory.Delete(directory, recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                Environment.Exit(ExitCode);
            }
            NSApplication.SharedApplication.ReplyToApplicationShouldTerminate(true);
        }
    }

    private async Task RunSmokeAsync()
    {
        // Opt-in native check: temporary DB, no HTTP/CLI, permission prompt, browser or login-item writes.
        try
        {
            if (statusItem?.Button is not { } button || contextMenu is null || panel is null)
                throw new InvalidOperationException("Native menu-bar/popover controls were not created.");
            string bundleVersion = NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "";
            if (bundleVersion != AppVersion)
                throw new InvalidOperationException($"Bundle version {bundleVersion} does not match app version {AppVersion}.");
            int ticks = timerTicks;
            // Run only the menu event-tracking mode; each pass may return after one input source.
            DateTime deadline = DateTime.UtcNow.AddSeconds(2.5);
            while (timerTicks == ticks && DateTime.UtcNow < deadline)
                NSRunLoop.Main.RunUntil(NSRunLoopMode.EventTracking, NSDate.FromTimeIntervalSinceNow(0.25));
            if (timerTicks == ticks)
                throw new InvalidOperationException("The countdown timer stopped while a menu was tracking events.");
            double iconContrast = MacStatusIcon.VerifyImages();
            if (button.Image is not { } icon || icon.Template ||
                icon.Size.Width != MacStatusIcon.Size || icon.Size.Height != MacStatusIcon.Size)
                throw new InvalidOperationException("The menu-bar button did not retain its 20-point color icon.");

            // Click routing: no attached menu (it would open on a left click), left toggles the popover,
            // right/control-click opens the short menu.
            if (statusItem.Menu is not null)
                throw new InvalidOperationException("The status item must not own a menu that opens on left click.");
            static NSEvent Click(NSEventType type, NSEventModifierMask flags = 0) =>
                NSEvent.MouseEvent(type, CGPoint.Empty, flags, 0, 0, null, 0, 1, 1f)!;
            if (IsContextClick(Click(NSEventType.LeftMouseUp)) || !IsContextClick(Click(NSEventType.RightMouseUp)) ||
                !IsContextClick(Click(NSEventType.LeftMouseUp, NSEventModifierMask.ControlKeyMask)))
                throw new InvalidOperationException("Left/right/control-click routing is wrong.");
            string[] menuTitles = contextMenu.Items.Select(item => item.IsSeparatorItem ? "-" : item.Title).ToArray();
            if (!menuTitles.SequenceEqual(["Refresh", "로그인 항목 설정 열기…", "-", "종료"]) ||
                contextMenu.Items[^1].KeyEquivalent != "q")
                throw new InvalidOperationException("Right-click menu must be Refresh / 로그인 항목 설정 열기… / 종료 ⌘Q.");

            ShowPanel();
            if (!panel.IsShown) throw new InvalidOperationException("Status popover did not open.");
            // Tooltips and active control accents need an active app and a key popover window right away.
            // macOS grants activation on real user input (a status-item click). A smoke run has none, so
            // activation is checked only when the system happens to grant it; the user check covers real clicks.
            DateTime activation = DateTime.UtcNow.AddSeconds(2);
            while (!(NSApplication.SharedApplication.Active && panel.Window?.IsKeyWindow == true) && DateTime.UtcNow < activation)
                NSRunLoop.Main.RunUntil(NSRunLoopMode.Default, NSDate.FromTimeIntervalSinceNow(0.05));
            if (panel.Window?.CanBecomeKeyWindow != true)
                throw new InvalidOperationException("The popover window cannot become key.");
            if (NSApplication.SharedApplication.Active && panel.Window.IsKeyWindow != true)
                throw new InvalidOperationException("The app is active but the opened popover is not the key window.");
            string activationResult = NSApplication.SharedApplication.Active
                ? "popover active/key on open"
                : "popover activation not checked (macOS did not grant activation without a user click)";
            // Readable tone text on the opaque background whatever the desktop behind it, in both appearances.
            double weakest = double.MaxValue;
            foreach (NSString name in new[] { NSAppearance.NameAqua, NSAppearance.NameDarkAqua })
            {
                NSAppearance appearance = NSAppearance.GetAppearance(name)
                    ?? throw new InvalidOperationException("Missing appearance " + name);
                if (MacControls.IsDark(appearance) != (name == NSAppearance.NameDarkAqua))
                    throw new InvalidOperationException("Appearance detection is wrong for " + name);
                foreach (PanelTone tone in Enum.GetValues<PanelTone>())
                {
                    // On the panel/quota-box background and on a hovered provider card.
                    foreach (var (surface, background) in new (string, Func<NSColor>)[]
                        { ("background", () => MacControls.PanelBackground), ("hovered card", MacControls.CardHoverBackground) })
                    {
                        double contrast = MacControls.Contrast(MacControls.Color(tone), background, appearance);
                        weakest = Math.Min(weakest, contrast);
                        if (contrast < 4.5)
                            throw new InvalidOperationException($"{tone} text contrast {contrast:0.00}:1 on the {surface} is below 4.5:1 in {name}.");
                    }
                }
            }
            panel.Close();
            if (panel.IsShown) throw new InvalidOperationException("Status popover did not close.");
            StatusItemClicked(); // No current mouse event: treated as a left click.
            if (!panel.IsShown) throw new InvalidOperationException("Status popover did not reopen from the status item.");

            string longNote = new string('a', UsageMeasurementFactory.MaximumNoteLength - 1) + "🍔";
            if (UsageMeasurementFactory.Note(longNote) != longNote[..(UsageMeasurementFactory.MaximumNoteLength - 1)])
                throw new InvalidOperationException("Note length limit split a character.");
            var now = DateTimeOffset.UtcNow;
            ProviderStatus claudeStatus = ProviderStates().Single(item => item.Provider == ProviderKind.Claude);
            foreach (UsageEventType type in Enum.GetValues<UsageEventType>())
                await SaveMeasurementAsync(UsageMeasurementFactory.WithNote(UsageMeasurementFactory.Capture(
                    ProviderKind.Claude, UsageEventType.Success, Schedule(now), claudeStatus, AppVersion), type, "native smoke note"));
            var (items, skipped) = await store.ReadUsageWithSkippedAsync(null, lifetime.Token);
            if (items.Count != 4 || skipped != 0 || items.Any(item => item.UserNote != "native smoke note") ||
                !items.Select(item => item.EventType).Order().SequenceEqual(Enum.GetValues<UsageEventType>()) ||
                items.Any(item => item.AppVersion != AppVersion))
                throw new InvalidOperationException("Four event types and notes did not persist.");
            var report = StatisticsAnalysis.Build(items);
            if (!MacStatisticsWindow.FormatRows(report.Providers).Contains("n=4", StringComparison.Ordinal) ||
                !MacStatisticsWindow.FormatRows(report.Hours).Contains("No data", StringComparison.Ordinal))
                throw new InvalidOperationException("Statistics sample sizes/No data were not rendered.");
            // Shared statistics text; the empty state names the Mac way to record (card right-click).
            if (MacStatisticsWindow.SummaryFor(0, "No data", 0) !=
                "직접 기록한 표본 n = 0 · 정책: No data\nNo data · " + MacStatisticsWindow.HowToRecord ||
                !MacStatisticsWindow.HowToRecord.Contains("오른쪽 클릭", StringComparison.Ordinal))
                throw new InvalidOperationException("Statistics empty-state text is wrong.");
            QuotaState quota = new(QuotaProvider.Codex, new(QuotaProvider.Codex,
                [new("session", "5시간", 100, now.AddMinutes(15), 300),
                 new("weekly", "주간", 9, now.AddDays(6), 10080)]),
                LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddMinutes(5));
            QuotaState claudeQuota = new(QuotaProvider.Claude, new(QuotaProvider.Claude,
                [new("session", "세션 (5시간)", 9, now.AddMinutes(98), 300),
                 new("weekly_all", "주간 전체", 37, now.AddDays(3), 10080),
                 new("weekly_scoped", "주간 Fable", 2, now.AddDays(3), 10080)]),
                LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddMinutes(83));
            ProviderStatus[] healthy = Enum.GetValues<ProviderKind>().Select(provider =>
                new ProviderStatus(provider, OfficialStatus.Operational, now, now, "관련 서비스 정상")).ToArray();
            panel.Update(Schedule(now), healthy, [quota, claudeQuota], false, now.AddMinutes(5), "");
            MacStatusPanel.Layout layout = panel.Verify(Schedule(now), healthy, [quota, claudeQuota]);
            // The whole card opens the official page (VoiceOver press and click share the action).
            int requests = statusPageRequests;
            if (!panel.PressCard(ProviderKind.Claude) || statusPageRequests != requests + 1)
                throw new InvalidOperationException("Pressing a provider card did not request its official page.");
            // Screen fit: a 1280x800 screen shows everything; a 1024x640 screen (13-inch "larger text")
            // keeps the whole popover visible by shrinking and scrolling only the quota area.
            const int MenuBarAndMargin = 25 + 24;
            CGSize standard = panel.FitFor(800 - MenuBarAndMargin);
            if (standard.Height > 800 - MenuBarAndMargin || panel.QuotaAreaHeight != panel.QuotaRowsHeight)
                throw new InvalidOperationException($"Popover {standard.Height}pt does not fit a 1280x800 screen without scrolling.");
            CGSize small = panel.FitFor(640 - MenuBarAndMargin);
            nfloat smallQuota = panel.QuotaAreaHeight;
            if (small.Height > 640 - MenuBarAndMargin || smallQuota >= panel.QuotaRowsHeight)
                throw new InvalidOperationException($"Popover {small.Height}pt is cut off on a 1024x640 screen.");
            panel.FitFor(layout.UsableHeight);
            if (!panel.QuotaRowText(QuotaProvider.Codex, "session").Contains("0% 남음 · 00:15:00", StringComparison.Ordinal))
                throw new InvalidOperationException("Exhausted quota/countdown display failed.");
            panel.Update(Schedule(now.AddMinutes(1)), healthy, [quota, claudeQuota], false, now.AddMinutes(5), "");
            if (!panel.QuotaRowText(QuotaProvider.Codex, "session").Contains("00:14:00", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected quota countdown did not advance.");
            panel.Close();
            ShowStatistics();
            await statisticsWindow!.RefreshAsync();
            if (!statisticsWindow.Window.IsVisible || statisticsWindow.Window.DangerousReleasedWhenClosed)
                throw new InvalidOperationException("Statistics window is not visible/retained.");
            if (!statisticsWindow.SummaryText.StartsWith("직접 기록한 표본 n = 4 · 정책: ", StringComparison.Ordinal) ||
                statisticsWindow.SummaryText.Contains('\n') || !statisticsWindow.ExplanationFits())
                throw new InvalidOperationException("Statistics summary/explanation did not use the shared text or fit.");
            statisticsWindow.Window.Close();
            if (statisticsWindow.Window.IsVisible)
                throw new InvalidOperationException("Statistics window did not close.");
            ShowStatistics();
            await statisticsWindow.RefreshAsync();
            if (!statisticsWindow.Window.IsVisible)
                throw new InvalidOperationException("Statistics window did not reopen from the menu-bar action.");
            Console.WriteLine($"PASS: bundle version, menu-tracking countdown timer, 20pt menu icon white/dark disc + color glyph light/dark 1x-2x pixels (glyph contrast min {iconContrast:0.0}:1), left/right/control-click routing, right-click menu, popover open/close/reopen, shared panel text/record menu/quota lines, {activationResult}, tone contrast >= 4.5:1 light+dark (min {weakest:0.0}:1), card text x = quota box text x ({layout.BoxTextX:0}pt; titles {layout.TitleX:0}pt), whole-card click/quota boxes read-only, popover {layout.Size.Width:0}x{layout.Size.Height:0}pt (this screen usable {layout.UsableHeight:0}pt; 1280x800 fits; 1024x640 {small.Height:0}pt with quota area {smallQuota:0}pt scrolling), 1,000-char note limit, temporary SQLite, four events/notes via shared factory, statistics (shared text, explanation fits, Mac empty-state hint), injected quota countdown; no account/network/settings changes.");
            ExitCode = 0;
        }
        catch (Exception error)
        {
            ExitCode = 1;
            Console.Error.WriteLine("FAIL: " + error.Message);
        }
        // Queue instead of calling during initialization: shutdown must await initialization, not itself.
        BeginInvokeOnMainThread(() => NSApplication.SharedApplication.Terminate(null));
    }
}
