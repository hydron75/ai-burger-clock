using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace AiBurgerClock;

// Pure metadata and in-process COM-interface checks. These do not register a
// class object, write a shortcut/registry value, send a toast or start a process.
internal static class WindowsQuotaToastChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Windows quota toast: " + label);
        }

        Content(Check);
        Check(WindowsQuotaToastSender.ReadSetting(() => Windows.UI.Notifications.NotificationSetting.Enabled) == Windows.UI.Notifications.NotificationSetting.Enabled,
            "known notification setting is preserved");
        Check(WindowsQuotaToastSender.ReadSetting(() => throw new COMException("synthetic", unchecked((int)0x80070490))) is null,
            "first-send unknown setting does not generate a hidden bootstrap toast");
        bool settingFailed = false;
        try { WindowsQuotaToastSender.ReadSetting(() => throw new COMException("synthetic", unchecked((int)0x80004005))); }
        catch (COMException) { settingFailed = true; }
        Check(settingFailed, "other setting errors remain transport errors");
        foreach (var blockedSetting in Enum.GetValues<Windows.UI.Notifications.NotificationSetting>().Where(value => value != Windows.UI.Notifications.NotificationSetting.Enabled))
            foreach (bool registered in new[] { false, true })
                foreach (bool callback in new[] { false, true })
                {
                    bool blocked = false;
                    try { WindowsQuotaToastSender.CheckReady(blockedSetting, registered, callback); }
                    catch (WindowsToastBlockedException) { blocked = true; }
                    Check(blocked, "Windows block precedes broken registration or callback server");
                }
        foreach (var readiness in new[] { (Registered: false, Callback: true), (Registered: true, Callback: false) })
        {
            bool unavailable = false;
            try { WindowsQuotaToastSender.CheckReady(Windows.UI.Notifications.NotificationSetting.Enabled, readiness.Registered, readiness.Callback); }
            catch (InvalidOperationException error) when (error is not WindowsToastBlockedException) { unavailable = true; }
            Check(unavailable, "enabled Windows setting does not hide a broken local registration/server");
        }
        WindowsQuotaToastSender.CheckReady(null, true, true);
        Check(true, "first-send unknown policy can submit once through Windows policy enforcement");
        WindowsQuotaToastSender.CheckReady(Windows.UI.Notifications.NotificationSetting.Enabled, true, true);
        Check(true, "complete enabled path is ready");
        IdentitiesAndPaths(Check);
        Callback(Check);
        ClassFactory(Check);
        return count;
    }

    private static void Content(Action<bool, string> check)
    {
        const string title = "Synthetic <quota> & \"title\" 'sample' 🍔";
        const string body = "Synthetic & <body> \"quotes\" 'apostrophes'\nSecond line: 합성 알림";
        string xml = WindowsQuotaToastSender.Content(title, body);
        var document = XDocument.Parse(xml);
        XElement root = document.Root!;
        check(root.Name == "toast", "generic toast root");
        check(root.Attribute("activationType")?.Value == "background", "notification activation has no foreground-app action");
        check(root.Attributes().Select(attribute => attribute.Name.LocalName).SequenceEqual(new[] { "activationType" }),
            "no launch arguments, protocol target or other activation payload");
        XElement? binding = root.Element("visual")?.Element("binding");
        check(binding?.Attribute("template")?.Value == "ToastGeneric", "generic text template");
        check(binding?.Elements("text").Select(element => element.Value).SequenceEqual(new[] { title, body }) == true,
            "title and body survive XML escaping unchanged");
        check(root.Descendants("actions").Count() == 0 && root.Descendants("action").Count() == 0 &&
            root.Descendants("input").Count() == 0, "no button, input or click action introduced");
        check(root.Descendants().Select(element => element.Name.LocalName).SequenceEqual(new[] { "visual", "binding", "text", "text" }),
            "payload can only create the two original text nodes");
        check(xml.Contains("&lt;quota&gt;", StringComparison.Ordinal) && xml.Contains("&amp;", StringComparison.Ordinal),
            "markup in existing messages cannot inject toast XML");

        var injected = XDocument.Parse(WindowsQuotaToastSender.Content("</text><actions><action content='synthetic'/></actions><text>", "&"));
        check(!injected.Descendants("actions").Any() && injected.Descendants("text").Count() == 2,
            "synthetic markup-injection title remains text");
        var empty = XDocument.Parse(WindowsQuotaToastSender.Content("", ""));
        check(empty.Descendants("text").Select(element => element.Value).SequenceEqual(new[] { "", "" }),
            "empty existing text is not replaced by new wording");
    }

    private static void IdentitiesAndPaths(Action<bool, string> check)
    {
        WindowsQuotaToastIdentity production = WindowsQuotaToastIdentity.Production;
        WindowsQuotaToastIdentity test = WindowsQuotaToastIdentity.Test;
        check(!production.IsTest && test.IsTest, "production and integration identities are explicit");
        check(production.Aumid != test.Aumid && production.Clsid != test.Clsid && production.ShortcutName != test.ShortcutName,
            "integration identity cannot overwrite a production AUMID, CLSID or shortcut");
        check(production.Clsid != Guid.Empty && test.Clsid != Guid.Empty && production.Aumid.Length <= 128 && test.Aumid.Length <= 128,
            "fixed notification identities have valid GUIDs and bounded AUMIDs");
        check(production.ActivationArguments == "--quota-toast-activated", "production callback command is fixed");
        check(test.ActivationArguments == "--quota-toast-activated --quota-toast-test", "integration callback command uses only the fixed TEST selector");
        const string executable = @"C:\Synthetic App\dist\win-x64\AI Burger Clock.exe";
        check(WindowsQuotaToastRegistration.Command(production, executable) == "\"" + executable + "\" --quota-toast-activated",
            "production COM command quotes the EXE and contains no normal-startup option");
        check(WindowsQuotaToastRegistration.Command(test, executable) == "\"" + executable + "\" --quota-toast-activated --quota-toast-test",
            "TEST COM command cannot select the production callback identity");
        check(WindowsQuotaToastRegistration.ShortcutPath(production) != WindowsQuotaToastRegistration.ShortcutPath(test),
            "derived shortcut paths are isolated without creating them");

        foreach (string path in new[]
        {
            executable,
            @"C:\Synthetic App\DIST\WIN-X64\ai burger clock.EXE",
            @"C:\Synthetic App\dist\win-x64\..\win-x64\AI Burger Clock.exe"
        })
            check(WindowsQuotaToastRegistration.IsDeploymentPath(path), "published deployment shape accepted: " + path);

        foreach (string path in new[]
        {
            @".\dist\win-x64\AI Burger Clock.exe",
            @"dist\win-x64\AI Burger Clock.exe",
            @"C:dist\win-x64\AI Burger Clock.exe",
            @"C:\Synthetic App\bin\Release\dist\win-x64\AI Burger Clock.exe",
            @"C:\Synthetic App\OBJ\dist\win-x64\AI Burger Clock.exe",
            @"C:\Synthetic App\artifacts\publish\dist\win-x64\AI Burger Clock.exe",
            @"C:\Synthetic App\.ai-burger-clock-tests\before\dist\win-x64\AI Burger Clock.exe",
            @"C:\Synthetic App\.ai-burger-clock-tests\after\dist\win-x64\AI Burger Clock.exe",
            @"C:\Synthetic App\dist\win-x64\AI Burger Clock TEST.exe",
            @"C:\Synthetic App\dist\arm64\AI Burger Clock.exe",
            @"C:\Synthetic App\publish\win-x64\AI Burger Clock.exe",
            @"C:\Synthetic App\dist\AI Burger Clock.exe"
        })
            check(!WindowsQuotaToastRegistration.IsDeploymentPath(path), "build/test/nondeployment shape rejected: " + path);
    }

    private static void Callback(Action<bool, string> check)
    {
        string expected = WindowsQuotaToastIdentity.Test.Aumid;
        int called = 0;
        var callback = new WindowsQuotaToastCallback(expected, () => called++);
        foreach (string app in new[] { "", "Synthetic.Other.App", WindowsQuotaToastIdentity.Production.Aumid, expected.ToUpperInvariant() })
        {
            int result = callback.Activate(app, "--register-quota-notifications --check-quotas --autostart", new IntPtr(1), uint.MaxValue);
            check(result == unchecked((int)0x80070057) && called == 0,
                "foreign or non-exact AUMID cannot invoke the callback: " + app);
        }
        check(callback.Activate(expected, "--register-quota-notifications --check-quotas --autostart", new IntPtr(1), uint.MaxValue) == 0 && called == 1,
            "valid callback acknowledges without interpreting launch options or dereferencing caller input");
        check(callback.Activate(expected, "", IntPtr.Zero, 0) == 0 && called == 2,
            "empty click arguments have the same no-action acknowledgment");
        var failing = new WindowsQuotaToastCallback(expected, () => throw new InvalidOperationException("synthetic callback failure"));
        check(failing.Activate(expected, "unused", IntPtr.Zero, 0) == unchecked((int)0x80004005),
            "callback failure becomes HRESULT instead of escaping across COM");
    }

    private static void ClassFactory(Action<bool, string> check)
    {
        int called = 0;
        var callback = new WindowsQuotaToastCallback(WindowsQuotaToastIdentity.Test.Aumid, () => called++);
        var factory = new WindowsQuotaToastClassFactory(callback);
        Guid callbackIid = typeof(IWindowsQuotaNotificationCallback).GUID;
        int aggregation = factory.CreateInstance(new IntPtr(1), ref callbackIid, out IntPtr aggregated);
        check(aggregation == unchecked((int)0x80040110) && aggregated == IntPtr.Zero,
            "COM aggregation is rejected without exposing an interface");
        Guid unsupportedIid = new("6F204C0C-F7CE-426C-B760-3C6F9069CCAD");
        int unsupported = factory.CreateInstance(IntPtr.Zero, ref unsupportedIid, out IntPtr absent);
        check(unsupported == unchecked((int)0x80004002) && absent == IntPtr.Zero,
            "unrelated interface requests are rejected");

        foreach (Guid supported in new[] { callbackIid, new Guid("00000000-0000-0000-C000-000000000046") })
        {
            Guid iid = supported;
            IntPtr instance = IntPtr.Zero;
            try
            {
                int result = factory.CreateInstance(IntPtr.Zero, ref iid, out instance);
                check(result == 0 && instance != IntPtr.Zero, "supported COM interface is returned: " + supported);
            }
            finally
            {
                if (instance != IntPtr.Zero) Marshal.Release(instance);
            }
        }
        check(called == 0, "creating and releasing an interface does not invoke activation");
        check(factory.LockServer(true) == 0 && factory.LockServer(false) == 0,
            "server-lock requests do not introduce an application action");
    }
}
