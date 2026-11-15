namespace AiBurgerClock;

// Append new providers: cached v1 JSON stores the existing numeric identities.
internal enum QuotaProvider { Codex, Claude, Gemini, Xai }

internal static class QuotaNames
{
    // Fixed policy, not a user setting. Keep the enum/cache/wire identity for compatibility.
    public static bool IsQueryEnabled(QuotaProvider provider) => provider != QuotaProvider.Gemini;
    public const string GeminiUnavailableText = "조회 안 함 · Antigravity 정책에 따라 자동 조회하지 않음";

    public static string For(QuotaProvider provider) => provider switch
    {
        QuotaProvider.Codex => "Work / Codex",
        QuotaProvider.Claude => "Claude",
        QuotaProvider.Gemini => "Gemini",
        QuotaProvider.Xai => "xAI",
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
