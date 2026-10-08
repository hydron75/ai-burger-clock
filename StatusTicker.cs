namespace AiBurgerClock;

// Why a host asks for a decision; maps the Windows RefreshStatus(notifyOnChange, notifyProviders) calls.
internal enum TickReason
{
    Initial,         // startup, first policy load, suppressed refresh: no notifications   = RefreshStatus(false)
    Timer,           // one-second timer or opening the panel: transition and provider alerts = RefreshStatus(true)
    ProviderChanged, // a monitor Changed event marshalled to the UI thread: same as Timer   = RefreshStatus(true)
    PolicyChanged    // holiday option saved: provider alerts only; a transition is consumed silently
                     //                                                                    = RefreshStatus(false, notifyProviders: true)
}

internal sealed record ProviderAlert(ProviderKind Provider, Recommendation Recommendation,
    bool Recovered, string Title, string Body);

internal sealed record TickResult(
    ScheduleSnapshot Schedule,
    IReadOnlyList<ProviderStatus> Providers,
    bool ScheduleChanged,
    (string Title, string Body)? Transition,
    IReadOnlyList<ProviderAlert> ProviderAlerts,
    TrayAppearance Appearance,
    bool AppearanceChanged);

// Plan PR 7a: the per-second display decision both hosts made separately (Windows RefreshStatus +
// UpdateProviderDisplay, Mac RefreshDisplay), following the Windows rules. It decides only; showing
// notifications, icons, tooltips and panels, the timer and thread marshalling stay in each host.
// Call it on the UI thread only; it is not thread-safe.
internal sealed class StatusTicker
{
    private readonly Func<ProviderKind, string> displayName;
    private readonly bool strictInput;
    private readonly Action<string>? warn;
    private readonly RecommendationNotifications notifications = new();
    private AgentState? lastState;
    private TrayAppearance? lastAppearance;
    private string warnedDuplicates = "";

    // strictInput: tests fail on a duplicated provider. Otherwise (the running app) the first value
    // is used and warn is told once per new set of duplicates, so the per-second path never throws.
    public StatusTicker(Func<ProviderKind, string>? displayName = null, bool strictInput = false, Action<string>? warn = null)
    {
        this.displayName = displayName ?? ProviderNames.Provider;
        this.strictInput = strictInput;
        this.warn = warn;
    }

    public TickResult Tick(ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers, TickReason reason)
    {
        ProviderStatus[] statuses = Normalize(providers);
        bool changed = lastState.HasValue && lastState.Value != schedule.State;
        lastState = schedule.State;
        (string Title, string Body)? transition =
            changed && reason is TickReason.Timer or TickReason.ProviderChanged
                ? TrayPresentation.TransitionNotification(schedule) : null;

        // Every provider is observed on every tick, even when alerts are suppressed, so the last
        // confirmed recommendation and the Schedule boundary stay current.
        bool alertsEnabled = reason != TickReason.Initial && !changed;
        var alerts = new List<ProviderAlert>();
        foreach (ProviderStatus status in statuses)
        {
            Recommendation current = RecommendationPolicy.Calculate(schedule.State, status.Status);
            if (!notifications.Observe(status.Provider, schedule.State, current, alertsEnabled)) continue;
            var (title, body) = TrayPresentation.ProviderNotification(status.Provider, current, status.Reason, displayName);
            alerts.Add(new(status.Provider, current, current == Recommendation.Go, title, body));
        }

        TrayAppearance appearance = TrayPresentation.Calculate(schedule.State, statuses);
        bool appearanceChanged = lastAppearance != appearance;
        lastAppearance = appearance;
        return new(schedule, Array.AsReadOnly(statuses), changed, transition, alerts.AsReadOnly(), appearance, appearanceChanged);
    }

    // One status per provider in ProviderKind order, copied from the input.
    private ProviderStatus[] Normalize(IReadOnlyList<ProviderStatus> providers)
    {
        var first = new Dictionary<ProviderKind, ProviderStatus>();
        var duplicates = new SortedSet<ProviderKind>();
        foreach (ProviderStatus status in providers)
            if (!first.TryAdd(status.Provider, status)) duplicates.Add(status.Provider);

        string key = string.Join(", ", duplicates);
        if (duplicates.Count > 0)
        {
            string message = $"Provider status listed more than once ({key}); the first value is used.";
            if (strictInput) throw new ArgumentException(message, nameof(providers));
            if (key != warnedDuplicates) warn?.Invoke(message);
        }
        warnedDuplicates = key; // Cleared when the input is clean again, so a later repeat warns again.
        return Enum.GetValues<ProviderKind>().Where(first.ContainsKey).Select(provider => first[provider]).ToArray();
    }
}
