namespace AiBurgerClock;

internal enum WindowsNotificationProvider { None, OpenAI, Claude, Gemini, Xai }

// Windows transport metadata only. The shared decisions, wording and queues stay at their callers.
internal sealed record WindowsNotificationRequest(WindowsQuotaNotificationKind Kind,
    WindowsNotificationProvider Provider, string Title, string Body)
{
    private static long nextId;
    internal long Id { get; } = Interlocked.Increment(ref nextId);
    internal string Tag { get; } = TagFor(Kind, Provider);
    internal bool IsQuota => Kind is WindowsQuotaNotificationKind.Recovery or WindowsQuotaNotificationKind.EarlyReset or WindowsQuotaNotificationKind.Combined;
    internal string Group => IsQuota ? "quota" : "status";
    internal string Header => IsQuota ? "한도" : "시간표·상태";
    internal ToolTipIcon Icon => Kind is WindowsQuotaNotificationKind.ScheduleBurger or WindowsQuotaNotificationKind.ProviderIssue
        ? ToolTipIcon.Warning : ToolTipIcon.Info;
    internal int BalloonTimeout => Kind is WindowsQuotaNotificationKind.HolidayOn or WindowsQuotaNotificationKind.HolidayOff ? 4000 : 6000;

    internal static WindowsNotificationRequest FromQuota(QuotaNotificationAlert alert) => new(
        alert.Recovery is not null && alert.EarlyReset is not null ? WindowsQuotaNotificationKind.Combined :
            alert.Recovery is not null ? WindowsQuotaNotificationKind.Recovery : WindowsQuotaNotificationKind.EarlyReset,
        alert.Provider switch
        {
            QuotaProvider.Codex => WindowsNotificationProvider.OpenAI,
            QuotaProvider.Claude => WindowsNotificationProvider.Claude,
            QuotaProvider.Gemini => WindowsNotificationProvider.Gemini,
            _ => throw new ArgumentOutOfRangeException(nameof(alert))
        }, alert.Title, alert.Body);

    internal static WindowsNotificationProvider ForProvider(ProviderKind provider) => provider switch
    {
        ProviderKind.OpenAI => WindowsNotificationProvider.OpenAI,
        ProviderKind.Claude => WindowsNotificationProvider.Claude,
        ProviderKind.Gemini => WindowsNotificationProvider.Gemini,
        ProviderKind.Xai => WindowsNotificationProvider.Xai,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static string TagFor(WindowsQuotaNotificationKind kind, WindowsNotificationProvider provider) => kind switch
    {
        WindowsQuotaNotificationKind.Recovery or WindowsQuotaNotificationKind.EarlyReset or WindowsQuotaNotificationKind.Combined => Guid.NewGuid().ToString("N")[..16],
        WindowsQuotaNotificationKind.ScheduleFull => "sched-full",
        WindowsQuotaNotificationKind.ScheduleBurger => "sched-burger",
        WindowsQuotaNotificationKind.HolidayOn or WindowsQuotaNotificationKind.HolidayOff => "holiday",
        WindowsQuotaNotificationKind.ProviderIssue or WindowsQuotaNotificationKind.ProviderRecovered =>
            "svc-" + provider.ToString().ToLowerInvariant() + (kind == WindowsQuotaNotificationKind.ProviderIssue ? "-issue" : "-recovered"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
