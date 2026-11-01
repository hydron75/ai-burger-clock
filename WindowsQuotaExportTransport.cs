using System.Net.Http.Headers;

namespace AiBurgerClock;

internal enum WindowsQuotaExportFailure { None, Settings, Credential, Payload, Address, Rejected, Redirect, Server, Network, Timeout }
internal sealed record WindowsQuotaExportResult(bool Succeeded, bool Retry, WindowsQuotaExportFailure Failure);
internal interface IWindowsQuotaExportTransport : IDisposable
{
    Task<WindowsQuotaExportResult> SendAsync(Uri endpoint, byte[] body, CancellationToken cancellationToken);
}

internal sealed class WindowsQuotaExportTransport : IWindowsQuotaExportTransport
{
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly HttpClient http;
    private readonly IWindowsQuotaExportCredentials credentials;
    private readonly TimeSpan timeout;

    internal static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
        CheckCertificateRevocationList = true
        // Keep Windows/default certificate and TLS validation. No validation callback.
    };

    internal WindowsQuotaExportTransport(IWindowsQuotaExportCredentials credentials,
        HttpMessageHandler? handler = null, TimeSpan? requestTimeout = null)
    {
        this.credentials = credentials;
        timeout = requestTimeout ?? RequestTimeout;
        http = new HttpClient(handler ?? CreateHandler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public async Task<WindowsQuotaExportResult> SendAsync(Uri endpoint, byte[] body, CancellationToken cancellationToken)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.UserInfo.Length != 0)
            return new(false, false, WindowsQuotaExportFailure.Address);
        if (body.Length > WindowsQuotaExportSnapshot.MaximumBytes)
            return new(false, false, WindowsQuotaExportFailure.Payload);
        string token;
        try { token = credentials.Read(); WindowsQuotaExportCredentials.Validate(token); }
        catch { return new(false, true, WindowsQuotaExportFailure.Credential); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Put, endpoint);
        request.Headers.Add("OAI-Sites-Authorization", "Bearer " + token);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            int status = (int)response.StatusCode; // Response/error bodies and reason phrases are never read/logged.
            return status switch
            {
                >= 200 and < 300 => new(true, false, WindowsQuotaExportFailure.None),
                >= 500 and < 600 => new(false, true, WindowsQuotaExportFailure.Server),
                >= 300 and < 400 => new(false, false, WindowsQuotaExportFailure.Redirect),
                _ => new(false, false, WindowsQuotaExportFailure.Rejected)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, true, WindowsQuotaExportFailure.Timeout); }
        catch (OperationCanceledException) { throw; }
        catch { return new(false, true, WindowsQuotaExportFailure.Network); }
    }

    public void Dispose() => http.Dispose();
}
