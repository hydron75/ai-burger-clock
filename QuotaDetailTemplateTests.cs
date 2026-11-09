using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

internal static class QuotaDetailTemplateTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota detail template: " + message);
        }
        var now = new DateTimeOffset(2032, 3, 4, 0, 0, 0, TimeSpan.Zero);
        foreach (var (scenario, state) in QuotaPresentationTests.DetailFixtures(now))
        {
            var section = QuotaPanelModel.Section(state, now);
            string key = state.Provider + "/" + scenario;
            Check(section.Detail == Expected(state.Provider, scenario), key + " exact synthetic detail");
            string[] titles = ["리셋·한도 복원 예정", "시간·사용량 비교", "조회 상태", "조회 주기"];
            string[] lines = section.Detail.Split('\n');
            int[] positions = titles.Select(title => Array.IndexOf(lines, title)).ToArray();
            Check(positions.All(position => position >= 0) && positions.SequenceEqual(positions.Order()) &&
                titles.All(title => lines.Count(line => line == title) == 1) && lines[1].StartsWith("범위: ", StringComparison.Ordinal),
                key + " has the same section titles once and in order");
            Check(section.Rows.All(row => row.Detail == section.Detail) && section.Metadata.Detail == section.Detail &&
                (section.Scope is null || section.Scope.Detail == section.Detail), key + " all targets share one detail");
            var unchanged = new { Heading = section.Heading with { Detail = "" },
                Scope = section.Scope is {} scope ? scope with { Detail = "" } : null,
                Rows = section.Rows.Select(row => row with { Detail = "" }), Metadata = section.Metadata with { Detail = "" } };
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(unchanged))));
            Check(hash == NonDetailHashes[key], key + " preserves every field other than Detail against the pre-change baseline");
        }
        foreach (var provider in Enum.GetValues<QuotaProvider>())
        {
            var state = QuotaPresentationTests.DetailFixtures(now).First(f => f.State.Provider == provider).State;
            var empty = QuotaPanelModel.Section(state with { Reading = new(provider, []) }, now);
            Check(empty.Rows.Count == 0 && empty.Detail.Contains("리셋·한도 복원 예정\n조회 값 없음", StringComparison.Ordinal) &&
                empty.Detail.Contains("시간·사용량 비교", StringComparison.Ordinal), provider + " empty windows retain both sections without inventing rows");
            var recent = new QuotaEarlyResetAlert(provider, 1, 2, now,
                [new("synthetic-weekly", "합성 주간", 10080, 60, 5, now.AddDays(2), now.AddDays(7))]);
            string original = QuotaPanelModel.Section(state, now).Detail;
            string record = QuotaEarlyResetText.Detail(recent, now);
            Check(QuotaPanelModel.Section(state, now, recent).Detail ==
                original.Replace("\n조회 상태\n", "\n" + record + "\n\n조회 상태\n", StringComparison.Ordinal),
                provider + " early-reset record stays verbatim between comparison and query state");
        }
        return count;
    }

    private static string Expected(QuotaProvider provider, string scenario)
    {
        (string name, string scope, string resetMinutes, string note) = provider switch
        {
            QuotaProvider.Codex => ("ChatGPT", "ChatGPT 계정의 Work/Codex 한도입니다. 일반 채팅의 모든 모델 한도를 뜻하지 않습니다.", "5", "—"),
            QuotaProvider.Claude => ("Claude", "Claude Code CLI의 /usage 응답으로 받은 구독 사용 한도입니다.", "5", "—"),
            QuotaProvider.Gemini => ("Gemini", "agy CLI의 Antigravity Gemini 모델 그룹 한도입니다. Gemini Apps 웹·모바일의 전체 한도가 아니며, Antigravity의 Claude/GPT 모델 그룹과 크레딧은 포함하지 않습니다.", "5", "Gemini 사용 0% 창의 리셋은 빠른 조회 기준에서 제외합니다."),
            QuotaProvider.Xai => ("xAI", "Grok CLI의 ACP _x.ai/billing 응답으로 받은 주간 크레딧 한도입니다.", "10", "xAI 예약 조회는 최소 10분입니다. 시작·Refresh·복귀·네트워크 조회에는 적용하지 않습니다."),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        bool missing = scenario == "none";
        string comparison = scenario switch
        {
            "normal" => "합성 주간: 시간 경과 50% · 균등 사용 기준보다 25%p 느림",
            "previous" or "error" => "합성 주간: 시간 비교: 이전 조회값",
            "none" => "조회 값 없음",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return $$"""
            {{name}} 한도 · 모든 시각 KST
            범위: {{scope}}

            리셋·한도 복원 예정
            {{(missing ? "조회 값 없음" : "합성 주간: 2032-03-07 21:00:00 KST")}}
            실제 복원 여부는 새 조회로 확인합니다.

            시간·사용량 비교
            {{(missing ? "마지막 조회 기준: 확인 불가" : "마지막 조회 기준, 방금 전")}}
            {{comparison}}
            리셋 예정에서 역산한 단순 시간 기준입니다. 소진 예측이나 실제 창 시작·활성 확인은 아닙니다.

            조회 상태
            마지막 성공: {{(missing ? "—" : "03-04 09:00")}}
            다음 조회: {{(missing ? "—" : "03-04 10:00")}}
            최근 시도: {{(missing ? "—" : "03-04 09:00:00")}}{{(scenario == "error" ? "\n합성 조회 오류\n합성 캐시 오류" : "")}}

            조회 주기
            기본 1시간 · 사용 90% 초과~100% 미만은 1시간 · 사용 100%는 15분 · 리셋 전후 15분은 {{resetMinutes}}분 · 실패 시 15분부터 재시도
            참고: {{note}}
            공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다.
            """.ReplaceLineEndings("\n");
    }

    // Synthetic c9be316 baselines of all QuotaLine fields after clearing only Detail.
    private static readonly IReadOnlyDictionary<string, string> NonDetailHashes = new Dictionary<string, string>
    {
        ["Codex/normal"] = "0F228FABF21E4BE74EABCB1B54FB283E205159736ECC081F5F69831D392E7514",
        ["Codex/previous"] = "E3397AE2E7AADEF82D9DA1EE1FEE458CBE6364BFE5C54FC417BC55DFB2D0BE57",
        ["Codex/none"] = "544785FD961E370A48E007C9A1E0AEBF8449433301B99800C633343C0B0BB344",
        ["Codex/error"] = "C4DB94D3ADBCD786ED1DF122B680CFFE3C7CB6A7C09C409C502417088CC0E074",
        ["Claude/normal"] = "6EB3E0F22CF76483155945F6C3AC7807EC589AE4B8F5D3C2C8C1A2690DF3C5ED",
        ["Claude/previous"] = "53472932E7ADAF8CE7241F92E256223005A4A10E9EA9C66FDFC6E61EE1CBB1E8",
        ["Claude/none"] = "9B96E8A17058749D425562A399B108600B502871341D46CB04125DE9F9156A56",
        ["Claude/error"] = "C86D8001E23A86F5D00C5AE399B53E66A82A7020AF6525DFEC1E27692554D03A",
        ["Gemini/normal"] = "E2AD7C29E3A6AAB1F5F7C286BABA83794771AC2E730ED9B67F8094D92719A48A",
        ["Gemini/previous"] = "148E7827AC939ED792F7F8BAEC41031872526FA6AD49E7F1B583F6CA3E9622D8",
        ["Gemini/none"] = "C0E9A123523F5CB084188CCEC3EAA1C8F8A07F54A964CD3443BBF0C328C669AF",
        ["Gemini/error"] = "560DDFB0CB21A29FFE39184E8DEB6197D460576530EB328411342AA3C12BB186",
        ["Xai/normal"] = "C7250220985173A40B6B0842FA04972CE8D20E2AE8297A6C5C83BFC57481538B",
        ["Xai/previous"] = "CAFBF586C5E328A0067AA291FEAE73D541F35C14CAB0DE12DCC15C8F900392DE",
        ["Xai/none"] = "7B7C32F1C48FEB8726A3563EFC88C68B71F8D1F43F9887A7DBCAFAEE60C73CA6",
        ["Xai/error"] = "75721A4EEBF67D88BA134A1171740B78322639F64D69E25D356D24B45C043CCD"
    };
}
