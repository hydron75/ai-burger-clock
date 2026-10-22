using AppKit;
using CoreGraphics;
using Foundation;
using System.Runtime.InteropServices;

namespace AiBurgerClock;

// Native smoke only. A smoke run opens the popover and the statistics window; on a display where another app is
// full screen (RDP, a video) that makes the menu bar appear over it, and it was once left behind there until
// log out (MACOS_PORT 39). Before those checks the run looks at the windows on screen (names and sizes only, no
// screen capture) and, if one covers a whole display, skips the checks that open windows and says so.
internal static class MacScreenGuard
{
    internal const string EnvironmentVariable = "AIBURGERCLOCK_SMOKE_SCREEN";

    // CGWindowListCopyWindowInfo has no binding in the macOS SDK package. kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements.
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr handle);

    private const uint OnScreenOnly = 1, ExcludeDesktopElements = 16;

    internal readonly record struct Covering(string Owner, int ScreenNumber);

    // AIBURGERCLOCK_SMOKE_SCREEN=skip forces the skip path (for testing it without a full-screen app); anything
    // else, or nothing, looks at the real screens.
    internal static Covering? Find()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) == "skip") return new("(forced by " + EnvironmentVariable + ")", 0);
        NSScreen[] screens = NSScreen.Screens;
        if (screens.Length == 0) return null;
        // Window bounds use a top-left origin on the primary display; NSScreen frames use a bottom-left one.
        double primaryHeight = screens[0].Frame.Height;
        int ownPid = Environment.ProcessId;
        IntPtr list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        if (list == IntPtr.Zero) return null;
        NSDictionary?[] windows;
        try { windows = NSArray.ArrayFromHandle<NSDictionary>(list) ?? []; }
        finally { CFRelease(list); }
        foreach (NSDictionary? window in windows)
        {
            // Layer 0 is the ordinary application layer (a full-screen app); the menu bar, Dock, wallpaper and
            // overlays use other layers.
            if (window is null || Number(window, "kCGWindowLayer") != 0 || Number(window, "kCGWindowOwnerPID") == ownPid) continue;
            if (window["kCGWindowBounds"] is not NSDictionary bounds) continue;
            double x = Number(bounds, "X"), y = Number(bounds, "Y"), width = Number(bounds, "Width"), height = Number(bounds, "Height");
            for (int index = 0; index < screens.Length; index++)
            {
                CGRect frame = screens[index].Frame;
                double top = primaryHeight - frame.Y - frame.Height;
                if (Math.Abs(x - frame.X) <= 1.5 && Math.Abs(y - top) <= 1.5 &&
                    width >= frame.Width - 1.5 && height >= frame.Height - 1.5)
                    return new(window["kCGWindowOwnerName"]?.ToString() ?? "(unknown)", index + 1);
            }
        }
        return null;
    }

    private static double Number(NSDictionary dictionary, string key) =>
        dictionary[key] is NSNumber number ? number.DoubleValue : double.NaN;
}
