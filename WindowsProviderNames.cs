namespace AiBurgerClock;

// Windows presentation only; provider identities, stored values and official URLs stay unchanged.
internal static class WindowsProviderNames
{
    internal static string Provider(ProviderKind provider) => Provider(provider.ToString());

    internal static string Provider(string provider) =>
        provider == nameof(ProviderKind.OpenAI) ? "ChatGPT" : provider;
}
