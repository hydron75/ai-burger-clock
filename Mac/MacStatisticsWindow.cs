using System.Text;
using AppKit;
using CoreGraphics;

namespace AiBurgerClock;

// Text comes from the shared StatisticsText (Windows wording); layout and the record how-to stay here.
internal sealed class MacStatisticsWindow : IDisposable
{
    // Where a Mac user records an experience since 0.2.0 (the menu-bar submenu is gone).
    internal const string HowToRecord = "메뉴바 팝오버의 Provider 카드를 오른쪽 클릭해 사용 경험을 기록하세요.";

    private readonly UsageStore store;
    internal NSWindow Window { get; }
    private readonly NSPopUpButton period;
    private readonly NSPopUpButton group;
    private readonly NSTextField summary;
    private readonly NSTextView rows;
    private readonly NSTextField explanation;
    private readonly NSButton refresh;
    private readonly CancellationTokenSource lifetime = new();
    private StatisticsReport? report;
    private bool disposed;
    private bool stopping;
    private bool loading;
    private int requestedPeriod;
    private Task currentLoad = Task.CompletedTask;

    internal MacStatisticsWindow(UsageStore store)
    {
        this.store = store;
        Window = new(new CGRect(0, 0, 790, 520), NSWindowStyle.Titled | NSWindowStyle.Closable,
            NSBackingStore.Buffered, false)
        {
            Title = "AI Burger Clock · Statistics",
            // Reuse the same native window after its close button is clicked.
            DangerousReleasedWhenClosed = false
        };
        var root = new NSView(new CGRect(0, 0, 790, 520));
        Window.ContentView = root;
        period = new NSPopUpButton(new CGRect(14, 478, 138, 28), false);
        period.AddItems(StatisticsText.Periods.ToArray());
        period.SelectItem(0);
        root.AddSubview(period);
        group = new NSPopUpButton(new CGRect(163, 478, 242, 28), false);
        group.AddItems(StatisticsText.Sections.ToArray());
        group.SelectItem(0);
        root.AddSubview(group);
        refresh = MacControls.Button(root, "새로 고침", new CGRect(422, 478, 116, 28), () => _ = RefreshAsync());
        summary = MacControls.Label(root, StatisticsText.Loading, 428, 43, 11);
        rows = MacControls.TextArea(root, new CGRect(14, 86, 762, 331), 12);
        explanation = MacControls.Label(root, StatisticsText.Explanation, 10, 64, 11);
        period.Activated += (_, _) => _ = RefreshAsync();
        group.Activated += (_, _) => Fill();
        Window.Center();
    }

    internal Task RefreshAsync()
    {
        if (disposed || stopping) return Task.CompletedTask;
        requestedPeriod = (int)period.IndexOfSelectedItem;
        if (!loading) currentLoad = LoadAsync();
        return currentLoad;
    }

    private async Task LoadAsync()
    {
        loading = true;
        refresh.Enabled = false;
        summary.StringValue = StatisticsText.Loading;
        try
        {
            // If a selection changes during I/O, load the latest selected period before rendering.
            int selected;
            do
            {
                selected = requestedPeriod;
                CancellationToken token = lifetime.Token;
                var since = StatisticsAnalysis.SinceUtc(selected, DateTimeOffset.UtcNow);
                var (items, skipped) = await store.ReadUsageWithSkippedAsync(since, token);
                var nextReport = await Task.Run(() => StatisticsAnalysis.Build(items, token), token);
                if (disposed || stopping || token.IsCancellationRequested) return;
                report = nextReport;
                summary.StringValue = SummaryFor(items.Count, report.Policies, skipped);
                Fill();
            } while (selected != requestedPeriod);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!disposed && !stopping) summary.StringValue = StatisticsText.ReadFailed(error.Message);
        }
        finally
        {
            loading = false;
            if (!disposed && !stopping) refresh.Enabled = true;
        }
    }

    private void Fill()
    {
        if (disposed || stopping || report is null) return;
        rows.Value = FormatRows(StatisticsText.Rows(report, (int)group.IndexOfSelectedItem));
    }

    // The empty-state line names the Mac way to record; Windows has no second line wording here.
    internal static string SummaryFor(int count, string policies, int skipped) =>
        StatisticsText.Summary(count, policies, skipped) + (count == 0 ? "\n" + StatisticsText.Empty(HowToRecord) : "");

    internal static string FormatRows(IReadOnlyList<StatisticsRow> source)
    {
        var text = new StringBuilder();
        foreach (StatisticsRow row in source)
        {
            EventCounts counts = row.Counts;
            string provider = Enum.TryParse(row.Provider, out ProviderKind kind) ? ProviderNames.Provider(kind) : row.Provider;
            text.AppendLine($"{provider} · {row.Group} · {StatisticsText.SampleSize(counts)}" +
                (StatisticsText.IsSmallSample(counts) ? " (소표본)" : ""));
            text.AppendLine($"Success {counts.Cell(counts.Success)} · Slow {counts.Cell(counts.Slow)} · Error {counts.Cell(counts.Error)}");
            text.AppendLine($"Interrupted {counts.Cell(counts.Interrupted)} · 문제 체감 {counts.Cell(counts.Adverse)}");
            text.AppendLine();
        }
        return text.ToString();
    }

    internal string SummaryText => summary.StringValue;

    // Smoke: the shared explanation (longer than the old Mac text) must fit its label without clipping.
    internal bool ExplanationFits()
    {
        CGSize needed = explanation.Cell.CellSizeForBounds(new CGRect(0, 0, explanation.Frame.Width, 10_000));
        return explanation.StringValue == StatisticsText.Explanation && needed.Height <= explanation.Frame.Height + 0.5;
    }

    internal void Show()
    {
        if (disposed || stopping) return;
        NSApplication.SharedApplication.Activate();
        Window.MakeKeyAndOrderFront(null);
        _ = RefreshAsync();
    }

    internal async Task StopAsync()
    {
        stopping = true;
        if (!disposed) lifetime.Cancel();
        await currentLoad;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        Window.Close();
        Window.Dispose();
        // Normal app shutdown awaits StopAsync first. Direct disposal still cancels safely.
        if (!loading) lifetime.Dispose();
    }
}
