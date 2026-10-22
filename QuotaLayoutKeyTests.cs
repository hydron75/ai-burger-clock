using System.Globalization;

namespace AiBurgerClock;

internal static class QuotaLayoutKeyTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota layout key: " + label);
        }
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        QuotaSectionText Section(QuotaProvider provider, params string[] ids) => QuotaPanelModel.Section(
            new(provider, new(provider, ids.Select(id => new QuotaWindow(id, "합성 한도", 20, now.AddDays(2), 10080)).ToArray()),
                LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(1)), now);
        string Key(params QuotaSectionText[] sections) => QuotaPanelModel.LayoutKey(sections);
        var two = Section(QuotaProvider.Codex, "a:primary", "b:primary");
        var one = Section(QuotaProvider.Codex, "a:primary,b:primary");
        Check(string.Join(',', two.Rows.Select(r => r.WindowId)) == string.Join(',', one.Rows.Select(r => r.WindowId)),
            "fixture matches the legacy comma collision");
        Check(Key(two) != Key(one), "one opaque ID is not two IDs");
        Check(Key(two) == Key(Section(QuotaProvider.Codex, "a:primary", "b:primary")), "same ordered shape is stable");
        Check(Key(two) != Key(Section(QuotaProvider.Codex, "b:primary", "a:primary")), "row order matters");
        Check(Key(two) != Key(two with { Provider = QuotaProvider.Claude }), "Provider identity matters");
        Check(Key(two) != Key(two with { Scope = null }), "scope visibility matters");
        Check(Key(two) != Key(two with { Rows = [] }), "row count matters");
        Check(Key() != Key(two with { Rows = [] }), "section count matters for empty rows too");
        Check(Key(two, one) != Key(one, two), "section order matters");
        var changedValues = two with
        {
            Heading = two.Heading with { Text = "변경된 합성 제목", Tone = PanelTone.Caution },
            Scope = two.Scope! with { Text = "변경된 설명" },
            Metadata = two.Metadata with { Text = "변경된 시각 안내" },
            Rows = two.Rows.Select(r => r with { Text = "변경된 합성 값", UsedPercent = 42, Tone = PanelTone.Danger,
                Detail = "변경된 상세", ElapsedPercent = 30, ComparisonText = "변경된 비교" }).ToArray()
        };
        Check(Key(two) == Key(changedValues), "display-only updates do not rebuild controls or reset hover/scroll");

        string[] opaqueIds = ["", "a", "b", "a,b", "a|b", "a:b", "0:", "12:ab", "Codex:True:a,b", "이름👩‍💻e\u0301", "a\0b", new string('x', 512)];
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var first in opaqueIds)
        {
            foreach (var ids in new[] { new[] { first }, new[] { first, "a" }, new[] { "a", first } })
            {
                string identity = System.Text.Json.JsonSerializer.Serialize(ids);
                string key = Key(Section(QuotaProvider.Gemini, ids));
                Check(!signatures.TryGetValue(key, out string? previous) || previous == identity,
                    "opaque delimiter/Unicode/empty/long IDs remain structurally distinct");
                signatures[key] = identity;
            }
        }
        var saved = CultureInfo.CurrentCulture;
        try
        {
            string invariant = Key(two, one);
            foreach (string name in new[] { "ko-KR", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Check(Key(two, one) == invariant, "layout encoding is culture-independent: " + name);
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }
        return count;
    }
}
