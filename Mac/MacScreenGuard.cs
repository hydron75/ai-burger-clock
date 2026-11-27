using AppKit;

namespace AiBurgerClock;

// Native smoke only. A smoke run opens the popover and the statistics window; on a display where another app is
// full screen (RDP, a video) that makes the menu bar appear over it, and it was once left behind there until
// log out (MACOS_PORT 39). Before those checks the run looks at the windows on screen (MacScreenCoverage), opens its
// windows on a display with no full-screen app (the status item's) and, if that display is covered, skips the checks
// that open windows and says so.
internal static class MacScreenGuard
{
    internal const string EnvironmentVariable = "AIBURGERCLOCK_SMOKE_SCREEN";

    // Which display the window checks use and why (one line for the smoke output), or why they are skipped.
    // NeedsAnchor: the status item's own display is covered, so the popover is anchored to a smoke-only window on Screen.
    internal readonly record struct Choice(NSScreen? Screen, bool NeedsAnchor, string Description,
        MacScreenCoverage.Covering? Skipped);

    // The popover opens under the status item, whose display macOS decides (not always the menu-bar display). A display
    // where another app is full screen (RDP, a video) is left alone: opening a window there shows the menu bar over that app.
    // The status item's display is used when it is free; otherwise the menu-bar display, then the others, with a smoke-only
    // anchor window for the popover. The statistics window goes to the same display. Only when every display is covered
    // are the window checks skipped. AIBURGERCLOCK_SMOKE_SCREEN=skip forces the skip path (for testing it without a
    // full-screen app); anything else, or nothing, looks at the real screens.
    internal static Choice Choose(NSScreen? statusItemScreen)
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) == "skip")
            return new(null, false, "window checks skipped (forced by " + EnvironmentVariable + ")", new("(forced by " + EnvironmentVariable + ")", 0));
        NSScreen[] screens = NSScreen.Screens;
        if (screens.Length == 0) return new(null, false, "no display information, window checks run on the system's choice", null);
        int own = statusItemScreen is null ? -1 : Array.FindIndex(screens, item => item.Frame.Equals(statusItemScreen.Frame));
        List<MacScreenCoverage.Covering> covered = MacScreenCoverage.Coverings(MacScreenCoverage.DisplayRects(screens));
        bool IsCovered(int index) => covered.Any(item => item.ScreenNumber == index + 1);
        // The status item's display first (the popover opens there by itself), then the menu-bar display, then the rest.
        int[] order = (own >= 0 ? new[] { own } : []).Concat(Enumerable.Range(0, screens.Length)).Distinct().ToArray();
        int chosen = order.FirstOrDefault(index => !IsCovered(index), -1);
        string notes = string.Join("; ", covered.Select(item => $"display {item.ScreenNumber} is covered by {item.Owner} and is left alone"));
        if (chosen < 0)
        {
            MacScreenCoverage.Covering first = covered.FirstOrDefault(item => item.ScreenNumber == (own >= 0 ? own : 0) + 1, covered[0]);
            return new(null, false, $"every display is covered ({notes})", first);
        }
        NSScreen screen = screens[chosen];
        bool anchor = chosen != own;
        string size = $"display {chosen + 1} of {screens.Length} ({screen.Frame.Width:0}x{screen.Frame.Height:0}pt" + (chosen == 0 ? ", menu-bar display" : "") + ")";
        string why = !anchor ? $"{size}: the status item's display, no full-screen app on it; popover and statistics window open there"
            : own >= 0 ? $"{size}: the status item's display {own + 1} is covered, so the popover is anchored to a smoke-only window here; statistics window here too"
            : $"{size}: the status item's display is unknown, so the popover is anchored to a smoke-only window here; statistics window here too";
        return new(screen, anchor, why + (notes.Length > 0 ? "; " + notes : ""), null);
    }
}
