using System.Text.Json;

namespace AiBurgerClock;

internal sealed record WindowsQuotaExportSettings(int Version, bool Enabled, string SiteUrl, string SourceId, long Seq);
internal interface IWindowsQuotaExportSettingsStore
{
    WindowsQuotaExportSettings Load();
    WindowsQuotaExportSettings Update(bool enabled, string siteUrl);
    WindowsQuotaExportSettings ReserveSequence();
}

// One normal tray process owns this Windows-only file. No token or account data.
// Reserve and durably rename BEFORE making a request, including an explicit test.
internal sealed class WindowsQuotaExportSettingsStore(string path) : IWindowsQuotaExportSettingsStore
{
    internal const string FileName = "mobile-sites-windows-v1.json";
    internal const long MaximumSequence = 9_007_199_254_740_991;
    private readonly object sync = new();
    private readonly string filePath = Path.GetFullPath(path);
    private WindowsQuotaExportSettings? current;

    internal static Uri Endpoint(string siteUrl)
    {
        if (siteUrl.Length > 2048 || !Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("An HTTPS site origin is required.");
        return new Uri(uri, "/api/quota");
    }

    public WindowsQuotaExportSettings Load()
    {
        lock (sync)
        {
            if (current is not null) return current;
            if (!File.Exists(filePath))
                return current = new(1, false, "", Guid.NewGuid().ToString("D"), 0);
            using var stream = File.OpenRead(filePath);
            if (stream.Length > 65536) throw new InvalidDataException("Invalid export settings.");
            var value = JsonSerializer.Deserialize(stream, WindowsQuotaExportJson.Default.WindowsQuotaExportSettings);
            if (value is null || value.Version != 1 ||
                !Guid.TryParseExact(value.SourceId, "D", out _) || value.Seq < 0 || value.Seq > MaximumSequence ||
                value.SiteUrl is null || (value.Enabled && value.SiteUrl.Length == 0))
                throw new InvalidDataException("Invalid export settings.");
            if (value.SiteUrl.Length > 0) _ = Endpoint(value.SiteUrl);
            return current = value;
        }
    }

    public WindowsQuotaExportSettings Update(bool enabled, string siteUrl)
    {
        lock (sync)
        {
            string normalized = siteUrl.Trim();
            if (normalized.Length > 0) normalized = Endpoint(normalized).GetLeftPart(UriPartial.Authority);
            else if (enabled) throw new ArgumentException("A site address is required.");
            var next = Load() with { Enabled = enabled, SiteUrl = normalized };
            Write(next);
            return current = next;
        }
    }

    public WindowsQuotaExportSettings ReserveSequence()
    {
        lock (sync)
        {
            var old = Load();
            if (old.Seq == MaximumSequence) throw new InvalidDataException("Export sequence limit reached.");
            var next = old with { Seq = old.Seq + 1 };
            Write(next);
            return current = next;
        }
    }

    private void Write(WindowsQuotaExportSettings value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string temporary = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, WindowsQuotaExportJson.Default.WindowsQuotaExportSettings);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, filePath, overwrite: true); // Same-directory atomic replacement.
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
