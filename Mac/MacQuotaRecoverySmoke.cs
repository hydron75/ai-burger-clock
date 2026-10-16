using AppKit;
using Foundation;

namespace AiBurgerClock;

// Native smoke for the Mac quota-recovery wiring. Synthetic readings and the smoke run's temporary
// database only: no account, CLI, network, notification permission or real delivery is involved.
// Each scenario uses its own MacQuotaRecovery so the app's real controller is only checked for what
// it is wired to (menu item, default OFF); the hand-off, ordering and invalidation rules are the
// same code in every instance.
internal static class MacQuotaRecoverySmoke
{
    private sealed class Harness
    {
        internal readonly List<QuotaRecoveryAlert> Shown = [];
        internal readonly List<(string Text, bool Error)> Feedback = [];
        internal readonly List<string> Order = [];
        internal readonly HashSet<Task> Writes = [];
        internal readonly Dictionary<QuotaProvider, QuotaState> States = [];
        internal readonly MacQuotaRecovery Recovery;
        internal long Clock = 1_000;
        internal int Refreshes;
        internal bool Stopping;
        internal bool ThrowOnShow;
        internal Func<QuotaProvider, QuotaState?>? StateSource;

        internal Harness(Func<bool, Task> save, Action<Action> post, bool denied = false)
        {
            Recovery = new(() => Stopping, save, provider => StateSource?.Invoke(provider) ?? States.GetValueOrDefault(provider),
                alert =>
                {
                    if (ThrowOnShow) throw new InvalidOperationException("synthetic delivery failure");
                    Shown.Add(alert);
                    Order.Add("quota");
                },
                (text, error) => Feedback.Add((text, error)), post,
                // The app's display pass: existing alerts first, then the pending quota alert.
                () => { Refreshes++; Recovery!.ShowPending(); },
                Writes, milliseconds: () => Clock, notificationsDenied: () => denied);
        }

        // A verified live response: what the monitor would publish and what the snapshot would then show.
        internal void Deliver(QuotaObservation observation, bool viaCallback = true)
        {
            States[observation.Provider] = new(observation.Provider, observation.Reading,
                observation.CheckedAtUtc, observation.CheckedAtUtc);
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

    internal static async Task<string> VerifyAsync(UsageStore store, MacQuotaRecovery real, NSMenu contextMenu,
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
        off.Tick(MacQuotaRecovery.DeferMilliseconds);        // the first alert's own spacing has elapsed
        Check(off.Shown.Count == 2, "re-exhaustion followed by recovery must notify again");

        // ---- Existing alerts first, then the quota alert ----
        off.Order.Clear();
        Send(off, Obs(QuotaProvider.Claude, 100));
        off.Existing();                                       // a schedule/service alert was just requested
        Send(off, Obs(QuotaProvider.Claude, 70));
        Check(off.Shown.Count == 2 && off.Recovery.PendingCount == 1, "a quota alert must wait behind an existing alert");
        off.Tick(MacQuotaRecovery.DeferMilliseconds - 1);
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
        off.Tick(MacQuotaRecovery.DeferMilliseconds);
        Check(off.Shown.Count == 3, "a dropped alert must not be shown later");
        // Period change then return: a queued alert from the earlier state is gone for good.
        Send(off, Obs(QuotaProvider.Claude, 100));
        off.Existing();
        Send(off, Obs(QuotaProvider.Claude, 60));
        Send(off, Obs(QuotaProvider.Claude, 60, sessionMinutes: 600));   // same id, other period
        Send(off, Obs(QuotaProvider.Claude, 60));                        // back to the original period
        Check(off.Recovery.PendingCount == 0, "a period change must drop the queued alert even if the period returns");
        off.Tick(MacQuotaRecovery.DeferMilliseconds);
        Check(off.Shown.Count == 3, "the old alert came back after a period round trip");
        // A failed later query (previous value) is not a current availability claim.
        Send(off, Obs(QuotaProvider.Claude, 100));
        off.Existing();
        Send(off, Obs(QuotaProvider.Claude, 40));
        Check(off.Recovery.PendingCount == 1, "recovery was not queued for the failed-query check");
        off.States[QuotaProvider.Claude] = off.States[QuotaProvider.Claude] with { IsPrevious = true, Error = "합성 실패" };
        off.Tick(MacQuotaRecovery.DeferMilliseconds);
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
        off.Tick(MacQuotaRecovery.DeferMilliseconds);
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
        restarted.Tick(MacQuotaRecovery.DeferMilliseconds);
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
}
