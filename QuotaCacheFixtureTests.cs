namespace AiBurgerClock;

// Seed/read fixture data including the retired agy key. Product code uses source
// adapters exclusively. These helpers have no host caller.
internal static class QuotaCacheFixture
{
    internal static Task SaveQuotaAsync(this UsageStore store, QuotaCache cache) =>
        store.WriteQuotaCacheAsync("AccountQuota.v1." + cache.Reading.Provider, cache, null);

    internal static Task<QuotaCache?> ReadQuotaAsync(this UsageStore store, QuotaProvider provider) =>
        store.ReadQuotaCacheAsync("AccountQuota.v1." + provider, provider, null);

    internal static Task SaveGeminiWebQuotaAsync(this UsageStore store, QuotaCache cache) =>
        store.WriteQuotaCacheAsync(QuotaCacheAdapter.GeminiWebKey, cache, QuotaCacheAdapter.ValidateGeminiWebCache);

    internal static Task<QuotaCache?> ReadGeminiWebQuotaAsync(this UsageStore store) =>
        QuotaCacheAdapter.ForGeminiWeb(store).ReadAsync(default);
}
