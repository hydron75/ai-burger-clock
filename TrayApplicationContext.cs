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
        private readonly ToolStripMenuItem quotaRecoveryMenuItem;
        private readonly ToolStripMenuItem quotaEarlyResetMenuItem;
        private readonly QuotaRecoveryNotifications quotaRecoveryNotifications = new();
        private readonly QuotaEarlyResetNotifications quotaEarlyResetNotifications = new();
        private sealed record PendingQuotaAlert(QuotaRecoveryAlert? Recovery, QuotaEarlyResetAlert? EarlyReset,
            QuotaObservation Observation);
        private readonly Dictionary<QuotaProvider, PendingQuotaAlert> pendingQuotaAlerts = new();
        // Latest official recommendation per Provider, never an unbounded event history.
        private readonly Dictionary<ProviderKind, ProviderAlert> pendingProviderAlerts = new();
        private long providerNotificationNotBefore;
        private long providerBalloonExpires;
        private readonly Func<long> notificationMilliseconds;
        private readonly Action<QuotaNotificationAlert> showQuotaNotification;
        private readonly Action<NotifyIcon, int> showBalloonNotification;
        private long quotaNotificationNotBefore;
        private bool quotaRecoveryEnabled;
        private bool quotaRecoverySettingsReady;
        private bool changingQuotaRecoverySetting;
        private int quotaNotificationEpoch;
        private bool quotaEarlyResetEnabled;
        private bool quotaEarlyResetSettingsReady;
        private bool changingQuotaEarlyResetSetting;
        private int quotaEarlyResetNotificationEpoch;
        private Icon? currentIcon;
        private TrayAppearance? currentAppearance;
        private bool disposed;
        private bool exiting;
        private readonly UsageStore store;
        private readonly HttpClient? ownedHttpClient; // Null when the caller supplied the client.
        private readonly StatusMonitor? monitor;
        private readonly AccountQuotaMonitor? quotaMonitor;
        private StatisticsWindow? statisticsWindow;
        private readonly HashSet<Task> pendingWrites = new();
        private readonly StatusTicker statusTicker;
        private int providerRefreshQueued;
        private readonly NetworkRefreshScheduler? networkRefresh;
        private readonly Action<Uri> openStatusPage;
        private volatile bool holidayAdjustmentEnabled;
        private bool holidaySettingsReady;
        private bool changingHolidaySetting;
        private readonly Task initialization;

        internal event Action<AgentState>? TransitionNotificationRequested;
        internal event Action<ProviderKind, Recommendation>? ProviderNotificationRequested;
        internal event Action<QuotaRecoveryAlert>? QuotaRecoveryNotificationRequested;
        internal event Action<QuotaNotificationAlert>? QuotaNotificationRequested;

        // Normal runs use UTC now; the explicit smoke test supplies a clock without changing Windows time.
        public TrayApplicationContext(bool startedAutomatically, Func<DateTimeOffset>? utcNow = null, bool enableServices = true, UsageStore? usageStore = null, HttpClient? statusHttpClient = null, Action<Uri>? openStatusPage = null, IAccountQuotaClient? accountQuotaClient = null, Func<long>? notificationMilliseconds = null, Action<QuotaNotificationAlert>? quotaNotificationSink = null, Action<NotifyIcon, int>? balloonNotificationSink = null)
        {
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            this.notificationMilliseconds = notificationMilliseconds ?? (() => Environment.TickCount64);
            if (quotaNotificationSink is not null) showQuotaNotification = quotaNotificationSink;
            else
            {
                var toast = new WindowsQuotaToastSender(WindowsQuotaToastIdentity.Production);
                var delivery = new WindowsQuotaNotificationDelivery(toast.Send, ShowNativeQuotaNotification);
                toast.Failed = delivery.RecordToastFailure;
                showQuotaNotification = delivery.Send;
            }
            // Native smoke injects both transports. Production behavior is independent
            // of service enablement and retains the original balloon durations.
            showBalloonNotification = balloonNotificationSink ?? ((icon, timeout) => icon.ShowBalloonTip(timeout));
            this.openStatusPage = openStatusPage ?? ProviderStatusPages.Open;
            store = usageStore ?? new UsageStore();
            statusTicker = new StatusTicker(WindowsProviderNames.Provider, warn: new WindowsWarningLog().Write);
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
            quotaRecoveryMenuItem = new ToolStripMenuItem(QuotaRecoveryText.MenuLabel)
            {
                Enabled = false, ToolTipText = QuotaRecoveryText.MenuDetail
            };
            quotaRecoveryMenuItem.Click += async (_, _) => await SetQuotaRecoveryNotificationsAsync(!quotaRecoveryEnabled);
            quotaEarlyResetMenuItem = new ToolStripMenuItem(QuotaEarlyResetText.MenuLabel)
            {
                Enabled = false, ToolTipText = QuotaEarlyResetText.MenuDetail
            };
            quotaEarlyResetMenuItem.Click += async (_, _) => await SetQuotaEarlyResetNotificationsAsync(!quotaEarlyResetEnabled);
            ToolStripMenuItem exitItem = new ToolStripMenuItem("종료");

            showItem.Click += delegate { ShowWindow(); };
            autoStartMenuItem.Click += OnMenuAutoStartClicked;
            holidayMenuItem.Click += async (_, _) => await SetHolidayAdjustmentAsync(holidayMenuItem.Checked);
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(stateMenuItem);
            menu.Items.Add(countdownMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(showItem);
            var refreshItem = new ToolStripMenuItem("상태·한도 새로 고침") { ToolTipText = "공식 서비스 상태와 ChatGPT (Work/Codex)·Claude·Gemini (Antigravity) 계정 한도를 함께 갱신" };
            refreshItem.Click += (_, _) => RefreshAll();
            menu.Items.Add(refreshItem);
            var recordRoot = new ToolStripMenuItem("사용 경험 기록");
            foreach (var provider in Enum.GetValues<ProviderKind>())
            {
                var recordMenu = StatusWindow.CreateRecordingMenu(provider, RecordMeasurement);
                recordMenus.Add(recordMenu);
                var recordItem = new ToolStripMenuItem(WindowsProviderNames.Provider(provider)) { DropDown = recordMenu };
                recordRoot.DropDownItems.Add(recordItem);
            }
            menu.Items.Add(recordRoot);
            var statisticsItem = new ToolStripMenuItem("Statistics");
            statisticsItem.Click += (_, _) => ShowStatistics();
            menu.Items.Add(statisticsItem);
            menu.Items.Add(quotaRecoveryMenuItem);
            menu.Items.Add(quotaEarlyResetMenuItem);
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
            // The ShowBalloonTip timeout argument does not prove native display completion.
            trayIcon.BalloonTipClosed += (_, _) => providerBalloonExpires = 0;
            trayIcon.BalloonTipClicked += (_, _) => providerBalloonExpires = 0;

            timer = new Timer { Interval = 1000 };
            timer.Tick += delegate { RefreshStatus(true); };

            if (enableServices)
            {
                if (statusHttpClient is null)
                {
                    ownedHttpClient = ProviderStatusClient.CreateHttpClient();
                }
                HttpClient httpClient = statusHttpClient ?? ownedHttpClient!;
                monitor = new StatusMonitor(new ProviderStatusClient(httpClient, this.utcNow), store, this.utcNow, scheduleAt: GetSchedule);
                quotaMonitor = new AccountQuotaMonitor(accountQuotaClient ?? new AccountQuotaClient(), store, this.utcNow);
                _ = statusWindow.Handle; // Hidden marshal target; polling never touches WinForms from worker threads.
                monitor.Changed += OnProviderChanged;
                quotaMonitor.Changed += OnProviderChanged;
                quotaMonitor.Succeeded += OnQuotaSucceeded;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                networkRefresh = new NetworkRefreshScheduler(() => RefreshAll(queueWhileRefreshing: true));
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
                statusWindow.SetFeedback(FeedbackText.HolidayReadFailed(error.Message), true);
            }
            if (exiting || disposed) return;
            try { quotaRecoveryEnabled = await store.GetQuotaRecoveryNotificationsEnabledAsync(); }
            catch
            {
                quotaRecoveryEnabled = false;
                statusWindow.SetFeedback(QuotaRecoveryText.SettingReadFailed, true);
            }
            if (exiting || disposed) return;
            quotaRecoverySettingsReady = true;
            RefreshQuotaRecoveryMenu();
            try { quotaEarlyResetEnabled = await store.GetQuotaEarlyResetNotificationsEnabledAsync(); }
            catch
            {
                quotaEarlyResetEnabled = false;
                statusWindow.SetFeedback(QuotaEarlyResetText.SettingReadFailed, true);
            }
            if (exiting || disposed) return;
            quotaEarlyResetSettingsReady = true;
            RefreshQuotaEarlyResetMenu();
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
                // A policy-only change must not consume a concurrently queued
                // provider incident when the schedule itself remains FULL.
                TickResult result = RefreshStatus(TickReason.PolicyChanged);
                var snapshot = GetSchedule(utcNow());
                statusWindow.SetFeedback(FeedbackText.HolidaySaved(enabled));
                if (result.ProviderAlerts.Count == 0 && pendingProviderAlerts.Count == 0 &&
                    notificationMilliseconds() >= providerBalloonExpires)
                {
                    // Do not immediately replace an important concurrent service alert.
                    (trayIcon.BalloonTipTitle, trayIcon.BalloonTipText) = FeedbackText.HolidayNotification(enabled, snapshot);
                    trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                    showBalloonNotification(trayIcon, 4000);
                    DeferQuotaNotifications(4500);
                    DeferProviderNotifications(4500);
                }
            }
            catch (Exception error)
            {
                if (!disposed) statusWindow.SetFeedback(FeedbackText.HolidaySaveFailed(error.Message), true);
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
            => RefreshStatus(notifyOnChange ? TickReason.Timer :
                notifyProviders ? TickReason.PolicyChanged : TickReason.Initial);

        private TickResult RefreshStatus(TickReason reason)
        {
            TickResult result = statusTicker.Tick(GetSchedule(utcNow()), CurrentProviders(), reason);
            ScheduleSnapshot snapshot = result.Schedule;
            statusWindow.UpdateStatus(snapshot);
            stateMenuItem.Text = "●  " + TrayPresentation.StateName(snapshot.State);
            stateMenuItem.ForeColor = TrayPresentation.StateColor(snapshot.State);
            countdownMenuItem.Text = "전환까지 " + StatusWindow.FormatRemaining(snapshot.Remaining);
            if (result.Transition is { } transition)
                ShowTransitionNotification(snapshot, transition);
            // Apply the decision in the existing order: schedule UI, transition,
            // icon/tooltip, provider alerts, then provider/quota UI.
            if (reason == TickReason.Initial) pendingProviderAlerts.Clear();
            UpdateProviderDisplay(result);
            ShowPendingQuotaNotification();
            return result;
        }

        private void ShowTransitionNotification(ScheduleSnapshot snapshot, (string Title, string Body) transition)
        {
            bool full = snapshot.State == AgentState.FullThrottle;
            (trayIcon.BalloonTipTitle, trayIcon.BalloonTipText) = transition;
            trayIcon.BalloonTipIcon = full ? ToolTipIcon.Info : ToolTipIcon.Warning;
            showBalloonNotification(trayIcon, 6000);
            DeferQuotaNotifications(6500);
            DeferProviderNotifications(6500);
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
                    if (!exiting && !disposed) RefreshStatus(TickReason.ProviderChanged);
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

        // Raised off the UI thread; the shared scheduler waits for DHCP/DNS and limits flapping adapters.
        internal void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable && !exiting && !disposed)
                networkRefresh?.OnNetworkAvailable();
        }

        private void RefreshAll(bool queueWhileRefreshing = false)
        {
            if (exiting || disposed) return;
            monitor?.RequestRefresh(queueWhileRefreshing);
            quotaMonitor?.RequestRefresh(queueWhileRefreshing);
        }

        private IReadOnlyList<ProviderStatus> CurrentProviders() => monitor?.Snapshot() ??
            Enum.GetValues<ProviderKind>().Select(p => ProviderStatus.Unknown(p)).ToArray();

        private void UpdateProviderDisplay(TickResult result)
        {
            if (result.AppearanceChanged)
            {
                ReplaceTrayIcon(result.Appearance);
                currentAppearance = result.Appearance;
            }
            trayIcon.Text = TrayPresentation.Tooltip(result.Schedule, result.Providers, WindowsProviderNames.Provider);
            foreach (ProviderAlert alert in result.ProviderAlerts)
                pendingProviderAlerts[alert.Provider] = alert;
            ShowPendingProviderNotification(result);
            statusWindow.UpdateProviders(result.Providers, result.Schedule, monitor?.IsRefreshing ?? false,
                monitor?.NextRefreshUtc, monitor?.StorageError ?? "");
            // Quotas never change official service health, schedule recommendations or tray colors.
            var recent = Enum.GetValues<QuotaProvider>()
                .Select(provider => (Provider: provider, Alert: quotaEarlyResetNotifications.Recent(provider)))
                .Where(item => item.Alert is not null).ToDictionary(item => item.Provider, item => item.Alert!);
            statusWindow.UpdateQuotas(quotaMonitor?.Snapshot() ??
                Enum.GetValues<QuotaProvider>().Select(p => new QuotaState(p)).ToArray(), utcNow(), recent);
        }

        private void DeferProviderNotifications(int milliseconds) =>
            providerNotificationNotBefore = Math.Max(providerNotificationNotBefore, notificationMilliseconds() + milliseconds);

        private void ShowPendingProviderNotification(TickResult result)
        {
            if (exiting || disposed) return;
            if (result.Schedule.State != AgentState.FullThrottle)
            {
                pendingProviderAlerts.Clear();
                return;
            }
            // Drop a stale recommendation before waiting or delivery. If an incident's
            // detail changes at the same severity, generate its text from the latest state.
            foreach (var provider in pendingProviderAlerts.Keys.ToArray())
            {
                var state = result.Providers.FirstOrDefault(state => state.Provider == provider);
                if (state is null || RecommendationPolicy.Calculate(result.Schedule.State, state.Status) !=
                    pendingProviderAlerts[provider].Recommendation)
                    pendingProviderAlerts.Remove(provider);
            }
            long now = notificationMilliseconds();
            if (now < providerNotificationNotBefore || now < providerBalloonExpires) return;
            foreach (var provider in pendingProviderAlerts.Keys.OrderBy(provider => provider).ToArray())
            {
                var alert = pendingProviderAlerts[provider];
                pendingProviderAlerts.Remove(provider);
                var state = result.Providers.Single(state => state.Provider == provider);
                (trayIcon.BalloonTipTitle, trayIcon.BalloonTipText) = TrayPresentation.ProviderNotification(
                    provider, alert.Recommendation, state.Reason, WindowsProviderNames.Provider);
                trayIcon.BalloonTipIcon = alert.Recovered ? ToolTipIcon.Info : ToolTipIcon.Warning;
                // Wait for Closed/Clicked, but don't block forever if Windows suppresses
                // the balloon and emits no event. This is a fallback, not a display guarantee.
                providerBalloonExpires = now + 30000;
                DeferProviderNotifications(6500);
                showBalloonNotification(trayIcon, 6000);
                DeferQuotaNotifications(6500);
                ProviderNotificationRequested?.Invoke(provider, alert.Recommendation);
                return; // Never submit multiple official balloons in one UI tick.
            }
        }

        private void OnQuotaSucceeded(QuotaObservation observation)
        {
            if (exiting || disposed || statusWindow.IsDisposed)
            {
                quotaMonitor?.RecordEarlyResetDecision(observation, "Excluded", "HostUnavailable");
                return;
            }
            int epoch = Volatile.Read(ref quotaNotificationEpoch);
            int earlyResetEpoch = Volatile.Read(ref quotaEarlyResetNotificationEpoch);
            try
            {
                // Unlike Changed, success observations must not be coalesced: every accepted
                // reading is needed to distinguish exhaustion and recovery in the right order.
                statusWindow.BeginInvoke(() =>
                {
                    if (exiting || disposed)
                    {
                        quotaMonitor?.RecordEarlyResetDecision(observation, "Excluded", "HostUnavailable");
                        return;
                    }
                    bool recoveryCurrent = epoch == Volatile.Read(ref quotaNotificationEpoch);
                    bool earlyResetCurrent = earlyResetEpoch == Volatile.Read(ref quotaEarlyResetNotificationEpoch);
                    if (!earlyResetCurrent)
                        quotaMonitor?.RecordEarlyResetDecision(observation, "Excluded", "SettingChangedBeforeEvaluation");
                    if (!recoveryCurrent && !earlyResetCurrent) return;
                    // Invalidate at every success, not just at delivery. If a window disappears
                    // or changes period and later returns, an old queued alert must stay gone.
                    if (pendingQuotaAlerts.TryGetValue(observation.Provider, out var pending))
                        KeepPending(observation.Provider, CurrentPending(pending, observation.Reading,
                            observation.CheckedAtUtc, observation.ContinuityId));
                    var recovery = recoveryCurrent ? quotaRecoveryNotifications.Observe(observation,
                        quotaRecoveryEnabled && quotaRecoverySettingsReady && !changingQuotaRecoverySetting) : null;
                    QuotaEarlyResetAlert? earlyReset = null;
                    if (earlyResetCurrent)
                    {
                        QuotaEarlyResetEvaluation evaluation = quotaEarlyResetNotifications.Evaluate(observation,
                            quotaEarlyResetEnabled && quotaEarlyResetSettingsReady && !changingQuotaEarlyResetSetting);
                        earlyReset = evaluation.Alert;
                        quotaMonitor?.RecordEarlyResetDecision(observation, earlyReset is null ? "Excluded" : "Alert", evaluation.Reason);
                    }
                    if (recovery is not null || earlyReset is not null)
                        pendingQuotaAlerts[observation.Provider] = new(recovery, earlyReset, observation);
                    // Existing schedule/official alerts always apply before a quota alert.
                    RefreshStatus(TickReason.ProviderChanged);
                });
            }
            catch (InvalidOperationException)
            {
                // Marshal target was closed during shutdown; this observation was not evaluated.
                quotaMonitor?.RecordEarlyResetDecision(observation, "Excluded", "HostUnavailable");
            }
        }

        private void DeferQuotaNotifications(int milliseconds) =>
            quotaNotificationNotBefore = Math.Max(quotaNotificationNotBefore, notificationMilliseconds() + milliseconds);

        private void ShowPendingQuotaNotification()
        {
            if (exiting || disposed) return;
            if (pendingProviderAlerts.Count > 0 || notificationMilliseconds() < providerBalloonExpires)
                return; // Protect the whole official batch, including its last native balloon.
            // At most one pending summary per Provider (three entries), never an unbounded history.
            foreach (var provider in pendingQuotaAlerts.Keys.OrderBy(provider => provider).ToArray())
            {
                var pending = pendingQuotaAlerts[provider];
                var state = quotaMonitor?.Snapshot().FirstOrDefault(state => state.Provider == provider);
                // A failed later query, disappearance, re-exhaustion or elapsed reset invalidates
                // a queued summary. Never show a past recovery as a current availability claim.
                if (state is null || state.IsPrevious || state.Reading is null ||
                    state.LastSuccessfulCheckUtc is not { } success || success < pending.Observation.CheckedAtUtc)
                {
                    pendingQuotaAlerts.Remove(provider);
                    continue;
                }
                // Wait for an in-flight read rather than sending against a partially
                // completed attempt whose eventual failure would break continuity.
                if (state.IsRefreshing) continue;
                pending = CurrentPending(pending, state.Reading, utcNow(), state.ContinuityId);
                KeepPending(provider, pending);
                if (pending is null) continue;
                var alert = QuotaNotificationText.Combine(pending.Recovery, pending.EarlyReset);
                if (alert is null) { pendingQuotaAlerts.Remove(provider); continue; }
                if (notificationMilliseconds() < quotaNotificationNotBefore) continue;
                pendingQuotaAlerts.Remove(provider);
                try
                {
                    showQuotaNotification(alert);
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    statusWindow.SetFeedback(alert.EarlyReset is not null
                        ? QuotaEarlyResetText.DeliveryFailed : QuotaRecoveryText.DeliveryFailed, true);
                    // The observation was already consumed. A display failure must not retry
                    // this same alert on every tick or interrupt quota polling.
                    DeferQuotaNotifications(6500);
                    return;
                }
                DeferQuotaNotifications(6500);
                if (alert.Recovery is { } recovery) QuotaRecoveryNotificationRequested?.Invoke(recovery);
                QuotaNotificationRequested?.Invoke(alert);
                return;
            }
        }

        private void ShowNativeQuotaNotification(QuotaNotificationAlert alert)
        {
            (trayIcon.BalloonTipTitle, trayIcon.BalloonTipText) = (alert.Title, alert.Body);
            trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            showBalloonNotification(trayIcon, 6000);
        }

        private PendingQuotaAlert? CurrentPending(PendingQuotaAlert pending, QuotaReading reading,
            DateTimeOffset now, long continuityId)
        {
            if (now < pending.Observation.CheckedAtUtc) return null;
            var recovery = quotaRecoveryEnabled && quotaRecoverySettingsReady && !changingQuotaRecoverySetting &&
                pending.Recovery is { } recovered && QuotaPendingAlertPolicy.RecoveryStillCurrent(recovered, pending.Observation.Reading, reading, now)
                ? recovered : null;
            var earlyReset = quotaEarlyResetEnabled && quotaEarlyResetSettingsReady && !changingQuotaEarlyResetSetting &&
                pending.EarlyReset is { } early && QuotaPendingAlertPolicy.EarlyResetStillCurrent(early, pending.Observation.Reading,
                    reading, now, continuityId) ? early : null;
            return recovery is null && earlyReset is null ? null : pending with { Recovery = recovery, EarlyReset = earlyReset };
        }

        private void KeepPending(QuotaProvider provider, PendingQuotaAlert? pending)
        {
            if (pending is null) pendingQuotaAlerts.Remove(provider);
            else pendingQuotaAlerts[provider] = pending;
        }

        private void DropPendingKind(bool earlyReset)
        {
            foreach (var provider in pendingQuotaAlerts.Keys.ToArray())
            {
                var pending = pendingQuotaAlerts[provider];
                pending = earlyReset ? pending with { EarlyReset = null } : pending with { Recovery = null };
                KeepPending(provider, pending.Recovery is null && pending.EarlyReset is null ? null : pending);
            }
        }

        internal async Task SetQuotaRecoveryNotificationsAsync(bool enabled)
        {
            if (exiting || disposed || !quotaRecoverySettingsReady || changingQuotaRecoverySetting) return;
            changingQuotaRecoverySetting = true;
            Interlocked.Increment(ref quotaNotificationEpoch);
            DropPendingKind(earlyReset: false);
            quotaRecoveryNotifications.Reset();
            RefreshQuotaRecoveryMenu();
            Task save = store.SetQuotaRecoveryNotificationsEnabledAsync(enabled);
            pendingWrites.Add(save);
            try
            {
                await save;
                if (exiting || disposed) return;
                quotaRecoveryEnabled = enabled;
                statusWindow.SetFeedback(QuotaRecoveryText.Saved(enabled));
            }
            catch
            {
                if (!disposed) statusWindow.SetFeedback(QuotaRecoveryText.SettingSaveFailed, true);
            }
            finally
            {
                pendingWrites.Remove(save);
                // Enabling never replays a recovery observed while disabled or while saving.
                Interlocked.Increment(ref quotaNotificationEpoch);
                DropPendingKind(earlyReset: false);
                quotaRecoveryNotifications.Reset();
                changingQuotaRecoverySetting = false;
                if (!disposed) RefreshQuotaRecoveryMenu();
            }
        }

        private void RefreshQuotaRecoveryMenu()
        {
            quotaRecoveryMenuItem.Checked = quotaRecoveryEnabled;
            quotaRecoveryMenuItem.Enabled = quotaRecoverySettingsReady && !changingQuotaRecoverySetting && !exiting;
        }

        internal async Task SetQuotaEarlyResetNotificationsAsync(bool enabled)
        {
            if (exiting || disposed || !quotaEarlyResetSettingsReady || changingQuotaEarlyResetSetting) return;
            changingQuotaEarlyResetSetting = true;
            Interlocked.Increment(ref quotaEarlyResetNotificationEpoch);
            DropPendingKind(earlyReset: true);
            quotaEarlyResetNotifications.Reset();
            RefreshQuotaEarlyResetMenu();
            Task save = store.SetQuotaEarlyResetNotificationsEnabledAsync(enabled);
            pendingWrites.Add(save);
            try
            {
                await save;
                if (exiting || disposed) return;
                quotaEarlyResetEnabled = enabled;
                statusWindow.SetFeedback(QuotaEarlyResetText.Saved(enabled));
            }
            catch
            {
                if (!disposed) statusWindow.SetFeedback(QuotaEarlyResetText.SettingSaveFailed, true);
            }
            finally
            {
                pendingWrites.Remove(save);
                Interlocked.Increment(ref quotaEarlyResetNotificationEpoch);
                DropPendingKind(earlyReset: true);
                quotaEarlyResetNotifications.Reset();
                changingQuotaEarlyResetSetting = false;
                if (!disposed) RefreshQuotaEarlyResetMenu();
            }
        }

        private void RefreshQuotaEarlyResetMenu()
        {
            quotaEarlyResetMenuItem.Checked = quotaEarlyResetEnabled;
            quotaEarlyResetMenuItem.Enabled = quotaEarlyResetSettingsReady && !changingQuotaEarlyResetSetting && !exiting;
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
                if (!disposed) statusWindow.SetFeedback(FeedbackText.RecordStartupFailed(error.Message), true);
                return;
            }
            if (exiting || disposed) return;
            var at = utcNow().ToUniversalTime();
            var schedule = GetSchedule(at);
            var status = CurrentProviders().Single(s => s.Provider == provider);
            var item = UsageMeasurementFactory.Capture(provider, type, schedule, status, Application.ProductVersion.Split('+')[0]);
            string? truncatedNoteFeedback = null;
            if (withNote)
            {
                using var dialog = new MeasurementDialog(provider, type);
                if (dialog.ShowDialog() != DialogResult.OK) return;
                type = dialog.EventType;
                string note = dialog.UserNote;
                item = UsageMeasurementFactory.WithNote(item, type, note);
                if (item.UserNote.Length < note.Length)
                    truncatedNoteFeedback = FeedbackText.NoteTruncated(item.UserNote.Length);
            }
            // Exit may start while the modal note dialog is open; ExitApplication has then
            // already collected pendingWrites, so a save started now would outlive it.
            if (exiting || disposed) return;
            Task save = store.AddUsageAsync(item);
            pendingWrites.Add(save);
            try
            {
                await save;
                if (!disposed)
                {
                    statusWindow.SetFeedback(truncatedNoteFeedback ?? FeedbackText.RecordSaved(provider, type, schedule));
                    if (!exiting) statusWindow.ShowNearTray();
                }
            }
            catch (Exception error)
            {
                if (!disposed)
                {
                    statusWindow.SetFeedback(FeedbackText.RecordFailed(error.Message), true);
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
            pendingQuotaAlerts.Clear();
            pendingProviderAlerts.Clear();
            Interlocked.Increment(ref quotaNotificationEpoch);
            Interlocked.Increment(ref quotaEarlyResetNotificationEpoch);
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
        internal bool QuotaRecoveryNotificationsEnabled => quotaRecoveryEnabled;
        internal ToolStripMenuItem QuotaRecoveryMenuItem => quotaRecoveryMenuItem;
        internal bool QuotaEarlyResetNotificationsEnabled => quotaEarlyResetEnabled;
        internal ToolStripMenuItem QuotaEarlyResetMenuItem => quotaEarlyResetMenuItem;
        internal QuotaEarlyResetAlert? RecentEarlyReset(QuotaProvider provider) =>
            exiting || disposed ? null : quotaEarlyResetNotifications.Recent(provider);

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
                networkRefresh?.Dispose();
                if (quotaMonitor is not null)
                {
                    quotaMonitor.Changed -= OnProviderChanged;
                    quotaMonitor.Succeeded -= OnQuotaSucceeded;
                }
                pendingQuotaAlerts.Clear();
                pendingProviderAlerts.Clear();
                Interlocked.Increment(ref quotaNotificationEpoch);
                Interlocked.Increment(ref quotaEarlyResetNotificationEpoch);
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
