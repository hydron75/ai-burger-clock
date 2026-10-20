namespace AiBurgerClock;

// All names, identities, percentages and timestamps in these fixtures are synthetic.
internal static class QuotaPendingAlertPolicyTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota pending alert: " + label);
        }

        DateTimeOffset now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        const long continuity = 17;
        QuotaWindow session = new("synthetic-session", "합성 5시간", 5, now.AddHours(4), 300);
        QuotaWindow weekly = new("synthetic-weekly", "합성 주간", 8, now.AddDays(4), 10080);
        QuotaWindow extra = new("synthetic-model-weekly", "합성 모델 주간", 50, now.AddDays(5), 10080);
        QuotaReading Reading(params QuotaWindow[] rows) => new(QuotaProvider.Claude, Array.AsReadOnly(rows));
        QuotaRecoveryAlert Recovery(params string[] ids) =>
            new(QuotaProvider.Claude, 7, "합성 회복 제목", "합성 회복 본문", Array.AsReadOnly(ids));
        QuotaEarlyResetWindow Change(QuotaWindow window) => new(window.Id, window.Label,
            window.WindowMinutes!.Value, 80, window.UsedPercent, now.AddDays(2), window.ResetsAtUtc!.Value);
        QuotaEarlyResetAlert Early(params QuotaWindow[] rows) =>
            new(QuotaProvider.Claude, 7, continuity, now, Array.AsReadOnly(rows.Select(Change).ToArray()));

        QuotaReading expected = Reading(session, weekly);
        QuotaRecoveryAlert recovery = Recovery(session.Id);
        QuotaEarlyResetAlert early = Early(weekly);
        bool RecoveryCurrent(QuotaReading current, DateTimeOffset? at = null) =>
            QuotaPendingAlertPolicy.RecoveryStillCurrent(recovery, expected, current, at ?? now);
        bool EarlyCurrent(QuotaReading current, DateTimeOffset? at = null, long id = continuity) =>
            QuotaPendingAlertPolicy.EarlyResetStillCurrent(early, expected, current, at ?? now, id);

        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
        {
            QuotaReading providerReading = expected with { Provider = provider };
            Check(QuotaPendingAlertPolicy.RecoveryStillCurrent(recovery with { Provider = provider },
                providerReading, providerReading, now), provider + " unchanged recovered row remains current");
            Check(QuotaPendingAlertPolicy.EarlyResetStillCurrent(early with { Provider = provider },
                providerReading, providerReading, now, continuity), provider + " unchanged early-reset row remains current");
        }

        // Recovery keeps its original, target-row-only contract. In particular,
        // weekly-only decrease, continuity and whole-key-set rules do not apply.
        Check(RecoveryCurrent(Reading(session, weekly)), "recovery accepts the original target row");
        Check(RecoveryCurrent(Reading(session with { UsedPercent = 99.96 }, weekly)),
            "recovery uses raw usage rather than display-rounded exhaustion");
        Check(RecoveryCurrent(Reading(session with { UsedPercent = Math.BitDecrement(100.0) }, weekly)),
            "recovery accepts the smallest representable amount below exhaustion");
        Check(!RecoveryCurrent(Reading(session with { UsedPercent = 100 }, weekly)), "reexhaustion invalidates recovery");
        Check(!RecoveryCurrent(Reading(weekly)), "a missing recovered target invalidates recovery");
        Check(!RecoveryCurrent(Reading(session with { Id = "SYNTHETIC-SESSION" }, weekly)),
            "recovery target identities remain case-sensitive");
        Check(!RecoveryCurrent(Reading(session with { WindowMinutes = 10080 }, weekly)),
            "recovery rejects a changed target duration");
        Check(!RecoveryCurrent(Reading(session with { WindowMinutes = null }, weekly)),
            "recovery rejects newly missing target duration metadata");
        Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(recovery, Reading(weekly), expected, now),
            "recovery requires the target to exist in its expected reading too");
        QuotaWindow unknownPeriod = session with { WindowMinutes = null };
        Check(QuotaPendingAlertPolicy.RecoveryStillCurrent(recovery, Reading(unknownPeriod, weekly),
            Reading(unknownPeriod, weekly), now), "unchanged unknown duration remains valid for recovery");
        Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(recovery, Reading(unknownPeriod, weekly), expected, now),
            "recovery rejects unknown-to-known target duration changes");
        Check(RecoveryCurrent(Reading(session with { Label = "다른 합성 표시 이름" }, weekly)),
            "a target label change does not change recovery identity");
        Check(RecoveryCurrent(Reading(weekly, session)), "recovery ignores row order");
        Check(RecoveryCurrent(Reading(session, weekly, extra)), "an unrelated added row does not invalidate recovery");
        Check(RecoveryCurrent(Reading(session)), "an unrelated removed row does not invalidate recovery");
        Check(RecoveryCurrent(Reading(session, weekly with { Id = "synthetic-other-weekly" })),
            "an unrelated scope change does not invalidate recovery");
        Check(RecoveryCurrent(Reading(session, weekly with { WindowMinutes = 300 })),
            "an unrelated duration change does not invalidate recovery");
        Check(RecoveryCurrent(Reading(session, weekly with { UsedPercent = 100, ResetsAtUtc = now.AddTicks(-1) })),
            "an unrelated exhausted or expired row does not invalidate recovery");
        Check(RecoveryCurrent(Reading(session with { ResetsAtUtc = null }, weekly)),
            "recovery does not require a provided reset timestamp");
        Check(!RecoveryCurrent(Reading(session with { ResetsAtUtc = now }, weekly)),
            "a recovery target reset exactly at now has expired");
        Check(!RecoveryCurrent(Reading(session with { ResetsAtUtc = now.AddTicks(-1) }, weekly)),
            "a recovery target reset one tick before now has expired");
        Check(RecoveryCurrent(Reading(session with { ResetsAtUtc = now.AddTicks(1) }, weekly)),
            "a recovery target reset one tick in the future remains valid");
        Check(RecoveryCurrent(Reading(session with { ResetsAtUtc = session.ResetsAtUtc!.Value.AddHours(1) }, weekly)),
            "recovery permits future reset movement beyond the early-reset tolerance");
        Check(RecoveryCurrent(expected, now.AddMinutes(90).AddTicks(1)),
            "recovery has no new ninety-minute age cap");
        Check(RecoveryCurrent(Reading(session with { ResetsAtUtc = now.AddDays(2) }, weekly), now.AddDays(1)),
            "an old recovery may remain current with a newly supplied future reset");

        QuotaRecoveryAlert multiRecovery = Recovery(session.Id, weekly.Id);
        Check(QuotaPendingAlertPolicy.RecoveryStillCurrent(multiRecovery, expected, expected, now),
            "all recovered targets still usable keeps a grouped recovery valid");
        Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(multiRecovery, expected,
            Reading(session with { UsedPercent = 100 }, weekly), now), "first grouped recovered target reexhaustion invalidates all");
        Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(multiRecovery, expected,
            Reading(session, weekly with { UsedPercent = 100 }), now), "last grouped recovered target reexhaustion invalidates all");
        Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(multiRecovery, expected, Reading(session), now),
            "one missing grouped recovered target invalidates all");
        Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(multiRecovery, expected,
            Reading(session, weekly with { ResetsAtUtc = now }), now), "one expired grouped recovered target invalidates all");
        Check(QuotaPendingAlertPolicy.RecoveryStillCurrent(multiRecovery, expected,
            Reading(weekly with { ResetsAtUtc = null }, session), now),
            "grouped recovery permits missing reset metadata and reordered targets");

        Check(EarlyCurrent(expected), "early reset accepts unchanged weekly evidence at the receipt instant");
        Check(!EarlyCurrent(expected, now.AddTicks(-1)), "a backwards clock invalidates early-reset evidence");
        Check(EarlyCurrent(expected, now.AddMinutes(90)), "early reset no longer imposes a ninety-minute age ceiling");
        Check(EarlyCurrent(expected, now.AddMinutes(90).AddTicks(1)), "one tick beyond former age ceiling remains valid");
        Check(EarlyCurrent(expected, now.AddHours(12)), "long sleep may retain future weekly decrease evidence");
        Check(EarlyCurrent(expected, now.AddMinutes(90).ToOffset(TimeSpan.FromHours(-7))),
            "early-reset chronology compares instants rather than displayed offsets");
        foreach (long invalidContinuity in new[] { 0L, -1L, continuity + 1 })
            Check(!EarlyCurrent(expected, id: invalidContinuity), "zero, negative or changed current continuity is rejected");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(early with { ContinuityId = 0 }, expected, expected, now, 0),
            "two legacy zero continuity values cannot establish validity");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(early with { ContinuityId = -1 }, expected, expected, now, -1),
            "matching negative continuity values cannot establish validity");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(early with { ContinuityId = 0 }, expected, expected, now, continuity),
            "a modern current reading cannot repair a legacy alert continuity");

        Check(EarlyCurrent(Reading(weekly, session)), "early-reset whole-key comparison ignores row order");
        Check(!EarlyCurrent(Reading(session, weekly, extra)), "an unrelated added key invalidates early reset");
        Check(!EarlyCurrent(Reading(weekly)), "an unrelated removed key invalidates early reset");
        Check(!EarlyCurrent(Reading(session with { Id = "synthetic-other-session" }, weekly)),
            "an unrelated changed identity invalidates the early-reset whole-key set");
        Check(!EarlyCurrent(Reading(session with { WindowMinutes = 10080 }, weekly)),
            "an unrelated changed duration invalidates the early-reset whole-key set");
        Check(!EarlyCurrent(Reading(session with { WindowMinutes = null }, weekly)),
            "an unrelated newly unknown duration invalidates the early-reset whole-key set");
        QuotaReading unknownUnrelated = Reading(session with { WindowMinutes = null }, weekly);
        Check(QuotaPendingAlertPolicy.EarlyResetStillCurrent(early, unknownUnrelated, unknownUnrelated, now, continuity),
            "unchanged unknown duration on an unrelated row is allowed in the whole-key set");
        Check(!EarlyCurrent(Reading(session)), "a missing early-reset target invalidates the candidate");
        Check(!EarlyCurrent(Reading(session, weekly with { WindowMinutes = 300 })),
            "a changed early-reset target duration invalidates the candidate");
        QuotaReading changedDuration = Reading(session, weekly with { WindowMinutes = 300 });
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(early, changedDuration, changedDuration, now, continuity),
            "target duration must also match the alert even when expected and current key sets match each other");
        QuotaReading changedScope = Reading(session, weekly with { Id = "synthetic-replacement" });
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(early, changedScope, changedScope, now, continuity),
            "an alert target cannot be replaced by a different matching expected/current scope");
        Check(EarlyCurrent(Reading(session, weekly with { Label = "다른 합성 표시 이름" })),
            "early reset uses target identity rather than its display label");
        Check(EarlyCurrent(Reading(session with { UsedPercent = 100, ResetsAtUtc = now.AddTicks(-1) }, weekly)),
            "unrelated exhausted or expired usage does not invalidate unchanged early-reset keys");

        Check(!EarlyCurrent(Reading(session, weekly with { UsedPercent = 100 })), "target reexhaustion invalidates early reset");
        Check(!EarlyCurrent(Reading(session, weekly with { UsedPercent = 80 })), "usage returning exactly to pre-reset level invalidates early reset");
        Check(!EarlyCurrent(Reading(session, weekly with { UsedPercent = Math.BitIncrement(80.0) })),
            "usage above the pre-reset level invalidates early reset before full exhaustion");
        Check(EarlyCurrent(Reading(session, weekly with { UsedPercent = Math.BitDecrement(80.0) })),
            "the smallest raw decrease below the recorded previous usage remains evidence");
        Check(EarlyCurrent(Reading(session, weekly with { UsedPercent = 79.96 })),
            "pending decrease uses raw usage rather than display rounding");
        Check(!EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = null })),
            "an early-reset target requires a provided reset timestamp");
        Check(!EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = now })),
            "an early-reset target reset exactly at now has expired");
        Check(!EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = now.AddTicks(-1) })),
            "an early-reset target reset before now has expired");

        DateTimeOffset recordedReset = weekly.ResetsAtUtc!.Value;
        foreach (TimeSpan shift in new[] { TimeSpan.FromMinutes(-1), TimeSpan.FromMinutes(1), TimeSpan.FromHours(-12), TimeSpan.FromHours(12) })
        {
            Check(EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = recordedReset + shift })),
                "future weekly reset may drift without a tolerance ceiling");
            TimeSpan inside = shift < TimeSpan.Zero ? shift.Add(TimeSpan.FromTicks(1)) : shift.Subtract(TimeSpan.FromTicks(1));
            Check(EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = recordedReset + inside })),
                "reset movement just inside a former tolerance boundary remains valid");
            TimeSpan outside = shift < TimeSpan.Zero ? shift.Subtract(TimeSpan.FromTicks(1)) : shift.Add(TimeSpan.FromTicks(1));
            Check(EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = recordedReset + outside })),
                "reset movement just outside a former tolerance boundary remains valid");
        }
        Check(EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = recordedReset.ToOffset(TimeSpan.FromHours(9)) })),
            "reset validity compares instants rather than displayed offsets");
        Check(EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = now.AddDays(7) })),
            "exact full weekly duration is consistent");
        Check(!EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = now.AddDays(7).AddTicks(1) })),
            "a future reset beyond the weekly duration is contradictory");
        foreach (long ticks in new[] { -1L, 0L, 1L })
            Check(EarlyCurrent(expected, now.AddDays(2).AddMinutes(-5).AddTicks(-ticks)) == (ticks > 0),
                "original scheduled reset must remain over five minutes away, tick " + ticks);
        Check(!EarlyCurrent(expected, now.AddDays(2)), "normal original scheduled reset expiry ends queued evidence");
        QuotaEarlyResetAlert nearReset = early with
        {
            Windows = Array.AsReadOnly(new[] { early.Windows[0] with { ResetUtc = now.AddSeconds(30) } })
        };
        Check(QuotaPendingAlertPolicy.EarlyResetStillCurrent(nearReset, expected,
            Reading(session, weekly with { ResetsAtUtc = now.AddTicks(1) }), now, continuity),
            "a currently provided reset one tick in the future is not expired");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(nearReset, expected,
            Reading(session, weekly with { ResetsAtUtc = now }), now, continuity),
            "past candidate reset metadata does not make a currently expired reset valid");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(Early(session), expected, expected, now, continuity),
            "a legacy five-hour early-reset candidate is no longer current");

        QuotaReading groupExpected = Reading(session, weekly, extra);
        QuotaEarlyResetAlert multiEarly = Early(weekly, extra);
        Check(QuotaPendingAlertPolicy.EarlyResetStillCurrent(multiEarly, groupExpected, Reading(extra, weekly, session), now, continuity),
            "all early-reset targets valid preserves a grouped candidate despite reordered rows");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(multiEarly, groupExpected,
            Reading(session, weekly with { UsedPercent = 100 }, extra), now, continuity),
            "first grouped early-reset target reexhaustion invalidates all");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(multiEarly, groupExpected,
            Reading(session, weekly, extra with { UsedPercent = 100 }), now, continuity),
            "last grouped early-reset target reexhaustion invalidates all");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(multiEarly, groupExpected,
            Reading(session, weekly, extra with { ResetsAtUtc = null }), now, continuity),
            "one missing grouped early-reset timestamp invalidates all");
        Check(!QuotaPendingAlertPolicy.EarlyResetStillCurrent(multiEarly, groupExpected,
            Reading(session, weekly, extra with { ResetsAtUtc = now.AddDays(7).AddTicks(1) }), now, continuity),
            "one grouped early-reset target with contradictory duration invalidates all");
        Check(QuotaPendingAlertPolicy.EarlyResetStillCurrent(multiEarly, groupExpected,
            Reading(session, weekly with { UsedPercent = 79.96 }, extra with { UsedPercent = 79.96 }), now, continuity),
            "a grouped early-reset candidate remains current while each raw usage stays below its previous value");

        Check(RecoveryCurrent(Reading(session, weekly with { ResetsAtUtc = null })) &&
            !EarlyCurrent(Reading(session, weekly with { ResetsAtUtc = null })),
            "only the invalid early-reset kind is discarded when recovery still has numeric evidence");
        Check(RecoveryCurrent(Reading(session, weekly, extra)) && !EarlyCurrent(Reading(session, weekly, extra)),
            "whole-key changes can invalidate early reset without invalidating the recovery kind");
        QuotaReading sessionExhausted = Reading(session with { UsedPercent = 100 }, weekly);
        Check(!QuotaPendingAlertPolicy.RecoveryStillCurrent(Recovery(session.Id), expected, sessionExhausted, now) &&
            EarlyCurrent(sessionExhausted), "an invalid recovery kind does not discard a distinct still-valid early-reset target");
        Check(expected.Windows.SequenceEqual(new[] { session, weekly }) && recovery.WindowIds.SequenceEqual(new[] { session.Id }) &&
            early.Windows.Count == 1 && early.Windows[0] == Change(weekly), "policy evaluation leaves expected reading and alert evidence unchanged");

        return count;
    }
}
