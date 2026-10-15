namespace AiBurgerClock;

// Only a newly verified live response may create an observation. Cache loads, refresh-start
// events and failures are not observations; Sequence increases independently per provider.
internal sealed record QuotaObservation(QuotaProvider Provider, long Sequence,
    DateTimeOffset CheckedAtUtc, QuotaReading Reading);

internal sealed record QuotaRecoveryAlert(QuotaProvider Provider, long Sequence,
    string Title, string Body, IReadOnlyList<string> WindowIds);

internal static class QuotaRecoveryText
{
    public const string SettingKey = "QuotaRecoveryNotificationsEnabled";
    public const string MenuLabel = "한도 회복 알림";
    public const string MenuDetail = "기본 꺼짐 · 새 성공 조회에서 소진한 한도의 회복을 확인하면 알림 · 추가 조회 없음";
    public const string SettingReadFailed = "한도 회복 알림 설정을 읽지 못했습니다. 알림은 꺼짐으로 유지합니다.";
    public const string SettingSaveFailed = "한도 회복 알림 설정을 저장하지 못했습니다. 기존 설정을 유지합니다.";
    public const string DeliveryFailed = "한도 회복 알림을 표시하지 못했습니다. OS 알림 설정을 확인하세요.";

    public static string Saved(bool enabled) => enabled
        ? "한도 회복 알림을 켰습니다. 다음 성공 조회부터 기준을 설정합니다."
        : "한도 회복 알림을 껐습니다.";

    public static string Title(QuotaProvider provider) => (provider switch
    {
        QuotaProvider.Codex => "ChatGPT (Work/Codex)",
        QuotaProvider.Gemini => "Gemini (Antigravity)",
        _ => ProviderNames.Quota(provider)
    }) + " 계정 한도 회복 확인";

    public static string Body(IReadOnlyList<QuotaWindow> recovered, bool otherExhausted)
    {
        // These are the already displayed row/model labels, never account metadata, values,
        // reset timestamps or internal IDs. Bound their size for native notification limits.
        string[] labels = recovered.Select(window => SafeLabel(window.Label)).Distinct(StringComparer.Ordinal).ToArray();
        string subject = string.Join(", ", labels.Take(3));
        if (labels.Length > 3) subject += " 외 " + (labels.Length - 3) + "개";
        string remaining = otherExhausted ? " 다른 한도는 아직 소진 상태로 남아 있을 수 있습니다." : "";
        return subject + " 한도에 잔여량이 확인되었습니다." + remaining +
            " 다른 한도와 서비스 상태에 따라 실제 사용은 제한될 수 있습니다.";
    }

    private static string SafeLabel(string label)
    {
        string safe = new(label.Where(character => !char.IsControl(character)).Take(40).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "확인한" : safe.Trim();
    }
}

// Pure decision state, owned by one host UI thread. It never polls, writes files, emits a
// notification or retains account values. Restarting/toggling resets the in-memory baseline.
internal sealed class QuotaRecoveryNotifications
{
    private readonly record struct WindowKey(string Id, int? Minutes);
    private sealed record Baseline(long Sequence, DateTimeOffset CheckedAtUtc,
        IReadOnlyDictionary<WindowKey, bool> Exhausted);
    private readonly Dictionary<QuotaProvider, Baseline> baselines = new();

    public void Reset() => baselines.Clear();

    public QuotaRecoveryAlert? Observe(QuotaObservation observation, bool enabled)
    {
        if (!Valid(observation)) return null;
        baselines.TryGetValue(observation.Provider, out Baseline? previous);
        if (previous is not null && observation.Sequence <= previous.Sequence) return null;

        // A clock correction is not evidence of a chronological recovery. Accept the new
        // sequence, but seed silently, just as after process start or an explicit Reset.
        bool compare = enabled && previous is not null && observation.CheckedAtUtc >= previous.CheckedAtUtc;
        var current = new Dictionary<WindowKey, bool>();
        var recovered = new List<QuotaWindow>();
        foreach (QuotaWindow window in observation.Reading.Windows)
        {
            // A successful response can still contain an expired row. Do not use it as a
            // new recovery or leave an exhausted baseline that a later row could recover.
            if (window.ResetsAtUtc is { } reset && reset <= observation.CheckedAtUtc) continue;
            var key = new WindowKey(window.Id, window.WindowMinutes);
            current.Add(key, window.UsedPercent == 100);
            if (compare && window.UsedPercent < 100 && previous!.Exhausted.TryGetValue(key, out bool exhausted) && exhausted)
                recovered.Add(window);
        }
        baselines[observation.Provider] = new(observation.Sequence, observation.CheckedAtUtc, current);
        if (recovered.Count == 0) return null;
        return new(observation.Provider, observation.Sequence, QuotaRecoveryText.Title(observation.Provider),
            QuotaRecoveryText.Body(recovered, current.Values.Any(exhausted => exhausted)),
            Array.AsReadOnly(recovered.Select(window => window.Id).ToArray()));
    }

    private static bool Valid(QuotaObservation? observation)
    {
        if (observation is null || !Enum.IsDefined(observation.Provider) || observation.Sequence <= 0 ||
            observation.CheckedAtUtc < DateTimeOffset.UnixEpoch || observation.Reading is null ||
            observation.Reading.Provider != observation.Provider || observation.Reading.Windows is not { Count: > 0 and <= 64 })
            return false;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (QuotaWindow? window in observation.Reading.Windows)
            if (window is null || string.IsNullOrWhiteSpace(window.Id) || window.Id.Length > 512 ||
                string.IsNullOrWhiteSpace(window.Label) || window.Label.Length > 180 || !ids.Add(window.Id) ||
                !double.IsFinite(window.UsedPercent) || window.UsedPercent is < 0 or > 100 || window.WindowMinutes is <= 0 ||
                window.ResetsAtUtc is { } reset && (reset.Year < 1970 || reset.Year > 9000))
                return false;
        return true;
    }
}
