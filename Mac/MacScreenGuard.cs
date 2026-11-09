using AppKit;
using CoreGraphics;
using Foundation;
using System.Runtime.InteropServices;

namespace AiBurgerClock;

// Native smoke only. A smoke run opens the popover and the statistics window; on a display where another app is
// full screen (RDP, a video) that makes the menu bar appear over it, and it was once left behind there until
// log out (MACOS_PORT 39). Before those checks the run looks at the windows on screen (names and sizes only, no
// screen capture), opens its windows on a display with no full-screen app (the status item's) and, if that display is
// covered, skips the checks that open windows and says so.
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

    // Which display the window checks use and why (one line for the smoke output), or why they are skipped.
    internal readonly record struct Choice(NSScreen? Screen, string Description, Covering? Skipped);

    // The popover opens under the status item, so its display is the status item's (the menu-bar display unless macOS put
    // the item elsewhere); the statistics window is placed on the same one. Another display having a full-screen app
    // (RDP, a video) does not matter: nothing of ours opens there and the menu bar stays hidden on it. Only when the
    // status item's own display is covered are the window checks skipped. AIBURGERCLOCK_SMOKE_SCREEN=skip forces the
    // skip path (for testing it without a full-screen app); anything else, or nothing, looks at the real screens.
    internal static Choice Choose(NSScreen? statusItemScreen)
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) == "skip")
            return new(null, "window checks skipped (forced by " + EnvironmentVariable + ")", new("(forced by " + EnvironmentVariable + ")", 0));
        NSScreen[] screens = NSScreen.Screens;
        if (screens.Length == 0) return new(null, "no display information, window checks run on the system's choice", null);
        int chosen = statusItemScreen is null ? -1 : Array.FindIndex(screens, item => item.Frame.Equals(statusItemScreen.Frame));
        bool known = chosen >= 0;
        if (!known) chosen = 0;
        List<Covering> covered = Coverings(screens);
        string others = string.Join("; ", covered.Where(item => item.ScreenNumber != chosen + 1)
            .Select(item => $"display {item.ScreenNumber} is covered by {item.Owner} and is left alone"));
        if (covered.Where(item => item.ScreenNumber == chosen + 1).Select(item => (Covering?)item).FirstOrDefault() is { } own)
            return new(null, $"display {chosen + 1} of {screens.Length} is the status item's display and is covered by {own.Owner}", own);
        string why = known ? "the status item's display, the popover opens there" : "the menu-bar display (status item's display unknown)";
        return new(screens[chosen], $"display {chosen + 1} of {screens.Length} ({screens[chosen].Frame.Width:0}x{screens[chosen].Frame.Height:0}pt): {why}; " +
            $"no full-screen app on it, popover and statistics window open there" + (others.Length > 0 ? "; " + others : ""), null);
    }

    // Every display that a window of another app covers whole (the first such window's app per display).
    private static List<Covering> Coverings(NSScreen[] screens)
    {
        var result = new List<Covering>();
        // Window bounds use a top-left origin on the primary display; NSScreen frames use a bottom-left one.
        double primaryHeight = screens[0].Frame.Height;
        int ownPid = Environment.ProcessId;
        IntPtr list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        if (list == IntPtr.Zero) return result;
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
                    width >= frame.Width - 1.5 && height >= frame.Height - 1.5 && result.All(item => item.ScreenNumber != index + 1))
                    result.Add(new(window["kCGWindowOwnerName"]?.ToString() ?? "(unknown)", index + 1));
            }
        }
        return result;
    }

    private static double Number(NSDictionary dictionary, string key) =>
        dictionary[key] is NSNumber number ? number.DoubleValue : double.NaN;
}
