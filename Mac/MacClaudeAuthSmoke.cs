using AppKit;
using CoreGraphics;

namespace AiBurgerClock;

// Native smoke for the Claude CLI login-expiry display and warning (Mac side of the shared ClaudeAuthCheck). Synthetic values
// only: every date and the login states below are made up and have nothing to do with a real account. No CLI is started,
// no notification is requested and the real warning setting of the app is not changed.
//   1. the Claude box, its tip and the popover for the login states (far / 7-day warning / expired / signed out / not a
//      subscription login / unknown / recovered by a newer quota success), all in the shared wording,
//   2. the menu item (default OFF, order, tooltip) and what the warning does (scripted clock, fake delivery):
//      one banner on entering the 7-day range, none again for the same end date, a new one for a later end date,
//   3. the setting and the warned end date through the real shared store (restart).
internal static class MacClaudeAuthSmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Claude login warning: " + message);
    }

    private static ClaudeAuthCheck Auth(ClaudeAuthOutcome outcome, string? method, DateTimeOffset? expiry, DateTimeOffset checkedAt) =>
        new(new ClaudeAuthStatus(outcome, method, expiry), checkedAt);

    internal static string VerifyPanel(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        QuotaState codex, QuotaState claude, QuotaState gemini, QuotaState xai, DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        DateTimeOffset after = utc.AddMinutes(1); // a login check newer than the quota success of the fixture
        QuotaState With(ClaudeAuthCheck auth, bool reading = true) => reading ? claude with { ClaudeAuth = auth }
            : new QuotaState(QuotaProvider.Claude, CheckedAtUtc: utc, NextCheckUtc: utc.AddMinutes(83), ClaudeAuth: auth);

        MacStatusPanel.Layout Show(QuotaState claudeState)
        {
            panel.Update(schedule, providers, [codex, claudeState, gemini, xai], false, now.AddMinutes(5), "");
            return panel.Verify(schedule, providers, [codex, claudeState, gemini, xai]);
        }

        MacStatusPanel.Layout baseline = Show(claude);
        int baselineBars = panel.QuotaBarCount;
        double largestTip = 0;
        var names = new List<string>();

        // (name, state, expected box note line or null, expected tone, expected phrases in the tip, forbidden phrases)
        (string Name, QuotaState State, string? Scope, PanelTone Tone, string[] Tip, string[] NotTip)[] scenes =
        [
            ("end far away", With(Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", utc.AddDays(20), after)), null, PanelTone.Normal,
                ["Claude CLI 로그인 상태", "로그인 종료: 늦어도 ", "KST(더 일찍 끝날 수 있음)", "로그인 상태 마지막 확인: "], [ClaudeAuthCheck.ExpiringText]),
            ("7 days left", With(Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", utc.AddDays(5), after)), ClaudeAuthCheck.ExpiringText, PanelTone.Caution,
                ["Claude CLI 로그인 상태", "로그인 종료: 늦어도 "], [ClaudeAuthCheck.LoginRequiredText]),
            ("exactly 7 days", With(Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", utc.AddDays(ClaudeAuthCheck.WarningLeadDays), after)),
                ClaudeAuthCheck.ExpiringText, PanelTone.Caution, ["Claude CLI 로그인 상태"], []),
            ("7 days and a second", With(Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", utc.AddDays(ClaudeAuthCheck.WarningLeadDays).AddSeconds(1), after)),
                null, PanelTone.Normal, ["Claude CLI 로그인 상태"], [ClaudeAuthCheck.ExpiringText]),
            ("ended, earlier reading kept", With(Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", utc.AddHours(-1), after)),
                ClaudeAuthCheck.LoginRequiredText, PanelTone.Danger, ["Claude CLI 로그인 상태"], []),
            ("signed out, earlier reading kept", With(Auth(ClaudeAuthOutcome.SignedOut, null, null, after)),
                ClaudeAuthCheck.LoginRequiredText, PanelTone.Danger, ["Claude CLI 로그인 상태", ClaudeAuthCheck.LoginRequiredText], []),
            ("signed out, no reading", With(Auth(ClaudeAuthOutcome.SignedOut, null, null, after), reading: false),
                null, PanelTone.Danger, ["Claude CLI 로그인 상태"], []),
            ("newer quota success", With(Auth(ClaudeAuthOutcome.SignedOut, null, null, utc.AddMinutes(-10))), null, PanelTone.Normal,
                ["Claude CLI 로그인 상태"], [ClaudeAuthCheck.LoginRequiredText]),
            ("not a subscription login", With(Auth(ClaudeAuthOutcome.SignedIn, "api_key", null, after)), null, PanelTone.Normal,
                ["구독 로그인이 아님(인증 방식: api_key)", "종료 시점 미제공"], [ClaudeAuthCheck.ExpiringText]),
            ("unknown", With(Auth(ClaudeAuthOutcome.Unknown, null, null, after)), null, PanelTone.Normal,
                [ClaudeAuthCheck.UnknownText], [ClaudeAuthCheck.LoginRequiredText, ClaudeAuthCheck.ExpiringText])
        ];

        foreach (var (name, state, scope, tone, tipHas, tipLacks) in scenes)
        {
            MacStatusPanel.Layout layout = Show(state);
            string where = name + ": ";
            QuotaSectionText section = QuotaPanelModel.Sections([codex, state, gemini, xai], now).Single(item => item.Provider == QuotaProvider.Claude);
            // Verify has already compared every line, the rows and the tip with the shared panel model; these are the shared words.
            if (name == "signed out, no reading")
            {
                Check(section.Rows is [{ Text: ClaudeAuthCheck.LoginRequiredText, Tone: PanelTone.Danger }] &&
                      panel.QuotaRowText(QuotaProvider.Claude, section.Rows[0].WindowId) == ClaudeAuthCheck.LoginRequiredText,
                    where + "the waiting row is not the shared sign-in text in the danger tone.");
                Check(panel.QuotaBarCount == baselineBars - 3, where + $"{panel.QuotaBarCount - baselineBars + 3} Claude bars, expected none.");
                Check(panel.QuotaRowTextFits(QuotaProvider.Claude, section.Rows[0].WindowId), where + "the sign-in text is cut off instead of shown whole.");
            }
            else
            {
                Check(section.Scope?.Text == scope && (scope is null || section.Scope!.Tone == tone),
                    where + $"the note line is '{section.Scope?.Text}' ({section.Scope?.Tone}), expected '{scope}' ({tone}).");
                Check(panel.QuotaBarCount == baselineBars, where + "the Claude bars changed with the login state.");
                if (scope is null && tone == PanelTone.Normal) Check(section.Rows.All(row => row.Tone != PanelTone.Danger), where + "a danger row without a reason.");
            }
            if (name.StartsWith("ended", StringComparison.Ordinal) || name.StartsWith("signed out, earlier", StringComparison.Ordinal))
                Check(section.Heading.Text.Contains("이전 조회값", StringComparison.Ordinal), where + "the earlier reading is not marked as earlier.");
            string detail = panel.QuotaDetail(QuotaProvider.Claude);
            Check(detail == section.Detail, where + "the box carries a text other than the shared detail.");
            foreach (string phrase in tipHas) Check(detail.Contains(phrase, StringComparison.Ordinal), where + $"the tip lacks '{phrase}'.");
            foreach (string phrase in tipLacks) Check(!detail.Contains(phrase, StringComparison.Ordinal), where + $"the tip contains '{phrase}'.");
            MacDetailTip.Metrics tip = MacDetailTip.Measure(detail);
            Check(Math.Abs((double)tip.Size.Width - MacDetailTip.Width) < 0.5 && tip.PlacedCharacters == tip.Characters,
                where + $"the tip does not lay out whole at {MacDetailTip.Width}pt (placed {tip.PlacedCharacters} of {tip.Characters}).");
            largestTip = Math.Max(largestTip, (double)tip.Size.Height);
            Check(panel.QuotaAreaHeight <= 340.5 && layout.Size.Height <= layout.UsableHeight, where + "the popover or quota area is too tall.");
            names.Add(name);
        }

        // The warning line adds one row to the Claude box: the whole popover still fits the two usual screens.
        Show(scenes[1].State);
        double[] screens = [800, 640];
        double[] fitted = new double[screens.Length];
        for (int index = 0; index < screens.Length; index++)
        {
            double usable = screens[index] - (25 + 24);
            CGSize size = panel.FitFor((nfloat)usable);
            fitted[index] = size.Height;
            Check(size.Height <= usable + 0.5 && panel.QuotaBoxScrollsIntoView(QuotaProvider.Claude) && panel.QuotaBoxScrollsIntoView(QuotaProvider.Xai),
                $"popover {size.Height:0}pt is cut off or a box is out of reach on a {screens[index]}pt screen.");
        }
        panel.FitFor(baseline.UsableHeight);
        Show(claude);
        return $"Claude CLI login states ({string.Join(" / ", names)}: shared note line + tone, shared tip sections, " +
               $"tip fits {MacDetailTip.Width}pt whole (tallest {largestTip:0}pt), other boxes unchanged, 1280x800 {fitted[0]:0}pt and 1024x640 {fitted[1]:0}pt not cut off)";
    }

    // Menu item and behavior with a scripted clock and fake delivery, then the real store for the setting and the warned end date.
    internal static async Task<string> VerifyWarningAsync(UsageStore store, MacClaudeAuthWarning real, NSMenu contextMenu,
        MacQuotaNotifications quota, CancellationToken cancellationToken)
    {
        // ---- The app's own instance: in the menu right after the two quota items, OFF by default, the shared label and tooltip ----
        Check(real.MenuItem.Title == ClaudeAuthCheck.MenuLabel && contextMenu.Items.Contains(real.MenuItem) &&
              Array.IndexOf(contextMenu.Items, real.MenuItem) == Array.IndexOf(contextMenu.Items, quota.EarlyResetMenuItem) + 1,
            "the menu item is not wired right after the early-reset item.");
        Check(real.Ready && !real.Enabled && !real.Checked && real.MenuItem.Enabled, "the app item is not ready and OFF by default.");
        Check(real.MenuItem.ToolTip == $"Claude CLI 로그인 종료 상한까지 {ClaudeAuthCheck.WarningLeadDays}일 이내가 되면 로그인 주기당 한 번 알립니다.",
            "the tooltip is not the Windows wording.");

        var utc = new DateTimeOffset(2026, 11, 24, 3, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = utc;
        ClaudeAuthCheck? auth = null;
        bool canShow = true, saveFails = false, readFails = false, showFails = false, rememberFails = false;
        bool stopping = false;
        int defers = 0;
        var shown = new List<(string Title, string Body)>();
        var feedback = new List<(string Text, bool Error)>();
        var stored = (Enabled: false, Warned: (DateTimeOffset?)null);
        TaskCompletionSource? saveGate = null;

        MacClaudeAuthWarning Make(HashSet<Task>? writes = null) => new(() => stopping,
            () => readFails ? Task.FromException<(bool, DateTimeOffset?)>(new IOException("read")) : Task.FromResult(stored),
            async value =>
            {
                if (saveGate is not null) await saveGate.Task;
                if (saveFails) throw new IOException("save");
                stored.Enabled = value;
            },
            expiry =>
            {
                if (rememberFails) return Task.FromException(new IOException("remember"));
                stored.Warned = expiry;
                return Task.CompletedTask;
            },
            () => auth, () => canShow,
            (title, body) =>
            {
                if (showFails) throw new InvalidOperationException("delivery");
                shown.Add((title, body));
            },
            () => defers++, (text, error) => feedback.Add((text, error)), writes ?? [], () => now);

        ClaudeAuthCheck Subscription(TimeSpan left) =>
            Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", utc.Add(left), utc);

        // ---- Before the setting is read nothing happens, whatever the login state ----
        MacClaudeAuthWarning item = Make();
        auth = Subscription(TimeSpan.FromDays(3));
        item.TryShow();
        Check(!item.Ready && !item.MenuItem.Enabled && shown.Count == 0, "an alert was shown or the item usable before the setting was read.");

        // ---- Default OFF: loaded, usable, unchecked, silent ----
        await item.LoadAsync();
        Check(item.Ready && !item.Enabled && !item.Checked && item.MenuItem.Enabled, "after loading, the item is not usable and OFF.");
        item.TryShow();
        Check(shown.Count == 0 && defers == 0, "an alert was shown while the setting is OFF.");

        // ---- ON: nothing outside the 7-day range, one banner on entering it ----
        await item.ToggleAsync();
        Check(item.Enabled && item.Checked && stored.Enabled && item.MenuItem.Enabled && !item.Changing, "switching ON did not save and check the item.");
        auth = Subscription(TimeSpan.FromDays(ClaudeAuthCheck.WarningLeadDays).Add(TimeSpan.FromSeconds(1)));
        item.TryShow();
        Check(shown.Count == 0, "an alert was shown just outside the 7-day range.");
        DateTimeOffset firstEnd = utc.AddDays(ClaudeAuthCheck.WarningLeadDays);
        auth = Subscription(TimeSpan.FromDays(ClaudeAuthCheck.WarningLeadDays));
        canShow = false;
        item.TryShow();
        Check(shown.Count == 0 && defers == 0 && item.WarnedExpiry is null, "the alert did not wait behind another alert.");
        canShow = true;
        item.TryShow();
        Check(shown is [{ Title: ClaudeAuthCheck.AlertTitle, Body: ClaudeAuthCheck.ExpiringText }] && defers == 1 && item.WarnedExpiry == firstEnd &&
              stored.Warned == firstEnd, "entering the 7-day range must show one banner in the shared words, defer the others and remember the end date.");
        item.TryShow();
        item.TryShow();
        Check(shown.Count == 1 && defers == 1, "the same end date was announced again.");

        // ---- A later end date (a new login) is a new warning; other states never warn ----
        now = utc.AddDays(40);
        auth = Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", now.AddDays(2), now);
        item.TryShow();
        Check(shown.Count == 2 && item.WarnedExpiry == now.AddDays(2), "a later end date did not warn again.");
        foreach (ClaudeAuthCheck? other in new ClaudeAuthCheck?[]
        {
            null,
            Auth(ClaudeAuthOutcome.SignedOut, null, null, now),
            Auth(ClaudeAuthOutcome.Unknown, null, null, now),
            Auth(ClaudeAuthOutcome.SignedIn, "api_key", now.AddDays(1), now),
            Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", now.AddHours(-1), now),
            Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", null, now),
            Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", now.AddDays(10), now)
        })
        {
            auth = other;
            item.TryShow();
        }
        Check(shown.Count == 2, "an alert was shown for a state that has no 7-day login end.");

        // ---- Shutdown and OFF silence it; the saved history survives ----
        auth = Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", now.AddDays(3), now);
        stopping = true;
        item.TryShow();
        stopping = false;
        Check(shown.Count == 2, "an alert was shown while stopping.");
        await item.ToggleAsync();
        Check(!item.Enabled && !item.Checked && !stored.Enabled, "switching OFF did not save and uncheck the item.");
        item.TryShow();
        Check(shown.Count == 2, "an alert was shown after switching OFF.");

        // ---- The item cannot be used while a save is running; a failed save keeps the old state ----
        saveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task toggling = item.ToggleAsync();
        Check(item.Changing && !item.MenuItem.Enabled, "the item stayed usable while its save was running.");
        await item.ToggleAsync();
        saveGate.SetResult();
        await toggling;
        saveGate = null;
        Check(item.Enabled && stored.Enabled && item.MenuItem.Enabled && !item.Changing, "the saved ON state is wrong after the save finished.");
        saveFails = true;
        await item.ToggleAsync();
        saveFails = false;
        Check(item.Enabled && item.Checked && item.MenuItem.Enabled && feedback.Count == 1 &&
              feedback[0] == (MacClaudeAuthWarning.SaveFailed, true), "a failed save must keep the old state and say so.");

        // ---- Delivery and history failures are reported once, the end date is still consumed ----
        feedback.Clear();
        now = utc.AddDays(80);
        auth = Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", now.AddDays(1), now);
        showFails = true;
        item.TryShow();
        showFails = false;
        Check(shown.Count == 2 && feedback is [{ Text: MacClaudeAuthWarning.DeliveryFailed, Error: true }] && item.WarnedExpiry == now.AddDays(1) &&
              defers == 3, "a failed delivery must be reported once and consume the end date.");
        item.TryShow();
        Check(shown.Count == 2, "a failed delivery was retried.");
        feedback.Clear();
        now = utc.AddDays(120);
        auth = Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", now.AddDays(1), now);
        rememberFails = true;
        item.TryShow();
        await Task.Delay(50, cancellationToken);
        rememberFails = false;
        Check(shown.Count == 3 && feedback is [{ Text: MacClaudeAuthWarning.HistoryFailed, Error: true }], "a failed history write must be reported once.");

        // ---- An unreadable setting: reported, never ON, the item stays unusable ----
        feedback.Clear();
        readFails = true;
        MacClaudeAuthWarning unreadable = Make();
        await unreadable.LoadAsync();
        readFails = false;
        auth = Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", now.AddDays(1), now);
        unreadable.TryShow();
        Check(!unreadable.Ready && !unreadable.Enabled && !unreadable.MenuItem.Enabled && feedback is [{ Text: MacClaudeAuthWarning.ReadFailed, Error: true }] &&
              shown.Count == 3, "an unreadable setting must be reported and leave the item OFF and unusable.");

        // ---- Restart: the setting and the warned end date come back from the shared store; the same end date stays quiet ----
        stored = (true, null);
        now = utc;
        DateTimeOffset end = utc.AddDays(5);
        auth = Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", end, utc);
        int before = shown.Count;
        MacClaudeAuthWarning first = Make();
        await first.LoadAsync();
        Check(first.Enabled && first.Checked && first.WarnedExpiry is null, "a restart did not restore the ON setting.");
        first.TryShow();
        Check(shown.Count == before + 1 && stored.Warned == end, "the first run did not warn once.");
        MacClaudeAuthWarning restarted = Make();
        await restarted.LoadAsync();
        Check(restarted.Enabled && restarted.WarnedExpiry == end, "a restart did not restore the warned end date.");
        restarted.TryShow();
        Check(shown.Count == before + 1, "a restart announced the same end date again.");
        auth = Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", end.AddDays(25), utc.AddDays(26));
        now = utc.AddDays(26);
        restarted.TryShow();
        Check(shown.Count == before + 2, "a later end date after a restart did not warn.");

        // ---- The real shared store: the keys Windows uses, round trip ----
        (bool Enabled, DateTimeOffset? Warned) saved = await store.ReadClaudeAuthWarningAsync(cancellationToken);
        Check(!saved.Enabled && saved.Warned is null, "the smoke database does not start with the setting OFF and no warned end date.");
        DateTimeOffset realEnd = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        MacClaudeAuthWarning viaStore = new(() => false, () => store.ReadClaudeAuthWarningAsync(cancellationToken),
            value => store.SetClaudeAuthWarningEnabledAsync(value, cancellationToken), expiry => store.RememberClaudeAuthWarningAsync(expiry, cancellationToken),
            () => Auth(ClaudeAuthOutcome.SignedIn, "claude.ai", realEnd, utc), () => true, (_, _) => { }, () => { }, (_, _) => { }, [],
            () => realEnd.AddDays(-3));
        await viaStore.LoadAsync();
        await viaStore.ToggleAsync();
        viaStore.TryShow();
        await Task.Delay(100, cancellationToken);
        saved = await store.ReadClaudeAuthWarningAsync(cancellationToken);
        Check(saved.Enabled && saved.Warned == realEnd, "the shared store did not keep the setting and the warned end date.");
        await store.SetClaudeAuthWarningEnabledAsync(false, cancellationToken);
        await store.RememberClaudeAuthWarningAsync(DateTimeOffset.UnixEpoch, cancellationToken);

        return "Claude login warning (menu item right after the early-reset item, OFF by default, Windows tooltip; one banner on entering the 7-day range and none for the same end date again, " +
               "a later end date warns again, none for signed out/unknown/non-subscription/ended/no end/far end; waits behind other alerts; silent while stopping or OFF; item locked during a save, failed save keeps the state; " +
               "failed delivery/history/read reported once; restart restores the setting and the warned end date; shared store round trip)";
    }
}
