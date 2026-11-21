using AppKit;
using CoreGraphics;
using Foundation;
using WebKit;

namespace AiBurgerClock;

// Shared WKWebView plumbing of the Gemini app usage reader (0.9.6): the app's own web data store, the delegates that
// refuse everything the page may ask for, and the window teardown. No cookie, token or storage API is used anywhere:
// the web view only loads the page, the app never reads what the login left in the data store.
internal static class MacGeminiWebKit
{
    internal const string Host = "gemini.google.com";

    // Fixed identifier of the app's own persistent data store (a constant of this app, not an account value). Logout
    // removes the store by this identifier. The user agent and every other setting stay at the WKWebView defaults.
    internal static readonly NSUuid DataStoreId = new("5c1b0e2a-7d3f-4b8e-9a61-2f4d8e0b7c13");

    internal static WKWebViewConfiguration Configuration(bool restrictPlayback)
    {
        var configuration = new WKWebViewConfiguration { WebsiteDataStore = WKWebsiteDataStore.Create(DataStoreId) };
        // Hidden reads never start audio or video on their own (no autoplay without a user gesture).
        if (restrictPlayback) configuration.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.All;
        return configuration;
    }

    internal static NSWindow CreateWindow(CGRect frame, string title, WKWebView web)
    {
        var window = new NSWindow(frame, NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered, false) { Title = title, ContentView = web };
        // Owned and released by this code only (a closed window must not release itself a second time).
        window.ReleaseWhenClosed(false);
        return window;
    }

    internal static void Release(NSWindow? window, WKWebView? web)
    {
        if (web is not null)
        {
            web.StopLoading();
            web.NavigationDelegate = null!;
            web.UIDelegate = null!;
        }
        if (window is not null)
        {
            window.OrderOut(null);
            window.ContentView = null!;
            window.Close();
        }
        web?.Dispose();
        window?.Dispose();
    }
}

// Everything the page may ask for is refused: new windows (window.open, target=_blank), camera/microphone capture and
// geolocation. JavaScript dialogs and file choosers are not implemented, so WebKit cancels them.
internal sealed class MacGeminiUiDelegate : WKUIDelegate
{
    public override WKWebView CreateWebView(WKWebView webView, WKWebViewConfiguration configuration,
        WKNavigationAction navigationAction, WKWindowFeatures windowFeatures) => null!;

    public override void RequestMediaCapturePermission(WKWebView webView, WKSecurityOrigin origin, WKFrameInfo frame,
        WKMediaCaptureType type, Action<WKPermissionDecision> decisionHandler) => decisionHandler(WKPermissionDecision.Deny);
}

// Main-frame navigation guard and completion callbacks. Only https is loaded; every https main-frame move is followed
// (Google may refresh a signed-in session through accounts.google.com and come back, so a hidden read must not stop it).
internal sealed class MacGeminiNavigationDelegate : WKNavigationDelegate
{
    internal Action? Finished;
    internal Action? Failed;
    internal Action<string>? Navigated;

    public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction,
        Action<WKNavigationActionPolicy> decisionHandler)
    {
        NSUrl? url = navigationAction.Request.Url;
        bool mainFrame = navigationAction.TargetFrame?.MainFrame ?? true;
        if (!mainFrame)
        {
            decisionHandler(WKNavigationActionPolicy.Allow);
            return;
        }
        if (url is null || url.Scheme != "https")
        {
            decisionHandler(WKNavigationActionPolicy.Cancel);
            return;
        }
        Navigated?.Invoke(url.Host ?? "");
        decisionHandler(WKNavigationActionPolicy.Allow);
    }

    public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation) => Finished?.Invoke();

    public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error) => Fail(error);

    public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error) => Fail(error);

    public override void ContentProcessDidTerminate(WKWebView webView) => Failed?.Invoke();

    // A cancel (our own policy decision or a newer load replacing this one) is not a failure.
    private void Fail(NSError error)
    {
        bool cancelled = (error.Domain == NSError.NSUrlErrorDomain && error.Code == -999) ||
            (error.Domain == "WebKitErrorDomain" && error.Code == 102);
        if (!cancelled) Failed?.Invoke();
    }
}
