namespace AiBurgerClock;

internal static class ClaudeAuthTests
{
    private static readonly DateTimeOffset Expiry = new(2030, 12, 31, 16, 0, 0, TimeSpan.Zero);
    private const string AuthJson = """
        {
            "loggedIn": true,
            "authMethod": "claude.ai",
            "loginExpiresAt": "2031-01-01T01:00:00+09:00",
            "email": "synthetic-sensitive@example.invalid",
            "orgId": "synthetic-sensitive-org",
            "orgName": "synthetic-sensitive-name",
            "planTier": "synthetic-sensitive-plan",
            "subscriptionType": "synthetic-sensitive-subscription",
            "configDirectory": "synthetic-sensitive-directory",
            "projectsDirectory": "synthetic-sensitive-projects",
            "other": { "nested": "synthetic-sensitive-nested" }
        }
        """;

    internal static async Task<int> RunAsync(string directory)
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Claude auth: " + label);
            checks++;
        }
        var status = ClaudeAuthStatus.Parse(0, AuthJson);
        Check(status == new ClaudeAuthStatus(ClaudeAuthOutcome.SignedIn, "claude.ai", Expiry),
            "parser keeps the supported method and normalizes the upper bound from an offset");
        Check(!status.ToString().Contains("synthetic-sensitive", StringComparison.Ordinal), "unknown and private fields cannot enter the model");
        Check(ClaudeAuthStatus.Parse(1, "") is { Outcome: ClaudeAuthOutcome.SignedOut }, "exit code 1 is authoritative");
        Check(ClaudeAuthStatus.Parse(1, AuthJson) is { Outcome: ClaudeAuthOutcome.SignedOut }, "exit code 1 overrides a contradictory reply");
        Check(ClaudeAuthStatus.Parse(0, """{"loggedIn":false,"authMethod":"none"}""") is { Outcome: ClaudeAuthOutcome.SignedOut },
            "false loggedIn also requires login");
        Check(ClaudeAuthStatus.Parse(2, AuthJson) == ClaudeAuthStatus.Unknown, "other exit code is unknown");
        var noExpiry = ClaudeAuthStatus.Parse(0, """{"loggedIn":true,"authMethod":"claude.ai","loginExpiresAt":null}""");
        Check(noExpiry is { Outcome: ClaudeAuthOutcome.SignedIn, LoginExpiresAtUtc: null }, "explicit null is a valid unknown end date");
        foreach (var invalid in new[]
        {
            "", "{", "[]", """{"loggedIn":"true","authMethod":"claude.ai","loginExpiresAt":null}""",
            """{"loggedIn":true,"authMethod":"claude.ai"}""",
            """{"loggedIn":true,"authMethod":"unknown-account-data","loginExpiresAt":null}""",
            """{"loggedIn":true,"authMethod":"claude.ai","loginExpiresAt":123}""",
            """{"loggedIn":true,"authMethod":"claude.ai","loginExpiresAt":"2031-01-01T01:00:00"}""",
            """{"loggedIn":true,"authMethod":"claude.ai","loginExpiresAt":null,"loginExpiresAt":null}"""
        })
            Check(ClaudeAuthStatus.Parse(0, invalid) == ClaudeAuthStatus.Unknown, "unrecognized reply fails closed");

        var now = Expiry.AddDays(-7);
        var auth = new ClaudeAuthCheck(status, now);
        var state = new QuotaState(QuotaProvider.Claude, Failure: new(QuotaFailureKind.Rejected, "claude.no-usage-report"), ClaudeAuth: auth);
        var normal = QuotaPanelModel.Section(state, now.AddTicks(-1));
        Check(normal.Scope is null, "outside the warning window the card has no warning");
        Check(normal.Detail.Contains("로그인 종료: 늦어도 01-01 01:00 KST(더 일찍 끝날 수 있음)", StringComparison.Ordinal) &&
            normal.Detail.Contains("로그인 상태 마지막 확인: 12-25 01:00 KST", StringComparison.Ordinal) &&
            !normal.Detail.Contains("synthetic-sensitive", StringComparison.Ordinal), "upper bound and last checked time display in KST across New Year");
        Check(QuotaPanelModel.Section(state, now).Scope is { Text: ClaudeAuthCheck.ExpiringText, Tone: PanelTone.Caution },
            "exact seven-day boundary adds the warning line");
        Check(auth.WarningExpiry(now) == Expiry && auth.WarningExpiry(now, Expiry.ToOffset(TimeSpan.FromHours(9))) is null,
            "one warning per absolute end date across offsets");
        Check(auth.WarningExpiry(Expiry.AddTicks(-1)) == Expiry && auth.WarningExpiry(Expiry) is null, "no warning at or after expiry");
        var renewed = auth with { Status = status with { LoginExpiresAtUtc = Expiry.AddDays(1) } };
        Check(renewed.WarningExpiry(now.AddDays(1), Expiry) == Expiry.AddDays(1), "a changed bound can warn in the new cycle");
        var expired = QuotaPanelModel.Section(state, Expiry);
        Check(expired.Rows.Single() is { Text: ClaudeAuthCheck.LoginRequiredText, Tone: PanelTone.Danger } &&
            !expired.Detail.Contains("한도 응답 없음", StringComparison.Ordinal), "at the end date login required replaces the generic error");
        var previous = state with { Reading = new(QuotaProvider.Claude, [new("session", "5시간", 61, Expiry.AddHours(1), 300)]) };
        var expiredWithValue = QuotaPanelModel.Section(previous, Expiry.AddMinutes(1));
        Check(expiredWithValue.Scope?.Text == ClaudeAuthCheck.LoginRequiredText &&
            expiredWithValue.Rows.Single().Tone == PanelTone.Muted, "previous quota remains visible beside the login prompt");
        var signedOut = state with { ClaudeAuth = auth with { Status = ClaudeAuthStatus.Parse(1, "") } };
        Check(QuotaPanelModel.Section(signedOut, now).Rows.Single().Text == ClaudeAuthCheck.LoginRequiredText, "signed out before expiry still prompts");
        var withoutExpiry = state with { ClaudeAuth = auth with { Status = noExpiry } };
        var missing = QuotaPanelModel.Section(withoutExpiry, now);
        Check(missing.Scope is null && missing.Detail.Contains("종료 시점 미제공", StringComparison.Ordinal), "null expiry never warns");
        var unknown = QuotaPanelModel.Section(state with { ClaudeAuth = auth with { Status = ClaudeAuthStatus.Unknown } }, now);
        Check(unknown.Scope is null && unknown.Detail.Contains(ClaudeAuthCheck.UnknownText, StringComparison.Ordinal) &&
            unknown.Rows.Single().Text == QuotaPanelModel.FailureMessage(QuotaProvider.Claude, QuotaFailureKind.Rejected), "unknown auth has its own detail and leaves the quota diagnosis intact");
        foreach (string kind in new[] { "api_key", "api_key_helper", "oauth_token", "third_party" })
        {
            var nonSubscription = ClaudeAuthStatus.Parse(0, AuthJson.Replace("claude.ai", kind, StringComparison.Ordinal));
            var section = QuotaPanelModel.Section(state with { ClaudeAuth = auth with { Status = nonSubscription } }, now);
            Check(section.Scope is null && section.Detail.Contains($"구독 로그인이 아님(인증 방식: {kind})", StringComparison.Ordinal),
                "a stored subscription bound does not warn users of a different active credential");
        }

        var start = AccountQuotaClient.CreateClaudeAuthStartInfo(Path.GetFullPath("synthetic-claude"), Path.GetTempPath());
        Check(start.ArgumentList.SequenceEqual(["auth", "status"]) && start.Environment["DISABLE_AUTOUPDATER"] == "1" &&
            AccountQuotaClient.ClaudeAuthTimeout == TimeSpan.FromSeconds(15), "fixed status-only command, auto-update off and 15-second deadline");
        Check(!start.UseShellExecute && start.CreateNoWindow && start.RedirectStandardOutput && start.RedirectStandardError,
            "raw pipes are captured without a shell or window");
        Check(await new AccountQuotaClient(claudeExecutable: "synthetic-no-path").ReadClaudeAuthAsync(default) == ClaudeAuthStatus.Unknown,
            "a missing executable reports only unknown");
        Check(ClaudeAuthCheck.IsDue(now, null, false), "startup is due immediately");
        Check(!ClaudeAuthCheck.IsDue(now.AddHours(6).AddTicks(-1), now, false) &&
            ClaudeAuthCheck.IsDue(now.AddHours(6), now, false), "normal interval boundary is exactly six hours");
        Check(!ClaudeAuthCheck.IsDue(now.AddMinutes(15).AddTicks(-1), now, true) &&
            ClaudeAuthCheck.IsDue(now.AddMinutes(15), now, true), "early retry needs a Claude quota failure and at least 15 minutes");

        var fake = new FakeClient();
        var clock = new Clock(now);
        using (var monitor = new AccountQuotaMonitor(fake, utcNow: clock.Read))
        {
            monitor.Start();
            await MonitorTests.WaitUntilAsync(() => monitor.LatestClaudeAuth is not null &&
                monitor.Snapshot().Single(row => row.Provider == QuotaProvider.Claude).LastSuccessfulCheckUtc is not null);
            Check(fake.AuthCalls == 1 && monitor.Snapshot().Single(row => row.Provider == QuotaProvider.Claude).ClaudeAuth is not null,
                "startup checks auth once and publishes only to Claude");
            clock.Set(now.AddMinutes(15));
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Codex);
            Check(fake.AuthCalls == 1, "another provider's failure does not wake the checker");
            fake.FailClaude = true;
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
            await MonitorTests.WaitUntilAsync(() => fake.AuthCalls == 2 && monitor.LatestClaudeAuth?.CheckedAtUtc == clock.Read(), 5000);
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
            await monitor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(fake.AuthCalls == 2, "a Claude failure wakes the checker once and the next failure is throttled");
        }

        foreach (var staleAuth in new[]
        {
            ClaudeAuthStatus.Parse(1, ""),
            ClaudeAuthStatus.Unknown,
            status with { LoginExpiresAtUtc = now }
        })
        {
            var recoveryClock = new Clock(now);
            var recoveryClient = new FakeClient { AuthStatus = staleAuth, FailClaude = true };
            using var monitor = new AccountQuotaMonitor(recoveryClient, utcNow: recoveryClock.Read);
            monitor.Start();
            await MonitorTests.WaitUntilAsync(() => monitor.LatestClaudeAuth?.Status == staleAuth &&
                monitor.Snapshot().Any(row => row.Provider == QuotaProvider.Claude && row.CheckedAtUtc is not null && !row.IsRefreshing));
            var releaseAuth = new TaskCompletionSource<ClaudeAuthStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            recoveryClient.SetNextAuthResult(releaseAuth);
            recoveryClock.Set(now.AddMinutes(1));
            recoveryClient.FailClaude = false;
            // Use the automatic path so the successful quota read is the only
            // trigger that can bypass the minimum interval in this step.
            monitor.RequestRefresh(queueWhileRefreshing: true);
            await MonitorTests.WaitUntilAsync(() => recoveryClient.AuthCalls == 2);
            var awaitingAuth = monitor.Snapshot().Single(row => row.Provider == QuotaProvider.Claude);
            var current = QuotaPanelModel.Section(awaitingAuth, recoveryClock.Read());
            Check(awaitingAuth.ClaudeAuth?.Status == staleAuth &&
                awaitingAuth.LastSuccessfulCheckUtc == recoveryClock.Read(),
                "first quota success bypasses the failure throttle while auth is still being rechecked");
            Check(current.Scope is null && current.Rows.Single().Tone == PanelTone.Good &&
                !current.Heading.Text.Contains("이전 조회값", StringComparison.Ordinal) &&
                !current.Detail.Contains(ClaudeAuthCheck.LoginRequiredText, StringComparison.Ordinal),
                "the new quota is current and no login prompt remains while rechecking stale auth");
            // An extra successful read before the recheck completes shares that recheck.
            await monitor.RefreshSourceIfDueAsync(QuotaProvider.Claude);
            recoveryClient.AuthStatus = status;
            releaseAuth.SetResult(status);
            await MonitorTests.WaitUntilAsync(() => monitor.LatestClaudeAuth?.Status == status);
            Check(QuotaPanelModel.Section(monitor.Snapshot().Single(row => row.Provider == QuotaProvider.Claude),
                recoveryClock.Read()).Scope?.Text == ClaudeAuthCheck.ExpiringText,
                "the verified login replaces the stale status and exposes its own expiry");
            recoveryClock.Set(now.AddMinutes(2));
            monitor.RequestRefresh();
            await MonitorTests.WaitUntilAsync(() => monitor.LatestClaudeAuth?.CheckedAtUtc == recoveryClock.Read());
            await monitor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(recoveryClient.AuthCalls == 3, "manual Refresh rechecks recent valid auth once");
        }

        var equallyRecent = previous with
        {
            ClaudeAuth = signedOut.ClaudeAuth,
            LastSuccessfulCheckUtc = signedOut.ClaudeAuth!.CheckedAtUtc
        };
        Check(QuotaPanelModel.Section(equallyRecent, now).Scope?.Text == ClaudeAuthCheck.LoginRequiredText,
            "an equally recent signed-out check is not ignored");

        var store = new UsageStore(Path.Combine(directory, "claude-auth.db"));
        Check(await store.ReadClaudeAuthWarningAsync() == (false, null), "the opt-in is off by default");
        await store.SetClaudeAuthWarningEnabledAsync(true);
        await store.RememberClaudeAuthWarningAsync(Expiry.ToOffset(TimeSpan.FromHours(9)));
        var restarted = new UsageStore(store.DatabasePath);
        Check(await restarted.ReadClaudeAuthWarningAsync() == (true, Expiry), "setting and deduplication survive restart");
        await restarted.SetClaudeAuthWarningEnabledAsync(false);
        Check(await store.ReadClaudeAuthWarningAsync() == (false, Expiry), "turning off preserves notification deduplication");
        return checks;
    }

    private sealed class Clock(DateTimeOffset now)
    {
        private long ticks = now.UtcTicks;
        public DateTimeOffset Read() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Set(DateTimeOffset at) => Interlocked.Exchange(ref ticks, at.UtcTicks);
    }

    private sealed class FakeClient : IAccountQuotaClient, IClaudeAuthClient
    {
        private int authCalls;
        private TaskCompletionSource<ClaudeAuthStatus>? nextAuthResult;
        internal bool FailClaude { get; set; }
        internal ClaudeAuthStatus AuthStatus { get; set; } = ClaudeAuthStatus.Parse(0, AuthJson);
        internal int AuthCalls => Volatile.Read(ref authCalls);
        internal void SetNextAuthResult(TaskCompletionSource<ClaudeAuthStatus> pending) =>
            Interlocked.Exchange(ref nextAuthResult, pending);
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) =>
            provider == QuotaProvider.Claude && !FailClaude
                ? Task.FromResult(new QuotaReading(provider, [new("session", "5시간", 20, null, 300)]))
                : Task.FromException<QuotaReading>(new InvalidDataException("Synthetic quota failure"));
        public Task<ClaudeAuthStatus> ReadClaudeAuthAsync(CancellationToken cancellationToken)
        {
            var pending = Interlocked.Exchange(ref nextAuthResult, null);
            Interlocked.Increment(ref authCalls);
            return pending?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(AuthStatus);
        }
    }
}
