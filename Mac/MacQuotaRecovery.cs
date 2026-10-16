using System.Collections.Concurrent;
using AppKit;

namespace AiBurgerClock;

// Mac host for the shared quota-recovery engine (QuotaRecoveryNotifications, #59). The decision, the
// text and the setting key are shared; this class only owns what a host must: the menu item, the
// hand-off from the polling task to the main thread, delivery order, and what invalidates a queued alert.
//
// Threading: everything runs on the main thread except OnSucceeded, which AccountQuotaMonitor calls on
// its polling task while that provider's gate is held. It therefore only checks the shutdown flag,
// enqueues the observation and returns. It never waits for the UI, a refresh or StopAsync.
internal sealed class MacQuotaRecovery
{
    // After any existing alert (schedule, service status, holiday) the quota alert waits this long, so the
    // banners appear one after another and the established notifications always come first.
    internal const int DeferMilliseconds = 6500;

    private readonly QuotaRecoveryNotifications engine = new();
    private readonly Func<bool> isStopping;
    private readonly Func<bool, Task> save;
    private readonly Func<QuotaProvider, QuotaState?> stateOf;
    private readonly Action<QuotaRecoveryAlert> show;
    private readonly Action<string, bool> feedback;
    private readonly Action<Action> post;
    private readonly Action refreshDisplay;
    private readonly HashSet<Task> pendingWrites;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<long> milliseconds;
    private readonly Func<bool> notificationsDenied;
    private readonly NSMenuItem menuItem;
    // At most one summary per provider; a new success or a toggle invalidates it.
    private readonly Dictionary<QuotaProvider, (QuotaRecoveryAlert Alert, QuotaObservation Observation)> pending = [];
    // Every accepted live response is delivered, in order. Unlike a coalesced display refresh, dropping
    // one could hide the exhausted -> recovered pair.
    private readonly ConcurrentQueue<(int Epoch, QuotaObservation Observation)> queue = new();
    private AccountQuotaMonitor? monitor;
    private bool enabled;
    private bool ready;
    private bool changing;
    private bool detached;
    private int epoch;
    private int drainScheduled;
    private long notBefore;

    internal MacQuotaRecovery(Func<bool> isStopping, Func<bool, Task> save, Func<QuotaProvider, QuotaState?> stateOf,
        Action<QuotaRecoveryAlert> show, Action<string, bool> feedback, Action<Action> post, Action refreshDisplay,
        HashSet<Task> pendingWrites, Func<DateTimeOffset>? utcNow = null, Func<long>? milliseconds = null,
        Func<bool>? notificationsDenied = null)
    {
        this.isStopping = isStopping;
        this.save = save;
        this.stateOf = stateOf;
        this.show = show;
        this.feedback = feedback;
        this.post = post;
        this.refreshDisplay = refreshDisplay;
        this.pendingWrites = pendingWrites;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.milliseconds = milliseconds ?? (() => Environment.TickCount64);
        this.notificationsDenied = notificationsDenied ?? (() => false);
        menuItem = new NSMenuItem(QuotaRecoveryText.MenuLabel, (_, _) => _ = ChangeAsync(!enabled))
        {
            Enabled = false, ToolTip = QuotaRecoveryText.MenuDetail
        };
    }

    internal NSMenuItem MenuItem => menuItem;
    internal bool Enabled => enabled;
    internal bool Ready => ready;
    internal bool Changing => changing;
    internal int PendingCount => pending.Count;
    internal int Epoch => Volatile.Read(ref epoch);
    // Smoke: no queued observation and no scheduled drain.
    internal bool Idle => queue.IsEmpty && Volatile.Read(ref drainScheduled) == 0;
    internal bool Checked => menuItem.State == NSCellStateValue.On;

    // Reads the stored setting once at startup. A missing key is OFF; an unreadable or invalid value is
    // reported and also OFF (never a silent ON). Monitoring and the app keep running either way.
    internal async Task LoadAsync(Func<Task<bool>> read)
    {
        bool value = false;
        bool failed = false;
        try { value = await read(); }
        catch (OperationCanceledException) when (isStopping()) { return; }
        catch { failed = true; }
        if (isStopping()) return;
        enabled = !failed && value;
        ready = true;
        if (failed) feedback(QuotaRecoveryText.SettingReadFailed, true);
        RefreshMenu();
    }

    internal void Attach(AccountQuotaMonitor target)
    {
        Detach();
        detached = false;
        monitor = target;
        target.Succeeded += OnSucceeded;
    }

    // Unsubscribes and invalidates everything that is queued or still being delivered. Used at shutdown
    // (before the monitors are awaited) and by the smoke run.
    internal void Detach()
    {
        detached = true;
        Interlocked.Increment(ref epoch);
        if (monitor is { } attached) attached.Succeeded -= OnSucceeded;
        monitor = null;
        pending.Clear();
        while (queue.TryDequeue(out _)) { }
    }

    // Polling task, provider gate held, outside the monitor's state lock. Hand-off only.
    internal void OnSucceeded(QuotaObservation observation)
    {
        if (detached || isStopping()) return;
        queue.Enqueue((Volatile.Read(ref epoch), observation));
        if (Interlocked.Exchange(ref drainScheduled, 1) == 0) post(Drain);
    }

    private void Drain()
    {
        // Reset first: an observation enqueued while this loop runs schedules another pass.
        Volatile.Write(ref drainScheduled, 0);
        while (queue.TryDequeue(out var item))
            Process(item.Epoch, item.Observation);
    }

    private void Process(int observedEpoch, QuotaObservation observation)
    {
        // The hand-off can outlive a toggle or shutdown: check again where the alert would be created.
        if (detached || isStopping() || observedEpoch != Volatile.Read(ref epoch)) return;
        // Invalidate at every success, not only at delivery: if a window disappears or changes period and
        // later returns, an alert queued for the earlier state must stay gone.
        if (pending.TryGetValue(observation.Provider, out var queued) &&
            (observation.CheckedAtUtc < queued.Observation.CheckedAtUtc ||
             !StillCurrent(queued.Alert, queued.Observation.Reading, observation.Reading, observation.CheckedAtUtc)))
            pending.Remove(observation.Provider);
        QuotaRecoveryAlert? alert = engine.Observe(observation, enabled && ready && !changing);
        if (alert is not null) pending[alert.Provider] = (alert, observation);
        // The host's normal display pass raises schedule/service alerts first, then ShowPending.
        refreshDisplay();
    }

    // An existing notification was just requested: quota alerts wait behind it.
    internal void DeferAfterExisting() => notBefore = Math.Max(notBefore, milliseconds() + DeferMilliseconds);

    internal void ShowPending()
    {
        if (detached || isStopping() || !enabled || changing) return;
        foreach (QuotaProvider provider in pending.Keys.OrderBy(provider => provider).ToArray())
        {
            var (alert, observation) = pending[provider];
            QuotaState? state = stateOf(provider);
            // A later failed query, a disappearance, a re-exhaustion or an elapsed reset invalidates a
            // queued summary. A past recovery is never shown as a current availability claim.
            if (state is null || state.IsPrevious || state.Reading is null ||
                state.LastSuccessfulCheckUtc < observation.CheckedAtUtc ||
                !StillCurrent(alert, observation.Reading, state.Reading, utcNow()))
            {
                pending.Remove(provider);
                continue;
            }
            if (milliseconds() < notBefore) continue;
            pending.Remove(provider);
            try { show(alert); }
            catch (Exception)
            {
                // The observation is consumed. A delivery failure must not retry on every tick or touch polling.
                feedback(QuotaRecoveryText.DeliveryFailed, true);
            }
            DeferAfterExisting();
            return;
        }
    }

    private static bool StillCurrent(QuotaRecoveryAlert alert, QuotaReading expected, QuotaReading current,
        DateTimeOffset now) => alert.WindowIds.All(id =>
            current.Windows.Any(window => window.Id == id && window.UsedPercent < 100 &&
                (window.ResetsAtUtc is null || window.ResetsAtUtc > now) &&
                expected.Windows.Any(old => old.Id == id && old.WindowMinutes == window.WindowMinutes)));

    internal async Task ChangeAsync(bool value)
    {
        if (isStopping() || !ready || changing) return;
        changing = true;
        // Toggling is a new baseline: stale callbacks, queued observations and queued alerts all go.
        Interlocked.Increment(ref epoch);
        pending.Clear();
        while (queue.TryDequeue(out _)) { }
        engine.Reset();
        RefreshMenu();
        Task write = Task.CompletedTask;
        try
        {
            write = save(value);
            pendingWrites.Add(write);
            await write;
            if (isStopping()) return;
            enabled = value;
            string text = QuotaRecoveryText.Saved(value);
            if (value && notificationsDenied())
                text += " 단, macOS 알림이 꺼져 있어 배너는 표시되지 않습니다.";
            feedback(text, false);
        }
        catch (Exception)
        {
            // Keep the previous setting and the menu check as they were.
            if (!isStopping()) feedback(QuotaRecoveryText.SettingSaveFailed, true);
        }
        finally
        {
            pendingWrites.Remove(write);
            // Enabling never replays a recovery observed while disabled or while saving.
            Interlocked.Increment(ref epoch);
            pending.Clear();
            while (queue.TryDequeue(out _)) { }
            engine.Reset();
            changing = false;
            if (!isStopping()) RefreshMenu();
        }
    }

    private void RefreshMenu()
    {
        menuItem.State = enabled ? NSCellStateValue.On : NSCellStateValue.Off;
        menuItem.Enabled = ready && !changing && !isStopping();
    }
}
