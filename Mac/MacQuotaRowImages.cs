namespace AiBurgerClock;

// Synthetic review images of the quota rows. Only used when the smoke run is given a folder
// (AIBURGERCLOCK_SMOKE_IMAGES); the model names, ratios and times are made up and are not an account's.
// It only uses the panel's public Update/Verify/SnapshotPng, so the same file also renders a "before" build.
internal static class MacQuotaRowImages
{
    internal const string EnvironmentVariable = "AIBURGERCLOCK_SMOKE_IMAGES";

    internal static QuotaState Gemini(DateTimeOffset now, string longName, string suffix) => new(QuotaProvider.Gemini,
        new(QuotaProvider.Gemini, [
            new("gemini-5h", "5시간", 0, now.AddHours(5), 300),
            new("gemini-weekly", "주간", 99.9, now.AddDays(6), 10080),
            new("gemini-model:1:a", "주간 " + longName + " A" + suffix, 37, now.AddDays(4), 10080),
            new("gemini-model:2:b", "주간 " + longName + " B" + suffix, 100, now.AddMinutes(14), 10080)]),
        LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(1));

    internal static void Render(MacStatusPanel panel, ScheduleSnapshot schedule, IReadOnlyList<ProviderStatus> providers,
        QuotaState codex, QuotaState claude, DateTimeOffset now, string prefix)
    {
        string? folder = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        (string name, string longName, string suffix)[] scenes =
        [
            ("short", "Argon", ""),
            ("long", "Gemini 4 Argon Ultra Experimental Preview Long Name", " Weekly Limit"),
            ("unicode", string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦🇰🇷é", 9)), " 끝")
        ];
        foreach (var (sceneName, longName, suffix) in scenes)
        {
            QuotaState[] states = [codex, claude, Gemini(now, longName, suffix)];
            panel.Update(schedule, providers, states, false, now.AddMinutes(5), "");
            panel.Verify(schedule, providers, states);
            if (panel.SnapshotPng() is { } png)
                File.WriteAllBytes(Path.Combine(folder, $"{prefix}-{sceneName}.png"), png.ToArray());
        }
    }
}
