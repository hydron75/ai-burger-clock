using AppKit;

namespace AiBurgerClock;

// Native check of the quota box rebuild rule (WIN-07 / #91): the panel keeps its quota boxes and rows across updates and
// rebuilds them only when the shared QuotaPanelModel.LayoutKey says the structure changed. Window IDs are opaque,
// so a delimiter-joined key can call two different structures "the same" and leave stale rows behind. The panel is
// built here but never shown: no window, no activation. Synthetic IDs and values only.
internal static class MacQuotaLayoutKeySmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Quota layout key: " + message);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static QuotaState Boxes(QuotaProvider provider, double used, params string[] ids) => new(provider,
        new(provider, ids.Select((id, index) => new QuotaWindow(id, "창 " + id, used + index, Now.AddDays(3), 10080)).ToArray()),
        LastSuccessfulCheckUtc: Now, NextCheckUtc: Now.AddHours(1));

    private static QuotaState Placeholder(QuotaProvider provider) => new(provider, null, Error: "조회 실패 예시");

    internal static string Verify()
    {
        var healthy = Enum.GetValues<ProviderKind>().Select(provider =>
            new ProviderStatus(provider, OfficialStatus.Operational, Now, Now, "관련 서비스 정상")).ToArray();
        ScheduleSnapshot schedule = AgentSchedule.GetSnapshot(Now);
        using var panel = new MacStatusPanel(() => { }, () => { }, _ => { }, _ => { }, _ => { }, (_, _, _) => { }, smoke: true);
        int steps = 0;

        // Updates the panel and checks that it holds exactly the rows the model describes: the same box count, the same row IDs per box.
        void Step(string name, params QuotaState[] states)
        {
            try { panel.Update(schedule, healthy, states, false, Now.AddMinutes(5), ""); }
            catch (Exception error) { throw new InvalidOperationException($"Quota layout key: {name}: the update threw {error.GetType().Name}: {error.Message}"); }
            IReadOnlyList<QuotaSectionText> sections = QuotaPanelModel.Sections(states, Now);
            Check(panel.QuotaBoxCount == sections.Count, $"{name}: {panel.QuotaBoxCount} boxes for {sections.Count} sections.");
            foreach (QuotaSectionText section in sections)
            {
                string[] expected = section.Rows.Select(row => row.WindowId).Order(StringComparer.Ordinal).ToArray();
                string[] actual = panel.QuotaRowIds(section.Provider);
                Check(expected.SequenceEqual(actual, StringComparer.Ordinal),
                    $"{name}: {section.Provider} shows {actual.Length} rows [{string.Join(", ", actual.Select(id => "'" + id + "'"))}] but the model has {expected.Length} [{string.Join(", ", expected.Select(id => "'" + id + "'"))}].");
            }
            steps++;
        }
        QuotaState Claude(params string[] ids) => Boxes(QuotaProvider.Claude, 20, ids);
        QuotaState Gemini(params string[] ids) => Boxes(QuotaProvider.Gemini, 5, ids);

        // Two rows whose IDs, joined with a comma, equal one row's ID.
        Step("two rows", Boxes(QuotaProvider.Codex, 10, "a:primary", "b:primary"), Claude("session"), Gemini("g5h"));
        Step("one row, id with a comma", Boxes(QuotaProvider.Codex, 10, "a:primary,b:primary"), Claude("session"), Gemini("g5h"));
        Step("two rows again", Boxes(QuotaProvider.Codex, 10, "a:primary", "b:primary"), Claude("session"), Gemini("g5h"));

        // Two boxes whose joined text equals one box with a delimiter-laden ID (the old key joined boxes with '|' and 'Provider:').
        Step("two boxes", Boxes(QuotaProvider.Codex, 10, "a"), Claude("b"));
        Step("one box, id with a delimiter", Boxes(QuotaProvider.Codex, 10, "a|Claude:b"));
        Step("two boxes again", Boxes(QuotaProvider.Codex, 10, "a"), Claude("b"));

        // A waiting box (one placeholder row) and a reading with no windows both used to look like "Provider:".
        Step("waiting box", Placeholder(QuotaProvider.Codex), Claude("session"), Gemini("g5h"));
        Step("reading without windows", new QuotaState(QuotaProvider.Codex, new(QuotaProvider.Codex, []), LastSuccessfulCheckUtc: Now, NextCheckUtc: Now.AddHours(1)),
            Claude("session"), Gemini("g5h"));
        Step("waiting box again", Placeholder(QuotaProvider.Codex), Claude("session"), Gemini("g5h"));
        Step("real rows", Boxes(QuotaProvider.Codex, 10, "five", "week"), Claude("session"), Gemini("g5h"));

        // Order, Unicode and delimiter characters in IDs.
        Step("order x,y", Boxes(QuotaProvider.Codex, 10, "x", "y"), Claude("session"), Gemini("g5h"));
        Step("order y,x", Boxes(QuotaProvider.Codex, 10, "y", "x"), Claude("session"), Gemini("g5h"));
        Step("unicode and delimiters", Boxes(QuotaProvider.Codex, 10, "한도🍔", "w|e,e:k", "a,b"), Claude("세션"), Gemini("g5h"));
        Step("single row", Boxes(QuotaProvider.Codex, 10, "한도🍔,w|e,e:k,a,b"), Claude("세션"), Gemini("g5h"));

        // Only values change: the boxes are kept, not rebuilt (no scroll or hover reset).
        QuotaState[] before = [Boxes(QuotaProvider.Codex, 10, "five", "week"), Claude("session"), Gemini("g5h")];
        Step("before value change", before);
        NSView[] boxes = Enum.GetValues<QuotaProvider>().Select(panel.QuotaBoxView).ToArray();
        Step("only values changed", Boxes(QuotaProvider.Codex, 55, "five", "week"), Claude("session") with { LastSuccessfulCheckUtc = Now.AddMinutes(1) }, Gemini("g5h"));
        Check(Enum.GetValues<QuotaProvider>().Select(panel.QuotaBoxView).Zip(boxes).All(pair => ReferenceEquals(pair.First, pair.Second)),
            "a value-only update rebuilt the quota boxes.");
        steps++;

        return $"quota layout key ({steps} updates: comma/delimiter/Unicode IDs, two rows <-> one, two boxes <-> one, waiting <-> no windows, order, value-only update keeps the boxes)";
    }
}
