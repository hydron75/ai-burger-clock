namespace AiBurgerClock;

internal static class SelfTest
{
    public static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        string directory = Directory.CreateTempSubdirectory("AiBurgerClock-tests-").FullName;
        try
        {
            int autoStart = AutoStartTests.Run();
            Console.WriteLine($"PASS autostart paths/Windows approval assessment: {autoStart:N0} assertions (no registry changes)");
            int schedule = ScheduleTests.Run();
            Console.WriteLine($"PASS Schedule/DST: {schedule:N0} assertions");
            int holidays = HolidayScheduleTests.Run();
            Console.WriteLine($"PASS US holidays/observed dates/extended intervals: {holidays:N0} assertions");
            int tray = TrayPresentationTests.Run();
            Console.WriteLine($"PASS tray colors/independent recommendations/tooltips/icons: {tray:N0} assertions");
            int sources = ProviderStatusTests.Run();
            Console.WriteLine($"PASS OpenAI/Claude/Gemini parsers + HTTP: {sources:N0} assertions");
            int monitor = await MonitorTests.RunAsync();
            Console.WriteLine($"PASS recommendations/freshness/polling/cancellation: {monitor:N0} assertions");
            int storage = await StorageTests.RunAsync(directory);
            Console.WriteLine($"PASS SQLite/measurement metadata/statistics: {storage:N0} assertions, including 10,000-row dataset");
            int holidayMonitor = await HolidayMonitorTests.RunAsync(directory);
            Console.WriteLine($"PASS background provider cache follows holiday policy: {holidayMonitor:N0} assertions");
            int quota = 0;
            AccountQuotaTests.Run((condition, label) =>
            {
                if (!condition) throw new InvalidOperationException(label);
                quota++;
            });
            int quotaClient = await AccountQuotaClientTests.RunAsync();
            Console.WriteLine($"PASS quota schemas/policy/CLI protocol: {quota + quotaClient:N0} assertions (no account calls)");
            int quotaMonitor = await AccountQuotaMonitorTests.RunAsync();
            Console.WriteLine($"PASS quota polling/isolation/SQLite cache/restart/cancellation: {quotaMonitor:N0} assertions");
            Console.WriteLine($"PASS ALL: {autoStart + schedule + holidays + tray + sources + monitor + storage + holidayMonitor + quota + quotaClient + quotaMonitor:N0} assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally
        {
            // This unique directory is created by this invocation; never a user database.
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { Console.WriteLine("Temporary test data retained: " + directory); }
        }
    }
}
