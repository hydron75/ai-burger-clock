namespace AiBurgerClock;

internal enum QuotaSourceMode { Active, Off, Unavailable }
internal enum QuotaExportMode { Completion, OnChange, None }
internal enum QuotaReadiness { Ready, Deferred }

internal sealed record QuotaPresentationPolicy(
    IReadOnlySet<QuotaFailureKind> HideReadingInPanel, IReadOnlySet<QuotaFailureKind> HideReadingInExport,
    bool ClearSuccessOnLoginRequired, Func<QuotaReading, bool>? AcceptForDisplay,
    bool ShowEarlyResetDetail, bool ApproximateReset = false)
{
    internal static readonly QuotaPresentationPolicy Cli = new(
        new HashSet<QuotaFailureKind>(), new HashSet<QuotaFailureKind>(), false, null, true);
    internal static readonly QuotaPresentationPolicy GeminiWeb = new(
        new HashSet<QuotaFailureKind> { QuotaFailureKind.LoginRequired, QuotaFailureKind.FormatChanged },
        new HashSet<QuotaFailureKind> { QuotaFailureKind.LoginRequired }, true, GeminiUsagePage.IsWebReading, false);
    internal static readonly QuotaPresentationPolicy AntigravityWeb = GeminiWeb with
    {
        AcceptForDisplay = AntigravityUsagePage.IsWebReading,
        ApproximateReset = true
    };
}

internal sealed record QuotaSourceTraits(IQuotaSchedule Schedule, TimeSpan? Timeout, bool EmitsObservations,
    bool DueCheckBeforeRead, bool StickyManualRefresh, bool RefreshOnRecovery,
    QuotaPresentationPolicy Presentation, QuotaExportMode Export, TimeSpan? ManualMinimumInterval = null,
    bool CancelLifetimeAsynchronously = false, bool CanSetMode = false, QuotaProvider? DependsOn = null);

internal interface IQuotaSource
{
    QuotaProvider Provider { get; }
    QuotaSourceTraits Traits { get; }
    IQuotaCacheAdapter Cache { get; }
    QuotaReadiness CheckReadiness() => QuotaReadiness.Ready;
    // The web parser needs the injected clock at response time, after WebView navigation.
    Task<QuotaReadResult> ReadAsync(Func<DateTimeOffset> utcNow, TimeZoneInfo zone, CancellationToken cancellationToken);
}

internal sealed class QuotaReadResult
{
    private QuotaReadResult(QuotaReading? reading, QuotaFailure? failure) => (Reading, Failure) = (reading, failure);

    internal QuotaReading? Reading { get; }
    internal QuotaFailure? Failure { get; }
    internal static QuotaReadResult Success(QuotaReading reading) =>
        new(reading ?? throw new QuotaReadException(QuotaFailureKind.FormatChanged, "validate.reading"), null);
    internal static QuotaReadResult Failed(QuotaFailure failure) => new(null, failure);
}

internal interface IQuotaSchedule
{
    QuotaCheckPlan? Next(DateTimeOffset now, QuotaReading? known, QuotaFailure? failure,
        int consecutiveFailures, IReadOnlyList<DateTimeOffset> anchors);

    DateTimeOffset[] UpdateAnchors(DateTimeOffset now, QuotaReading? previous, QuotaReading? known,
        IReadOnlyList<DateTimeOffset> anchors) => [];
}

internal sealed class UsageBasedSchedule(QuotaProvider provider) : IQuotaSchedule
{
    public DateTimeOffset[] UpdateAnchors(DateTimeOffset now, QuotaReading? previous, QuotaReading? known,
        IReadOnlyList<DateTimeOffset> anchors) =>
        // Keep the previous reset's +15-minute band after the server advances to the next reset.
        AccountQuotaPolicy.CoalesceResetAnchors(anchors
            .Concat(AccountQuotaPolicy.CurrentResetAnchors(previous?.Windows ?? [], provider))
            .Concat(AccountQuotaPolicy.CurrentResetAnchors(known?.Windows ?? [], provider))
            .Where(reset => reset.AddMinutes(15) >= now), maximumCount: 64);

    public QuotaCheckPlan Next(DateTimeOffset now, QuotaReading? known, QuotaFailure? failure,
        int consecutiveFailures, IReadOnlyList<DateTimeOffset> anchors)
    {
        var plan = AccountQuotaPolicy.GetNextCheckPlan(now, now, known?.Windows ?? [], anchors, provider);
        if (failure is null) return plan;
        var retry = now + AccountQuotaPolicy.GetFailureRetryDelay(consecutiveFailures, known?.Windows ?? []);
        return retry <= plan.NextCheckUtc ? new(retry, QuotaCheckReason.FailureRetry) : plan;
    }
}

internal sealed class FixedSchedule(TimeSpan interval, bool stopOnLoginRequired) : IQuotaSchedule
{
    public QuotaCheckPlan? Next(DateTimeOffset now, QuotaReading? known, QuotaFailure? failure,
        int consecutiveFailures, IReadOnlyList<DateTimeOffset> anchors) =>
        stopOnLoginRequired && failure?.Kind == QuotaFailureKind.LoginRequired ? null :
            new(now + interval, failure is null ? QuotaCheckReason.Regular : QuotaCheckReason.FailureRetry);
}

internal sealed class CliQuotaSource(QuotaProvider provider, IAccountQuotaClient client, UsageStore? store) : IQuotaSource
{
    public QuotaProvider Provider { get; } = provider;
    public IQuotaCacheAdapter Cache { get; } = QuotaCacheAdapter.ForCli(store, provider);
    public QuotaSourceTraits Traits { get; } = new(new UsageBasedSchedule(provider),
        provider == QuotaProvider.Xai ? AccountQuotaClient.GrokTimeout : TimeSpan.FromSeconds(40),
        EmitsObservations: true, DueCheckBeforeRead: false, StickyManualRefresh: false, RefreshOnRecovery: true,
        QuotaPresentationPolicy.Cli, QuotaExportMode.Completion);

    public async Task<QuotaReadResult> ReadAsync(Func<DateTimeOffset> utcNow, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        try { return QuotaReadResult.Success(await client.ReadAsync(Provider, cancellationToken).ConfigureAwait(false)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { return QuotaReadResult.Failed(AccountQuotaClient.FailureFromRead(error)); }
    }
}

internal sealed class GeminiWebQuotaSource(IGeminiUsagePageReader reader, UsageStore? store) : IQuotaSource
{
    public QuotaProvider Provider => QuotaProvider.Gemini;
    public IQuotaCacheAdapter Cache { get; } = QuotaCacheAdapter.ForGeminiWeb(store);
    public QuotaSourceTraits Traits { get; } = new(new FixedSchedule(TimeSpan.FromMinutes(GeminiUsagePage.PollMinutes), stopOnLoginRequired: true),
        Timeout: null, EmitsObservations: false, DueCheckBeforeRead: true, StickyManualRefresh: true, RefreshOnRecovery: false,
        QuotaPresentationPolicy.GeminiWeb, QuotaExportMode.OnChange, CancelLifetimeAsynchronously: true, CanSetMode: true);

    public async Task<QuotaReadResult> ReadAsync(Func<DateTimeOffset> utcNow, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        GeminiUsagePageLoad load;
        try
        {
            load = await reader.LoadAsync(GeminiUsagePage.Url, GeminiUsagePage.ReadScript, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { load = GeminiUsagePageLoad.LoadFailed; }

        cancellationToken.ThrowIfCancellationRequested();
        var result = GeminiUsagePage.Parse(load, utcNow().ToUniversalTime(), zone);
        return result.Reading is { } reading ? QuotaReadResult.Success(reading) : QuotaReadResult.Failed(result.Failure!);
    }
}

internal sealed class AntigravityWebQuotaSource(IAntigravityUsagePageReader reader, UsageStore? store,
    Func<QuotaReadiness>? hostReadiness = null) : IQuotaSource
{
    public QuotaProvider Provider => QuotaProvider.Antigravity;
    public IQuotaCacheAdapter Cache { get; } = QuotaCacheAdapter.ForAntigravityWeb(store);
    public QuotaSourceTraits Traits { get; } = new(
        new FixedSchedule(TimeSpan.FromMinutes(AntigravityUsagePage.PollMinutes), stopOnLoginRequired: true),
        Timeout: null, EmitsObservations: false, DueCheckBeforeRead: true, StickyManualRefresh: true, RefreshOnRecovery: false,
        QuotaPresentationPolicy.AntigravityWeb, QuotaExportMode.None, TimeSpan.FromMinutes(AntigravityUsagePage.ManualMinimumMinutes),
        CancelLifetimeAsynchronously: true, CanSetMode: true, DependsOn: QuotaProvider.Gemini);

    public QuotaReadiness CheckReadiness() => hostReadiness?.Invoke() ?? QuotaReadiness.Ready;

    public async Task<QuotaReadResult> ReadAsync(Func<DateTimeOffset> utcNow, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        AntigravityUsagePageLoad load;
        try { load = await reader.LoadAntigravityAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { load = default; }
        cancellationToken.ThrowIfCancellationRequested();
        return AntigravityUsagePage.Parse(load, utcNow());
    }
}

internal sealed class UnavailableQuotaSource(QuotaProvider provider) : IQuotaSource
{
    public QuotaProvider Provider { get; } = provider;
    public IQuotaCacheAdapter Cache { get; } = QuotaCacheAdapter.Unavailable(provider);
    public QuotaSourceTraits Traits { get; } = new(new FixedSchedule(TimeSpan.Zero, stopOnLoginRequired: true),
        Timeout: null, EmitsObservations: false, DueCheckBeforeRead: true, StickyManualRefresh: false, RefreshOnRecovery: false,
        QuotaPresentationPolicy.Cli, QuotaExportMode.None);

    public Task<QuotaReadResult> ReadAsync(Func<DateTimeOffset> utcNow, TimeZoneInfo zone, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("An unavailable quota source cannot be read.");
}
