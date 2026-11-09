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
// mark is torn apart. Other tooltips (buttons, schedule, options) stay system tooltips.
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

    // The tip as it is drawn: panel colors, a thin outline, the text laid out above.
    private sealed class TipView : NSView
    {
        private readonly TextLayout layout;
        internal TipView(string text)
        {
            layout = new TextLayout(text);
            Frame = new CGRect(CGPoint.Empty, layout.Measure().Size);
        }

        public override bool IsFlipped => true;

        public override void DrawRect(CGRect dirtyRect)
        {
            NSBezierPath path = NSBezierPath.FromRoundedRect(Bounds.Inset(0.5f, 0.5f), 6, 6);
            MacControls.PanelBackground.SetFill();
            path.Fill();
            NSColor.Separator.SetStroke();
            path.LineWidth = 1;
            path.Stroke();
            layout.Manager.DrawGlyphs(layout.Manager.GetGlyphRange(layout.Container), new CGPoint(Padding, Padding));
        }
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
            IgnoresMouseEvents = true;
            HidesOnDeactivate = false;
            ReleaseWhenClosed(false);
            ContentView = view;
        }

        public override bool CanBecomeKeyWindow => false;
        public override bool CanBecomeMainWindow => false;
    }

    private static NSView? owner;
    private static Func<string>? ownerText;
    private static NSTimer? timer;
    private static TipPanel? panel;
    private static NSObject? eventMonitor;
    private static NSWindow? parent;
    private static string shownText = "";

    internal static bool IsShown => panel is { IsVisible: true };
    internal static CGRect? ShownFrame => panel is { IsVisible: true } ? panel.Frame : null;

    // The cursor entered the view: show the tip once it has rested there.
    internal static void Arm(NSView view, Func<string> text)
    {
        Hide();
        owner = view;
        ownerText = text;
        timer = NSTimer.CreateScheduledTimer(ShowDelay, false, _ => Present(view, text(), NSEvent.CurrentMouseLocation));
    }

    internal static void Cancel(NSView view)
    {
        if (ReferenceEquals(owner, view)) Hide();
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
        var content = new TipView(text);
        panel = new TipPanel(content);
        shownText = text;
        owner = view;
        parent = window;

        NSScreen? screen = NSScreen.Screens.FirstOrDefault(item => item.Frame.Contains(cursor)) ?? window.Screen ?? NSScreen.MainScreen;
        CGRect area = screen?.VisibleFrame ?? new CGRect(0, 0, 1280, 800);
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

        // Any click, key or scroll, or the window losing key, or the lifetime ending, takes the tip away.
        eventMonitor ??= NSEvent.AddLocalMonitorForEventsMatchingMask(
            NSEventMask.LeftMouseDown | NSEventMask.RightMouseDown | NSEventMask.OtherMouseDown | NSEventMask.KeyDown | NSEventMask.ScrollWheel,
            e => { Hide(); return e; });
        timer = NSTimer.CreateScheduledTimer(Lifetime, false, _ => Hide());
    }

    private static void Remove()
    {
        if (panel is null) return;
        parent?.RemoveChildWindow(panel);
        panel.OrderOut(null);
        panel.Close();
        panel = null;
        parent = null;
    }

    internal static void Hide()
    {
        timer?.Invalidate();
        timer = null;
        Remove();
        if (eventMonitor is not null) { NSEvent.RemoveMonitor(eventMonitor); eventMonitor = null; }
        owner = null;
        ownerText = null;
        shownText = "";
    }
}
