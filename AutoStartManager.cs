using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace AiBurgerClock;

internal enum AutoStartState { NotRegistered, Enabled, DifferentExecutable, DisabledByWindows, Unknown }
internal enum StartupApproval { Allowed, Disabled, Unknown }

internal sealed record AutoStartStatus(AutoStartState State, string Detail)
{
    public bool IsEnabled => State == AutoStartState.Enabled;
    public string Label => State switch
    {
        AutoStartState.DifferentExecutable => "Windows 자동 실행 · 다른 경로",
        AutoStartState.DisabledByWindows => "Windows 자동 실행 · 차단됨",
        AutoStartState.Unknown => "Windows 자동 실행 · 확인 필요",
        _ => "Windows 시작 시 자동 실행"
    };
}

internal static class AutoStartManager
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovalKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ValueName = "AI Burger Clock";

    public static bool IsEnabled() => GetStatus().IsEnabled;

    public static AutoStartStatus GetStatus()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        using var approval = Registry.CurrentUser.OpenSubKey(ApprovalKeyPath, false);
        var runValue = RegistryValueSnapshot.Read(run);
        var approvalValue = RegistryValueSnapshot.Read(approval);
        return Evaluate(runValue.Value, runValue.Kind, approvalValue.Value, approvalValue.Kind, Application.ExecutablePath);
    }

    internal static string CommandFor(string executable) => $"\"{executable}\" --autostart";

    // Pure assessment: opening the app, window or menu must never change registration.
    internal static AutoStartStatus Evaluate(object? command, RegistryValueKind commandKind,
        object? approval, RegistryValueKind approvalKind, string executable)
    {
        if (command is null)
            return new(AutoStartState.NotRegistered, "자동 실행이 등록되어 있지 않습니다. 체크하면 현재 실행 파일을 등록합니다.");
        if (command is not string text || commandKind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            return new(AutoStartState.Unknown, "자동 실행 등록값의 형식을 확인할 수 없습니다.");
        if (commandKind == RegistryValueKind.ExpandString) text = Environment.ExpandEnvironmentVariables(text);
        var match = Regex.Match(text.Trim(), "^(?:\"(?<exe>[^\"]+)\"|(?<exe>[^\\s\"]+))\\s+--autostart$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        if (!match.Success)
            return new(AutoStartState.Unknown, "실행 경로 또는 --autostart 인수가 올바르지 않습니다.\n등록: " + text);
        string registered = match.Groups["exe"].Value;
        bool samePath;
        try
        {
            if (!Path.IsPathFullyQualified(registered) || !Path.IsPathFullyQualified(executable))
                return new(AutoStartState.Unknown, "자동 실행 경로가 절대 경로가 아닙니다.\n등록: " + text);
            samePath = string.Equals(Path.GetFullPath(registered), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(AutoStartState.Unknown, "자동 실행 경로를 해석하지 못했습니다.\n등록: " + text);
        }
        var permission = ReadApproval(approval, approvalKind);
        string paths = $"\n등록: {registered}\n현재: {executable}";
        if (!samePath)
            return new(AutoStartState.DifferentExecutable, "다른 실행 파일이 등록되어 있습니다. 체크하면 현재 파일로 변경합니다." +
                (permission == StartupApproval.Disabled ? "\nWindows에서도 자동 실행이 꺼져 있습니다." : "") + paths);
        return permission switch
        {
            StartupApproval.Allowed => new(AutoStartState.Enabled, "현재 실행 파일로 자동 실행이 등록되어 있습니다." + paths),
            StartupApproval.Disabled => new(AutoStartState.DisabledByWindows,
                "Windows 시작 앱 설정에서 꺼져 있습니다. 체크하면 현재 파일로 등록하고 다시 켭니다." + paths),
            _ => new(AutoStartState.Unknown, "Windows 자동 실행 승인 정보를 해석하지 못했습니다. Windows 설정 > 앱 > 시작 프로그램에서 확인하세요." + paths)
        };
    }

    internal static StartupApproval ReadApproval(object? value, RegistryValueKind kind)
    {
        if (value is null) return StartupApproval.Allowed;
        // Windows-internal format: recognize known states only; future/invalid states fail closed.
        if (kind != RegistryValueKind.Binary || value is not byte[] { Length: 12 } bytes)
            return StartupApproval.Unknown;
        return BitConverter.ToUInt32(bytes, 0) switch
        {
            0 or 2 or 6 => StartupApproval.Allowed,
            3 or 7 => StartupApproval.Disabled,
            _ => StartupApproval.Unknown
        };
    }

    // Called only by an explicit checkbox/menu action, never by startup or display refresh.
    public static void SetEnabled(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        using var approval = Registry.CurrentUser.OpenSubKey(ApprovalKeyPath, enabled);
        var previousRun = RegistryValueSnapshot.Read(run);
        var previousApproval = RegistryValueSnapshot.Read(approval);
        var permission = ReadApproval(previousApproval.Value, previousApproval.Kind);
        if (enabled && permission == StartupApproval.Unknown)
            throw new InvalidOperationException("Windows 자동 실행 승인 정보가 알려진 형식이 아닙니다. Windows 설정 > 앱 > 시작 프로그램에서 직접 켜 주세요.");
        bool approvalChanged = false;
        try
        {
            if (enabled)
            {
                run.SetValue(ValueName, CommandFor(Application.ExecutablePath), RegistryValueKind.String);
                if (permission == StartupApproval.Disabled && approval is not null)
                {
                    // Removing only our known disable override restores Windows' default permission.
                    approval.DeleteValue(ValueName, false);
                    approvalChanged = true;
                }
                if (!GetStatus().IsEnabled)
                    throw new InvalidOperationException("자동 실행 등록 확인에 실패했습니다.");
            }
            else run.DeleteValue(ValueName, false);
        }
        catch (Exception original)
        {
            try
            {
                try { previousRun.Restore(run); }
                finally { if (approvalChanged) previousApproval.Restore(approval!); }
            }
            catch (Exception restoreError)
            {
                // Report both causes; a rollback failure must not hide why the change failed.
                throw new InvalidOperationException(original.Message + "\n기존 설정 복원도 실패했습니다: " + restoreError.Message,
                    new AggregateException(original, restoreError));
            }
            throw;
        }
    }
}

// Keep the original type and unexpanded value when restoring this app's registration.
internal sealed record RegistryValueSnapshot(object? Value, RegistryValueKind Kind)
{
    public static RegistryValueSnapshot Read(RegistryKey? key)
    {
        object? value = key?.GetValue(AutoStartManager.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new(value, value is null ? RegistryValueKind.None : key!.GetValueKind(AutoStartManager.ValueName));
    }

    public void Restore(RegistryKey key)
    {
        if (Value is null) key.DeleteValue(AutoStartManager.ValueName, false);
        else key.SetValue(AutoStartManager.ValueName, Value, Kind);
    }

    public bool Matches(RegistryValueSnapshot other) => Kind == other.Kind &&
        (Value is byte[] bytes && other.Value is byte[] otherBytes ? bytes.SequenceEqual(otherBytes) :
         Value is string[] strings && other.Value is string[] otherStrings ? strings.SequenceEqual(otherStrings) : Equals(Value, other.Value));
}
