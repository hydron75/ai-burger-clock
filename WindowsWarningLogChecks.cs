using System.Globalization;
using System.Text;

namespace AiBurgerClock;

// Windows-only filesystem checks. The Checks suffix keeps this out of the portable *Tests.cs list.
internal static class WindowsWarningLogChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Windows warning log: " + label);
        }

        string directory = Path.Combine(Path.GetTempPath(), "AIBurgerClock-warning-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FreshAndAppend(directory, Check);
            TickerWarnings(directory, Check);
            RotationAndBounds(directory, Check);
            UnicodeAndNewlines(directory, Check);
            FailedWrites(directory, Check);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { Console.Error.WriteLine("Temporary warning log checks retained: " + directory); }
            catch (UnauthorizedAccessException) { Console.Error.WriteLine("Temporary warning log checks retained: " + directory); }
        }
        return count;
    }

    private static void FreshAndAppend(string root, Action<bool, string> check)
    {
        var defaults = new WindowsWarningLog();
        check(defaults.LogPath == Path.Combine(AppPaths.DataDirectory, "logs", "status-ticker.log"), "default data-folder path");
        check(defaults.PreviousLogPath == Path.Combine(AppPaths.DataDirectory, "logs", "status-ticker.previous.log"), "default previous path");
        string directory = Path.Combine(root, "fresh");
        var log = new WindowsWarningLog(directory);
        check(!Directory.Exists(directory) && !File.Exists(log.LogPath), "construction performs no filesystem writes");
        log.Write("first");
        check(File.Exists(log.LogPath) && !File.Exists(log.PreviousLogPath), "first warning creates only the current file");
        string[] first = File.ReadAllLines(log.LogPath, Encoding.UTF8);
        check(first.Length == 1 && first[0].EndsWith("] WARN first", StringComparison.Ordinal), "timestamp and warning on one line");
        check(DateTimeOffset.TryParseExact(first[0].Substring(1, 24), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
            && utc.Offset == TimeSpan.Zero, "timestamp explicitly identifies UTC");
        log.Write("second");
        string[] appended = File.ReadAllLines(log.LogPath, Encoding.UTF8);
        check(appended.Length == 2 && appended[0] == first[0] && appended[1].EndsWith("] WARN second", StringComparison.Ordinal),
            "the second warning appends without replacing the first");
        byte[] bytes = File.ReadAllBytes(log.LogPath);
        check(bytes.Length <= WindowsWarningLog.MaximumFileBytes && bytes[0] == (byte)'[', "UTF-8 has no BOM and stays bounded");
    }

    private static void TickerWarnings(string root, Action<bool, string> check)
    {
        var log = new WindowsWarningLog(Path.Combine(root, "ticker"));
        var ticker = new StatusTicker(warn: log.Write);
        var schedule = AgentSchedule.GetSnapshot(DateTimeOffset.Parse("2026-06-16T05:00:00Z", CultureInfo.InvariantCulture));
        var status = ProviderStatus.Unknown(ProviderKind.Claude);
        var duplicated = ticker.Tick(schedule, [status, status], TickReason.Initial);
        check(duplicated.Providers.Count == 1, "a duplicate warns without aborting the tick");
        string[] lines = File.ReadAllLines(log.LogPath, Encoding.UTF8);
        check(lines.Length == 1 && lines[0].Contains("more than once (Claude)", StringComparison.Ordinal), "ticker warning reaches the file sink");
        ticker.Tick(schedule, [status, status], TickReason.Timer);
        check(File.ReadAllLines(log.LogPath).Length == 1, "the same duplicate set does not log every second");
        ticker.Tick(schedule, [status], TickReason.Timer);
        ticker.Tick(schedule, [status, status], TickReason.Timer);
        check(File.ReadAllLines(log.LogPath).Length == 2, "a duplicate after clean input is logged again");
    }

    private static void RotationAndBounds(string root, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "rotation");
        var log = new WindowsWarningLog(directory);
        log.Write(new string('a', WindowsWarningLog.MaximumFileBytes));
        byte[] full = File.ReadAllBytes(log.LogPath);
        check(full.Length == WindowsWarningLog.MaximumFileBytes, "a long ASCII warning includes its timestamp and LF within 64 KiB");
        log.Write("after first rotation");
        check(File.ReadAllBytes(log.PreviousLogPath).SequenceEqual(full), "rotation preserves the previous current file");
        check(File.ReadAllLines(log.LogPath) is [var current] && current.EndsWith("after first rotation", StringComparison.Ordinal),
            "rotation starts a fresh current file");
        log.Write(new string('b', WindowsWarningLog.MaximumFileBytes));
        check(File.ReadAllLines(log.PreviousLogPath) is [var previous] && previous.EndsWith("after first rotation", StringComparison.Ordinal),
            "a later rotation replaces the old previous file");
        string payload = new('c', WindowsWarningLog.MaximumFileBytes);
        for (int i = 0; i < 64; i++) log.Write("cycle " + i.ToString(CultureInfo.InvariantCulture) + " " + payload);
        string[] files = Directory.GetFiles(directory);
        check(files.Length == 2 && files.Contains(log.LogPath) && files.Contains(log.PreviousLogPath), "repeated rotation keeps only two files");
        check(files.All(path => new FileInfo(path).Length <= WindowsWarningLog.MaximumFileBytes), "both files stay within 64 KiB");
        check(files.Sum(path => new FileInfo(path).Length) <= 2L * WindowsWarningLog.MaximumFileBytes, "total retained warning data stays within 128 KiB");
        check(File.ReadAllText(log.LogPath).Contains("cycle 63 ", StringComparison.Ordinal) &&
            File.ReadAllText(log.PreviousLogPath).Contains("cycle 62 ", StringComparison.Ordinal), "the two latest cycles are retained");
    }

    private static void UnicodeAndNewlines(string root, Action<bool, string> check)
    {
        var log = new WindowsWarningLog(Path.Combine(root, "unicode"));
        log.Write(string.Concat(Enumerable.Repeat("🍔한\r\n줄\u000B\u000C\u0085\u2028\u2029", 20_000)));
        byte[] bytes = File.ReadAllBytes(log.LogPath);
        string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        check(bytes.Length <= WindowsWarningLog.MaximumFileBytes, "long multibyte text obeys the byte limit");
        check(text.Contains("🍔한", StringComparison.Ordinal) && !text.Contains('\uFFFD'), "UTF-8 truncation does not split emoji or Korean text");
        check(text.EndsWith(" [truncated]\n", StringComparison.Ordinal), "a cut warning is marked and ends with one LF");
        check(text.Count(character => character == '\n') == 1 && text[..^1].All(character =>
            !char.IsControl(character) && character is not '\u2028' and not '\u2029'), "all input line separators become spaces");
        check(File.ReadAllLines(log.LogPath).Length == 1, "long Unicode input still creates one log entry");
    }

    private static void FailedWrites(string root, Action<bool, string> check)
    {
        var locked = new WindowsWarningLog(Path.Combine(root, "locked"));
        locked.Write("retained");
        byte[] original = File.ReadAllBytes(locked.LogPath);
        using (var handle = new FileStream(locked.LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            locked.Write("cannot append");
            var ticker = new StatusTicker(warn: locked.Write);
            var status = ProviderStatus.Unknown(ProviderKind.OpenAI);
            var schedule = AgentSchedule.GetSnapshot(DateTimeOffset.Parse("2026-06-16T05:00:00Z", CultureInfo.InvariantCulture));
            check(ticker.Tick(schedule, [status, status], TickReason.Initial).Providers.Count == 1,
                "a locked warning file cannot abort a StatusTicker callback");
        }
        check(File.ReadAllBytes(locked.LogPath).SequenceEqual(original), "a failed append leaves the current log untouched");
        locked.Write("write recovers");
        check(File.ReadAllLines(locked.LogPath).Length == 2, "a later unlocked append succeeds");

        var rotation = new WindowsWarningLog(Path.Combine(root, "locked-rotation"));
        rotation.Write(new string('x', WindowsWarningLog.MaximumFileBytes));
        rotation.Write("previous content");
        rotation.Write(new string('y', WindowsWarningLog.MaximumFileBytes));
        byte[] currentBefore = File.ReadAllBytes(rotation.LogPath);
        byte[] previousBefore = File.ReadAllBytes(rotation.PreviousLogPath);
        using (var handle = new FileStream(rotation.PreviousLogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            rotation.Write("cannot rotate");
        check(File.ReadAllBytes(rotation.LogPath).SequenceEqual(currentBefore) && File.ReadAllBytes(rotation.PreviousLogPath).SequenceEqual(previousBefore),
            "failed rotation preserves both existing files rather than growing the current file");
        rotation.Write("rotation recovers");
        check(File.ReadAllLines(rotation.LogPath) is [var recovered] && recovered.EndsWith("rotation recovers", StringComparison.Ordinal),
            "rotation can recover after the lock is released");

        string blockedDirectory = Path.Combine(root, "not-a-directory");
        File.WriteAllText(blockedDirectory, "blocker");
        new WindowsWarningLog(blockedDirectory).Write("cannot create the directory");
        check(File.ReadAllText(blockedDirectory) == "blocker", "directory-creation failure is contained and leaves the blocking file alone");
    }
}
