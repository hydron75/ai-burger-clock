namespace AiBurgerClock;

internal sealed partial class TrayApplicationContext
{
    private readonly CancellationTokenSource geminiWebLifetime = new();
    private IWindowsGeminiWebSession? geminiWebSession;
    private WindowsGeminiWebAccess? geminiWebAccess;
    private WindowsWarningLog? geminiWebWarnings;

    private AccountQuotaMonitor CreateWindowsQuotaMonitor(IAccountQuotaClient client, IWindowsGeminiWebSession? session)
    {
        string dataDirectory = Path.GetDirectoryName(Path.GetFullPath(store.DatabasePath))!;
        var settingsStore = new WindowsGeminiWebSettingsStore(Path.Combine(dataDirectory, WindowsGeminiWebSettingsStore.FileName));
        geminiWebWarnings = new WindowsWarningLog(Path.Combine(dataDirectory, "logs"));
        WindowsGeminiWebSettings settings;
        try { settings = settingsStore.Load(); }
        catch
        {
            settings = new();
            ReportGeminiIssue("Gemini 웹 조회 설정을 읽지 못했습니다. 조회를 꺼 둡니다.");
        }

        geminiWebSession = session ?? new WindowsGeminiWebSession(statusWindow, dataDirectory);
        statusWindow.UseGeminiWebQuota();
        var quotas = new AccountQuotaMonitor(client, store, utcNow, geminiWebReader: geminiWebSession,
            geminiWebEnabled: settings.Enabled);
        var access = new WindowsGeminiWebAccess(quotas, store, geminiWebSession, settingsStore, settings);
        geminiWebAccess = access;

        var root = new ToolStripMenuItem(WindowsGeminiWebAccess.MenuLabel);
        var login = new ToolStripMenuItem("로그인…");
        var off = new ToolStripMenuItem("끄기");
        var logout = new ToolStripMenuItem("로그아웃");
        root.DropDownItems.AddRange([login, off, logout]);
        menu.Items.Insert(menu.Items.IndexOf(mobileExportMenuItem), root);

        void UpdateMenu()
        {
            var state = access.MenuState;
            login.Enabled = state.Login;
            off.Enabled = state.Off;
            logout.Enabled = state.Logout;
            root.ToolTipText = access.LogoutPending ? WindowsGeminiWebAccess.LogoutPendingText : "";
        }

        menu.Opening += (_, _) => UpdateMenu();
        root.DropDownOpening += (_, _) => UpdateMenu();
        login.Click += async (_, _) => await RunGeminiActionAsync(access.LoginAsync, "Gemini 로그인 창을 열거나 설정을 저장하지 못했습니다.");
        off.Click += async (_, _) => await RunGeminiActionAsync(_ =>
        {
            access.TurnOff();
            return Task.CompletedTask;
        }, "Gemini 웹 조회 설정을 저장하지 못했습니다.");
        logout.Click += async (_, _) => await RunGeminiActionAsync(access.LogoutAsync, "Gemini 로그아웃 설정을 저장하지 못했습니다.");
        UpdateMenu();
        return quotas;
    }

    private async Task RunGeminiActionAsync(Func<CancellationToken, Task> action, string failure)
    {
        if (exiting || disposed) return;
        var token = geminiWebLifetime.Token;
        try { await action(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            if (!exiting && !disposed)
                ReportGeminiIssue(geminiWebAccess is { LogoutPending: true } ? WindowsGeminiWebAccess.LogoutPendingText : failure);
        }
    }

    private void ReportGeminiIssue(string message)
    {
        geminiWebWarnings?.Write(message);
        statusWindow.SetFeedback(message, true);
    }

}
