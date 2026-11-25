namespace AiBurgerClock;

// Windows native paths and an inert launch failure fixture. No installed CLI starts.
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
        catch (QuotaReadException error) { shellRejected = error.Failure == new QuotaFailure(QuotaFailureKind.NotInstalled, "cli.not-executable"); }
        Check(shellRejected, "shell wrappers rejected before launch");
        bool relativeRejected = false;
        try { await new AccountQuotaClient(claudeExecutable: "claude.exe").ReadAsync(QuotaProvider.Claude, CancellationToken.None); }
        catch (QuotaReadException error) { relativeRejected = error.Failure == new QuotaFailure(QuotaFailureKind.NotInstalled, "cli.not-executable"); }
        Check(relativeRejected, "relative executable cannot resolve against working directory");
        foreach (string path in new[] { @"C:\invalid\grok.exe", "grok.exe" })
        {
            bool rejected = false;
            try { await new AccountQuotaClient(grokExecutable: path).ReadAsync(QuotaProvider.Xai, CancellationToken.None); }
            catch (QuotaReadException error) { rejected = error.Failure == new QuotaFailure(QuotaFailureKind.NotInstalled, "cli.not-executable"); }
            Check(rejected, "missing/relative Grok fails before launch, never a zero quota");
        }
        var fixture = Directory.CreateTempSubdirectory("AiBurgerClock-cli-start-");
        string invalid = Path.Combine(fixture.FullName, "invalid.exe");
        try
        {
            File.WriteAllText(invalid, "Synthetic invalid executable");
            bool classified = false;
            try { await new AccountQuotaClient(claudeExecutable: invalid, workingDirectory: fixture.FullName).ReadAsync(QuotaProvider.Claude, default); }
            catch (QuotaReadException error)
            {
                classified = error.Failure == new QuotaFailure(QuotaFailureKind.Unreachable, "cli.start-failed") &&
                    error.Message == "cli.start-failed" && error.InnerException is null;
            }
            Check(classified, "a local start failure keeps no native error or path");
        }
        finally
        {
            File.Delete(invalid);
            fixture.Delete();
        }
        return count;
    }
}
