using System.Text.RegularExpressions;
using AppKit;

namespace AiBurgerClock;

// Native smoke for the xAI weekly-credit quota box (Grok CLI, one row "주간 크레딧"). Synthetic readings only:
// the ratio and the times are made up and have nothing to do with a real account. It checks the Mac rendering of the
// shared rows and the Mac colors, not the shared parser (covered by the common tests).
internal static class MacXaiQuotaSmoke
{
    // Same limit as MacStatusPanel.MaximumQuotaHeight.
    private const double MaximumQuotaAreaHeight = 340;
    private const string RowId = AccountQuotaParsers.GrokWeeklyWindowId;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("xAI quota box: " + message);
    }

    internal static QuotaState Reading(DateTimeOffset now, double used, bool previous = false) => new(QuotaProvider.Xai,
        new(QuotaProvider.Xai, [new(RowId, "주간 크레딧", used, now.AddDays(3), AccountQuotaParsers.GrokWeeklyWindowMinutes)]),
        LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(1), IsPrevious: previous);

    // Resolved 8-bit components of a color under one appearance.
    private static int[] Rgb(NSColor color, bool dark)
    {
        NSColor resolved = MacControls.Resolved(color, dark);
        return [(int)Math.Round(resolved.RedComponent * 255), (int)Math.Round(resolved.GreenComponent * 255),
            (int)Math.Round(resolved.BlueComponent * 255)];
    }

    internal static string Verify(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        QuotaState codex, QuotaState claude, QuotaState gemini, QuotaState xai, DateTimeOffset now)
    {
        MacStatusPanel.Layout Show(params QuotaState[] states)
        {
            panel.Update(schedule, providers, states, false, now.AddMinutes(5), "");
            // Verify also checks the shared text, the bar color per row (current: provider color, previous: previous color),
            // the time layer, the single spoken row, the single Provider detail and that rows do not overlap.
            return panel.Verify(schedule, providers, states);
        }

        // ---- The standard fixture: four boxes, one more bar than the three-box fixture (7 -> 8) ----
        MacStatusPanel.Layout standard = Show(codex, claude, gemini, xai);
        Check(panel.QuotaBoxCount == 4, $"expected four quota boxes, found {panel.QuotaBoxCount}.");
        Check(panel.QuotaBarCount == 8, $"expected 8 bars in total (7 + the xAI weekly row), found {panel.QuotaBarCount}.");
        Check(panel.QuotaRowIds(QuotaProvider.Xai).SequenceEqual([RowId]), "the xAI box must hold exactly the weekly credit row.");
        QuotaSectionText section = QuotaPanelModel.Sections([codex, claude, gemini, xai], now).Single(item => item.Provider == QuotaProvider.Xai);
        Check(section.Heading.Text == "xAI" && section.Scope is null, "the xAI box heading must be 'xAI' with no scope line.");

        // ---- Row text, bar and time layer ----
        string text = panel.QuotaRowText(QuotaProvider.Xai, RowId);
        Check(Regex.IsMatch(text, @"^주간 크레딧  37% 사용 · 약 .+ 후 리셋$"), $"the weekly credit row text has the wrong shape: '{text}'.");
        Check(panel.QuotaBarFill(QuotaProvider.Xai, RowId) >= 2, "a 37% row must draw a visible fill.");
        var time = panel.QuotaBarTime(QuotaProvider.Xai, RowId);
        Check(time.Elapsed is not null && time.MarkerWidth == 1, "the current weekly row must draw the time layer and the 1pt marker.");

        // ---- VoiceOver: the bar is the row's one element. Name = row text then the shared comparison; description = box detail ----
        string spoken = panel.QuotaRowSpoken(QuotaProvider.Xai, RowId);
        Check(spoken.StartsWith(text + ". 시간 경과 ", StringComparison.Ordinal) && spoken.EndsWith(" 기준", StringComparison.Ordinal),
            $"the row is not spoken as row text, comparison and age: '{spoken}'.");
        var access = panel.QuotaBarAccessibility(QuotaProvider.Xai, RowId);
        Check(access.Label == spoken, "the bar name differs from the spoken row.");
        Check(access.Tip == section.Detail && section.Detail.Contains("xAI", StringComparison.Ordinal) &&
              section.Detail.Contains("주간 크레딧", StringComparison.Ordinal) && section.Detail.Contains("모든 시각 KST", StringComparison.Ordinal),
            "the bar description must be the shared xAI detail (provider, weekly credits, KST).");
        bool helpIsDetail = access.Help == section.Detail;

        // ---- Popover height: the limit stays; the fourth box scrolls inside the quota area ----
        panel.FitFor(standard.UsableHeight);
        Check(panel.QuotaRowsHeight > MaximumQuotaAreaHeight, $"four boxes ({panel.QuotaRowsHeight:0}pt) were expected to exceed the {MaximumQuotaAreaHeight}pt limit.");
        Check(panel.QuotaAreaHeight <= MaximumQuotaAreaHeight + 0.5, $"quota area {panel.QuotaAreaHeight:0}pt is taller than its {MaximumQuotaAreaHeight}pt limit.");
        Check(standard.Size.Height <= standard.UsableHeight, "the popover is taller than this screen's usable height.");
        foreach (QuotaProvider provider in new[] { QuotaProvider.Codex, QuotaProvider.Claude, QuotaProvider.Gemini, QuotaProvider.Xai })
            Check(panel.QuotaBoxScrollsIntoView(provider), $"the {provider} box cannot be scrolled fully into view in the quota area.");
        MacStatusPanel.Layout three = Show(codex, claude, gemini);
        Check(Math.Abs(three.Size.Height - standard.Size.Height) <= 40 && three.Size.Height <= standard.Size.Height + 0.5,
            $"the fourth box changed the popover from {three.Size.Height:0}pt to {standard.Size.Height:0}pt (expected at most the quota area limit).");
        Show(codex, claude, gemini, xai);

        // ---- Colors under both appearances: current vs previous vs no value ----
        foreach (bool dark in new[] { false, true })
        {
            string name = dark ? "dark" : "light";
            NSAppearance appearance = NSAppearance.GetAppearance(dark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua)!;
            NSColor current = MacControls.QuotaBarColor(QuotaProvider.Xai), previous = MacControls.QuotaBarPreviousColor(QuotaProvider.Xai);
            int[] currentRgb = Rgb(current, dark), previousRgb = Rgb(previous, dark);
            int expectedCurrent = dark ? 0xEB : 0x20;
            Check(currentRgb.All(value => value == expectedCurrent), $"the xAI fill is {string.Join(",", currentRgb)} in {name}, expected #{expectedCurrent:X2}{expectedCurrent:X2}{expectedCurrent:X2}.");
            Check(previousRgb.All(value => value == 0x80), $"the xAI previous fill is {string.Join(",", previousRgb)} in {name}, expected #808080.");
            // Not the Gemini fallback blue, and not the shared muted tone used by the other providers.
            Check(!currentRgb.SequenceEqual(Rgb(MacControls.QuotaBarColor(QuotaProvider.Gemini), dark)), $"the xAI fill is still the Gemini color in {name}.");
            double currentPanel = MacControls.Contrast(current, () => MacControls.PanelBackground, appearance);
            double previousPanel = MacControls.Contrast(previous, () => MacControls.PanelBackground, appearance);
            double currentPrevious = MacControls.Contrast(current, () => previous, appearance);
            Check(currentPanel >= 3.0 && previousPanel >= 3.0, $"xAI fills against the panel are {currentPanel:0.00}:1 / {previousPanel:0.00}:1 in {name} (need 3:1).");
            Check(currentPrevious >= 3.0, $"xAI current vs previous fill is {currentPrevious:0.00}:1 in {name} (need 3:1).");
        }

        // ---- Previous value: gray fill, previous mark in the text, no time layer ----
        Show(codex, claude, gemini, Reading(now, 37, previous: true));
        Check(panel.QuotaBarCount == 8 && panel.QuotaBarTime(QuotaProvider.Xai, RowId).Elapsed is null,
            "a previous xAI value must keep its bar without a time layer.");
        Check(panel.QuotaRowText(QuotaProvider.Xai, RowId).Contains("(이전)", StringComparison.Ordinal), "a previous xAI value must keep its previous mark.");
        Check(panel.QuotaRowSpoken(QuotaProvider.Xai, RowId) == panel.QuotaRowText(QuotaProvider.Xai, RowId),
            "a previous xAI value must be spoken as its plain row text.");

        // ---- No value: a failed or waiting reading has a text row and no bar ----
        Show(codex, claude, gemini, new QuotaState(QuotaProvider.Xai, null, Error: "조회 실패 예시"));
        Check(panel.QuotaBarCount == 7 && panel.QuotaRowIds(QuotaProvider.Xai).Length == 1, "a failed xAI reading must show one text row and no bar.");
        string failed = panel.QuotaRowText(QuotaProvider.Xai, panel.QuotaRowIds(QuotaProvider.Xai)[0]);
        Check(failed == "조회 실패 예시", "a failed xAI reading must show the shared error text.");
        Show(codex, claude, gemini, new QuotaState(QuotaProvider.Xai));
        Check(panel.QuotaBarCount == 7, "a waiting xAI reading must have no bar.");
        string waiting = panel.QuotaRowText(QuotaProvider.Xai, panel.QuotaRowIds(QuotaProvider.Xai)[0]);
        Check(waiting.StartsWith("한도 조회 대기", StringComparison.Ordinal), $"a waiting xAI reading shows '{waiting}'.");

        // ---- Exhausted: 100% fills the whole bar ----
        Show(codex, claude, gemini, Reading(now, 100));
        Check(panel.QuotaBarFill(QuotaProvider.Xai, RowId) == standard.BoxTextWidth, "a 100% xAI row must fill the whole bar.");
        Show(codex, claude, gemini, xai);
        panel.FitFor(standard.UsableHeight);

        return $"xAI weekly credit box (4 boxes, 8 bars; row text, spoken name + shared detail" +
               $"{(helpIsDetail ? " + accessibility help" : "; accessibility help differs from tooltip")}; fill #202020 light / #EBEBEB dark, previous #808080, " +
               $"each >= 3:1 from the panel and from each other; previous/failed/waiting/100% rows; quota area capped at {MaximumQuotaAreaHeight}pt, " +
               $"all four boxes scroll into view, popover {standard.Size.Height:0}pt)";
    }
}
