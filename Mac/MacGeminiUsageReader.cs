using System.Text.Json;
using AppKit;
using CoreGraphics;
using Foundation;
using WebKit;

namespace AiBurgerClock;

// What one hidden read does after the page has finished loading, kept free of AppKit so the native smoke can drive it
// with synthetic answers. The script is tried right away and then every 250 ms until 5 s have passed; a redirect away
// from the usage page ends it at once. The whole read (load + retries) is limited to 30 s by the caller.
internal static class MacGeminiPageReading
{
    internal const int RetryMilliseconds = 250;
    internal const int RetryWindowMilliseconds = 5000;
    internal const int TotalTimeoutSeconds = 30;

    internal static async Task<GeminiUsagePageLoad> ReadAsync(Func<CancellationToken, Task<string?>> run,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        GeminiUsagePageLoad last = GeminiUsagePageLoad.LoadFailed;
        for (int attempt = 0; attempt <= RetryWindowMilliseconds / RetryMilliseconds; attempt++)
        {
            if (attempt > 0) await delay(TimeSpan.FromMilliseconds(RetryMilliseconds), cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            last = Interpret(await run(cancellationToken).ConfigureAwait(true));
            if (IsFinal(last)) return last;
        }
        return last;
    }

    // The page's answer is {url, text}. The URL is cut to scheme, host and path before it goes anywhere else.
    internal static GeminiUsagePageLoad Interpret(string? json)
    {
        if (string.IsNullOrEmpty(json)) return GeminiUsagePageLoad.LoadFailed;
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("url", out JsonElement url) ||
                url.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? parsed))
                return GeminiUsagePageLoad.LoadFailed;
            string? text = root.TryGetProperty("text", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            return new(parsed.GetLeftPart(UriPartial.Path), text);
        }
        catch (JsonException) { return GeminiUsagePageLoad.LoadFailed; }
    }

    internal static bool IsUsagePage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) && parsed.Scheme == Uri.UriSchemeHttps &&
        parsed.Host == MacGeminiWebKit.Host && parsed.AbsolutePath == "/usage";

    // Done when the page is somewhere else (no point waiting) or when the usage text has both sections. A usage page
    // without them is retried until the 5 s are up and then returned as it is (the parser calls that a format change).
    private static bool IsFinal(GeminiUsagePageLoad load)
    {
        if (load.FinalUrl is null) return false;
        if (!IsUsagePage(load.FinalUrl)) return true;
        return load.Text is { } text && text.Contains("Weekly limit", StringComparison.Ordinal) &&
            text.Contains("% used", StringComparison.Ordinal);
    }
}

// IGeminiUsagePageReader for the Mac host. Every read builds one hidden window with one WKWebView on the app's own
// data store, loads the usage page, reads the text through the shared script and releases both again. It runs on the
// main thread, keeps App Nap away while it works and honours cancellation.
internal sealed class MacGeminiUsageReader(Action<Action> runOnMainThread) : IGeminiUsagePageReader
{
    private int active;

    public Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<GeminiUsagePageLoad>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<GeminiUsagePageLoad>(cancellationToken);
        Interlocked.Increment(ref active);
        runOnMainThread(async () =>
        {
            try { done.TrySetResult(await LoadOnMainAsync(url, script, cancellationToken)); }
            catch (OperationCanceledException) { done.TrySetCanceled(cancellationToken); }
            catch (Exception) { done.TrySetResult(GeminiUsagePageLoad.LoadFailed); }
            finally { Interlocked.Decrement(ref active); }
        });
        return done.Task;
    }

    private static async Task<GeminiUsagePageLoad> LoadOnMainAsync(string url, string script, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(MacGeminiPageReading.TotalTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        linked.Token.ThrowIfCancellationRequested(); // already cancelled: no window, no request
        NSObject activity = NSProcessInfo.ProcessInfo.BeginActivity(NSActivityOptions.InitiatedAllowingIdleSystemSleep,
            "Gemini usage page read");
        Session? session = null;
        try
        {
            session = new Session();
            Session.Result navigation = await session.LoadAsync(url, linked.Token);
            if (navigation.Left is { } destination) return new(destination, null);
            if (!navigation.Finished) return GeminiUsagePageLoad.LoadFailed;
            return await MacGeminiPageReading.ReadAsync(token => session.EvaluateAsync(script, token), Task.Delay, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GeminiUsagePageLoad.LoadFailed; // the 30 s limit
        }
        finally
        {
            session?.Dispose();
            NSProcessInfo.ProcessInfo.EndActivity(activity);
        }
    }

    // Waits until no read owns a web view any more (logout removes the data store only after that).
    internal async Task WaitIdleAsync(TimeSpan limit)
    {
        DateTime end = DateTime.UtcNow + limit;
        while (Volatile.Read(ref active) > 0 && DateTime.UtcNow < end) await Task.Delay(50);
    }

    // Logout: remove the app's own data store (login session, cookies, storage) by its identifier. The caller closes the
    // login window and waits for the reads first; the store cannot be removed while a web view still uses it.
    // False when the store is still listed afterwards.
    internal async Task<bool> RemoveDataStoreAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            // Release the managed wrappers of closed web views and stores.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (!await IsListedAsync()) return true; // never created, or already gone
            try { await WKWebsiteDataStore.RemoveAsync(MacGeminiWebKit.DataStoreId); }
            catch (Exception) { /* still in use or a transient error: checked below */ }
            if (!await IsListedAsync()) return true;
            if (attempt == 2) await WipeDataAsync();
            await Task.Delay(500);
        }
        return false;
    }

    private static async Task<bool> IsListedAsync()
    {
        // Listing the stores as the very first WebKit call of the process crashes the app (checked: SIGSEGV inside WebKit's
        // fetchAllDataStoreIdentifiers). A throw-away in-memory store starts WebKit up first and leaves nothing on disk.
        using (WKWebsiteDataStore.NonPersistentDataStore) { }
        NSUuid?[] identifiers = (await WKWebsiteDataStore.FetchAllDataStoreIdentifiersAsync()).ToArray();
        return identifiers.Any(identifier => identifier is not null && identifier.IsEqual(MacGeminiWebKit.DataStoreId));
    }

    // Fallback when the store itself will not go away: empty it, then try to remove it again.
    private static async Task WipeDataAsync()
    {
        try
        {
            using WKWebsiteDataStore store = WKWebsiteDataStore.Create(MacGeminiWebKit.DataStoreId);
            await store.RemoveDataOfTypesAsync(WKWebsiteDataStore.AllWebsiteDataTypes, NSDate.FromTimeIntervalSince1970(0));
        }
        catch (Exception) { /* the next attempt reports the outcome */ }
    }

    private sealed class Session : IDisposable
    {
        internal readonly record struct Result(bool Finished, string? Left);

        private readonly NSWindow window;
        private readonly WKWebView web;
        private readonly MacGeminiNavigationDelegate navigation = new(stayOnGemini: true);
        private readonly MacGeminiUiDelegate ui = new();
        private string? blockedDestination;
        private readonly TaskCompletionSource<Result> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Session()
        {
            web = new WKWebView(new CGRect(0, 0, 1100, 800), MacGeminiWebKit.Configuration(restrictPlayback: true))
            {
                NavigationDelegate = navigation,
                UIDelegate = ui
            };
            // A window of the size the page was checked with, ordered out and never shown (checked with a hidden window).
            window = MacGeminiWebKit.CreateWindow(new CGRect(200, 200, 1100, 800), "Gemini", web);
            window.OrderOut(null);
            navigation.Finished = () =>
            {
                // No sound from a hidden page, whatever it tries to start.
                web.SetAllMediaPlaybackSuspended(true, () => { });
                loaded.TrySetResult(new(true, null));
            };
            navigation.Failed = () => loaded.TrySetResult(new(false, null));
            navigation.Left = destination =>
            {
                blockedDestination = destination;
                loaded.TrySetResult(new(false, destination));
            };
        }

        internal async Task<Result> LoadAsync(string url, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => loaded.TrySetCanceled(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            web.LoadRequest(new NSUrlRequest(new NSUrl(url)));
            return await loaded.Task;
        }

        // A move away from gemini.google.com that was blocked after the page had finished loading (a script redirect)
        // still ends the read as "somewhere else": the page itself stays on the usage address because the move was refused.
        internal async Task<string?> EvaluateAsync(string script, CancellationToken cancellationToken)
        {
            if (blockedDestination is { } destination)
                return "{\"url\":\"" + destination.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\"text\":null}";
            // EvaluateJavaScript cannot be cancelled: stop waiting for it when the read is cancelled or times out.
            Task<NSObject?> evaluation = web.EvaluateJavaScriptAsync(script)!;
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(() => cancelled.TrySetResult());
            if (await Task.WhenAny(evaluation, cancelled.Task) != evaluation)
            {
                _ = evaluation.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                cancellationToken.ThrowIfCancellationRequested();
            }
            try { return (await evaluation as NSString)?.ToString(); }
            catch (NSErrorException) { return null; }
        }

        public void Dispose()
        {
            navigation.Finished = navigation.Failed = null;
            navigation.Left = null;
            MacGeminiWebKit.Release(window, web);
        }
    }
}
