using System.Drawing.Imaging;

namespace AiBurgerClock;

// Only the two fixed information/warning assets are persisted, so a toast can retain
// its image after the process exits. Quota notifications do not request an image.
internal sealed class WindowsNotificationImages(string? directory = null)
{
    private readonly string directory = directory ?? Path.Combine(AppPaths.DataDirectory, "notification-images");

    internal string? Get(ToolTipIcon icon)
    {
        if (icon is not (ToolTipIcon.Info or ToolTipIcon.Warning)) return null;
        try
        {
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return null;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, icon == ToolTipIcon.Info ? "info-v1.png" : "warning-v1.png");
            if (File.Exists(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 64 * 1024) return null;
                using var existing = Image.FromFile(path);
                return existing.Size == new Size(96, 96) ? new Uri(path).AbsoluteUri : null;
            }
            using var bitmap = new Bitmap(96, 96, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawIcon(icon == ToolTipIcon.Info ? SystemIcons.Information : SystemIcons.Warning, new Rectangle(0, 0, 96, 96));
            }
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) bitmap.Save(stream, ImageFormat.Png);
            return new Uri(path).AbsoluteUri;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.ExternalException or System.Security.SecurityException)
        {
            // Optional image failure must not suppress the original message or cause a balloon fallback.
            return null;
        }
    }
}
