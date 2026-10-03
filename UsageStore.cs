using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

// Connections live for one operation. SQLite work runs off the UI thread and is
// serialized within this store; WAL and transactions also protect other readers.
internal sealed class UsageStore(string? databasePath = null)
{
    internal const int SchemaVersion = 2;
    internal const string HolidaySettingKey = "UsFederalHolidaysEnabled";
    private const int MaximumQuotaCacheCharacters = 512 * 1024;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Exception? schemaInitializationFailure;
    public string DatabasePath { get; } = databasePath ?? Path.Combine(AppPaths.DataDirectory, "burgerclock.db");

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ => true, cancellationToken);

    // Two bounded cache entries in existing metadata; no measurement/history schema changes.
    internal Task SaveQuotaAsync(QuotaCache cache, CancellationToken cancellationToken = default) =>
        ExecuteAsync(connection =>
        {
            ValidateQuotaCache(cache, cache.Reading.Provider);
            string json = JsonSerializer.Serialize(cache, QuotaJsonContext.Default.QuotaCache);
            if (json.Length > MaximumQuotaCacheCharacters) throw new InvalidDataException("Invalid quota cache size.");
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO AppMetadata(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
            command.Parameters.AddWithValue("$key", "AccountQuota.v1." + cache.Reading.Provider);
            command.Parameters.AddWithValue("$value", json);
            command.ExecuteNonQuery();
            return true;
        }, cancellationToken);

    internal Task<QuotaCache?> ReadQuotaAsync(QuotaProvider provider, CancellationToken cancellationToken = default) =>
        ExecuteAsync<QuotaCache?>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM AppMetadata WHERE Key=$key;";
            command.Parameters.AddWithValue("$key", "AccountQuota.v1." + provider);
            if (command.ExecuteScalar() is not string json) return null;
            if (json.Length > MaximumQuotaCacheCharacters) throw new InvalidDataException("Invalid quota cache size.");
            var cache = JsonSerializer.Deserialize(json, QuotaJsonContext.Default.QuotaCache) ?? throw new InvalidDataException("Invalid quota cache.");
            ValidateQuotaCache(cache, provider);
            return cache;
        }, cancellationToken);

    internal static void ValidateQuotaCache(QuotaCache cache, QuotaProvider provider)
    {
        if (cache.Version != 1 || !Enum.IsDefined(provider) || cache.Reading is null || cache.Reading.Provider != provider ||
            cache.Reading.Windows is not { Count: > 0 and <= 64 } || cache.ResetAnchors is not { Count: <= 64 } ||
            cache.SuccessfulAtUtc < DateTimeOffset.UnixEpoch ||
            cache.Reading.Windows.Any(w => w is null || string.IsNullOrWhiteSpace(w.Id) || w.Id.Length > 512 ||
                string.IsNullOrWhiteSpace(w.Label) || w.Label.Length > 180 || !double.IsFinite(w.UsedPercent) ||
                w.UsedPercent < 0 || w.UsedPercent > 100 || w.WindowMinutes is <= 0 ||
                w.ResetsAtUtc is { } reset && (reset.Year < 1970 || reset.Year > 9000)) ||
            cache.ResetAnchors.Any(r => r.Year < 1970 || r.Year > 9000) ||
            cache.Reading.Windows.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() != cache.Reading.Windows.Count)
            throw new InvalidDataException("Invalid quota cache.");
    }

    // Missing means the new default applies. Invalid data is reported rather than
    // silently changing the user's selected schedule policy.
    public Task<bool> GetHolidayAdjustmentAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(connection =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM AppMetadata WHERE Key=$key;";
            command.Parameters.AddWithValue("$key", HolidaySettingKey);
            return command.ExecuteScalar() switch
            {
                null => true,
                "1" => true,
                "0" => false,
                _ => throw new InvalidOperationException("미국 연방 공휴일 보정 설정을 읽을 수 없습니다.")
            };
        }, cancellationToken);

    public Task SetHolidayAdjustmentAsync(bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO AppMetadata(Key,Value) VALUES($key,$value)
                ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;
                """;
            command.Parameters.AddWithValue("$key", HolidaySettingKey);
            command.Parameters.AddWithValue("$value", enabled ? "1" : "0");
            command.ExecuteNonQuery();
            return true;
        }, cancellationToken);

    public Task AddUsageAsync(UsageMeasurement item, CancellationToken cancellationToken = default) =>
        AddUsageBatchAsync([item], cancellationToken);

    // Each column sits next to its value, so adding a column cannot misalign parameters.
    private static readonly (string Column, Func<UsageMeasurement, object> Value)[] UsageColumns =
    [
        ("EventId", item => item.EventId),
        ("Provider", item => item.Provider.ToString()),
        ("EventType", item => item.EventType.ToString()),
        ("TimestampUtc", item => Utc(item.TimestampUtc)),
        ("ScheduleState", item => item.ScheduleState.ToString()),
        ("WeekendExtendedFullThrottle", item => item.WeekendExtendedFullThrottle),
        ("EasternUtcOffsetMinutes", item => item.EasternUtcOffsetMinutes),
        ("PacificUtcOffsetMinutes", item => item.PacificUtcOffsetMinutes),
        ("EasternIsDst", item => item.EasternIsDst),
        ("PacificIsDst", item => item.PacificIsDst),
        ("SchedulePolicyVersion", item => item.SchedulePolicyVersion),
        ("OfficialStatus", item => item.OfficialStatus.ToString()),
        ("EffectiveRecommendation", item => item.EffectiveRecommendation.ToString()),
        ("RelevantComponent", item => item.RelevantComponent),
        ("IncidentId", item => item.IncidentId),
        ("UserNote", item => item.UserNote),
        ("AppVersion", item => item.AppVersion),
        ("HolidayAdjustmentEnabled", item => (object?)item.HolidayAdjustmentEnabled ?? DBNull.Value),
        ("HolidayExtendedFullThrottle", item => item.HolidayExtendedFullThrottle),
        ("HolidayNames", item => item.HolidayNames),
    ];

    private static readonly string InsertUsageSql =
        "INSERT INTO UsageEvents(" + string.Join(",", UsageColumns.Select(c => c.Column)) +
        ") VALUES(" + string.Join(",", UsageColumns.Select(c => "$" + c.Column)) + ");";

    internal Task AddUsageBatchAsync(IReadOnlyList<UsageMeasurement> items, CancellationToken cancellationToken = default) =>
        ExecuteAsync(connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = InsertUsageSql;
            foreach (var (column, _) in UsageColumns)
                command.Parameters.Add(new SqliteParameter("$" + column, null));
            foreach (UsageMeasurement item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int i = 0; i < UsageColumns.Length; i++) command.Parameters[i].Value = UsageColumns[i].Value(item);
                command.ExecuteNonQuery();
            }
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return true;
        }, cancellationToken);

    public async Task<IReadOnlyList<UsageMeasurement>> ReadUsageAsync(DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken = default) =>
        (await ReadUsageWithSkippedAsync(sinceUtc, cancellationToken).ConfigureAwait(false)).Rows;

    // A row this version cannot interpret (e.g. written by a newer version) is skipped
    // and counted instead of failing every statistics read.
    public Task<(IReadOnlyList<UsageMeasurement> Rows, int Skipped)> ReadUsageWithSkippedAsync(DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken = default) => ExecuteAsync<(IReadOnlyList<UsageMeasurement>, int)>(connection =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT EventId,Provider,EventType,TimestampUtc,ScheduleState,WeekendExtendedFullThrottle,
                  EasternUtcOffsetMinutes,PacificUtcOffsetMinutes,EasternIsDst,PacificIsDst,SchedulePolicyVersion,
                  OfficialStatus,EffectiveRecommendation,RelevantComponent,IncidentId,UserNote,AppVersion,
                  HolidayAdjustmentEnabled,HolidayExtendedFullThrottle,HolidayNames
                FROM UsageEvents
                """ + (sinceUtc.HasValue ? " WHERE TimestampUtc >= $since" : "") + " ORDER BY TimestampUtc,EventId;";
            if (sinceUtc.HasValue) command.Parameters.AddWithValue("$since", Utc(sinceUtc.Value));
            using SqliteDataReader reader = command.ExecuteReader();
            List<UsageMeasurement> rows = [];
            int skipped = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    rows.Add(new UsageMeasurement(reader.GetString(0), ParseName<ProviderKind>(reader.GetString(1)),
                        ParseName<UsageEventType>(reader.GetString(2)), ParseUtc(reader.GetString(3)),
                        ParseName<AgentState>(reader.GetString(4)), reader.GetBoolean(5), reader.GetInt32(6),
                        reader.GetInt32(7), reader.GetBoolean(8), reader.GetBoolean(9), reader.GetString(10),
                        ParseName<OfficialStatus>(reader.GetString(11)), ParseName<Recommendation>(reader.GetString(12)),
                        reader.GetString(13), reader.GetString(14), reader.GetString(15), reader.GetString(16),
                        reader.IsDBNull(17) ? null : reader.GetBoolean(17), reader.GetBoolean(18), reader.GetString(19)));
                }
                catch (FormatException) { skipped++; }
            }
            return (rows, skipped);
        }, cancellationToken);

    public Task SaveProviderAsync(ProviderStatus status, ScheduleSnapshot schedule, Recommendation recommendation,
        CancellationToken cancellationToken = default) => ExecuteAsync(connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            bool changed;
            using (SqliteCommand previous = connection.CreateCommand())
            {
                previous.Transaction = transaction;
                previous.CommandText = """
                    SELECT OfficialStatus,EffectiveRecommendation,RelevantComponent,IncidentId,IncidentTitle,LastKnownStatus
                    FROM ProviderStatusHistory WHERE Provider=$provider ORDER BY StatusId DESC LIMIT 1;
                    """;
                previous.Parameters.AddWithValue("$provider", status.Provider.ToString());
                using SqliteDataReader reader = previous.ExecuteReader();
                changed = !reader.Read() || reader.GetString(0) != status.Status.ToString() ||
                    reader.GetString(1) != recommendation.ToString() || reader.GetString(2) != status.RelevantComponent ||
                    reader.GetString(3) != status.IncidentId || reader.GetString(4) != status.IncidentTitle ||
                    (reader.IsDBNull(5) ? null : reader.GetString(5)) != status.LastKnownStatus?.ToString();
            }
            cancellationToken.ThrowIfCancellationRequested();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            const string columns = "Provider,CheckedAtUtc,OfficialStatus,EffectiveRecommendation,RelevantComponent," +
                "IncidentId,IncidentTitle,ScheduleStateAtCheck,LastSuccessfulCheckUtc,Source,Reason,LastKnownStatus";
            const string values = "$provider,$checked,$official,$recommendation,$component,$incident,$title," +
                "$schedule,$success,$source,$reason,$known";
            command.CommandText = "INSERT INTO ProviderStatusCache(" + columns + ") VALUES(" + values + ") " +
                "ON CONFLICT(Provider) DO UPDATE SET CheckedAtUtc=excluded.CheckedAtUtc," +
                "OfficialStatus=excluded.OfficialStatus,EffectiveRecommendation=excluded.EffectiveRecommendation," +
                "RelevantComponent=excluded.RelevantComponent,IncidentId=excluded.IncidentId,IncidentTitle=excluded.IncidentTitle," +
                "ScheduleStateAtCheck=excluded.ScheduleStateAtCheck,LastSuccessfulCheckUtc=excluded.LastSuccessfulCheckUtc," +
                "Source=excluded.Source,Reason=excluded.Reason,LastKnownStatus=excluded.LastKnownStatus;";
            AddProviderParameters(command, status, schedule, recommendation);
            command.ExecuteNonQuery();
            if (changed)
            {
                command.CommandText = "INSERT INTO ProviderStatusHistory(" + columns + ") VALUES(" + values + ");";
                command.ExecuteNonQuery();
            }
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return true;
        }, cancellationToken);

    public Task<IReadOnlyList<ProviderStatus>> ReadLatestStatusesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<ProviderStatus>>(connection =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT Provider,OfficialStatus,CheckedAtUtc,LastSuccessfulCheckUtc,Reason,
                  RelevantComponent,IncidentId,IncidentTitle,Source,LastKnownStatus
                FROM ProviderStatusCache ORDER BY Provider;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            List<ProviderStatus> rows = [];
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // An unreadable cache row is only a missing cache entry; the next poll replaces it.
                try
                {
                    rows.Add(new ProviderStatus(ParseName<ProviderKind>(reader.GetString(0)),
                        ParseName<OfficialStatus>(reader.GetString(1)), ParseUtc(reader.GetString(2)),
                        reader.IsDBNull(3) ? null : ParseUtc(reader.GetString(3)), reader.GetString(4), reader.GetString(5),
                        reader.GetString(6), reader.GetString(7), reader.GetString(8),
                        reader.IsDBNull(9) ? null : ParseName<OfficialStatus>(reader.GetString(9))));
                }
                catch (FormatException) { }
            }
            return rows;
        }, cancellationToken);

    private async Task<T> ExecuteAsync<T>(Func<SqliteConnection, T> operation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (schemaInitializationFailure is not null)
                    throw SchemaInitializationError(schemaInitializationFailure);
                string? directory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
                if (directory is not null) Directory.CreateDirectory(directory);
                using SqliteConnection connection = new(new SqliteConnectionStringBuilder
                {
                    DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false, DefaultTimeout = 5
                }.ToString());
                connection.Open();
                try { EnsureSchema(connection); }
                catch (Exception ex) when (LatchesSchemaFailure(ex))
                {
                    // Polling must not repeatedly create whole-DB backups after a
                    // failed migration. The app owns one store; retry on restart.
                    schemaInitializationFailure = ex;
                    throw SchemaInitializationError(ex);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return operation(connection);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    // A failed backup verification (InvalidDataException) must also stop retries;
    // otherwise every poll would create another whole-DB backup. A newer schema is
    // rejected before any backup, so that check remains safe to repeat.
    internal static bool LatchesSchemaFailure(Exception ex) =>
        ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException;

    private static InvalidOperationException SchemaInitializationError(Exception cause) => new(
        "데이터베이스 준비에 실패하여 자동 재시도를 중단했습니다. 원본과 pre-schema2 백업을 확인한 뒤 앱을 다시 시작하세요. " + cause.Message,
        cause);

    private static void EnsureSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        long version = ReadSchemaVersion(command);
        if (version == SchemaVersion) return;

        if (version == 1) BackupBeforeMigration(connection);

        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteScalar();
        using SqliteTransaction transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        // A different store/process may have migrated after the initial check.
        // Recheck while holding SQLite's write transaction before ALTER TABLE.
        version = ReadSchemaVersion(command);
        if (version == SchemaVersion)
        {
            transaction.Commit();
            return;
        }
        if (version == 0)
        {
            command.CommandText = """
            CREATE TABLE IF NOT EXISTS AppMetadata(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS UsageEvents(
              EventId TEXT PRIMARY KEY, Provider TEXT NOT NULL, EventType TEXT NOT NULL,
              TimestampUtc TEXT NOT NULL, ScheduleState TEXT NOT NULL,
              WeekendExtendedFullThrottle INTEGER NOT NULL, EasternUtcOffsetMinutes INTEGER NOT NULL,
              PacificUtcOffsetMinutes INTEGER NOT NULL, EasternIsDst INTEGER NOT NULL, PacificIsDst INTEGER NOT NULL,
              SchedulePolicyVersion TEXT NOT NULL, OfficialStatus TEXT NOT NULL, EffectiveRecommendation TEXT NOT NULL,
              RelevantComponent TEXT NOT NULL, IncidentId TEXT NOT NULL, UserNote TEXT NOT NULL, AppVersion TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_UsageEvents_Timestamp ON UsageEvents(TimestampUtc);
            CREATE INDEX IF NOT EXISTS IX_UsageEvents_ProviderTimestamp ON UsageEvents(Provider,TimestampUtc);
            CREATE TABLE IF NOT EXISTS ProviderStatusHistory(
              StatusId INTEGER PRIMARY KEY AUTOINCREMENT, Provider TEXT NOT NULL, CheckedAtUtc TEXT NOT NULL,
              OfficialStatus TEXT NOT NULL, EffectiveRecommendation TEXT NOT NULL, RelevantComponent TEXT NOT NULL,
              IncidentId TEXT NOT NULL, IncidentTitle TEXT NOT NULL, ScheduleStateAtCheck TEXT NOT NULL,
              LastSuccessfulCheckUtc TEXT, Source TEXT NOT NULL, Reason TEXT NOT NULL, LastKnownStatus TEXT);
            CREATE INDEX IF NOT EXISTS IX_StatusHistory_ProviderStatusId ON ProviderStatusHistory(Provider,StatusId DESC);
            CREATE INDEX IF NOT EXISTS IX_StatusHistory_Checked ON ProviderStatusHistory(CheckedAtUtc);
            CREATE TABLE IF NOT EXISTS ProviderStatusCache(
              Provider TEXT PRIMARY KEY, CheckedAtUtc TEXT NOT NULL, OfficialStatus TEXT NOT NULL,
              EffectiveRecommendation TEXT NOT NULL, RelevantComponent TEXT NOT NULL, IncidentId TEXT NOT NULL,
              IncidentTitle TEXT NOT NULL, ScheduleStateAtCheck TEXT NOT NULL, LastSuccessfulCheckUtc TEXT,
              Source TEXT NOT NULL, Reason TEXT NOT NULL, LastKnownStatus TEXT);
            """;
            command.ExecuteNonQuery();
        }
        // Additive v1 -> v2 migration. NULL deliberately means the old event did
        // not record this setting: historical classifications are never guessed.
        command.CommandText = """
            ALTER TABLE UsageEvents ADD COLUMN HolidayAdjustmentEnabled INTEGER;
            ALTER TABLE UsageEvents ADD COLUMN HolidayExtendedFullThrottle INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE UsageEvents ADD COLUMN HolidayNames TEXT NOT NULL DEFAULT '';
            INSERT INTO AppMetadata(Key,Value) VALUES('SchemaVersion','2')
              ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;
            PRAGMA user_version=2;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    // Reads PRAGMA user_version and refuses a database written by a newer app version.
    private static long ReadSchemaVersion(SqliteCommand command)
    {
        command.CommandText = "PRAGMA user_version;";
        long version = (long)(command.ExecuteScalar() ?? 0L);
        if (version > SchemaVersion)
            throw new InvalidOperationException("이 데이터베이스는 더 새로운 AI Burger Clock 버전에서 생성되었습니다.");
        return version;
    }

    private static void BackupBeforeMigration(SqliteConnection source)
    {
        // SQLite's online backup API includes committed WAL contents; copying
        // only the .db file would not. Each migration attempt keeps a unique file.
        string original = Path.GetFullPath(source.DataSource);
        string backupPath = Path.Combine(Path.GetDirectoryName(original)!,
            Path.GetFileNameWithoutExtension(original) + ".pre-schema2-" +
            DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff'Z'", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N") + ".db");
        using (FileStream reserved = new(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using SqliteConnection backup = new(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5
        }.ToString());
        backup.Open();
        source.BackupDatabase(backup);
        using SqliteCommand check = backup.CreateCommand();
        check.CommandText = "PRAGMA quick_check;";
        if (!string.Equals(check.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("마이그레이션 전 데이터베이스 백업 검증에 실패했습니다.");
    }

    private static void AddProviderParameters(SqliteCommand command, ProviderStatus status,
        ScheduleSnapshot schedule, Recommendation recommendation)
    {
        command.Parameters.AddWithValue("$provider", status.Provider.ToString());
        command.Parameters.AddWithValue("$checked", Utc(status.CheckedAtUtc));
        command.Parameters.AddWithValue("$official", status.Status.ToString());
        command.Parameters.AddWithValue("$recommendation", recommendation.ToString());
        command.Parameters.AddWithValue("$component", status.RelevantComponent);
        command.Parameters.AddWithValue("$incident", status.IncidentId);
        command.Parameters.AddWithValue("$title", status.IncidentTitle);
        command.Parameters.AddWithValue("$schedule", schedule.State.ToString());
        command.Parameters.AddWithValue("$success", status.LastSuccessfulCheckUtc.HasValue ? Utc(status.LastSuccessfulCheckUtc.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$source", status.Source);
        command.Parameters.AddWithValue("$reason", status.Reason);
        command.Parameters.AddWithValue("$known", (object?)status.LastKnownStatus?.ToString() ?? DBNull.Value);
    }

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    // Values are stored with ToString(); numeric text such as "7" is not a known name.
    private static T ParseName<T>(string value) where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value) ? Enum.Parse<T>(value)
            : throw new FormatException($"알 수 없는 {typeof(T).Name} 값: {value}");

    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
