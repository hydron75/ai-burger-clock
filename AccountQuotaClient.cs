using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

/// <summary>Uses the installed official clients' read-only commands, never their credential files.</summary>
internal sealed class AccountQuotaClient(
    string? codexExecutable = null,
    string? claudeExecutable = null,
    string? workingDirectory = null,
    TimeSpan? timeout = null,
    string? geminiExecutable = null) : IAccountQuotaClient
{
    internal const int MaximumOutputCharacters = 2 * 1024 * 1024;
    internal const int MaximumLineCharacters = 512 * 1024;
    private readonly TimeSpan commandTimeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<QuotaReading> ReadAsync(QuotaProvider provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string executable = provider switch
        {
            QuotaProvider.Codex => codexExecutable ?? FindExecutable(provider),
            QuotaProvider.Claude => claudeExecutable ?? FindExecutable(provider),
            QuotaProvider.Gemini => geminiExecutable ?? FindExecutable(provider),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        // Absolute paths and ArgumentList avoid shell interpretation and CWD lookup.
        // Keep Windows' .exe requirement; Unix CLI executables normally have no extension.
        if (!CanExecute(executable))
            throw new FileNotFoundException("공식 CLI 실행 파일을 찾을 수 없습니다. CLI 설치와 로그인을 확인하세요.");

        string directory = workingDirectory ?? Path.Combine(AppPaths.DataDirectory, "quota-cli");
        Directory.CreateDirectory(directory);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(commandTimeout);
        // An older/incompatible CLI may treat an unknown slash command as model input.
        // Enable only stable 1.x releases from 1.3.1; never auto-upgrade the CLI.
        if (provider == QuotaProvider.Gemini)
            await VerifyGeminiVersionAsync(executable, directory, deadline.Token, cancellationToken).ConfigureAwait(false);
        using var process = new Process { StartInfo = CreateStartInfo(provider, executable, directory) };
        Task? stderr = null;
        Task<QuotaReading>? stdout = null;
        bool started = false;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("공식 CLI를 시작하지 못했습니다.");
            started = true;
            stderr = DrainErrorAsync(process.StandardError, deadline.Token, provider == QuotaProvider.Gemini);
            if (provider == QuotaProvider.Claude)
            {
                process.StandardInput.Close();
                stdout = ReadClaudeProtocolAsync(process.StandardOutput, deadline.Token);
            }
            else if (provider == QuotaProvider.Gemini)
            {
                process.StandardInput.Close();
                stdout = ReadGeminiProtocolAsync(process.StandardOutput, deadline.Token);
            }
            else
            {
                stdout = ReadCodexProtocolAsync(process.StandardOutput, process.StandardInput, deadline.Token);
            }

            // Drain both pipes at once; a stderr limit/failure must not leave stdout waiting forever.
            Task first = await Task.WhenAny(stdout, stderr).ConfigureAwait(false);
            if (first == stderr) await stderr.ConfigureAwait(false);
            QuotaReading reading = await stdout.ConfigureAwait(false);
            if (stderr.IsFaulted) await stderr.ConfigureAwait(false);
            if (provider != QuotaProvider.Codex)
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidDataException("공식 CLI 한도 조회가 실패했습니다.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return reading;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("한도 조회 시간이 초과되었습니다. 다음 조회 또는 Refresh에서 다시 확인합니다.");
        }
        catch (Win32Exception)
        {
            // Native exceptions can include user paths. Do not send them to UI/logs.
            throw new InvalidOperationException("공식 CLI를 실행할 수 없습니다. 설치 상태를 확인하세요.");
        }
        catch (InvalidDataException) { throw; }
        catch (IOException)
        {
            throw new InvalidOperationException("공식 CLI 응답을 읽지 못했습니다.");
        }
        finally
        {
            deadline.Cancel();
            if (started)
            {
                try { process.StandardInput.Close(); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                // app-server stays alive after its reply. Own only this invocation and its children.
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(stopDeadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
            }
            await ObserveStoppedAsync(stdout).ConfigureAwait(false);
            await ObserveStoppedAsync(stderr).ConfigureAwait(false);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(QuotaProvider provider, string executable, string directory)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = directory
        };
        if (provider == QuotaProvider.Codex)
        {
            start.ArgumentList.Add("app-server");
            start.ArgumentList.Add("--listen");
            start.ArgumentList.Add("stdio://");
        }
        else if (provider == QuotaProvider.Claude)
        {
            foreach (string argument in new[]
            {
                "--print", "--output-format", "stream-json", "--verbose",
                "--setting-sources", "", "--settings", "{\"disableAllHooks\":true}",
                "--no-session-persistence", "--no-chrome", "--strict-mcp-config",
                "--mcp-config", "{\"mcpServers\":{}}", "--tools", "", "--max-turns", "0",
                "--permission-prompts", "none", "--prompt-suggestions", "false", "--", "/usage"
            }) start.ArgumentList.Add(argument);
            start.Environment["DISABLE_AUTOUPDATER"] = "1";
            // Do not set CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: on CLI 2.1.284
            // the otherwise-identical /usage returned limits:null with it, and three
            // valid quota rows without it (2026-09-30 guarded A/B). Respect any
            // existing user environment instead of changing its persistent settings.
        }
        else if (provider == QuotaProvider.Gemini)
        {
            foreach (string argument in new[] { "-p", "/usage", "--output-format", "json", "--print-timeout", "20s" })
                start.ArgumentList.Add(argument);
            start.Environment["AGY_CLI_DISABLE_AUTO_UPDATE"] = "true";
            // 1.3.1 has no official per-invocation user hooks/MCP exclusion switch.
            // The user accepted that limitation (2026-10-08). No bypass, remote
            // control, resume, login, or slash-command-disabling flag is used.
        }
        else throw new ArgumentOutOfRangeException(nameof(provider));
        if (OperatingSystem.IsMacOS())
        {
            // npm-installed official commands may use /usr/bin/env node. Supply known
            // absolute CLI directories without sourcing shell profiles or reading auth files.
            var directories = MacCliPaths.Candidates(provider,
                (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.CurrentDirectory)
                .Select(path => Path.GetDirectoryName(path)!).Concat(["/usr/bin", "/bin", "/usr/sbin", "/sbin"]);
            start.Environment["PATH"] = string.Join(Path.PathSeparator, directories.Distinct(StringComparer.Ordinal));
        }
        return start;
    }

    internal static bool IsSupportedGeminiVersion(string text)
    {
        string versionText = text.Trim();
        string[] parts = versionText.Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0 ||
            (part.Length > 1 && part[0] == '0') || part.Any(c => c < '0' || c > '9')))
            return false;
        return Version.TryParse(versionText, out var version) && version.Major == 1 &&
            (version.Minor > 3 || (version.Minor == 3 && version.Build >= 1));
    }

    private static async Task VerifyGeminiVersionAsync(string executable, string directory,
        CancellationToken token, CancellationToken callerToken)
    {
        using var versionLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var start = CreateStartInfo(QuotaProvider.Gemini, executable, directory);
        start.ArgumentList.Clear();
        start.ArgumentList.Add("--version");
        using var process = new Process { StartInfo = start };
        Task<string>? output = null;
        Task? error = null;
        bool started = false;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("agy CLI를 시작하지 못했습니다.");
            started = true;
            process.StandardInput.Close();
            output = ReadGeminiOutputAsync(process.StandardOutput, versionLifetime.Token);
            error = DrainErrorAsync(process.StandardError, versionLifetime.Token, stopOnAuthentication: true);
            Task first = await Task.WhenAny(output, error).ConfigureAwait(false);
            if (first == error) await error.ConfigureAwait(false);
            string version = await output.ConfigureAwait(false);
            await process.WaitForExitAsync(versionLifetime.Token).ConfigureAwait(false);
            await error.ConfigureAwait(false);
            if (process.ExitCode != 0 || !IsSupportedGeminiVersion(version))
                throw new NotSupportedException("agy CLI 1.3.1 이상, 2.0 미만의 안정 버전만 한도 조회에 사용합니다.");
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new TimeoutException("agy CLI 버전 확인 시간이 초과되었습니다.");
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("agy CLI를 실행할 수 없습니다.");
        }
        finally
        {
            versionLifetime.Cancel();
            if (started)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
            }
            await ObserveStoppedAsync(output).ConfigureAwait(false);
            await ObserveStoppedAsync(error).ConfigureAwait(false);
        }
    }

    private static bool CanExecute(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) return false;
        if (!OperatingSystem.IsMacOS())
            return string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase);
        try
        {
            return (File.GetUnixFileMode(executable) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static async Task<QuotaReading> ReadClaudeProtocolAsync(TextReader output, CancellationToken token)
    {
        string? report = null;
        bool localOnlySuccess = false;
        await foreach (string line in ReadLinesAsync(output, token).ConfigureAwait(false))
        {
            using var document = ParseLine(line);
            JsonElement root = document.RootElement;
            string? type = String(root, "type");
            if (type == "assistant" && root.TryGetProperty("usage_report", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
                report = line;
            if (type == "result")
            {
                if (localOnlySuccess) throw new InvalidDataException("Claude CLI가 중복된 결과를 반환했습니다.");
                localOnlySuccess = String(root, "local_command") == "usage" &&
                    String(root, "subtype") == "success" &&
                    root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.False &&
                    IsZero(root, "num_turns") && IsZero(root, "total_cost_usd") &&
                    root.TryGetProperty("usage", out var tokens) && tokens.ValueKind == JsonValueKind.Object &&
                    IsZero(tokens, "input_tokens") && IsZero(tokens, "output_tokens") &&
                    OptionalZero(tokens, "cache_read_input_tokens") && OptionalZero(tokens, "cache_creation_input_tokens") &&
                    (!root.TryGetProperty("modelUsage", out var models) ||
                        models.ValueKind == JsonValueKind.Object && !models.EnumerateObject().Any());
                if (!localOnlySuccess)
                    throw new InvalidDataException("Claude의 모델 호출 없는 /usage 결과를 확인하지 못했습니다.");
            }
        }
        if (!localOnlySuccess || report is null)
            throw new InvalidDataException("Claude CLI에서 구조화된 구독 한도를 받지 못했습니다.");
        return AccountQuotaParsers.ParseClaude(report);
    }

    internal static async Task<QuotaReading> ReadCodexProtocolAsync(TextReader output, TextWriter input, CancellationToken token)
    {
        await SendLineAsync(input,
            "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"ai_burger_clock\",\"title\":\"AI Burger Clock\",\"version\":\"" +
            (typeof(AccountQuotaClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0") + "\"}}}", token).ConfigureAwait(false);
        bool initialized = false;
        await foreach (string line in ReadLinesAsync(output, token).ConfigureAwait(false))
        {
            using var document = ParseLine(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number ||
                !id.TryGetInt32(out int number)) continue;
            if (number != (initialized ? 2 : 1)) continue;
            if (root.TryGetProperty("error", out _) || !root.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Codex가 한도 조회를 완료하지 못했습니다. 공식 CLI의 로그인 상태를 확인하세요.");
            if (!initialized)
            {
                initialized = true;
                await SendLineAsync(input, "{\"method\":\"initialized\"}", token).ConfigureAwait(false);
                await SendLineAsync(input, "{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}", token).ConfigureAwait(false);
            }
            else return AccountQuotaParsers.ParseCodex(result.GetRawText());
        }
        throw new InvalidDataException("Codex CLI에서 구독 한도 응답을 받지 못했습니다.");
    }

    internal static async Task<QuotaReading> ReadGeminiProtocolAsync(TextReader output, CancellationToken token)
    {
        string json = await ReadGeminiOutputAsync(output, token).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            string[] counters = ["input_tokens", "output_tokens", "thinking_tokens", "cache_read_tokens", "total_tokens"];
            if (root.ValueKind != JsonValueKind.Object || String(root, "status") != "SUCCESS" ||
                String(root, "conversation_id") != "" || !IsZero(root, "num_turns") ||
                !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object ||
                !counters.All(name => IsZero(usage, name)) || !usage.EnumerateObject().All(field => IsZero(usage, field.Name)) ||
                !OptionalZero(root, "total_cost_usd") ||
                !root.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.Object ||
                String(command, "name") != "usage" || !command.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("agy의 모델 호출 없는 /usage 결과를 확인하지 못했습니다.");
            return AccountQuotaParsers.ParseGemini(data.GetRawText());
        }
        catch (JsonException)
        {
            throw new InvalidDataException("agy CLI에서 구조화된 한도 응답을 받지 못했습니다.");
        }
    }

    // Accept a single JSON document, including pretty-printed output. Never use
    // the human-readable TSV/prose field or report an OAuth URL/raw diagnostics.
    private static async Task<string> ReadGeminiOutputAsync(TextReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        var output = new StringBuilder();
        string tail = "";
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > MaximumOutputCharacters)
                throw new InvalidDataException("공식 CLI 응답이 크기 제한을 초과했습니다.");
            tail = CheckGeminiAuthentication(tail, buffer, read);
            output.Append(buffer, 0, read);
        }
        return output.ToString();
    }

    private static string CheckGeminiAuthentication(string tail, char[] buffer, int count)
    {
        string text = tail + new string(buffer, 0, count);
        if (new[] { "authentication required", "waiting for authentication", "could not authenticate", "sign in to continue", "accounts.google.com/o/oauth2" }
            .Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("agy CLI 로그인은 공식 CLI에서 먼저 완료하세요.");
        return text.Length <= 128 ? text : text[^128..];
    }

    private static async Task SendLineAsync(TextWriter writer, string line, CancellationToken token)
    {
        await writer.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
        await writer.FlushAsync(token).ConfigureAwait(false);
    }

    private static JsonDocument ParseLine(string line)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException) { }
        throw new InvalidDataException("공식 CLI 응답 형식을 확인하지 못했습니다.");
    }

    private static string? String(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static bool IsZero(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number &&
        item.TryGetDouble(out double number) && number == 0;

    private static bool OptionalZero(JsonElement value, string name) => !value.TryGetProperty(name, out _) || IsZero(value, name);

    // Chunk reads cap memory even when a damaged or incompatible CLI emits a huge unterminated line.
    private static async IAsyncEnumerable<string> ReadLinesAsync(TextReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();
        int total = 0;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaximumOutputCharacters) throw new InvalidDataException("공식 CLI 응답이 크기 제한을 초과했습니다.");
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == '\n')
                {
                    string line = pending.ToString().Trim();
                    pending.Clear();
                    if (line.Length > 0) yield return line;
                }
                else
                {
                    if (pending.Length >= MaximumLineCharacters)
                        throw new InvalidDataException("공식 CLI 응답 행이 크기 제한을 초과했습니다.");
                    pending.Append(buffer[i]);
                }
            }
        }
        string remaining = pending.ToString().Trim();
        if (remaining.Length > 0) yield return remaining;
    }

    internal static async Task DrainErrorAsync(TextReader reader, CancellationToken token, bool stopOnAuthentication = false)
    {
        var buffer = new char[4096];
        int total = 0;
        string tail = "";
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > MaximumOutputCharacters) throw new InvalidDataException("공식 CLI 진단 출력이 크기 제한을 초과했습니다.");
            if (stopOnAuthentication) tail = CheckGeminiAuthentication(tail, buffer, read);
        }
        // Intentionally do not retain or log stderr: it can contain private paths or account details.
    }

    private static async Task ObserveStoppedAsync(Task? task)
    {
        if (task is null) return;
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception) { } // Cleanup only; the primary failure is reported by ReadAsync.
    }

    internal static string FindExecutable(QuotaProvider provider)
    {
        if (OperatingSystem.IsMacOS())
        {
            var paths = MacCliPaths.Candidates(provider,
                (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.CurrentDirectory);
            foreach (string path in paths)
                if (CanExecute(path)) return path;
            throw new FileNotFoundException(CliName(provider) +
                " 공식 CLI가 필요합니다. 설치 및 본인 계정 로그인을 먼저 완료하세요.");
        }
        string name = CliName(provider).ToLowerInvariant() + ".exe";
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string>();
        string current = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar);
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string path = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(path)) continue;
            try
            {
                if (string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), current, StringComparison.OrdinalIgnoreCase)) continue;
                candidates.Add(Path.Combine(path, name));
                if (provider == QuotaProvider.Claude)
                    candidates.Add(Path.Combine(path, "node_modules", "@anthropic-ai", "claude-code", "bin", name));
                else if (provider == QuotaProvider.Codex)
                {
                    candidates.Add(Path.Combine(path, "node_modules", "@openai", "codex", "vendor", "x86_64-pc-windows-msvc", "codex", name));
                    candidates.Add(Path.Combine(path, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "codex", name));
                }
            }
            catch (ArgumentException) { }
        }
        if (provider == QuotaProvider.Claude)
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", name));
        }
        if (provider == QuotaProvider.Codex)
        {
            string root = Path.Combine(local, "OpenAI", "Codex", "bin");
            try
            {
                if (Directory.Exists(root))
                    foreach (string folder in Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc))
                        candidates.Add(Path.Combine(folder, name));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (provider == QuotaProvider.Gemini)
            candidates.Add(Path.Combine(local, "agy", "bin", name));
        foreach (string candidate in candidates)
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        throw new FileNotFoundException(CliName(provider) +
            " 공식 CLI가 필요합니다. 설치 및 본인 계정 로그인을 먼저 완료하세요.");
    }

    private static string CliName(QuotaProvider provider) => provider switch
    {
        QuotaProvider.Codex => "Codex",
        QuotaProvider.Claude => "Claude",
        QuotaProvider.Gemini => "agy",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}
