using System.Reflection;
using System.Net;

namespace AiBurgerClock;

// Native message-loop checks with synthetic snapshots and an in-memory vault/HTTP sender.
internal static class WindowsQuotaExportUiChecks
{
    private sealed class Client(WindowsQuotaExportChecks.Clock time) : IAccountQuotaClient
    {
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult(WindowsQuotaExportChecks.Readings(time.GetUtcNow()).Single(s => s.Provider == provider).Reading!);
    }

    internal static async Task RunAsync(string? reportDirectory)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Mobile export UI: " + label);
            Console.WriteLine("PASS: Mobile export " + label);
        }
        static async Task Wait(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Synthetic mobile export UI wait");
                await Task.Delay(10);
            }
        }
        using var fixture = new WindowsQuotaExportChecks.Fixture(false);
        await fixture.Service.Initialization;
        using var context = new TrayApplicationContext(true, fixture.Time.GetUtcNow, enableServices: false,
            usageStore: new UsageStore(Path.Combine(fixture.DirectoryPath, "ui.db")), mobileExportService: fixture.Service,
            notificationSink: _ => throw new InvalidOperationException("Synthetic UI must not submit notifications"));
        var timer = (System.Windows.Forms.Timer)typeof(TrayApplicationContext).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        timer.Stop();
        Check(context.MobileExportMenuItem.Text == "모바일 사이트 연동…", "tray entry text");
        context.MobileExportMenuItem.PerformClick();
        using var window = Application.OpenForms.OfType<WindowsQuotaExportWindow>().Single();
        window.Text += " (합성 데이터)";
        await Wait(() => window.SaveButton.Enabled);
        Check(!window.EnabledCheck.Checked && !fixture.Service.Status.Enabled, "setting defaults OFF");
        Check(window.CredentialLabel.Text.Contains("저장됨") && !window.CredentialLabel.Text.Contains(WindowsQuotaExportChecks.Credentials.SyntheticToken), "vault presence does not reveal value");
        Check(window.TokenInput.UseSystemPasswordChar && window.TokenInput.TextLength == 0, "replacement field is masked and never prefilled");
        Check(!window.TestButton.Enabled && fixture.Http.Count == 0 && fixture.Vault.Reads == 0, "opening settings does not send or read token");
        window.SiteInput.Text = "https://quota.example.test";
        Check(!window.TestButton.Enabled, "unsaved address cannot be tested");
        window.SaveButton.PerformClick();
        await Wait(() => window.TestButton.Enabled);
        Check(!fixture.Service.Status.Enabled && fixture.Http.Count == 0, "saving an OFF address does not send");
        window.ReplaceButton.PerformClick();
        Check(window.TokenInput.Visible && window.TokenInput.UseSystemPasswordChar, "explicit token replacement opens masked input");
        const string replacement = "SYNTHETIC-REPLACEMENT-NOT-A-CREDENTIAL";
        window.TokenInput.Text = replacement;
        window.SaveButton.PerformClick();
        await Wait(() => window.TestButton.Enabled && fixture.Vault.Writes == 1);
        Check(fixture.Vault.Value == replacement && window.TokenInput.TextLength == 0, "save replaces only fake vault and clears input");
        Check(!File.ReadAllText(fixture.SettingsPath).Contains(replacement), "saved Windows file excludes token");
        window.TestButton.PerformClick();
        await Wait(() => fixture.Service.Status.LastSuccessUtc is not null);
        await fixture.Service.WaitForIdleAsync();
        Check(fixture.Http.Count == 1 && !fixture.Service.Status.Enabled, "explicit test sends once while leaving setting OFF");
        await Wait(() => window.StatusLabel.Text.Contains("12:00:00 KST"));
        Check(!window.StatusLabel.Text.Contains(replacement) && !window.StatusLabel.Text.Contains("SYNTHETIC-PRIVATE"), "status has generalized metadata only");
        window.EnabledCheck.Checked = true; window.SaveButton.PerformClick();
        await Wait(() => window.TestButton.Enabled && fixture.Service.Status.Enabled);
        Check(fixture.Http.Count == 1, "enabling does not add a startup/configuration transmission");

        var server = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Http.Reply = (_, cancellation) => server.Task.WaitAsync(cancellation);
        fixture.Service.Completed(fixture.States);
        await Wait(() => fixture.Http.Count == 2);
        using var heartbeat = new System.Windows.Forms.Timer { Interval = 20 };
        var uiAlive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.Tick += (_, _) => { heartbeat.Stop(); uiAlive.TrySetResult(); };
        heartbeat.Start();
        await uiAlive.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!server.Task.IsCompleted && fixture.Service.Status.Sending, "UI timer continues while HTTPS response is held");
        using (var monitor = new AccountQuotaMonitor(new Client(fixture.Time), utcNow: fixture.Time.GetUtcNow))
        using (var bridge = new WindowsQuotaExportBridge(monitor, fixture.Service.Completed))
        {
            await Task.WhenAll(Enum.GetValues<QuotaProvider>().Select(p => monitor.RefreshOnceAsync(p))).WaitAsync(TimeSpan.FromSeconds(5));
            Check(monitor.Snapshot().All(s => !s.IsRefreshing && s.LastSuccessfulCheckUtc is not null) && !server.Task.IsCompleted,
                "all Provider reads finish while export server is slow");
        }
        server.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await fixture.Service.WaitForIdleAsync();
        await Wait(() => window.StatusLabel.Text.Contains("12:00:30 KST") && window.StatusLabel.Text.Contains("서버 일시 오류"));
        Check(fixture.Service.Status.RetryAtUtc == fixture.Time.GetUtcNow().AddSeconds(30), "retry time and generalized failure shown");
        if (reportDirectory is not null)
        {
            Directory.CreateDirectory(reportDirectory);
            using var image = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(reportDirectory, "mobile-sites-settings-synthetic.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        fixture.Http.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        fixture.Time.Advance(TimeSpan.FromSeconds(30)); fixture.Service.Tick();
        await fixture.Service.WaitForIdleAsync();
        Check(fixture.Http.Count == 3 && fixture.Service.Status.RetryAtUtc is null, "retry sends latest coalesced snapshot once");
        window.EnabledCheck.Checked = false; window.SaveButton.PerformClick();
        await Wait(() => window.TestButton.Enabled && !fixture.Service.Status.Enabled);
        fixture.Time.Advance(TimeSpan.FromHours(2)); fixture.Service.Tick(); fixture.Service.Completed(fixture.States);
        await fixture.Service.WaitForIdleAsync();
        Check(fixture.Http.Count == 3, "disabled UI setting stops automatic completion and heartbeat sends");
        Check(fixture.Vault.Writes == 1 && fixture.Http.Requests.All(r => r.Uri.Host == "quota.example.test"), "only fake vault and HTTP used; zero operating registration/site calls");
        window.Close();
    }
}
