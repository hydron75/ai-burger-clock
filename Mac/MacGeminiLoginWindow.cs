using AppKit;
using CoreGraphics;
using Foundation;
using WebKit;

namespace AiBurgerClock;

// The visible window where the user signs in to Google themselves (password, 2-step verification and all). The app does
// not type, read or keep anything: it only watches where the page goes. When the usage page opens the window closes and
// the host switches the reader on; closing the window without that changes nothing. Same data store and defaults as the
// hidden reader, and the same refusals (no new windows, no camera/microphone, no location).
internal sealed class MacGeminiLoginWindow
{
    private readonly Action<Action> runOnMainThread;
    private NSWindow? window;
    private WKWebView? web;
    private MacGeminiNavigationDelegate? navigation;
    private MacGeminiUiDelegate? ui;
    private Action? succeeded;
    // The page went to a sign-in host; when it comes back to Gemini the usage page is opened once to confirm the login.
    private bool leftGemini;
    private bool verifying;

    internal MacGeminiLoginWindow(Action<Action> runOnMainThread) => this.runOnMainThread = runOnMainThread;

    internal bool IsOpen => window is not null;

    internal void Show(Action succeeded)
    {
        if (window is not null)
        {
            window.MakeKeyAndOrderFront(null);
            NSApplication.SharedApplication.Activate();
            return;
        }
        this.succeeded = succeeded;
        leftGemini = verifying = false;
        ui = new();
        navigation = new(stayOnGemini: false);
        web = new WKWebView(new CGRect(0, 0, 980, 760), MacGeminiWebKit.Configuration(restrictPlayback: false))
        {
            NavigationDelegate = navigation,
            UIDelegate = ui
        };
        window = MacGeminiWebKit.CreateWindow(new CGRect(240, 160, 980, 760), "Gemini 로그인", web);
        window.WillClose += (_, _) => Teardown();
        navigation.Navigated = host => { if (host != MacGeminiWebKit.Host) leftGemini = true; };
        navigation.Finished = OnFinished;
        window.Center();
        window.MakeKeyAndOrderFront(null);
        NSApplication.SharedApplication.Activate();
        web.LoadRequest(new NSUrlRequest(new NSUrl(GeminiUsagePage.Url)));
    }

    internal void Close() => window?.Close();

    private void OnFinished()
    {
        NSUrl? url = web?.Url;
        if (url is null) return;
        if (url.Host == MacGeminiWebKit.Host && url.Path == "/usage")
        {
            Action? done = succeeded;
            Close();
            done?.Invoke();
            return;
        }
        if (verifying)
        {
            verifying = false; // the confirmation landed elsewhere: still signed out, the window stays open
            return;
        }
        if (url.Host == MacGeminiWebKit.Host && leftGemini)
        {
            leftGemini = false;
            verifying = true;
            web!.LoadRequest(new NSUrlRequest(new NSUrl(GeminiUsagePage.Url)));
        }
    }

    // After the window closed (by the user or by a successful login): release the web view on the next run-loop turn,
    // never inside the window's own close notification.
    private void Teardown()
    {
        NSWindow? closedWindow = window;
        WKWebView? closedWeb = web;
        MacGeminiNavigationDelegate? closedNavigation = navigation;
        window = null;
        web = null;
        navigation = null;
        ui = null;
        succeeded = null;
        if (closedNavigation is not null)
        {
            closedNavigation.Finished = null;
            closedNavigation.Navigated = null;
        }
        runOnMainThread(() => MacGeminiWebKit.Release(closedWindow, closedWeb));
    }
}
