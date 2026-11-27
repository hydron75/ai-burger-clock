using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace AiBurgerClock;

internal sealed record WindowsAntigravityDom(string? Url, string? Text, string? VisibilityState, string? DocumentLanguage);

internal static class WindowsAntigravitySession
{
    // Win64 WTSINFOEXW: the aligned union starts at 8, and the level-one
    // flags follow SessionId and SessionState. Never read other session data.
    private const int SessionFlagsOffset = 16;
    private const int WtsSessionInfoEx = 25;

    internal static QuotaReadiness Readiness()
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, uint.MaxValue, WtsSessionInfoEx, out var info, out int bytes))
            return QuotaReadiness.Deferred;
        try
        {
            return bytes >= SessionFlagsOffset + sizeof(int) && Marshal.ReadInt32(info) == 1 &&
                Marshal.ReadInt32(info, SessionFlagsOffset) == 1 ? QuotaReadiness.Ready : QuotaReadiness.Deferred;
        }
        finally { WTSFreeMemory(info); }
    }

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr info, out int bytes);

    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    private static extern void WTSFreeMemory(IntPtr memory);
}

internal sealed partial class WindowsGeminiWebSession
{
    public async Task<AntigravityUsagePageLoad> LoadAntigravityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var read = viewDispatcher.InvokeAsync<AntigravityUsagePageLoad>(
                async _ => await ReadAntigravityOnUiAsync(cancellationToken), cancellationToken);
            return await WaitWithinAsync(read, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return default; }
    }

    private async Task<AntigravityUsagePageLoad> ReadAntigravityOnUiAsync(CancellationToken token)
    {
        await profileUse.WaitAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ViewTimeout);
        Uri? final = null;
        try { return await WaitWithinAsync(ReadAntigravityWithProfileAsync(uri => final = uri, deadline.Token), deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(final, null, null, null, TimedOut: true); }
    }

    private async Task<AntigravityUsagePageLoad> ReadAntigravityWithProfileAsync(Action<Uri?> observeDestination, CancellationToken token)
    {
        try
        {
            using var host = new HiddenHost();
            var controller = await CreateControllerAsync(host, token);
            try
            {
                controller.IsVisible = true;
                var core = controller.CoreWebView2;
                core.Settings.AreDefaultScriptDialogsEnabled = false;
                core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
                core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
                var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                ulong? firstId = null;
                core.NavigationStarting += (_, args) =>
                {
                    if (firstId is null && AntigravityUsagePage.IsUsagePage(ParseUri(args.Uri))) firstId = args.NavigationId;
                };
                core.NavigationCompleted += (_, args) =>
                {
                    if (args.NavigationId == firstId)
                        navigation.TrySetResult(args.IsSuccess || args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled);
                };
                core.Navigate(AntigravityUsagePage.Url);
                bool loaded = await navigation.Task.WaitAsync(token);
                var final = ParseUri(core.Source);
                observeDestination(final);
                if (!loaded) return AntigravityUsagePage.IsLoginPage(final) ? new(final, null, null, null, false) : default;
                return await ReadAntigravityDomAsync(() => core.Source,
                    () => core.ExecuteScriptAsync(AntigravityUsagePage.ReadScript), token, observeDestination);
            }
            finally { controller.Close(); }
        }
        finally { profileUse.Release(); }
    }

    internal static async Task<AntigravityUsagePageLoad> ReadAntigravityDomAsync(
        Func<string> source, Func<Task<string>> execute, CancellationToken token, Action<Uri?>? observeDestination = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        AntigravityUsagePageLoad last = default;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var final = ParseUri(source());
                observeDestination?.Invoke(final);
                if (AntigravityUsagePage.IsUsagePage(final))
                {
                    var dom = DecodeAntigravity(await execute().WaitAsync(deadline.Token));
                    var scriptUri = ParseUri(dom?.Url);
                    if (scriptUri == final && ParseUri(source()) == final)
                    {
                        last = new(final, dom!.Text, dom.VisibilityState, dom.DocumentLanguage, TimedOut: false);
                        if (last is { Text: not null, VisibilityState: not "hidden" }) return last;
                    }
                }
                else last = new(final, null, null, null, TimedOut: false);
                await Task.Delay(250, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            var final = ParseUri(source());
            return final == last.FinalUri ? last : new(final, null, null, null, TimedOut: false);
        }
    }

    private static Uri? ParseUri(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;

    private static WindowsAntigravityDom? DecodeAntigravity(string encoded)
    {
        try
        {
            string? json = JsonSerializer.Deserialize(encoded, WindowsGeminiWebJson.Default.String);
            return json is null ? null : JsonSerializer.Deserialize(json, WindowsGeminiWebJson.Default.WindowsAntigravityDom);
        }
        catch (JsonException) { return null; }
    }
}
