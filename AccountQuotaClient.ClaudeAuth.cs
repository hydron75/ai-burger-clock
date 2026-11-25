using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AiBurgerClock;

internal sealed partial class AccountQuotaClient : IClaudeAuthClient
{
    internal static readonly TimeSpan ClaudeAuthTimeout = TimeSpan.FromSeconds(15);

    public async Task<ClaudeAuthStatus> ReadClaudeAuthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ClaudeAuthTimeout);
        using var process = new Process();
        Task<string>? stdout = null;
        Task? stderr = null;
        bool started = false;
        try
        {
            string executable = GetExecutable(QuotaProvider.Claude);
            if (!CanExecute(executable)) return ClaudeAuthStatus.Unknown;
            string directory = WorkingDirectory;
            Directory.CreateDirectory(directory);
            process.StartInfo = CreateClaudeAuthStartInfo(executable, directory);
            deadline.Token.ThrowIfCancellationRequested();
            if (!process.Start()) return ClaudeAuthStatus.Unknown;
            started = true;
            process.StandardInput.Close();
            stdout = ReadClaudeAuthOutputAsync(process.StandardOutput, deadline.Token);
            stderr = DrainErrorAsync(process.StandardError, deadline.Token);
            Task first = await Task.WhenAny(stdout, stderr).ConfigureAwait(false);
            await first.ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return ClaudeAuthStatus.Parse(process.ExitCode, await stdout.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ClaudeAuthStatus.Unknown; }
        catch (Exception error) when (error is QuotaReadException or IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            // Process errors may include CLI paths or account details.
            return ClaudeAuthStatus.Unknown;
        }
        finally
        {
            deadline.Cancel();
            if (started) await StopCliProcessAsync(process).ConfigureAwait(false);
            await ObserveStoppedAsync(stdout).ConfigureAwait(false);
            await ObserveStoppedAsync(stderr).ConfigureAwait(false);
        }
    }

    internal static ProcessStartInfo CreateClaudeAuthStartInfo(string executable, string directory)
    {
        var start = CreateProcessStartInfo(executable, directory);
        start.ArgumentList.Add("auth");
        start.ArgumentList.Add("status");
        start.Environment["DISABLE_AUTOUPDATER"] = "1";
        PrepareMacPath(start, QuotaProvider.Claude);
        return start;
    }

    private static async Task<string> ReadClaudeAuthOutputAsync(TextReader output, CancellationToken token)
    {
        var json = new StringBuilder();
        await foreach (string line in ReadLinesAsync(output, token).ConfigureAwait(false))
            json.AppendLine(line);
        return json.ToString();
    }
}
