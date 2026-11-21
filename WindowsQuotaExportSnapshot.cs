using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiBurgerClock;

// Windows wire contract only. Never serialize QuotaState/QuotaReading themselves:
// their errors, cache diagnostics and future account fields are not export fields.
internal sealed record WindowsQuotaExportRow(string Id, string Label, double UsedPercent,
    DateTimeOffset? ResetsAtUtc, int? WindowMinutes, bool IsPrevious);
internal sealed record WindowsQuotaExportProvider(string Provider, string Reading,
    DateTimeOffset? LastSuccessUtc, DateTimeOffset? NextCheckUtc, WindowsQuotaExportRow[] Rows,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WebState = null);
internal sealed record WindowsQuotaExportEnvelope(string Schema, int SchemaVersion, string Type,
    string SourceId, long Seq, string AppVersion, DateTimeOffset GeneratedAtUtc,
    int HeartbeatSeconds, WindowsQuotaExportProvider[] Providers);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WindowsQuotaExportEnvelope))]
[JsonSerializable(typeof(WindowsQuotaExportSettings))]
internal partial class WindowsQuotaExportJson : JsonSerializerContext;

internal static class WindowsQuotaExportSnapshot
{
    internal const int MaximumBytes = 1024 * 1024;

    internal static byte[] Serialize(IReadOnlyList<QuotaState> states, WindowsQuotaExportSettings settings,
        DateTimeOffset generatedAtUtc, string appVersion)
    {
        static bool Text(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(c => c < 32 || c == 127)) return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsHighSurrogate(value[i])) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false; }
                else if (char.IsLowSurrogate(value[i])) return false;
            return true;
        }
        if (states.Count != 4 || !Guid.TryParseExact(settings.SourceId, "D", out _) || settings.Seq < 1 ||
            settings.Seq > WindowsQuotaExportSettingsStore.MaximumSequence || !Text(appVersion, 64))
            throw new InvalidDataException("Invalid quota export envelope.");
        var providers = Enum.GetValues<QuotaProvider>().Select(provider =>
        {
            var state = states.Single(s => s.Provider == provider);
            if (provider == QuotaProvider.Gemini) return Gemini(state, generatedAtUtc);
            bool none = state.Reading is null;
            if (!none && (state.LastSuccessfulCheckUtc is null || state.Reading!.Windows.Count is < 1 or > 64 ||
                state.Reading.Windows.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() != state.Reading.Windows.Count ||
                state.Reading.Windows.Any(w => !Text(w.Id, 512) || !Text(w.Label, 80) || !double.IsFinite(w.UsedPercent) ||
                    w.UsedPercent is < 0 or > 100 || w.WindowMinutes is <= 0 or > 5_256_000)))
                throw new InvalidDataException("Invalid quota export rows.");
            var rows = Rows(state.Reading, state.IsPrevious, generatedAtUtc);
            return new WindowsQuotaExportProvider(provider.ToString(), none ? "none" : state.IsPrevious ? "previous" : "ok",
                none ? null : state.LastSuccessfulCheckUtc?.ToUniversalTime(), state.NextCheckUtc?.ToUniversalTime(), rows);
        }).ToArray();
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new WindowsQuotaExportEnvelope(
            "aiburgerclock.export", 2, "quota", settings.SourceId, settings.Seq, appVersion,
            generatedAtUtc.ToUniversalTime(), 900, providers), WindowsQuotaExportJson.Default.WindowsQuotaExportEnvelope);
        if (body.Length > MaximumBytes) throw new InvalidDataException("Quota export exceeds its size limit.");
        return body;
    }

    internal static WindowsQuotaExportProvider Gemini(QuotaState state, DateTimeOffset? generatedAtUtc = null)
    {
        string? webState = state.GeminiWeb switch
        {
            { IsEnabled: false } => "off",
            { Outcome: GeminiUsageOutcome.LoginRequired } => "loginRequired",
            { Outcome: GeminiUsageOutcome.FormatChanged } => "formatChanged",
            { Outcome: GeminiUsageOutcome.LoadFailed } => "loadFailed",
            { Outcome: GeminiUsageOutcome.Ok } => "ok",
            _ => null
        };
        bool exposesReading = webState is "ok" or "formatChanged" or "loadFailed";
        bool hasRows = exposesReading && state.LastSuccessfulCheckUtc is not null &&
            GeminiUsagePage.IsWebReading(state.Reading);
        bool previous = state.IsPrevious || webState is "formatChanged" or "loadFailed";
        return new(nameof(QuotaProvider.Gemini), hasRows ? previous ? "previous" : "ok" : "none",
            hasRows ? state.LastSuccessfulCheckUtc?.ToUniversalTime() : null,
            exposesReading ? state.NextCheckUtc?.ToUniversalTime() : null,
            hasRows ? Rows(state.Reading, previous, generatedAtUtc) : [], webState);
    }

    private static WindowsQuotaExportRow[] Rows(QuotaReading? reading, bool previous, DateTimeOffset? generatedAtUtc) =>
        reading?.Windows.Select(window => new WindowsQuotaExportRow(
            window.Id, window.Label, window.UsedPercent, window.ResetsAtUtc?.ToUniversalTime(),
            window.WindowMinutes, previous || window.ResetsAtUtc <= generatedAtUtc)).ToArray() ?? [];
}

// A monitor invokes Changed synchronously at refresh start and at completion.
// Each Provider's runner awaits that callback before beginning its next read.
// Capture independently of the coalesced UI callback; no network/file/UI work here.
internal sealed class WindowsQuotaExportBridge : IDisposable
{
    private readonly object sync = new();
    private readonly AccountQuotaMonitor monitor;
    private readonly Action<IReadOnlyList<QuotaState>> completed;
    private readonly HashSet<QuotaProvider> started = new();
    private WindowsQuotaExportProvider lastGemini;
    private bool disposed;

    internal WindowsQuotaExportBridge(AccountQuotaMonitor monitor, Action<IReadOnlyList<QuotaState>> completed)
    {
        this.monitor = monitor;
        this.completed = completed;
        lastGemini = WindowsQuotaExportSnapshot.Gemini(monitor.Snapshot().Single(s => s.Provider == QuotaProvider.Gemini));
        monitor.Changed += OnChanged;
    }

    private void OnChanged()
    {
        lock (sync)
        {
            if (disposed) return;
            var states = monitor.Snapshot();
            var geminiState = states.Single(s => s.Provider == QuotaProvider.Gemini);
            var gemini = WindowsQuotaExportSnapshot.Gemini(geminiState);
            // A pending read or a new polling timestamp alone does not trigger a send.
            bool geminiChanged = !geminiState.IsRefreshing && gemini.WebState is not null &&
                (gemini.WebState != lastGemini.WebState || gemini.Reading != lastGemini.Reading ||
                 !gemini.Rows.SequenceEqual(lastGemini.Rows));
            bool otherCompleted = false;
            foreach (var state in states)
            {
                if (state.Provider == QuotaProvider.Gemini) continue;
                if (state.IsRefreshing) started.Add(state.Provider);
                else if (started.Remove(state.Provider) && state.CheckedAtUtc is not null && state.NextCheckUtc is not null)
                {
                    otherCompleted = true;
                    completed(states); // Includes failed reads; excludes cache load, start and cancellation without a result.
                }
            }
            if (geminiChanged || otherCompleted) lastGemini = gemini;
            if (geminiChanged && !otherCompleted) completed(states);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            monitor.Changed -= OnChanged;
            started.Clear();
        }
    }
}
