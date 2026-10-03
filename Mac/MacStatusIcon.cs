using AppKit;
using CoreGraphics;
using CoreText;
using Foundation;

namespace AiBurgerClock;

internal static class MacStatusIcon
{
    internal const int Size = 20;

    internal static NSImage Create(TrayAppearance appearance)
    {
        // Bake the color into bitmap representations instead of relying on a
        // status-button tint/system symbol. Both menu bars still need a native check.
        var image = new NSImage(new CGSize(Size, Size)) { Template = false };
        try
        {
            foreach (int scale in new[] { 1, 2 })
            {
                using var representation = CreateRepresentation(appearance, scale);
                image.AddRepresentation(representation);
            }
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static NSBitmapImageRep CreateRepresentation(TrayAppearance appearance, int scale)
    {
        int pixels = Size * scale;
        using var colorSpace = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(IntPtr.Zero, pixels, pixels, 8, pixels * 4,
            colorSpace, CGImageAlphaInfo.PremultipliedLast);
        context.ClearRect(new CGRect(0, 0, pixels, pixels));
        context.ScaleCTM(scale, scale);
        var color = appearance.Color;
        context.SetFillColor(color.R / 255f, color.G / 255f, color.B / 255f, 1);
        context.FillEllipseInRect(new CGRect(0.5, 0.5, Size - 1, Size - 1));

        using var font = new CTFont("Helvetica-Bold", 14);
        using var white = new CGColor(1, 1, 1, 1);
        using var text = new NSAttributedString(appearance.Glyph,
            new CTStringAttributes { Font = font, ForegroundColor = white });
        using var line = new CTLine(text);
        CGRect bounds = line.GetImageBounds(context);
        context.TextPosition = new CGPoint((Size - bounds.Width) / 2 - bounds.X,
            (Size - bounds.Height) / 2 - bounds.Y);
        line.Draw(context);

        using var raster = context.ToImage() ?? throw new InvalidOperationException("Menu-bar icon rendering failed.");
        return new NSBitmapImageRep(raster) { Size = new CGSize(Size, Size) };
    }

    // Native smoke only: test the actual image pixels, not just the requested tint.
    internal static void VerifyImages()
    {
        foreach (AgentState schedule in Enum.GetValues<AgentState>())
        foreach (TrayAttention attention in Enum.GetValues<TrayAttention>())
        {
            var appearance = new TrayAppearance(schedule, attention);
            using var image = Create(appearance);
            if (image.Template || image.Size.Width != Size || image.Size.Height != Size)
                throw new InvalidOperationException("Menu-bar icon must be a non-template 20-point image.");
            NSImageRep[] representations = image.Representations();
            try
            {
                if (representations.Length != 2)
                    throw new InvalidOperationException("Menu-bar icon needs 1x and 2x bitmap representations.");
                foreach (int scale in new[] { 1, 2 })
                {
                    var bitmap = representations.OfType<NSBitmapImageRep>().Single(rep =>
                        rep.PixelsWide == Size * scale && rep.PixelsHigh == Size * scale);
                    if (bitmap.Size.Width != Size || bitmap.Size.Height != Size)
                        throw new InvalidOperationException("Retina icon logical size changed.");
                    var pixel = Pixel(bitmap, 3 * scale, Size * scale / 2);
                    var expected = appearance.Color;
                    if (pixel.A < 0.98 || Math.Abs(pixel.R * 255 - expected.R) > 3 ||
                        Math.Abs(pixel.G * 255 - expected.G) > 3 || Math.Abs(pixel.B * 255 - expected.B) > 3)
                        throw new InvalidOperationException("Menu-bar icon state color was not baked into the bitmap.");
                    if (Pixel(bitmap, 0, 0).A > 0.05)
                        throw new InvalidOperationException("Menu-bar icon background must be transparent.");
                    bool whiteGlyph = false;
                    for (int y = 4 * scale; y < 16 * scale; y++)
                    for (int x = 5 * scale; x < 15 * scale; x++)
                    {
                        var glyph = Pixel(bitmap, x, y);
                        whiteGlyph |= glyph.A > 0.98 && glyph.R > 0.9 && glyph.G > 0.9 && glyph.B > 0.9;
                    }
                    if (!whiteGlyph) throw new InvalidOperationException("Menu-bar F/B glyph was not rendered.");
                }
            }
            finally
            {
                foreach (NSImageRep representation in representations) representation.Dispose();
            }
        }
    }

    private static (double R, double G, double B, double A) Pixel(NSBitmapImageRep bitmap, int x, int y)
    {
        using var color = bitmap.ColorAt(x, y) ?? throw new InvalidOperationException("Menu-bar pixel unavailable.");
        color.GetRgba(out var red, out var green, out var blue, out var alpha);
        return ((double)red, (double)green, (double)blue, (double)alpha);
    }
}
