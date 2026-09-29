namespace AiBurgerClock;

// Explicit read-only diagnostic. No UI, database writes, model requests, or raw CLI output.
internal static class LiveQuotaProbe
{
    internal static async Task<int> RunAsync()
    {
        var client = new AccountQuotaClient();
        int failures = 0;
        foreach (var provider in Enum.GetValues<QuotaProvider>())
        {
            try
            {
                var reading = await client.ReadAsync(provider, CancellationToken.None);
                Console.WriteLine($"PASS {QuotaNames.For(provider)}: {reading.Windows.Count} quota windows");
                foreach (var window in reading.Windows)
                    Console.WriteLine($"  {window.Label}: used={window.UsedPercent:0.##}% remaining={window.RemainingPercent:0.##}% resetUtc={window.ResetsAtUtc:O}");
            }
            catch (Exception error)
            {
                failures++;
                Console.WriteLine($"UNAVAILABLE {QuotaNames.For(provider)}: {error.GetType().Name}");
            }
        }
        Console.WriteLine("Only installed official CLI quota commands were requested. No reset credits were used.");
        return failures == 0 ? 0 : 1;
    }
}
