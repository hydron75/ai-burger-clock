namespace AiBurgerClock;

internal sealed partial class AccountQuotaMonitor
{
    private readonly IClaudeAuthClient? claudeAuthClient;
    private readonly SemaphoreSlim claudeAuthWake = new(0, 1);
    private ClaudeAuthCheck? latestClaudeAuth;
    internal ClaudeAuthCheck? LatestClaudeAuth => Volatile.Read(ref latestClaudeAuth);

    private void RequestClaudeAuthAfterFailure()
    {
        lock (sync)
            if (claudeAuthClient is not null && !disposed && !lifetime.IsCancellationRequested && claudeAuthWake.CurrentCount == 0)
                claudeAuthWake.Release();
    }

    private async Task RunClaudeAuthAsync()
    {
        CancellationToken cancellationToken = lifetime.Token;
        DateTimeOffset? lastAttemptUtc = null;
        bool afterQuotaFailure = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = utcNow().ToUniversalTime();
                if (ClaudeAuthCheck.IsDue(now, lastAttemptUtc, afterQuotaFailure))
                {
                    lastAttemptUtc = now;
                    ClaudeAuthStatus status;
                    try { status = await claudeAuthClient!.ReadClaudeAuthAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) { status = ClaudeAuthStatus.Unknown; }
                    cancellationToken.ThrowIfCancellationRequested();
                    Volatile.Write(ref latestClaudeAuth, new(status, utcNow().ToUniversalTime()));
                    RaiseChanged();
                    continue;
                }
                TimeSpan delay = ClaudeAuthCheck.PollInterval - (now - lastAttemptUtc!.Value);
                afterQuotaFailure = await claudeAuthWake.WaitAsync(
                    delay <= ClaudeAuthCheck.PollInterval ? delay : ClaudeAuthCheck.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
