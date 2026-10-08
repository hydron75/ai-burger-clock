using AppKit;
using CoreGraphics;
using CoreText;
using Foundation;

namespace AiBurgerClock;

// Decision 5-4: a white disc (a dark disc on a dark menu bar) with the F/B glyph and a thin ring in
// the attention color. The image is drawn on demand under the menu bar's own appearance, so each
// display's menu bar (light, dark or inactive) gets the matching disc instead of one baked bitmap.
internal static class MacStatusIcon
{
    internal const int Size = 20;
    // The ring keeps the disc's edge visible on a light menu bar, where a plain white disc fades out.
    private const float RingWidth = 1.5f;
    private static readonly NSColor LightDisc = NSColor.FromSrgb(1f, 1f, 1f, 1f);
    private static readonly NSColor DarkDisc = NSColor.FromSrgb(44 / 255f, 44 / 255f, 46 / 255f, 1f);

    internal static NSImage Create(TrayAppearance appearance)
    {
        NSImage image = NSImage.ImageWithSize(new CGSize(Size, Size), false, _ =>
        {
            Draw(appearance, MacControls.IsDark(NSAppearance.CurrentDrawingAppearance));
            return true;
        });
        // Not a template: the attention color is part of the icon, not a system tint.
        image.Template = false;
        return image;
    }

    // Same light/dark pairs as the popover's tone colors (Tone*.colorset).
    internal static PanelTone Tone(TrayAttention attention) => attention switch
    {
        TrayAttention.Green => PanelTone.Good,
        TrayAttention.Orange => PanelTone.Caution,
        TrayAttention.Red => PanelTone.Danger,
        _ => PanelTone.Muted
    };

    internal static (NSColor Disc, NSColor Glyph) Colors(TrayAttention attention, bool dark) =>
        (dark ? DarkDisc : LightDisc, MacControls.Resolved(MacControls.Color(Tone(attention)), dark));

    private static void Draw(TrayAppearance appearance, bool dark)
    {
        var (disc, glyph) = Colors(appearance.Attention, dark);
        NSBezierPath circle = NSBezierPath.FromOvalInRect(new CGRect(1, 1, Size - 2, Size - 2));
        disc.SetFill();
        circle.Fill();
        glyph.SetStroke();
        circle.LineWidth = RingWidth;
        circle.Stroke();

        CGContext context = NSGraphicsContext.CurrentContext?.CGContext
            ?? throw new InvalidOperationException("Menu-bar icon has no drawing context.");
        context.SaveState();
        try
        {
            using var font = new CTFont("Helvetica-Bold", 13);
            using var text = new NSAttributedString(appearance.Glyph,
                new CTStringAttributes { Font = font, ForegroundColor = glyph.CGColor });
            using var line = new CTLine(text);
            context.TextMatrix = CGAffineTransform.MakeIdentity();
            CGRect bounds = line.GetImageBounds(context);
            context.TextPosition = new CGPoint((Size - bounds.Width) / 2 - bounds.X,
                (Size - bounds.Height) / 2 - bounds.Y);
            line.Draw(context);
        }
        finally { context.RestoreState(); }
    }

    // Native smoke only: render the real image under light and dark appearances at 1x and 2x and test
    // its pixels and contrast, not just the requested colors.
    internal static double VerifyImages()
    {
        double weakest = double.MaxValue;
        foreach (AgentState schedule in Enum.GetValues<AgentState>())
        foreach (TrayAttention attention in Enum.GetValues<TrayAttention>())
        {
            var appearance = new TrayAppearance(schedule, attention);
            // One image for both appearances: a cached light rendering must not leak into a dark bar.
            using var image = Create(appearance);
            if (image.Template || image.Size.Width != Size || image.Size.Height != Size)
                throw new InvalidOperationException("Menu-bar icon must be a non-template 20-point image.");
            foreach (bool dark in new[] { false, true })
            {
                var (disc, glyph) = Colors(attention, dark);
                double contrast = MacControls.Contrast(glyph, () => disc, NSAppearance.GetAppearance(NSAppearance.NameAqua)!);
                weakest = Math.Min(weakest, contrast);
                if (contrast < 4.5)
                    throw new InvalidOperationException($"{attention} glyph contrast {contrast:0.00}:1 on the {(dark ? "dark" : "light")} disc is below 4.5:1.");
                foreach (int scale in new[] { 1, 2 })
                {
                    using NSBitmapImageRep bitmap = Render(image, dark, scale);
                    string context = $"{schedule}/{attention}/{(dark ? "dark" : "light")}/{scale}x";
                    if (Pixel(bitmap, 0, 0).A > 0.05)
                        throw new InvalidOperationException("Menu-bar icon corners must be transparent: " + context);
                    // Inside the ring, left of the glyph: the disc color for this appearance.
                    if (!Near(Pixel(bitmap, 4 * scale, Size * scale / 2), disc, 0.03))
                        throw new InvalidOperationException("Menu-bar icon disc color is wrong: " + context);
                    bool glyphDrawn = false;
                    for (int y = 5 * scale; y < 15 * scale && !glyphDrawn; y++)
                    for (int x = 6 * scale; x < 14 * scale && !glyphDrawn; x++)
                        glyphDrawn = Near(Pixel(bitmap, x, y), glyph, 0.04);
                    if (!glyphDrawn)
                        throw new InvalidOperationException("Menu-bar F/B glyph was not drawn in the attention color: " + context);
                }
            }
        }
        return weakest;
    }

    private static NSBitmapImageRep Render(NSImage image, bool dark, int scale)
    {
        int pixels = Size * scale;
        var bitmap = new NSBitmapImageRep(IntPtr.Zero, pixels, pixels, 8, 4, true, false,
            "NSDeviceRGBColorSpace", 0, 0) { Size = new CGSize(Size, Size) };
        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            NSGraphicsContext.CurrentContext = NSGraphicsContext.FromBitmap(bitmap);
            NSAppearance.GetAppearance(dark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua)!
                .PerformAsCurrentDrawingAppearance(() => image.Draw(new CGRect(0, 0, Size, Size)));
        }
        finally { NSGraphicsContext.GlobalRestoreGraphicsState(); }
        return bitmap;
    }

    private static bool Near((double R, double G, double B, double A) pixel, NSColor expected, double tolerance)
    {
        NSColor color = expected.UsingColorSpace(NSColorSpace.SRGBColorSpace)!;
        return pixel.A > 0.98 && Math.Abs(pixel.R - color.RedComponent) <= tolerance &&
            Math.Abs(pixel.G - color.GreenComponent) <= tolerance && Math.Abs(pixel.B - color.BlueComponent) <= tolerance;
    }

    // Read back in sRGB so the comparison does not depend on the display's device color space.
    private static (double R, double G, double B, double A) Pixel(NSBitmapImageRep bitmap, int x, int y)
    {
        using var raw = bitmap.ColorAt(x, y) ?? throw new InvalidOperationException("Menu-bar pixel unavailable.");
        NSColor color = raw.UsingColorSpace(NSColorSpace.SRGBColorSpace) ?? raw;
        color.GetRgba(out var red, out var green, out var blue, out var alpha);
        return (red, green, blue, alpha);
    }
}
