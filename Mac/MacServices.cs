using AppKit;
using Foundation;
using ServiceManagement;
using UserNotifications;

namespace AiBurgerClock;

internal sealed class MacNotifications : UNUserNotificationCenterDelegate
{
    private readonly Action<string> feedback;
    private readonly MacNotificationSender sender;
    private readonly MacNotificationPermission permission;
    private NSObject? activationObserver;
    private bool stopped;

    internal MacNotifications(Action<string> feedback, MacNotificationImageStore? images = null)
    {
        this.feedback = feedback;
        MacNotificationImageStore store = images ?? MacNotificationImageStore.Default();
        // Nothing is in flight at start, so any picture file still here is a leftover.
        store.Sweep();
        sender = new(store,
            (request, done) => UNUserNotificationCenter.Current.AddNotificationRequest(request, error => done(error)),
            action => InvokeOnMainThread(action),
            () => MacControls.IsDark(NSApplication.SharedApplication.EffectiveAppearance),
            () =>
            {
                if (!stopped) feedback("알림을 표시하지 못했습니다. macOS 알림 설정을 확인하세요.");
            });
        // The permission can change in System Settings while the app runs: look again when a notification finds it
        // off, and whenever the app is activated (see MacNotificationPermission).
        permission = new(
            answer => UNUserNotificationCenter.Current.GetNotificationSettings(settings =>
            {
                UNAuthorizationStatus status = settings.AuthorizationStatus;
                answer(status is UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional,
                    status != UNAuthorizationStatus.NotDetermined);
            }),
            action => InvokeOnMainThread(action),
            () => feedback("알림이 꺼져 있습니다. macOS 시스템 설정 → 알림에서 허용할 수 있습니다."));
        activationObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.DidBecomeActiveNotification,
            _ => permission.Refresh());
        // Keep this delegate alive: the center stores a weak reference.
        UNUserNotificationCenter.Current.Delegate = this;
    }

    internal void RequestPermission()
    {
        UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert,
            (granted, error) => InvokeOnMainThread(() =>
            {
                if (stopped) return;
                permission.Answered(granted && error is null);
                if (permission.Denied) feedback("알림이 꺼져 있습니다. macOS 시스템 설정 → 알림에서 허용할 수 있습니다.");
            }));
    }

    // True once macOS answered the permission request and the answer was "not allowed".
    internal bool Denied => permission.Denied;

    // appearance is the menu-bar icon's TrayAppearance of this moment (schedule/service notices); quota notices pass null.
    internal void Show(string title, string body, MacNotificationKind kind, TrayAppearance? appearance)
    {
        if (stopped) return;
        permission.Send(() => sender.Send(title, body, kind, appearance));
    }

    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);

    internal void Stop()
    {
        stopped = true;
        permission.Stop();
        if (activationObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(activationObserver);
            activationObserver.Dispose();
            activationObserver = null;
        }
        UNUserNotificationCenter.Current.Delegate = null;
    }
}

internal static class MacAutoStart
{
    internal static (bool Enabled, string Detail) Read()
    {
        return SMAppService.MainApp.Status switch
        {
            SMAppServiceStatus.Enabled => (true, "로그인 시 자동 실행"),
            SMAppServiceStatus.RequiresApproval => (false, "자동 실행: 시스템 설정 승인 필요"),
            SMAppServiceStatus.NotFound => (false, "자동 실행: .app 설치 위치 확인 필요"),
            _ => (false, "로그인 시 자동 실행")
        };
    }

    internal static string Set(bool enabled)
    {
        bool succeeded = enabled
            ? SMAppService.MainApp.Register(out var error)
            : SMAppService.MainApp.Unregister(out error);
        if (!succeeded) return "자동 실행을 변경하지 못했습니다: " + (error?.LocalizedDescription ?? "알 수 없는 오류");
        return SMAppService.MainApp.Status == SMAppServiceStatus.RequiresApproval
            ? "시스템 설정 → 일반 → 로그인 항목에서 AI Burger Clock을 승인하세요."
            : "자동 실행 설정을 변경했습니다.";
    }

    internal static void OpenSettings() => SMAppService.OpenSystemSettingsLoginItems();
}
