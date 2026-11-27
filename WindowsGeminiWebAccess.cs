using System.Text.Json;

namespace AiBurgerClock;

internal sealed record WindowsGeminiWebSettings(bool Enabled = false, bool LogoutPending = false, bool AntigravityEnabled = false);

internal sealed class WindowsGeminiWebSettingsStore(string path)
{
    internal const string FileName = "gemini-web-settings.json";

    internal WindowsGeminiWebSettings Load()
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 1024) throw new InvalidDataException("Invalid Gemini web settings.");
            var settings = JsonSerializer.Deserialize(file, WindowsGeminiWebJson.Default.WindowsGeminiWebSettings);
            return settings is null || settings.LogoutPending && (settings.Enabled || settings.AntigravityEnabled)
                ? throw new InvalidDataException("Invalid Gemini web settings.")
                : settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
    }

    internal void Save(WindowsGeminiWebSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, settings, WindowsGeminiWebJson.Default.WindowsGeminiWebSettings);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal interface IWindowsGeminiWebSession : IGeminiUsagePageReader, IDisposable
{
    Task<bool> SignInAsync(CancellationToken cancellationToken);
    Task DeleteProfileAsync(CancellationToken cancellationToken);
}

internal readonly record struct WindowsGeminiWebMenuState(bool Login, bool Off, bool Logout);

internal sealed class WindowsGeminiWebAccess(
    AccountQuotaMonitor monitor, IWindowsGeminiWebSession session,
    WindowsGeminiWebSettingsStore settingsStore, WindowsGeminiWebSettings initial)
{
    private const QuotaProvider Provider = QuotaProvider.Gemini;
    private WindowsGeminiWebSettings settings = initial;
    private bool busy;
    private bool logoutModeSet;

    internal const string LogoutPendingText = "Gemini 로그아웃 정리를 완료하지 못했습니다. 다음 시작 때 다시 시도합니다.";
    internal bool LogoutPending => settings.LogoutPending;
    internal bool AntigravityEnabled => settings.AntigravityEnabled;
    internal bool CanToggleAntigravity => settings.Enabled && !busy && !LogoutPending && HasAntigravity;
    private bool HasAntigravity => monitor.Snapshot().Any(state =>
        state.Provider == QuotaProvider.Antigravity && state.Mode != QuotaSourceMode.Unavailable);

    internal WindowsGeminiWebMenuState MenuState
    {
        get
        {
            var states = monitor.Snapshot();
            return MenuFor(states.Single(state => state.Provider == Provider), busy, LogoutPending,
                states.Any(state => state is { Provider: QuotaProvider.Antigravity, Mode: QuotaSourceMode.Active,
                    Failure.Kind: QuotaFailureKind.LoginRequired }));
        }
    }

    internal static WindowsGeminiWebMenuState MenuFor(QuotaState state, bool busy, bool logoutPending, bool antigravityNeedsLogin = false)
    {
        if (state.Mode == QuotaSourceMode.Unavailable || busy) return default;
        if (logoutPending) return new(false, false, true);
        return new(state.Mode == QuotaSourceMode.Off || state.Failure?.Kind == QuotaFailureKind.LoginRequired || antigravityNeedsLogin,
            state.Mode == QuotaSourceMode.Active, true);
    }

    internal static void RequestRefresh(AccountQuotaMonitor? monitor, bool queueWhileRefreshing, bool refreshGeminiWeb)
    {
        monitor?.RequestRefresh(queueWhileRefreshing);
        if (refreshGeminiWeb && monitor is not null)
        {
            monitor.RequestSourceRefresh(Provider);
            monitor.RequestSourceRefresh(QuotaProvider.Antigravity);
        }
    }

    internal async Task LoginAsync(CancellationToken cancellationToken)
    {
        if (!MenuState.Login) return;
        busy = true;
        try
        {
            if (!await session.SignInAsync(cancellationToken)) return;
            cancellationToken.ThrowIfCancellationRequested();
            Save(settings with { Enabled = true });
            monitor.SetMode(Provider, QuotaSourceMode.Active);
            UpdateAntigravityMode(afterSignIn: true);
        }
        finally { busy = false; }
    }

    internal void TurnOff()
    {
        if (!MenuState.Off) return;
        Save(settings with { Enabled = false });
        UpdateAntigravityMode();
        monitor.SetMode(Provider, QuotaSourceMode.Off);
    }

    internal void ToggleAntigravity()
    {
        if (!CanToggleAntigravity) return;
        Save(settings with { AntigravityEnabled = !settings.AntigravityEnabled });
        UpdateAntigravityMode();
    }

    private void Save(WindowsGeminiWebSettings next)
    {
        settingsStore.Save(next);
        settings = next;
    }

    private void UpdateAntigravityMode(bool afterSignIn = false)
    {
        if (!HasAntigravity) return;
        var mode = settings.Enabled && settings.AntigravityEnabled ? QuotaSourceMode.Active : QuotaSourceMode.Off;
        if (monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Antigravity).Mode != mode)
            monitor.SetMode(QuotaProvider.Antigravity, mode);
        else if (afterSignIn && mode == QuotaSourceMode.Active)
            monitor.RequestSourceRefresh(QuotaProvider.Antigravity, afterSignIn: true);
    }

    internal async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (!MenuState.Logout) return;
        if (!LogoutPending)
        {
            // Record the retry before touching the cache or profile. A restart
            // remains off even if the previous process could not finish deletion.
            Save(new(LogoutPending: true));
        }
        await RetryLogoutAsync(cancellationToken);
    }

    internal async Task RetryLogoutAsync(CancellationToken cancellationToken)
    {
        if (!LogoutPending || busy) return;
        busy = true;
        try
        {
            // A restart with logout pending is already off. Establish this
            // monitor's off generation before queuing the cache delete.
            if (!logoutModeSet)
            {
                if (HasAntigravity) monitor.SetMode(QuotaProvider.Antigravity, QuotaSourceMode.Off);
                monitor.SetMode(Provider, QuotaSourceMode.Off);
                logoutModeSet = true;
            }
            await monitor.ClearCacheAsync(Provider, cancellationToken);
            if (HasAntigravity) await monitor.ClearCacheAsync(QuotaProvider.Antigravity, cancellationToken);
            await session.DeleteProfileAsync(cancellationToken);
            Save(new());
            logoutModeSet = false;
        }
        finally { busy = false; }
    }
}
