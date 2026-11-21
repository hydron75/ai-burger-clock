using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace AiBurgerClock;

internal static class WindowsGeminiWebChecks
{
    internal sealed class FakeSession : IWindowsGeminiWebSession
    {
        private int reads;
        internal int Reads => Volatile.Read(ref reads);
        internal int SignIns { get; private set; }
        internal int ProfileDeletes { get; private set; }
        internal bool LoginSucceeds { get; set; }
        internal Func<GeminiUsagePageLoad> ReadPage { get; set; } = () => GeminiUsagePageLoad.LoadFailed;
        internal Action? BeforeSignIn { get; set; }
        internal Func<CancellationToken, Task>? BeforeDelete { get; set; }
        public bool HasProfile { get; set; }

        public Task<bool> SignInAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeSignIn?.Invoke();
            SignIns++;
            HasProfile |= LoginSucceeds;
            return Task.FromResult(LoginSucceeds);
        }

        public async Task DeleteProfileAsync(CancellationToken cancellationToken)
        {
            ProfileDeletes++;
            if (BeforeDelete is not null) await BeforeDelete(cancellationToken);
            HasProfile = false;
        }

        public Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (url != GeminiUsagePage.Url || script != GeminiUsagePage.ReadScript) throw new InvalidOperationException("Unexpected Gemini read contract.");
            Interlocked.Increment(ref reads);
            return Task.FromResult(ReadPage());
        }

        public void Dispose() { }
    }

    internal static GeminiUsagePageLoad ValidPage(DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        string fiveHour = local.AddHours(1).ToString("h:mm tt", CultureInfo.InvariantCulture);
        string weekly = local.AddDays(2).ToString("MMM d 'at' h:mm tt", CultureInfo.InvariantCulture);
        return new(GeminiUsagePage.Url, $"Current usage\n37% used\nResets at {fiveHour}\nWeekly limit\nResets {weekly}\n61% used");
    }

    private sealed class NoCli : IAccountQuotaClient
    {
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken token) =>
            throw new InvalidOperationException("Live CLI is outside the Gemini fixture.");
    }

    internal static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string test)
        {
            if (!condition) throw new InvalidOperationException("Windows Gemini web: " + test);
            count++;
        }
        var dom = new WindowsGeminiPageDom(GeminiUsagePage.Url, "Synthetic quote \" and newline\n");
        string json = JsonSerializer.Serialize(dom, WindowsGeminiWebJson.Default.WindowsGeminiPageDom);
        static string Encode(WindowsGeminiPageDom sample) => JsonSerializer.Serialize(
            JsonSerializer.Serialize(sample, WindowsGeminiWebJson.Default.WindowsGeminiPageDom),
            WindowsGeminiWebJson.Default.String);
        string scriptResult = Encode(dom);
        Check(WindowsGeminiWebSession.DecodePage(scriptResult) == dom, "unquotes the script result and then reads JSON");
        Check(WindowsGeminiWebSession.DecodePage(json) is null &&
            WindowsGeminiWebSession.DecodePage("null") is null && WindowsGeminiWebSession.DecodePage("\"bad json\"") is null,
            "rejects invalid or missing script results");
        var fixtureNow = GeminiUsagePageTests.Now;
        var goodPage = ValidPage(fixtureNow, TimeZoneInfo.Utc);
        string goodScript = Encode(new(goodPage.FinalUrl, goodPage.Text));
        var scripts = new Queue<string>([Encode(dom with { Text = "" }),
            Encode(dom with { Text = goodPage.Text!.Split("Weekly limit", StringSplitOptions.None)[0] }), goodScript]);
        var extracted = await WindowsGeminiWebSession.ReadDomAsync(() => GeminiUsagePage.Url,
            () => Task.FromResult(scripts.Dequeue()), () => fixtureNow, TimeZoneInfo.Utc, default).ConfigureAwait(false);
        Check(scripts.Count == 0 && extracted == goodPage, "waits through blank and partially rendered sections");
        const string accounts = "https://accounts.google.com/signin";
        int locationSamples = 0, executions = 0;
        var renewed = await WindowsGeminiWebSession.ReadDomAsync(
            () => ++locationSamples == 1 ? accounts : GeminiUsagePage.Url,
            () => { executions++; return Task.FromResult(goodScript); }, () => fixtureNow, TimeZoneInfo.Utc, default).ConfigureAwait(false);
        Check(locationSamples >= 2 && executions == 1 && renewed == goodPage, "a sign-in detour can return to usage");
        var stayed = await WindowsGeminiWebSession.ReadDomAsync(() => accounts,
            () => throw new InvalidOperationException("Script must not run on an accounts page."),
            () => fixtureNow, TimeZoneInfo.Utc, default).ConfigureAwait(false);
        Check(stayed == new GeminiUsagePageLoad(accounts, null) &&
            GeminiUsagePage.Parse(stayed, fixtureNow, TimeZoneInfo.Utc).Outcome == GeminiUsageOutcome.LoginRequired,
            "an off-origin final location is passed back for LoginRequired after the wait");
        Check(WindowsGeminiWebSession.IsUsagePage(GeminiUsagePage.Url) &&
            WindowsGeminiWebSession.IsUsagePage("https://gemini.google.com/usage") &&
            !WindowsGeminiWebSession.IsUsagePage("https://gemini.google.com/app") &&
            !WindowsGeminiWebSession.IsUsagePage("http://gemini.google.com/usage") &&
            !WindowsGeminiWebSession.IsUsagePage("https://gemini.google.com.example.test/usage"),
            "login completion requires the exact HTTPS origin and path");
        const string app = "https://gemini.google.com/app";
        const CoreWebView2WebErrorStatus noError = CoreWebView2WebErrorStatus.Unknown;
        int reopens = 0;
        var warnings = new List<string>();
        var redirect = new WindowsGeminiWebSession.LoginNavigation(() => reopens++, warnings.Add);
        redirect.Started(1, GeminiUsagePage.Url);
        redirect.Started(1, accounts, redirected: true);
        redirect.Started(1, app, redirected: true);
        Check(!redirect.Completed(1, app, true, noError) && reopens == 1, "an off-origin redirect returning to the app reopens usage once");
        redirect.Started(2, GeminiUsagePage.Url);
        Check(redirect.Completed(2, GeminiUsagePage.Url, true, noError) && reopens == 1, "the reopened usage page finishes login");

        reopens = 0;
        var separateNavigations = new WindowsGeminiWebSession.LoginNavigation(() => reopens++, warnings.Add);
        separateNavigations.Started(3, GeminiUsagePage.Url);
        Check(!separateNavigations.Completed(3, app, true, noError) && reopens == 0, "an initial app redirect alone does not reopen usage");
        separateNavigations.Started(4, accounts);
        Check(!separateNavigations.Completed(4, accounts, true, noError) && reopens == 0, "off-origin login stays open");
        separateNavigations.Started(5, app);
        Check(!separateNavigations.Completed(5, app, true, noError) && reopens == 1, "an interactive sign-in returning to the app reopens usage");
        separateNavigations.Started(6, GeminiUsagePage.Url);
        Check(!separateNavigations.Completed(6, app, true, noError) && reopens == 1, "a retry landing on the app keeps the window open without looping");
        separateNavigations.Started(7, accounts);
        separateNavigations.Started(8, app);
        Check(!separateNavigations.Completed(8, app, true, noError) && reopens == 1, "another sign-in detour cannot reopen usage again");

        var failed = new WindowsGeminiWebSession.LoginNavigation(() => reopens++, warnings.Add);
        failed.Started(9, GeminiUsagePage.Url);
        Check(!failed.Completed(9, "https://example.test/?fixture=private", false, CoreWebView2WebErrorStatus.CannotConnect) &&
            warnings.SequenceEqual(["Gemini 로그인 [초기 탐색] CannotConnect"]),
            "a failed initial navigation keeps waiting and reports only stage and error code");
        var superseded = new WindowsGeminiWebSession.LoginNavigation(() => reopens++, warnings.Add);
        superseded.Started(10, GeminiUsagePage.Url);
        Check(!superseded.Completed(10, app, false, CoreWebView2WebErrorStatus.OperationCanceled) && warnings.Count == 1,
            "a canceled navigation does not report an initial failure");
        superseded.Started(11, accounts);
        Check(!superseded.Completed(10, app, false, CoreWebView2WebErrorStatus.CannotConnect) && warnings.Count == 1,
            "late completions cannot affect the new navigation");

        bool loader = NativeLibrary.TryLoad("WebView2Loader.dll", typeof(Microsoft.Web.WebView2.Core.CoreWebView2Environment).Assembly, null, out var handle);
        if (loader) NativeLibrary.Free(handle);
        Check(loader, "the x64 loader resolves without creating a WebView");
        Check(WindowsGeminiWebAccess.MenuFor(null, false, false) == default &&
            WindowsGeminiWebAccess.MenuFor(new(), true, true) == default, "no commands are available while a profile operation is running");
        Check(WindowsGeminiWebAccess.MenuFor(new(IsEnabled: false), false, false) == new WindowsGeminiWebMenuState(true, false, true),
            "a new or off profile has login and logout available");
        Check(WindowsGeminiWebAccess.MenuFor(new(IsEnabled: false), false, true) == new WindowsGeminiWebMenuState(false, false, true),
            "pending cleanup still allows a manual logout retry");
        Check(WindowsGeminiWebAccess.MenuFor(new(GeminiUsageOutcome.LoginRequired), false, false) == new WindowsGeminiWebMenuState(true, true, true),
            "expired session can log in again, turn off, or clear the profile");
        foreach (var outcome in new[] { GeminiUsageOutcome.Ok, GeminiUsageOutcome.FormatChanged, GeminiUsageOutcome.LoadFailed })
            Check(WindowsGeminiWebAccess.MenuFor(new(outcome), false, false) == new WindowsGeminiWebMenuState(false, true, true),
                "enabled session retains off and logout during ordinary results");

        var temp = Directory.CreateTempSubdirectory("AiBurgerClock-gemini-tests-");
        try
        {
            var now = GeminiUsagePageTests.Now;
            var store = new UsageStore(Path.Combine(temp.FullName, "synthetic.db"));
            string preferences = Path.Combine(temp.FullName, WindowsGeminiWebSettingsStore.FileName);
            var settings = new WindowsGeminiWebSettingsStore(preferences);
            using var session = new FakeSession { ReadPage = () => ValidPage(now, TimeZoneInfo.Utc) };
            using var monitor = new AccountQuotaMonitor(new NoCli(), store, () => now, geminiWebReader: session, geminiWebTimeZone: TimeZoneInfo.Utc);
            var access = new WindowsGeminiWebAccess(monitor, store, session, settings, settings.Load());
            QuotaState Gemini() => monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Gemini);

            Check(settings.Load() == new WindowsGeminiWebSettings() && !File.Exists(preferences), "absent settings are off without a write");
            await access.LoginAsync(default);
            await monitor.RefreshGeminiWebIfDueAsync();
            Check(Gemini().GeminiWeb is { IsEnabled: false } && session.Reads == 0 && !File.Exists(preferences),
                "closing login without success neither enables nor schedules a read");
            session.LoginSucceeds = true;
            await access.LoginAsync(default);
            Check(Gemini().GeminiWeb is { IsEnabled: true } &&
                new WindowsGeminiWebSettingsStore(preferences).Load() == new WindowsGeminiWebSettings(Enabled: true),
                "successful login enables and persists");
            using (var restarted = new AccountQuotaMonitor(new NoCli(), utcNow: () => now, geminiWebReader: session,
                geminiWebEnabled: settings.Load().Enabled))
                Check(restarted.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).GeminiWeb is { IsEnabled: true },
                    "saved enabled flag is usable at startup");
            await monitor.RefreshGeminiWebIfDueAsync();
            Check(Gemini().GeminiWeb?.Outcome == GeminiUsageOutcome.Ok && session.Reads == 1, "fake reader publishes current rows");

            WindowsGeminiWebAccess.RequestRefresh(monitor, queueWhileRefreshing: false, refreshGeminiWeb: false);
            WindowsGeminiWebAccess.RequestRefresh(monitor, queueWhileRefreshing: true, refreshGeminiWeb: false);
            await monitor.RefreshGeminiWebIfDueAsync();
            Check(session.Reads == 1, "refreshes without Gemini, including queued recovery, do not wake Gemini");
            WindowsGeminiWebAccess.RequestRefresh(monitor, queueWhileRefreshing: false, refreshGeminiWeb: true);
            await monitor.RefreshGeminiWebIfDueAsync();
            Check(session.Reads == 2, "manual refresh wakes Gemini before the next scheduled read");

            var legacy = new QuotaReading(QuotaProvider.Gemini, [new("synthetic-agy", "Synthetic legacy", 19, now.AddDays(1), 10080)]);
            await store.SaveQuotaAsync(new(1, legacy, now, []));
            access.TurnOff();
            Check(settings.Load() == new WindowsGeminiWebSettings() && Gemini().GeminiWeb is { IsEnabled: false },
                "turn off is persisted and removes live state");
            Check(session.HasProfile && session.ProfileDeletes == 0 && await store.ReadGeminiWebQuotaAsync() is not null,
                "turning off preserves the web profile and cache");

            session.BeforeDelete = async _ =>
            {
                Check(Gemini().GeminiWeb is { IsEnabled: false } && await store.ReadGeminiWebQuotaAsync() is null &&
                    settings.Load() == new WindowsGeminiWebSettings(LogoutPending: true),
                    "profile cleanup sees off, a deleted cache, and a durable pending flag");
                throw new IOException("Synthetic profile still locked");
            };
            try { await access.LogoutAsync(default); throw new InvalidOperationException("Expected synthetic profile failure."); }
            catch (IOException) { }
            Check(access.LogoutPending && access.MenuState == new WindowsGeminiWebMenuState(false, false, true) &&
                session.HasProfile && session.ProfileDeletes == 1, "failed cleanup permits logout retry and blocks login");
            var retry = new WindowsGeminiWebAccess(monitor, store, session, settings, settings.Load());

            var waitingForExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var browserExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.BeforeDelete = async token =>
            {
                waitingForExit.TrySetResult();
                await browserExited.Task.WaitAsync(token);
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var cleanup = retry.RetryLogoutAsync(timeout.Token);
            try
            {
                await waitingForExit.Task.WaitAsync(timeout.Token);
                Check(session.HasProfile && settings.Load().LogoutPending && !cleanup.IsCompleted && retry.MenuState == default,
                    "pending retry waits for profile release and disables the menu while running");
            }
            finally { browserExited.TrySetResult(); }
            await cleanup;
            Check(!session.HasProfile && session.ProfileDeletes == 2 && settings.Load() == new WindowsGeminiWebSettings(),
                "retry clears profile and resets the pending flag after release");
            Check(await store.ReadGeminiWebQuotaAsync() is null &&
                (await store.ReadQuotaAsync(QuotaProvider.Gemini))?.Reading.Windows.SequenceEqual(legacy.Windows) == true,
                "logout only removes the web cache");
            Check(retry.MenuState == new WindowsGeminiWebMenuState(true, false, true) && session.SignIns == 2,
                "logout leaves explicit login and logout available while off");
        }
        finally
        {
            if (temp.Parent?.FullName != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !temp.Name.StartsWith("AiBurgerClock-gemini-tests-", StringComparison.Ordinal))
                throw new IOException("Synthetic Gemini fixture cleanup boundary mismatch.");
            temp.Delete(recursive: true);
        }
        return count;
    }
}
