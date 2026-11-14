using AppKit;
using CoreGraphics;

namespace AiBurgerClock;

// Native smoke for the fixed-width Provider detail tip (status card, quota box) and the shared detail template of #130.
// Every status and quota text below is synthetic (made-up incidents, URLs and readings, nothing from an account or a
// provider's real state). It checks that the Mac tip is the shared detail, always the same outer width, wraps instead of
// cutting off, and (when the popover is shown) really appears at that width inside the screen.
internal static class MacDetailTipSmoke
{
    private const string LongToken = "https://status.example.invalid/synthetic/";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Detail tip: " + message);
    }

    // Four cards with every optional part present and overlong lines: a long source URL, a long incident title, a long expiry note.
    internal static ProviderStatus[] Statuses(DateTimeOffset now) =>
    [
        new(ProviderKind.OpenAI, OfficialStatus.Degraded, now, now, "ChatGPT: 일부 기능 저하 (합성 예시)", "ChatGPT (합성)", "synthetic-openai-1",
            "합성 사건: 응답 지연이 길게 이어지고 있는 경우의 긴 제목 줄바꿈 확인용 문장입니다",
            "https://status.openai.com/api/v2/summary.json + " + LongToken + new string('a', 240)),
        new(ProviderKind.Claude, OfficialStatus.Operational, now, now, "관련 서비스 정상", Source: "https://status.claude.com/api/v2/summary.json"),
        new(ProviderKind.Gemini, OfficialStatus.Unknown, now, now.AddHours(-2), "범위 미확인 사건 있음 (합성 예시)",
            Source: "https://www.google.com/appsstatus/dashboard/incidents.json + " + LongToken + new string('b', 180))
            { UncertainIncidentTitle = "합성 사건: 범위를 알 수 없는 사건", UncertainIncidentId = "synthetic-gemini-1", LastKnownStatus = OfficialStatus.Operational, LastKnownStatusUtc = now.AddHours(-2) },
        new(ProviderKind.Xai, OfficialStatus.PartialOutage, now, now, "Cursor: Dashboard 일부 장애 (합성 예시)",
            Source: XaiStatusFeed.RssUrl + " [수신 실패] + " + XaiStatusFeed.SummaryUrl + " [응답 수신]")
            { XaiIncidentExpiryNote = XaiStatusFeed.ExpiryNote }
    ];

    // The standard quota boxes with one overlong error line each in the first and the last box.
    internal static QuotaState[] Quotas(QuotaState codex, QuotaState claude, QuotaState gemini, QuotaState xai) =>
        [codex with { Error = "조회 실패 합성 예시 " + LongToken + new string('c', 200) }, claude, gemini, xai];

    // A Gemini box with the most windows the parser accepts (64, synthetic model names) and an overlong error: a detail
    // taller than any screen.
    internal static QuotaState TallGemini(QuotaState gemini, DateTimeOffset now) => gemini with
    {
        Reading = new(QuotaProvider.Gemini, Enumerable.Range(0, 64)
            .Select(i => new QuotaWindow($"synthetic-model-{i}", $"합성 모델 {i + 1}", (i * 7) % 100, now.AddHours(i + 1), 300)).ToList()),
        Error = "조회 실패 합성 예시 " + LongToken + new string('d', 300)
    };

    private static void CheckText(string owner, string text)
    {
        MacDetailTip.Metrics metrics = MacDetailTip.Measure(text);
        Check(metrics.Size.Width == MacDetailTip.Width, $"{owner}: outer width {metrics.Size.Width}pt, expected {MacDetailTip.Width}pt.");
        Check(metrics.WidestLine <= MacDetailTip.Width - 2 * MacDetailTip.Padding + 0.5,
            $"{owner}: a line is {metrics.WidestLine:0.0}pt wide, wider than the text area.");
        Check(metrics.PlacedCharacters == metrics.Characters, $"{owner}: {metrics.PlacedCharacters} of {metrics.Characters} characters were placed (the rest is cut off).");
        int logical = text.Count(c => c == '\n') + 1;
        Check(metrics.Lines >= logical && metrics.Lines == metrics.WrappedLines,
            $"{owner}: the text layout drew {metrics.Lines} lines for {metrics.WrappedLines} wrapped lines ({logical} text lines): a line overflowed and wrapped again.");
        string? split = MacDetailTip.SplitShortWord(text);
        Check(split is null, $"{owner}: the word '{split}' fits a line but was split across two.");
    }

    internal static string Verify(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> healthy,
        QuotaState codex, QuotaState claude, QuotaState gemini, QuotaState xai, DateTimeOffset now)
    {
        ProviderStatus[] statuses = Statuses(now);
        QuotaState[] quotas = Quotas(codex, claude, gemini, xai);
        panel.Update(schedule, statuses, quotas, false, now.AddMinutes(5), "");
        panel.Verify(schedule, statuses, quotas);

        int status = 0, quota = 0, longest = 0;
        var cards = StatusPanelModel.Cards(statuses, schedule.State);
        foreach (ProviderCardText card in cards)
        {
            Check(panel.CardDetail(card.Provider) == card.Detail, $"{card.Provider} card carries a text other than the shared detail.");
            CheckText($"{card.Provider} card", card.Detail);
            Check(!card.Detail.Contains(" + ", StringComparison.Ordinal) && !card.Detail.Contains("[응답 수신]", StringComparison.Ordinal),
                $"{card.Provider} card detail still joins sources with ' + ' or shows the receipt badge.");
            status++;
            longest = Math.Max(longest, (int)MacDetailTip.Measure(card.Detail).Size.Height);
        }
        foreach (QuotaSectionText section in QuotaPanelModel.Sections(quotas, now))
        {
            Check(panel.QuotaDetail(section.Provider) == section.Detail, $"{section.Provider} quota box carries a text other than the shared detail.");
            CheckText($"{section.Provider} quota box", section.Detail);
            quota++;
            longest = Math.Max(longest, (int)MacDetailTip.Measure(section.Detail).Size.Height);
        }
        Check(status == 4 && quota == 4, $"expected four cards and four boxes, found {status} and {quota}.");

        // Stress: one word longer than the line, a Korean sentence, and emoji / combining marks must wrap whole.
        CheckText("long URL", LongToken + new string('z', 400));
        CheckText("Korean sentence", string.Concat(Enumerable.Repeat("공식 상태 페이지 열기 · 우클릭: 사용 경험 기록 · ", 8)));
        CheckText("emoji and combining marks", string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦🇰🇷é", 40)));

        // The real tip, only when the popover is on screen: same width for every card and box, inside the screen, gone after Hide.
        int shown = 0;
        if (panel.IsShown)
        {
            var widths = new List<double>();
            foreach (ProviderKind provider in Enum.GetValues<ProviderKind>()) widths.Add(ShowAndHide(panel.ShowDetailTip(provider, null), $"{provider} card"));
            foreach (QuotaProvider provider in new[] { QuotaProvider.Codex, QuotaProvider.Claude, QuotaProvider.Gemini, QuotaProvider.Xai })
                widths.Add(ShowAndHide(panel.ShowDetailTip(null, provider), $"{provider} quota box"));
            Check(widths.Distinct().Count() == 1 && widths[0] == MacDetailTip.Width, $"the shown tips have different widths: {string.Join(", ", widths)}.");
            shown = widths.Count;
        }

        // A detail taller than the screen: capped to the usable height and scrollable, nothing lost.
        QuotaState[] tallQuotas = [codex, claude, TallGemini(gemini, now), xai];
        string tall = QuotaPanelModel.Sections(tallQuotas, now).First(section => section.Provider == QuotaProvider.Gemini).Detail;
        CheckText("tall Gemini box", tall);
        const double ProbeHeight = 600;
        MacDetailTip.OverflowProbe probe = MacDetailTip.Probe(tall, ProbeHeight);
        Check(probe.Natural > ProbeHeight && probe.Overflows && probe.FrameHeight == ProbeHeight,
            $"tall detail ({probe.Natural}pt) was not capped to {ProbeHeight}pt (frame {probe.FrameHeight}pt, overflow {probe.Overflows}).");
        Check(probe.TopOffset == 0 && Math.Abs(probe.EndOffset - probe.MaxOffset) < 0.5 && probe.EndOffset + probe.Viewport >= probe.Natural - 0.5,
            $"tall detail: the end of the text is not reachable by scrolling (top {probe.TopOffset}, end {probe.EndOffset}, max {probe.MaxOffset}, viewport {probe.Viewport}, text {probe.Natural}).");
        string fits = QuotaPanelModel.Sections(quotas, now).First(section => section.Provider == QuotaProvider.Claude).Detail;
        Check(!MacDetailTip.Probe(fits, ProbeHeight).Overflows, "a detail that fits the screen must not scroll.");
        string tallResult = $"tall Gemini detail (64 synthetic windows + long error, {probe.Natural:0}pt) capped at {ProbeHeight:0}pt and scrolls to its end; ";
        if (panel.IsShown)
        {
            panel.Update(schedule, statuses, tallQuotas, false, now.AddMinutes(5), "");
            CGRect? frame = panel.ShowDetailTip(null, QuotaProvider.Gemini);
            Check(frame is { } f && MacDetailTip.IsShown, "tall detail: the tip did not appear.");
            NSScreen screen = NSScreen.Screens.FirstOrDefault(item => item.Frame.IntersectsWith(frame!.Value)) ?? NSScreen.MainScreen!;
            Check(screen.VisibleFrame.Contains(frame!.Value), $"tall detail: the tip {frame.Value} is not inside the screen {screen.VisibleFrame}.");
            Check(frame.Value.Width == MacDetailTip.Width, $"tall detail: tip width {frame.Value.Width}pt, expected {MacDetailTip.Width}pt.");
            Check(MacDetailTip.ShownScrolls && MacDetailTip.ShownScroll is { } scroll && scroll.Natural > scroll.Viewport,
                "tall detail: the tip on screen does not scroll although the text is taller than it.");
            double maxOffset = MacDetailTip.ShownScroll!.Value.Natural - MacDetailTip.ShownScroll!.Value.Viewport;
            MacDetailTip.ScrollShownTo(maxOffset);
            Check(Math.Abs(MacDetailTip.ShownScroll!.Value.Offset - maxOffset) < 0.5, "tall detail: the tip on screen did not scroll to its end.");
            tallResult += $"on screen {frame.Value.Height:0}pt high in a {screen.VisibleFrame.Height:0}pt area, scrolled to {maxOffset:0}pt; ";
            MacDetailTip.Hide();
            Check(!MacDetailTip.IsShown, "tall detail: the tip stayed after Hide.");
        }
        panel.Update(schedule, healthy, [codex, claude, gemini, xai], false, now.AddMinutes(5), "");
        panel.Verify(schedule, healthy, [codex, claude, gemini, xai]);
        return tallResult + $"detail tip ({status} status cards + {quota} quota boxes at {MacDetailTip.Width}pt fixed width, tallest {longest}pt; long URL, " +
               $"Korean words and emoji wrap without cutting off; " +
               (shown > 0 ? $"{shown} tips shown on screen at one width and removed again)" : "popover not shown: on-screen tips not checked)");
    }

    private static double ShowAndHide(CGRect? frame, string owner)
    {
        Check(frame is { } f && MacDetailTip.IsShown, $"{owner}: the tip did not appear.");
        double width = (double)frame!.Value.Width;
        NSScreen screen = NSScreen.Screens.FirstOrDefault(item => item.Frame.IntersectsWith(frame.Value)) ?? NSScreen.MainScreen!;
        Check(screen.VisibleFrame.Contains(frame.Value), $"{owner}: the tip {frame.Value} is not inside the screen {screen.VisibleFrame}.");
        MacDetailTip.Hide();
        Check(!MacDetailTip.IsShown, $"{owner}: the tip stayed after Hide.");
        return width;
    }
}
