using System.Runtime.InteropServices;
using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AiBurgerClock;

internal sealed class WindowsQuotaToastSender(WindowsQuotaToastIdentity identity)
{
    private static long nativeSubmissionAttempts;
    internal static long NativeSubmissionAttempts => Interlocked.Read(ref nativeSubmissionAttempts);
    private readonly Queue<ToastNotification> retained = new();
    private readonly WindowsNotificationImages images = new();
    internal Action<WindowsNotificationRequest, int>? Failed { get; set; }
    internal Action<WindowsNotificationRequest>? Completed { get; set; }

    internal void Send(QuotaNotificationAlert alert) => Send(WindowsNotificationRequest.FromQuota(alert));

    internal void Send(WindowsNotificationRequest alert)
    {
        Interlocked.Increment(ref nativeSubmissionAttempts);
        var notifier = ToastNotificationManager.CreateToastNotifier(identity.Aumid);
        var setting = ReadSetting(() => notifier.Setting);
        CheckReady(setting, WindowsQuotaToastRegistration.IsRegistered(identity), identity.IsTest || WindowsQuotaToastActivation.ProductionActive);
        var xml = new XmlDocument();
        xml.LoadXml(Content(alert, alert.IsQuota ? null : images.Get(alert.Icon)));
        var toast = new ToastNotification(xml) { Tag = alert.Tag, Group = alert.Group };
        toast.Failed += (_, args) => { Failed?.Invoke(alert, args.ErrorCode.HResult); Completed?.Invoke(alert); };
        toast.Dismissed += (_, _) => Completed?.Invoke(alert);
        toast.Activated += (_, _) => Completed?.Invoke(alert);
        // Keep projected event sources alive, bounded independently of Windows' history.
        retained.Enqueue(toast);
        while (retained.Count > 64) retained.Dequeue();
        notifier.Show(toast);
    }

    internal static void CheckReady(NotificationSetting? setting, bool registered, bool callbackActive)
    {
        // Check a known Windows block FIRST, even when our registration/server is broken.
        // Otherwise a failed COM startup could accidentally bypass the user's choice via balloon.
        if (setting is not null && setting != NotificationSetting.Enabled)
            throw new WindowsToastBlockedException(setting switch
            {
                NotificationSetting.DisabledForApplication => WindowsToastBlockReason.DisabledForApplication,
                NotificationSetting.DisabledForUser => WindowsToastBlockReason.DisabledForUser,
                NotificationSetting.DisabledByGroupPolicy => WindowsToastBlockReason.DisabledByGroupPolicy,
                _ => WindowsToastBlockReason.DisabledByManifest
            });
        if (!registered) throw new InvalidOperationException("Quota toast registration unavailable.");
        if (!callbackActive) throw new InvalidOperationException("Quota callback server unavailable.");
    }

    internal static NotificationSetting? ReadSetting(Func<NotificationSetting> read)
    {
        try { return read(); }
        // Windows cannot report Setting for an identity that has never sent a toast.
        // Do not emit a hidden bootstrap notification just to read this property.
        // Submit the real notification once; Windows itself still enforces all policy.
        catch (COMException error) when (error.HResult == unchecked((int)0x80070490)) { return null; }
    }

    internal static string Content(string title, string body) => Content(new(WindowsQuotaNotificationKind.Recovery,
        WindowsNotificationProvider.OpenAI, title, body));

    internal static string Content(WindowsNotificationRequest alert, string? imageUri = null)
    {
        string image = !alert.IsQuota && Uri.TryCreate(imageUri, UriKind.Absolute, out var uri) && uri.IsFile
            ? "<image placement=\"appLogoOverride\" src=\"" + SecurityElement.Escape(uri.AbsoluteUri) + "\"/>" : "";
        return "<toast activationType=\"background\"><header id=\"" + alert.Group + "\" title=\"" + alert.Header +
            "\" arguments=\"" + alert.Group + "\" activationType=\"foreground\"/><visual><binding template=\"ToastGeneric\"><text>" +
            SecurityElement.Escape(alert.Title) + "</text><text>" + SecurityElement.Escape(alert.Body) +
            "</text>" + image + "</binding></visual></toast>";
    }
}

// This COM server has NO dependency on TrayApplicationContext, UsageStore,
// ApplicationConfiguration, CLI clients, automatic startup or the normal mutex.
// Its entire activation operation is acknowledging INotificationActivationCallback.
internal sealed class WindowsQuotaToastActivation : IDisposable
{
    private static int productionActive;
    internal static bool ProductionActive => Volatile.Read(ref productionActive) != 0;
    private readonly ManualResetEventSlim stop = new();
    private readonly ManualResetEventSlim ready = new();
    private readonly ManualResetEventSlim invoked = new();
    private readonly Thread worker;
    private Exception? failure;
    private readonly WindowsQuotaToastIdentity identity;
    private readonly Action? callbackObserved;

    private WindowsQuotaToastActivation(WindowsQuotaToastIdentity identity, Action? callbackObserved)
    {
        this.identity = identity;
        this.callbackObserved = callbackObserved;
        worker = new Thread(RunServer) { IsBackground = true, Name = "Quota notification COM callback" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(10))) { stop.Set(); throw new TimeoutException("COM callback initialization timed out."); }
        if (failure is not null) { worker.Join(); throw new InvalidOperationException("COM callback initialization failed.", failure); }
    }

    internal static WindowsQuotaToastActivation Start(WindowsQuotaToastIdentity identity, Action? callbackObserved = null) => new(identity, callbackObserved);

    internal static WindowsQuotaToastActivation? StartIfRegistered(WindowsQuotaToastIdentity identity)
    {
        if (!WindowsQuotaToastRegistration.IsRegistered(identity)) return null;
        try { return Start(identity); }
        catch (Exception error) when (error is InvalidOperationException or COMException or TimeoutException) { return null; }
    }

    // Called by Program BEFORE normal mutex creation or any UI/DB/startup initialization.
    internal static int RunOnly(WindowsQuotaToastIdentity identity)
    {
        if (!WindowsQuotaToastRegistration.IsRegistered(identity)) return 3;
        try
        {
            using var server = Start(identity, identity.IsTest ? WindowsQuotaToastIntegration.RecordCallback : null);
            if (identity.IsTest) Console.WriteLine("TEST activation callback server ready; no normal startup.");
            return server.invoked.Wait(TimeSpan.FromSeconds(30)) ? 0 : 4;
        }
        catch { return 5; } // No raw activation arguments or account data are written.
    }

    private void RunServer()
    {
        uint cookie = 0;
        bool initialized = false;
        bool active = false;
        try
        {
            Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0)); // MTA receives RPC callbacks without a UI pump.
            initialized = true;
            var callback = new WindowsQuotaToastCallback(identity.Aumid, () =>
            {
                try { callbackObserved?.Invoke(); }
                finally { invoked.Set(); }
            });
            var factory = new WindowsQuotaToastClassFactory(callback);
            Guid clsid = identity.Clsid;
            Marshal.ThrowExceptionForHR(CoRegisterClassObject(ref clsid, factory, 4, 1, out cookie));
            if (!identity.IsTest) { Interlocked.Increment(ref productionActive); active = true; }
            ready.Set();
            stop.Wait();
            if (cookie != 0) { CoRevokeClassObject(cookie); cookie = 0; }
            GC.KeepAlive(factory); GC.KeepAlive(callback);
        }
        catch (Exception error) { failure = error; ready.Set(); }
        finally
        {
            if (active) Interlocked.Decrement(ref productionActive);
            if (cookie != 0) CoRevokeClassObject(cookie);
            if (initialized) CoUninitialize();
        }
    }

    public void Dispose()
    {
        stop.Set(); worker.Join();
        stop.Dispose(); ready.Dispose(); invoked.Dispose();
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint concurrency);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoRegisterClassObject(ref Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint cookie);
    [DllImport("ole32.dll")] private static extern int CoRevokeClassObject(uint cookie);
}

[ComVisible(true), Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWindowsQuotaNotificationCallback
{
    [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, IntPtr userInput, uint inputCount);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class WindowsQuotaToastCallback : IWindowsQuotaNotificationCallback
{
    private readonly string expectedAumid;
    private readonly Action acknowledge;
    internal WindowsQuotaToastCallback(string expectedAumid, Action acknowledge)
    { this.expectedAumid = expectedAumid; this.acknowledge = acknowledge; }
    public int Activate(string appUserModelId, string arguments, IntPtr userInput, uint inputCount)
    {
        if (!string.Equals(appUserModelId, expectedAumid, StringComparison.Ordinal)) return unchecked((int)0x80070057);
        // Intentionally ignore all launch args/user input. No normal app activation or writes.
        try { acknowledge(); return 0; }
        catch { return unchecked((int)0x80004005); }
    }
}

[ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWindowsQuotaClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class WindowsQuotaToastClassFactory : IWindowsQuotaClassFactory
{
    private readonly IWindowsQuotaNotificationCallback callback;
    internal WindowsQuotaToastClassFactory(IWindowsQuotaNotificationCallback callback) => this.callback = callback;
    public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outer != IntPtr.Zero) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
        if (iid != typeof(IWindowsQuotaNotificationCallback).GUID && iid != new Guid("00000000-0000-0000-C000-000000000046"))
            return unchecked((int)0x80004002);
        IntPtr unknown = Marshal.GetIUnknownForObject(callback);
        try { return Marshal.QueryInterface(unknown, in iid, out instance); }
        finally { Marshal.Release(unknown); }
    }
    public int LockServer(bool locked) => 0;
}
