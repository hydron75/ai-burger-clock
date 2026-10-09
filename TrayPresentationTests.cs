namespace AiBurgerClock;

// Explicit Windows self-test only: native pixels and the Windows naming adapter.
internal static class TrayPresentationTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Tray presentation test: " + message);
        }

        Check(WindowsProviderNames.Provider(ProviderKind.OpenAI) == "ChatGPT", "Windows provider identity displays ChatGPT");
        Check(WindowsProviderNames.Provider("OpenAI") == "ChatGPT", "Existing statistics provider text displays ChatGPT");
        Check(WindowsProviderNames.Provider(ProviderKind.Claude) == "Claude" && WindowsProviderNames.Provider(ProviderKind.Gemini) == "Gemini",
            "Other Windows provider names stay unchanged");
        Check(WindowsProviderNames.Provider("Other") == "Other", "Unrecognized statistics text is preserved");

        foreach (var state in Enum.GetValues<AgentState>())
        foreach (var attention in Enum.GetValues<TrayAttention>())
        {
            var appearance = new TrayAppearance(state, attention);
            using var icon = TrayApplicationContext.CreateStatusIcon(appearance);
            Check(icon.Width == 32 && icon.Height == 32, "Native icon dimensions: " + appearance);
            foreach (int size in new[] { 32, 16 })
            {
                using var bitmap = new Bitmap(size, size);
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.DrawIcon(icon, new Rectangle(0, 0, size, size));
                // Left-middle interior avoids both the anti-aliased outer edge and the white F/B glyph.
                Color sample = bitmap.GetPixel(size / 5, size / 2);
                Color expected = appearance.Color;
                Check(sample.A > 240 && Math.Abs(sample.R - expected.R) <= 15 &&
                    Math.Abs(sample.G - expected.G) <= 15 && Math.Abs(sample.B - expected.B) <= 15,
                    "Rendered icon background at " + size + "px: " + appearance);
                bool lightGlyphPresent = false;
                for (int y = size / 4; y < size * 3 / 4; y++)
                for (int x = size / 4; x < size * 3 / 4; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    lightGlyphPresent |= pixel.A > 200 && pixel.R > 220 && pixel.G > 220 && pixel.B > 220;
                }
                Check(lightGlyphPresent, "Light glyph pixels survive " + size + "px rendering: " + appearance);
            }
        }

        return count;
    }
}
