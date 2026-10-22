using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AiBurgerClock;

// Native smoke only: every clock, quota, service response and database is synthetic.
// The real message loop is retained so background success dispatch and actual tray
// menu clicks exercise the host contract rather than calling the pure engine directly.
internal static class QuotaEarlyResetUiChecks
{
    internal static async Task RunAsync()
    {
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-early-reset-ui-").FullName;
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(9));
        long milliseconds = 60000;
        var store = new UsageStore(Path.Combine(directory, "early-reset-ui.db"));
        var client = new EarlyResetClient(Rows(25, 25));
        using var handler = new TestStatusHttpHandler();
        using var http = new HttpClient(handler);
        TrayApplicationContext? context = null;
        bool disposed = false;
        var nativeAlerts = new List<QuotaNotificationAlert>();
        var submitted = new List<QuotaNotificationAlert>();
        var balloonRequests = new List<(string Title, int Timeout)>();
        int nativeBalloonEvents = 0;
        var recoveryAlerts = new List<QuotaRecoveryAlert>();
        var deliveredThreads = new List<int>();
        var officialAlerts = new List<ProviderKind>();
        int uiThread = Environment.CurrentManagedThreadId;

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Quota early reset UI: " + label);
            Console.WriteLine("PASS: " + label);
        }
        static async Task Wait(Func<bool> condition, string label)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Quota early reset UI: " + label);
                await Task.Delay(20);
            }
        }
        QuotaWindow[] Rows(double used, double weeklyUsed, DateTimeOffset? modelReset = null,
            DateTimeOffset? weeklyReset = null, int modelMinutes = 10080) =>
        [
            new("synthetic-weekly-model", modelMinutes == 300 ? "5시간" : "주간 합성 모델", used,
                modelReset ?? (modelMinutes == 300 ? now.AddHours(2) : now.AddDays(2)), modelMinutes),
            new("synthetic-weekly", "주간", weeklyUsed, weeklyReset ?? now.AddDays(2), 10080)
        ];
        async Task FlushUi()
        {
            // Success is posted before RefreshOnceAsync returns. This sentinel waits
            // for that actual message ordering without guessing a startup/click delay.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context!.StatusWindow.BeginInvoke(() => completion.SetResult());
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        async Task Refresh(QuotaProvider provider)
        {
            await Task.Run(() => context!.QuotaMonitor!.RefreshOnceAsync(provider));
            await FlushUi();
        }
        async Task ToggleEarly(bool enabled)
        {
            if (context!.QuotaEarlyResetNotificationsEnabled == enabled) return;
            context.QuotaEarlyResetMenuItem.PerformClick();
            await Wait(() => context.QuotaEarlyResetMenuItem.Enabled &&
                context.QuotaEarlyResetNotificationsEnabled == enabled && context.QuotaEarlyResetMenuItem.Checked == enabled,
                "persisted early reset menu toggle completes");
            await FlushUi();
        }
        async Task ToggleRecovery(bool enabled)
        {
            await context!.SetQuotaRecoveryNotificationsAsync(enabled);
            await FlushUi();
        }
        void Drain()
        {
            // A fake service send has no real Closed callback; model normal
            // completion instead of treating elapsed spacing as native closure.
            ProviderBalloonChecks.CompleteNativeBalloon(context!.TrayIcon);
            milliseconds += 7000; // Fake notification spacing, never a longer real test wait.
            context.RefreshStatus(false);
        }
        void Step()
        {
            now = now.AddMinutes(1);
            milliseconds += 60000;
        }
        async Task InitialRequestsComplete()
        {
            await context!.Initialization;
            await Wait(() => context.ProviderMonitor is { IsRefreshing: false } &&
                context.ProviderMonitor.Snapshot().All(state => state.LastSuccessfulCheckUtc.HasValue) &&
                context.QuotaMonitor!.Snapshot().All(state => state.LastSuccessfulCheckUtc.HasValue &&
                    !state.IsRefreshing && !state.IsPrevious), "private initial status and quota requests finish");
            await FlushUi();
        }
        async Task SetSaveFailure(bool fail)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = store.DatabasePath, Pooling = false
            }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = fail ? """
                CREATE TRIGGER early_reset_ui_save_failure BEFORE UPDATE OF Value ON AppMetadata
                WHEN NEW.Key = 'QuotaEarlyResetNotificationsEnabled'
                BEGIN SELECT RAISE(ABORT, 'Synthetic early reset preference failure'); END;
                """ : "DROP TRIGGER IF EXISTS early_reset_ui_save_failure;";
            await command.ExecuteNonQueryAsync();
        }
        JsonElement[] Decisions()
        {
            string path = Path.Combine(directory, "logs", "quota-early-reset-decisions-v1.jsonl");
            if (!File.Exists(path)) return [];
            var entries = new List<JsonElement>();
            foreach (string line in File.ReadLines(path))
            {
                using JsonDocument entry = JsonDocument.Parse(line);
                entries.Add(entry.RootElement.Clone());
            }
            return entries.ToArray();
        }
        bool HasDecision(QuotaProvider provider, string outcome, string? reason = null)
        {
            JsonElement[] entries = Decisions().Where(entry =>
                entry.GetProperty("Provider").GetString() == provider.ToString()).ToArray();
            if (entries.Length == 0) return false;
            JsonElement latest = entries[^1];
            return latest.GetProperty("Succeeded").GetBoolean() &&
                latest.GetProperty("Outcome").GetString() == outcome &&
                latest.GetProperty("Reason").GetString() != "NotEvaluated" &&
                (reason is null || latest.GetProperty("Reason").GetString() == reason);
        }
        async Task BlockBehindOfficialAlert()
        {
            int count = officialAlerts.Count;
            handler.OpenAiStatus = handler.OpenAiStatus == OfficialStatus.Operational
                ? OfficialStatus.Degraded : OfficialStatus.Operational;
            await context!.ProviderMonitor!.RefreshOnceAsync();
            await FlushUi();
            Check(officialAlerts.Count > count, "A real synthetic service change takes priority before the early reset banner");
        }
        async Task<QuotaWindow[]> QueueWeeklyEarlyReset()
        {
            await ToggleRecovery(false);
            await ToggleEarly(false);
            await ToggleEarly(true);
            client.Set(QuotaProvider.Claude, Rows(60, 60));
            await Refresh(QuotaProvider.Claude);
            await BlockBehindOfficialAlert();
            int count = nativeAlerts.Count;
            string priorityTitle = context!.TrayIcon.BalloonTipTitle;
            now = now.AddMinutes(1); // Keep monotonic notification spacing blocked on purpose.
            QuotaWindow[] changed = Rows(0, 60, now.AddHours(3), now.AddDays(2));
            client.Set(QuotaProvider.Claude, changed);
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == count && context.TrayIcon.BalloonTipTitle == priorityTitle &&
                context.RecentEarlyReset(QuotaProvider.Claude) is { Windows.Count: 1 },
                "A model-specific weekly early reset is recorded but queues behind the higher priority service balloon");
            return changed;
        }

        try
        {
            context = new TrayApplicationContext(true, () => now, usageStore: store,
                statusHttpClient: http, accountQuotaClient: client,
                notificationMilliseconds: () => milliseconds, quotaNotificationSink: submitted.Add,
                balloonNotificationSink: (icon, timeout) => balloonRequests.Add((icon.BalloonTipTitle, timeout)));
            context.TrayIcon.BalloonTipShown += (_, _) => nativeBalloonEvents++;
            context.QuotaNotificationRequested += alert =>
            {
                nativeAlerts.Add(alert);
                deliveredThreads.Add(Environment.CurrentManagedThreadId);
            };
            context.QuotaRecoveryNotificationRequested += recoveryAlerts.Add;
            context.ProviderNotificationRequested += (provider, _) => officialAlerts.Add(provider);
            await InitialRequestsComplete();
            Check(!context.QuotaEarlyResetNotificationsEnabled && !context.QuotaEarlyResetMenuItem.Checked &&
                context.QuotaEarlyResetMenuItem.Enabled && context.QuotaEarlyResetMenuItem.Text == QuotaEarlyResetText.MenuLabel,
                "Early reset tray preference starts available and OFF with shared wording");
            Check(context.QuotaEarlyResetMenuItem.ToolTipText == QuotaEarlyResetText.MenuDetail && nativeAlerts.Count == 0,
                "Initial private live successes remain silent and the early reset menu explains opt-in scope");
            Check(Enum.GetValues<QuotaProvider>().All(provider => HasDecision(provider, "Excluded", "Disabled")),
                "Windows records the OFF early reset decision for each private successful Provider query");
            var menu = context.TrayIcon.ContextMenuStrip!;
            Check(menu.Items.IndexOf(context.QuotaEarlyResetMenuItem) == menu.Items.IndexOf(context.QuotaRecoveryMenuItem) + 1 &&
                menu.Items.IndexOf(context.QuotaEarlyResetMenuItem) < menu.Items.IndexOf(context.AutoStartMenuItem),
                "Early reset menu follows recovery and precedes auto start without changing the existing menu order");

            int calls = client.TotalCalls;
            int serviceCalls = handler.RequestCount;
            await ToggleEarly(true);
            Check(await new UsageStore(store.DatabasePath).GetQuotaEarlyResetNotificationsEnabledAsync() &&
                !await store.GetQuotaRecoveryNotificationsEnabledAsync() && !context.QuotaRecoveryMenuItem.Checked &&
                context.StatusWindow.FeedbackLabel.Text == QuotaEarlyResetText.Saved(true),
                "Actual early reset menu Click persists its independent opt-in with shared feedback");
            Check(client.TotalCalls == calls && handler.RequestCount == serviceCalls,
                "Changing early reset opt-in does not trigger quota or service queries");
            await SetSaveFailure(true);
            try
            {
                context.QuotaEarlyResetMenuItem.PerformClick();
                await Wait(() => context.QuotaEarlyResetMenuItem.Enabled &&
                    context.StatusWindow.FeedbackLabel.Text == QuotaEarlyResetText.SettingSaveFailed,
                    "early reset preference failure reaches the actual feedback label");
                Check(context.QuotaEarlyResetNotificationsEnabled && context.QuotaEarlyResetMenuItem.Checked &&
                    await store.GetQuotaEarlyResetNotificationsEnabledAsync() && !context.QuotaRecoveryNotificationsEnabled,
                    "Failed early reset save retains the prior checked persisted selection and recovery opt-out");
                Check(context.StatusWindow.FeedbackLabel.ForeColor == Color.Firebrick && client.TotalCalls == calls &&
                    handler.RequestCount == serviceCalls,
                    "Failed early reset save shows shared error styling without any additional query");
            }
            finally { await SetSaveFailure(false); }

            DateTimeOffset unchangedReset = now.AddDays(4.6);
            client.Set(QuotaProvider.Claude, Rows(17, 17, unchangedReset, unchangedReset));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == 0, "First success after early reset opt-in seeds both windows silently");
            Step();
            client.Set(QuotaProvider.Claude, Rows(0, 0, unchangedReset, unchangedReset));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == 1 && nativeAlerts[^1] is { Provider: QuotaProvider.Claude, Recovery: null, EarlyReset.Windows.Count: 2 },
                "A seventeen-to-zero weekly decrease before the unchanged reset coalesces both weekly rows into one native banner");
            Check(HasDecision(QuotaProvider.Claude, "Alert", "Alert"),
                "The UI decision updates the same completed diagnostic row to an alert candidate rather than claiming OS delivery");
            Check(recoveryAlerts.Count == 0 && submitted.Count == 1 && submitted[^1] == nativeAlerts[^1],
                "Early-only notification submits the exact shared payload without inventing a recovery event or native OS delivery");
            Check(!nativeAlerts[^1].Body.Contains('%') && !nativeAlerts[^1].Body.Contains("KST", StringComparison.Ordinal) &&
                !nativeAlerts[^1].Body.Contains("synthetic-", StringComparison.Ordinal) &&
                nativeAlerts[^1].Body.Contains("예정보다 일찍 초기화된 것으로 보입니다", StringComparison.Ordinal),
                "Early reset banner uses cautious wording and omits local percentages timestamps and internal identities");
            Check(deliveredThreads.All(thread => thread == uiThread) && client.ReadThreads.Any(thread => thread != uiThread),
                "Actual background quota success dispatches its native balloon on the Windows UI thread");
            QuotaEarlyResetAlert firstRecent = context.RecentEarlyReset(QuotaProvider.Claude)!;
            QuotaState state = context.QuotaMonitor!.Snapshot().Single(item => item.Provider == QuotaProvider.Claude);
            string detail = QuotaPanelModel.Section(state, now, firstRecent).Detail;
            Check(detail.Contains(QuotaEarlyResetText.Detail(firstRecent, now), StringComparison.Ordinal) &&
                detail.Contains("사용 17% → 0%", StringComparison.Ordinal) &&
                detail.Contains("이전 리셋 예정", StringComparison.Ordinal) && detail.Contains("다음 리셋 예정", StringComparison.Ordinal),
                "Provider detail includes the session-only early reset values and both reset times through the shared model");
            var providerBox = context.StatusWindow.QuotaView.Controls.OfType<Panel>()
                .Single(box => box.Name == "ClaudeQuotaBox");
            var heading = providerBox.Controls.OfType<Label>().First();
            Check(context.StatusWindow.QuotaView.DetailFor(heading) == detail && providerBox.AccessibleDescription == detail,
                "Actual Windows hover and accessibility binding receive the shared session history without adding a quota row");
            await Refresh(QuotaProvider.Claude);
            Drain();
            Check(nativeAlerts.Count == 1 && context.RecentEarlyReset(QuotaProvider.Claude) == firstRecent,
                "Repeated accepted Refresh and UI tick cannot duplicate the same early reset or append history");

            client.Set(QuotaProvider.Claude, Rows(60, 60));
            await Refresh(QuotaProvider.Claude);
            Step();
            client.Set(QuotaProvider.Claude, Rows(5, 5, now.AddHours(3), now.AddDays(3)));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == 2 && context.RecentEarlyReset(QuotaProvider.Claude) is { } latest &&
                latest.Sequence > firstRecent.Sequence && latest.Windows.All(window => window.UsedPercent == 5) &&
                context.RecentEarlyReset(QuotaProvider.Gemini) is null,
                "A later independent early reset replaces the latest one for only its Provider");

            int rulesBefore = nativeAlerts.Count;
            client.Set(QuotaProvider.Claude, Rows(60, 60, modelMinutes: 300));
            await Refresh(QuotaProvider.Claude);
            Step();
            client.Set(QuotaProvider.Claude, Rows(0, 60, modelMinutes: 300));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == rulesBefore,
                "A five-hour decrease is excluded while an unchanged weekly window remains silent");
            Check(HasDecision(QuotaProvider.Claude, "Excluded"),
                "A five-hour-only decrease records a completed exclusion instead of remaining unevaluated");
            Step();
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == rulesBefore,
                "An unchanged zero-to-zero five-hour reading and unchanged weekly reading do not infer a reset");
            Step();
            client.Set(QuotaProvider.Claude, Rows(1, 61, modelMinutes: 300));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == rulesBefore,
                "Increasing usage never creates an early reset notification");

            DateTimeOffset longReset = now.AddDays(4.6);
            client.Set(QuotaProvider.Claude, Rows(60, 60, longReset, longReset));
            await Refresh(QuotaProvider.Claude);
            Step();
            client.Set(QuotaProvider.Claude, Rows(59, 59, longReset, longReset));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == rulesBefore + 1 && nativeAlerts[^1].EarlyReset is { Windows.Count: 2 } small &&
                small.Windows.All(window => window.PreviousUsedPercent == 60 && window.UsedPercent == 59 &&
                    window.PreviousResetUtc == window.ResetUtc),
                "A strict one-point weekly decrease above ten percent alerts without a changed reset timestamp");
            client.Set(QuotaProvider.Claude, Rows(60, 60, longReset, longReset));
            await Refresh(QuotaProvider.Claude);
            now = now.AddHours(2);
            milliseconds += 2 * 60 * 60000;
            client.Set(QuotaProvider.Claude, Rows(17, 17, longReset, longReset));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == rulesBefore + 2 && nativeAlerts[^1].EarlyReset is { Windows.Count: 2 },
                "More than ninety minutes between successes does not suppress a weekly decrease before its scheduled reset");
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = store.DatabasePath, Pooling = false
            }.ToString()))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM AppMetadata WHERE Key LIKE '%EarlyReset%';";
                Check((long)(await command.ExecuteScalarAsync())! == 1,
                    "Only the early reset preference is persisted and the private observation history is not written to metadata");
            }

            calls = client.TotalCalls;
            await ToggleRecovery(true);
            await ToggleEarly(false);
            await ToggleEarly(true);
            Check(context.QuotaRecoveryNotificationsEnabled && await store.GetQuotaRecoveryNotificationsEnabledAsync() &&
                await store.GetQuotaEarlyResetNotificationsEnabledAsync() && client.TotalCalls == calls,
                "Recovery and early reset independently persist ON without polling during either setting change");
            int before = nativeAlerts.Count;
            int recoveryBefore = recoveryAlerts.Count;
            client.Set(QuotaProvider.Claude, Rows(100, 100));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == before, "Re-enabling early reset consumes its first successful baseline silently");
            Step();
            client.Set(QuotaProvider.Claude, Rows(0, 0, now.AddHours(3), now.AddDays(3)));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == before + 1 && nativeAlerts[^1] is { Recovery: not null, EarlyReset: not null } &&
                recoveryAlerts.Count == recoveryBefore + 1 && nativeAlerts[^1].EarlyReset!.Windows.Count == 2 &&
                nativeAlerts[^1].Recovery!.WindowIds.Count == 2,
                "Simultaneous exhausted recovery and early reset share exactly one Provider native banner");

            await ToggleEarly(false);
            before = nativeAlerts.Count;
            client.Set(QuotaProvider.Claude, Rows(100, 100));
            await Refresh(QuotaProvider.Claude);
            Step();
            client.Set(QuotaProvider.Claude, Rows(25, 25));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == before + 1 && nativeAlerts[^1] is { Recovery: not null, EarlyReset: null } &&
                context.QuotaRecoveryNotificationsEnabled && !context.QuotaEarlyResetNotificationsEnabled,
                "Recovery-only mode retains the original recovery behavior when early reset is disabled");

            await ToggleRecovery(false);
            await ToggleEarly(true);
            before = nativeAlerts.Count;
            client.Set(QuotaProvider.Codex, Rows(60, 60));
            await Refresh(QuotaProvider.Codex);
            Step();
            client.Set(QuotaProvider.Codex, Rows(5, 5, now.AddHours(3), now.AddDays(3)));
            await Refresh(QuotaProvider.Codex);
            Check(nativeAlerts.Count == before + 1 && nativeAlerts[^1] is
                { Provider: QuotaProvider.Codex, Recovery: null, EarlyReset: not null } &&
                !await store.GetQuotaRecoveryNotificationsEnabledAsync() && await store.GetQuotaEarlyResetNotificationsEnabledAsync(),
                "Early reset remains independently active for another Provider while recovery is OFF");

            await ToggleRecovery(true);
            client.Set(QuotaProvider.Claude, Rows(100, 100));
            await Refresh(QuotaProvider.Claude);
            QuotaEarlyResetAlert? historyBeforeFailure = context.RecentEarlyReset(QuotaProvider.Claude);
            before = nativeAlerts.Count;
            Step();
            client.Set(QuotaProvider.Claude, Rows(0, 0, now.AddHours(3), now.AddDays(3)), fail: true);
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == before && context.QuotaMonitor.Snapshot()
                .Single(item => item.Provider == QuotaProvider.Claude).IsPrevious,
                "A failed synthetic query marks values previous without creating a native notification");
            Step();
            client.Set(QuotaProvider.Claude, Rows(0, 0, now.AddHours(3), now.AddDays(3)));
            await Refresh(QuotaProvider.Claude);
            Check(nativeAlerts.Count == before + 1 && nativeAlerts[^1] is { Recovery: not null, EarlyReset: null } &&
                context.RecentEarlyReset(QuotaProvider.Claude) == historyBeforeFailure,
                "Interrupted continuity suppresses inferred early reset while preserving legitimate failure-to-recovery alerts");

            QuotaWindow[] stillCurrentWeekly = await QueueWeeklyEarlyReset();
            before = nativeAlerts.Count;
            now = now.AddMinutes(91);
            client.Set(QuotaProvider.Claude, stillCurrentWeekly);
            await Refresh(QuotaProvider.Claude);
            Drain();
            Check(nativeAlerts.Count == before + 1 && nativeAlerts[^1] is { Recovery: null, EarlyReset.Windows.Count: 1 } &&
                submitted[^1] == nativeAlerts[^1],
                "A freshly revalidated queued weekly early reset survives ninety minutes and drains once after service notification spacing");
            Drain();
            Check(nativeAlerts.Count == before + 1, "An already delivered queued early reset cannot repeat on another UI tick");

            await QueueWeeklyEarlyReset();
            before = nativeAlerts.Count;
            client.Set(QuotaProvider.Claude, Rows(0, 60), fail: true);
            await Refresh(QuotaProvider.Claude);
            client.Set(QuotaProvider.Claude, Rows(0, 60));
            await Refresh(QuotaProvider.Claude);
            Drain();
            Check(nativeAlerts.Count == before,
                "A later failure invalidates a queued early reset and a subsequent success cannot revive it");

            QuotaWindow[] queued = await QueueWeeklyEarlyReset();
            before = nativeAlerts.Count;
            client.Set(QuotaProvider.Claude, queued.Select(window => window.Id == "synthetic-weekly-model"
                ? window with { WindowMinutes = 360 } : window).ToArray());
            await Refresh(QuotaProvider.Claude);
            client.Set(QuotaProvider.Claude, queued);
            await Refresh(QuotaProvider.Claude);
            Drain();
            Check(nativeAlerts.Count == before,
                "Changing and restoring a queued row duration cannot revive the earlier inferred reset notification");

            await QueueWeeklyEarlyReset();
            before = nativeAlerts.Count;
            calls = client.TotalCalls;
            await ToggleEarly(false);
            Drain();
            Check(nativeAlerts.Count == before && client.TotalCalls == calls &&
                !await store.GetQuotaEarlyResetNotificationsEnabledAsync(),
                "Opt-out persists independently and cancels queued early reset without issuing any query");
            await ToggleEarly(true);
            client.Set(QuotaProvider.Claude, Rows(0, 60));
            await Refresh(QuotaProvider.Claude);
            Drain();
            Check(nativeAlerts.Count == before, "Re-enabling after a queued opt-out does not replay previous session history");

            queued = await QueueWeeklyEarlyReset();
            before = nativeAlerts.Count;
            client.Set(QuotaProvider.Claude, queued.Select(window => window.Id == "synthetic-weekly-model"
                ? window with { UsedPercent = 100 } : window).ToArray());
            await Refresh(QuotaProvider.Claude);
            Drain();
            Check(nativeAlerts.Count == before,
                "Re-exhausting a queued weekly row invalidates the earlier inferred reset banner");

            queued = await QueueWeeklyEarlyReset();
            before = nativeAlerts.Count;
            now = queued.Single(window => window.Id == "synthetic-weekly-model").ResetsAtUtc!.Value.AddSeconds(1);
            Drain();
            Check(nativeAlerts.Count == before,
                "A queued early reset is discarded after its affected reset time expires rather than presented as current");

            client.Set(QuotaProvider.Gemini, Rows(5, 5), block: true);
            Task pending = Task.Run(() => context.QuotaMonitor.RefreshOnceAsync(QuotaProvider.Gemini));
            await Wait(() => context.QuotaMonitor.Snapshot().Single(item => item.Provider == QuotaProvider.Gemini).IsRefreshing,
                "a cancellable background synthetic Gemini request starts");
            await context.QuotaMonitor.StopAsync();
            bool canceled = false;
            try { await pending; }
            catch (OperationCanceledException) { canceled = true; }
            await FlushUi();
            Check(canceled && nativeAlerts.Count == before,
                "Canceling a pending request cannot create an early reset observation or native banner");
            await context.ProviderMonitor!.StopAsync();
            context.Dispose();
            disposed = true;
            Check(context.StatusWindow.IsDisposed, "Private early reset host stops without exiting the outer Windows smoke loop");
            JsonElement[] decisions = Decisions();
            Check(decisions.Length > 0 && decisions.Select(entry => entry.GetProperty("AttemptId").GetString())
                .Distinct(StringComparer.Ordinal).Count() == decisions.Length,
                "Updating UI candidate and exclusion reasons leaves exactly one local diagnostic line per query attempt");

            // The scenarios above deliberately leave nonzero usage in several cached
            // weekly windows. Prepare this duplicate-only restart fixture explicitly:
            // otherwise a fresh zero reading is a legitimate cached early reset now.
            bool zeroCachesReady = true;
            foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
            {
                await store.SaveQuotaAsync(new(1, new(provider, Rows(0, 0)), now.AddMinutes(-1), []));
                QuotaCache? cached = await store.ReadQuotaAsync(provider);
                zeroCachesReady &= cached is not null && cached.SuccessfulAtUtc < now &&
                    cached.Reading.Windows.All(window => window.UsedPercent == 0);
            }
            Check(zeroCachesReady,
                "The private duplicate-restart fixture has a verified earlier zero-use success cache for every Provider");
            int oldHostAlerts = nativeAlerts.Count;
            var restartedAlerts = new List<QuotaNotificationAlert>();
            var restartedClient = new EarlyResetClient(Rows(0, 0));
            context = new TrayApplicationContext(true, () => now, usageStore: new UsageStore(store.DatabasePath),
                statusHttpClient: http, accountQuotaClient: restartedClient,
                notificationMilliseconds: () => milliseconds, quotaNotificationSink: submitted.Add,
                balloonNotificationSink: (icon, timeout) => balloonRequests.Add((icon.BalloonTipTitle, timeout)));
            context.TrayIcon.BalloonTipShown += (_, _) => nativeBalloonEvents++;
            disposed = false;
            context.QuotaNotificationRequested += restartedAlerts.Add;
            await InitialRequestsComplete();
            Drain();
            Check(context.QuotaEarlyResetNotificationsEnabled && context.QuotaEarlyResetMenuItem.Checked &&
                !context.QuotaRecoveryNotificationsEnabled,
                "Restart reloads the independently persisted early reset preference without enabling recovery");
            Check(Enum.GetValues<QuotaProvider>().All(provider => context.RecentEarlyReset(provider) is null) &&
                restartedAlerts.Count == 0 && nativeAlerts.Count == oldHostAlerts,
                "Application restart clears local early reset history and unchanged zero-use cached values do not create notifications");
            await context.QuotaMonitor!.StopAsync();
            await context.ProviderMonitor!.StopAsync();
            context.Dispose();
            disposed = true;
            Check(context.StatusWindow.IsDisposed, "Restarted private early reset host also disposes without ending the smoke loop");

            DateTimeOffset cachedWeeklyReset = now.AddDays(4.6);
            await store.SaveQuotaAsync(new(1,
                new(QuotaProvider.Codex, [new("synthetic-weekly", "주간", 17, cachedWeeklyReset, 10080)]), now, []));
            now = now.AddMinutes(1);
            var cacheResetClient = new EarlyResetClient(Rows(0, 0));
            cacheResetClient.Set(QuotaProvider.Codex,
                [new("synthetic-weekly", "주간", 0, cachedWeeklyReset, 10080)]);
            var cacheResetAlerts = new List<QuotaNotificationAlert>();
            context = new TrayApplicationContext(true, () => now, usageStore: new UsageStore(store.DatabasePath),
                statusHttpClient: http, accountQuotaClient: cacheResetClient,
                notificationMilliseconds: () => milliseconds, quotaNotificationSink: submitted.Add,
                balloonNotificationSink: (icon, timeout) => balloonRequests.Add((icon.BalloonTipTitle, timeout)));
            context.TrayIcon.BalloonTipShown += (_, _) => nativeBalloonEvents++;
            disposed = false;
            context.QuotaNotificationRequested += cacheResetAlerts.Add;
            await InitialRequestsComplete();
            Drain();
            Check(context.RecentEarlyReset(QuotaProvider.Codex) is { Windows.Count: 1 } fromCache &&
                fromCache.Windows[0] is { PreviousUsedPercent: 17, UsedPercent: 0 } &&
                fromCache.Windows[0].PreviousResetUtc == cachedWeeklyReset &&
                Enum.GetValues<QuotaProvider>().Where(provider => provider != QuotaProvider.Codex)
                    .All(provider => context.RecentEarlyReset(provider) is null),
                "The first live Windows success after restart compares the cached weekly seventeen-to-zero decrease before its reset");
            Check(cacheResetAlerts.Count == 1 && cacheResetAlerts[0] is
                { Provider: QuotaProvider.Codex, Recovery: null, EarlyReset.Windows.Count: 1 } &&
                context.QuotaEarlyResetNotificationsEnabled && !context.QuotaRecoveryNotificationsEnabled &&
                cacheResetClient.TotalCalls == Enum.GetValues<QuotaProvider>().Length,
                "A cached weekly early reset delivers one native request from normal startup reads without changing recovery or polling again");
            Check(HasDecision(QuotaProvider.Codex, "Alert", "Alert"),
                "The first-live cached early reset candidate is recorded in its completed local diagnostic row");
            await context.QuotaMonitor!.StopAsync();
            await context.ProviderMonitor!.StopAsync();
            context.Dispose();
            disposed = true;
            Check(context.StatusWindow.IsDisposed,
                "The cached-decrease restart host disposes normally and leaves the outer smoke loop running");
            Check(submitted.Count == nativeAlerts.Count + restartedAlerts.Count + cacheResetAlerts.Count &&
                submitted.Count > 0 && balloonRequests.Count > 0 && nativeBalloonEvents == 0,
                "Every early-reset smoke host captures quota and service requests without native notification delivery");
        }
        finally
        {
            if (context is not null && !disposed)
            {
                if (context.QuotaMonitor is not null) await context.QuotaMonitor.StopAsync();
                if (context.ProviderMonitor is not null) await context.ProviderMonitor.StopAsync();
                context.Dispose();
            }
            // Only this exact newly created temporary directory belongs to this check.
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { Console.WriteLine("INFO: Private early reset smoke data remains in its temporary folder."); }
        }
        await RunDeliveryFailureAsync();
    }

    private static async Task RunDeliveryFailureAsync()
    {
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-early-reset-delivery-").FullName;
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(9));
        long milliseconds = 60000;
        var store = new UsageStore(Path.Combine(directory, "delivery-failure.db"));
        QuotaWindow[] Rows(double used, DateTimeOffset reset) =>
            [new("synthetic-weekly", "주간", used, reset, 10080)];
        var client = new EarlyResetClient(Rows(60, now.AddHours(2)));
        using var handler = new TestStatusHttpHandler();
        using var http = new HttpClient(handler);
        TrayApplicationContext? context = null;
        bool disposed = false;
        bool rejectDelivery = true;
        int attempts = 0;
        int balloonRequests = 0;
        int nativeBalloonEvents = 0;
        var delivered = new List<QuotaNotificationAlert>();

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Quota early reset delivery UI: " + label);
            Console.WriteLine("PASS: " + label);
        }
        async Task FlushUi()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context!.StatusWindow.BeginInvoke(() => completion.SetResult());
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        async Task Refresh()
        {
            await Task.Run(() => context!.QuotaMonitor!.RefreshOnceAsync(QuotaProvider.Claude));
            await FlushUi();
        }

        try
        {
            context = new TrayApplicationContext(true, () => now, usageStore: store,
                statusHttpClient: http, accountQuotaClient: client,
                notificationMilliseconds: () => milliseconds,
                quotaNotificationSink: _ =>
                {
                    attempts++;
                    if (rejectDelivery) throw new InvalidOperationException("Synthetic native notification delivery failure");
                }, balloonNotificationSink: (_, _) => balloonRequests++);
            context.TrayIcon.BalloonTipShown += (_, _) => nativeBalloonEvents++;
            context.QuotaNotificationRequested += delivered.Add;
            await context.Initialization;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (context.ProviderMonitor is not { IsRefreshing: false } ||
                context.ProviderMonitor.Snapshot().Any(state => !state.LastSuccessfulCheckUtc.HasValue) ||
                context.QuotaMonitor!.Snapshot().Any(state => !state.LastSuccessfulCheckUtc.HasValue || state.IsRefreshing))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Private delivery failure host startup");
                await Task.Delay(20);
            }
            await FlushUi();
            Check(!context.QuotaEarlyResetNotificationsEnabled && attempts == 0 && delivered.Count == 0,
                "Injected delivery failure host starts silent with early reset opt-in OFF");
            int calls = client.TotalCalls;
            int serviceCalls = handler.RequestCount;
            var otherProviderChecks = context.QuotaMonitor.Snapshot().Where(state => state.Provider != QuotaProvider.Claude)
                .Select(state => state.CheckedAtUtc).ToArray();
            await context.SetQuotaEarlyResetNotificationsAsync(true);
            await FlushUi();
            Check(await store.GetQuotaEarlyResetNotificationsEnabledAsync() &&
                !await store.GetQuotaRecoveryNotificationsEnabledAsync() && client.TotalCalls == calls &&
                handler.RequestCount == serviceCalls,
                "Delivery test opt-in persists independently without querying or changing recovery preferences");
            await Refresh();
            Check(attempts == 0 && delivered.Count == 0 && context.RecentEarlyReset(QuotaProvider.Claude) is null,
                "First successful opt-in reading seeds silently even with a rejecting notification sink");

            now = now.AddMinutes(1);
            milliseconds += 60000;
            client.Set(QuotaProvider.Claude, Rows(10, now.AddHours(3)));
            await Refresh();
            Check(attempts == 1 && delivered.Count == 0 &&
                context.StatusWindow.FeedbackLabel.Text == QuotaEarlyResetText.DeliveryFailed &&
                context.StatusWindow.FeedbackLabel.ForeColor == Color.Firebrick,
                "Synthetic native delivery failure is attempted once and reports shared error feedback without a delivered event");
            QuotaEarlyResetAlert? failedDeliveryHistory = context.RecentEarlyReset(QuotaProvider.Claude);
            Check(failedDeliveryHistory is { Windows.Count: 1 } && failedDeliveryHistory.Windows[0].UsedPercent == 10,
                "A confirmed early reset remains in local session detail even when its native banner cannot be delivered");
            calls = client.TotalCalls;
            for (int tick = 0; tick < 3; tick++)
            {
                milliseconds += 7000;
                context.RefreshStatus(false);
                await FlushUi();
            }
            Check(attempts == 1 && delivered.Count == 0 && client.TotalCalls == calls,
                "Repeated UI ticks do not retry a consumed failed delivery or issue another quota query");

            client.Set(QuotaProvider.Claude, Rows(60, now.AddHours(2)));
            await Refresh();
            Check(attempts == 1 && context.QuotaMonitor.Snapshot().Single(state => state.Provider == QuotaProvider.Claude)
                is { IsRefreshing: false, IsPrevious: false, Error: "" },
                "A native delivery failure does not interrupt later successful quota refreshes");
            now = now.AddMinutes(1);
            milliseconds += 60000;
            rejectDelivery = false;
            client.Set(QuotaProvider.Claude, Rows(5, now.AddHours(3)));
            await Refresh();
            Check(attempts == 2 && delivered.Count == 1 && delivered[0] is { Recovery: null, EarlyReset: not null },
                "A new legitimate early reset can deliver once after a prior native delivery failure");
            Check(context.RecentEarlyReset(QuotaProvider.Claude) is { } recent &&
                recent.Sequence > failedDeliveryHistory!.Sequence && recent.Windows[0].UsedPercent == 5,
                "A later confirmed event replaces the single local history entry after delivery recovers");
            Check(context.QuotaEarlyResetNotificationsEnabled && !context.QuotaRecoveryNotificationsEnabled &&
                context.QuotaMonitor.Snapshot().Where(state => state.Provider != QuotaProvider.Claude)
                    .Select(state => state.CheckedAtUtc).SequenceEqual(otherProviderChecks) && handler.RequestCount == serviceCalls,
                "Delivery failure and recovery preserve both preferences and do not query unrelated Providers");
            await context.QuotaMonitor.StopAsync();
            await context.ProviderMonitor!.StopAsync();
            context.Dispose();
            disposed = true;
            Check(context.StatusWindow.IsDisposed,
                "Injected delivery failure host stops and disposes normally without touching actual OS notification settings");
            Check(attempts == 2 && delivered.Count == 1 && balloonRequests == 0 && nativeBalloonEvents == 0,
                "Synthetic quota delivery failure and recovery never fall through to a real Windows notification");
        }
        finally
        {
            if (context is not null && !disposed)
            {
                if (context.QuotaMonitor is not null) await context.QuotaMonitor.StopAsync();
                if (context.ProviderMonitor is not null) await context.ProviderMonitor.StopAsync();
                context.Dispose();
            }
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { Console.WriteLine("INFO: Private early reset delivery smoke data remains in its temporary folder."); }
        }
    }

    private sealed class EarlyResetClient(IReadOnlyList<QuotaWindow> initial) : IAccountQuotaClient
    {
        private sealed record Response(IReadOnlyList<QuotaWindow> Windows, bool Fail = false, bool Block = false);
        private readonly object sync = new();
        private readonly Dictionary<QuotaProvider, Response> responses = Enum.GetValues<QuotaProvider>()
            .ToDictionary(provider => provider, _ => new Response(initial.ToArray()));
        private readonly List<int> readThreads = new();
        private int calls;
        internal int TotalCalls => Volatile.Read(ref calls);
        internal int[] ReadThreads { get { lock (sync) return readThreads.ToArray(); } }

        internal void Set(QuotaProvider provider, IReadOnlyList<QuotaWindow> windows, bool fail = false, bool block = false)
        {
            lock (sync) responses[provider] = new(windows.ToArray(), fail, block);
        }

        public async Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Response response;
            lock (sync)
            {
                response = responses[provider];
                readThreads.Add(Environment.CurrentManagedThreadId);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (response.Block) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            if (response.Fail) throw new IOException("Synthetic early reset smoke query failure");
            return new(provider, response.Windows);
        }
    }
}
