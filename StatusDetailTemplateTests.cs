using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

internal static class StatusDetailTemplateTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Status detail template: " + message);
        }
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var now = new DateTimeOffset(2032, 3, 4, 0, 0, 0, TimeSpan.Zero);
            foreach (var (scenario, state) in Fixtures(now))
            {
                string key = state.Provider + "/" + scenario;
                var card = StatusPanelModel.Card(state, AgentState.FullThrottle);
                Check(card.Detail == Expected(state.Provider, scenario), key + " exact synthetic detail");
                string[] lines = card.Detail.Split('\n');
                int query = Array.IndexOf(lines, "조회 상태"), incidents = Array.IndexOf(lines, "사건"), sources = Array.IndexOf(lines, "출처");
                Check(lines[0] == "클릭: 공식 상태 페이지 열기 · 우클릭: 사용 경험 기록" &&
                    lines[1].StartsWith("현재 판정: ", StringComparison.Ordinal) && lines[2].StartsWith("대상: ", StringComparison.Ordinal) &&
                    query > 2 && incidents > query && sources > incidents &&
                    new[] { "조회 상태", "사건", "출처" }.All(title => lines.Count(line => line == title) == 1), key + " ordered sections");
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(card with { Detail = "" }))));
                Check(hash == CardHashes[key], key + " changes no recommendation, heading, tone, official line or reason");
                Check(!card.Detail.Contains(" + ", StringComparison.Ordinal) && !card.Detail.Contains("[응답 수신]", StringComparison.Ordinal) &&
                    lines.Skip(sources + 1).All(line => line.Split("https://", StringSplitOptions.None).Length <= 2), key + " one URL per source line without redundant receipt badges");
            }
            foreach (var provider in Enum.GetValues<ProviderKind>())
            {
                string empty = StatusPanelModel.Card(ProviderStatus.Unknown(provider), AgentState.FullThrottle).Detail;
                Check(empty.Contains("최근 조회 시도: 없음\n마지막 확인(응답 수신): 없음\n마지막 확인 당시 상태: 없음", StringComparison.Ordinal) &&
                    empty.EndsWith("사건\n진행 중 사건: 없음\n\n출처\n—", StringComparison.Ordinal), provider + " no-data placeholders do not invent receipt/history/incidents");
            }
            var xai = Fixtures(now).First(f => f.State.Provider == ProviderKind.Xai).State;
            string partial = StatusPanelModel.Card(xai with { Source = XaiStatusFeed.RssUrl + " [수신 실패] + " + XaiStatusFeed.SummaryUrl + " [응답 수신]" }, AgentState.FullThrottle).Detail;
            Check(partial.EndsWith("출처\n" + XaiStatusFeed.RssUrl + " [수신 실패]\n" + StringComparison.Ordinal), "partial-source failure evidence is retained");
            string expired = StatusPanelModel.Card(xai with { XaiIncidentExpiryNote = XaiStatusFeed.ExpiryNote }, AgentState.FullThrottle).Detail;
            Check(expired.Contains("진행 중 사건: 없음\n과거 사건 해제: " + XaiStatusFeed.ExpiryNote + "\n\n출처", StringComparison.Ordinal), "expiry note remains historical and inside incident section");
            var ids = xai with { IncidentId = "synthetic-confirmed", UncertainIncidentId = "synthetic-uncertain" };
            string idDetail = StatusPanelModel.Card(ids, AgentState.FullThrottle).Detail;
            Check(idDetail.Contains("확인된 사건 ID: synthetic-confirmed\n범위 미확인 사건 ID: synthetic-uncertain", StringComparison.Ordinal) &&
                !idDetail.Contains("진행 중 사건: 없음", StringComparison.Ordinal), "IDs without titles remain visible rather than being erased");
            return count;
        }
        finally { CultureInfo.CurrentCulture = savedCulture; }
    }

    private static string Expected(ProviderKind provider, string scenario)
    {
        string scope = provider switch
        {
            ProviderKind.OpenAI => "ChatGPT/Work·대화·Agent·Deep Research·파일·검색·로그인·Apps/연결 서비스·Codex(Web/Desktop/API/VS Code/CLI)",
            ProviderKind.Claude => "Claude.ai·Claude Code·Claude Cowork·Claude API(api.anthropic.com)",
            ProviderKind.Gemini => "Google 공식 대시보드의 Gemini 제품 · Gemini Notebook·기타 제품 제외",
            _ => "Grok·Cursor·Grok Bot · 공식 사건 피드 기준 (Imagine 제외)"
        };
        (string reason, string receipt, string historical, string failure, string events) = scenario switch
        {
            "normal" => ("합성 관련 서비스 정상", "09:00:00", "정상", "", "진행 중 사건: 없음"),
            "incident" => ("합성 관련 서비스 장애", "09:00:00", "일부 장애", "", "확인된 사건: 합성 진행 중 사건\n확인된 사건 ID: synthetic-confirmed"),
            "uncertain" => ("합성 범위 미확인", "09:00:00", "정상 · 03-04 08:55:00 KST", "", "범위 미확인 사건: 합성 범위 미확인 사건\n범위 미확인 사건 ID: synthetic-uncertain"),
            _ => ("합성 응답 수신 실패", "08:50:00", "성능 저하 · 03-04 08:45:00 KST", "\n최근 응답 수신 실패 이유: 합성 응답 수신 실패", "진행 중 사건: 없음")
        };
        string related = scenario == "incident" ? "관련: 합성 관련 서비스\n" : provider == ProviderKind.Gemini ? "관련: Gemini\n" : "";
        string sources = provider switch
        {
            ProviderKind.OpenAI => "https://status.openai.com/api/v2/summary.json\nhttps://status.openai.com/api/v2/components.json\nhttps://status.openai.com/api/v2/incidents.json",
            ProviderKind.Claude => "https://status.claude.com/api/v2/summary.json",
            ProviderKind.Gemini => "https://www.google.com/appsstatus/dashboard/products.json\nhttps://www.google.com/appsstatus/dashboard/incidents.json",
            _ => "https://status.x.ai/feed.xml\nhttps://data.x.ai/status/summary.json\nhttps://data.x.ai/status/incidents.json"
        };
        return $$"""
            클릭: 공식 상태 페이지 열기 · 우클릭: 사용 경험 기록
            현재 판정: {{reason}}
            대상: {{scope}}

            조회 상태
            최근 조회 시도: 03-04 09:00:00 KST
            마지막 확인(응답 수신): 03-04 {{receipt}} KST
            마지막 확인 당시 상태: {{historical}}{{failure}}

            사건
            {{related}}{{events}}

            출처
            {{sources}}
            """.ReplaceLineEndings("\n");
    }

    // Synthetic be58d8d baselines, clearing Detail only.
    private static readonly IReadOnlyDictionary<string, string> CardHashes = new Dictionary<string, string>
    {
        ["OpenAI/normal"] = "F60C66012CDDE0B541E77D232332FAAAFD353915123FA5C73383423F8C745C3F",
        ["OpenAI/incident"] = "C823DE4B49838FCE48764C16E8A28296FF7461FA624DDB775BD3ABC29C04F50E",
        ["OpenAI/uncertain"] = "F5F783C369B12C9B21FB807D3F6A7AAEAF8BFF5CB4E2626C77085DD2D5FBB5E7",
        ["OpenAI/failed"] = "B24B79F718A449D5080EE91EFB219F12CEB1F4CBFF8A720A30609A0A18D0647D",
        ["Claude/normal"] = "7E3453071AC3A5D3573CA92CBC45EDB9655D81C78AB02FBC394342957168699C",
        ["Claude/incident"] = "044BFCBE7CB4F6F56794398E64E893A36C229C75789945904BAED455B78E0CEE",
        ["Claude/uncertain"] = "0E0050328F9D5512DF2C9667CA3C0CB384CCE4E86CDE635C20FEB48D30BAA668",
        ["Claude/failed"] = "38DCE9176BC17C22A70E91F9F5C024D2EEB6269A0420EE969010913CC11BAD9C",
        ["Gemini/normal"] = "360ADD91E4AF0445ABE2FDA3DEB841D084F07B751A3D86DD8CCB6A96C88F8600",
        ["Gemini/incident"] = "175B5F150587567C3D61D798E30A2096DA8AC301DA3B6E4E23482D1ABEA3C04E",
        ["Gemini/uncertain"] = "94E2C0F344B19EC579047A164D055177890E2F286706C5750B3F454A7FDD4895",
        ["Gemini/failed"] = "891E6B2EB0E109533FFA7472F57D51B5AFDA5DBBDC0F83929F11B8FAE966D6A6",
        ["Xai/normal"] = "E24529DFECD431CB834A5DAB1011B1B4202CC22F7052AA6395D99520C6AEADF2",
        ["Xai/incident"] = "563C0DD8D336D335A6EEEFACD9C6AD000C1242F27571F538AA5CBBCEFF39466B",
        ["Xai/uncertain"] = "C7728F69B3FD1EB4BDD71092D38576B30327FA8A541EA370C87CCCCF637D7E99",
        ["Xai/failed"] = "7E969ACE348221F356D1D6C06A247A9EEB8F561268BA910AE63F218D54FAE2CF"
    };
    internal static IEnumerable<(string Scenario, ProviderStatus State)> Fixtures(DateTimeOffset now)
    {
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            string[] urls = provider switch
            {
                ProviderKind.OpenAI => [ProviderStatusClient.OpenAiSummaryUrl, ProviderStatusClient.OpenAiComponentsUrl, ProviderStatusClient.OpenAiIncidentsUrl],
                ProviderKind.Claude => [ProviderStatusClient.ClaudeSummaryUrl],
                ProviderKind.Gemini => [ProviderStatusClient.GoogleProductsUrl, ProviderStatusClient.GoogleIncidentsUrl],
                _ => [XaiStatusFeed.RssUrl, XaiStatusFeed.SummaryUrl, XaiStatusFeed.IncidentsUrl]
            };
            string source = string.Join(" + ", urls.Select(url => url + (provider == ProviderKind.Xai ? " [응답 수신]" : "")));
            var normal = new ProviderStatus(provider, OfficialStatus.Operational, now, now, "합성 관련 서비스 정상",
                provider == ProviderKind.Gemini ? "Gemini" : "", Source: source,
                LastKnownStatus: OfficialStatus.Operational) { LastKnownStatusUtc = now };
            yield return ("normal", normal);
            yield return ("incident", normal with { Status = OfficialStatus.PartialOutage, Reason = "합성 관련 서비스 장애",
                RelevantComponent = "합성 관련 서비스", IncidentTitle = "합성 진행 중 사건", IncidentId = "synthetic-confirmed",
                LastKnownStatus = OfficialStatus.PartialOutage });
            yield return ("uncertain", normal with { Status = OfficialStatus.Unknown, Reason = "합성 범위 미확인",
                AssessmentIssue = "합성 범위 미확인", UncertainIncidentTitle = "합성 범위 미확인 사건", UncertainIncidentId = "synthetic-uncertain",
                LastKnownStatusUtc = now.AddMinutes(-5) });
            yield return ("failed", normal with { Status = OfficialStatus.Unknown, Reason = "합성 응답 수신 실패",
                LastSuccessfulCheckUtc = now.AddMinutes(-10), LastKnownStatus = OfficialStatus.Degraded,
                LastKnownStatusUtc = now.AddMinutes(-15) });
        }
    }
}
