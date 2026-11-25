namespace AiBurgerClock;

internal enum QuotaFailureKind { LoginRequired, NotInstalled, NotPermitted, Timeout, Unreachable, Rejected, FormatChanged }

internal sealed record QuotaFailure(QuotaFailureKind Kind, string Code);

// The existing client contract returns a reading or throws. Carry only a
// classified failure across that boundary; never attach the original exception.
internal sealed class QuotaReadException(QuotaFailureKind kind, string code) : Exception(code)
{
    internal QuotaFailure Failure { get; } = new(kind, code);
}
