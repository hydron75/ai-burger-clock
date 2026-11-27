using AppKit;
using CoreGraphics;

namespace AiBurgerClock;

// Native smoke for the Gemini app usage reader and its Mac UI (0.9.6). Synthetic values only: every page text, percentage and
// time below is made up and has nothing to do with a real account. No web view is created, no network is used and the
// real settings file is not touched; the actual WKWebView sign-in and hidden read are checked by the user by hand.
//   1. the page-reading loop (250 ms retries for 5 s, redirects, URL cut, 30 s limit) driven with scripted answers,
//   2. the settings file, the menu items and what each item does (recording fake backend),
//   3. the Gemini box, its tip and the popover in the five states off / sign-in needed / normal / format changed / load failed,
//   4. the real backend with a monitor that is never started and a temporary database: source mode and cache delete.
internal static class MacGeminiWebSmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Gemini web: " + message);
    }

    private static string Page(string url, string? text) =>
        "{\"url\":\"" + url + "\",\"text\":" + (text is null ? "null" : "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"") + "}";

    private const string UsageUrl = "https://gemini.google.com/usage?hl=en";
    private const string UsageText = "Usage limits\nSYNTHETIC PLAN\n\nYour plan's limits (synthetic text).\n\nUpdated just now\n\nCurrent usage\ninfo\n40% used\nResets at 3:00 PM\n" +
        "Weekly limit\nResets Nov 27 at 9:00 AM\n12% used\nGet 5x more usage (synthetic)\n$0/month\nUpgrade";

    internal static async Task<string> VerifyReadingAsync()
    {
        Check(MacGeminiPageReading.RetryMilliseconds == 250 && MacGeminiPageReading.RetryWindowMilliseconds == 5000 &&
              MacGeminiPageReading.TotalTimeoutSeconds == 30, "the retry interval, retry window or total limit is not 250 ms / 5 s / 30 s.");
        var now = new DateTimeOffset(2026, 11, 21, 14, 0, 0, TimeSpan.Zero);

        async Task<(GeminiUsagePageLoad Load, int Attempts, List<TimeSpan> Delays)> Run(Func<int, string?> answer, CancellationToken token = default)
        {
            int attempts = 0;
            var delays = new List<TimeSpan>();
            var load = await MacGeminiPageReading.ReadAsync(_ => Task.FromResult(answer(attempts++)),
                (span, _) => { delays.Add(span); return Task.CompletedTask; }, token);
            return (load, attempts, delays);
        }

        // Ready on the first try: no wait, no retry, and the parser accepts what was read (Ok, two windows).
        var ready = await Run(_ => Page(UsageUrl, UsageText));
        var parsed = GeminiUsagePage.Parse(ready.Load, now, TimeZoneInfo.Utc);
        Check(ready.Attempts == 1 && ready.Delays.Count == 0 && parsed.Outcome == GeminiUsageOutcome.Ok &&
              parsed.Reading!.Windows.Select(window => window.UsedPercent).SequenceEqual([40, 12]),
              "a page that is ready was not read once and accepted.");
        // The URL is cut to scheme, host and path: no query reaches the parser (or anything after it).
        Check(ready.Load.FinalUrl == "https://gemini.google.com/usage", "the usage URL kept its query.");

        // The element appears late: retried every 250 ms until it is there.
        var late = await Run(attempt => Page(UsageUrl, attempt < 3 ? null : UsageText));
        Check(late.Attempts == 4 && late.Delays.Count == 3 && late.Delays.All(span => span == TimeSpan.FromMilliseconds(250)) &&
              GeminiUsagePage.Parse(late.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.Ok,
              "a late element was not retried every 250 ms.");

        // Never appears: 20 retries = 5 s of waiting (21 reads), then the page as it is is a format change.
        var missing = await Run(_ => Page(UsageUrl, null));
        Check(missing.Attempts == 21 && missing.Delays.Count == 20 && missing.Delays.Aggregate(TimeSpan.Zero, (sum, span) => sum + span) == TimeSpan.FromSeconds(5) &&
              GeminiUsagePage.Parse(missing.Load, now, TimeZoneInfo.Utc) is { Outcome: GeminiUsageOutcome.FormatChanged },
              "a missing element was not retried for exactly 5 s and reported as a format change.");
        // A snapshot that has the headings but not yet the weekly value is read again, not accepted as it is.
        const string PartialText = "Usage limits\nSYNTHETIC PLAN\n\nCurrent usage\ninfo\n40% used\nResets at 3:00 PM\nWeekly limit";
        var partial = await Run(attempt => Page(UsageUrl, attempt < 2 ? PartialText : UsageText));
        Check(partial.Attempts == 3 && partial.Delays.Count == 2 && GeminiUsagePage.Parse(partial.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.Ok,
            "a partly filled page was accepted before the weekly values appeared.");
        var partialOnly = await Run(_ => Page(UsageUrl, PartialText));
        Check(partialOnly.Attempts == 21 && GeminiUsagePage.Parse(partialOnly.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.FormatChanged,
            "a page that stays partly filled was not a format change after 5 s.");
        var incomplete = await Run(_ => Page(UsageUrl, "Usage limits\nSYNTHETIC PLAN"));
        Check(incomplete.Attempts == 21 && GeminiUsagePage.Parse(incomplete.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.FormatChanged,
            "a usage page without the two sections was not retried and reported as a format change.");

        // Another page does not end the read: it keeps looking for the whole 5 s window and returns the last page seen, with
        // only host and path (a sign-in token in the query never leaves the web view). The parser calls that "sign-in needed".
        var app = await Run(_ => Page("https://gemini.google.com/app?synthetic-token=ABC", null));
        Check(app.Attempts == 21 && app.Delays.Aggregate(TimeSpan.Zero, (sum, span) => sum + span) == TimeSpan.FromSeconds(5) &&
              app.Load.FinalUrl == "https://gemini.google.com/app" &&
              GeminiUsagePage.Parse(app.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.LoginRequired, "a page that stays on the app page was not reported as sign-in needed after 5 s.");
        const string signIn = "https://accounts.google.com/v3/signin/identifier?continue=synthetic-token#fragment";
        var accounts = await Run(_ => Page(signIn, null));
        Check(accounts.Attempts == 21 && accounts.Load.FinalUrl == "https://accounts.google.com/v3/signin/identifier" &&
              GeminiUsagePage.Parse(accounts.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.LoginRequired, "a page that stays on the sign-in host kept a query or fragment or was not sign-in needed.");
        // Through another host and back to the usage page: the sections are read there (Ok), no sign-in needed.
        var bounce = await Run(attempt => attempt < 4 ? Page(signIn, null) : Page(UsageUrl, UsageText));
        Check(bounce.Attempts == 5 && bounce.Delays.Count == 4 && GeminiUsagePage.Parse(bounce.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.Ok,
            "a pass through another host and back to the usage page was not read.");
        // Back on the usage page but the values come late: still read.
        var bounceLate = await Run(attempt => attempt < 3 ? Page(signIn, null) : attempt < 8 ? Page(UsageUrl, null) : Page(UsageUrl, UsageText));
        Check(bounceLate.Attempts == 9 && GeminiUsagePage.Parse(bounceLate.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.Ok,
            "a late value after a pass through another host was not read.");
        // An unreadable answer in the middle of a move does not replace the last page seen.
        var midMove = await Run(attempt => attempt < 20 ? Page(signIn, null) : null);
        Check(midMove.Attempts == 21 && GeminiUsagePage.Parse(midMove.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.LoginRequired,
            "an unreadable last answer replaced the last page seen.");

        // No answer from the page (or an unreadable one): retried for the same 5 s, then a failed load.
        foreach (string? answer in new string?[] { null, "not json", "{\"text\":\"x\"}", "{\"url\":5}" })
        {
            var failed = await Run(_ => answer);
            Check(failed.Attempts == 21 && GeminiUsagePage.Parse(failed.Load, now, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.LoadFailed,
                "an unreadable answer was not a failed load.");
        }

        // Cancellation is honoured between reads.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool threw = false;
        try { await Run(_ => Page(UsageUrl, null), cancelled.Token); }
        catch (OperationCanceledException) { threw = true; }
        Check(threw, "a cancelled read did not stop.");

        return "page reading (ready at once; late element retried every 250 ms; missing element 20 retries = 5 s then format changed; another page is waited out for 5 s (host+path only, then sign-in needed) and a return to the usage page is read; " +
               "unreadable answer = failed load; cancellation; 30 s total limit)";
    }

    private sealed class FakeBackend : IMacGeminiWebBackend
    {
        internal readonly List<string> Calls = [];
        internal Action? Succeeded;
        internal bool RemoveResult = true;
        internal bool ThrowOnRemove;
        public bool LoginOpen { get; set; }
        public void SetEnabled(bool enabled) => Calls.Add("enabled:" + enabled);
        public void SetAntigravity(bool active, bool afterSignIn = false) { } // checked by MacAntigravitySmoke
        public Task DeleteCacheAsync() { Calls.Add("delete-cache"); return Task.CompletedTask; }
        public void ShowLogin(Action succeeded) { Calls.Add("show-login"); LoginOpen = true; Succeeded = succeeded; }
        public void CloseLogin() { if (LoginOpen) Calls.Add("close-login"); LoginOpen = false; }
        public Task WaitReadsAsync() { Calls.Add("wait-reads"); return Task.CompletedTask; }
        public Task<bool> RemoveDataStoreAsync()
        {
            Calls.Add("remove-store");
            if (ThrowOnRemove) throw new InvalidOperationException("synthetic");
            return Task.FromResult(RemoveResult);
        }
    }

    internal static async Task<string> VerifyControllerAsync(NSMenu contextMenu, MacGeminiWebController appController)
    {
        // The app's own menu: the submenu is in the right-click menu with its four items, and a fresh run starts off.
        NSMenuItem? item = contextMenu.Items.FirstOrDefault(entry => entry.Title == GeminiWebText.MenuTitle);
        Check(item?.Submenu is { } appSubmenu && appSubmenu.Items.Select(entry => entry.Title).SequenceEqual(["로그인…", "끄기", "로그아웃", AntigravityWebText.MenuLabel]),
            "the right-click menu has no 'Gemini 사용량' submenu with 로그인… / 끄기 / 로그아웃 / Antigravity.");
        Check(!appController.Enabled && !appController.HasSession && appController.Items == (true, false, true),
            "a fresh run is not off with sign-in and logout usable.");

        // Which items can be used in each state.
        // 로그아웃 is usable in every state except while a logout is running.
        (bool Enabled, bool LoginOpen, bool Busy, (bool, bool, bool) Expected)[] states =
        [
            (false, false, false, (true, false, true)),
            (false, true, false, (false, false, true)),
            (true, false, false, (true, true, true)),
            (true, true, false, (false, true, true)),
            (true, false, true, (false, false, false)),
            (false, false, true, (false, false, false))
        ];
        foreach (var state in states)
            Check(MacGeminiWebController.ItemsFor(state.Enabled, state.LoginOpen, state.Busy) == state.Expected,
                $"menu items for enabled={state.Enabled} login-open={state.LoginOpen} busy={state.Busy}.");

        string directory = Path.Combine(Path.GetTempPath(), "gemini-web-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Settings: missing = off; a file that says "on" without a session cannot switch it on; round trip; unreadable = off.
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: false }, "missing settings are not off.");
            File.WriteAllText(Path.Combine(directory, MacGeminiWebSettings.FileName), "enabled=1\n");
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: false }, "'on' without a session switched the reader on.");
            File.WriteAllText(Path.Combine(directory, MacGeminiWebSettings.FileName), "enabled=1\nsession=1\n");
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: true, HasSession: true }, "saved 'on' was not read back.");
            File.WriteAllText(Path.Combine(directory, MacGeminiWebSettings.FileName), "\u0000garbage");
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: false }, "garbage settings are not off.");
            File.Delete(Path.Combine(directory, MacGeminiWebSettings.FileName));

            var settings = MacGeminiWebSettings.Load(directory);
            var backend = new FakeBackend();
            var feedback = new List<(string Text, bool Error)>();
            var controller = new MacGeminiWebController(settings, backend, (text, error) => feedback.Add((text, error)));
            Check(controller.MenuItem.Title == "Gemini 사용량" && controller.MenuItem.Submenu!.Items.Length == 4, "the controller's submenu is wrong.");

            // 끄기 does nothing while off.
            controller.Off();
            Check(backend.Calls.Count == 0, "'끄기' acted while off.");

            // 로그인…: the window opens, nothing is switched on, the sign-in item waits.
            controller.Login();
            Check(backend.Calls.SequenceEqual(["show-login"]) && !settings.Enabled && controller.Items == (false, false, true),
                "opening the sign-in window switched something on or left the sign-in item usable.");
            // Closed without signing in: nothing changed, the sign-in item is usable again.
            backend.LoginOpen = false;
            controller.UpdateMenu();
            Check(!MacGeminiWebSettings.Load(directory).Enabled && controller.Items == (true, false, true) && feedback.Count == 0,
                "closing the sign-in window without signing in changed something.");
            // Signed in: switched on, saved, usable items follow.
            controller.Login();
            backend.LoginOpen = false;
            backend.Succeeded!();
            Check(backend.Calls.SequenceEqual(["show-login", "show-login", "enabled:True"]) && MacGeminiWebSettings.Load(directory) is { Enabled: true, HasSession: true } &&
                  controller.Items == (true, true, true) && feedback[^1] is ("Gemini 사용량 조회를 켰습니다.", false), "a completed sign-in did not switch the reader on and save it.");

            // 끄기: off, saved, the login stays (so 로그아웃 stays usable), no cache or store is touched.
            backend.Calls.Clear();
            controller.Off();
            Check(backend.Calls.SequenceEqual(["enabled:False"]) && MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: true } &&
                  controller.Items == (true, false, true), "'끄기' did not only switch the reader off.");

            // The switch survives a restart: on again, then a new settings load (start-up) sees it on.
            controller.Login();
            backend.LoginOpen = false;
            backend.Succeeded!();
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: true, HasSession: true }, "the 'on' setting did not survive a reload.");

            // 로그아웃: off first, then the cache, then the web views, then the data store; the session flag clears only after.
            backend.Calls.Clear();
            backend.LoginOpen = true; // a sign-in window is open: it is closed too
            await controller.LogoutAsync();
            Check(backend.Calls.SequenceEqual(["enabled:False", "close-login", "delete-cache", "wait-reads", "remove-store"]) &&
                  MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: false } && controller.Items == (true, false, true) &&
                  feedback[^1] is { Error: false } && feedback[^1].Text.Contains("삭제했습니다", StringComparison.Ordinal),
                "logout did not run off -> cache -> web views -> data store, or left a session behind.");

            // A data store that cannot be confirmed gone keeps the session flag, so 로그아웃 can be run again.
            controller.Login();
            backend.LoginOpen = false;
            backend.Succeeded!();
            backend.RemoveResult = false;
            backend.Calls.Clear();
            await controller.LogoutAsync();
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: true } && controller.Items == (true, false, true) && feedback[^1].Error,
                "a logout that could not remove the data store dropped the session flag or hid the retry.");
            backend.RemoveResult = true;
            backend.ThrowOnRemove = true;
            await controller.LogoutAsync();
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: true } && controller.Items == (true, false, true) && feedback[^1].Error,
                "a logout that threw left the menu unusable or dropped the session flag.");
            backend.ThrowOnRemove = false;
            await controller.LogoutAsync();
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: false } && controller.Items == (true, false, true) && !feedback[^1].Error,
                "the repeated logout did not finish.");

            // 로그아웃 also runs with nothing recorded (a session may sit in the data store without a confirmed login).
            backend.Calls.Clear();
            await controller.LogoutAsync();
            Check(backend.Calls.SequenceEqual(["enabled:False", "delete-cache", "wait-reads", "remove-store"]) &&
                  MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: false } && controller.Items == (true, false, true),
                "logout did not run its cleanup while off with no session recorded.");

            // Shutdown closes an open sign-in window.
            controller.Login();
            controller.Shutdown();
            Check(!backend.LoginOpen, "shutdown left the sign-in window open.");
        }
        finally { Directory.Delete(directory, recursive: true); }

        return "Gemini menu (right-click 'Gemini 사용량' with 로그인… / 끄기 / 로그아웃 and the Antigravity item; 6 usable-item states (로그아웃 always usable unless busy); settings missing/garbage/on-without-session = off, round trip; " +
               "sign-in window closed without login = no change; login = on + saved; 끄기 keeps login; logout = off -> cache -> web views -> data store, failure and exception keep retry, repeat finishes; shutdown closes sign-in)";
    }

    private sealed class NoClient : IAccountQuotaClient
    {
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic: no query is expected");
    }

    internal static async Task<string> VerifyBackendAsync(CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gemini-web-backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new UsageStore(Path.Combine(directory, "burgerclock.db"));
            var cache = QuotaCacheAdapter.ForGeminiWeb(store);
            // The monitors are never started, so no read runs. The reader's main-thread dispatch throws, so it cannot create
            // a web view either way; the login window is never shown.
            var reader = new MacGeminiUsageReader(_ => throw new InvalidOperationException("synthetic: no read is expected"));
            var antigravity = new MacAntigravityUsageReader(_ => throw new InvalidOperationException("synthetic: no read is expected"));
            var login = new MacGeminiLoginWindow(_ => { });
            DateTimeOffset utc = DateTimeOffset.UtcNow;
            QuotaReading reading = new(QuotaProvider.Gemini,
            [
                new(GeminiUsagePage.FiveHourId, "5시간", 40, utc.AddHours(2), 300),
                new(GeminiUsagePage.WeeklyId, "주간", 12, utc.AddDays(5), 10080)
            ]);
            Task Save() => cache.WriteAsync(reading, utc, [], cancellationToken);
            async Task<bool> Saved() => await cache.ReadAsync(cancellationToken) is not null;
            static QuotaSourceMode Mode(AccountQuotaMonitor monitor) =>
                monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Gemini).Mode;

            using (var monitor = new AccountQuotaMonitor(new NoClient(), store, geminiWebReader: reader, geminiWebEnabled: true))
            {
                var backend = new MacGeminiWebBackend(() => monitor, store, reader, antigravity, login, cancellationToken);
                await Save();
                Check(Mode(monitor) == QuotaSourceMode.Active && await Saved(), "the backend fixture did not start on with a saved reading.");
                backend.SetEnabled(false);
                Check(Mode(monitor) == QuotaSourceMode.Off && await Saved(), "'off' did not switch the source off and keep the reading.");
                await backend.DeleteCacheAsync();
                Check(!await Saved(), "logout's delete left the saved reading with a running monitor.");
                backend.SetEnabled(true);
                Check(Mode(monitor) == QuotaSourceMode.Active, "sign-in did not switch the source on.");
            }

            // Started with the setting off: logout switches it off again first, which is what allows the delete.
            using (var monitor = new AccountQuotaMonitor(new NoClient(), store, geminiWebReader: reader, geminiWebEnabled: false))
            {
                var backend = new MacGeminiWebBackend(() => monitor, store, reader, antigravity, login, cancellationToken);
                await Save();
                backend.SetEnabled(false);
                await backend.DeleteCacheAsync();
                Check(Mode(monitor) == QuotaSourceMode.Off && !await Saved(), "logout's delete failed for a monitor that started off.");
            }

            // No monitor yet (start-up not finished): the saved reading is still deleted.
            var early = new MacGeminiWebBackend(() => null, store, reader, antigravity, login, cancellationToken);
            await Save();
            early.SetEnabled(false);
            await early.DeleteCacheAsync();
            Check(!await Saved(), "logout's delete left the saved reading without a monitor.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        }
        return "Gemini backend (real monitor and temporary DB: off keeps the saved reading, sign-in switches on, logout's delete works with a monitor that started on or off and without one)";
    }

    internal static string VerifyPanel(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        QuotaState codex, QuotaState claude, QuotaState xai, DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        QuotaReading reading = new(QuotaProvider.Gemini,
        [
            new(GeminiUsagePage.FiveHourId, "5시간", 40, utc.AddHours(2), 300),
            new(GeminiUsagePage.WeeklyId, "주간", 12, utc.AddDays(5), 10080)
        ]);
        QuotaState Web(QuotaFailure? failure = null, QuotaReading? shown = null, bool previous = false) =>
            new(QuotaProvider.Gemini, shown, CheckedAtUtc: utc.AddMinutes(-2), LastSuccessfulCheckUtc: shown is null ? null : utc.AddMinutes(-2),
                NextCheckUtc: utc.AddMinutes(13), IsPrevious: previous, ContinuityId: 1, Failure: failure,
                Presentation: QuotaPresentationPolicy.GeminiWeb, HasResultSinceModeChange: true);

        static QuotaFailure Parsed(GeminiUsageOutcome outcome, GeminiUsageFailureRule rule) =>
            GeminiUsagePage.ClassifyFailure(outcome, new(rule))!;

        static string Failed(QuotaFailureKind kind) => QuotaPanelModel.FailureMessage(QuotaProvider.Gemini, kind);

        (string Name, QuotaState State, string? Row, int Bars)[] scenes =
        [
            ("off", new QuotaState(QuotaProvider.Gemini, ContinuityId: 1, Mode: QuotaSourceMode.Off, Presentation: QuotaPresentationPolicy.GeminiWeb),
                "꺼짐 · 로그인하면 Gemini 앱 사용량 조회", 0),
            ("sign-in needed", Web(Parsed(GeminiUsageOutcome.LoginRequired, GeminiUsageFailureRule.Destination)), Failed(QuotaFailureKind.LoginRequired), 0),
            ("normal", Web(shown: reading), null, 2),
            ("format changed", Web(Parsed(GeminiUsageOutcome.FormatChanged, GeminiUsageFailureRule.MissingSection), reading, previous: true), Failed(QuotaFailureKind.FormatChanged), 0),
            ("load failed, no earlier reading", Web(Parsed(GeminiUsageOutcome.LoadFailed, GeminiUsageFailureRule.PageLoad)), Failed(QuotaFailureKind.Unreachable), 0),
            ("load failed, earlier reading kept", Web(Parsed(GeminiUsageOutcome.LoadFailed, GeminiUsageFailureRule.PageLoad), reading, previous: true), null, 2)
        ];

        MacStatusPanel.Layout Show(params QuotaState[] states)
        {
            panel.Update(schedule, providers, states, false, now.AddMinutes(5), "");
            return panel.Verify(schedule, providers, states);
        }

        MacStatusPanel.Layout without = Show(codex, claude, xai);
        int baselineBars = panel.QuotaBarCount;
        var details = new List<string>();
        double largestTip = 0;
        foreach (var (name, gemini, expectedRow, expectedBars) in scenes)
        {
            MacStatusPanel.Layout layout = Show(codex, claude, gemini, xai);
            string where = name + ": ";
            QuotaSectionText section = QuotaPanelModel.Sections([codex, claude, gemini, xai], now).Single(item => item.Provider == QuotaProvider.Gemini);
            // Every row is the shared text (Verify checks it too) and the bars are exactly the rows that carry a value.
            Check(panel.QuotaRowIds(QuotaProvider.Gemini).Order(StringComparer.Ordinal).SequenceEqual(section.Rows.Select(row => row.WindowId).Order(StringComparer.Ordinal)),
                where + "the Gemini box rows are not the shared rows.");
            Check(panel.QuotaBarCount == baselineBars + expectedBars, where + $"{panel.QuotaBarCount - baselineBars} Gemini bars, expected {expectedBars}.");
            if (expectedRow is not null)
            {
                Check(section.Rows.Count == 1 && panel.QuotaRowText(QuotaProvider.Gemini, section.Rows[0].WindowId) == expectedRow,
                    where + $"the row is '{panel.QuotaRowText(QuotaProvider.Gemini, section.Rows[0].WindowId)}', expected '{expectedRow}'.");
                Check(panel.QuotaRowTextFits(QuotaProvider.Gemini, section.Rows[0].WindowId), where + "the notice is cut off instead of shown whole.");
                // Only a sign-in that is needed is a warning; the other notices stay muted.
                PanelTone tone = name == "sign-in needed" ? PanelTone.Danger : PanelTone.Muted;
                Check(section.Rows[0].Tone == tone, where + $"the notice tone is {section.Rows[0].Tone}, expected {tone}.");
            }
            else
            {
                // Two windows: the shared 5-hour and weekly rows, each with a bar and the time layer data.
                Check(section.Rows.Count == 2 && section.Rows.All(row => row.UsedPercent is not null) &&
                      panel.QuotaRowText(QuotaProvider.Gemini, GeminiUsagePage.FiveHourId).StartsWith("5시간", StringComparison.Ordinal) &&
                      panel.QuotaRowText(QuotaProvider.Gemini, GeminiUsagePage.WeeklyId).StartsWith("주간", StringComparison.Ordinal) &&
                      panel.QuotaBarFill(QuotaProvider.Gemini, GeminiUsagePage.FiveHourId) > panel.QuotaBarFill(QuotaProvider.Gemini, GeminiUsagePage.WeeklyId),
                    where + "the two windows are not shown as bars in proportion.");
            }
            // One Provider detail per box, the shared text (Windows wording), fitted whole into the fixed-width tip.
            string detail = panel.QuotaDetail(QuotaProvider.Gemini);
            Check(detail == section.Detail && detail.Contains("Gemini 한도", StringComparison.Ordinal), where + "the box carries a text other than the shared detail.");
            Check(!detail.Contains("Antigravity 약관상", StringComparison.Ordinal), where + "a web state shows the Antigravity stop text.");
            if (name == "off") Check(detail.Contains("꺼짐", StringComparison.Ordinal), where + "the tip does not say the reader is off.");
            if (name == "normal")
                Check(detail.Contains("gemini.google.com/usage", StringComparison.Ordinal) && detail.Contains($"자동 {GeminiUsagePage.PollMinutes}분", StringComparison.Ordinal),
                    where + "the tip lacks the source page or the polling interval.");
            MacDetailTip.Metrics tip = MacDetailTip.Measure(detail);
            Check(Math.Abs((double)tip.Size.Width - MacDetailTip.Width) < 0.5 && tip.PlacedCharacters == tip.Characters,
                where + $"the tip does not lay out whole at {MacDetailTip.Width}pt (placed {tip.PlacedCharacters} of {tip.Characters}).");
            largestTip = Math.Max(largestTip, (double)tip.Size.Height);
            Check(panel.QuotaAreaHeight <= 340.5 && layout.Size.Height <= layout.UsableHeight, where + "the popover or quota area is too tall.");
            details.Add(name);
        }

        // Normal state on the two usual screens: the whole popover fits, the boxes stay reachable.
        Show(codex, claude, scenes[2].State, xai);
        double[] screens = [800, 640];
        double[] fitted = new double[screens.Length];
        for (int index = 0; index < screens.Length; index++)
        {
            double usable = screens[index] - (25 + 24);
            CGSize size = panel.FitFor((nfloat)usable);
            fitted[index] = size.Height;
            Check(size.Height <= usable + 0.5 && panel.QuotaBoxScrollsIntoView(QuotaProvider.Gemini) && panel.QuotaBoxScrollsIntoView(QuotaProvider.Xai),
                $"popover {size.Height:0}pt is cut off or a box is out of reach on a {screens[index]}pt screen.");
        }
        panel.FitFor(without.UsableHeight);
        Show(codex, claude, scenes[0].State, xai);

        return $"Gemini web states ({string.Join(" / ", details)}: shared rows and tip, notices whole on one line, 2 bars only for normal and for an earlier reading kept after a failed load, " +
               $"tip fits {MacDetailTip.Width}pt whole (tallest {largestTip:0}pt), other boxes unchanged, 1280x800 {fitted[0]:0}pt and 1024x640 {fitted[1]:0}pt not cut off)";
    }
}
