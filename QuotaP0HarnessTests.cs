using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

// The scenarios use this boundary, shared presentation, and the actual export
// bytes. P2 can replace the adapters here without changing the assertions.
internal sealed partial class QuotaP0Harness : IDisposable
{
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(8);
    internal static readonly DateTimeOffset Epoch = GeminiUsagePageTests.Now;
    internal static readonly GeminiUsagePageLoad WebOk = new(GeminiUsagePage.Url, GeminiUsagePageTests.English);
    internal static readonly GeminiUsagePageLoad WebChanged = new(GeminiUsagePage.Url, null);
    internal static readonly GeminiUsagePageLoad WebSignedOut = new("https://gemini.google.com/app", null);
    internal static readonly ClaudeAuthStatus SignedIn = new(ClaudeAuthOutcome.SignedIn, "claude.ai");
    internal static readonly string LegacyGeminiKey = "AccountQuota.v1.Gemini";
    internal static readonly string WebGeminiKey = "AccountQuota.v1.GeminiWeb";

    private readonly AccountQuotaMonitor monitor;
    private readonly UsageStore? store;
    private readonly Cli cli;
    private readonly Web web;
    private long utcTicks;

    internal QuotaP0Harness(string? database = null, bool hasWeb = false, bool enabled = false, bool hasAuth = false)
    {
        Now = Epoch;
        if (database is not null) store = new(database);
        cli = hasAuth ? new CliWithAuth(this) : new Cli(this);
        web = new(this);
        monitor = new(cli, store, () => Now, geminiWebReader: hasWeb ? web : null,
            geminiWebEnabled: enabled, geminiWebTimeZone: TimeZoneInfo.Utc);
    }

    internal DateTimeOffset Now
    {
        get => new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref utcTicks, value.UtcTicks);
    }

    internal Func<QuotaProvider, CancellationToken, Task<QuotaReading>>? CliRead { get; set; }
    internal Func<CancellationToken, Task<GeminiUsagePageLoad>> WebRead { get; set; } = _ => Task.FromResult(WebOk);
    internal Func<CancellationToken, Task<ClaudeAuthStatus>> AuthRead { get; set; } = _ => Task.FromResult(SignedIn);
    internal int CliCalls(QuotaProvider provider) => cli.Calls(provider);
    internal int WebCalls => Volatile.Read(ref web.Calls);
    internal int AuthCalls => cli is CliWithAuth auth ? Volatile.Read(ref auth.AuthCalls) : 0;
    internal ClaudeAuthCheck? Auth => monitor.LatestClaudeAuth;

    internal readonly record struct StateView(IReadOnlyList<QuotaWindow> Rows, DateTimeOffset? Attempted,
        DateTimeOffset? LastSuccess, DateTimeOffset? NextCheck, bool ReadingNow, bool Previous, long Continuity,
        bool? WebEnabled, string? WebOutcome, string CacheError, string? WebDiagnostic);

    internal StateView State(QuotaProvider provider)
    {
        var state = monitor.Snapshot().Single(item => item.Provider == provider);
        return new(state.Reading?.Windows ?? [], state.CheckedAtUtc, state.LastSuccessfulCheckUtc, state.NextCheckUtc,
            state.IsRefreshing, state.IsPrevious, state.ContinuityId, state.GeminiWeb?.IsEnabled,
            state.GeminiWeb?.Outcome?.ToString(), state.CacheError, state.GeminiWeb?.Diagnostic?.Rule.ToString());
    }

    internal QuotaSectionText Panel(QuotaProvider provider, QuotaEarlyResetAlert? earlyReset = null) =>
        QuotaPanelModel.Section(monitor.Snapshot().Single(item => item.Provider == provider), Now, earlyReset);

    internal string Layout() => QuotaPanelModel.LayoutKey(QuotaPanelModel.Sections(monitor.Snapshot(), Now));

    internal QuotaSectionText PanelWithOldGeminiReading()
    {
        var state = monitor.Snapshot().Single(item => item.Provider == QuotaProvider.Gemini);
        return QuotaPanelModel.Section(state with { Reading = Reading(QuotaProvider.Gemini) }, Now);
    }

    internal void WatchChanges(Action action) => monitor.Changed += action;
    internal void WatchSuccess(Action<QuotaObservation> action) => monitor.Succeeded += action;
    internal void Start() => monitor.Start();
    internal Task StopAsync() => monitor.StopAsync().WaitAsync(Limit);
    internal void EnableWeb(bool enabled) => monitor.SetGeminiWebEnabled(enabled);
    internal void RequestWeb() => monitor.RequestGeminiWebRefresh();
    internal void Recover() => monitor.RequestRefresh(queueWhileRefreshing: true);
    internal void RefreshManually()
    {
        monitor.RequestRefresh(queueWhileRefreshing: false);
        monitor.RequestGeminiWebRefresh();
    }
    internal Task ReadCliAsync(QuotaProvider provider, CancellationToken token = default) => monitor.RefreshOnceAsync(provider, token);
    internal Task ReadWebAsync(CancellationToken token = default) => monitor.RefreshGeminiWebIfDueAsync(token);
    internal async Task LogoutCacheAsync()
    {
        monitor.SetGeminiWebEnabled(false);
        await store!.DeleteGeminiWebQuotaAsync().ConfigureAwait(false);
    }

    internal static QuotaReading Reading(QuotaProvider provider, double used = 37, DateTimeOffset? reset = null) =>
        provider == QuotaProvider.Xai
            ? new(provider, [new("grok:weekly-credits", "주간 크레딧", used, reset, 10080)])
            : new(provider, [new("fixture:session", "5시간", used, reset, 300)]);

    internal static ClaudeAuthStatus ParseAuth(int exitCode, string json) => ClaudeAuthStatus.Parse(exitCode, json);
    internal static bool AuthDue(DateTimeOffset now, DateTimeOffset? last, bool afterFailure) =>
        ClaudeAuthCheck.IsDue(now, last, afterFailure);

    internal Task SeedCliCacheAsync(QuotaProvider provider, double used, DateTimeOffset successfulAt) =>
        store!.SaveQuotaAsync(new(1, Reading(provider, used), successfulAt, []));

    internal Task SeedWebCacheAsync(DateTimeOffset successfulAt)
    {
        var reading = GeminiUsagePage.Parse(WebOk, Epoch, TimeZoneInfo.Utc).Reading!;
        return store!.SaveGeminiWebQuotaAsync(new(1, reading, successfulAt, []));
    }

    internal string? CacheJson(string key)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = store!.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppMetadata WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    internal string LogText(string file)
    {
        string path = Path.Combine(Path.GetDirectoryName(store!.DatabasePath)!, "logs", file);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    internal string LastPollingReason(QuotaProvider provider)
    {
        using var doc = JsonDocument.Parse(LogText("quota-polling-diagnostics-v2.json"));
        return doc.RootElement.GetProperty("Entries").EnumerateArray()
            .Last(item => item.GetProperty("Provider").GetString() == provider.ToString()).GetProperty("Reason").GetString()!;
    }

    internal async Task<IDisposable> PauseCacheAsync()
    {
        await store!.InitializeAsync().ConfigureAwait(false);
        // The existing store gate lets the test stop a real persistence request
        // at a deterministic boundary with no change to the production API.
        var gate = (SemaphoreSlim)typeof(UsageStore).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        if (!await gate.WaitAsync(Limit).ConfigureAwait(false)) throw new TimeoutException("Test cache gate is busy.");
        return new CachePause(gate);
    }

    internal static Task WaitAsync(Func<bool> predicate) => MonitorTests.WaitUntilAsync(predicate, (int)Limit.TotalMilliseconds);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static async Task<bool> CancelledAsync(Task task)
    {
        try { await task.WaitAsync(Limit).ConfigureAwait(false); return false; }
        catch (OperationCanceledException) { return true; }
    }

    public void Dispose() => monitor.Dispose();

    private sealed class CachePause(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? held = gate;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
    }

    private class Cli(QuotaP0Harness owner) : IAccountQuotaClient
    {
        protected QuotaP0Harness Owner { get; } = owner;
        private readonly ConcurrentDictionary<QuotaProvider, int> calls = new();
        internal int Calls(QuotaProvider p) => calls.GetValueOrDefault(p);
        public Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken token)
        {
            calls.AddOrUpdate(provider, 1, (_, count) => count + 1);
            return Owner.CliRead?.Invoke(provider, token) ?? Task.FromResult(Reading(provider));
        }
    }

    private sealed class CliWithAuth(QuotaP0Harness owner) : Cli(owner), IClaudeAuthClient
    {
        internal int AuthCalls;
        public Task<ClaudeAuthStatus> ReadClaudeAuthAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref AuthCalls);
            return Owner.AuthRead(cancellationToken);
        }
    }

    private sealed class Web(QuotaP0Harness owner) : IGeminiUsagePageReader
    {
        internal int Calls;
        public Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return owner.WebRead(cancellationToken);
        }
    }
}
