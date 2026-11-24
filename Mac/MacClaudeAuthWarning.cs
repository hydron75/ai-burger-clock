using AppKit;

namespace AiBurgerClock;

// Mac host of the Claude CLI login-expiry warning. What counts as a warning (7 days before the stored login's end, the
// subscription login only, once per end date), the texts and the setting keys are shared (ClaudeAuthCheck,
// UsageStore); this class only owns what a host must: the menu item, the setting, and the hand-off to a
// notification. Same contract as the Windows tray (TrayApplicationContext.ClaudeAuth.cs):
//   - Default OFF, switched only by the menu item, persisted in the shared database.
//   - One notification per login end date; the last warned end date is kept so a restart does not repeat it.
//   - It waits behind every other alert (schedule, service status, quota), like the quota notifications do.
// Everything runs on the main thread (the display tick and the menu).
internal sealed class MacClaudeAuthWarning
{
    internal const string ReadFailed = "Claude 로그인 알림 설정을 읽지 못했습니다.";
    internal const string SaveFailed = "Claude 로그인 알림 설정을 저장하지 못했습니다.";
    internal const string HistoryFailed = "Claude 로그인 알림 이력을 저장하지 못했습니다.";
    internal const string DeliveryFailed = "Claude 로그인 만료 알림을 보내지 못했습니다.";

    private readonly Func<bool> isStopping;
    private readonly Func<Task<(bool Enabled, DateTimeOffset? WarnedExpiry)>> read;
    private readonly Func<bool, Task> saveEnabled;
    private readonly Func<DateTimeOffset, Task> rememberExpiry;
    private readonly Func<ClaudeAuthCheck?> latest;
    private readonly Func<bool> canShowNow;
    private readonly Action<string, string> show;
    private readonly Action deferAfterShow;
    private readonly Action<string, bool> feedback;
    private readonly HashSet<Task> pendingWrites;
    private readonly Func<DateTimeOffset> utcNow;
    private bool enabled;
    private bool ready;
    private bool changing;
    private DateTimeOffset? warnedExpiry;

    internal MacClaudeAuthWarning(Func<bool> isStopping, Func<Task<(bool Enabled, DateTimeOffset? WarnedExpiry)>> read,
        Func<bool, Task> saveEnabled, Func<DateTimeOffset, Task> rememberExpiry, Func<ClaudeAuthCheck?> latest,
        Func<bool> canShowNow, Action<string, string> show, Action deferAfterShow, Action<string, bool> feedback,
        HashSet<Task> pendingWrites, Func<DateTimeOffset>? utcNow = null)
    {
        this.isStopping = isStopping;
        this.read = read;
        this.saveEnabled = saveEnabled;
        this.rememberExpiry = rememberExpiry;
        this.latest = latest;
        this.canShowNow = canShowNow;
        this.show = show;
        this.deferAfterShow = deferAfterShow;
        this.feedback = feedback;
        this.pendingWrites = pendingWrites;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        MenuItem = new NSMenuItem(ClaudeAuthCheck.MenuLabel, (_, _) => _ = ToggleAsync())
        {
            Enabled = false,
            ToolTip = $"Claude CLI 로그인 종료 상한까지 {ClaudeAuthCheck.WarningLeadDays}일 이내가 되면 로그인 주기당 한 번 알립니다."
        };
    }

    internal NSMenuItem MenuItem { get; }
    internal bool Enabled => enabled;
    internal bool Ready => ready;
    internal bool Changing => changing;
    internal bool Checked => MenuItem.State == NSCellStateValue.On;
    internal DateTimeOffset? WarnedExpiry => warnedExpiry;

    // A missing key is OFF; an unreadable value is reported and the item stays unusable (never a silent ON).
    internal async Task LoadAsync()
    {
        try { (enabled, warnedExpiry) = await read(); }
        catch (OperationCanceledException) when (isStopping()) { return; }
        catch
        {
            if (!isStopping()) feedback(ReadFailed, true);
            return;
        }
        if (isStopping()) return;
        ready = true;
        RefreshMenu();
    }

    internal async Task ToggleAsync()
    {
        if (!ready || changing || isStopping()) return;
        bool value = !enabled;
        changing = true;
        RefreshMenu();
        Task save = saveEnabled(value);
        pendingWrites.Add(save);
        try
        {
            await save;
            if (!isStopping()) enabled = value;
        }
        catch
        {
            if (!isStopping()) feedback(SaveFailed, true);
        }
        finally
        {
            pendingWrites.Remove(save);
            changing = false;
            if (!isStopping()) RefreshMenu();
        }
    }

    private void RefreshMenu()
    {
        MenuItem.State = enabled ? NSCellStateValue.On : NSCellStateValue.Off;
        MenuItem.Enabled = ready && !changing && !isStopping();
    }

    // Called from every display pass, after the schedule, service and quota alerts were requested.
    internal void TryShow()
    {
        if (isStopping() || !ready || !enabled || changing || !canShowNow()) return;
        if (latest()?.WarningExpiry(utcNow(), warnedExpiry) is not { } candidate) return;
        warnedExpiry = candidate;
        try { show(ClaudeAuthCheck.AlertTitle, ClaudeAuthCheck.ExpiringText); }
        catch (Exception) { feedback(DeliveryFailed, true); }
        deferAfterShow();
        _ = RememberAsync(candidate);
    }

    private async Task RememberAsync(DateTimeOffset expiry)
    {
        Task save = rememberExpiry(expiry);
        pendingWrites.Add(save);
        try { await save; }
        catch
        {
            if (!isStopping()) feedback(HistoryFailed, true);
        }
        finally { pendingWrites.Remove(save); }
    }
}
