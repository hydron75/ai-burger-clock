namespace AiBurgerClock;

// Append new providers: cached v1 JSON stores the existing numeric identities.
internal enum QuotaProvider { Codex, Claude, Gemini, Xai, Antigravity }

internal static class QuotaProviders
{
    internal static IReadOnlyList<QuotaProvider> Boxes { get; } =
        Array.AsReadOnly(new[] { QuotaProvider.Codex, QuotaProvider.Claude, QuotaProvider.Gemini, QuotaProvider.Xai });
    internal static IReadOnlyList<QuotaProvider> All { get; } =
        Array.AsReadOnly(Boxes.Append(QuotaProvider.Antigravity).ToArray());
}

internal static class QuotaNames
{
    // Default CLI route and quota alert/export eligibility. Gemini web activation
    // is separate and deliberately does not change the enum or wire identity.
    public static bool HasCliRoute(QuotaProvider provider) =>
        provider is QuotaProvider.Codex or QuotaProvider.Claude or QuotaProvider.Xai;
    public const string GeminiUnavailableText = "조회 안 함 · Antigravity 정책에 따라 자동 조회하지 않음";

    public static string For(QuotaProvider provider) => provider switch
    {
        QuotaProvider.Codex => "Work / Codex",
        QuotaProvider.Claude => "Claude",
        QuotaProvider.Gemini => "Gemini",
        QuotaProvider.Xai => "xAI",
        QuotaProvider.Antigravity => "Antigravity",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}

// These are account quotas, not service-health observations or measured task results.
internal sealed record QuotaWindow(
    string Id, string Label, double UsedPercent, DateTimeOffset? ResetsAtUtc, int? WindowMinutes = null)
{
    public double RemainingPercent => 100 - UsedPercent;
}

internal sealed record QuotaReading(QuotaProvider Provider, IReadOnlyList<QuotaWindow> Windows);

internal interface IAccountQuotaClient
{
    Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken);
}
