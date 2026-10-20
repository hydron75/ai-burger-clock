using AppKit;
using Foundation;

namespace AiBurgerClock;

// Native smoke for the Mac quota notification wiring (recovery #59 and early reset #70). Synthetic readings
// and the smoke run's temporary database only: no account, CLI, network, notification permission or real
// delivery is involved. Each scenario uses its own MacQuotaNotifications so the app's real controller is only
// checked for what it is wired to (menu items, default OFF); the hand-off, ordering and invalidation rules
// are the same code in every instance.
internal static class MacQuotaRecoverySmoke
{
    private sealed class Harness
    {
        internal readonly List<QuotaNotificationAlert> Shown = [];
        internal readonly List<(string Text, bool Error)> Feedback = [];
        internal readonly List<string> Order = [];
        internal readonly HashSet<Task> Writes = [];
        internal readonly Dictionary<QuotaProvider, QuotaState> States = [];
        internal readonly MacQuotaNotifications Recovery;
        internal long Clock = 1_000;
        internal int Refreshes;
        internal bool Stopping;
        internal bool ThrowOnShow;
        // What the controller wrote to the early-reset decision log (outcome and fixed reason only).
        internal readonly List<(long Sequence, string Outcome, string Reason)> Decisions = [];
        internal Func<QuotaProvider, QuotaState?>? StateSource;
        // Added to the real clock for the controller's "now". Obs() stamps responses up to a few minutes after the
        // process start, and a queued alert is never validated against a clock earlier than its own response,
        // so "now" starts a few minutes ahead (BaseClockOffset); later checks add to it.
        internal static readonly TimeSpan BaseClockOffset = TimeSpan.FromMinutes(5);
        internal TimeSpan ClockOffset = BaseClockOffset;

        internal Harness(Func<bool, Task> save, Action<Action> post, bool denied = false, Func<bool, Task>? saveEarlyReset = null)
        {
            Recovery = new(() => Stopping, save, saveEarlyReset ?? (_ => Task.CompletedTask),
                provider => StateSource?.Invoke(provider) ?? States.GetValueOrDefault(provider),
                alert =>
                {
                    if (ThrowOnShow) throw new InvalidOperationException("synthetic delivery failure");
                    Shown.Add(alert);
                    Order.Add("quota");
                },
                (text, error) => Feedback.Add((text, error)), post,
                // The app's display pass: existing alerts first, then the pending quota alert.
                () => { Refreshes++; Recovery!.ShowPending(); },
                Writes, utcNow: () => DateTimeOffset.UtcNow + ClockOffset, milliseconds: () => Clock, notificationsDenied: () => denied,
                recordDecision: (observation, outcome, reason) => Decisions.Add((observation.Sequence, outcome, reason)));
        }

        // A verified live response: what the monitor would publish and what the snapshot would then show.
        internal void Deliver(QuotaObservation observation, bool viaCallback = true)
        {
            States[observation.Provider] = new QuotaState(observation.Provider, observation.Reading,
                observation.CheckedAtUtc, observation.CheckedAtUtc) with { ContinuityId = observation.ContinuityId };
            if (viaCallback) Recovery.OnSucceeded(observation);
        }

        internal void Existing()
        {
            Order.Add("existing");
            Recovery.DeferAfterExisting();
        }

        internal void Tick(long milliseconds)
        {
            Clock += milliseconds;
            Recovery.ShowPending();
        }
    }

    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;
    private static readonly Dictionary<QuotaProvider, long> sequences = [];

    private static QuotaObservation Obs(QuotaProvider provider, double session, double weekly = 20, long? sequence = null,
        DateTimeOffset? at = null, int sessionMinutes = 300, string sessionId = "session")
    {
        long next = sequence ?? (sequences[provider] = sequences.GetValueOrDefault(provider) + 1);
        DateTimeOffset checkedAt = at ?? Start.AddSeconds(next);
        var reading = new QuotaReading(provider,
        [
            new(sessionId, "합성 세션", session, checkedAt.AddHours(2), sessionMinutes),
            new("weekly_all", "합성 주간", weekly, checkedAt.AddDays(3), 10080)
        ]);
        return new(provider, next, checkedAt, reading);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Quota recovery: " + message);
    }

    // Run the main run loop until the hand-off queue is empty (the Drain posted by OnSucceeded has run).
    private static void Pump(Harness harness)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        do NSRunLoop.Main.RunUntil(NSRunLoopMode.Default, NSDate.FromTimeIntervalSinceNow(0.01));
        while (!harness.Recovery.Idle && DateTime.UtcNow < deadline);
        Check(harness.Recovery.Idle, "the hand-off queue did not drain");
        NSRunLoop.Main.RunUntil(NSRunLoopMode.Default, NSDate.FromTimeIntervalSinceNow(0.01));
    }

    private static void Send(Harness harness, QuotaObservation observation)
    {
        harness.Deliver(observation);
        Pump(harness);
    }

    private sealed class ScriptedClient : IAccountQuotaClient
    {
        private readonly Queue<double> sessionValues = new();
        internal readonly List<bool> CallbackOnMain = [];
        internal double Fallback = 10;
        internal int Calls;

        internal void Script(params double[] values)
        {
            foreach (double value in values) sessionValues.Enqueue(value);
        }

        public async Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            // Continue on the thread pool, as the CLI client does.
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref Calls);
            double session = provider == QuotaProvider.Claude && sessionValues.TryDequeue(out double value) ? value : Fallback;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new(provider,
            [
                new("session", "합성 세션", session, now.AddHours(2), 300),
                new("weekly_all", "합성 주간", 20, now.AddDays(3), 10080)
            ]);
        }
    }

    internal static async Task<string> VerifyAsync(UsageStore store, MacQuotaNotifications real, NSMenu contextMenu,
        Action<Action> post, CancellationToken cancellationToken)
    {
        // ---- The app's own controller: wired into the right-click menu, loaded, default OFF ----
        Check(real.Ready && !real.Enabled && !real.Checked && real.MenuItem.Enabled &&
              contextMenu.Items.Contains(real.MenuItem), "the app controller is not wired into the menu or is not OFF by default");
        Check(real.MenuItem.Title == QuotaRecoveryText.MenuLabel && real.MenuItem.ToolTip == QuotaRecoveryText.MenuDetail,
            "the menu item does not use the shared label and detail");
        Check(!await store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken), "a fresh database must read OFF");

        // ---- Default OFF: nothing is decided, shown or queued ----
        var off = new Harness(enabled => store.SetQuotaRecoveryNotificationsEnabledAsync(enabled, cancellationToken), post);
        await off.Recovery.LoadAsync(() => store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken));
        Check(off.Recovery.Ready && !off.Recovery.Enabled && !off.Recovery.Checked, "loaded state must be OFF");
        Send(off, Obs(QuotaProvider.Claude, 100));
        Send(off, Obs(QuotaProvider.Claude, 40));
        Check(off.Shown.Count == 0 && off.Recovery.PendingCount == 0 && off.Refreshes == 2,
            "OFF must not queue or show a recovery (the display pass still runs)");

        // ---- Turn ON: persisted, new baseline, feedback ----
        int epochBefore = off.Recovery.Epoch;
        await off.Recovery.ChangeAsync(true);
        Check(off.Recovery.Enabled && off.Recovery.Checked && off.Recovery.Epoch > epochBefore && !off.Recovery.Changing &&
              await store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken) &&
              off.Feedback[^1] == (QuotaRecoveryText.Saved(true), false) && off.Writes.Count == 0,
            "turning ON must persist, check the menu, advance the epoch and report");
        Send(off, Obs(QuotaProvider.Claude, 100));           // first success after enabling: baseline only
        Check(off.Shown.Count == 0 && off.Recovery.PendingCount == 0, "the first success after enabling must stay silent");
        Send(off, Obs(QuotaProvider.Claude, 55));            // exhausted -> remaining: one alert
        Check(off.Shown.Count == 1 && off.Shown[0].Title == QuotaRecoveryText.Title(QuotaProvider.Claude) &&
              off.Shown[0].Body.Contains("합성 세션", StringComparison.Ordinal) &&
              !off.Shown[0].Body.Contains('%') && off.Recovery.PendingCount == 0,
            "exhausted -> remaining must request exactly one alert with the shared title and a generic body");
        Send(off, Obs(QuotaProvider.Claude, 55, sequence: sequences[QuotaProvider.Claude]));   // same sequence again
        Send(off, Obs(QuotaProvider.Claude, 50));
        Check(off.Shown.Count == 1, "a duplicate or an unchanged recovered value must not notify again");
        Send(off, Obs(QuotaProvider.Claude, 100));
        Send(off, Obs(QuotaProvider.Claude, 80));
        off.Tick(MacQuotaNotifications.DeferMilliseconds);        // the first alert's own spacing has elapsed
        Check(off.Shown.Count == 2, "re-exhaustion followed by recovery must notify again");

        // ---- Existing alerts first, then the quota alert ----
        off.Order.Clear();
        Send(off, Obs(QuotaProvider.Claude, 100));
        off.Existing();                                       // a schedule/service alert was just requested
        Send(off, Obs(QuotaProvider.Claude, 70));
        Check(off.Shown.Count == 2 && off.Recovery.PendingCount == 1, "a quota alert must wait behind an existing alert");
        off.Tick(MacQuotaNotifications.DeferMilliseconds - 1);
        Check(off.Shown.Count == 2, "the quota alert must not appear before the deferral has elapsed");
        off.Tick(1);
        Check(off.Shown.Count == 3 && off.Order.SequenceEqual(["existing", "quota"]) && off.Recovery.PendingCount == 0,
            "the quota alert must appear after the existing alert and the deferral");

        // ---- A queued alert that is no longer true is dropped, never shown ----
        off.Existing();
        Send(off, Obs(QuotaProvider.Claude, 100));
        Send(off, Obs(QuotaProvider.Claude, 60));
        Check(off.Recovery.PendingCount == 1, "recovery was not queued behind the deferral");
        Send(off, Obs(QuotaProvider.Claude, 100));            // exhausted again before delivery
        Check(off.Recovery.PendingCount == 0, "re-exhaustion must drop the queued alert");
        off.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(off.Shown.Count == 3, "a dropped alert must not be shown later");
        // Period change then return: a queued alert from the earlier state is gone for good.
        Send(off, Obs(QuotaProvider.Claude, 100));
        off.Existing();
        Send(off, Obs(QuotaProvider.Claude, 60));
        Send(off, Obs(QuotaProvider.Claude, 60, sessionMinutes: 600));   // same id, other period
        Send(off, Obs(QuotaProvider.Claude, 60));                        // back to the original period
        Check(off.Recovery.PendingCount == 0, "a period change must drop the queued alert even if the period returns");
        off.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(off.Shown.Count == 3, "the old alert came back after a period round trip");
        // A failed later query (previous value) is not a current availability claim.
        Send(off, Obs(QuotaProvider.Claude, 100));
        off.Existing();
        Send(off, Obs(QuotaProvider.Claude, 40));
        Check(off.Recovery.PendingCount == 1, "recovery was not queued for the failed-query check");
        off.States[QuotaProvider.Claude] = off.States[QuotaProvider.Claude] with { IsPrevious = true, Error = "합성 실패" };
        off.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(off.Shown.Count == 3 && off.Recovery.PendingCount == 0, "a failed later query must drop the queued alert");

        // ---- Toggle: queued observations and alerts are invalidated, the baseline restarts ----
        Send(off, Obs(QuotaProvider.Claude, 100));
        off.Existing();
        Send(off, Obs(QuotaProvider.Claude, 40));
        Check(off.Recovery.PendingCount == 1, "recovery was not queued for the toggle check");
        QuotaObservation inFlight = Obs(QuotaProvider.Claude, 100);
        off.Deliver(inFlight);                                // enqueued, not yet drained
        await off.Recovery.ChangeAsync(false);
        Pump(off);
        Check(!off.Recovery.Enabled && !off.Recovery.Checked && off.Recovery.PendingCount == 0 &&
              !await store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken) &&
              off.Feedback[^1] == (QuotaRecoveryText.Saved(false), false),
            "turning OFF must persist, uncheck, and clear the queued alert");
        off.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(off.Shown.Count == 3, "turning OFF must not show a queued alert");
        await off.Recovery.ChangeAsync(true);
        Send(off, Obs(QuotaProvider.Claude, 70));            // the pre-toggle exhaustion must not count
        Check(off.Shown.Count == 3 && off.Recovery.PendingCount == 0, "re-enabling must not replay an earlier recovery");

        // ---- Restart: the stored ON is read back, memory starts fresh and the first success is silent ----
        var restarted = new Harness(enabled => store.SetQuotaRecoveryNotificationsEnabledAsync(enabled, cancellationToken), post);
        await restarted.Recovery.LoadAsync(() => store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken));
        Check(restarted.Recovery.Enabled && restarted.Recovery.Checked && restarted.Recovery.MenuItem.Enabled,
            "the stored ON setting was not restored after a restart");
        Send(restarted, Obs(QuotaProvider.Claude, 40, sequence: 1));   // remaining value as the first success
        Check(restarted.Shown.Count == 0, "the first success after a restart must be silent");
        Send(restarted, Obs(QuotaProvider.Claude, 100));
        Send(restarted, Obs(QuotaProvider.Claude, 30));
        Check(restarted.Shown.Count == 1, "a recovery after a restart baseline must notify");

        // ---- Shutdown: nothing is accepted, queued, toggled or shown once the app is stopping ----
        restarted.Stopping = true;
        int accepted = restarted.Refreshes;
        Send(restarted, Obs(QuotaProvider.Claude, 100));
        Send(restarted, Obs(QuotaProvider.Claude, 10));
        await restarted.Recovery.ChangeAsync(false);
        restarted.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(restarted.Refreshes == accepted && restarted.Shown.Count == 1 && restarted.Recovery.Idle && restarted.Recovery.Enabled &&
              await store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken),
            "a stopping app must ignore observations and toggles and show nothing");

        // ---- Setting save failure keeps the previous setting and menu check ----
        var failing = new Harness(_ => Task.FromException(new IOException("synthetic save failure")), post);
        await failing.Recovery.LoadAsync(() => Task.FromResult(true));
        await failing.Recovery.ChangeAsync(false);
        Check(failing.Recovery.Enabled && failing.Recovery.Checked && failing.Recovery.MenuItem.Enabled &&
              !failing.Recovery.Changing && failing.Feedback[^1] == (QuotaRecoveryText.SettingSaveFailed, true) &&
              failing.Writes.Count == 0, "a failed save must keep ON, the check and an enabled menu item");
        var failingOn = new Harness(_ => Task.FromException(new IOException("synthetic save failure")), post);
        await failingOn.Recovery.LoadAsync(() => Task.FromResult(false));
        await failingOn.Recovery.ChangeAsync(true);
        Check(!failingOn.Recovery.Enabled && !failingOn.Recovery.Checked, "a failed save must keep OFF and the check unset");
        var throwing = new Harness(_ => throw new IOException("synthetic synchronous failure"), post);
        await throwing.Recovery.LoadAsync(() => Task.FromResult(false));
        await throwing.Recovery.ChangeAsync(true);
        Check(!throwing.Recovery.Enabled && !throwing.Recovery.Changing && throwing.Recovery.MenuItem.Enabled,
            "a save that throws synchronously must not leave the menu locked");
        // An unreadable setting is OFF with a message; the app keeps running.
        var unreadable = new Harness(_ => Task.CompletedTask, post);
        await unreadable.Recovery.LoadAsync(() => Task.FromException<bool>(new InvalidDataException("synthetic invalid value")));
        Check(unreadable.Recovery.Ready && !unreadable.Recovery.Enabled &&
              unreadable.Feedback[^1] == (QuotaRecoveryText.SettingReadFailed, true), "an unreadable setting must be OFF with a message");

        // ---- Permission denied / delivery failure never blocks anything ----
        var denied = new Harness(_ => Task.CompletedTask, post, denied: true);
        await denied.Recovery.LoadAsync(() => Task.FromResult(false));
        await denied.Recovery.ChangeAsync(true);
        Check(denied.Feedback[^1].Text.StartsWith(QuotaRecoveryText.Saved(true), StringComparison.Ordinal) &&
              denied.Feedback[^1].Text.Contains("macOS 알림이 꺼져", StringComparison.Ordinal) && !denied.Feedback[^1].Error,
            "enabling while macOS notifications are off must say so");
        denied.ThrowOnShow = true;
        Send(denied, Obs(QuotaProvider.Codex, 100));
        Send(denied, Obs(QuotaProvider.Codex, 20));
        int refreshes = denied.Refreshes;
        Check(denied.Shown.Count == 0 && denied.Recovery.PendingCount == 0 &&
              denied.Feedback[^1] == (QuotaRecoveryText.DeliveryFailed, true),
            "a delivery failure must consume the alert and report once");
        denied.Tick(60_000);
        Send(denied, Obs(QuotaProvider.Codex, 100));
        Check(denied.Refreshes > refreshes && denied.Shown.Count == 0, "polling/display must continue after a delivery failure");

        // ---- Real monitor: callback off the main thread, in-order and not coalesced, clean shutdown ----
        var live = new Harness(enabled => store.SetQuotaRecoveryNotificationsEnabledAsync(enabled, cancellationToken), post);
        await live.Recovery.LoadAsync(() => store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken));
        await live.Recovery.ChangeAsync(true);
        var client = new ScriptedClient();
        using (var monitor = new AccountQuotaMonitor(client, null))
        {
            live.StateSource = provider => monitor.Snapshot().FirstOrDefault(state => state.Provider == provider);
            var threads = new List<bool>();
            monitor.Succeeded += _ => threads.Add(NSThread.IsMain);
            live.Recovery.Attach(monitor);
            client.Script(100, 35);
            // Block the main thread for both responses: the callback must only enqueue and return, and
            // the two observations must still arrive one by one (not merged into the last one).
            Task first = monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
            Check(first.Wait(TimeSpan.FromSeconds(5)), "the success callback blocked while the main thread was busy");
            Task second = monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
            Check(second.Wait(TimeSpan.FromSeconds(5)), "the second success callback blocked while the main thread was busy");
            Check(live.Shown.Count == 0 && live.Refreshes == 0, "nothing may run on the main thread while it was blocked");
            Pump(live);
            Check(threads.Count == 2 && threads.All(onMain => !onMain), "the success callback must run off the main thread");
            Check(live.Shown.Count == 1 && live.Refreshes == 2,
                "two queued successes must be processed individually, in order, so exhausted -> remaining is not lost");
            // Unsubscribed: later responses are ignored without touching the UI.
            live.Recovery.Detach();
            client.Script(100, 20);
            await monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
            await monitor.RefreshOnceAsync(QuotaProvider.Claude, cancellationToken);
            Pump(live);
            Check(live.Shown.Count == 1 && live.Refreshes == 2 && live.Recovery.PendingCount == 0,
                "a detached controller must ignore later responses");
            // Orderly stop while the polling task owns the gate and the callback is subscribed.
            var running = new Harness(_ => Task.CompletedTask, post);
            await running.Recovery.LoadAsync(() => Task.FromResult(true));
            var polling = new AccountQuotaMonitor(new ScriptedClient(), null);
            running.Recovery.Attach(polling);
            polling.Start();
            DateTime until = DateTime.UtcNow.AddSeconds(5);
            while (running.Refreshes == 0 && DateTime.UtcNow < until)
                NSRunLoop.Main.RunUntil(NSRunLoopMode.Default, NSDate.FromTimeIntervalSinceNow(0.02));
            Check(running.Refreshes > 0, "the polling monitor never delivered a live success");
            running.Recovery.Detach();
            Task stop = polling.StopAsync();
            Check(await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))) == stop, "StopAsync did not finish with the callback attached");
            polling.Dispose();
        }

        await store.SetQuotaRecoveryNotificationsEnabledAsync(false, cancellationToken);
        return "quota recovery (menu item + default OFF, first/cache silent, exhausted->remaining once, duplicate/re-exhaust, " +
               "existing alerts first, queued alert dropped on re-exhaust/period change/failed query/toggle, restart, save-failure keeps setting, " +
               "denied/delivery failure non-blocking, callback off-main + ordered + not coalesced, detach, orderly stop)";
    }

    // ---- Early reset (#70) in the shared controller ----------------------------------------------------
    private static readonly Dictionary<QuotaProvider, long> earlySequences = [];

    private static QuotaWindow EW(string id, double used, DateTimeOffset reset, int minutes = 10080) =>
        new(id, "합성 " + id, used, reset, minutes);

    private static QuotaObservation EO(QuotaProvider provider, DateTimeOffset at, long continuity, params QuotaWindow[] windows) =>
        new(provider, earlySequences[provider] = earlySequences.GetValueOrDefault(provider) + 1, at,
            new QuotaReading(provider, windows), continuity);

    // A live success that carries the previous successful reading (a restart with a cache, or the monitor's own).
    private static QuotaObservation EOP(QuotaProvider provider, DateTimeOffset at, long continuity, QuotaPreviousSuccess previous,
        params QuotaWindow[] windows) =>
        new(provider, earlySequences[provider] = earlySequences.GetValueOrDefault(provider) + 1, at,
            new QuotaReading(provider, windows), continuity, previous);

    // Two verified successes 30 minutes apart, then the early reset: before = baseline, after = decision.
    private static void EBefore(Harness harness, QuotaProvider provider, long continuity = 1, params QuotaWindow[] windows) =>
        Send(harness, EO(provider, DateTimeOffset.UtcNow.AddMinutes(-30), continuity, windows));

    private static void EAfter(Harness harness, QuotaProvider provider, long continuity = 1, params QuotaWindow[] windows) =>
        Send(harness, EO(provider, DateTimeOffset.UtcNow, continuity, windows));

    internal static async Task<string> VerifyEarlyResetAsync(UsageStore store, MacQuotaNotifications real, NSMenu contextMenu,
        Action<Action> post, CancellationToken cancellationToken)
    {
        DateTimeOffset origin = DateTimeOffset.UtcNow;
        DateTimeOffset reset0 = origin.AddDays(3);
        DateTimeOffset reset1 = origin.AddDays(6.9);
        // A 5 hour window never produces an early reset (#82); recovery still treats it as before.
        DateTimeOffset five = origin.AddHours(2);
        const QuotaProvider P = QuotaProvider.Claude;
        Harness NewHarness(bool denied = false, Func<bool, Task>? saveEarly = null, Func<bool, Task>? saveRecovery = null) =>
            new(saveRecovery ?? (enabled => store.SetQuotaRecoveryNotificationsEnabledAsync(enabled, cancellationToken)), post, denied,
                saveEarly ?? (enabled => store.SetQuotaEarlyResetNotificationsEnabledAsync(enabled, cancellationToken)));
        Func<Task<bool>> readEarly = () => store.GetQuotaEarlyResetNotificationsEnabledAsync(cancellationToken);
        Func<Task<bool>> readRecovery = () => store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken);

        // ---- The app's own controller: second menu item right after recovery, shared label/detail, default OFF ----
        int recoveryAt = Array.IndexOf(contextMenu.Items, real.MenuItem);
        Check(real.EarlyResetReady && !real.EarlyResetEnabled && !real.EarlyResetChecked && real.EarlyResetMenuItem.Enabled &&
              recoveryAt >= 0 && Array.IndexOf(contextMenu.Items, real.EarlyResetMenuItem) == recoveryAt + 1,
            "the early-reset item is not wired right after the recovery item, loaded and OFF by default");
        Check(real.EarlyResetMenuItem.Title == QuotaEarlyResetText.MenuLabel && real.EarlyResetMenuItem.ToolTip == QuotaEarlyResetText.MenuDetail,
            "the early-reset menu item does not use the shared label and detail");
        Check(!await store.GetQuotaEarlyResetNotificationsEnabledAsync(cancellationToken), "a fresh database must read the early-reset setting OFF");

        // ---- Default OFF: nothing is decided, shown, queued or recorded ----
        var h = NewHarness();
        await h.Recovery.LoadAsync(readRecovery);
        await h.Recovery.LoadEarlyResetAsync(readEarly);
        Check(h.Recovery.EarlyResetReady && !h.Recovery.EarlyResetEnabled && !h.Recovery.EarlyResetChecked && !h.Recovery.Enabled,
            "both settings must load OFF");
        EBefore(h, P, 1, EW("weekly_all", 60, reset0));
        EAfter(h, P, 1, EW("weekly_all", 4, reset1));
        Check(h.Shown.Count == 0 && h.Recovery.PendingCount == 0 && h.Recovery.RecentEarlyResets().Count == 0,
            "OFF must not queue, show or remember an early reset");
        Check(h.Decisions.Count == 2 && h.Decisions.All(d => d is { Outcome: "Excluded", Reason: "Disabled" }),
            "while OFF every success must be closed in the decision log as Excluded/Disabled");

        // ---- Turn ON: persisted, independent of recovery, new baseline, feedback ----
        int recoveryEpoch = h.Recovery.Epoch;
        int earlyEpoch = h.Recovery.EarlyResetEpoch;
        await h.Recovery.ChangeEarlyResetAsync(true);
        Check(h.Recovery.EarlyResetEnabled && h.Recovery.EarlyResetChecked && h.Recovery.EarlyResetEpoch > earlyEpoch &&
              h.Recovery.Epoch == recoveryEpoch && !h.Recovery.Enabled && !h.Recovery.Checked && !h.Recovery.EarlyResetChanging &&
              await store.GetQuotaEarlyResetNotificationsEnabledAsync(cancellationToken) &&
              !await store.GetQuotaRecoveryNotificationsEnabledAsync(cancellationToken) &&
              h.Feedback[^1] == (QuotaEarlyResetText.Saved(true), false) && h.Writes.Count == 0,
            "turning early reset ON must persist, check its item, advance only its epoch and leave recovery OFF");
        EBefore(h, P, 1, EW("weekly_all", 60, reset0));          // first success after enabling: baseline only
        Check(h.Shown.Count == 0 && h.Recovery.PendingCount == 0 && h.Decisions[^1] is { Outcome: "Excluded", Reason: "NoPreviousSuccess" },
            "the first success after enabling must stay silent and be recorded as NoPreviousSuccess");
        EAfter(h, P, 1, EW("weekly_all", 4, reset1));            // 60% -> 4%, reset moved later
        Check(h.Shown.Count == 1 && h.Shown[0].EarlyReset is not null && h.Shown[0].Recovery is null &&
              h.Shown[0].Title == QuotaEarlyResetText.Title(P) && !h.Shown[0].Body.Any(char.IsDigit) &&
              !h.Shown[0].Body.Contains('%') && h.Recovery.PendingCount == 0 &&
              h.Recovery.RecentEarlyResets().TryGetValue(P, out QuotaEarlyResetAlert? recent) && recent.Windows.Count == 1,
            "an early reset must request exactly one banner with the shared title, no numbers, and be remembered for the detail");
        Check(h.Decisions[^1] is { Outcome: "Alert", Reason: "Alert" }, "the alert must be recorded once as Alert/Alert");
        EAfter(h, P, 1, EW("weekly_all", 4, reset1));            // the next success with unchanged values
        Check(h.Shown.Count == 1 && h.Decisions[^1] is { Outcome: "Excluded", Reason: "UsageNotDecreased" },
            "an unchanged value after the early reset must not notify again (UsageNotDecreased)");

        // ---- Early reset and recovery on the same response: one banner ----
        await h.Recovery.ChangeAsync(true);
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        EBefore(h, P, 1, EW("weekly_all", 100, reset0), EW("other", 100, five, 300));
        EAfter(h, P, 1, EW("weekly_all", 4, reset1), EW("other", 50, five, 300));
        Check(h.Shown.Count == 2 && h.Shown[1].Recovery is not null && h.Shown[1].EarlyReset is not null &&
              h.Shown[1].Title == QuotaEarlyResetText.Title(P) &&
              h.Shown[1].Body.Contains("다른 한도에도 잔여량이 확인되었습니다", StringComparison.Ordinal),
            "a recovery and an early reset from one response must become ONE banner with the early-reset title");
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        EBefore(h, P, 1, EW("weekly_all", 100, reset1));
        EAfter(h, P, 1, EW("weekly_all", 5, reset1.AddMinutes(30)));   // same window recovers and resets: not listed twice
        Check(h.Shown.Count == 3 && h.Shown[2].Recovery is not null && h.Shown[2].EarlyReset is not null &&
              !h.Shown[2].Body.Contains("다른 한도에도 잔여량이 확인되었습니다", StringComparison.Ordinal),
            "the same window in both must not be listed twice");
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        EBefore(h, P, 1, EW("other", 100, five, 300));
        EAfter(h, P, 1, EW("other", 55, five, 300));                    // a 5 hour recovery: never an early reset
        Check(h.Shown.Count == 4 && h.Shown[3].Recovery is not null && h.Shown[3].EarlyReset is null &&
              h.Shown[3].Title == QuotaRecoveryText.Title(P), "a recovery alone must keep the recovery title and text");

        // ---- Existing alerts first, then the quota alert, 6.5 seconds apart ----
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        h.Order.Clear();
        h.Existing();
        EBefore(h, P, 1, EW("weekly_all", 60, reset0));
        EAfter(h, P, 1, EW("weekly_all", 4, reset1));
        Check(h.Shown.Count == 4 && h.Recovery.PendingCount == 1, "an early-reset banner must wait behind an existing alert");
        h.Tick(MacQuotaNotifications.DeferMilliseconds - 1);
        Check(h.Shown.Count == 4, "the banner must not appear before the deferral has elapsed");
        h.Tick(1);
        Check(h.Shown.Count == 5 && h.Order.SequenceEqual(["existing", "quota"]) && h.Recovery.PendingCount == 0,
            "the banner must appear after the existing alert and the deferral");

        // ---- A queued early-reset banner that is no longer true is dropped, never shown ----
        void Queue(long continuity = 1, params QuotaWindow[] extra)
        {
            h.Tick(MacQuotaNotifications.DeferMilliseconds);
            h.Existing();
            EBefore(h, P, continuity, [EW("weekly_all", 60, reset0), .. extra]);
            EAfter(h, P, continuity, [EW("weekly_all", 4, reset1), .. extra]);
            Check(h.Recovery.PendingCount == 1, "the early-reset banner was not queued behind the deferral");
        }
        int shown = h.Shown.Count;
        Queue();
        h.States[P] = h.States[P] with { IsPrevious = true, Error = "합성 실패" };      // a failed later query
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown && h.Recovery.PendingCount == 0, "a failed later query must drop the queued banner");
        Queue();
        h.States[P] = h.States[P] with { ContinuityId = 2 };                             // a read failed in between
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown && h.Recovery.PendingCount == 0, "a broken read sequence must drop the queued banner");
        Queue();
        EAfter(h, P, 1, EW("weekly_all", 4, reset1), EW("added", 4, reset1));            // window set changed
        Check(h.Recovery.PendingCount == 0, "a changed window set must drop the queued banner");
        Queue();
        EAfter(h, P, 1, EW("weekly_all", 100, reset1));                                  // exhausted again before delivery
        Check(h.Recovery.PendingCount == 0, "re-exhaustion must drop the queued banner");
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown, "a dropped banner must not be shown later");
        // #82: there is no 90 minute expiry any more (a banner stays true while its conditions hold) ...
        Queue();
        h.ClockOffset = Harness.BaseClockOffset + TimeSpan.FromMinutes(91);
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown + 1 && h.Recovery.PendingCount == 0, "a banner older than 90 minutes must still be shown while it is true");
        h.ClockOffset = Harness.BaseClockOffset;
        shown = h.Shown.Count;
        // ... but it is dropped once the previous scheduled reset has passed or the usage is not below the earlier value again.
        Queue();
        h.ClockOffset = Harness.BaseClockOffset + TimeSpan.FromDays(3);
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown && h.Recovery.PendingCount == 0, "a banner whose previous scheduled reset has passed must be dropped");
        h.ClockOffset = Harness.BaseClockOffset;
        Queue();
        EAfter(h, P, 1, EW("weekly_all", 61, reset1));                                   // back above the earlier 60%
        Check(h.Recovery.PendingCount == 0, "a usage that is no longer below the earlier value must drop the queued banner");
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown, "a dropped banner must not be shown later");
        Queue();
        h.States[P] = h.States[P] with { IsRefreshing = true };                          // a read is in flight
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown && h.Recovery.PendingCount == 1, "an in-flight read must hold the banner, not show or drop it");
        h.States[P] = h.States[P] with { IsRefreshing = false };
        h.Tick(1);
        Check(h.Shown.Count == shown + 1 && h.Recovery.PendingCount == 0, "the held banner must appear once the read has finished");
        shown = h.Shown.Count;
        // Only the latest summary per Provider is kept.
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        h.Existing();
        EBefore(h, P, 1, EW("weekly_all", 60, reset0));
        EAfter(h, P, 1, EW("weekly_all", 4, reset1));
        EBefore(h, P, 1, EW("weekly_all", 60, reset0));
        EAfter(h, P, 1, EW("weekly_all", 3, reset1));
        Check(h.Recovery.PendingCount == 1, "only one summary per Provider may be queued");
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown + 1, "the latest queued summary must be shown once");
        shown = h.Shown.Count;

        // ---- Each kind is invalidated by its own setting only ----
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        h.Existing();
        EBefore(h, P, 1, EW("weekly_all", 100, reset0), EW("other", 100, five, 300));
        EAfter(h, P, 1, EW("weekly_all", 4, reset1), EW("other", 50, five, 300));
        Check(h.Recovery.PendingCount == 1, "the combined banner was not queued");
        await h.Recovery.ChangeEarlyResetAsync(false);                                   // early OFF: recovery part survives
        Check(h.Recovery.PendingCount == 1 && h.Recovery.Enabled && !h.Recovery.EarlyResetEnabled &&
              h.Recovery.RecentEarlyResets().ContainsKey(P), "turning early reset OFF must keep the recovery part and the remembered record");
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown + 1 && h.Shown[^1].EarlyReset is null && h.Shown[^1].Recovery is not null &&
              h.Shown[^1].Title == QuotaRecoveryText.Title(P), "after early reset is turned OFF only the recovery text may appear");
        shown = h.Shown.Count;
        await h.Recovery.ChangeEarlyResetAsync(true);
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        h.Existing();
        EBefore(h, P, 1, EW("weekly_all", 100, reset0), EW("other", 100, five, 300));
        EAfter(h, P, 1, EW("weekly_all", 4, reset1), EW("other", 50, five, 300));
        await h.Recovery.ChangeAsync(false);                                             // recovery OFF: early part survives
        Check(h.Recovery.PendingCount == 1 && !h.Recovery.Enabled && h.Recovery.EarlyResetEnabled,
            "turning recovery OFF must keep the early-reset part");
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown + 1 && h.Shown[^1].Recovery is null && h.Shown[^1].EarlyReset is not null,
            "after recovery is turned OFF only the early-reset banner may appear");
        shown = h.Shown.Count;
        // Turning early reset OFF with its own banner queued drops it; re-enabling must not replay the earlier pair.
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        h.Existing();
        EBefore(h, P, 1, EW("weekly_all", 60, reset0));
        EAfter(h, P, 1, EW("weekly_all", 4, reset1));
        QuotaObservation inFlight = EO(P, DateTimeOffset.UtcNow, 1, EW("weekly_all", 4, reset1));
        h.Deliver(inFlight);                                                             // enqueued, not yet drained
        await h.Recovery.ChangeEarlyResetAsync(false);
        Pump(h);
        Check(h.Decisions.Any(d => d.Reason == "SettingChangedBeforeEvaluation"), "a response handed over before a toggle must be closed as SettingChangedBeforeEvaluation");
        Check(h.Recovery.PendingCount == 0 && !h.Recovery.EarlyResetEnabled && !h.Recovery.EarlyResetChecked &&
              !await store.GetQuotaEarlyResetNotificationsEnabledAsync(cancellationToken) &&
              h.Feedback[^1] == (QuotaEarlyResetText.Saved(false), false), "turning early reset OFF must persist and clear its queued banner");
        h.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(h.Shown.Count == shown, "turning OFF must not show a queued banner");
        await h.Recovery.ChangeEarlyResetAsync(true);
        EAfter(h, P, 1, EW("weekly_all", 3, reset1.AddHours(1)));                        // first success after re-enabling
        Check(h.Shown.Count == shown, "re-enabling must not replay an earlier early reset");

        // ---- #82 weekly rule in the controller: any strict decrease of the weekly window, never the 5 hour window ----
        var w = NewHarness();
        await w.Recovery.LoadEarlyResetAsync(() => Task.FromResult(true));
        EBefore(w, P, 1, EW("weekly_all", 17, reset0));
        EAfter(w, P, 1, EW("weekly_all", 16, reset0));                                   // 1 point, same reset time
        Check(w.Shown.Count == 1 && w.Shown[0].EarlyReset is not null && w.Decisions[^1] is { Outcome: "Alert", Reason: "Alert" },
            "a 1 point decrease of the weekly window must notify even with an unchanged reset time (no 20 point / 10% / reset-change rule)");
        w.Tick(MacQuotaNotifications.DeferMilliseconds);
        EBefore(w, P, 1, EW("five_hour", 60, five, 300));
        EAfter(w, P, 1, EW("five_hour", 4, five.AddHours(5), 300));
        Check(w.Shown.Count == 1 && w.Decisions[^1] is { Outcome: "Excluded", Reason: "NoWeeklyWindow" }, "a 5 hour window must never be an early reset");
        EBefore(w, P, 1, EW("weekly_all", 0, reset0));
        EAfter(w, P, 1, EW("weekly_all", 0, reset1));                                    // an unused window whose reset moves
        Check(w.Shown.Count == 1 && w.Decisions[^1] is { Outcome: "Excluded", Reason: "UsageNotDecreased" }, "an unused 0% window with a moving reset must not notify");
        EBefore(w, P, 1, EW("weekly_all", 60, origin.AddMinutes(4)));
        EAfter(w, P, 1, EW("weekly_all", 5, reset1));                                    // the previous reset was about to happen: normal reset
        Check(w.Shown.Count == 1 && w.Decisions[^1] is { Outcome: "Excluded", Reason: "NotBeforeScheduledReset" }, "a normal scheduled reset must not notify");
        EBefore(w, P, 1, EW("weekly_all", 60, reset0));
        EAfter(w, P, 2, EW("weekly_all", 5, reset1));                                    // another continuity id: a read failed in between
        Check(w.Shown.Count == 1 && w.Decisions[^1] is { Outcome: "Excluded", Reason: "ContinuityChanged" }, "a changed continuity id must not notify");

        // ---- #82 restart cache vs setting toggle: the cache is compared on a restart, never after a toggle ----
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        QuotaPreviousSuccess cached = new(new QuotaReading(P, [EW("weekly_all", 17, reset0)]), nowUtc.AddDays(-2), 1);
        var restartedWithCache = NewHarness();
        await restartedWithCache.Recovery.LoadEarlyResetAsync(() => Task.FromResult(true));
        Send(restartedWithCache, EOP(P, nowUtc, 1, cached, EW("weekly_all", 0, reset1)));
        Check(restartedWithCache.Shown.Count == 1 && restartedWithCache.Shown[0].EarlyReset is not null &&
              restartedWithCache.Decisions[^1] is { Outcome: "Alert", Reason: "Alert" },
            "a restart with a cached 17% must notify for a 0% first success");
        var toggled = NewHarness();
        await toggled.Recovery.LoadEarlyResetAsync(() => Task.FromResult(true));
        await toggled.Recovery.ChangeEarlyResetAsync(false);
        await toggled.Recovery.ChangeEarlyResetAsync(true);
        Send(toggled, EOP(P, nowUtc, 1, cached, EW("weekly_all", 0, reset1)));
        Check(toggled.Shown.Count == 0 && toggled.Decisions[^1] is { Outcome: "Excluded", Reason: "NoPreviousSuccess" },
            "the first success after OFF -> ON must only set the baseline, not compare with the cache");
        var cachedZero = NewHarness();
        await cachedZero.Recovery.LoadEarlyResetAsync(() => Task.FromResult(true));
        Send(cachedZero, EOP(P, nowUtc, 1, new(new QuotaReading(P, [EW("weekly_all", 0, reset0)]), nowUtc.AddDays(-2), 1), EW("weekly_all", 0, reset0)));
        Check(cachedZero.Shown.Count == 0 && cachedZero.Decisions[^1] is { Reason: "UsageNotDecreased" }, "a 0% cache and a 0% first success must not notify");

        // ---- #82 decision log: a response handed over but dropped at shutdown is closed too ----
        var detaching = NewHarness();
        await detaching.Recovery.LoadEarlyResetAsync(() => Task.FromResult(true));
        detaching.Deliver(EO(P, DateTimeOffset.UtcNow, 1, EW("weekly_all", 4, reset1)));      // handed over, not yet drained
        detaching.Recovery.Detach();
        Pump(detaching);
        Check(detaching.Decisions.Count == 1 && detaching.Decisions[0] is { Outcome: "Excluded", Reason: "HostUnavailable" } &&
              detaching.Shown.Count == 0, "a response dropped by Detach must be closed as HostUnavailable");

        // ---- Restart: the stored ON is read back, memory (including the record for the detail) starts fresh ----
        var restarted = NewHarness();
        await restarted.Recovery.LoadAsync(readRecovery);
        await restarted.Recovery.LoadEarlyResetAsync(readEarly);
        Check(restarted.Recovery.EarlyResetEnabled && restarted.Recovery.EarlyResetChecked && restarted.Recovery.EarlyResetMenuItem.Enabled &&
              restarted.Recovery.RecentEarlyResets().Count == 0, "the stored ON was not restored, or the record survived a restart");
        EAfter(restarted, P, 1, EW("weekly_all", 4, reset1));                            // already low: first success is only a baseline
        Check(restarted.Shown.Count == 0 && restarted.Decisions[^1] is { Outcome: "Excluded", Reason: "NoPreviousSuccess" },
            "the first success after a restart without a cache must be silent");

        // ---- Shutdown: nothing is accepted, queued, toggled or shown once the app is stopping ----
        restarted.Stopping = true;
        int accepted = restarted.Refreshes;
        EBefore(restarted, P, 1, EW("weekly_all", 60, reset0));
        EAfter(restarted, P, 1, EW("weekly_all", 4, reset1));
        await restarted.Recovery.ChangeEarlyResetAsync(false);
        restarted.Tick(MacQuotaNotifications.DeferMilliseconds);
        Check(restarted.Decisions.Count(d => d.Reason == "HostUnavailable") == 2, "observations ignored while stopping must be closed as HostUnavailable");
        Check(restarted.Refreshes == accepted && restarted.Shown.Count == 0 && restarted.Recovery.Idle && restarted.Recovery.EarlyResetEnabled &&
              await store.GetQuotaEarlyResetNotificationsEnabledAsync(cancellationToken),
            "a stopping app must ignore observations and toggles and show nothing");

        // ---- Save / read failures keep the previous setting ----
        var failing = NewHarness(saveEarly: _ => Task.FromException(new IOException("synthetic save failure")));
        await failing.Recovery.LoadEarlyResetAsync(() => Task.FromResult(true));
        await failing.Recovery.ChangeEarlyResetAsync(false);
        Check(failing.Recovery.EarlyResetEnabled && failing.Recovery.EarlyResetChecked && failing.Recovery.EarlyResetMenuItem.Enabled &&
              !failing.Recovery.EarlyResetChanging && failing.Feedback[^1] == (QuotaEarlyResetText.SettingSaveFailed, true) && failing.Writes.Count == 0,
            "a failed save must keep ON, the check and an enabled menu item");
        var throwing = NewHarness(saveEarly: _ => throw new IOException("synthetic synchronous failure"));
        await throwing.Recovery.LoadEarlyResetAsync(() => Task.FromResult(false));
        await throwing.Recovery.ChangeEarlyResetAsync(true);
        Check(!throwing.Recovery.EarlyResetEnabled && !throwing.Recovery.EarlyResetChanging && throwing.Recovery.EarlyResetMenuItem.Enabled,
            "a save that throws synchronously must not leave the menu locked");
        var unreadable = NewHarness();
        await unreadable.Recovery.LoadEarlyResetAsync(() => Task.FromException<bool>(new InvalidDataException("synthetic invalid value")));
        Check(unreadable.Recovery.EarlyResetReady && !unreadable.Recovery.EarlyResetEnabled &&
              unreadable.Feedback[^1] == (QuotaEarlyResetText.SettingReadFailed, true), "an unreadable setting must be OFF with a message");

        // ---- Permission denied / delivery failure never blocks anything ----
        var denied = NewHarness(denied: true, saveEarly: _ => Task.CompletedTask);
        await denied.Recovery.LoadEarlyResetAsync(() => Task.FromResult(false));
        await denied.Recovery.ChangeEarlyResetAsync(true);
        Check(denied.Feedback[^1].Text.StartsWith(QuotaEarlyResetText.Saved(true), StringComparison.Ordinal) &&
              denied.Feedback[^1].Text.Contains("macOS 알림이 꺼져", StringComparison.Ordinal) && !denied.Feedback[^1].Error,
            "enabling while macOS notifications are off must say so");
        denied.ThrowOnShow = true;
        EBefore(denied, QuotaProvider.Codex, 1, EW("weekly_all", 60, reset0));
        EAfter(denied, QuotaProvider.Codex, 1, EW("weekly_all", 4, reset1));
        int refreshes = denied.Refreshes;
        Check(denied.Shown.Count == 0 && denied.Recovery.PendingCount == 0 &&
              denied.Feedback[^1] == (QuotaEarlyResetText.DeliveryFailed, true), "a delivery failure must consume the banner and report once");
        denied.Tick(60_000);
        EAfter(denied, QuotaProvider.Codex, 1, EW("weekly_all", 4, reset1));
        Check(denied.Refreshes > refreshes && denied.Shown.Count == 0, "polling/display must continue after a delivery failure");

        await store.SetQuotaEarlyResetNotificationsEnabledAsync(false, cancellationToken);
        await store.SetQuotaRecoveryNotificationsEnabledAsync(false, cancellationToken);
        return "early reset controller (second menu item right after recovery + default OFF, independent persisted toggle, first success silent, " +
               "one banner, recovery+early merged once and same window not listed twice, recovery/early alone, existing alerts first + 6.5 s, " +
               "queued banner dropped on failed query/broken sequence/window change/re-exhaustion/passed reset/usage back up (no 90 min limit), held while a read is in flight, latest only, " +
               "weekly-only 1 point decrease and 5 hour exclusion, restart cache vs toggle, decision log reasons, " +
               "kind-isolated toggles, restart clears the record, shutdown, save/read failure, denied/delivery failure)";
    }
}
