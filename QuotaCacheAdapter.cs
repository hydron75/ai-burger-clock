namespace AiBurgerClock;

internal interface IQuotaCacheAdapter
{
    Task<QuotaCache?> ReadAsync(CancellationToken cancellationToken);
    Task WriteAsync(QuotaReading reading, DateTimeOffset at, IReadOnlyList<DateTimeOffset> anchors, CancellationToken cancellationToken);
    Task DeleteAsync(CancellationToken cancellationToken);
    bool RestoreOnStart(QuotaSourceMode mode);
}

internal sealed class QuotaCacheAdapter(UsageStore? store, string key, QuotaProvider provider,
    Action<QuotaCache>? validate = null) : IQuotaCacheAdapter
{
    internal const string GeminiWebKey = "AccountQuota.v1.GeminiWeb";

    internal static QuotaCacheAdapter ForCli(UsageStore? store, QuotaProvider provider)
    {
        if (!QuotaNames.HasCliRoute(provider)) throw new ArgumentOutOfRangeException(nameof(provider));
        return new(store, "AccountQuota.v1." + provider, provider);
    }

    internal static QuotaCacheAdapter ForGeminiWeb(UsageStore? store) =>
        new(store, GeminiWebKey, QuotaProvider.Gemini, ValidateGeminiWebCache);

    internal static QuotaCacheAdapter Unavailable(QuotaProvider provider) => new(null, "", provider);

    public bool RestoreOnStart(QuotaSourceMode mode) => store is not null && mode == QuotaSourceMode.Active;

    public Task<QuotaCache?> ReadAsync(CancellationToken cancellationToken) =>
        store?.ReadQuotaCacheAsync(key, provider, validate, cancellationToken) ?? Task.FromResult<QuotaCache?>(null);

    public Task WriteAsync(QuotaReading reading, DateTimeOffset at, IReadOnlyList<DateTimeOffset> anchors, CancellationToken cancellationToken) =>
        store?.WriteQuotaCacheAsync(key, new(1, reading, at, anchors), validate, cancellationToken) ?? Task.CompletedTask;

    public Task DeleteAsync(CancellationToken cancellationToken) =>
        store?.DeleteQuotaCacheAsync(key, cancellationToken) ?? Task.CompletedTask;

    internal static void ValidateGeminiWebCache(QuotaCache cache)
    {
        if (!GeminiUsagePage.IsWebReading(cache.Reading) || cache.ResetAnchors.Count != 0 || cache.ResetAnchorPolicyVersion != 0)
            throw new InvalidDataException("Invalid Gemini web quota cache.");
    }
}
