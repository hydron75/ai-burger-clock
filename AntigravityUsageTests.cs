namespace AiBurgerClock;

internal static class AntigravityUsageTests
{
    internal static readonly DateTimeOffset Epoch = new(2026, 11, 26, 12, 0, 50, TimeSpan.Zero);
    internal const string Text = """
        Gemini Models
        Weekly Limit Remaining
        You have used some of your weekly limit, it will fully refresh in 2 days, 3 hours.
        91%
        Five Hour Limit Remaining
        You have used some of your 5-hour limit, it will fully refresh in 1 hour, 2 minutes.
        61%
        """;

    internal static AntigravityUsagePageLoad Valid(string? text = Text) =>
        new(new Uri(AntigravityUsagePage.Url), text, "visible", "en-US", false);

    internal sealed class Reader : IAntigravityUsagePageReader
    {
        internal AntigravityUsagePageLoad Page = Valid();
        internal int Calls { get; private set; }

        public Task<AntigravityUsagePageLoad> LoadAntigravityAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Page);
        }
    }

    internal static async Task<int> RunAsync(string directory)
    {
        int count = 0;
        void Check(bool result, string label)
        {
            if (!result) throw new InvalidOperationException("Antigravity usage: " + label);
            count++;
        }
        const QuotaProvider provider = QuotaProvider.Antigravity;
        var reading = AntigravityUsagePage.Parse(Valid(), Epoch).Reading!;
        Check(QuotaProviders.All.SequenceEqual(QuotaProviders.Boxes.Append(provider)) &&
            (int)provider == 4 && QuotaNames.For(provider) == "Antigravity" && !QuotaNames.HasCliRoute(provider),
            "trailing identity with no CLI route or fifth box");
        Check(AntigravityUsagePage.IsWebReading(reading) && reading.Windows[0].UsedPercent == 39 &&
            reading.Windows[1].UsedPercent == 9, "two eligible windows convert remaining to used");
        DateTimeOffset floor = Epoch.AddSeconds(-50);
        Check(reading.Windows[0].ResetsAtUtc == floor.AddMinutes(63) &&
            reading.Windows[1].ResetsAtUtc == floor.AddDays(2).AddHours(3).AddMinutes(30), "half the displayed precision and minute truncation");
        var single = AntigravityUsagePage.Parse(Valid(Text.Replace("1 hour, 2 minutes", "1 hour")), Epoch).Reading!;
        Check(single.Windows[0].ResetsAtUtc == floor.AddMinutes(90), "single component and singular unit");
        var full = AntigravityUsagePage.Parse(Valid(Text.Replace("61%", "100%")
            .Replace("You have used some of your 5-hour limit, it will fully refresh in 1 hour, 2 minutes.", "")), Epoch).Reading!;
        Check(full.Windows[0] is { UsedPercent: 0, ResetsAtUtc: null } && AntigravityUsagePage.IsWebReading(full),
            "unused window can omit a period");
        foreach (var replacement in new[] { "6 hours", "2 minutes, 1 hour", "1 hour, 2 hours", "1 hour, 2 minutes, 3 minutes", "tomorrow" })
            Check(AntigravityUsagePage.Parse(Valid(Text.Replace("1 hour, 2 minutes", replacement)), Epoch).Failure
                is { Kind: QuotaFailureKind.FormatChanged, Code: "antigravity.duration" }, "reject invalid durations");
        Check(AntigravityUsagePage.Parse(Valid(Text.Replace("2 days, 3 hours", "7 days, 1 minute")), Epoch).Failure?.Code
            == "antigravity.duration", "weekly duration has its own bound");
        foreach (var percent in new[] { "101%", "61%\n62%", "61.5%", "" })
            Check(AntigravityUsagePage.Parse(Valid(Text.Replace("61%", percent)), Epoch).Failure?.Code == "antigravity.percent",
                "reject absent, duplicate, nonintegral or out-of-range remaining ratios");
        foreach (var labels in new[] { "Unknown\n", "Five Hour Limit Remaining\nFive Hour Limit Remaining" })
            Check(AntigravityUsagePage.Parse(Valid(Text.Replace("Five Hour Limit Remaining", labels)), Epoch).Failure?.Code
                == "antigravity.sections", "reject missing or duplicate labels");
        Check(AntigravityUsagePage.Parse(Valid(Text + "\nGemini Models"), Epoch).Failure?.Code == "antigravity.sections",
            "reject repeated group title");
        foreach (var (load, code) in new (AntigravityUsagePageLoad, string)[]
        {
            (Valid() with { FinalUri = new("https://accounts.google.com/"), TimedOut = true }, "destination"),
            (Valid() with { FinalUri = new("https://example.invalid/"), DocumentLanguage = "ko" }, "load"),
            (Valid() with { TimedOut = true, DocumentLanguage = "ko" }, "load"),
            (Valid(null) with { DocumentLanguage = "ko", VisibilityState = "hidden" }, "language"),
            (Valid(null) with { VisibilityState = "hidden" }, "not-visible"),
            (Valid(null), "not-rendered"),
            (Valid(""), "sections")
        })
            Check(AntigravityUsagePage.Parse(load, Epoch).Failure?.Code == "antigravity." + code, "classification precedence");
        foreach (string language in new[] { "ko", "" })
        {
            var localized = AntigravityUsagePage.Parse(Valid() with { DocumentLanguage = language }, Epoch);
            Check(localized.Reading?.Windows.SequenceEqual(reading.Windows) == true,
                "an English usage section parses regardless of the document language");
        }
        var noScriptResponse = new AntigravityUsagePageLoad(new Uri(AntigravityUsagePage.Url), null, null, null, TimedOut: false);
        Check(AntigravityUsagePage.Parse(noScriptResponse, Epoch).Failure is
            { Kind: QuotaFailureKind.Unreachable, Code: "antigravity.not-rendered" },
            "a usage page with no script response is classified as not rendered");
        var missingLanguage = Valid(null) with { DocumentLanguage = "", VisibilityState = "visible" };
        Check(AntigravityUsagePage.Parse(missingLanguage, Epoch).Failure is
            { Kind: QuotaFailureKind.Unreachable, Code: "antigravity.not-rendered" },
            "a visible page with an empty document language and no usage section is classified as not rendered");

        var now = Epoch;
        var reader = new Reader();
        var store = new UsageStore(Path.Combine(directory, "antigravity", "fixture.db"));
        var cache = QuotaCacheAdapter.ForAntigravityWeb(store);
        await cache.WriteAsync(reading, now, [], default);
        Check((await cache.ReadAsync(default))?.Reading.Windows.SequenceEqual(reading.Windows) == true &&
            !cache.RestoreOnStart(QuotaSourceMode.Off) && cache.RestoreOnStart(QuotaSourceMode.Active), "dedicated cache and startup mode guard");
        try
        {
            await store.WriteQuotaCacheAsync(QuotaCacheAdapter.GeminiWebKey, new(1, reading, now, []), null);
            throw new InvalidOperationException("Antigravity wrote to the Gemini key.");
        }
        catch (InvalidDataException) { count++; }
        var invalid = reading with { Windows = [reading.Windows[0] with { ResetsAtUtc = null }, reading.Windows[1]] };
        Check(!AntigravityUsagePage.IsWebReading(invalid), "nonzero use requires a UTC reset");

        bool ready = false;
        var source = new AntigravityWebQuotaSource(reader, store, () => ready ? QuotaReadiness.Ready : QuotaReadiness.Deferred);
        Check(source.Traits is { Export: QuotaExportMode.None, Timeout: null, EmitsObservations: false,
            RefreshOnRecovery: false, StickyManualRefresh: true, DueCheckBeforeRead: true, DependsOn: QuotaProvider.Gemini,
            CancelLifetimeAsynchronously: true, CanSetMode: true }, "web source traits");
        using var monitor = new AccountQuotaMonitor([(source, QuotaSourceMode.Active)], store, () => now);
        int observations = 0;
        monitor.Succeeded += _ => observations++;
        var original = monitor.Snapshot().Single();
        monitor.RequestSourceRefresh(provider);
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 0 && monitor.Snapshot().Single() == original, "deferred due check does not modify state");
        ready = true;
        now = now.AddMinutes(4);
        monitor.RequestSourceRefresh(provider);
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 0, "manual request cannot bypass deferred interval");
        now = now.AddMinutes(1);
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 1 && monitor.Snapshot().Single().NextCheckUtc == now.AddMinutes(30) && observations == 0,
            "ready at deferred boundary uses thirty minutes with no notification observation");
        reader.Page = Valid(Text.Replace("61%", "0%").Replace("1 hour, 2 minutes", "3 minutes"));
        monitor.RequestSourceRefresh(provider);
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 2 && monitor.Snapshot().Single().CheckedAtUtc == now &&
            monitor.Snapshot().Single().NextCheckUtc == now.AddMinutes(30), "manual refresh is immediate and exhaustion near reset stays at thirty minutes");
        reader.Page = Valid(Text.Replace("61%", "9%").Replace("1 hour, 2 minutes", "3 minutes"));
        monitor.RequestSourceRefresh(provider);
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 3 && monitor.Snapshot().Single().NextCheckUtc == now.AddMinutes(30),
            "usage over ninety percent and a near reset also use thirty minutes");
        Check((await cache.ReadAsync(default))?.ResetAnchors.Count == 0, "successful reads still save zero reset anchors");
        reader.Page = default;
        for (int failure = 0; failure < 2; failure++)
        {
            now = monitor.Snapshot().Single().NextCheckUtc!.Value;
            await monitor.RefreshSourceIfDueAsync(provider);
            Check(monitor.Snapshot().Single() is { IsPrevious: true, Failure.Kind: QuotaFailureKind.Unreachable }
                && monitor.Snapshot().Single().NextCheckUtc == now.AddMinutes(30), "consecutive failures keep the same thirty-minute delay");
        }
        reader.Page = Valid() with { FinalUri = new Uri("https://accounts.google.com/") };
        monitor.RequestSourceRefresh(provider);
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 6 && monitor.Snapshot().Single() is { NextCheckUtc: null, Failure.Kind: QuotaFailureKind.LoginRequired },
            "login required pauses auto checks");
        now = now.AddHours(2);
        reader.Page = Valid();
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 6, "paused deadline does not resume automatically");
        monitor.RequestSourceRefresh(provider);
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 7 && monitor.Snapshot().Single().NextCheckUtc == now.AddMinutes(30),
            "a manual request can resume a paused session");
        monitor.SetMode(provider, QuotaSourceMode.Off);
        monitor.SetMode(provider, QuotaSourceMode.Active);
        await monitor.RefreshSourceIfDueAsync(provider);
        Check(reader.Calls == 8, "explicit re-enable triggers a read immediately");
        monitor.SetMode(provider, QuotaSourceMode.Off);
        await monitor.ClearCacheAsync(provider);
        Check(await cache.ReadAsync(default) is null, "explicit off clears the dedicated cache");

        QuotaState[] boxes = QuotaProviders.Boxes.Select(p => new QuotaState(p)).ToArray();
        var before = QuotaPanelModel.Sections(boxes, Epoch);
        var inactive = QuotaPanelModel.Sections([.. boxes, new(provider, Mode: QuotaSourceMode.Off)], Epoch);
        Check(QuotaPanelModel.LayoutKey(before) == QuotaPanelModel.LayoutKey(inactive) &&
            before.Select(x => x.Detail).SequenceEqual(inactive.Select(x => x.Detail)), "off keeps layout key and details unchanged");
        var both = QuotaPanelModel.Sections([.. boxes, new(provider)], Epoch);
        var parts = both.Single(s => s.Provider == QuotaProvider.Gemini).Parts!;
        Check(both.Count == 4 && parts.Count == 2 && parts.SelectMany(p => p.Rows).Select(r => r.WindowId).Distinct().Count() == 2 &&
            QuotaPanelModel.LayoutKey(both) != QuotaPanelModel.LayoutKey(before), "two parts have distinct placeholders and layout identity");
        var estimate = reading.Windows[0].ResetsAtUtc!.Value;
        var active = new QuotaState(provider, reading, LastSuccessfulCheckUtc: Epoch);
        Check(QuotaPanelModel.Section(active, Epoch).Detail.Contains("자동 30분 · 수동 Refresh · 실패 시 30분 뒤 다시 조회",
            StringComparison.Ordinal), "Antigravity displays the same fixed cadence guidance as Gemini and xAI");
        Check(QuotaPanelModel.Section(active, estimate.AddMinutes(29)).Rows[0].Tone != PanelTone.Muted &&
            QuotaPanelModel.Section(active, estimate.AddMinutes(30)).Rows[0].Tone == PanelTone.Muted &&
            QuotaPanelModel.Section(active, estimate).Detail.Contains("약 ", StringComparison.Ordinal) &&
            QuotaPanelModel.Section(active, estimate.AddMinutes(-43)).Rows[0].Text.Contains("약 43분 후 리셋", StringComparison.Ordinal),
            "minute-level approximate reset rendering and thirty-minute grace");
        return count;
    }
}
