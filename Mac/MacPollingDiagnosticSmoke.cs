using System.Text.Json;

namespace AiBurgerClock;

// Native check of the quota polling diagnostics (#63) in the Mac build (trimmed, source-generated JSON).
// Synthetic readings and a fake clock only; the diagnostic file lives beside a temporary SQLite database.
internal static class MacPollingDiagnosticSmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Polling diagnostics: " + message);
    }

    private sealed class Client : IAccountQuotaClient
    {
        internal DateTimeOffset Clock;
        internal Func<QuotaProvider, DateTimeOffset, QuotaReading?> Script = (_, _) => null;

        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            QuotaReading? reading = Script(provider, Clock);
            if (reading is null) throw new InvalidOperationException("synthetic failure");
            return Task.FromResult(reading);
        }
    }

    private sealed record Entry(string Provider, DateTimeOffset Attempted, DateTimeOffset Recorded, DateTimeOffset Next,
        string Reason, DateTimeOffset? Selected, bool Succeeded, int Failures, int Anchors);

    private static (int Version, List<Entry> Entries, int Bytes, HashSet<string> Keys) Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        var keys = new HashSet<string>();
        var entries = new List<Entry>();
        foreach (JsonElement item in document.RootElement.GetProperty("Entries").EnumerateArray())
        {
            foreach (JsonProperty property in item.EnumerateObject()) keys.Add(property.Name);
            DateTimeOffset? selected = item.GetProperty("SelectedResetUtc").ValueKind == JsonValueKind.Null
                ? null : item.GetProperty("SelectedResetUtc").GetDateTimeOffset();
            entries.Add(new(item.GetProperty("Provider").GetString()!, item.GetProperty("AttemptedAtUtc").GetDateTimeOffset(),
                item.GetProperty("RecordedAtUtc").GetDateTimeOffset(), item.GetProperty("NextCheckUtc").GetDateTimeOffset(),
                item.GetProperty("Reason").GetString()!, selected, item.GetProperty("Succeeded").GetBoolean(),
                item.GetProperty("ConsecutiveFailures").GetInt32(), item.GetProperty("ResetAnchorCount").GetInt32()));
        }
        return (document.RootElement.GetProperty("Version").GetInt32(), entries, bytes.Length, keys);
    }

    internal static async Task<string> VerifyAsync(CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "aibc-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new UsageStore(Path.Combine(directory, "burgerclock.db"));
            string logPath = Path.Combine(directory, "logs", "quota-polling-diagnostics.json");
            var client = new Client { Clock = new DateTimeOffset(2026, 3, 3, 3, 0, 0, TimeSpan.Zero) };
            using var monitor = new AccountQuotaMonitor(client, store, () => client.Clock);
            async Task<Entry> Poll(QuotaProvider provider, Func<QuotaProvider, DateTimeOffset, QuotaReading?> script)
            {
                client.Script = script;
                await monitor.RefreshOnceAsync(provider, cancellationToken);
                Check(File.Exists(logPath), "no diagnostic file was written");
                return Read(logPath).Entries[^1];
            }
            QuotaReading R(QuotaProvider p, params QuotaWindow[] w) => new(p, w);
            DateTimeOffset T() => client.Clock;

            // Regular: unused moving windows only (no reset is selected, 6 hour regular schedule).
            Entry regular = await Poll(QuotaProvider.Gemini, (p, now) => R(p,
                new QuotaWindow("gemini-5h", "5시간", 0, now.AddHours(5), 300), new QuotaWindow("gemini-weekly", "주간", 0, now.AddDays(6), 10080)));
            Check(regular is { Provider: "Gemini", Reason: "Regular", Selected: null, Succeeded: true, Failures: 0, Anchors: 0 },
                $"regular entry has the wrong shape ({regular.Reason}, anchors {regular.Anchors}).");
            Check(regular.Next == T().AddHours(6) && regular.Attempted == T() && regular.Recorded == T(),
                "regular entry must record attempted/recorded/next times (next = +6h).");

            // ResetPeriod: a used window resetting in 10 minutes -> 5 minute checks, that reset selected.
            DateTimeOffset reset = T().AddMinutes(10);
            Entry period = await Poll(QuotaProvider.Codex, (p, now) => R(p, new QuotaWindow("codex-weekly", "주간", 40, reset, 10080)));
            Check(period.Reason == "ResetPeriod" && period.Selected == reset && period.Next == T().AddMinutes(5) && period.Anchors == 1,
                $"reset period entry wrong ({period.Reason}, next +{(period.Next - T()).TotalMinutes:0}m).");

            // ResetEntry: reset 40 minutes ahead -> next check at reset - 15 minutes, that reset selected.
            client.Clock = client.Clock.AddHours(1); // the earlier +15m band is over
            DateTimeOffset later = T().AddMinutes(40);
            Entry entry = await Poll(QuotaProvider.Codex, (p, now) => R(p, new QuotaWindow("codex-weekly", "주간", 40, later, 10080)));
            Check(entry.Reason == "ResetEntry" && entry.Selected == later && entry.Next == later.AddMinutes(-15),
                $"reset entry wrong ({entry.Reason}).");

            // Exhausted: 100% -> 15 minute checks.
            Entry exhausted = await Poll(QuotaProvider.Claude, (p, now) => R(p, new QuotaWindow("session", "세션", 100, now.AddHours(3), 300)));
            Check(exhausted.Reason == "Exhausted" && exhausted.Next == T().AddMinutes(15), "exhausted entry wrong.");

            // Failure: retry reason, Succeeded false, consecutive failures counted, nothing but metadata stored.
            Entry failed = await Poll(QuotaProvider.Claude, (_, _) => null);
            Check(failed is { Reason: "FailureRetry", Succeeded: false, Failures: 1 } && failed.Next == T().AddMinutes(15),
                $"failure entry wrong ({failed.Reason}, failures {failed.Failures}).");

            // Claude reset jitter within one minute is coalesced to one band (two endpoints at most).
            DateTimeOffset weekly = T().AddDays(5);
            var random = new Random(7);
            int lastAnchors = 0;
            for (int poll = 0; poll < 40; poll++)
            {
                client.Clock = client.Clock.AddMinutes(1);
                DateTimeOffset jittered = weekly.AddMilliseconds(random.Next(0, 55_000));
                lastAnchors = (await Poll(QuotaProvider.Claude, (p, now) => R(p,
                    new QuotaWindow("weekly_all", "주간", 30, jittered, 10080), new QuotaWindow("weekly_scoped", "주간 모델", 0, jittered, 10080)))).Anchors;
            }
            int jitterAnchors = lastAnchors - failed.Anchors; // anchors the weekly reset added on top of the earlier one
            Check(jitterAnchors <= 2, $"sub-minute jitter left {jitterAnchors} anchors (expected one band, at most 2 endpoints).");
            // A steady 40s drift is a chain, not jitter: the +/-15m union must not collapse into one pair.
            int chain = 0;
            for (int poll = 0; poll < 12; poll++)
            {
                client.Clock = client.Clock.AddMinutes(1);
                DateTimeOffset moving = T().AddDays(5).AddSeconds(40 * poll);
                chain = (await Poll(QuotaProvider.Claude, (p, now) => R(p, new QuotaWindow("weekly_all", "주간", 30, moving, 10080)))).Anchors;
            }
            Check(chain >= 8 && chain <= 64, $"a 40s drift chain was collapsed to {chain} anchors.");

            // Limits: 256 entries and 128 KiB, newest kept, file still valid JSON, one file only.
            for (int poll = 0; poll < 300; poll++)
            {
                client.Clock = client.Clock.AddMinutes(1);
                await Poll((QuotaProvider)(poll % 3), (p, now) => poll % 7 == 0 ? null
                    : R(p, new QuotaWindow("w", "합성", poll % 100, now.AddMinutes(20 + poll % 50), 300)));
            }
            var (version, entries, bytes, keys) = Read(logPath);
            Check(version == 1 && entries.Count == 256, $"expected 256 entries, found {entries.Count}.");
            Check(bytes <= 128 * 1024, $"file is {bytes} bytes (limit 131072).");
            Check(entries[^1].Attempted == T() && entries.Zip(entries.Skip(1)).All(pair => pair.First.Attempted <= pair.Second.Attempted),
                "newest entries must be kept in order.");
            string[] expected = ["Provider", "AttemptedAtUtc", "RecordedAtUtc", "NextCheckUtc", "Reason", "SelectedResetUtc",
                "Succeeded", "ConsecutiveFailures", "ResetAnchorCount"];
            Check(keys.SetEquals(expected), "the stored fields are not exactly the nine scheduling fields: " + string.Join(",", keys));
            Check(Directory.GetFiles(Path.Combine(directory, "logs")).Length == 1, "temporary files were left behind.");
            return $"polling diagnostics (Regular/ResetPeriod/ResetEntry/Exhausted/FailureRetry with next + selected reset, 1m jitter -> {jitterAnchors} anchors, " +
                   $"40s chain kept {chain}, 256 entries {bytes / 1024.0:0.0}KiB, 9 scheduling fields only)";
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
