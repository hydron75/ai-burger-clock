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

        // Detection and thresholds.
        Check(Pair(W("w", 60, oldReset), W("w", 5, newReset)) is { Windows.Count: 1 }, "60% -> 5% with a moved reset must be detected");
        Check(Pair(W("w", 60, oldReset), W("w", 10, newReset)) is not null, "new usage of exactly 10% must still count");
        Check(Pair(W("w", 60, oldReset), W("w", 10.1, newReset)) is null, "new usage above 10% must not count");
        Check(Pair(W("w", 30, oldReset), W("w", 10, newReset)) is not null, "a 20 point drop must count");
        Check(Pair(W("w", 29.9, oldReset), W("w", 10, newReset)) is null, "a 19.9 point drop must not count");
        // Excluded: normal reset, unused 0%, reset jitter, too long a gap, changed window set.
        Check(Pair(W("w", 60, at.AddMinutes(5)), W("w", 5, newReset)) is null, "a previous reset only 5 minutes away is a normal reset");
        Check(Pair(W("w", 60, at.AddMinutes(5.1)), W("w", 5, newReset)) is not null, "a previous reset 5.1 minutes away is still early");
        Check(Pair(W("w", 60, T0.AddMinutes(10)), W("w", 5, newReset)) is null, "a previous reset that already passed is a normal reset");
        Check(Pair(W("w", 0, oldReset), W("w", 0, newReset)) is null, "an unused 0% window with a moving reset must not count");
        Check(Pair(W("w", 60, oldReset), W("w", 5, oldReset.AddSeconds(30))) is null, "a reset change of 30 seconds is jitter");
        Check(Pair(W("w", 60, oldReset), W("w", 5, newReset), TimeSpan.FromMinutes(90)) is not null, "a 90 minute gap is allowed");
        Check(Pair(W("w", 60, oldReset), W("w", 5, newReset.AddMinutes(1)), TimeSpan.FromMinutes(91)) is null, "a 91 minute gap is too long");
        Check(Pair(W("w", 60, oldReset), W("w", 5, newReset, 300)) is null, "a changed window length is a different window set");
        {
            var engine = new QuotaEarlyResetNotifications();
            engine.Observe(Obs(1, T0, 1, W("w", 60, oldReset)), true);
            Check(engine.Observe(Obs(2, at, 1, W("w", 5, newReset), W("x", 5, newReset)), true) is null, "an added window must give no alert");
            // Disabled: no baseline is armed, so enabling later needs a fresh pair.
            var off = new QuotaEarlyResetNotifications();
            off.Observe(Obs(1, T0, 1, W("w", 60, oldReset)), false);
            Check(off.Observe(Obs(2, at, 1, W("w", 5, newReset)), true) is null, "the first success after enabling only arms the baseline");
        }

        // A failed read between two successes breaks continuity (real monitor, fake clock, scripted client).
        DateTimeOffset clock = T0;
        var script = new Queue<Func<DateTimeOffset, QuotaReading?>>();
        var client = new ScriptedClient(() => script.Dequeue()(clock));
        using var monitor = new AccountQuotaMonitor(client, null, () => clock);
        var continuity = new List<long>();
        var engineLive = new QuotaEarlyResetNotifications();
        var alerts = new List<QuotaEarlyResetAlert?>();
        monitor.Succeeded += observation =>
        {
            continuity.Add(observation.ContinuityId);
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
        long before = continuity[^1];
        await Poll(now => Reading(60, now.AddDays(3)));
        await Poll(_ => null); // a started read that fails
        await Poll(now => Reading(5, now.AddDays(7).AddMinutes(-1)));
        Check(alerts.Count == 4 && alerts[3] is null && continuity[3] > continuity[2],
            "a failed read between two successes must change the continuity id and suppress the alert");

        // Same response as a recovery: one combined notification per Provider response.
        var recovery = new QuotaRecoveryNotifications();
        var early = new QuotaEarlyResetNotifications();
        QuotaObservation A = Obs(1, T0, 1, W("a", 100, oldReset), W("b", 100, oldReset));
        QuotaObservation B = Obs(2, at, 1, W("a", 4, newReset), W("b", 50, oldReset));
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
        return $"early reset engine ({checks} checks: detection, 10% and 20 point edges, normal/unused/jitter/90 min/window-set exclusions, failure breaks continuity, " +
               "recovery+early combine into one banner, detail only)";
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
