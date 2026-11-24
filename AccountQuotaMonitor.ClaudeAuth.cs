namespace AiBurgerClock;

internal sealed partial class AccountQuotaMonitor
{
    private readonly IClaudeAuthClient? claudeAuthClient;
    private readonly SemaphoreSlim claudeAuthWake = new(0, 1);
    private enum ClaudeAuthRequest { None, QuotaFailure, Immediate }
    private ClaudeAuthRequest pendingClaudeAuthRequest;
    private ClaudeAuthCheck? latestClaudeAuth;
    private ClaudeAuthCheck? lastClaudeAuthRecoveryRequest;
    internal ClaudeAuthCheck? LatestClaudeAuth => Volatile.Read(ref latestClaudeAuth);

    private void RequestClaudeAuthAfterQuota(QuotaObservation? success)
    {
        lock (sync)
        {
            if (success is null)
            {
                QueueClaudeAuthCheck(ClaudeAuthRequest.QuotaFailure);
                return;
            }
            var auth = LatestClaudeAuth;
            if (auth is null || ReferenceEquals(auth, lastClaudeAuthRecoveryRequest) ||
                (auth.Status.Outcome != ClaudeAuthOutcome.Unknown && !auth.RequiresLogin(success.CheckedAtUtc))) return;
            lastClaudeAuthRecoveryRequest = auth;
            QueueClaudeAuthCheck(ClaudeAuthRequest.Immediate);
        }
    }

    // Callers hold sync. A throttled failure must not replace a pending immediate check.
    private void QueueClaudeAuthCheck(ClaudeAuthRequest request)
    {
        if (claudeAuthClient is null || disposed || lifetime.IsCancellationRequested) return;
        if (request > pendingClaudeAuthRequest) pendingClaudeAuthRequest = request;
        if (claudeAuthWake.CurrentCount == 0) claudeAuthWake.Release();
    }

    private async Task RunClaudeAuthAsync()
    {
        CancellationToken cancellationToken = lifetime.Token;
        DateTimeOffset? lastAttemptUtc = null;
        var request = ClaudeAuthRequest.None;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = utcNow().ToUniversalTime();
                if (request == ClaudeAuthRequest.Immediate ||
                    ClaudeAuthCheck.IsDue(now, lastAttemptUtc, request == ClaudeAuthRequest.QuotaFailure))
                {
                    lastAttemptUtc = now;
                    request = ClaudeAuthRequest.None;
                    ClaudeAuthStatus status;
                    try { status = await claudeAuthClient!.ReadClaudeAuthAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) { status = ClaudeAuthStatus.Unknown; }
                    cancellationToken.ThrowIfCancellationRequested();
                    // A CLI check can see the credentials at launch. A quota success during
                    // that check is newer evidence even if the CLI returns after the quota.
                    Volatile.Write(ref latestClaudeAuth, new(status, now));
                    RaiseChanged();
                    continue;
                }
                TimeSpan delay = ClaudeAuthCheck.PollInterval - (now - lastAttemptUtc!.Value);
                bool signaled = await claudeAuthWake.WaitAsync(
                    delay <= ClaudeAuthCheck.PollInterval ? delay : ClaudeAuthCheck.PollInterval, cancellationToken).ConfigureAwait(false);
                lock (sync)
                {
                    request = signaled ? pendingClaudeAuthRequest : ClaudeAuthRequest.None;
                    if (signaled) pendingClaudeAuthRequest = ClaudeAuthRequest.None;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
