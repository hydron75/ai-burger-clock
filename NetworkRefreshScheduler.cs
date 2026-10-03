namespace AiBurgerClock;

// Refresh after a network change; shared by the Windows and macOS hosts.
// Adapters (VPN, virtual switches, a second interface) can flap and each refresh starts both
// quota CLIs, so refreshes run at most once per minute. A short settle delay lets DHCP/DNS
// finish: on macOS a refresh started as soon as an address appeared failed its quota CLI call.
// A change during the wait only moves the pending refresh, so the final state is still refreshed.
internal sealed class NetworkRefreshScheduler : IDisposable
{
    internal static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(5);

    private readonly Action refresh;
    private readonly Func<long> ticks;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    // Only canceled, never disposed: a pending wait may still read its token.
    private readonly CancellationTokenSource lifetime = new();
    private readonly object sync = new();
    private long? lastRefreshTick;
    private long dueTick;
    private bool pending;

    internal NetworkRefreshScheduler(Action refresh, Func<long>? ticks = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.refresh = refresh;
        this.ticks = ticks ?? (() => Environment.TickCount64);
        this.delay = delay ?? Task.Delay;
    }

    // Thread-safe: NetworkChange raises its events off the UI thread.
    internal void OnNetworkAvailable()
    {
        lock (sync)
        {
            if (lifetime.IsCancellationRequested) return;
            long due = ticks() + (long)SettleDelay.TotalMilliseconds;
            if (lastRefreshTick is { } last)
                due = Math.Max(due, last + (long)MinimumInterval.TotalMilliseconds);
            dueTick = due;
            if (pending) return;
            pending = true;
        }
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                long wait;
                lock (sync)
                {
                    if (lifetime.IsCancellationRequested) return;
                    wait = dueTick - ticks();
                    if (wait <= 0)
                    {
                        pending = false;
                        lastRefreshTick = ticks();
                        break;
                    }
                }
                await delay(TimeSpan.FromMilliseconds(wait), lifetime.Token).ConfigureAwait(false);
            }
            refresh();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public void Dispose() => lifetime.Cancel();
}
