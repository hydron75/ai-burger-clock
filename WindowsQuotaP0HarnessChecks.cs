using System.Collections.Concurrent;

namespace AiBurgerClock;

// Windows wiring for the portable fixture. Scenario assertions never see the
// monitor or bridge, only calls, snapshots, and bytes from the real serializer.
internal sealed partial class QuotaP0Harness
{
    private static readonly WindowsQuotaExportSettings ExportSettings =
        new(1, true, "https://quota.example.invalid", "00000000-0000-4000-8000-000000000001", 1);

    internal IDisposable CapturePackets(ConcurrentQueue<byte[]> packets) =>
        new WindowsQuotaExportBridge(monitor, states => packets.Enqueue(Serialize(states)));

    internal byte[] CurrentPacket() => Serialize(monitor.Snapshot());

    private byte[] Serialize(IReadOnlyList<QuotaState> states) =>
        WindowsQuotaExportSnapshot.Serialize(states, ExportSettings, Now, "2.9.8");

    internal static int WindowsDetailWidth => AccountQuotaView.DetailWidth;

    internal WindowsLogoutFixture PrepareWindowsLogout(Func<Task> deleteProfile, bool pending = false) =>
        new(monitor, store!, deleteProfile, pending);

    internal sealed class WindowsLogoutFixture
    {
        private readonly WindowsGeminiWebSettingsStore settings;
        private readonly WindowsGeminiWebAccess access;

        internal WindowsLogoutFixture(AccountQuotaMonitor monitor, UsageStore store, Func<Task> deleteProfile, bool pending)
        {
            settings = new(Path.Combine(Path.GetDirectoryName(store.DatabasePath)!, "p0-settings.json"));
            var initial = new WindowsGeminiWebSettings(Enabled: !pending, LogoutPending: pending);
            settings.Save(initial);
            access = new(monitor, store, new LogoutSession(deleteProfile), settings, initial);
        }

        internal bool Pending => settings.Load().LogoutPending;
        internal bool Enabled => settings.Load().Enabled;
        internal Task LogoutAsync() => access.LogoutAsync(default);
        internal Task RetryAsync() => access.RetryLogoutAsync(default);

        private sealed class LogoutSession(Func<Task> deleteProfile) : IWindowsGeminiWebSession
        {
            public Task DeleteProfileAsync(CancellationToken cancellationToken) => deleteProfile();
            public Task<bool> SignInAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public void Dispose() { }
        }
    }
}
