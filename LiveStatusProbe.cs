namespace AiBurgerClock;

internal static class LiveStatusProbe
{
    // Explicit diagnostic: reads official feeds, writes no user events or database state.
    public static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseCookies = false })
        { Timeout = StatusMonitor.RequestTimeout };
        var client = new ProviderStatusClient(http);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        int failures = 0;
        await Task.WhenAll(Enum.GetValues<ProviderKind>().Select(async provider =>
        {
            try
            {
                var status = await client.FetchAsync(provider, stop.Token);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(status));
                // Exit code reports receipt failure, not a claim that every service is GO.
                if (status.LastSuccessfulCheckUtc is null) Interlocked.Increment(ref failures);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(provider + ": " + error.Message);
                Interlocked.Increment(ref failures);
            }
        }));
        return failures == 0 ? 0 : 1;
    }
}
