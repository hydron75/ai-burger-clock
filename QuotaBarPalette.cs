namespace AiBurgerClock;

// Windows-only colors. Contrast is measured after compositing against the actual control background,
// not against the provider color's uncomposited alpha. No Windows-wide dark-mode support is implied.
internal sealed record QuotaBarPalette(Color Track, Color ElapsedFill, Color UsedFill, Color Marker,
    Color? MarkerHalo, bool HasDistinctTint, double? TintOpacity, bool HighContrast)
{
    internal const double TintContrastTarget = 1.5;
    internal const double MarkerContrastTarget = 3.0;

    internal static QuotaBarPalette Create(Color used, Color background, bool highContrast = false,
        Color? highContrastTrack = null, Color? highContrastFill = null)
    {
        Color track;
        if (highContrast)
        {
            track = highContrastTrack ?? SystemColors.Window;
            used = highContrastFill ?? SystemColors.Highlight;
        }
        else
        {
            // Preserve today's white-panel track exactly. Other backgrounds are useful to native
            // drawing callers and palette checks, without changing the host's current white panels.
            track = background.ToArgb() == Color.White.ToArgb() ? Color.FromArgb(232, 234, 237)
                : Composite(Luminance(background) < 0.5 ? Color.White : Color.Black, background, 0.14);
        }

        Color elapsed = track;
        double? opacity = null;
        if (!highContrast)
        {
            // 25% is only the preferred starting point. Select an actually distinguishable mixture
            // for this provider/background pair. Both adjacent fills must meet the local 1.5:1 goal.
            foreach (int percent in Enumerable.Range(1, 99).OrderBy(value => Math.Abs(value - 25)))
            {
                Color candidate = Composite(used, track, percent / 100.0);
                if (Contrast(track, candidate) < TintContrastTarget || Contrast(candidate, used) < TintContrastTarget)
                    continue;
                elapsed = candidate;
                opacity = percent / 100.0;
                break;
            }
        }

        // A single core works with the standard palettes. If a high-contrast/custom palette spans
        // both black and white, an opposite halo preserves a visible edge on every adjacent fill.
        Color[] surfaces = [track, elapsed, used];
        double black = surfaces.Min(color => Contrast(Color.Black, color));
        double white = surfaces.Min(color => Contrast(Color.White, color));
        Color marker = black >= white ? Color.Black : Color.White;
        Color? halo = Math.Max(black, white) >= MarkerContrastTarget ? null
            : marker.ToArgb() == Color.Black.ToArgb() ? Color.White : Color.Black;
        return new(track, elapsed, used, marker, halo, opacity is not null, opacity, highContrast);
    }

    // If no tint can meet both targets (for example track/used contrast < 1.5 squared), the caller
    // deliberately omits the tint but still draws the time marker. Never silently invent a color.
    internal bool MarkerVisibleOn(Color surface) => Contrast(Marker, surface) >= MarkerContrastTarget ||
        MarkerHalo is { } halo && Contrast(halo, surface) >= MarkerContrastTarget;

    internal static double Contrast(Color first, Color second)
    {
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static Color Composite(Color foreground, Color background, double opacity)
    {
        int Mix(byte a, byte b) => (int)Math.Round(a * opacity + b * (1 - opacity));
        return Color.FromArgb(Mix(foreground.R, background.R), Mix(foreground.G, background.G), Mix(foreground.B, background.B));
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
