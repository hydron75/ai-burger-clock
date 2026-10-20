namespace AiBurgerClock;

// Native smoke for the shared early-reset decision (#70) as it runs in the Mac build (trimmed, real AccountQuotaMonitor),
// plus the Provider detail on the real panel. Synthetic readings and a fake clock only: no account, CLI or network.
internal static class MacEarlyResetSmoke
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 3, 3, 0, 0, TimeSpan.Zero);
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Early reset: " + message);
    }

    private static QuotaWindow W(string id, double used, DateTimeOffset reset, int minutes = 10080) =>
        new(id, "합성 " + id, used, reset, minutes);

    private static QuotaObservation Obs(long seq, DateTimeOffset at, long continuity, params QuotaWindow[] windows) =>
        new(QuotaProvider.Claude, seq, at, new QuotaReading(QuotaProvider.Claude, windows), continuity);

    // Two consecutive verified successes 30 minutes apart; returns the alert for the second one.
    private static QuotaEarlyResetAlert? Pair(QuotaWindow before, QuotaWindow after, TimeSpan? gap = null)
    {
        var engine = new QuotaEarlyResetNotifications();
        Check(engine.Observe(Obs(1, T0, 1, before), true) is null, "the first success must only set the baseline");
        return engine.Observe(Obs(2, T0 + (gap ?? TimeSpan.FromMinutes(30)), 1, after), true);
    }

    internal static async Task<string> VerifyAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset at = T0 + TimeSpan.FromMinutes(30);
        DateTimeOffset oldReset = T0.AddDays(3);
        DateTimeOffset newReset = at.AddDays(7).AddMinutes(-1);

        // Detection (#82): only the weekly window counts, usage must strictly decrease, and the previous scheduled
        // reset must be more than 5 minutes away. There is no 20 point drop, no 10% ceiling, no 90 minute gap
        // limit and no reset-change requirement.
        Check(Pair(W("w", 60, oldReset), W("w", 5, newReset)) is { Windows.Count: 1 }, "60% -> 5% with a moved reset must be detected");
        Check(Pair(W("w", 17, oldReset), W("w", 0, newReset), TimeSpan.FromHours(1)) is not null, "17% -> 0% with 4+ days left must be detected");
        Check(Pair(W("w", 60, oldReset), W("w", 10.1, newReset)) is not null, "new usage above 10% must now count");
        Check(Pair(W("w", 17, oldReset), W("w", 16, newReset)) is not null, "a 1 point drop must count (no 20 point rule)");
        Check(Pair(W("w", 60, oldReset), W("w", 5, oldReset)) is not null, "a decrease with the same reset time must count (no reset-change rule)");
        Check(Pair(W("w", 60, oldReset), W("w", 5, oldReset.AddSeconds(30))) is not null, "a 30 second reset change no longer matters");
        Check(Pair(W("w", 60, oldReset), W("w", 5, newReset), TimeSpan.FromMinutes(91)) is not null &&
              Pair(W("w", 60, oldReset), W("w", 5, newReset), TimeSpan.FromHours(30)) is not null, "there is no gap limit between two successes");
        // Excluded: equal or higher usage (also an unused 0% window with a moving reset), a normal reset, the 5 hour window.
        Check(Pair(W("w", 0, oldReset), W("w", 0, newReset)) is null, "an unused 0% window with a moving reset must not count");
        Check(Pair(W("w", 60, oldReset), W("w", 60, newReset)) is null && Pair(W("w", 60, oldReset), W("w", 61, newReset)) is null,
            "equal or higher usage must not count");
        Check(Pair(W("w", 60, at.AddMinutes(5)), W("w", 5, newReset)) is null, "a previous reset only 5 minutes away is a normal reset");
        Check(Pair(W("w", 60, at.AddMinutes(5.1)), W("w", 5, newReset)) is not null, "a previous reset 5.1 minutes away is still early");
        Check(Pair(W("w", 60, T0.AddMinutes(10)), W("w", 5, newReset)) is null, "a previous reset that already passed is a normal reset");
        Check(Pair(W("w", 60, T0.AddMinutes(200)), W("w", 5, T0.AddMinutes(300), 300), TimeSpan.FromMinutes(30)) is null &&
              Pair(W("f", 60, T0.AddHours(4), 300), W("f", 5, at.AddHours(5), 300)) is null, "the 5 hour window is never an early reset");
        Check(Pair(W("w", 60, oldReset), W("w", 5, newReset, 300)) is null, "a changed window length is a different window set");
        {
            var engine = new QuotaEarlyResetNotifications();
            engine.Observe(Obs(1, T0, 1, W("w", 60, oldReset)), true);
            Check(engine.Observe(Obs(2, at, 1, W("w", 5, newReset), W("x", 5, newReset)), true) is null, "an added window must give no alert");
            // Disabled: no baseline is armed, so enabling later needs a fresh pair.
            var off = new QuotaEarlyResetNotifications();
            off.Observe(Obs(1, T0, 1, W("w", 60, oldReset)), false);
            Check(off.Observe(Obs(2, at, 1, W("w", 5, newReset)), true) is null, "the first success after enabling only arms the baseline");
            // The fixed reasons recorded in the decision log (no usage, time or name in them).
            string Reason(QuotaWindow before, QuotaWindow after, long continuity = 1)
            {
                var e = new QuotaEarlyResetNotifications();
                e.Evaluate(Obs(1, T0, 1, before), true);
                return e.Evaluate(Obs(2, at, continuity, after), true).Reason;
            }
            Check(Reason(W("w", 60, oldReset), W("w", 5, newReset)) == "Alert", "the reason for an alert is Alert");
            Check(Reason(W("w", 60, oldReset), W("w", 60, newReset)) == "UsageNotDecreased", "equal usage: UsageNotDecreased");
            Check(Reason(W("w", 60, at.AddMinutes(5)), W("w", 5, newReset)) == "NotBeforeScheduledReset", "a normal reset: NotBeforeScheduledReset");
            Check(Reason(W("w", 60, oldReset), W("w", 5, newReset, 300)) == "WindowSetChanged", "another length: WindowSetChanged");
            Check(Reason(W("f", 60, T0.AddHours(4), 300), W("f", 5, at.AddHours(5), 300)) == "NoWeeklyWindow", "a 5 hour window: NoWeeklyWindow");
            Check(Reason(W("w", 60, oldReset), W("w", 5, newReset), 2) == "ContinuityChanged", "another continuity id: ContinuityChanged");
            var dup = new QuotaEarlyResetNotifications();
            dup.Evaluate(Obs(5, T0, 1, W("w", 60, oldReset)), true);
            Check(dup.Evaluate(Obs(5, at, 1, W("w", 5, newReset)), true).Reason == "DuplicateObservation", "the same sequence again: DuplicateObservation");
            Check(new QuotaEarlyResetNotifications().Evaluate(Obs(1, T0, 1, W("w", 60, oldReset)), true).Reason == "NoPreviousSuccess",
                "a first success without a previous one: NoPreviousSuccess");
            Check(Reason(W("w", 60, oldReset), W("w", 5, newReset)) == "Alert" &&
                  new QuotaEarlyResetNotifications().Evaluate(Obs(1, T0, 1, W("w", 60, oldReset)), false).Reason == "Disabled", "disabled: Disabled");
        }

        // A cached previous success (a restart) is compared with the first new success, but a setting toggle
        // never compares with it. The cache and the monitor generation are not an account identity.
        {
            QuotaPreviousSuccess cached = new(new QuotaReading(QuotaProvider.Claude, [W("w", 17, oldReset)]), T0, 1);
            QuotaObservation first = new(QuotaProvider.Claude, 1, T0 + TimeSpan.FromDays(2), new QuotaReading(QuotaProvider.Claude,
                [W("w", 0, T0.AddDays(9))]), 1, cached);
            Check(new QuotaEarlyResetNotifications().Observe(first, true) is { Windows.Count: 1 }, "a restart with a cached 17% must detect a 0% first success");
            var toggled = new QuotaEarlyResetNotifications();
            toggled.Reset();
            Check(toggled.Observe(first, true) is null, "the first success after a toggle must only set the baseline, not use the cache");
            Check(new QuotaEarlyResetNotifications().Observe(first with { ContinuityId = 2 }, true) is null, "a cache from another continuity must not compare");
            QuotaObservation sameZero = new(QuotaProvider.Claude, 1, T0 + TimeSpan.FromDays(2), new QuotaReading(QuotaProvider.Claude,
                [W("w", 0, T0.AddDays(9))]), 1, new QuotaPreviousSuccess(new QuotaReading(QuotaProvider.Claude, [W("w", 0, T0.AddDays(9))]), T0, 1));
            Check(new QuotaEarlyResetNotifications().Observe(sameZero, true) is null, "a 0% cache and a 0% first success must not count");
        }

        // A failed read between two successes breaks continuity (real monitor, fake clock, scripted client).
        DateTimeOffset clock = T0;
        var script = new Queue<Func<DateTimeOffset, QuotaReading?>>();
        var client = new ScriptedClient(() => script.Dequeue()(clock));
        using var monitor = new AccountQuotaMonitor(client, null, () => clock);
        var continuity = new List<long>();
        var engineLive = new QuotaEarlyResetNotifications();
        var alerts = new List<QuotaEarlyResetAlert?>();
        var previousSeen = new List<QuotaPreviousSuccess?>();
        monitor.Succeeded += observation =>
        {
            continuity.Add(observation.ContinuityId);
            previousSeen.Add(observation.PreviousSuccess);
            alerts.Add(engineLive.Observe(observation, true));
        };
        QuotaReading Reading(double used, DateTimeOffset reset) => new(QuotaProvider.Claude, [W("w", used, reset)]);
        async Task Poll(Func<DateTimeOffset, QuotaReading?> next, int minutes = 30)
        {
            clock += TimeSpan.FromMinutes(minutes);
            script.Enqueue(next);
            await monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
        }
        await Poll(now => Reading(60, now.AddDays(3)));
        await Poll(now => Reading(5, now.AddDays(7).AddMinutes(-1)));
        Check(alerts.Count == 2 && alerts[0] is null && alerts[1] is not null && continuity[0] == continuity[1] && continuity[0] > 0,
            "two consecutive live successes must share one positive continuity id and detect the early reset");
        Check(previousSeen[0] is null && previousSeen[1] is { ContinuityId: > 0 } && previousSeen[1]!.Reading.Windows.Single().UsedPercent == 60,
            "the real monitor must hand over the previous successful reading with its generation");
        long before = continuity[^1];
        await Poll(now => Reading(60, now.AddDays(3)));
        await Poll(_ => null); // a started read that fails
        await Poll(now => Reading(5, now.AddDays(7).AddMinutes(-1)));
        Check(alerts.Count == 4 && alerts[3] is null && continuity[3] > continuity[2],
            "a failed read between two successes must change the continuity id and suppress the alert");

        // Same response as a recovery: one combined notification per Provider response.
        var recovery = new QuotaRecoveryNotifications();
        var early = new QuotaEarlyResetNotifications();
        // "b" is a 5 hour window: it recovers but is never an early reset (#82), so only "a" is early.
        DateTimeOffset fiveReset = T0.AddHours(2);
        QuotaObservation A = Obs(1, T0, 1, W("a", 100, oldReset), W("b", 100, fiveReset, 300));
        QuotaObservation B = Obs(2, at, 1, W("a", 4, newReset), W("b", 50, fiveReset, 300));
        recovery.Observe(A, true); early.Observe(A, true);
        QuotaRecoveryAlert? rec = recovery.Observe(B, true);
        QuotaEarlyResetAlert? ear = early.Observe(B, true);
        Check(rec is not null && ear is { Windows.Count: 1 }, "both a recovery and an early reset must hold for this response");
        QuotaNotificationAlert? both = QuotaNotificationText.Combine(rec, ear);
        Check(both is { Recovery: not null, EarlyReset: not null } && both.Title == QuotaEarlyResetText.Title(QuotaProvider.Claude),
            "one combined notification must carry the early-reset title");
        Check(both!.Body.Contains("다른 한도에도 잔여량이 확인되었습니다"), "a recovered window outside the early reset must be mentioned once");
        Check(!both.Body.Any(char.IsDigit) && !both.Body.Contains('%') && !both.Body.Contains(':'), "the banner must carry no numbers, percentages or times");
        // Recovery alone keeps its own text; early alone has no recovery; a mismatched pair is refused.
        QuotaNotificationAlert? only = QuotaNotificationText.Combine(rec, null);
        Check(only is { EarlyReset: null } && only.Title == rec!.Title && only.Body == rec.Body, "recovery alone must keep its exact text");
        QuotaNotificationAlert? earlyOnly = QuotaNotificationText.Combine(null, ear);
        Check(earlyOnly is { Recovery: null, EarlyReset: not null }, "an early reset alone must combine without a recovery");
        Check(QuotaNotificationText.Combine(rec, ear! with { Sequence = ear.Sequence + 1 }) is null, "a pair from different responses must not combine");
        var sameWindow = new QuotaRecoveryNotifications();
        var sameEarly = new QuotaEarlyResetNotifications();
        QuotaObservation C = Obs(1, T0, 1, W("a", 100, oldReset));
        QuotaObservation D = Obs(2, at, 1, W("a", 3, newReset));
        sameWindow.Observe(C, true); sameEarly.Observe(C, true);
        QuotaNotificationAlert? single = QuotaNotificationText.Combine(sameWindow.Observe(D, true), sameEarly.Observe(D, true));
        Check(single is not null && !single.Body.Contains("다른 한도에도 잔여량이 확인되었습니다"), "the same window in both must not be listed twice");

        // Provider detail: the recent record appears only in the detail, rows are unchanged.
        QuotaState state = new(QuotaProvider.Claude, new QuotaReading(QuotaProvider.Claude, [W("a", 4, newReset)]),
            LastSuccessfulCheckUtc: at, NextCheckUtc: at.AddHours(1), ContinuityId: 1);
        QuotaSectionText plain = QuotaPanelModel.Section(state, at.AddMinutes(10));
        QuotaSectionText withRecent = QuotaPanelModel.Section(state, at.AddMinutes(10), ear);
        Check(!plain.Detail.Contains("조기 초기화") && withRecent.Detail.Contains("최근 조기 초기화 가능성"),
            "the recent early reset must appear in the Provider detail only when given");
        Check(plain.Rows.Count == withRecent.Rows.Count && plain.Rows.Select(r => r.Text).SequenceEqual(withRecent.Rows.Select(r => r.Text)),
            "rows must not change when a recent early reset is attached");
        return $"early reset engine ({checks} checks: weekly-only decrease detection without 20 point/10%/gap/reset-change rules, equal/normal-reset/5 hour/window-set exclusions, " +
               "fixed decision reasons, cached previous success vs toggle, failure breaks continuity, recovery+early combine into one banner, detail only)";
    }

    // The real chain on this host: a real monitor (temporary SQLite, fake clock, scripted client) appends one row per read to the
    // early-reset decision log, and the Mac controller finishes that row once on the main thread with the engine's decision.
    internal static async Task<string> VerifyDecisionLogAsync(Action<Action> post, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "aibc-decisions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new UsageStore(Path.Combine(directory, "burgerclock.db"));
            string logPath = Path.Combine(directory, "logs", "quota-early-reset-decisions-v1.jsonl");
            DateTimeOffset clock = T0;
            var script = new Queue<Func<DateTimeOffset, QuotaReading?>>();
            var client = new ScriptedClient(() => script.Dequeue()(clock));
            using var monitor = new AccountQuotaMonitor(client, store, () => clock);
            var shown = new List<QuotaNotificationAlert>();
            MacQuotaNotifications? controller = null;
            controller = new MacQuotaNotifications(() => false, _ => Task.CompletedTask, _ => Task.CompletedTask,
                provider => monitor.Snapshot().FirstOrDefault(state => state.Provider == provider), shown.Add, (_, _) => { }, post,
                () => controller!.ShowPending(), [], utcNow: () => clock);
            await controller.LoadEarlyResetAsync(() => Task.FromResult(true));
            controller.Attach(monitor);

            QuotaReading Weekly(double used, DateTimeOffset reset) => new(QuotaProvider.Claude, [W("w", used, reset)]);
            async Task Poll(Func<DateTimeOffset, QuotaReading?> next)
            {
                clock += TimeSpan.FromMinutes(30);
                script.Enqueue(next);
                await monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
                DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                do NSRunLoop.Main.RunUntil(NSRunLoopMode.Default, NSDate.FromTimeIntervalSinceNow(0.01));
                while (!controller.Idle && DateTime.UtcNow < deadline);
                Check(controller.Idle, "the hand-off queue did not drain");
            }
            List<(string Outcome, string Reason, bool Succeeded)> Rows()
            {
                var rows = new List<(string, string, bool)>();
                foreach (string line in File.ReadAllLines(logPath))
                {
                    using var document = System.Text.Json.JsonDocument.Parse(line);
                    rows.Add((document.RootElement.GetProperty("Outcome").GetString()!, document.RootElement.GetProperty("Reason").GetString()!,
                        document.RootElement.GetProperty("Succeeded").GetBoolean()));
                }
                return rows;
            }

            await Poll(now => Weekly(60, now.AddDays(3)));                                   // first success: only a baseline
            await Poll(now => Weekly(5, now.AddDays(7).AddMinutes(-1)));                     // 60 -> 5, reset moved: an alert
            await Poll(now => Weekly(5, now.AddDays(7).AddMinutes(-1)));                     // unchanged
            await Poll(_ => null);                                                          // a read that fails
            await Poll(now => Weekly(2, now.AddDays(7).AddMinutes(-1)));                     // a lower value after the failure
            var rows = Rows();
            Check(rows.Count == 5, $"expected one decision row per read, found {rows.Count}.");
            Check(rows[0] is ("Excluded", "NoPreviousSuccess", true) && rows[1] is ("Alert", "Alert", true) &&
                  rows[2] is ("Excluded", "UsageNotDecreased", true) && rows[3] is ("Excluded", "ReadFailed", false) &&
                  rows[4] is ("Excluded", "ContinuityChanged", true),
                "the decision rows are not NoPreviousSuccess / Alert / UsageNotDecreased / ReadFailed / ContinuityChanged: " +
                string.Join(", ", rows.Select(row => row.Outcome + "/" + row.Reason)));
            Check(shown.Count == 1 && shown[0].EarlyReset is not null, "exactly one banner must be requested for the one alert row");
            // The row holds no usage, reset time or name: nine fixed fields and none of the synthetic values.
            string text = File.ReadAllText(logPath);
            using (var first = System.Text.Json.JsonDocument.Parse(text.Split('\n')[0]))
                Check(first.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(
                    ["AttemptId", "AttemptedAtUtc", "ContinuityId", "Outcome", "Provider", "Reason", "RecordedAtUtc", "Sequence", "Succeeded"]),
                    "the decision row must have exactly the nine fixed fields");
            Check(!text.Contains("합성", StringComparison.Ordinal) && !text.Contains("\"w\"", StringComparison.Ordinal) && !text.Contains("UsedPercent", StringComparison.Ordinal),
                "the decision log must not contain a usage, reset time or window name");
            Check(Directory.GetFiles(Path.Combine(directory, "logs")).All(file => !file.EndsWith(".tmp", StringComparison.Ordinal)),
                "a temporary decision-log file was left behind");
            controller.Detach();
            return $"early reset decision log (real monitor + Mac controller: one row per read finished once as NoPreviousSuccess/Alert/UsageNotDecreased/ReadFailed/ContinuityChanged, " +
                   "nine fixed fields, no usage/time/name, one banner)";
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
        }
    }

    // Regression (#82 review): a Succeeded callback the monitor had already copied into its invocation list can run after
    // Detach. It must close ITS OWN monitor's decision-log row as HostUnavailable (never NotEvaluated), without evaluating,
    // alerting or touching another monitor's log, a later subscription, or a re-attachment of the same monitor.
    // Real temporary monitors and loggers; a blocker handler registered before the controller holds the poll open.
    internal static async Task<string> VerifyLateCallbackAsync(Action<Action> post, CancellationToken cancellationToken)
    {
        string root = Path.Combine(Path.GetTempPath(), "aibc-late-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var disposables = new List<IDisposable>();
            (AccountQuotaMonitor Monitor, string Log, Queue<Func<DateTimeOffset, QuotaReading?>> Script, Func<DateTimeOffset> Clock, Action Advance) Make(string name)
            {
                string directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                DateTimeOffset clock = T0;
                var script = new Queue<Func<DateTimeOffset, QuotaReading?>>();
                var client = new ScriptedClient(() => script.Dequeue()(clock));
                var monitor = new AccountQuotaMonitor(client, new UsageStore(Path.Combine(directory, "burgerclock.db")), () => clock);
                disposables.Add(monitor);
                return (monitor, Path.Combine(directory, "logs", "quota-early-reset-decisions-v1.jsonl"), script, () => clock,
                    () => clock += TimeSpan.FromMinutes(30));
            }
            QuotaReading Weekly(double used, DateTimeOffset reset) => new(QuotaProvider.Claude, [W("w", used, reset)]);
            string[] Reasons(string log) => File.ReadAllLines(log).Select(line =>
            {
                using var document = System.Text.Json.JsonDocument.Parse(line);
                return document.RootElement.GetProperty("Outcome").GetString() + "/" + document.RootElement.GetProperty("Reason").GetString();
            }).ToArray();
            var shown = new List<QuotaNotificationAlert>();
            AccountQuotaMonitor? attached = null;
            MacQuotaNotifications? controller = null;
            controller = new MacQuotaNotifications(() => false, _ => Task.CompletedTask, _ => Task.CompletedTask,
                provider => attached?.Snapshot().FirstOrDefault(state => state.Provider == provider), shown.Add, (_, _) => { }, post,
                () => controller!.ShowPending(), [], utcNow: () => T0.AddDays(1));
            await controller.LoadEarlyResetAsync(() => Task.FromResult(true));
            void Settle()
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                do NSRunLoop.Main.RunUntil(NSRunLoopMode.Default, NSDate.FromTimeIntervalSinceNow(0.01));
                while (!controller!.Idle && DateTime.UtcNow < deadline);
                Check(controller.Idle, "the hand-off queue did not drain");
            }

            // The blocker is registered BEFORE the controller subscribes, so it runs first and holds the poll open after the
            // monitor has already copied its invocation list (which will contain the controller's handler).
            (TaskCompletionSource Reached, ManualResetEventSlim Gate) Block(AccountQuotaMonitor monitor)
            {
                var reached = new TaskCompletionSource();
                var gate = new ManualResetEventSlim(false);
                monitor.Succeeded += _ => { reached.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(10)); };
                return (reached, gate);
            }
            async Task<Task> StartPoll(AccountQuotaMonitor monitor, Queue<Func<DateTimeOffset, QuotaReading?>> script, Action advance,
                Func<DateTimeOffset, QuotaReading?> reading, TaskCompletionSource reached)
            {
                advance();
                script.Enqueue(reading);
                Task poll = Task.Run(() => monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken));
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                return poll;
            }

            // 1. Detach while a copied callback is pending: the ORIGINAL file is closed as HostUnavailable.
            var one = Make("one");
            attached = one.Monitor;
            var held = Block(one.Monitor);
            controller.Attach(one.Monitor);
            Task heldPoll = await StartPoll(one.Monitor, one.Script, one.Advance, now => Weekly(60, now.AddDays(3)), held.Reached);
            Check(File.Exists(one.Log) && Reasons(one.Log).SequenceEqual(["Excluded/NotEvaluated"]),
                "the read must have a NotEvaluated row while its callback is pending (" +
                (File.Exists(one.Log) ? string.Join(",", Reasons(one.Log)) : "no log file at " + one.Log) + ")");
            controller.Detach();
            held.Gate.Set();
            await heldPoll;
            Settle();
            Check(Reasons(one.Log).SequenceEqual(["Excluded/HostUnavailable"]),
                "a callback that ran after Detach must close its own row as HostUnavailable, not leave NotEvaluated: " + string.Join(",", Reasons(one.Log)));
            Check(shown.Count == 0 && controller.PendingCount == 0 && controller.RecentEarlyResets().Count == 0,
                "a late callback must not evaluate, queue, show or remember anything");

            // 2. Re-attach to ANOTHER monitor before the old callback runs: the old row is closed in the old file only.
            var oldOne = Make("old");
            var newOne = Make("new");
            attached = oldOne.Monitor;
            held = Block(oldOne.Monitor);
            controller.Attach(oldOne.Monitor);
            heldPoll = await StartPoll(oldOne.Monitor, oldOne.Script, oldOne.Advance, now => Weekly(60, now.AddDays(3)), held.Reached);
            controller.Detach();
            attached = newOne.Monitor;
            controller.Attach(newOne.Monitor);
            held.Gate.Set();
            await heldPoll;
            Settle();
            Check(Reasons(oldOne.Log).SequenceEqual(["Excluded/HostUnavailable"]), "the old monitor's row must be closed as HostUnavailable in the old log");
            Check(!File.Exists(newOne.Log), "the late callback of the old monitor must not write into the new monitor's log");
            newOne.Advance();
            newOne.Script.Enqueue(now => Weekly(5, now.AddDays(7).AddMinutes(-1)));
            await newOne.Monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
            Settle();
            Check(Reasons(newOne.Log).SequenceEqual(["Excluded/NoPreviousSuccess"]) && shown.Count == 0,
                "the new subscription must start fresh: the old response was neither compared nor alerted: " + string.Join(",", Reasons(newOne.Log)));

            // 3. Re-attach the SAME monitor: the old callback belongs to the ended subscription and is only closed.
            var same = Make("same");
            attached = same.Monitor;
            controller.Detach();
            held = Block(same.Monitor);
            controller.Attach(same.Monitor);
            heldPoll = await StartPoll(same.Monitor, same.Script, same.Advance, now => Weekly(60, now.AddDays(3)), held.Reached);
            controller.Detach();
            controller.Attach(same.Monitor);
            held.Gate.Set();
            await heldPoll;
            Settle();
            Check(Reasons(same.Log).SequenceEqual(["Excluded/HostUnavailable"]), "the old subscription's response must be closed as HostUnavailable");
            same.Advance();
            same.Script.Enqueue(now => Weekly(5, now.AddDays(7).AddMinutes(-1)));
            await same.Monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
            Settle();
            // The monitor itself hands its last successful reading to the next response (the shared PreviousSuccess), so the
            // new response is compared with it once: that alert is the NEW response's. The old callback alerted nothing.
            Check(Reasons(same.Log).SequenceEqual(["Excluded/HostUnavailable", "Alert/Alert"]) && shown.Count == 1,
                "the old response must be closed without an alert and only the new response may alert: " + string.Join(",", Reasons(same.Log)));

            // 4. Normal path is unchanged: a live subscription still evaluates and finishes its row once.
            same.Advance();
            same.Script.Enqueue(now => Weekly(2, now.AddDays(7).AddMinutes(-1)));
            await same.Monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
            Settle();
            Check(Reasons(same.Log).Last() == "Alert/Alert" && shown.Count + controller.PendingCount == 2,
                "a live subscription must still evaluate each response once and alert once per decrease");
            controller.Detach();
            foreach (IDisposable disposable in disposables) disposable.Dispose();
            return "late Succeeded callback after Detach (real monitors/loggers: original row closed as HostUnavailable not NotEvaluated, no evaluation/alert, " +
                   "other monitor's log untouched, same-monitor re-attach gives no alert for the old response, live path unchanged)";
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch (IOException) { }
        }
    }

    // The Provider detail carries the remembered early reset; rows, bars and the popover size do not change.
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Early reset: " + message);
    }

    internal static string VerifyPanel(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        IReadOnlyList<QuotaState> quotas, DateTimeOffset now)
    {
        panel.Update(schedule, providers, quotas, false, now.AddMinutes(5), "");
        MacStatusPanel.Layout plain = panel.Verify(schedule, providers, quotas);
        string rowBefore = panel.QuotaRowText(QuotaProvider.Claude, "session");
        string spokenBefore = panel.QuotaRowSpoken(QuotaProvider.Claude, "session");
        int barsBefore = panel.QuotaBarCount;
        var recent = new Dictionary<QuotaProvider, QuotaEarlyResetAlert>
        {
            [QuotaProvider.Claude] = new(QuotaProvider.Claude, 5, 1, now, [new("weekly_all", "합성 주간", 10080, 60, 4, now.AddDays(3), now.AddDays(7).AddMinutes(-1))])
        };
        panel.Update(schedule, providers, quotas, false, now.AddMinutes(5), "", recent);
        // Verify also checks that every tooltip of the Claude box equals the shared Provider detail built WITH the record.
        MacStatusPanel.Layout shown = panel.Verify(schedule, providers, quotas, recent);
        QuotaSectionText claude = QuotaPanelModel.Sections(quotas, now, recent).Single(section => section.Provider == QuotaProvider.Claude);
        QuotaSectionText gemini = QuotaPanelModel.Sections(quotas, now, recent).Single(section => section.Provider == QuotaProvider.Gemini);
        Require(claude.Detail.Contains("최근 조기 초기화 가능성") && !gemini.Detail.Contains("조기 초기화"),
            "the record must appear in its own Provider's detail only");
        Require(plain.Size == shown.Size && panel.QuotaBarCount == barsBefore &&
              panel.QuotaRowText(QuotaProvider.Claude, "session") == rowBefore && panel.QuotaRowSpoken(QuotaProvider.Claude, "session") == spokenBefore,
            "the record must not change the popover size, rows, bars or the spoken row");
        // Back to the standard state for the rest of the smoke run.
        panel.Update(schedule, providers, quotas, false, now.AddMinutes(5), "");
        panel.Verify(schedule, providers, quotas);
        return $"early reset Provider detail (record in its own box tooltip, popover {shown.Size.Width:0}x{shown.Size.Height:0}pt and rows unchanged)";
    }

    private sealed class ScriptedClient(Func<QuotaReading?> next) : IAccountQuotaClient
    {
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            QuotaReading? reading = next();
            return reading is null ? Task.FromException<QuotaReading>(new InvalidOperationException("synthetic failure")) : Task.FromResult(reading);
        }
    }
}
