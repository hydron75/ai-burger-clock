using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiBurgerClock;

// AttemptId is a fresh Guid per read, not an account identifier. This closed
// schema deliberately excludes usage, reset timestamps, labels and raw errors.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record QuotaEarlyResetDiagnosticEntry(
    [property: JsonRequired] string AttemptId,
    [property: JsonRequired] string Provider,
    [property: JsonRequired] DateTimeOffset AttemptedAtUtc,
    [property: JsonRequired] DateTimeOffset RecordedAtUtc,
    [property: JsonRequired] bool Succeeded,
    [property: JsonRequired] long? Sequence,
    [property: JsonRequired] long ContinuityId,
    [property: JsonRequired] string Outcome,
    [property: JsonRequired] string Reason);

internal sealed class QuotaEarlyResetDiagnosticLog
{
    internal const int MaximumEntries = 256;
    internal const int MaximumBytes = 128 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object gate = new();
    private readonly string directory;

    internal QuotaEarlyResetDiagnosticLog(string directory)
    {
        this.directory = directory;
        LogPath = Path.Combine(directory, "quota-early-reset-decisions-v1.jsonl");
    }

    internal string LogPath { get; }

    // The completion row is appended once. UI evaluation updates that row, so
    // polling and host callbacks never create two decisions for one read.
    internal bool TryAppend(QuotaEarlyResetDiagnosticEntry entry)
    {
        lock (gate)
        {
            try
            {
                if (!IsValid(entry)) return false;
                List<QuotaEarlyResetDiagnosticEntry> entries = ReadExisting();
                if (entries.Any(existing => SameAttempt(existing.AttemptId, entry.AttemptId))) return false;
                entries.Add(entry);
                return TryReplace(entries);
            }
            catch (Exception exception) when (IsDiagnosticFailure(exception)) { return false; }
        }
    }

    internal bool TryUpdate(string attemptId, string outcome, string reason)
    {
        lock (gate)
        {
            try
            {
                if (!IsAttemptId(attemptId) || !IsDecision(outcome, reason)) return false;
                List<QuotaEarlyResetDiagnosticEntry> entries = ReadExisting();
                int index = entries.FindIndex(entry => SameAttempt(entry.AttemptId, attemptId));
                if (index < 0) return false;
                QuotaEarlyResetDiagnosticEntry updated = entries[index] with { Outcome = outcome, Reason = reason };
                if (!IsValid(updated)) return false;
                entries[index] = updated;
                return TryReplace(entries);
            }
            catch (Exception exception) when (IsDiagnosticFailure(exception)) { return false; }
        }
    }

    private bool TryReplace(List<QuotaEarlyResetDiagnosticEntry> entries)
    {
        string? temporaryPath = null;
        try
        {
            if (entries.Count > MaximumEntries)
                entries.RemoveRange(0, entries.Count - MaximumEntries);
            byte[] bytes = Serialize(entries);
            while (bytes.Length > MaximumBytes && entries.Count > 1)
            {
                entries.RemoveAt(0);
                bytes = Serialize(entries);
            }
            if (bytes.Length > MaximumBytes) return false;

            CheckDirectory();
            Directory.CreateDirectory(directory);
            CheckDirectory();
            CheckTarget();
            temporaryPath = LogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (FileStream temporary = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                temporary.Write(bytes);
                temporary.Flush(flushToDisk: true);
            }
            // Only a complete sibling file is renamed over the previous log.
            // Recheck reparse/read-only targets before the atomic replacement.
            CheckTarget();
            File.Move(temporaryPath, LogPath, overwrite: true);
            temporaryPath = null;
            return true;
        }
        catch (Exception exception) when (IsDiagnosticFailure(exception)) { return false; }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception exception) when (IsDiagnosticFailure(exception)) { }
            }
        }
    }

    private List<QuotaEarlyResetDiagnosticEntry> ReadExisting()
    {
        CheckDirectory();
        if (!CheckTarget()) return [];
        using FileStream stream = new(LogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Diagnostic history is oversized.");
        byte[] bytes = new byte[MaximumBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > MaximumBytes) throw new InvalidDataException("Diagnostic history is oversized.");
        string text = StrictUtf8.GetString(bytes, 0, count);
        string[] lines = text.Split('\n');
        int lineCount = lines.Length;
        if (lineCount > 0 && lines[^1].Length == 0) lineCount--;
        if (lineCount is < 1 or > MaximumEntries)
            throw new InvalidDataException("Unrecognized diagnostic row count.");

        List<QuotaEarlyResetDiagnosticEntry> entries = new(lineCount);
        HashSet<string> attempts = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < lineCount; index++)
        {
            string line = lines[index];
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Unrecognized diagnostic row.");
            JsonProperty[] fields = document.RootElement.EnumerateObject().ToArray();
            if (fields.Length != 9 || fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != 9)
                throw new InvalidDataException("Unrecognized diagnostic fields.");
            QuotaEarlyResetDiagnosticEntry? entry = JsonSerializer.Deserialize(
                line, QuotaJsonContext.Default.QuotaEarlyResetDiagnosticEntry);
            if (entry is null || !IsValid(entry) || !attempts.Add(entry.AttemptId))
                throw new InvalidDataException("Unrecognized diagnostic metadata.");
            entries.Add(entry);
        }
        return entries;
    }

    private void CheckDirectory()
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(directory); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Unrecognized diagnostic directory.");
    }

    private bool CheckTarget()
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(LogPath); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
            throw new InvalidDataException("Unrecognized diagnostic target.");
        return true;
    }

    private static byte[] Serialize(IEnumerable<QuotaEarlyResetDiagnosticEntry> entries)
    {
        using MemoryStream stream = new();
        foreach (QuotaEarlyResetDiagnosticEntry entry in entries)
        {
            // Default metadata is compact; each serialization is exactly one
            // JSON object, followed by one LF regardless of host platform.
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(entry, QuotaJsonContext.Default.QuotaEarlyResetDiagnosticEntry);
            stream.Write(line);
            stream.WriteByte((byte)'\n');
        }
        return stream.ToArray();
    }

    private static bool IsValid(QuotaEarlyResetDiagnosticEntry? entry) => entry is not null
        && IsAttemptId(entry.AttemptId)
        && entry.Provider is "Codex" or "Claude" or "Gemini"
        && IsUtc(entry.AttemptedAtUtc) && IsUtc(entry.RecordedAtUtc)
        && (entry.Sequence is null || entry.Sequence > 0)
        && entry.ContinuityId > 0
        && IsDecision(entry.Outcome, entry.Reason)
        && (entry.Succeeded
            ? entry.Reason is not ("ReadFailed" or "ReadCancelled")
            : entry.Outcome == "Excluded" && entry.Reason is "ReadFailed" or "ReadCancelled");

    private static bool IsDecision(string outcome, string reason) => outcome switch
    {
        "Alert" => reason == "Alert",
        "Excluded" => reason is "NotEvaluated" or "ReadFailed" or "ReadCancelled" or "HostUnavailable"
            or "SettingChangedBeforeEvaluation" or "Disabled" or "InvalidObservation" or "DuplicateObservation"
            or "NoPreviousSuccess" or "ContinuityChanged" or "ClockNotForward" or "WindowSetChanged"
            or "NoWeeklyWindow" or "ResetUnavailable" or "InvalidResetWindow" or "NotBeforeScheduledReset"
            or "UsageNotDecreased",
        _ => false
    };

    private static bool IsAttemptId(string? value) => value?.Length == 32
        && Guid.TryParseExact(value, "N", out Guid guid) && guid != Guid.Empty;

    private static bool SameAttempt(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool IsUtc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;
    private static bool IsDiagnosticFailure(Exception exception) => exception is IOException or InvalidDataException
        or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException or ArgumentException;
}
