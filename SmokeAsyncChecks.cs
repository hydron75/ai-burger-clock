using Timer = System.Windows.Forms.Timer;

namespace AiBurgerClock;

// Windows smoke owns this guard, not the app's scheduling or polling policy.
internal sealed class SmokeStepGuard
{
    private int entered;
    internal bool TryEnter() => Interlocked.CompareExchange(ref entered, 1, 0) == 0;
    internal void Exit() => Volatile.Write(ref entered, 0);
}

internal static class SmokeAsyncChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await CheckStepSerializationAsync(check);
        await CheckGeminiCompletionGateAsync(check);
    }

    private static async Task CheckStepSerializationAsync(Action<bool, string> check)
    {
        using var timer = new Timer { Interval = 20 };
        var guard = new SmokeStepGuard();
        var initialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var skippedTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextStep = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int step = 0, inFlight = 0, peak = 0, completed = 0;
        timer.Tick += async (_, _) =>
        {
            if (!guard.TryEnter()) { skippedTick.TrySetResult(); return; }
            inFlight++;
            peak = Math.Max(peak, inFlight);
            try
            {
                switch (step++)
                {
                    case 0:
                        firstEntered.TrySetResult();
                        await initialization.Task;
                        completed++;
                        break;
                    default:
                        timer.Stop();
                        nextStep.TrySetResult();
                        break;
                }
            }
            catch (Exception error) { nextStep.TrySetException(error); }
            finally { inFlight--; guard.Exit(); }
        };
        try
        {
            timer.Start();
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await skippedTick.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(step == 1 && completed == 0 && inFlight == 1 && !nextStep.Task.IsCompleted,
                "Smoke step guard blocks a second Windows timer tick while initialization is held");
            initialization.SetResult();
            await nextStep.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(step == 2 && completed == 1 && peak == 1,
                "Smoke advances exactly once after initialization without overlapping callbacks");
            check(guard.TryEnter(), "Smoke step guard is reusable after async completion");
            guard.Exit();
            try
            {
                if (!guard.TryEnter()) throw new InvalidOperationException("Synthetic smoke guard setup");
                throw new IOException("Synthetic smoke callback failure");
            }
            catch (IOException) { }
            finally { guard.Exit(); }
            check(guard.TryEnter(), "Smoke step guard is released by the callback failure finally path");
            guard.Exit();
        }
        finally { timer.Stop(); initialization.TrySetResult(); }
    }

    private static async Task CheckGeminiCompletionGateAsync(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(9));
        var responseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TestAccountQuotaClient(() => now) { NextGeminiResponseGate = responseGate };
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
        using var cancellation = new CancellationTokenSource();
        Task response = monitor.RefreshOnceAsync(QuotaProvider.Gemini, cancellation.Token);
        using var heartbeat = new Timer { Interval = 20 };
        var uiContinuation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.Tick += (_, _) => { heartbeat.Stop(); uiContinuation.TrySetResult(); };
        try
        {
            check(!response.IsCompleted && monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing,
                "Synthetic Gemini response remains pending until its explicit completion gate is released");
            heartbeat.Start();
            await uiContinuation.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(!response.IsCompleted && !responseGate.Task.IsCompleted &&
                monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing,
                "A delayed Windows timer continuation cannot accidentally complete the held Gemini response");
            responseGate.SetResult();
            await response.WaitAsync(TimeSpan.FromSeconds(5));
            check(!monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing &&
                !monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsPrevious && client.Calls(QuotaProvider.Gemini) == 1,
                "Explicit gate release accepts exactly one successful Gemini response");
            await monitor.RefreshOnceAsync(QuotaProvider.Gemini, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
            check(client.Calls(QuotaProvider.Gemini) == 2 && !monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing,
                "Synthetic response gate is consumed once and does not delay the next Gemini read");
        }
        finally
        {
            heartbeat.Stop();
            responseGate.TrySetResult();
            await cancellation.CancelAsync();
            await response.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
