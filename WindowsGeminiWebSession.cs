using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

namespace AiBurgerClock;

internal sealed record WindowsGeminiPageDom(string? Url, string? Text);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(WindowsGeminiPageDom))]
[JsonSerializable(typeof(WindowsAntigravityDom))]
[JsonSerializable(typeof(WindowsGeminiWebSettings))]
internal partial class WindowsGeminiWebJson : JsonSerializerContext;

internal sealed partial class WindowsGeminiWebSession(Control dispatcher, string dataDirectory, Action<string> reportLoginIssue)
    : IWindowsGeminiWebSession, IAntigravityUsagePageReader
{
    internal const string ProfileName = "GeminiWeb";
    private readonly Control viewDispatcher = dispatcher;
    private static readonly TimeSpan ViewTimeout = TimeSpan.FromSeconds(30);
    private readonly string profilePath = Path.GetFullPath(Path.Combine(dataDirectory, ProfileName));
    private readonly SemaphoreSlim profileUse = new(1, 1);
    private readonly Dictionary<uint, TaskCompletionSource> browserExits = new();
    private Task<CoreWebView2Environment>? environment;
    private bool environmentNeedsReplacement;
    private Form? loginWindow;

    public async Task<GeminiUsagePageLoad> LoadAsync(string url, string script, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ViewTimeout);
        var token = deadline.Token;
        try
        {
            var read = viewDispatcher.InvokeAsync<GeminiUsagePageLoad>(async _ => await ReadOnUiAsync(url, script, token), token);
            return await WaitWithinAsync(read, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return GeminiUsagePageLoad.LoadFailed; }
    }

    private async Task<GeminiUsagePageLoad> ReadOnUiAsync(string url, string script, CancellationToken token)
    {
        await profileUse.WaitAsync(token);
        try
        {
            using var host = new HiddenHost();
            var controller = await CreateControllerAsync(host, token);
            try
            {
                var core = controller.CoreWebView2;
                core.Settings.AreDefaultScriptDialogsEnabled = false;
                core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
                core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
                var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                bool navigationFailed = false;
                ulong? navigationId = null;
                core.NavigationStarting += (_, args) =>
                {
                    if (navigationId is not null || IsUsagePage(args.Uri)) navigationId = args.NavigationId;
                };
                core.NavigationCompleted += (_, args) =>
                {
                    if (args.NavigationId != navigationId || (!args.IsSuccess && args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)) return;
                    navigationFailed = !args.IsSuccess;
                    navigation.TrySetResult(args.IsSuccess);
                };
                core.Navigate(url);
                if (!await navigation.Task.WaitAsync(token)) return GeminiUsagePageLoad.LoadFailed;

                var page = await ReadDomAsync(() => core.Source, () => core.ExecuteScriptAsync(script),
                    () => DateTimeOffset.UtcNow, TimeZoneInfo.Local, token);
                return navigationFailed ? GeminiUsagePageLoad.LoadFailed : page;
            }
            finally { controller.Close(); }
        }
        finally { profileUse.Release(); }
    }

    internal static async Task<GeminiUsagePageLoad> ReadDomAsync(
        Func<string> source, Func<Task<string>> execute, Func<DateTimeOffset> now, TimeZoneInfo zone, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var domToken = deadline.Token;
        GeminiUsagePageLoad lastUsage = default;
        try
        {
            while (true)
            {
                domToken.ThrowIfCancellationRequested();
                string url = source();
                if (IsUsagePage(url))
                {
                    // An accounts.google.com detour can renew an existing login.
                    // Allow navigation there and only run the read script on /usage.
                    var dom = DecodePage(await execute().WaitAsync(domToken));
                    lastUsage = IsUsagePage(dom?.Url) && IsUsagePage(source()) ? new(dom!.Url, dom.Text) : default;
                    if (GeminiUsagePage.Parse(lastUsage, now(), zone).Outcome == GeminiUsageOutcome.Ok)
                        return lastUsage;
                }
                else lastUsage = default;
                await Task.Delay(250, domToken);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            string final = source();
            return new(final, IsUsagePage(final) && lastUsage.FinalUrl == final ? lastUsage.Text : null);
        }
    }

    public async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var host = new Form
        {
            Text = "Gemini 사용량 로그인",
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(900, 680),
            MinimumSize = new Size(440, 360),
            AutoScaleMode = AutoScaleMode.Dpi
        };
        loginWindow = host;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        void Closed(object? sender, FormClosedEventArgs args) => lifetime.Cancel();
        host.FormClosed += Closed;
        try
        {
            if (!WindowsGeminiLoginWindow.Show(host))
            {
                lifetime.Token.ThrowIfCancellationRequested();
                reportLoginIssue("Gemini 로그인 [창 표시] WindowNotVisible");
                MessageBox.Show("Gemini 로그인 창을 표시하지 못했습니다. 트레이 메뉴에서 다시 시도해 주세요.",
                    host.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var signIn = SignInOnUiAsync(host, lifetime.Token);
            return await WaitWithinAsync(signIn, lifetime.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        finally
        {
            host.FormClosed -= Closed;
            loginWindow = null;
            if (!host.IsDisposed) host.Close();
        }
    }

    private async Task<bool> SignInOnUiAsync(Form host, CancellationToken token)
    {
        await profileUse.WaitAsync(token);
        try
        {
            var controller = await CreateControllerAsync(host, token);
            void SizeController(object? sender, EventArgs args) => controller.Bounds = host.ClientRectangle;
            host.Resize += SizeController;
            try
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var core = controller.CoreWebView2;
                bool NavigateToUsage()
                {
                    try
                    {
                        core.Navigate(GeminiUsagePage.Url);
                        return true;
                    }
                    catch (Exception error) when (!token.IsCancellationRequested)
                    {
                        reportLoginIssue($"Gemini 로그인 [탐색 시작] 0x{error.HResult:X8}");
                        return false;
                    }
                }

                var navigation = new LoginNavigation(() => NavigateToUsage(), reportLoginIssue);
                core.NavigationStarting += (_, args) => navigation.Started(args.NavigationId, args.Uri, args.IsRedirected);
                core.NavigationCompleted += (_, args) =>
                {
                    if (!token.IsCancellationRequested &&
                        navigation.Completed(args.NavigationId, core.Source, args.IsSuccess, args.WebErrorStatus))
                        completion.TrySetResult(true);
                };
                return NavigateToUsage() && await completion.Task.WaitAsync(token);
            }
            finally
            {
                host.Resize -= SizeController;
                controller.Close();
            }
        }
        finally { profileUse.Release(); }
    }

    internal sealed class LoginNavigation(Action reopenUsage, Action<string> reportIssue)
    {
        private ulong? initialId;
        private ulong? currentId;
        private bool leftGemini;
        private bool reopenedUsage;

        internal void Started(ulong id, string? url, bool redirected = false)
        {
            if (!redirected)
            {
                if (initialId is null && !IsUsagePage(url)) return;
                initialId ??= id;
                currentId = id;
            }
            if (id == currentId && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                uri.Scheme is "https" or "http" && !IsGeminiOrigin(uri))
                leftGemini = true;
        }

        internal bool Completed(ulong id, string? url, bool success, CoreWebView2WebErrorStatus error)
        {
            if (id != currentId) return false;
            if (!success)
            {
                if (id == initialId && error != CoreWebView2WebErrorStatus.OperationCanceled)
                    reportIssue($"Gemini 로그인 [초기 탐색] {error}");
                return false;
            }
            if (IsUsagePage(url)) return true;
            if (leftGemini && !reopenedUsage && Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsGeminiOrigin(uri))
            {
                reopenedUsage = true;
                reopenUsage();
            }
            return false;
        }
    }

    private static async Task<T> WaitWithinAsync<T>(Task<T> task, CancellationToken token)
    {
        try { return await task.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The native task can finish after the caller leaves. Its UI
            // continuation still owns controller cleanup and the profile gate.
            _ = task.ContinueWith(static late => { _ = late.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    private async Task<CoreWebView2Controller> CreateControllerAsync(Form host, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (environmentNeedsReplacement) await ReleaseEnvironmentAsync(token);
        var pending = environment ??= CreateEnvironmentAsync();
        CoreWebView2Environment shared;
        try { shared = await pending; }
        catch { environment = null; throw; }
        token.ThrowIfCancellationRequested();

        // Native creation cannot be cancelled. Keep the profile gate until a
        // late controller is closed, even when the caller's deadline has elapsed.
        CoreWebView2Controller controller;
        try { controller = await shared.CreateCoreWebView2ControllerAsync(host.Handle); }
        catch
        {
            // A runtime update can retire the binary behind a cached environment.
            // Retain its exit handler until the old browser has released the profile.
            environmentNeedsReplacement = true;
            throw;
        }
        try
        {
            var core = controller.CoreWebView2;
            TrackBrowser(core.BrowserProcessId);
            token.ThrowIfCancellationRequested();
            controller.Bounds = host.ClientRectangle;
            core.IsMuted = true;
            core.PermissionRequested += (_, args) =>
            {
                args.State = CoreWebView2PermissionState.Deny;
                args.SavesInProfile = false;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) =>
            {
                args.Cancel = true;
                args.Handled = true;
            };
            return controller;
        }
        catch { controller.Close(); throw; }
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")) ||
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS")))
            throw new InvalidOperationException("Gemini WebView profile or browser options are overridden.");
        VerifyProfilePath();
        var shared = await CoreWebView2Environment.CreateAsync(userDataFolder: profilePath);
        if (!string.Equals(Path.GetFullPath(shared.UserDataFolder), profilePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected Gemini WebView profile.");
        VerifyProfilePath();
        shared.BrowserProcessExited += OnBrowserProcessExited;
        return shared;
    }

    private void TrackBrowser(uint id)
    {
        if (id != 0) browserExits.TryAdd(id, new(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private void OnBrowserProcessExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs args)
    {
        if (browserExits.Remove(args.BrowserProcessId, out var exited)) exited.TrySetResult();
    }

    public async Task DeleteProfileAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ViewTimeout);
        var token = deadline.Token;
        await profileUse.WaitAsync(token);
        try
        {
            await ReleaseEnvironmentAsync(token);
            await Task.Run(() => DeleteOwnedProfile(token), token);
        }
        finally { profileUse.Release(); }
    }

    private async Task ReleaseEnvironmentAsync(CancellationToken token)
    {
        if (environment is { } pending)
        {
            var shared = await pending;
            // Also cover a browser started before a controller failed to initialize.
            foreach (var process in shared.GetProcessInfos())
                if (process.Kind == CoreWebView2ProcessKind.Browser) TrackBrowser((uint)process.ProcessId);
            await Task.WhenAll(browserExits.Values.Select(exited => exited.Task)).WaitAsync(token);
            shared.BrowserProcessExited -= OnBrowserProcessExited;
            environment = null;
        }
        environmentNeedsReplacement = false;
    }

    private void VerifyProfilePath()
    {
        for (DirectoryInfo? directory = new(profilePath); directory is not null; directory = directory.Parent)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(directory.FullName); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new IOException("Invalid Gemini WebView profile directory.");
        }
    }

    private void DeleteOwnedProfile(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        VerifyProfilePath();
        try { Directory.Delete(profilePath, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private static bool IsGeminiOrigin(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Host == "gemini.google.com";

    internal static bool IsUsagePage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsGeminiOrigin(uri) && uri.AbsolutePath == "/usage";

    internal static WindowsGeminiPageDom? DecodePage(string encoded)
    {
        try
        {
            string? json = JsonSerializer.Deserialize(encoded, WindowsGeminiWebJson.Default.String);
            return json is null ? null : JsonSerializer.Deserialize(json, WindowsGeminiWebJson.Default.WindowsGeminiPageDom);
        }
        catch (JsonException) { return null; }
    }

    public void Dispose()
    {
        loginWindow?.Close();
        if (environment is { IsCompletedSuccessfully: true })
            environment.Result.BrowserProcessExited -= OnBrowserProcessExited;
    }

    private sealed class HiddenHost : Form
    {
        internal HiddenHost()
        {
            ShowInTaskbar = false;
            ClientSize = new Size(960, 720);
            FormBorderStyle = FormBorderStyle.None;
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                const int ToolWindow = 0x80;
                const int NoActivate = 0x08000000;
                var result = base.CreateParams;
                result.ExStyle |= ToolWindow | NoActivate;
                return result;
            }
        }
    }
}
