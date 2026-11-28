using System.Collections.Concurrent;
using System.Text.Json;
using static AiBurgerClock.QuotaP0Harness;
using static AiBurgerClock.QuotaProvider;

namespace AiBurgerClock;

internal static class QuotaP0CharacterizationTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        int count = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Quota P0 §" + name);
            count++;
        }

        // Each numbered clause has a scenario, even where a single fixture
        // carries observations across more than one clause.
        await EventsAsync(directory, Check);
        await CliScheduleAsync(directory, Check);
        await WebScheduleAsync(Check);
        await RefreshAndShutdownAsync(Check);
        await ClaudeAuthAsync(Check);
        await DisplayAndCacheAsync(directory, Check);
        return count;
    }

    private static string Database(string directory, string scenario)
    {
        string root = Path.Combine(directory, "p0", scenario);
        Directory.CreateDirectory(root);
        return Path.Combine(root, "test.db");
    }

    private static async Task EventsAsync(string directory, Action<bool, string> check)
    {
        using var h = new QuotaP0Harness(Database(directory, "events"));
        await h.ReadCliAsync(Codex);
        check(h.LogText("quota-early-reset-decisions-v2.jsonl").Contains("\"HostUnavailable\"", StringComparison.Ordinal),
            "3.1-1 unattended live observation has HostUnavailable");

        var seen = new ConcurrentQueue<QuotaObservation>();
        StateView? callbackView = null;
        h.WatchSuccess(observation =>
        {
            seen.Enqueue(observation);
            if (seen.Count != 1) return;
            // A different thread must be able to snapshot while the callback is
            // running; the same Provider must still be unable to start a read.
            callbackView = Task.Run(() => h.State(Codex)).WaitAsync(Limit).GetAwaiter().GetResult();
            h.ReadCliAsync(Codex).WaitAsync(Limit).GetAwaiter().GetResult();
        });
        await h.ReadCliAsync(Codex);
        check(seen.Count == 1 && h.CliCalls(Codex) == 2 && callbackView is { ReadingNow: false, LastSuccess: not null },
            "3.1-1 accepted success fires once outside the state lock and inside the Provider gate");

        h.CliRead = (_, _) => Task.FromException<QuotaReading>(new InvalidDataException("Synthetic rejected reading"));
        await h.ReadCliAsync(Codex);
        check(seen.Count == 1 && h.State(Codex).Continuity == 2, "3.1-1 failed invocation breaks continuity with no Succeeded");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        check(await CancelledAsync(h.ReadCliAsync(Codex, cancelled.Token)) &&
            h.State(Codex).Continuity == 2 && h.CliCalls(Codex) == 3, "3.1-1 cancellation before starting a read preserves continuity");

        h.CliRead = (p, _) => Task.FromResult(Reading(p, 52));
        h.Now = Epoch.AddMinutes(1);
        using (var hold = await h.PauseCacheAsync())
        using (var cancelWrite = new CancellationTokenSource())
        {
            Task read = h.ReadCliAsync(Codex, cancelWrite.Token);
            await WaitAsync(() => h.State(Codex).LastSuccess == h.Now && h.State(Codex).ReadingNow);
            cancelWrite.Cancel();
            check(await CancelledAsync(read) && seen.Count == 1 && h.State(Codex) is { Continuity: 3, ReadingNow: false } &&
                h.State(Codex).Rows.Single().UsedPercent == 52, "3.1-1 canceled cache persistence keeps accepted value but emits no success");
        }

        using var restore = new QuotaP0Harness(Database(directory, "startup"));
        await restore.SeedCliCacheAsync(Codex, 73, Epoch.AddMinutes(-1));
        var cacheSeen = Signal<StateView>();
        var started = Signal();
        restore.WatchChanges(() =>
        {
            var state = restore.State(Codex);
            if (!state.ReadingNow && state.Attempted is null && state.Rows.Count != 0) cacheSeen.TrySetResult(state);
        });
        restore.WatchSuccess(seen.Enqueue);
        restore.CliRead = async (p, token) =>
        {
            if (p == Codex) started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reading(p);
        };
        restore.Start();
        try
        {
            await started.Task.WaitAsync(Limit);
            var cached = await cacheSeen.Task.WaitAsync(Limit);
            check(cached is { Previous: true, Continuity: 1 } && cached.Rows.Single().UsedPercent == 73 && seen.Count == 1,
                "3.1-2 startup publishes the previous cache before the immediate live read, with no cached Succeeded");
            check(restore.State(Codex).ReadingNow && restore.CliCalls(Codex) == 1,
                "3.1-2 startup does not wait for a scheduled deadline after cache restoration");
        }
        finally { await restore.StopAsync(); }
        check(restore.State(Codex) is { ReadingNow: false, Continuity: 2 } && seen.Count == 1,
            "3.1-8 CLI-only StopAsync cancels and drains an active CLI read without an observation");
    }

    private static async Task CliScheduleAsync(string directory, Action<bool, string> check)
    {
        using var h = new QuotaP0Harness(Database(directory, "schedule"));
        await h.ReadCliAsync(Codex);
        var next = h.State(Codex).NextCheck!.Value;
        h.Now = next.AddMinutes(5);
        check(!h.State(Codex).Previous, "3.1-3 exactly five minutes after due is still current");
        h.Now = h.Now.AddTicks(1);
        check(h.State(Codex).Previous, "3.1-3 late beyond the grace period is previous");
        h.Now = Epoch.AddMinutes(-5);
        check(!h.State(Codex).Previous, "3.1-3 exactly five minutes before success is still current");
        h.Now = h.Now.AddTicks(-1);
        check(h.State(Codex).Previous, "3.1-3 a larger backward clock jump is previous");
        h.Now = Epoch;
        foreach (var sample in new[] { (37d, 60, "Regular"), (90d, 60, "Regular"), (90.01d, 60, "LowRemaining"), (100d, 15, "Exhausted") })
        {
            h.CliRead = (p, _) => Task.FromResult(Reading(p, sample.Item1));
            await h.ReadCliAsync(Codex);
            check(h.State(Codex).NextCheck == Epoch.AddMinutes(sample.Item2) && h.LastPollingReason(Codex) == sample.Item3,
                "3.1-4 regular, exact 90%, low remaining and exhausted select their current plan");
        }
        h.CliRead = (p, _) => Task.FromResult(Reading(p, 38, Epoch.AddMinutes(18)));
        await h.ReadCliAsync(Codex);
        await h.ReadCliAsync(Xai);
        check(h.State(Codex).NextCheck == Epoch.AddMinutes(3) && h.State(Xai).NextCheck == Epoch.AddMinutes(30) &&
            h.LastPollingReason(Codex) == "ResetEntry" && h.LastPollingReason(Xai) == "Regular", "3.1-4 xAI ignores scheduled reset entry");
        h.Now = Epoch.AddMinutes(5);
        await h.ReadCliAsync(Codex);
        await h.ReadCliAsync(Xai);
        check(h.State(Codex).NextCheck == h.Now.AddMinutes(5) && h.State(Xai).NextCheck == h.Now.AddMinutes(30) &&
            h.LastPollingReason(Xai) == "Regular", "3.1-4 xAI keeps thirty minutes in the former reset band");
        h.Now = Epoch.AddHours(2);
        h.CliRead = (_, _) => Task.FromException<QuotaReading>(new IOException("Synthetic offline"));
        for (int failure = 0; failure < 4; failure++)
        {
            await h.ReadCliAsync(Xai);
            check(h.State(Xai).NextCheck == h.Now.AddMinutes(30) && h.LastPollingReason(Xai) == "FailureRetry",
                "3.1-4 repeated xAI failures retry in thirty minutes");
            h.Now = h.Now.AddMinutes(30);
        }
    }

    private static async Task WebScheduleAsync(Action<bool, string> check)
    {
        using var h = new QuotaP0Harness(hasWeb: true, enabled: true);
        int successes = 0;
        h.WatchSuccess(_ => successes++);
        await h.ReadWebAsync();
        check(h.WebCalls == 1 && h.State(Gemini) is { WebOutcome: "Ok" } && h.State(Gemini).NextCheck == Epoch.AddMinutes(30),
            "3.1-5 first web success uses a fixed 30-minute interval");
        h.Now = Epoch.AddMinutes(30).AddTicks(-1);
        h.Recover();
        await h.ReadWebAsync();
        check(h.WebCalls == 1, "3.1-5 a recovery wake before the due time does not read web usage");
        h.Now = h.Now.AddTicks(1);
        h.WebRead = _ => Task.FromResult(GeminiUsagePageLoad.LoadFailed);
        await h.ReadWebAsync();
        check(h.WebCalls == 2 && h.State(Gemini) is { WebOutcome: "LoadFailed", Previous: true } &&
            h.State(Gemini).NextCheck == h.Now.AddMinutes(30), "3.1-5 due time is inclusive and a failed read still waits 30 minutes");
        h.WebRead = _ => Task.FromResult(WebSignedOut);
        h.RequestWeb();
        await h.ReadWebAsync();
        h.Now = h.Now.AddHours(1);
        h.Recover();
        await h.ReadWebAsync();
        check(h.WebCalls == 3 && h.State(Gemini) is { WebOutcome: "LoginRequired", NextCheck: null },
            "3.1-5 login required pauses scheduled web reads");
        h.WebRead = _ => Task.FromResult(WebOk);
        h.RequestWeb();
        // Keep the absolute fixture resets valid at the advanced clock.
        await h.ReadWebAsync();
        check(h.WebCalls == 4 && h.State(Gemini).WebOutcome == "Ok", "3.1-5 a web manual request can resume a paused session");
        h.WebRead = _ => Task.FromResult(WebSignedOut);
        h.RequestWeb();
        await h.ReadWebAsync();
        h.WebRead = _ => Task.FromResult(WebOk);
        h.EnableWeb(true);
        await h.ReadWebAsync();
        check(h.WebCalls == 6 && h.State(Gemini).WebOutcome == "Ok" && successes == 0,
            "3.1-5 fresh login resumes scheduling and web reads never emit Succeeded");
    }

    private static async Task RefreshAndShutdownAsync(Action<bool, string> check)
    {
        using (var h = new QuotaP0Harness(hasWeb: true, enabled: true))
        {
            var cliReply = Signal<QuotaReading>();
            var webReply = Signal<GeminiUsagePageLoad>();
            h.CliRead = (p, token) => p == Codex ? cliReply.Task.WaitAsync(token) :
                Task.FromResult(Reading(p, reset: p == Xai ? Epoch.AddMinutes(18) : null));
            h.WebRead = token => webReply.Task.WaitAsync(token);
            h.Start();
            try
            {
                await WaitAsync(() => h.CliCalls(Codex) == 1 && h.WebCalls == 1 && !h.State(Xai).ReadingNow &&
                    h.State(Xai).NextCheck == Epoch.AddMinutes(30));
                h.RefreshManually();
                h.RefreshManually();
                check(h.CliCalls(Codex) == 1 && h.WebCalls == 1, "3.1-6 overlapping manual requests do not start parallel reads");
                cliReply.SetResult(Reading(Codex));
                webReply.SetResult(WebOk);
                await WaitAsync(() => h.WebCalls == 2 && !h.State(Gemini).ReadingNow && h.State(Codex).LastSuccess == Epoch);
                // Neither scheduled deadline is near; this observes the asynchronous
                // loops after the sticky request and before supplying another wake.
                await Task.Delay(100);
                check(h.CliCalls(Codex) == 1 && h.WebCalls == 2, "3.1-6 in-flight CLI manual wakes drop, web manual wakes coalesce and survive");
                check(h.CliCalls(Xai) >= 2 && h.Now == Epoch, "3.1-4 the xAI schedule does not delay a manual refresh");
                var recoveryReply = Signal<QuotaReading>();
                h.CliRead = (p, token) => p == Codex ? recoveryReply.Task.WaitAsync(token) : Task.FromResult(Reading(p));
                h.Recover();
                await WaitAsync(() => h.CliCalls(Codex) == 2);
                check(h.Now == Epoch && h.WebCalls == 2, "3.1-4/6 recovery wakes CLI before its scheduled time and leaves web alone");
                h.Recover();
                h.Recover();
                recoveryReply.SetResult(Reading(Codex));
                await WaitAsync(() => h.CliCalls(Codex) == 3);
                check(h.WebCalls == 2, "3.1-6 recovery while CLI is busy leaves one pending CLI read and no extra web read");
            }
            finally { await h.StopAsync(); }
        }

        foreach (string action in new[] { "disable", "stop", "dispose" })
        {
            using var h = new QuotaP0Harness(hasWeb: true, enabled: true);
            // Disable hands its cancellation task to the read's finally. For
            // lifetime shutdown, keep the blocked callback and reply on separate
            // threads because linked-token disposal can itself wait for cancellation.
            var readerReply = action == "disable" ? new TaskCompletionSource<GeminiUsagePageLoad>() : Signal<GeminiUsagePageLoad>();
            var cancellationStarted = Signal();
            var cancellationReturn = Signal();
            h.WebRead = token =>
            {
                token.Register(() =>
                {
                    _ = h.State(Gemini);
                    cancellationStarted.TrySetResult();
                    cancellationReturn.Task.WaitAsync(Limit).GetAwaiter().GetResult();
                });
                return readerReply.Task;
            };
            Task read = h.ReadWebAsync();
            Task? stop = null;
            try
            {
                if (action == "disable") await Task.Run(() => h.EnableWeb(false)).WaitAsync(Limit);
                else if (action == "stop") stop = h.StopAsync();
                else await Task.Run(h.Dispose).WaitAsync(Limit);
                await cancellationStarted.Task.WaitAsync(Limit);
                check(!cancellationReturn.Task.IsCompleted && !read.IsCompleted && stop?.IsCompleted != true,
                    "3.1-8 " + action + " begins cancellation without blocking the caller or holding the snapshot lock");
                if (action == "disable")
                {
                    check(h.State(Gemini) is { WebEnabled: false, ReadingNow: false, Continuity: 1 },
                        "3.1-7/8 disabling clears state immediately");
                    h.EnableWeb(true);
                }
                readerReply.SetResult(WebOk);
                if (action == "disable")
                {
                    await h.ReadWebAsync().WaitAsync(Limit);
                    check(!read.IsCompleted && h.WebCalls == 1,
                        "3.1-8 the Provider gate stays held while the cancellation callback is running");
                }
                else if (action == "stop")
                    check(!stop!.IsCompleted, "3.1-8 StopAsync waits for active web work and its cancellation callback");
            }
            finally
            {
                cancellationReturn.TrySetResult();
                readerReply.TrySetResult(WebOk);
                await read.WaitAsync(Limit);
                if (stop is not null) await stop;
            }
            check(h.State(Gemini) is { Rows.Count: 0, CacheError: "", ReadingNow: false, LastSuccess: null },
                "3.1-8 " + action + " ignores a canceled late result");
            if (action == "disable")
            {
                h.WebRead = _ => Task.FromResult(WebOk);
                await h.ReadWebAsync();
                check(h.WebCalls == 2 && h.State(Gemini).WebOutcome == "Ok", "3.1-8 the next generation can read after cleanup");
            }
        }
    }

    private static async Task ClaudeAuthAsync(Action<bool, string> check)
    {
        check(AuthDue(Epoch, null, false) && !AuthDue(Epoch.AddHours(6).AddTicks(-1), Epoch, false) &&
            AuthDue(Epoch.AddHours(6), Epoch, false), "3.2-1 startup and the exact six-hour boundary");
        check(!AuthDue(Epoch.AddMinutes(15).AddTicks(-1), Epoch, true) && AuthDue(Epoch.AddMinutes(15), Epoch, true),
            "3.2-1 failures are throttled strictly inside 15 minutes");

        using (var scheduled = new QuotaP0Harness(hasAuth: true))
        {
            var first = Signal<ClaudeAuthStatus>();
            scheduled.AuthRead = _ => scheduled.AuthCalls == 1 ? first.Task : Task.FromResult(SignedIn);
            scheduled.Start();
            try
            {
                await WaitAsync(() => scheduled.AuthCalls == 1);
                scheduled.Now = Epoch.AddHours(6);
                first.SetResult(SignedIn);
                await WaitAsync(() => scheduled.AuthCalls == 2 && scheduled.Auth?.CheckedAtUtc == scheduled.Now);
                check(scheduled.AuthCalls == 2, "3.2-1 scheduled probe executes at six hours without a manual wake");
            }
            finally { first.TrySetResult(SignedIn); await scheduled.StopAsync(); }
        }

        using (var h = new QuotaP0Harness(hasAuth: true))
        {
            var first = Signal<ClaudeAuthStatus>();
            var second = Signal<ClaudeAuthStatus>();
            h.AuthRead = _ => h.AuthCalls switch { 1 => first.Task, 2 => second.Task, _ => Task.FromResult(SignedIn) };
            h.Start();
            try
            {
                await WaitAsync(() => h.AuthCalls == 1 && !h.State(Claude).ReadingNow && h.State(Claude).LastSuccess is not null);
                h.Now = Epoch.AddMinutes(1);
                h.RefreshManually();
                await WaitAsync(() => h.CliCalls(Claude) >= 2 && !h.State(Claude).ReadingNow);
                h.CliRead = (_, _) => Task.FromException<QuotaReading>(new IOException("Synthetic auth test failure"));
                await h.ReadCliAsync(Claude);
                check(h.AuthCalls == 1, "3.2-1 requests queue behind the current auth probe");
                first.SetResult(SignedIn);
                await WaitAsync(() => h.AuthCalls == 2);
                check(h.Auth?.CheckedAtUtc == Epoch && h.Now == Epoch.AddMinutes(1),
                    "3.2-1/3 failure does not lower queued Immediate and auth uses the start timestamp");

                h.Now = Epoch.AddMinutes(16).AddMilliseconds(-50);
                await h.ReadCliAsync(Claude);
                second.SetResult(SignedIn);
                await WaitAsync(() => h.Auth?.CheckedAtUtc == Epoch.AddMinutes(1));
                await Task.Delay(100);
                h.Now = Epoch.AddMinutes(16);
                await Task.Delay(100);
                check(h.AuthCalls == 2, "3.2-1 a throttled failure is discarded instead of running at the 15-minute boundary");
                await h.ReadCliAsync(Claude);
                await WaitAsync(() => h.AuthCalls == 3);
                check(h.AuthCalls == 3, "3.2-1 a new failure at the boundary wakes auth immediately");
            }
            finally
            {
                first.TrySetResult(SignedIn);
                second.TrySetResult(SignedIn);
                await h.StopAsync();
            }
        }

        using (var cancelled = new QuotaP0Harness(hasAuth: true))
        {
            cancelled.Start();
            try
            {
                await WaitAsync(() => cancelled.Auth is not null && cancelled.State(Claude) is { LastSuccess: not null, ReadingNow: false });
                cancelled.Now = Epoch.AddMinutes(20);
                var cliEntered = Signal();
                cancelled.CliRead = async (p, token) =>
                {
                    cliEntered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Reading(p);
                };
                using var stopRead = new CancellationTokenSource();
                Task pending = cancelled.ReadCliAsync(Claude, stopRead.Token);
                await cliEntered.Task.WaitAsync(Limit);
                stopRead.Cancel();
                check(await CancelledAsync(pending), "3.2-2 exercise a started and canceled quota refresh");
                await Task.Delay(100);
                check(cancelled.AuthCalls == 1, "3.2-2 a canceled quota attempt does not wake auth, even beyond its minimum interval");
            }
            finally { await cancelled.StopAsync(); }
        }

        foreach (var prior in new[] { ParseAuth(1, ""), ClaudeAuthStatus.Unknown, SignedIn with { LoginExpiresAtUtc = Epoch } })
        {
            using var h = new QuotaP0Harness(hasAuth: true);
            var followUp = Signal<ClaudeAuthStatus>();
            var gateReleased = Signal<bool>();
            h.AuthRead = async _ =>
            {
                if (h.AuthCalls == 1) return prior;
                if (h.AuthCalls != 2) return SignedIn;
                int before = h.CliCalls(Claude);
                await h.ReadCliAsync(Claude);
                gateReleased.TrySetResult(h.CliCalls(Claude) == before + 1);
                return await followUp.Task;
            };
            h.CliRead = (p, _) => p == Claude ? Task.FromException<QuotaReading>(new IOException()) : Task.FromResult(Reading(p));
            h.Start();
            try
            {
                await WaitAsync(() => h.Auth?.Status == prior && !h.State(Claude).ReadingNow && h.State(Claude).Attempted is not null);
                h.Now = Epoch.AddMinutes(1);
                h.CliRead = (p, _) => Task.FromResult(Reading(p));
                await h.ReadCliAsync(Claude);
                check(await gateReleased.Task.WaitAsync(Limit),
                    "3.2-2 the recovery probe runs after the successful quota read released its Provider gate");
                await h.ReadCliAsync(Claude);
                check(h.Panel(Claude).Rows.Single().Tone == PanelTone.Good && h.Panel(Claude).Scope is null,
                    "3.2-2/3 a newer quota success clears stale login-required UI before the recheck completes");
                followUp.SetResult(SignedIn);
                await WaitAsync(() => h.Auth?.Status == SignedIn);
                await h.StopAsync();
                check(h.AuthCalls == 2, "3.2-2 success with expired, signed-out or unknown evidence requests one recheck per observation");
            }
            finally { followUp.TrySetResult(SignedIn); await h.StopAsync(); }
        }

        foreach (var sample in new[]
        {
            (Auth: ParseAuth(1, ""), NeedsLogin: true, Warns: false),
            (Auth: SignedIn with { LoginExpiresAtUtc = Epoch }, NeedsLogin: true, Warns: false),
            (Auth: SignedIn with { LoginExpiresAtUtc = Epoch.AddDays(7) }, NeedsLogin: false, Warns: true),
            (Auth: ParseAuth(0, """{"loggedIn":true,"authMethod":"api_key","loginExpiresAt":"2035-04-06T10:00:00Z"}"""), NeedsLogin: false, Warns: false)
        })
        {
            using var h = new QuotaP0Harness(hasAuth: true);
            h.AuthRead = _ => Task.FromResult(sample.Auth);
            h.Start();
            try
            {
                await WaitAsync(() => h.Auth?.Status == sample.Auth && h.State(Claude).LastSuccess is not null);
                var panel = h.Panel(Claude);
                check(panel.Detail.Contains("Claude CLI 로그인 필요", StringComparison.Ordinal) == sample.NeedsLogin &&
                    (panel.Scope?.Tone == PanelTone.Caution) == sample.Warns,
                    "3.2-4 only current signed-out/expired evidence prompts, and only claude.ai can warn");
                if (sample.Auth.AuthMethod == "api_key")
                    check(panel.Detail.Contains("인증 방식: api_key", StringComparison.Ordinal), "3.2-5 an allowlisted method can be displayed");
                if (sample.NeedsLogin)
                    check(panel.Rows.Single() is { Tone: PanelTone.Muted, UsedPercent: 37 } &&
                        panel.Heading.Text.Contains("이전 조회값", StringComparison.Ordinal), "3.3-2 confirmed CLI login required retains a muted previous reading");
            }
            finally { await h.StopAsync(); }
        }
        check(ParseAuth(0, """{"loggedIn":true,"authMethod":"SYNTHETIC-PRIVATE"}""") == ClaudeAuthStatus.Unknown,
            "3.2-5 unrecognized authentication method is discarded");
    }

    private static async Task DisplayAndCacheAsync(string directory, Action<bool, string> check)
    {
        using (var toggled = new QuotaP0Harness(Database(directory, "generation"), hasWeb: true))
        {
            await toggled.SeedWebCacheAsync(Epoch.AddMinutes(-1));
            var beforeFirstRead = Signal<StateView>();
            toggled.WebRead = async token =>
            {
                beforeFirstRead.TrySetResult(toggled.State(Gemini));
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return WebOk;
            };
            toggled.EnableWeb(true);
            toggled.Start();
            try
            {
                var started = await beforeFirstRead.Task.WaitAsync(Limit);
                check(started is { Rows.Count: 0, WebOutcome: null, LastSuccess: null, Continuity: 1 },
                    "3.1-7 a mode change before Start prevents restoration even when the web cache exists");
            }
            finally { await toggled.StopAsync(); }
        }

        using var h = new QuotaP0Harness(Database(directory, "display"), hasWeb: true);
        await h.SeedCliCacheAsync(Gemini, 84, Epoch.AddMinutes(-5));
        await h.SeedWebCacheAsync(Epoch.AddMinutes(-1));
        string? legacy = h.CacheJson(LegacyGeminiKey);
        h.EnableWeb(true);
        check(h.State(Gemini) is { WebEnabled: true, WebOutcome: null, Rows.Count: 0, Continuity: 1, LastSuccess: null },
            "3.1-7 enabling clears state and never loads any previously stored reading");
        await h.ReadWebAsync();
        var alert = new QuotaEarlyResetAlert(Gemini, 1, 1, Epoch, []);
        check(h.Panel(Gemini, alert).Detail == h.Panel(Gemini).Detail && h.PanelWithOldGeminiReading().Rows.Single().UsedPercent is null,
            "3.3-1 the displayed web reading must have web IDs and no early-reset detail");

        using (var cache = JsonDocument.Parse(h.CacheJson(WebGeminiKey)!))
        {
            var root = cache.RootElement;
            check(root.GetProperty("Version").GetInt32() == 1 && root.GetProperty("ResetAnchors").GetArrayLength() == 0 &&
                (!root.TryGetProperty("ResetAnchorPolicyVersion", out var version) || version.GetInt32() == 0),
                "3.3-3 Gemini web cache is v1 with no anchors and policy version zero");
        }
        var before = h.Panel(Gemini);
        string initialLayout = h.Layout();
        h.Now = Epoch.AddMinutes(1);
        h.WebRead = _ => Task.FromResult(GeminiUsagePageLoad.LoadFailed);
        h.RequestWeb();
        await h.ReadWebAsync();
        var previous = h.Panel(Gemini);
        check(previous.Rows.Select(row => row.UsedPercent).SequenceEqual(before.Rows.Select(row => row.UsedPercent)) &&
            previous.Rows.All(row => row.Tone == PanelTone.Muted) && h.Layout() == initialLayout,
            "3.3-1/7 load failure keeps muted rows and changing text/tone does not change LayoutKey");
        h.WebRead = _ => Task.FromResult(WebChanged);
        h.RequestWeb();
        await h.ReadWebAsync();
        check(h.Panel(Gemini).Rows.Single() is { Text: "형식 변경 · 조회 불가", UsedPercent: null } &&
            h.State(Gemini).Rows.Count == 2 && h.Layout() != initialLayout, "3.3-1/7 FormatChanged keeps internal values but removes displayed rows");
        const string privateMarker = "SYNTHETIC-PRIVATE-CLI-OUTPUT";
        h.WebRead = _ => Task.FromResult(new GeminiUsagePageLoad("https://gemini.google.com/app?token=" + privateMarker, privateMarker));
        h.RequestWeb();
        await h.ReadWebAsync();
        check(h.Panel(Gemini).Rows.Single() is { Text: "로그인 필요 · Gemini 앱 사용량", UsedPercent: null } &&
            h.State(Gemini) is { Rows.Count: 0, LastSuccess: null, NextCheck: null }, "3.3-1 login required clears result and schedule");

        check(h.State(Gemini).WebDiagnostic == "Destination" && !h.Panel(Gemini).Detail.Contains(privateMarker, StringComparison.Ordinal),
            "3.3-6 a web redirect keeps a rule and excludes raw URL, cookie/token-like input, and page text");
        await h.ReadCliAsync(Claude);
        foreach (var error in new Exception[] { new InvalidDataException(privateMarker), new TimeoutException(privateMarker),
            new IOException(privateMarker), new FileNotFoundException(privateMarker), new UnauthorizedAccessException(privateMarker) })
        {
            h.CliRead = (_, _) => Task.FromException<QuotaReading>(error);
            await h.ReadCliAsync(Claude);
            var panel = h.Panel(Claude);
            check(panel.Rows.Single() is { UsedPercent: 37, Tone: PanelTone.Muted } &&
                panel.Heading.Text.Contains("이전 조회값", StringComparison.Ordinal), "3.3-2 a CLI failure preserves the previous number and tone");
            check(!panel.Detail.Contains(privateMarker, StringComparison.Ordinal), "3.3-6 raw CLI error does not enter presentation");
        }
        check(h.CacheJson("AccountQuota.v1.Claude") is { } cliJson &&
            cliJson.Contains("\"Version\":1", StringComparison.Ordinal) && !cliJson.Contains(privateMarker, StringComparison.Ordinal) &&
            !h.LogText("quota-polling-diagnostics-v2.json").Contains(privateMarker, StringComparison.Ordinal) &&
            !h.LogText("quota-early-reset-decisions-v2.jsonl").Contains(privateMarker, StringComparison.Ordinal),
            "3.3-3/6 legacy CLI cache stays at v1 and no raw error reaches cache or diagnostics");
        var normalized = ParseAuth(0, """{"loggedIn":true,"authMethod":"claude.ai","loginExpiresAt":null,"email":"SYNTHETIC-PRIVATE","token":"SYNTHETIC-PRIVATE","orgId":"SYNTHETIC-PRIVATE"}""");
        check(normalized == SignedIn && !normalized.ToString().Contains("SYNTHETIC-PRIVATE", StringComparison.Ordinal),
            "3.2-5/3.3-6 auth response conveys only the documented allowlist fields");

        h.WebRead = _ => Task.FromResult(WebOk);
        h.EnableWeb(true);
        using (var held = await h.PauseCacheAsync())
        {
            Task save = h.ReadWebAsync();
            await WaitAsync(() => h.State(Gemini) is { ReadingNow: true, LastSuccess: not null });
            Task logout = h.LogoutCacheAsync();
            check(h.State(Gemini) is { WebEnabled: false, Rows.Count: 0 } && !logout.IsCompleted,
                "3.3-5 logout disables first while the shared store has a pending accepted write");
            held.Dispose();
            await Task.WhenAll(save, logout).WaitAsync(Limit);
        }
        check(h.CacheJson(WebGeminiKey) is null && h.CacheJson(LegacyGeminiKey) == legacy && h.State(Gemini) is { CacheError: "", ReadingNow: false },
            "3.3-3/5 logout leaves web cache deleted after the save race and preserves the old agy key byte-for-byte");

        check(QuotaProviders.Boxes.Select(p => ((int)p, p.ToString()))
            .SequenceEqual([(0, "Codex"), (1, "Claude"), (2, "Gemini"), (3, "Xai")]), "3.3-8 Provider ordinal and wire names are fixed");
    }
}
