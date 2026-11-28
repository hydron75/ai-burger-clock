using System.Text.Json;
using AppKit;
using CoreGraphics;
using Foundation;
using WebKit;

namespace AiBurgerClock;

// What one Antigravity read does after the page has finished loading, kept free of AppKit so the native smoke can drive it
// with synthetic answers. The shared script is tried right away and then every 250 ms until the reading area is there on
// a visible page, or until the 20 s window ends; leaving the usage page does not end the wait. The URL is cut to scheme,
// host and path before it goes anywhere else, and only the reading area's text is ever taken from the page.
internal static class MacAntigravityPageReading
{
    internal const int RetryMilliseconds = 250;
    internal const int RetryWindowSeconds = 20;
    internal const int TotalTimeoutSeconds = 30;

    // 'window' ends the wait for the reading area (the last answer is returned); 'total' is the caller's cancellation and
    // the whole read's limit, and is passed on as a cancellation.
    internal static async Task<AntigravityUsagePageLoad> ReadAsync(Func<string?> source,
        Func<CancellationToken, Task<string?>> run, Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken window, CancellationToken total)
    {
        AntigravityUsagePageLoad last = default;
        try
        {
            while (true)
            {
                window.ThrowIfCancellationRequested();
                Uri? current = Cut(source());
                if (AntigravityUsagePage.IsUsagePage(current))
                {
                    // An answer counts only when it came from the page that is still shown (a move in between is read again).
                    if (Interpret(await run(window).ConfigureAwait(true)) is { } answer &&
                        answer.FinalUri == current && Cut(source()) == current)
                    {
                        last = answer;
                        if (last is { Text: not null, VisibilityState: not "hidden" }) return last;
                    }
                }
                else last = new(current, null, null, null, TimedOut: false);
                await delay(TimeSpan.FromMilliseconds(RetryMilliseconds), window).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (!total.IsCancellationRequested)
        {
            Uri? final = Cut(source());
            return final == last.FinalUri ? last : new(final, null, null, null, TimedOut: false);
        }
    }

    // The page's answer is {url, text, visibilityState, documentLanguage}.
    internal static AntigravityUsagePageLoad? Interpret(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? Value(string name) =>
                root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;
            return Cut(Value("url")) is { } url
                ? new(url, Value("text"), Value("visibilityState"), Value("documentLanguage"), TimedOut: false) : null;
        }
        catch (JsonException) { return null; }
    }

    internal static Uri? Cut(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? new Uri(parsed.GetLeftPart(UriPartial.Path)) : null;
}

// IAntigravityUsagePageReader for the Mac host. The page draws its values only while it counts as visible, so each read
// puts a fully transparent window on a display that no full-screen app covers (never over one: that shows the menu bar
// there, MACOS_PORT 39), without activating the app, and releases window and web view as soon as the read ends. It uses
// the same web data store as the Gemini app reader (the Gemini sign-in covers both pages) and reads no cookie, token or
// storage.
internal sealed class MacAntigravityUsageReader(Action<Action> runOnMainThread) : IAntigravityUsagePageReader
{
    private const double PageWidth = 1100, PageHeight = 800;
    private int active;

    public Task<AntigravityUsagePageLoad> LoadAntigravityAsync(CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<AntigravityUsagePageLoad>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<AntigravityUsagePageLoad>(cancellationToken);
        Interlocked.Increment(ref active);
        runOnMainThread(async () =>
        {
            try { done.TrySetResult(await LoadOnMainAsync(cancellationToken)); }
            catch (OperationCanceledException) { done.TrySetCanceled(cancellationToken); }
            catch (Exception) { done.TrySetResult(default); } // the shared parser calls this a failed load
            finally { Interlocked.Decrement(ref active); }
        });
        return done.Task;
    }

    private static async Task<AntigravityUsagePageLoad> LoadOnMainAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(MacAntigravityPageReading.TotalTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        linked.Token.ThrowIfCancellationRequested(); // already cancelled: no window, no request
        // The monitor asked MacScreenCoverage.Readiness first; if every display got covered since, no window is opened.
        if (MacScreenCoverage.FreeScreen() is not { } screen) return default;
        NSObject activity = NSProcessInfo.ProcessInfo.BeginActivity(NSActivityOptions.InitiatedAllowingIdleSystemSleep,
            "Antigravity usage page read");
        Session? session = null;
        try
        {
            session = new Session(screen);
            if (!await session.LoadAsync(AntigravityUsagePage.Url, linked.Token))
            {
                Uri? failed = MacAntigravityPageReading.Cut(session.Url);
                return AntigravityUsagePage.IsLoginPage(failed) ? new(failed, null, null, null, TimedOut: false) : default;
            }
            using var window = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            window.CancelAfter(TimeSpan.FromSeconds(MacAntigravityPageReading.RetryWindowSeconds));
            Session shown = session;
            return await MacAntigravityPageReading.ReadAsync(() => shown.Url,
                token => shown.EvaluateAsync(AntigravityUsagePage.ReadScript, token), Task.Delay, window.Token, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // the 30 s limit
            return new(MacAntigravityPageReading.Cut(session?.Url), null, null, null, TimedOut: true);
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

    // The read window: borderless, alpha 0, above ordinary windows, click-through, in every Space but not in Mission
    // Control or the window cycle, and without fullScreenAuxiliary, so it never joins a full-screen Space.
    internal static NSWindow CreateWindow(CGRect area)
    {
        double width = Math.Min(PageWidth, area.Width), height = Math.Min(PageHeight, area.Height);
        var frame = new CGRect(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        var window = new NSWindow(frame, NSWindowStyle.Borderless, NSBackingStore.Buffered, false)
        {
            AlphaValue = 0,
            Level = NSWindowLevel.Status,
            IgnoresMouseEvents = true,
            HasShadow = false,
            CollectionBehavior = NSWindowCollectionBehavior.CanJoinAllSpaces | NSWindowCollectionBehavior.Transient |
                NSWindowCollectionBehavior.IgnoresCycle
        };
        // Owned and released by this code only (a closed window must not release itself a second time).
        window.ReleaseWhenClosed(false);
        return window;
    }

    private sealed class Session : IDisposable
    {
        private readonly NSWindow window;
        private readonly WKWebView web;
        private readonly MacGeminiNavigationDelegate navigation = new();
        private readonly MacGeminiUiDelegate ui = new();
        private readonly TaskCompletionSource<bool> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Session(NSScreen screen)
        {
            window = CreateWindow(screen.VisibleFrame);
            web = new WKWebView(new CGRect(CGPoint.Empty, window.Frame.Size), MacGeminiWebKit.Configuration(restrictPlayback: true))
            {
                NavigationDelegate = navigation,
                UIDelegate = ui
            };
            window.ContentView = web;
            navigation.Finished = () =>
            {
                // No sound from a hidden page, whatever it tries to start.
                web.SetAllMediaPlaybackSuspended(true, () => { });
                loaded.TrySetResult(true);
            };
            navigation.Failed = () => loaded.TrySetResult(false);
            // In front within its level without activating the app or taking key focus.
            window.OrderFrontRegardless();
        }

        internal string? Url => web.Url?.AbsoluteString;

        internal async Task<bool> LoadAsync(string url, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => loaded.TrySetCanceled(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            web.LoadRequest(new NSUrlRequest(new NSUrl(url)));
            return await loaded.Task;
        }

        internal async Task<string?> EvaluateAsync(string script, CancellationToken cancellationToken)
        {
            // EvaluateJavaScript cannot be cancelled: stop waiting for it when the wait ends or the read is cancelled.
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
