namespace AiBurgerClock;

internal static class SelfTest
{
    public static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        try
        {
            int autoStart = AutoStartTests.Run();
            Console.WriteLine($"PASS autostart paths/Windows approval assessment: {autoStart:N0} assertions (no registry changes)");
            int shared = await SharedTestSuite.RunAllAsync();
            int tray = TrayPresentationTests.Run();
            Console.WriteLine($"PASS Windows provider adapter/native tray icon pixels: {tray:N0} assertions");
            int quotaClient = await AccountQuotaClientTests.RunAsync();
            Console.WriteLine($"PASS Windows quota CLI paths: {quotaClient:N0} assertions (no account calls)");
            int warningLog = WindowsWarningLogChecks.Run();
            Console.WriteLine($"PASS Windows ticker warning log/rotation/failure isolation: {warningLog:N0} assertions (temporary files only)");
            Console.WriteLine($"PASS ALL: {autoStart + shared + tray + quotaClient + warningLog:N0} assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }
}
