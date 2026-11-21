using System.Text.Json;
using AppKit;
using CoreGraphics;
using Foundation;
using WebKit;

namespace AiBurgerClock;

// What one hidden read does after the page has finished loading, kept free of AppKit so the native smoke can drive it
// with synthetic answers. The script is tried right away and then every 250 ms until 5 s have passed. Leaving the usage
// page does not end it: Google may pass through another host and come back, so the read keeps looking for the two
// sections for the whole window and otherwise returns the last page it saw. The whole read (load + retries) is limited to
// 30 s by the caller.
internal static class MacGeminiPageReading
{
    internal const int RetryMilliseconds = 250;
    internal const int RetryWindowMilliseconds = 5000;
    internal const int TotalTimeoutSeconds = 30;

    internal static async Task<GeminiUsagePageLoad> ReadAsync(Func<CancellationToken, Task<string?>> run,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        GeminiUsagePageLoad? page = null;
        for (int attempt = 0; attempt <= RetryWindowMilliseconds / RetryMilliseconds; attempt++)
        {
            if (attempt > 0) await delay(TimeSpan.FromMilliseconds(RetryMilliseconds), cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            GeminiUsagePageLoad current = Interpret(await run(cancellationToken).ConfigureAwait(true));
            // An answer that cannot be read (the page is in the middle of a move) does not replace the last page seen.
            if (current.FinalUrl is null) continue;
            page = current;
            if (IsComplete(current)) return current;
        }
        return page ?? GeminiUsagePageLoad.LoadFailed;
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

    // Done only when the usage page shows both sections with their values and reset times: the 5-hour part before the
    // "Weekly limit" heading and the weekly part after it each need "% used" and "Resets". A page that is still filling
    // in (a heading with nothing under it yet) is read again. Anything else (an incomplete usage page, or another address)
    // is retried until the 5 s are up and then returned as it is; the shared parser then calls the first a format change
    // and the second sign-in needed. Time does not matter here, so the check needs no clock.
    private static bool IsComplete(GeminiUsagePageLoad load)
    {
        if (!IsUsagePage(load.FinalUrl) || load.Text is not { } text) return false;
        int weekly = text.IndexOf("Weekly limit", StringComparison.Ordinal);
        if (weekly < 0) return false;
        static bool Filled(string part) =>
            part.Contains("% used", StringComparison.Ordinal) && part.Contains("Resets", StringComparison.Ordinal);
        return Filled(text[..weekly]) && Filled(text[weekly..]);
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
            if (!await session.LoadAsync(url, linked.Token)) return GeminiUsagePageLoad.LoadFailed;
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
        private readonly NSWindow window;
        private readonly WKWebView web;
        private readonly MacGeminiNavigationDelegate navigation = new();
        private readonly MacGeminiUiDelegate ui = new();
        private readonly TaskCompletionSource<bool> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
                loaded.TrySetResult(true);
            };
            navigation.Failed = () => loaded.TrySetResult(false);
        }

        internal async Task<bool> LoadAsync(string url, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => loaded.TrySetCanceled(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            web.LoadRequest(new NSUrlRequest(new NSUrl(url)));
            return await loaded.Task;
        }

        internal async Task<string?> EvaluateAsync(string script, CancellationToken cancellationToken)
        {
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
            MacGeminiWebKit.Release(window, web);
        }
    }
}
