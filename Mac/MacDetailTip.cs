using AppKit;
using CoreGraphics;
using Foundation;

namespace AiBurgerClock;

// The shared Provider detail (status card, quota box) as a tip of one fixed width. The system tooltip (NSView.ToolTip)
// takes its width from its longest line and gives no control over it, so a long URL or target line made one Provider's
// tip much wider than the others. Here the outer width is always Width; only the height follows the text. The lines
// are broken here, the way the Windows detail popup does it: at spaces only, so a Korean word or a path such as
// 'Apps/연결' moves to the next line whole (text layout alone also breaks at '·' and '/'); only a word longer than the
// line, such as a URL, breaks inside itself, on text-element boundaries, so nothing is cut off and no emoji or combining
// mark is torn apart. A detail taller than the screen's usable height (many Gemini windows, long errors) is capped to that
// height and scrolls inside the tip, like the Windows popup: only then does the tip take mouse input (wheel, scroller) and stay
// while the cursor is on it. Other tooltips (buttons, schedule, options) stay system tooltips.
internal static class MacDetailTip
{
    internal const double Width = 320;
    internal const double Padding = 8;
    private const double TextWidth = Width - 2 * Padding;
    // Rest on the item this long before the tip appears; it also disappears after Lifetime, like the Windows popup.
    private const double ShowDelay = 0.6;
    private const double Lifetime = 25;
    private const double ScreenMargin = 4;

    // What the layout of one text came to, for the smoke: the width the text may use, how wide its widest line is, and
    // whether every character was placed on some line.
    // Lines is what the text layout drew, WrappedLines what this file broke; they are equal when no line overflowed.
    internal readonly record struct Metrics(CGSize Size, int Lines, int WrappedLines, double WidestLine, int PlacedCharacters, int Characters);

    private static NSAttributedString Styled(string text)
    {
        var style = new NSMutableParagraphStyle { LineBreakMode = NSLineBreakMode.ByWordWrapping, LineBreakStrategy = NSLineBreakStrategy.HangulWordPriority };
        return new NSAttributedString(text, new NSStringAttributes { Font = NSFont.ToolTipsFontOfSize(0), ForegroundColor = NSColor.Label, ParagraphStyle = style });
    }

    // Width of a string on one line (no wrapping), measured by the same text layout that draws it.
    private sealed class Ruler
    {
        private readonly NSTextStorage storage = new(Styled(""));
        private readonly NSLayoutManager manager = new();
        private readonly NSTextContainer container = new(new CGSize(100000, nfloat.MaxValue)) { LineFragmentPadding = 0 };

        internal Ruler()
        {
            storage.AddLayoutManager(manager);
            manager.AddTextContainer(container);
        }

        internal double Width(string text)
        {
            storage.SetString(Styled(text));
            manager.EnsureLayoutForTextContainer(container);
            return (double)manager.GetUsedRect(container).Width;
        }
    }

    // Break the text into lines no wider than the text area: at whitespace only; a word wider than a line is split by
    // text element. Logical lines (and empty ones) of the shared text are kept.
    private static List<string> Wrap(string text)
    {
        var ruler = new Ruler();
        var result = new List<string>();
        foreach (string logical in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = "", space = "";
            foreach (System.Text.RegularExpressions.Match token in System.Text.RegularExpressions.Regex.Matches(logical, @"\s+|\S+"))
            {
                string word = token.Value;
                if (string.IsNullOrWhiteSpace(word)) { space += word; continue; }
                if (ruler.Width(line + space + word) <= TextWidth) { line += space + word; space = ""; continue; }
                if (line.Length > 0) { result.Add(line); line = ""; }
                space = "";
                if (ruler.Width(word) <= TextWidth) { line = word; continue; }
                var elements = System.Globalization.StringInfo.GetTextElementEnumerator(word);
                while (elements.MoveNext())
                {
                    string element = elements.GetTextElement();
                    if (line.Length > 0 && ruler.Width(line + element) > TextWidth) { result.Add(line); line = ""; }
                    line += element;
                }
            }
            result.Add(line);
        }
        return result;
    }

    private sealed class TextLayout
    {
        internal readonly List<string> Lines;
        internal readonly string Original;
        internal readonly NSTextStorage Storage;
        internal readonly NSLayoutManager Manager = new();
        internal readonly NSTextContainer Container = new(new CGSize(TextWidth, nfloat.MaxValue)) { LineFragmentPadding = 0 };

        internal TextLayout(string text)
        {
            Original = text;
            Lines = Wrap(text);
            Storage = new NSTextStorage(Styled(string.Join('\n', Lines)));
            Storage.AddLayoutManager(Manager);
            Manager.AddTextContainer(Container);
            Manager.EnsureLayoutForTextContainer(Container);
        }

        internal Metrics Measure()
        {
            CGRect used = Manager.GetUsedRect(Container);
            string laid = Storage.Value ?? "";
            int fragments = 0, placed = 0;
            double widest = 0;
            Manager.EnumerateLineFragments(Manager.GetGlyphRange(Container), (CGRect rect, CGRect usedRect, NSTextContainer container, NSRange glyphRange, out bool stop) =>
            {
                stop = false;
                fragments++;
                widest = Math.Max(widest, (double)usedRect.Width);
                NSRange characters = Manager.GetCharacterRange(glyphRange, out _);
                placed += laid.Substring((int)characters.Location, (int)characters.Length).Count(c => !char.IsWhiteSpace(c));
            });
            return new(new CGSize(Width, Math.Ceiling((double)used.Height) + 2 * Padding), fragments, Lines.Count, widest,
                placed, Original.Count(c => !char.IsWhiteSpace(c)));
        }
    }

    internal static Metrics Measure(string text) => new TextLayout(text).Measure();

    // Smoke: the first word that fits a line on its own but is not whole on one line (null when none). Only a word
    // wider than the line, such as a long URL, may be broken inside itself.
    internal static string? SplitShortWord(string text)
    {
        var layout = new TextLayout(text);
        var ruler = new Ruler();
        foreach (System.Text.RegularExpressions.Match token in System.Text.RegularExpressions.Regex.Matches(text, @"\S+"))
            if (ruler.Width(token.Value) <= TextWidth && !layout.Lines.Any(line => line.Contains(token.Value, StringComparison.Ordinal)))
                return token.Value;
        return null;
    }

    // The text of a tip taller than the screen, drawn inside the scroll view of its TipView.
    private sealed class TextBody : NSView
    {
        private readonly TextLayout layout;
        internal TextBody(TextLayout layout, CGRect frame) : base(frame) => this.layout = layout;
        public override bool IsFlipped => true;
        public override void DrawRect(CGRect dirtyRect) =>
            layout.Manager.DrawGlyphs(layout.Manager.GetGlyphRange(layout.Container), new CGPoint(Padding - ScrollInset, Padding));
    }

    private const double ScrollInset = 1;

    // The tip as it is drawn: panel colors, a thin outline, the text laid out above. When the text is taller than
    // maxHeight the view is maxHeight high and the text sits in a scroll view (overlay scroller: the text keeps its width).
    private sealed class TipView : NSView
    {
        private readonly TextLayout layout;
        private readonly NSScrollView? scroll;
        private NSTrackingArea? tracking;

        internal TipView(string text, double maxHeight = double.MaxValue)
        {
            layout = new TextLayout(text);
            CGSize natural = layout.Measure().Size;
            NaturalHeight = (double)natural.Height;
            if (NaturalHeight <= maxHeight)
            {
                Frame = new CGRect(CGPoint.Empty, natural);
                return;
            }
            Frame = new CGRect(0, 0, Width, Math.Max(1, maxHeight));
            scroll = new NSScrollView(new CGRect(ScrollInset, ScrollInset, Width - 2 * ScrollInset, Math.Max(1, maxHeight - 2 * ScrollInset)))
            {
                HasVerticalScroller = true,
                HasHorizontalScroller = false,
                AutohidesScrollers = false,
                ScrollerStyle = NSScrollerStyle.Overlay,
                DrawsBackground = false,
                BorderType = NSBorderType.NoBorder,
                VerticalScrollElasticity = NSScrollElasticity.None,
                DocumentView = new TextBody(layout, new CGRect(0, 0, Width - 2 * ScrollInset, NaturalHeight))
            };
            AddSubview(scroll);
        }

        // The height the whole text needs (the view is shorter when it scrolls).
        internal double NaturalHeight { get; }
        internal bool Overflows => scroll is not null;
        internal double ViewportHeight => scroll is null ? NaturalHeight : (double)scroll.ContentView.Bounds.Height;
        internal double ScrollOffset => scroll is null ? 0 : (double)scroll.ContentView.Bounds.Y;
        internal double MaxScrollOffset => Math.Max(0, NaturalHeight - ViewportHeight);

        // Scroll the text to a vertical offset (clamped); what the wheel or the scroller does.
        internal void ScrollTo(double offset)
        {
            if (scroll is null) return;
            scroll.LayoutSubtreeIfNeeded();
            scroll.ContentView.ScrollToPoint(new CGPoint(0, Math.Clamp(offset, 0, MaxScrollOffset)));
            scroll.ReflectScrolledClipView(scroll.ContentView);
        }

        internal void FlashScrollers() => scroll?.FlashScrollers();

        public override bool IsFlipped => true;

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();
            if (tracking is not null) { RemoveTrackingArea(tracking); tracking = null; }
            if (scroll is null) return;
            tracking = new NSTrackingArea(Bounds, NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveAlways |
                NSTrackingAreaOptions.InVisibleRect, this, null);
            AddTrackingArea(tracking);
        }

        public override void MouseEntered(NSEvent theEvent) => CursorOnTip();
        public override void MouseExited(NSEvent theEvent) => CursorLeftTip();

        public override void DrawRect(CGRect dirtyRect)
        {
            NSBezierPath path = NSBezierPath.FromRoundedRect(Bounds.Inset(0.5f, 0.5f), 6, 6);
            MacControls.PanelBackground.SetFill();
            path.Fill();
            NSColor.Separator.SetStroke();
            path.LineWidth = 1;
            path.Stroke();
            if (scroll is null) layout.Manager.DrawGlyphs(layout.Manager.GetGlyphRange(layout.Container), new CGPoint(Padding, Padding));
        }
    }

    // Smoke: lay a text out for a screen that allows maxHeight, scroll it to its end and back, and say what happened.
    internal readonly record struct OverflowProbe(double Natural, double FrameHeight, bool Overflows, double MaxOffset, double TopOffset, double EndOffset, double Viewport);

    internal static OverflowProbe Probe(string text, double maxHeight)
    {
        var view = new TipView(text, maxHeight);
        view.ScrollTo(0);
        double top = view.ScrollOffset;
        view.ScrollTo(double.MaxValue);
        return new(view.NaturalHeight, (double)view.Frame.Height, view.Overflows, view.MaxScrollOffset, top, view.ScrollOffset, view.ViewportHeight);
    }

    // Smoke / review pictures: the tip drawn off screen under an explicit appearance.
    internal static NSData? SnapshotPng(string text, bool dark)
    {
        var view = new TipView(text)
        {
            Appearance = NSAppearance.GetAppearance(dark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua)
        };
        NSBitmapImageRep? bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds);
        if (bitmap is null) return null;
        view.CacheDisplay(view.Bounds, bitmap);
        return bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
    }

    // ---- Showing it for a hovered view ----

    private sealed class TipPanel : NSPanel
    {
        internal TipPanel(TipView view) : base(view.Frame, NSWindowStyle.Borderless | NSWindowStyle.NonactivatingPanel, NSBackingStore.Buffered, false)
        {
            IsOpaque = false;
            BackgroundColor = NSColor.Clear;
            HasShadow = true;
            // Like a native tooltip, a tip that fits does not take the mouse from the card below it; only a scrolling one does.
            IgnoresMouseEvents = !view.Overflows;
            HidesOnDeactivate = false;
            ReleaseWhenClosed(false);
            ContentView = view;
        }

        public override bool CanBecomeKeyWindow => false;
        public override bool CanBecomeMainWindow => false;
        internal bool Scrolls => !IgnoresMouseEvents;
    }

    private static NSView? owner;
    private static Func<string>? ownerText;
    private static NSTimer? timer;
    private static NSTimer? leaveTimer;
    private static TipPanel? panel;
    private static TipView? tipView;
    private static NSObject? eventMonitor;
    private static NSWindow? parent;
    private static string shownText = "";

    internal static bool IsShown => panel is { IsVisible: true };
    internal static CGRect? ShownFrame => panel is { IsVisible: true } ? panel.Frame : null;
    internal static bool ShownScrolls => panel is { IsVisible: true, Scrolls: true };
    internal static (double Natural, double Viewport, double Offset)? ShownScroll =>
        tipView is { Overflows: true } && panel is { IsVisible: true } ? (tipView.NaturalHeight, tipView.ViewportHeight, tipView.ScrollOffset) : null;
    internal static void ScrollShownTo(double offset) => tipView?.ScrollTo(offset);

    // The cursor entered the view: show the tip once it has rested there.
    internal static void Arm(NSView view, Func<string> text)
    {
        // Back on the card of a scrolling tip that is still up (the cursor went to the tip and returned): keep it.
        if (panel is { IsVisible: true, Scrolls: true } && ReferenceEquals(owner, view))
        {
            leaveTimer?.Invalidate();
            leaveTimer = null;
            return;
        }
        Hide();
        owner = view;
        ownerText = text;
        timer = NSTimer.CreateScheduledTimer(ShowDelay, false, _ => Present(view, text(), NSEvent.CurrentMouseLocation));
    }

    internal static void Cancel(NSView view)
    {
        if (!ReferenceEquals(owner, view)) return;
        if (panel is { IsVisible: true, Scrolls: true }) CursorLeftTip(); // the cursor may be on its way to the tip
        else Hide();
    }

    private static void CursorOnTip()
    {
        leaveTimer?.Invalidate();
        leaveTimer = null;
    }

    // The cursor left the card or the scrolling tip: after a short rest, remove the tip unless it is on the tip or the card.
    private static void CursorLeftTip()
    {
        leaveTimer?.Invalidate();
        leaveTimer = NSTimer.CreateScheduledTimer(ShowDelay, false, _ =>
        {
            leaveTimer = null;
            if (panel is null) return;
            CGPoint cursor = NSEvent.CurrentMouseLocation;
            if (panel.Frame.Contains(cursor)) return;
            if (owner?.Window is { } window)
            {
                CGPoint inView = owner.ConvertPointFromView(window.ConvertPointFromScreen(cursor), null);
                if (owner.Bounds.Contains(inView)) return;
            }
            Hide();
        });
    }

    // The shared text of the hovered view changed while its tip is up: redraw it with the new text.
    internal static void TextChanged(NSView view)
    {
        if (panel is { IsVisible: true } && ReferenceEquals(owner, view) && ownerText is { } text && text() != shownText)
            Present(view, text(), NSEvent.CurrentMouseLocation);
    }

    // Show the tip for the view at a screen point (below and right of the cursor, flipped or moved to stay on the screen).
    internal static void Present(NSView view, string text, CGPoint cursor)
    {
        if (text.Length == 0 || view.Window is not { IsVisible: true } window) { Hide(); return; }
        timer?.Invalidate();
        timer = null;
        if (panel is not null) Remove();
        NSScreen? screen = NSScreen.Screens.FirstOrDefault(item => item.Frame.Contains(cursor)) ?? window.Screen ?? NSScreen.MainScreen;
        CGRect area = screen?.VisibleFrame ?? new CGRect(0, 0, 1280, 800);
        // Taller than the usable height: capped to it, the rest is reached by scrolling inside the tip.
        var content = new TipView(text, (double)area.Height - 2 * ScreenMargin);
        tipView = content;
        panel = new TipPanel(content);
        shownText = text;
        owner = view;
        parent = window;

        CGSize size = content.Frame.Size;
        double x = Math.Min((double)cursor.X + 12, (double)area.Right - size.Width - ScreenMargin);
        x = Math.Max(x, (double)area.Left + ScreenMargin);
        // Cocoa screen coordinates grow upward: the bottom edge of the usable area is its Y, the top edge Y + Height.
        double bottom = (double)area.Y, top = (double)(area.Y + area.Height);
        double y = (double)cursor.Y - 16 - size.Height;
        if (y < bottom + ScreenMargin) y = (double)cursor.Y + 20;
        y = Math.Min(y, top - size.Height - ScreenMargin);
        y = Math.Max(y, bottom + ScreenMargin);
        panel.SetFrame(new CGRect(x, y, size.Width, size.Height), false);

        // Above the popover: a child window of the one holding the hovered view.
        panel.Level = (NSWindowLevel)((long)window.Level + 1);
        window.AddChildWindow(panel, NSWindowOrderingMode.Above);
        panel.OrderFront(null);
        if (content.Overflows) content.FlashScrollers();

        // Any click, key or scroll, or the window losing key, or the lifetime ending, takes the tip away; a scrolling tip
        // keeps its own wheel and scroller clicks.
        eventMonitor ??= NSEvent.AddLocalMonitorForEventsMatchingMask(
            NSEventMask.LeftMouseDown | NSEventMask.RightMouseDown | NSEventMask.OtherMouseDown | NSEventMask.KeyDown | NSEventMask.ScrollWheel,
            e =>
            {
                if (e.Type != NSEventType.KeyDown && panel is { Scrolls: true } && ReferenceEquals(e.Window, panel)) return e;
                Hide();
                return e;
            });
        timer = NSTimer.CreateScheduledTimer(Lifetime, false, _ => Hide());
    }

    private static void Remove()
    {
        if (panel is null) return;
        parent?.RemoveChildWindow(panel);
        panel.OrderOut(null);
        panel.Close();
        panel = null;
        tipView = null;
        parent = null;
    }

    internal static void Hide()
    {
        timer?.Invalidate();
        timer = null;
        leaveTimer?.Invalidate();
        leaveTimer = null;
        Remove();
        if (eventMonitor is not null) { NSEvent.RemoveMonitor(eventMonitor); eventMonitor = null; }
        owner = null;
        ownerText = null;
        shownText = "";
    }
}
