namespace AiBurgerClock;

// Every identity, label, percentage and timestamp here is synthetic, never account output.
internal static class QuotaEarlyResetNotificationTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota early reset: " + label);
        }
        DateTimeOffset now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        QuotaWindow Row(double used = 60, int? minutes = 300, string id = "synthetic-session", string label = "합성 세션 (5시간)") =>
            new(id, label, used, now.AddHours(2), minutes);
        QuotaObservation Observation(long sequence, DateTimeOffset at, params QuotaWindow[] rows) =>
            new(QuotaProvider.Claude, sequence, at, new(QuotaProvider.Claude, Array.AsReadOnly(rows)), ContinuityId: 1);
        QuotaObservation For(QuotaProvider provider, long sequence, DateTimeOffset at, params QuotaWindow[] rows) =>
            Observation(sequence, at, rows) with { Provider = provider, Reading = new(provider, Array.AsReadOnly(rows)) };
        QuotaWindow Changed(double used = 0, int? minutes = 300, string id = "synthetic-session", string label = "합성 세션 (5시간)") =>
            Row(used, minutes, id, label) with { ResetsAtUtc = now.AddHours(4) };
        QuotaEarlyResetNotifications Seed(params QuotaWindow[] rows)
        {
            var engine = new QuotaEarlyResetNotifications();
            Check(engine.Observe(Observation(1, now, rows), true) is null, "first success seeds silently");
            return engine;
        }
        QuotaEarlyResetAlert? Transition(QuotaProvider provider, int minutes, string id, double oldUsed = 60, double used = 0,
            TimeSpan? gap = null, DateTimeOffset? oldReset = null, DateTimeOffset? reset = null)
        {
            var engine = new QuotaEarlyResetNotifications();
            QuotaWindow before = Row(oldUsed, minutes, id) with { ResetsAtUtc = oldReset ?? now.AddHours(2) };
            QuotaWindow after = Changed(used, minutes, id) with { ResetsAtUtc = reset ?? now.AddHours(4) };
            Check(engine.Observe(For(provider, 1, now, before), true) is null, "threshold seed " + provider + "/" + id);
            return engine.Observe(For(provider, 2, now + (gap ?? TimeSpan.FromHours(1)), after), true);
        }

        Check(QuotaEarlyResetText.SettingKey == "QuotaEarlyResetNotificationsEnabled", "independent stable setting key");
        Check(QuotaEarlyResetText.SettingKey != QuotaRecoveryText.SettingKey, "recovery consent is not early-reset consent");
        Check(QuotaEarlyResetText.MenuLabel == "한도 조기 초기화 알림", "shared menu label");
        Check(QuotaEarlyResetText.MenuDetail.Contains("기본 꺼짐", StringComparison.Ordinal) &&
            QuotaEarlyResetText.MenuDetail.Contains("연속 성공 조회", StringComparison.Ordinal) &&
            QuotaEarlyResetText.MenuDetail.Contains("추가 조회 없음", StringComparison.Ordinal), "conservative menu scope");
        Check(QuotaEarlyResetText.SettingReadFailed.Contains("꺼짐", StringComparison.Ordinal) &&
            QuotaEarlyResetText.SettingSaveFailed.Contains("기존 설정", StringComparison.Ordinal), "safe setting failure text");
        Check(QuotaEarlyResetText.Saved(true).Contains("다음 성공 조회", StringComparison.Ordinal) &&
            QuotaEarlyResetText.Saved(false) == "한도 조기 초기화 알림을 껐습니다.", "shared setting feedback");
        Check(QuotaEarlyResetText.DeliveryFailed.Contains("OS 알림 설정", StringComparison.Ordinal), "shared delivery failure feedback");

        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
        foreach ((int minutes, string id) in new[] { (300, "synthetic-session"), (10080, "synthetic-weekly"), (10080, "synthetic-model-weekly") })
        {
            string name = provider + "/" + id;
            QuotaEarlyResetAlert? alert = Transition(provider, minutes, id);
            Check(alert is not null && alert.Provider == provider && alert.Sequence == 2 && alert.ContinuityId == 1, name + " metadata");
            Check(alert!.CheckedAtUtc == now.AddHours(1) && alert.Windows.Count == 1 && alert.Windows[0].Id == id &&
                alert.Windows[0].WindowMinutes == minutes, name + " row and receipt retained");
            Check(alert.Windows[0].PreviousUsedPercent == 60 && alert.Windows[0].UsedPercent == 0 &&
                alert.Windows[0].PreviousResetUtc == now.AddHours(2) && alert.Windows[0].ResetUtc == now.AddHours(4), name + " local detail values retained");
            Check(Transition(provider, minutes, id, oldUsed: 30, used: 10) is not null, name + " exact 20-point drop and 10 percent accepted");
            Check(Transition(provider, minutes, id, oldUsed: Math.BitDecrement(30.0), used: 10) is null, name + " just under 20 points rejected");
            Check(Transition(provider, minutes, id, oldUsed: Math.BitIncrement(30.0), used: 10) is not null, name + " just over 20 points accepted");
            Check(Transition(provider, minutes, id, used: Math.BitIncrement(10.0)) is null, name + " just above 10 percent rejected");
            Check(Transition(provider, minutes, id, used: Math.BitDecrement(10.0)) is not null, name + " just below 10 percent accepted");
            Check(Transition(provider, minutes, id, oldUsed: 19.999999, used: 0) is null, name + " raw drop does not round to threshold");
            Check(Transition(provider, minutes, id, oldUsed: 20, used: 0) is not null, name + " exact zero with exact drop accepted");
            Check(Transition(provider, minutes, id, oldUsed: 0, used: 0) is null, name + " inactive zero reset drift excluded");
            Check(Transition(provider, minutes, id, oldUsed: 15, used: 0) is null, name + " small-to-zero reduction deliberately missed");
            Check(Transition(provider, minutes, id, oldUsed: 60, used: 35) is null, name + " a partial adjustment is not near reset");
            Check(Transition(provider, minutes, id, oldUsed: 99.96, used: 0) is not null, name + " raw nonexhaustion may still reset early");
            Check(Transition(provider, minutes, id, oldUsed: 100, used: 0) is not null, name + " exhausted reset also qualifies");

            foreach (long ticks in new[] { -1L, 0L, 1L })
            {
                DateTimeOffset oldReset = now.AddHours(1).AddMinutes(5).AddTicks(ticks);
                Check((Transition(provider, minutes, id, oldReset: oldReset) is not null) == (ticks > 0), name + " five-minute margin ticks " + ticks);
                DateTimeOffset later = now.AddHours(2).AddMinutes(1).AddTicks(ticks);
                Check((Transition(provider, minutes, id, reset: later) is not null) == (ticks > 0), name + " forward one-minute drift ticks " + ticks);
                DateTimeOffset earlier = now.AddHours(2).AddMinutes(-1).AddTicks(-ticks);
                Check((Transition(provider, minutes, id, reset: earlier) is not null) == (ticks > 0), name + " backward one-minute drift ticks " + ticks);
                TimeSpan gap = TimeSpan.FromMinutes(90) + TimeSpan.FromTicks(ticks);
                Check((Transition(provider, minutes, id, gap: gap) is not null) == (ticks <= 0), name + " ninety-minute continuity ticks " + ticks);
            }
            Check(Transition(provider, minutes, id, gap: TimeSpan.Zero) is null, name + " equal receipts cannot prove chronology");
            Check(Transition(provider, minutes, id, gap: TimeSpan.FromTicks(-1)) is null, name + " backwards clock silent rebase");
            Check(Transition(provider, minutes, id, oldReset: now.AddHours(4), reset: now.AddHours(3)) is not null,
                name + " reset may move earlier without claiming cause");
            Check(Transition(provider, minutes, id, reset: now.AddHours(2)) is null, name + " unchanged reset excluded");
            Check(Transition(provider, minutes, id, oldReset: now.AddHours(1)) is null, name + " normal scheduled reset excluded");
            Check(Transition(provider, minutes, id, oldReset: now.AddMinutes(59)) is null, name + " already elapsed reset excluded");
            Check(Transition(provider, minutes, id, oldReset: now) is null, name + " old expired window excluded");
            Check(Transition(provider, minutes, id, oldReset: now.AddMinutes(minutes).AddTicks(1)) is null,
                name + " old reset beyond full duration excluded");
            Check(Transition(provider, minutes, id, reset: now.AddHours(1)) is null, name + " new reset equal to receipt excluded");
            Check(Transition(provider, minutes, id, reset: now.AddMinutes(59)) is null, name + " new past reset excluded");
            Check(Transition(provider, minutes, id, reset: now.AddHours(1).AddMinutes(minutes).AddTicks(1)) is null,
                name + " new reset beyond full duration excluded");
            Check(Transition(provider, minutes, id, reset: now.AddHours(1).AddMinutes(minutes)) is not null,
                name + " exactly full duration is consistent");

            string scope = provider switch
            {
                QuotaProvider.Codex => "ChatGPT (Work/Codex)",
                QuotaProvider.Gemini => "Gemini (Antigravity)",
                _ => "Claude"
            };
            Check(QuotaEarlyResetText.Title(provider) == scope + " 한도 조기 초기화 가능성", name + " explicit quota service scope");
            string body = QuotaEarlyResetText.Body(alert);
            Check(body.Contains("예정보다 일찍 초기화된 것으로 보입니다", StringComparison.Ordinal) &&
                body.Contains("원인은 확인되지 않았습니다", StringComparison.Ordinal), name + " inference not declared fact");
            Check(!body.Contains('%') && !body.Contains("2026", StringComparison.Ordinal) && !body.Contains(id, StringComparison.Ordinal),
                name + " banner omits numbers, dates and internal identities");
        }

        var unknown = Seed(Row(minutes: null));
        Check(unknown.Observe(Observation(2, now.AddHours(1), Changed(minutes: null)), true) is null, "unknown duration never inferred from label");
        var absentOldReset = Seed(Row() with { ResetsAtUtc = null });
        Check(absentOldReset.Observe(Observation(2, now.AddHours(1), Changed()), true) is null, "missing old reset excluded");
        var absentNewReset = Seed(Row());
        Check(absentNewReset.Observe(Observation(2, now.AddHours(1), Changed() with { ResetsAtUtc = null }), true) is null,
            "missing new reset excluded");
        Check(absentNewReset.Observe(Observation(3, now.AddMinutes(61), Changed()), true) is null, "missing metadata does not revive stale baseline");
        var label = Seed(Row());
        Check(label.Observe(Observation(2, now.AddHours(1), Changed(label: "합성 새 표시명")), true)?.Windows[0].Label == "합성 새 표시명",
            "display label change retains identity and uses latest label");

        var independent = new QuotaEarlyResetNotifications();
        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
        {
            Check(independent.Observe(For(provider, 1, now, Row()), true) is null, "independent Provider baseline " + provider);
            Check(independent.Recent(provider) is null, "no history before confirmed comparison " + provider);
        }
        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
        {
            QuotaEarlyResetAlert? alert = independent.Observe(For(provider, 2, now.AddHours(1), Changed()), true);
            Check(alert?.Provider == provider && independent.Recent(provider) == alert, "separate latest history " + provider);
        }
        Check(independent.Recent((QuotaProvider)999) is null, "unknown Provider has no history");

        var changedPeriod = Seed(Row());
        Check(changedPeriod.Observe(Observation(2, now.AddHours(1), Changed(minutes: 10080)), true) is null, "duration change rebases");
        Check(changedPeriod.Observe(Observation(3, now.AddMinutes(61), Changed()), true) is null, "returning duration cannot reuse old state");
        var changedScope = Seed(Row());
        Check(changedScope.Observe(Observation(2, now.AddHours(1), Changed(id: "synthetic-other-scope")), true) is null, "scope change rebases");
        Check(changedScope.Observe(Observation(3, now.AddMinutes(61), Changed()), true) is null, "scope reappearance is a new baseline");
        var added = Seed(Row());
        Check(added.Observe(Observation(2, now.AddHours(1), Changed(), Row(id: "synthetic-extra")), true) is null,
            "whole Provider key-set change conservatively rebases even existing candidate");
        var removed = Seed(Row(), Row(id: "synthetic-extra"));
        Check(removed.Observe(Observation(2, now.AddHours(1), Changed()), true) is null, "removed row suppresses simultaneous candidate");
        Check(removed.Observe(Observation(3, now.AddMinutes(61), Changed(), Row(id: "synthetic-extra")), true) is null,
            "missing row reappearance suppresses old comparison");
        var otherPeriod = Seed(Row(), Row(id: "synthetic-extra"));
        Check(otherPeriod.Observe(Observation(2, now.AddHours(1), Changed(), Row(minutes: 10080, id: "synthetic-extra")), true) is null,
            "unrelated duration change rebases whole Provider");
        var reordered = Seed(Row(), Row(id: "synthetic-extra"));
        Check(reordered.Observe(Observation(2, now.AddHours(1), Changed(id: "synthetic-extra"), Changed()), true)?.Windows
            .Select(window => window.Id).SequenceEqual(new[] { "synthetic-extra", "synthetic-session" }) == true,
            "unchanged keys may reorder and grouped result preserves current display order");
        var unknownOther = Seed(Row(), Row(minutes: null, id: "synthetic-extra"));
        Check(unknownOther.Observe(Observation(2, now.AddHours(1), Changed(), Changed(minutes: null, id: "synthetic-extra")), true)?.Windows
            .Select(window => window.Id).SequenceEqual(new[] { "synthetic-session" }) == true,
            "unchanged unknown-period row stays in key set but cannot itself create a candidate");

        var legacy = new QuotaEarlyResetNotifications();
        Check(legacy.Observe(Observation(1, now, Row()) with { ContinuityId = 0 }, true) is null, "legacy observation seeds silently");
        Check(legacy.Observe(Observation(2, now.AddMinutes(1), Changed()) with { ContinuityId = 0 }, true) is null,
            "legacy zero cannot establish successful continuity");
        Check(legacy.Observe(Observation(3, now.AddMinutes(2), Row()), true) is null, "first modern continuity seeds silently");
        Check(legacy.Observe(Observation(4, now.AddMinutes(3), Changed()), true) is not null, "later consecutive modern observations compare");
        var orphan = Seed(Row());
        Check(orphan.Observe(Observation(2, now.AddMinutes(1), Changed()) with { ContinuityId = 0 }, true) is null,
            "orphan zero is not evidence");
        Check(orphan.Observe(Observation(2, now.AddMinutes(1), Changed()), true) is not null,
            "orphan zero cannot consume good continuity or sequence");
        var failed = Seed(Row());
        Check(failed.Observe(Observation(2, now.AddMinutes(1), Changed()) with { ContinuityId = 2 }, true) is null,
            "failed invalid timed-out or cancelled read breaks continuity");
        Check(failed.Observe(Observation(3, now.AddMinutes(2), Row()) with { ContinuityId = 2 }, true) is null,
            "post-failure rise establishes a fresh comparison");
        Check(failed.Observe(Observation(4, now.AddMinutes(3), Changed()) with { ContinuityId = 2 }, true) is not null,
            "new uninterrupted decrease after a failure may alert");
        var skipped = Seed(Row());
        Check(skipped.Observe(Observation(2, now.AddMinutes(1), Changed()), true) is not null,
            "skipped busy refresh does not change success sequence or continuity");
        var missingSuccess = Seed(Row());
        Check(missingSuccess.Observe(Observation(3, now.AddMinutes(1), Changed()), true) is null, "missing delivered success cannot be bridged");
        Check(missingSuccess.Observe(Observation(4, now.AddMinutes(2), Row()), true) is null, "gap rebase establishes current state");
        Check(missingSuccess.Observe(Observation(5, now.AddMinutes(3), Changed()), true) is not null,
            "subsequent adjacent observations work after a sequence gap");

        var clock = Seed(Row());
        Check(clock.Observe(Observation(2, now.AddTicks(-1), Changed()), true) is null, "backwards clock is silent");
        Check(clock.Observe(Observation(3, now.AddMinutes(1), Changed()), true) is null, "backwards-clock rebase does not replay old drop");
        var offsets = Seed(Row());
        Check(offsets.Observe(Observation(2, now.AddHours(1).ToOffset(TimeSpan.FromHours(-7)), Changed()), true) is not null,
            "receipt comparisons use instants, not display offset");
        var stale = Seed(Row());
        Check(stale.Observe(Observation(2, now.AddMinutes(91), Changed()), true) is null, "long-success-gap rebase silent");
        Check(stale.Observe(Observation(3, now.AddMinutes(92), Changed()), true) is null, "long gap cannot replay old decrease later");

        var enabled = new QuotaEarlyResetNotifications();
        Check(enabled.Observe(Observation(1, now, Row()), false) is null && enabled.Recent(QuotaProvider.Claude) is null,
            "disabled initial response creates no history");
        Check(enabled.Observe(Observation(2, now.AddMinutes(1), Changed()), false) is null && enabled.Recent(QuotaProvider.Claude) is null,
            "disabled candidate creates no history");
        Check(enabled.Observe(Observation(3, now.AddMinutes(2), Row()), true) is null, "first enabled response silently seeds");
        QuotaEarlyResetAlert? firstHistory = enabled.Observe(Observation(4, now.AddMinutes(3), Changed()), true);
        Check(firstHistory is not null && enabled.Recent(QuotaProvider.Claude) == firstHistory, "enabled transition retained locally");
        enabled.Reset();
        Check(enabled.Recent(QuotaProvider.Claude) == firstHistory, "setting reset retains session history");
        Check(enabled.Observe(Observation(5, now.AddMinutes(4), Row()), true) is null, "setting reset requires first silent baseline");
        Check(enabled.Observe(Observation(6, now.AddMinutes(5), Changed()), false) is null && enabled.Recent(QuotaProvider.Claude) == firstHistory,
            "disabled candidate neither replaces nor deletes observed history");
        Check(enabled.Observe(Observation(7, now.AddMinutes(6), Row()), true) is null, "reenable does not inherit disabled baseline");
        QuotaEarlyResetAlert? newest = enabled.Observe(Observation(8, now.AddMinutes(7), Changed()), true);
        Check(newest?.Sequence == 8 && enabled.Recent(QuotaProvider.Claude) == newest && newest != firstHistory,
            "only latest observed event retained per Provider");
        Check(new QuotaEarlyResetNotifications().Recent(QuotaProvider.Claude) is null, "new session has no persisted history");

        var duplicates = Seed(Row());
        Check(duplicates.Observe(Observation(1, now.AddMinutes(1), Changed()), true) is null, "duplicate sequence ignored");
        Check(duplicates.Observe(Observation(0, now.AddMinutes(1), Changed()), true) is null, "invalid older sequence ignored");
        QuotaEarlyResetAlert? once = duplicates.Observe(Observation(2, now.AddMinutes(1), Changed()), true);
        Check(once is not null, "first observed transition alerts");
        Check(duplicates.Observe(Observation(2, now.AddMinutes(2), Row()), true) is null, "duplicate cannot rearm");
        Check(duplicates.Observe(Observation(1, now.AddMinutes(2), Row()), true) is null, "out-of-order cannot rearm");
        Check(duplicates.Observe(Observation(3, now.AddMinutes(2), Changed()), true) is null, "repeated Refresh has no repeated drop");
        Check(duplicates.Recent(QuotaProvider.Claude) == once, "quiet repeated Refresh does not rewrite history receipt");
        Check(duplicates.Observe(Observation(4, now.AddMinutes(3), Row()), true) is null, "usage rise silently rearms a later new event");
        Check(duplicates.Observe(Observation(5, now.AddMinutes(4), Changed()), true)?.Sequence == 5, "new decrease after rise alerts again");
        var extremeSequence = new QuotaEarlyResetNotifications();
        Check(extremeSequence.Observe(Observation(long.MaxValue - 1, now, Row()), true) is null, "large success sequence seeds");
        Check(extremeSequence.Observe(Observation(long.MaxValue, now.AddHours(1), Changed()), true) is not null,
            "last positive success sequence does not overflow");

        var mutable = new List<QuotaWindow> { Row() };
        var frozen = new QuotaEarlyResetNotifications();
        Check(frozen.Observe(Observation(1, now) with { Reading = new(QuotaProvider.Claude, mutable) }, true) is null,
            "mutable input seeds");
        mutable[0] = Changed();
        QuotaEarlyResetAlert? immutable = frozen.Observe(Observation(2, now.AddHours(1), Changed()), true);
        Check(immutable is not null && immutable.Windows[0].PreviousUsedPercent == 60, "baseline owns a frozen row copy");
        bool readOnly = false;
        try { ((IList<QuotaEarlyResetWindow>)immutable!.Windows)[0] = immutable.Windows[0] with { UsedPercent = 10 }; }
        catch (NotSupportedException) { readOnly = true; }
        Check(readOnly && frozen.Recent(QuotaProvider.Claude)!.Windows[0].UsedPercent == 0, "retained alert windows are immutable");

        var manyRows = Enumerable.Range(0, 64).Select(index => Row(id: "private-synthetic-id-" + index,
            label: new string((char)('가' + index), 100) + "\r\n\t")).ToArray();
        var many = Seed(manyRows);
        QuotaEarlyResetAlert? manyAlert = many.Observe(Observation(2, now.AddHours(1), manyRows.Select(row => row with
            { UsedPercent = 0, ResetsAtUtc = now.AddHours(4) }).ToArray()), true);
        Check(manyAlert?.Windows.Count == 64, "bounded maximum rows all retained for detail");
        string bounded = QuotaEarlyResetText.Body(manyAlert!);
        Check(bounded.Length <= 255 && !bounded.Any(char.IsControl), "OS banner bounded and single-line");
        Check(bounded.Contains("외 62개", StringComparison.Ordinal) && !bounded.Contains("private-synthetic-id", StringComparison.Ordinal),
            "banner summarizes names without IDs");
        Check(manyAlert!.Windows.All(row => row.PreviousUsedPercent == 60 && row.UsedPercent == 0), "all retained row values frozen");

        QuotaEarlyResetAlert detailAlert = immutable! with { Windows = Array.AsReadOnly(new[] { immutable!.Windows[0] with { UsedPercent = 5 } }) };
        string detail = QuotaEarlyResetText.Detail(detailAlert, detailAlert.CheckedAtUtc.AddMinutes(17));
        Check(detail.Contains("사용 60% → 5%", StringComparison.Ordinal), "local detail includes previous/current synthetic usage");
        Check(detail.Contains("이전 리셋 예정:", StringComparison.Ordinal) && detail.Contains("다음 리셋 예정:", StringComparison.Ordinal) &&
            detail.Contains("2026-01-15 23:00:00 KST", StringComparison.Ordinal) && detail.Contains("2026-01-16 01:00:00 KST", StringComparison.Ordinal),
            "local detail includes exact old/new reset in KST");
        Check(detail.Contains("확인 17분 전", StringComparison.Ordinal) && detail.Contains("확인(조회 응답 수신)", StringComparison.Ordinal) &&
            !detail.Contains("마지막 조회 기준", StringComparison.Ordinal),
            "detail age is receipt age, not asserted event age");
        Check(detail.Contains("현재 한도 값이 아닌 당시의 확인 기록입니다", StringComparison.Ordinal),
            "retained event detail is explicitly historical rather than current quota");
        Check(detail.Contains("실제 초기화 시각·원인이나 전체 계정의 초기화를 확인한 것은 아닙니다", StringComparison.Ordinal) &&
            !detail.Contains("synthetic-session", StringComparison.Ordinal), "detail explains unsupported cause and hides internal ID");
        Check(QuotaEarlyResetText.Detail(detailAlert, detailAlert.CheckedAtUtc.AddTicks(-1)).Contains("\n확인 시각 확인 필요\n", StringComparison.Ordinal),
            "detail clock reversal has exact safe age wording and no negative duration");
        Check(QuotaEarlyResetText.Detail(detailAlert, detailAlert.CheckedAtUtc).Contains("\n방금 전 확인\n", StringComparison.Ordinal),
            "exact receipt instant has fresh confirmation wording");
        Check(QuotaEarlyResetText.Detail(detailAlert, detailAlert.CheckedAtUtc.AddSeconds(59)).Contains("\n방금 전 확인\n", StringComparison.Ordinal),
            "sub-minute confirmation age is not rounded up");
        Check(QuotaEarlyResetText.Detail(detailAlert, detailAlert.CheckedAtUtc.AddMinutes(1)).Contains("\n확인 1분 전\n", StringComparison.Ordinal),
            "one-minute confirmation boundary");
        Check(QuotaEarlyResetText.Detail(detailAlert, detailAlert.CheckedAtUtc.AddHours(1)).Contains("\n확인 1시간 전\n", StringComparison.Ordinal),
            "one-hour confirmation boundary omits zero minutes");
        Check(QuotaEarlyResetText.Detail(detailAlert, detailAlert.CheckedAtUtc.AddHours(25)).Contains("\n확인 1일 1시간 전\n", StringComparison.Ordinal),
            "old session history reports confirmation days and hours");

        var recovery = new QuotaRecoveryNotifications();
        var early = new QuotaEarlyResetNotifications();
        QuotaObservation exhausted = Observation(1, now, Row(100), Row(100, 10080, "synthetic-weekly", "합성 주간"));
        Check(recovery.Observe(exhausted, true) is null && early.Observe(exhausted, true) is null, "combined engines seed independently");
        QuotaObservation recovered = Observation(2, now.AddHours(1), Changed(), Row(20, 10080, "synthetic-weekly", "합성 주간"));
        QuotaRecoveryAlert? recoveryAlert = recovery.Observe(recovered, true);
        QuotaEarlyResetAlert? earlyAlert = early.Observe(recovered, true);
        Check(recoveryAlert?.WindowIds.Count == 2 && earlyAlert?.Windows.Count == 1, "same response can qualify for both causes");
        QuotaNotificationAlert? combined = QuotaNotificationText.Combine(recoveryAlert, earlyAlert);
        Check(combined is not null && combined.Provider == QuotaProvider.Claude && combined.Sequence == 2 &&
            combined.Recovery == recoveryAlert && combined.EarlyReset == earlyAlert, "one Provider notification preserves both provenance records");
        Check(combined!.Title == QuotaEarlyResetText.Title(QuotaProvider.Claude) &&
            combined.Body.Contains("다른 한도에도 잔여량이 확인되었습니다", StringComparison.Ordinal), "early inference takes precedence with distinct recovered row summary");
        Check(!combined.Body.Contains('%') && !combined.Body.Contains("2026", StringComparison.Ordinal) &&
            !combined.Body.Contains("synthetic-", StringComparison.Ordinal), "combined banner remains privacy-safe");
        QuotaNotificationAlert? sameRows = QuotaNotificationText.Combine(recoveryAlert! with { WindowIds = Array.AsReadOnly(new[] { "synthetic-session" }) }, earlyAlert);
        Check(sameRows is not null && sameRows.Body == QuotaEarlyResetText.Body(earlyAlert!), "same recovered row not duplicated in body");
        QuotaNotificationAlert? legacyOnly = QuotaNotificationText.Combine(recoveryAlert, null);
        Check(legacyOnly is not null && legacyOnly.Title == recoveryAlert!.Title && legacyOnly.Body == recoveryAlert.Body && legacyOnly.EarlyReset is null,
            "recovery-only behavior and text exact legacy contract");
        Check(QuotaNotificationText.Combine(null, earlyAlert)?.Body == QuotaEarlyResetText.Body(earlyAlert!), "early-only setting works independently");
        Check(QuotaNotificationText.Combine(null, null) is null, "no candidate creates no notification");
        Check(QuotaNotificationText.Combine(recoveryAlert! with { Provider = QuotaProvider.Codex }, earlyAlert) is null,
            "different Provider candidates never cross-combine");
        Check(QuotaNotificationText.Combine(recoveryAlert! with { Sequence = 3 }, earlyAlert) is null,
            "different success candidates never cross-combine");
        var manyRecovery = new QuotaRecoveryAlert(QuotaProvider.Claude, 2, "합성 제목", "합성 본문", Array.AsReadOnly(new[] { "different-row" }));
        Check(QuotaNotificationText.Combine(manyRecovery, manyAlert)?.Body.Length <= 255, "combined maximum banner remains bounded");
        QuotaEarlyResetAlert unusableLabel = detailAlert with
            { Windows = Array.AsReadOnly(new[] { detailAlert.Windows[0] with { Label = "\r\n\t" } }) };
        Check(QuotaEarlyResetText.Body(unusableLabel).StartsWith("확인한 한도", StringComparison.Ordinal), "empty sanitized label uses generic subject");

        var invalidFixtures = new List<QuotaObservation>
        {
            Observation(2, now.AddHours(1), Changed()) with { Provider = (QuotaProvider)999 },
            Observation(2, now.AddHours(1), Changed()) with { Sequence = -1 },
            Observation(2, now.AddHours(1), Changed()) with { ContinuityId = -1 },
            Observation(2, now.AddHours(1), Changed()) with { CheckedAtUtc = DateTimeOffset.UnixEpoch.AddTicks(-1) },
            Observation(2, now.AddHours(1), Changed()) with { CheckedAtUtc = new DateTimeOffset(9001, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            Observation(2, now.AddHours(1), Changed()) with { Reading = null! },
            Observation(2, now.AddHours(1), Changed()) with { Reading = new(QuotaProvider.Gemini, [Changed()]) },
            Observation(2, now.AddHours(1), Changed()) with { Reading = new(QuotaProvider.Claude, null!) },
            Observation(2, now.AddHours(1)),
            Observation(2, now.AddHours(1), Enumerable.Range(0, 65).Select(index => Changed(id: "extra-" + index)).ToArray()),
            Observation(2, now.AddHours(1), (QuotaWindow)null!),
            Observation(2, now.AddHours(1), Changed(), Changed()),
            Observation(2, now.AddHours(1), Changed(id: " ")),
            Observation(2, now.AddHours(1), Changed(id: new string('a', 513))),
            Observation(2, now.AddHours(1), Changed(label: " ")),
            Observation(2, now.AddHours(1), Changed(label: new string('a', 181))),
            Observation(2, now.AddHours(1), Changed(minutes: 0)),
            Observation(2, now.AddHours(1), Changed(minutes: -1)),
            Observation(2, now.AddHours(1), Changed(minutes: 5_256_001)),
            Observation(2, now.AddHours(1), Changed() with { ResetsAtUtc = DateTimeOffset.UnixEpoch.AddTicks(-1) }),
            Observation(2, now.AddHours(1), Changed() with { ResetsAtUtc = new DateTimeOffset(9001, 1, 1, 0, 0, 0, TimeSpan.Zero) })
        };
        foreach (double used in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.1, 100.1 })
            invalidFixtures.Add(Observation(2, now.AddHours(1), Changed(used)));
        foreach (QuotaObservation invalid in invalidFixtures)
        {
            var engine = Seed(Row());
            Check(engine.Observe(invalid, true) is null && engine.Recent(QuotaProvider.Claude) is null,
                "malformed observation ignored without history or throw");
            Check(engine.Observe(Observation(2, now.AddHours(1), Changed()), true) is not null,
                "malformed observation cannot consume valid success sequence or poison comparison");
        }
        Check(new QuotaEarlyResetNotifications().Observe(null!, true) is null, "null observation safely ignored");
        return count;
    }
}
