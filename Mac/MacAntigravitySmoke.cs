using AppKit;
using CoreGraphics;
using Foundation;

namespace AiBurgerClock;

// Native smoke for the Antigravity usage reader and its Mac UI. Synthetic values only: every page text, percentage and
// time below is made up and has nothing to do with a real account. No web view is created and no network is used; the
// actual page read (visibility of the transparent window, displays, lock) is checked separately by hand.
//   1. the page-reading loop driven with scripted answers,
//   2. the screen decision (locked / asleep / covered / unknown), the display functions and the read window's settings,
//   3. the Antigravity menu item, its setting and the logout order (recording fake backend),
//   4. the real backend with a monitor that is never started and a temporary database: source modes and both caches,
//   5. the two-part Gemini box, its states, and the unchanged one-part box while Antigravity is off.
internal static class MacAntigravitySmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Antigravity: " + message);
    }

    private const string PageUrl = "https://antigravity.google.com/remote";
    private const string SignInUrl = "https://accounts.google.com/v3/signin/identifier?continue=synthetic-token#fragment";
    private const string PageText = "Gemini Models\nWeekly Limit Remaining\nYou have used some of your weekly limit, it will fully refresh in 4 days, 3 hours.\n" +
        "45%\nFive Hour Limit Remaining\nYou have used some of your 5-hour limit, it will fully refresh in 2 hours, 10 minutes.\n70%";

    private static string Answer(string url, string? text, string visibility = "visible")
    {
        static string Quote(string? value) => value is null ? "null"
            : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        return $"{{\"url\":{Quote(url)},\"text\":{Quote(text)},\"visibilityState\":{Quote(visibility)},\"documentLanguage\":\"en\"}}";
    }

    internal static async Task<string> VerifyReadingAsync()
    {
        Check(MacAntigravityPageReading.RetryMilliseconds == 250 && MacAntigravityPageReading.RetryWindowSeconds == 20 &&
              MacAntigravityPageReading.TotalTimeoutSeconds == 30, "the retry interval, wait window or total limit is not 250 ms / 20 s / 30 s.");
        var now = new DateTimeOffset(2026, 11, 28, 3, 0, 0, TimeSpan.Zero);

        // The wait window ends after 'waits' delays (the real one ends after 20 s).
        async Task<(AntigravityUsagePageLoad Load, int Reads, List<TimeSpan> Delays)> Run(Func<int, string> source,
            Func<int, string?> answer, int waits = 10)
        {
            int turn = 0, reads = 0;
            var delays = new List<TimeSpan>();
            using var window = new CancellationTokenSource();
            var load = await MacAntigravityPageReading.ReadAsync(() => source(turn), _ => { reads++; return Task.FromResult(answer(turn)); },
                (span, token) =>
                {
                    delays.Add(span);
                    turn++;
                    if (delays.Count >= waits) window.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }, window.Token, CancellationToken.None);
            return (load, reads, delays);
        }
        static string? Rule(AntigravityUsagePageLoad load, DateTimeOffset at) => AntigravityUsagePage.Parse(load, at).Failure?.Code;
        const string Query = PageUrl + "?settingsOpen=true&settingsScreen=Models";

        // Ready on the first try: one read, no wait; the query never leaves the web view and the shared parser accepts it.
        var ready = await Run(_ => Query, _ => Answer(Query, PageText));
        var parsed = AntigravityUsagePage.Parse(ready.Load, now);
        Check(ready.Reads == 1 && ready.Delays.Count == 0 && ready.Load.FinalUri?.AbsoluteUri == PageUrl &&
              parsed.Reading is { } reading && reading.Windows.Select(item => item.UsedPercent).SequenceEqual([30, 55]),
              "a page that is ready was not read once, cut to its path and accepted.");
        // The area appears late: retried every 250 ms until it is there.
        var late = await Run(_ => Query, turn => Answer(Query, turn < 3 ? null : PageText));
        Check(late.Reads == 4 && late.Delays.Count == 3 && late.Delays.All(span => span == TimeSpan.FromMilliseconds(250)) &&
              AntigravityUsagePage.Parse(late.Load, now).Reading is not null, "a late reading area was not retried every 250 ms.");
        // A hidden page is not accepted although the text is there; it is waited out and reported as not visible.
        var hidden = await Run(_ => Query, _ => Answer(Query, PageText, "hidden"));
        Check(hidden.Reads == 10 && Rule(hidden.Load, now) == "antigravity.not-visible", "a hidden page was accepted or misreported.");
        var empty = await Run(_ => Query, _ => Answer(Query, null));
        Check(empty.Reads == 10 && Rule(empty.Load, now) == "antigravity.not-rendered", "a missing reading area was misreported.");
        // The sign-in host: no script runs there, only scheme, host and path remain, and it is "sign-in needed".
        var signIn = await Run(_ => SignInUrl, _ => Answer(Query, PageText));
        Check(signIn.Reads == 0 && signIn.Load.FinalUri?.AbsoluteUri == "https://accounts.google.com/v3/signin/identifier" &&
              AntigravityUsagePage.Parse(signIn.Load, now).Failure?.Kind == QuotaFailureKind.LoginRequired,
              "the sign-in host was scripted, kept its query or was not reported as sign-in needed.");
        var bounce = await Run(turn => turn < 4 ? SignInUrl : Query, _ => Answer(Query, PageText));
        Check(bounce.Reads == 1 && bounce.Delays.Count == 4 && AntigravityUsagePage.Parse(bounce.Load, now).Reading is not null,
              "a pass through the sign-in host and back was not read.");
        // An answer from another address than the one shown (a move in between) is not used.
        var moved = await Run(_ => Query, turn => Answer(turn < 2 ? "https://antigravity.google.com/elsewhere" : Query, PageText));
        Check(moved.Reads == 3 && AntigravityUsagePage.Parse(moved.Load, now).Reading is not null, "an answer from another address was used.");
        Check(MacAntigravityPageReading.Interpret(null) is null && MacAntigravityPageReading.Interpret("not json") is null &&
              MacAntigravityPageReading.Interpret("{\"url\":5}") is null && MacAntigravityPageReading.Interpret("[]") is null,
              "an unreadable answer was accepted.");
        // The caller's cancellation (or the 30 s limit) is passed on, not turned into a result.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool threw = false;
        try { await MacAntigravityPageReading.ReadAsync(() => Query, _ => Task.FromResult<string?>(null), Task.Delay, cancelled.Token, cancelled.Token); }
        catch (OperationCanceledException) { threw = true; }
        Check(threw, "a cancelled read did not stop.");
        return "Antigravity page reading (ready at once, query cut; late area retried every 250 ms; hidden / missing area waited out; sign-in host not scripted and sign-in needed; " +
               "move in between ignored; cancellation; 20 s wait / 30 s total)";
    }

    internal static string VerifyScreen()
    {
        MacScreenCoverage.Display free = new(false, false), asleep = new(true, false), covered = new(false, true);
        (bool Locked, MacScreenCoverage.Display[]? Displays, QuotaReadiness Expected)[] cases =
        [
            (false, [free], QuotaReadiness.Ready), (true, [free], QuotaReadiness.Deferred), (true, null, QuotaReadiness.Deferred),
            (false, [asleep], QuotaReadiness.Deferred), (false, [covered], QuotaReadiness.Deferred),
            (false, [asleep, covered], QuotaReadiness.Deferred), (false, [covered, free], QuotaReadiness.Ready),
            (false, [asleep, free], QuotaReadiness.Ready), (false, null, QuotaReadiness.Ready), (false, [], QuotaReadiness.Ready)
        ];
        foreach (var item in cases)
            Check(MacScreenCoverage.Decide(item.Locked, item.Displays) == item.Expected,
                $"locked={item.Locked} displays=[{string.Join(", ", item.Displays ?? [])}] was not {item.Expected}.");
        // The real functions answer on this machine, and the CoreGraphics display rectangles match the AppKit screens.
        QuotaReadiness now = MacScreenCoverage.Readiness();
        NSScreen[] screens = NSScreen.Screens;
        CGRect[] rects = screens.Length == 0 ? [] : MacScreenCoverage.DisplayRects(screens);
        for (int index = 0; index < screens.Length; index++)
        {
            Check(screens[index].DeviceDescription["NSScreenNumber"] is NSNumber, "a screen has no display number.");
            CGRect bounds = MacScreenCoverage.Bounds(((NSNumber)screens[index].DeviceDescription["NSScreenNumber"]!).UInt32Value);
            Check(Math.Abs((double)(bounds.X - rects[index].X)) < 1 && Math.Abs((double)(bounds.Y - rects[index].Y)) < 1 &&
                  Math.Abs((double)(bounds.Width - rects[index].Width)) < 1 && Math.Abs((double)(bounds.Height - rects[index].Height)) < 1,
                $"display {index + 1}: CoreGraphics bounds differ from the AppKit screen rectangle.");
        }
        // The read window, created but never ordered in.
        var area = new CGRect(100, 50, 2000, 1200);
        NSWindow window = MacAntigravityUsageReader.CreateWindow(area);
        try
        {
            Check(window.AlphaValue == 0 && window.Level == NSWindowLevel.Status && window.IgnoresMouseEvents && !window.HasShadow &&
                  window.StyleMask == NSWindowStyle.Borderless && !window.CanBecomeKeyWindow && !window.IsVisible &&
                  window.CollectionBehavior == (NSWindowCollectionBehavior.CanJoinAllSpaces | NSWindowCollectionBehavior.Transient |
                      NSWindowCollectionBehavior.IgnoresCycle),
                "the read window is not transparent, click-through, status-level, in all Spaces without fullScreenAuxiliary.");
            Check(window.Frame.Equals(new CGRect(550, 250, 1100, 800)), $"the read window is not centred inside the display area ({window.Frame}).");
        }
        finally { window.Close(); window.Dispose(); }
        using (NSWindow small = MacAntigravityUsageReader.CreateWindow(new CGRect(0, 0, 900, 600)))
        {
            Check(small.Frame.Equals(new CGRect(0, 0, 900, 600)), "the read window spills over a small display.");
            small.Close();
        }
        return $"screen decision ({cases.Length} locked/asleep/covered/unknown cases; display rectangles match on {screens.Length} display(s), current readiness {now}; " +
               "read window alpha 0, status level, click-through, no fullScreenAuxiliary, inside the display)";
    }

    private sealed class FakeBackend : IMacGeminiWebBackend
    {
        internal readonly List<string> Calls = [];
        internal Action? Succeeded;
        internal bool RemoveResult = true;
        public bool LoginOpen { get; set; }
        public void SetEnabled(bool enabled) => Calls.Add("gemini:" + enabled);
        public void SetAntigravity(bool active, bool afterSignIn = false) => Calls.Add("antigravity:" + active + (afterSignIn ? ":sign-in" : ""));
        public Task DeleteCacheAsync() { Calls.Add("delete-cache"); return Task.CompletedTask; }
        public void ShowLogin(Action succeeded) { Calls.Add("show-login"); LoginOpen = true; Succeeded = succeeded; }
        public void CloseLogin() => LoginOpen = false;
        public Task WaitReadsAsync() { Calls.Add("wait-reads"); return Task.CompletedTask; }
        public Task<bool> RemoveDataStoreAsync() { Calls.Add("remove-store"); return Task.FromResult(RemoveResult); }
    }

    internal static async Task<string> VerifyMenuAsync(MacGeminiWebController appController)
    {
        Check(appController.AntigravityItem is (false, false, AntigravityWebText.EnableGemini) && !appController.AntigravityEnabled,
            "a fresh run does not show the Antigravity item unchecked, disabled and with the enable-Gemini text.");
        string directory = Path.Combine(Path.GetTempPath(), "antigravity-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, MacGeminiWebSettings.FileName);
            // A 0.10.0 settings file has no Antigravity line: off.
            File.WriteAllText(path, "enabled=1\nsession=1\n");
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: true, AntigravityEnabled: false, AntigravityActive: false },
                "a file without the Antigravity line did not leave it off.");
            File.WriteAllText(path, "antigravity=1\n");
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: false, AntigravityEnabled: true, AntigravityActive: false },
                "the saved Antigravity choice was active without the Gemini reader.");
            File.Delete(path);

            var settings = MacGeminiWebSettings.Load(directory);
            var backend = new FakeBackend();
            var feedback = new List<(string Text, bool Error)>();
            var controller = new MacGeminiWebController(settings, backend, (text, error) => feedback.Add((text, error)));
            string[] titles = controller.MenuItem.Submenu!.Items.Select(item => item.Title).ToArray();
            Check(titles.SequenceEqual([GeminiWebText.LoginLabel, GeminiWebText.OffLabel, GeminiWebText.LogoutLabel, AntigravityWebText.MenuLabel]),
                "the submenu items are wrong.");
            controller.ToggleAntigravity();
            Check(backend.Calls.Count == 0 && !settings.AntigravityEnabled && controller.AntigravityItem is (false, false, AntigravityWebText.EnableGemini),
                "the Antigravity item acted while the Gemini reader was off.");

            void SignIn() { controller.Login(); backend.LoginOpen = false; backend.Succeeded!(); }
            SignIn();
            Check(backend.Calls.SequenceEqual(["show-login", "gemini:True", "antigravity:False:sign-in"]) &&
                  controller.AntigravityItem is (true, false, ""), "the first sign-in switched Antigravity on by itself or left its item unusable.");
            backend.Calls.Clear();
            controller.ToggleAntigravity();
            Check(backend.Calls.SequenceEqual(["antigravity:True"]) && controller.AntigravityItem is (true, true, "") &&
                  MacGeminiWebSettings.Load(directory) is { AntigravityActive: true } && feedback.Count == 1, "checking the item did not switch Antigravity on and save it.");
            // A later sign-in asks the source that is on to read again.
            backend.Calls.Clear();
            SignIn();
            Check(backend.Calls.SequenceEqual(["show-login", "gemini:True", "antigravity:True:sign-in"]), "a sign-in did not wake Antigravity after Gemini.");
            // 끄기: Antigravity off with the Gemini reader, the choice kept and shown on the disabled item.
            backend.Calls.Clear();
            controller.Off();
            Check(backend.Calls.SequenceEqual(["antigravity:False", "gemini:False"]) &&
                  controller.AntigravityItem is (false, true, AntigravityWebText.EnableGemini) &&
                  MacGeminiWebSettings.Load(directory) is { Enabled: false, AntigravityEnabled: true }, "'끄기' did not switch both off and keep the choice.");
            backend.Calls.Clear();
            SignIn();
            Check(backend.Calls.SequenceEqual(["show-login", "gemini:True", "antigravity:True:sign-in"]), "signing in again did not restore the kept choice.");
            controller.ToggleAntigravity();
            Check(backend.Calls[^1] == "antigravity:False" && controller.AntigravityItem is (true, false, "") &&
                  MacGeminiWebSettings.Load(directory) is { Enabled: true, AntigravityEnabled: false }, "unchecking did not switch only Antigravity off.");
            controller.ToggleAntigravity();

            // 로그아웃: choice cleared, both off, both caches, both readers, then the data store; never switched on again by itself.
            backend.Calls.Clear();
            await controller.LogoutAsync();
            Check(backend.Calls.SequenceEqual(["antigravity:False", "gemini:False", "delete-cache", "wait-reads", "remove-store"]) &&
                  MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: false, AntigravityEnabled: false } &&
                  controller.AntigravityItem is (false, false, AntigravityWebText.EnableGemini), "logout order or the cleared choice is wrong.");
            backend.Calls.Clear();
            SignIn();
            Check(backend.Calls[^1] == "antigravity:False:sign-in" && !settings.AntigravityEnabled, "signing in after logout switched Antigravity on.");
            // A logout that cannot remove the data store still leaves the choice cleared.
            controller.ToggleAntigravity();
            backend.RemoveResult = false;
            await controller.LogoutAsync();
            Check(MacGeminiWebSettings.Load(directory) is { Enabled: false, HasSession: true, AntigravityEnabled: false } &&
                  controller.AntigravityItem is (false, false, AntigravityWebText.EnableGemini), "a failed logout kept the Antigravity choice.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        return "Antigravity menu (4th item, default off; disabled with the enable-Gemini text while Gemini is off; separate saved choice; Gemini off keeps it; sign-in wakes it after Gemini; " +
               "logout clears it first, failure included)";
    }

    private sealed class NoClient : IAccountQuotaClient
    {
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic: no query is expected");
    }

    internal static async Task<string> VerifyBackendAsync(CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "antigravity-backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new UsageStore(Path.Combine(directory, "burgerclock.db"));
            var geminiCache = QuotaCacheAdapter.ForGeminiWeb(store);
            var antigravityCache = QuotaCacheAdapter.ForAntigravityWeb(store);
            // The monitors are never started, so no read runs. Both readers' main-thread dispatch throws, so neither can
            // create a window or a web view; the login window is never shown.
            static void NoRead(Action _) => throw new InvalidOperationException("synthetic: no read is expected");
            var gemini = new MacGeminiUsageReader(NoRead);
            var antigravity = new MacAntigravityUsageReader(NoRead);
            var login = new MacGeminiLoginWindow(_ => { });
            DateTimeOffset utc = DateTimeOffset.UtcNow;
            async Task Save()
            {
                await geminiCache.WriteAsync(GeminiReading(utc), utc, [], cancellationToken);
                await antigravityCache.WriteAsync(Reading(utc), utc, [], cancellationToken);
            }
            async Task<(bool Gemini, bool Antigravity)> Saved() => (await geminiCache.ReadAsync(cancellationToken) is not null,
                await antigravityCache.ReadAsync(cancellationToken) is not null);
            static QuotaSourceMode Mode(AccountQuotaMonitor monitor) =>
                monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Antigravity).Mode;
            AccountQuotaMonitor Create(bool geminiOn, bool antigravityOn) => new(AccountQuotaMonitor.CreateSources(new NoClient(), store,
                gemini, geminiOn, antigravity, antigravityOn, MacScreenCoverage.Readiness), store);
            MacGeminiWebBackend Backend(AccountQuotaMonitor? monitor) => new(() => monitor, store, gemini, antigravity, login, cancellationToken);

            foreach (var (geminiOn, antigravityOn, expected) in new[] { (true, true, QuotaSourceMode.Active), (true, false, QuotaSourceMode.Off),
                (false, true, QuotaSourceMode.Off), (false, false, QuotaSourceMode.Off) })
            {
                using var started = Create(geminiOn, antigravityOn);
                Check(Mode(started) == expected, $"start-up with Gemini {geminiOn} / Antigravity {antigravityOn} was not {expected}.");
            }

            using (var monitor = Create(geminiOn: true, antigravityOn: false))
            {
                var backend = Backend(monitor);
                int changes = 0;
                monitor.Changed += () => changes++;
                backend.SetAntigravity(true);
                Check(Mode(monitor) == QuotaSourceMode.Active && changes == 1, "switching on did not activate the source.");
                backend.SetAntigravity(true);
                backend.SetAntigravity(true, afterSignIn: true);
                Check(Mode(monitor) == QuotaSourceMode.Active && changes == 1, "a source that is on was reset instead of asked to read again.");
                await Save();
                backend.SetAntigravity(false);
                Check(Mode(monitor) == QuotaSourceMode.Off && await Saved() == (true, true), "switching off did not keep the saved reading.");
                backend.SetEnabled(false);
                await backend.DeleteCacheAsync();
                Check(await Saved() == (false, false), "logout's delete left a saved reading with a running monitor.");
                await backend.WaitReadsAsync();
            }
            // Started off (never switched): logout's "off" is still passed on, which is what allows the delete.
            using (var monitor = Create(geminiOn: false, antigravityOn: false))
            {
                var backend = Backend(monitor);
                await Save();
                backend.SetAntigravity(false);
                backend.SetEnabled(false);
                await backend.DeleteCacheAsync();
                Check(await Saved() == (false, false), "logout's delete failed for sources that started off.");
            }
            // Without any web reader neither source can be switched; the calls are ignored and logout still deletes both.
            using (var monitor = new AccountQuotaMonitor(new NoClient(), store))
            {
                var backend = Backend(monitor);
                await Save();
                backend.SetEnabled(true);
                backend.SetAntigravity(true, afterSignIn: true);
                backend.SetAntigravity(false);
                backend.SetEnabled(false);
                await backend.DeleteCacheAsync();
                Check(Mode(monitor) == QuotaSourceMode.Unavailable && await Saved() == (false, false), "logout's delete failed without web sources.");
            }
            // A monitor without an Antigravity source, and no monitor at all: both entries are still deleted.
            using (var monitor = new AccountQuotaMonitor(new NoClient(), store, geminiWebReader: gemini, geminiWebEnabled: true))
            {
                var backend = Backend(monitor);
                await Save();
                backend.SetAntigravity(true);
                backend.SetAntigravity(false);
                backend.SetEnabled(false);
                await backend.DeleteCacheAsync();
                Check(Mode(monitor) == QuotaSourceMode.Unavailable && await Saved() == (false, false), "logout's delete failed without an Antigravity source.");
            }
            var early = Backend(null);
            await Save();
            early.SetAntigravity(false);
            early.SetEnabled(false);
            await early.DeleteCacheAsync();
            Check(await Saved() == (false, false), "logout's delete left a saved reading without a monitor.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        }
        return "Antigravity backend (real monitor and temporary DB: start-up mode needs both settings, an active source is not reset, logout deletes both caches with, without and before a monitor)";
    }

    private static QuotaReading GeminiReading(DateTimeOffset utc) => new(QuotaProvider.Gemini,
    [
        new(GeminiUsagePage.FiveHourId, "5시간", 40, utc.AddHours(2), 300),
        new(GeminiUsagePage.WeeklyId, "주간", 12, utc.AddDays(5), 10080)
    ]);

    private static QuotaReading Reading(DateTimeOffset utc) => new(QuotaProvider.Antigravity,
    [
        new(AntigravityUsagePage.FiveHourId, "5시간", 30, utc.ToUniversalTime().AddHours(3), 300),
        new(AntigravityUsagePage.WeeklyId, "주간", 55, utc.ToUniversalTime().AddDays(4), 10080)
    ]);

    internal static string VerifyPanel(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        QuotaState codex, QuotaState claude, QuotaState xai, DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        QuotaState Web(QuotaProvider provider, QuotaReading? reading, QuotaFailure? failure = null) => new(provider, reading,
            CheckedAtUtc: utc.AddMinutes(-2), LastSuccessfulCheckUtc: reading is null ? null : utc.AddMinutes(-2), NextCheckUtc: utc.AddMinutes(13),
            ContinuityId: 1, Failure: failure, HasResultSinceModeChange: true,
            Presentation: provider == QuotaProvider.Gemini ? QuotaPresentationPolicy.GeminiWeb : QuotaPresentationPolicy.AntigravityWeb);
        QuotaFailure signIn = new(QuotaFailureKind.LoginRequired, "synthetic");
        QuotaState gemini = Web(QuotaProvider.Gemini, GeminiReading(utc));
        QuotaState antigravity = Web(QuotaProvider.Antigravity, Reading(utc));
        QuotaState Waiting(QuotaProvider provider) => new(provider, ContinuityId: 1,
            Presentation: provider == QuotaProvider.Gemini ? QuotaPresentationPolicy.GeminiWeb : QuotaPresentationPolicy.AntigravityWeb);
        QuotaState off = new(QuotaProvider.Antigravity, ContinuityId: 1, Mode: QuotaSourceMode.Off,
            Presentation: QuotaPresentationPolicy.AntigravityWeb);

        // Draws the states and compares the Gemini box's painted lines with the shared model, top to bottom.
        (MacStatusPanel.Layout Layout, QuotaSectionText Section, string[] Lines) Show(QuotaState first, QuotaState? second)
        {
            QuotaState[] states = second is null ? [codex, claude, first, xai] : [codex, claude, first, xai, second];
            panel.Update(schedule, providers, states, false, now.AddMinutes(5), "");
            var layout = panel.Verify(schedule, providers, states);
            QuotaSectionText section = QuotaPanelModel.Sections(states, now).Single(item => item.Provider == QuotaProvider.Gemini);
            var expected = new List<string> { section.Heading.Text };
            if (section.Scope is { } scope) expected.Add(scope.Text);
            foreach (QuotaSectionPart part in section.Parts ?? [new(section.Provider, section.Heading, section.Rows, section.Metadata)])
            {
                if (section.Parts is not null) expected.Add(part.Title.Text);
                foreach (QuotaLine row in part.Rows)
                {
                    expected.Add(row.Text);
                    if (row.UsedPercent is not null) expected.Add("(bar)");
                }
                if (part.Metadata.Text.Length > 0) expected.Add(part.Metadata.Text);
            }
            string[] lines = panel.QuotaBoxLines(QuotaProvider.Gemini);
            Check(lines.SequenceEqual(expected), $"the Gemini box lines are [{string.Join(" | ", lines)}], expected [{string.Join(" | ", expected)}].");
            Check(panel.QuotaDetail(QuotaProvider.Gemini) == section.Detail, "the Gemini box carries a text other than the shared detail.");
            MacDetailTip.Metrics tip = MacDetailTip.Measure(section.Detail);
            Check(Math.Abs((double)tip.Size.Width - MacDetailTip.Width) < 0.5 && tip.PlacedCharacters == tip.Characters,
                $"the tip does not lay out whole at {MacDetailTip.Width}pt.");
            return (layout, section, lines);
        }

        // Off (and a host without the source) is the one-part box as before: same structure key, lines and height.
        var without = Show(gemini, null);
        string key = QuotaPanelModel.LayoutKey(QuotaPanelModel.Sections([codex, claude, gemini, xai], now));
        nfloat height = panel.QuotaBoxHeight(QuotaProvider.Gemini);
        int bars = panel.QuotaBarCount;
        foreach (QuotaState state in new[] { off, off with { Mode = QuotaSourceMode.Unavailable } })
        {
            var single = Show(gemini, state);
            Check(single.Section.Parts is null && QuotaPanelModel.LayoutKey(QuotaPanelModel.Sections([codex, claude, gemini, xai, state], now)) == key &&
                  single.Lines.SequenceEqual(without.Lines) && panel.QuotaBoxHeight(QuotaProvider.Gemini) == height && panel.QuotaBarCount == bars,
                  $"Antigravity {state.Mode} changed the Gemini box.");
        }
        NSView oneBox = panel.QuotaBoxView(QuotaProvider.Gemini);

        // Both parts with values: a new box, two titles, four rows and bars. Every web row says "약 ... 후 리셋"; only the
        // Antigravity detail marks the reset time itself as an estimate ("약 yyyy-MM-dd HH:mm KST").
        var both = Show(gemini, antigravity);
        Check(!ReferenceEquals(oneBox, panel.QuotaBoxView(QuotaProvider.Gemini)) && panel.QuotaBoxCount == 4 && panel.QuotaBarCount == bars + 2 &&
              panel.QuotaBoxHeight(QuotaProvider.Gemini) > height, "switching Antigravity on did not rebuild the box with two more bars.");
        var parts = both.Section.Parts!;
        Check(both.Section.Heading.Text == "Gemini" && parts[0].Title.Text.StartsWith("Gemini 앱", StringComparison.Ordinal) &&
              parts[1].Title.Text.StartsWith("Antigravity", StringComparison.Ordinal) &&
              parts[1].Rows.All(row => row.Text.Contains("약 ", StringComparison.Ordinal) && row.Detail.Contains("약 20", StringComparison.Ordinal)) &&
              parts[0].Rows.All(row => !row.Detail.Contains("약 20", StringComparison.Ordinal)) &&
              panel.QuotaRowIds(QuotaProvider.Gemini).Length == 4, "the two parts, their rows or the estimate mark are wrong.");
        Check(parts.All(part => part.Title.Detail.Contains(MacPollingDiagnosticSmoke.FixedCadenceLine, StringComparison.Ordinal)),
              "a part's detail does not state the fixed cadence.");
        NSView twoBox = panel.QuotaBoxView(QuotaProvider.Gemini);
        Show(gemini, antigravity with { LastSuccessfulCheckUtc = utc.AddMinutes(-1) });
        Check(ReferenceEquals(twoBox, panel.QuotaBoxView(QuotaProvider.Gemini)), "a value-only update rebuilt the two-part box.");
        string? folder = Environment.GetEnvironmentVariable(MacQuotaRowImages.EnvironmentVariable);
        void Picture(string name)
        {
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            foreach (bool dark in new[] { false, true })
            {
                panel.SetSmokeAppearance(dark);
                panel.QuotaBoxScrollsIntoView(QuotaProvider.Gemini);
                foreach (bool bottom in new[] { false, true })
                {
                    panel.ScrollQuotaArea(bottom);
                    if (panel.SnapshotPng() is { } png)
                        File.WriteAllBytes(Path.Combine(folder, $"mac-antigravity-{name}-{(bottom ? "bottom" : "top")}-{(dark ? "dark" : "light")}.png"), png.ToArray());
                }
                panel.ScrollQuotaArea(toBottom: false);
            }
            panel.SetSmokeAppearance(null);
        }
        Show(gemini, antigravity);
        Picture("both");

        // No result yet in either part: two placeholder rows with their own IDs, no bars.
        var waiting = Show(Waiting(QuotaProvider.Gemini), Waiting(QuotaProvider.Antigravity));
        Check(panel.QuotaRowIds(QuotaProvider.Gemini).SequenceEqual([QuotaProvider.Antigravity + ":placeholder", QuotaProvider.Gemini + ":placeholder"]) &&
              panel.QuotaBarCount == bars - 2, "the two placeholder rows share an ID or drew a bar.");
        Picture("waiting");
        // Gemini needs a sign-in: the Antigravity part says so instead of showing its earlier values.
        var geminiSignIn = Show(Web(QuotaProvider.Gemini, null, signIn), antigravity);
        Check(geminiSignIn.Section.Parts![1].Rows is [{ Text: AntigravityWebText.GeminiLoginRequired, Tone: PanelTone.Danger }] &&
              panel.QuotaBarCount == bars - 2, "the Gemini sign-in state did not replace the Antigravity values.");
        Picture("gemini-sign-in");
        // Only Antigravity needs a sign-in: its part warns, the Gemini part keeps its values.
        var ownSignIn = Show(gemini, Web(QuotaProvider.Antigravity, null, signIn));
        Check(ownSignIn.Section.Parts![1].Rows is [{ Tone: PanelTone.Danger } warning] &&
              warning.Text == QuotaPanelModel.FailureMessage(QuotaProvider.Antigravity, QuotaFailureKind.LoginRequired) &&
              panel.QuotaBarCount == bars, "the Antigravity sign-in state is wrong.");

        // The usual screens: popover not cut off, every control reachable, the other boxes still reachable.
        Show(gemini, antigravity);
        var sizes = new List<string>();
        foreach (int screen in new[] { 800, 640 })
        {
            double usable = screen - (25 + 24);
            CGSize size = panel.FitFor((nfloat)usable);
            var audit = panel.ScrollAudit();
            nfloat area = panel.QuotaAreaHeight, box = panel.QuotaBoxHeight(QuotaProvider.Gemini);
            Check(size.Height <= usable + 0.5 && audit.Unreachable is null && panel.QuotaBoxScrollsIntoView(QuotaProvider.Xai) &&
                  (box > area || panel.QuotaBoxScrollsIntoView(QuotaProvider.Gemini)),
                $"on a {screen}pt screen the popover is cut off or a control or box is out of reach.");
            sizes.Add($"{screen}pt screen: popover {size.Height:0}pt, quota area {area:0}pt, Gemini box {box:0}pt");
        }
        panel.FitFor(without.Layout.UsableHeight);
        Show(gemini, off);
        return $"two-part Gemini box (off/unavailable identical to the one-part box; both values, Antigravity reset marked as an estimate; waiting placeholders; Gemini and own sign-in; " +
               $"value-only update keeps the box; {string.Join("; ", sizes)})";
    }
}
