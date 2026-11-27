using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using System.Runtime.InteropServices;

namespace AiBurgerClock;

// Which displays another app covers whole (a full-screen app), judged from window names and sizes only (no screen
// capture), and whether the screen can be used at all. A window opened over a full-screen app shows the menu bar over it
// (MACOS_PORT 39), so the smoke (MacScreenGuard) and the Antigravity usage read leave those displays alone. Rectangles
// are in CoreGraphics coordinates (top-left origin of the primary display).
internal static class MacScreenCoverage
{
    private const string CoreGraphicsPath = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    // CGWindowListCopyWindowInfo and the display functions have no binding in the macOS SDK package.
    // kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements.
    [DllImport(CoreGraphicsPath)]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphicsPath)]
    private static extern int CGGetOnlineDisplayList(uint maximum, [Out] uint[] displays, out uint count);

    [DllImport(CoreGraphicsPath)]
    private static extern Rect CGDisplayBounds(uint display);

    [DllImport(CoreGraphicsPath)]
    private static extern int CGDisplayIsAsleep(uint display);

    [DllImport(CoreGraphicsPath)]
    private static extern IntPtr CGSessionCopyCurrentDictionary();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr handle);

    private const uint OnScreenOnly = 1, ExcludeDesktopElements = 16;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rect
    {
        internal readonly double X, Y, Width, Height;
    }

    internal readonly record struct Covering(string Owner, int ScreenNumber);

    internal readonly record struct Display(bool Asleep, bool Covered)
    {
        internal bool Usable => !Asleep && !Covered;
    }

    // Product readiness for a read that has to put a window on screen: not while the screen is locked, and not while every
    // display is asleep or covered. What cannot be determined (no display list) does not block the read.
    internal static QuotaReadiness Decide(bool locked, IReadOnlyList<Display>? displays) =>
        locked || displays is { Count: > 0 } && !displays.Any(display => display.Usable)
            ? QuotaReadiness.Deferred : QuotaReadiness.Ready;

    // CoreGraphics only, so the monitor may ask from its own thread (never waits for the main thread).
    internal static QuotaReadiness Readiness()
    {
        try { return Decide(ScreenLocked(), Displays()); }
        catch (Exception) { return QuotaReadiness.Ready; }
    }

    // Main thread. The first display a read window may use (the menu-bar display first), or null when none is awake and
    // uncovered.
    internal static NSScreen? FreeScreen()
    {
        NSScreen[] screens = NSScreen.Screens;
        if (screens.Length == 0) return null;
        List<Covering> covered = Coverings(DisplayRects(screens));
        for (int index = 0; index < screens.Length; index++)
        {
            bool asleep = screens[index].DeviceDescription["NSScreenNumber"] is NSNumber number &&
                CGDisplayIsAsleep(number.UInt32Value) != 0;
            if (new Display(asleep, covered.Any(item => item.ScreenNumber == index + 1)).Usable) return screens[index];
        }
        return null;
    }

    // Window bounds use a top-left origin on the primary display; NSScreen frames use a bottom-left one.
    internal static CGRect[] DisplayRects(NSScreen[] screens)
    {
        double primaryHeight = screens[0].Frame.Height;
        return screens.Select(screen => new CGRect(screen.Frame.X, primaryHeight - screen.Frame.Y - screen.Frame.Height,
            screen.Frame.Width, screen.Frame.Height)).ToArray();
    }

    // Every display that a window of another app covers whole (the first such window's app per display; ScreenNumber
    // counts from 1 in the order of the given rectangles).
    internal static List<Covering> Coverings(IReadOnlyList<CGRect> displays)
    {
        var result = new List<Covering>();
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
            for (int index = 0; index < displays.Count; index++)
            {
                CGRect display = displays[index];
                if (Math.Abs(x - display.X) <= 1.5 && Math.Abs(y - display.Y) <= 1.5 &&
                    width >= display.Width - 1.5 && height >= display.Height - 1.5 && result.All(item => item.ScreenNumber != index + 1))
                    result.Add(new(window["kCGWindowOwnerName"]?.ToString() ?? "(unknown)", index + 1));
            }
        }
        return result;
    }

    // The current login session says whether the screen is locked. No session information counts as not locked.
    private static bool ScreenLocked()
    {
        IntPtr handle = CGSessionCopyCurrentDictionary();
        if (handle == IntPtr.Zero) return false;
        using NSDictionary? session = Runtime.GetNSObject<NSDictionary>(handle, owns: true);
        return session?["CGSSessionScreenIsLocked"] is NSNumber locked && locked.BoolValue;
    }

    internal static CGRect Bounds(uint display)
    {
        Rect bounds = CGDisplayBounds(display);
        return new CGRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static IReadOnlyList<Display>? Displays()
    {
        var ids = new uint[32];
        if (CGGetOnlineDisplayList((uint)ids.Length, ids, out uint count) != 0 || count == 0) return null;
        CGRect[] rects = ids.Take((int)count).Select(Bounds).ToArray();
        List<Covering> covered = Coverings(rects);
        return rects.Select((_, index) => new Display(CGDisplayIsAsleep(ids[index]) != 0,
            covered.Any(item => item.ScreenNumber == index + 1))).ToArray();
    }

    private static double Number(NSDictionary dictionary, string key) =>
        dictionary[key] is NSNumber number ? number.DoubleValue : double.NaN;
}
