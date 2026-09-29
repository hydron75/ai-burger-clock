using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace AiBurgerClock
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly StatusWindow statusWindow;
        private readonly Timer timer;
        private readonly ContextMenuStrip menu;
        // Assigned DropDowns are not auto-generated, so disposing the menu does not dispose them.
        private readonly List<ContextMenuStrip> recordMenus = new();
        private readonly Font stateMenuFont;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly ToolStripMenuItem stateMenuItem;
        private readonly ToolStripMenuItem countdownMenuItem;
        private readonly ToolStripMenuItem autoStartMenuItem;
        private readonly ToolStripMenuItem holidayMenuItem;
        private Icon? currentIcon;
        private TrayAppearance? currentAppearance;
        private AgentState? lastState;
        private bool disposed;
        private bool exiting;
        private readonly UsageStore store;
        private readonly HttpClient? ownedHttpClient; // Null when the caller supplied the client.
        private readonly StatusMonitor? monitor;
        private readonly AccountQuotaMonitor? quotaMonitor;
        private StatisticsWindow? statisticsWindow;
        private readonly HashSet<Task> pendingWrites = new();
        private readonly RecommendationNotifications providerNotifications = new();
        private int providerNotificationSerial;
        private int providerRefreshQueued;
        private long lastNetworkRefreshTick;
        internal static readonly TimeSpan NetworkRefreshMinimumInterval = TimeSpan.FromMinutes(1);
        private readonly Action<Uri> openStatusPage;
        private volatile bool holidayAdjustmentEnabled;
        private bool holidaySettingsReady;
        private bool changingHolidaySetting;
        private readonly Task initialization;

        internal event Action<AgentState>? TransitionNotificationRequested;
        internal event Action<ProviderKind, Recommendation>? ProviderNotificationRequested;

        // Normal runs use UTC now; the explicit smoke test supplies a clock without changing Windows time.
        public TrayApplicationContext(bool startedAutomatically, Func<DateTimeOffset>? utcNow = null, bool enableServices = true, UsageStore? usageStore = null, HttpClient? statusHttpClient = null, Action<Uri>? openStatusPage = null, IAccountQuotaClient? accountQuotaClient = null)
        {
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            this.openStatusPage = openStatusPage ?? ProviderStatusPages.Open;
            store = usageStore ?? new UsageStore();
            statusWindow = new StatusWindow();
            statusWindow.AutoStartChanged += OnWindowAutoStartChanged;
            statusWindow.RefreshRequested += (_, _) => RefreshAll();
            statusWindow.StatisticsRequested += (_, _) => ShowStatistics();
            statusWindow.RecordRequested += RecordMeasurement;
            statusWindow.StatusPageRequested += OpenStatusPage;
            statusWindow.HolidayAdjustmentChanged += async (_, _) => await SetHolidayAdjustmentAsync(statusWindow.HolidayAdjustmentChecked);

            menu = new ContextMenuStrip();
            stateMenuFont = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            stateMenuItem = new ToolStripMenuItem { Enabled = false, Font = stateMenuFont };
            countdownMenuItem = new ToolStripMenuItem { Enabled = false };
            ToolStripMenuItem showItem = new ToolStripMenuItem("상태 창 열기");
            autoStartMenuItem = new ToolStripMenuItem("Windows 시작 시 자동 실행") { CheckOnClick = true };
            holidayMenuItem = new ToolStripMenuItem("미국 연방 공휴일 보정") { CheckOnClick = true, Enabled = false,
                ToolTipText = "미국 연방 정기 공휴일·대체휴일의 업무 구간 제외 (서비스 품질 보장 아님)" };
            ToolStripMenuItem exitItem = new ToolStripMenuItem("종료");

            showItem.Click += delegate { ShowWindow(); };
            autoStartMenuItem.Click += OnMenuAutoStartClicked;
            holidayMenuItem.Click += async (_, _) => await SetHolidayAdjustmentAsync(holidayMenuItem.Checked);
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(stateMenuItem);
            menu.Items.Add(countdownMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(showItem);
            var refreshItem = new ToolStripMenuItem("상태·한도 새로 고침") { ToolTipText = "공식 서비스 상태와 Work / Codex·Claude 계정 한도를 함께 갱신" };
            refreshItem.Click += (_, _) => RefreshAll();
            menu.Items.Add(refreshItem);
            var recordRoot = new ToolStripMenuItem("사용 경험 기록");
            foreach (var provider in Enum.GetValues<ProviderKind>())
            {
                var recordMenu = StatusWindow.CreateRecordingMenu(provider, RecordMeasurement);
                recordMenus.Add(recordMenu);
                var recordItem = new ToolStripMenuItem(provider.ToString()) { DropDown = recordMenu };
                recordRoot.DropDownItems.Add(recordItem);
            }
            menu.Items.Add(recordRoot);
            var statisticsItem = new ToolStripMenuItem("Statistics");
            statisticsItem.Click += (_, _) => ShowStatistics();
            menu.Items.Add(statisticsItem);
            menu.Items.Add(autoStartMenuItem);
            menu.Items.Add(holidayMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            menu.ShowItemToolTips = true;
            menu.Opening += (_, _) => RefreshAutoStartChecks();
            statusWindow.Activated += (_, _) => RefreshAutoStartChecks();

            trayIcon = new NotifyIcon
            {
                ContextMenuStrip = menu,
                Text = "AI Burger Clock",
                Visible = true
            };
            trayIcon.MouseClick += delegate(object? sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    ShowWindow();
            };

            timer = new Timer { Interval = 1000 };
            timer.Tick += delegate { RefreshStatus(true); };

            if (enableServices)
            {
                if (statusHttpClient is null)
                {
                    ownedHttpClient = new HttpClient(new SocketsHttpHandler
                    {
                        UseCookies = false,
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                        ConnectTimeout = TimeSpan.FromSeconds(5),
                        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                    }) { Timeout = StatusMonitor.RequestTimeout };
                }
                HttpClient httpClient = statusHttpClient ?? ownedHttpClient!;
                monitor = new StatusMonitor(new ProviderStatusClient(httpClient, this.utcNow), store, this.utcNow, scheduleAt: GetSchedule);
                quotaMonitor = new AccountQuotaMonitor(accountQuotaClient ?? new AccountQuotaClient(), store, this.utcNow);
                _ = statusWindow.Handle; // Hidden marshal target; polling never touches WinForms from worker threads.
                monitor.Changed += OnProviderChanged;
                quotaMonitor.Changed += OnProviderChanged;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            }
            RefreshAutoStartChecks();
            RefreshStatus(false);
            timer.Start();

            if (!startedAutomatically)
                ShowWindow();
            initialization = InitializeServicesAsync(enableServices);
        }

        private ScheduleSnapshot GetSchedule(DateTimeOffset instant) => AgentSchedule.GetSnapshot(instant, holidayAdjustmentEnabled);

        private async Task InitializeServicesAsync(bool enableServices)
        {
            if (!enableServices) return;
            try { holidayAdjustmentEnabled = await store.GetHolidayAdjustmentAsync(); }
            catch (Exception error)
            {
                // Never silently apply an unconfirmed policy if persisted settings cannot be read.
                statusWindow.SetFeedback("공휴일 설정 확인 실패 · 보정 OFF: " + error.Message, true);
            }
            if (exiting || disposed) return;
            holidaySettingsReady = true;
            RefreshHolidayControls();
            RefreshStatus(false); // Initial policy load is not a service recovery or a time transition.
            monitor?.Start();
            quotaMonitor?.Start();
        }

        internal async Task SetHolidayAdjustmentAsync(bool enabled)
        {
            if (exiting || disposed || !holidaySettingsReady || changingHolidaySetting) return;
            changingHolidaySetting = true;
            RefreshHolidayControls();
            Task save = store.SetHolidayAdjustmentAsync(enabled);
            pendingWrites.Add(save);
            try
            {
                await save;
                if (exiting || disposed) return;
                holidayAdjustmentEnabled = enabled;
                int alertsBeforeRefresh = providerNotificationSerial;
                // A policy-only change must not consume a concurrently queued
                // provider incident when the schedule itself remains FULL.
                RefreshStatus(false, notifyProviders: true);
                var snapshot = GetSchedule(utcNow());
                statusWindow.SetFeedback("공휴일 보정 " + (enabled ? "ON" : "OFF") + " · 시간표에 반영됨");
                if (alertsBeforeRefresh == providerNotificationSerial)
                {
                    // Do not immediately replace an important concurrent service alert.
                    trayIcon.BalloonTipTitle = "공휴일 보정 " + (enabled ? "켜짐" : "꺼짐");
                    trayIcon.BalloonTipText = $"시간표 정책이 변경되었습니다. 다음 전환: {snapshot.NextTransitionKst:MM-dd HH:mm} KST.\nProvider 공식 상태는 별도로 확인하세요.";
                    trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                    trayIcon.ShowBalloonTip(4000);
                }
            }
            catch (Exception error)
            {
                if (!disposed) statusWindow.SetFeedback("공휴일 설정 저장 실패 · 기존 정책 유지: " + error.Message, true);
            }
            finally
            {
                pendingWrites.Remove(save);
                changingHolidaySetting = false;
                if (!disposed) RefreshHolidayControls();
            }
        }

        private void RefreshHolidayControls()
        {
            bool ready = holidaySettingsReady && !changingHolidaySetting && !exiting;
            holidayMenuItem.Checked = holidayAdjustmentEnabled;
            holidayMenuItem.Enabled = ready;
            statusWindow.SetHolidayAdjustment(holidayAdjustmentEnabled, ready);
        }

        internal void RefreshStatus(bool notifyOnChange, bool notifyProviders = false)
        {
            ScheduleSnapshot snapshot = GetSchedule(utcNow());
            bool changed = lastState.HasValue && lastState.Value != snapshot.State;
            statusWindow.UpdateStatus(snapshot);
            stateMenuItem.Text = "●  " + TrayPresentation.StateName(snapshot.State);
            stateMenuItem.ForeColor = TrayPresentation.StateColor(snapshot.State);
            countdownMenuItem.Text = "전환까지 " + StatusWindow.FormatRemaining(snapshot.Remaining);
            lastState = snapshot.State;

            if (notifyOnChange && changed)
                ShowTransitionNotification(snapshot);
            // A timer tick can run before the posted network callback. It must not
            // silently consume an official-status change and suppress its notification.
            UpdateProviderDisplay((notifyOnChange || notifyProviders) && !changed, snapshot);
        }

        private void ShowTransitionNotification(ScheduleSnapshot snapshot)
        {
            bool full = snapshot.State == AgentState.FullThrottle;
            trayIcon.BalloonTipTitle = TrayPresentation.StateName(snapshot.State) + " 시작";
            trayIcon.BalloonTipText = full
                ? (snapshot.IsHolidayExtendedFullThrottle ? "미국 공휴일이 포함된 연장 FULL 구간입니다. " : "미국 업무시간 밖입니다. ") +
                    $"다음 전환: {snapshot.NextTransitionKst:MM-dd HH:mm} KST. Provider별 공식 상태와 작업 권고도 확인하세요."
                : "새로운 대형 Agent 작업은 다음 FULL THROTTLE까지 미뤄두세요.";
            trayIcon.BalloonTipIcon = full ? ToolTipIcon.Info : ToolTipIcon.Warning;
            trayIcon.ShowBalloonTip(6000);
            TransitionNotificationRequested?.Invoke(snapshot.State);
        }

        internal void ShowWindow()
        {
            RefreshAutoStartChecks();
            RefreshStatus(true);
            statusWindow.ShowNearTray();
        }

        private void OnProviderChanged()
        {
            if (exiting || disposed || statusWindow.IsDisposed) return;
            // A poll raises Changed several times; while one UI refresh is still queued, later
            // events are covered by it because it reads the latest snapshot when it runs.
            if (Interlocked.Exchange(ref providerRefreshQueued, 1) == 1) return;
            try
            {
                statusWindow.BeginInvoke(() =>
                {
                    Volatile.Write(ref providerRefreshQueued, 0); // Before reading the snapshot.
                    if (!exiting && !disposed) RefreshStatus(true);
                });
            }
            catch (InvalidOperationException)
            {
                Volatile.Write(ref providerRefreshQueued, 0); // Window already shutting down.
            }
        }

        // Do not rely on the polling wait ending promptly after sleep; refresh at once on
        // resume, even if a pass started before suspension is still running. Raised off the
        // UI thread; RequestRefresh is thread-safe and coalesces.
        internal void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume && !exiting && !disposed)
                RefreshAll(queueWhileRefreshing: true);
        }

        internal void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable && !exiting && !disposed &&
                TryClaimNetworkRefresh(ref lastNetworkRefreshTick, Environment.TickCount64))
                RefreshAll(queueWhileRefreshing: true);
        }

        // Adapters (VPN, virtual switches) can flap, and each refresh starts both quota CLIs.
        // Raised off the UI thread; at most one network-triggered refresh per minute.
        internal static bool TryClaimNetworkRefresh(ref long lastTick, long nowTick)
        {
            long last = Interlocked.Read(ref lastTick);
            if (last != 0 && nowTick - last < (long)NetworkRefreshMinimumInterval.TotalMilliseconds) return false;
            return Interlocked.CompareExchange(ref lastTick, nowTick, last) == last;
        }

        private void RefreshAll(bool queueWhileRefreshing = false)
        {
            if (exiting || disposed) return;
            monitor?.RequestRefresh(queueWhileRefreshing);
            quotaMonitor?.RequestRefresh(queueWhileRefreshing);
        }

        private IReadOnlyList<ProviderStatus> CurrentProviders() => monitor?.Snapshot() ??
            Enum.GetValues<ProviderKind>().Select(p => ProviderStatus.Unknown(p)).ToArray();

        private void UpdateProviderDisplay(bool notify, ScheduleSnapshot schedule)
        {
            var providers = CurrentProviders();
            TrayAppearance appearance = TrayPresentation.Calculate(schedule.State, providers);
            if (currentAppearance != appearance)
            {
                ReplaceTrayIcon(appearance);
                currentAppearance = appearance;
            }
            trayIcon.Text = TrayPresentation.Tooltip(schedule, providers);
            foreach (var status in providers)
            {
                var current = RecommendationPolicy.Calculate(schedule.State, status.Status);
                if (providerNotifications.Observe(status.Provider, schedule.State, current, notify))
                {
                    bool recovered = current == Recommendation.Go;
                    trayIcon.BalloonTipTitle = status.Provider + (recovered ? " 정상화" : " 작업 권고 변경");
                    trayIcon.BalloonTipText = recovered
                        ? "관련 서비스가 정상화되었습니다. 현재 FULL THROTTLE이므로 대규모 작업 재개 가능."
                        : $"현재 FULL THROTTLE이지만 공식 서비스 문제가 있습니다. {RecommendationPolicy.Label(current)}: 새 대형 작업을 미루세요.\n{status.Reason}";
                    trayIcon.BalloonTipIcon = recovered ? ToolTipIcon.Info : ToolTipIcon.Warning;
                    trayIcon.ShowBalloonTip(6000);
                    providerNotificationSerial++;
                    ProviderNotificationRequested?.Invoke(status.Provider, current);
                }
            }
            statusWindow.UpdateProviders(providers, schedule, monitor?.IsRefreshing ?? false,
                monitor?.NextRefreshUtc, monitor?.StorageError ?? "");
            // Quotas never change official service health, schedule recommendations or tray colors.
            statusWindow.UpdateQuotas(quotaMonitor?.Snapshot() ??
                Enum.GetValues<QuotaProvider>().Select(p => new QuotaState(p)).ToArray(), utcNow());
        }

        private void OpenStatusPage(ProviderKind provider)
        {
            if (exiting) return;
            try { openStatusPage(ProviderStatusPages.For(provider)); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
            {
                statusWindow.SetFeedback("공식 페이지를 열지 못했습니다. 기본 브라우저 설정을 확인하세요.", true);
            }
        }

        private async void RecordMeasurement(ProviderKind provider, UsageEventType type, bool withNote)
        {
            if (exiting) return;
            // async void: a faulted startup task must be reported here, not rethrown.
            try { await initialization; }
            catch (Exception error)
            {
                if (!disposed) statusWindow.SetFeedback("초기화 실패로 기록하지 못했습니다: " + error.Message, true);
                return;
            }
            if (exiting || disposed) return;
            var at = utcNow().ToUniversalTime();
            var schedule = GetSchedule(at);
            var status = CurrentProviders().Single(s => s.Provider == provider);
            string note = "";
            if (withNote)
            {
                using var dialog = new MeasurementDialog(provider, type);
                if (dialog.ShowDialog() != DialogResult.OK) return;
                type = dialog.EventType;
                note = dialog.UserNote;
            }
            // Exit may start while the modal note dialog is open; ExitApplication has then
            // already collected pendingWrites, so a save started now would outlive it.
            if (exiting || disposed) return;
            var item = new UsageMeasurement(Guid.NewGuid().ToString("N"), provider, type, at,
                schedule.State, schedule.IsWeekendExtendedFullThrottle, schedule.EasternUtcOffsetMinutes,
                schedule.PacificUtcOffsetMinutes, schedule.EasternIsDst, schedule.PacificIsDst,
                schedule.SchedulePolicyVersion, status.Status, RecommendationPolicy.Calculate(schedule.State, status.Status),
                status.RelevantComponent, status.IncidentId, note, Application.ProductVersion.Split('+')[0],
                schedule.HolidayAdjustmentEnabled, schedule.IsHolidayExtendedFullThrottle, schedule.HolidayNames);
            Task save = store.AddUsageAsync(item);
            pendingWrites.Add(save);
            try
            {
                await save;
                if (!disposed)
                {
                    statusWindow.SetFeedback($"{provider} · {type} 저장됨 ({schedule.NowKst:HH:mm} KST)");
                    if (!exiting) statusWindow.ShowNearTray();
                }
            }
            catch (Exception error)
            {
                if (!disposed)
                {
                    statusWindow.SetFeedback("기록 저장 실패: " + error.Message, true);
                    if (!exiting) statusWindow.ShowNearTray();
                }
            }
            finally { pendingWrites.Remove(save); }
        }

        internal void ShowStatistics()
        {
            if (exiting) return;
            if (statisticsWindow is null || statisticsWindow.IsDisposed)
            {
                statisticsWindow = new StatisticsWindow(store, utcNow);
                statisticsWindow.FormClosed += (_, _) => statisticsWindow = null;
            }
            statisticsWindow.Show();
            statisticsWindow.Activate();
        }

        private void OnMenuAutoStartClicked(object? sender, EventArgs e)
        {
            TrySetAutoStart(autoStartMenuItem.Checked);
        }

        private void OnWindowAutoStartChanged(object? sender, EventArgs e)
        {
            TrySetAutoStart(statusWindow.AutoStartChecked);
        }

        private void TrySetAutoStart(bool enabled)
        {
            try
            {
                AutoStartManager.SetEnabled(enabled);
            }
            catch (Exception ex)
            {
                string text = "자동 실행 설정을 변경하지 못했습니다.\n\n" + ex.Message;
                if (statusWindow.Visible)
                    statusWindow.WithoutAutoHide(() => MessageBox.Show(statusWindow, text,
                        "AI Burger Clock", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                else
                    MessageBox.Show(text, "AI Burger Clock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshAutoStartChecks();
        }

        internal void RefreshAutoStartChecks()
        {
            AutoStartStatus status;
            try { status = AutoStartManager.GetStatus(); }
            catch (Exception error)
            {
                status = new(AutoStartState.Unknown, "자동 실행 설정을 읽지 못했습니다. " + error.Message);
            }
            autoStartMenuItem.Checked = status.IsEnabled;
            autoStartMenuItem.Text = status.Label;
            autoStartMenuItem.ToolTipText = status.Detail;
            statusWindow.SetAutoStartStatus(status);
        }

        private void ReplaceTrayIcon(TrayAppearance appearance)
        {
            Icon replacement = CreateStatusIcon(appearance);
            trayIcon.Icon = replacement;
            if (currentIcon != null)
                currentIcon.Dispose();
            currentIcon = replacement;
        }

        internal static Icon CreateStatusIcon(TrayAppearance appearance)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Brush brush = new SolidBrush(appearance.Color))
                    graphics.FillEllipse(brush, 1, 1, 30, 30);

                string glyph = appearance.Glyph;
                using (Font font = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    SizeF size = graphics.MeasureString(glyph, font);
                    graphics.DrawString(glyph, font, textBrush, (32 - size.Width) / 2F, (32 - size.Height) / 2F - 1F);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                        return (Icon)temporary.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        internal async void ExitApplication()
        {
            if (exiting) return;
            exiting = true;
            timer.Stop();
            trayIcon.Visible = false;
            statisticsWindow?.Close();
            try
            {
                try { await initialization; }
                catch { /* A startup failure must not block exit or surface from async void. */ }
                if (monitor is not null) await monitor.StopAsync();
                if (quotaMonitor is not null) await quotaMonitor.StopAsync();
                try { await Task.WhenAll(pendingWrites.ToArray()); }
                catch { /* Individual record handler already reports save failures. */ }
            }
            finally { ExitThread(); }
        }

        internal NotifyIcon TrayIcon => trayIcon;
        internal StatusWindow StatusWindow => statusWindow;
        internal ToolStripMenuItem AutoStartMenuItem => autoStartMenuItem;
        internal StatusMonitor? ProviderMonitor => monitor;
        internal AccountQuotaMonitor? QuotaMonitor => quotaMonitor;
        internal Task Initialization => initialization;
        internal bool HolidayAdjustmentEnabled => holidayAdjustmentEnabled;
        internal ToolStripMenuItem HolidayMenuItem => holidayMenuItem;
        internal TrayAppearance? CurrentAppearance => currentAppearance;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                if (monitor is not null)
                {
                    monitor.Changed -= OnProviderChanged;
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                    NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                }
                if (quotaMonitor is not null) quotaMonitor.Changed -= OnProviderChanged;
                quotaMonitor?.Dispose();
                monitor?.Dispose();
                ownedHttpClient?.Dispose();
                statisticsWindow?.Dispose();
                timer.Stop();
                timer.Dispose();
                trayIcon.Visible = false;
                trayIcon.Dispose();
                statusWindow.Dispose();
                menu.Dispose();
                foreach (var recordMenu in recordMenus) recordMenu.Dispose();
                stateMenuFont.Dispose();
                if (currentIcon != null)
                    currentIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
