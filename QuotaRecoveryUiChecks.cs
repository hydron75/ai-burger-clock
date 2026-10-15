using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

// Invoked only by --smoke-test, inside its real Windows message loop. All quota
// values, HTTP responses, clocks and the private database are synthetic.
internal static class QuotaRecoveryUiChecks
{
    internal static async Task RunAsync()
    {
        string testDirectory = Directory.CreateTempSubdirectory("AiBurgerClock-quota-recovery-ui-").FullName;
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(9));
        long milliseconds = 60000;
        var store = new UsageStore(Path.Combine(testDirectory, "recovery-ui.db"));
        var client = new RecoveryClient(() => now);
        using var handler = new TestStatusHttpHandler();
        using var http = new HttpClient(handler);
        TrayApplicationContext? context = null;
        bool contextDisposed = false;
        var alerts = new List<QuotaRecoveryAlert>();
        int uiThread = Environment.CurrentManagedThreadId;
        var deliveryThreads = new List<int>();

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Quota recovery UI: " + label);
            Console.WriteLine("PASS: " + label);
        }
        static async Task Wait(Func<bool> condition, string label)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Quota recovery UI: " + label);
                await Task.Delay(20);
            }
        }
        async Task FlushUi()
        {
            // The observation handler marshals to WinForms. A posted sentinel is
            // ordered after that callback, rather than guessing an arbitrary delay.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context!.StatusWindow.BeginInvoke(() => completion.SetResult());
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        async Task Refresh(QuotaProvider provider)
        {
            await context!.QuotaMonitor!.RefreshOnceAsync(provider);
            await FlushUi();
        }
        async Task Toggle(bool enabled)
        {
            context!.QuotaRecoveryMenuItem.PerformClick();
            await Wait(() => context.QuotaRecoveryMenuItem.Enabled &&
                context.QuotaRecoveryNotificationsEnabled == enabled &&
                context.QuotaRecoveryMenuItem.Checked == enabled, "persisted menu toggle completion");
            await FlushUi();
        }
        void AdvanceNotificationQueue()
        {
            milliseconds += 7000; // Fake monotonic time; no product interval is shortened.
            context!.RefreshStatus(false);
        }
        async Task SetSettingFailure(bool fail)
        {
            // Scope the deliberate failure to this test's one metadata key. The
            // normal store's background operations still use its usual SQLite path.
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = store.DatabasePath, Pooling = false
            }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = fail
                ? """
                    CREATE TRIGGER recovery_setting_test_failure BEFORE UPDATE OF Value ON AppMetadata
                    WHEN NEW.Key = 'QuotaRecoveryNotificationsEnabled'
                    BEGIN SELECT RAISE(ABORT, 'Synthetic quota recovery preference failure'); END;
                    """
                : "DROP TRIGGER IF EXISTS recovery_setting_test_failure;";
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            Check(!await store.GetQuotaRecoveryNotificationsEnabledAsync(),
                "Fresh quota recovery preference defaults OFF without a new schema");
            context = new TrayApplicationContext(true, () => now, usageStore: store,
                statusHttpClient: http, accountQuotaClient: client,
                notificationMilliseconds: () => milliseconds);
            context.QuotaRecoveryNotificationRequested += alert =>
            {
                alerts.Add(alert);
                deliveryThreads.Add(Environment.CurrentManagedThreadId);
            };
            await context.Initialization;
            await Wait(() => context.ProviderMonitor is { IsRefreshing: false } &&
                context.ProviderMonitor.Snapshot().All(state => state.LastSuccessfulCheckUtc.HasValue) &&
                context.QuotaMonitor!.Snapshot().All(state => state.LastSuccessfulCheckUtc.HasValue && !state.IsRefreshing),
                "initial fake status and quota requests complete");
            await FlushUi();
            Check(!context.QuotaRecoveryNotificationsEnabled && !context.QuotaRecoveryMenuItem.Checked &&
                context.QuotaRecoveryMenuItem.Enabled && context.QuotaRecoveryMenuItem.Text == QuotaRecoveryText.MenuLabel,
                "Quota recovery tray menu initializes enabled and unchecked with shared wording");
            Check(context.QuotaRecoveryMenuItem.ToolTipText == QuotaRecoveryText.MenuDetail && alerts.Count == 0,
                "Initial verified readings remain silent and the menu explains opt-in scope");

            int calls = client.TotalCalls;
            int statusCalls = handler.RequestCount;
            await Toggle(true);
            Check(await new UsageStore(store.DatabasePath).GetQuotaRecoveryNotificationsEnabledAsync() &&
                context.StatusWindow.FeedbackLabel.Text == QuotaRecoveryText.Saved(true),
                "Actual tray Click persists quota recovery ON and displays shared save feedback");
            Check(client.TotalCalls == calls && handler.RequestCount == statusCalls,
                "Enabling quota recovery neither polls nor alters other Provider requests");

            await SetSettingFailure(true);
            try
            {
                context.QuotaRecoveryMenuItem.PerformClick();
                await Wait(() => context.QuotaRecoveryMenuItem.Enabled &&
                    context.StatusWindow.FeedbackLabel.Text == QuotaRecoveryText.SettingSaveFailed,
                    "synthetic preference-save failure reaches feedback");
                Check(context.QuotaRecoveryNotificationsEnabled && context.QuotaRecoveryMenuItem.Checked &&
                    await store.GetQuotaRecoveryNotificationsEnabledAsync(),
                    "Failed preference save retains the prior persisted opt-in and checked menu state");
                Check(context.StatusWindow.FeedbackLabel.ForeColor == Color.Firebrick &&
                    client.TotalCalls == calls && handler.RequestCount == statusCalls,
                    "Failed preference save displays shared error feedback without adding any query");
            }
            finally { await SetSettingFailure(false); }

            client.Set(QuotaProvider.Claude, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 0, "First successful reading after opt-in seeds an exhausted baseline silently");
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 0, "Repeated exhausted success does not request a recovery balloon");
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 1 && alerts[0].Provider == QuotaProvider.Claude &&
                alerts[0].WindowIds.SequenceEqual(new[] { "session" }),
                "New accepted recovery reaches exactly one native request for its recovered window");
            Check(context.TrayIcon.BalloonTipTitle == alerts[0].Title && context.TrayIcon.BalloonTipText == alerts[0].Body &&
                context.TrayIcon.BalloonTipIcon == ToolTipIcon.Info,
                "Native recovery balloon uses the exact shared title and body with informational styling");
            Check(alerts[0].Body.Contains("다른 한도는 아직 소진 상태", StringComparison.Ordinal) &&
                !alerts[0].WindowIds.Contains("weekly"),
                "Partial recovery identifies only recovered rows without promising whole-Provider availability");
            Check(deliveryThreads.All(thread => thread == uiThread),
                "Background quota success marshals its native alert to the actual WinForms UI thread");

            calls = client.TotalCalls;
            var appearance = context.CurrentAppearance;
            var due = context.QuotaMonitor!.Snapshot().Select(state => state.NextCheckUtc).ToArray();
            context.ShowWindow();
            context.StatusWindow.QuotaButton.PerformClick();
            context.RefreshStatus(false);
            context.RefreshStatus(false);
            context.StatusWindow.Hide();
            await FlushUi();
            Check(alerts.Count == 1 && client.TotalCalls == calls && context.CurrentAppearance == appearance &&
                context.QuotaMonitor.Snapshot().Select(state => state.NextCheckUtc).SequenceEqual(due),
                "Opening, switching, hiding and ticking the quota UI cannot duplicate recovery or change polling");
            await Refresh(QuotaProvider.Claude);
            AdvanceNotificationQueue();
            Check(alerts.Count == 1, "Repeated recovered readings remain silent after native notification spacing expires");
            client.Set(QuotaProvider.Claude, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 2 && alerts[1].Sequence > alerts[0].Sequence,
                "A newly exhausted reading rearms exactly one later recovery notification");

            client.Set(QuotaProvider.Claude, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100, fail: true);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 2 && context.QuotaMonitor.Snapshot().Single(state => state.Provider == QuotaProvider.Claude).IsPrevious,
                "Synthetic lookup failure keeps previous values without masquerading as recovery");
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            AdvanceNotificationQueue();
            Check(alerts.Count == 3 && alerts[^1].Provider == QuotaProvider.Claude,
                "A fresh verified response after failure still compares the prior live exhausted baseline");

            var officialAlerts = new List<ProviderKind>();
            context.ProviderNotificationRequested += (provider, _) => officialAlerts.Add(provider);
            handler.OpenAiStatus = OfficialStatus.Degraded;
            await context.ProviderMonitor!.RefreshOnceAsync();
            await FlushUi();
            string priorityTitle = context.TrayIcon.BalloonTipTitle;
            Check(officialAlerts.Contains(ProviderKind.OpenAI),
                "Synthetic official-status change still requests its original service notification");
            client.Set(QuotaProvider.Claude, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 3 && context.TrayIcon.BalloonTipTitle == priorityTitle,
                "A concurrent quota recovery queues without replacing the higher-priority service balloon");
            AdvanceNotificationQueue();
            Check(alerts.Count == 4 && context.TrayIcon.BalloonTipTitle == alerts[^1].Title,
                "Queued quota recovery drains after the existing service notification spacing");

            foreach (var provider in new[] { QuotaProvider.Codex, QuotaProvider.Gemini })
            {
                client.Set(provider, used: 100, weeklyUsed: 100);
                await Refresh(provider);
                client.Set(provider, used: 25, weeklyUsed: 25);
                await Refresh(provider);
            }
            Check(alerts.Count == 4, "Independent Provider recoveries wait while a preceding native balloon is active");
            AdvanceNotificationQueue();
            Check(alerts.Count == 5 && alerts[^1].WindowIds.SequenceEqual(new[] { "session", "weekly" }),
                "Multiple recovered windows in one response share one Provider notification");
            AdvanceNotificationQueue();
            Check(alerts.Count == 6 && alerts.TakeLast(2).Select(alert => alert.Provider).Distinct().Count() == 2 &&
                alerts.TakeLast(2).All(alert => alert.Provider is QuotaProvider.Codex or QuotaProvider.Gemini),
                "Separate Provider recoveries drain individually without losing either queued notification");

            handler.OpenAiStatus = OfficialStatus.Operational;
            await context.ProviderMonitor.RefreshOnceAsync();
            await FlushUi();
            client.Set(QuotaProvider.Claude, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 6, "A synthetic recovery waits behind an official recovery balloon before a later failure");
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100, fail: true);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 6 && context.QuotaMonitor.Snapshot().Single(state => state.Provider == QuotaProvider.Claude).IsPrevious,
                "A later synthetic failure marks queued recovery values previous while notification spacing is active");
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            AdvanceNotificationQueue();
            Check(alerts.Count == 6,
                "A transient failure discards queued recovery instead of reviving it after a later successful snapshot");

            handler.OpenAiStatus = OfficialStatus.Degraded;
            await context.ProviderMonitor.RefreshOnceAsync();
            await FlushUi();
            client.Set(QuotaProvider.Claude, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 6, "A synthetic recovery remains queued before the same row changes its window duration");
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100, sessionMinutes: 360);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 6 && context.QuotaMonitor.Snapshot().Single(state => state.Provider == QuotaProvider.Claude)
                    .Reading!.Windows.Single(window => window.Id == "session").WindowMinutes == 360,
                "A queued row accepts a different synthetic duration without creating a new recovery");
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            AdvanceNotificationQueue();
            Check(alerts.Count == 6 && context.QuotaMonitor.Snapshot().Single(state => state.Provider == QuotaProvider.Claude)
                    .Reading!.Windows.Single(window => window.Id == "session").WindowMinutes == 300,
                "Changing and restoring a window duration cannot revive an invalidated recovery queued during notification spacing");

            // The preceding invalidation checks consumed their spacing. Start a
            // fresh priority window so this opt-out really cancels a pending item.
            handler.OpenAiStatus = OfficialStatus.Operational;
            await context.ProviderMonitor.RefreshOnceAsync();
            await FlushUi();

            client.Set(QuotaProvider.Claude, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            client.Set(QuotaProvider.Claude, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Claude);
            Check(alerts.Count == 6, "Synthetic recovery is pending before the user opts out");
            calls = client.TotalCalls;
            await Toggle(false);
            Check(!await store.GetQuotaRecoveryNotificationsEnabledAsync() && client.TotalCalls == calls &&
                context.StatusWindow.FeedbackLabel.Text == QuotaRecoveryText.Saved(false),
                "Actual OFF Click persists opt-out without querying or changing the save feedback contract");
            AdvanceNotificationQueue();
            Check(alerts.Count == 6, "Opt-out clears queued recovery rather than delivering it on the next timer tick");
            await Toggle(true);
            await Refresh(QuotaProvider.Claude);
            AdvanceNotificationQueue();
            Check(alerts.Count == 6, "Re-enabling seeds a fresh successful baseline without replaying old recovery");

            client.Set(QuotaProvider.Codex, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Codex);

            client.Set(QuotaProvider.Gemini, used: 25, weeklyUsed: 25, block: true);
            Task pending = context.QuotaMonitor.RefreshOnceAsync(QuotaProvider.Gemini);
            await Wait(() => context.QuotaMonitor.Snapshot().Single(state => state.Provider == QuotaProvider.Gemini).IsRefreshing,
                "cancellable fake quota request starts");
            await context.QuotaMonitor.StopAsync();
            bool cancelled = false;
            try { await pending; }
            catch (OperationCanceledException) { cancelled = true; }
            await FlushUi();
            Check(cancelled && alerts.Count == 6,
                "Stopping a pending synthetic quota request cancels it without creating an observation or notification");
            await context.ProviderMonitor.StopAsync();
            context.Dispose();
            contextDisposed = true;
            Check(context.StatusWindow.IsDisposed && alerts.Count == 6,
                "Disposing the private smoke host releases its window and notification subscriptions without ending the outer loop");

            var restartedClient = new RecoveryClient(() => now);
            var restartedAlerts = new List<QuotaRecoveryAlert>();
            context = new TrayApplicationContext(true, () => now, usageStore: new UsageStore(store.DatabasePath),
                statusHttpClient: http, accountQuotaClient: restartedClient,
                notificationMilliseconds: () => milliseconds);
            contextDisposed = false;
            context.QuotaRecoveryNotificationRequested += restartedAlerts.Add;
            await context.Initialization;
            await Wait(() => context.ProviderMonitor is { IsRefreshing: false } &&
                context.ProviderMonitor.Snapshot().All(state => state.LastSuccessfulCheckUtc.HasValue) &&
                context.QuotaMonitor!.Snapshot().All(state => state.LastSuccessfulCheckUtc.HasValue && !state.IsRefreshing && !state.IsPrevious),
                "restarted fake Provider initial success completes");
            await FlushUi();
            AdvanceNotificationQueue();
            Check(context.QuotaRecoveryNotificationsEnabled && context.QuotaRecoveryMenuItem.Checked &&
                context.QuotaRecoveryMenuItem.Enabled,
                "Restarting the private host reloads the persisted opt-in without changing the default policy");
            Check(restartedAlerts.Count == 0 && alerts.Count == 6,
                "Restart cache exhaustion followed by first fresh recovery seeds silently without replaying old-host alerts");
            restartedClient.Set(QuotaProvider.Codex, used: 100, weeklyUsed: 100);
            await Refresh(QuotaProvider.Codex);
            restartedClient.Set(QuotaProvider.Codex, used: 25, weeklyUsed: 100);
            await Refresh(QuotaProvider.Codex);
            Check(restartedAlerts.Count == 1 && alerts.Count == 6,
                "The restarted host rearms a new live recovery without invoking disposed-host subscriptions");
            await context.QuotaMonitor!.StopAsync();
            await context.ProviderMonitor!.StopAsync();
            context.Dispose();
            contextDisposed = true;
            Check(context.StatusWindow.IsDisposed,
                "The restarted private host also stops and disposes without exiting the outer smoke loop");
        }
        finally
        {
            if (context is not null && !contextDisposed)
            {
                if (context.QuotaMonitor is not null) await context.QuotaMonitor.StopAsync();
                if (context.ProviderMonitor is not null) await context.ProviderMonitor.StopAsync();
                context.Dispose();
            }
            // This exact fresh temporary directory is owned only by this check.
            try { Directory.Delete(testDirectory, recursive: true); }
            catch (IOException) { Console.WriteLine("INFO: Private quota recovery smoke data retained in a temporary folder."); }
        }
    }

    private sealed class RecoveryClient(Func<DateTimeOffset> clock) : IAccountQuotaClient
    {
        private sealed record Response(double Used, double WeeklyUsed, bool Fail = false, bool Block = false, int SessionMinutes = 300);
        private readonly object sync = new();
        private readonly Dictionary<QuotaProvider, Response> responses = Enum.GetValues<QuotaProvider>()
            .ToDictionary(provider => provider, _ => new Response(25, 25));
        private int calls;
        internal int TotalCalls => Volatile.Read(ref calls);
        internal void Set(QuotaProvider provider, double used, double weeklyUsed, bool fail = false, bool block = false,
            int sessionMinutes = 300)
        {
            lock (sync) responses[provider] = new(used, weeklyUsed, fail, block, sessionMinutes);
        }

        public async Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Response response;
            lock (sync) response = responses[provider];
            cancellationToken.ThrowIfCancellationRequested();
            if (response.Block) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            if (response.Fail) throw new IOException("Synthetic quota recovery smoke failure");
            return new(provider,
            [
                new("session", "5시간", response.Used, clock().AddHours(2), response.SessionMinutes),
                new("weekly", "주간", response.WeeklyUsed, clock().AddDays(2), 10080)
            ]);
        }
    }
}
