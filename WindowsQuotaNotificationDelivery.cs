using System.Security;
using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

internal enum WindowsToastBlockReason
{
    DisabledForApplication,
    DisabledForUser,
    DisabledByGroupPolicy,
    DisabledByManifest
}

// A Windows preference/policy rejection is not an API failure. Do not bypass it
// by falling back to the legacy balloon transport.
internal sealed class WindowsToastBlockedException(WindowsToastBlockReason reason)
    : InvalidOperationException("Windows notifications are disabled.")
{
    internal WindowsToastBlockReason Reason { get; } = Enum.IsDefined(reason)
        ? reason : throw new ArgumentOutOfRangeException(nameof(reason));
}

internal enum WindowsQuotaNotificationKind { Recovery, EarlyReset, Combined }
internal enum WindowsQuotaNotificationTransport { Toast, Balloon }
internal enum WindowsQuotaNotificationResult { Submitted, Failed, Blocked }
internal enum WindowsQuotaNotificationReason
{
    None,
    SynchronousFailure,
    AsynchronousFailure,
    DisabledForApplication,
    DisabledForUser,
    DisabledByGroupPolicy,
    DisabledByManifest
}

// Closed, Windows-only audit metadata: no banner text, account identifiers,
// quota values, reset times, arbitrary errors or exception messages belong here.
internal sealed record WindowsQuotaNotificationDeliveryEntry(
    DateTimeOffset RecordedAtUtc,
    WindowsQuotaNotificationKind Kind,
    QuotaProvider Provider,
    WindowsQuotaNotificationTransport Transport,
    WindowsQuotaNotificationResult Result,
    WindowsQuotaNotificationReason Reason,
    int? HResult);

internal sealed class WindowsQuotaNotificationDelivery
{
    private readonly Action<QuotaNotificationAlert> toastSender;
    private readonly Action<QuotaNotificationAlert> balloonSender;
    private readonly Action<WindowsQuotaNotificationDeliveryEntry> record;

    internal WindowsQuotaNotificationDelivery(Action<QuotaNotificationAlert> toastSender,
        Action<QuotaNotificationAlert> balloonSender, Action<WindowsQuotaNotificationDeliveryEntry>? record = null)
    {
        this.toastSender = toastSender ?? throw new ArgumentNullException(nameof(toastSender));
        this.balloonSender = balloonSender ?? throw new ArgumentNullException(nameof(balloonSender));
        this.record = record ?? new WindowsQuotaNotificationDeliveryLog().Write;
    }

    // Submitted means the synchronous API completed, not that Windows showed a
    // banner or retained it in Action Center. A candidate is never resubmitted
    // merely because no visible banner or persistence confirmation arrives.
    internal void Send(QuotaNotificationAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        try
        {
            toastSender(alert);
            Record(alert, WindowsQuotaNotificationTransport.Toast, WindowsQuotaNotificationResult.Submitted,
                WindowsQuotaNotificationReason.None, null);
            return;
        }
        catch (WindowsToastBlockedException blocked)
        {
            Record(alert, WindowsQuotaNotificationTransport.Toast, WindowsQuotaNotificationResult.Blocked,
                blocked.Reason switch
                {
                    WindowsToastBlockReason.DisabledForApplication => WindowsQuotaNotificationReason.DisabledForApplication,
                    WindowsToastBlockReason.DisabledForUser => WindowsQuotaNotificationReason.DisabledForUser,
                    WindowsToastBlockReason.DisabledByGroupPolicy => WindowsQuotaNotificationReason.DisabledByGroupPolicy,
                    _ => WindowsQuotaNotificationReason.DisabledByManifest
                }, null);
            return;
        }
        catch (Exception error) when (IsDeliveryFailure(error))
        {
            Record(alert, WindowsQuotaNotificationTransport.Toast, WindowsQuotaNotificationResult.Failed,
                WindowsQuotaNotificationReason.SynchronousFailure, error.HResult);
        }

        // Only a synchronous registration/submission failure gets one fallback.
        // A fallback failure remains visible to the host's existing error path.
        try
        {
            balloonSender(alert);
            Record(alert, WindowsQuotaNotificationTransport.Balloon, WindowsQuotaNotificationResult.Submitted,
                WindowsQuotaNotificationReason.None, null);
        }
        catch (Exception error) when (IsDeliveryFailure(error))
        {
            Record(alert, WindowsQuotaNotificationTransport.Balloon, WindowsQuotaNotificationResult.Failed,
                WindowsQuotaNotificationReason.SynchronousFailure, error.HResult);
            throw;
        }
    }

    // An eventual Windows failure is diagnostic only: retrying/falling back at
    // this point could duplicate an already submitted notification.
    internal void RecordToastFailure(QuotaNotificationAlert alert, int hresult) =>
        Record(alert, WindowsQuotaNotificationTransport.Toast, WindowsQuotaNotificationResult.Failed,
            WindowsQuotaNotificationReason.AsynchronousFailure, hresult);

    private void Record(QuotaNotificationAlert alert, WindowsQuotaNotificationTransport transport,
        WindowsQuotaNotificationResult result, WindowsQuotaNotificationReason reason, int? hresult)
    {
        try
        {
            if (!Enum.IsDefined(alert.Provider) || alert.Recovery is null && alert.EarlyReset is null) return;
            WindowsQuotaNotificationKind kind = alert.Recovery is not null && alert.EarlyReset is not null
                ? WindowsQuotaNotificationKind.Combined
                : alert.Recovery is not null ? WindowsQuotaNotificationKind.Recovery : WindowsQuotaNotificationKind.EarlyReset;
            record(new(DateTimeOffset.UtcNow, kind, alert.Provider, transport, result, reason, hresult));
        }
        catch (Exception error) when (IsDeliveryFailure(error))
        {
            // Optional recording must not cause a duplicate send or stop delivery.
        }
    }

    private static bool IsDeliveryFailure(Exception error) => error is not
        (OutOfMemoryException or StackOverflowException or AccessViolationException);
}

internal sealed class WindowsQuotaNotificationDeliveryLog
{
    internal const int MaximumEntries = 256;
    internal const int MaximumBytes = 128 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object gate = new();
    private readonly string directory;

    internal WindowsQuotaNotificationDeliveryLog(string? directory = null)
    {
        this.directory = directory ?? Path.Combine(AppPaths.DataDirectory, "logs");
        LogPath = Path.Combine(this.directory, "quota-notification-delivery-v1.jsonl");
    }

    internal string LogPath { get; }
    internal void Write(WindowsQuotaNotificationDeliveryEntry entry) => TryWrite(entry);

    internal bool TryWrite(WindowsQuotaNotificationDeliveryEntry entry)
    {
        lock (gate)
        {
            string? temporaryPath = null;
            try
            {
                if (!Valid(entry)) return false;
                List<WindowsQuotaNotificationDeliveryEntry> entries = ReadExisting();
                entries.Add(entry);
                if (entries.Count > MaximumEntries) entries.RemoveRange(0, entries.Count - MaximumEntries);
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
                CheckTarget();
                File.Move(temporaryPath, LogPath, overwrite: true);
                temporaryPath = null;
                return true;
            }
            catch (Exception error) when (IsLogFailure(error)) { return false; }
            finally
            {
                if (temporaryPath is not null)
                {
                    try { File.Delete(temporaryPath); }
                    catch (Exception error) when (IsLogFailure(error)) { }
                }
            }
        }
    }

    private List<WindowsQuotaNotificationDeliveryEntry> ReadExisting()
    {
        CheckDirectory();
        if (!CheckTarget()) return [];
        using FileStream stream = new(LogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Oversized notification delivery log.");
        byte[] buffer = new byte[MaximumBytes + 1];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = stream.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > MaximumBytes) throw new InvalidDataException("Oversized notification delivery log.");
        string text = StrictUtf8.GetString(buffer, 0, count);
        string[] lines = text.Split('\n');
        int length = lines.Length;
        if (length > 0 && lines[^1].Length == 0) length--;
        if (length is < 1 or > MaximumEntries) throw new InvalidDataException("Invalid notification delivery row count.");
        List<WindowsQuotaNotificationDeliveryEntry> entries = new(length);
        for (int index = 0; index < length; index++)
        {
            using JsonDocument document = JsonDocument.Parse(lines[index]);
            JsonElement row = document.RootElement;
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != 7 ||
                row.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != 7)
                throw new InvalidDataException("Invalid notification delivery fields.");
            var entry = new WindowsQuotaNotificationDeliveryEntry(
                row.GetProperty("RecordedAtUtc").GetDateTimeOffset(),
                ReadEnum<WindowsQuotaNotificationKind>(row, "Kind"),
                ReadEnum<QuotaProvider>(row, "Provider"),
                ReadEnum<WindowsQuotaNotificationTransport>(row, "Transport"),
                ReadEnum<WindowsQuotaNotificationResult>(row, "Result"),
                ReadEnum<WindowsQuotaNotificationReason>(row, "Reason"),
                row.GetProperty("HResult").ValueKind == JsonValueKind.Null ? null : row.GetProperty("HResult").GetInt32());
            if (!Valid(entry)) throw new InvalidDataException("Invalid notification delivery metadata.");
            entries.Add(entry);
        }
        return entries;
    }

    private static T ReadEnum<T>(JsonElement row, string name) where T : struct, Enum =>
        row.GetProperty(name).ValueKind == JsonValueKind.String &&
        Enum.TryParse(row.GetProperty(name).GetString(), ignoreCase: false, out T value) && Enum.IsDefined(value) &&
        value.ToString() == row.GetProperty(name).GetString()
            ? value : throw new InvalidDataException("Invalid notification delivery enum.");

    private static byte[] Serialize(IEnumerable<WindowsQuotaNotificationDeliveryEntry> entries)
    {
        using MemoryStream stream = new();
        foreach (WindowsQuotaNotificationDeliveryEntry entry in entries)
        {
            using (Utf8JsonWriter writer = new(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("RecordedAtUtc", entry.RecordedAtUtc);
                writer.WriteString("Kind", entry.Kind.ToString());
                writer.WriteString("Provider", entry.Provider.ToString());
                writer.WriteString("Transport", entry.Transport.ToString());
                writer.WriteString("Result", entry.Result.ToString());
                writer.WriteString("Reason", entry.Reason.ToString());
                if (entry.HResult is { } hresult) writer.WriteNumber("HResult", hresult);
                else writer.WriteNull("HResult");
                writer.WriteEndObject();
            }
            stream.WriteByte((byte)'\n');
        }
        return stream.ToArray();
    }

    private static bool Valid(WindowsQuotaNotificationDeliveryEntry? entry) => entry is not null &&
        entry.RecordedAtUtc != default && entry.RecordedAtUtc.Offset == TimeSpan.Zero &&
        Enum.IsDefined(entry.Kind) && Enum.IsDefined(entry.Provider) && Enum.IsDefined(entry.Transport) &&
        Enum.IsDefined(entry.Result) && Enum.IsDefined(entry.Reason) && (entry.Result switch
        {
            WindowsQuotaNotificationResult.Submitted => entry.Reason == WindowsQuotaNotificationReason.None && entry.HResult is null,
            WindowsQuotaNotificationResult.Failed => entry.Reason == WindowsQuotaNotificationReason.SynchronousFailure ||
                entry.Reason == WindowsQuotaNotificationReason.AsynchronousFailure && entry.Transport == WindowsQuotaNotificationTransport.Toast,
            WindowsQuotaNotificationResult.Blocked => entry.Transport == WindowsQuotaNotificationTransport.Toast && entry.HResult is null &&
                entry.Reason is WindowsQuotaNotificationReason.DisabledForApplication or WindowsQuotaNotificationReason.DisabledForUser or
                    WindowsQuotaNotificationReason.DisabledByGroupPolicy or WindowsQuotaNotificationReason.DisabledByManifest,
            _ => false
        });

    private void CheckDirectory()
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(directory); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Unrecognized notification delivery directory.");
    }

    private bool CheckTarget()
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(LogPath); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
            throw new InvalidDataException("Unrecognized notification delivery target.");
        return true;
    }

    private static bool IsLogFailure(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        SecurityException or JsonException or NotSupportedException or ArgumentException or InvalidOperationException or
        KeyNotFoundException or FormatException or OverflowException;
}
