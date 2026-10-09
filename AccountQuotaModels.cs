namespace AiBurgerClock;

// Append new providers: cached v1 JSON stores the existing numeric identities.
internal enum QuotaProvider { Codex, Claude, Gemini }

internal static class QuotaNames
{
    public static string For(QuotaProvider provider) => provider switch
    {
        QuotaProvider.Codex => "Work / Codex",
        QuotaProvider.Claude => "Claude",
        QuotaProvider.Gemini => "Gemini",
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
