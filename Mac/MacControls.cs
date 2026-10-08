using AppKit;
using CoreGraphics;

namespace AiBurgerClock;

// Small AppKit factories shared by the status popover and the statistics window.
internal static class MacControls
{
    // Frame-based label for fixed-layout windows (statistics).
    internal static NSTextField Label(NSView view, string text, int y, int height, int size = 12, bool bold = false)
    {
        var label = new NSTextField(new CGRect(14, y, view.Frame.Width - 28, height))
        {
            StringValue = text, Editable = false, Selectable = true, Bordered = false, Bezeled = false,
            DrawsBackground = false, Font = Font(size, bold),
            TextColor = NSColor.Label, UsesSingleLineMode = false, LineBreakMode = NSLineBreakMode.ByWordWrapping
        };
        view.AddSubview(label);
        return label;
    }

    internal static NSButton Button(NSView view, string title, CGRect frame, Action action)
    {
        var button = new NSButton(frame) { Title = title, BezelStyle = NSBezelStyle.Rounded };
        button.Activated += (_, _) => action();
        view.AddSubview(button);
        return button;
    }

    internal static NSTextView TextArea(NSView view, CGRect frame, int size)
    {
        var scroll = new NSScrollView(frame) { HasVerticalScroller = true, AutohidesScrollers = true };
        var text = new NSTextView(new CGRect(0, 0, frame.Width - 16, frame.Height))
        {
            Editable = false, Selectable = true, RichText = false, VerticallyResizable = true,
            HorizontallyResizable = false, Font = Font(size),
            TextColor = NSColor.Label, BackgroundColor = NSColor.WindowBackground
        };
        text.TextContainer!.WidthTracksTextView = true;
        text.TextContainer.Size = new CGSize(frame.Width - 16, float.MaxValue);
        text.MaxSize = new CGSize(frame.Width - 16, float.MaxValue);
        scroll.DocumentView = text;
        view.AddSubview(scroll);
        return text;
    }

    internal static NSFont Font(nfloat size, bool bold = false) =>
        (bold ? NSFont.BoldSystemFontOfSize(size) : NSFont.SystemFontOfSize(size)) ??
        throw new InvalidOperationException("macOS 기본 글꼴을 불러오지 못했습니다.");

    // Opaque popover background: white in light mode, the system dark control color in dark mode.
    // A translucent popover lets a bright desktop wash out colored text behind it.
    internal static NSColor PanelBackground => NSColor.ControlBackground;

    // Tone colors are named light/dark pairs in Assets.xcassets (Tone*.colorset), chosen for at least
    // 4.5:1 contrast on PanelBackground in both appearances; the bright system green/orange are only
    // about 2:1 on white. The shared model only names the tone.
    internal static NSColor Color(PanelTone tone) => tone switch
    {
        PanelTone.Good => Named("ToneGood"),
        PanelTone.Caution => Named("ToneCaution"),
        PanelTone.Danger => Named("ToneDanger"),
        PanelTone.Muted => Named("ToneMuted"),
        _ => NSColor.Label
    };

    private static readonly Dictionary<string, NSColor> named = [];

    private static NSColor Named(string name)
    {
        if (!named.TryGetValue(name, out NSColor? color))
            named[name] = color = NSColor.FromName(name) ??
                throw new InvalidOperationException($"Asset catalog color {name} is missing from the bundle.");
        return color;
    }

    // WCAG contrast ratio of two colors as drawn under the given appearance (native smoke check).
    internal static double Contrast(NSColor foreground, NSColor background, NSAppearance appearance)
    {
        double a = 0, b = 0;
        appearance.PerformAsCurrentDrawingAppearance(() =>
        {
            a = Luminance(foreground);
            b = Luminance(background);
        });
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(NSColor color)
    {
        NSColor srgb = color.UsingColorSpace(NSColorSpace.SRGBColorSpace) ??
            throw new InvalidOperationException("Color has no sRGB representation.");
        static double Linear(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(srgb.RedComponent) + 0.7152 * Linear(srgb.GreenComponent) + 0.0722 * Linear(srgb.BlueComponent);
    }

    internal static bool IsDark(NSAppearance appearance) =>
        appearance.FindBestMatch([NSAppearance.NameAqua, NSAppearance.NameDarkAqua]) == NSAppearance.NameDarkAqua.ToString();
}
