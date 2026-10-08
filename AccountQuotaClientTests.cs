namespace AiBurgerClock;

// Windows native path and rejected executable fixtures only: no CLI starts.
internal static class AccountQuotaClientTests
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota CLI Windows path test: " + message);
        }

        var start = AccountQuotaClient.CreateStartInfo(QuotaProvider.Claude, @"C:\native path\claude.exe", @"C:\neutral");
        Check(start.FileName == @"C:\native path\claude.exe" && start.WorkingDirectory == @"C:\neutral", "native path and neutral cwd");
        bool shellRejected = false;
        try { await new AccountQuotaClient(codexExecutable: @"C:\invalid\codex.cmd").ReadAsync(QuotaProvider.Codex, CancellationToken.None); }
        catch (FileNotFoundException) { shellRejected = true; }
        Check(shellRejected, "shell wrappers rejected before launch");
        bool relativeRejected = false;
        try { await new AccountQuotaClient(claudeExecutable: "claude.exe").ReadAsync(QuotaProvider.Claude, CancellationToken.None); }
        catch (FileNotFoundException) { relativeRejected = true; }
        Check(relativeRejected, "relative executable cannot resolve against working directory");
        return count;
    }
}
