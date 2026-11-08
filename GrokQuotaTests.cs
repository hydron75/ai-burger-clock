using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiBurgerClock;

// Synthetic JSON/pipes/clocks and temporary SQLite only. No installed CLI or credentials.
internal static class GrokQuotaTests
{
    internal const string Billing = """
        {"config":{"creditUsagePercent":37.5,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY",
        "start":"2032-03-01T00:00:00Z","end":"2032-03-08T00:00:00Z"},"prepaidBalance":{"val":"SYNTHETIC-PRIVATE"},
        "isUnifiedBillingUser":true,"billingPeriodEnd":"2032-04-01T00:00:00Z"},"subscription_tier":"SYNTHETIC-PRIVATE"}
        """;

    internal static async Task<int> RunAsync(string directory)
    {
        int count = 0;
        void Check(bool value, string label) { count++; if (!value) throw new InvalidOperationException("Grok quota: " + label); }
        void Reject(string json, string label)
        {
            try { AccountQuotaParsers.ParseGrok(json); Check(false, label); }
            catch (InvalidDataException error) { Check(!error.ToString().Contains("SYNTHETIC-PRIVATE", StringComparison.Ordinal), label); }
        }
        string Changed(Action<JsonNode> update)
        {
            JsonNode root = JsonNode.Parse(Billing)!;
            update(root);
            return root.ToJsonString();
        }
        var now = DateTimeOffset.Parse("2032-03-04T00:00:00Z");
        QuotaReading reading = AccountQuotaParsers.ParseGrok(Billing);
        QuotaWindow row = reading.Windows.Single();
        Check(reading.Provider == QuotaProvider.Xai && row.Label == "주간 크레딧" && row.Id == AccountQuotaParsers.GrokWeeklyWindowId,
            "one stable weekly-credit row, not an account-derived ID");
        Check(row.UsedPercent == 37.5 && row.WindowMinutes == 10080 && row.ResetsAtUtc == now.AddDays(4), "typed usage/end/verified duration");
        Check((int)QuotaProvider.Codex == 0 && (int)QuotaProvider.Claude == 1 && (int)QuotaProvider.Gemini == 2 && (int)QuotaProvider.Xai == 3,
            "append only: old numeric cache identities survive");
        foreach (double valid in new[] { 0d, .001, 99.999, 100 })
            Check(AccountQuotaParsers.ParseGrok(Changed(n => n["config"]!["creditUsagePercent"] = valid)).Windows[0].UsedPercent == valid,
                "valid numeric boundary retained without rounding");
        foreach (string type in new[] { "USAGE_PERIOD_TYPE_MONTHLY", "WEEK", "WEEKLY", "UNKNOWN", "" })
            Reject(Changed(n => n["config"]!["currentPeriod"]!["type"] = type), "unknown/non-weekly enum fails");
        foreach (JsonNode? invalid in new JsonNode?[] { null, JsonValue.Create("0"), JsonValue.Create(-.01), JsonValue.Create(100.01), JsonValue.Create(true) })
            Reject(Changed(n => n["config"]!["creditUsagePercent"] = invalid?.DeepClone()), "null/coerced/out-of-range usage fails");
        Reject(Changed(n => n["config"]!.AsObject().Remove("creditUsagePercent")), "absent usage is not zero");
        foreach (string key in new[] { "start", "end", "type" })
        {
            Reject(Changed(n => n["config"]!["currentPeriod"]!.AsObject().Remove(key)), "missing period field fails");
            Reject(Changed(n => n["config"]!["currentPeriod"]![key] = null), "null period field fails");
        }
        foreach (string end in new[] { "2032-03-07T23:59:59Z", "2032-03-08T00:00:01Z", "2032-03-09T00:00:00Z", "2032-03-01T00:00:00Z", "2032-03-08T00:00:00", "bad" })
            Reject(Changed(n => n["config"]!["currentPeriod"]!["end"] = end), "no duration rounding, zero/reverse/timezone-less period fails");
        foreach (string json in new[] { "", "null", "[]", "{}", "SYNTHETIC-PRIVATE", Billing + Billing, Billing.Replace("37.5", "1e999", StringComparison.Ordinal) })
            Reject(json, "invalid/oversize numeric envelope rejected");
        Check(AccountQuotaParsers.ParseGrok(Changed(n => n["unknown"] = new JsonObject { ["extra"] = "SYNTHETIC-PRIVATE" })).Windows[0] == row,
            "unknown fields including optional on-demand data ignored");
        Check(AccountQuotaParsers.ParseGrok(Changed(n => n["config"]!["onDemandCap"] = new JsonArray("SYNTHETIC-PRIVATE"))).Windows[0] == row,
            "unused fields cannot become another row or a zero limit");
        Reject(new string('x', 2_000_001), "billing response size bounded");

        string Init = """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1}}""";
        string Auth = """{"jsonrpc":"2.0","id":2,"result":{}}""";
        string Reply = "{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":" + Billing + "}";
        string Transcript(params string[] lines) => string.Join('\n', lines.Select(line => line.Replace("\r", "").Replace("\n", ""))) + "\n";
        using var written = new StringWriter();
        var protocol = await AccountQuotaClient.ReadGrokProtocolAsync(new Chunks(Transcript(Init, Auth,
            """{"jsonrpc":"2.0","method":"_x.ai/test-notification","params":{"secret":"SYNTHETIC-PRIVATE"}}""", Reply)), written, CancellationToken.None);
        Check(protocol.Windows.Single() == row, "chunked NDJSON and unrelated notifications accepted");
        string[] requests = written.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Check(requests.Length == 3, "only three client requests");
        string[] methods = ["initialize", "authenticate", "_x.ai/billing"];
        for (int i = 0; i < requests.Length; i++)
        {
            using var doc = JsonDocument.Parse(requests[i]);
            Check(doc.RootElement.GetProperty("method").GetString() == methods[i] && doc.RootElement.GetProperty("id").GetInt32() == i + 1,
                "fixed request order and IDs");
        }
        using (var auth = JsonDocument.Parse(requests[1]))
            Check(auth.RootElement.GetProperty("params").GetProperty("methodId").GetString() == "cached_token" &&
                auth.RootElement.GetProperty("params").GetProperty("_meta").GetProperty("headless").GetBoolean(), "always cached headless auth");
        Check(!written.ToString().Contains("session/", StringComparison.Ordinal) && !written.ToString().Contains("SYNTHETIC-PRIVATE", StringComparison.Ordinal),
            "no model/session calls or returned private data in outgoing requests");
        foreach (var test in new[]
        {
            (Text: Transcript(Init, """{"jsonrpc":"2.0","id":2,"error":{"code":-32000,"message":"SYNTHETIC-PRIVATE"}}"""), Requests: 2),
            (Text: Transcript(Init, Auth, """{"jsonrpc":"2.0","id":3,"error":{"code":-32601,"message":"SYNTHETIC-PRIVATE"}}"""), Requests: 3),
            (Text: Transcript(Init.Replace("\"protocolVersion\":1", "\"protocolVersion\":2", StringComparison.Ordinal)), Requests: 1),
            (Text: Transcript(Init, """{"jsonrpc":"2.0","id":3,"result":{}}"""), Requests: 2),
            (Text: Transcript(Init, """{"jsonrpc":"2.0","id":"2","result":{}}"""), Requests: 2),
            (Text: Transcript(Init, """{"jsonrpc":"2.0","id":8,"method":"session/request_permission","params":{}}"""), Requests: 2),
            (Text: "", Requests: 1), (Text: "SYNTHETIC-PRIVATE\n", Requests: 1),
            (Text: new string('x', AccountQuotaClient.MaximumLineCharacters + 1), Requests: 1)
        })
        {
            using var sent = new StringWriter();
            try { await AccountQuotaClient.ReadGrokProtocolAsync(new StringReader(test.Text), sent, CancellationToken.None); Check(false, "protocol must fail"); }
            catch (InvalidDataException error) { Check(!error.Message.Contains("SYNTHETIC-PRIVATE", StringComparison.Ordinal), "error payload never reaches UI"); }
            Check(sent.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == test.Requests, "failure stops without retries/login/model fallback");
        }
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(20)))
        {
            try { await AccountQuotaClient.ReadGrokProtocolAsync(new Blocked(), new StringWriter(), cancel.Token); Check(false, "cancel blocking read"); }
            catch (OperationCanceledException) { Check(true, "deadline cancels blocked protocol IO"); }
        }
        var setup = AccountQuotaClient.CreateStartInfo(QuotaProvider.Xai, "/synthetic path/grok", "/neutral");
        Check(setup.ArgumentList.SequenceEqual(["--no-auto-update", "agent", "--no-leader", "stdio"]), "documented fixed args, isolated leader");
        Check(!setup.UseShellExecute && setup.CreateNoWindow && setup.RedirectStandardInput && setup.RedirectStandardOutput && setup.RedirectStandardError,
            "direct process with all pipes; no shell");
        Check(!setup.Environment.ContainsKey("GROK_LOG_FILE") && !setup.Environment.ContainsKey("GROK_DEBUG_LOG") && setup.Environment["RUST_LOG"] == "off",
            "child-only diagnostics suppression, no raw/token log destinations");
        Check(AccountQuotaClient.GrokTimeout == TimeSpan.FromSeconds(45), "shared client/monitor Grok deadline is 45 seconds");
        var mac = MacCliPaths.Candidates(QuotaProvider.Xai, ["relative", "/tmp/project", "/opt/custom"], "/Users/synthetic", "/tmp/project");
        Check(mac.Contains("/Users/synthetic/.grok/bin/grok") && mac.Contains("/Users/synthetic/.local/bin/grok") &&
            mac.Contains("/opt/custom/grok") && !mac.Any(path => path.Contains("project", StringComparison.Ordinal) || !path.StartsWith('/')),
            "Mac native/PATH fallbacks, no relative/current directory");

        foreach (int offset in Enumerable.Range(-30, 76))
        {
            DateTimeOffset reset = now.AddMinutes(offset);
            var windows = new[] { row with { ResetsAtUtc = reset } };
            QuotaCheckPlan plan = AccountQuotaPolicy.GetNextCheckPlan(now, now, windows, provider: QuotaProvider.Xai);
            Check(plan.NextCheckUtc - now >= TimeSpan.FromMinutes(10), "every scheduled entry/band/exit respects the floor");
            if (plan.Reason is QuotaCheckReason.ResetEntry or QuotaCheckReason.ResetPeriod)
                Check(plan.NextCheckUtc >= reset.AddMinutes(-15) && plan.NextCheckUtc <= reset.AddMinutes(15), "no trailing fast poll outside reset band");
        }
        Check(AccountQuotaPolicy.GetNextCheckPlan(now, now, [row with { ResetsAtUtc = now.AddMinutes(17) }], provider: QuotaProvider.Xai)
            is { Reason: QuotaCheckReason.ResetEntry, NextCheckUtc: var entry } && entry == now.AddMinutes(10), "near entry coalesces to ten minutes");
        foreach (var provider in new[] { QuotaProvider.Codex, QuotaProvider.Claude, QuotaProvider.Gemini })
            Check(AccountQuotaPolicy.GetNextCheckPlan(now, now, [row with { ResetsAtUtc = now.AddMinutes(3) }], provider: provider).NextCheckUtc == now.AddMinutes(5),
                "existing providers retain five-minute reset polling");
        foreach (double used in new[] { 20d, 95, 100 })
        {
            var plan = AccountQuotaPolicy.GetNextCheckPlan(now, now, [row with { UsedPercent = used }], provider: QuotaProvider.Xai);
            Check(plan.NextCheckUtc == now + (used == 100 ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1)), "regular/high/exhausted policy unchanged");
        }
        var state = new QuotaState(QuotaProvider.Xai, reading, LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(1));
        var section = QuotaPanelModel.Section(state, now);
        Check(section.Heading.Text == "xAI" && section.Rows.Single().NameText == "주간 크레딧" && section.Rows.Single().ElapsedPercent is not null,
            "shared box/row/pacing text");
        Check(section.Detail.Contains("최소 10분", StringComparison.Ordinal), "provider-specific policy guidance");
        Check(QuotaPacing.Calculate(row with { UsedPercent = 0 }, now, false, now).ElapsedPercent is not null, "weekly zero still has a time layer");
        Check(QuotaPanelModel.Section(state with { IsPrevious = true }, now).Rows[0].ElapsedPercent is null, "previous hides comparison");
        var recovery = new QuotaRecoveryNotifications();
        var early = new QuotaEarlyResetNotifications();
        QuotaReading R(double used) => reading with { Windows = [row with { UsedPercent = used }] };
        Check(recovery.Observe(new(QuotaProvider.Xai, 1, now, R(100), 1), true) is null && early.Observe(new(QuotaProvider.Xai, 1, now, R(100), 1), true) is null,
            "first arrival seeds without an alert");
        var observation = new QuotaObservation(QuotaProvider.Xai, 2, now.AddMinutes(1), R(20), 1);
        var recovered = recovery.Observe(observation, true);
        var resetAlert = early.Observe(observation, true);
        Check(recovered is not null && resetAlert is not null && QuotaNotificationText.Combine(recovered, resetAlert) is { Provider: QuotaProvider.Xai },
            "existing recovery/weekly early-reset/combined alert rules include xAI");
        Check(early.Observe(observation, true) is null, "duplicate sequence never alerts twice");

        var store = new UsageStore(Path.Combine(directory, "grok-synthetic.db"));
        await store.SaveQuotaAsync(new(1, reading, now, []));
        var cached = await store.ReadQuotaAsync(QuotaProvider.Xai);
        Check(cached?.Reading.Windows.Single() == row && cached.Version == 1, "new provider uses unchanged cache schema");
        string cacheJson = JsonSerializer.Serialize(cached, QuotaJsonContext.Default.QuotaCache);
        Check(!cacheJson.Contains("SYNTHETIC-PRIVATE", StringComparison.Ordinal) && !cacheJson.Contains("subscription", StringComparison.Ordinal) &&
            !cacheJson.Contains("prepaid", StringComparison.Ordinal), "cache only contains normalized quota data");
        string logs = Path.Combine(directory, "grok-versioned-logs");
        Directory.CreateDirectory(logs);
        string oldPolling = Path.Combine(logs, "quota-polling-diagnostics.json");
        string oldDecisions = Path.Combine(logs, "quota-early-reset-decisions-v1.jsonl");
        File.WriteAllText(oldPolling, "Synthetic retained v1 polling");
        File.WriteAllText(oldDecisions, "Synthetic retained v1 decisions");
        var polling = new QuotaPollingDiagnosticLog(logs);
        var decisions = new QuotaEarlyResetDiagnosticLog(logs);
        Check(polling.TryWrite(new("Xai", now, now, now.AddHours(1), "Regular", null, true, 0, 0)), "v2 polling accepts xAI");
        Check(decisions.TryAppend(new("abcdefabcdefabcdefabcdef00000001", "Xai", now, now, true, 1, 1, "Excluded", "NotEvaluated")), "v2 decisions accept xAI");
        Check(File.ReadAllText(oldPolling) == "Synthetic retained v1 polling" && File.ReadAllText(oldDecisions) == "Synthetic retained v1 decisions",
            "old diagnostic files are neither read nor overwritten");
        var client = new Client { Reading = reading };
        using var monitor = new AccountQuotaMonitor(client, utcNow: () => now);
        await monitor.RefreshOnceAsync(QuotaProvider.Xai);
        client.Fail = true;
        await monitor.RefreshOnceAsync(QuotaProvider.Xai);
        Check(monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Xai) is { IsPrevious: true, Reading: not null }, "failure preserves verified data");
        client.Fail = false;
        int before = client.Calls;
        await monitor.RefreshOnceAsync(QuotaProvider.Xai);
        Check(client.Calls == before + 1, "an explicit read is not throttled by the scheduling floor");
        using var none = new AccountQuotaMonitor(new Client { Fail = true }, utcNow: () => now);
        await none.RefreshOnceAsync(QuotaProvider.Xai);
        Check(none.Snapshot().Single(s => s.Provider == QuotaProvider.Xai).Reading is null, "failure with no prior data never creates zero");
        return count;
    }

    private sealed class Client : IAccountQuotaClient
    {
        internal QuotaReading? Reading;
        internal bool Fail;
        internal int Calls;
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail ? Task.FromException<QuotaReading>(new IOException("Synthetic offline")) : Task.FromResult(Reading!);
        }
    }
    private sealed class Chunks(string text) : StringReader(text)
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
    }
    private sealed class Blocked : TextReader
    {
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
}
