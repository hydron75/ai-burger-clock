using System.Text.RegularExpressions;
using CoreGraphics;

namespace AiBurgerClock;

// Native smoke for the Gemini model-specific weekly rows (#62). Synthetic readings only: the model
// name, ratios and times are made up and have nothing to do with a real account.
// It checks the Mac rendering of the shared rows, not the shared parser (covered by the common tests).
internal static class MacGeminiModelRowSmoke
{
    // Same limit as MacStatusPanel.MaximumQuotaHeight: the quota area never grows past it and scrolls.
    private const double MaximumQuotaAreaHeight = 340;
    private const double MenuBarAndMargin = 25 + 24;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Gemini model rows: " + message);
    }

    internal static string Verify(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        QuotaState codex, QuotaState claude, DateTimeOffset now)
    {
        const string ModelId = "gemini-model:14:Gemini 4 Argongemini-argon-wtu-weekly";
        const string SecondModelId = "gemini-model:12:Gemini 5 Flashgemini-flash-weekly";
        const string LongModelId = "gemini-model:41:Gemini 4 Argon Ultra Experimental Preview Longgemini-argon-long-weekly";
        QuotaWindow five = new("gemini-5h", "5시간", 0, now.AddHours(5), 300);
        QuotaWindow week = new("gemini-weekly", "주간", 0.4, now.AddDays(6), 10080);
        QuotaWindow model = new(ModelId, "주간 Gemini 4 Argon", 12, now.AddDays(5), 10080);
        QuotaWindow second = new(SecondModelId, "주간 Gemini 5 Flash", 55, now.AddDays(3), 10080);
        QuotaWindow longModel = new(LongModelId, "주간 Gemini 4 Argon Ultra Experimental Preview Long", 37, now.AddDays(4), 10080);

        QuotaState Gemini(params QuotaWindow[] rows) => new(QuotaProvider.Gemini, new(QuotaProvider.Gemini, rows),
            LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(6));
        MacStatusPanel.Layout Show(QuotaState gemini)
        {
            QuotaState[] states = [codex, claude, gemini];
            panel.Update(schedule, providers, states, false, now.AddMinutes(5), "");
            // Verify also checks that rows do not overlap, the quota area fits its rows, text matches the shared model.
            return panel.Verify(schedule, providers, states);
        }

        // Compatibility: the old two-row response is unchanged.
        MacStatusPanel.Layout two = Show(Gemini(five, week));
        Check(panel.QuotaBarCount == 7, $"the two-row Gemini box must keep 7 bars in total, found {panel.QuotaBarCount}.");

        // The new response: 5 hour, weekly and the model weekly are three rows of the same box.
        MacStatusPanel.Layout three = Show(Gemini(five, week, model));
        Check(panel.QuotaBarCount == 8, $"three Gemini rows must give 8 bars in total, found {panel.QuotaBarCount}.");
        foreach (string id in new[] { five.Id, week.Id, ModelId })
        {
            Check(panel.QuotaRowText(QuotaProvider.Gemini, id).Length > 0, $"row {id} is missing its text.");
            Check(panel.QuotaBarFill(QuotaProvider.Gemini, id) >= 0, $"row {id} is missing its bar.");
        }
        Check(panel.QuotaBarFill(QuotaProvider.Gemini, ModelId) >= 2, "a 12% model row must draw a visible fill.");

        // Row text and the spoken name use the same shape as the other weekly rows.
        string text = panel.QuotaRowText(QuotaProvider.Gemini, ModelId);
        Check(Regex.IsMatch(text, @"^주간 Gemini 4 Argon  12% 사용 · 약 .+ 후 리셋$"), "the model row text does not have the weekly row shape.");
        string weekText = panel.QuotaRowText(QuotaProvider.Gemini, week.Id);
        Check(Regex.IsMatch(weekText, @"^주간  \d+(\.\d+)?% 사용 · 약 .+ 후 리셋$"), "the common weekly row text changed.");
        string spoken = panel.QuotaRowSpoken(QuotaProvider.Gemini, ModelId);
        string weekSpoken = panel.QuotaRowSpoken(QuotaProvider.Gemini, week.Id);
        Check(spoken.StartsWith(text + ". 시간 경과 ", StringComparison.Ordinal) && spoken.EndsWith(" 기준", StringComparison.Ordinal),
            "the model row is not spoken as row text, then the shared comparison, then its age.");
        Check(Regex.Replace(spoken[text.Length..], @"\d+(\.\d+)?", "#") == Regex.Replace(weekSpoken[weekText.Length..], @"\d+(\.\d+)?", "#"),
            "the model row comparison wording differs from the common weekly row.");
        var time = panel.QuotaBarTime(QuotaProvider.Gemini, ModelId);
        Check(time.Elapsed is not null && time.MarkerWidth == 1, "the model row must draw the time layer and the 1pt marker.");
        Check(panel.QuotaBarTime(QuotaProvider.Gemini, five.Id).Elapsed is null, "the 0% five-hour row must still hide its time layer.");

        // Height: the popover only grows by what the capped quota area needs, and stays on screen.
        double growth = three.Size.Height - two.Size.Height;
        Check(growth > 0 && growth <= 40, $"popover grew {growth:0}pt for the third row (expected a small growth).");
        Check(panel.QuotaAreaHeight <= MaximumQuotaAreaHeight + 0.5, $"quota area {panel.QuotaAreaHeight:0}pt is taller than its {MaximumQuotaAreaHeight}pt limit.");
        Check(three.Size.Height <= three.UsableHeight, "the popover is taller than this screen's usable height.");
        // Two models (four rows in the box) must not grow the popover any further: the rest scrolls.
        MacStatusPanel.Layout four = Show(Gemini(five, week, model, second));
        Check(panel.QuotaBarCount == 9, $"two model rows must give 9 bars in total, found {panel.QuotaBarCount}.");
        Check(Math.Abs(four.Size.Height - three.Size.Height) < 0.5 && panel.QuotaAreaHeight <= MaximumQuotaAreaHeight + 0.5,
            "a fourth Gemini row must scroll inside the quota area instead of growing the popover.");

        // Not cut off on the screens used before: the whole popover fits, the quota rows stay reachable by scrolling.
        double[] screens = [800, 640];
        double[] fitted = new double[screens.Length];
        for (int index = 0; index < screens.Length; index++)
        {
            double usable = screens[index] - MenuBarAndMargin;
            CGSize size = panel.FitFor((nfloat)usable);
            fitted[index] = size.Height;
            Check(size.Height <= usable + 0.5, $"popover {size.Height:0}pt is cut off on a {screens[index]}pt screen.");
            Check(panel.QuotaAreaHeight > 0 && panel.QuotaAreaHeight <= panel.QuotaRowsHeight + 0.5,
                $"quota area {panel.QuotaAreaHeight:0}pt is not a window onto {panel.QuotaRowsHeight:0}pt of rows on a {screens[index]}pt screen.");
        }
        panel.FitFor(four.UsableHeight);

        // A very long model name must not break the layout (rows do not overlap, bars stay 8); how the
        // text is shortened is recorded in MACOS_PORT as an observation and is not asserted here.
        MacStatusPanel.Layout longName = Show(Gemini(five, week, longModel));
        Check(panel.QuotaBarCount == 8 && longName.Size.Height <= longName.UsableHeight, "a long model name broke the layout.");
        Check(panel.QuotaRowSpoken(QuotaProvider.Gemini, LongModelId).StartsWith(panel.QuotaRowText(QuotaProvider.Gemini, LongModelId), StringComparison.Ordinal),
            "a long model name must still be spoken in full.");

        return $"Gemini model weekly rows (2 rows 7 bars {two.Size.Height:0}pt -> 3 rows 8 bars {three.Size.Height:0}pt, capped quota area scrolls, " +
               $"1280x800 {fitted[0]:0}pt and 1024x640 {fitted[1]:0}pt not cut off, weekly row shape + spoken comparison, long name keeps layout)";
    }
}
