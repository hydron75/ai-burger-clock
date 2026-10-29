using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.UI.Notifications;

namespace AiBurgerClock;

// Explicit developer-only native integration commands. Fixed TEST identity only;
// they are never reached from --self-test/--smoke-test or production notification dispatch.
internal static class WindowsQuotaToastIntegration
{
    internal static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-burger-clock-tests", "quota-toast");
    internal static string CallbackPath => Path.Combine(DirectoryPath, "activation.jsonl");
    internal static void RecordCallback()
    {
        Directory.CreateDirectory(DirectoryPath);
        string entry = JsonSerializer.Serialize(new { AtUtc = DateTimeOffset.UtcNow, Pid = Environment.ProcessId, Callback = true });
        File.AppendAllText(CallbackPath, entry + "\n");
    }

    internal static int Run(string command)
    {
        var id = WindowsQuotaToastIdentity.Test;
        try
        {
            switch (command)
            {
                case "register": WindowsQuotaToastRegistration.Register(id); Console.WriteLine("TEST registration only: " + id.Aumid); return 0;
                case "unregister":
                    WindowsQuotaToastRegistration.ValidateForRemoval(id);
                    // Clear is scoped ONLY to the immutable TEST AUMID, never production or another app.
                    ToastNotificationManager.History.Clear(id.Aumid);
                    WindowsQuotaToastRegistration.Unregister(id, false); return 0;
                case "unregister-dry-run": WindowsQuotaToastRegistration.Unregister(id, true); return 0;
                case "send":
                    var sender = new WindowsQuotaToastSender(id);
                    sender.Send(new QuotaNotificationAlert(QuotaProvider.Codex, 1, "AI Burger Clock quota TEST",
                        "Synthetic notification: verify banner dismissal and notification-center retention.", null, null));
                    Console.WriteLine("TEST toast submitted; no account lookup.");
                    // Keep native Failed event observable briefly, not a delivery assertion delay.
                    Thread.Sleep(1000); return 0;
                case "history":
                    Console.WriteLine(JsonSerializer.Serialize(new { TestAumid = id.Aumid,
                        Count = ToastNotificationManager.History.GetHistory(id.Aumid).Count })); return 0;
                case "host":
                    if (!WindowsQuotaToastRegistration.IsRegistered(id)) return 3;
                    using (var server = WindowsQuotaToastActivation.Start(id, RecordCallback))
                    using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\AiBurgerClock-QuotaToastTestStop"))
                    { Console.WriteLine("TEST callback host ready; no tray, DB, services or normal mutex."); return stop.WaitOne(TimeSpan.FromMinutes(5)) ? 0 : 4; }
                case "stop-host":
                    using (var stop = EventWaitHandle.OpenExisting("Local\\AiBurgerClock-QuotaToastTestStop")) stop.Set(); return 0;
                case "invoke":
                case "invoke-unknown":
                    // Exercises real COM local-server activation, NOT a simulated notification click.
                    Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 2));
                    try
                    {
                    Guid clsid = id.Clsid, iid = command == "invoke-unknown" ? new("00000000-0000-0000-C000-000000000046") : typeof(IWindowsQuotaNotificationCallback).GUID;
                    Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out var pointer));
                    try
                    {
                        var callback = (IWindowsQuotaNotificationCallback)Marshal.GetObjectForIUnknown(pointer);
                        try { Marshal.ThrowExceptionForHR(callback.Activate(id.Aumid, "synthetic", IntPtr.Zero, 0)); }
                        finally { Marshal.FinalReleaseComObject(callback); }
                    }
                    finally { Marshal.Release(pointer); }
                    Console.WriteLine("TEST COM callback acknowledged."); return 0;
                    }
                    finally { CoUninitialize(); }
                default: Console.Error.WriteLine("Unknown quota toast test command."); return 2;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Quota toast TEST failed: {error.GetType().Name}, HRESULT=0x{error.HResult:X8}");
            Console.Error.WriteLine(error.StackTrace); // Synthetic test path only; no notification/account contents.
            return 1;
        }
    }
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint concurrency);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
