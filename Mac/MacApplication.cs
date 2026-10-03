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
    private MacStatusWindow? statusWindow;
    private MacStatisticsWindow? statisticsWindow;
    private MacNotifications? notifications;
    private NSStatusItem? statusItem;
    private NSMenu? menu;
    private NSMenuItem? scheduleItem;
    private NSMenuItem? countdownItem;
    private NSMenuItem? holidayItem;
    private NSMenuItem? autoStartItem;
    private NSTimer? timer;
    private NSObject? wakeObserver;
    private AgentState? lastSchedule;
    private TrayAppearance? lastAppearance;
    // Apply the user's holiday policy only after its stored setting is read successfully.
    private bool holidayEnabled;
    private bool holidayReady;
    private bool savingHoliday;
    private bool stopping;
    private bool stopped;
    private int uiRefreshQueued;
    private long lastNetworkRefreshTick;
    internal int ExitCode { get; private set; }

    public override void DidFinishLaunching(NSNotification notification)
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        statusWindow = new(RefreshAll, ShowStatistics, enabled => _ = ChangeHolidayAsync(enabled), ChangeAutoStart,
            OpenStatusPage, (provider, type, note) => _ = RecordAsync(provider, type, note), smoke);
        CreateMenu();
        statusItem = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Variable);
        statusItem.Menu = menu;
        RefreshDisplay(false);
        // One existing-style countdown timer; Schedule itself caches its date/policy calculations.
        timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromSeconds(1), _ => RefreshDisplay(true));
        initialization = InitializeAsync();
    }

    private void CreateMenu()
    {
        menu = new NSMenu { AutoEnablesItems = false };
        scheduleItem = new NSMenuItem("AI Burger Clock") { Enabled = false };
        countdownItem = new NSMenuItem("전환까지 —") { Enabled = false };
        menu.AddItem(scheduleItem);
        menu.AddItem(countdownItem);
        menu.AddItem(new NSMenuItem("상태 창 열기", (_, _) => ShowWindow()));
        menu.AddItem(new NSMenuItem("Refresh", (_, _) => RefreshAll()));
        menu.AddItem(new NSMenuItem("Statistics", (_, _) => ShowStatistics()));
        menu.AddItem(NSMenuItem.SeparatorItem);
        foreach (ProviderKind provider in Enum.GetValues<ProviderKind>())
        {
            ProviderKind captured = provider;
            var item = new NSMenuItem(provider + " 사용 경험 기록")
            {
                Submenu = MacStatusWindow.RecordMenu(provider, (p, type, note) => _ = RecordAsync(p, type, note))
            };
            menu.AddItem(item);
            menu.AddItem(new NSMenuItem(provider + " 공식 상태 ↗", (_, _) => OpenStatusPage(captured)));
        }
        menu.AddItem(NSMenuItem.SeparatorItem);
        holidayItem = new NSMenuItem("미국 연방 공휴일 보정", (_, _) => _ = ChangeHolidayAsync(!holidayEnabled)) { Enabled = false };
        menu.AddItem(holidayItem);
        autoStartItem = new NSMenuItem("로그인 시 자동 실행", (_, _) => ToggleAutoStart())
        {
            Enabled = !smoke
        };
        menu.AddItem(autoStartItem);
        menu.AddItem(new NSMenuItem("로그인 항목 설정 열기…", (_, _) => MacAutoStart.OpenSettings()) { Enabled = !smoke });
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("종료", "q", (_, _) => NSApplication.SharedApplication.Terminate(null)));
    }

    private async Task InitializeAsync()
    {
        try
        {
            await store.InitializeAsync(lifetime.Token);
            holidayEnabled = await store.GetHolidayAdjustmentAsync(lifetime.Token);
            if (stopping) return;
            holidayReady = true;
            RefreshHolidayControls();
            RefreshDisplay(false);
            if (smoke)
            {
                await RunSmokeAsync();
                return;
            }
            http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            statusMonitor = new(new ProviderStatusClient(http), store, scheduleAt: Schedule);
            quotaMonitor = new(new AccountQuotaClient(), store);
            statusMonitor.Changed += QueueDisplayRefresh;
            quotaMonitor.Changed += QueueDisplayRefresh;
            // System notification permission is optional and must not stop status/quota polling.
            try
            {
                notifications = new(text => statusWindow?.SetFeedback(text));
                notifications.RequestPermission();
            }
            catch (Exception) { statusWindow?.SetFeedback("알림 초기화 실패 · macOS 알림 설정을 확인하세요."); }
            RefreshAutoStart();
            wakeObserver = NSWorkspace.SharedWorkspace.NotificationCenter.AddObserver(NSWorkspace.DidWakeNotification,
                _ => RefreshAll(queueWhileRefreshing: true));
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
            statusMonitor.Start();
            quotaMonitor.Start();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            statusWindow?.SetFeedback("초기화 실패: " + error.Message);
            if (smoke)
            {
                ExitCode = 1;
                Console.Error.WriteLine("FAIL: native smoke initialization");
                NSApplication.SharedApplication.Terminate(null);
            }
        }
    }

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

    private void RefreshDisplay(bool notify)
    {
        if (stopping) return;
        ScheduleSnapshot snapshot = Schedule(DateTimeOffset.UtcNow);
        var providers = ProviderStates();
        bool changed = lastSchedule.HasValue && lastSchedule != snapshot.State;
        if (notify && changed)
        {
            string body = snapshot.State == AgentState.FullThrottle
                ? (snapshot.IsHolidayExtendedFullThrottle ? "미국 공휴일이 포함된 연장 FULL 구간입니다. " : "미국 업무시간 밖입니다. ") +
                    $"다음 전환: {snapshot.NextTransitionKst:MM-dd HH:mm} KST. Provider별 상태를 확인하세요."
                : "새 대형 Agent 작업은 다음 FULL THROTTLE까지 미뤄두세요.";
            notifications?.Show(TrayPresentation.StateName(snapshot.State) + " 시작", body);
        }
        lastSchedule = snapshot.State;
        foreach (ProviderStatus status in providers)
        {
            var recommendation = RecommendationPolicy.Calculate(snapshot.State, status.Status);
            if (recommendationNotifications.Observe(status.Provider, snapshot.State, recommendation, notify && !changed))
                notifications?.Show(status.Provider + (recommendation == Recommendation.Go ? " 정상화" : " 작업 권고 변경"),
                    recommendation == Recommendation.Go
                        ? "관련 서비스가 정상화되었습니다. 현재 FULL THROTTLE이므로 대규모 작업 재개 가능."
                        : $"FULL THROTTLE이지만 공식 서비스 문제가 있습니다. {RecommendationPolicy.Label(recommendation)}: 새 대형 작업을 미루세요.\n{status.Reason}");
        }
        if (scheduleItem is not null) scheduleItem.Title = TrayPresentation.StateName(snapshot.State);
        if (countdownItem is not null) countdownItem.Title = "전환까지 " + DisplayFormatting.FormatRemaining(snapshot.Remaining);
        TrayAppearance appearance = TrayPresentation.Calculate(snapshot.State, providers);
        if (statusItem?.Button is { } button)
        {
            if (lastAppearance != appearance)
            {
                // SF Symbols stay sharp at all menu-bar scales; same policy/color as Windows.
                using var image = NSImage.GetSystemSymbol(appearance.Glyph.ToLowerInvariant() + ".circle.fill", appearance.Glyph);
                button.Image = image;
                button.Title = image is null ? appearance.Glyph : "";
                button.ContentTintColor = MacStatusWindow.Color(appearance.Color);
                lastAppearance = appearance;
            }
            button.ToolTip = TrayPresentation.Tooltip(snapshot, providers);
        }
        statusWindow?.Update(snapshot, providers, quotaMonitor?.Snapshot() ??
            Enum.GetValues<QuotaProvider>().Select(provider => new QuotaState(provider)).ToArray(),
            statusMonitor?.IsRefreshing ?? false, statusMonitor?.NextRefreshUtc, statusMonitor?.StorageError ?? "");
    }

    private void ShowWindow()
    {
        if (stopping) return;
        if (!smoke) RefreshAutoStart();
        RefreshDisplay(true);
        statusWindow?.Show();
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

    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs args)
    {
        if (!args.IsAvailable || stopping) return;
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref lastNetworkRefreshTick);
        if (last != 0 && now - last < 60_000) return;
        if (Interlocked.CompareExchange(ref lastNetworkRefreshTick, now, last) == last)
            RefreshAll(queueWhileRefreshing: true);
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
            // Changing the user's policy isn't a clock transition or a new service incident.
            lastSchedule = null;
            RefreshDisplay(false);
            statusWindow?.SetFeedback("미국 연방 공휴일 보정 " + (enabled ? "켜짐" : "꺼짐"));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!stopping) statusWindow?.SetFeedback("공휴일 설정 저장 실패: " + error.Message); }
        finally
        {
            pendingWrites.Remove(save);
            savingHoliday = false;
            if (!stopping) RefreshHolidayControls();
        }
    }

    private void RefreshHolidayControls()
    {
        bool ready = holidayReady && !savingHoliday && !stopping;
        if (holidayItem is not null)
        {
            holidayItem.State = holidayEnabled ? NSCellStateValue.On : NSCellStateValue.Off;
            holidayItem.Enabled = ready;
        }
        statusWindow?.SetHoliday(holidayEnabled, ready);
    }

    private void ChangeAutoStart(bool enabled)
    {
        if (stopping || smoke) return;
        try { statusWindow?.SetFeedback(MacAutoStart.Set(enabled)); }
        catch (Exception error) { statusWindow?.SetFeedback("자동 실행 변경 실패: " + error.Message); }
        RefreshAutoStart();
    }

    private void ToggleAutoStart()
    {
        if (stopping || smoke) return;
        try { ChangeAutoStart(!MacAutoStart.Read().Enabled); }
        catch (Exception error) { statusWindow?.SetFeedback("자동 실행 확인 실패: " + error.Message); }
    }

    private void RefreshAutoStart()
    {
        try
        {
            var status = MacAutoStart.Read();
            statusWindow?.SetAutoStart(status.Enabled, status.Detail);
            if (autoStartItem is not null)
            {
                autoStartItem.State = status.Enabled ? NSCellStateValue.On : NSCellStateValue.Off;
                autoStartItem.Title = status.Detail;
            }
        }
        catch (Exception error) { statusWindow?.SetFeedback("자동 실행 확인 실패: " + error.Message); }
    }

    private void OpenStatusPage(ProviderKind provider)
    {
        if (stopping || smoke) return;
        using var url = new NSUrl(ProviderStatusPages.For(provider).AbsoluteUri);
        if (!NSWorkspace.SharedWorkspace.OpenUrl(url))
            statusWindow?.SetFeedback("공식 상태 페이지를 열지 못했습니다. 기본 브라우저를 확인하세요.");
    }

    private async Task RecordAsync(ProviderKind provider, UsageEventType type, bool withNote)
    {
        try
        {
            await initialization;
            if (stopping) return;
            string note = "";
            if (withNote)
            {
                var value = AskNote(provider, type);
                if (value is null || stopping) return;
                (type, note) = value.Value;
            }
            await SaveMeasurementAsync(provider, type, note);
        }
        catch (Exception error)
        {
            if (!stopping) statusWindow?.SetFeedback("사용 경험 저장 실패: " + error.Message);
        }
    }

    private async Task SaveMeasurementAsync(ProviderKind provider, UsageEventType type, string note)
    {
        if (stopping) return;
        ScheduleSnapshot schedule = Schedule(DateTimeOffset.UtcNow);
        ProviderStatus status = ProviderStates().Single(item => item.Provider == provider);
        var measurement = new UsageMeasurement(Guid.NewGuid().ToString("N"), provider, type, schedule.NowUtc,
            schedule.State, schedule.IsWeekendExtendedFullThrottle, schedule.EasternUtcOffsetMinutes,
            schedule.PacificUtcOffsetMinutes, schedule.EasternIsDst, schedule.PacificIsDst,
            schedule.SchedulePolicyVersion, status.Status, RecommendationPolicy.Calculate(schedule.State, status.Status),
            status.RelevantComponent, status.IncidentId, note,
            typeof(MacApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0",
            schedule.HolidayAdjustmentEnabled, schedule.IsHolidayExtendedFullThrottle, schedule.HolidayNames);
        Task save = store.AddUsageAsync(measurement);
        pendingWrites.Add(save);
        try
        {
            await save;
            if (!stopping) statusWindow?.SetFeedback($"{provider} · {type} 저장됨 ({schedule.NowKst:HH:mm} KST)");
        }
        finally { pendingWrites.Remove(save); }
    }

    private static (UsageEventType Type, string Note)? AskNote(ProviderKind provider, UsageEventType type)
    {
        using var alert = new NSAlert
        {
            MessageText = provider + " 사용 경험 기록",
            InformativeText = "메모는 선택 사항입니다. Prompt·답변·계정 정보는 기록하지 마세요."
        };
        using var accessory = new NSView(new CGRect(0, 0, 330, 87));
        using var choice = new NSPopUpButton(new CGRect(0, 54, 164, 27), false);
        choice.AddItems(Enum.GetNames<UsageEventType>());
        choice.SelectItem((int)type);
        using var note = new NSTextField(new CGRect(0, 8, 330, 34)) { PlaceholderString = "선택적 메모 (최대 2,000자)" };
        accessory.AddSubview(choice);
        accessory.AddSubview(note);
        alert.AccessoryView = accessory;
        alert.AddButton("저장");
        alert.AddButton("취소");
        NSApplication.SharedApplication.Activate();
        if (alert.RunModal() != 1000) return null;
        string text = note.StringValue.Trim();
        return ((UsageEventType)(int)choice.IndexOfSelectedItem, text.Length <= 2000 ? text : text[..2000]);
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
            statusWindow?.Dispose();
            if (statusItem is not null) NSStatusBar.SystemStatusBar.RemoveStatusItem(statusItem);
            statusItem?.Dispose();
            menu?.Dispose();
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
            if (statusItem?.Button is null || menu is null || statusWindow is null)
                throw new InvalidOperationException("Native menu-bar/window controls were not created.");
            statusWindow.Show();
            if (!statusWindow.Window.IsVisible || statusWindow.Window.DangerousReleasedWhenClosed)
                throw new InvalidOperationException("Status window is not visible/retained.");
            statusWindow.Window.Close();
            if (statusWindow.Window.IsVisible)
                throw new InvalidOperationException("Status window did not close.");
            ShowWindow();
            if (!statusWindow.Window.IsVisible)
                throw new InvalidOperationException("Status window did not reopen from the menu-bar action.");
            foreach (UsageEventType type in Enum.GetValues<UsageEventType>())
                await SaveMeasurementAsync(ProviderKind.Claude, type, "native smoke note");
            var (items, skipped) = await store.ReadUsageWithSkippedAsync(null, lifetime.Token);
            if (items.Count != 4 || skipped != 0 || items.Any(item => item.UserNote != "native smoke note"))
                throw new InvalidOperationException("Four event types and notes did not persist.");
            var report = StatisticsAnalysis.Build(items);
            if (!MacStatisticsWindow.FormatRows(report.Providers).Contains("n=4", StringComparison.Ordinal) ||
                !MacStatisticsWindow.FormatRows(report.Hours).Contains("No data", StringComparison.Ordinal))
                throw new InvalidOperationException("Statistics sample sizes/No data were not rendered.");
            var now = DateTimeOffset.UtcNow;
            QuotaState quota = new(QuotaProvider.Codex, new(QuotaProvider.Codex,
                [new("session", "5시간", 100, now.AddMinutes(15), 300)]), NextCheckUtc: now.AddMinutes(5));
            if (!MacStatusWindow.QuotaText(quota, now).Contains("0%", StringComparison.Ordinal))
                throw new InvalidOperationException("Exhausted quota display failed.");
            ShowStatistics();
            await statisticsWindow!.RefreshAsync();
            if (!statisticsWindow.Window.IsVisible || statisticsWindow.Window.DangerousReleasedWhenClosed)
                throw new InvalidOperationException("Statistics window is not visible/retained.");
            statisticsWindow.Window.Close();
            if (statisticsWindow.Window.IsVisible)
                throw new InvalidOperationException("Statistics window did not close.");
            ShowStatistics();
            await statisticsWindow.RefreshAsync();
            if (!statisticsWindow.Window.IsVisible)
                throw new InvalidOperationException("Statistics window did not reopen from the menu-bar action.");
            Console.WriteLine("PASS: native controls/window close-reopen, temporary SQLite, four events/notes, statistics, quota countdown; no account/network/settings changes.");
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
