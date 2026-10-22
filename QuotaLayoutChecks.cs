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
        return count;
    }
}
