using AppKit;

namespace AiBurgerClock;

// What the controller needs from the rest of the app. The real one (MacGeminiWebBackend) is wired to the monitor, the
// store, the two readers and the login window; the native smoke drives the controller with a recording fake.
internal interface IMacGeminiWebBackend
{
    bool LoginOpen { get; }
    void SetEnabled(bool enabled);
    void SetAntigravity(bool active, bool afterSignIn = false);
    Task DeleteCacheAsync();
    void ShowLogin(Action succeeded);
    void CloseLogin();
    Task WaitReadsAsync();
    Task<bool> RemoveDataStoreAsync();
}

// The "Gemini 사용량" submenu of the menu-bar (right-click) menu and what its items do:
//   로그인…  open the sign-in window; on success the reader is switched on and the setting saved.
//   끄기     switch both readers off. The login (data store), the cached readings and the Antigravity choice stay.
//   로그아웃 clear the Antigravity choice, both readers off, delete both cached readings, release every web view, then
//            remove the app's web data store.
//   Antigravity 사용량 조회  check item (default off), usable only while the Gemini reader is on.
// The Gemini reader is switched on only through a completed login, never by a setting alone.
internal sealed class MacGeminiWebController
{
    private readonly MacGeminiWebSettings settings;
    private readonly IMacGeminiWebBackend backend;
    private readonly Action<string, bool> feedback;
    private readonly NSMenuItem login;
    private readonly NSMenuItem off;
    private readonly NSMenuItem logout;
    private readonly NSMenuItem antigravity;
    private readonly MenuUpdater updater;
    private bool busy;

    internal MacGeminiWebController(MacGeminiWebSettings settings, IMacGeminiWebBackend backend, Action<string, bool> feedback)
    {
        this.settings = settings;
        this.backend = backend;
        this.feedback = feedback;
        login = new NSMenuItem(GeminiWebText.LoginLabel, (_, _) => Login());
        off = new NSMenuItem(GeminiWebText.OffLabel, (_, _) => Off());
        logout = new NSMenuItem(GeminiWebText.LogoutLabel, (_, _) => _ = LogoutAsync());
        antigravity = new NSMenuItem(AntigravityWebText.MenuLabel, (_, _) => ToggleAntigravity());
        updater = new(UpdateMenu);
        var submenu = new NSMenu(GeminiWebText.MenuTitle) { AutoEnablesItems = false, Delegate = updater };
        submenu.AddItem(login);
        submenu.AddItem(off);
        submenu.AddItem(logout);
        submenu.AddItem(antigravity);
        MenuItem = new NSMenuItem(GeminiWebText.MenuTitle) { Submenu = submenu };
        UpdateMenu();
    }

    internal NSMenuItem MenuItem { get; }

    // App shutdown: an open sign-in window must not outlive the app's teardown.
    internal void Shutdown() => backend.CloseLogin();

    internal bool Enabled => settings.Enabled;

    internal bool HasSession => settings.HasSession;

    internal bool AntigravityEnabled => settings.AntigravityEnabled;

    // Which items can be used: sign-in while no sign-in window is open, "끄기" only while on, "로그아웃" always (a session
    // may sit in the web data store even when no login was confirmed here), nothing while a logout is running.
    internal static (bool Login, bool Off, bool Logout) ItemsFor(bool enabled, bool loginOpen, bool busy) =>
        busy ? (false, false, false) : (!loginOpen, enabled, true);

    internal (bool Login, bool Off, bool Logout) Items => (login.Enabled, off.Enabled, logout.Enabled);

    internal (bool Enabled, bool Checked, string Tip) AntigravityItem =>
        (antigravity.Enabled, antigravity.State == NSCellStateValue.On, antigravity.ToolTip ?? "");

    internal void UpdateMenu()
    {
        var (canLogin, canOff, canLogout) = ItemsFor(settings.Enabled, backend.LoginOpen, busy);
        login.Enabled = canLogin;
        off.Enabled = canOff;
        logout.Enabled = canLogout;
        antigravity.Enabled = !busy && settings.Enabled;
        antigravity.State = settings.AntigravityEnabled ? NSCellStateValue.On : NSCellStateValue.Off;
        antigravity.ToolTip = antigravity.Enabled ? "" : AntigravityWebText.EnableGemini;
    }

    internal void Login()
    {
        if (busy) return;
        backend.ShowLogin(OnLoginSucceeded);
        UpdateMenu();
    }

    internal void OnLoginSucceeded()
    {
        bool saved = settings.Save(enabled: true, hasSession: true);
        backend.SetEnabled(true);
        // After the Gemini source (its sign-in failure is cleared): an Antigravity read that stopped for the sign-in runs again.
        backend.SetAntigravity(settings.AntigravityActive, afterSignIn: true);
        feedback(saved ? "Gemini 사용량 조회를 켰습니다."
            : "Gemini 사용량 조회를 켰지만 설정을 저장하지 못했습니다. 다음 실행 때 다시 로그인해야 할 수 있습니다.", !saved);
        UpdateMenu();
    }

    internal void Off()
    {
        if (busy || !settings.Enabled) return;
        backend.SetAntigravity(false);
        backend.SetEnabled(false);
        bool saved = settings.Save(enabled: false, hasSession: settings.HasSession);
        feedback(saved ? "Gemini 사용량 조회를 껐습니다. 로그인 정보는 유지됩니다."
            : "Gemini 사용량 조회를 껐지만 설정을 저장하지 못했습니다.", !saved);
        UpdateMenu();
    }

    internal void ToggleAntigravity()
    {
        if (busy || !settings.Enabled) return;
        bool saved = settings.SaveAntigravity(!settings.AntigravityEnabled);
        backend.SetAntigravity(settings.AntigravityActive);
        if (!saved) feedback("Antigravity 사용량 설정을 저장하지 못했습니다.", true);
        UpdateMenu();
    }

    internal async Task LogoutAsync()
    {
        if (busy) return;
        busy = true;
        UpdateMenu();
        try
        {
            // The Antigravity choice is cleared too: another account may sign in next, so it is never switched back on by itself.
            settings.SaveAntigravity(false);
            backend.SetAntigravity(false);
            backend.SetEnabled(false);
            bool offSaved = settings.Save(enabled: false, hasSession: settings.HasSession);
            backend.CloseLogin();
            // Every step runs even if an earlier one failed: a database error must not keep the login session on disk.
            bool cacheDeleted = await Succeeded(backend.DeleteCacheAsync);
            await Succeeded(backend.WaitReadsAsync);
            bool storeRemoved = false;
            try { storeRemoved = await backend.RemoveDataStoreAsync(); }
            catch (Exception) { /* reported below */ }
            if (storeRemoved && cacheDeleted)
            {
                bool saved = settings.Save(enabled: false, hasSession: false);
                feedback(saved ? "Gemini 로그아웃: 저장된 로그인 정보와 한도 기록을 삭제했습니다."
                    : "Gemini 로그인 정보는 삭제했지만 설정을 저장하지 못했습니다.", !saved);
            }
            else
            {
                if (!offSaved) feedback("Gemini 설정을 저장하지 못했습니다.", true);
                feedback(storeRemoved ? "Gemini 한도 기록 삭제를 확인하지 못했습니다. 로그아웃을 다시 실행하세요."
                    : "Gemini 로그인 정보 삭제를 확인하지 못했습니다. 로그아웃을 다시 실행하세요.", true);
            }
        }
        finally
        {
            busy = false;
            UpdateMenu();
        }
    }

    private static async Task<bool> Succeeded(Func<Task> step)
    {
        try
        {
            await step();
            return true;
        }
        catch (Exception) { return false; }
    }
}

// The sign-in window can be closed by the user at any time: look at the state again whenever the submenu opens.
internal sealed class MenuUpdater(Action update) : NSMenuDelegate
{
    public override void NeedsUpdate(NSMenu menu) => update();
}

// The real backend: the monitor (created after start-up, hence the getter), the store the monitor writes its caches
// through, the two readers and the login window.
internal sealed class MacGeminiWebBackend(Func<AccountQuotaMonitor?> monitor, UsageStore store, MacGeminiUsageReader reader,
    MacAntigravityUsageReader antigravity, MacGeminiLoginWindow loginWindow, CancellationToken lifetime) : IMacGeminiWebBackend
{
    public bool LoginOpen => loginWindow.IsOpen;

    // Only a registered web source can be switched: a monitor without the reader (Unavailable) is left alone.
    public void SetEnabled(bool enabled)
    {
        if (monitor() is { } running && Mode(running, QuotaProvider.Gemini) != QuotaSourceMode.Unavailable)
            running.SetMode(QuotaProvider.Gemini, enabled ? QuotaSourceMode.Active : QuotaSourceMode.Off);
    }

    // A source that is already on keeps its reading (turning it on again would drop it); after a sign-in it is only asked
    // to read again. Switching off is always passed on: that is what allows the cache delete.
    public void SetAntigravity(bool active, bool afterSignIn = false)
    {
        if (monitor() is not { } running) return;
        QuotaSourceMode mode = Mode(running, QuotaProvider.Antigravity);
        if (mode == QuotaSourceMode.Unavailable) return;
        if (active && mode == QuotaSourceMode.Active)
        {
            if (afterSignIn) running.RequestSourceRefresh(QuotaProvider.Antigravity, afterSignIn: true);
        }
        else running.SetMode(QuotaProvider.Antigravity, active ? QuotaSourceMode.Active : QuotaSourceMode.Off);
    }

    // Both web caches; the second is deleted even when the first one fails.
    public Task DeleteCacheAsync() => Task.WhenAll(
        DeleteAsync(QuotaProvider.Gemini, QuotaCacheAdapter.ForGeminiWeb(store)),
        DeleteAsync(QuotaProvider.Antigravity, QuotaCacheAdapter.ForAntigravityWeb(store)));

    // The monitor accepts this only after the source was switched off, which logout does first. Without a monitor (start-up
    // not finished) or without such a source the same cache entry is deleted through the same store.
    private async Task DeleteAsync(QuotaProvider provider, QuotaCacheAdapter cache) =>
        await (monitor() is { } running && Mode(running, provider) != QuotaSourceMode.Unavailable
            ? running.ClearCacheAsync(provider, lifetime) : cache.DeleteAsync(lifetime));

    private static QuotaSourceMode Mode(AccountQuotaMonitor running, QuotaProvider provider) =>
        running.Snapshot().Single(state => state.Provider == provider).Mode;

    public void ShowLogin(Action succeeded) => loginWindow.Show(succeeded);

    public void CloseLogin() => loginWindow.Close();

    public Task WaitReadsAsync() => Task.WhenAll(
        reader.WaitIdleAsync(TimeSpan.FromSeconds(MacGeminiPageReading.TotalTimeoutSeconds + 5)),
        antigravity.WaitIdleAsync(TimeSpan.FromSeconds(MacAntigravityPageReading.TotalTimeoutSeconds + 5)));

    public Task<bool> RemoveDataStoreAsync() => reader.RemoveDataStoreAsync();
}
