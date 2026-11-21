using System.Drawing.Imaging;
using Microsoft.Win32;

namespace AiBurgerClock;

internal static class WindowsGeminiWebUiChecks
{
    internal static async Task RunAsync(TrayApplicationContext context, WindowsGeminiWebChecks.FakeSession session,
        Func<DateTimeOffset> clock, string? reportDirectory, Action<bool, string> check)
    {
        var monitor = context.QuotaMonitor!;
        var window = context.StatusWindow;
        var view = window.QuotaView;
        var menu = context.TrayIcon.ContextMenuStrip!;
        var root = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Gemini 사용량");
        var items = root.DropDownItems.OfType<ToolStripMenuItem>().ToArray();
        var refresh = window.Controls.OfType<Button>().Single(button => button.Text == "Refresh");
        QuotaState Gemini() => monitor.Snapshot().Single(state => state.Provider == QuotaProvider.Gemini);

        void WithMenu(Action action) => window.WithoutAutoHide(() =>
        {
            menu.Show(window, new Point(20, 20));
            root.ShowDropDown();
            try { action(); }
            finally { root.HideDropDown(); menu.Close(); }
            return true;
        });

        static async Task WaitAsync(Func<bool> ready)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!ready()) await Task.Delay(20, timeout.Token);
        }

        async Task ShowStateAsync(string name, string titleText, string detailText, int barCount, bool[] enabled)
        {
            context.RefreshStatus(false);
            var box = view.Controls.OfType<Panel>().Single(panel => panel.Name == "GeminiQuotaBox");
            view.ScrollControlIntoView(box);
            await SmokeUiProbe.SettleAsync(window);
            check(box.Controls.OfType<Label>().Any(label => label.Text.Contains(titleText, StringComparison.Ordinal)) &&
                box.Controls.OfType<QuotaBalanceBar>().Count() == barCount, $"Gemini web {name} renders its quota rows");
            SmokeTest.RenderAndCheckLayout(window, $"gemini-web-{name}.png", reportDirectory);

            view.ShowDetail(QuotaProvider.Gemini, box.PointToScreen(new Point(8, 8)));
            var popup = view.Details;
            check(popup.Visible && popup.DetailText == view.DetailFor(box) &&
                popup.DetailText.Contains("gemini.google.com/usage", StringComparison.Ordinal) &&
                popup.DetailText.Contains(detailText, StringComparison.Ordinal), $"Gemini web {name} shows its full detail");
            Save(popup, $"gemini-web-{name}-detail.png");
            view.HideDetail();

            WithMenu(() =>
            {
                check(root.DropDown.Visible && items.Select(item => item.Text).SequenceEqual(["로그인…", "끄기", "로그아웃"]) &&
                    items.Select(item => item.Enabled).SequenceEqual(enabled), $"Gemini web {name} enables its native menu actions");
                Save(root.DropDown, $"gemini-web-{name}-menu.png");
            });
        }

        void Save(Control control, string file)
        {
            if (reportDirectory is null) return;
            using var bitmap = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(reportDirectory, file), ImageFormat.Png);
        }

        context.ShowWindow();
        string originalTitle = window.Text;
        window.Text += " (합성 데이터)";
        if (!view.Visible) window.QuotaButton.PerformClick();
        try
        {
            await ShowStateAsync("off", "꺼짐 · 로그인하면 Gemini 앱 사용량 조회", "로그인하면 웹 사용량 조회가 켜집니다", 0,
                [true, false, true]);
            check(session.Reads == 0 && session.SignIns == 0, "Gemini web off did not initialize the fake reader");

            session.LoginSucceeds = true;
            session.ReadPage = () => new("https://gemini.google.com/app", null);
            WithMenu(() => items[0].PerformClick());
            await WaitAsync(() => Gemini().GeminiWeb?.Outcome == GeminiUsageOutcome.LoginRequired && !Gemini().IsRefreshing);
            await ShowStateAsync("login-required", "로그인 필요 · Gemini 앱 사용량", "로그인 필요 · Gemini 앱 사용량", 0, [true, true, true]);

            session.ReadPage = () => WindowsGeminiWebChecks.ValidPage(clock(), TimeZoneInfo.Local);
            WithMenu(() => items[0].PerformClick());
            await WaitAsync(() => Gemini().GeminiWeb?.Outcome == GeminiUsageOutcome.Ok && !Gemini().IsRefreshing);
            await ShowStateAsync("ok", "5시간", "자동 15분", 2, [false, true, true]);

            int reads = session.Reads;
            var trayRefresh = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "상태·한도 새로 고침");
            check(trayRefresh.ToolTipText?.Contains("로그인한 Gemini 앱 사용량", StringComparison.Ordinal) == true,
                "Native tray refresh tooltip includes signed-in Gemini app usage");
            session.ReadPage = () => new(GeminiUsagePage.Url, null);
            trayRefresh.PerformClick();
            await WaitAsync(() => Gemini().GeminiWeb?.Outcome == GeminiUsageOutcome.FormatChanged && !Gemini().IsRefreshing);
            check(session.Reads == reads + 1, "Native tray refresh wakes Gemini exactly once");

            context.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));
            await monitor.RefreshGeminiWebIfDueAsync();
            check(session.Reads == reads + 1, "Resume does not wake Gemini");
            context.RefreshAfterRecovery(); // The callback passed to NetworkRefreshScheduler, with no NIC changes.
            await monitor.RefreshGeminiWebIfDueAsync();
            check(session.Reads == reads + 1, "The network scheduler's recovery callback does not wake Gemini");
            await ShowStateAsync("format-changed", "형식 변경 · 조회 불가", "형식 변경 · 조회 불가", 0, [false, true, true]);

            session.ReadPage = () => GeminiUsagePageLoad.LoadFailed;
            context.ShowWindow();
            await SmokeUiProbe.SettleAsync(window);
            int statusClicks = 0;
            void CountClick(object? sender, EventArgs args) => statusClicks++;
            refresh.Click += CountClick;
            try
            {
                window.WithoutAutoHide(() =>
                {
                    refresh.Select();
                    refresh.PerformClick();
                    return true;
                });
            }
            finally { refresh.Click -= CountClick; }
            check(statusClicks == 1, "The reopened status window delivers one native Refresh click");
            await WaitAsync(() => Gemini().GeminiWeb?.Outcome == GeminiUsageOutcome.LoadFailed && !Gemini().IsRefreshing);
            check(session.Reads == reads + 2, "The native status Refresh button wakes Gemini exactly once");
            await ShowStateAsync("load-failed", "Gemini · 이전 조회값", "한도 조회 실패", 2, [false, true, true]);

            WithMenu(() => items[1].PerformClick());
            check(Gemini().GeminiWeb is { IsEnabled: false } && session.HasProfile, "Native Off keeps the synthetic profile");
            WithMenu(() => items[2].PerformClick());
            await WaitAsync(() => !session.HasProfile);
            check(Gemini().GeminiWeb is { IsEnabled: false } && session.ProfileDeletes == 1, "Native logout clears the synthetic profile once");
        }
        finally
        {
            view.HideDetail();
            window.Text = originalTitle;
            if (view.Visible) window.QuotaButton.PerformClick();
        }
    }
}
