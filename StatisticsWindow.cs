namespace AiBurgerClock;

internal sealed class StatisticsWindow : Form
{
    private readonly UsageStore store;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly ComboBox period;
    private readonly Button refresh;
    private readonly Label summary;
    private readonly Dictionary<string, DataGridView> grids = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Font formFont = new("Segoe UI", 9F); // Not disposed by the form itself.
    private bool loading;

    public StatisticsWindow(UsageStore store, Func<DateTimeOffset>? utcNow = null)
    {
        SuspendLayout();
        this.store = store;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        Text = "AI Burger Clock · Statistics";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(860, 525);
        MinimumSize = new Size(720, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = formFont;
        BackColor = Color.FromArgb(248, 249, 250);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Fill, WrapContents = false };
        toolbar.Controls.Add(new Label { Text = "기간", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        period = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        period.Items.AddRange(["최근 7일", "최근 30일", "전체"]);
        period.SelectedIndex = 0;
        toolbar.Controls.Add(period);
        refresh = new Button { Text = "새로 고침", AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
        toolbar.Controls.Add(refresh);
        summary = new Label { Dock = DockStyle.Fill, Text = "불러오는 중…", AutoEllipsis = true };
        TabControl tabs = new() { Dock = DockStyle.Fill };
        AddTab(tabs, "providers", "Provider 비교");
        AddTab(tabs, "hours", "KST 시간대");
        AddTab(tabs, "schedules", "Schedule / DST");
        AddTab(tabs, "official", "공식 상태 × 체감");
        AddTab(tabs, "operationalHours", "공식 정상 시간대");
        Label explanation = new()
        {
            Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0),
            Text = "각 비율의 분모는 해당 행의 n입니다. 문제 체감 = Slow + Error + Interrupted. n < 30은 소표본입니다.\n" +
                "직접 선택해 남긴 체감 기록이며 전체 사용의 장애율·인과관계를 뜻하지 않습니다. 공식 상태와 체감은 별개입니다.\n" +
                "Schedule 행은 겹치는 집단입니다. 정책별·공휴일 ON/OFF별 행으로 비교하세요. 이전 기록은 재분류하지 않습니다.",
            ForeColor = Color.FromArgb(90, 90, 90)
        };
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(summary, 0, 1);
        layout.Controls.Add(tabs, 0, 2);
        layout.Controls.Add(explanation, 0, 3);
        Controls.Add(layout);
        Shown += async (_, _) => await RefreshAsync();
        period.SelectedIndexChanged += async (_, _) => await RefreshAsync();
        refresh.Click += async (_, _) => await RefreshAsync();
        FormClosed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        ResumeLayout(false);
        PerformLayout();
    }

    private void AddTab(TabControl tabs, string key, string title)
    {
        TabPage page = new(title);
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
            BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        foreach ((string label, int min, float weight) in new[]
        {
            ("Provider", 65, 70f), ("구간 / 조건", 140, 180f), ("n", 50, 55f),
            ("Success n / %", 85, 95f), ("Slow n / %", 85, 95f), ("Error n / %", 85, 95f),
            ("Interrupted n / %", 95, 105f), ("문제 체감 n / %", 95, 105f)
        })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = label, MinimumWidth = min, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        page.Controls.Add(grid);
        tabs.TabPages.Add(page);
        grids.Add(key, grid);
    }

    private async Task RefreshAsync()
    {
        if (loading || lifetime.IsCancellationRequested) return;
        loading = true;
        period.Enabled = refresh.Enabled = false;
        summary.Text = "불러오는 중…";
        try
        {
            CancellationToken token = lifetime.Token;
            DateTimeOffset? since = StatisticsAnalysis.SinceUtc(period.SelectedIndex, utcNow());
            (IReadOnlyList<UsageMeasurement> items, int skipped) = await store.ReadUsageWithSkippedAsync(since, token);
            StatisticsReport report = await Task.Run(() => StatisticsAnalysis.Build(items, token), token);
            if (IsDisposed || token.IsCancellationRequested) return;
            Fill(grids["providers"], report.Providers);
            Fill(grids["hours"], report.Hours);
            Fill(grids["schedules"], report.Schedules);
            Fill(grids["official"], report.Official);
            Fill(grids["operationalHours"], report.OperationalHours);
            summary.Text = $"직접 기록한 표본 n = {items.Count:N0} · 정책: {report.Policies}" +
                (skipped > 0 ? $" · 읽을 수 없는 기록 {skipped:N0}건 제외" : "") + "\n" +
                (items.Count == 0 ? "No data · Provider 행이나 트레이 메뉴에서 사용 경험을 기록하세요." : "각 탭에서 Provider·시간대·Schedule·공식 상태별 체감을 비교할 수 있습니다.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed) summary.Text = "기록을 읽을 수 없습니다: " + ex.Message;
        }
        finally
        {
            loading = false;
            if (!IsDisposed) period.Enabled = refresh.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) formFont.Dispose();
    }

    private static void Fill(DataGridView grid, IReadOnlyList<StatisticsRow> rows)
    {
        grid.SuspendLayout();
        try
        {
            grid.Rows.Clear();
            foreach (StatisticsRow row in rows)
            {
                EventCounts c = row.Counts;
                int index = grid.Rows.Add(row.Provider, row.Group, c.Total == 0 ? "n=0" : $"n={c.Total:N0}",
                    c.Cell(c.Success), c.Cell(c.Slow), c.Cell(c.Error), c.Cell(c.Interrupted), c.Cell(c.Adverse));
                grid.Rows[index].DefaultCellStyle.ForeColor = c.Total == 0 ? Color.Gray : Color.FromArgb(40, 40, 40);
                grid.Rows[index].Cells[1].ToolTipText = row.Group;
                if (c.Total is > 0 and < 30) grid.Rows[index].Cells[2].ToolTipText = "소표본: 이 비율만으로 시간대의 우열을 판단하지 마세요.";
            }
            grid.ClearSelection();
        }
        finally { grid.ResumeLayout(); }
    }
}
