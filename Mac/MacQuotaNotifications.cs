using System.Collections.Concurrent;
using AppKit;

namespace AiBurgerClock;

// Mac host for the shared quota notifications: the recovery engine (QuotaRecoveryNotifications, #59) and the
// early-reset engine (QuotaEarlyResetNotifications, #70). Decisions, texts, setting keys and the combined banner
// (QuotaNotificationText.Combine) are shared; this class only owns what a host must: the two menu items, the
// hand-off from the polling task to the main thread, delivery order, and what invalidates a queued alert.
//
// One controller handles both kinds on purpose: a response that is a recovery and an early reset at once must
// become ONE banner per Provider, which two independent controllers could not merge. Each kind keeps its own
// setting, menu item and epoch, so toggling one never discards the other kind's queued work (the Windows host
// follows the same contract).
//
// Threading: everything runs on the main thread except OnSucceeded, which AccountQuotaMonitor calls on
// its polling task while that provider's gate is held. It therefore only checks the shutdown flag,
// enqueues the observation and returns. It never waits for the UI, a refresh or StopAsync.
internal sealed class MacQuotaNotifications
{
    // After any existing alert (schedule, service status, holiday) the quota alert waits this long, so the
    // banners appear one after another and the established notifications always come first.
    internal const int DeferMilliseconds = 6500;

    // Messages that differ per kind.
    internal sealed record SettingText(Func<bool, string> Saved, string ReadFailed, string SaveFailed, string DeliveryFailed);

    // One user setting. The epoch invalidates queued observations and alerts of this kind only.
    internal sealed class Setting
    {
        private readonly Func<bool> isStopping;
        internal int Epoch;
        internal bool Enabled;
        internal bool Ready;
        internal bool Changing;

        internal Setting(string label, string detail, SettingText text, Func<bool, Task> save, Func<bool> isStopping,
            Action<Setting> toggle)
        {
            this.isStopping = isStopping;
            Text = text;
            Save = save;
            Item = new NSMenuItem(label, (_, _) => toggle(this)) { Enabled = false, ToolTip = detail };
        }

        internal NSMenuItem Item { get; }
        internal SettingText Text { get; }
        internal Func<bool, Task> Save { get; }
        internal bool Active => Enabled && Ready && !Changing;
        internal bool Checked => Item.State == NSCellStateValue.On;
        internal int CurrentEpoch => Volatile.Read(ref Epoch);

        internal void RefreshMenu()
        {
            Item.State = Enabled ? NSCellStateValue.On : NSCellStateValue.Off;
            Item.Enabled = Ready && !Changing && !isStopping();
        }
    }

    // At most one summary per provider; a new success or a toggle of its kind invalidates it.
    private sealed record Pending(QuotaRecoveryAlert? Recovery, QuotaEarlyResetAlert? EarlyReset, QuotaObservation Observation);

    private readonly QuotaRecoveryNotifications recoveryEngine = new();
    private readonly QuotaEarlyResetNotifications earlyResetEngine = new();
    private readonly Func<bool> isStopping;
    private readonly Func<QuotaProvider, QuotaState?> stateOf;
    private readonly Action<QuotaNotificationAlert> show;
    private readonly Action<string, bool> feedback;
    private readonly Action<Action> post;
    private readonly Action refreshDisplay;
    private readonly HashSet<Task> pendingWrites;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<long> milliseconds;
    private readonly Func<bool> notificationsDenied;
    private readonly Action<QuotaObservation, string, string>? recordDecision;
    private readonly Setting recovery;
    private readonly Setting earlyReset;
    private readonly Dictionary<QuotaProvider, Pending> pending = [];
    // Every accepted live response is delivered, in order. Unlike a coalesced display refresh, dropping
    // one could hide the exhausted -> recovered pair or an early-reset pair.
    private readonly ConcurrentQueue<(int RecoveryEpoch, int EarlyResetEpoch, QuotaObservation Observation)> queue = new();
    private AccountQuotaMonitor? monitor;
    private bool detached;
    private int drainScheduled;
    private long notBefore;

    internal MacQuotaNotifications(Func<bool> isStopping, Func<bool, Task> saveRecovery, Func<bool, Task> saveEarlyReset,
        Func<QuotaProvider, QuotaState?> stateOf, Action<QuotaNotificationAlert> show, Action<string, bool> feedback,
        Action<Action> post, Action refreshDisplay, HashSet<Task> pendingWrites, Func<DateTimeOffset>? utcNow = null,
        Func<long>? milliseconds = null, Func<bool>? notificationsDenied = null,
        Action<QuotaObservation, string, string>? recordDecision = null)
    {
        this.isStopping = isStopping;
        this.stateOf = stateOf;
        this.show = show;
        this.feedback = feedback;
        this.post = post;
        this.refreshDisplay = refreshDisplay;
        this.pendingWrites = pendingWrites;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.milliseconds = milliseconds ?? (() => Environment.TickCount64);
        this.notificationsDenied = notificationsDenied ?? (() => false);
        this.recordDecision = recordDecision;
        recovery = new(QuotaRecoveryText.MenuLabel, QuotaRecoveryText.MenuDetail,
            new(QuotaRecoveryText.Saved, QuotaRecoveryText.SettingReadFailed, QuotaRecoveryText.SettingSaveFailed,
                QuotaRecoveryText.DeliveryFailed),
            saveRecovery, isStopping, setting => _ = ChangeAsync(setting, !setting.Enabled));
        earlyReset = new(QuotaEarlyResetText.MenuLabel, QuotaEarlyResetText.MenuDetail,
            new(QuotaEarlyResetText.Saved, QuotaEarlyResetText.SettingReadFailed, QuotaEarlyResetText.SettingSaveFailed,
                QuotaEarlyResetText.DeliveryFailed),
            saveEarlyReset, isStopping, setting => _ = ChangeAsync(setting, !setting.Enabled));
    }

    // Recovery setting (kept as the plain members the earlier wiring used).
    internal NSMenuItem MenuItem => recovery.Item;
    internal bool Enabled => recovery.Enabled;
    internal bool Ready => recovery.Ready;
    internal bool Changing => recovery.Changing;
    internal int Epoch => recovery.CurrentEpoch;
    internal bool Checked => recovery.Checked;

    // Early-reset setting.
    internal NSMenuItem EarlyResetMenuItem => earlyReset.Item;
    internal bool EarlyResetEnabled => earlyReset.Enabled;
    internal bool EarlyResetReady => earlyReset.Ready;
    internal bool EarlyResetChanging => earlyReset.Changing;
    internal int EarlyResetEpoch => earlyReset.CurrentEpoch;
    internal bool EarlyResetChecked => earlyReset.Checked;

    internal int PendingCount => pending.Count;
    // Smoke: no queued observation and no scheduled drain.
    internal bool Idle => queue.IsEmpty && Volatile.Read(ref drainScheduled) == 0;

    // The latest early-reset record per Provider seen during this run (in memory only), for the Provider detail.
    internal IReadOnlyDictionary<QuotaProvider, QuotaEarlyResetAlert> RecentEarlyResets()
    {
        var recent = new Dictionary<QuotaProvider, QuotaEarlyResetAlert>();
        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
            if (earlyResetEngine.Recent(provider) is { } alert) recent[provider] = alert;
        return recent;
    }

    // Reads each stored setting once at startup. A missing key is OFF; an unreadable or invalid value is
    // reported and also OFF (never a silent ON). Monitoring and the app keep running either way.
    internal Task LoadAsync(Func<Task<bool>> read) => LoadAsync(recovery, read);
    internal Task LoadEarlyResetAsync(Func<Task<bool>> read) => LoadAsync(earlyReset, read);

    private async Task LoadAsync(Setting setting, Func<Task<bool>> read)
    {
        bool value = false;
        bool failed = false;
        try { value = await read(); }
        catch (OperationCanceledException) when (isStopping()) { return; }
        catch { failed = true; }
        if (isStopping()) return;
        setting.Enabled = !failed && value;
        setting.Ready = true;
        if (failed) feedback(setting.Text.ReadFailed, true);
        setting.RefreshMenu();
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
        Interlocked.Increment(ref recovery.Epoch);
        Interlocked.Increment(ref earlyReset.Epoch);
        if (monitor is { } attached) attached.Succeeded -= OnSucceeded;
        pending.Clear();
        // A response that was handed over but not evaluated is closed in the decision log, not left open.
        while (queue.TryDequeue(out var dropped)) Record(dropped.Observation, "Excluded", "HostUnavailable");
        monitor = null;
    }

    // The one row the monitor appended for this read is finished with the decision (no usage, reset time or
    // name is written). It is a decision record, not proof that macOS showed a banner. Safe if no log exists.
    private void Record(QuotaObservation observation, string outcome, string reason)
    {
        if (recordDecision is not null) recordDecision(observation, outcome, reason);
        else monitor?.RecordEarlyResetDecision(observation, outcome, reason);
    }

    // Polling task, provider gate held, outside the monitor's state lock. Hand-off only.
    internal void OnSucceeded(QuotaObservation observation)
    {
        if (detached || isStopping())
        {
            Record(observation, "Excluded", "HostUnavailable");
            return;
        }
        queue.Enqueue((recovery.CurrentEpoch, earlyReset.CurrentEpoch, observation));
        if (Interlocked.Exchange(ref drainScheduled, 1) == 0) post(Drain);
    }

    private void Drain()
    {
        // Reset first: an observation enqueued while this loop runs schedules another pass.
        Volatile.Write(ref drainScheduled, 0);
        while (queue.TryDequeue(out var item))
            Process(item.RecoveryEpoch, item.EarlyResetEpoch, item.Observation);
    }

    private void Process(int observedRecoveryEpoch, int observedEarlyResetEpoch, QuotaObservation observation)
    {
        // The hand-off can outlive a toggle or shutdown: check again where an alert would be created. A kind
        // whose setting changed since the response was queued is skipped; the other kind is still decided.
        if (detached || isStopping())
        {
            Record(observation, "Excluded", "HostUnavailable");
            return;
        }
        bool recoveryCurrent = observedRecoveryEpoch == recovery.CurrentEpoch;
        bool earlyResetCurrent = observedEarlyResetEpoch == earlyReset.CurrentEpoch;
        if (!earlyResetCurrent) Record(observation, "Excluded", "SettingChangedBeforeEvaluation");
        if (!recoveryCurrent && !earlyResetCurrent) return;
        // Invalidate at every success, not only at delivery: if a window disappears or changes period and
        // later returns, an alert queued for the earlier state must stay gone.
        if (pending.TryGetValue(observation.Provider, out Pending? queued))
            Keep(observation.Provider, Current(queued, observation.Reading, observation.CheckedAtUtc, observation.ContinuityId));
        QuotaRecoveryAlert? recovered = recoveryCurrent ? recoveryEngine.Observe(observation, recovery.Active) : null;
        QuotaEarlyResetAlert? early = null;
        if (earlyResetCurrent)
        {
            QuotaEarlyResetEvaluation evaluation = earlyResetEngine.Evaluate(observation, earlyReset.Active);
            early = evaluation.Alert;
            Record(observation, early is null ? "Excluded" : "Alert", evaluation.Reason);
        }
        if (recovered is not null || early is not null) pending[observation.Provider] = new(recovered, early, observation);
        // The host's normal display pass raises schedule/service alerts first, then ShowPending.
        refreshDisplay();
    }

    // An existing notification was just requested: quota alerts wait behind it.
    internal void DeferAfterExisting() => notBefore = Math.Max(notBefore, milliseconds() + DeferMilliseconds);

    internal void ShowPending()
    {
        if (detached || isStopping()) return;
        foreach (QuotaProvider provider in pending.Keys.OrderBy(provider => provider).ToArray())
        {
            Pending queued = pending[provider];
            QuotaState? state = stateOf(provider);
            // A later failed query, a disappearance, a re-exhaustion or an elapsed reset invalidates a
            // queued summary. A past recovery is never shown as a current availability claim.
            if (state is null || state.IsPrevious || state.Reading is null ||
                state.LastSuccessfulCheckUtc is not { } success || success < queued.Observation.CheckedAtUtc)
            {
                pending.Remove(provider);
                continue;
            }
            // Wait for an in-flight read rather than sending against a partially completed attempt whose
            // eventual failure would break continuity.
            if (state.IsRefreshing) continue;
            Pending? current = Current(queued, state.Reading, utcNow(), state.ContinuityId);
            Keep(provider, current);
            if (current is null) continue;
            QuotaNotificationAlert? alert = QuotaNotificationText.Combine(current.Recovery, current.EarlyReset);
            if (alert is null) { pending.Remove(provider); continue; }
            if (milliseconds() < notBefore) continue;
            pending.Remove(provider);
            try { show(alert); }
            catch (Exception)
            {
                // The observation is consumed. A delivery failure must not retry on every tick or touch polling.
                feedback((alert.EarlyReset is not null ? earlyReset : recovery).Text.DeliveryFailed, true);
            }
            DeferAfterExisting();
            return;
        }
    }

    private void Keep(QuotaProvider provider, Pending? value)
    {
        if (value is null) pending.Remove(provider);
        else pending[provider] = value;
    }

    // Re-validates each kind against the current reading and its own setting; drops what is no longer true.
    // The validity rules are the shared QuotaPendingAlertPolicy; this host keeps settings, epochs, queue and delivery.
    private Pending? Current(Pending queued, QuotaReading reading, DateTimeOffset now, long continuityId)
    {
        if (now < queued.Observation.CheckedAtUtc) return null;
        QuotaRecoveryAlert? recovered = recovery.Active && queued.Recovery is { } alert &&
            QuotaPendingAlertPolicy.RecoveryStillCurrent(alert, queued.Observation.Reading, reading, now) ? alert : null;
        QuotaEarlyResetAlert? early = earlyReset.Active && queued.EarlyReset is { } change &&
            QuotaPendingAlertPolicy.EarlyResetStillCurrent(change, queued.Observation.Reading, reading, now, continuityId) ? change : null;
        return recovered is null && early is null ? null : queued with { Recovery = recovered, EarlyReset = early };
    }

    internal Task ChangeAsync(bool value) => ChangeAsync(recovery, value);
    internal Task ChangeEarlyResetAsync(bool value) => ChangeAsync(earlyReset, value);

    private async Task ChangeAsync(Setting setting, bool value)
    {
        if (isStopping() || !setting.Ready || setting.Changing) return;
        setting.Changing = true;
        // Toggling is a new baseline for this kind: stale callbacks, queued observations and queued alerts go.
        Interlocked.Increment(ref setting.Epoch);
        Invalidate(setting);
        setting.RefreshMenu();
        Task write = Task.CompletedTask;
        try
        {
            write = setting.Save(value);
            pendingWrites.Add(write);
            await write;
            if (isStopping()) return;
            setting.Enabled = value;
            string text = setting.Text.Saved(value);
            if (value && notificationsDenied())
                text += " 단, macOS 알림이 꺼져 있어 배너는 표시되지 않습니다.";
            feedback(text, false);
        }
        catch (Exception)
        {
            // Keep the previous setting and the menu check as they were.
            if (!isStopping()) feedback(setting.Text.SaveFailed, true);
        }
        finally
        {
            pendingWrites.Remove(write);
            // Enabling never replays an event observed while disabled or while saving.
            Interlocked.Increment(ref setting.Epoch);
            Invalidate(setting);
            setting.Changing = false;
            if (!isStopping()) setting.RefreshMenu();
        }
    }

    // Drops this kind's queued alerts and comparison baselines. Already observed early-reset history (Recent) stays.
    private void Invalidate(Setting setting)
    {
        bool isEarly = ReferenceEquals(setting, earlyReset);
        foreach (QuotaProvider provider in pending.Keys.ToArray())
        {
            Pending value = pending[provider];
            value = isEarly ? value with { EarlyReset = null } : value with { Recovery = null };
            Keep(provider, value.Recovery is null && value.EarlyReset is null ? null : value);
        }
        if (isEarly) earlyResetEngine.Reset();
        else recoveryEngine.Reset();
    }
}
