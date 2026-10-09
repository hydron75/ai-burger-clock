using System.Text.Json.Nodes;

namespace AiBurgerClock;

// Portable transport fixtures only; never starts an installed agy CLI.
internal static class GeminiQuotaClientTests
{
    private const string Success = """
        {"conversation_id":"","status":"SUCCESS","response":"Human TSV is deliberately not quota data",
         "duration_seconds":0,"num_turns":0,
         "usage":{"input_tokens":0,"output_tokens":0,"thinking_tokens":0,"cache_read_tokens":0,"total_tokens":0},
         "command":{"name":"usage","data":{"groups":[
           {"name":"Gemini Models","buckets":[
             {"id":"gemini-5h","window":"5h","remaining_fraction":0.984375,"reset_time":"2027-02-10T16:05:00Z"},
             {"id":"gemini-weekly","window":"weekly","remaining_fraction":0.99609375,"reset_time":"2027-02-17T11:05:00Z"}]},
           {"name":"Claude and GPT models","buckets":[{"id":"3p-5h","remaining_fraction":0}]}
         ]}}}
        """;

    internal static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Gemini CLI: " + label);
        }
        async Task Reject(Func<Task> call, string label)
        {
            try { await call(); Check(false, label); }
            catch (InvalidDataException error)
            {
                Check(!error.Message.Contains("private-secret", StringComparison.Ordinal), label + "; no raw output");
            }
        }
        Task<QuotaReading> Read(string json) => AccountQuotaClient.ReadGeminiProtocolAsync(new StringReader(json), CancellationToken.None);
        string Change(Action<JsonNode> update)
        {
            var root = JsonNode.Parse(Success)!;
            update(root);
            return root.ToJsonString();
        }

        string? updaterBefore = Environment.GetEnvironmentVariable("AGY_CLI_DISABLE_AUTO_UPDATE");
        var start = AccountQuotaClient.CreateStartInfo(QuotaProvider.Gemini, "/synthetic path/agy", "/neutral");
        Check(!start.UseShellExecute && start.CreateNoWindow, "no shell or terminal");
        Check(start.RedirectStandardInput && start.RedirectStandardOutput && start.RedirectStandardError, "all pipes redirected");
        Check(start.FileName == "/synthetic path/agy" && start.WorkingDirectory == "/neutral", "absolute executable and neutral directory retained");
        Check(start.ArgumentList.SequenceEqual(new[] { "-p", "/usage", "--output-format", "json", "--print-timeout", "20s" }),
            "only the builtin read-only command with explicit timeout");
        Check(start.Environment["AGY_CLI_DISABLE_AUTO_UPDATE"] == "true", "documented exact updater opt-out in child environment");
        Check(Environment.GetEnvironmentVariable("AGY_CLI_DISABLE_AUTO_UPDATE") == updaterBefore, "parent settings remain unchanged");
        Check(!start.ArgumentList.Contains("--disable-slash-commands") && !start.ArgumentList.Contains("--agent") &&
            !start.ArgumentList.Contains("--remote-control") && !start.ArgumentList.Contains("--continue"), "no model fallback or remote/resume options");
        foreach (string version in new[] { "1.3.1", "1.3.1\r\n", "1.3.2", "1.3.10", "1.4.0", "1.4.12", "1.10.0" })
            Check(AccountQuotaClient.IsSupportedGeminiVersion(version), "stable supported version accepted: " + version.Trim());
        foreach (string version in new[] { "", "1.1.10", "1.1.11", "1.2.99", "1.3.0", "2.0.0", "2.1.0", "agy 1.3.1",
            "1.3.1\n/usage", "1.3", "1.3.1.0", "1.3.2-beta.1", "1.3.2+build.5", "1.03.1", "1.3.-1", "1.3.2147483648",
            "1.3.x", "1. 3.1" })
            Check(!AccountQuotaClient.IsSupportedGeminiVersion(version), "unsupported or ambiguous version blocked before sending a prompt: " + version);

        QuotaReading reading = await Read(Success);
        Check(reading.Provider == QuotaProvider.Gemini && reading.Windows.Count == 2, "single pretty-printed command result is accepted");
        Check(reading.Windows[0].Id == "gemini-5h" && reading.Windows[1].Id == "gemini-weekly", "only typed Gemini buckets, not prose or 3p, are used");
        Check(Math.Abs(reading.Windows[1].RemainingPercent - 99.609375) < 1e-12, "precise fractions survive transport");
        Check((await Read(Change(root => root["response"] = "wrong prose"))).Windows.Count == 2, "human response field never supplies limits");
        await Reject(() => Read(Change(root => root["status"] = "ERROR")), "error status rejected");
        await Reject(() => Read(Change(root => root["conversation_id"] = "created-conversation")), "new conversation rejected");
        await Reject(() => Read(Change(root => root["conversation_id"] = null)), "missing conversation evidence rejected");
        await Reject(() => Read(Change(root => root["num_turns"] = 1)), "agent turn rejected");
        await Reject(() => Read(Change(root => root["num_turns"] = null)), "missing turn evidence rejected");
        foreach (string counter in new[] { "input_tokens", "output_tokens", "thinking_tokens", "cache_read_tokens", "total_tokens" })
        {
            await Reject(() => Read(Change(root => root["usage"]![counter] = 1)), "nonzero " + counter + " rejected");
            await Reject(() => Read(Change(root => root["usage"]!.AsObject().Remove(counter))), "missing " + counter + " rejected");
        }
        await Reject(() => Read(Change(root => root["usage"]!["extra_tokens"] = 1)), "future nonzero token field rejected");
        await Reject(() => Read(Change(root => root["usage"] = null)), "missing usage evidence rejected");
        await Reject(() => Read(Change(root => root["total_cost_usd"] = 0.01)), "reported model cost rejected");
        await Reject(() => Read(Change(root => root["command"]!["name"] = "other")), "wrong builtin rejected");
        await Reject(() => Read(Change(root => root["command"]!["data"] = null)), "missing typed data rejected");
        await Reject(() => Read(Change(root => root["command"] = null)), "prose alone cannot supply limits");
        foreach (string invalid in new[] { "", "private-secret", "null", "[]", Success + Success, "notice\n" + Success })
            await Reject(() => Read(invalid), "not one complete JSON object");
        await Reject(() => Read(new string('x', AccountQuotaClient.MaximumOutputCharacters + 1)), "unterminated output bounded");

        foreach (string marker in new[] { "Authentication required", "Waiting for authentication", "Could not authenticate", "Sign in to continue", "https://accounts.google.com/o/oauth2/auth" })
        {
            await Reject(() => AccountQuotaClient.ReadGeminiProtocolAsync(new ChunkedReader(marker + " private-secret", 1), CancellationToken.None),
                "stdout authentication prompt split across chunks stops without user input");
            await Reject(() => AccountQuotaClient.DrainErrorAsync(new ChunkedReader(marker + " private-secret", 1), CancellationToken.None, true),
                "stderr authentication prompt split across chunks is not swallowed");
        }
        await AccountQuotaClient.DrainErrorAsync(new StringReader("ordinary diagnostics private-secret"), CancellationToken.None, true);
        Check(true, "ordinary diagnostics are discarded without retaining or exposing output");
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30)))
        {
            bool stopped = false;
            try { await AccountQuotaClient.ReadGeminiProtocolAsync(new WaitingReader(), cancellation.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "blocked agy stdout honors cancellation");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            bool stopped = false;
            try { await new AccountQuotaClient().ReadAsync(QuotaProvider.Gemini, cancellation.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "pre-cancelled Gemini request never looks up or starts an executable");
        }
        bool relativeRejected = false;
        try { await new AccountQuotaClient(geminiExecutable: "agy.exe").ReadAsync(QuotaProvider.Gemini, CancellationToken.None); }
        catch (FileNotFoundException) { relativeRejected = true; }
        Check(relativeRejected, "relative agy path cannot execute from the current directory");
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

    private sealed class ChunkedReader(string text, int chunk) : StringReader(text)
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], cancellationToken);
    }
}
