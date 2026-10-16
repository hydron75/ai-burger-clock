using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

internal static class QuotaPollingDiagnosticTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        string root = Path.Combine(directory, "quota-polling-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int assertions = 0;
        void Check(bool value, string description)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Quota polling diagnostics test: " + description);
        }

        DateTimeOffset basis = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        QuotaPollingDiagnostic Entry(int index, string provider = "Gemini", string reason = "Regular") => new(
            provider, basis.AddMinutes(index), basis.AddMinutes(index).AddSeconds(2),
            basis.AddMinutes(index).AddHours(6), reason, null, true, 0, 0);
        QuotaPollingDiagnosticFile Read(string path) => JsonSerializer.Deserialize(
            File.ReadAllBytes(path), QuotaJsonContext.Default.QuotaPollingDiagnosticFile)
            ?? throw new InvalidOperationException("The synthetic diagnostic file was null.");
        byte[] Bytes(QuotaPollingDiagnosticFile file) => JsonSerializer.SerializeToUtf8Bytes(
            file, QuotaJsonContext.Default.QuotaPollingDiagnosticFile);

        string ringDirectory = Path.Combine(root, "ring");
        var log = new QuotaPollingDiagnosticLog(ringDirectory);
        Check(log.LogPath == Path.Combine(ringDirectory, "quota-polling-diagnostics.json"),
            "the caller controls the directory; the filename is stable on both hosts");
        Check(!Directory.Exists(ringDirectory), "construction performs no disk writes");
        Check(log.TryWrite(Entry(0)), "the first reservation creates the directory and history");
        QuotaPollingDiagnosticFile first = Read(log.LogPath);
        Check(first.Version == 1 && first.Entries.Count == 1 && first.Entries[0] == Entry(0),
            "all diagnostic fields round-trip using generated metadata");
        for (int i = 1; i <= 260; i++)
            Check(log.TryWrite(Entry(i)), "a bounded history accepts a new synthetic reservation");
        QuotaPollingDiagnosticFile ring = Read(log.LogPath);
        Check(ring.Entries.Count == QuotaPollingDiagnosticLog.MaximumEntries,
            "all Providers share one 256-entry capacity");
        Check(ring.Entries[0] == Entry(5) && ring.Entries[^1] == Entry(260),
            "only the oldest reservations are discarded and the latest is preserved");
        Check(ring.Entries.SequenceEqual(Enumerable.Range(5, 256).Select(i => Entry(i))),
            "history retains scheduling order rather than sorting or deduplicating events");
        Check(new FileInfo(log.LogPath).Length <= QuotaPollingDiagnosticLog.MaximumBytes,
            "serialized history stays within 128 KiB");
        Check(new QuotaPollingDiagnosticLog(ringDirectory).TryWrite(Entry(261, "Claude")),
            "a fresh logger preserves history across a process-style restart");
        QuotaPollingDiagnosticFile restarted = Read(log.LogPath);
        Check(restarted.Entries[0] == Entry(6) && restarted.Entries[^1] == Entry(261, "Claude"),
            "restart appends without clearing existing reservations");

        string[] reasons = ["Regular", "LowRemaining", "Exhausted", "ResetPeriod", "ResetEntry", "FailureRetry", "Overdue"];
        string[] providers = ["Codex", "Claude", "Gemini"];
        var reasonsLog = new QuotaPollingDiagnosticLog(Path.Combine(root, "reasons"));
        foreach (string provider in providers)
        foreach (string reason in reasons)
        {
            QuotaPollingDiagnostic entry = Entry(0, provider, reason) with
            {
                Succeeded = reason != "FailureRetry",
                ConsecutiveFailures = reason == "FailureRetry" ? 2 : 0,
                SelectedResetUtc = reason is "ResetPeriod" or "ResetEntry" ? basis.AddHours(1) : null,
                ResetAnchorCount = 2
            };
            Check(reasonsLog.TryWrite(entry), "each allowlisted Provider/reason combination is accepted");
            Check(Read(reasonsLog.LogPath).Entries[^1] == entry,
                "selected-reset and failure metadata retain their distinct meanings");
        }

        using (JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(reasonsLog.LogPath)))
        {
            Check(json.RootElement.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "Entries", "Version" }),
                "the root contains only schema version and scheduling entries");
            string[] allowed = ["AttemptedAtUtc", "ConsecutiveFailures", "NextCheckUtc", "Provider", "Reason",
                "RecordedAtUtc", "ResetAnchorCount", "SelectedResetUtc", "Succeeded"];
            Check(json.RootElement.GetProperty("Entries").EnumerateArray().All(entry =>
                entry.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(allowed)),
                "privacy schema excludes account/plan/usage/window IDs/raw CLI/error messages");
            Check(json.RootElement.GetProperty("Entries").EnumerateArray().All(entry =>
                providers.Contains(entry.GetProperty("Provider").GetString())
                && reasons.Contains(entry.GetProperty("Reason").GetString())),
                "the only stored free-form strings are short fixed allowlisted labels");
        }

        var concurrent = new QuotaPollingDiagnosticLog(Path.Combine(root, "concurrent"));
        Task<bool>[] writers = Enumerable.Range(0, 48).Select(index => Task.Run(() =>
            concurrent.TryWrite(Entry(index, providers[index % providers.Length])))).ToArray();
        bool[] writes = await Task.WhenAll(writers);
        Check(writes.All(value => value), "concurrent Provider writes all complete without racing replacement");
        QuotaPollingDiagnosticFile concurrentFile = Read(concurrent.LogPath);
        Check(concurrentFile.Entries.Count == 48 && concurrentFile.Entries.Select(e => e.AttemptedAtUtc).Distinct().Count() == 48,
            "same-logger concurrency does not lose or duplicate Provider reservations");
        Check(Directory.GetFiles(Path.GetDirectoryName(concurrent.LogPath)!, "*.tmp").Length == 0,
            "successful atomic replacements leave no owned temporary files");

        QuotaPollingDiagnostic[] invalidEntries =
        [
            Entry(0) with { Provider = "Other" },
            Entry(0) with { Provider = "Gemini\nsynthetic-private-value" },
            Entry(0) with { Reason = "unknown" },
            Entry(0) with { Reason = new string('x', 1024) },
            Entry(0) with { AttemptedAtUtc = default },
            Entry(0) with { RecordedAtUtc = default },
            Entry(0) with { NextCheckUtc = default },
            Entry(0) with { SelectedResetUtc = default(DateTimeOffset) },
            Entry(0) with { RecordedAtUtc = basis.ToOffset(TimeSpan.FromHours(9)) },
            Entry(0) with { SelectedResetUtc = basis.ToOffset(TimeSpan.FromHours(9)) },
            Entry(0) with { ConsecutiveFailures = -1 },
            Entry(0) with { ResetAnchorCount = -1 },
            Entry(0) with { ResetAnchorCount = 65 }
        ];
        var invalid = new QuotaPollingDiagnosticLog(Path.Combine(root, "invalid"));
        foreach (QuotaPollingDiagnostic entry in invalidEntries)
            Check(!invalid.TryWrite(entry), "invalid labels/dates/counts cannot be persisted");
        Check(!File.Exists(invalid.LogPath), "rejected metadata does not create an empty history");

        var maximum = new QuotaPollingDiagnosticLog(Path.Combine(root, "maximum"));
        Check(maximum.TryWrite(Entry(0) with { ResetAnchorCount = 64, ConsecutiveFailures = int.MaxValue,
            SelectedResetUtc = basis.AddYears(1), Succeeded = false }), "valid upper-bound counters do not overflow");
        Check(new FileInfo(maximum.LogPath).Length <= QuotaPollingDiagnosticLog.MaximumBytes,
            "upper-bound metadata still satisfies the serialized size limit");

        string corruptDirectory = Path.Combine(root, "corrupt");
        Directory.CreateDirectory(corruptDirectory);
        var corrupt = new QuotaPollingDiagnosticLog(corruptDirectory);
        string validEntry = Encoding.UTF8.GetString(Bytes(new QuotaPollingDiagnosticFile(1, [Entry(0)])));
        string[] invalidFiles =
        [
            "not-json", "null", "{}", "{\"Version\":2,\"Entries\":[]}",
            "{\"Version\":1,\"Entries\":[],\"Unknown\":true}",
            "{\"Version\":1,\"Entries\":null}", "{\"Version\":1,\"Entries\":[null]}",
            validEntry.Replace("\"Succeeded\":true,", "", StringComparison.Ordinal),
            validEntry.Replace("\"ResetAnchorCount\":0", "\"ResetAnchorCount\":0,\"AccountId\":\"synthetic\"", StringComparison.Ordinal),
            validEntry.Replace("\"Regular\"", "\"unknown\"", StringComparison.Ordinal)
        ];
        foreach (string text in invalidFiles)
        {
            byte[] original = Encoding.UTF8.GetBytes(text);
            File.WriteAllBytes(corrupt.LogPath, original);
            Check(!corrupt.TryWrite(Entry(1)), "corrupt/unknown schema history returns false, not an exception");
            Check(File.ReadAllBytes(corrupt.LogPath).SequenceEqual(original),
                "unrecognized history is preserved rather than silently overwritten");
        }
        byte[] oversized = new byte[QuotaPollingDiagnosticLog.MaximumBytes + 1];
        Array.Fill(oversized, (byte)' ');
        File.WriteAllBytes(corrupt.LogPath, oversized);
        Check(!corrupt.TryWrite(Entry(1)) && File.ReadAllBytes(corrupt.LogPath).SequenceEqual(oversized),
            "oversized existing files are neither read unboundedly nor overwritten");
        byte[] tooMany = Bytes(new QuotaPollingDiagnosticFile(1, Enumerable.Range(0, 257).Select(i => Entry(i)).ToArray()));
        File.WriteAllBytes(corrupt.LogPath, tooMany);
        Check(!corrupt.TryWrite(Entry(258)) && File.ReadAllBytes(corrupt.LogPath).SequenceEqual(tooMany),
            "an unrecognized entry count is not silently adopted");

        string blocked = Path.Combine(root, "not-a-directory");
        File.WriteAllText(blocked, "synthetic blocker");
        Check(!new QuotaPollingDiagnosticLog(blocked).TryWrite(Entry(0)),
            "unwritable directory topology cannot interrupt quota polling");
        Check(File.ReadAllText(blocked) == "synthetic blocker", "failure leaves the blocking file unchanged");
        string directoryTarget = Path.Combine(root, "directory-target");
        var directoryLog = new QuotaPollingDiagnosticLog(directoryTarget);
        Directory.CreateDirectory(directoryLog.LogPath);
        Check(!directoryLog.TryWrite(Entry(0)), "a directory at the target filename is not replaced");
        Check(Directory.Exists(directoryLog.LogPath), "an unrelated target directory remains intact");

        var locked = new QuotaPollingDiagnosticLog(Path.Combine(root, "locked"));
        Check(locked.TryWrite(Entry(0)), "the locked-file fixture starts with valid owned history");
        byte[] beforeLock = File.ReadAllBytes(locked.LogPath);
        using (FileStream held = new(locked.LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check(!locked.TryWrite(Entry(1)), "an exclusively locked history fails quietly without blocking polling");
        }
        Check(File.ReadAllBytes(locked.LogPath).SequenceEqual(beforeLock),
            "lock failure preserves the existing complete history");
        Check(locked.TryWrite(Entry(1)), "a later reservation can recover after the lock is released");

        string foreignTemporary = Path.Combine(ringDirectory, "quota-polling-diagnostics.json.foreign.tmp");
        File.WriteAllText(foreignTemporary, "synthetic unrelated temporary file");
        Check(log.TryWrite(Entry(262)), "a normal append succeeds beside an unrelated temporary file");
        Check(File.ReadAllText(foreignTemporary) == "synthetic unrelated temporary file",
            "cleanup never scans or deletes unrelated temporary files");
        Check(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).SequenceEqual(new[] { foreignTemporary }),
            "success and failure leave no owned temporary artifacts");
        return assertions;
    }
}
