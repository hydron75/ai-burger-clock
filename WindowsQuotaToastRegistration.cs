using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace AiBurgerClock;

internal sealed record WindowsQuotaToastIdentity(string Aumid, Guid Clsid, string ShortcutName, bool IsTest)
{
    internal static readonly WindowsQuotaToastIdentity Production = new("Hydron75.AIBurgerClock.Quota",
        new("10B09E94-A6B4-4ABA-9B9F-C1551C7A409B"), "AI Burger Clock - quota notifications.lnk", false);
    internal static readonly WindowsQuotaToastIdentity Test = new("Hydron75.AIBurgerClock.Quota.Test",
        new("5FEEEA89-C21D-45B1-9946-4A69A7BD3625"), "AI Burger Clock - quota TEST.lnk", true);
    internal string ActivationArguments => "--quota-toast-activated" + (IsTest ? " --quota-toast-test" : "");
}

// Persistent registration is EXPLICIT, never part of normal startup, self-test or smoke.
// All paths are derived from the real host EXE, not Assembly.Location (empty in a single file).
internal static class WindowsQuotaToastRegistration
{
    private const string Owner = "AiBurgerClock.QuotaToast.v1";
    internal static string CurrentExecutable => Path.GetFullPath(Environment.ProcessPath ?? throw new InvalidOperationException("Executable unavailable."));
    internal static string ShortcutPath(WindowsQuotaToastIdentity id) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), id.ShortcutName);
    private static string AppKey(WindowsQuotaToastIdentity id) => @"Software\Classes\AppUserModelId\" + id.Aumid;
    private static string ClassKey(WindowsQuotaToastIdentity id) => @"Software\Classes\CLSID\" + id.Clsid.ToString("B");
    internal static string Command(WindowsQuotaToastIdentity id, string executable) => $"\"{executable}\" {id.ActivationArguments}";

    internal static bool IsDeploymentPath(string executable)
    {
        if (!Path.IsPathFullyQualified(executable)) return false;
        string full = Path.GetFullPath(executable);
        string[] parts = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) || part.Equals("artifacts", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".ai-burger-clock-tests", StringComparison.OrdinalIgnoreCase))) return false;
        var directory = new DirectoryInfo(Path.GetDirectoryName(full)!);
        return Path.GetFileName(full).Equals("AI Burger Clock.exe", StringComparison.OrdinalIgnoreCase) &&
            directory.Name.Equals("win-x64", StringComparison.OrdinalIgnoreCase) &&
            directory.Parent?.Name.Equals("dist", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static void RequireNoReparse(string path)
    {
        for (string? cursor = Path.GetFullPath(path); cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Notification registration refuses redirected paths.");
    }

    internal static bool IsRegistered(WindowsQuotaToastIdentity id)
    {
        try { ValidateOwned(id, CurrentExecutable, requireComplete: true); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or COMException or System.Security.SecurityException)
        { return false; }
    }

    private static bool ValidateOwned(WindowsQuotaToastIdentity id, string executable, bool requireComplete)
    {
        RequireNoReparse(executable);
        string shortcut = ShortcutPath(id);
        RequireNoReparse(shortcut);
        using var app = Registry.CurrentUser.OpenSubKey(AppKey(id));
        using var cls = Registry.CurrentUser.OpenSubKey(ClassKey(id));
        using var server = cls?.OpenSubKey("LocalServer32");
        bool present = File.Exists(shortcut) || app is not null || cls is not null;
        if (app is not null && (!Owned(app, executable) ||
            !string.Equals(app.GetValue("CustomActivator") as string, id.Clsid.ToString("B"), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("AUMID registration is not owned by this executable.");
        if (cls is not null && (!Owned(cls, executable) || server is null ||
            !string.Equals(server.GetValue(null) as string, Command(id, executable), StringComparison.OrdinalIgnoreCase) ||
            cls.GetSubKeyNames().Any(name => !name.Equals("LocalServer32", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("COM registration is not owned by this executable.");
        if (File.Exists(shortcut) && !WindowsToastShortcut.Matches(shortcut, executable, id))
            throw new InvalidOperationException("Start menu shortcut is not owned by this executable.");
        if (requireComplete && (!File.Exists(shortcut) || app is null || cls is null))
            throw new InvalidOperationException("Quota notification registration is incomplete.");
        return present;
    }

    private static bool Owned(RegistryKey key, string executable) => key.GetValue("Owner") as string == Owner &&
        string.Equals(key.GetValue("ExecutablePath") as string, executable, StringComparison.OrdinalIgnoreCase);

    internal static void Register(WindowsQuotaToastIdentity id)
    {
        string exe = CurrentExecutable;
        if (!id.IsTest && !IsDeploymentPath(exe))
            throw new InvalidOperationException("Production registration requires the deployed dist/win-x64 executable; test/build paths are refused.");
        ValidateOwned(id, exe, requireComplete: false); // Refuse another owner's assets BEFORE any writes.
        string shortcut = ShortcutPath(id);
        bool hadShortcut = File.Exists(shortcut);
        using var oldApp = Registry.CurrentUser.OpenSubKey(AppKey(id));
        using var oldClass = Registry.CurrentUser.OpenSubKey(ClassKey(id));
        bool hadApp = oldApp is not null, hadClass = oldClass is not null;
        try
        {
            using (var app = Registry.CurrentUser.CreateSubKey(AppKey(id)))
            {
                app.SetValue("Owner", Owner); app.SetValue("ExecutablePath", exe);
                app.SetValue("DisplayName", id.IsTest ? "AI Burger Clock quota TEST" : "AI Burger Clock");
                app.SetValue("IconUri", exe); app.SetValue("CustomActivator", id.Clsid.ToString("B"));
            }
            using (var cls = Registry.CurrentUser.CreateSubKey(ClassKey(id)))
            {
                cls.SetValue("Owner", Owner); cls.SetValue("ExecutablePath", exe);
                using var server = cls.CreateSubKey("LocalServer32");
                server.SetValue(null, Command(id, exe));
            }
            if (!hadShortcut) WindowsToastShortcut.Create(shortcut, exe, id);
            ValidateOwned(id, exe, requireComplete: true);
        }
        catch
        {
            // Roll back only newly created assets; never delete pre-existing registrations.
            if (!hadShortcut && File.Exists(shortcut)) File.Delete(shortcut);
            if (!hadApp) Registry.CurrentUser.DeleteSubKeyTree(AppKey(id), false);
            if (!hadClass) Registry.CurrentUser.DeleteSubKeyTree(ClassKey(id), false);
            throw;
        }
    }

    internal static void Unregister(WindowsQuotaToastIdentity id, bool dryRun)
    {
        string exe = CurrentExecutable;
        ValidateOwned(id, exe, requireComplete: false);
        Console.WriteLine("Shortcut: " + ShortcutPath(id));
        Console.WriteLine("HKCU: " + AppKey(id));
        Console.WriteLine("HKCU: " + ClassKey(id));
        if (dryRun) { Console.WriteLine("Dry run: no registration changed."); return; }
        // Never clear production notification history, Run, StartupApproved or user settings.
        if (File.Exists(ShortcutPath(id))) File.Delete(ShortcutPath(id));
        Registry.CurrentUser.DeleteSubKeyTree(AppKey(id), false);
        Registry.CurrentUser.DeleteSubKeyTree(ClassKey(id), false);
        Console.WriteLine("Owned registration removed. It is not recreated at normal startup.");
    }

    internal static void ValidateForRemoval(WindowsQuotaToastIdentity id) =>
        ValidateOwned(id, CurrentExecutable, requireComplete: false);
}

internal static class WindowsToastShortcut
{
    private static readonly Guid PropertyFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    internal static void Create(string path, string executable, WindowsQuotaToastIdentity id)
    {
        object link = Activator.CreateInstance(Type.GetTypeFromCLSID(new("00021401-0000-0000-C000-000000000046"), true)!)!;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".lnk";
        try
        {
            var shell = (IShellLinkW)link;
            shell.SetPath(executable); shell.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            shell.SetDescription("AI Burger Clock quota notifications"); shell.SetIconLocation(executable, 0);
            var properties = (IPropertyStore)link;
            var appKey = new PropertyKey(PropertyFormat, 5); var clsKey = new PropertyKey(PropertyFormat, 26);
            using (var value = PropVariant.FromString(id.Aumid)) { var copy = value; properties.SetValue(ref appKey, ref copy); }
            using (var value = PropVariant.FromGuid(id.Clsid)) { var copy = value; properties.SetValue(ref clsKey, ref copy); }
            properties.Commit();
            ((IPersistFile)link).Save(temporary, true);
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static bool Matches(string path, string executable, WindowsQuotaToastIdentity id)
    {
        object link = Activator.CreateInstance(Type.GetTypeFromCLSID(new("00021401-0000-0000-C000-000000000046"), true)!)!;
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var buffer = new StringBuilder(32768);
            ((IShellLinkW)link).GetPath(buffer, buffer.Capacity, IntPtr.Zero, 4);
            if (!string.Equals(buffer.ToString(), executable, StringComparison.OrdinalIgnoreCase)) return false;
            buffer.Clear(); ((IShellLinkW)link).GetArguments(buffer, buffer.Capacity);
            if (buffer.Length != 0) return false;
            buffer.Clear(); ((IShellLinkW)link).GetWorkingDirectory(buffer, buffer.Capacity);
            if (!string.Equals(buffer.ToString(), Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase)) return false;
            var properties = (IPropertyStore)link;
            var appKey = new PropertyKey(PropertyFormat, 5); var clsKey = new PropertyKey(PropertyFormat, 26);
            properties.GetValue(ref appKey, out var app); properties.GetValue(ref clsKey, out var cls);
            try { return app.StringValue == id.Aumid && cls.GuidValue == id.Clsid; }
            finally { app.Dispose(); cls.Dispose(); }
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant : IDisposable
    {
        [FieldOffset(0)] private ushort type;
        [FieldOffset(8)] private IntPtr pointer;
        internal string? StringValue => type == 31 ? Marshal.PtrToStringUni(pointer) : null;
        internal Guid? GuidValue => type == 72 ? Marshal.PtrToStructure<Guid>(pointer) : null;
        internal static PropVariant FromString(string value) => new() { type = 31, pointer = Marshal.StringToCoTaskMemUni(value) };
        internal static PropVariant FromGuid(Guid value)
        {
            var result = new PropVariant { type = 72, pointer = Marshal.AllocCoTaskMem(16) };
            Marshal.StructureToPtr(value, result.pointer, false); return result;
        }
        public void Dispose() => PropVariantClear(ref this);
    }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value); void SetValue(ref PropertyKey key, ref PropVariant value); void Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int length, IntPtr findData, uint flags);
        void GetIDList(out IntPtr id); void SetIDList(IntPtr id);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short hotkey); void SetHotkey(short hotkey); void GetShowCmd(out int show); void SetShowCmd(int show);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string text, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string text, uint reserved); void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string text);
    }
}
