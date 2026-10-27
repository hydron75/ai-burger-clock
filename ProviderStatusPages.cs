using System.Diagnostics;

namespace AiBurgerClock;

internal static class ProviderStatusPages
{
    // Fixed HTTPS destinations, never an untrusted URL supplied by a status feed or user note.
    internal static Uri For(ProviderKind provider) => new(provider switch
    {
        ProviderKind.OpenAI => "https://status.openai.com/",
        ProviderKind.Claude => "https://status.claude.com/",
        ProviderKind.Gemini => "https://www.google.com/appsstatus/dashboard/",
        ProviderKind.Xai => "https://status.x.ai/",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    });

    internal const string XaiUsageLabel = "사용량 보기";
    internal static Uri XaiUsage => new("https://grok.com/dashboard");

    internal static void Open(Uri uri)
    {
        using var browser = Process.Start(StartInfo(uri));
    }

    internal static ProcessStartInfo StartInfo(Uri uri) => new(uri.AbsoluteUri) { UseShellExecute = true };
}
