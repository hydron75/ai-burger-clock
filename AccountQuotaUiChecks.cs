using Microsoft.Win32;
using System.Globalization;

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
        Check(context.StatusWindow.QuotaView.Visible, "Quota toggle shows ChatGPT Work/Codex and Claude without enlarging the popup");
        var labels = context.StatusWindow.QuotaView.Controls.OfType<Label>().ToArray();
        Check(labels.Any(l => l.Text == "ChatGPT") && labels.Any(l => l.Text == "Work/Codex") && labels.Any(l => l.Text.Contains("Claude")) &&
            !labels.Any(l => l.Text.Contains("Gemini")), "Only requested account quota providers appear; no Gemini quota substitute");
        var chatGptHeading = labels.Single(l => l.Text == "ChatGPT");
        var scope = labels.Single(l => l.Text == "Work/Codex");
        Check(scope.Top >= chatGptHeading.Bottom && labels.Any(l => l.Top >= scope.Bottom && l.Text.StartsWith("5시간", StringComparison.Ordinal)),
            "ChatGPT quota heading has Work/Codex on a separate line above its limits");
        Check(chatGptHeading.ForeColor == SystemColors.ControlText && scope.ForeColor == Color.DimGray,
            "Shared Normal and Muted quota tones retain Windows heading and scope colors");
        Check(labels.Single(l => l.Text.StartsWith("5시간  82%", StringComparison.Ordinal)).ForeColor == Color.FromArgb(25, 115, 75) &&
            labels.Single(l => l.Text.StartsWith("주간  8%", StringComparison.Ordinal)).ForeColor == Color.DarkOrange,
            "Shared Good and Caution quota tones retain Windows remaining-balance colors");
        Check(labels.All(l => l.Bottom <= context.StatusWindow.QuotaView.ClientSize.Height) &&
            !context.StatusWindow.QuotaView.VerticalScroll.Visible,
            "Standard Codex two-window and Claude three-window quotas fit the existing panel without scrolling");
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
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Single(l => l.Text.StartsWith("5시간  100% (이전)", StringComparison.Ordinal)).ForeColor == Color.DimGray,
            "Previous quota values keep the Windows muted color");
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
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Single(l => l.Text.Contains("3% (이전)")).ForeColor == Color.DimGray,
            "Elapsed reset quota rows use the shared Muted tone with the Windows palette");
        var exhausted = new QuotaState(QuotaProvider.Claude, new(QuotaProvider.Claude,
            [new("weekly", "주간", 100, clock().AddDays(1), 10080)]),
            LastSuccessfulCheckUtc: clock(), NextCheckUtc: clock().AddMinutes(15));
        context.StatusWindow.UpdateQuotas([exhausted], clock());
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text.Contains("주간  0% 남음")),
            "Exhausted quota remains zero instead of inventing credit-based recovery");
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Single(l => l.Text.StartsWith("주간  0% 남음", StringComparison.Ordinal)).ForeColor == Color.Firebrick,
            "Exhausted quota rows retain the Windows Danger color");
        string nextExhaustedCheck = AgentSchedule.ToKst(exhausted.NextCheckUtc!.Value).ToString("MM-dd HH:mm");
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text.Contains("다음 " + nextExhaustedCheck)),
            "Quota metadata displays the exhausted provider's next 15m check");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-exhausted.png", reportDirectory);
        context.RefreshStatus(false);
        context.StatusWindow.QuotaButton.PerformClick();
        Check(!context.StatusWindow.QuotaView.Visible && context.StatusWindow.Controls.OfType<Panel>().Count(p => p.Visible) == 3,
            "Status toggle restores all three official status rows including Gemini");
        Check(context.StatusWindow.Controls.OfType<Label>().Any(l => l.Text.Contains("최근 조회 시도:")), "Status footer is restored immediately");

        var previousCodex = monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Codex) with { IsPrevious = true, Error = "Synthetic offline condition" };
        context.StatusWindow.UpdateQuotas([previousCodex], clock());
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text == "ChatGPT · 이전 조회값") &&
            context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text == "Work/Codex"),
            "Previous Codex values keep the ChatGPT heading and Work/Codex scope");
        context.StatusWindow.UpdateQuotas([previousCodex with { IsRefreshing = true }], clock());
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text == "ChatGPT · 확인 중") &&
            context.StatusWindow.QuotaView.Controls.OfType<Label>().Any(l => l.Text == "Work/Codex"),
            "Refreshing Codex values keep the ChatGPT heading and Work/Codex scope");

        context.StatusWindow.UpdateQuotas([previousCodex with { CacheError = "Synthetic cache error" }], clock());
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Single(l => l.Text.StartsWith("성공 ", StringComparison.Ordinal)).ForeColor == Color.Firebrick,
            "Shared cache-error metadata keeps the Windows Danger color");
        context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Claude)], clock());
        Check(context.StatusWindow.QuotaView.Controls.OfType<Label>().Single(l => l.Text == "한도 조회 대기 · 공식 CLI 로그인 필요").ForeColor == Color.DimGray,
            "Shared pre-reading placeholder keeps its text and Windows muted color");

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Claude,
                new QuotaReading(QuotaProvider.Claude, [new QuotaWindow("culture", "주간", 12.34, null)]))], clock());
            var cultureRow = context.StatusWindow.QuotaView.Controls.OfType<Label>().Single(l => l.Text.StartsWith("주간  ", StringComparison.Ordinal));
            Check(cultureRow.Text.Contains("87.7%") && cultureRow.AccessibleDescription?.Contains("사용 12.3% / 잔여 87.7%") == true,
                "Windows quota row and tooltip use the shared invariant percentage format in de-DE");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        context.RefreshStatus(false);
    }
}
