using Microsoft.Win32;

namespace AiBurgerClock;

internal sealed class TestAccountQuotaClient(Func<DateTimeOffset> clock) : IAccountQuotaClient
{
    private int codexCalls;
    private int claudeCalls;
    internal bool FailClaude { get; set; }
    internal int Calls(QuotaProvider provider) => provider == QuotaProvider.Codex ? Volatile.Read(ref codexCalls) : Volatile.Read(ref claudeCalls);
    public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (provider == QuotaProvider.Codex) Interlocked.Increment(ref codexCalls);
        else Interlocked.Increment(ref claudeCalls);
        if (provider == QuotaProvider.Claude && FailClaude) throw new IOException("Synthetic offline condition");
        var now = clock();
        QuotaWindow[] windows = provider == QuotaProvider.Codex
            ? [new("session", "5시간", 18, now.AddHours(2), 300), new("weekly", "주간", 92, now.AddDays(2), 10080)]
            : [new("session", "5시간", 0, now.AddMinutes(12), 300), new("weekly", "주간", 45, now.AddDays(3), 10080), new("fable", "주간 · Fable", 1, now.AddDays(3), 10080)];
        return Task.FromResult(new QuotaReading(provider, windows));
    }
}

internal static class AccountQuotaUiChecks
{
    internal static async Task RunAsync(TrayApplicationContext context, TestAccountQuotaClient client,
        Func<DateTimeOffset> clock, string? reportDirectory)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine("PASS: " + label);
        }
        async Task Wait(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Quota UI condition");
                await Task.Delay(25);
            }
        }
        var monitor = context.QuotaMonitor!;
        await monitor.RefreshOnceAsync(QuotaProvider.Codex);
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        context.ShowWindow();
        var appearance = context.CurrentAppearance;
        var size = context.StatusWindow.ClientSize;
        context.StatusWindow.QuotaButton.PerformClick();
        Check(context.StatusWindow.QuotaView.Visible, "Quota toggle shows Work / Codex and Claude without enlarging the popup");
        var labels = context.StatusWindow.QuotaView.Controls.OfType<Label>().ToArray();
        Check(labels.Any(l => l.Text.Contains("Work / Codex")) && labels.Any(l => l.Text.Contains("Claude")) &&
            !labels.Any(l => l.Text.Contains("Gemini")), "Only requested account quota providers appear; no Gemini quota substitute");
        Check(labels.Any(l => l.Text.Contains("100%")) && labels.Any(l => l.Text.Contains("55%")) && labels.Any(l => l.Text.Contains("99%")),
            "UI converts consumed percentages into remaining percentages and retains model-scoped windows");
        Check(labels.Any(l => l.AccessibleDescription?.Contains("잔여 0%는 15분") == true),
            "Quota tooltip explains exhausted 15m polling separately from the 5m reset band");
        Check(labels.All(l => l.Height >= TextRenderer.MeasureText(l.Text, l.Font).Height), "Quota rows accommodate DPI-scaled text height");
        Check(context.StatusWindow.ClientSize == size && context.CurrentAppearance == appearance, "Quota values never change tray health or original window dimensions");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas.png", reportDirectory);

        client.FailClaude = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        context.RefreshStatus(false);
        Check(monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Claude).IsPrevious &&
            !monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Codex).IsPrevious, "One failed account keeps its previous values without downgrading the other account");
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text.Contains("Claude · 이전 조회값")),
            "Failed refresh explicitly labels the old quota instead of inventing zero or full balance");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-previous.png", reportDirectory);
        client.FailClaude = false;

        int before = client.Calls(QuotaProvider.Claude);
        context.StatusWindow.Controls.OfType<Button>().Single(b => b.Text == "Refresh").PerformClick();
        await Wait(() => client.Calls(QuotaProvider.Claude) > before && monitor.Snapshot().All(s => !s.IsRefreshing));
        Check(!monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Claude).IsPrevious, "Existing Refresh button recovers quota display");
        before = client.Calls(QuotaProvider.Claude);
        context.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));
        await Wait(() => client.Calls(QuotaProvider.Claude) > before && monitor.Snapshot().All(s => !s.IsRefreshing));
        Check(true, "Sleep resume queues one fresh official CLI quota read");

        var expired = new QuotaState(QuotaProvider.Codex, new(QuotaProvider.Codex,
            [new("expired", "5시간", 97, clock().AddSeconds(-1), 300)]), LastSuccessfulCheckUtc: clock());
        context.StatusWindow.UpdateQuotas([expired], clock());
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text.Contains("3% (이전)") && l.Text.Contains("갱신 대기")),
            "Elapsed reset countdown preserves observed balance until a new result arrives");
        var exhausted = new QuotaState(QuotaProvider.Claude, new(QuotaProvider.Claude,
            [new("weekly", "주간", 100, clock().AddDays(1), 10080)]),
            LastSuccessfulCheckUtc: clock(), NextCheckUtc: clock().AddMinutes(15));
        context.StatusWindow.UpdateQuotas([exhausted], clock());
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text.Contains("주간  0% 남음")),
            "Exhausted quota remains zero instead of inventing credit-based recovery");
        string nextExhaustedCheck = AgentSchedule.ToKst(exhausted.NextCheckUtc!.Value).ToString("MM-dd HH:mm");
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text.Contains("다음 " + nextExhaustedCheck)),
            "Quota metadata displays the exhausted provider's next 15m check");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-exhausted.png", reportDirectory);
        context.RefreshStatus(false);
        context.StatusWindow.QuotaButton.PerformClick();
        Check(!context.StatusWindow.QuotaView.Visible && context.StatusWindow.Controls.OfType<Panel>().Count(p => p.Visible) == 3,
            "Status toggle restores all three official status rows including Gemini");
        Check(context.StatusWindow.Controls.OfType<Label>().Any(l => l.Text.Contains("최근 조회 시도:")), "Status footer is restored immediately");
    }
}
