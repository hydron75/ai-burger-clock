using System.Runtime.InteropServices;

namespace AiBurgerClock;

internal static class WindowsGeminiLoginWindow
{
    private const int ShowNormal = 1;

    internal static bool Show(Form window)
    {
        window.Show();
        if (!window.IsHandleCreated) return false;
        IntPtr handle = window.Handle;
        // STARTUPINFO can suppress the first ShowWindow call in a hidden launch.
        if (!IsWindowVisible(handle)) ShowWindow(handle, ShowNormal);
        if (!IsWindowVisible(handle)) return false;
        window.BringToFront();
        window.Activate();
        return IsWindowVisible(handle);
    }

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
