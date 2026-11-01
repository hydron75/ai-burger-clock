using System.Net;
using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

// Windows-only synthetic checks: intentionally not *Tests.cs, so Shared.Tests and Mac do not compile them.
internal static class WindowsQuotaExportChecks
{
    internal sealed class Clock : TimeProvider
    {
        private long milliseconds;
        internal readonly DateTimeOffset Origin = new(2026, 1, 5, 3, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref milliseconds);
        public override DateTimeOffset GetUtcNow() => Origin.AddMilliseconds(GetTimestamp());
        internal void Advance(TimeSpan duration) => Interlocked.Add(ref milliseconds, (long)duration.TotalMilliseconds);
    }

    internal sealed class Credentials : IWindowsQuotaExportCredentials
    {
        internal const string SyntheticToken = "SYNTHETIC-SERVICE-TOKEN-NOT-A-CREDENTIAL";
        internal string Value = SyntheticToken;
        internal int Reads, Writes, Checks;
        internal bool Fail;
        public bool Exists() { Checks++; return Value.Length > 0; }
        public string Read() { Interlocked.Increment(ref Reads); if (Fail) throw new IOException(SyntheticToken); return Value; }
        public void Replace(string token) { Writes++; if (Fail) throw new IOException(SyntheticToken); Value = token; }
    }

    internal sealed record Request(Uri Uri, byte[] Body, string Header);
    internal sealed class Handler : HttpMessageHandler
    {
        private readonly object sync = new();
        internal readonly List<Request> Requests = new();
        internal Func<int, CancellationToken, Task<HttpResponseMessage>> Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        internal int Count { get { lock (sync) return Requests.Count; } }
        internal Request Last { get { lock (sync) return Requests[^1]; } }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new Request(request.RequestUri!, await request.Content!.ReadAsByteArrayAsync(cancellationToken),
                request.Headers.GetValues("OAI-Sites-Authorization").Single());
            int index; lock (sync) { index = Requests.Count; Requests.Add(captured); }
            if (captured.Uri.Host != "quota.example.test") throw new InvalidOperationException("Only the synthetic host is permitted.");
            return await Reply(index, cancellationToken);
        }
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly string DirectoryPath = Directory.CreateTempSubdirectory("AiBurgerClock-export-tests-").FullName;
        internal string SettingsPath => Path.Combine(DirectoryPath, WindowsQuotaExportSettingsStore.FileName);
        internal readonly Clock Time = new();
        internal readonly Credentials Vault = new();
        internal readonly Handler Http = new();
        internal WindowsQuotaExportSettingsStore Store { get; }
        internal WindowsQuotaExportService Service { get; }
        internal IReadOnlyList<QuotaState> States;
        internal Fixture(bool enabled = true)
        {
            States = Readings(Time.GetUtcNow());
            Store = new(SettingsPath);
            if (enabled) Store.Update(true, "https://quota.example.test");
            Service = new(Store, Vault, new WindowsQuotaExportTransport(Vault, Http), () => States, "0.0.0-synthetic", Time);
        }
        public void Dispose()
        {
            Service.Dispose();
            string resolved = Path.GetFullPath(DirectoryPath);
            if (Path.GetDirectoryName(resolved) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(resolved).StartsWith("AiBurgerClock-export-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Temporary cleanup boundary mismatch.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    internal static IReadOnlyList<QuotaState> Readings(DateTimeOffset now, double used = 37.5) =>
        Enum.GetValues<QuotaProvider>().Select(provider => new QuotaState(provider,
            new(provider, [new("synthetic-weekly", "주간 합성 모델 👩🏽‍💻", used, now.AddDays(2), 10080)]),
            now, now, now.AddHours(1), Error: "SYNTHETIC-PRIVATE-ERROR", CacheError: "SYNTHETIC-PRIVATE-CACHE", ContinuityId: 1234)).ToArray();

    private sealed class Client(Clock clock) : IAccountQuotaClient
    {
        internal bool Fail;
        internal TaskCompletionSource? Gate;
        public async Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
        {
            if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
            if (Fail) throw new InvalidDataException(Credentials.SyntheticToken);
            return Readings(clock.GetUtcNow()).Single(s => s.Provider == provider).Reading!;
        }
    }

    private sealed class UnreadContent : HttpContent
    {
        internal int Reads;
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) { Reads++; throw new IOException("Response body must not be read"); }
        protected override bool TryComputeLength(out long length) { length = int.MaxValue; return true; }
    }

    private sealed class IgnoringCancellationTransport : IWindowsQuotaExportTransport
    {
        internal readonly TaskCompletionSource<WindowsQuotaExportResult> First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<WindowsQuotaExportResult> Second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<(Uri Uri, byte[] Body, CancellationToken Token)> Requests = new();
        internal int Count { get { lock (Requests) return Requests.Count; } }
        public Task<WindowsQuotaExportResult> SendAsync(Uri endpoint, byte[] body, CancellationToken cancellationToken)
        {
            int index; lock (Requests) { index = Requests.Count; Requests.Add((endpoint, body, cancellationToken)); }
            return index == 0 ? First.Task : Second.Task; // Intentionally ignore cancellation to exercise the epoch guard.
        }
        public void Dispose() { First.TrySetResult(new(false, false, WindowsQuotaExportFailure.Network)); Second.TrySetResult(new(false, false, WindowsQuotaExportFailure.Network)); }
    }

    private sealed class FailingSettings(IWindowsQuotaExportSettingsStore inner) : IWindowsQuotaExportSettingsStore
    {
        private int updates;
        public WindowsQuotaExportSettings Load() => inner.Load();
        public WindowsQuotaExportSettings ReserveSequence() => inner.ReserveSequence();
        public WindowsQuotaExportSettings Update(bool enabled, string siteUrl) => ++updates == 2
            ? throw new IOException("Synthetic final settings write failure") : inner.Update(enabled, siteUrl);
    }

    internal static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string name) { count++; if (!condition) throw new InvalidOperationException("Mobile export: " + name); }
        static JsonDocument Body(Request request) => JsonDocument.Parse(request.Body);
        static long Seq(Request request) { using var doc = Body(request); return doc.RootElement.GetProperty("seq").GetInt64(); }
        static async Task Wait(Func<bool> condition)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!condition()) await Task.Delay(1, deadline.Token);
        }

        using (var f = new Fixture(false))
        {
            await f.Service.Initialization;
            Check(!f.Service.Status.Enabled && !File.Exists(f.SettingsPath), "missing settings default OFF without creating a file");
            f.Service.Completed(f.States); f.Time.Advance(TimeSpan.FromDays(1)); f.Service.Tick();
            await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 0 && f.Vault.Reads == 0, "OFF produces no automatic request or credential read");
            Check(await f.Service.SaveAsync(false, "https://quota.example.test", null), "save while OFF");
            Check(f.Http.Count == 0, "saving does not send");
            Check(await f.Service.TestAsync(), "explicit OFF test queued"); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 1 && !f.Service.Status.Enabled, "OFF test is one explicit request and leaves OFF");
            Check(f.Vault.Reads == 1 && f.Http.Last.Header == "Bearer " + Credentials.SyntheticToken, "credential read only at dispatch");
            Check(!File.ReadAllText(f.SettingsPath).Contains(Credentials.SyntheticToken), "settings have no token");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            Check(f.Http.Count == 0, "enabled startup itself sends nothing");
            f.Service.Completed(f.States); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 1 && f.Service.Status.LastSuccessUtc == f.Time.GetUtcNow(), "successful completed read is sent");
            using var doc = Body(f.Http.Last); var root = doc.RootElement;
            Check(root.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "schema", "schemaVersion", "type", "sourceId", "seq", "appVersion", "generatedAtUtc", "heartbeatSeconds", "providers" }), "envelope allowlist");
            Check(root.GetProperty("schema").GetString() == "aiburgerclock.export" && root.GetProperty("heartbeatSeconds").GetInt32() == 900, "schema and heartbeat contract");
            Check(root.GetProperty("providers").EnumerateArray().Select(p => p.GetProperty("provider").GetString()).SequenceEqual(new[] { "Codex", "Claude", "Gemini" }), "exact Provider order");
            foreach (var provider in root.GetProperty("providers").EnumerateArray())
            {
                Check(provider.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "provider", "reading", "lastSuccessUtc", "nextCheckUtc", "rows" }), "Provider allowlist");
                Check(provider.GetProperty("rows")[0].EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "id", "label", "usedPercent", "resetsAtUtc", "windowMinutes", "isPrevious" }), "row allowlist");
            }
            string json = Encoding.UTF8.GetString(f.Http.Last.Body);
            foreach (string forbidden in new[] { Credentials.SyntheticToken, "SYNTHETIC-PRIVATE", "continuityId", "accountId", "email", "plan", "error", "cliOutput", "token" })
                Check(!json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), "forbidden field/value absent");
            Check(f.Http.Last.Uri.AbsolutePath == "/api/quota" && f.Http.Last.Uri.Scheme == "https", "fixed API suffix");
            long first = Seq(f.Http.Last);
            f.States = f.States.Select(s => s with { IsPrevious = true }).ToArray();
            f.Service.Completed(f.States); await f.Service.WaitForIdleAsync();
            using var previous = Body(f.Http.Last);
            Check(previous.RootElement.GetProperty("providers").EnumerateArray().All(p => p.GetProperty("reading").GetString() == "previous"), "failed read keeps previous readings");
            Check(Seq(f.Http.Last) == first + 1, "new snapshot reserves a greater sequence");
            f.States = Enum.GetValues<QuotaProvider>().Select(p => new QuotaState(p, Error: Credentials.SyntheticToken)).ToArray();
            f.Service.Completed(f.States); await f.Service.WaitForIdleAsync();
            using var none = Body(f.Http.Last);
            Check(none.RootElement.GetProperty("providers").EnumerateArray().All(p => p.GetProperty("reading").GetString() == "none" && p.GetProperty("rows").GetArrayLength() == 0 && p.GetProperty("lastSuccessUtc").ValueKind == JsonValueKind.Null), "failure without history has none, not invented zeros");
            int before = f.Http.Count;
            f.Time.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromMilliseconds(1)); f.Service.Tick(); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == before, "heartbeat not early");
            f.Time.Advance(TimeSpan.FromMilliseconds(1)); f.Service.Tick(); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == before + 1, "heartbeat sends without a quota change");
            f.Time.Advance(TimeSpan.FromHours(9)); f.Service.Tick(); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == before + 2, "resume catches up only once");
            var reloaded = new WindowsQuotaExportSettingsStore(f.SettingsPath).Load();
            Check(reloaded.SourceId == f.Store.Load().SourceId && reloaded.Seq == Seq(f.Http.Last), "source and reserved seq survive restart");
            Check(new WindowsQuotaExportSettingsStore(f.SettingsPath).ReserveSequence().Seq == reloaded.Seq + 1, "restart never reuses an attempted seq");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            f.Http.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            f.Service.Completed(f.States); await f.Service.WaitForIdleAsync();
            byte[] original = f.Http.Last.Body;
            foreach (int delay in new[] { 30, 120, 300, 900, 900 })
            {
                Check(f.Service.Status.RetryAtUtc == f.Time.GetUtcNow().AddSeconds(delay), "exact retry schedule and cap");
                int before = f.Http.Count;
                f.Time.Advance(TimeSpan.FromSeconds(delay) - TimeSpan.FromMilliseconds(1)); f.Service.Tick(); await f.Service.WaitForIdleAsync();
                Check(f.Http.Count == before, "retry not before deadline");
                f.Time.Advance(TimeSpan.FromMilliseconds(1)); f.Service.Tick(); await f.Service.WaitForIdleAsync();
                Check(f.Http.Count == before + 1, "one request at retry deadline");
            }
            // A due heartbeat may supersede the failed packet, but never sends before the retry deadline.
            Check(Seq(f.Http.Last) >= Seq(f.Http.Requests[0]), "retry/heartbeat sequence does not regress");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            f.Http.Reply = (index, _) => index == 0 ? Task.FromException<HttpResponseMessage>(new HttpRequestException(Credentials.SyntheticToken)) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            f.Service.Completed(f.States); await f.Service.WaitForIdleAsync();
            byte[] acceptedButLost = f.Http.Last.Body;
            f.Time.Advance(TimeSpan.FromSeconds(30)); f.Service.Tick(); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 2 && f.Http.Last.Body.SequenceEqual(acceptedButLost), "lost response retries identical bytes and seq");
            Check(f.Service.Status.LastSuccessUtc is not null && f.Service.Status.RetryAtUtc is null, "2xx including duplicate acknowledgement ends retry");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            var blocked = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Reply = (index, ct) => index == 0 ? blocked.Task.WaitAsync(ct) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            f.Service.Completed(f.States); await Wait(() => f.Http.Count == 1);
            for (int i = 1; i <= 100; i++) { f.Time.Advance(TimeSpan.FromMilliseconds(1)); f.States = Readings(f.Time.GetUtcNow(), i); f.Service.Completed(f.States); }
            Check(f.Http.Count == 1, "one request in flight with a slow server");
            blocked.SetResult(new HttpResponseMessage(HttpStatusCode.OK)); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 2, "100 pending snapshots coalesce to one latest");
            using var latest = Body(f.Http.Last);
            Check(latest.RootElement.GetProperty("providers")[0].GetProperty("rows")[0].GetProperty("usedPercent").GetDouble() == 100, "latest snapshot survives coalescing");
            Check(Seq(f.Http.Last) == Seq(f.Http.Requests[0]) + 1, "coalesced drafts do not consume disk seq until dispatch");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            var client = new Client(f.Time);
            using var monitor = new AccountQuotaMonitor(client, utcNow: f.Time.GetUtcNow);
            int completions = 0;
            using var bridge = new WindowsQuotaExportBridge(monitor, states => { Interlocked.Increment(ref completions); f.Service.Completed(states); });
            var blocked = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Reply = (index, ct) => index == 0 ? blocked.Task.WaitAsync(ct) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            for (int round = 0; round < 40; round++)
            {
                client.Fail = round % 2 == 1;
                await Task.WhenAll(Enum.GetValues<QuotaProvider>().Select(p => monitor.RefreshOnceAsync(p))).WaitAsync(TimeSpan.FromSeconds(3));
                Check(completions == (round + 1) * 3, "parallel completion round has no missed or duplicate success/failure");
            }
            Check(f.Http.Count <= 1, "slow sender never holds Provider callbacks");
            blocked.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count is >= 1 and <= 2, "parallel observations retain bounded latest export");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            var cachedStore = new UsageStore(Path.Combine(f.DirectoryPath, "synthetic-cache.db"));
            foreach (var state in f.States)
                await cachedStore.SaveQuotaAsync(new(1, state.Reading!, f.Time.GetUtcNow().AddHours(-1), []));
            var client = new Client(f.Time) { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            using var monitor = new AccountQuotaMonitor(client, cachedStore, f.Time.GetUtcNow);
            int completions = 0;
            using var bridge = new WindowsQuotaExportBridge(monitor, states => { Interlocked.Increment(ref completions); f.Service.Completed(states); });
            try
            {
                monitor.Start();
                await Wait(() => monitor.Snapshot().All(s => s.IsRefreshing && s.Reading is not null));
                Check(completions == 0 && f.Http.Count == 0 && f.Vault.Reads == 0, "startup/cache/start events do not export");
                client.Gate.SetResult();
                await Wait(() => Volatile.Read(ref completions) == 3);
                await f.Service.WaitForIdleAsync();
                Check(completions == 3 && f.Http.Count > 0, "only completed live startup reads export");
            }
            finally { await monitor.StopAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }
        }

        foreach (int status in new[] { 200, 202, 204, 400, 401, 403, 404, 408, 413, 429, 500, 503, 302, 307 })
        {
            using var f = new Fixture(); await f.Service.Initialization;
            f.Http.Reply = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { ReasonPhrase = Credentials.SyntheticToken });
            f.Service.Completed(f.States); await f.Service.WaitForIdleAsync();
            Check((f.Service.Status.LastSuccessUtc is not null) == (status is >= 200 and < 300), "2xx success classification");
            Check((f.Service.Status.RetryAtUtc is not null) == (status >= 500), "4xx/redirect stop; only 5xx retry");
            Check(f.Http.Count == 1, "no automatic redirect");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            var blocked = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Reply = (_, ct) => blocked.Task.WaitAsync(ct);
            f.Service.Completed(f.States); await Wait(() => f.Http.Count == 1);
            Check(await f.Service.SaveAsync(false, "https://quota.example.test", null), "disable while request is in flight");
            blocked.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)); await f.Service.WaitForIdleAsync();
            f.Time.Advance(TimeSpan.FromHours(1)); f.Service.Tick(); f.Service.Completed(f.States); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 1 && f.Service.Status.LastSuccessUtc is null && f.Service.Status.RetryAtUtc is null, "disable cancels queue and ignores late completion");
        }

        using (var f = new Fixture(false))
        {
            await f.Service.Initialization;
            const string rotated = "SYNTHETIC-NEW-SERVICE-TOKEN";
            Check(await f.Service.SaveAsync(false, "https://quota.example.test", rotated), "replace credential through isolated vault");
            Check(f.Vault.Writes == 1 && !File.ReadAllText(f.SettingsPath).Contains(rotated), "replacement does not enter settings");
            f.Http.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            await f.Service.TestAsync(); await f.Service.WaitForIdleAsync();
            Check(f.Http.Last.Header == "Bearer " + rotated, "dispatch reads the newly replaced token rather than a cached token");
            f.Time.Advance(TimeSpan.FromMinutes(30)); f.Service.Tick(); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 1 && f.Service.Status.RetryAtUtc is null, "OFF explicit test does not start automatic retries");
        }

        foreach (bool oldSuccess in new[] { false, true })
        {
            using var f = new Fixture();
            using var sender = new IgnoringCancellationTransport();
            using var service = new WindowsQuotaExportService(f.Store, f.Vault, sender, () => f.States, "0.0.0-synthetic", f.Time);
            await service.Initialization;
            service.Completed(f.States); await Wait(() => sender.Count == 1);
            f.Time.Advance(TimeSpan.FromMinutes(1));
            Check(await service.SaveAsync(true, "https://changed.example.test", "SYNTHETIC-ROTATED-TOKEN"), "change address/token while old request ignores cancellation");
            service.Completed(Readings(f.Time.GetUtcNow(), 42));
            Check(sender.Requests[0].Token.IsCancellationRequested && sender.Count == 1, "cancel old request without starting a parallel request");
            sender.First.SetResult(new(oldSuccess, !oldSuccess, oldSuccess ? WindowsQuotaExportFailure.None : WindowsQuotaExportFailure.Server));
            await Wait(() => sender.Count == 2);
            Check(service.Status.LastSuccessUtc is null && service.Status.RetryAtUtc is null && service.Status.Failure == WindowsQuotaExportFailure.None,
                "late old 2xx/5xx cannot update new success/failure/backoff");
            Check(sender.Requests[0].Uri.Host == "quota.example.test" && sender.Requests[1].Uri.Host == "changed.example.test", "next request uses saved new address");
            using var oldBody = JsonDocument.Parse(sender.Requests[0].Body); using var newBody = JsonDocument.Parse(sender.Requests[1].Body);
            Check(newBody.RootElement.GetProperty("seq").GetInt64() > oldBody.RootElement.GetProperty("seq").GetInt64(), "reconfiguration preserves source sequence");
            sender.Second.SetResult(new(true, false, WindowsQuotaExportFailure.None)); await service.WaitForIdleAsync();
            Check(service.Status.LastSuccessUtc == f.Time.GetUtcNow(), "new generation alone records success");
        }

        foreach (bool failVault in new[] { false, true })
        {
            using var f = new Fixture(); f.Vault.Fail = failVault;
            IWindowsQuotaExportSettingsStore interruptedStore = failVault ? f.Store : new FailingSettings(f.Store);
            using var transport = new WindowsQuotaExportTransport(f.Vault, new Handler());
            using var service = new WindowsQuotaExportService(interruptedStore, f.Vault, transport, () => f.States, "0.0.0-synthetic", f.Time);
            await service.Initialization;
            var identity = f.Store.Load();
            Check(!await service.SaveAsync(true, "https://quota.example.test", "SYNTHETIC-ROTATED-TOKEN"), "token/config partial failure reports failure");
            var persisted = new WindowsQuotaExportSettingsStore(f.SettingsPath).Load();
            Check(!persisted.Enabled && persisted.SourceId == identity.SourceId && persisted.Seq == identity.Seq, "partial token save persists OFF and preserves identity");
            Check(service.Status.Failure == (failVault ? WindowsQuotaExportFailure.Credential : WindowsQuotaExportFailure.Settings), "failed credential/file phase is generalized accurately");
            Check(!File.ReadAllText(f.SettingsPath).Contains("SYNTHETIC-ROTATED-TOKEN"), "partial save never places token in file");
            using var restartHttp = new Handler();
            using var restarted = new WindowsQuotaExportService(new WindowsQuotaExportSettingsStore(f.SettingsPath), f.Vault,
                new WindowsQuotaExportTransport(f.Vault, restartHttp), () => f.States, "0.0.0-synthetic", f.Time);
            await restarted.Initialization; restarted.Completed(f.States); f.Time.Advance(TimeSpan.FromHours(1)); restarted.Tick(); await restarted.WaitForIdleAsync();
            Check(!restarted.Status.Enabled && restartHttp.Count == 0 && f.Vault.Reads == 0, "restart after either partial failure cannot send old/new token automatically");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            var row = f.States[0].Reading!.Windows[0] with { Label = new string('x', 81) };
            var invalid = f.States.ToArray(); invalid[0] = invalid[0] with { Reading = new(QuotaProvider.Codex, [row]) };
            f.Service.Completed(invalid); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 0 && f.Service.Status.Failure == WindowsQuotaExportFailure.Payload && f.Service.Status.RetryAtUtc is null, "out-of-contract label fails export without truncating app data");
            Check(invalid[0].Reading!.Windows[0].Label.Length == 81, "sender never edits the existing reading");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            byte[] before = File.ReadAllBytes(f.SettingsPath);
            using (File.Open(f.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool rejected = false; try { f.Store.ReserveSequence(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { rejected = true; }
                Check(rejected, "locked settings reject reservation before network");
                f.Service.Completed(f.States); await f.Service.WaitForIdleAsync();
                Check(f.Service.Status.Failure == WindowsQuotaExportFailure.Settings && f.Service.Status.RetryAtUtc == f.Time.GetUtcNow().AddSeconds(30), "reservation failure is separate from HTTP and scheduled safely");
            }
            Check(File.ReadAllBytes(f.SettingsPath).SequenceEqual(before), "failed atomic replacement preserves previous file");
            Check(!Directory.EnumerateFiles(f.DirectoryPath, "*.tmp-*").Any(), "owned failed temporary file is removed");
            Check(f.Http.Count == 0, "failed reservation never transmits");
            f.Time.Advance(TimeSpan.FromSeconds(30)); f.Service.Tick(); await f.Service.WaitForIdleAsync();
            Check(f.Http.Count == 1 && Seq(f.Http.Last) == 1, "released storage lock permits a durably reserved retry");
        }

        using (var f = new Fixture())
        {
            await f.Service.Initialization;
            byte[] before = File.ReadAllBytes(f.SettingsPath);
            using (File.Open(f.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!await f.Service.SaveAsync(true, "https://quota.example.test", "SYNTHETIC-UNSAVED-TOKEN"), "failed OFF pre-save prevents token replacement");
            Check(f.Vault.Writes == 0 && File.ReadAllBytes(f.SettingsPath).SequenceEqual(before), "OFF pre-save failure leaves both old stores unchanged");
            Check(!f.Service.Status.Enabled && f.Http.Count == 0, "failed pre-save pauses this session without sending");
        }

        using (var f = new Fixture(false))
        {
            await f.Service.Initialization;
            File.WriteAllText(f.SettingsPath, "{invalid synthetic settings}");
            using var broken = new WindowsQuotaExportService(new WindowsQuotaExportSettingsStore(f.SettingsPath), f.Vault,
                new WindowsQuotaExportTransport(f.Vault, new Handler()), () => f.States, "0.0.0-synthetic", f.Time);
            await broken.Initialization; broken.Completed(f.States); broken.Tick(); await broken.WaitForIdleAsync();
            Check(!broken.Status.Ready && !broken.Status.Enabled && broken.Status.Failure == WindowsQuotaExportFailure.Settings, "invalid persisted identity fails closed without resetting source/seq");
            Check(File.ReadAllText(f.SettingsPath) == "{invalid synthetic settings}", "invalid file is not overwritten");
        }

        using (var handler = WindowsQuotaExportTransport.CreateHandler())
        {
            Check(!handler.AllowAutoRedirect && !handler.UseCookies && !handler.UseDefaultCredentials, "production handler redirect/cookie/credential guards");
            Check(handler.ServerCertificateCustomValidationCallback is null && handler.CheckCertificateRevocationList, "normal Windows certificate validation retained");
            Check(WindowsQuotaExportTransport.RequestTimeout == TimeSpan.FromSeconds(15), "production timeout remains 15 seconds");
        }
        foreach (string address in new[] { "http://quota.example.test", "https://user:secret@quota.example.test", "https://quota.example.test/path", "https://quota.example.test/?token=synthetic", "https://quota.example.test/#fragment", "not an address" })
        {
            bool rejected = false; try { WindowsQuotaExportSettingsStore.Endpoint(address); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid site address rejected before credential access");
        }
        using (var f = new Fixture(false))
        using (var transport = new WindowsQuotaExportTransport(f.Vault, f.Http, TimeSpan.FromMilliseconds(20)))
        {
            Check(!(await transport.SendAsync(new Uri("http://quota.example.test/api/quota"), [], default)).Retry && f.Vault.Reads == 0, "HTTP rejected without reading token");
            Check((await transport.SendAsync(new Uri("https://quota.example.test/api/quota"), new byte[WindowsQuotaExportSnapshot.MaximumBytes + 1], default)).Failure == WindowsQuotaExportFailure.Payload && f.Http.Count == 0, "1 MiB body guard precedes credential/network access");
            f.Http.Reply = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); };
            Check((await transport.SendAsync(new Uri("https://quota.example.test/api/quota"), [], default)).Failure == WindowsQuotaExportFailure.Timeout, "request deadline cancels a stalled server");
            var unread = new UnreadContent(); f.Http.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = unread });
            Check((await transport.SendAsync(new Uri("https://quota.example.test/api/quota"), [], default)).Succeeded && unread.Reads == 0, "response bodies are neither buffered nor inspected");
            f.Vault.Fail = true;
            Check((await transport.SendAsync(new Uri("https://quota.example.test/api/quota"), [], default)).Failure == WindowsQuotaExportFailure.Credential, "credential errors are generalized");
        }
        return count;
    }
}
