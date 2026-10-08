namespace AiBurgerClock;

// Display names shared by every host; provider identities, stored values and official URLs stay unchanged.
internal static class ProviderNames
{
    internal static string Provider(ProviderKind provider) => Provider(provider.ToString());

    internal static string Provider(string provider) =>
        provider == nameof(ProviderKind.OpenAI) ? "ChatGPT" : provider;

    // The Codex account quota is shown under the ChatGPT product name (scope line: Work/Codex).
    internal static string Quota(QuotaProvider provider) =>
        provider == QuotaProvider.Codex ? "ChatGPT" : QuotaNames.For(provider);
}
