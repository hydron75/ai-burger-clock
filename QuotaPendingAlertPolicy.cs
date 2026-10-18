namespace AiBurgerClock;

// Pure revalidation of already-created pending alerts against a verified reading.
// Hosts still own settings/epochs, clock reversal, failed/previous readings, in-flight
// deferral, queues and delivery. These are deliberately different contracts: recovery
// does not acquire early-reset-only age, continuity, whole-window-set or reset-drift rules.
internal static class QuotaPendingAlertPolicy
{
    public static bool RecoveryStillCurrent(QuotaRecoveryAlert alert, QuotaReading expected,
        QuotaReading current, DateTimeOffset now) => alert.WindowIds.All(id =>
            current.Windows.Any(window => window.Id == id && window.UsedPercent < 100 &&
                (window.ResetsAtUtc is null || window.ResetsAtUtc > now) &&
                expected.Windows.Any(old => old.Id == id && old.WindowMinutes == window.WindowMinutes)));

    public static bool EarlyResetStillCurrent(QuotaEarlyResetAlert alert, QuotaReading expected,
        QuotaReading current, DateTimeOffset now, long continuityId) =>
        continuityId > 0 && continuityId == alert.ContinuityId && now >= alert.CheckedAtUtc &&
        now - alert.CheckedAtUtc <= TimeSpan.FromMinutes(90) &&
        expected.Windows.Select(window => (window.Id, window.WindowMinutes)).ToHashSet()
            .SetEquals(current.Windows.Select(window => (window.Id, window.WindowMinutes))) &&
        alert.Windows.All(change => current.Windows.Any(window => window.Id == change.Id &&
            window.WindowMinutes == change.WindowMinutes && window.UsedPercent < 100 &&
            window.ResetsAtUtc is { } reset && reset > now &&
            (reset - change.ResetUtc).Duration() <= TimeSpan.FromMinutes(1)));
}
