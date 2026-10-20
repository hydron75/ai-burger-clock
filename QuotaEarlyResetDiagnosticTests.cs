using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

internal static class QuotaEarlyResetDiagnosticTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        string root = Path.Combine(directory, "quota-early-reset-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int assertions = 0;
        void Check(bool value, string description)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Early-reset diagnostics test: " + description);
        }

        DateTimeOffset basis = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        // Deterministic IDs belong to synthetic fixtures only. Production callers
        // generate a fresh Guid per attempt, never an account-derived identifier.
        string Id(int index) => "abcdefabcdefabcdefabcdef" + (index + 1).ToString("x8", CultureInfo.InvariantCulture);
        QuotaEarlyResetDiagnosticEntry Entry(int index, string provider = "Codex", string reason = "NotEvaluated") => new(
            Id(index), provider, basis.AddSeconds(index), basis.AddSeconds(index + 1),
            true, index + 1, 1, "Excluded", reason);
        string Line(QuotaEarlyResetDiagnosticEntry entry) => JsonSerializer.Serialize(
            entry, QuotaJsonContext.Default.QuotaEarlyResetDiagnosticEntry);
        QuotaEarlyResetDiagnosticEntry[] Read(string path) => File.ReadAllLines(path, new UTF8Encoding(false, true))
            .Select(line => JsonSerializer.Deserialize(line, QuotaJsonContext.Default.QuotaEarlyResetDiagnosticEntry)
                ?? throw new InvalidOperationException("A synthetic diagnostic row was null.")).ToArray();

        var log = new QuotaEarlyResetDiagnosticLog(Path.Combine(root, "ordinary"));
        Check(log.LogPath == Path.Combine(root, "ordinary", "quota-early-reset-decisions-v1.jsonl"),
            "the independent versioned JSONL filename does not touch polling diagnostics");
        Check(!Directory.Exists(Path.GetDirectoryName(log.LogPath)), "construction has no filesystem side effects");
        Check(!log.TryUpdate(Id(0), "Alert", "Alert"), "update cannot invent a missing attempt");
        Check(!Directory.Exists(Path.GetDirectoryName(log.LogPath)), "a missing update creates neither file nor directory");
        Check(log.TryAppend(Entry(0)), "poll completion appends one provisional decision");
        Check(Read(log.LogPath).SequenceEqual(new[] { Entry(0) }), "all fields round-trip with generated metadata");
        Check(File.ReadAllBytes(log.LogPath)[^1] == (byte)'\n', "one complete JSON line ends with LF on each host");
        Check(!log.TryAppend(Entry(0)), "the same attempt cannot append a duplicate row");
        Check(!log.TryAppend(Entry(0) with { AttemptId = Id(0).ToUpperInvariant() }),
            "Guid casing cannot create a duplicate attempt");
        Check(log.TryUpdate(Id(0), "Alert", "Alert"), "UI evaluation updates the existing row");
        Check(Read(log.LogPath).SequenceEqual(new[] { Entry(0) with { Outcome = "Alert", Reason = "Alert" } }),
            "updating a decision preserves timestamps, Provider, sequence and continuity metadata");
        Check(File.ReadAllLines(log.LogPath).Length == 1, "completion plus evaluation is still one line per read");
        Check(log.TryUpdate(Id(0), "Excluded", "SettingChangedBeforeEvaluation"),
            "setting-epoch invalidation updates the same attempt");
        Check(log.TryUpdate(Id(0), "Excluded", "HostUnavailable"), "missing host callbacks have a fixed diagnostic reason");
        Check(Read(log.LogPath)[0].Reason == "HostUnavailable", "the final decision remains readable after updates");
        Check(new QuotaEarlyResetDiagnosticLog(Path.GetDirectoryName(log.LogPath)!).TryAppend(Entry(1)),
            "a fresh logger can preserve history across an application restart");
        Check(Read(log.LogPath).Length == 2, "restart appends rather than clearing decisions");

        string[] reasons = ["NotEvaluated", "HostUnavailable", "SettingChangedBeforeEvaluation", "Disabled",
            "InvalidObservation", "DuplicateObservation", "NoPreviousSuccess", "ContinuityChanged", "ClockNotForward",
            "WindowSetChanged", "NoWeeklyWindow", "ResetUnavailable", "InvalidResetWindow", "NotBeforeScheduledReset",
            "UsageNotDecreased"];
        string[] providers = ["Codex", "Claude", "Gemini"];
        var allowed = new QuotaEarlyResetDiagnosticLog(Path.Combine(root, "allowlist"));
        int allowedIndex = 100;
        foreach (string provider in providers)
        foreach (string reason in reasons)
        {
            QuotaEarlyResetDiagnosticEntry entry = Entry(allowedIndex++, provider, reason);
            Check(allowed.TryAppend(entry), "each fixed Provider/exclusion-reason combination is accepted");
            Check(Read(allowed.LogPath)[^1] == entry, "fixed outcomes cannot smuggle additional free-form text");
        }
        foreach (string provider in providers)
        {
            QuotaEarlyResetDiagnosticEntry alert = Entry(allowedIndex++, provider) with { Outcome = "Alert", Reason = "Alert" };
            Check(allowed.TryAppend(alert), "each Provider supports one fixed alert decision");
            Check(Read(allowed.LogPath)[^1] == alert, "the alert schema is identical for all Providers");
            foreach (string reason in new[] { "ReadFailed", "ReadCancelled" })
            {
                QuotaEarlyResetDiagnosticEntry failed = Entry(allowedIndex++, provider, reason) with { Succeeded = false, Sequence = null };
                Check(allowed.TryAppend(failed), "failure and cancellation each append one row without a success sequence");
                Check(!allowed.TryUpdate(failed.AttemptId, "Alert", "Alert"), "a failed read cannot be relabelled as an alert");
            }
        }

        string[] allowedFields = ["AttemptId", "AttemptedAtUtc", "ContinuityId", "Outcome", "Provider",
            "Reason", "RecordedAtUtc", "Sequence", "Succeeded"];
        foreach (string line in File.ReadAllLines(allowed.LogPath))
        {
            using JsonDocument json = JsonDocument.Parse(line);
            Check(json.RootElement.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(allowedFields),
                "privacy schema stores no usage/remaining/reset/plan/account/label/exception fields");
        }
        Check(Read(allowed.LogPath).All(entry => Guid.TryParseExact(entry.AttemptId, "N", out _)),
            "attempt correlation contains only synthetic GUID-shaped per-call IDs");

        var invalid = new QuotaEarlyResetDiagnosticLog(Path.Combine(root, "invalid"));
        QuotaEarlyResetDiagnosticEntry[] badEntries =
        [
            Entry(0) with { AttemptId = null! },
            Entry(0) with { AttemptId = "synthetic-account@example.invalid" },
            Entry(0) with { AttemptId = Guid.Empty.ToString("N") },
            Entry(0) with { AttemptId = Guid.NewGuid().ToString("D") },
            Entry(0) with { Provider = null! },
            Entry(0) with { Provider = "Other" },
            Entry(0) with { Provider = "Codex\nsynthetic-private-value" },
            Entry(0) with { Outcome = "Unknown" },
            Entry(0) with { Outcome = null! },
            Entry(0) with { Outcome = "Alert" },
            Entry(0) with { Reason = "Alert" },
            Entry(0) with { Reason = null! },
            Entry(0) with { Reason = "synthetic-free-form-error" },
            Entry(0) with { Reason = new string('x', 4096) },
            Entry(0) with { Reason = "ReadFailed" },
            Entry(0) with { Reason = "ReadCancelled" },
            Entry(0) with { Succeeded = false },
            Entry(0) with { AttemptedAtUtc = default },
            Entry(0) with { RecordedAtUtc = default },
            Entry(0) with { AttemptedAtUtc = basis.ToOffset(TimeSpan.FromHours(9)) },
            Entry(0) with { RecordedAtUtc = basis.ToOffset(TimeSpan.FromHours(9)) },
            Entry(0) with { Sequence = 0 },
            Entry(0) with { Sequence = -1 },
            Entry(0) with { ContinuityId = 0 },
            Entry(0) with { ContinuityId = -1 }
        ];
        foreach (QuotaEarlyResetDiagnosticEntry entry in badEntries)
            Check(!invalid.TryAppend(entry), "invalid metadata is rejected before creating a diagnostic file");
        Check(!File.Exists(invalid.LogPath), "rejected values never create an empty or privacy-unsafe history");
        foreach ((string id, string outcome, string reason) in new[]
        {
            ("invalid", "Alert", "Alert"), (Id(0), "Unknown", "Disabled"), (Id(0), "Excluded", "Unknown"),
            (Id(0), "Alert", "Disabled"), (Id(0), "Excluded", "Alert"), (Id(0), "Excluded", "ReadFailed")
        })
            Check(!log.TryUpdate(id, outcome, reason), "invalid or inconsistent updates are rejected");
        Check(log.TryAppend(Entry(2) with { Sequence = long.MaxValue, ContinuityId = long.MaxValue }),
            "positive upper-bound counters serialize without overflow");
        Check(log.TryAppend(Entry(3) with { Sequence = null, RecordedAtUtc = basis.AddHours(-1), Reason = "ClockNotForward" }),
            "a wall-clock reversal can itself be diagnosed without inventing a timestamp");

        var ring = new QuotaEarlyResetDiagnosticLog(Path.Combine(root, "ring"));
        for (int index = 0; index < 255; index++)
            Check(ring.TryAppend(Entry(index)), "rotation fixture appends one synthetic read");
        Check(Read(ring.LogPath).Length == 255, "255 reads remain 255 lines");
        Check(ring.TryAppend(Entry(255)) && Read(ring.LogPath).Length == 256, "the 256th read fills the capacity");
        Check(ring.TryAppend(Entry(256)), "a 257th read rotates out only the oldest line");
        Check(Read(ring.LogPath).SequenceEqual(Enumerable.Range(1, 256).Select(index => Entry(index))),
            "rotation preserves order and retains the latest 256 attempts");
        Check(!ring.TryUpdate(Id(0), "Alert", "Alert"), "an evicted attempt cannot be recreated by a late callback");
        Check(ring.TryUpdate(Id(1), "Alert", "Alert") && Read(ring.LogPath).Length == 256,
            "updating the oldest retained read consumes no additional history slot");
        Check(new FileInfo(ring.LogPath).Length <= QuotaEarlyResetDiagnosticLog.MaximumBytes,
            "full history is bounded to 128 KiB");

        string corruptDirectory = Path.Combine(root, "corrupt");
        Directory.CreateDirectory(corruptDirectory);
        var corrupt = new QuotaEarlyResetDiagnosticLog(corruptDirectory);
        string good = Line(Entry(0));
        string[] badFiles =
        [
            "", "not-json\n", "null\n", "{}\n", "[]\n", good + "\n\n", good + "\n" + good + "\n",
            good.Replace("\"Succeeded\":true,", "", StringComparison.Ordinal) + "\n",
            good.Replace("\"ContinuityId\":1", "\"ContinuityId\":1,\"AccountId\":\"synthetic\"", StringComparison.Ordinal) + "\n",
            good.Replace("\"Outcome\":\"Excluded\"", "\"Outcome\":\"Excluded\",\"Outcome\":\"Excluded\"", StringComparison.Ordinal) + "\n",
            good.Replace("\"Codex\"", "\"Unknown\"", StringComparison.Ordinal) + "\n",
            good.Replace("\"NotEvaluated\"", "\"synthetic-server-message\"", StringComparison.Ordinal) + "\n",
            string.Join("\n", Enumerable.Range(0, 257).Select(index => Line(Entry(index)))) + "\n"
        ];
        void CheckPreserved(byte[] bytes, string description)
        {
            File.WriteAllBytes(corrupt.LogPath, bytes);
            Check(!corrupt.TryAppend(Entry(800)), description + " rejects append without escaping an exception");
            Check(!corrupt.TryUpdate(Id(0), "Alert", "Alert"), description + " rejects update without escaping an exception");
            Check(File.ReadAllBytes(corrupt.LogPath).SequenceEqual(bytes), description + " preserves the original file");
        }
        foreach (string text in badFiles)
            CheckPreserved(Encoding.UTF8.GetBytes(text), "unrecognized/corrupt JSONL");
        CheckPreserved([0x7b, 0x22, 0xff, 0x22, 0x3a, 0x31, 0x7d, 0x0a], "invalid UTF-8");
        CheckPreserved(Encoding.UTF8.GetBytes(good.Replace("Codex", "Co\ndex", StringComparison.Ordinal) + "\n"),
            "a multi-line JSON object");
        CheckPreserved(Enumerable.Repeat((byte)' ', QuotaEarlyResetDiagnosticLog.MaximumBytes + 1).ToArray(),
            "oversized existing history");
        byte[] boundary = Encoding.UTF8.GetBytes(good + new string(' ', QuotaEarlyResetDiagnosticLog.MaximumBytes
            - Encoding.UTF8.GetByteCount(good) - 1) + "\n");
        File.WriteAllBytes(corrupt.LogPath, boundary);
        Check(boundary.Length == QuotaEarlyResetDiagnosticLog.MaximumBytes, "the boundary fixture is exactly 128 KiB");
        Check(corrupt.TryAppend(Entry(801)), "a valid bounded file can be read at exactly the byte limit");
        Check(Read(corrupt.LogPath).Length == 2 && new FileInfo(corrupt.LogPath).Length <= QuotaEarlyResetDiagnosticLog.MaximumBytes,
            "rewriting compacts whitespace and keeps the new complete row within the limit");

        var concurrent = new QuotaEarlyResetDiagnosticLog(Path.Combine(root, "concurrent"));
        bool[] results = await Task.WhenAll(Enumerable.Range(0, 48).Select(index => Task.Run(() =>
        {
            QuotaEarlyResetDiagnosticEntry entry = Entry(index, providers[index % providers.Length]);
            return concurrent.TryAppend(entry) && concurrent.TryUpdate(entry.AttemptId, "Alert", "Alert");
        })));
        Check(results.All(value => value), "Provider completions and UI decision updates serialize through the same lock");
        QuotaEarlyResetDiagnosticEntry[] concurrentRows = Read(concurrent.LogPath);
        Check(concurrentRows.Length == 48 && concurrentRows.Select(entry => entry.AttemptId).Distinct().Count() == 48,
            "concurrent methods neither lose reads nor duplicate lines");
        Check(concurrentRows.All(entry => entry.Outcome == "Alert" && entry.Reason == "Alert"),
            "concurrent updates all reach their own previously appended rows");
        Check(Directory.GetFiles(Path.GetDirectoryName(concurrent.LogPath)!, "*.tmp").Length == 0,
            "successful atomic replacements leave no owned temporary files");

        string blockedDirectory = Path.Combine(root, "not-a-directory");
        File.WriteAllText(blockedDirectory, "synthetic blocker");
        var blocked = new QuotaEarlyResetDiagnosticLog(blockedDirectory);
        Check(!blocked.TryAppend(Entry(0)) && !blocked.TryUpdate(Id(0), "Alert", "Alert"),
            "invalid directory topology never fails the calling read or alert");
        Check(File.ReadAllText(blockedDirectory) == "synthetic blocker", "the blocking file is untouched");
        var directoryTarget = new QuotaEarlyResetDiagnosticLog(Path.Combine(root, "directory-target"));
        Directory.CreateDirectory(directoryTarget.LogPath);
        Check(!directoryTarget.TryAppend(Entry(0)) && !directoryTarget.TryUpdate(Id(0), "Alert", "Alert"),
            "a directory at the log filename is not adopted as a history file");
        Check(Directory.Exists(directoryTarget.LogPath), "failure preserves the unrelated target directory");

        var locked = new QuotaEarlyResetDiagnosticLog(Path.Combine(root, "locked"));
        Check(locked.TryAppend(Entry(0)), "the permissions fixture starts with a complete valid row");
        byte[] beforeLock = File.ReadAllBytes(locked.LogPath);
        using (FileStream held = new(locked.LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check(!locked.TryAppend(Entry(1)), "an inaccessible history fails append quietly");
            Check(!locked.TryUpdate(Id(0), "Alert", "Alert"), "an inaccessible history fails update quietly");
        }
        Check(File.ReadAllBytes(locked.LogPath).SequenceEqual(beforeLock), "access failure preserves the complete old history");
        FileAttributes originalAttributes = File.GetAttributes(locked.LogPath);
        try
        {
            File.SetAttributes(locked.LogPath, originalAttributes | FileAttributes.ReadOnly);
            Check(!locked.TryAppend(Entry(1)) && !locked.TryUpdate(Id(0), "Alert", "Alert"),
                "read-only history is preserved even on filesystems allowing replacement of read-only files");
        }
        finally { File.SetAttributes(locked.LogPath, originalAttributes); }
        Check(File.ReadAllBytes(locked.LogPath).SequenceEqual(beforeLock), "read-only rejection preserves bytes");
        Check(locked.TryAppend(Entry(1)), "logging recovers after temporary access restrictions are removed");
        byte[] beforeReplacement = File.ReadAllBytes(locked.LogPath);
        bool replacedWhileReadHeld;
        using (FileStream held = new(locked.LogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Windows permits ReadExisting but refuses the final rename while
            // this handle omits Delete sharing, exercising post-temp cleanup.
            // POSIX can permit atomic replacement despite the old read handle.
            replacedWhileReadHeld = locked.TryAppend(Entry(2));
        }
        Check(replacedWhileReadHeld
                ? Read(locked.LogPath)[^1] == Entry(2)
                : File.ReadAllBytes(locked.LogPath).SequenceEqual(beforeReplacement),
            "the filesystem either commits a complete replacement or retains the complete old history");
        Check(Directory.GetFiles(Path.GetDirectoryName(locked.LogPath)!, "*.tmp").Length == 0,
            "a replacement rejected after temp creation leaves no owned temporary file");

        string backing = Path.Combine(root, "synthetic-link-backing.jsonl");
        File.WriteAllText(backing, good + "\n", new UTF8Encoding(false));
        byte[] backingBytes = File.ReadAllBytes(backing);
        string reparseDirectory = Path.Combine(root, "reparse");
        Directory.CreateDirectory(reparseDirectory);
        var reparse = new QuotaEarlyResetDiagnosticLog(reparseDirectory);
        bool linkCreated = false;
        bool linkUnavailable = false;
        try
        {
            try
            {
                File.CreateSymbolicLink(reparse.LogPath, backing);
                linkCreated = true;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                linkUnavailable = true;
                Console.WriteLine("SKIP early-reset diagnostic symlink fixture: the temporary filesystem disallows link creation.");
            }
            Check(linkCreated || linkUnavailable, "the link fixture either exists or is explicitly unavailable on this filesystem");
            Check(!linkCreated || !reparse.TryAppend(Entry(1)), "an existing reparse history refuses append when links are supported");
            Check(!linkCreated || !reparse.TryUpdate(Id(0), "Alert", "Alert"), "an existing reparse history refuses update when links are supported");
            Check(File.ReadAllBytes(backing).SequenceEqual(backingBytes), "the unrelated symlink target remains unchanged");
        }
        finally { if (linkCreated) File.Delete(reparse.LogPath); }

        string foreignTemporary = log.LogPath + ".foreign.tmp";
        File.WriteAllText(foreignTemporary, "synthetic unrelated temporary file");
        Check(log.TryAppend(Entry(4)), "atomic replacement can coexist with an unrelated temporary file");
        Check(File.ReadAllText(foreignTemporary) == "synthetic unrelated temporary file",
            "cleanup only owns the uniquely named temporary file for the current call");
        Check(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).SequenceEqual(new[] { foreignTemporary }),
            "success and every diagnosed failure leave no owned temporary artifacts");
        return assertions;
    }
}
