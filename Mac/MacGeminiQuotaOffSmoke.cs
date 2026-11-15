using System.Text.RegularExpressions;
using CoreGraphics;

namespace AiBurgerClock;

// Native smoke for the Gemini automatic query stop (#156). Synthetic readings only: every window name, ratio, time and
// error below is made up and has nothing to do with a real account. It checks the Mac rendering of the shared fixed notice
// (QuotaPanelModel.Section for a Provider whose query is off), not the shared model itself (covered by the common tests):
// whatever Gemini state reaches the panel (a current or previous reading, 64 windows, a refresh in progress, a failed or
// waiting query, a cache error), the box shows ONE notice row and nothing else, and the other boxes are unaffected.
internal static class MacGeminiQuotaOffSmoke
{
    // Same limit as MacStatusPanel.MaximumQuotaHeight: the quota area never grows past it and scrolls.
    private const double MaximumQuotaAreaHeight = 340;
    private const double MenuBarAndMargin = 25 + 24;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Gemini quota off: " + message);
    }

    internal static string Verify(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        QuotaState codex, QuotaState claude, QuotaState xai, DateTimeOffset now)
    {
        string notice = QuotaNames.GeminiUnavailableText;
        string[] expectedDetailLines =
        [
            "Gemini 한도 · 조회 안 함",
            "Antigravity 약관상 앱에서 자동 조회하지 않습니다.",
            "근거: https://antigravity.google/docs/faq.md",
            "공식 서비스 상태는 상태 카드에서 확인하세요."
        ];
        QuotaWindow[] ordinary =
        [
            new("gemini-5h", "5시간", 0, now.AddHours(5), 300),
            new("gemini-weekly", "주간", 0.4, now.AddDays(6), 10080),
            new("gemini-model:14:synthetic", "주간 합성 모델", 12, now.AddDays(5), 10080)
        ];
        QuotaWindow[] tall = Enumerable.Range(0, 64)
            .Select(i => new QuotaWindow($"synthetic-model-{i}", $"합성 모델 {i + 1}", (i * 7) % 100, now.AddHours(i + 1), 300)).ToArray();
        QuotaState Reading(QuotaWindow[] windows, bool previous = false, string error = "", string cacheError = "", bool refreshing = false) =>
            new(QuotaProvider.Gemini, new(QuotaProvider.Gemini, windows), LastSuccessfulCheckUtc: now.AddMinutes(-3),
                NextCheckUtc: now.AddHours(1), CheckedAtUtc: now.AddMinutes(-3), IsPrevious: previous, IsRefreshing: refreshing,
                Error: error, CacheError: cacheError);
        (string Name, QuotaState State)[] scenes =
        [
            ("current old values", Reading(ordinary)),
            ("previous old values with an error", Reading(ordinary, previous: true, error: "조회 실패 합성 예시")),
            ("64 old windows with an error", Reading(tall, error: "조회 실패 합성 예시 " + new string('x', 200))),
            ("refresh in progress", Reading(ordinary, refreshing: true)),
            ("cache error", Reading(ordinary, cacheError: "캐시 오류 합성 예시")),
            ("failed query", new QuotaState(QuotaProvider.Gemini, null, Error: "조회 실패 합성 예시")),
            ("waiting for the first query", new QuotaState(QuotaProvider.Gemini)),
            ("no windows", new QuotaState(QuotaProvider.Gemini, new(QuotaProvider.Gemini, []), LastSuccessfulCheckUtc: now))
        ];

        MacStatusPanel.Layout Show(params QuotaState[] states)
        {
            panel.Update(schedule, providers, states, false, now.AddMinutes(5), "");
            // Verify also checks the shared text of every line, the single Provider detail per box, VoiceOver consistency
            // (a row without a bar keeps its plain text field), alignment of the visible lines and that nothing overlaps.
            return panel.Verify(schedule, providers, states);
        }

        // Baseline without any Gemini input: the other three boxes alone, to compare bars and heights with.
        MacStatusPanel.Layout without = Show(codex, claude, xai);
        int baselineBars = panel.QuotaBarCount;

        string? firstDetail = null;
        nfloat? firstBoxHeight = null, firstPopover = null, firstRows = null;
        foreach (var (name, gemini) in scenes)
        {
            MacStatusPanel.Layout layout = Show(codex, claude, gemini, xai);
            string where = name + ": ";
            // One row (the notice), no bar, no time layer: the bar count is the other three boxes' count and nothing more.
            Check(panel.QuotaRowIds(QuotaProvider.Gemini).SequenceEqual([""]), where + "the Gemini box must hold exactly one text row.");
            Check(panel.QuotaBarCount == baselineBars, $"{where}the Gemini box added bars ({panel.QuotaBarCount} vs {baselineBars} without it).");
            string row = panel.QuotaRowText(QuotaProvider.Gemini, "");
            Check(row == notice, where + $"the row is '{row}', not the fixed notice.");
            Check(panel.QuotaRowTextFits(QuotaProvider.Gemini, ""), where + "the notice is cut off instead of shown whole on its one line.");
            var parts = panel.QuotaRowParts(QuotaProvider.Gemini, "");
            Check(parts.Name == notice && parts.Value.Length == 0, where + "the notice must be one plain text (no name/value split).");
            // VoiceOver: the plain text field is the element (no bar replaces it), read once as the notice.
            Check(panel.QuotaRowSpoken(QuotaProvider.Gemini, "") == notice, where + "the notice is not spoken as its plain text.");
            Check(!Regex.IsMatch(row, @"\d"), where + "the notice row shows a number.");
            // No second line under the notice: the shared metadata line is empty and takes no room; there is no scope line.
            QuotaSectionText section = QuotaPanelModel.Sections([codex, claude, gemini, xai], now).Single(item => item.Provider == QuotaProvider.Gemini);
            Check(section.Scope is null && section.Rows.Count == 1 && section.Metadata.Text.Length == 0 && panel.QuotaMetadataHidden(QuotaProvider.Gemini),
                where + "the Gemini box must be heading + notice only (no scope line, no check-time line).");
            // The Provider detail is the same fixed text whatever the input: no old value, time or error leaks into it.
            string detail = panel.QuotaDetail(QuotaProvider.Gemini);
            Check(detail == section.Detail && detail == QuotaPanelModel.GeminiUnavailableDetail, where + "the box carries a text other than the shared fixed detail.");
            // The tip is exactly these four lines (shared constant, Windows wording): no old value, time, error or polling text.
            Check(detail.Split('\n').SequenceEqual(expectedDetailLines), where + "the Gemini tip is not the four fixed lines.");
            firstDetail ??= detail;
            Check(detail == firstDetail, where + "the Provider detail depends on the input.");
            // The box, the popover and the quota rows keep the same size for every input (the 64-window input must not grow anything).
            nfloat boxHeight = panel.QuotaBoxHeight(QuotaProvider.Gemini);
            firstBoxHeight ??= boxHeight;
            firstPopover ??= layout.Size.Height;
            firstRows ??= panel.QuotaRowsHeight;
            Check(Math.Abs(boxHeight - firstBoxHeight.Value) < 0.5 && Math.Abs(layout.Size.Height - firstPopover.Value) < 0.5 &&
                  Math.Abs(panel.QuotaRowsHeight - firstRows.Value) < 0.5, where + "the box or popover size depends on the input.");
            Check(panel.QuotaAreaHeight <= MaximumQuotaAreaHeight + 0.5 && layout.Size.Height <= layout.UsableHeight, where + "the popover or quota area is too tall.");
        }

        // The tip is not just equal text: it fits the fixed-width tip without cutting off (a long line wraps, none is cut off).
        MacDetailTip.Metrics tip = MacDetailTip.Measure(firstDetail!);
        Check(Math.Abs((double)tip.Size.Width - MacDetailTip.Width) < 0.5 && tip.PlacedCharacters == tip.Characters,
            $"the Gemini tip does not lay out whole at {MacDetailTip.Width}pt (placed {tip.PlacedCharacters} of {tip.Characters} characters).");
        // The box is a heading plus one line: shorter than the box of the one-row xAI credit (heading + row + bar + check-time line).
        nfloat xaiBoxHeight = panel.QuotaBoxHeight(QuotaProvider.Xai);
        Check(firstBoxHeight!.Value < xaiBoxHeight - 0.5,
            $"the Gemini box ({firstBoxHeight.Value:0.#}pt) is not shorter than the xAI box ({xaiBoxHeight:0.#}pt).");
        // The other boxes are unaffected: same bars, same text as without Gemini input.
        Show(codex, claude, scenes[0].State, xai);
        Check(panel.QuotaBarCount == baselineBars && panel.QuotaRowIds(QuotaProvider.Codex).Length == 2 && panel.QuotaRowIds(QuotaProvider.Claude).Length == 3 &&
              panel.QuotaRowIds(QuotaProvider.Xai).Length == 1, "the other three boxes changed their rows.");

        // A failed or waiting Gemini state still leaves the other boxes' bars and the popover alone, and a Gemini early-reset/recovery
        // record offered to the panel is ignored (the shared model returns the fixed text).
        var recent = new Dictionary<QuotaProvider, QuotaEarlyResetAlert>
        {
            [QuotaProvider.Gemini] = new(QuotaProvider.Gemini, 5, 1, now, [new("gemini-weekly", "합성 주간", 10080, 60, 4, now.AddDays(3), now.AddDays(7).AddMinutes(-1))])
        };
        panel.Update(schedule, providers, [codex, claude, scenes[0].State, xai], false, now.AddMinutes(5), "", recent);
        MacStatusPanel.Layout withRecord = panel.Verify(schedule, providers, [codex, claude, scenes[0].State, xai], recent);
        Check(!panel.QuotaDetail(QuotaProvider.Gemini).Contains("조기 초기화", StringComparison.Ordinal) && panel.QuotaDetail(QuotaProvider.Gemini) == firstDetail &&
              Math.Abs(withRecord.Size.Height - firstPopover!.Value) < 0.5, "a Gemini early-reset record changed the Gemini box.");

        // Not cut off on the screens used before: the whole popover fits, the quota rows stay reachable by scrolling.
        double[] screens = [800, 640];
        double[] fitted = new double[screens.Length];
        Show(codex, claude, scenes[2].State, xai);
        for (int index = 0; index < screens.Length; index++)
        {
            double usable = screens[index] - MenuBarAndMargin;
            CGSize size = panel.FitFor((nfloat)usable);
            fitted[index] = size.Height;
            Check(size.Height <= usable + 0.5, $"popover {size.Height:0}pt is cut off on a {screens[index]}pt screen.");
            Check(panel.QuotaAreaHeight > 0 && panel.QuotaAreaHeight <= panel.QuotaRowsHeight + 0.5,
                $"quota area {panel.QuotaAreaHeight:0}pt is not a window onto {panel.QuotaRowsHeight:0}pt of rows on a {screens[index]}pt screen.");
            Check(panel.QuotaBoxScrollsIntoView(QuotaProvider.Gemini) && panel.QuotaBoxScrollsIntoView(QuotaProvider.Xai),
                $"the Gemini and xAI boxes cannot be scrolled into view on a {screens[index]}pt screen.");
        }
        panel.FitFor(without.UsableHeight);
        Show(codex, claude, scenes[0].State, xai);

        return $"Gemini quota off ({scenes.Length} inputs incl. old values, 64 windows, refresh, error, waiting, no windows: one fixed notice row shown whole on one line, " +
               $"no bar/time layer/check-time line/scope line, {baselineBars} bars unchanged, fixed four-line tip (shared constant, fits the tip width), Gemini box {firstBoxHeight:0.#}pt (xAI box {xaiBoxHeight:0.#}pt), " +
               $"popover {firstPopover:0}pt unchanged by input, Gemini early-reset record ignored, spoken as the plain notice, 1280x800 {fitted[0]:0}pt and 1024x640 {fitted[1]:0}pt not cut off)";
    }
}
