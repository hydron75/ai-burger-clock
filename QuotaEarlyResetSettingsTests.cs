using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

internal static class QuotaEarlyResetSettingsTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        string path = Path.Combine(directory, "quota-early-reset-settings-" + Guid.NewGuid().ToString("N") + ".db");
        var store = new UsageStore(path);
        int assertions = 0;
        void Check(bool value, string description)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Quota early reset settings test: " + description);
        }

        Check(QuotaEarlyResetText.SettingKey != QuotaRecoveryText.SettingKey,
            "early reset and recovery use independent metadata keys");
        Check(!await store.GetQuotaEarlyResetNotificationsEnabledAsync(), "a fresh database defaults early reset OFF");
        Check(!await store.GetQuotaRecoveryNotificationsEnabledAsync(), "a fresh database keeps recovery OFF independently");
        Check(CountKey(path, QuotaEarlyResetText.SettingKey) == 0,
            "reading the missing early reset setting does not persist a preference");
        string schemaBefore = ScalarText(path,
            "SELECT group_concat(name || ':' || sql, char(10)) FROM (SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name);");
        Check(ScalarLong(path, "PRAGMA user_version;") == UsageStore.SchemaVersion,
            "the preference starts with existing schema 2");

        // Simulate an existing installation with cached quotas and an independently
        // enabled recovery preference, rather than using any actual app database.
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
        {
            var reading = new QuotaReading(provider, [new("synthetic-weekly", "주간", 60, now.AddDays(2), 10080)]);
            await store.SaveQuotaAsync(new(1, reading, now, [now.AddDays(2)], AccountQuotaPolicy.ResetAnchorPolicyVersion));
        }
        string[] cacheBefore = Enum.GetValues<QuotaProvider>()
            .Select(provider => RawValue(path, "AccountQuota.v1." + provider)).ToArray();
        await store.SetQuotaRecoveryNotificationsEnabledAsync(true);
        await store.SetHolidayAdjustmentAsync(false);
        Check(!await new UsageStore(path).GetQuotaEarlyResetNotificationsEnabledAsync(),
            "an existing database with recovery ON and cached quotas still defaults early reset OFF");
        Check(CountKey(path, QuotaEarlyResetText.SettingKey) == 0,
            "reading an existing database does not backfill the new setting");

        foreach (bool recoveryEnabled in new[] { false, true })
        foreach (bool earlyResetEnabled in new[] { false, true })
        {
            await store.SetQuotaRecoveryNotificationsEnabledAsync(recoveryEnabled);
            await store.SetQuotaEarlyResetNotificationsEnabledAsync(earlyResetEnabled);
            var reopened = new UsageStore(path);
            Check(await reopened.GetQuotaEarlyResetNotificationsEnabledAsync() == earlyResetEnabled,
                "each explicit early reset selection survives a process-style reopen");
            Check(await reopened.GetQuotaRecoveryNotificationsEnabledAsync() == recoveryEnabled,
                "each combination preserves the independent recovery selection");
            Check(!await reopened.GetHolidayAdjustmentAsync(), "notification toggles preserve the independent holiday preference");
        }
        Check(CountKey(path, QuotaEarlyResetText.SettingKey) == 1,
            "repeated early reset toggles update one bounded preference row");
        Check(CountKey(path, QuotaRecoveryText.SettingKey) == 1,
            "recovery continues to occupy its own single preference row");

        using (CancellationTokenSource cancellation = new())
        {
            cancellation.Cancel();
            bool readCanceled = false;
            try { _ = await store.GetQuotaEarlyResetNotificationsEnabledAsync(cancellation.Token); }
            catch (OperationCanceledException) { readCanceled = true; }
            Check(readCanceled, "a canceled early reset read preserves cancellation");
            bool writeCanceled = false;
            try { await store.SetQuotaEarlyResetNotificationsEnabledAsync(false, cancellation.Token); }
            catch (OperationCanceledException) { writeCanceled = true; }
            Check(writeCanceled && await new UsageStore(path).GetQuotaEarlyResetNotificationsEnabledAsync(),
                "a canceled early reset write leaves the saved ON selection unchanged");
            Check(await store.GetQuotaRecoveryNotificationsEnabledAsync(),
                "a canceled early reset write cannot change the independent recovery setting");
        }

        foreach (string invalid in new[] { "true", "2", "", "synthetic-private-invalid-value" })
        {
            SetRaw(path, QuotaEarlyResetText.SettingKey, invalid);
            string? error = null;
            try { _ = await new UsageStore(path).GetQuotaEarlyResetNotificationsEnabledAsync(); }
            catch (InvalidDataException exception) { error = exception.Message; }
            Check(error == "한도 조기 초기화 알림 설정을 읽을 수 없습니다.",
                "invalid early reset preferences raise a generic error without exposing raw data");
            Check(await store.GetQuotaRecoveryNotificationsEnabledAsync(),
                "an invalid early reset preference does not poison recovery reads");
        }
        await store.SetQuotaEarlyResetNotificationsEnabledAsync(false);
        Check(!await new UsageStore(path).GetQuotaEarlyResetNotificationsEnabledAsync(),
            "an explicit OFF choice repairs an invalid early reset preference");
        SetRaw(path, QuotaRecoveryText.SettingKey, "synthetic-private-invalid-recovery");
        Check(!await store.GetQuotaEarlyResetNotificationsEnabledAsync(),
            "an invalid recovery preference does not poison early reset reads");
        await store.SetQuotaEarlyResetNotificationsEnabledAsync(true);
        Check(await new UsageStore(path).GetQuotaEarlyResetNotificationsEnabledAsync(),
            "an invalid recovery preference does not block early reset saves");
        Check(RawValue(path, QuotaRecoveryText.SettingKey) == "synthetic-private-invalid-recovery",
            "saving early reset does not silently repair or overwrite recovery metadata");
        await store.SetQuotaRecoveryNotificationsEnabledAsync(true);

        Execute(path, """
            CREATE TRIGGER reject_synthetic_early_reset_setting BEFORE INSERT ON AppMetadata
            WHEN NEW.Key = 'QuotaEarlyResetNotificationsEnabled'
            BEGIN SELECT RAISE(ABORT, 'Synthetic early reset preference failure'); END;
            """);
        bool saveFailed = false;
        try { await store.SetQuotaEarlyResetNotificationsEnabledAsync(false); }
        catch (SqliteException) { saveFailed = true; }
        Check(saveFailed, "a failed early reset save propagates so the host can retain its prior selection");
        Check(await new UsageStore(path).GetQuotaEarlyResetNotificationsEnabledAsync(),
            "a failed early reset save preserves the previous persisted ON selection");
        await store.SetQuotaRecoveryNotificationsEnabledAsync(false);
        Check(!await store.GetQuotaRecoveryNotificationsEnabledAsync(),
            "an early reset save failure does not prevent independent recovery saves");
        Execute(path, "DROP TRIGGER reject_synthetic_early_reset_setting;");
        await store.SetQuotaEarlyResetNotificationsEnabledAsync(false);
        Check(!await store.GetQuotaEarlyResetNotificationsEnabledAsync(),
            "an explicit retry after the write failure saves normally");

        string[] cacheAfter = Enum.GetValues<QuotaProvider>()
            .Select(provider => RawValue(path, "AccountQuota.v1." + provider)).ToArray();
        Check(cacheBefore.SequenceEqual(cacheAfter), "notification settings leave all existing quota cache JSON untouched");
        foreach (QuotaProvider provider in Enum.GetValues<QuotaProvider>())
        {
            QuotaCache? cache = await new UsageStore(path).ReadQuotaAsync(provider);
            Check(cache is { Version: 1 } && cache.Reading.Provider == provider && cache.Reading.Windows.Count == 1 &&
                cache.Reading.Windows[0].Id == "synthetic-weekly" && cache.SuccessfulAtUtc == now,
                "existing cached quotas remain readable after both independent settings are toggled");
        }
        Check(ScalarLong(path, "PRAGMA user_version;") == 2,
            "early reset preferences do not migrate or increment schema 2");
        Check(schemaBefore == ScalarText(path,
            "SELECT group_concat(name || ':' || sql, char(10)) FROM (SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name);"),
            "all table definitions are unchanged and there is no early reset history table");
        Check(ScalarLong(path, "SELECT COUNT(*) FROM UsageEvents;") == 0 &&
            ScalarLong(path, "SELECT COUNT(*) FROM ProviderStatusHistory;") == 0,
            "preference operations never create usage or status event history");
        Check(ScalarLong(path, "SELECT COUNT(*) FROM AppMetadata;") == 7,
            "metadata contains only schema, three caches, holiday and two preferences, not early reset history");

        // A directory is an unusable database path on both hosts without changing
        // permissions, locking the user's database or depending on a particular OS.
        string blockedPath = Path.Combine(directory, "early-reset-settings-blocked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(blockedPath);
        var blocked = new UsageStore(blockedPath);
        bool readFailed = false;
        try { _ = await blocked.GetQuotaEarlyResetNotificationsEnabledAsync(); }
        catch (SqliteException) { readFailed = true; }
        Check(readFailed, "database read failures are reported rather than silently enabling early reset notifications");
        bool writeFailed = false;
        try { await blocked.SetQuotaEarlyResetNotificationsEnabledAsync(true); }
        catch (SqliteException) { writeFailed = true; }
        Check(writeFailed, "database write failures are reported rather than claiming to save a preference");
        return assertions;
    }

    private static long CountKey(string path, string key)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AppMetadata WHERE Key=$key;";
        command.Parameters.AddWithValue("$key", key);
        return (long)command.ExecuteScalar()!;
    }

    private static string RawValue(string path, string key)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppMetadata WHERE Key=$key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string ?? "";
    }

    private static void SetRaw(string path, string key, string value)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppMetadata(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static string ScalarText(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string ?? "";
    }

    private static void Execute(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return connection;
    }
}
