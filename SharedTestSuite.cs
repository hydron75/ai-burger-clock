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
            Report("status ticker: transitions/provider alerts/icon (Windows rules)", StatusTickerTests.Run());
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
            Report("Gemini Antigravity quota schemas/policy", GeminiQuotaTests.Run());
            Report("Gemini model weekly scopes/pacing/recovery/cache (synthetic)", await GeminiModelQuotaTests.RunAsync(directory));
            Report("Gemini unused-reset polling/cache migration (synthetic)", await GeminiPollingPolicyTests.RunAsync(directory));
            Report("quota polling diagnostic history (synthetic)", await QuotaPollingDiagnosticTests.RunAsync(directory));
            Report("quota scheduling reasons/reset coalescing (synthetic)", await QuotaSchedulingTests.RunAsync(directory));
            Report("Gemini quota CLI guards/protocol (synthetic)", await GeminiQuotaClientTests.RunAsync());
            Report("quota monitor/restart/cancellation (fake CLI)", await AccountQuotaMonitorTests.RunAsync());
            Report("quota CLI setup/protocol/cancellation (POSIX fixtures)", await SharedQuotaProtocolTests.RunAsync());
            Report("paths/IANA/formatting/CLI candidates", PortablePlatformTests.Run());
            Report("panel text/tones (Windows golden)", PanelModelTests.Run());
            Report("quota usage/countdown/provider detail (portable)", QuotaPresentationTests.Run());
            Report("quota snapshot time/usage comparison (portable)", QuotaPacingTests.Run());
            Report("quota recovery notification decisions/text (synthetic)", QuotaRecoveryNotificationTests.Run());
            Report("quota recovery notification settings (temporary SQLite)", await QuotaRecoverySettingsTests.RunAsync(directory));
            Report("quota early-reset decisions/text/combined alerts (synthetic)", QuotaEarlyResetNotificationTests.Run());
            Report("quota early-reset notification settings (temporary SQLite)", await QuotaEarlyResetSettingsTests.RunAsync(directory));
            Report("quota pending-alert revalidation (synthetic)", QuotaPendingAlertPolicyTests.Run());
            Report("recording/note limit/feedback text (Windows golden)", RecordingTests.Run());
            Report("statistics text (Windows golden)", StatisticsTextTests.Run());
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
