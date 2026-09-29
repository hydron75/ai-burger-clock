using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

internal static class HolidayMonitorTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        var now = new DateTimeOffset(2026, 11, 26, 23, 5, 0, TimeSpan.FromHours(9));
        bool enabled = true;
        var store = new UsageStore(Path.Combine(directory, "holiday-monitor.db"));
        using var http = new HttpClient(new TestStatusHttpHandler());
        using var monitor = new StatusMonitor(new ProviderStatusClient(http), store, () => now,
            scheduleAt: time => AgentSchedule.GetSnapshot(time, enabled));
        await monitor.RefreshOnceAsync();
        int assertions = 0;
        void CheckCache(string state, string recommendation)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = store.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ProviderStatusCache WHERE ScheduleStateAtCheck=$state AND EffectiveRecommendation=$recommendation;";
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$recommendation", recommendation);
            assertions++;
            if ((long)command.ExecuteScalar()! != 3)
                throw new InvalidOperationException("Background provider cache did not use the selected holiday policy.");
        }
        CheckCache("FullThrottle", "Go");
        enabled = false;
        await monitor.RefreshOnceAsync();
        CheckCache("BurgerTime", "BurgerTime");
        await monitor.StopAsync();
        return assertions;
    }
}
