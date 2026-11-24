namespace AiBurgerClock;

internal sealed partial class TrayApplicationContext
{
    private readonly ToolStripMenuItem claudeAuthWarningMenuItem = new(ClaudeAuthCheck.MenuLabel)
    {
        Enabled = false,
        ToolTipText = $"Claude CLI 로그인 종료 상한까지 {ClaudeAuthCheck.WarningLeadDays}일 이내가 되면 로그인 주기당 한 번 알립니다."
    };
    private bool claudeAuthWarningEnabled;
    private DateTimeOffset? warnedClaudeExpiry;

    private async Task LoadClaudeAuthWarningAsync()
    {
        try { (claudeAuthWarningEnabled, warnedClaudeExpiry) = await store.ReadClaudeAuthWarningAsync(); }
        catch
        {
            if (!disposed && !exiting) statusWindow.SetFeedback("Claude 로그인 알림 설정을 읽지 못했습니다.", true);
            return;
        }
        if (disposed || exiting) return;
        claudeAuthWarningMenuItem.Checked = claudeAuthWarningEnabled;
        claudeAuthWarningMenuItem.Enabled = true;
    }

    private async Task ToggleClaudeAuthWarningAsync()
    {
        if (!claudeAuthWarningMenuItem.Enabled || exiting || disposed) return;
        bool enabled = !claudeAuthWarningEnabled;
        claudeAuthWarningMenuItem.Enabled = false;
        Task save = store.SetClaudeAuthWarningEnabledAsync(enabled);
        pendingWrites.Add(save);
        try
        {
            await save;
            if (!exiting && !disposed) claudeAuthWarningEnabled = claudeAuthWarningMenuItem.Checked = enabled;
        }
        catch
        {
            if (!exiting && !disposed) statusWindow.SetFeedback("Claude 로그인 알림 설정을 저장하지 못했습니다.", true);
        }
        finally
        {
            pendingWrites.Remove(save);
            if (!exiting && !disposed) claudeAuthWarningMenuItem.Enabled = true;
        }
    }

    private async void TryShowClaudeAuthWarning()
    {
        if (exiting || disposed || !claudeAuthWarningMenuItem.Enabled || !claudeAuthWarningEnabled) return;
        long nowMs = notificationMilliseconds();
        if (pendingProviderAlerts.Count > 0 || nowMs < providerBalloonExpires || nowMs < quotaNotificationNotBefore) return;
        DateTimeOffset? expiry = quotaMonitor?.LatestClaudeAuth?.WarningExpiry(utcNow(), warnedClaudeExpiry);
        if (expiry is not { } candidate) return;

        warnedClaudeExpiry = candidate;
        DeferQuotaNotifications(6500);
        try
        {
            SubmitNotification(new(WindowsQuotaNotificationKind.ClaudeAuthExpiry, WindowsNotificationProvider.Claude,
                ClaudeAuthCheck.AlertTitle, ClaudeAuthCheck.ExpiringText));
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            statusWindow.SetFeedback("Claude 로그인 만료 알림을 보내지 못했습니다.", true);
        }
        Task save = store.RememberClaudeAuthWarningAsync(candidate);
        pendingWrites.Add(save);
        try { await save; }
        catch
        {
            if (!exiting && !disposed) statusWindow.SetFeedback("Claude 로그인 알림 이력을 저장하지 못했습니다.", true);
        }
        finally { pendingWrites.Remove(save); }
    }
}
