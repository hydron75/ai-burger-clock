using System.Text.Json;

namespace AiBurgerClock;

internal sealed partial class AccountQuotaClient
{
    // The only client requests are initialize, cached headless authentication and billing.
    // No session/new, session/prompt, API-key fallback, login or model call is available here.
    internal static async Task<QuotaReading> ReadGrokProtocolAsync(TextReader output, TextWriter input,
        CancellationToken token)
    {
        const string initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{"fs":{"readTextFile":false,"writeTextFile":false},"terminal":false}}}""";
        // Official example: https://docs.x.ai/build/cli/headless-scripting
        const string authenticate = """{"jsonrpc":"2.0","id":2,"method":"authenticate","params":{"methodId":"cached_token","_meta":{"headless":true}}}""";
        const string billing = """{"jsonrpc":"2.0","id":3,"method":"_x.ai/billing","params":{}}""";
        await SendLineAsync(input, initialize, token).ConfigureAwait(false);
        int expected = 1;
        await foreach (string line in ReadLinesAsync(output, token).ConfigureAwait(false))
        {
            using var document = ParseLine(line);
            JsonElement root = document.RootElement;
            if (String(root, "jsonrpc") != "2.0") throw GrokInvalid();
            if (root.TryGetProperty("method", out _))
            {
                // Notifications may contain credentials: ignore their payload, never log it.
                // A server request would need an unsupported tool/permission capability.
                if (root.TryGetProperty("id", out _)) throw GrokInvalid();
                continue;
            }
            if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt32(out int number) || number != expected)
                throw GrokInvalid();
            if (root.TryGetProperty("error", out _) || !root.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Object) throw GrokInvalid();
            if (expected == 1)
            {
                if (!result.TryGetProperty("protocolVersion", out var version)
                    || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int protocol) || protocol != 1)
                    throw GrokInvalid();
                await SendLineAsync(input, authenticate, token).ConfigureAwait(false);
            }
            else if (expected == 2)
                await SendLineAsync(input, billing, token).ConfigureAwait(false);
            else return AccountQuotaParsers.ParseGrok(result.GetRawText());
            expected++;
        }
        throw GrokInvalid();
    }

    private static InvalidDataException GrokInvalid() =>
        new("Grok CLI 한도 조회를 완료하지 못했습니다. 공식 CLI의 로그인 상태와 지원 여부를 확인하세요.");
}
