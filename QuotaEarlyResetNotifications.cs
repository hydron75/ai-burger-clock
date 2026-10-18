using System.Globalization;

namespace AiBurgerClock;

internal sealed record QuotaEarlyResetWindow(string Id, string Label, int WindowMinutes,
    double PreviousUsedPercent, double UsedPercent, DateTimeOffset PreviousResetUtc, DateTimeOffset ResetUtc);

internal sealed record QuotaEarlyResetAlert(QuotaProvider Provider, long Sequence, long ContinuityId,
    DateTimeOffset CheckedAtUtc, IReadOnlyList<QuotaEarlyResetWindow> Windows);

// Pure, session-only comparison state, owned by one host UI thread. No polling, storage,
// notification delivery, account identity or reset-cause inference takes place here.
internal sealed class QuotaEarlyResetNotifications
{
    private sealed record Baseline(long Sequence, long ContinuityId, DateTimeOffset CheckedAtUtc,
        bool Armed, IReadOnlyDictionary<string, QuotaWindow> Windows);

    private readonly Dictionary<QuotaProvider, Baseline> baselines = new();
    private readonly Dictionary<QuotaProvider, QuotaEarlyResetAlert> recent = new();

    // Toggling a setting invalidates comparison baselines, not already observed session history.
    // Constructing a fresh engine (application restart) clears both; nothing is persisted.
    public void Reset() => baselines.Clear();

    public QuotaEarlyResetAlert? Recent(QuotaProvider provider) =>
        recent.TryGetValue(provider, out QuotaEarlyResetAlert? alert) ? alert : null;

    public QuotaEarlyResetAlert? Observe(QuotaObservation observation, bool enabled)
    {
        if (!Valid(observation)) return null;
        baselines.TryGetValue(observation.Provider, out Baseline? previous);
        if (previous is not null && observation.Sequence <= previous.Sequence) return null;
        // A legacy/cache/orphan observation has no proof that failed attempts did not occur.
        // In particular it must not replace a newer monitor's verified continuity baseline.
        if (observation.ContinuityId == 0 && previous is { ContinuityId: > 0 }) return null;

        var windows = observation.Reading.Windows.ToDictionary(window => window.Id, StringComparer.Ordinal);
        TimeSpan gap = previous is null ? TimeSpan.Zero : observation.CheckedAtUtc - previous.CheckedAtUtc;
        bool compare = enabled && previous is { Armed: true, ContinuityId: > 0 } &&
            observation.ContinuityId == previous.ContinuityId && previous.Sequence != long.MaxValue &&
            observation.Sequence == previous.Sequence + 1 && gap > TimeSpan.Zero && gap <= TimeSpan.FromMinutes(90) &&
            SameWindows(previous.Windows, windows);

        // Immutable QuotaWindow records are copied out of the caller's mutable collection.
        baselines[observation.Provider] = new(observation.Sequence, observation.ContinuityId,
            observation.CheckedAtUtc, enabled && observation.ContinuityId > 0, windows);
        if (!compare) return null;

        var changed = new List<QuotaEarlyResetWindow>();
        foreach (QuotaWindow window in observation.Reading.Windows)
        {
            QuotaWindow old = previous!.Windows[window.Id];
            if (window.WindowMinutes is not { } minutes ||
                old.ResetsAtUtc is not { } previousReset || window.ResetsAtUtc is not { } reset ||
                old.UsedPercent - window.UsedPercent < 20 || window.UsedPercent > 10 ||
                previousReset - observation.CheckedAtUtc <= TimeSpan.FromMinutes(5) ||
                (reset - previousReset).Duration() <= TimeSpan.FromMinutes(1) ||
                !WithinWindow(previousReset, previous.CheckedAtUtc, minutes) ||
                !WithinWindow(reset, observation.CheckedAtUtc, minutes))
                continue;
            changed.Add(new(window.Id, window.Label, minutes, old.UsedPercent, window.UsedPercent, previousReset, reset));
        }
        if (changed.Count == 0) return null;
        var alert = new QuotaEarlyResetAlert(observation.Provider, observation.Sequence, observation.ContinuityId,
            observation.CheckedAtUtc, Array.AsReadOnly(changed.ToArray()));
        recent[observation.Provider] = alert;
        return alert;
    }

    private static bool SameWindows(IReadOnlyDictionary<string, QuotaWindow> previous,
        IReadOnlyDictionary<string, QuotaWindow> current) => previous.Count == current.Count &&
        current.All(pair => previous.TryGetValue(pair.Key, out QuotaWindow? old) &&
            old.WindowMinutes == pair.Value.WindowMinutes);

    private static bool WithinWindow(DateTimeOffset reset, DateTimeOffset receipt, int minutes)
    {
        TimeSpan remaining = reset - receipt;
        return remaining > TimeSpan.Zero && remaining <= TimeSpan.FromMinutes(minutes);
    }

    private static bool Valid(QuotaObservation? observation)
    {
        if (observation is null || !Enum.IsDefined(observation.Provider) || observation.Sequence <= 0 ||
            observation.ContinuityId < 0 || observation.CheckedAtUtc < DateTimeOffset.UnixEpoch ||
            observation.CheckedAtUtc.Year > 9000 || observation.Reading is null ||
            observation.Reading.Provider != observation.Provider || observation.Reading.Windows is not { Count: > 0 and <= 64 })
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (QuotaWindow? window in observation.Reading.Windows)
            if (window is null || string.IsNullOrWhiteSpace(window.Id) || window.Id.Length > 512 ||
                string.IsNullOrWhiteSpace(window.Label) || window.Label.Length > 180 || !ids.Add(window.Id) ||
                !double.IsFinite(window.UsedPercent) || window.UsedPercent is < 0 or > 100 ||
                window.WindowMinutes is <= 0 or > 5_256_000 ||
                window.ResetsAtUtc is { } reset && (reset.Year < 1970 || reset.Year > 9000))
                return false;
        return true;
    }
}

internal static class QuotaEarlyResetText
{
    public const string SettingKey = "QuotaEarlyResetNotificationsEnabled";
    public const string MenuLabel = "한도 조기 초기화 알림";
    public const string MenuDetail = "기본 꺼짐 · 연속 성공 조회에서 예정 전 사용량 감소·리셋 변경을 확인하면 알림 · 추가 조회 없음";
    public const string SettingReadFailed = "한도 조기 초기화 알림 설정을 읽지 못했습니다. 알림은 꺼짐으로 유지합니다.";
    public const string SettingSaveFailed = "한도 조기 초기화 알림 설정을 저장하지 못했습니다. 기존 설정을 유지합니다.";
    public const string DeliveryFailed = "한도 조기 초기화 알림을 표시하지 못했습니다. OS 알림 설정을 확인하세요.";

    public static string Saved(bool enabled) => enabled
        ? "한도 조기 초기화 알림을 켰습니다. 다음 성공 조회부터 기준을 설정합니다."
        : "한도 조기 초기화 알림을 껐습니다.";

    public static string Title(QuotaProvider provider) => (provider switch
    {
        QuotaProvider.Codex => "ChatGPT (Work/Codex)",
        QuotaProvider.Gemini => "Gemini (Antigravity)",
        _ => ProviderNames.Quota(provider)
    }) + " 한도 조기 초기화 가능성";

    public static string Body(QuotaEarlyResetAlert alert)
    {
        // Only bounded, already displayed row labels belong in an OS banner. Percentages,
        // reset/receipt timestamps and internal identities stay in the local Provider detail.
        string[] labels = alert.Windows.Select(window => SafeLabel(window.Label)).Distinct(StringComparer.Ordinal).ToArray();
        string subject = string.Join(", ", labels.Take(2));
        if (labels.Length > 2) subject += " 외 " + (labels.Length - 2).ToString(CultureInfo.InvariantCulture) + "개";
        if (subject.Length == 0) subject = "확인한";
        return subject + " 한도가 예정보다 일찍 초기화된 것으로 보입니다." +
            " 초기화 원인은 확인되지 않았습니다. 다른 한도와 서비스 상태에 따라 실제 사용은 제한될 수 있습니다.";
    }

    public static string Detail(QuotaEarlyResetAlert alert, DateTimeOffset now)
    {
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        static string At(DateTimeOffset value) => AgentSchedule.ToKst(value)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " KST";
        var lines = new List<string>
        {
            "최근 조기 초기화 가능성 (이번 실행 중 확인)",
            ConfirmationAge(alert.CheckedAtUtc, now),
            "확인(조회 응답 수신): " + At(alert.CheckedAtUtc),
            "현재 한도 값이 아닌 당시의 확인 기록입니다."
        };
        foreach (QuotaEarlyResetWindow window in alert.Windows.Take(64))
        {
            lines.Add(SafeLabel(window.Label) + ": 사용 " + Number(window.PreviousUsedPercent) + "% → " + Number(window.UsedPercent) + "%");
            lines.Add("이전 리셋 예정: " + At(window.PreviousResetUtc));
            lines.Add("다음 리셋 예정: " + At(window.ResetUtc));
        }
        lines.Add("예정보다 일찍 초기화된 것으로 보입니다. 실제 초기화 시각·원인이나 전체 계정의 초기화를 확인한 것은 아닙니다.");
        return string.Join("\n", lines);
    }

    private static string ConfirmationAge(DateTimeOffset confirmedAtUtc, DateTimeOffset now)
    {
        if (confirmedAtUtc > now) return "확인 시각 확인 필요";
        TimeSpan age = now - confirmedAtUtc;
        string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        if (age < TimeSpan.FromMinutes(1)) return "방금 전 확인";
        if (age < TimeSpan.FromHours(1)) return "확인 " + Number(age.Minutes) + "분 전";
        if (age < TimeSpan.FromDays(1))
            return "확인 " + Number(age.Hours) + "시간" + (age.Minutes > 0 ? " " + Number(age.Minutes) + "분" : "") + " 전";
        return "확인 " + Number(age.Days) + "일" + (age.Hours > 0 ? " " + Number(age.Hours) + "시간" : "") + " 전";
    }

    private static string SafeLabel(string label)
    {
        string safe = new(label.Where(character => !char.IsControl(character)).Take(40).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "확인한" : safe.Trim();
    }
}

internal sealed record QuotaNotificationAlert(QuotaProvider Provider, long Sequence, string Title, string Body,
    QuotaRecoveryAlert? Recovery, QuotaEarlyResetAlert? EarlyReset);

// Each accepted Provider response becomes at most one OS notification. Legacy recovery-only
// text remains byte-for-byte unchanged. A mismatched pair is invalid, never cross-combined.
internal static class QuotaNotificationText
{
    public static QuotaNotificationAlert? Combine(QuotaRecoveryAlert? recovery, QuotaEarlyResetAlert? earlyReset)
    {
        if (earlyReset is null)
            return recovery is null ? null : new(recovery.Provider, recovery.Sequence, recovery.Title, recovery.Body, recovery, null);
        if (recovery is not null && (recovery.Provider != earlyReset.Provider || recovery.Sequence != earlyReset.Sequence))
            return null;
        string body = QuotaEarlyResetText.Body(earlyReset);
        if (recovery is not null)
        {
            var earlyIds = earlyReset.Windows.Select(window => window.Id).ToHashSet(StringComparer.Ordinal);
            if (recovery.WindowIds.Any(id => !earlyIds.Contains(id)))
                body += " 다른 한도에도 잔여량이 확인되었습니다.";
        }
        return new(earlyReset.Provider, earlyReset.Sequence, QuotaEarlyResetText.Title(earlyReset.Provider), body, recovery, earlyReset);
    }
}
