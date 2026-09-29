using System.Text.Json;

namespace AiBurgerClock;

// Synthetic pipes only: no installed CLI is started and no account, network or credentials are read.
internal static class AccountQuotaClientTests
{
    private const string ClaudeReport = """{"type":"assistant","usage_report":{"rate_limits":{"limits":[{"kind":"session","percent":12,"resets_at":"2026-09-30T05:00:00Z"},{"kind":"weekly_all","percent":45,"resets_at":"2026-10-01T05:00:00Z"}]}}}""";
    private const string ClaudeResult = """{"type":"result","subtype":"success","is_error":false,"local_command":"usage","num_turns":0,"total_cost_usd":0,"usage":{"input_tokens":0,"output_tokens":0,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"modelUsage":{}}""";
    private const string CodexResult = """{"rateLimits":{"primary":{"usedPercent":15,"windowDurationMins":300,"resetsAt":1790744400},"secondary":{"usedPercent":25,"windowDurationMins":10080,"resetsAt":1791003600}}}""";

    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota CLI transport test: " + message);
        }
        async Task RejectAsync(Func<Task> call, string message)
        {
            bool rejected = false;
            try { await call(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        Task<QuotaReading> Claude(string text) => AccountQuotaClient.ReadClaudeProtocolAsync(new StringReader(text), CancellationToken.None);
        Task<QuotaReading> Codex(string text) => AccountQuotaClient.ReadCodexProtocolAsync(new StringReader(text), new StringWriter(), CancellationToken.None);

        var start = AccountQuotaClient.CreateStartInfo(QuotaProvider.Claude, @"C:\native path\claude.exe", @"C:\neutral");
        Check(!start.UseShellExecute && start.CreateNoWindow, "no shell or console window");
        Check(start.RedirectStandardInput && start.RedirectStandardOutput && start.RedirectStandardError, "all pipes redirected");
        Check(start.FileName == @"C:\native path\claude.exe" && start.WorkingDirectory == @"C:\neutral", "native path and neutral cwd");
        string[] args = start.ArgumentList.ToArray();
        Check(args[^2] == "--" && args[^1] == "/usage", "only built-in usage, no natural language");
        Check(args[Array.IndexOf(args, "--max-turns") + 1] == "0", "zero model turns guard");
        Check(args[Array.IndexOf(args, "--tools") + 1] == "", "empty argument retained for tools");
        Check(args[Array.IndexOf(args, "--setting-sources") + 1] == "", "empty argument retained for setting sources");
        Check(args[Array.IndexOf(args, "--settings") + 1] == "{\"disableAllHooks\":true}", "all hooks disabled");
        Check(args.Contains("--strict-mcp-config") && args[Array.IndexOf(args, "--mcp-config") + 1] == "{\"mcpServers\":{}}", "no MCP servers");
        Check(args.Contains("--no-session-persistence") && args.Contains("--no-chrome"), "no persistence or browser");
        Check(!args.Contains("--bare") && !args.Contains("--dangerously-skip-permissions"), "no bypass or OAuth disabling");
        Check(start.Environment["DISABLE_AUTOUPDATER"] == "1", "do not update installed software");
        start.Environment.TryGetValue("CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC", out string? inheritedTrafficSetting);
        Check(inheritedTrafficSetting == Environment.GetEnvironmentVariable("CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"),
            "do not inject traffic suppression that disables real quota reads");
        var codexStart = AccountQuotaClient.CreateStartInfo(QuotaProvider.Codex, @"C:\native path\codex.exe", @"C:\neutral");
        Check(codexStart.ArgumentList.SequenceEqual(new[] { "app-server", "--listen", "stdio://" }), "Codex local stdio only");

        QuotaReading claude = await Claude("\r\n" + ClaudeReport + "\r\n" + ClaudeResult);
        Check(claude.Provider == QuotaProvider.Claude && claude.Windows.Count == 2, "accept structured report with validated result");
        Check(claude.Windows[0].UsedPercent == 12 && claude.Windows[1].RemainingPercent == 55, "transport preserves quota values");
        await RejectAsync(() => Claude(ClaudeReport), "do not accept report without success result");
        await RejectAsync(() => Claude(ClaudeResult), "do not accept result without report");
        await RejectAsync(() => Claude(ClaudeReport + "\n" + ClaudeResult.Replace("\"usage\"", "\"other\"")), "wrong local command");
        await RejectAsync(() => Claude(ClaudeReport + "\n" + ClaudeResult.Replace("\"num_turns\":0", "\"num_turns\":1")), "model turns rejected");
        await RejectAsync(() => Claude(ClaudeReport + "\n" + ClaudeResult.Replace("\"input_tokens\":0", "\"input_tokens\":1")), "model input rejected");
        await RejectAsync(() => Claude(ClaudeReport + "\n" + ClaudeResult.Replace("\"output_tokens\":0", "\"output_tokens\":1")), "model output rejected");
        await RejectAsync(() => Claude(ClaudeReport + "\n" + ClaudeResult.Replace("\"cache_read_input_tokens\":0", "\"cache_read_input_tokens\":1")), "cache usage rejected");
        await RejectAsync(() => Claude(ClaudeReport + "\n" + ClaudeResult.Replace("\"total_cost_usd\":0", "\"total_cost_usd\":0.01")), "reported cost rejected");
        await RejectAsync(() => Claude(ClaudeReport + "\n" + ClaudeResult.Replace("\"is_error\":false", "\"is_error\":true")), "error result rejected");
        await RejectAsync(() => Claude(ClaudeReport + "\n" + ClaudeResult + "\n" + ClaudeResult), "duplicate result rejected");
        await RejectAsync(() => Claude("""{"type":"assistant","message":{"content":[{"type":"text","text":"12% used"}]}}""" + "\n" + ClaudeResult), "model prose cannot supply quotas");
        await RejectAsync(() => Claude("not-json\n" + ClaudeReport + "\n" + ClaudeResult), "malformed stream rejected");
        await RejectAsync(() => Claude("[]\n"), "non-object event rejected");
        await RejectAsync(() => Claude(new string('x', AccountQuotaClient.MaximumLineCharacters + 1)), "unterminated line bounded");
        await RejectAsync(() => Claude(string.Concat(Enumerable.Repeat(new string(' ', 4095) + "\n", 513))), "total output bounded");

        string wire = "{\"id\":1,\"result\":{}}\n{\"method\":\"account/updated\",\"params\":{}}\n{\"id\":2,\"result\":" + CodexResult + "}\n";
        var input = new StringWriter();
        QuotaReading codex = await AccountQuotaClient.ReadCodexProtocolAsync(new StringReader(wire), input, CancellationToken.None);
        Check(codex.Provider == QuotaProvider.Codex && codex.Windows.Count == 2, "read-only RPC result parsed");
        Check(codex.Windows[0].UsedPercent == 15 && codex.Windows[1].UsedPercent == 25, "RPC quota values preserved");
        string[] sent = input.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Check(sent.Length == 3, "exactly initialize, initialized and rate limits read");
        using (JsonDocument first = JsonDocument.Parse(sent[0]))
            Check(first.RootElement.GetProperty("method").GetString() == "initialize", "initialize first");
        using (JsonDocument second = JsonDocument.Parse(sent[1]))
            Check(second.RootElement.GetProperty("method").GetString() == "initialized" && !second.RootElement.TryGetProperty("id", out _), "initialized notification");
        using (JsonDocument third = JsonDocument.Parse(sent[2]))
            Check(third.RootElement.GetProperty("method").GetString() == "account/rateLimits/read", "only read quota after handshake");
        Check(!input.ToString().Contains("thread/") && !input.ToString().Contains("turn/") && !input.ToString().Contains("login"), "no session, model or auth mutation requests");
        await RejectAsync(() => Codex("{\"id\":1,\"error\":{\"message\":\"sensitive detail\"}}"), "initialize error rejected");
        await RejectAsync(() => Codex("{\"id\":1,\"result\":{}}\n{\"id\":2,\"error\":{}}"), "quota RPC error rejected");
        await RejectAsync(() => Codex("{\"id\":2,\"result\":" + CodexResult + "}"), "quota before initialize not accepted");
        await RejectAsync(() => Codex(wire.Replace("\"id\":2", "\"id\":99")), "unrelated response id ignored");
        await RejectAsync(() => Codex(wire.Replace("\"id\":2", "\"id\":\"2\"")), "string response id not mistaken for own integer id");
        await RejectAsync(() => Codex("bad"), "Codex malformed response rejected");
        try
        {
            await Codex("{\"id\":1,\"error\":{\"message\":\"secret-token\"}}");
            Check(false, "RPC error expected");
        }
        catch (InvalidDataException error)
        {
            Check(!error.Message.Contains("secret-token"), "raw errors never propagated");
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool cancelled = false;
        try { await new AccountQuotaClient().ReadAsync(QuotaProvider.Codex, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "pre-cancelled request starts no CLI");
        bool shellRejected = false;
        try { await new AccountQuotaClient(codexExecutable: @"C:\invalid\codex.cmd").ReadAsync(QuotaProvider.Codex, CancellationToken.None); }
        catch (FileNotFoundException) { shellRejected = true; }
        Check(shellRejected, "shell wrappers rejected before launch");
        bool relativeRejected = false;
        try { await new AccountQuotaClient(claudeExecutable: "claude.exe").ReadAsync(QuotaProvider.Claude, CancellationToken.None); }
        catch (FileNotFoundException) { relativeRejected = true; }
        Check(relativeRejected, "relative executable cannot resolve against working directory");
        using var pipeCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        bool pipeStopped = false;
        try { await AccountQuotaClient.ReadClaudeProtocolAsync(new WaitingReader(), pipeCancellation.Token); }
        catch (OperationCanceledException) { pipeStopped = true; }
        Check(pipeStopped, "blocked output read honors shutdown cancellation");
        return count;
    }

    private sealed class WaitingReader : TextReader
    {
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
