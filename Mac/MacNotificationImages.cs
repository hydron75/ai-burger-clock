using AppKit;
using CoreGraphics;
using CoreText;
using Foundation;
using UserNotifications;

namespace AiBurgerClock;

// What a notification is about. Only the look of the banner depends on it: the text, the conditions and the spacing
// of the notifications come from the shared code and the hosts and are not touched here.
internal enum MacNotificationKind { ScheduleFull, ScheduleBurger, ProviderRecovered, ProviderIssue, Holiday, Quota }

// The Windows balloon levels (TrayApplicationContext): Info for FULL THROTTLE, recovery/normalization, the holiday
// setting and quota alerts; Warning for the other transitions and the other recommendation changes.
internal enum MacNotificationLevel { Info, Warning }

internal readonly record struct MacNotificationStyle(MacNotificationLevel Level, string Thread, bool StatusIcon);

internal static class MacNotificationLook
{
    // Notification Center groups by this: schedule/service notices apart from quota notices.
    internal const string StatusThread = "aiburgerclock.status";
    internal const string QuotaThread = "aiburgerclock.quota";

    internal static MacNotificationStyle Style(MacNotificationKind kind) => kind switch
    {
        MacNotificationKind.ScheduleFull or MacNotificationKind.ProviderRecovered or MacNotificationKind.Holiday =>
            new(MacNotificationLevel.Info, StatusThread, true),
        MacNotificationKind.ScheduleBurger or MacNotificationKind.ProviderIssue =>
            new(MacNotificationLevel.Warning, StatusThread, true),
        MacNotificationKind.Quota => new(MacNotificationLevel.Info, QuotaThread, false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static MacNotificationKind ForSchedule(AgentState state) =>
        state == AgentState.FullThrottle ? MacNotificationKind.ScheduleFull : MacNotificationKind.ScheduleBurger;

    internal static MacNotificationKind ForProviderAlert(ProviderAlert alert) =>
        alert.Recovered ? MacNotificationKind.ProviderRecovered : MacNotificationKind.ProviderIssue;
}

// The picture attached to a notification (shown at the banner's right edge; the app icon on the left cannot change).
// Schedule/service notices carry the menu-bar drawing of that moment, drawn by the same code (MacStatusIcon.Draw)
// scaled up, so the two can only differ if the TrayAppearance given here differs. Quota notices carry a blue
// arrow disc that is not one of the status colors. Both get a small level badge in the lower-right corner.
internal static class MacNotificationImage
{
    // Six pixels per point of the 20-point menu-bar grid, the scale the menu-bar icon is checked at, so both are snapped the same way.
    internal const int Pixels = 120;
    private static readonly NSColor InfoBadge = NSColor.FromSrgb(0f, 122 / 255f, 1f, 1f);
    private static readonly NSColor WarningBadge = NSColor.FromSrgb(1f, 204 / 255f, 0f, 1f);
    private static readonly NSColor WarningMark = NSColor.FromSrgb(40 / 255f, 30 / 255f, 0f, 1f);
    private static readonly NSColor QuotaLight = NSColor.FromSrgb(0f, 110 / 255f, 214 / 255f, 1f);
    private static readonly NSColor QuotaDark = NSColor.FromSrgb(100 / 255f, 210 / 255f, 1f, 1f);
    // Badge box on the 20-point grid: it sits just outside the disc, in the transparent corner.
    private static readonly CGRect BadgeBox = new(12.2, 0.6, 7.2, 7.2);

    internal static NSColor QuotaAccent(bool dark) => dark ? QuotaDark : QuotaLight;
    internal static NSColor LevelColor(MacNotificationLevel level) => level == MacNotificationLevel.Info ? InfoBadge : WarningBadge;

    // PNG bytes, or null when this kind needs a status appearance that is not known yet (before the first tick).
    internal static NSData? Png(MacNotificationKind kind, TrayAppearance? appearance, bool dark)
    {
        MacNotificationStyle style = MacNotificationLook.Style(kind);
        if (style.StatusIcon && appearance is null) return null;
        // Drawn into the same kind of bitmap context the menu-bar icon is checked with (so text is smoothed the same
        // way), then converted to sRGB: the drawn colors come back as the same sRGB numbers, and the PNG is tagged, because
        // an untagged one would be read with another gamma.
        using var bitmap = new NSBitmapImageRep(IntPtr.Zero, Pixels, Pixels, 8, 4, true, false,
            "NSDeviceRGBColorSpace", 0, 0);
        NSGraphicsContext? graphics = NSGraphicsContext.FromBitmap(bitmap);
        if (graphics is null) return null;
        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            NSGraphicsContext.CurrentContext = graphics;
            CGContext context = graphics.CGContext;
            context.ClearRect(new CGRect(0, 0, Pixels, Pixels));
            context.ScaleCTM((nfloat)Pixels / MacStatusIcon.Size, (nfloat)Pixels / MacStatusIcon.Size);
            if (style.StatusIcon) MacStatusIcon.Draw(appearance!.Value, dark);
            else MacStatusIcon.DrawDisc(QuotaAccent(dark), "\u21BB", dark);
            DrawBadge(context, style.Level);
        }
        finally { NSGraphicsContext.GlobalRestoreGraphicsState(); }
        using NSBitmapImageRep? converted = bitmap.ConvertingToColorSpace(NSColorSpace.SRGBColorSpace, NSColorRenderingIntent.Default);
        return (converted ?? bitmap).RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
    }

    private static void DrawBadge(CGContext context, MacNotificationLevel level)
    {
        nfloat left = BadgeBox.X, bottom = BadgeBox.Y, right = BadgeBox.X + BadgeBox.Width, top = BadgeBox.Y + BadgeBox.Height;
        CGPoint middle = new((left + right) / 2, (bottom + top) / 2);
        LevelColor(level).SetFill();
        if (level == MacNotificationLevel.Info)
        {
            NSBezierPath.FromOvalInRect(BadgeBox).Fill();
            DrawLetter(context, "i", 6.4f, NSColor.White, middle);
            return;
        }
        var triangle = new NSBezierPath();
        triangle.MoveTo(new CGPoint(middle.X, top));
        triangle.LineTo(new CGPoint(right, bottom));
        triangle.LineTo(new CGPoint(left, bottom));
        triangle.ClosePath();
        triangle.Fill();
        DrawLetter(context, "!", 5.6f, WarningMark, new CGPoint(middle.X, middle.Y - 0.6f));
    }

    // The text position is reset first: GetImageBounds is relative to it, and the disc was drawn before.
    private static void DrawLetter(CGContext context, string letter, float size, NSColor color, CGPoint center)
    {
        context.SaveState();
        try
        {
            using var font = new CTFont("Helvetica-Bold", size);
            using var text = new NSAttributedString(letter,
                new CTStringAttributes { Font = font, ForegroundColor = color.CGColor });
            using var line = new CTLine(text);
            context.TextMatrix = CGAffineTransform.MakeIdentity();
            context.TextPosition = CGPoint.Empty;
            CGRect bounds = line.GetImageBounds(context);
            context.TextPosition = new CGPoint(center.X - bounds.Width / 2 - bounds.X, center.Y - bounds.Height / 2 - bounds.Y);
            line.Draw(context);
        }
        finally { context.RestoreState(); }
    }
}

// Temporary picture files for notifications: one file per notification in the app's cache folder. The system takes
// its own copy when it accepts the request, so ours is deleted right after; leftovers (a crash, a rejected request)
// are swept at the next start, and the folder never keeps more than MaximumFiles.
internal sealed class MacNotificationImageStore
{
    internal const int MaximumFiles = 20;
    private readonly string directory;

    internal MacNotificationImageStore(string directory) => this.directory = directory;

    internal string Directory => directory;

    internal static MacNotificationImageStore Default()
    {
        string caches = NSSearchPath.GetDirectories(NSSearchPathDirectory.CachesDirectory, NSSearchPathDomain.User, true)
            .FirstOrDefault() ?? Path.GetTempPath();
        string bundle = NSBundle.MainBundle.BundleIdentifier ?? "com.hydron75.aiburgerclock";
        return new(Path.Combine(caches, bundle, "notification-images"));
    }

    // Null when the file could not be written; the caller then sends the notification without a picture.
    internal string? Write(NSData png, long serial, MacNotificationKind kind)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            TrimToLimit();
            string path = Path.Combine(directory, $"{serial}-{kind}.png".ToLowerInvariant());
            File.WriteAllBytes(path, png.ToArray());
            return path;
        }
        catch (Exception error) when (IsFileFailure(error)) { return null; }
    }

    internal void Delete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch (Exception error) when (IsFileFailure(error)) { }
    }

    // At start nothing is in flight, so every file here is a leftover.
    internal void Sweep()
    {
        try
        {
            if (!System.IO.Directory.Exists(directory)) return;
            foreach (string file in System.IO.Directory.GetFiles(directory, "*.png")) Delete(file);
        }
        catch (Exception error) when (IsFileFailure(error)) { }
    }

    internal string[] Files()
    {
        try { return System.IO.Directory.Exists(directory) ? System.IO.Directory.GetFiles(directory) : []; }
        catch (Exception error) when (IsFileFailure(error)) { return []; }
    }

    private void TrimToLimit()
    {
        string[] files = System.IO.Directory.GetFiles(directory, "*.png");
        if (files.Length < MaximumFiles) return;
        foreach (string old in files.OrderBy(File.GetLastWriteTimeUtc).Take(files.Length - MaximumFiles + 1)) Delete(old);
    }

    private static bool IsFileFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}

// Builds and submits one notification request. The picture is decoration: if it cannot be made or attached, or the
// system refuses a request that carries it, the same notification goes out once more without it. Only the plain
// notification failing is reported. The center, the main thread and the appearance are injected for the smoke run.
internal sealed class MacNotificationSender
{
    private readonly MacNotificationImageStore store;
    private readonly Action<UNNotificationRequest, Action<NSError?>> add;
    private readonly Action<Action> onMain;
    private readonly Func<bool> isDark;
    private readonly Action failed;
    private long serial;

    internal MacNotificationSender(MacNotificationImageStore store, Action<UNNotificationRequest, Action<NSError?>> add,
        Action<Action> onMain, Func<bool> isDark, Action failed)
    {
        this.store = store;
        this.add = add;
        this.onMain = onMain;
        this.isDark = isDark;
        this.failed = failed;
    }

    internal void Send(string title, string body, MacNotificationKind kind, TrayAppearance? appearance) =>
        Submit(++serial, title, body, kind, appearance, withImage: true);

    private void Submit(long id, string title, string body, MacNotificationKind kind, TrayAppearance? appearance, bool withImage)
    {
        using UNMutableNotificationContent content = Build(title, body, kind, appearance, withImage, id, out string? file);
        bool attached = file is not null;
        using var request = UNNotificationRequest.FromIdentifier("aiburgerclock-" + id, content, null);
        add(request, error =>
        {
            store.Delete(file);
            if (error is null) return;
            if (attached) onMain(() => Submit(id, title, body, kind, appearance, withImage: false));
            else onMain(failed);
        });
    }

    // file is the temporary picture (null when the content has no attachment).
    internal UNMutableNotificationContent Build(string title, string body, MacNotificationKind kind, TrayAppearance? appearance,
        bool withImage, long id, out string? file)
    {
        var content = new UNMutableNotificationContent
        {
            Title = title, Body = body, ThreadIdentifier = MacNotificationLook.Style(kind).Thread
        };
        file = null;
        if (!withImage) return content;
        try
        {
            using NSData? png = MacNotificationImage.Png(kind, appearance, isDark());
            if (png is null) return content;
            file = store.Write(png, id, kind);
            if (file is null) return content;
            UNNotificationAttachment? attachment = UNNotificationAttachment.FromIdentifier(
                "status-" + id, NSUrl.FromFilename(file), (NSDictionary?)null, out NSError? error);
            if (attachment is null || error is not null)
            {
                store.Delete(file);
                file = null;
                return content;
            }
            content.Attachments = [attachment];
        }
        catch (Exception)
        {
            // Drawing is best effort: whatever fails here must never cost the notification itself.
            store.Delete(file);
            file = null;
        }
        return content;
    }
}
