using System.Text.Json;

namespace AiBurgerClock;

internal interface IClaudeAuthClient
{
    Task<ClaudeAuthStatus> ReadClaudeAuthAsync(CancellationToken cancellationToken);
}

internal enum ClaudeAuthOutcome { Unknown, SignedOut, SignedIn }

// Only the documented discriminator and the normalized upper bound leave the parser.
internal sealed record ClaudeAuthStatus(ClaudeAuthOutcome Outcome, string? AuthMethod = null,
    DateTimeOffset? LoginExpiresAtUtc = null)
{
    internal static readonly ClaudeAuthStatus Unknown = new(ClaudeAuthOutcome.Unknown);
    private static readonly ClaudeAuthStatus SignedOut = new(ClaudeAuthOutcome.SignedOut);

    internal static ClaudeAuthStatus Parse(int exitCode, string json)
    {
        if (exitCode == 1) return SignedOut;
        if (exitCode != 0) return Unknown;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unknown;

            JsonElement loggedIn = default, method = default, expiresAt = default;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "loggedIn":
                        if (loggedIn.ValueKind != JsonValueKind.Undefined) return Unknown;
                        loggedIn = property.Value;
                        break;
                    case "authMethod":
                        if (method.ValueKind != JsonValueKind.Undefined) return Unknown;
                        method = property.Value;
                        break;
                    case "loginExpiresAt":
                        if (expiresAt.ValueKind != JsonValueKind.Undefined) return Unknown;
                        expiresAt = property.Value;
                        break;
                }
            }
            if (loggedIn.ValueKind == JsonValueKind.False) return SignedOut;
            if (loggedIn.ValueKind != JsonValueKind.True || method.ValueKind != JsonValueKind.String) return Unknown;
            string? authMethod = method.GetString();
            if (authMethod is not ("claude.ai" or "oauth_token" or "api_key" or "api_key_helper" or "third_party"))
                return Unknown;
            if (expiresAt.ValueKind == JsonValueKind.Null ||
                (expiresAt.ValueKind == JsonValueKind.Undefined && authMethod != "claude.ai"))
                return new(ClaudeAuthOutcome.SignedIn, authMethod);
            if (expiresAt.ValueKind != JsonValueKind.String || !expiresAt.TryGetDateTimeOffset(out var expiry)) return Unknown;
            string timestamp = expiresAt.GetString()!;
            if (!timestamp.EndsWith('Z') && !(timestamp.Length >= 6 && timestamp[^6] is '+' or '-')) return Unknown;
            // The end date belongs to the stored subscription login, not to an active API key or gateway.
            return new(ClaudeAuthOutcome.SignedIn, authMethod, authMethod == "claude.ai" ? expiry.ToUniversalTime() : null);
        }
        catch (JsonException) { return Unknown; }
    }
}

internal sealed record ClaudeAuthCheck(ClaudeAuthStatus Status, DateTimeOffset CheckedAtUtc)
{
    internal const string LoginRequiredText = "Claude CLI 로그인 필요 · 터미널에서 claude auth login";
    internal const string ExpiringText = "로그인 만료 임박 · 터미널에서 claude auth login";
    internal const string UnknownText = "로그인 상태 확인 불가";
    internal const string AlertTitle = "Claude CLI 로그인 만료 임박";
    internal const string MenuLabel = "Claude CLI 로그인 만료 알림";
    internal const int WarningLeadDays = 7;
    internal static readonly TimeSpan WarningLeadTime = TimeSpan.FromDays(WarningLeadDays);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);
    internal static readonly TimeSpan FailureMinimumInterval = TimeSpan.FromMinutes(15);

    internal static bool IsDue(DateTimeOffset now, DateTimeOffset? lastAttemptUtc, bool afterQuotaFailure) =>
        lastAttemptUtc is null || now - lastAttemptUtc >= (afterQuotaFailure ? FailureMinimumInterval : PollInterval);

    internal bool RequiresLogin(DateTimeOffset now) =>
        Status.Outcome == ClaudeAuthOutcome.SignedOut || Status.LoginExpiresAtUtc <= now;

    internal DateTimeOffset? WarningExpiry(DateTimeOffset now, DateTimeOffset? alreadyWarned = null) =>
        Status is { Outcome: ClaudeAuthOutcome.SignedIn, AuthMethod: "claude.ai", LoginExpiresAtUtc: { } expiry } &&
        expiry != alreadyWarned && expiry > now &&
        expiry - now <= WarningLeadTime ? expiry : null;
}
