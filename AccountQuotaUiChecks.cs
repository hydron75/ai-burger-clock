using Microsoft.Win32;
using System.Globalization;

namespace AiBurgerClock;

internal sealed class TestAccountQuotaClient(Func<DateTimeOffset> clock, SmokeQueryActivity? activity = null) : IAccountQuotaClient
{
    private int codexCalls;
    private int claudeCalls;
    private int geminiCalls;
    private TaskCompletionSource? geminiResponseGate;
    internal bool FailClaude { get; set; }
    internal bool FailGemini { get; set; }
    internal TaskCompletionSource? NextGeminiResponseGate { set => Interlocked.Exchange(ref geminiResponseGate, value); }
    internal int Calls(QuotaProvider provider) => provider switch
    {
        QuotaProvider.Codex => Volatile.Read(ref codexCalls),
        QuotaProvider.Claude => Volatile.Read(ref claudeCalls),
        _ => Volatile.Read(ref geminiCalls)
    };
    public async Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (provider)
        {
            case QuotaProvider.Codex: Interlocked.Increment(ref codexCalls); break;
            case QuotaProvider.Claude: Interlocked.Increment(ref claudeCalls); break;
            case QuotaProvider.Gemini:
                Interlocked.Increment(ref geminiCalls);
                break;
        }
        var responseGate = provider == QuotaProvider.Gemini ? Interlocked.Exchange(ref geminiResponseGate, null) : null;
        using var query = activity?.Begin("fake-quota/" + provider + (responseGate is null ? "" : "/completion-gate"));
        if (responseGate is not null) await responseGate.Task.WaitAsync(cancellationToken);
        if ((provider == QuotaProvider.Claude && FailClaude) || (provider == QuotaProvider.Gemini && FailGemini))
            throw new IOException("Synthetic offline condition");
        var now = clock();
        QuotaWindow[] windows = provider switch
        {
            QuotaProvider.Codex => [new("session", "5시간", 18, now.AddHours(2), 300), new("weekly", "주간", 92, now.AddDays(2), 10080)],
            QuotaProvider.Claude => [new("session", "5시간", 0, now.AddMinutes(12), 300), new("weekly", "주간", 45, now.AddDays(3), 10080), new("fable", "주간 · Fable", 1, now.AddDays(3), 10080)],
            _ => AccountQuotaParsers.ParseGemini($$"""
                {"groups":[{"name":"Gemini Models","buckets":[
                  {"id":"gemini-5h","window":"5h","remaining_fraction":0.9799,"reset_time":"{{now.AddHours(3):O}}"},
                  {"id":"gemini-weekly","window":"weekly","remaining_fraction":0.9966,"reset_time":"{{now.AddDays(6):O}}"}]},
                  {"name":"Gemini Synthetic Model","buckets":[
                    {"id":"gemini-synthetic-weekly","window":"weekly","remaining_fraction":0.65,"reset_time":"{{now.AddDays(4):O}}"}]}]}
                """).Windows.ToArray()
        };
        return new QuotaReading(provider, windows);
    }
}

internal static class AccountQuotaUiChecks
{
    internal static async Task RunAsync(TrayApplicationContext context, TestAccountQuotaClient client,
        Func<DateTimeOffset> clock, Action<DateTimeOffset> setClock, string? reportDirectory, SmokeDiagnostics diagnostics)
    {
        void Check(bool condition, string label)
        {
            if (!condition) { diagnostics.Record("quota.check-failed", new { label }); throw new InvalidOperationException(label); }
            Console.WriteLine("PASS: " + label);
        }
        async Task Wait(Func<bool> condition, string label = "Quota UI condition", Func<object>? detail = null)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            diagnostics.Record("quota.wait-start", new { label, detail = detail?.Invoke() });
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    diagnostics.Record("quota.wait-timeout", new { label, elapsedMs = elapsed.Elapsed.TotalMilliseconds, detail = detail?.Invoke() });
                    throw new TimeoutException("Quota UI condition: " + label);
                }
                await Task.Delay(25);
            }
            diagnostics.Record("quota.wait-end", new { label, elapsedMs = elapsed.Elapsed.TotalMilliseconds, detail = detail?.Invoke() });
        }
        void ClickQuotaButton(string label)
        {
            // Async checks yield to the real popup's Deactivate/auto-hide policy.
            // Use the same reopening path as a user; do not force visibility or disable auto-hide.
            context.ShowWindow();
            diagnostics.Record("quota.toggle-before-click", new { label });
            Check(context.StatusWindow.Visible && context.StatusWindow.QuotaButton.Enabled && context.StatusWindow.QuotaButton.CanSelect,
                label + ": quota toggle is actionable after normal reopening");
            int clicks = diagnostics.QuotaClicks;
            context.StatusWindow.QuotaButton.PerformClick();
            diagnostics.Record("quota.toggle-after-click", new { label });
            Check(diagnostics.QuotaClicks == clicks + 1, label + ": exactly one actual quota Click");
        }
        var monitor = context.QuotaMonitor!;
        Label[] QuotaLabels() => context.StatusWindow.QuotaView.Controls.OfType<Panel>()
            .SelectMany(box => box.Controls.OfType<Label>()).ToArray();
        Panel Box(QuotaProvider provider) => context.StatusWindow.QuotaView.Controls.OfType<Panel>()
            .Single(box => box.Name == provider + "QuotaBox");
        Label[] ProviderLabels(QuotaProvider provider) => Box(provider).Controls.OfType<Label>().ToArray();
        foreach (var provider in Enum.GetValues<QuotaProvider>()) await monitor.RefreshOnceAsync(provider);
        var callsBeforeOpen = Enum.GetValues<QuotaProvider>().ToDictionary(provider => provider, client.Calls);
        var dueBeforeOpen = monitor.Snapshot().Select(state => state.NextCheckUtc).ToArray();
        context.ShowWindow();
        var appearance = context.CurrentAppearance;
        var size = context.StatusWindow.ClientSize;
        var title = context.StatusWindow.Controls.OfType<Label>().Single(label => label.Text == StatusPanelModel.Title);
        var titleBounds = title.Bounds;
        var statusBoxes = context.StatusWindow.Controls.OfType<Panel>()
            .Where(panel => panel != context.StatusWindow.QuotaView).ToArray();
        int hiddenQuotaClicks = diagnostics.QuotaClicks;
        string hiddenQuotaText = context.StatusWindow.QuotaButton.Text;
        context.StatusWindow.Hide();
        Check(!context.StatusWindow.Visible && !context.StatusWindow.QuotaButton.CanSelect,
            "Hidden popup makes the quota toggle unselectable");
        context.StatusWindow.QuotaButton.PerformClick();
        Check(diagnostics.QuotaClicks == hiddenQuotaClicks && context.StatusWindow.QuotaButton.Text == hiddenQuotaText,
            "Hidden quota PerformClick emits no Click and preserves the selected mode");
        ClickQuotaButton("Show initial quota view");
        Check(Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) == callsBeforeOpen[provider]) &&
            monitor.Snapshot().Select(state => state.NextCheckUtc).SequenceEqual(dueBeforeOpen),
            "Opening and toggling quota view never requests a query or changes its existing schedule");
        Check(context.StatusWindow.QuotaView.Visible, "Quota toggle shows ChatGPT Work/Codex and Claude without enlarging the popup");
        context.StatusWindow.UpdateQuotas(monitor.Snapshot().Where(s => s.Provider != QuotaProvider.Gemini).ToArray(), clock());
        var labels = QuotaLabels();
        var boxes = context.StatusWindow.QuotaView.Controls.OfType<Panel>().ToArray();
        Check(boxes.Length == 2 && boxes[0].Name == "CodexQuotaBox" && boxes[1].Name == "ClaudeQuotaBox",
            "Each account quota has its own Provider box in the original order");
        Check(title.Text == QuotaPanelModel.AccessibleName && title.Bounds == titleBounds &&
            context.StatusWindow.QuotaView.AccessibleName == QuotaPanelModel.AccessibleName &&
            context.StatusWindow.Controls.OfType<Label>().Any(label => label.Text == QuotaPanelModel.Caption),
            "Usage title, accessibility and caption agree without moving the original outside baseline");
        Check(boxes.All(box => box.BackColor == statusBoxes[0].BackColor && box.BorderStyle == statusBoxes[0].BorderStyle &&
            box.Width == context.StatusWindow.QuotaView.ClientSize.Width) &&
            context.StatusWindow.QuotaView.Width == statusBoxes[0].Width && context.StatusWindow.QuotaView.BackColor == context.StatusWindow.BackColor,
            "Quota Provider cards retain white status styling within the original width, allowing for the vertical scrollbar");
        Check(boxes.All(box => box.Left == 0 && box.Controls.OfType<Label>().All(label =>
            label.Left == statusBoxes[0].Controls.OfType<Label>().First().Left)),
            "Quota and status card text starts at the same inner x position");
        Check(boxes[0].Controls.OfType<Label>().Single(label => label.Text == "ChatGPT").Top ==
            statusBoxes[0].Controls.OfType<Label>().First().Top,
            "Quota and status card headings use the same top padding");
        Check(boxes.All(box => box.Cursor == Cursors.Default && box.Controls.Cast<Control>().All(control =>
            control.Cursor == Cursors.Default && control.ContextMenuStrip is null)),
            "Non-clickable quota boxes retain the default cursor and no recording menus");
        var mouseEnter = typeof(Control).GetMethod("OnMouseEnter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var mouseLeave = typeof(Control).GetMethod("OnMouseLeave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var box in boxes)
        {
            mouseEnter.Invoke(box, [EventArgs.Empty]);
            foreach (Control label in box.Controls) mouseEnter.Invoke(label, [EventArgs.Empty]);
            Check(box.BackColor == Color.White && box.Controls.Cast<Control>().All(control => control.BackColor == Color.White),
                "Quota box stays white when the pointer enters its box and text");
            mouseLeave.Invoke(box, [EventArgs.Empty]);
        }
        Check(labels.Any(l => l.Text == "ChatGPT") && labels.Any(l => l.Text == "Work/Codex") && labels.Any(l => l.Text.Contains("Claude")) &&
            !labels.Any(l => l.Text.Contains("Gemini")), "Existing two-provider layout remains unchanged when Gemini is absent");
        var chatGptHeading = labels.Single(l => l.Text == "ChatGPT");
        var scope = labels.Single(l => l.Text == "Work/Codex");
        Check(scope.Top >= chatGptHeading.Bottom && ProviderLabels(QuotaProvider.Codex).Any(l => l.Top >= scope.Bottom && l.Text.StartsWith("5시간", StringComparison.Ordinal)),
            "ChatGPT quota heading has Work/Codex on a separate line above its limits");
        Check(chatGptHeading.ForeColor == SystemColors.ControlText && scope.ForeColor == Color.DimGray,
            "Shared Normal and Muted quota tones retain Windows heading and scope colors");
        Check(labels.Single(l => l.Text.StartsWith("5시간  18% 사용", StringComparison.Ordinal)).ForeColor == Color.FromArgb(25, 115, 75) &&
            labels.Single(l => l.Text.StartsWith("주간  92% 사용", StringComparison.Ordinal)).ForeColor == Color.DarkOrange &&
            labels.Any(l => l.Text == "5시간  18% 사용 · 약 2시간 후 리셋") &&
            labels.Any(l => l.Text == "주간  92% 사용 · 약 2일 후 리셋"),
            "Used percentages keep shared tones while session and weekly rows use their own two-unit countdowns");
        Check(boxes.All(box => box.Controls.Cast<Control>().All(child => child.Bottom <= box.Height)) &&
            context.StatusWindow.QuotaView.VerticalScroll.Visible && !context.StatusWindow.QuotaView.HorizontalScroll.Visible,
            "Balance bars expand Provider cards inside the unchanged quota-only vertical viewport");
        int Scale(int value) => (int)Math.Round(value * context.StatusWindow.QuotaView.DeviceDpi / 96.0);
        foreach (var box in boxes)
            Check(box.Controls.OfType<QuotaBalanceBar>().All(bar => bar.Left == Scale(8) && bar.Width == box.ClientSize.Width - Scale(16)),
                "Balance bars align with their Provider card's text padding: " + box.Name);
        Check(Box(QuotaProvider.Codex).Controls.OfType<QuotaBalanceBar>().Select(bar => bar.UsedPercent).SequenceEqual(new[] { 18.0, 92.0 }) &&
            Box(QuotaProvider.Claude).Controls.OfType<QuotaBalanceBar>().Select(bar => bar.UsedPercent).SequenceEqual(new[] { 0.0, 45.0, 1.0 }),
            "Usage bars show the consumed percentages, not the remaining fraction");
        Check(boxes.SelectMany(box => box.Controls.OfType<QuotaBalanceBar>()).All(bar =>
            bar.FillColor == QuotaBalanceBar.ProviderColor(bar.Parent == Box(QuotaProvider.Codex) ? QuotaProvider.Codex : QuotaProvider.Claude)),
            "Fresh usage bars retain the chosen ChatGPT green and Claude orange service colors");
        Check(ProviderLabels(QuotaProvider.Claude).Any(l => l.Text.StartsWith("5시간  0% 사용", StringComparison.Ordinal)) &&
            ProviderLabels(QuotaProvider.Claude).Any(l => l.Text.StartsWith("주간  45% 사용", StringComparison.Ordinal)) &&
            ProviderLabels(QuotaProvider.Claude).Any(l => l.Text.StartsWith("주간 · Fable  1% 사용", StringComparison.Ordinal)),
            "Windows displays used percentages including inactive and model-scoped windows");
        Check(labels.Any(l => l.AccessibleDescription?.Contains("사용 100%는 15분") == true),
            "Quota tooltip explains exhausted 15m polling separately from the 5m reset band");
        Check(labels.All(l => l.Height >= TextRenderer.MeasureText(l.Text, l.Font).Height), "Quota rows accommodate DPI-scaled text height");
        Check(context.StatusWindow.ClientSize == size && context.CurrentAppearance == appearance, "Quota values never change tray health or original window dimensions");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-two-providers.png", reportDirectory);
        await QuotaToolTipUiChecks.RunAsync(context.StatusWindow.QuotaView, labels, reportDirectory, Check);

        context.RefreshStatus(false);
        boxes = context.StatusWindow.QuotaView.Controls.OfType<Panel>().ToArray();
        labels = QuotaLabels();
        Check(boxes.Select(box => box.Name).SequenceEqual(new[] { "CodexQuotaBox", "ClaudeQuotaBox", "GeminiQuotaBox" }),
            "Gemini has a third Provider box after ChatGPT and Claude");
        var geminiBox = Box(QuotaProvider.Gemini);
        Check(geminiBox.BackColor == Color.White && geminiBox.BorderStyle == boxes[0].BorderStyle &&
            geminiBox.Width == boxes[0].Width && geminiBox.Cursor == Cursors.Default &&
            geminiBox.Controls.OfType<Label>().All(label => label.Left == boxes[0].Controls.OfType<Label>().First().Left),
            "Gemini quota box uses the same non-clickable white card and text padding");
        mouseEnter.Invoke(geminiBox, [EventArgs.Empty]);
        foreach (Control label in geminiBox.Controls) mouseEnter.Invoke(label, [EventArgs.Empty]);
        Check(geminiBox.BackColor == Color.White && geminiBox.Controls.Cast<Control>().All(control => control.BackColor == Color.White),
            "Gemini quota box has no hover effect");
        Check(ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == "Gemini") &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == QuotaPanelModel.GeminiScope) &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text.StartsWith("5시간  2% 사용", StringComparison.Ordinal)) &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text.StartsWith("주간  0.3% 사용", StringComparison.Ordinal)) &&
            ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text.StartsWith("주간 Gemini Synthetic Model  35% 사용", StringComparison.Ordinal)),
            "Gemini shows common and model-specific Antigravity windows without other products");
        Check(ProviderLabels(QuotaProvider.Gemini).Where(l => l.Text.Contains("% 사용", StringComparison.Ordinal))
            .Select(l => l.Text.Split("  ")[0]).SequenceEqual(new[] { "5시간", "주간", "주간 Gemini Synthetic Model" }),
            "Gemini row order is five-hour, unchanged common weekly, then model weekly");
        Check(ProviderLabels(QuotaProvider.Gemini).Any(l => l.AccessibleDescription?.Contains("Gemini Apps 웹·모바일의 전체 한도가 아니며", StringComparison.Ordinal) == true),
            "Gemini scope details do not imply Gemini Apps or third-party model quotas");
        Check(geminiBox.Controls.OfType<QuotaBalanceBar>().Count() == 3 &&
            geminiBox.Controls.OfType<QuotaBalanceBar>().All(bar => bar.FillColor == QuotaBalanceBar.ProviderColor(QuotaProvider.Gemini)) &&
            Math.Abs(geminiBox.Controls.OfType<QuotaBalanceBar>().First().UsedPercent - 2.01) < 1e-9,
            "Gemini blue bars retain precise consumed fractions from the verified reading");
        Check(geminiBox.Controls.OfType<QuotaBalanceBar>().Last().ElapsedPercent is > 42 and < 43 &&
            geminiBox.Controls.OfType<QuotaBalanceBar>().Last().AccessibleName!.Contains("균등 사용 기준보다", StringComparison.Ordinal) &&
            context.StatusWindow.QuotaView.DetailFor(geminiBox).Contains("주간 Gemini Synthetic Model:", StringComparison.Ordinal),
            "Gemini model weekly has a time boundary, row accessibility comparison and Provider detail");
        Check(context.StatusWindow.ClientSize == size && context.StatusWindow.QuotaView.VerticalScroll.Visible &&
            !context.StatusWindow.QuotaView.HorizontalScroll.Visible && boxes.All(box =>
                box.Controls.OfType<Label>().All(label => label.Bottom <= box.Height)),
            "Three Provider boxes scroll only inside the existing quota viewport without enlarging the popup");
        Check(labels.All(l => l.Height >= TextRenderer.MeasureText(l.Text, l.Font).Height),
            "All three Provider boxes accommodate DPI-scaled text height");
        foreach (var section in QuotaPanelModel.Sections(monitor.Snapshot(), clock()))
        {
            var providerLabels = ProviderLabels(section.Provider);
            Check(section.Rows.All(row => providerLabels.Single(label => label.Text == row.Text).AccessibleName == (row.ReadingText ?? row.Text) &&
                    providerLabels.Single(label => label.Text == row.Text).AccessibleDescription == (row.UsedPercent.HasValue ? "" : row.Detail)) &&
                providerLabels.Where(label => !section.Rows.Any(row => row.Text == label.Text))
                    .All(label => label.AccessibleDescription == section.Detail) &&
                Box(section.Provider).Controls.OfType<QuotaBalanceBar>().Select(bar => bar.AccessibleName)
                    .SequenceEqual(section.Rows.Where(row => row.UsedPercent.HasValue).Select(row => section.Heading.Text + " · " + row.ReadingText)) &&
                Box(section.Provider).Controls.OfType<QuotaBalanceBar>().All(bar => bar.AccessibleDescription == ""),
                "Windows quota rows and bars bind the full shared reading once without parsing Provider detail: " + section.Provider);
            Check(Box(section.Provider).AccessibleDescription == section.Detail &&
                Box(section.Provider).Controls.Cast<Control>().All(control =>
                    context.StatusWindow.QuotaView.DetailFor(control) == section.Detail),
                "Provider-wide tooltip and card accessibility retain the full unchanged detail: " + section.Provider);
            Check(Box(section.Provider).Controls.OfType<QuotaBalanceBar>().Select(bar => bar.ElapsedPercent)
                .SequenceEqual(section.Rows.Where(row => row.UsedPercent.HasValue).Select(row => row.ElapsedPercent)),
                "Windows time boundaries use the shared eligible snapshot percentages: " + section.Provider);
        }
        var comparisonSnapshot = monitor.Snapshot();
        var beforeAge = boxes.SelectMany(box => box.Controls.OfType<QuotaBalanceBar>())
            .Select(bar => (bar.UsedPercent, bar.ElapsedPercent)).ToArray();
        context.StatusWindow.UpdateQuotas(comparisonSnapshot, clock().AddMinutes(17));
        Check(boxes.SelectMany(box => box.Controls.OfType<QuotaBalanceBar>())
            .Select(bar => (bar.UsedPercent, bar.ElapsedPercent)).SequenceEqual(beforeAge) &&
            context.StatusWindow.QuotaView.DetailFor(Box(QuotaProvider.Codex)).Contains("마지막 조회 기준, 17분 전", StringComparison.Ordinal),
            "Local time changes Provider reference age without moving usage or time boundaries");
        foreach (var section in QuotaPanelModel.Sections(comparisonSnapshot, clock().AddMinutes(17)))
            Check(section.Rows.All(row => ProviderLabels(section.Provider).Single(label => label.Text == row.Text)
                    .AccessibleName == row.ReadingText) &&
                Box(section.Provider).Controls.OfType<QuotaBalanceBar>().All(bar =>
                    bar.AccessibleDescription == "" && (!bar.ElapsedPercent.HasValue ||
                        bar.AccessibleName?.EndsWith("17분 전 기준", StringComparison.Ordinal) == true)),
                "Screen-reader row and bar ages advance using only their own shared comparison: " + section.Provider);
        Check(Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) == callsBeforeOpen[provider]) &&
            monitor.Snapshot().Select(state => state.NextCheckUtc).SequenceEqual(dueBeforeOpen),
            "Reference-age display performs no query and preserves provider-specific due dates");
        context.StatusWindow.UpdateQuotas(comparisonSnapshot, clock());
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas.png", reportDirectory);
        context.StatusWindow.QuotaView.ScrollControlIntoView(geminiBox);
        var geminiMetadata = ProviderLabels(QuotaProvider.Gemini).Single(l => l.Text.StartsWith("성공 ", StringComparison.Ordinal));
        Check(context.StatusWindow.QuotaView.RectangleToScreen(context.StatusWindow.QuotaView.ClientRectangle)
            .Contains(geminiMetadata.RectangleToScreen(geminiMetadata.ClientRectangle)),
            "Gemini metadata is reachable by quota-only vertical scrolling");
        var scrollPosition = context.StatusWindow.QuotaView.AutoScrollPosition;
        var retainedBoxes = boxes.ToArray();
        context.StatusWindow.UpdateQuotas(monitor.Snapshot(), clock().AddSeconds(1));
        Check(retainedBoxes.SequenceEqual(context.StatusWindow.QuotaView.Controls.OfType<Panel>()) &&
            context.StatusWindow.QuotaView.AutoScrollPosition == scrollPosition,
            "Countdown refresh preserves Provider boxes and the scrolled Gemini position");
        context.StatusWindow.UpdateQuotas(monitor.Snapshot(), clock());
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-gemini.png", reportDirectory);
        context.StatusWindow.QuotaView.AutoScrollPosition = Point.Empty;
        await QuotaToolTipUiChecks.CheckGeminiAsync(context.StatusWindow.QuotaView, reportDirectory, Check);

        client.FailClaude = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        context.RefreshStatus(false);
        Check(monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Claude).IsPrevious &&
            !monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Codex).IsPrevious, "One failed account keeps its previous values without downgrading the other account");
        Check(QuotaLabels().Any(l => l.Text.Contains("Claude · 이전 조회값")),
            "Failed refresh explicitly labels the old quota instead of inventing zero or full balance");
        Check(QuotaLabels().Single(l => l.Text.StartsWith("5시간  0% 사용 (이전)", StringComparison.Ordinal)).ForeColor == Color.DimGray,
            "Previous quota values keep the Windows muted color");
        Check(Box(QuotaProvider.Claude).Controls.OfType<QuotaBalanceBar>().All(bar => bar.FillColor == Color.Gray) &&
            Box(QuotaProvider.Claude).Controls.OfType<QuotaBalanceBar>().First().UsedPercent == 0,
            "Previous balance bars become gray without inventing or discarding the last observed values");
        Check(Box(QuotaProvider.Claude).Controls.OfType<QuotaBalanceBar>().All(bar => bar.ElapsedPercent is null),
            "A failed Provider refresh removes time layers and boundaries from its retained values");
        var failedSection = QuotaPanelModel.Section(monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Claude), clock());
        Check(ProviderLabels(QuotaProvider.Claude).All(label => label.AccessibleDescription ==
                (failedSection.Rows.Any(row => row.Text == label.Text) ? "" : failedSection.Detail)) &&
            Box(QuotaProvider.Claude).Controls.OfType<QuotaBalanceBar>().All(bar => bar.AccessibleDescription == "") &&
            context.StatusWindow.QuotaView.DetailFor(Box(QuotaProvider.Claude)) == failedSection.Detail,
            "Failed refresh clears stale row comparisons while retaining the full Provider error tooltip");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-previous.png", reportDirectory);
        client.FailClaude = false;
        await monitor.RefreshOnceAsync(QuotaProvider.Claude);
        client.FailGemini = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Gemini);
        context.RefreshStatus(false);
        Check(monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsPrevious &&
            monitor.Snapshot().Where(s => s.Provider != QuotaProvider.Gemini).All(s => !s.IsPrevious),
            "Gemini lookup failure retains only its own previous verified reading");
        Check(ProviderLabels(QuotaProvider.Gemini).Any(l => l.Text == "Gemini · 이전 조회값") &&
            ProviderLabels(QuotaProvider.Gemini).Single(l => l.Text.StartsWith("5시간  ", StringComparison.Ordinal)).ForeColor == Color.DimGray,
            "Gemini previous values are explicitly labeled and muted");
        context.StatusWindow.QuotaView.ScrollControlIntoView(Box(QuotaProvider.Gemini));
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-gemini-previous.png", reportDirectory);
        client.FailGemini = false;

        await Wait(() => !context.ProviderMonitor!.IsRefreshing && monitor.Snapshot().All(s => !s.IsRefreshing),
            "providers idle before manual Refresh");
        context.ShowWindow();
        var refreshButton = context.StatusWindow.Controls.OfType<Button>().Single(b => b.Text == "Refresh");
        Check(context.StatusWindow.Visible && refreshButton.Enabled && refreshButton.CanSelect,
            "Quota Refresh is actionable after normal reopening and provider completion");
        var callsBeforeRefresh = Enum.GetValues<QuotaProvider>().ToDictionary(provider => provider, client.Calls);
        int before = client.Calls(QuotaProvider.Claude);
        int refreshClicks = diagnostics.RefreshClicks;
        diagnostics.Record("quota.refresh-before-click", new { callsBeforeRefresh });
        refreshButton.PerformClick();
        diagnostics.Record("quota.refresh-after-click");
        Check(diagnostics.RefreshClicks == refreshClicks + 1, "Quota Refresh emits exactly one actual Click before waiting for results");
        await Wait(() => client.Calls(QuotaProvider.Claude) > before && monitor.Snapshot().All(s => !s.IsRefreshing), "manual Refresh",
            () => new { claudeBefore = before, claudeNow = client.Calls(QuotaProvider.Claude), claudeAdvanced = client.Calls(QuotaProvider.Claude) > before, allIdle = monitor.Snapshot().All(s => !s.IsRefreshing) });
        Check(monitor.Snapshot().All(s => !s.IsPrevious) &&
            Enum.GetValues<QuotaProvider>().All(provider => client.Calls(provider) > callsBeforeRefresh[provider]),
            "Existing Refresh button recovers all three independent quota displays");
        before = client.Calls(QuotaProvider.Claude);
        context.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));
        await Wait(() => client.Calls(QuotaProvider.Claude) > before && monitor.Snapshot().All(s => !s.IsRefreshing), "Resume",
            () => new { claudeBefore = before, claudeNow = client.Calls(QuotaProvider.Claude), claudeAdvanced = client.Calls(QuotaProvider.Claude) > before, allIdle = monitor.Snapshot().All(s => !s.IsRefreshing) });
        Check(true, "Sleep resume queues fresh quota reads including Gemini");

        var expired = new QuotaState(QuotaProvider.Codex, new(QuotaProvider.Codex,
            [new("expired", "5시간", 97, clock().AddSeconds(-1), 300)]), LastSuccessfulCheckUtc: clock());
        context.StatusWindow.UpdateQuotas([expired], clock());
        Check(QuotaLabels().Any(l => l.Text.Contains("97% 사용 (이전)") && l.Text.Contains("갱신 대기")),
            "Elapsed reset countdown preserves observed usage until a new result arrives");
        Check(QuotaLabels().Single(l => l.Text.Contains("97% 사용 (이전)")).ForeColor == Color.DimGray,
            "Elapsed reset quota rows use the shared Muted tone with the Windows palette");
        Check(Box(QuotaProvider.Codex).Controls.OfType<QuotaBalanceBar>().Single() is { UsedPercent: 97, FillColor: var expiredColor } &&
            expiredColor == Color.Gray && context.StatusWindow.QuotaView.DetailFor(Box(QuotaProvider.Codex)).Contains("예정 시각 경과", StringComparison.Ordinal),
            "Elapsed reset keeps the observed 97% used bar and states that recovery requires a fresh reading");
        Check(Box(QuotaProvider.Codex).Controls.OfType<QuotaBalanceBar>().Single().ElapsedPercent is null,
            "A passed reset never leaves a current time boundary on the old usage bar");
        Check(Box(QuotaProvider.Codex).Controls.OfType<QuotaBalanceBar>().Single().AccessibleDescription == "" &&
            QuotaLabels().Single(label => label.Text.Contains("97% 사용 (이전)")).AccessibleDescription == "",
            "A passed reset clears row and bar comparison descriptions rather than announcing an obsolete pace");
        var exhausted = new QuotaState(QuotaProvider.Claude, new(QuotaProvider.Claude,
            [new("weekly", "주간", 100, clock().AddDays(1), 10080)]),
            LastSuccessfulCheckUtc: clock(), NextCheckUtc: clock().AddMinutes(15));
        context.StatusWindow.UpdateQuotas([exhausted], clock());
        Check(QuotaLabels().Any(l => l.Text.Contains("주간  100% 사용")),
            "Exhausted quota shows 100% used instead of inventing credit-based recovery");
        Check(QuotaLabels().Single(l => l.Text.StartsWith("주간  100% 사용", StringComparison.Ordinal)).ForeColor == Color.Firebrick,
            "Exhausted quota rows retain the Windows Danger color");
        var exhaustedBar = Box(QuotaProvider.Claude).Controls.OfType<QuotaBalanceBar>().Single();
        Check(exhaustedBar.FilledWidth == exhaustedBar.ClientSize.Width,
            "An exhausted usage bar is completely filled at 100% used");
        string nextExhaustedCheck = AgentSchedule.ToKst(exhausted.NextCheckUtc!.Value).ToString("MM-dd HH:mm");
        Check(QuotaLabels().Any(l => l.Text.Contains("다음 " + nextExhaustedCheck)),
            "Quota metadata displays the exhausted provider's next 15m check");
        SmokeTest.RenderAndCheckLayout(context.StatusWindow, "account-quotas-exhausted.png", reportDirectory);
        context.RefreshStatus(false);
        diagnostics.Record("quota.status-toggle-before-click");
        ClickQuotaButton("Restore official status view");
        diagnostics.Record("quota.status-toggle-before-check");
        Check(!context.StatusWindow.QuotaView.Visible && context.StatusWindow.Controls.OfType<Panel>().Count(p => p.Visible) == 4,
            "Status toggle restores all four official status rows including xAI");
        Check(title.Text == StatusPanelModel.Title && title.Bounds == titleBounds,
            "Status toggle restores the original title without moving either baseline");
        Check(context.StatusWindow.Controls.OfType<Label>().Any(l => l.Text.Contains("최근 조회 시도:")), "Status footer is restored immediately");

        var previousCodex = monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Codex) with { IsPrevious = true, Error = "Synthetic offline condition" };
        context.StatusWindow.UpdateQuotas([previousCodex], clock());
        Check(QuotaLabels().Any(l => l.Text == "ChatGPT · 이전 조회값") &&
            QuotaLabels().Any(l => l.Text == "Work/Codex"),
            "Previous Codex values keep the ChatGPT heading and Work/Codex scope");
        context.StatusWindow.UpdateQuotas([previousCodex with { IsRefreshing = true }], clock());
        Check(QuotaLabels().Any(l => l.Text == "ChatGPT · 확인 중") &&
            QuotaLabels().Any(l => l.Text == "Work/Codex"),
            "Refreshing Codex values keep the ChatGPT heading and Work/Codex scope");

        context.StatusWindow.UpdateQuotas([previousCodex with { CacheError = "Synthetic cache error" }], clock());
        Check(QuotaLabels().Single(l => l.Text.StartsWith("성공 ", StringComparison.Ordinal)).ForeColor == Color.Firebrick,
            "Shared cache-error metadata keeps the Windows Danger color");
        context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Claude)], clock());
        Check(QuotaLabels().Single(l => l.Text == "한도 조회 대기 · 공식 CLI 로그인 필요").ForeColor == Color.DimGray,
            "Shared pre-reading placeholder keeps its text and Windows muted color");

        context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Gemini)], clock());
        Check(QuotaLabels().Any(l => l.Text == "Gemini") && QuotaLabels().Any(l => l.Text == QuotaPanelModel.GeminiScope) &&
            QuotaLabels().Single(l => l.Text == "한도 조회 대기 · agy CLI 로그인 필요").ForeColor == Color.DimGray,
            "Missing Gemini reading uses the agy login placeholder without inventing a balance");
        Check(!Box(QuotaProvider.Gemini).Controls.OfType<QuotaBalanceBar>().Any(),
            "Missing or unavailable quota readings do not create guessed balance bars");
        context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Gemini, Error: "한도 응답 형식을 확인하지 못했습니다.")], clock());
        Check(QuotaLabels().Any(l => l.Text == "한도 응답 형식을 확인하지 못했습니다.") &&
            QuotaLabels().All(l => !l.Text.Contains("%", StringComparison.Ordinal)),
            "Unavailable Gemini schema is explained instead of displaying guessed quota percentages");

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Claude,
                new QuotaReading(QuotaProvider.Claude, [new QuotaWindow("culture", "주간", 12.34, null)]))], clock());
            var cultureRow = QuotaLabels().Single(l => l.Text.StartsWith("주간  ", StringComparison.Ordinal));
            Check(cultureRow.Text.Contains("12.3% 사용") && cultureRow.AccessibleDescription == "" &&
                context.StatusWindow.QuotaView.DetailFor(cultureRow) == context.StatusWindow.QuotaView.DetailFor(Box(QuotaProvider.Claude)),
                "Windows used row keeps invariant formatting and no guessed comparison, with unchanged Provider tooltip in de-DE");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        ClickQuotaButton("Show extra quota rows");
        QuotaRowUiChecks.Run(context.StatusWindow, monitor.Snapshot(), clock(), reportDirectory, Check);
        var extraWindows = Enumerable.Range(0, 12).Select(index => new QuotaWindow("extra-" + index, "주간 · 모델 " + index,
            index, clock().AddDays(1))).ToArray();
        context.StatusWindow.UpdateQuotas([new QuotaState(QuotaProvider.Claude, new QuotaReading(QuotaProvider.Claude, extraWindows))], clock());
        Check(context.StatusWindow.QuotaView.VerticalScroll.Visible && !context.StatusWindow.QuotaView.HorizontalScroll.Visible &&
            context.StatusWindow.ClientSize == size,
            "Extra model-scoped quota rows scroll vertically without enlarging the popup");
        context.StatusWindow.UpdateQuotas(monitor.Snapshot().Where(s => s.Provider != QuotaProvider.Gemini).ToArray(), clock());
        Check(context.StatusWindow.QuotaView.VerticalScroll.Visible && !context.StatusWindow.QuotaView.HorizontalScroll.Visible,
            "Returning to two Providers retains only the vertical scrolling required by their balance bars");
        context.RefreshStatus(false);
        diagnostics.Phase = "quota.synthetic-held-response";
        diagnostics.Record("quota.synthetic-held-response-start");
        await CheckResponsiveRefreshAsync(context, client, clock, setClock, Check, condition => Wait(condition));
        ClickQuotaButton("Restore status after delayed Gemini response");
        context.RefreshStatus(false);
    }

    private static async Task CheckResponsiveRefreshAsync(TrayApplicationContext context, TestAccountQuotaClient client,
        Func<DateTimeOffset> clock, Action<DateTimeOffset> setClock, Action<bool, string> check, Func<Func<bool>, Task> wait)
    {
        var monitor = context.QuotaMonitor!;
        var originalNow = clock();
        var calls = Enum.GetValues<QuotaProvider>().ToDictionary(provider => provider, client.Calls);
        var menu = context.TrayIcon.ContextMenuStrip!;
        int uiTicks = 0;
        var tooltipValues = new HashSet<string>();
        var countdownValues = new HashSet<string>();
        using var heartbeat = new System.Windows.Forms.Timer { Interval = 200 };
        var responseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.Tick += (_, _) =>
        {
            setClock(clock().AddSeconds(1));
            uiTicks++;
            tooltipValues.Add(context.TrayIcon.Text);
            foreach (var label in context.StatusWindow.Controls.OfType<Label>().Where(l => l.Text.StartsWith("전환까지", StringComparison.Ordinal)))
                countdownValues.Add(label.Text);
        };
        try
        {
            client.NextGeminiResponseGate = responseGate;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            heartbeat.Start();
            menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "상태·한도 새로 고침").PerformClick();
            check(elapsed.ElapsedMilliseconds < 1000, "Tray Refresh action returns immediately while Gemini waits for a synthetic completion gate");
            await wait(() => client.Calls(QuotaProvider.Gemini) > calls[QuotaProvider.Gemini] &&
                monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing);
            await Task.Delay(1500);
            menu.Show(context.StatusWindow, new Point(20, 20));
            check(menu.Visible && monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing,
                "Native tray context menu opens while Gemini quota lookup remains pending");
            menu.Close();
            context.StatusWindow.Close();
            check(!context.StatusWindow.Visible && !context.StatusWindow.IsDisposed,
                "Status window can hide while Gemini quota lookup is pending");
            menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "상태 창 열기").PerformClick();
            check(context.StatusWindow.Visible && monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing,
                "Tray menu reopens the status window before the Gemini lookup finishes");
            // UI responsiveness has a bounded wait of its own. The fake response
            // cannot finish merely because this continuation runs late.
            await wait(() => uiTicks >= 20 && tooltipValues.Count >= 3 && countdownValues.Count >= 3);
            check(monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing && uiTicks >= 20 &&
                tooltipValues.Count >= 3 && countdownValues.Count >= 3,
                "Real Windows timer keeps tray tooltip and status countdown updating while Gemini is explicitly held");
            check(monitor.Snapshot().Where(s => s.Provider != QuotaProvider.Gemini).All(s => !s.IsRefreshing) &&
                client.Calls(QuotaProvider.Codex) > calls[QuotaProvider.Codex] &&
                client.Calls(QuotaProvider.Claude) > calls[QuotaProvider.Claude] &&
                client.Calls(QuotaProvider.Gemini) == calls[QuotaProvider.Gemini] + 1,
                "Other quota providers complete independently without repeating the pending Gemini lookup");
            responseGate.SetResult();
            await wait(() => !monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsRefreshing);
            await wait(() => context.StatusWindow.QuotaView.Controls.OfType<Panel>()
                .Single(box => box.Name == "GeminiQuotaBox").Controls.OfType<Label>().Any(label => label.Text == "Gemini"));
            check(!monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini).IsPrevious,
                "Delayed Gemini response updates its Provider box after completion");
            Console.WriteLine($"INFO: synthetic Gemini held-response completed in {elapsed.Elapsed.TotalSeconds:0.00}s; UI heartbeat={uiTicks}, tray countdown values={tooltipValues.Count}, status countdown values={countdownValues.Count}.");
        }
        finally
        {
            heartbeat.Stop();
            client.NextGeminiResponseGate = null;
            responseGate.TrySetResult();
            menu.Close();
            setClock(originalNow);
            context.RefreshStatus(false);
        }
    }
}
