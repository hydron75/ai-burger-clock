using static AiBurgerClock.QuotaFailureKind;
using static AiBurgerClock.QuotaProvider;

namespace AiBurgerClock;

internal static class QuotaFailureTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Quota failure: " + label);
            count++;
        }

        const string privateMarker = "SYNTHETIC-PRIVATE";
        async Task FailureAsync(Func<Task> run, QuotaFailureKind kind, string code)
        {
            try { await run(); }
            catch (QuotaReadException error)
            {
                Check(error.Failure == new QuotaFailure(kind, code) && error.Message == code && error.InnerException is null,
                    code + " carries the classified failure only");
                return;
            }
            throw new InvalidOperationException("Expected classified failure " + code);
        }

        Task<QuotaReading> ClaudeWire(string text) =>
            AccountQuotaClient.ReadClaudeProtocolAsync(new StringReader(text), default);
        Task<QuotaReading> CodexRpc(string text) =>
            AccountQuotaClient.ReadCodexProtocolAsync(new StringReader(text), new StringWriter(), default);
        Task<QuotaReading> Grok(string text) =>
            AccountQuotaClient.ReadGrokProtocolAsync(new StringReader(text), new StringWriter(), default);

        const string local = SharedQuotaProtocolTests.ClaudeResult;
        await FailureAsync(() => ClaudeWire(local.Replace("\"local_command\":\"usage\"", "\"local_command\":\"other\"")),
            Rejected, "claude.result-not-local");
        await FailureAsync(() => ClaudeWire(local), Rejected, "claude.no-usage-report");
        await FailureAsync(() => ClaudeWire("""{"type":"assistant","usage_report":{"rate_limits":{"limits":null}}}""" + "\n" + local),
            Rejected, "claude.no-usage-report");
        await FailureAsync(() => ClaudeWire("""{"type":"assistant","usage_report":{"rate_limits":{"limits":[{"kind":"unknown"}]}}}""" + "\n" + local),
            FormatChanged, "parser.claude");
        await FailureAsync(() => ClaudeWire(local + "\n" + local), FormatChanged, "claude.duplicate-result");
        await FailureAsync(() => ClaudeWire(privateMarker), FormatChanged, "cli.bad-line");
        await FailureAsync(() => ClaudeWire(new string('x', AccountQuotaClient.MaximumLineCharacters + 1)), FormatChanged, "cli.too-large");
        await FailureAsync(() => AccountQuotaClient.DrainErrorAsync(new StringReader(new string('x', AccountQuotaClient.MaximumOutputCharacters + 1)), default),
            FormatChanged, "cli.too-large");
        await FailureAsync(() => CodexRpc("""{"id":1,"error":{"message":"SYNTHETIC-PRIVATE"}}"""), Rejected, "codex.rpc-error");
        await FailureAsync(() => CodexRpc("""{"id":1,"result":null}"""), FormatChanged, "parser.codex.result");
        await FailureAsync(() => CodexRpc("""{"id":1,"result":{}}""" + "\n" + """{"id":2,"result":{}}"""), FormatChanged, "parser.codex");
        await FailureAsync(() => Grok("""{"jsonrpc":"2.0","id":1,"error":{"message":"SYNTHETIC-PRIVATE"}}"""), Rejected, "grok.rpc-error");
        await FailureAsync(() => Grok("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1}}""" + "\n" +
            """{"jsonrpc":"2.0","id":2,"result":{}}""" + "\n" + """{"jsonrpc":"2.0","id":3,"result":{}}"""), FormatChanged, "parser.grok");

        var now = GeminiUsagePageTests.Now;
        var known = new QuotaReading(Claude, [new("session", "5시간", 37, now.AddHours(2), 300)]);
        await FailureAsync(() => Task.FromResult(AccountQuotaClient.ValidateReading(known, Codex, now)), FormatChanged, "validate.reading");
        await FailureAsync(() => new AccountQuotaClient(claudeExecutable: "relative-claude").ReadAsync(Claude, default),
            NotInstalled, "cli.not-executable");
        Check(AccountQuotaClient.FailureFromRead(new FileNotFoundException(privateMarker)) == new QuotaFailure(NotInstalled, "cli.not-found") &&
            AccountQuotaClient.FailureFromRead(new UnauthorizedAccessException(privateMarker)) == new QuotaFailure(NotPermitted, "cli.workdir-denied") &&
            AccountQuotaClient.FailureFromRead(new IOException(privateMarker)) == new QuotaFailure(Unreachable, "cli.read-failed"),
            "legacy injected exceptions are normalized without carrying their messages");

        var messages = new[]
        {
            (NotInstalled, "cli.not-found", "공식 CLI를 찾지 못했습니다. 설치 경로를 확인하세요."),
            (NotPermitted, "cli.workdir-denied", "앱 작업 폴더 권한을 확인하세요."),
            (QuotaFailureKind.Timeout, "timeout", "한도 조회 시간 초과"),
            (Unreachable, "cli.read-failed", "한도 조회 실패 · 네트워크와 CLI 상태를 확인하세요."),
            (Rejected, "claude.no-usage-report", "한도 조회 거부 · CLI 로그인과 버전을 확인하세요."),
            (FormatChanged, "parser.claude", "형식 변경 · 조회 불가"),
            (LoginRequired, "auth.required", ClaudeAuthCheck.LoginRequiredText)
        };
        foreach (var (kind, code, text) in messages)
        {
            var state = new QuotaState(Claude, Failure: new(kind, code));
            var panel = QuotaPanelModel.Section(state, now);
            Check(state.Error == text && panel.Rows.Single() is var row && row.Text == text &&
                row.Tone == (kind == LoginRequired ? PanelTone.Danger : PanelTone.Muted), code + " matches the shared wording and tone");
            Check(panel.Detail.Split('\n').Count(line => line == "진단 코드: " + code) == 1, code + " appears once in detail only");
        }
        Check(QuotaPanelModel.Section(new(Codex), now).Rows.Single().Text == "한도 조회 대기", "empty CLI row makes no login assertion");

        foreach (var kind in new[] { Rejected, FormatChanged })
        {
            var auth = new ClaudeAuthCheck(ClaudeAuthStatus.Parse(1, ""), now);
            var state = new QuotaState(Claude, known, LastSuccessfulCheckUtc: now.AddMinutes(-1),
                IsPrevious: true, ClaudeAuth: auth, Failure: new(kind, "parser.synthetic"));
            var panel = QuotaPanelModel.Section(state, now);
            Check(panel.Scope is { Text: ClaudeAuthCheck.LoginRequiredText, Tone: PanelTone.Danger } &&
                panel.Rows.Single() is { UsedPercent: 37, Tone: PanelTone.Muted } &&
                panel.Detail.Contains("진단 코드: parser.synthetic") && !panel.Detail.Contains(state.Error),
                "fresh Claude login evidence overrides the read error without hiding previous values or its diagnosis");
            var newerSuccess = state with { LastSuccessfulCheckUtc = now.AddTicks(1) };
            Check(QuotaPanelModel.Section(newerSuccess, now).Scope is null &&
                QuotaPanelModel.Section(newerSuccess, now).Detail.Contains(state.Error),
                "older auth does not override a newer quota success");
        }

        var loadCases = new[]
        {
            (new GeminiUsagePageLoad("https://gemini.google.com/app", privateMarker), LoginRequired, "gemini.destination", "로그인 필요 · Gemini 앱 사용량"),
            (new GeminiUsagePageLoad(GeminiUsagePage.Url, null), FormatChanged, "gemini.missing-element", "형식 변경 · 조회 불가"),
            (new GeminiUsagePageLoad(GeminiUsagePage.Url, privateMarker), FormatChanged, "gemini.missing-section", "형식 변경 · 조회 불가"),
            (GeminiUsagePageLoad.LoadFailed, Unreachable, "gemini.load", "한도 조회 실패 · 네트워크와 로그인을 확인하세요.")
        };
        foreach (var (load, kind, code, text) in loadCases)
        {
            var reader = new Reader(load);
            using var monitor = new AccountQuotaMonitor(new Client(), utcNow: () => now, geminiWebReader: reader,
                geminiWebEnabled: true, geminiWebTimeZone: TimeZoneInfo.Utc);
            await monitor.RefreshGeminiWebIfDueAsync();
            var state = monitor.Snapshot().Single(s => s.Provider == Gemini);
            var panel = QuotaPanelModel.Section(state, now);
            Check(state.Failure == new QuotaFailure(kind, code) && state.Error == text && panel.Rows.Single().Text == text &&
                panel.Rows.Single().Tone == (kind == LoginRequired ? PanelTone.Danger : PanelTone.Muted) &&
                panel.Detail.Contains("진단 코드: " + code) && !panel.Detail.Contains(privateMarker), "Gemini projects only the closed diagnostic code");
        }

        var root = Path.Combine(directory, "failure-classification");
        var store = new UsageStore(Path.Combine(root, "test.db"));
        var client = new Client { Read = _ => Task.FromResult(known) };
        using (var monitor = new AccountQuotaMonitor(client, store, () => now))
        {
            await monitor.RefreshOnceAsync(Claude);
            client.Read = _ => Task.FromException<QuotaReading>(new QuotaReadException(FormatChanged, "parser.claude"));
            await monitor.RefreshOnceAsync(Claude);
            var stale = monitor.Snapshot().Single(s => s.Provider == Claude);
            Check(stale.Failure == new QuotaFailure(FormatChanged, "parser.claude") && stale.IsPrevious &&
                stale.Reading?.Windows.Single() == known.Windows.Single(), "the monitor retains values while recording a typed failure");
            var cache = await store.ReadQuotaAsync(Claude);
            Check(cache?.Reading.Windows.Single() == known.Windows.Single() &&
                Directory.EnumerateFiles(Path.Combine(root, "logs")).All(file => !File.ReadAllText(file).Contains("parser.claude")),
                "typed diagnostic codes stay out of cache and polling logs");
            client.Read = _ => Task.FromResult(known);
            await monitor.RefreshOnceAsync(Claude);
            Check(monitor.Snapshot().Single(s => s.Provider == Claude) is { Failure: null, Error: "", IsPrevious: false },
                "an accepted live read clears the typed failure and compatibility text");
        }
        return count;
    }

    private sealed class Client : IAccountQuotaClient
    {
        internal Func<QuotaProvider, Task<QuotaReading>> Read { get; set; } = _ => throw new InvalidOperationException("Unexpected fake CLI read.");
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken) => Read(provider);
    }

    private sealed class Reader(GeminiUsagePageLoad load) : IGeminiUsagePageReader
    {
        public Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken) => Task.FromResult(load);
    }
}
