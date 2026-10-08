using Microsoft.Win32;
using System.Globalization;

namespace AiBurgerClock;

internal sealed class TestAccountQuotaClient(Func<DateTimeOffset> clock) : IAccountQuotaClient
{
    private int codexCalls;
    private int claudeCalls;
    private int geminiCalls;
    internal bool FailClaude { get; set; }
    internal bool FailGemini { get; set; }
    internal int Calls(QuotaProvider provider) => provider switch
    {
        QuotaProvider.Codex => Volatile.Read(ref codexCalls),
        QuotaProvider.Claude => Volatile.Read(ref claudeCalls),
        QuotaProvider.Gemini => Volatile.Read(ref geminiCalls),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
    public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (provider)
        {
            case QuotaProvider.Codex: Interlocked.Increment(ref codexCalls); break;
            case QuotaProvider.Claude: Interlocked.Increment(ref claudeCalls); break;
            case QuotaProvider.Gemini: Interlocked.Increment(ref geminiCalls); break;
            default: throw new ArgumentOutOfRangeException(nameof(provider));
        }
        if (provider == QuotaProvider.Claude && FailClaude) throw new IOException("Synthetic offline condition");
        if (provider == QuotaProvider.Gemini && FailGemini) throw new IOException("Synthetic Gemini offline condition");
        var now = clock();
        QuotaWindow[] windows = provider switch
        {
            QuotaProvider.Codex => [new("session", "5시간", 18, now.AddHours(2), 300), new("weekly", "주간", 92, now.AddDays(2), 10080)],
            QuotaProvider.Claude => [new("session", "5시간", 0, now.AddMinutes(12), 300), new("weekly", "주간", 45, now.AddDays(3), 10080), new("fable", "주간 · Fable", 1, now.AddDays(3), 10080)],
            QuotaProvider.Gemini => [new("gemini-5h", "5시간", 2.01, now.AddHours(3), 300), new("gemini-weekly", "주간", 0.34, now.AddDays(6), 10080)],
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
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
        var quotaView = context.StatusWindow.QuotaView;
        Panel Card(QuotaProvider provider) => quotaView.Controls.OfType<Panel>().Single(p => Equals(p.Tag, provider));
        Label[] ProviderLabels(QuotaProvider provider) => Card(provider).Controls.OfType<Label>().ToArray();
        Label[] AllQuotaLabels() => quotaView.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<Label>()).ToArray();
        foreach (var provider in Enum.GetValues<QuotaProvider>()) await monitor.RefreshOnceAsync(provider);
        context.ShowWindow();
        var appearance = context.CurrentAppearance;
        var size = context.StatusWindow.ClientSize;
        context.StatusWindow.QuotaButton.PerformClick();
        Check(quotaView.Visible, "Quota toggle shows ChatGPT, Claude and Antigravity Gemini without enlarging the popup");
        var labels = AllQuotaLabels();
        Check(labels.Any(l => l.Text == "ChatGPT") && labels.Any(l => l.Text == "Work/Codex") && labels.Any(l => l.Text.Contains("Claude")) &&
            labels.Any(l => l.Text == "Gemini") && labels.Any(l => l.Text == "Antigravity · Gemini 모델"),
            "Gemini quota explicitly identifies Antigravity Gemini models, not Gemini Apps or third-party models");
        Check(quotaView.Controls.OfType<Panel>().Select(p => (QuotaProvider)p.Tag!).SequenceEqual(Enum.GetValues<QuotaProvider>()),
            "Each account quota has its own box in the shared provider order");
        var chatGptHeading = labels.Single(l => l.Text == "ChatGPT");
        var scope = labels.Single(l => l.Text == "Work/Codex");
        Check(scope.Top >= chatGptHeading.Bottom && ProviderLabels(QuotaProvider.Codex).Any(l => l.Top >= scope.Bottom && l.Text.StartsWith("5시간", StringComparison.Ordinal)),
            "ChatGPT quota heading has Work/Codex on a separate line above its limits");
        Check(chatGptHeading.ForeColor == SystemColors.ControlText && scope.ForeColor == Color.DimGray,
            "Shared Normal and Muted quota tones retain Windows heading and scope colors");
        Check(labels.Single(l => l.Text.StartsWith("5시간  82%", StringComparison.Ordinal)).ForeColor == Color.FromArgb(25, 115, 75) &&
            labels.Single(l => l.Text.StartsWith("주간  8%", StringComparison.Ordinal)).ForeColor == Color.DarkOrange,
            "Shared Good and Caution quota tones retain Windows remaining-balance colors");
        Check(quotaView.VerticalScroll.Visible && !quotaView.HorizontalScroll.Visible,
            "Three account quota boxes scroll vertically inside the existing panel without horizontal overflow");
        var geminiLabels = ProviderLabels(QuotaProvider.Gemini);
        Check(geminiLabels.Length == 5 && geminiLabels.Count(l => l.Text.StartsWith("5시간  ", StringComparison.Ordinal)) == 1 &&
            geminiLabels.Count(l => l.Text.StartsWith("주간  ", StringComparison.Ordinal)) == 1,
            "Antigravity Gemini displays exactly its 5-hour and weekly buckets with heading, scope and metadata");
        Check(geminiLabels.Any(l => l.Text.StartsWith("5시간  98%", StringComparison.Ordinal)) &&
            geminiLabels.Any(l => l.Text.StartsWith("주간  99.7%", StringComparison.Ordinal)),
            "Gemini fractional balances retain the shared percentage formatting rather than rounded CLI text");
        Check(labels.Any(l => l.Text.Contains("100%")) && labels.Any(l => l.Text.Contains("55%")) && labels.Any(l => l.Text.Contains("99%")),
            "UI converts consumed percentages into remaining percentages and retains model-scoped windows");
        Check(labels.Any(l => l.AccessibleDescription?.Contains("잔여 0%는 15분") == true),
            "Quota tooltip explains exhausted 15m polling separately from the 5m reset band");
        Check(labels.All(l => l.Height >= TextRenderer.MeasureText(l.Text, l.Font).Height), "Quota rows accommodate DPI-scaled text height");
        var cards = quotaView.Controls.OfType<Panel>().ToArray();
        Check(cards.All(p => p.Controls.Cast<Control>().All(l => p.ClientRectangle.Contains(l.Bounds))),
            "Quota card padding contains every DPI-scaled row including its last metadata line");
        Check(cards.All(p => p.Left == 0 && p.Right <= quotaView.ClientSize.Width && p.BackColor == Color.White) &&
            cards.Zip(cards.Skip(1)).All(pair => pair.First.Bottom < pair.Second.Top),
            "Quota provider boxes use the white status-card shape, fit the viewport and stay separated");
        var sectionTitle = quotaView.Controls.OfType<Label>().Single(l => l.Text == QuotaPanelModel.AccessibleName);
        var mainTitle = context.StatusWindow.Controls.OfType<Label>().Single(l => l.Text == StatusPanelModel.Title);
        Check(sectionTitle.PointToScreen(Point.Empty).X == mainTitle.PointToScreen(Point.Empty).X,
            "Account quota section title starts on the same outside baseline as AI AGENT TRAFFIC");
        var statusCard = context.StatusWindow.Controls.OfType<Panel>().First(p => p != quotaView);
        int insideX = statusCard.Controls.OfType<Label>().First().PointToScreen(Point.Empty).X;
        Check(labels.All(l => l.PointToScreen(Point.Empty).X == insideX),
            "Every quota box label uses the same inside baseline as the official status cards");
        var surfaces = cards.SelectMany(p => new[] { (Control)p }.Concat(p.Controls.Cast<Control>())).ToArray();
        Check(surfaces.All(c => c.Cursor == Cursors.Default && c.ContextMenuStrip is null),
            "Non-clickable quota boxes have no hand cursor or recording menu");
        int quotaClicks = 0;
        void StatusClick(ProviderKind _) => quotaClicks++;
        void RecordingClick(ProviderKind _, UsageEventType __, bool ___) => quotaClicks++;
        context.StatusWindow.StatusPageRequested += StatusClick;
        context.StatusWindow.RecordRequested += RecordingClick;
        try
        {
            var enter = typeof(Control).GetMethod("OnMouseEnter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var leave = typeof(Control).GetMethod("OnMouseLeave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var click = typeof(Control).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            foreach (var surface in surfaces)
            {
                enter.Invoke(surface, [EventArgs.Empty]);
                click.Invoke(surface, [new MouseEventArgs(MouseButtons.Left, 1, 4, 4, 0)]);
                click.Invoke(surface, [new MouseEventArgs(MouseButtons.Right, 1, 4, 4, 0)]);
                leave.Invoke(surface, [EventArgs.Empty]);
            }
        }
        finally
        {
            context.StatusWindow.StatusPageRequested -= StatusClick;
            context.StatusWindow.RecordRequested -= RecordingClick;
        }
        Check(quotaClicks == 0 && surfaces.All(c => c.BackColor == Color.White),
            "Quota boxes never change on hover or route clicks to status pages or recording");
        Check(context.StatusWindow.ClientSize == size && context.CurrentAppearance == appearance, "Quota values never change tray health or original window dimensions");
        quotaView.AutoScrollPosition = Point.Empty;
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas.png", reportDirectory);

        quotaView.ScrollControlIntoView(Card(QuotaProvider.Gemini));
        var lastMetadata = geminiLabels.Single(l => l.Name == "QuotaMetadata");
        Check(quotaView.RectangleToScreen(quotaView.ClientRectangle).Contains(lastMetadata.RectangleToScreen(lastMetadata.ClientRectangle)),
            "Scrolling the quota area reaches the Gemini account's last metadata line");
        Check(context.StatusWindow.ClientSize == size && context.StatusWindow.Controls.OfType<Button>().All(b => b.Visible),
            "Scrolling account quotas never moves the window controls or changes its size");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-gemini.png", reportDirectory);
        var scroll = quotaView.AutoScrollPosition;
        var oldCards = quotaView.Controls.OfType<Panel>().ToArray();
        var oldLabels = AllQuotaLabels();
        context.StatusWindow.UpdateQuotas(monitor.Snapshot(), clock().AddSeconds(1));
        Check(quotaView.AutoScrollPosition == scroll && oldCards.SequenceEqual(quotaView.Controls.OfType<Panel>()) &&
            oldLabels.SequenceEqual(AllQuotaLabels()),
            "Countdown updates preserve scroll position and existing quota card/label instances");
        quotaView.AutoScrollPosition = Point.Empty;

        client.FailClaude = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        context.RefreshStatus(false);
        Check(monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Claude).IsPrevious &&
            monitor.Snapshot().Where(s => s.Provider != QuotaProvider.Claude).All(s => !s.IsPrevious),
            "One failed account keeps its previous values without downgrading ChatGPT or Gemini");
        Check(ProviderLabels(QuotaProvider.Claude).Any(l => l.Text.Contains("Claude · 이전 조회값")),
            "Failed refresh explicitly labels the old quota instead of inventing zero or full balance");
        Check(ProviderLabels(QuotaProvider.Claude).Single(l => l.Text.StartsWith("5시간  100% (이전)", StringComparison.Ordinal)).ForeColor == Color.DimGray,
            "Previous quota values keep the Windows muted color");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-previous.png", reportDirectory);
        client.FailClaude = false;
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);

        client.FailGemini = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        context.RefreshStatus(false);
        Check(monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsPrevious &&
            monitor.Snapshot().Where(s => s.Provider != QuotaProvider.Gemini).All(s => !s.IsPrevious),
            "A failed Antigravity query preserves only Gemini's old values without affecting other accounts");
        Check(ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == "Gemini · 이전 조회값") &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == "Antigravity · Gemini 모델") &&
            ProviderLabels(QuotaProvider.Gemini).Single(l => l.Text.StartsWith("5시간  98% (이전)", StringComparison.Ordinal)).ForeColor == Color.DimGray,
            "Old Gemini values keep their Antigravity scope and muted balance rather than inventing a full reset");
        quotaView.ScrollControlIntoView(Card(QuotaProvider.Gemini));
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-gemini-previous.png", reportDirectory);
        client.FailGemini = false;

        var before = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, client.Calls);
        context.ShowWindow();
        context.StatusWindow.Controls.OfType<Button>().Single(b => b.Text == "Refresh").PerformClick();
        await Wait(() => before.All(pair => client.Calls(pair.Key) > pair.Value) && monitor.Snapshot().All(s => !s.IsRefreshing));
        context.RefreshStatus(false);
        Check(monitor.Snapshot().All(s => !s.IsPrevious), "Existing Refresh button queries all three accounts and recovers the Gemini display");
        Check(ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == "Gemini") &&
            ProviderLabels(QuotaProvider.Gemini).Single(l => l.Text.StartsWith("5시간  98%", StringComparison.Ordinal)).ForeColor == Color.FromArgb(25, 115, 75),
            "Fresh Gemini quota replaces old values with the shared Good tone");
        before = Enum.GetValues<QuotaProvider>().ToDictionary(p => p, client.Calls);
        context.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));
        await Wait(() => before.All(pair => client.Calls(pair.Key) > pair.Value) && monitor.Snapshot().All(s => !s.IsRefreshing));
        Check(true, "Sleep resume queues a fresh official CLI quota read for ChatGPT, Claude and Gemini");

        var expired = new QuotaState(QuotaProvider.Codex, new(QuotaProvider.Codex,
            [new("expired", "5시간", 97, clock().AddSeconds(-1), 300)]), LastSuccessfulCheckUtc: clock());
        context.StatusWindow.UpdateQuotas([expired], clock());
        Check(AllQuotaLabels().Any(l => l.Text.Contains("3% (이전)") && l.Text.Contains("갱신 대기")),
            "Elapsed reset countdown preserves observed balance until a new result arrives");
        Check(AllQuotaLabels().Single(l => l.Text.Contains("3% (이전)")).ForeColor == Color.DimGray,
            "Elapsed reset quota rows use the shared Muted tone with the Windows palette");
        var expiredGemini = new QuotaState(QuotaProvider.Gemini, new(QuotaProvider.Gemini,
            [new("gemini-5h", "5시간", 25, clock().AddSeconds(-1), 300), new("gemini-weekly", "주간", 12, clock().AddDays(1), 10080)]),
            LastSuccessfulCheckUtc: clock());
        context.StatusWindow.UpdateQuotas([expiredGemini], clock());
        Check(ProviderLabels(QuotaProvider.Gemini).Single(l => l.Text.StartsWith("5시간  75% (이전)", StringComparison.Ordinal)).ForeColor == Color.DimGray &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text.StartsWith("주간  88% 남음", StringComparison.Ordinal)),
            "An elapsed Gemini 5-hour reset preserves its observed balance while the weekly window remains current");
        Check(ProviderLabels(QuotaProvider.Gemini).Single(l => l.Text.StartsWith("5시간  ", StringComparison.Ordinal)).AccessibleDescription?.Contains("새 조회로 회복 확인 필요") == true,
            "Gemini reset details require a new quota result rather than assuming a timed recovery");
        var exhausted = new QuotaState(QuotaProvider.Claude, new(QuotaProvider.Claude,
            [new("weekly", "주간", 100, clock().AddDays(1), 10080)]),
            LastSuccessfulCheckUtc: clock(), NextCheckUtc: clock().AddMinutes(15));
        context.StatusWindow.UpdateQuotas([exhausted], clock());
        Check(AllQuotaLabels().Any(l => l.Text.Contains("주간  0% 남음")),
            "Exhausted quota remains zero instead of inventing credit-based recovery");
        Check(AllQuotaLabels().Single(l => l.Text.StartsWith("주간  0% 남음", StringComparison.Ordinal)).ForeColor == Color.Firebrick,
            "Exhausted quota rows retain the Windows Danger color");
        string nextExhaustedCheck = AgentSchedule.ToKst(exhausted.NextCheckUtc!.Value).ToString("MM-dd HH:mm");
        Check(AllQuotaLabels().Any(l => l.Text.Contains("다음 " + nextExhaustedCheck)),
            "Quota metadata displays the exhausted provider's next 15m check");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-exhausted.png", reportDirectory);
        var exhaustedGemini = new QuotaState(QuotaProvider.Gemini, new(QuotaProvider.Gemini,
            [new("gemini-5h", "5시간", 100, clock().AddHours(2), 300), new("gemini-weekly", "주간", 10, clock().AddDays(1), 10080)]),
            LastSuccessfulCheckUtc: clock(), NextCheckUtc: clock().AddMinutes(15));
        context.StatusWindow.UpdateQuotas([exhaustedGemini], clock());
        Check(ProviderLabels(QuotaProvider.Gemini).Single(l => l.Text.StartsWith("5시간  0% 남음", StringComparison.Ordinal)).ForeColor == Color.Firebrick &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text.Contains("다음 " + nextExhaustedCheck)),
            "Exhausted Gemini uses the shared Danger tone and displays its next 15-minute check");
        context.RefreshStatus(false);
        context.StatusWindow.QuotaButton.PerformClick();
        Check(!context.StatusWindow.QuotaView.Visible && context.StatusWindow.Controls.OfType<Panel>().Count(p => p.Visible) == 3,
            "Status toggle restores all three official status rows including Gemini");
        Check(context.StatusWindow.Controls.OfType<Label>().Any(l => l.Text.Contains("최근 조회 시도:")), "Status footer is restored immediately");

        var previousCodex = monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Codex) with { IsPrevious = true, Error = "Synthetic offline condition" };
        context.StatusWindow.UpdateQuotas([previousCodex], clock());
        Check(AllQuotaLabels().Any(l => l.Text == "ChatGPT · 이전 조회값") &&
            AllQuotaLabels().Any(l => l.Text == "Work/Codex"),
            "Previous Codex values keep the ChatGPT heading and Work/Codex scope");
        context.StatusWindow.UpdateQuotas([previousCodex with { IsRefreshing = true }], clock());
        Check(AllQuotaLabels().Any(l => l.Text == "ChatGPT · 확인 중") &&
            AllQuotaLabels().Any(l => l.Text == "Work/Codex"),
            "Refreshing Codex values keep the ChatGPT heading and Work/Codex scope");

        context.StatusWindow.UpdateQuotas([previousCodex with { CacheError = "Synthetic cache error" }], clock());
        Check(AllQuotaLabels().Single(l => l.Text.StartsWith("성공 ", StringComparison.Ordinal)).ForeColor == Color.Firebrick,
            "Shared cache-error metadata keeps the Windows Danger color");
        context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Claude)], clock());
        Check(AllQuotaLabels().Single(l => l.Text == "한도 조회 대기 · 공식 CLI 로그인 필요").ForeColor == Color.DimGray,
            "Shared pre-reading placeholder keeps its text and Windows muted color");
        const string missingGeminiCli = "공식 CLI를 찾지 못했습니다. 설치 경로를 확인하세요.";
        context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Gemini, Error: missingGeminiCli)], clock());
        Check(ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == "Gemini") &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == "Antigravity · Gemini 모델") &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == missingGeminiCli && l.ForeColor == Color.DimGray) &&
            ProviderLabels(QuotaProvider.Gemini).All(l => !l.Text.Contains('%')),
            "Missing Antigravity CLI displays an unverified Gemini error, not zero or full quota");

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Claude,
                new QuotaReading(QuotaProvider.Claude, [new QuotaWindow("culture", "주간", 12.34, null)]))], clock());
            var cultureRow = AllQuotaLabels().Single(l => l.Text.StartsWith("주간  ", StringComparison.Ordinal));
            Check(cultureRow.Text.Contains("87.7%") && cultureRow.AccessibleDescription?.Contains("사용 12.3% / 잔여 87.7%") == true,
                "Windows quota row and tooltip use the shared invariant percentage format in de-DE");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        context.RefreshStatus(false);
    }
}
