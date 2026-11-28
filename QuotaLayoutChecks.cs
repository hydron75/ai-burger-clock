namespace AiBurgerClock;

// Off-screen Windows controls only: no tray, message loop, real queries, DB or notifications.
internal static class QuotaLayoutChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota layout identity: " + label);
        }
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        string window = $$"""{"usedPercent":20,"resetsAt":{{now.AddHours(2).ToUnixTimeSeconds()}},"windowDurationMins":300}""";
        var two = AccountQuotaParsers.ParseCodex("{\"rateLimitsByLimitId\":{\"a\":{\"primary\":" + window + "},\"b\":{\"primary\":" + window + "}}}");
        var one = AccountQuotaParsers.ParseCodex("{\"rateLimitsByLimitId\":{\"a:primary,b\":{\"primary\":" + window + "}}}");
        Check(two.Windows.Count == 2 && one.Windows.Count == 1, "both structurally valid synthetic Codex responses parse");
        Check(string.Join(',', two.Windows.Select(w => w.Id)) == string.Join(',', one.Windows.Select(w => w.Id)),
            "different valid ID arrays collide in the legacy delimiter encoding");
        QuotaState State(QuotaReading reading) => new(QuotaProvider.Codex, reading,
            LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(1));
        using var view = new AccountQuotaView(() => new Point(-10000, -10000)) { Size = new Size(360, 500) };
        int Rows() => view.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<AccountQuotaView.RowLabel>()).Count();
        int Bars() => view.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<QuotaBalanceBar>()).Count();
        view.UpdateQuotas([State(two)], now);
        Check(Rows() == 2 && Bars() == 2, "first response creates two native rows and bars");
        view.UpdateQuotas([State(one)], now);
        Check(Rows() == 1 && Bars() == 1, "colliding legacy key rebuilds one native row and bar without exception");
        view.UpdateQuotas([State(two)], now);
        Check(Rows() == 2 && Bars() == 2, "reverse transition rebuilds both original rows and bars");
        view.UpdateQuotas([State(two with { Windows = two.Windows.Reverse().ToArray() })], now);
        Check(Rows() == 2 && Bars() == 2, "reordered IDs remain addressable without stale dictionary entries");
        var historicalGemini = new QuotaState(QuotaProvider.Gemini, new(QuotaProvider.Gemini, two.Windows),
            CheckedAtUtc: now, LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(1), IsPrevious: true, IsRefreshing: true, Mode: QuotaSourceMode.Unavailable);
        view.UpdateQuotas([historicalGemini], now);
        var gemini = view.Controls.OfType<Panel>().Single();
        var notice = gemini.Controls.OfType<AccountQuotaView.RowLabel>().Single();
        Check(gemini.Name == "GeminiQuotaBox" && Rows() == 1 && Bars() == 0 &&
            notice.Text == QuotaNames.GeminiUnavailableText && notice.AccessibleName == QuotaNames.GeminiUnavailableText &&
            gemini.Controls.OfType<Label>().All(label => !label.Text.Contains('%')),
            "historical Gemini data renders one policy notice without percentages or bars");
        view.UpdateQuotas([new(QuotaProvider.Gemini, Mode: QuotaSourceMode.Unavailable)], now.AddMinutes(17));
        Check(ReferenceEquals(gemini, view.Controls.OfType<Panel>().Single()) &&
            ReferenceEquals(notice, gemini.Controls.OfType<AccountQuotaView.RowLabel>().Single()) &&
            notice.Text == QuotaNames.GeminiUnavailableText && Bars() == 0,
            "Gemini policy card and row stay stable across historical and empty snapshots");
        const QuotaProvider antigravity = QuotaProvider.Antigravity;
        QuotaState[] providers = QuotaProviders.Boxes.Select(provider => new QuotaState(provider)).ToArray();
        view.UpdateQuotas([.. providers, new(antigravity)], now);
        Panel Combined() => view.Controls.OfType<Panel>().Single(panel => panel.Name == "GeminiQuotaBox");
        Check(view.Controls.OfType<Panel>().Count() == 4 && Combined().Controls.OfType<AccountQuotaView.RowLabel>().Count() == 2 &&
            Combined().Controls.OfType<QuotaBalanceBar>().Count() == 0, "two source placeholders share one box with neither bar");
        QuotaReading ag = AntigravityUsagePage.Parse(AntigravityUsageTests.Valid(), now).Reading!;
        var geminiWeb = GeminiUsagePage.Parse(new(GeminiUsagePage.Url, GeminiUsagePageTests.English),
            GeminiUsagePageTests.Now, TimeZoneInfo.Utc).Reading!;
        providers[(int)QuotaProvider.Gemini] = new(QuotaProvider.Gemini, geminiWeb, LastSuccessfulCheckUtc: now);
        view.UpdateQuotas([.. providers, new(antigravity, ag, LastSuccessfulCheckUtc: now)], now);
        Check(Combined().Controls.OfType<QuotaBalanceBar>().Count() == 4 &&
            Combined().Controls.OfType<Label>().Any(label => label.Text == "Gemini 앱") &&
            Combined().Controls.OfType<Label>().Any(label => label.Text == "Antigravity"),
            "four synthetic rows and their source headings render in the Gemini box");
        Check(view.DetailFor(Combined()).Contains("약 ", StringComparison.Ordinal) &&
            Combined().Controls.OfType<QuotaBalanceBar>().All(bar => bar.Bottom <= Combined().Height),
            "the combined detail shows approximate estimates and bars fit the box");
        view.UpdateQuotas([.. providers, new(antigravity, Mode: QuotaSourceMode.Off)], now);
        Check(Combined().Controls.OfType<QuotaBalanceBar>().Count() == 2 &&
            !Combined().Controls.OfType<Label>().Any(label => label.Text == "Antigravity"),
            "turning off Antigravity restores the original native Gemini layout");
        return count;
    }
}
