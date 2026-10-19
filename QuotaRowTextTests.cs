using System.Globalization;
using System.Text;

namespace AiBurgerClock;

// Synthetic names and receipts only. No parser/cache/identity or quota policy changes.
internal static class QuotaRowTextTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota row text: " + label);
        }
        void Unsplit(QuotaLine line, string label) => Check(
            line.NameText is null && line.ValueText is null && line.ReadingText is null, label);

        var legacy = new QuotaLine("기존 한 줄", PanelTone.Normal, "기존 상세", "synthetic-id", 10, 20, "기존 비교");
        Check(legacy.Text == "기존 한 줄" && legacy.Detail == "기존 상세" &&
            legacy.WindowId == "synthetic-id" && legacy.ComparisonText == "기존 비교",
            "existing positional arguments keep their meaning");
        Unsplit(legacy, "new optional fields remain null for a legacy caller");

        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        foreach (var provider in Enum.GetValues<QuotaProvider>())
        {
            QuotaWindow[] windows =
            [
                new("synthetic-session", "5시간", 55, now.AddHours(3), 300),
                new("synthetic-weekly", "주간", 10, now.AddHours(126), 10080),
                new("synthetic-model-weekly", "주간 Synthetic Model Alpha", 12.34, now.AddDays(2), 10080),
                new("synthetic-missing", "합성 기간 미제공", 0, null)
            ];
            var reading = new QuotaReading(provider, windows);
            var state = new QuotaState(provider, reading, now, now, now.AddHours(1));
            var section = QuotaPanelModel.Section(state, now.AddMinutes(17));
            string[] values =
            [
                "55% 사용 · 약 2시간 43분 후 리셋",
                "10% 사용 · 약 5일 6시간 후 리셋",
                "12.3% 사용 · 약 2일 후 리셋",
                "0% 사용 · 리셋 미제공"
            ];
            Check(section.Rows.Count == windows.Length, provider + " no new visible row");
            for (int index = 0; index < windows.Length; index++)
            {
                QuotaLine row = section.Rows[index];
                QuotaWindow window = windows[index];
                string expectedText = window.Label + "  " + values[index];
                Check(row.NameText == window.Label && row.ValueText == values[index],
                    provider + " name/value split " + index);
                Check(row.Text == expectedText && row.Text == row.NameText + "  " + row.ValueText,
                    provider + " old whole-row bytes and double-space separator " + index);
                Check(row.WindowId == window.Id && row.UsedPercent == window.UsedPercent,
                    provider + " identity and observed usage unchanged " + index);
                Check(row.Detail == section.Detail,
                    provider + " whole-Provider detail is unchanged by splitting " + index);
                string expectedReading = row.ComparisonText is { } comparison
                    ? expectedText + ". " + comparison : expectedText;
                Check(row.ReadingText == expectedReading,
                    provider + " full unsqueezed reading includes comparison once " + index);
                Check(row.ComparisonText is null || row.ReadingText!.Split(row.ComparisonText, StringSplitOptions.None).Length == 2,
                    provider + " comparison is not duplicated " + index);
            }
            Check(section.Rows[0].ComparisonText == "시간 경과 40% · 균등 사용 기준보다 15%p 빠름 · 17분 전 기준" &&
                section.Rows[0].ReadingText == "5시간  55% 사용 · 약 2시간 43분 후 리셋. 시간 경과 40% · 균등 사용 기준보다 15%p 빠름 · 17분 전 기준",
                provider + " full accessibility golden preserves the comparison");
            Unsplit(section.Heading, provider + " heading is not a split quota row");
            Unsplit(section.Metadata, provider + " metadata is not a split quota row");
            if (section.Scope is { } scope) Unsplit(scope, provider + " scope is not a split quota row");

            var previous = QuotaPanelModel.Section(state with { IsPrevious = true, Error = "합성 조회 실패" }, now);
            Check(previous.Rows.All(row => row.ValueText!.Contains("사용 (이전)", StringComparison.Ordinal) &&
                row.Text == row.NameText + "  " + row.ValueText && row.ReadingText == row.Text),
                provider + " previous value keeps existing wording with no stale comparison");
            Check(previous.Rows.All(row => row.ComparisonText is null && row.ElapsedPercent is null && row.Tone == PanelTone.Muted),
                provider + " previous-value availability and tones unchanged");
            var inFlight = QuotaPanelModel.Section(state with { IsRefreshing = true }, now.AddMinutes(17));
            Check(inFlight.Rows.SequenceEqual(section.Rows.Select(row => row with { Detail = inFlight.Detail })),
                provider + " an in-flight request retains the existing row contract");
            foreach (var emptyState in new[] { new QuotaState(provider), new QuotaState(provider, Error: "합성 실패") })
            {
                var empty = QuotaPanelModel.Section(emptyState, now);
                Unsplit(empty.Rows.Single(), provider + " placeholder keeps legacy text-only layout");
                Check(empty.Rows.Single().WindowId.Length == 0 && empty.Rows.Single().UsedPercent is null,
                    provider + " placeholder is not an invented quota window");
            }
            var expired = QuotaPanelModel.Section(state, now.AddHours(3));
            Check(expired.Rows[0].ValueText == "55% 사용 (이전) · 갱신 대기" &&
                expired.Rows[0].ReadingText == expired.Rows[0].Text && expired.Rows[0].ComparisonText is null,
                provider + " an elapsed reset keeps its existing value without a comparison");
            Check(ReferenceEquals(state.Reading, reading) && ReferenceEquals(reading.Windows, windows) &&
                windows[0].Label == "5시간" && windows[2].Label == "주간 Synthetic Model Alpha" &&
                state.NextCheckUtc == now.AddHours(1) && state.LastSuccessfulCheckUtc == now,
                provider + " presentation never mutates source names, reading or polling state");
        }

        var detailState = new QuotaState(QuotaProvider.Claude, new(QuotaProvider.Claude,
            [new("synthetic-detail", "합성 모델 주간", 12.34, null)]), now, now, now.AddHours(1));
        string detailGolden = string.Join('\n',
        [
            "Claude · 모든 시각 KST",
            "리셋·한도 복원 예정",
            "합성 모델 주간: 제공되지 않음",
            "실제 복원 여부는 새 조회로 확인합니다.\n",
            "시간·사용량 비교",
            "마지막 조회 기준, 방금 전",
            "합성 모델 주간: 시간 비교: 리셋 시각 미제공",
            "리셋 예정에서 역산한 단순 시간 기준입니다. 소진 예측이나 실제 창 시작·활성 확인은 아닙니다.\n",
            "마지막 성공: 10-20 21:00",
            "다음 조회: 10-20 22:00",
            "최근 시도: 10-20 21:00:00",
            "기본 1시간 · 사용 90% 초과~100% 미만은 1시간 · 사용 100%는 15분 · 리셋 전후 15분은 5분 · 실패 시 15분부터 재시도",
            "공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다."
        ]);
        var detailSection = QuotaPanelModel.Section(detailState, now);
        Check(detailSection.Detail == detailGolden && detailSection.Rows.Single().Detail == detailGolden,
            "whole-Provider tooltip golden is byte-for-byte unchanged");
        Check(detailSection.Rows.Single().ReadingText == "합성 모델 주간  12.3% 사용 · 리셋 미제공" &&
            detailSection.Rows.Single().ComparisonText is null, "hidden comparison adds no punctuation or invented text");

        // This is the existing validated label, not an original name reconstructed from a cache.
        string normalizedName = "주간 " + new string('N', 75);
        var normalized = QuotaPanelModel.Section(detailState with
        {
            Reading = new(QuotaProvider.Claude, [new("synthetic-normalized", normalizedName, 10, null)])
        }, now).Rows.Single();
        Check(normalized.NameText == normalizedName && normalized.Text.StartsWith(normalizedName + "  ", StringComparison.Ordinal) &&
            normalized.ReadingText == normalized.Text, "the panel never further truncates or repairs a normalized label");

        double Elements(string value) => StringInfo.ParseCombiningCharacters(value).Length;
        string Fit(string value, double width) => DisplayFormatting.MiddleEllipsis(value, width, Elements);
        Check(Fit("", 10) == "", "empty text");
        Check(Fit("ABCDE", 0) == "" && Fit("ABCDE", -1) == "" && Fit("ABCDE", double.NaN) == "",
            "nonpositive or unavailable width has no display text");
        Check(Fit("ABCDE", 0.999) == "", "ellipsis itself must fit");
        Check(Fit("ABCDE", 1) == "…" && Fit("ABCDE", 2) == "…",
            "a tiny budget never preserves only one end");
        Check(Fit("ABCDE", 3) == "A…E", "both ends survive at the first usable middle-ellipsis width");
        Check(Fit("ABCDE", 4) == "AB…E", "odd retained count gives the front one extra element");
        Check(Fit("ABCDEF", 5) == "AB…EF", "even retained count is balanced");
        Check(Fit("ABCDE", 5) == "ABCDE" && Fit("ABCDE", 6) == "ABCDE" &&
            Fit("ABCDE", double.PositiveInfinity) == "ABCDE", "an entire fitting name is returned unchanged");
        Check(Fit("A", 1) == "A" && DisplayFormatting.MiddleEllipsis("W", 1, value => value == "W" ? 2 : 1) == "…",
            "a single grapheme is either intact or represented only by the ellipsis");
        Check(Fit("AB", 1) == "…" && Fit("AB", 2) == "AB", "two-element name never silently loses its last element");
        Check(Fit("Long prefix Alpha", 12).EndsWith("Alpha", StringComparison.Ordinal) &&
            Fit("Long prefix Beta", 12).EndsWith(" Beta", StringComparison.Ordinal),
            "a distinguishing suffix remains visible when its balanced budget permits it");
        Check(Fit("Alpha shared suffix", 12).StartsWith("Alpha", StringComparison.Ordinal) &&
            Fit("Beta shared suffix", 12).StartsWith("Beta", StringComparison.Ordinal),
            "a distinguishing prefix remains visible when its balanced budget permits it");

        var measured = new List<string>();
        double ShapedWidth(string value)
        {
            measured.Add(value);
            return value switch
            {
                "…" => 1,
                "ABC…FG" => 4,
                "AB…FG" => 6,
                _ => 10
            };
        }
        Check(DisplayFormatting.MiddleEllipsis("ABCDEFG", 5, ShapedWidth) == "ABC…FG",
            "whole-candidate measurement need not be monotonic under shaping");
        Check(measured.SequenceEqual(new[] { "ABCDEFG", "…", "ABC…EFG", "ABC…FG" }),
            "host callback measures the full name, ellipsis and largest balanced candidates");
        Check(DisplayFormatting.MiddleEllipsis("ABCDE", 5, _ => 7) == "",
            "host-measured width, not text length, decides whether the ellipsis fits");
        Check(DisplayFormatting.MiddleEllipsis("ABCDE", 5, _ => double.NaN) == "",
            "unavailable glyph measurement is not treated as fitting");

        // Every item is one extended grapheme cluster; all names are synthetic.
        string[] clusters = ["A", "👩🏽‍💻", "🇰🇷", "e\u0301", "가\u0301", "👨‍👩‍👧‍👦", "Z"];
        string unicodeName = string.Concat(clusters);
        Check(Elements(unicodeName) == clusters.Length, "runtime segmentation recognizes emoji, ZWJ, flags and combining marks");
        for (int width = 1; width <= clusters.Length + 1; width++)
        {
            string result = Fit(unicodeName, width);
            string expected;
            if (width >= clusters.Length) expected = unicodeName;
            else if (width < 3) expected = "…";
            else
            {
                int kept = width - 1;
                expected = string.Concat(clusters.Take((kept + 1) / 2)) + "…" + string.Concat(clusters.TakeLast(kept / 2));
            }
            Check(result == expected, "Unicode balanced text-element boundary at width " + width);
            Check(Elements(result) <= width && result.EnumerateRunes().All(rune => rune.Value != Rune.ReplacementChar.Value),
                "Unicode width fits without split surrogate or cluster at width " + width);
        }
        Check(Fit("e\u0301XYZ🇰🇷", 4) == "e\u0301X…🇰🇷", "decomposed first and flag last elements remain whole");
        Check(Fit("👩🏽‍💻XYZ👨‍👩‍👧‍👦", 3) == "👩🏽‍💻…👨‍👩‍👧‍👦", "skin-tone and family endpoints are never split");
        Check(Fit("1️⃣XYZ2️⃣", 3) == "1️⃣…2️⃣", "keycap grapheme endpoints remain whole");
        Check(unicodeName == string.Concat(clusters) && normalized.NameText == normalizedName,
            "formatting does not mutate the unsqueezed name or row contract");
        return count;
    }
}
