namespace AiBurgerClock;

// All labels, IDs, percentages and timestamps in these fixtures are synthetic.
internal static class QuotaRecoveryNotificationTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota recovery: " + label);
        }
        DateTimeOffset now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        QuotaWindow Row(string id = "synthetic-session", double used = 100, int? minutes = 300,
            string label = "합성 세션 (5시간)") => new(id, label, used, now.AddDays(7), minutes);
        QuotaObservation Observation(long sequence, params QuotaWindow[] rows) =>
            new(QuotaProvider.Claude, sequence, now.AddSeconds(sequence), new(QuotaProvider.Claude, Array.AsReadOnly(rows)));
        QuotaObservation Provider(QuotaProvider provider, long sequence, params QuotaWindow[] rows) =>
            Observation(sequence, rows) with { Provider = provider, Reading = new(provider, Array.AsReadOnly(rows)) };
        QuotaRecoveryNotifications Seed(params QuotaWindow[] rows)
        {
            var engine = new QuotaRecoveryNotifications();
            Check(engine.Observe(Observation(1, rows), true) is null, "first live success seeds without alert");
            return engine;
        }

        Check(QuotaRecoveryText.MenuLabel == "한도 회복 알림", "shared menu label");
        Check(QuotaRecoveryText.MenuDetail.Contains("기본 꺼짐", StringComparison.Ordinal) &&
            QuotaRecoveryText.MenuDetail.Contains("추가 조회 없음", StringComparison.Ordinal), "menu explains default and unchanged polling");
        Check(QuotaRecoveryText.SettingKey == "QuotaRecoveryNotificationsEnabled", "shared durable setting identity");
        Check(QuotaRecoveryText.Saved(true).Contains("다음 성공 조회", StringComparison.Ordinal) &&
            QuotaRecoveryText.Saved(false) == "한도 회복 알림을 껐습니다.", "shared toggle feedback");
        Check(QuotaRecoveryText.SettingReadFailed.Contains("꺼짐", StringComparison.Ordinal) &&
            QuotaRecoveryText.SettingSaveFailed.Contains("기존 설정", StringComparison.Ordinal), "safe setting failure feedback");
        Check(QuotaRecoveryText.DeliveryFailed == "한도 회복 알림을 표시하지 못했습니다. OS 알림 설정을 확인하세요.",
            "shared safe notification delivery failure feedback");

        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
        {
            var engine = new QuotaRecoveryNotifications();
            Check(engine.Observe(Provider(provider, 1, Row()), true) is null, provider + " silent first observation");
            QuotaRecoveryAlert? alert = engine.Observe(Provider(provider, 2, Row(used: 20)), true);
            Check(alert is not null, provider + " exhausted row recovers");
            Check(alert!.Provider == provider && alert.Sequence == 2 && alert.WindowIds.SequenceEqual(new[] { "synthetic-session" }),
                provider + " provider/sequence/identity preserved");
            string scopedName = provider switch
            {
                QuotaProvider.Codex => "ChatGPT (Work/Codex)",
                QuotaProvider.Gemini => "Gemini (Antigravity)",
                _ => "Claude"
            };
            Check(alert.Title == scopedName + " 계정 한도 회복 확인", provider + " quota scope in shared title");
            Check(alert.Body == "합성 세션 (5시간) 한도에 잔여량이 확인되었습니다. 다른 한도와 서비스 상태에 따라 실제 사용은 제한될 수 있습니다.",
                provider + " shared caution does not promise provider availability");
            Check(!alert.Body.Contains('%') && !alert.Body.Contains("synthetic-session", StringComparison.Ordinal) &&
                !alert.Body.Contains("2026", StringComparison.Ordinal), provider + " body excludes values/timestamps/IDs");
            Check(engine.Observe(Provider(provider, 3, Row(used: 20)), true) is null, provider + " repeated recovery remains quiet");
            Check(engine.Observe(Provider(provider, 4, Row(used: 0)), true) is null, provider + " normal usage decrease is not a second recovery");
            Check(engine.Observe(Provider(provider, 5, Row()), true) is null, provider + " reexhaustion arms without alert");
            Check(engine.Observe(Provider(provider, 6, Row(used: 99.999999999)), true) is not null,
                provider + " raw positive remaining amount recovers even if display rounds to exhausted");
            bool immutable = false;
            try { ((IList<string>)alert.WindowIds)[0] = "changed"; }
            catch (NotSupportedException) { immutable = true; }
            Check(immutable && alert.WindowIds[0] == "synthetic-session", provider + " returned window identities are immutable");
        }

        var distinctProviders = new QuotaRecoveryNotifications();
        Check(distinctProviders.Observe(Provider(QuotaProvider.Codex, 5, Row()), true) is null, "Codex baseline created");
        Check(distinctProviders.Observe(Provider(QuotaProvider.Claude, 1, Row(used: 10)), true) is null,
            "another provider does not inherit exhaustion or sequence");
        Check(distinctProviders.Observe(Provider(QuotaProvider.Gemini, 1, Row()), true) is null, "Gemini baseline independent");
        Check(distinctProviders.Observe(Provider(QuotaProvider.Codex, 6, Row(used: 10)), true)?.Provider == QuotaProvider.Codex,
            "Codex transition remains independent");
        Check(distinctProviders.Observe(Provider(QuotaProvider.Gemini, 2, Row(used: 10)), true)?.Provider == QuotaProvider.Gemini,
            "Gemini transition remains independent");

        var multi = Seed(Row(), Row("synthetic-weekly", minutes: 10080, label: "합성 주간 전체"),
            Row("synthetic-model", minutes: 10080, label: "합성 주간 모델"));
        QuotaRecoveryAlert? grouped = multi.Observe(Observation(2,
            Row("synthetic-model", 10, 10080, "합성 주간 모델"), Row(used: 40),
            Row("synthetic-weekly", minutes: 10080, label: "합성 주간 전체")), true);
        Check(grouped is not null && grouped.WindowIds.SequenceEqual(new[] { "synthetic-model", "synthetic-session" }),
            "one provider alert groups all recovered windows in current order");
        Check(grouped!.Body.Contains("합성 주간 모델, 합성 세션 (5시간)", StringComparison.Ordinal) &&
            grouped.Body.Contains("다른 한도는 아직 소진 상태", StringComparison.Ordinal), "partial recovery explicitly warns about other exhausted limits");
        Check(!grouped.WindowIds.Contains("synthetic-weekly"), "still exhausted row is not named as recovered");
        Check(multi.Observe(Observation(3, Row(used: 40), Row("synthetic-weekly", 30, 10080, "합성 주간 전체"),
            Row("synthetic-model", 10, 10080, "합성 주간 모델")), true)?.WindowIds.SequenceEqual(new[] { "synthetic-weekly" }) == true,
            "later recovery only includes the newly recovered row");

        var labelChange = Seed(Row());
        Check(labelChange.Observe(Observation(2, Row(used: 10, label: "다르게 표시된 합성 세션")), true)?.Body
            .StartsWith("다르게 표시된 합성 세션 한도", StringComparison.Ordinal) == true,
            "label changes do not change identity and current label is used");
        var periodChange = Seed(Row());
        Check(periodChange.Observe(Observation(2, Row(used: 10, minutes: 10080)), true) is null, "changed duration starts a new baseline");
        Check(periodChange.Observe(Observation(3, Row(used: 10)), true) is null, "returning old duration cannot reuse an older baseline");
        var idChange = Seed(Row());
        Check(idChange.Observe(Observation(2, Row("different-scope", 10)), true) is null, "changed scope identity starts a new baseline");
        Check(idChange.Observe(Observation(3, Row(used: 10)), true) is null, "missing and reappearing row starts a new baseline");
        var missing = Seed(Row(), Row("other", 20));
        Check(missing.Observe(Observation(2, Row("other", 20)), true) is null, "missing row is dropped");
        Check(missing.Observe(Observation(3, Row(used: 10), Row("other", 20)), true) is null, "reappearing row cannot recover historical exhaustion");

        var resetChange = Seed(Row());
        Check(resetChange.Observe(Observation(2, Row(used: 30) with { ResetsAtUtc = now.AddDays(14) }), true) is not null,
            "a reset timestamp change does not hide confirmed numeric recovery");
        var onlyReset = Seed(Row(used: 20));
        Check(onlyReset.Observe(Observation(2, Row(used: 20) with { ResetsAtUtc = now.AddDays(14) }), true) is null,
            "a moving inactive reset timestamp alone is not a recovery");
        var absentReset = Seed(Row() with { ResetsAtUtc = null });
        Check(absentReset.Observe(Observation(2, Row(used: 70) with { ResetsAtUtc = null }), true) is not null,
            "numeric recovery is usable when reset timestamp is unavailable");
        var absentPeriod = Seed(Row(minutes: null));
        Check(absentPeriod.Observe(Observation(2, Row(used: 70, minutes: null)), true) is not null,
            "unchanged unknown period still supports numeric recovery");
        var knownPeriod = Seed(Row());
        Check(knownPeriod.Observe(Observation(2, Row(used: 70, minutes: null)), true) is null,
            "losing period metadata starts a new baseline");

        foreach (DateTimeOffset reset in new[] { now.AddSeconds(2), now.AddSeconds(1), now.AddDays(-1) })
        {
            var expired = Seed(Row());
            Check(expired.Observe(Observation(2, Row(used: 20) with { ResetsAtUtc = reset }), true) is null,
                "a newly received expired row is not recovery");
            Check(expired.Observe(Observation(3, Row(used: 20)), true) is null,
                "expired response removes the exhaustion baseline");
        }
        var expiredExhaustion = Seed(Row() with { ResetsAtUtc = now });
        Check(expiredExhaustion.Observe(Observation(2, Row(used: 20)), true) is null,
            "expired exhaustion cannot arm a recovery");
        var fraction = Seed(Row(used: 99.96));
        Check(fraction.Observe(Observation(2, Row(used: 0)), true) is null, "display rounding does not create exhaustion");
        var bitFraction = Seed(Row(used: Math.BitDecrement(100.0)));
        Check(bitFraction.Observe(Observation(2, Row(used: 0)), true) is null, "smallest representable remaining amount is not exhaustion");

        var sequence = Seed(Row());
        Check(sequence.Observe(Observation(1, Row(used: 10)), true) is null, "duplicate sequence rejected");
        Check(sequence.Observe(Observation(0, Row(used: 10)), true) is null, "old sequence rejected");
        Check(sequence.Observe(Observation(2, Row(used: 10)), true) is not null, "rejected observation cannot consume a real recovery");
        Check(sequence.Observe(Observation(2, Row()), true) is null, "duplicate cannot falsely rearm exhaustion");
        Check(sequence.Observe(Observation(3, Row(used: 10)), true) is null, "late duplicate has no downstream effect");
        var clock = Seed(Row());
        Check(clock.Observe(Observation(2, Row(used: 10)) with { CheckedAtUtc = now }, true) is null,
            "backwards clock reseeds silently");
        Check(clock.Observe(Observation(3, Row(used: 10)), true) is null, "clock-reseeded recovery does not replay later");
        var sameTime = Seed(Row());
        Check(sameTime.Observe(Observation(2, Row(used: 10)) with { CheckedAtUtc = now.AddSeconds(1) }, true) is not null,
            "distinct successful observations may share the same receipt instant");
        var offsets = Seed(Row());
        Check(offsets.Observe(Observation(2, Row(used: 10)) with
            { CheckedAtUtc = now.AddSeconds(2).ToOffset(TimeSpan.FromHours(-7)) }, true) is not null,
            "clock comparisons use instants rather than displayed local offsets");
        var gap = Seed(Row());
        Check(gap.Observe(Observation(2, Row(used: 10)) with { CheckedAtUtc = now.AddDays(7) }, true) is null,
            "long gap with an expired incoming reset remains conservative");
        var failureGap = Seed(Row());
        Check(failureGap.Observe(Observation(2, Row(used: 10) with { ResetsAtUtc = now.AddDays(21) }) with
            { CheckedAtUtc = now.AddDays(8) }, true) is not null,
            "failures have no Observe and a later live success can still recover an armed row");

        var disabled = new QuotaRecoveryNotifications();
        Check(disabled.Observe(Observation(1, Row()), false) is null, "disabled initial observation is silent");
        Check(disabled.Observe(Observation(2, Row(used: 10)), false) is null, "disabled recovery is silent");
        Check(disabled.Observe(Observation(3, Row(used: 10)), true) is null, "disabled recovery advances the baseline rather than replaying");
        Check(disabled.Observe(Observation(4, Row()), true) is null, "enabled reexhaustion arms");
        disabled.Reset();
        Check(disabled.Observe(Observation(5, Row(used: 10)), true) is null, "reset after settings toggle requires a new silent baseline");
        Check(disabled.Observe(Observation(6, Row()), true) is null, "baseline rearmed after reset");
        Check(disabled.Observe(Observation(7, Row(used: 10)), true) is not null, "new post-reset transition can alert");
        disabled.Reset();
        Check(disabled.Observe(Observation(1, Row(used: 10)), true) is null, "Reset also clears sequence history for a new monitor lifetime");

        var mutation = new QuotaRecoveryNotifications();
        var mutableRows = new List<QuotaWindow> { Row() };
        Check(mutation.Observe(Observation(1, Row()) with { Reading = new(QuotaProvider.Claude, mutableRows) }, true) is null,
            "mutable source fixture seeds");
        mutableRows[0] = Row(used: 10);
        Check(mutation.Observe(Observation(2, Row(used: 10)), true) is not null,
            "baseline copies exhaustion rather than retaining a mutable source collection");

        var privacyRows = Enumerable.Range(0, 64).Select(index => Row("private-synthetic-id-" + index, label:
            new string((char)('가' + index), 100) + "\r\n\t")).ToArray();
        var privacy = Seed(privacyRows);
        QuotaRecoveryAlert? bounded = privacy.Observe(Observation(2, privacyRows.Select(row => row with { UsedPercent = 20 }).ToArray()), true);
        Check(bounded is not null && bounded.WindowIds.Count == 64, "group retains all recovered IDs for host bookkeeping");
        Check(bounded!.Body.Length <= 255 && !bounded.Body.Any(char.IsControl), "native text stays bounded and single-line");
        Check(bounded.Body.Contains("외 61개", StringComparison.Ordinal) && !bounded.Body.Contains("private-synthetic-id", StringComparison.Ordinal),
            "bounded labels summarize without exposing internal IDs");
        Check(!bounded.Body.Contains("초기화", StringComparison.Ordinal) && !bounded.Body.Contains("리셋", StringComparison.Ordinal) &&
            !bounded.Body.Contains("복구 완료", StringComparison.Ordinal), "text does not infer reset cause or whole-provider recovery");
        Check(QuotaRecoveryText.Body([Row(label: "\r\n\t")], false).StartsWith("확인한 한도", StringComparison.Ordinal),
            "unusable labels use generic safe wording");

        var invalidFixtures = new List<QuotaObservation>
        {
            Observation(2, Row()) with { Provider = (QuotaProvider)999 },
            Observation(2, Row()) with { Sequence = -1 },
            Observation(2, Row()) with { CheckedAtUtc = DateTimeOffset.UnixEpoch.AddTicks(-1) },
            Observation(2, Row()) with { Reading = null! },
            Observation(2, Row()) with { Reading = new(QuotaProvider.Gemini, [Row()]) },
            Observation(2, Row()) with { Reading = new(QuotaProvider.Claude, null!) },
            Observation(2),
            Observation(2, Enumerable.Range(0, 65).Select(index => Row("extra-" + index)).ToArray()),
            Observation(2, (QuotaWindow)null!),
            Observation(2, Row(), Row()),
            Observation(2, Row(id: " ")),
            Observation(2, Row(id: new string('a', 513))),
            Observation(2, Row(label: " ")),
            Observation(2, Row(label: new string('a', 181))),
            Observation(2, Row(minutes: 0)),
            Observation(2, Row(minutes: -1)),
            Observation(2, Row() with { ResetsAtUtc = DateTimeOffset.UnixEpoch.AddDays(-1) }),
            Observation(2, Row() with { ResetsAtUtc = new DateTimeOffset(9001, 1, 1, 0, 0, 0, TimeSpan.Zero) })
        };
        foreach (double used in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.1, 100.1 })
            invalidFixtures.Add(Observation(2, Row(used: used)));
        foreach (QuotaObservation invalid in invalidFixtures)
        {
            var conservative = Seed(Row());
            Check(conservative.Observe(invalid, true) is null, "malformed observation is rejected without production throw");
            Check(conservative.Observe(Observation(3, Row(used: 10)), true) is not null,
                "malformed observation cannot poison or consume a verified baseline");
        }
        Check(new QuotaRecoveryNotifications().Observe(null!, true) is null, "null observation is ignored without production throw");
        return count;
    }
}
