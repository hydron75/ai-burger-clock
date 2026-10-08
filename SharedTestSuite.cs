namespace AiBurgerClock;

// Canonical execution list for portable tests and the Windows self-test.
internal static class SharedTestSuite
{
    internal static async Task<int> RunAllAsync()
    {
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-shared-tests-").FullName;
        try
        {
            int total = 0;
            void Report(string label, int assertions)
            {
                total += assertions;
                Console.WriteLine($"PASS {label}: {assertions:N0} assertions");
            }

            Report("Schedule/DST", ScheduleTests.Run());
            Report("US holidays", HolidayScheduleTests.Run());
            Report("official sources (in-memory HTTP)", ProviderStatusTests.Run());
            Report("service monitor/notification policy", await MonitorTests.RunAsync());
            Report("tray colors/recommendations/tooltips (portable)", SharedTrayPresentationTests.Run());
            // StorageTests includes HolidayStorageTests; do not execute it a second time here.
            Report("SQLite/measurements/statistics/10,000 rows", await StorageTests.RunAsync(directory));
            Report("holiday metadata/cache", await HolidayMonitorTests.RunAsync(directory));
            int quota = 0;
            AccountQuotaTests.Run((condition, label) =>
            {
                if (!condition) throw new InvalidOperationException(label);
                quota++;
            });
            Report("quota schemas/polling policy", quota);
            Report("quota monitor/restart/cancellation (fake CLI)", await AccountQuotaMonitorTests.RunAsync());
            Report("quota CLI setup/protocol/cancellation (POSIX fixtures)", await SharedQuotaProtocolTests.RunAsync());
            Report("paths/IANA/formatting/CLI candidates", PortablePlatformTests.Run());
            Report("panel text/tones (Windows golden)", PanelModelTests.Run());
            Report("recording/note limit/feedback text (Windows golden)", RecordingTests.Run());
            return total;
        }
        finally
        {
            // Only the unique temporary fixture directory owned by this invocation.
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { Console.Error.WriteLine("Test fixtures retained: " + directory); }
        }
    }
}
