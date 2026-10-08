using AiBurgerClock;

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
    Report("paths/IANA/formatting/CLI candidates", PortablePlatformTests.Run());
    Report("panel text/tones (Windows golden)", PanelModelTests.Run());
    Report("recording/note limit/feedback text (Windows golden)", RecordingTests.Run());
    Console.WriteLine($"PASS ALL SHARED: {total:N0} assertions");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: " + error);
    return 1;
}
finally
{
    // Only the unique temporary fixture directory owned by this invocation.
    try { Directory.Delete(directory, recursive: true); }
    catch (IOException) { Console.Error.WriteLine("Test fixtures retained: " + directory); }
}
