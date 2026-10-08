using System.Globalization;

namespace AiBurgerClock;

// Golden checks for the shared per-second decision (plan PR 7a). Expected behavior is the Windows
// 2.2.3 TrayApplicationContext.RefreshStatus + UpdateProviderDisplay logic; no UI or notifications.
internal static class StatusTickerTests
{
    private static readonly ScheduleSnapshot Full = Snapshot("2026-06-16T05:00:00Z");     // Tue FULL
    private static readonly ScheduleSnapshot Burger = Snapshot("2026-06-16T14:00:00Z");   // Tue BURGER
    private static readonly ScheduleSnapshot Weekend = Snapshot("2026-06-20T05:00:00Z");  // Sat FULL

    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Status ticker: " + label);
        }

        Check(Full.State == AgentState.FullThrottle && Burger.State == AgentState.BurgerTime &&
            Weekend.State == AgentState.FullThrottle, "fixture schedules");
        FirstTickAndTransitions(Check);
        ProviderAlerts(Check);
        PolicyChanges(Check);
        OrderingAndCopies(Check);
        Appearance(Check);
        Duplicates(Check);
        MatchesWindowsLogic(Check);
        return count;
    }

    private static ScheduleSnapshot Snapshot(string utc) =>
        AgentSchedule.GetSnapshot(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture));

    private static ProviderStatus Status(ProviderKind provider, OfficialStatus status) =>
        new(provider, status, DateTimeOffset.UnixEpoch, null, provider + " " + status);

    private static ProviderStatus[] All(OfficialStatus openAi, OfficialStatus claude = OfficialStatus.Operational,
        OfficialStatus gemini = OfficialStatus.Operational) =>
        [Status(ProviderKind.OpenAI, openAi), Status(ProviderKind.Claude, claude), Status(ProviderKind.Gemini, gemini)];

    private static StatusTicker Strict() => new(strictInput: true);

    private static void FirstTickAndTransitions(Action<bool, string> check)
    {
        var ticker = Strict();
        var first = ticker.Tick(Full, All(OfficialStatus.Operational), TickReason.Timer);
        check(!first.ScheduleChanged && first.Transition is null && first.ProviderAlerts.Count == 0 && first.AppearanceChanged,
            "first decision has no previous state: no transition or alert, icon is new");

        var burger = ticker.Tick(Burger, All(OfficialStatus.Degraded), TickReason.Timer);
        check(burger.ScheduleChanged && burger.Transition == TrayPresentation.TransitionNotification(Burger),
            "FULL to BURGER on the timer notifies the transition once");
        check(burger.Transition?.Title == "BURGER TIME 시작", "transition title");
        check(burger.ProviderAlerts.Count == 0, "no provider alert on the transition tick");
        check(ticker.Tick(Burger, All(OfficialStatus.Degraded), TickReason.Timer).Transition is null, "no repeat while BURGER");

        var back = ticker.Tick(Full, All(OfficialStatus.Operational), TickReason.ProviderChanged);
        check(back.ScheduleChanged && back.Transition?.Title == "FULL THROTTLE 시작" && back.ProviderAlerts.Count == 0,
            "ProviderChanged notifies a transition like the timer");

        // Initial is a suppressed refresh, not a reset: it consumes a change silently.
        var initial = ticker.Tick(Burger, All(OfficialStatus.Operational), TickReason.Initial);
        check(initial.ScheduleChanged && initial.Transition is null, "Initial consumes a schedule change silently");
        check(ticker.Tick(Burger, All(OfficialStatus.Operational), TickReason.Timer) is { ScheduleChanged: false, Transition: null },
            "the consumed change is not announced later");

        // Weekend and weekday FULL are the same state: only AgentState counts as a transition.
        var full = new StatusTicker(strictInput: true);
        full.Tick(Full, All(OfficialStatus.Operational), TickReason.Initial);
        check(!full.Tick(Weekend, All(OfficialStatus.Operational), TickReason.Timer).ScheduleChanged,
            "countdown or extension changes within FULL are not transitions");
    }

    private static void ProviderAlerts(Action<bool, string> check)
    {
        var ticker = Strict();
        ticker.Tick(Full, All(OfficialStatus.Operational), TickReason.Initial);
        var hold = ticker.Tick(Full, All(OfficialStatus.Degraded), TickReason.Timer);
        check(hold.ProviderAlerts is [{ Provider: ProviderKind.OpenAI, Recommendation: Recommendation.Hold, Recovered: false }],
            "GO to HOLD alerts once");
        var expected = TrayPresentation.ProviderNotification(ProviderKind.OpenAI, Recommendation.Hold, "OpenAI Degraded", ProviderNames.Provider);
        check(hold.ProviderAlerts[0].Title == "ChatGPT 작업 권고 변경" && hold.ProviderAlerts[0].Body == expected.Body,
            "alert text is the shared provider notification with the ChatGPT name");
        check(ticker.Tick(Full, All(OfficialStatus.Degraded), TickReason.Timer).ProviderAlerts.Count == 0, "no repeat while HOLD");
        check(ticker.Tick(Full, All(OfficialStatus.MajorOutage), TickReason.ProviderChanged).ProviderAlerts
            is [{ Recommendation: Recommendation.Stop }], "HOLD to STOP alerts");
        // Unknown/STALE are gaps: the last confirmed STOP survives them.
        check(ticker.Tick(Full, All(OfficialStatus.Unknown), TickReason.Timer).ProviderAlerts.Count == 0, "no alert for UNKNOWN");
        check(ticker.Tick(Full, All(OfficialStatus.Stale), TickReason.Timer).ProviderAlerts.Count == 0, "no alert for STALE");
        var recovered = ticker.Tick(Full, All(OfficialStatus.Operational), TickReason.Timer);
        check(recovered.ProviderAlerts is [{ Recommendation: Recommendation.Go, Recovered: true, Title: "ChatGPT 정상화" }],
            "STOP to GO across the gap alerts recovery");

        // BURGER TIME never alerts and resets the confirmed state at the boundary.
        ticker.Tick(Burger, All(OfficialStatus.Operational), TickReason.Timer);
        check(ticker.Tick(Burger, All(OfficialStatus.MajorOutage), TickReason.Timer).ProviderAlerts.Count == 0, "no alert in BURGER");
        ticker.Tick(Full, All(OfficialStatus.MajorOutage), TickReason.Timer);
        check(ticker.Tick(Full, All(OfficialStatus.MajorOutage), TickReason.Timer).ProviderAlerts.Count == 0,
            "the first FULL value after BURGER only becomes the confirmed state");

        // Suppressed decisions still observe, so a change seen during Initial is not announced later.
        var observed = Strict();
        observed.Tick(Full, All(OfficialStatus.Operational), TickReason.Initial);
        check(observed.Tick(Full, All(OfficialStatus.Degraded), TickReason.Initial).ProviderAlerts.Count == 0, "Initial suppresses alerts");
        check(observed.Tick(Full, All(OfficialStatus.Degraded), TickReason.Timer).ProviderAlerts.Count == 0,
            "a change observed during Initial is already confirmed");

        // Why alerts also need "no transition this tick": a BURGER tick with no provider list never
        // reaches Observe, so the confirmed GO survives; the next FULL tick is still a transition.
        var gap = Strict();
        gap.Tick(Full, All(OfficialStatus.Operational), TickReason.Initial);
        gap.Tick(Burger, [], TickReason.Timer);
        var afterGap = gap.Tick(Full, All(OfficialStatus.Degraded), TickReason.Timer);
        check(afterGap.Transition is not null && afterGap.ProviderAlerts.Count == 0,
            "no provider alert on a transition tick even when Observe missed the boundary");

        var named = new StatusTicker(provider => "Provider-" + (int)provider, strictInput: true);
        named.Tick(Full, All(OfficialStatus.Operational), TickReason.Initial);
        check(named.Tick(Full, All(OfficialStatus.Degraded), TickReason.Timer).ProviderAlerts[0].Title == "Provider-0 작업 권고 변경",
            "display name callback is used for alert titles");
    }

    private static void PolicyChanges(Action<bool, string> check)
    {
        // Same FULL state: the policy refresh delivers a concurrent provider alert, so a host skips
        // the holiday notification (ProviderAlerts not empty).
        var same = Strict();
        same.Tick(Full, All(OfficialStatus.Operational), TickReason.Initial);
        var concurrent = same.Tick(Weekend, All(OfficialStatus.Degraded), TickReason.PolicyChanged);
        check(!concurrent.ScheduleChanged && concurrent.Transition is null && concurrent.ProviderAlerts.Count == 1,
            "PolicyChanged without a transition alerts providers only");

        // The policy itself changes the state: the transition is consumed silently and provider alerts
        // are suppressed, so a host shows the holiday notification (ProviderAlerts empty).
        var moved = Strict();
        moved.Tick(Burger, All(OfficialStatus.Operational), TickReason.Initial);
        var policy = moved.Tick(Full, All(OfficialStatus.Degraded), TickReason.PolicyChanged);
        check(policy.ScheduleChanged && policy.Transition is null && policy.ProviderAlerts.Count == 0,
            "PolicyChanged with a transition notifies nothing");
        check(moved.Tick(Full, All(OfficialStatus.Degraded), TickReason.Timer) is { ScheduleChanged: false, Transition: null },
            "the policy transition is not announced on the next timer tick");
    }

    private static void OrderingAndCopies(Action<bool, string> check)
    {
        var ticker = Strict();
        ProviderStatus[] reversed = All(OfficialStatus.Operational).Reverse().ToArray();
        ticker.Tick(Full, reversed, TickReason.Initial);
        var input = new List<ProviderStatus>
        {
            Status(ProviderKind.Gemini, OfficialStatus.Degraded),
            Status(ProviderKind.OpenAI, OfficialStatus.MajorOutage),
            Status(ProviderKind.Claude, OfficialStatus.Degraded)
        };
        var result = ticker.Tick(Full, input, TickReason.Timer);
        check(result.Providers.Select(s => s.Provider).SequenceEqual(Enum.GetValues<ProviderKind>()), "providers in ProviderKind order");
        check(result.ProviderAlerts.Select(a => a.Provider).SequenceEqual(Enum.GetValues<ProviderKind>()),
            "simultaneous alerts in ProviderKind order");
        input.Clear();
        input.Add(Status(ProviderKind.OpenAI, OfficialStatus.Operational));
        check(result.Providers.Count == 3 && result.Providers[0].Status == OfficialStatus.MajorOutage,
            "the result is a copy; later input changes do not alter it");
        check(result.Providers is not ProviderStatus[] && result.ProviderAlerts is not List<ProviderAlert>,
            "result lists are read-only views");
    }

    private static void Appearance(Action<bool, string> check)
    {
        var ticker = Strict();
        var unknown = ticker.Tick(Full, Enum.GetValues<ProviderKind>().Select(p => ProviderStatus.Unknown(p)).ToArray(), TickReason.Initial);
        check(unknown.AppearanceChanged && unknown.Appearance == new TrayAppearance(AgentState.FullThrottle, TrayAttention.Gray),
            "first icon before any check is a gray F");
        check(!ticker.Tick(Full, Enum.GetValues<ProviderKind>().Select(p => ProviderStatus.Unknown(p)).ToArray(), TickReason.Timer).AppearanceChanged,
            "same appearance is reused");
        var green = ticker.Tick(Full, All(OfficialStatus.Operational), TickReason.ProviderChanged);
        check(green.AppearanceChanged && green.Appearance.Attention == TrayAttention.Green, "healthy FULL turns green");
        check(ticker.Tick(Full, [Status(ProviderKind.OpenAI, OfficialStatus.Operational)], TickReason.Timer).Appearance.Attention
            == TrayAttention.Gray, "a missing provider is allowed and shows gray");
    }

    private static void Duplicates(Action<bool, string> check)
    {
        ProviderStatus[] duplicated =
        [
            Status(ProviderKind.OpenAI, OfficialStatus.Degraded),
            Status(ProviderKind.Claude, OfficialStatus.Operational),
            Status(ProviderKind.OpenAI, OfficialStatus.Operational),
            Status(ProviderKind.Gemini, OfficialStatus.Operational)
        ];

        // Tests: a duplicate is a contract violation and changes no state.
        var strict = Strict();
        strict.Tick(Burger, All(OfficialStatus.Operational), TickReason.Initial);
        bool threw = false;
        try { strict.Tick(Full, duplicated, TickReason.Timer); }
        catch (ArgumentException) { threw = true; }
        check(threw, "strict mode rejects a duplicated provider");
        check(strict.Tick(Full, All(OfficialStatus.Operational), TickReason.Timer).Transition is not null,
            "a rejected call leaves the previous state, so the transition is still announced");

        // Running app: first value wins, no exception, one warning per new duplicate set.
        var warnings = new List<string>();
        var lenient = new StatusTicker(warn: warnings.Add);
        var first = lenient.Tick(Full, duplicated, TickReason.Initial);
        check(first.Providers.Count == 3 && first.Providers[0].Status == OfficialStatus.Degraded &&
            first.Appearance.Attention == TrayAttention.Orange, "the first OpenAI value is used");
        check(warnings is [var message] && message.Contains("OpenAI", StringComparison.Ordinal), "one warning names the provider");
        lenient.Tick(Full, duplicated, TickReason.Timer);
        check(warnings.Count == 1, "the same duplicate set does not warn every second");
        lenient.Tick(Full, [.. duplicated, Status(ProviderKind.Claude, OfficialStatus.Unknown)], TickReason.Timer);
        check(warnings.Count == 2 && warnings[1].Contains("OpenAI, Claude", StringComparison.Ordinal), "a changed duplicate set warns again");
        lenient.Tick(Full, All(OfficialStatus.Operational), TickReason.Timer);
        lenient.Tick(Full, duplicated, TickReason.Timer);
        check(warnings.Count == 3, "a duplicate reappearing after clean input warns again");
        check(new StatusTicker().Tick(Full, duplicated, TickReason.Timer).Providers.Count == 3, "no warning callback is required");
    }

    // A literal copy of the Windows host logic before 7a, run against random sequences.
    private sealed class WindowsHost
    {
        private readonly RecommendationNotifications notifications = new();
        private AgentState? lastState;
        private TrayAppearance? currentAppearance;

        public (bool Changed, (string, string)? Transition, List<(ProviderKind, Recommendation, string, string)> Alerts,
            TrayAppearance Appearance, bool AppearanceChanged) RefreshStatus(ScheduleSnapshot snapshot,
            IReadOnlyList<ProviderStatus> providers, bool notifyOnChange, bool notifyProviders)
        {
            bool changed = lastState.HasValue && lastState.Value != snapshot.State;
            lastState = snapshot.State;
            (string, string)? transition = notifyOnChange && changed ? TrayPresentation.TransitionNotification(snapshot) : null;
            bool notify = (notifyOnChange || notifyProviders) && !changed;
            TrayAppearance appearance = TrayPresentation.Calculate(snapshot.State, providers);
            bool appearanceChanged = currentAppearance != appearance;
            currentAppearance = appearance;
            var alerts = new List<(ProviderKind, Recommendation, string, string)>();
            foreach (var status in providers)
            {
                var current = RecommendationPolicy.Calculate(snapshot.State, status.Status);
                if (notifications.Observe(status.Provider, snapshot.State, current, notify))
                {
                    var (title, body) = TrayPresentation.ProviderNotification(status.Provider, current, status.Reason, ProviderNames.Provider);
                    alerts.Add((status.Provider, current, title, body));
                }
            }
            return (changed, transition, alerts, appearance, appearanceChanged);
        }
    }

    private static void MatchesWindowsLogic(Action<bool, string> check)
    {
        var random = new Random(20261009);
        ScheduleSnapshot[] schedules = [Full, Burger, Weekend];
        OfficialStatus[] statuses = Enum.GetValues<OfficialStatus>();
        TickReason[] reasons = Enum.GetValues<TickReason>();
        for (int run = 0; run < 20; run++)
        {
            var ticker = Strict();
            var host = new WindowsHost();
            int alerts = 0, transitions = 0;
            for (int step = 0; step < 500; step++)
            {
                // Mostly one schedule and stable providers, so alerts and transitions actually occur.
                ScheduleSnapshot schedule = schedules[random.Next(10) < 8 ? step / 40 % 2 : random.Next(schedules.Length)];
                ProviderStatus[] providers = Enum.GetValues<ProviderKind>()
                    .Select(p => Status(p, statuses[random.Next(4) == 0 ? random.Next(statuses.Length) : 0])).ToArray();
                TickReason reason = reasons[random.Next(reasons.Length)];
                var expected = host.RefreshStatus(schedule, providers, reason is TickReason.Timer or TickReason.ProviderChanged,
                    reason == TickReason.PolicyChanged);
                var actual = ticker.Tick(schedule, providers.OrderBy(_ => random.Next()).ToArray(), reason);
                bool same = actual.ScheduleChanged == expected.Changed && actual.Transition == expected.Transition &&
                    actual.Appearance == expected.Appearance && actual.AppearanceChanged == expected.AppearanceChanged &&
                    actual.ProviderAlerts.Select(a => (a.Provider, a.Recommendation, a.Title, a.Body)).SequenceEqual(expected.Alerts) &&
                    actual.ProviderAlerts.All(a => a.Recovered == (a.Recommendation == Recommendation.Go));
                if (!same) check(false, $"differs from the Windows logic at run {run}, step {step} ({reason})");
                alerts += expected.Alerts.Count;
                transitions += expected.Transition is null ? 0 : 1;
            }
            check(alerts > 0 && transitions > 0, $"run {run} exercised alerts and transitions");
        }
        check(true, "10,000 random decisions match the Windows logic, with shuffled input order");
    }
}
