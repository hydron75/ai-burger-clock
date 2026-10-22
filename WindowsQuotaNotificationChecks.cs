using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

// Fake transports and temporary files only. No toast, balloon, registry or
// Start Menu APIs are called by these Windows-only checks.
internal static class WindowsQuotaNotificationChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Windows quota notification: " + label);
        }

        string root = Path.Combine(Path.GetTempPath(), "AIBurgerClock-quota-delivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Dispatch(Check);
            Blocking(Check);
            KindsAndPrivateData(root, Check);
            BoundsAndAppend(root, Check);
            InvalidFilesAndFailureIsolation(root, Check);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { Console.Error.WriteLine("Temporary quota notification checks retained: " + root); }
            catch (UnauthorizedAccessException) { Console.Error.WriteLine("Temporary quota notification checks retained: " + root); }
        }
        return count;
    }

    private static void Dispatch(Action<bool, string> check)
    {
        QuotaNotificationAlert alert = Alert(QuotaProvider.Codex, recovery: true, earlyReset: false);
        int toasts = 0, balloons = 0;
        List<WindowsQuotaNotificationDeliveryEntry> entries = [];
        var delivery = new WindowsQuotaNotificationDelivery(value => { toasts++; check(ReferenceEquals(value, alert), "toast payload unchanged"); },
            _ => balloons++, entries.Add);
        delivery.Send(alert);
        check(toasts == 1 && balloons == 0, "successful toast submits once without fallback");
        check(entries is [{ Transport: WindowsQuotaNotificationTransport.Toast, Result: WindowsQuotaNotificationResult.Submitted,
            Reason: WindowsQuotaNotificationReason.None, HResult: null }], "successful synchronous submission is recorded without claiming visibility");
        check(entries[0].RecordedAtUtc.Offset == TimeSpan.Zero && entries[0].RecordedAtUtc != default, "delivery time is UTC");

        entries.Clear(); toasts = 0; balloons = 0;
        var submitError = new COMException("synthetic secret that must never be logged", unchecked((int)0x80004005));
        var fallback = new WindowsQuotaNotificationDelivery(_ => { toasts++; throw submitError; },
            value => { balloons++; check(ReferenceEquals(value, alert), "fallback payload unchanged"); }, entries.Add);
        fallback.Send(alert);
        check(toasts == 1 && balloons == 1, "synchronous failure falls back exactly once");
        check(entries is [
            { Transport: WindowsQuotaNotificationTransport.Toast, Result: WindowsQuotaNotificationResult.Failed,
                Reason: WindowsQuotaNotificationReason.SynchronousFailure },
            { Transport: WindowsQuotaNotificationTransport.Balloon, Result: WindowsQuotaNotificationResult.Submitted }],
            "failed toast and submitted fallback have separate records in transport order");
        check(entries[0].HResult == submitError.HResult && entries[1].HResult is null, "only the numeric failure code is carried");

        foreach (Exception error in new Exception[] { new InvalidOperationException("registration missing"), new Win32Exception(5),
            new IOException("registration I/O"), new UnauthorizedAccessException("registration denied") })
        {
            int fallbackCount = 0;
            new WindowsQuotaNotificationDelivery(_ => throw error, _ => fallbackCount++, _ => { }).Send(alert);
            check(fallbackCount == 1, "registration/API failure gets a single fallback: " + error.GetType().Name);
        }

        entries.Clear(); balloons = 0;
        var fallbackError = new Win32Exception(5);
        bool sameFailure = false;
        try
        {
            new WindowsQuotaNotificationDelivery(_ => throw submitError, _ => { balloons++; throw fallbackError; }, entries.Add).Send(alert);
        }
        catch (Win32Exception error) { sameFailure = ReferenceEquals(error, fallbackError); }
        check(sameFailure && balloons == 1, "fallback failure is not retried and reaches the existing host error path");
        check(entries is [{ Result: WindowsQuotaNotificationResult.Failed },
            { Transport: WindowsQuotaNotificationTransport.Balloon, Result: WindowsQuotaNotificationResult.Failed }],
            "both failures are recorded independently");

        entries.Clear();
        delivery.RecordToastFailure(alert, unchecked((int)0x80070490));
        check(entries is [{ Transport: WindowsQuotaNotificationTransport.Toast, Result: WindowsQuotaNotificationResult.Failed,
            Reason: WindowsQuotaNotificationReason.AsynchronousFailure }], "eventual failure is diagnostic only");
        check(balloons == 1, "eventual failure does not trigger a second transport");

        int successfulToasts = 0, unexpectedFallbacks = 0;
        new WindowsQuotaNotificationDelivery(_ => successfulToasts++, _ => unexpectedFallbacks++,
            _ => throw new InvalidOperationException("recording failed")).Send(alert);
        check(successfulToasts == 1 && unexpectedFallbacks == 0, "recording exception cannot change a successful send into fallback");
    }

    private static void Blocking(Action<bool, string> check)
    {
        QuotaNotificationAlert alert = Alert(QuotaProvider.Claude, recovery: false, earlyReset: true);
        foreach (WindowsToastBlockReason reason in Enum.GetValues<WindowsToastBlockReason>())
        {
            int balloons = 0;
            List<WindowsQuotaNotificationDeliveryEntry> entries = [];
            new WindowsQuotaNotificationDelivery(_ => throw new WindowsToastBlockedException(reason),
                _ => balloons++, entries.Add).Send(alert);
            check(balloons == 0, "Windows blocking preference/policy is never bypassed: " + reason);
            check(entries is [{ Transport: WindowsQuotaNotificationTransport.Toast, Result: WindowsQuotaNotificationResult.Blocked,
                HResult: null }] && entries[0].Reason.ToString() == reason.ToString(), "blocking cause has an allowlisted record: " + reason);
        }
    }

    private static void KindsAndPrivateData(string root, Action<bool, string> check)
    {
        var defaults = new WindowsQuotaNotificationDeliveryLog();
        check(defaults.LogPath == Path.Combine(AppPaths.DataDirectory, "logs", "quota-notification-delivery-v1.jsonl"),
            "delivery audit has a separate Windows logs path");
        string directory = Path.Combine(root, "privacy");
        var log = new WindowsQuotaNotificationDeliveryLog(directory);
        check(!Directory.Exists(directory), "constructing a logger creates no files");
        List<WindowsQuotaNotificationDeliveryEntry> captured = [];
        var delivery = new WindowsQuotaNotificationDelivery(_ => { }, _ => throw new InvalidOperationException("unused"),
            entry => { captured.Add(entry); log.Write(entry); });
        delivery.Send(Alert(QuotaProvider.Codex, recovery: true, earlyReset: false));
        delivery.Send(Alert(QuotaProvider.Claude, recovery: false, earlyReset: true));
        delivery.Send(Alert(QuotaProvider.Gemini, recovery: true, earlyReset: true));
        check(captured.Select(entry => entry.Kind).SequenceEqual(new[] { WindowsQuotaNotificationKind.Recovery,
            WindowsQuotaNotificationKind.EarlyReset, WindowsQuotaNotificationKind.Combined }), "kind is derived without inspecting banner strings");
        check(captured.Select(entry => entry.Provider).SequenceEqual(Enum.GetValues<QuotaProvider>()), "only stable Provider enum values are recorded");
        byte[] bytes = File.ReadAllBytes(log.LogPath);
        string text = new UTF8Encoding(false, true).GetString(bytes);
        check(bytes[0] == (byte)'{' && text.Count(character => character == '\n') == 3, "compact UTF-8 JSONL uses no BOM and one LF per row");
        check(!text.Contains("PRIVATE", StringComparison.Ordinal) && !text.Contains("37.125", StringComparison.Ordinal) &&
            !text.Contains("2099-12-31", StringComparison.Ordinal),
            "payload strings do not leak into metadata");
        check(!text.Contains("PRIVATE", StringComparison.Ordinal) && !text.Contains("Title", StringComparison.Ordinal) &&
            !text.Contains("Body", StringComparison.Ordinal) && !text.Contains("Sequence", StringComparison.Ordinal) &&
            !text.Contains("Continuity", StringComparison.Ordinal) && !text.Contains("ResetsAt", StringComparison.Ordinal),
            "titles, bodies, IDs, usage and reset fields are absent");
        foreach (string line in File.ReadAllLines(log.LogPath))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            check(document.RootElement.EnumerateObject().Select(property => property.Name).SequenceEqual(new[]
                { "RecordedAtUtc", "Kind", "Provider", "Transport", "Result", "Reason", "HResult" }), "closed seven-field audit schema");
        }
        var reloaded = new WindowsQuotaNotificationDeliveryLog(directory);
        check(reloaded.TryWrite(captured[0]) && File.ReadAllLines(log.LogPath).Length == 4, "a fresh logger can validate and append existing rows");
    }

    private static void BoundsAndAppend(string root, Action<bool, string> check)
    {
        var log = new WindowsQuotaNotificationDeliveryLog(Path.Combine(root, "bounds"));
        var first = Entry(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        for (int index = 0; index < WindowsQuotaNotificationDeliveryLog.MaximumEntries + 12; index++)
            check(log.TryWrite(first with { RecordedAtUtc = first.RecordedAtUtc.AddSeconds(index) }), "bounded write " + index);
        string[] lines = File.ReadAllLines(log.LogPath);
        check(lines.Length == WindowsQuotaNotificationDeliveryLog.MaximumEntries, "only the latest 256 delivery records survive");
        check(new FileInfo(log.LogPath).Length <= WindowsQuotaNotificationDeliveryLog.MaximumBytes, "log stays within 128 KiB");
        using JsonDocument oldest = JsonDocument.Parse(lines[0]);
        using JsonDocument newest = JsonDocument.Parse(lines[^1]);
        check(oldest.RootElement.GetProperty("RecordedAtUtc").GetDateTimeOffset() == first.RecordedAtUtc.AddSeconds(12), "oldest rows are discarded first");
        check(newest.RootElement.GetProperty("RecordedAtUtc").GetDateTimeOffset() == first.RecordedAtUtc.AddSeconds(267), "most recent row is retained");
        check(Directory.GetFiles(Path.GetDirectoryName(log.LogPath)!).Length == 1, "atomic writes leave no temporary siblings");

        var parallel = new WindowsQuotaNotificationDeliveryLog(Path.Combine(root, "parallel"));
        Parallel.For(0, 64, index => parallel.Write(first with { RecordedAtUtc = first.RecordedAtUtc.AddSeconds(index) }));
        check(File.ReadAllLines(parallel.LogPath).Length == 64, "synchronous and native callback writers share a serialization gate");
    }

    private static void InvalidFilesAndFailureIsolation(string root, Action<bool, string> check)
    {
        var log = new WindowsQuotaNotificationDeliveryLog(Path.Combine(root, "invalid"));
        var entry = Entry(DateTimeOffset.UtcNow);
        check(!log.TryWrite(entry with { Provider = (QuotaProvider)42 }), "unknown Provider rejected before file creation");
        check(!log.TryWrite(entry with { RecordedAtUtc = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(9)) }), "non-UTC timestamp rejected");
        check(!log.TryWrite(entry with { Result = WindowsQuotaNotificationResult.Submitted, HResult = 5 }), "success cannot carry arbitrary failure metadata");
        check(!log.TryWrite(entry with { Result = WindowsQuotaNotificationResult.Blocked,
            Transport = WindowsQuotaNotificationTransport.Balloon, Reason = WindowsQuotaNotificationReason.DisabledForUser }), "blocking cannot describe a bypass balloon");
        check(!Directory.Exists(Path.GetDirectoryName(log.LogPath)), "invalid entries have no filesystem effects");

        check(log.TryWrite(entry), "a valid entry creates the file");
        byte[] original = File.ReadAllBytes(log.LogPath);
        using (var locked = new FileStream(log.LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(!log.TryWrite(entry), "locked file failure is contained");
        check(File.ReadAllBytes(log.LogPath).SequenceEqual(original), "failed append preserves the complete old file");
        File.SetAttributes(log.LogPath, FileAttributes.ReadOnly);
        try { check(!log.TryWrite(entry), "read-only file is not overwritten"); }
        finally { File.SetAttributes(log.LogPath, FileAttributes.Normal); }
        check(log.TryWrite(entry), "recording recovers after a transient failure");

        string corrupt = File.ReadAllText(log.LogPath).Replace("\"Kind\":\"Recovery\"", "\"Kind\":\"PRIVATE\"", StringComparison.Ordinal);
        File.WriteAllText(log.LogPath, corrupt, new UTF8Encoding(false));
        check(!log.TryWrite(entry) && File.ReadAllText(log.LogPath) == corrupt, "unexpected metadata cannot be copied into a newly written audit");
        File.WriteAllText(log.LogPath, new string('x', WindowsQuotaNotificationDeliveryLog.MaximumBytes + 1), new UTF8Encoding(false));
        check(!log.TryWrite(entry), "oversized existing files are neither read unbounded nor appended");

        string blocker = Path.Combine(root, "blocked-directory");
        File.WriteAllText(blocker, "retained");
        var blocked = new WindowsQuotaNotificationDeliveryLog(blocker);
        int toasts = 0, balloons = 0;
        new WindowsQuotaNotificationDelivery(_ => toasts++, _ => balloons++, blocked.Write).Send(
            Alert(QuotaProvider.Gemini, recovery: true, earlyReset: false));
        check(toasts == 1 && balloons == 0 && File.ReadAllText(blocker) == "retained", "filesystem failures do not change notification behavior");
    }

    private static WindowsQuotaNotificationDeliveryEntry Entry(DateTimeOffset time) => new(time,
        WindowsQuotaNotificationKind.Recovery, QuotaProvider.Codex, WindowsQuotaNotificationTransport.Toast,
        WindowsQuotaNotificationResult.Submitted, WindowsQuotaNotificationReason.None, null);

    private static QuotaNotificationAlert Alert(QuotaProvider provider, bool recovery, bool earlyReset)
    {
        const string text = "PRIVATE synthetic banner/account text 37.125% 2099-12-31T23:59:59Z";
        QuotaRecoveryAlert? recoveryAlert = recovery ? new(provider, 7, text, text, ["PRIVATE-window-id"]) : null;
        QuotaEarlyResetAlert? earlyAlert = earlyReset ? new(provider, 7, 1, DateTimeOffset.UtcNow,
            [new("PRIVATE-window-id", text, 10080, 72.125, 3.125,
                DateTimeOffset.UtcNow.AddDays(6), DateTimeOffset.UtcNow.AddDays(7))]) : null;
        return new(provider, 7, text, text, recoveryAlert, earlyAlert);
    }
}
