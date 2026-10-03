using AppKit;
using Foundation;
using ServiceManagement;
using UserNotifications;

namespace AiBurgerClock;

internal sealed class MacNotifications : UNUserNotificationCenterDelegate
{
    private readonly Action<string> feedback;
    private bool authorized;
    private bool stopped;
    private long serial;

    internal MacNotifications(Action<string> feedback)
    {
        this.feedback = feedback;
        // Keep this delegate alive: the center stores a weak reference.
        UNUserNotificationCenter.Current.Delegate = this;
    }

    internal void RequestPermission()
    {
        UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert,
            (granted, error) => InvokeOnMainThread(() =>
            {
                if (stopped) return;
                authorized = granted && error is null;
                if (!authorized) feedback("알림이 꺼져 있습니다. macOS 시스템 설정 → 알림에서 허용할 수 있습니다.");
            }));
    }

    internal void Show(string title, string body)
    {
        if (stopped || !authorized) return;
        using var content = new UNMutableNotificationContent { Title = title, Body = body };
        using var request = UNNotificationRequest.FromIdentifier("aiburgerclock-" + ++serial, content, null);
        UNUserNotificationCenter.Current.AddNotificationRequest(request, error =>
        {
            if (error is not null) InvokeOnMainThread(() =>
            {
                if (!stopped) feedback("알림을 표시하지 못했습니다. macOS 알림 설정을 확인하세요.");
            });
        });
    }

    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);

    internal void Stop()
    {
        stopped = true;
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
