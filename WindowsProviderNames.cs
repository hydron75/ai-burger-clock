namespace AiBurgerClock;

// Windows callers keep this adapter; display names now come from the shared model.
internal static class WindowsProviderNames
{
    internal static string Provider(ProviderKind provider) => ProviderNames.Provider(provider);

    internal static string Provider(string provider) => ProviderNames.Provider(provider);
}
