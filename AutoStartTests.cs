using Microsoft.Win32;

namespace AiBurgerClock;

// Pure startup assessment tests. No registry keys, user settings or environment variables are changed.
internal static class AutoStartTests
{
    public static int Run()
    {
        const string executable = @"C:\Apps\AI Burger Clock\AI Burger Clock.exe";
        string command = AutoStartManager.CommandFor(executable);
        int count = 0;
        void Check(bool condition, string description)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Autostart test failed: " + description);
        }
        AutoStartStatus Assess(object? value, RegistryValueKind kind = RegistryValueKind.String,
            object? approval = null, RegistryValueKind approvalKind = RegistryValueKind.Binary,
            string current = executable) => AutoStartManager.Evaluate(value, kind, approval, approvalKind, current);
        void Expect(string description, object? value, AutoStartState expected,
            RegistryValueKind kind = RegistryValueKind.String, object? approval = null,
            RegistryValueKind approvalKind = RegistryValueKind.Binary, string current = executable)
        {
            var result = Assess(value, kind, approval, approvalKind, current);
            Check(result.State == expected, description);
            Check(result.IsEnabled == (expected == AutoStartState.Enabled), description + " enabled indicator");
            Check(!string.IsNullOrWhiteSpace(result.Detail) && !string.IsNullOrWhiteSpace(result.Label), description + " explanatory UI text");
        }

        Check(command == "\"" + executable + "\" --autostart", "quoted current path and startup argument");
        Expect("missing registration", null, AutoStartState.NotRegistered, RegistryValueKind.None);
        Expect("orphan disabled approval cannot register startup", null, AutoStartState.NotRegistered,
            approval: ApprovalBytes(3));
        Expect("orphan unknown approval cannot register startup", null, AutoStartState.NotRegistered,
            approval: ApprovalBytes(123));
        Expect("current executable without approval override", command, AutoStartState.Enabled);
        Expect("case-insensitive path and flag", command.ToUpperInvariant(), AutoStartState.Enabled);
        Expect("outer whitespace and argument separator", " \t\"" + executable + "\"\t--autostart \t", AutoStartState.Enabled);
        Expect("normalized dot segments", AutoStartManager.CommandFor(@"C:\Apps\AI Burger Clock\old\..\AI Burger Clock.exe"), AutoStartState.Enabled);
        Expect("normalized forward slashes", AutoStartManager.CommandFor("C:/Apps/AI Burger Clock/AI Burger Clock.exe"), AutoStartState.Enabled);
        Expect("unquoted path without spaces", @"C:\Tools\BurgerClock.exe --autostart", AutoStartState.Enabled,
            current: @"C:\Tools\BurgerClock.exe");
        Expect("different release path", AutoStartManager.CommandFor(@"C:\Old\AI Burger Clock.exe"), AutoStartState.DifferentExecutable);
        Expect("same filename in another directory", AutoStartManager.CommandFor(@"D:\Apps\AI Burger Clock\AI Burger Clock.exe"), AutoStartState.DifferentExecutable);
        var differentAndDisabled = Assess(AutoStartManager.CommandFor(@"C:\Old\AI Burger Clock.exe"), approval: ApprovalBytes(3));
        Check(differentAndDisabled.State == AutoStartState.DifferentExecutable && !differentAndDisabled.IsEnabled,
            "wrong executable is never enabled despite any approval");
        Check(differentAndDisabled.Detail.Contains("Windows", StringComparison.Ordinal) &&
            differentAndDisabled.Detail.Contains(executable, StringComparison.Ordinal), "mismatched and disabled detail retains current target");

        foreach (string malformed in new[]
        {
            "", " ", executable, "\"" + executable + "\"", executable + " --autostart",
            "\"" + executable + " --autostart", "\"\" --autostart", command + " --extra",
            "\"" + executable + "\" --autostart=true", "\"" + executable + "\" -autostart",
            "\"" + executable + "\" --auto-start", "\"" + executable + "\" \"--autostart\"",
            "\"" + executable + "\"--autostart", command + " & other.exe", "cmd.exe /c " + command,
            "\"C:\\Bad\0\\BurgerClock.exe\" --autostart"
        })
            Expect("malformed command: " + malformed.Replace('\0', '?'), malformed, AutoStartState.Unknown);
        foreach (var malformed in new (object Value, RegistryValueKind Kind)[]
        {
            (42, RegistryValueKind.DWord), (42L, RegistryValueKind.QWord), (new byte[12], RegistryValueKind.Binary),
            (new[] { command }, RegistryValueKind.MultiString), (command, RegistryValueKind.DWord),
            (command, RegistryValueKind.None), (command, RegistryValueKind.Unknown), (42, RegistryValueKind.String)
        })
            Expect("wrong registration data/kind " + malformed.Kind, malformed.Value, AutoStartState.Unknown, malformed.Kind);
        foreach (string unqualified in new[] { "BurgerClock.exe", @".\BurgerClock.exe", @"C:BurgerClock.exe", @"\Apps\BurgerClock.exe" })
            Expect("unqualified executable is unknown: " + unqualified, AutoStartManager.CommandFor(unqualified), AutoStartState.Unknown);

        string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (string.IsNullOrWhiteSpace(systemRoot))
            throw new InvalidOperationException("Autostart tests require the Windows SystemRoot environment variable.");
        string expandedExecutable = Path.Combine(systemRoot, "AiBurgerClock-test.exe");
        const string expandableCommand = "\"%SystemRoot%\\AiBurgerClock-test.exe\" --autostart";
        Expect("REG_EXPAND_SZ environment expansion", expandableCommand, AutoStartState.Enabled,
            RegistryValueKind.ExpandString, current: expandedExecutable);
        Check(!Assess(expandableCommand, RegistryValueKind.String, current: expandedExecutable).IsEnabled,
            "REG_SZ is not incorrectly environment-expanded");

        Check(AutoStartManager.ReadApproval(null, RegistryValueKind.None) == StartupApproval.Allowed,
            "missing Windows approval override is allowed");
        foreach (uint flag in new uint[] { 0, 2, 6 })
        {
            byte[] bytes = ApprovalBytes(flag);
            Check(AutoStartManager.ReadApproval(bytes, RegistryValueKind.Binary) == StartupApproval.Allowed,
                "recognized allowed approval " + flag);
            Expect("allowed approval " + flag, command, AutoStartState.Enabled, approval: bytes);
        }
        foreach (uint flag in new uint[] { 3, 7 })
        {
            byte[] bytes = ApprovalBytes(flag);
            Check(AutoStartManager.ReadApproval(bytes, RegistryValueKind.Binary) == StartupApproval.Disabled,
                "recognized disabled approval " + flag);
            Expect("disabled approval " + flag, command, AutoStartState.DisabledByWindows, approval: bytes);
        }
        foreach (uint flag in new uint[] { 1, 4, 5, 8, 0x102, uint.MaxValue })
        {
            byte[] bytes = ApprovalBytes(flag);
            Check(AutoStartManager.ReadApproval(bytes, RegistryValueKind.Binary) == StartupApproval.Unknown,
                "future approval flags fail closed " + flag);
            Expect("unknown approval flag " + flag, command, AutoStartState.Unknown, approval: bytes);
        }
        foreach (int length in new[] { 0, 1, 4, 8, 11, 13, 16 })
        {
            var bytes = new byte[length];
            if (length > 0) bytes[0] = 2;
            Check(AutoStartManager.ReadApproval(bytes, RegistryValueKind.Binary) == StartupApproval.Unknown,
                "wrong approval byte length " + length);
            Expect("malformed approval length " + length, command, AutoStartState.Unknown, approval: bytes);
        }
        foreach (var malformed in new (object Value, RegistryValueKind Kind)[]
        {
            ("020000000000000000000000", RegistryValueKind.String), (2, RegistryValueKind.DWord),
            (2L, RegistryValueKind.QWord), (new[] { "2" }, RegistryValueKind.MultiString),
            (ApprovalBytes(2), RegistryValueKind.String), (ApprovalBytes(2), RegistryValueKind.None),
            ("2", RegistryValueKind.Binary)
        })
        {
            Check(AutoStartManager.ReadApproval(malformed.Value, malformed.Kind) == StartupApproval.Unknown,
                "wrong approval data/kind " + malformed.Kind);
            Expect("invalid Windows approval " + malformed.Kind, command, AutoStartState.Unknown,
                approval: malformed.Value, approvalKind: malformed.Kind);
        }

        // Assessment must neither rewrite nor normalize the supplied binary snapshot in-place.
        byte[] disabledWithTimestamp = ApprovalBytes(3);
        for (int i = 4; i < disabledWithTimestamp.Length; i++) disabledWithTimestamp[i] = (byte)(i * 7);
        byte[] original = (byte[])disabledWithTimestamp.Clone();
        for (int i = 0; i < 3; i++)
            Check(Assess(command, approval: disabledWithTimestamp).State == AutoStartState.DisabledByWindows,
                "repeat assessment keeps disabled state");
        Check(disabledWithTimestamp.SequenceEqual(original), "assessment preserves approval timestamp bytes");

        return count;
    }

    private static byte[] ApprovalBytes(uint flag)
    {
        var bytes = new byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, flag);
        return bytes;
    }
}
