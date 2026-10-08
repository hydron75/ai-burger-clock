using System.Globalization;
using System.Security;
using System.Text;

namespace AiBurgerClock;

// The Windows host's warning sink: no file is created until a warning is received.
// StatusTicker calls it on the UI thread; failed logging must not stop a display tick.
internal sealed class WindowsWarningLog
{
    internal const int MaximumFileBytes = 64 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly string directory;
    private readonly object gate = new();

    internal WindowsWarningLog(string? directory = null) =>
        this.directory = directory ?? Path.Combine(AppPaths.DataDirectory, "logs");

    internal string LogPath => Path.Combine(directory, "status-ticker.log");
    internal string PreviousLogPath => Path.Combine(directory, "status-ticker.previous.log");

    internal void Write(string message)
    {
        try
        {
            byte[] line = EncodeLine(message);
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                using (var current = new FileStream(LogPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    if (current.Length <= MaximumFileBytes - line.Length)
                    {
                        current.Position = current.Length;
                        current.Write(line);
                        return;
                    }
                }

                // If rotation fails (for example a locked previous file), skip this warning.
                // Appending anyway would defeat the size limit.
                File.Move(LogPath, PreviousLogPath, overwrite: true);
                using var next = new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                next.Write(line);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException)
        {
            // This optional diagnostic must never turn a recoverable duplicate into a UI failure.
        }
    }

    private static byte[] EncodeLine(string message)
    {
        string prefix = "[" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
            + "] WARN ";
        string singleLine = string.Concat(message.Select(character =>
            char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character));
        byte[] line = new byte[MaximumFileBytes];
        int written = Utf8.GetBytes(prefix.AsSpan(), line);
        int available = MaximumFileBytes - written - 1; // Include the final LF in the limit.
        if (Utf8.GetByteCount(singleLine) <= available)
        {
            written += Utf8.GetBytes(singleLine.AsSpan(), line.AsSpan(written, available));
        }
        else
        {
            const string truncated = " [truncated]";
            int suffixBytes = Utf8.GetByteCount(truncated);
            // Encoder.Convert stops at a complete UTF-8 character, not midway through an emoji.
            Utf8.GetEncoder().Convert(singleLine.AsSpan(), line.AsSpan(written, available - suffixBytes), flush: true,
                out _, out int bytesUsed, out _);
            written += bytesUsed;
            written += Utf8.GetBytes(truncated.AsSpan(), line.AsSpan(written, suffixBytes));
        }
        line[written++] = (byte)'\n';
        Array.Resize(ref line, written);
        return line;
    }
}
