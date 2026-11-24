namespace AiBurgerClock;

internal static class WindowsClaudeAuthUiChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-claude-auth-ui-").FullName;
        try
        {
            var now = new DateTimeOffset(2030, 7, 12, 3, 0, 0, TimeSpan.Zero);
            var expiry = now.AddDays(7);
            var store = new UsageStore(Path.Combine(directory, "synthetic.db"));
            using var status = new HttpClient(new TestStatusHttpHandler());
            var requests = new List<WindowsNotificationRequest>();
            long notificationMs = 0;
            using var context = new TrayApplicationContext(true, () => now, usageStore: store, statusHttpClient: status,
                accountQuotaClient: new FakeClient(now, expiry), notificationMilliseconds: () => notificationMs,
                notificationSink: requests.Add, geminiSession: new WindowsGeminiWebChecks.FakeSession());
            var quotas = context.QuotaMonitor!;
            try
            {
                await context.Initialization;
                await MonitorTests.WaitUntilAsync(() => quotas.LatestClaudeAuth is not null);
                var item = context.TrayIcon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>()
                    .Single(item => item.Text == ClaudeAuthCheck.MenuLabel);
                context.RefreshStatus(false);
                var box = context.StatusWindow.QuotaView.Controls["ClaudeQuotaBox"]!;
                check(box.Controls.OfType<Label>().Any(label => label.Text == ClaudeAuthCheck.ExpiringText),
                    "Claude warning line is visible even with OS alerts turned off");
                check(!item.Checked && !requests.Any(request => request.Kind == WindowsQuotaNotificationKind.ClaudeAuthExpiry),
                    "Claude warning menu defaults off and submits nothing");

                item.PerformClick();
                await MonitorTests.WaitUntilAsync(() => item.Checked);
                notificationMs += 30000;
                context.RefreshStatus(false);
                await MonitorTests.WaitUntilAsync(() => requests.Any(request => request.Kind == WindowsQuotaNotificationKind.ClaudeAuthExpiry));
                context.RefreshStatus(false);
                var alert = requests.Single(request => request.Kind == WindowsQuotaNotificationKind.ClaudeAuthExpiry);
                check(alert.Provider == WindowsNotificationProvider.Claude && alert.Body == ClaudeAuthCheck.ExpiringText,
                    "Claude warning submits one account-free message through the fake transport");
                check(await store.ReadClaudeAuthWarningAsync() == (true, expiry), "the sent cycle is persisted across process restart");

                now = expiry;
                context.RefreshStatus(false);
                var expiredBox = context.StatusWindow.QuotaView.Controls["ClaudeQuotaBox"]!;
                check(expiredBox.Controls.OfType<Label>().Any(label => label.Text == ClaudeAuthCheck.LoginRequiredText),
                    "the Claude card switches from warning to login required at the upper bound");
            }
            finally { await quotas.StopAsync(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class FakeClient(DateTimeOffset now, DateTimeOffset expiry) : IAccountQuotaClient, IClaudeAuthClient
    {
        private readonly TestAccountQuotaClient quotas = new(() => now);
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) =>
            quotas.ReadAsync(provider, cancellationToken);
        public Task<ClaudeAuthStatus> ReadClaudeAuthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ClaudeAuthStatus(ClaudeAuthOutcome.SignedIn, "claude.ai", expiry));
    }
}
