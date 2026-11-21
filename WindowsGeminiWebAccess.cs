using System.Text.Json;

namespace AiBurgerClock;

internal sealed record WindowsGeminiWebSettings(bool Enabled = false, bool LogoutPending = false);

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
            return settings is { Enabled: true, LogoutPending: true } || settings is null
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
    AccountQuotaMonitor monitor, UsageStore store, IWindowsGeminiWebSession session,
    WindowsGeminiWebSettingsStore settingsStore, WindowsGeminiWebSettings initial)
{
    private WindowsGeminiWebSettings settings = initial;
    private bool busy;

    internal const string LogoutPendingText = "Gemini 로그아웃 정리를 완료하지 못했습니다. 다음 시작 때 다시 시도합니다.";
    internal bool LogoutPending => settings.LogoutPending;

    internal WindowsGeminiWebMenuState MenuState =>
        MenuFor(monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Gemini).GeminiWeb,
            busy, LogoutPending);

    internal static WindowsGeminiWebMenuState MenuFor(GeminiWebQuotaState? state, bool busy, bool logoutPending)
    {
        if (state is null || busy) return default;
        if (logoutPending) return new(false, false, true);
        return new(!state.IsEnabled || state.Outcome == GeminiUsageOutcome.LoginRequired, state.IsEnabled, true);
    }

    internal static void RequestRefresh(AccountQuotaMonitor? monitor, bool queueWhileRefreshing, bool refreshGeminiWeb)
    {
        monitor?.RequestRefresh(queueWhileRefreshing);
        if (refreshGeminiWeb) monitor?.RequestGeminiWebRefresh();
    }

    internal async Task LoginAsync(CancellationToken cancellationToken)
    {
        if (!MenuState.Login) return;
        busy = true;
        try
        {
            if (!await session.SignInAsync(cancellationToken)) return;
            cancellationToken.ThrowIfCancellationRequested();
            settingsStore.Save(new(Enabled: true));
            settings = new(Enabled: true);
            monitor.SetGeminiWebEnabled(true);
        }
        finally { busy = false; }
    }

    internal void TurnOff()
    {
        if (!MenuState.Off) return;
        var off = new WindowsGeminiWebSettings();
        settingsStore.Save(off);
        settings = off;
        monitor.SetGeminiWebEnabled(false);
    }

    internal async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (!MenuState.Logout) return;
        if (!LogoutPending)
        {
            // Record the retry before touching the cache or profile. A restart
            // remains off even if the previous process could not finish deletion.
            var pending = new WindowsGeminiWebSettings(LogoutPending: true);
            settingsStore.Save(pending);
            settings = pending;
            monitor.SetGeminiWebEnabled(false);
        }
        await RetryLogoutAsync(cancellationToken);
    }

    internal async Task RetryLogoutAsync(CancellationToken cancellationToken)
    {
        if (!LogoutPending || busy) return;
        busy = true;
        try
        {
            await store.DeleteGeminiWebQuotaAsync(cancellationToken);
            await session.DeleteProfileAsync(cancellationToken);
            settingsStore.Save(new());
            settings = new();
        }
        finally { busy = false; }
    }
}
