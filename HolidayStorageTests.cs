using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

internal static class HolidayStorageTests
{
    private const string LegacyUsageColumns = "EventId,Provider,EventType,TimestampUtc,ScheduleState," +
        "WeekendExtendedFullThrottle,EasternUtcOffsetMinutes,PacificUtcOffsetMinutes,EasternIsDst," +
        "PacificIsDst,SchedulePolicyVersion,OfficialStatus,EffectiveRecommendation,RelevantComponent," +
        "IncidentId,UserNote,AppVersion";

    public static async Task<int> RunAsync(string tempDirectory)
    {
        // Every path is a freshly allocated test directory, never the user's DB.
        string directory = Path.Combine(Path.GetFullPath(tempDirectory), "holiday-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int assertions = 0;
        void Check(bool result, string description)
        {
            assertions++;
            if (!result) throw new InvalidOperationException("Holiday storage test failed: " + description);
        }

        string freshPath = Path.Combine(directory, "fresh.db");
        UsageStore fresh = new(freshPath);
        Check(await fresh.GetHolidayAdjustmentAsync(), "new database defaults to holiday adjustment ON");
        Check(!Directory.EnumerateFiles(directory, "fresh.pre-schema2-*.db").Any(), "first-run creation needs no migration backup");
        Check(Scalar(freshPath, "SELECT COUNT(*) FROM pragma_table_info('UsageEvents') WHERE name IN ('HolidayAdjustmentEnabled','HolidayExtendedFullThrottle','HolidayNames');") == 3, "new holiday columns present");
        await fresh.SetHolidayAdjustmentAsync(false);
        Check(!await new UsageStore(freshPath).GetHolidayAdjustmentAsync(), "OFF setting survives process-style reopen");
        await fresh.SetHolidayAdjustmentAsync(true);
        Check(await new UsageStore(freshPath).GetHolidayAdjustmentAsync(), "ON setting survives reopen");
        using (CancellationTokenSource canceled = new())
        {
            canceled.Cancel();
            bool canceledWrite = false;
            try { await fresh.SetHolidayAdjustmentAsync(false, canceled.Token); }
            catch (OperationCanceledException) { canceledWrite = true; }
            Check(canceledWrite && await fresh.GetHolidayAdjustmentAsync(), "canceled setting write preserves prior value");
        }

        string legacyPath = Path.Combine(directory, "legacy.db");
        CreateVersionOne(legacyPath);
        using SqliteConnection keepWalOpen = Open(legacyPath);
        using (SqliteCommand enableWal = keepWalOpen.CreateCommand())
        {
            enableWal.CommandText = "PRAGMA journal_mode=WAL;";
            Check(string.Equals(enableWal.ExecuteScalar() as string, "wal", StringComparison.OrdinalIgnoreCase), "migration fixture uses WAL");
        }
        // An idle sqlite3_open handle need not participate in WAL at all. Hold a
        // real read snapshot so the following committed write cannot checkpoint.
        using SqliteTransaction pinnedWal = keepWalOpen.BeginTransaction(deferred: true);
        using (SqliteCommand beginRead = keepWalOpen.CreateCommand())
        {
            beginRead.Transaction = pinnedWal;
            beginRead.CommandText = "SELECT COUNT(*) FROM AppMetadata;";
            beginRead.ExecuteScalar();
        }
        Execute(legacyPath, "INSERT INTO AppMetadata VALUES('CommittedWalValue','must be backed up');");
        Check(File.Exists(legacyPath + "-wal") && new FileInfo(legacyPath + "-wal").Length > 0, "fixture retains committed changes in WAL before migration");
        string legacyUsage = Rows(legacyPath, "SELECT " + LegacyUsageColumns + " FROM UsageEvents ORDER BY EventId;");
        string legacyHistory = Rows(legacyPath, "SELECT * FROM ProviderStatusHistory ORDER BY StatusId;");
        string legacyCache = Rows(legacyPath, "SELECT * FROM ProviderStatusCache ORDER BY Provider;");
        UsageStore migrated = new(legacyPath);
        await migrated.InitializeAsync();
        Check(Scalar(legacyPath, "PRAGMA user_version;") == 2, "populated genuine v1 database upgraded to v2");
        Check(Scalar(legacyPath, "SELECT CAST(Value AS INTEGER) FROM AppMetadata WHERE Key='SchemaVersion';") == 2, "metadata version upgraded in same transaction");
        Check(Scalar(legacyPath, "SELECT COUNT(*) FROM AppMetadata WHERE Key='ExistingSetting' AND Value='keep me';") == 1, "unrelated metadata preserved");
        Check(Rows(legacyPath, "SELECT " + LegacyUsageColumns + " FROM UsageEvents ORDER BY EventId;") == legacyUsage, "all original event values preserved exactly");
        Check(Rows(legacyPath, "SELECT * FROM ProviderStatusHistory ORDER BY StatusId;") == legacyHistory, "all history values and IDs preserved exactly");
        Check(Rows(legacyPath, "SELECT * FROM ProviderStatusCache ORDER BY Provider;") == legacyCache, "all status cache values preserved exactly");
        UsageMeasurement old = (await migrated.ReadUsageAsync(null)).Single();
        Check(old.HolidayAdjustmentEnabled is null && !old.HolidayExtendedFullThrottle && old.HolidayNames.Length == 0, "old event defaults to unrecorded setting, never guessed OFF or holiday");
        Check(old.SchedulePolicyVersion == "us-business-et09-pt18-v1" && old.ScheduleState == AgentState.BurgerTime, "historical schedule classification not recomputed");
        Check(old.UserNote == "기존 기록 'quoted'", "Unicode and quoted legacy note retained");
        Check(await migrated.GetHolidayAdjustmentAsync(), "v1 upgrade with no setting defaults to ON");
        ProviderStatus cached = (await migrated.ReadLatestStatusesAsync()).Single();
        Check(cached.Status == OfficialStatus.Stale && cached.LastKnownStatus == OfficialStatus.Degraded && cached.IncidentId == "legacy-incident", "reopened cache preserves stale incident state");

        string backup = Directory.EnumerateFiles(directory, "legacy.pre-schema2-*.db").Single();
        Check(Scalar(backup, "PRAGMA user_version;") == 1, "pre-migration backup remains schema 1");
        Check(Rows(backup, "SELECT " + LegacyUsageColumns + " FROM UsageEvents ORDER BY EventId;") == legacyUsage, "backup contains original event values");
        Check(Rows(backup, "SELECT * FROM ProviderStatusHistory ORDER BY StatusId;") == legacyHistory, "backup contains original status history");
        Check(Rows(backup, "SELECT * FROM ProviderStatusCache ORDER BY Provider;") == legacyCache, "backup contains original status cache");
        Check(Scalar(backup, "SELECT COUNT(*) FROM pragma_quick_check WHERE quick_check='ok';") == 1, "backup integrity verified");
        Check(Scalar(backup, "SELECT COUNT(*) FROM AppMetadata WHERE Key='CommittedWalValue' AND Value='must be backed up';") == 1, "backup includes committed WAL contents");
        Check(Scalar(backup, "SELECT COUNT(*) FROM pragma_table_info('UsageEvents') WHERE name='HolidayAdjustmentEnabled';") == 0, "backup was taken before schema mutation");
        await new UsageStore(legacyPath).InitializeAsync();
        Check(Directory.EnumerateFiles(directory, "legacy.pre-schema2-*.db").Count() == 1, "v2 reopen is idempotent and creates no redundant backup");

        UsageMeasurement holiday = old with
        {
            EventId = "holiday-new", EventType = UsageEventType.Success, ScheduleState = AgentState.FullThrottle,
            WeekendExtendedFullThrottle = true, SchedulePolicyVersion = "us-business-et09-pt18-v2",
            EffectiveRecommendation = Recommendation.Go, OfficialStatus = OfficialStatus.Operational,
            HolidayAdjustmentEnabled = true, HolidayExtendedFullThrottle = true,
            HolidayNames = "Washington's Birthday · 공휴일", AppVersion = "2.1-test"
        };
        UsageMeasurement disabled = holiday with
        {
            EventId = "holiday-off", EventType = UsageEventType.Error, ScheduleState = AgentState.BurgerTime,
            EffectiveRecommendation = Recommendation.BurgerTime, WeekendExtendedFullThrottle = false,
            HolidayAdjustmentEnabled = false, HolidayExtendedFullThrottle = false, HolidayNames = ""
        };
        UsageMeasurement ordinaryWeekend = holiday with
        {
            EventId = "ordinary-weekend", EventType = UsageEventType.Slow,
            HolidayExtendedFullThrottle = false, HolidayNames = ""
        };
        await migrated.AddUsageBatchAsync([holiday, disabled, ordinaryWeekend]);
        await migrated.SetHolidayAdjustmentAsync(false);
        UsageStore restarted = new(legacyPath);
        IReadOnlyList<UsageMeasurement> read = await restarted.ReadUsageAsync(null);
        foreach (UsageMeasurement expected in new[] { old, holiday, disabled, ordinaryWeekend })
            Check(read.Single(item => item.EventId == expected.EventId) == expected, "every old/new holiday field survives restart");
        Check(!await restarted.GetHolidayAdjustmentAsync(), "migrated database persists explicit OFF");
        Check((await restarted.ReadLatestStatusesAsync()).Single() == cached, "usage/settings writes do not alter official state cache");

        StatisticsReport report = StatisticsAnalysis.Build(read);
        EventCounts Count(string group, string provider = "OpenAI") =>
            report.Schedules.Single(row => row.Provider == provider && row.Group == group).Counts;
        Check(Count("공휴일 보정 ON").Total == 2, "ON cohort independent of current setting");
        Check(Count("공휴일 보정 OFF").Total == 1, "explicit OFF cohort counted separately");
        Check(Count("공휴일 보정 미기록 (이전 버전)").Total == 1, "legacy unknown setting not conflated with OFF");
        Check(Count("공휴일 연장 FULL (주말 중복 포함)").Success == 1, "holiday extension includes overlapping weekend");
        Check(Count("Weekend / Extended FULL (공휴일 제외)").Slow == 1 && Count("Weekend / Extended FULL (공휴일 제외)").Total == 1, "ordinary weekend excludes holiday extension");
        Check(Count("일반 FULL").Cell(0) == "No data", "holiday and weekend extensions excluded from ordinary FULL");
        Check(Count("Policy: us-business-et09-pt18-v1 × BURGER").Total == 1, "legacy FULL/BURGER comparison isolated by policy");
        Check(Count("Policy: us-business-et09-pt18-v2 × FULL").Total == 2, "new policy FULL cohort isolated");
        Check(Count("Policy: us-business-et09-pt18-v2 × 공휴일 OFF × BURGER").Error == 1, "policy and holiday OFF combination isolated");
        Check(Count("Policy: us-business-et09-pt18-v2 × 공휴일 ON × BURGER").Cell(0) == "No data", "empty setting/schedule combination displayed as No data");
        Check(Count("Policy: us-business-et09-pt18-v2 × FULL", "Claude").Cell(0) == "No data", "missing provider shown for existing policy");
        StatisticsReport empty = StatisticsAnalysis.Build([]);
        Check(empty.Schedules.Where(row => row.Group.StartsWith("공휴일", StringComparison.Ordinal)).All(row => row.Counts.Cell(0) == "No data"), "empty holiday cohorts do not show zero percent");

        // Make the third ALTER fail: the first two must roll back, together with
        // both schema version markers. No production database is involved.
        string rollbackPath = Path.Combine(directory, "rollback.db");
        CreateVersionOne(rollbackPath);
        Execute(rollbackPath, "ALTER TABLE UsageEvents ADD COLUMN HolidayNames TEXT NOT NULL DEFAULT ''; ");
        string rollbackBefore = Rows(rollbackPath, "SELECT * FROM UsageEvents ORDER BY EventId;");
        bool migrationFailed = false;
        UsageStore failingStore = new(rollbackPath);
        try { await failingStore.InitializeAsync(); }
        catch (InvalidOperationException ex) when (ex.InnerException is SqliteException) { migrationFailed = true; }
        Check(migrationFailed, "inconsistent legacy schema fails migration visibly");
        Check(Scalar(rollbackPath, "PRAGMA user_version;") == 1 && Scalar(rollbackPath, "SELECT CAST(Value AS INTEGER) FROM AppMetadata WHERE Key='SchemaVersion';") == 1, "failed migration preserves both version markers");
        Check(Scalar(rollbackPath, "SELECT COUNT(*) FROM pragma_table_info('UsageEvents') WHERE name IN ('HolidayAdjustmentEnabled','HolidayExtendedFullThrottle');") == 0, "partial ALTER operations rolled back");
        Check(Rows(rollbackPath, "SELECT * FROM UsageEvents ORDER BY EventId;") == rollbackBefore, "failed migration preserves original event values");
        Check(Directory.EnumerateFiles(directory, "rollback.pre-schema2-*.db").Count() == 1, "failed migration retains pre-migration recovery backup");
        for (int retry = 0; retry < 3; retry++)
        {
            bool retryStopped = false;
            try { await failingStore.ReadUsageAsync(null); }
            catch (InvalidOperationException ex) { retryStopped = ex.Message.Contains("다시 시작", StringComparison.Ordinal); }
            Check(retryStopped, "failed migration latched for store lifetime with restart guidance");
        }
        Check(Directory.EnumerateFiles(directory, "rollback.pre-schema2-*.db").Count() == 1, "repeated store operations after failure create no additional backup");

        string futurePath = Path.Combine(directory, "future.db");
        CreateVersionOne(futurePath);
        Execute(futurePath, "PRAGMA user_version=99; UPDATE AppMetadata SET Value='99' WHERE Key='SchemaVersion';");
        bool futureRejected = false;
        try { await new UsageStore(futurePath).InitializeAsync(); }
        catch (InvalidOperationException) { futureRejected = true; }
        Check(futureRejected && Scalar(futurePath, "PRAGMA user_version;") == 99, "future schema rejected without downgrade");
        Check(!Directory.EnumerateFiles(directory, "future.pre-schema2-*.db").Any(), "future schema does not trigger migration or backup");
        Check(Scalar(legacyPath, "SELECT COUNT(*) FROM pragma_integrity_check WHERE integrity_check='ok';") == 1, "migrated database integrity after restart/write");
        return assertions;
    }

    private static void CreateVersionOne(string path) => Execute(path, """
        PRAGMA journal_mode=WAL;
        CREATE TABLE AppMetadata(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
        INSERT INTO AppMetadata VALUES('SchemaVersion','1'),('ExistingSetting','keep me');
        CREATE TABLE UsageEvents(
          EventId TEXT PRIMARY KEY, Provider TEXT NOT NULL, EventType TEXT NOT NULL,
          TimestampUtc TEXT NOT NULL, ScheduleState TEXT NOT NULL,
          WeekendExtendedFullThrottle INTEGER NOT NULL, EasternUtcOffsetMinutes INTEGER NOT NULL,
          PacificUtcOffsetMinutes INTEGER NOT NULL, EasternIsDst INTEGER NOT NULL, PacificIsDst INTEGER NOT NULL,
          SchedulePolicyVersion TEXT NOT NULL, OfficialStatus TEXT NOT NULL, EffectiveRecommendation TEXT NOT NULL,
          RelevantComponent TEXT NOT NULL, IncidentId TEXT NOT NULL, UserNote TEXT NOT NULL, AppVersion TEXT NOT NULL);
        CREATE INDEX IX_UsageEvents_Timestamp ON UsageEvents(TimestampUtc);
        CREATE INDEX IX_UsageEvents_ProviderTimestamp ON UsageEvents(Provider,TimestampUtc);
        INSERT INTO UsageEvents VALUES('legacy-event','OpenAI','Interrupted','2026-09-07T15:00:00.0000000Z',
          'BurgerTime',0,-240,-420,1,1,'us-business-et09-pt18-v1','Stale','BurgerCheck',
          'ChatGPT','legacy-incident','기존 기록 ''quoted''','2.0.2');
        CREATE TABLE ProviderStatusHistory(
          StatusId INTEGER PRIMARY KEY AUTOINCREMENT, Provider TEXT NOT NULL, CheckedAtUtc TEXT NOT NULL,
          OfficialStatus TEXT NOT NULL, EffectiveRecommendation TEXT NOT NULL, RelevantComponent TEXT NOT NULL,
          IncidentId TEXT NOT NULL, IncidentTitle TEXT NOT NULL, ScheduleStateAtCheck TEXT NOT NULL,
          LastSuccessfulCheckUtc TEXT, Source TEXT NOT NULL, Reason TEXT NOT NULL, LastKnownStatus TEXT);
        CREATE INDEX IX_StatusHistory_ProviderStatusId ON ProviderStatusHistory(Provider,StatusId DESC);
        CREATE INDEX IX_StatusHistory_Checked ON ProviderStatusHistory(CheckedAtUtc);
        INSERT INTO ProviderStatusHistory VALUES(17,'OpenAI','2026-09-07T15:00:00.0000000Z','Stale','BurgerCheck',
          'ChatGPT','legacy-incident','기존 장애','BurgerTime','2026-09-07T14:40:00.0000000Z','fixture://official','network failed','Degraded');
        CREATE TABLE ProviderStatusCache(
          Provider TEXT PRIMARY KEY, CheckedAtUtc TEXT NOT NULL, OfficialStatus TEXT NOT NULL,
          EffectiveRecommendation TEXT NOT NULL, RelevantComponent TEXT NOT NULL, IncidentId TEXT NOT NULL,
          IncidentTitle TEXT NOT NULL, ScheduleStateAtCheck TEXT NOT NULL, LastSuccessfulCheckUtc TEXT,
          Source TEXT NOT NULL, Reason TEXT NOT NULL, LastKnownStatus TEXT);
        INSERT INTO ProviderStatusCache SELECT Provider,CheckedAtUtc,OfficialStatus,EffectiveRecommendation,RelevantComponent,
          IncidentId,IncidentTitle,ScheduleStateAtCheck,LastSuccessfulCheckUtc,Source,Reason,LastKnownStatus FROM ProviderStatusHistory;
        PRAGMA user_version=1;
        """);

    private static void Execute(string path, string sql)
    {
        using SqliteConnection connection = Open(path);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql)
    {
        using SqliteConnection connection = Open(path);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Rows(string path, string sql)
    {
        using SqliteConnection connection = Open(path);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<object?[]> rows = [];
        while (reader.Read())
        {
            object?[] values = new object?[reader.FieldCount];
            for (int i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(values);
        }
        return JsonSerializer.Serialize(rows);
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
