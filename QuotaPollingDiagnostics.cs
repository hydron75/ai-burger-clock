using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiBurgerClock;

// Deliberately contains scheduling metadata only: no CLI output, account identity,
// plan, window identity, percentages, or exception messages may be stored here.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record QuotaPollingDiagnostic(
    [property: JsonRequired] string Provider,
    [property: JsonRequired] DateTimeOffset AttemptedAtUtc,
    [property: JsonRequired] DateTimeOffset RecordedAtUtc,
    [property: JsonRequired] DateTimeOffset NextCheckUtc,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] DateTimeOffset? SelectedResetUtc,
    [property: JsonRequired] bool Succeeded,
    [property: JsonRequired] int ConsecutiveFailures,
    [property: JsonRequired] int ResetAnchorCount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record QuotaPollingDiagnosticFile(
    [property: JsonRequired] int Version,
    [property: JsonRequired] IReadOnlyList<QuotaPollingDiagnostic> Entries);

internal sealed class QuotaPollingDiagnosticLog
{
    internal const int MaximumEntries = 256;
    internal const int MaximumBytes = 128 * 1024;
    private const int CurrentVersion = 1;
    private readonly object gate = new();
    private readonly string directory;

    internal QuotaPollingDiagnosticLog(string directory)
    {
        this.directory = directory;
        LogPath = Path.Combine(directory, "quota-polling-diagnostics.json");
    }

    internal string LogPath { get; }

    // A diagnostic failure must not turn an otherwise successful quota read into
    // a failed read or interrupt recovery notification delivery. Unknown/corrupt
    // existing files are preserved for inspection rather than silently replaced.
    internal bool TryWrite(QuotaPollingDiagnostic entry)
    {
        lock (gate)
        {
            string? temporaryPath = null;
            try
            {
                if (!IsValid(entry)) return false;
                List<QuotaPollingDiagnostic> entries = ReadExisting();
                entries.Add(entry);
                if (entries.Count > MaximumEntries)
                    entries.RemoveRange(0, entries.Count - MaximumEntries);

                byte[] bytes = Serialize(entries);
                while (bytes.Length > MaximumBytes && entries.Count > 1)
                {
                    entries.RemoveAt(0);
                    bytes = Serialize(entries);
                }
                if (bytes.Length > MaximumBytes) return false;

                Directory.CreateDirectory(directory);
                temporaryPath = LogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (FileStream temporary = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    temporary.Write(bytes);
                    temporary.Flush(flushToDisk: true);
                }
                // The sibling temporary file keeps the replacement on the same
                // volume. Readers see a complete old or new JSON document.
                File.Move(temporaryPath, LogPath, overwrite: true);
                temporaryPath = null;
                return true;
            }
            catch (Exception exception) when (IsDiagnosticFailure(exception))
            {
                return false;
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    // Only this call's uniquely named temporary file is owned.
                    try { File.Delete(temporaryPath); }
                    catch (Exception exception) when (IsDiagnosticFailure(exception)) { }
                }
            }
        }
    }

    private List<QuotaPollingDiagnostic> ReadExisting()
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(LogPath); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Unrecognized diagnostic target.");

        using FileStream stream = new(LogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes)
            throw new InvalidDataException("Diagnostic history exceeds its size limit.");
        // Bound the read itself too, even if another process can modify the file
        // despite sharing rules on a particular filesystem.
        byte[] bytes = new byte[MaximumBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > MaximumBytes)
            throw new InvalidDataException("Diagnostic history exceeds its size limit.");
        QuotaPollingDiagnosticFile? file = JsonSerializer.Deserialize(
            bytes.AsSpan(0, count), QuotaJsonContext.Default.QuotaPollingDiagnosticFile);
        if (file is null || file.Version != CurrentVersion || file.Entries is null
            || file.Entries.Count > MaximumEntries || file.Entries.Any(entry => !IsValid(entry)))
            throw new InvalidDataException("Unrecognized diagnostic history.");
        return [.. file.Entries];
    }

    private static byte[] Serialize(List<QuotaPollingDiagnostic> entries) =>
        JsonSerializer.SerializeToUtf8Bytes(new QuotaPollingDiagnosticFile(CurrentVersion, entries),
            QuotaJsonContext.Default.QuotaPollingDiagnosticFile);

    private static bool IsValid(QuotaPollingDiagnostic? entry) => entry is not null
        && entry.Provider is "Codex" or "Claude" or "Gemini"
        && entry.Reason is "Regular" or "LowRemaining" or "Exhausted" or "ResetPeriod"
            or "ResetEntry" or "FailureRetry" or "Overdue"
        && IsUtc(entry.AttemptedAtUtc) && IsUtc(entry.RecordedAtUtc) && IsUtc(entry.NextCheckUtc)
        && (entry.SelectedResetUtc is null || IsUtc(entry.SelectedResetUtc.Value))
        && entry.ConsecutiveFailures >= 0 && entry.ResetAnchorCount is >= 0 and <= 64;

    private static bool IsUtc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;

    private static bool IsDiagnosticFailure(Exception exception) => exception is IOException
        or InvalidDataException or UnauthorizedAccessException or SecurityException or JsonException
        or NotSupportedException or ArgumentException;
}
