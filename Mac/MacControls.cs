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

    // Hover tint for clickable provider cards; resolved while drawing, so it follows the appearance
    // and accent color. Light enough that tone text keeps at least 4.5:1 (smoke-checked).
    internal static NSColor CardHoverBackground() =>
        PanelBackground.BlendedColor(0.08f, NSColor.ControlAccent) ?? PanelBackground;

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

    // Usage-bar fills use the same provider colors as Windows (ChatGPT #10A37F, Claude #D97757,
    // Gemini #4285F4). Each keeps at least 3:1 against PanelBackground in both appearances
    // (smoke-checked, graphical-object contrast); previous/elapsed values use the Muted tone instead.
    internal static NSColor QuotaBarColor(QuotaProvider provider) => provider switch
    {
        QuotaProvider.Codex => ProviderGreen,
        QuotaProvider.Claude => ProviderOrange,
        _ => ProviderBlue
    };

    private static readonly NSColor ProviderGreen = NSColor.FromSrgb(16 / 255f, 163 / 255f, 127 / 255f, 1);
    private static readonly NSColor ProviderOrange = NSColor.FromSrgb(217 / 255f, 119 / 255f, 87 / 255f, 1);
    private static readonly NSColor ProviderBlue = NSColor.FromSrgb(66 / 255f, 133 / 255f, 244 / 255f, 1);

    // ---- Usage-bar layers: track -> provider time tint -> used fill -> time marker ----
    // Resolved per appearance from the actual panel background, like the Windows palette: the tint is
    // the used color mixed into the track at an opacity chosen so that tint<->track and tint<->used
    // are each at least 1.5:1 (25% is only the preferred start), and the 1pt marker is black or white,
    // whichever keeps at least 3:1 against every adjacent face (an opposite halo covers the rest).
    internal readonly record struct Rgb(double R, double G, double B)
    {
        internal double Luminance
        {
            get
            {
                static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                return 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);
            }
        }

        internal static Rgb White => new(1, 1, 1);
        internal static Rgb Black => new(0, 0, 0);

        internal static double Contrast(Rgb first, Rgb second)
        {
            double a = first.Luminance, b = second.Luminance;
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        // foreground over background at the given opacity, rounded to 8-bit like drawn pixels.
        internal static Rgb Mix(Rgb foreground, Rgb background, double opacity)
        {
            static double Round8(double v) => Math.Round(v * 255) / 255;
            return new(Round8(foreground.R * opacity + background.R * (1 - opacity)),
                Round8(foreground.G * opacity + background.G * (1 - opacity)),
                Round8(foreground.B * opacity + background.B * (1 - opacity)));
        }

        internal NSColor ToColor() => NSColor.FromSrgb((nfloat)R, (nfloat)G, (nfloat)B, 1);
    }

    internal sealed record BarPalette(Rgb Track, Rgb Elapsed, Rgb Used, Rgb Marker, Rgb? Halo,
        bool HasDistinctTint, double? TintOpacity)
    {
        internal const double TintContrastTarget = 1.5;
        internal const double MarkerContrastTarget = 3.0;

        internal static BarPalette Create(Rgb used, Rgb background)
        {
            // Light panels use the Windows white-panel track (about 9% black, 232 on white): a darker track
            // leaves too narrow a band for a tint that is 1.5:1 from both the track and the used color.
            bool darkBackground = background.Luminance < 0.5;
            Rgb track = Rgb.Mix(darkBackground ? Rgb.White : Rgb.Black, background, darkBackground ? 0.14 : 0.09);
            Rgb elapsed = track;
            double? opacity = null;
            foreach (int percent in Enumerable.Range(1, 99).OrderBy(value => Math.Abs(value - 25)))
            {
                Rgb candidate = Rgb.Mix(used, track, percent / 100.0);
                if (Rgb.Contrast(track, candidate) < TintContrastTarget || Rgb.Contrast(candidate, used) < TintContrastTarget)
                    continue;
                elapsed = candidate;
                opacity = percent / 100.0;
                break;
            }
            Rgb[] faces = [track, elapsed, used];
            double black = faces.Min(face => Rgb.Contrast(Rgb.Black, face));
            double white = faces.Min(face => Rgb.Contrast(Rgb.White, face));
            Rgb marker = black >= white ? Rgb.Black : Rgb.White;
            Rgb? halo = Math.Max(black, white) >= MarkerContrastTarget ? null
                : marker == Rgb.Black ? Rgb.White : Rgb.Black;
            return new(track, elapsed, used, marker, halo, opacity is not null, opacity);
        }

        // Smoke: every adjacent pair meets its target (tint omitted only when it cannot).
        internal bool MeetsTargets =>
            (!HasDistinctTint || Rgb.Contrast(Track, Elapsed) >= TintContrastTarget && Rgb.Contrast(Elapsed, Used) >= TintContrastTarget) &&
            new[] { Track, HasDistinctTint ? Elapsed : Track, Used }.All(face =>
                Rgb.Contrast(Marker, face) >= MarkerContrastTarget ||
                Halo is { } halo && Rgb.Contrast(halo, face) >= MarkerContrastTarget);
    }

    private static readonly Dictionary<(Rgb Used, bool Dark), BarPalette> barPalettes = [];

    // Palette for a bar fill color under light or dark appearance (resolved against the panel background).
    internal static BarPalette BarPaletteFor(NSColor used, bool dark)
    {
        Rgb usedRgb = ToRgb(Resolved(used, dark));
        if (!barPalettes.TryGetValue((usedRgb, dark), out BarPalette? palette))
            barPalettes[(usedRgb, dark)] = palette = BarPalette.Create(usedRgb, ToRgb(Resolved(PanelBackground, dark)));
        return palette;
    }

    private static Rgb ToRgb(NSColor srgb) =>
        new(Math.Round(srgb.RedComponent * 255) / 255, Math.Round(srgb.GreenComponent * 255) / 255,
            Math.Round(srgb.BlueComponent * 255) / 255);

    private static readonly Dictionary<string, NSColor> named = [];

    private static NSColor Named(string name)
    {
        if (!named.TryGetValue(name, out NSColor? color))
            named[name] = color = NSColor.FromName(name) ??
                throw new InvalidOperationException($"Asset catalog color {name} is missing from the bundle.");
        return color;
    }

    // WCAG contrast ratio of two colors as drawn under the given appearance (native smoke check).
    // The background is a factory so a blended color is also resolved under that appearance.
    internal static double Contrast(NSColor foreground, Func<NSColor> background, NSAppearance appearance)
    {
        double a = 0, b = 0;
        appearance.PerformAsCurrentDrawingAppearance(() =>
        {
            a = Luminance(foreground);
            b = Luminance(background());
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

    // A dynamic color fixed to its light or dark value (for drawing that picks the appearance itself).
    internal static NSColor Resolved(NSColor color, bool dark)
    {
        NSColor? resolved = null;
        NSAppearance.GetAppearance(dark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua)!
            .PerformAsCurrentDrawingAppearance(() => resolved = color.UsingColorSpace(NSColorSpace.SRGBColorSpace));
        return resolved ?? throw new InvalidOperationException("Color has no sRGB representation.");
    }

    internal static bool IsDark(NSAppearance appearance) =>
        appearance.FindBestMatch([NSAppearance.NameAqua, NSAppearance.NameDarkAqua]) == NSAppearance.NameDarkAqua.ToString();
}
