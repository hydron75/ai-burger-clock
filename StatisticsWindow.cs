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
        period.Items.AddRange(StatisticsText.Periods.ToArray());
        period.SelectedIndex = 0;
        toolbar.Controls.Add(period);
        refresh = new Button { Text = "새로 고침", AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
        toolbar.Controls.Add(refresh);
        summary = new Label { Dock = DockStyle.Fill, Text = StatisticsText.Loading, AutoEllipsis = true };
        TabControl tabs = new() { Dock = DockStyle.Fill };
        AddTab(tabs, "providers", StatisticsText.Sections[0]);
        AddTab(tabs, "hours", StatisticsText.Sections[1]);
        AddTab(tabs, "schedules", StatisticsText.Sections[2]);
        AddTab(tabs, "official", StatisticsText.Sections[3]);
        AddTab(tabs, "operationalHours", StatisticsText.Sections[4]);
        Label explanation = new()
        {
            Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0),
            Text = StatisticsText.Explanation,
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
        summary.Text = StatisticsText.Loading;
        try
        {
            CancellationToken token = lifetime.Token;
            DateTimeOffset? since = StatisticsAnalysis.SinceUtc(period.SelectedIndex, utcNow());
            (IReadOnlyList<UsageMeasurement> items, int skipped) = await store.ReadUsageWithSkippedAsync(since, token);
            StatisticsReport report = await Task.Run(() => StatisticsAnalysis.Build(items, token), token);
            if (IsDisposed || token.IsCancellationRequested) return;
            Fill(grids["providers"], StatisticsText.Rows(report, 0));
            Fill(grids["hours"], StatisticsText.Rows(report, 1));
            Fill(grids["schedules"], StatisticsText.Rows(report, 2));
            Fill(grids["official"], StatisticsText.Rows(report, 3));
            Fill(grids["operationalHours"], StatisticsText.Rows(report, 4));
            summary.Text = StatisticsText.Summary(items.Count, report.Policies, skipped) + "\n" +
                (items.Count == 0 ? StatisticsText.Empty("Provider 행이나 트레이 메뉴에서 사용 경험을 기록하세요.") : "각 탭에서 Provider·시간대·Schedule·공식 상태별 체감을 비교할 수 있습니다.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed) summary.Text = StatisticsText.ReadFailed(ex.Message);
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
                int index = grid.Rows.Add(WindowsProviderNames.Provider(row.Provider), row.Group, StatisticsText.SampleSize(c),
                    c.Cell(c.Success), c.Cell(c.Slow), c.Cell(c.Error), c.Cell(c.Interrupted), c.Cell(c.Adverse));
                grid.Rows[index].DefaultCellStyle.ForeColor = c.Total == 0 ? Color.Gray : Color.FromArgb(40, 40, 40);
                grid.Rows[index].Cells[1].ToolTipText = row.Group;
                if (StatisticsText.IsSmallSample(c)) grid.Rows[index].Cells[2].ToolTipText = StatisticsText.SmallSampleNote;
            }
            grid.ClearSelection();
        }
        finally { grid.ResumeLayout(); }
    }
}
