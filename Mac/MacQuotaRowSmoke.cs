using System.Globalization;

namespace AiBurgerClock;

// Native smoke for the quota row name/value split (#80). Synthetic readings only: names, ratios and times
// are made up and have nothing to do with a real account. It checks the Mac layout of the shared row
// fields (NameText, ValueText, ReadingText) and the shared MiddleEllipsis, not the shared logic itself
// (covered by the common tests).
internal static class MacQuotaRowSmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Quota rows: " + message);
    }

    private static bool IsWhole(string shown, string original)
    {
        // A shortened name keeps whole text elements at both ends: the text before and after the ellipsis
        // are a prefix and a suffix of the original that start/end on element boundaries.
        int cut = shown.IndexOf('…');
        if (cut < 0) return shown == original;
        string front = shown[..cut], back = shown[(cut + 1)..];
        if (!original.StartsWith(front, StringComparison.Ordinal) || !original.EndsWith(back, StringComparison.Ordinal)) return false;
        if (front.Length + back.Length >= original.Length) return false;
        int[] starts = StringInfo.ParseCombiningCharacters(original);
        return Array.IndexOf(starts, front.Length) >= 0 && Array.IndexOf(starts, original.Length - back.Length) >= 0;
    }

    internal static string Verify(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        QuotaState codex, QuotaState claude, DateTimeOffset now)
    {
        const string Prefix = "Gemini 4 Argon Ultra Experimental Preview Long Name";
        QuotaState Gemini(bool previous, params QuotaWindow[] rows) => new(QuotaProvider.Gemini, new(QuotaProvider.Gemini, rows),
            LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(1), IsPrevious: previous);
        QuotaWindow Row(string id, string label, double used, DateTimeOffset? reset) => new(id, label, used, reset, 10080);

        (MacStatusPanel.Layout Layout, IReadOnlyList<QuotaLine> Rows) Show(QuotaState gemini)
        {
            QuotaState[] states = [codex, claude, gemini];
            panel.Update(schedule, providers, states, false, now.AddMinutes(5), "");
            // Verify also checks the name/value split, spoken row, alignment, overlaps and the popover size.
            MacStatusPanel.Layout layout = panel.Verify(schedule, providers, states);
            IReadOnlyList<QuotaSectionText> sections = QuotaPanelModel.Sections(states, schedule.NowUtc, null);
            return (layout, sections.Single(section => section.Provider == QuotaProvider.Gemini).Rows);
        }

        int partsChecked = 0, shortened = 0;
        void CheckRows(IReadOnlyList<QuotaLine> rows, string scene)
        {
            foreach (QuotaLine row in rows)
            {
                if (row.NameText is null || row.ValueText is null) continue;
                var parts = panel.QuotaRowParts(QuotaProvider.Gemini, row.WindowId);
                partsChecked++;
                Check(parts.Value == row.ValueText && parts.ValueFits, $"{scene}: the value of '{row.WindowId}' was not kept whole.");
                Check(parts.Name.Length > 0 && parts.NameWidth > 0, $"{scene}: the name of '{row.WindowId}' disappeared.");
                Check(IsWhole(parts.Name, row.NameText), $"{scene}: the name of '{row.WindowId}' is not the original or a whole-element middle ellipsis.");
                if (parts.Name != row.NameText) shortened++;
                string spoken = panel.QuotaRowSpoken(QuotaProvider.Gemini, row.WindowId);
                Check(spoken == (row.ReadingText ?? row.Text), $"{scene}: '{row.WindowId}' is not spoken as the shared reading text.");
                Check(spoken.Contains(row.NameText, StringComparison.Ordinal), $"{scene}: '{row.WindowId}' is not spoken with its whole name.");
                if (row.ComparisonText is { Length: > 0 } comparison)
                    Check(spoken.Split(comparison).Length == 2, $"{scene}: the comparison of '{row.WindowId}' is spoken more than once.");
                Check(panel.QuotaRowText(QuotaProvider.Gemini, row.WindowId) == row.Text, $"{scene}: the shared row text changed.");
            }
        }

        // Baseline: short names show whole, the layout is the reference for every other scene.
        var shortRows = Show(Gemini(false, Row("gemini-5h", "5시간", 0, now.AddHours(5)), Row("gemini-weekly", "주간", 0.4, now.AddDays(6))));
        CheckRows(shortRows.Rows, "short names");
        Check(shortened == 0, "short names must not be shortened.");
        int bars = panel.QuotaBarCount;
        nfloat rowsHeight = panel.QuotaRowsHeight;

        // Long names with a shared start and different ends: only the name is shortened, in the middle.
        var longRows = Show(Gemini(false,
            Row("gemini-5h", "5시간", 0, now.AddHours(5)), Row("gemini-weekly", "주간", 99.9, now.AddDays(6)),
            Row("m1", "주간 " + Prefix + " A Weekly Limit", 37, now.AddDays(4)),
            Row("m2", "주간 " + Prefix + " B Weekly Limit", 100, now.AddMinutes(14))));
        CheckRows(longRows.Rows, "long names");
        Check(shortened >= 2, "long names must be shortened.");
        var a = panel.QuotaRowParts(QuotaProvider.Gemini, "m1");
        var b = panel.QuotaRowParts(QuotaProvider.Gemini, "m2");
        Check(a.Name.StartsWith("주간 Gemini", StringComparison.Ordinal) && a.Name.EndsWith("Limit", StringComparison.Ordinal) && a.Name.Contains('…'),
            "a long name must keep its start and its end around the ellipsis.");
        Check(a.Value != b.Value && a.ValueFits && b.ValueFits, "different values must both stay whole.");
        Check(panel.QuotaBarCount == bars + 2, "two more rows must give two more bars.");

        // Emoji, ZWJ family, flag and combining mark must never be cut in the middle of an element.
        string element = "👨‍👩‍👧‍👦🇰🇷é";
        var unicode = Show(Gemini(false, Row("gemini-5h", "5시간", 0, now.AddHours(5)),
            Row("u1", "주간 " + string.Concat(Enumerable.Repeat(element, 12)) + " 끝", 55, now.AddDays(3))));
        CheckRows(unicode.Rows, "unicode names");
        Check(panel.QuotaRowParts(QuotaProvider.Gemini, "u1").Name.Contains('…'), "the unicode name was expected to be shortened.");

        // Value forms: 99.9 and 100 percent, previous marker (previous state and elapsed reset), no reset time.
        var forms = Show(Gemini(true,
            Row("f1", "주간 " + Prefix, 99.9, now.AddDays(1)), Row("f2", "주간 " + Prefix + " 2", 100, now.AddMinutes(14)),
            Row("f3", "5시간 " + Prefix, 12, now.AddMinutes(-3)), Row("f4", "주간 " + Prefix + " 4", 0, null)));
        CheckRows(forms.Rows, "value forms");
        Check(panel.QuotaRowParts(QuotaProvider.Gemini, "f1").Value.Contains("(이전)", StringComparison.Ordinal),
            "a previous reading must keep its previous mark in the value.");
        Check(panel.QuotaRowParts(QuotaProvider.Gemini, "f3").Value.Contains("(이전)", StringComparison.Ordinal), "an elapsed reset must keep its previous mark.");

        // The same rows with short and long names give the same popover and quota area: only the painting differs.
        var sameShape = Show(Gemini(false, Row("gemini-5h", "5시간", 0, now.AddHours(5)), Row("gemini-weekly", "주간", 0.4, now.AddDays(6))));
        Check(Math.Abs(sameShape.Layout.Size.Height - shortRows.Layout.Size.Height) < 0.5 && Math.Abs(panel.QuotaRowsHeight - rowsHeight) < 0.5 &&
              panel.QuotaBarCount == bars, "names and values must not change the popover height, row or bar count.");
        Check(longRows.Layout.Size.Height <= longRows.Layout.UsableHeight && unicode.Layout.Size.Height <= unicode.Layout.UsableHeight,
            "a long name broke the layout.");

        return $"quota row name/value split ({partsChecked} rows: values whole, {shortened} names shortened in the middle, " +
               "whole-element Unicode, previous/100%/no-reset values, spoken once as the shared reading, popover height and bars unchanged)";
    }
}
