# Gemini / Antigravity 한도 — 미배포 구현·Windows 검증

초기 구현·검증일: **2026-10-08 KST**. 최신 main 통합·재검증일: **2026-10-09 KST**. 브랜치: `feature/gemini-agy-quotas`. 작업 전 main: `aa8dd50c2d6a66df951306f410537dbff42a525d`.

작업 중 main에 병합된 PR #29의 계획 문서 갱신만 fast-forward로 반영했다. PR base는 `612df8716e507733847204c5f3ce8214c6f1b357`이며 앱·검사 코드는 기존 비교 기준과 동일하다. 아래 비교 수치는 실제 실행한 `aa8dd50` 기준을 사용한다.

공통 코드와 Windows 한도 UI 변경이다. **Mac 검증·PR 병합은 대기 중이며 버전 변경·publish·dist 교체는 하지 않았다.** 현재 Windows 배포본은 2.2.4 그대로다. 이 문서는 배포 완료 기록이 아니다.

## 조회 대상과 데이터

공식 Antigravity CLI `agy` **1.3.1 이상·2.0 미만의 안정 버전**에서 로컬 내장 명령을 사용한다. 이 PC의 설치 버전은 2026-10-09 KST에 **1.3.2**로 확인했다(`--version` 종료 코드 0). 버전 확인과 실제 계정 조회 성공은 별개다.

```text
agy --version
agy -p /usage --output-format json --print-timeout 20s
```

두 번째 명령은 버전 사전 확인이 성공한 경우에만 실행한다. 인수는 shell을 거치지 않고 `ProcessStartInfo.ArgumentList`로 전달한다.

| 응답 범위 | 표시 | 잔여량 | 리셋 |
|---|---|---|---|
| `command.data.groups`의 `Gemini Models` | Gemini / Antigravity · Gemini 모델 | 이 그룹만 사용 | 다른 모델 그룹과 합치지 않음 |
| `gemini-5h`, `window: 5h` | 5시간 | `remaining_fraction × 100` | `reset_time` ISO 8601 → UTC 저장 / KST 표시 |
| `gemini-weekly`, `window: weekly` | 주간 | 같은 방식 | 같은 방식 |

- Gemini Apps 웹·모바일의 전체 한도로 표시하지 않는다. Antigravity의 Claude/GPT 그룹과 크레딧도 제외한다.
- 사람이 읽는 `response` 문구를 재해석하지 않고 구조화된 `command.data`만 사용한다. 그룹 중복·알 수 없는 Gemini bucket·기간 불일치·잘못된 잔여율은 실패로 처리한다.
- 잔여율은 유한한 0~1 값이어야 한다. 한 창이 누락되어도 존재하는 창만 표시하며 그룹 전체 누락·빈 값은 조회 불가다. 리셋 시각의 누락·null은 미제공으로 표시하고 추정하지 않는다.
- 내부 `UsedPercent`로 변환하되 아주 작은 양수의 반올림 때문에 정확히 0% 잔여로 판정되지 않도록 처리했다. 화면 반올림과 조회 주기의 실제 0% 판정은 구분한다.
- CLI 응답 수신 시각과 서버 데이터 원래 생성 시각은 다르다. 공식 변경 기록의 `/usage` refresh 설명만으로 캐시 TTL·매번의 신선도·실시간 갱신을 보장하지 않는다.

## 실행 조건과 남은 제약

1. 설치·로그인된 공식 native `agy`를 사용한다. Windows는 절대 PATH 후보와 `%LOCALAPPDATA%/agy/bin/agy.exe`, Mac 공통 경로는 GUI PATH와 `~/.local/bin`, `/opt/homebrew/bin`, `/usr/local/bin` 후보를 사용한다. 앱이 설치·로그인·인증 갱신하지 않는다.
2. **1.3.1 이상·2.0 미만의 안정 버전**을 허용한다. 1.3.1 미만·2.x·배너·사전 배포/빌드 접미사·모호한 버전에는 `/usage`를 보내지 않는다. 지원 범위 안에서도 기존 JSON 구조·정상 종료·0턴/0토큰 검사를 모두 통과해야 결과를 채택한다. 오류 문구는 `agy CLI 1.3.1 이상이 필요합니다(2.0 미만 안정 버전).`이다. 모든 1.x 릴리스의 실제 계정 호환성을 미리 검증했다는 뜻은 아니다.
3. 자식 실행에만 공식 `AGY_CLI_DISABLE_AUTO_UPDATE=true`를 설정한다. 사용자 영구 환경·설정은 바꾸지 않는다. 표준 입력을 닫고 출력·오류를 동시에 읽는다. CLI print timeout은 20초, 버전 확인을 포함한 앱 deadline은 30초다. 종료·stdout/stderr 정리의 각 대기는 5초로 제한하지만 여러 정리 대기가 있으므로 전체 wall-clock을 30초 또는 35초 이내라고 보장하지 않는다. Monitor는 별도의 40초 취소 제한을 사용한다.
4. 인증 대기 표식을 발견하면 중단한다. 출력은 2 MiB·JSON 깊이 32 등으로 제한하며 시간 초과·취소 시 자신이 시작한 프로세스 트리만 종료한다. 원문 오류·OAuth URL·계정 정보는 화면이나 로그로 내보내지 않는다.
5. 정상 종료·`status: SUCCESS`·빈 `conversation_id`·`num_turns: 0`·`command.name: usage`를 확인한다. 필수 입력/출력/thinking/cache-read/total token 카운터는 모두 0이어야 하고 새 usage 카운터의 비영 값과 제공된 비용의 비영 값도 거부한다. 검증되지 않은 응답은 한도 캐시에 채택하지 않는다.
6. **설정 격리 제한:** 이 버전에서 실행별 사용자 hooks·MCP 제외 공식 옵션은 확인하지 못했다. 기존 설정을 읽거나 초기화할 수 있다는 제한을 설명했고 사용자가 자동 연결을 명시적으로 승인했다. 입력 닫기·제한시간·응답 검증이 hooks/MCP 초기화 자체를 막는 것은 아니다. 완전한 설정 격리·아무 부작용도 없음·모든 계정 실행의 절대 비소비로 확대하지 않는다.

앱은 인증 토큰·쿠키 파일을 직접 읽거나 비공개 usage HTTP를 호출하지 않는다. 리셋권 조회·사용, 결제·크레딧, 모델 작업 실행도 추가하지 않는다.

## 자동 조회와 캐시

`QuotaProvider.Gemini`를 기존 enum의 끝에 추가해 Codex/Claude의 저장 숫자 식별자를 유지했다. 기존 `AccountQuotaMonitor`의 Provider별 독립 루프·Refresh·시작·절전 복귀·연결 복구 경로에 연결한다.

- 기본 6시간 → 실제 잔여 0% 초과~10% 미만 1시간 → 정확히 0% 15분 → 리셋 ±15분 5분 중 가장 짧은 주기를 적용한다.
- 알려진 리셋 15분 전에는 긴 대기를 끝내고 진입하며, 이전 리셋의 +15분 구간도 보존한다. 기존 실패 재시도와 마지막 성공값 유지 규칙을 그대로 사용한다.
- 리셋 시각이 지났다고 100%를 가정하거나 한도 감소·회복만으로 리셋 원인을 확정하지 않는다. 회복 알림은 추가하지 않았다.
- 기존 SQLite **schema 2**를 유지한다. 정상 앱 실행에서는 `AppMetadata`의 `AccountQuota.v1.Gemini`에 정규화된 한도 정보만 저장한다. 전체 CLI 출력·계정 식별 정보·자격 증명은 저장하지 않는다.
- 이번 실제 조회 검증은 Gemini 클라이언트만 호출했고 사용자 DB를 읽거나 저장하지 않았다. 캐시·재시작은 임시 DB 검사로 확인했다.

## Windows UI

[MACOS_UI_PLAN.md](MACOS_UI_PLAN.md)의 결정 9/10을 따른다. Windows는 **상태 보기 ↔ 한도 보기**를 유지한다. 한도 제목은 바깥 기준선, 내용은 Provider별 흰 박스, 안쪽 글자 기준선은 기존 Provider 카드와 같다. 클릭할 수 없는 한도 박스에는 hover·메뉴를 추가하지 않았다.

기존 창 크기 374×518 logical px와 한도 viewport 342×214를 유지한다. 세 Provider가 늘어난 만큼 **한도 영역만 세로 스크롤**하며 가로 스크롤이나 창 전체 스크롤은 없다. 같은 행 구성의 매초 갱신은 컨트롤과 스크롤 위치를 보존한다. 범위 설명·정확한 KST 리셋·최근 성공/시도·다음 조회·실패 이유는 Tooltip에 남긴다.

## 최신 main 통합 뒤 Windows 검증 (2026-10-09 KST)

Git 비교 기준은 `main @ e9504bc3e119bec4faf3ebb2c2cef40147701964`(#41 포함)이다. 기존 PR HEAD `14cef69de9350803158da39090635167a799ae05`에 main을 merge했고, 검증한 C# 소스는 통합 커밋 `243468d337d2d02a2447c11abd02c5241fb767ce`와 같다. 후속 커밋은 검증 기록·PNG만 추가한다. main의 공통 검사 분리·StatisticsText·StatusTicker·Provider별 박스·메모 잘림 안내를 보존했다. Mac 전용 파일·AGENTS·계획·버전 파일은 main 대비 변경하지 않았다.

SDK **10.0.401**. 두 소스에서 `build.ps1`을 실행해 각각 **경고 0 / 오류 0**, 실제 WinExe `--self-test` 종료 코드 **0**을 확인했다. `Start-Process -Wait -PassThru`의 실제 ExitCode를 검사하는 스크립트 경로를 사용했다.

| 검사 그룹 | 최신 main | 통합 후 | 차이 |
|---|---:|---:|---:|
| Gemini 한도 파서/정책 | 0 | 85 | +85 |
| Gemini CLI 보호/버전/프로토콜 | 0 | 78 | +78 |
| 한도 Monitor/재시작/취소 | 80 | 98 | +18 |
| 경로/IANA/표시/CLI 후보 | 37 | 39 | +2 |
| PanelModel 문구/톤 | 249 | 261 | +12 |
| 나머지 공통 검사 | 250,509 | 250,509 | 0 |
| **공통 검사 합계** | **250,875** | **251,070** | **+195** |
| **Windows 전용 검사 합계** | **307** | **307** | **0** |
| **Windows self-test 전체** | **251,182** | **251,377** | **+195** |

감소·중복 실행한 그룹은 없다. Windows 전용 307건은 자동 시작 판정 229 + 트레이 픽셀 44 + CLI 경로 3 + 경고 로그 31이다. 이전 #30의 +179에서 버전 허용·거부 검사 16건을 더해 +195다. OS 독립 `Shared.Tests`도 실제 실행하여 **251,070건 PASS**, 종료 코드 **0**을 확인했다.

### 창·트레이·조회 중 응답성

- 최신 main Release EXE의 `--smoke-test --report-directory ...`: **255 PASS**, 종료 코드 **0**.
- 통합 후 최종 Release EXE: **278 PASS**, 종료 코드 **0**. 임시 DB·가짜 HTTP/CLI·주입 시계만 사용했으며 `--verify-autostart`는 사용하지 않았다.
- 실제 Windows 메시지 루프에서 Gemini 가짜 응답만 **10초** 지연했다. 트레이 Refresh handler가 즉시 반환했고, 대기 중 native 메뉴 표시·상태 창 닫기/재열기가 정상 동작했다.
- 최종 측정 **10.04초**, UI heartbeat **49회**, 실제 트레이 Tooltip/상태 카운트다운 각각 **12종**으로 갱신됐다. 다른 두 Provider는 독립 완료했고 Gemini 중복 조회는 없었다. 이 검사는 **합성 지연의 응답성**이며 설치된 agy 1.3.2의 실제 계정 조회는 이번에 실행하지 않았다.
- 초기 두 번은 상태 하단 문구 검사에서 예상치 않은 한도 모드가 관측되어 실패했다. 당시 Click 경로를 기록하지 않아 원인은 **미확정**이다. 임시 호출 추적을 추가한 검사와 이를 제거한 최종 검사 모두 통과했으며, 상태 모드·알림 제품 코드는 수정하지 않았다. 임시 추적은 커밋하지 않았다.
- BalloonTipShown **17건** 관측은 시각적 알림 전달 보장이 아니다.

### 전후 PNG 비교

동일 시각·가짜 데이터·DPI로 생성한 원본 PNG를 열어 확인하고 SHA-256 및 픽셀 차이를 비교했다.

- **7개 SHA-256 동일:** `burger-time`, `full-throttle`, `holiday-midweek`, `holiday-weekend`, `provider-outage`, `note-truncated`, `account-quotas-exhausted`.
- **3개 의도한 변경:** `account-quotas`, `account-quotas-previous`, `account-quotas-note-truncated`. 세 Provider로 늘어나 한도 영역 안에 세로 스크롤바가 생긴다. 창 374×518 / 한도 viewport 342×214 logical px, 제목 기준선·박스 패딩·상태 전환은 유지했다.
- **2개 포커스 상태 차이:** `statistics`는 183픽셀, (49,238)~(199,271)의 탭 점선 테두리만 다르고 `measurement-dialog`는 652픽셀, (46,91)~(665,124)의 선택 상자 점선 테두리만 다르다. 내용·수치·배치는 같고 해당 두 제품 화면 코드는 main과 동일하다. 원본 PNG가 모두 해시 동일하다고 기록하지 않는다.
- 추가 `account-quotas-two-providers.png`는 latest main의 `account-quotas.png`와 SHA-256까지 동일하다. `account-quotas-gemini`·`account-quotas-gemini-previous`는 Gemini 박스 정상/이전 값과 마지막 행에 접근하는 스크롤을 확인한다.
- 전후 첨부: [before](docs/reviews/gemini-agy-refresh-20261009/before), [after](docs/reviews/gemini-agy-refresh-20261009/after). 모두 합성 데이터이며 실제 계정 수치·원문·EXE·DB를 올리지 않는다.
- 최종 로그·PNG: ignored `artifacts/gemini-agy-refresh-20261009/{baseline,after}/`. 초기 구현 로그는 아래 과거 기록과 별개다.

### 배포본 보존·미수행

기존 2.2.4 EXE는 정상 종료 뒤 검사를 진행했으며 검사 후 같은 파일을 다시 실행했다. FileVersion **2.2.4.0**, SHA-256 **AF255AE4AD8642F149D07AFB6E819CC18CE3EBD76437182A4413A3204F62C06B**는 작업 전후 동일하다. 배포 EXE·사용자 DB·자동 시작 등록·공개 조사 자동화는 변경하지 않았다.

**이번 미수행:** agy 1.3.2 실제 계정 조회, Mac build/native smoke·팝오버 Gemini 박스, 실제 네트워크 전체 단절·재연결·절전·재부팅·재로그인, 장시간 자동 조회, hooks/MCP·모든 계정/플랜 조합, 시각적 알림 전달. 사용자 Mac에서 동일 PR HEAD의 agy **1.3.2 실제 조회와 팝오버 Gemini 박스**를 확인받기 전에는 병합하지 않는다.

## 초기 Windows 검증 (2026-10-08 KST·과거 결과)

SDK **10.0.401**. 기준 main과 변경 소스에서 `build.ps1`을 실행했다. 두 빌드 모두 **경고 0 / 오류 0**. WinExe 자체 검사·smoke는 `Start-Process -Wait -PassThru`의 실제 `ExitCode`로 확인했다.

| 검사 그룹 | 기준 main | 변경 후 | 차이 |
|---|---:|---:|---:|
| 자동 시작 판정 | 229 | 229 | 0 |
| Schedule/DST | 242,363 | 242,363 | 0 |
| US holidays | 1,399 | 1,399 | 0 |
| 공식 상태 파서 | 96 | 96 | 0 |
| Monitor/알림 정책 | 119 | 119 | 0 |
| SQLite/통계 | 132 | 132 | 0 |
| 공휴일 캐시 | 2 | 2 | 0 |
| 기존 한도 파서/정책 | 143 | 143 | 0 |
| Gemini 파서 | 0 | 85 | +85 |
| Gemini CLI 보호/프로토콜 (합성) | 0 | 62 | +62 |
| 한도 Monitor/캐시/재시작 | 80 | 98 | +18 |
| 경로/표시/CLI 후보 | 37 | 39 | +2 |
| 공통 패널 문구/톤 | 247 | 259 | +12 |
| 기록/피드백 | 40 | 40 | 0 |
| 트레이 | 6,130 | 6,130 | 0 |
| 기존 CLI 프로토콜 | 49 | 49 | 0 |
| **Windows 전체** | **251,066** | **251,245** | **+179** |

- Windows 자체 검사 종료 코드 **0**, 감소한 그룹 없음. 공통 검사 전체 **244,837건** 통과했다. 목록은 `SharedTestSuite` 한 곳에만 등록했다.
- Windows native smoke: **241 assertions**, 종료 코드 **0** (기준 220, +21). 상태·통계·한도·기록·공휴일·복귀·누락 CLI·이전 값·스크롤·리소스 정리를 확인했다. 자동 시작 변경 옵션은 사용하지 않았다.
- BalloonTipShown 이벤트 17건을 관측했다. 이는 실제 알림이 사용자에게 시각적으로 노출됐다는 보장은 아니다.
- PNG SHA-256 비교: `burger-time`, `full-throttle`, `holiday-midweek`, `holiday-weekend`, `measurement-dialog`, `provider-outage`, `statistics` **7개 동일**. `account-quotas`, `account-quotas-previous`, `account-quotas-exhausted` **3개만 의도한 박스 배치 변경**. `account-quotas-gemini`, `account-quotas-gemini-previous` **2개 추가**해 정상·이전 값을 직접 확인했다.
- 실제 설치된 agy **1.3.1**: 새 Release DLL의 Gemini `ReadAsync`만 호출해 **5시간·주간 두 창**, 정상 종료와 위 0턴/0토큰 조건 통과를 확인했다. 약 9.57초 소요, 응답 수신 `2026-10-08T13:39:53Z`. 실제 잔여율·계정 정보는 저장소에 올리지 않는다. 다른 Provider 계정 조회·사용자 DB 저장은 하지 않았다.
- 로그·PNG는 로컬 ignored `artifacts/gemini-agy-20261008/baseline/`, `after/`에 있다. 실계정 probe도 ignored artifact 안에만 있으며 앱 의존성을 추가하지 않았다.

**미수행:** Mac 빌드·native smoke, 실제 네트워크 전체 단절·재연결, 실제 절전·재부팅·재로그인, 새 앱의 장시간 자동 조회, 실제 hooks/MCP 설정의 모든 조합, 모든 플랜·계정, 새 AGY 버전, 알림의 시각적 전달. 모델 소비를 유도하는 검사는 하지 않았다.

## 변경 파일과 Mac 확인 요청

- 공통 원본: `AccountQuotaModels.cs`, `AccountQuotaClient.cs`, `AccountQuotaParsers.cs`, `AccountQuotaMonitor.cs`, `MacCliPaths.cs`, `QuotaPanelModel.cs`, `UsageStore.cs`(주석만).
- 공통 검사: `GeminiQuotaTests.cs`, `GeminiQuotaClientTests.cs` 신규; `AccountQuotaMonitorTests.cs`, `PortablePlatformTests.cs`, `PanelModelTests.cs`, `SharedTestSuite.cs` 수정.
- Windows 전용: `AccountQuotaView.cs`, `AccountQuotaUiChecks.cs`, `SmokeTest.cs`, `UIRegressionChecks.cs`(세 박스 통합 검사), `TrayApplicationContext.cs`(Refresh Tooltip 범위 설명).
- 문서: 이 문서와 `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`, `ACCOUNT_QUOTAS.md`.
- `Mac/`, `MACOS_PORT.md`, `MACOS_UI_PLAN.md`, `AGENTS.md`, 버전·의존성·dist는 수정하지 않았다.

공통 원본 변경이므로 동일 PR HEAD에서 Mac 담당의 `Mac/build.sh`와 native `--smoke-test` 결과를 PR 코멘트로 받기 전에는 병합하지 않는다. 확인할 항목:

1. 최신 PR HEAD에서 공통 검사 **251,070건**과 Mac Release/trimming 빌드, 기존 native smoke 통과 여부. 과거 244,837건은 이번 확인 기준이 아니다.
2. Mac의 **한도 항상 표시** 방식을 유지한 세 Provider 박스·Gemini 범위 문구·정상/이전/누락 한도·스크롤·짧은 화면 배치.
3. 기존 Claude/Codex 응답과 캐시 식별자 호환, Gemini 추가 캐시의 재시작·Provider별 실패 격리.
4. 설치·로그인된 **agy 1.3.2로 실제 독립 조회**하고 5시간·주간 응답·정상 종료·0턴/0토큰 조건을 확인한다. 1.3.1 이상·2.0 미만 안정 버전의 절대 경로, 1.3.1 미만·2.x·미설치·인증 필요 시 안전한 조회 불가 처리도 확인한다. raw 응답·계정 수치는 공개하지 않는다. 사용자 hooks/MCP 격리 제한은 Mac에도 같다.
5. 실제 Mac 계정 확인은 별도 사용자 허용 조건에서만 수행하고, raw 출력·OAuth 정보는 PR에 붙이지 않는다. 공통 자동 정책과 Mac Refresh·절전 복귀·연결 복구 연결도 구분해서 기록한다.

## 공식 근거

- [공식 `/usage` 명령](https://antigravity.google/docs/cli/commands/usage/), [Headless 설명](https://antigravity.google/docs/cli/headless/).
- [1.3.1 고정 공식 CHANGELOG](https://github.com/google-antigravity/antigravity-cli/blob/1.3.1/CHANGELOG.md): 1.1.11의 구조화된 로컬 `/usage`·모델 턴/대화 생성 없는 처리, 1.0.1의 usage refresh, 후속 timeout·headless 정리 변경. 공개 변경 기록과 이번 1.3.1 실제 계정 검증일은 구분한다.
- [1.3.2 고정 공식 CHANGELOG](https://github.com/google-antigravity/antigravity-cli/blob/1.3.2/CHANGELOG.md): 새 버전의 공개 기록은 실제 계정 조회 성공·독립 최신 데이터 보장과 구별한다.
- [공식 troubleshooting](https://antigravity.google/docs/cli/troubleshooting/): 자동 업데이트 차단 환경 변수.
- [플랜별 Antigravity 범위](https://antigravity.google/docs/plans/), [hooks](https://antigravity.google/docs/hooks), [MCP](https://antigravity.google/docs/mcp), [CLI settings](https://antigravity.google/docs/settings?tab=cli).
