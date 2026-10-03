using System.Text;
using AppKit;
using CoreGraphics;

namespace AiBurgerClock;

internal sealed class MacStatisticsWindow : IDisposable
{
    private readonly UsageStore store;
    internal NSWindow Window { get; }
    private readonly NSPopUpButton period;
    private readonly NSPopUpButton group;
    private readonly NSTextField summary;
    private readonly NSTextView rows;
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
        period.AddItems(["최근 7일", "최근 30일", "전체"]);
        period.SelectItem(0);
        root.AddSubview(period);
        group = new NSPopUpButton(new CGRect(163, 478, 242, 28), false);
        group.AddItems(["Provider 비교", "KST 시간대", "Schedule / DST", "공식 상태 × 체감", "공식 정상 시간대"]);
        group.SelectItem(0);
        root.AddSubview(group);
        refresh = MacStatusWindow.Button(root, "새로 고침", new CGRect(422, 478, 116, 28), () => _ = RefreshAsync());
        summary = MacStatusWindow.Label(root, "불러오는 중…", 428, 43, 11);
        rows = MacStatusWindow.TextArea(root, new CGRect(14, 86, 762, 331), 12);
        MacStatusWindow.Label(root,
            "각 행의 n이 비율의 분모이며 n < 30은 소표본입니다. 문제 체감 = Slow + Error + Interrupted.\n" +
            "직접 남긴 체감 기록이지 전체 사용의 장애율·인과관계가 아닙니다. 공식 상태와 체감은 별개입니다.\n" +
            "Schedule 집단은 중복될 수 있습니다. 정책·공휴일 ON/OFF를 구분하며 이전 기록은 재분류하지 않습니다.",
            10, 64, 11);
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
        summary.StringValue = "불러오는 중…";
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
                summary.StringValue = $"직접 기록한 표본 n = {items.Count:N0} · 정책: {report.Policies}" +
                    (skipped > 0 ? $" · 읽을 수 없는 기록 {skipped:N0}건 제외" : "") +
                    (items.Count == 0 ? "\nNo data · 메뉴바의 사용 경험 기록을 이용하세요." : "");
                Fill();
            } while (selected != requestedPeriod);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!disposed && !stopping) summary.StringValue = "통계 읽기 실패: " + error.Message;
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
        IReadOnlyList<StatisticsRow> selected = (int)group.IndexOfSelectedItem switch
        {
            0 => report.Providers,
            1 => report.Hours,
            2 => report.Schedules,
            3 => report.Official,
            _ => report.OperationalHours
        };
        rows.Value = FormatRows(selected);
    }

    internal static string FormatRows(IReadOnlyList<StatisticsRow> source)
    {
        var text = new StringBuilder();
        foreach (StatisticsRow row in source)
        {
            EventCounts counts = row.Counts;
            text.AppendLine($"{row.Provider} · {row.Group} · n={counts.Total:N0}" +
                (counts.Total is > 0 and < 30 ? " (소표본)" : ""));
            text.AppendLine($"Success {counts.Cell(counts.Success)} · Slow {counts.Cell(counts.Slow)} · Error {counts.Cell(counts.Error)}");
            text.AppendLine($"Interrupted {counts.Cell(counts.Interrupted)} · 문제 체감 {counts.Cell(counts.Adverse)}");
            text.AppendLine();
        }
        return text.ToString();
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
