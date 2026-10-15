using Microsoft.Data.Sqlite;

namespace AiBurgerClock;

internal static class QuotaRecoverySettingsTests
{
    internal static async Task<int> RunAsync(string directory)
    {
        string path = Path.Combine(directory, "quota-recovery-settings-" + Guid.NewGuid().ToString("N") + ".db");
        var store = new UsageStore(path);
        int assertions = 0;
        void Check(bool value, string description)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Quota recovery settings test: " + description);
        }

        Check(!await store.GetQuotaRecoveryNotificationsEnabledAsync(), "new and existing metadata default to notifications OFF");
        Check(Scalar(path, "PRAGMA user_version;") == UsageStore.SchemaVersion,
            "recovery preference uses the existing SQLite schema");
        Check(Scalar(path, "SELECT COUNT(*) FROM AppMetadata WHERE Key=$key;") == 0,
            "reading the missing setting does not write a value");
        await store.SetHolidayAdjustmentAsync(false);
        await store.SetQuotaRecoveryNotificationsEnabledAsync(true);
        Check(await new UsageStore(path).GetQuotaRecoveryNotificationsEnabledAsync(), "explicit ON survives a process-style reopen");
        Check(!await store.GetHolidayAdjustmentAsync(), "the new setting does not change the independent holiday setting");
        await store.SetQuotaRecoveryNotificationsEnabledAsync(false);
        Check(!await new UsageStore(path).GetQuotaRecoveryNotificationsEnabledAsync(), "explicit OFF survives reopen");
        await store.SetQuotaRecoveryNotificationsEnabledAsync(true);
        Check(Scalar(path, "SELECT COUNT(*) FROM AppMetadata WHERE Key=$key;") == 1,
            "repeated toggles update one bounded metadata row, not an event history");

        using (CancellationTokenSource cancellation = new())
        {
            cancellation.Cancel();
            bool canceledRead = false;
            try { _ = await store.GetQuotaRecoveryNotificationsEnabledAsync(cancellation.Token); }
            catch (OperationCanceledException) { canceledRead = true; }
            Check(canceledRead, "a canceled preference read preserves cancellation");
            bool canceledWrite = false;
            try { await store.SetQuotaRecoveryNotificationsEnabledAsync(false, cancellation.Token); }
            catch (OperationCanceledException) { canceledWrite = true; }
            Check(canceledWrite && await store.GetQuotaRecoveryNotificationsEnabledAsync(),
                "a canceled preference write cannot change the saved value");
        }

        foreach (string invalid in new[] { "true", "2", "", "synthetic-invalid-value" })
        {
            SetRaw(path, invalid);
            string? error = null;
            try { _ = await new UsageStore(path).GetQuotaRecoveryNotificationsEnabledAsync(); }
            catch (InvalidDataException exception) { error = exception.Message; }
            Check(error == "한도 회복 알림 설정을 읽을 수 없습니다.",
                "invalid persisted values are reported generically without exposing raw stored data");
        }
        await store.SetQuotaRecoveryNotificationsEnabledAsync(false);
        Check(!await store.GetQuotaRecoveryNotificationsEnabledAsync(), "an explicit user choice repairs an invalid preference");
        Check(Scalar(path, "SELECT COUNT(*) FROM UsageEvents;") == 0
            && Scalar(path, "SELECT COUNT(*) FROM ProviderStatusHistory;") == 0,
            "preference reads and writes do not create measurements or service status history");
        Check(Scalar(path, "PRAGMA user_version;") == 2,
            "the notification setting never increments or migrates schema 2");

        // A directory is a deterministic unusable DB path, without changing
        // permissions or touching the actual app data on either host.
        string blockedPath = Path.Combine(directory, "quota-recovery-blocked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(blockedPath);
        var blocked = new UsageStore(blockedPath);
        bool readFailure = false;
        try { _ = await blocked.GetQuotaRecoveryNotificationsEnabledAsync(); }
        catch (SqliteException) { readFailure = true; }
        Check(readFailure, "database read errors are reported instead of silently enabling notifications");
        bool writeFailure = false;
        try { await blocked.SetQuotaRecoveryNotificationsEnabledAsync(true); }
        catch (SqliteException) { writeFailure = true; }
        Check(writeFailure, "database write errors propagate so the host can retain the prior UI selection");
        return assertions;
    }

    private static long Scalar(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$key", QuotaRecoveryText.SettingKey);
        return (long)command.ExecuteScalar()!;
    }

    private static void SetRaw(string path, string value)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppMetadata(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
        command.Parameters.AddWithValue("$key", QuotaRecoveryText.SettingKey);
        command.Parameters.AddWithValue("$value", value);
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
