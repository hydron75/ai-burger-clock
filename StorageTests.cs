using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

internal static class StorageTests
{
    // Always create a unique child directory. These checks cannot touch the user's DB.
    public static async Task<int> RunAsync(string tempDirectory)
    {
        string directory = Path.Combine(Path.GetFullPath(tempDirectory), "storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "test.db");
        UsageStore store = new(path);
        int assertions = 0;
        void Check(bool result, string description)
        {
            assertions++;
            if (!result) throw new InvalidOperationException("Storage test failed: " + description);
        }

        Check(!File.Exists(path), "new database absent before initialization");
        await store.InitializeAsync();
        Check(File.Exists(path), "first-run automatic creation");
        Check(Scalar(path, "PRAGMA user_version;") == UsageStore.SchemaVersion, "SQLite schema version");
        Check(Scalar(path, "SELECT CAST(Value AS INTEGER) FROM AppMetadata WHERE Key='SchemaVersion';") == UsageStore.SchemaVersion, "metadata schema version");
        Check(Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('UsageEvents','ProviderStatusHistory','ProviderStatusCache','AppMetadata');") == 4, "required tables");
        Check(Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%';") == 4, "query indexes");
        Check((await store.ReadUsageAsync(null)).Count == 0, "empty event data");
        Check((await store.ReadLatestStatusesAsync()).Count == 0, "empty status data");
        StatisticsReport empty = StatisticsAnalysis.Build([]);
        Check(empty.Providers.Count == 3 && empty.Providers.All(row => row.Counts.Total == 0), "all missing providers shown");
        Check(empty.Hours.Count == 72 && empty.Hours.All(row => row.Counts.Cell(0) == "No data"), "all empty hours and no division by zero");
        Check(empty.Schedules.All(row => row.Counts.Cell(0) == "No data"), "missing schedule/DST cohorts");
        Check(empty.Official.Count == 18 && empty.Policies == "No data", "empty official comparisons");

        DateTimeOffset now = new(2026, 9, 19, 3, 0, 0, TimeSpan.Zero);
        List<UsageMeasurement> fixtures = [];
        foreach (ProviderKind provider in Enum.GetValues<ProviderKind>())
        {
            foreach (UsageEventType type in Enum.GetValues<UsageEventType>())
            {
                int i = (int)type;
                int age = i switch { 0 => 0, 1 => 6, 2 => 20, _ => 40 };
                bool dst = i < 2;
                fixtures.Add(new(Guid.NewGuid().ToString("N"), provider, type, now.AddDays(-age),
                    i % 2 == 0 ? AgentState.FullThrottle : AgentState.BurgerTime,
                    i == 0, dst ? -240 : -300, dst ? -420 : -480, dst, dst, "us-et09-pt18-v1",
                    i == 0 ? OfficialStatus.PartialOutage : OfficialStatus.Operational,
                    i == 0 ? Recommendation.Stop : i == 2 ? Recommendation.Go : Recommendation.BurgerTime,
                    "사용자 테스트 Component", "incident-'quoted'", "한글 메모 😃 '; DROP TABLE UsageEvents; --", "2.0.0-test"));
            }
        }
        await store.AddUsageBatchAsync(fixtures);
        IReadOnlyList<UsageMeasurement> saved = await store.ReadUsageAsync(null);
        Check(saved.Count == 12, "all providers and event types saved");
        foreach (UsageMeasurement item in fixtures)
            Check(saved.Single(row => row.EventId == item.EventId) == item, "all metadata fields and Unicode notes round-trip");
        Check(saved.All(row => row.TimestampUtc.Offset == TimeSpan.Zero), "timestamps stored/read as UTC");
        Check(saved.All(row => row.KstHour == 12), "KST hour derived correctly");
        Check(saved.Single(row => row.EventId == fixtures[0].EventId).KstDay == DayOfWeek.Saturday, "KST weekday derived correctly");
        Check((await store.ReadUsageAsync(StatisticsAnalysis.SinceUtc(0, now))).Count == 6, "7-day rolling period");
        Check((await store.ReadUsageAsync(StatisticsAnalysis.SinceUtc(1, now))).Count == 9, "30-day rolling period");
        Check((await store.ReadUsageAsync(StatisticsAnalysis.SinceUtc(2, now))).Count == 12, "all-time period");
        Check((await store.ReadUsageAsync(now.AddTicks(1))).Count == 0, "exclusive data beyond cutoff");
        Check((await store.ReadUsageAsync(now)).Count == 3, "inclusive exact cutoff");

        StatisticsReport report = StatisticsAnalysis.Build(saved);
        Check(report.Providers.All(row => row.Counts == new EventCounts(1, 1, 1, 1)), "provider counts");
        Check(report.Providers.All(row => row.Counts.Cell(row.Counts.Success).Contains("25")), "four-event denominator percentages");
        Check(report.Hours.Count(row => row.Counts.Total == 4) == 3, "per-provider hourly totals");
        Check(report.Hours.Count(row => row.Counts.Total == 0) == 69, "missing hours retained");
        Check(report.Schedules.Where(row => row.Group == "FULL THROTTLE").All(row => row.Counts.Total == 2), "FULL schedule comparison");
        Check(report.Schedules.Where(row => row.Group == "BURGER TIME").All(row => row.Counts.Total == 2), "BURGER schedule comparison");
        Check(report.Schedules.Where(row => row.Group == "Weekend / Extended FULL (공휴일 제외)").All(row => row.Counts.Total == 1), "extended weekend comparison");
        Check(report.Schedules.Where(row => row.Group == "일반 FULL").All(row => row.Counts.Total == 1), "ordinary full comparison");
        Check(report.Schedules.Where(row => row.Group.StartsWith("US DST", StringComparison.Ordinal)).All(row => row.Counts.Total == 2), "DST cohorts");
        Check(report.Schedules.Where(row => row.Group.StartsWith("US Standard", StringComparison.Ordinal)).All(row => row.Counts.Total == 2), "standard-time cohorts");
        Check(report.Schedules.Where(row => row.Group.StartsWith("US Mixed", StringComparison.Ordinal)).All(row => row.Counts.Total == 0), "mixed offset cohort not conflated");
        Check(report.Official.Where(row => row.Group == RecommendationPolicy.OfficialLabel(OfficialStatus.Operational)).All(row => row.Counts.Slow == 1 && row.Counts.Error == 1), "operational with user slow/error remains separate");
        Check(report.Official.Where(row => row.Group == RecommendationPolicy.OfficialLabel(OfficialStatus.PartialOutage)).All(row => row.Counts.Success == 1), "official outage with successful usage preserved");
        Check(report.Providers.All(row => row.Counts.Adverse == 3), "problem experience aggregate");
        Check(report.OperationalHours.Where(row => row.Group.StartsWith("12:", StringComparison.Ordinal)).All(row => row.Counts.Total == 3 && row.Counts.Adverse == 3), "officially operational but problematic KST hours");
        Check(report.Schedules.Where(row => row.Group == "DST × BURGER").All(row => row.Counts.Total == 1), "schedule comparison inside DST cohort");
        Check(report.Schedules.Where(row => row.Group == "Standard × BURGER").All(row => row.Counts.Total == 1), "schedule comparison inside standard cohort");
        StatisticsReport small = StatisticsAnalysis.Build([fixtures[0]]);
        Check(small.Providers.Single(row => row.Provider == "OpenAI").Counts.Total == 1, "small sample n=1");
        Check(small.Providers.Single(row => row.Provider == "Claude").Counts.Cell(0) == "No data", "provider missing within nonempty data");
        StatisticsReport mixed = StatisticsAnalysis.Build([fixtures[0] with { PacificIsDst = false }]);
        Check(mixed.Schedules.Single(row => row.Provider == "OpenAI" && row.Group.StartsWith("US Mixed", StringComparison.Ordinal)).Counts.Total == 1, "DST transition mixed ET/PT explicitly separated");

        UsageStore reopened = new(path);
        Check((await reopened.ReadUsageAsync(null)).Count == fixtures.Count, "restart/reopen existing data");
        ScheduleSnapshot schedule = AgentSchedule.GetSnapshot(now);
        foreach (ProviderKind provider in Enum.GetValues<ProviderKind>())
            await store.SaveProviderAsync(new(provider, OfficialStatus.Operational, now, now, "Operational", Source: "fixture://official"), schedule, Recommendation.Go);
        Check(Scalar(path, "SELECT COUNT(*) FROM ProviderStatusHistory;") == 3, "first provider states captured");
        ProviderStatus update = new(ProviderKind.OpenAI, OfficialStatus.Operational, now.AddMinutes(5), now.AddMinutes(5),
            "Operational", Source: "fixture://official");
        await store.SaveProviderAsync(update, schedule, Recommendation.Go);
        Check(Scalar(path, "SELECT COUNT(*) FROM ProviderStatusHistory;") == 3, "identical polls not added to history");
        Check((await reopened.ReadLatestStatusesAsync()).Single(row => row.Provider == ProviderKind.OpenAI) == update, "cache and last success updated without history growth");
        ProviderStatus incident = update with { Status = OfficialStatus.Degraded, RelevantComponent = "ChatGPT", IncidentId = "test-1", IncidentTitle = "Test issue", Reason = "Slow" };
        await store.SaveProviderAsync(incident, schedule, Recommendation.Hold);
        await store.SaveProviderAsync(incident with { CheckedAtUtc = now.AddMinutes(10) }, schedule, Recommendation.Hold);
        Check(Scalar(path, "SELECT COUNT(*) FROM ProviderStatusHistory;") == 4, "changed state saved and duplicate incident deduplicated");
        await store.SaveProviderAsync(incident with { IncidentId = "test-2" }, schedule, Recommendation.Hold);
        Check(Scalar(path, "SELECT COUNT(*) FROM ProviderStatusHistory;") == 5, "incident identity change saved");
        await store.SaveProviderAsync(incident with { IncidentId = "test-2" }, schedule, Recommendation.BurgerServiceIssue);
        Check(Scalar(path, "SELECT COUNT(*) FROM ProviderStatusHistory;") == 6, "effective recommendation change saved");
        ProviderStatus failed = incident with { Status = OfficialStatus.Stale, LastKnownStatus = OfficialStatus.Degraded, CheckedAtUtc = now.AddMinutes(30), Reason = "Network unavailable" };
        await store.SaveProviderAsync(failed, schedule, Recommendation.Check);
        Check((await reopened.ReadLatestStatusesAsync()).Single(row => row.Provider == ProviderKind.OpenAI) == failed, "stale cache preserves last successful instant, last-known state and source metadata");
        Check((await store.ReadLatestStatusesAsync()).Count == 3, "one failed provider does not remove others");

        using (CancellationTokenSource canceled = new())
        {
            canceled.Cancel();
            bool cancelled = false;
            try { await store.AddUsageAsync(fixtures[0] with { EventId = "must-not-write" }, canceled.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && (await store.ReadUsageAsync(null)).Count == 12, "canceled writes do not mutate data");
        }
        bool duplicateFailed = false;
        try { await store.AddUsageBatchAsync([fixtures[0] with { EventId = "rollback-new" }, fixtures[0]]); }
        catch (SqliteException) { duplicateFailed = true; }
        Check(duplicateFailed && (await store.ReadUsageAsync(null)).Count == 12, "failed transaction rolls back all inserts");

        List<UsageMeasurement> large = Enumerable.Range(0, 10_000).Select(i => fixtures[i % fixtures.Count] with
        {
            EventId = "bulk-" + i, TimestampUtc = now.AddMinutes(-i)
        }).ToList();
        await store.AddUsageBatchAsync(large);
        IReadOnlyList<UsageMeasurement> many = await reopened.ReadUsageAsync(null);
        Check(many.Count == 10_012, "10k transactional write and reopen");
        Check(StatisticsAnalysis.Build(many).Providers.Sum(row => row.Counts.Total) == 10_012, "10k statistics sample counts");
        Check(Scalar(path, "SELECT COUNT(*) FROM pragma_integrity_check WHERE integrity_check='ok';") == 1, "database integrity after writes and rollback");

        // No connection pooling or persistent handles: deleting this isolated DB
        // models the supported user-deleted-database recovery at next operation.
        File.Delete(path);
        Check(!File.Exists(path), "isolated database deletion");
        await store.InitializeAsync();
        Check((await store.ReadUsageAsync(null)).Count == 0 && (await store.ReadLatestStatusesAsync()).Count == 0, "deleted DB recreates cleanly on same store");
        await store.AddUsageAsync(fixtures[0]);
        Check((await new UsageStore(path).ReadUsageAsync(null)).Count == 1, "recreated database remains usable");

        // Rows this version cannot interpret (unknown or numeric enum text) must not break reads.
        const string usageColumns = "EventType,TimestampUtc,ScheduleState,WeekendExtendedFullThrottle,EasternUtcOffsetMinutes,PacificUtcOffsetMinutes,EasternIsDst,PacificIsDst,SchedulePolicyVersion,OfficialStatus,EffectiveRecommendation,RelevantComponent,IncidentId,UserNote,AppVersion,HolidayAdjustmentEnabled,HolidayExtendedFullThrottle,HolidayNames";
        Execute(path, $"INSERT INTO UsageEvents(EventId,Provider,{usageColumns}) SELECT 'unknown-provider','FutureProvider',{usageColumns} FROM UsageEvents LIMIT 1;" +
            $"INSERT INTO UsageEvents(EventId,Provider,{usageColumns}) SELECT 'numeric-provider','7',{usageColumns} FROM UsageEvents LIMIT 1;" +
            "INSERT INTO ProviderStatusCache(Provider,CheckedAtUtc,OfficialStatus,EffectiveRecommendation,RelevantComponent,IncidentId," +
            "IncidentTitle,ScheduleStateAtCheck,LastSuccessfulCheckUtc,Source,Reason,LastKnownStatus) VALUES('FutureProvider'," +
            "'2026-09-19T00:00:00.0000000Z','Operational','Go','','','','FullThrottle',NULL,'','',NULL);");
        var (readable, skipped) = await store.ReadUsageWithSkippedAsync(null);
        Check(readable.Count == 1 && skipped == 2, "unreadable usage rows are skipped and counted");
        Check((await store.ReadUsageAsync(null)).Count == 1, "usage read continues past unreadable rows");
        Check((await store.ReadLatestStatusesAsync()).Count == 0, "unreadable cached status row is skipped");
        return assertions + await HolidayStorageTests.RunAsync(tempDirectory);
    }

    private static void Execute(string path, string sql)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
