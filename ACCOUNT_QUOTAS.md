# 2.2.0 — Work / Codex·Claude 계정 한도

구현·검증일: **2026-09-30 KST**. Gemini 계정 한도는 제외했다. 기존 Gemini 공식 상태·실측 입력·통계는 변경하지 않았다.

## 사용하는 방법

1. 설치된 공식 Codex 및 Claude Code CLI에 본인 계정으로 로그인해 둔다. 앱은 설치·업데이트·로그인을 대신 수행하지 않는다.
2. 트레이를 클릭한 뒤 **한도 보기**를 누른다. 같은 크기의 화면에 두 계정의 실제 반환된 한도 창만 표시한다. 창이 많으면 해당 영역을 스크롤할 수 있다.
3. 잔여율과 리셋 카운트다운을 보고, 마우스를 올려 정확한 KST 리셋 시각·마지막 성공·최근 시도·다음 조회·오류를 확인한다.
4. 기존 **Refresh**는 공식 상태와 두 계정 한도를 함께 갱신한다. **상태 보기**로 기존 세 Provider 화면에 돌아온다.

사용률이 45%라면 잔여량은 55%다. 값이 없다고 0%나 무제한으로 바꾸지 않는다. Work와 Codex는 사용자가 확인한 공통 한도라는 전제에서 **Work / Codex**로 표시한다. 일반 ChatGPT 모든 모델·메시지 한도를 의미하지 않는다.

계정 한도는 시간표·공식 장애·사용자 실측과 분리했다. 한도 부족이나 조회 실패가 기존 GO/HOLD/STOP, 트레이 색상, Windows 장애 알림, 통계 결과를 바꾸지 않는다. 새 한도 알림이나 리셋권 사용 기능은 추가하지 않았다.

## 읽는 경로와 경계

### Work / Codex

설치된 native `codex.exe app-server --listen stdio://`에 표준 입출력으로 다음 요청만 보낸다.

```text
initialize → initialized → account/rateLimits/read
```

`rateLimitsByLimitId`를 우선하고 없는 경우에만 `rateLimits`를 사용한다. `primary`/`secondary` 위치가 아닌 `windowDurationMins`로 5시간·주간 등을 구분한다. `usedPercent`는 사용률이며 `resetsAt`은 Unix 초다. 현재 계정이 주간 한도만 반환하면 주간만 표시한다.

공식 계약: [Codex App Server](https://learn.chatgpt.com/docs/app-server). `thread/start`·`turn/start`·리셋권 사용 요청은 보내지 않는다.

### Claude

공식 native `claude.exe`의 headless `/usage`를 사용한다. 핵심 명령은 `claude -p "/usage" --output-format stream-json --verbose`이며 실제 앱에서는 다음 보호 조건도 적용한다.

- 사용자 설정 소스 제외, 모든 훅 비활성화, strict 빈 MCP 설정, 사용 도구 없음.
- `--max-turns 0`, `--permission-prompts none`, 프롬프트 제안·Chrome·세션 저장 비활성화.
- 자식 프로세스의 자동 업데이트 비활성화. 중립 작업 폴더 사용.
- 정상 종료와 `local_command=usage`, 성공 결과, 모델 턴·토큰·캐시·보고 비용 0, 비어 있는 `modelUsage`를 확인한 뒤 값 채택.

`assistant.usage_report.rate_limits.limits[]`의 `session`, `weekly_all`, `weekly_scoped`, `percent`, ISO 8601 `resets_at`을 검증한다. 모델별 범위를 따로 표시하며, `is_active=false`나 사용률 0%인 정상 창도 버리지 않는다. `extra_usage`는 한도나 리셋권 개수 대신 사용하지 않는다. 모르는 형식·빈 목록·null이면 조회 불가다.

근거는 사용자가 제공한 **CLI 2.1.284** 실제 구조화 출력, 설치 CLI 도움말, [공식 headless 안내](https://code.claude.com/docs/en/headless), [공식 SDK 변경 기록](https://github.com/anthropics/claude-agent-sdk-typescript/blob/main/CHANGELOG.md)의 usage report 계약이다. SDK나 Node.js를 앱에 추가하지 않았다.

### 이번 검증에서 고친 Claude 문제

최초 보호 설정에는 `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1`도 넣었다. 이때 공식 CLI는 성공·0턴·0토큰으로 종료했지만 `rate_limits.limits:null`을 반환했다. **다른 모든 인자를 유지하고 이 환경 변수 설정만 제외한 비교에서 한도 3개가 반환됐다.** 이 실행 조건에서는 해당 옵션이 조회를 막는 것으로 좁혀져, 앱이 이 변수를 강제로 설정하지 않도록 수정하고 회귀 검사를 추가했다.

사용자의 영구 환경 변수·CLI 설정은 바꾸지 않는다. 따라서 사용자가 직접 이 환경 변수를 켠 환경에서는 여전히 한도 조회가 막힐 수 있다. 과거 다른 날짜·버전의 null 응답 원인까지 이번 비교로 확정한 것은 아니다.

모델 호출 없는 결과를 관측한 것이 모든 버전·계정의 서버 측 절대 비소비 보증을 뜻하지 않는다. 공식 CLI의 내부 인증·통신은 CLI가 관리한다. 앱은 토큰·쿠키·인증 파일을 읽지 않고 비공개 usage HTTP endpoint를 직접 호출하지 않는다. 전체 stdout/stderr·계정 식별자·대화·프롬프트는 DB/로그에 저장하지 않는다.

## 자동 조회

| 조건 | 해당 Provider 조회 주기 |
|---|---|
| 기본 | 6시간 |
| 확인된 어떤 한도 창이든 잔여 10% 미만 | 1시간 |
| 알려진 어떤 리셋 시각이든 15분 전~15분 후 | 5분 |

- 가장 짧은 주기를 사용한다. 정확히 10%는 기본 주기다.
- 긴 대기 중에도 리셋 15분 전에 조회한다. 새 응답이 다음 리셋 시각을 반환해도 이전 리셋의 +15분 구간은 유지한다.
- 다음 5분 후보가 모든 리셋 구간 밖이면 6시간/1시간으로 복귀한다. 미래의 다른 리셋 구간 진입은 계속 우선한다.
- 시작·절전 복귀·인터넷 연결 복구 때 즉시 1회 요청한다. 진행 중인 Provider 요청은 중복 실행하지 않고 복귀 요청은 최대 1회 대기로 합친다.
- 카운트다운은 기존 1초 UI 타이머에서 계산한다. 화면을 매초 그린다고 매초 CLI를 실행하지 않는다.
- CLI 호출은 기본 30초 제한이다. 출력/행 크기를 제한하고 양쪽 출력을 비동기로 읽는다. 종료 시 앱이 만든 프로세스 트리만 정리한다.

조회 실패 후에는 마지막 성공값·성공 시각을 보존하고 **이전 조회값**으로 표시한다. 재시작 캐시도 새 조회 전까지 이전 값이다. 리셋 시각 경과는 **갱신 대기**이며 자동 100% 회복 판정이 아니다. 예정된 조회가 5분 이상 지나도록 이루어지지 않거나 시계가 크게 뒤로 이동하면 역시 이전 값으로 취급한다.

여기서 ‘성공 시각’은 앱이 유효한 CLI 응답을 받은 시각이다. 서버 데이터 원래 생성 시각·캐시 여부를 별도로 제공하지 않으면 신선도를 더 강하게 보장하지 않는다. 예정보다 이른 리셋은 다음 실제 조회에서 반영되며 최대 기본 주기만큼 발견이 늦을 수 있다. 사용률 감소만으로 자연/수동/시스템 리셋 원인을 구분하지 않는다.

## 저장·변경 범위

- 대상: `net10.0-windows`, WinForms, Windows 11 x64. 버전 **2.2.0**.
- NuGet: 기존 `Microsoft.Data.Sqlite 10.0.12`만 유지. 추가 의존성 없음.
- DB: `%LOCALAPPDATA%\AIBurgerClock\burgerclock.db`, **schema 2 유지**, 이번 기능의 schema migration 없음.
- `AppMetadata`의 `AccountQuota.v1.Codex`, `AccountQuota.v1.Claude` 두 항목만 덮어쓴다. 캐시 payload version 1, Provider·한도 수치·기간·리셋·마지막 성공 시각·유효한 리셋 구간 기준 시각을 저장한다.
- 한도 창 최대 64개, 캐시 읽기/쓰기 동일 크기 제한. Unicode 이름이 많은 정상 캐시도 저장 후 다시 읽히도록 회귀 검사했다.
- 기존 `UsageEvents`, `ProviderStatusHistory`, `ProviderStatusCache`, 공휴일 설정·시간표 정책은 분리 유지한다.

기존 변경 파일: `AiBurgerClock.csproj`, `Program.cs`, `TrayApplicationContext.cs`, `StatusWindow.cs`, `UsageStore.cs`, `SelfTest.cs`, `SmokeTest.cs`, `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`.

새 파일: `AccountQuotaModels.cs`, `AccountQuotaParsers.cs`, `AccountQuotaPolicy.cs`, `AccountQuotaClient.cs`, `AccountQuotaMonitor.cs`, `AccountQuotaView.cs`, `AccountQuotaTests.cs`, `AccountQuotaClientTests.cs`, `AccountQuotaMonitorTests.cs`, `AccountQuotaUiChecks.cs`, `LiveQuotaProbe.cs`, 이 문서.

## 검증 결과

### 빌드와 자동 검사

- .NET SDK **10.0.401**, Release build/publish `-warnaserror`: **경고 0 / 오류 0**.
- 일반 Release 및 최종 단일 EXE `--self-test`: **250,654 assertions 통과**, 실제 프로세스 종료 코드 0.
- 한도 파서·기간 판정·공식 CLI 프로토콜·출력 크기·취소·정확한 조회 경계: **158**.
- 한도 Provider 격리·동시 조회 합치기·종료·SQLite 캐시·재시작·손상·64개 Unicode 창: **54**.
- 기존 자동 시작 판정 229, DST/Schedule 242,363, 공휴일 1,399, 트레이 6,122, 공식 상태 파서/HTTP 96, 상태 갱신·권고·취소 99, 저장/통계 132, 공휴일 캐시 2도 통과.
- `--smoke-test`: PNG 생성 포함 **117 PASS**, 종료 코드 0. 임시 DB·가짜 HTTP·가짜 한도만 사용.

UI 검사는 트레이 시작·열기·숨기기·전환·카운트다운·알림, 세 Provider의 클릭·권고·기록, Success/Slow/Error/Interrupted·메모, Statistics 7일/30일/전체·No data, 공휴일, 한도 화면 전환·잔여율·실패 후 이전 값·Refresh·모의 Resume·리셋 경과·Gemini 상태 복귀·정상 종료를 확인했다. DPI 배율이 적용된 한도 화면 PNG를 열어 글자·배치를 확인했다.

자동 시작 검사는 가짜 값에 대한 순수 판정이며 `--verify-autostart`로 실제 레지스트리를 쓰는 검사는 하지 않았다. Windows BalloonTipShown 15회는 알림 요청/이벤트 증거이지 사용자 화면에서 모두 보였다는 보장은 아니다. 실제 재부팅·절전·물리적인 네트워크 단절, 장기 누수 계측은 수행하지 않았다.

### 실제 배포본 확인

**2026-09-30 02:09 KST** 기존 dist 경로에서 최종 EXE를 트레이로 실행하고 Responding=True를 확인했다. 정상 시작 경로가 다음 두 한도를 실제로 조회해 DB에 저장했다. 개인 계정의 구체적인 잔여량·리셋 시각은 저장소 문서에 포함하지 않는다.

- Work / Codex: 주간 1개. 잔여율·리셋 시각 조회 및 저장 확인.
- Claude: 5시간·주간 전체·모델별 주간 3개. 각 잔여율·리셋 시각 조회 및 저장 확인.

이는 당시 값이며 현재 잔여량 보장이 아니다. Claude 응답은 모델 턴·토큰·보고 비용 0 검사까지 통과해야 채택된다. 서비스 상태도 OpenAI·Claude·Gemini 세 곳 모두 새 성공 조회가 기록됐다. Gemini 한도 대신 Antigravity나 API quota를 넣지 않았다.

배포 전후 DB 읽기 전용 비교에서 quick_check=ok, schema 2, 기존 사용자 실측·비한도 AppMetadata 설정 해시가 동일했다. 실제 사용 기록은 0건이므로 비어 있지 않은 기록·재시작 보존은 테스트의 1만 건 데이터로 검증했다. HKCU Run 경로는 기존 dist EXE `--autostart` 그대로이며 레지스트리 편집은 하지 않았다.

로컬 검사 로그·PNG: `artifacts/quota-2.2.0/`. 초기 Claude 실패 로그는 수정 전 증거이며 최종 배포본의 두 계정 성공 조회는 위 기록과 실제 캐시에 해당한다. 인증 정보·전체 CLI 출력은 로그에 남기지 않았다.

## Git 기준점과 백업

| 기준 | 커밋 / 상태 |
|---|---|
| 기능 구현 전 로컬 main | `a1195613279ce6f4cd07a4d078a29b0f564b2833` |
| GitHub 게시 전 최신 main | `d255919b799192af389b4655a350b6689f5b401c` — 이전 기준과 차이는 AGENTS.md만 있음 |
| 2.2.0 구현·버전 커밋 | `a9ba6be59f8df2c48c19fe6b08e6b7be8e17e2f4` |
| 게시 전 Windows 빌드·자체 검사 대상 | `a9ba6be59f8df2c48c19fe6b08e6b7be8e17e2f4` — 경고/오류 0, 250,654 assertions, 종료 코드 0 |
| PR 병합 | 미병합. 게시용 브랜치 `feature/account-quotas-2.2.0` |

GitHub 게시 전 재검증에서는 현재 실행 중인 dist 앱을 교체하거나 계정을 다시 조회하지 않았다. UI 117 PASS는 최초 로컬 배포 검증 결과이며, 그 이후 기능 소스 변경 없이 Git 기록·문서만 정리했다. 마지막 검사 이후의 후속 커밋이 이 문서만 변경했는지도 Git diff로 확인한다.

- 소스 기준점: `a1195613279ce6f4cd07a4d078a29b0f564b2833`, 최초 작업 트리 깨끗함.
- 소스 백업: `../backups/AiBurgerClock-before-quota-a119561-20260930.zip`, SHA-256 `84010E190C68AA5D345365DE5CEB82F80F5A15E230847CB430537001F30B8170`.
- 기존 EXE·DB 백업: `../backups/quota-2.2.0-before-deploy-20260930/`. 앱 정상 종료 및 DB WAL 없음 확인 후 복사·해시 일치 확인.
- 기존 2.1.2 EXE SHA-256: `AE79C4BBEDF6B3629828D20ED7A7626591E78BA836A0BC2CC992140B8AA28013`.
- 원본/백업 DB SHA-256: `97B959EE179E3731C360870FD5C7E1CD5D451796A48163BAD45EF5DD63E6600F`.
- 최종 EXE: **`dist/win-x64/AI Burger Clock.exe`**, FileVersion **2.2.0.0**, **2,958,571 bytes**.
- 로컬 배포 EXE ProductVersion: `2.2.0+a1195613279ce6f4cd07a4d078a29b0f564b2833`. 구현을 커밋하기 전에 빌드했으므로 이 접미사는 기능 구현 전 HEAD다. 커밋되지 않았던 2.2.0 소스 변경까지 포함한 실행 파일이며 해당 HEAD만 체크아웃해서는 같은 기능을 재현할 수 없다.
- 최종 EXE SHA-256: **`21E462C8F7B834292EBAE47C67FBACF3822B707904FC6D4911EA9BB955EA6A6A`**.
- framework-dependent 단일 EXE이므로 .NET 10 Desktop Runtime x64가 필요하다. 계정 한도에는 별도로 로그인된 공식 CLI가 필요하다.

최초 로컬 배포 후 사용자의 별도 요청으로 소스·문서를 GitHub PR에 올린다. 기존 주간 모니터링 자동화는 변경하지 않았다. EXE·DB·캐시·백업·로컬 진단 로그는 저장소에 포함하지 않는다.

이전 버전으로 돌아가려면 앱을 종료한 뒤 위 백업의 2.1.2 EXE를 기존 dist 경로로 복사한다. schema 2는 같으며 이번 변경은 기존 메타데이터에 두 캐시 항목을 추가한 것뿐이다. DB까지 복원해야 할 경우 현재 DB도 먼저 보존한 뒤, 모든 앱 연결이 닫힌 상태에서 백업 DB를 복원한다. 이때 백업 이후의 사용 기록은 되돌아간다는 점에 주의한다.

## 남은 제한

Claude headless usage report의 버전별 변경·선택적 필드·캐시 신선도에는 여전히 주의가 필요하다. 로그아웃·CLI 미설치·서버 실패·지원되지 않는 응답이면 조회 불가/이전 값으로 표시한다. 앱이 토큰을 고쳐서 복구하거나 보호 조건을 풀어 추론 요청을 보내지 않는다. CLI의 공식 로그인/사용량 화면에서 확인한 뒤 Refresh하면 된다.

Gemini Apps 한도, 리셋권 개수/사용, 한도 알림, 사용자별 다중 계정 선택, 한도 이력 그래프, 실측 기반 추천·자동 시간표 조정은 이번 범위에 포함하지 않는다.
