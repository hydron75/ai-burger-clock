using System.Collections.Concurrent;
using System.Text.Json;
using static AiBurgerClock.QuotaP0Harness;
using static AiBurgerClock.QuotaProvider;

namespace AiBurgerClock;

internal static class WindowsQuotaP0Checks
{
    internal static async Task<int> RunAsync()
    {
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-quota-p0-").FullName;
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Windows quota P0 §" + label);
            checks++;
        }
        try
        {
            await WebStatesAsync(directory, Check);
            await CoalescingAsync(directory, Check);
            await CancellationAsync(directory, Check);
            await LogoutAsync(directory, Check);
            Check(WindowsDetailWidth == 320, "3.3-7 detail width is 320 DIP at 96 DPI");
            return checks;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static JsonElement Provider(byte[] bytes, string name = "Gemini")
    {
        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.GetProperty("providers").EnumerateArray()
            .Single(p => p.GetProperty("provider").GetString() == name).Clone();
    }

    private static async Task WebStatesAsync(string directory, Action<bool, string> check)
    {
        using var h = new QuotaP0Harness(hasWeb: true);
        var packets = new ConcurrentQueue<byte[]>();
        using var bridge = h.CapturePackets(packets);
        h.EnableWeb(true);
        var awaiting = Provider(h.CurrentPacket());
        check(packets.IsEmpty && !awaiting.TryGetProperty("webState", out _) &&
            awaiting.GetProperty("nextCheckUtc").ValueKind == JsonValueKind.Null,
            "3.3-4 enabling has null/omitted webState and sends nothing");
        await h.ReadWebAsync();
        var live = Provider(packets.Single());
        check(live.GetProperty("webState").GetString() == "ok" && live.GetProperty("reading").GetString() == "ok" &&
            live.GetProperty("rows").GetArrayLength() == 2 && live.GetProperty("nextCheckUtc").GetDateTimeOffset() == Epoch.AddMinutes(30),
            "3.3-4 schema v2 sends both live Gemini rows and its deadline");
        h.RequestWeb();
        await h.ReadWebAsync();
        check(packets.Count == 1, "3.3-4 unchanged web results do not send");

        foreach (var sample in new[] { (WebChanged, "formatChanged"), (GeminiUsagePageLoad.LoadFailed, "loadFailed") })
        {
            h.WebRead = _ => Task.FromResult(sample.Item1);
            h.RequestWeb();
            await h.ReadWebAsync();
            var r = Provider(packets.Last());
            check(r.GetProperty("webState").GetString() == sample.Item2 && r.GetProperty("reading").GetString() == "previous" &&
                r.GetProperty("rows").EnumerateArray().Count(row => row.GetProperty("isPrevious").GetBoolean()) == 2,
                "3.3-1/4 web error is exported as previous when values survive");
            check(h.Panel(Gemini).Rows.Count == (sample.Item2 == "formatChanged" ? 1 : 2) &&
                (h.Panel(Gemini).Rows[0].UsedPercent is null) == (sample.Item2 == "formatChanged"),
                "3.3-1 FormatChanged hides numbers in the shared panel while LoadFailed retains them");
        }
        h.WebRead = _ => Task.FromResult(WebSignedOut);
        h.RequestWeb();
        await h.ReadWebAsync();
        var signedOut = Provider(packets.Last());
        check(signedOut.GetProperty("webState").GetString() == "loginRequired" &&
            signedOut.GetProperty("lastSuccessUtc").ValueKind == JsonValueKind.Null && signedOut.GetProperty("nextCheckUtc").ValueKind == JsonValueKind.Null &&
            signedOut.GetProperty("reading").GetString() == "none" && signedOut.GetProperty("rows").GetArrayLength() == 0,
            "3.3-1/4 login required clears export values and timestamps");
        h.EnableWeb(false);
        check(Provider(packets.Last()).GetProperty("webState").GetString() == "off" &&
            Provider(packets.Last()).GetProperty("nextCheckUtc").ValueKind == JsonValueKind.Null, "3.3-4 off is an explicit state with no deadline");
        using (var packet = JsonDocument.Parse(packets.First()))
        {
            check(packet.RootElement.GetProperty("schemaVersion").GetInt32() == 2 &&
                packet.RootElement.GetProperty("providers").EnumerateArray().Select(p => p.GetProperty("provider").GetString())
                    .SequenceEqual(["Codex", "Claude", "Gemini", "Xai"]), "3.3-4/8 schema and identity order survive on the wire");
        }

        using var cached = new QuotaP0Harness(Path.Combine(directory, "cache", "test.db"), hasWeb: true, enabled: true);
        await cached.SeedWebCacheAsync(Epoch.AddMinutes(-1));
        var restored = new ConcurrentQueue<byte[]>();
        using var cachedBridge = cached.CapturePackets(restored);
        cached.CliRead = async (p, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reading(p);
        };
        cached.WebRead = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return WebOk;
        };
        cached.Start();
        try
        {
            await WaitAsync(() => cached.State(Gemini) is { Rows.Count: 2, ReadingNow: true });
            var duringRestore = Provider(cached.CurrentPacket());
            check(cached.State(Gemini) is { Previous: true, WebOutcome: null } && restored.IsEmpty &&
                !duringRestore.TryGetProperty("webState", out _) && duringRestore.GetProperty("rows").GetArrayLength() == 0,
                "3.1-7/3.3-4 initially enabled restores a previous web cache without exporting or exposing it on the wire");
        }
        finally { await cached.StopAsync(); }
        check(restored.IsEmpty, "3.3-4 canceling the first in-flight web/CLI reads does not export");

        // Today nextCheck follows the web outcome, including a cold error with
        // no rows. Preserve that observation separately from the design wording.
        foreach (var failure in new[] { WebChanged, GeminiUsagePageLoad.LoadFailed })
        {
            using var cold = new QuotaP0Harness(hasWeb: true, enabled: true);
            cold.WebRead = _ => Task.FromResult(failure);
            await cold.ReadWebAsync();
            var sent = Provider(cold.CurrentPacket());
            check(sent.GetProperty("reading").GetString() == "none" && sent.GetProperty("rows").GetArrayLength() == 0 &&
                sent.GetProperty("nextCheckUtc").GetDateTimeOffset() == Epoch.AddMinutes(30), "3.3-4 observed: cold web failures still carry nextCheckUtc");
        }
    }

    private static async Task CoalescingAsync(string directory, Action<bool, string> check)
    {
        using (var h = new QuotaP0Harness(hasWeb: true, enabled: true))
        {
            using var overlap = new SemaphoreSlim(0, 1);
            var webChanged = Signal();
            int blocked = 0;
            h.WatchChanges(() =>
            {
                if (h.State(Gemini) is not { ReadingNow: false, WebOutcome: "Ok" } ||
                    Interlocked.Exchange(ref blocked, 1) != 0) return;
                webChanged.TrySetResult();
                if (!overlap.Wait(Limit)) throw new TimeoutException("Synthetic export overlap expired.");
            });
            var packets = new ConcurrentQueue<byte[]>();
            using var bridge = h.CapturePackets(packets);
            var cliReply = Signal<QuotaReading>();
            h.CliRead = (_, token) => cliReply.Task.WaitAsync(token);
            Task cli = h.ReadCliAsync(Codex);
            Task web = Task.Run(() => h.ReadWebAsync());
            try
            {
                await webChanged.Task.WaitAsync(Limit);
                cliReply.SetResult(Reading(Codex));
                await cli.WaitAsync(Limit);
                check(packets.Count == 1 && Provider(packets.Single()).GetProperty("webState").GetString() == "ok" &&
                    Provider(packets.Single(), "Codex").GetProperty("reading").GetString() == "ok",
                    "3.3-4 a single bridge callback with simultaneous CLI completion and web change exports once");
            }
            finally
            {
                overlap.Release();
                cliReply.TrySetResult(Reading(Codex));
                await Task.WhenAll(web, cli).WaitAsync(Limit);
            }
            check(packets.Count == 1, "3.3-4 delayed web callback cannot repeat the same update");
        }

        using var active = new QuotaP0Harness(Path.Combine(directory, "active", "test.db"), hasWeb: true, enabled: true);
        var updates = new ConcurrentQueue<byte[]>();
        using var outbound = active.CapturePackets(updates);
        using (var cache = await active.PauseCacheAsync())
        {
            Task web = active.ReadWebAsync();
            await WaitAsync(() => active.State(Gemini) is { ReadingNow: true, WebOutcome: "Ok" });
            active.CliRead = (_, _) => Task.FromException<QuotaReading>(new IOException("Synthetic non-cacheable CLI failure"));
            await active.ReadCliAsync(Codex);
            check(updates.Count == 1 && Provider(updates.Single()).GetProperty("rows").GetArrayLength() == 2 &&
                active.State(Gemini).ReadingNow, "3.3-4 CLI completion sends the current web baseline while web is still saving");
            cache.Dispose();
            await web.WaitAsync(Limit);
        }
        check(updates.Count == 1, "3.3-4 CLI completion updates the Gemini comparison even during a web read");
    }

    private static async Task CancellationAsync(string directory, Action<bool, string> check)
    {
        foreach (bool web in new[] { false, true })
        {
            using var h = new QuotaP0Harness(Path.Combine(directory, web ? "cancel-web" : "cancel-cli", "test.db"),
                hasWeb: web, enabled: web);
            var packets = new ConcurrentQueue<byte[]>();
            using var bridge = h.CapturePackets(packets);
            var started = Signal();
            h.CliRead = async (p, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Reading(p);
            };
            h.WebRead = async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return WebOk;
            };
            Task Read(CancellationToken token) => web ? h.ReadWebAsync(token) : h.ReadCliAsync(Codex, token);
            using (var cancel = new CancellationTokenSource())
            {
                Task attempt = Read(cancel.Token);
                await started.Task.WaitAsync(Limit);
                cancel.Cancel();
                await CancelledAsync(attempt);
                check(packets.IsEmpty, "3.3-4 cancel before result acceptance sends zero");
            }

            // Unlike cancellation of the read, canceling cache persistence
            // leaves an accepted snapshot. The current bridge exports it.
            h.CliRead = (p, _) => Task.FromResult(Reading(p));
            h.WebRead = _ => Task.FromResult(WebOk);
            using var held = await h.PauseCacheAsync();
            using var cancelCache = new CancellationTokenSource();
            Task saving = Read(cancelCache.Token);
            await WaitAsync(() => h.State(web ? Gemini : Codex) is { ReadingNow: true, LastSuccess: not null });
            cancelCache.Cancel();
            await CancelledAsync(saving);
            check(packets.Count == 1 && Provider(packets.Single(), web ? "Gemini" : "Codex").GetProperty("reading").GetString() == "ok",
                "3.3-4 observed: cache-save cancellation exports the already accepted quota");
        }
    }

    private static async Task LogoutAsync(string directory, Action<bool, string> check)
    {
        using var h = new QuotaP0Harness(Path.Combine(directory, "logout", "test.db"), hasWeb: true, enabled: true);
        await h.SeedWebCacheAsync(Epoch.AddMinutes(-1));
        var order = new List<string>();
        WindowsLogoutFixture? host = null;
        h.WatchChanges(() =>
        {
            if (h.State(Gemini).WebEnabled == false)
                order.Add(host!.Pending ? "off-after-pending" : "off-before-pending");
        });
        host = h.PrepareWindowsLogout(() =>
        {
            order.Add(h.CacheJson(WebGeminiKey) is null && h.State(Gemini).WebEnabled == false && host!.Pending ? "profile-after-cache" : "profile-too-early");
            throw new IOException("Synthetic profile deletion failure");
        });
        try { await host.LogoutAsync(); }
        catch (IOException) { }
        check(host.Pending && !host.Enabled && order.SequenceEqual(["off-after-pending", "profile-after-cache"]),
            "3.3-5 Windows records retry before disabling and deletes cache before attempting profile deletion");
        host = h.PrepareWindowsLogout(() =>
        {
            order.Add(h.CacheJson(WebGeminiKey) is null && host!.Pending ? "retried-profile" : "retry-too-early");
            return Task.CompletedTask;
        }, pending: true);
        await host.RetryAsync();
        check(!host.Pending && !host.Enabled && order.Last() == "retried-profile", "3.3-5 successful Windows retry clears the pending setting only after profile deletion");
    }
}
