namespace AiBurgerClock;

// Synthetic-only Windows painting checks. They share DrawBar with the real control and need no form,
// native message loop, account query or DB. Every sample reads the center scanline: rounded track
// corners are deliberately excluded, and fill samples stay away from the final time marker.
internal static class QuotaBarUiChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (Color background in new[] { Color.White, Color.FromArgb(30, 30, 30) })
        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
        {
            var palette = QuotaBarPalette.Create(QuotaBalanceBar.ProviderColor(provider), background);
            check(palette.HasDistinctTint && palette.TintOpacity is > 0 and < 1 &&
                QuotaBarPalette.Contrast(palette.Track, palette.ElapsedFill) >= QuotaBarPalette.TintContrastTarget &&
                QuotaBarPalette.Contrast(palette.ElapsedFill, palette.UsedFill) >= QuotaBarPalette.TintContrastTarget,
                $"Time tint meets both composited 1.5:1 contrast targets for {provider} on {background}");
            check(new[] { palette.Track, palette.ElapsedFill, palette.UsedFill }.All(palette.MarkerVisibleOn),
                $"Time marker meets its 3:1 visibility target on all layers for {provider} on {background}");
        }
        var indistinct = QuotaBarPalette.Create(Color.FromArgb(232, 234, 237), Color.White);
        check(!indistinct.HasDistinctTint && indistinct.TintOpacity is null,
            "An impossible tint contrast pair explicitly falls back to the time marker, not a guessed tint");
        var highContrast = QuotaBarPalette.Create(Color.Green, Color.White, true, Color.Black, Color.White);
        check(highContrast.HighContrast && !highContrast.HasDistinctTint && highContrast.Track == Color.Black &&
            highContrast.UsedFill == Color.White && highContrast.MarkerHalo is not null &&
            highContrast.MarkerVisibleOn(Color.Black) && highContrast.MarkerVisibleOn(Color.White),
            "High contrast uses supplied system colors and an opposite marker edge rather than provider tint");

        foreach (int dpi in new[] { 96, 144, 192 })
        {
            int Scale(int value) => (int)Math.Round(value * dpi / 96.0);
            using var bar = new QuotaBalanceBar { Size = new Size(Scale(200), Scale(6)) };
            using var bitmap = new Bitmap(bar.Width, bar.Height);
            bitmap.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(bitmap);
            int y = bar.Height / 2;
            check(bar.MarkerWidth(dpi) == (int)Math.Round(dpi / 96.0, MidpointRounding.AwayFromZero) &&
                bar.Height == Scale(6), $"Marker is a pixel-aligned 1-DIP core inside the unchanged 6-DIP bar at {dpi} DPI");
            Color Pixel(int percent) => bitmap.GetPixel((int)Math.Round((bar.Width - 1) * percent / 100.0), y);
            void Paint(double used, double? elapsed, PanelTone tone = PanelTone.Good)
            {
                bar.UpdateBalance(QuotaProvider.Claude, used, tone, "Synthetic quota", "Synthetic Provider detail", elapsed);
                graphics.Clear(bar.BackColor);
                bar.DrawBar(graphics, dpi);
            }
            void CheckMarker(string scenario)
            {
                int x = bar.MarkerLeft(dpi);
                bool visible = Enumerable.Range(x, bar.MarkerWidth(dpi)).Any(sample =>
                    bitmap.GetPixel(sample, y).ToArgb() == bar.Palette().Marker.ToArgb());
                check(visible, $"{scenario}: elapsed marker remains visible and pixel-aligned at {dpi} DPI");
            }
            Paint(25, 65);
            var palette = bar.Palette();
            check(ReferenceEquals(palette, bar.Palette()),
                $"Unchanged colors reuse the resolved contrast palette instead of searching on every repaint at {dpi} DPI");
            check(Pixel(10).ToArgb() == palette.UsedFill.ToArgb() &&
                Pixel(45).ToArgb() == palette.ElapsedFill.ToArgb() && Pixel(85).ToArgb() == palette.Track.ToArgb(),
                $"E > U paints used over time tint over the gray track without changing bar height at {dpi} DPI");
            CheckMarker("E > U");
            Paint(70, 30);
            check(Pixel(15).ToArgb() == palette.UsedFill.ToArgb() && Pixel(50).ToArgb() == palette.UsedFill.ToArgb() &&
                Pixel(85).ToArgb() == palette.Track.ToArgb(),
                $"E < U keeps the existing used fill, with the hidden time position separately marked at {dpi} DPI");
            CheckMarker("E < U");
            Paint(50, 50);
            CheckMarker("E = U");
            Paint(0, 60);
            check(Pixel(30).ToArgb() == palette.ElapsedFill.ToArgb() && Pixel(85).ToArgb() == palette.Track.ToArgb(),
                $"A valid weekly time layer is still drawn when used is zero at {dpi} DPI");
            CheckMarker("U = 0");
            Paint(100, 35);
            check(Pixel(80).ToArgb() == palette.UsedFill.ToArgb(), $"100% used remains filled at {dpi} DPI");
            CheckMarker("U = 100");
            Paint(30, 0);
            CheckMarker("E = 0");
            Paint(30, 100);
            CheckMarker("E = 100");
            Paint(30, 60, PanelTone.Muted);
            check(bar.ElapsedPercent is null && Pixel(10).ToArgb() == Color.Gray.ToArgb() &&
                Pixel(50).ToArgb() == bar.Palette().Track.ToArgb(),
                $"Previous/elapsed values stay gray and suppress time tint and marker at {dpi} DPI");
            foreach (double? invalid in new double?[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 101 })
            {
                Paint(30, invalid);
                check(bar.ElapsedPercent is null && Pixel(50).ToArgb() == palette.Track.ToArgb(),
                    $"Unavailable/invalid time data never creates a guessed layer at {dpi} DPI: {invalid}");
            }
            Paint(30, 60);
            graphics.Clear(bar.BackColor);
            bar.DrawBar(graphics, dpi, true, Color.Black, Color.White);
            bool markerEdge = bitmap.GetPixel(bar.MarkerLeft(dpi), y).ToArgb() != Color.Black.ToArgb() ||
                bitmap.GetPixel(Math.Max(0, bar.MarkerLeft(dpi) - 1), y).ToArgb() == Color.White.ToArgb();
            check(Pixel(10).ToArgb() == Color.White.ToArgb() && Pixel(85).ToArgb() == Color.Black.ToArgb() && markerEdge,
                $"High-contrast painting preserves usage and the time boundary at {dpi} DPI");
            bar.BackColor = Color.FromArgb(30, 30, 30);
            Paint(25, 65);
            var dark = bar.Palette();
            check(Pixel(10).ToArgb() == dark.UsedFill.ToArgb() && Pixel(45).ToArgb() == dark.ElapsedFill.ToArgb() &&
                Pixel(85).ToArgb() == dark.Track.ToArgb(),
                $"Painting resolves time tint against the actual dark background at {dpi} DPI");
            CheckMarker("Dark background");
        }
    }
}
