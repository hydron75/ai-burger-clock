# 한도 조회 구조 재설계 — 조회원·실패 분류·로그인 상태

- **상태:** 설계 확정(2026-11-25, 메인). 독립 검토(opus) 지적 2건 차단·11건 중요·6건 경미를 반영했다. 단계별 PR은 6절 순서로 진행한다.
- **진행:** P0 특성 검사 #177. P0에서 확인한 실제 동작에 맞춰 3.3-4의 송신 항목 2개를 정정했다.
- **추가(2026-11-26):** Antigravity 웹 조회원(4.7, 단계 P2.5)과 결정 기록. 페이지 probe 결과를 근거로 적었다.
- **범위:** 계정 한도 조회(`AccountQuota*`, Gemini 웹, Antigravity 웹, Claude 로그인 확인)와 그 표시·알림·송신 연결.
- **범위 밖:** 공식 서비스 상태(`StatusMonitor`, xAI 피드)와 사이트 파서 통합(9절).

## 1. 왜 바꾸는가

2026-11-24 Windows의 Claude 한도가 20시간 동안 "한도 응답 없음"으로만 표시됐다. 원인은 CLI 로그인 만료였지만, 앱은 로그인 만료·응답 형식 변경·CLI 오류 종료를 같은 문구로 보여 줬다. #171에서 Claude 전용 로그인 확인 루프를 붙여 해결했다.

같은 형태의 추가가 반복되고 있다.

| 추가 | 방식 | 결과 |
|---|---|---|
| Gemini 웹 조회(#163·#164·#165) | 모니터에 전용 루프·상태·캐시·주기 정책 추가 | 조회 루프 둘, `QuotaState.GeminiWeb` 전용 필드 |
| Claude 로그인 확인(#171) | 전용 루프·요청 큐·스냅샷 주입 | 조회 루프 셋, `QuotaState.ClaudeAuth` 전용 필드 |
| Gemini 송신(#170) | 송신 연결부에 Gemini 전용 변화 비교 | 송신 규칙이 Provider별로 갈림 |

Provider나 경로가 바뀔 때마다 모니터·상태·표시·송신·양쪽 호스트에 특수 분기가 생긴다. 이 문서는 이를 한 틀로 묶는다.

## 2. 현재 구조

- **`AccountQuotaMonitor`(공통):** Provider마다 `RunAsync` 루프가 하나다. `QuotaNames.IsQueryEnabled`가 참인 Codex·Claude·xAI만 돈다. 실패는 예외 형식에 따라 한국어 문구로 바꿔 `QuotaState.Error`에 넣는다(`RefreshOnceAsync`).
- **`GeminiWebQuotaMonitor.cs`(공통, 같은 partial class):** Gemini 웹 전용이다.
  - 루프 `RunGeminiWebAsync`, 세대 번호, 켜기·끄기, 15분 고정 주기, 별도 캐시 키를 가진다.
  - `Succeeded`, 조기 초기화 기록, 조회 진단 로그를 내지 않는다.
  - 수동 요청은 `RequestGeminiWebRefresh`로 따로 받는다. 공용 `RequestRefresh`는 Gemini를 깨우지 않는다.
- **`AccountQuotaMonitor.ClaudeAuth.cs`(공통):** Claude 로그인 확인 루프와 요청 우선순위 큐를 둔다. `LatestClaudeAuth`를 스냅샷에 끼워 넣는다. 두 호스트가 `LatestClaudeAuth`로 만료 알림을 판정한다.
- **`AccountQuotaClient`(공통):** CLI 세 개의 실행과 프로토콜을 맡는다. 실패는 `InvalidDataException`, `FileNotFoundException`, `TimeoutException` 등으로 던지며, 메시지에 문구가 섞여 있다.
- **`QuotaNames.IsQueryEnabled`:** "CLI 경로가 있음"과 "회복·조기 초기화·대기 알림 대상"을 한 함수로 겸한다. 호출처는 다음과 같다.
  - 클라이언트(`AccountQuotaClient.cs:25,137,346`)
  - `MacCliPaths.cs:11`
  - `LiveQuotaProbe.cs:12`
  - `QuotaPanelModel.cs:82`
  - `QuotaRecoveryNotifications.cs:72`
  - `QuotaEarlyResetNotifications.cs:42`(진단 사유 `QueryDisabled`)
  - `QuotaPendingAlertPolicy.cs:10,17`
  - 모니터(`:90,106,159`)
- **`QuotaPanelModel`(공통):** Gemini 웹 상태와 Claude 로그인 상태를 전용 분기로 표시한다.
- **송신(`WindowsQuotaExportBridge`, Windows):** CLI는 조회 시작→완료 전이로 송신한다. Gemini만 값·상태 변화 비교로 송신한다.
- **호스트:** Gemini 메뉴(로그인·끄기·로그아웃)와 Claude 로그인 알림 메뉴를 각자 만든다.

## 3. 지켜야 할 동작(불변 조건)

재구성 단계(P2)는 아래를 바꾸지 않는다. P0 특성 검사로 하나씩 고정한다.

### 3.1 조회·이벤트

1. **`Succeeded`:**
   - 받아들인 실시간 응답마다 정확히 한 번 발생한다. Provider 게이트를 쥔 채, 상태 잠금 밖에서 발생한다.
   - 캐시·실패·취소에는 발생하지 않는다.
   - 유효한 응답이라도 캐시 저장이 취소되면 값은 유지하고, `Succeeded` 없이 `ContinuityId`를 올린다.
   - `ContinuityId`는 조회를 시작한 경우에만 올린다.
   - 처리기가 없으면 조기 초기화 결정은 `HostUnavailable`로 기록한다.
2. **시작:** CLI 조회원은 캐시 복원 뒤 `Changed`를 한 번 내고 즉시 조회한다.
3. **스냅샷:** `NextCheckUtc + 5분`을 지났거나 `LastSuccess − 5분`보다 이르면 `IsPrevious`를 덧씌운다.
4. **조회 주기(CLI):**
   - `AccountQuotaPolicy` 그대로다. 기본 1시간, 90% 초과, 100%, 리셋 전후 ±15분, 연속 실패 재시도(15→30→60분)를 적용한다.
   - xAI 최소 10분은 예약된 리셋 구간·진입 조회에만 적용한다.
   - CLI 루프는 깨어나면 조건 없이 조회한다.
5. **조회 주기(Gemini 웹):**
   - 15분 고정이다. 성공과 실패 모두 다음 15분에 조회한다.
   - 로그인 필요면 자동 조회를 멈춘다. 수동 요청이나 새로 켤 때만 다시 시작한다.
   - 깨어나도 기한 확인 후에만 조회한다. 그래서 끄기로 깨워도 조회하지 않는다.
6. **수동·복귀 구분:**
   - 호스트는 수동 Refresh 때 `RequestRefresh(false)`와 Gemini 웹 요청을 각각 부른다. Mac smoke는 Gemini 웹 요청을 생략한다.
   - 복귀·네트워크 복구는 `RequestRefresh(true)`로 CLI만 대기열에 넣는다.
   - CLI 수동 요청은 진행 중인 조회가 있으면 버린다. Gemini 웹 수동 요청은 진행 중이어도 남겨 끝난 뒤 한 번 더 조회한다.
7. **켜기·끄기(Gemini 웹):**
   - 켜면 상태를 비운다. 값 없음, `ContinuityId` 1, 캐시 복원 없음이다.
   - 캐시 복원은 처음부터 켜져 있고 세대 번호가 0이며 아직 조회 전일 때만 한다.
8. **취소·종료:**
   - 웹 리더가 있으면 수명 취소는 비동기(`CancelAsync`)다. 끄기의 진행 중 조회 취소도 잠금 안에서 비동기로 시작한다.
   - 조회의 `finally`는 그 취소를 기다린 뒤 게이트를 놓는다.
   - await 뒤의 모든 상태 쓰기(값, 캐시 저장, `CacheError`, `IsRefreshing`)는 세대 번호를 확인한다.
   - `StopAsync`와 `Dispose`의 현재 분기를 유지한다.

### 3.2 로그인 확인(Claude)

1. **확인 시점:** 시작, 6시간마다, 한도 조회 실패 직후, 수동 Refresh다.
   - 실패 직후 요청이 직전 시도 15분 이내면 미루지 않고 버린다.
   - 수동과 회복 요청(Immediate)은 15분·6시간 제한을 무시한다.
   - 우선순위는 None < QuotaFailure < Immediate다. 제한에 걸린 요청이 대기 중인 Immediate를 낮추지 않는다.
2. **회복 확인:** 한도 조회가 성공했을 때, 최근 로그인 상태가 확인 불가이거나 그 성공 시각 기준으로 로그인이 필요하면 한 번 다시 확인한다. 같은 확인 결과로는 한 번만 요청한다.
   - 조회 후 요청은 조회가 시작됐고 취소되지 않은 경우에만, 게이트를 놓은 뒤 보낸다.
3. **확인 시각:** `CheckedAtUtc`는 확인을 시작한 시각이다. 그래서 "확인이 마지막 성공보다 새로울 때만 로그인 필요로 표시" 규칙이 맞게 동작한다.
4. **로그인 필요 판정:** 로그아웃이거나, 로그인 상태에서 종료 상한이 지난 경우(`RequiresLogin`)다. 만료 경고는 구독 로그인(`claude.ai`)에만 낸다.
5. **인증 방식:** 허용 목록의 값만 표시하고, CLI 원문은 쓰지 않는다.

### 3.3 표시·캐시·송신·개인정보

1. **Gemini 웹 값 표시:**
   - 박스는 로그인 필요와 형식 변경이면 값을 숨기고, 조회 실패면 이전 값으로 둔다.
   - 송신은 로그인 필요면 값을 빼고, 형식 변경과 조회 실패면 `previous`로 보낸다.
   - 로그인 필요가 되면 `LastSuccessfulCheckUtc`와 `NextCheckUtc`를 비운다.
   - 박스는 `IsWebReading`을 통과한 값만 보이고, Gemini의 조기 초기화 상세를 표시하지 않는다.
2. **CLI 값 표시:** 모든 실패에서 마지막 확인 값을 "이전 조회값"으로 흐리게 남긴다. Claude 로그인 필요도 같다.
3. **캐시:**
   - v1 JSON과 키(`AccountQuota.v1.{Provider}`, `AccountQuota.v1.GeminiWeb`)를 유지한다.
   - 웹 캐시는 기준점 0개, 정책 버전 0이다.
   - **`AccountQuota.v1.Gemini`(옛 agy 캐시)는 어떤 조회원도 읽거나 쓰거나 옮기지 않는다.**
4. **송신(스키마 v2):**
   - `webState`는 off, loginRequired, formatChanged, loadFailed, ok에 더해 **null(켜져 있지만 아직 이번 켜기 뒤 결과 없음, 캐시 복원 포함)** 상태가 있다.
   - null이면 Gemini 변화로 송신하지 않는다. 그래서 시작이나 캐시 로드는 송신을 만들지 않는다.
   - Gemini `nextCheckUtc`는 `webState`가 ok, formatChanged, loadFailed일 때 보낸다. 이전 값이 없어 `reading=none`이고 `rows`가 비어 있어도 포함한다. off, loginRequired, null이면 보내지 않는다(P0 #177로 확인).
   - 다른 Provider 완료와 Gemini 변화가 같은 처리 안에서 겹치면 한 번만 보낸다.
   - 다른 Provider가 완료될 때마다 Gemini 비교 기준을 갱신한다. Gemini 조회 중에도 그렇다.
   - 응답이 상태에 반영되기 전에 취소된 조회는 송신하지 않는다. 응답이 반영된 뒤 캐시 저장을 기다리는 중에 취소되면, CLI와 Gemini 웹 모두 이미 반영된 값으로 한 번 송신한다(P0 #177로 확인). 이 경우에도 `Succeeded`는 발생하지 않는다(3.1-1).
5. **로그아웃 순서:**
   - Windows: 대기 기록 저장 → 끄기 → 같은 `UsageStore`로 캐시 삭제 → 프로필 삭제 → 설정 저장. 실패하면 다음 시작 때 `Start()` 뒤에 다시 시도한다.
   - Mac: 끄기 → 캐시 삭제 → 진행 중 조회 대기 → 데이터 저장소 삭제.
   - 두 호스트 모두, 끄기가 먼저이고 같은 저장소 직렬화로 진행 중 저장이 삭제보다 앞서는 것에 기댄다.
6. **개인정보:** CLI 원문, stderr, 쿠키, 토큰, 이메일을 저장·로그·표시하지 않는다. 진단에는 규칙 코드만 남긴다.
7. **표시 틀:** 공통 문구와 톤, `LayoutKey` 구조, 상세 320 폭 틀을 유지한다. 문구는 공통에만 둔다.
8. **식별자:** `QuotaProvider`의 숫자 값과 이름을 유지한다(캐시·송신 식별자).

## 4. 목표 구조

### 4.1 실패 분류

예외 형식을 문구로 바꾸던 일을, 조회원이 원인을 분류해 돌려주는 방식으로 바꾼다.

```csharp
internal enum QuotaFailureKind
{
    LoginRequired,  // 로그인 없음·만료가 증거로 확인됨(웹 로그인 화면 이동, 로그인 확인 결과)
    NotInstalled,   // 공식 CLI 실행 파일 없음 또는 실행 불가 형식
    NotPermitted,   // 작업 폴더 생성·실행 권한 거부
    Timeout,        // 모니터 제한 시간 초과
    Unreachable,    // 프로세스 실행·응답 읽기·네트워크 실패, 웹 로드 실패
    Rejected,       // 응답은 왔으나 오류로 끝남. 원인 미상(로그인일 수도 있음)
    FormatChanged   // 응답은 정상 종료했으나 형식이 기대와 다름
}

internal sealed record QuotaFailure(QuotaFailureKind Kind, string Code);
```

**발생 위치별 분류표** (클라이언트 안에서 분류한다)

| 발생 위치 | 종류 | Code |
|---|---|---|
| 실행 파일 없음(`FindExecutable`), 실행 불가 형식(`CanExecute`) | NotInstalled | `cli.not-found`, `cli.not-executable` |
| 작업 폴더 생성의 `UnauthorizedAccessException` | NotPermitted | `cli.workdir-denied` |
| 프로세스 시작 실패, `Win32Exception`, 응답 읽기 `IOException` | Unreachable | `cli.start-failed`, `cli.read-failed` |
| 모니터 제한 시간 | Timeout | `timeout` |
| 0이 아닌 종료 코드 | Rejected | `cli.exit-nonzero` |
| Codex JSON-RPC 오류 응답 | Rejected | `codex.rpc-error` |
| Claude 결과가 로컬 `/usage` 성공이 아님, `usage_report` 없음(`limits:null` 포함) | Rejected | `claude.result-not-local`, `claude.no-usage-report` |
| 응답 줄 JSON 아님, 크기 제한 초과, 중복 결과 | FormatChanged | `cli.bad-line`, `cli.too-large`, `claude.duplicate-result` |
| 파서 거부(모르는 한도 종류, 필드 누락), 받은 값 검증 실패(`ValidateQuotaCache`) | FormatChanged | `parser.*`, `validate.*` |
| Gemini 웹: 로그인 화면 이동 / 형식 불일치 / 로드 실패 | LoginRequired / FormatChanged / Unreachable | `gemini.destination` / `gemini.{규칙}` / `gemini.load` |

- **로그인 필요는 추정하지 않는다.** Claude 로그인 만료 때 실제로 어떤 응답이 오는지는 미확인이다(8절). 그래서 `usage_report` 없음은 `Rejected`로 둔다. 표시를 로그인 필요로 올리는 것은 로그인 확인 결과가 있을 때뿐이다(3.2).
- **`Code` 기록 위치:** 기존 조회 진단 파일은 모르는 필드를 거부하므로 확장하지 않는다. `Code`는 상태와 상세 표시에만 두고 저장하지 않는다. Gemini 진단의 줄 수(`LineCount`)는 버린다.
- **옛 agy 분기:** `NotSupportedException` agy 분기(`AccountQuotaMonitor.cs:198`)는 도달하지 않는 코드라 P1에서 지운다.

**문구와 톤** (공통 모델, 조회원 유형별)

| 종류 | CLI 박스 문구 | Gemini 웹 박스 문구 | 톤 |
|---|---|---|---|
| (결과 없음) | 한도 조회 대기 | 한도 조회 대기 · Gemini 앱 사용량 | Muted |
| LoginRequired | `{CLI} 로그인 필요 · 터미널에서 {로그인 명령}` | 로그인 필요 · Gemini 앱 사용량 | Danger |
| NotInstalled | 공식 CLI를 찾지 못했습니다. 설치 경로를 확인하세요. | — | Muted |
| NotPermitted | 앱 작업 폴더 권한을 확인하세요. | — | Muted |
| Timeout | 한도 조회 시간 초과 | 한도 조회 시간 초과 | Muted |
| Unreachable | 한도 조회 실패 · 네트워크와 CLI 상태를 확인하세요. | 한도 조회 실패 · 네트워크와 로그인을 확인하세요. | Muted |
| Rejected | 한도 조회 거부 · CLI 로그인과 버전을 확인하세요. | — | Muted |
| FormatChanged | 형식 변경 · 조회 불가 | 형식 변경 · 조회 불가 | Muted |

"(결과 없음)"의 CLI 문구는 지금 "한도 조회 대기 · 공식 CLI 로그인 필요"다. 근거 없이 로그인을 단정하므로 P1에서 바꾼다.

### 4.2 로그인 상태

```csharp
internal enum QuotaAuthStatus { Unknown, SignedIn, SignedOut }

internal sealed record QuotaAuth(QuotaAuthStatus Status, DateTimeOffset CheckedAtUtc,   // 확인 시작 시각
    string? Method = null,                                                               // 허용 목록 값만
    DateTimeOffset? ExpiresNoLaterThanUtc = null);

internal interface IQuotaAuthProbe
{
    Task<QuotaAuth> ProbeAsync(DateTimeOffset startedAtUtc, CancellationToken cancellationToken);
}
```

- **확인 경로:** 공식 읽기 전용 명령만 쓴다. 인증 파일, 키체인, 쿠키는 읽지 않는다.
  - Claude: `claude auth status`(#171). `ClaudeAuthStatus`와 `ClaudeAuthCheck`를 이 형식으로 옮긴다.
  - Gemini 웹: 확인 명령이 없다. 조회 결과가 곧 로그인 증거다.
  - Codex·Grok: 공식 상태 명령이 있는지와 출력 형식이 미확인이다. P3에서 조사하고, 공식이며 읽기 전용이고 모델 호출이 없을 때만 붙인다.
- **확인 일정:** 3.2의 계약(시점, 버림과 우선순위, 회복 확인, 확인 시각)을 조회원 단위로 일반화한다. 일정은 조회 루프와 별개의 경량 루프로 돌고, 조회 게이트를 쓰지 않는다.
- **표시와 알림:**
  - 박스 상세에 "로그인 상태" 절을 둔다. 종료 상한이 있으면 7일 전부터 "로그인 만료 임박" 경고 줄을 보인다.
  - 알림 설정 키(`ClaudeAuthExpiryNotificationsEnabled`, `ClaudeAuthWarnedExpiryUtc`)는 유지한다. 종료 시점을 주는 Provider가 늘면 Provider별 기록 키를 추가한다.

### 4.3 조회원 인터페이스

```csharp
internal enum QuotaSourceMode { Active, Off, Unavailable }   // Unavailable: 정책상 조회 안 함(웹 리더 없는 Gemini)

internal sealed record QuotaSourceTraits(
    IQuotaSchedule Schedule,
    TimeSpan? Timeout,               // 모니터 제한 시간. CLI 40초(xAI 45초), 웹은 null(호스트 30초가 Unreachable로)
    bool EmitsObservations,          // Succeeded·회복·조기 초기화·조회 진단 대상
    bool DueCheckBeforeRead,         // 깨어나도 기한일 때만 조회(웹)
    bool StickyManualRefresh,        // 진행 중 수동 요청을 남김(웹)
    bool RefreshOnRecovery,          // 절전 복귀·네트워크 복구 때 조회(CLI)
    QuotaPresentationPolicy Presentation,
    QuotaExportMode Export,          // Completion / OnChange / None(Antigravity, P4 전까지)
    TimeSpan? ManualMinimumInterval = null,
    bool CancelLifetimeAsynchronously = false, // UI 취소 콜백을 사용하는 웹 조회원
    bool CanSetMode = false,         // 등록된 웹 조회원만 켜기·끄기 지원
    QuotaProvider? DependsOn = null); // 의존 조회원이 LoginRequired면 미룸

internal sealed record QuotaPresentationPolicy(
    IReadOnlySet<QuotaFailureKind> HideReadingInPanel,       // 웹: {LoginRequired, FormatChanged}, CLI: {}
    IReadOnlySet<QuotaFailureKind> HideReadingInExport,      // 웹: {LoginRequired}, CLI: {}
    bool ClearSuccessOnLoginRequired,                        // 웹: 참
    Func<QuotaReading, bool>? AcceptForDisplay,              // 웹: IsWebReading
    bool ShowEarlyResetDetail,                               // 웹: 거짓
    bool ApproximateReset = false);                          // Antigravity: 참(상대 시간에서 계산한 예상 시각, P2.5)

internal interface IQuotaCacheAdapter                        // 조회원별 캐시 읽기·쓰기·삭제·복원 규칙
{
    Task<QuotaCache?> ReadAsync(CancellationToken ct);
    Task WriteAsync(QuotaReading reading, DateTimeOffset at, IReadOnlyList<DateTimeOffset> anchors, CancellationToken ct);
    Task DeleteAsync(CancellationToken ct);
    bool RestoreOnStart(QuotaSourceMode mode);               // 웹: 처음부터 켜져 있을 때만
}

internal interface IQuotaSource
{
    QuotaProvider Provider { get; }
    QuotaSourceTraits Traits { get; }
    IQuotaCacheAdapter Cache { get; }
    IQuotaAuthProbe? Auth { get; }
    Task<QuotaReadResult> ReadAsync(DateTimeOffset nowUtc, TimeZoneInfo zone, CancellationToken cancellationToken);
}

internal sealed record QuotaReadResult(QuotaReading? Reading, QuotaFailure? Failure);  // 둘 중 정확히 하나
```

- **받은 값 검증:** 모니터가 `Reading`을 받으면 조회원과 관계없이 지금처럼 검증한다(`UsageStore.ValidateQuotaCache`, 웹은 `IsWebReading`). 실패는 `FormatChanged`/`validate.*`로 바꾼다.
- **캐시:** 키 이름이 아니라 어댑터가 저장 위치를 정한다.
  - CLI 어댑터는 `AccountQuota.v1.{Provider}`에 기준점과 함께 쓴다.
  - 웹 어댑터는 `AccountQuota.v1.GeminiWeb`에 기준점 없이 쓰고, 웹 전용 검증을 한다.
  - `UsageStore`는 키를 직접 받는 내부 API를 노출하고, `SaveQuotaAsync`가 Provider로 키를 만드는 경로는 CLI 어댑터 안으로 숨긴다.
  - Gemini 옛 기준점 재구성 규칙(`AccountQuotaMonitor.cs:135-137`)은 실제 운영에서 도달하지 않는다. P2에서 지우고, 옛 키를 건드리지 않는다는 검사(`GeminiPollingPolicyTests`)는 유지한다.
- **주기 정책:**

```csharp
internal interface IQuotaSchedule
{
    QuotaCheckPlan Next(DateTimeOffset nowUtc, QuotaReading? known, QuotaFailure? lastFailure,
        int consecutiveFailures, IReadOnlyList<DateTimeOffset> anchors);   // null 계획 = 자동 조회 중지
}
```

- **주기 정책 구현:**
  - `UsageBasedSchedule(provider)`: `AccountQuotaPolicy`의 계획 계산, 실패 재시도, 기준점 병합을 그대로 감싼다. xAI 최소 간격도 그대로다.
  - `FixedSchedule(15분, stopOnLoginRequired: true)`: Gemini 웹. 로그인 필요면 null을 돌려준다.
  - 두 정책 모두 진단용 `QuotaCheckPlan.Reason`과 `SelectedResetUtc`를 지금처럼 낸다.
- **조회원 구현:**
  - `CliQuotaSource`(Codex·Claude·xAI): 기존 `AccountQuotaClient` 프로토콜을 감싸고, 4.1 표대로 분류한다.
  - `GeminiWebQuotaSource`: `IGeminiUsagePageReader`와 `GeminiUsagePage.Parse`를 감싼다. 모니터의 시각과 시간대를 받는다.
  - `UnavailableQuotaSource`: 웹 리더가 없는 호스트의 Gemini다. 항상 `Unavailable`이다.
- **`IsQueryEnabled` 대체:**
  - 정적 함수 `QuotaNames.HasCliRoute(provider)`로 이름만 바꿔 유지한다. 클라이언트, `MacCliPaths`, `LiveQuotaProbe`가 쓰며, `MacCliPaths.Candidates(Gemini)`는 계속 빈 목록이다.
  - 회복·조기 초기화·대기 알림 정책에는 대상 여부를 인자로 넘긴다(모니터가 `EmitsObservations`로 채움). 진단 사유 `QueryDisabled`는 유지한다.
  - 모니터와 표시는 `Mode`를 본다.
- **등록:** 호스트가 시작할 때 조회원 목록을 넘긴다. 기존 생성자 서명은 호환 팩토리로 남겨, 검사와 Mac smoke 호출부를 한꺼번에 바꾸지 않아도 되게 한다. 플러그인 탐색과 DI는 쓰지 않는다. Provider당 조회원은 하나다.

### 4.4 상태 모델

```csharp
internal sealed record QuotaState(QuotaProvider Provider, QuotaReading? Reading = null,
    DateTimeOffset? CheckedAtUtc = null, DateTimeOffset? LastSuccessfulCheckUtc = null,
    DateTimeOffset? NextCheckUtc = null, bool IsRefreshing = false, bool IsPrevious = false,
    string CacheError = "", long ContinuityId = 0,
    QuotaSourceMode Mode = QuotaSourceMode.Active,     // 끝에 선택 인자로 추가(위치 인자 호환)
    QuotaFailure? Failure = null, QuotaAuth? Auth = null,
    bool HasResultSinceModeChange = false);            // 송신 webState null 판정
```

- `Error` 문자열과 `GeminiWeb`, `ClaudeAuth` 전용 필드는 마이그레이션 단계에서 계산 속성으로 남겼다가, 호출부를 옮긴 뒤 지운다(6절).
- **Gemini `webState` 계산:**

| 조건 | webState |
|---|---|
| `Mode == Off` | off |
| `Mode == Active`, `HasResultSinceModeChange` 거짓(캐시 복원 포함) | **null** |
| `Failure.Kind == LoginRequired` | loginRequired |
| `Failure.Kind == FormatChanged` | formatChanged |
| `Failure.Kind ∈ {Unreachable, Timeout, Rejected, NotInstalled, NotPermitted}` | loadFailed |
| 실패 없음 | ok |

### 4.5 모니터

- **조회 루프:** 조회원마다 같은 루프 하나를 돈다. 흐름은 캐시 복원(어댑터 규칙) → 대기 → 기한 확인(`DueCheckBeforeRead`) → 조회 → 검증 → 상태 갱신 → 캐시 쓰기 → (`EmitsObservations`면) `Succeeded`, 조기 초기화 기록, 조회 진단이다. 3.1의 취소·세대 규칙을 공통 루프에 그대로 옮긴다.
- **로그인 확인:** `Auth`가 있는 조회원마다 경량 루프를 돈다(3.2).
- **공개 API:**
  - `Snapshot()`, `Start()`, `StopAsync()`, `Dispose()`: 그대로.
  - `RequestRefresh(bool queueWhileRefreshing)`: 그대로. `RefreshOnRecovery` 조회원만 깨우고, 수동이면 로그인 확인 Immediate를 함께 요청한다.
  - `RequestSourceRefresh(QuotaProvider)`: `RequestGeminiWebRefresh`를 일반화한다. `StickyManualRefresh` 규칙을 따른다. 호스트의 수동·복귀 구분은 지금처럼 호스트가 정한다.
  - `SetMode(QuotaProvider, QuotaSourceMode)`는 `CanSetMode` 조회원에 적용한다. 세대 번호 증가, 진행 중 조회의 비동기 취소, 켤 때 상태 비우기(3.1-7)를 포함한다. CLI나 미등록 조회원에는 예외를 반환한다.
  - `ClearCacheAsync(QuotaProvider)`: 로그아웃용이다.
    - `Mode ≠ Active`와 세대 번호 증가를 먼저 요구한다.
    - 저장소 직렬화 순서상 이미 받아들인 진행 중 저장보다 뒤에 실행된다.
    - 모니터가 돌지 않아도 동작한다.
    - 두 호스트의 로그아웃 순서(3.3-5)를 바꾸지 않는다.
  - `LatestAuth(QuotaProvider)`: `LatestClaudeAuth`를 일반화한다. 호스트 알림은 P3에서 이 API로 옮긴다.

### 4.6 표시·알림·송신

- **`QuotaPanelModel`:** Provider별 분기를 `Mode`, `Failure`, `Auth`, `Presentation` 기준의 한 경로로 정리한다. 아래 항목은 조회원 유형이나 주기 정책별로 남지만, 문구는 계속 공통에 둔다.
  - 범위 설명
  - 조회 주기 줄과 "참고" 줄
  - 수신 시각 설명
  - 결과 없음 문구
  - 로그인 명령
- **회복·조기 초기화·대기 알림:** 인자로 받은 대상 여부를 쓴다. 결과는 지금과 같다(Gemini 웹 제외).
- **송신:** 연결부가 `Traits.Export`를 본다.
  - `Completion`: CLI 조회가 완료될 때마다 보낸다.
  - `OnChange`: Gemini가 `webState`, reading, 행이 바뀔 때만 보내고, null이면 보내지 않는다.
  - 3.3-4의 겹침·기준 갱신·취소 규칙은 그대로다.

### 4.7 Antigravity 웹 조회원 (P2.5)

Gemini 앱과 Antigravity는 한도가 따로다. 2026-11-26 사용자가 Gemini 박스에 Antigravity "Gemini Models" 묶음의 5시간·주간 한도를 함께 자동 표시하기로 정했다(5절 결정 기록). P2의 조회원 구조 위에 조회원 하나를 더하는 방식으로 만든다.

**근거: 페이지 probe (2026-11-26, 제품 코드 변경 없음, 계정 값 미기록)**

- 대상: `https://antigravity.google.com/remote?settingsOpen=true&settingsScreen=Models`. URL만으로 Models 설정 화면이 열린다.
- 로그인: 새 전용 프로필에서 `gemini.google.com`에 한 번 로그인하면, 같은 프로필로 추가 로그인 없이 열린다(Windows·Mac). 최종 위치는 `antigravity.google.com/remote`다.
- 로컬 호스트: Antigravity 데스크톱 앱과 `agy`가 없어도 Models 화면이 열린다(Windows).
- 렌더링: 페이지가 받은 뒤 브라우저 안에서 그리는 화면이다. 문서 가시성이 `hidden`이면 30초 동안 값이 나타나지 않았다.

| OS | 방식 | `visibilityState` | 값 2줄 | 사용자 눈 |
|---|---|---|---|---|
| Windows | 보이는 창 | visible | 약 4.2초 | 보임 |
| Windows | V1 2.9.6 숨김 창 | hidden | 30초 미관찰 | 안 보임 |
| Windows | **V2 숨김 호스트 + 컨트롤러 `IsVisible = true`** | visible | 3.7초 | 안 보임 |
| Windows | V3 화면 밖 창 / V4 V3 + occlusion 끄기 인수 | hidden | 30초 미관찰 | 안 보임 |
| Mac | 보이는 창 | visible | 4.0초 | 보임 |
| Mac | M1 현재 Gemini reader(`orderOut`) / M2 화면 밖 창 | hidden | 30초 미관찰 | 안 보임 |
| Mac | M3a 화면 안 1×1 창 | visible | 3.5초 | 안 보임 |
| Mac | **M3b `alphaValue` 0 창** | visible | 3.5초, 앱 비활성 상태 4.0초 | 안 보임 |

- 문구 형식(숫자는 #): 텍스트 순서는 라벨 → 설명 문장 → 남은 비율이다.

```
Gemini Models
Weekly Limit Remaining
You have used some of your weekly limit, it will fully refresh in # days, ## hours.
##%
Five Hour Limit Remaining
You have used some of your #-hour limit, it will fully refresh in # hours, ## minutes.
##%
```

- 다른 묶음: "Claude and GPT models" 등. 읽지 않는다.
- 사용자 확인(Mac 1·2차): 창, Dock, 점, 메뉴바 이상은 보이지 않았다. 2차 측정 때 앞의 앱이 실제 전체 화면이었는지는 확인되지 않았다(로그의 전체 화면 표시값 false).
- 로그아웃 상태의 페이지 동작(로그인 화면 이동 여부)은 시험하지 않았다(8절).

**식별자와 열거형 영향**

- `QuotaProvider.Antigravity`를 열거형 끝에 추가한다(3.3-8, 기존 숫자 값 유지). `QuotaNames.For(Antigravity)`는 `"Antigravity"`다.
- `QuotaNames.HasCliRoute(Antigravity)`는 거짓이다. `MacCliPaths`의 후보도 비어 있다.
- 모니터 상태와 `Snapshot()`에는 Antigravity 상태가 들어간다(5개). 박스와 송신은 4개 그대로다. 그래서 공통에 두 목록을 둔다.
  - `QuotaProviders.All`: 열거형 전체. 모니터 내부 사전, 캐시, 진단이 쓴다.
  - `QuotaProviders.Boxes`: Codex, Claude, Gemini, xAI. 박스 구성, 송신, 알림, 툴팁, 최근 조기 초기화 표시가 쓴다.
- 제품 코드의 `Enum.GetValues<QuotaProvider>()`는 모두 둘 중 하나로 바꾼다. 2026-11-26 기준 위치는 다음과 같다. PR에서 `grep`으로 남은 곳이 없음을 확인한다.
  - `QuotaPanelModel.Sections`(상태마다 박스를 만듦 → `Boxes` 기준, Antigravity 상태는 Gemini 박스로 넘김)
  - `WindowsQuotaExportSnapshot.Serialize`(`states.Count != 4` 검사와 Provider 순회 → `Boxes` 기준)
  - `TrayApplicationContext.cs:210,426,430`, `Mac/MacApplication.cs:312,771`, `Mac/MacQuotaNotifications.cs:162`, `QuotaEarlyResetNotifications.cs:31`, `LiveQuotaProbe.cs:10`, `Mac/MacQuotaLayoutKeySmoke.cs:88`(smoke)
  - 모니터 내부 사전(`AccountQuotaMonitor.cs:36-45`)은 `All`, 루프 시작(`:101`)은 P2 조회원 목록을 따른다.
- `QuotaProvider`에 대한 `switch`(예: `WindowsNotificationRequest.cs:25`, `QuotaNames.For`)는 Antigravity 분기를 더하거나, `Boxes` 대상만 들어온다는 전제를 검사로 고정한다. 알림 매핑은 Antigravity를 받지 않는다(`EmitsObservations` 거짓).
- 검사의 열거형 순회는 P2.5 PR에서 하나씩 보고, CLI 호출 수를 세는 검사는 `HasCliRoute` 대상만 세도록 바꾼다. 바꾼 검사 수를 PR에 적는다.

**송신 제외 (P4 전까지)**

- `WindowsQuotaExportBridge`는 P2.5 시점에도 Provider별 분기다(`Traits.Export`는 P4에서 읽는다). 그래서 P2.5에서 브리지가 Antigravity 상태 변화로 송신하지 않도록 명시적으로 거른다. `QuotaExportMode.None`은 P4의 목표 값이다.
- 검사: Antigravity 조회 성공·실패·켜기·끄기 뒤 송신 횟수 0, 다른 Provider 송신 바이트가 P2.5 전과 같음(P0 하네스).

**캐시와 받은 값 검증**

- 웹 어댑터로 `AccountQuota.v1.AntigravityWeb`에 기준점 0개, 정책 버전 0으로 쓴다. 복원 규칙은 Gemini 웹과 같다(처음부터 켜져 있을 때만).
- Gemini 판정(`GeminiUsagePage.IsWebReading`, `UsageStore` 검증의 Gemini 키 비교)을 그대로 쓰지 않는다. Provider와 ID가 달라 값이 늘 숨겨지고 저장도 실패하기 때문이다. Antigravity 전용 판정을 둔다.
  - `AntigravityUsagePage.IsWebReading`: Provider Antigravity, 한도 2개, ID `antigravity-web:five-hour`·`antigravity-web:weekly`, `WindowMinutes` 300·10080, `UsedPercent` 0–100, 초기화 시각은 UTC.
  - 초기화 시각 없음은 `UsedPercent == 0`일 때만 받는다.
  - 웹 어댑터는 조회원별 판정 함수를 받아 저장·복원 때 쓴다. `UsageStore`의 키 비교에 Antigravity 키를 더한다.
- 한도 라벨은 공통 상수로 두고, Gemini 앱 줄과 구분되게 짓는다.

**조회원과 리더**

- `AntigravityWebQuotaSource`: `IAntigravityUsagePageReader`와 `AntigravityUsagePage.Parse`(공통)를 감싼다. 모니터의 시각과 시간대를 받는다.
- **리더 결과:** `AntigravityUsagePageLoad(Uri? FinalUri, string? Text, string? VisibilityState, string? DocumentLanguage, bool TimedOut)`. `Text`는 아래 "읽을 영역"의 텍스트만 담는다. 페이지 전체 텍스트를 넘기지 않는다.
- **세션:** Gemini 웹과 같은 프로필·데이터 저장소를 쓴다(Windows WebView2 사용자 데이터 폴더, Mac `WKWebsiteDataStore`). 별도 로그인 창은 없다. 로그인은 기존 Gemini 로그인 메뉴로 한다.
- **Windows 리더:** 2.9.6의 숨김 호스트 창을 쓰고, 이 조회의 컨트롤러에만 `CoreWebView2Controller.IsVisible = true`를 지정한다. 브라우저 인수는 바꾸지 않는다(같은 프로필의 환경 옵션 일치 문제). 로그인 창과 Gemini 앱 읽기는 지금 방식 그대로다. 프로필 사용은 기존 직렬화(`WindowsGeminiWebSession`)를 따르고, 제한 시간은 프로필을 잡은 뒤부터 잰다.
- **Mac 리더:** 읽는 동안만 투명 창을 띄우고, 읽기가 끝나면 바로 해제한다. 설정은 `alphaValue` 0, `statusBar` 레벨, `ignoresMouseEvents`, `canJoinAllSpaces`·`transient`·`ignoresCycle`, 활성화 없음이다.
  - **`fullScreenAuxiliary`는 쓰지 않는다.** 대신 `MacScreenGuard`의 화면 판정(창 이름·크기만 보고, 화면은 캡처하지 않음)을 제품 공용으로 옮겨, 전체 화면 앱이 덮지 않은 디스플레이에 창을 둔다. 모든 디스플레이가 덮였으면 읽지 않고 미룬다. MACOS_PORT 39절 증상은 22회 반복 뒤에 재발했으므로, 매시간 전체 화면 위에 창을 여는 일은 처음부터 피한다.
  - 이 설정은 probe의 M3b와 다르다. 구현 PR에서 앱 비활성 상태로 값이 나오는지 다시 확인한다.
  - Gemini 앱 리더는 바꾸지 않는다.
- **대기:** 탐색 완료 뒤 250ms마다 읽을 영역을 찾고, 찾으면 바로 끝낸다. 탐색 완료 뒤 최대 20초, 호스트 전체 제한 30초다. probe에서는 4초 안팎이었다.

**읽기 전 확인(미루기)**

- `IQuotaSource`에 `QuotaReadiness CheckReadiness()`(Ready / Deferred)를 더한다. 기본은 Ready다. 모니터는 기한 확인 단계에서, `IsRefreshing`과 `NextCheckUtc`를 바꾸기 전에 이것을 부른다. 그래서 4.3의 "값 또는 실패 정확히 하나" 계약은 그대로다.
- Deferred면 상태를 바꾸지 않고 5분 뒤 다시 기한을 확인한다. 기한이 지난 채로 남으므로 3.1-3 규칙대로 값이 "이전 조회값"으로 흐려진다. 잠금이 풀리면 5분 안에 읽는다. 따로 깨우는 이벤트는 두지 않는다.
- Deferred 조건:
  - Windows는 세션 플래그가 잠금(0)일 때만 미룬다. API 실패·UNKNOWN·기타 값은 Ready로 처리한다.
  - Mac: 화면 잠금, 디스플레이 잠자기, 모든 디스플레이가 전체 화면 앱에 덮임.
  - 공통 루프는 `DependsOn`으로 지정한 조회원의 실패가 `LoginRequired`이면 미룬다. Antigravity의 의존 조회원은 같은 세션을 쓰는 Gemini 웹이다. 상태 줄에는 Gemini 로그인이 필요하다는 공통 문구를 보인다.
- 수동 Refresh도 Deferred면 읽지 않는다.

**파서 (`AntigravityUsagePage.Parse`, 공통)**

- **읽을 영역(리더가 DOM에서 고름):** 텍스트가 정확히 "Gemini Models"인 요소에서 위로 올라가며, `Weekly Limit Remaining`과 `Five Hour Limit Remaining`을 모두 포함하는 가장 가까운 조상을 고른다. 그 조상의 `innerText`가 읽을 영역이다. 다른 묶음은 이 조상 밖에 있으므로 같은 라벨이 다른 묶음에 있어도 섞이지 않는다.
- **영역 안 규칙:** "Gemini Models"와 두 라벨이 각각 한 번만 있어야 한다. 각 라벨의 구간은 그 라벨부터 다음 라벨(또는 영역 끝)까지다.
- **남은 비율:** 구간 안의 `^\d{1,3}%$` 줄이 정확히 하나여야 한다. 0–100 정수만 받는다. `UsedPercent = 100 − 남은 비율`이다.
- **초기화 기간:** 구간 안에서 `fully refresh in` 뒤의 기간만 읽는다. 문장의 다른 표현은 보지 않는다.
  - 단위는 `day(s)`, `hour(s)`, `minute(s)`, 성분은 1–2개, 큰 단위부터, 같은 단위 반복 없음이다.
  - 상한은 5시간 한도 5시간, 주간 한도 7일이다. 넘으면 형식 변경이다.
- **예상 초기화 시각:** 표시 정밀도는 가장 작은 단위다(분이면 1분, 시간이면 1시간, 일이면 1일). 시각은 `조회 시각 + 기간 + 정밀도/2`로 저장하고 분 단위로 자른다. 박스와 상세는 "약"을 붙이고 분까지만 보인다(`QuotaPresentationPolicy.ApproximateReset`). 경과 판정(`QuotaPanelModel.Tone`)은 `ApproximateReset`이면 저장 시각 + 30분이 지나야 경과로 본다. 일 단위 정밀도는 주간 한도가 하루 넘게 남았을 때만 나오므로, 경과 판정 무렵에는 시간·분 단위로 다시 읽힌다.
- **기간 문장이 없을 때:** 남은 비율이 100이면 초기화 시각 없음으로 받는다. 그 밖에는 형식 변경이다. 100%일 때의 실제 문구는 미확인이다(8절).
- **판정 순서(앞이 우선):**

| 순서 | 조건 | 종류 / Code |
|---|---|---|
| 1 | 최종 위치가 Google 로그인 화면 | LoginRequired / `antigravity.destination` |
| 2 | 탐색 실패, 최종 위치가 `antigravity.google.com/remote` 아님 | Unreachable / `antigravity.load` |
| 3 | 읽을 영역이 없고 `DocumentLanguage`가 `null`이 아닌 값이며 `en`으로 시작하지 않음. 언어가 `null`이면 다음 단계로 진행 | FormatChanged / `antigravity.language` |
| 4 | 대기 끝에 `VisibilityState == "hidden"` | Unreachable / `antigravity.not-visible` |
| 5 | 읽을 영역 없음 | Unreachable / `antigravity.not-rendered` |
| 6 | 영역은 있으나 위 규칙 위반 | FormatChanged / `antigravity.{규칙}` |

- **개인정보:** 페이지 원문, 이메일, 요금제 문구는 저장·로그·표시하지 않는다. 리더는 읽을 영역 밖의 텍스트를 넘기지 않는다. 진단에는 규칙 코드만 남긴다.

**조회 특성 (`QuotaSourceTraits`)**

| 항목 | 값 | 이유 |
|---|---|---|
| `Schedule` | `FixedSchedule(1시간, stopOnLoginRequired: true)` | 약관 위험을 줄이려고 Gemini 웹(15분)보다 드물게 읽는다 |
| `Timeout` | null(호스트 30초) | 웹 조회원 공통 |
| `EmitsObservations` | 거짓 | 회복·조기 초기화·대기 알림 대상이 아니다 |
| `DueCheckBeforeRead` | 참 | 웹 조회원 공통 |
| `StickyManualRefresh` | 참 | 수동 Refresh 때 `RequestSourceRefresh(Antigravity)`를 함께 부른다 |
| `RefreshOnRecovery` | 거짓 | 복귀 때 따로 읽지 않는다 |
| `CancelLifetimeAsynchronously` | 참 | WebView 취소 콜백이 UI 스레드를 쓸 수 있다 |
| `CanSetMode` | 참 | 사용자가 별도로 켜고 끈다 |
| `DependsOn` | Gemini | 공용 세션이므로 Gemini 로그인 필요 상태에서 읽지 않는다 |
| `Presentation` | 로그인 필요·형식 변경이면 값 숨김, `AcceptForDisplay = AntigravityUsagePage.IsWebReading`, `ShowEarlyResetDetail` 거짓, `ApproximateReset` 참 | Gemini 웹과 같은 숨김 규칙에 Antigravity 판정 |
| `Export` | `None`(P4 목표 값, P2.5에서는 브리지가 직접 거름) | 사이트를 먼저 바꿔야 한다(6.2 P4) |

- **수동 Refresh 최소 간격:** 마지막 조회 시작 뒤 10분 안의 수동 요청은 버린다. 트레이 Refresh를 여러 번 눌러도 Antigravity 페이지를 반복해서 열지 않게 하려는 것이다. 새로 켤 때와 Gemini 재로그인 직후 요청은 이 제한을 받지 않는다.

**켜기·끄기·로그인·로그아웃**

- 설정 키를 따로 두고 **기본값은 꺼짐**이다. 사용자가 직접 켜야 조회를 시작한다.
- 실제 `Mode`는 "Gemini 웹이 켜짐 그리고 Antigravity 설정이 켜짐"일 때만 Active다. 모니터에 넘기는 `SetMode`는 두 설정이 바뀔 때 호스트가 계산해 부른다.
- Gemini 웹을 끄면 Antigravity는 Off가 되지만 Antigravity 설정은 남는다. Gemini 웹을 다시 켜면 설정대로 다시 Active가 된다.
- Gemini 웹이 꺼져 있으면 메뉴 항목은 비활성이고, Gemini 조회를 먼저 켜야 한다는 공통 문구를 보인다.
- **Gemini 재로그인:** 로그인 창이 성공으로 끝나면(`WindowsGeminiWebAccess.cs:91`, `MacGeminiWebController.cs:96`의 Gemini 재시작 지점) Antigravity가 Active일 때 `RequestSourceRefresh(Antigravity)`도 부른다. 로그인 필요로 멈춘 Antigravity가 다시 돈다.
- **로그아웃:** Antigravity 설정을 끄고(다른 계정으로 다시 로그인할 수 있으므로 자동으로 다시 켜지 않는다), 두 조회원을 모두 끄고, 두 캐시를 지운 뒤 프로필·데이터 저장소를 지운다. 3.3-5 순서를 두 조회원에 그대로 적용한다.
  - Mac: 진행 중 조회 대기(`MacGeminiUsageReader.cs:118`)에 Antigravity 리더도 넣는다.
  - Windows: 다음 시작 때의 재시도(`RetryLogoutAsync`)도 두 캐시를 지운다.
- 메뉴 문구는 공통 상수(`AntigravityWebText.MenuLabel` 등)로 두고, Windows 트레이와 Mac 메뉴바 메뉴에 각자 항목을 만든다(AGENTS 트레이·메뉴바 메뉴 규칙).

**표시**

- Antigravity가 Active이거나 값·상태가 있으면, Gemini 박스는 두 부분으로 나뉜다. 첫 부분은 Gemini 앱 5시간·주간, 둘째 부분은 Antigravity 5시간·주간이다. 꺼져 있으면 지금과 같은 한 부분이다.
- **모델:** `QuotaSectionText`에 선택 필드 `Parts`(`QuotaSectionPart(QuotaProvider Source, QuotaLine Title, IReadOnlyList<QuotaLine> Rows, QuotaLine Metadata)` 목록)를 더한다.
  - 한 부분 박스는 `Parts`가 비어 있고 지금 필드를 그대로 쓴다. 기존 박스 3개와 Antigravity가 꺼진 Gemini 박스는 바뀌지 않는다.
  - 두 부분 박스에서는 `Rows`와 `Metadata`가 첫 부분과 같다. 호스트는 `Parts`가 있으면 부분마다 제목·행·상태 줄을 그린다.
- **행 ID:** 결과가 없을 때의 자리 표시 행은 `WindowId`가 빈 문자열이라, 두 부분에 하나씩 생기면 호스트 행 캐시 키가 겹친다(`AccountQuotaView.cs:333`, `Mac/MacStatusPanel.cs:296`). 자리 표시 행 ID를 `{Source}:placeholder`로 정한다.
- **`LayoutKey`:** 부분 수, 각 부분의 `Source`와 행 ID 목록을 포함한다. 부분이 생기거나 사라지면 호스트가 박스를 다시 만든다.
- **톤:** 박스 제목(`Heading`)은 지금처럼 Normal이다. 톤은 각 부분의 행과 상태 줄에만 붙는다. 박스 전체 톤은 두지 않는다.
- **문구:** 실패 문구는 4.1 표의 Gemini 웹 열과 같은 틀이고, 뒤의 출처 설명만 "Antigravity 사용량"으로 바뀐다. Gemini 로그인 필요로 미룬 경우, Gemini 웹이 꺼져 있는 경우의 문구를 더한다. 문구는 공통에만 둔다.
- 이 변경은 `QuotaPanelModel`을 고치므로 PR 본문에 "모바일 사이트 반영 필요"를 적는다. 단, 송신에 Antigravity가 들어가는 P4 전까지 사이트가 할 일은 없다는 점도 함께 적는다.

**구현 확인 항목 (PR 통과 기준에 더함)**

- 공통 검사:
  - 파서: 정상, 단수형, 1성분 기간, 상한 초과, 단위 순서 위반, 100% 기간 없음, 라벨 중복·누락, 비율 범위 밖, 구간 안 비율 2개, 비영어, 판정 순서.
  - 조회원 특성, 수동 최소 간격, Deferred에서 상태 불변, 예상 시각 경과 판정.
  - 로그아웃 순서, Gemini 웹 끄기·켜기 연동, 재로그인 깨우기.
  - 송신 0회와 다른 Provider 송신 바이트 불변.
  - 박스 구성: Antigravity 꺼짐 시 기존 `LayoutKey`·문구 불변, 두 부분 박스의 자리 표시 행 ID 중복 없음.
- Windows: 실제 조회에서 창·작업 표시줄·Alt+Tab 비노출, 세션 잠금 때 미룸.
- Mac:
  - 실제 조회에서 창·Dock·Mission Control 비노출, 앱 비활성 상태에서 값 표시(`fullScreenAuxiliary` 없이).
  - 전체 화면 앱이 한 디스플레이를 덮은 상태에서 다른 디스플레이에 창을 두는지, 모두 덮였을 때 미루는지(1회만, MACOS_PORT 39절).
  - 화면 잠금 때 미룸.
  - smoke는 Antigravity 리더를 띄우지 않는다.

## 5. 설계 결정 기록

- **`QuotaProvider` 열거형은 유지한다.** 캐시·송신 식별자이고, Provider당 조회원은 하나면 충분하다. 경로가 바뀌면(agy → 웹) 조회원을 바꾼다.
- **실패 종류는 닫힌 열거형이다.** 새 원인은 리뷰를 거쳐 추가한다. 모르는 경우는 `Rejected`나 `FormatChanged`로 두고, 로그인을 추정하지 않는다.
- **로그인 확인은 공식 명령만 쓴다.** 인증 파일을 읽으면 Provider 내부 형식에 묶이고 개인정보 위험이 커진다. 자동 재로그인과 장기 토큰 보관은 하지 않는다(2026-11-24 결정).
- **플러그인 구조는 쓰지 않는다.** Provider는 손으로 등록하는 4개이고, 탐색 구조는 이득보다 검사 부담이 크다.
- **문구는 공통에만 둔다.** 클라이언트 예외 메시지에 섞인 문구는 P1에서 없앤다.
- **웹과 CLI의 표시 차이는 정책 객체로 표현한다.** 불리언 하나로는 박스·송신·성공 시각 처리의 차이를 담을 수 없다(검토 지적).
- **Antigravity 한도를 자동으로 읽는다(2026-11-26, 사용자 결정).** Antigravity 약관 §6은 제3자 소프트웨어로 서비스에 접근하는 것을 위반으로 보고, Antigravity·Gemini CLI 계정 정지를 언급한다. 사용자는 이 위험을 알고 받아들였다. 위험을 줄이는 장치는 1시간 주기, 별도 켜기와 기본 꺼짐, Models 설정 화면 읽기만(클릭·원격 세션 조작·내부 API·쿠키·토큰 사용 없음), UA 변경 없음이다(4.7).
- **Antigravity는 Gemini와 다른 조회원이다.** 로그인·실패·켜기·캐시가 따로 움직이기 때문이다(예: Gemini 앱은 정상인데 Antigravity 형식만 바뀜). 표시는 Gemini 박스 하나로 합친다. "Provider당 조회원 하나" 원칙을 지키려고 열거형 끝에 `Antigravity`를 추가한다.
- **숨김 읽기는 가시성 판정에 기댄다.** 문서에 없는 동작이라 깨질 수 있음을 알고 택했다. 깨지면 진단 코드로 바로 드러나게 한다(4.7 가시성 의존 대응).

## 6. 단계별 PR

### 6.1 공통 절차

- **공통 PR:** Windows가 올린다. Mac은 같은 HEAD에서 공통 검사, Mac 빌드, native smoke로 확인한다. 호스트 변경이 있으면 AGENTS 종속 PR 순서(#132)를 따른다.
- **공통 파일 추가·삭제:** `Shared/SharedSources.props`를 함께 고친다. 새 검사는 `SharedTestSuite`에 등록한다.
- **송신 검사:** Windows `--self-test`에서만 돈다. 그래서 송신 관련 P0 항목은 Windows 결과로만 확인한다.
- **사이트 반영:** 표시 문구나 판정이 바뀌는 PR(P1·P3)은 본문에 "모바일 사이트 반영 필요" 항목을 적는다(AGENTS 모바일 사이트 절).
- **배포 기록:** 배포하는 단계마다 버전을 올리고 기록 문서를 남긴다(AGENTS 버전 기록 절).

### 6.2 단계

| 단계 | 내용 | 사용자에게 보이는 변화 | 배포 |
|---|---|---|---|
| P0 | **검사 하네스와 특성 검사.** 모니터 생성, 조회 구동, 시계 제어, 스냅샷→표시 문구, 송신 바이트를 다루는 얇은 하네스를 만든다. 3절의 모든 항목을 이 하네스와 `QuotaPanelModel`·송신 출력만으로 검사한다. 제품 코드 변경 없음 | 없음 | 없음 |
| P1 | **실패 분류.** `QuotaFailure`와 4.1 분류표, `QuotaState.Failure`, 문구를 공통 모델로 이동, "결과 없음" 문구 수정, agy 분기 삭제. 로그인 필요 표시는 기존 Claude 확인 결과에 따르는 규칙을 그대로 쓴다(`QuotaState.ClaudeAuth` 유지) | 실패 문구가 원인별로 구분됨 | Windows·Mac |
| P2 | **조회원 인터페이스와 공통 루프.** `IQuotaSource`, 어댑터, 주기 정책, `SetMode`, `RequestSourceRefresh`, `ClearCacheAsync`, 호환 팩토리, `IsQueryEnabled` 대체, Gemini 전용 루프·상태 필드 제거, 호스트의 Gemini 메뉴 연결 변경, 기존 검사·smoke의 호출부 이전(건수 증감 기록). **Claude 로그인 확인 루프와 `LatestClaudeAuth`, `QuotaState.ClaudeAuth`는 그대로 둔다** | 없음(P0 검사 그대로 통과) | Windows·Mac(회귀만 관찰) |
| P2.5 | **Antigravity 웹 조회원(4.7).** P2가 운영에서 3일 이상 회귀 없이 돈 뒤 시작한다. 공통 파서·조회원·표시, 두 호스트의 리더·메뉴·로그아웃 연동. 송신 제외. P3와 순서는 준비된 쪽이 먼저지만, 같은 배포에 묶지 않는다 | 켜면 Gemini 박스가 네 줄 | Windows·Mac |
| P3 | **로그인 상태 일반화.** P2가 운영에서 3일 이상 회귀 없이 돈 뒤 시작한다. `QuotaAuth`, `IQuotaAuthProbe`, Claude 확인 이전, `LatestAuth`, 호스트 알림의 API 변경, Gemini 웹 로그인 상태 연결, Codex·Grok 공식 상태 명령 조사(있을 때만 추가), 박스 상세 "로그인 상태" 절 공통화 | Gemini와 조사 결과에 따라 Codex·xAI에도 로그인 상태 표시 | Windows·Mac |
| P4 | **송신 일반화.** `Traits.Export` 기반 송신, Antigravity 줄 송신, (선택) v2에 실패 종류 선택 필드 추가. 사이트가 허용 목록 밖 필드를 지우므로 **사이트를 먼저 배포하고 앱을 그다음 배포한다** | 사이트에 로그인 필요 표시(선택 시) | 사이트 → Windows |

- **통과 기준:**
  - P0 검사가 통과 기준이다.
  - P2는 P0의 하네스만 고칠 수 있고, 검사 판정(assertion)은 고치지 않는다.
  - P1과 P3에서 의도한 변화는 해당 판정을 고치면서 PR 본문에 "의도한 변경"으로 적는다.
- **P2를 따로 배포하는 이유:** P2는 표시 변화가 없는 재구성이다. 회귀가 나오면 P2만 되돌릴 수 있게 따로 배포한다. 되돌리기는 P3 병합 전까지만 가능하므로, P3는 관찰 기간 뒤 시작한다.

## 7. 위험과 대응

- **스레드·취소 규칙 회귀:** 가장 깨지기 쉬운 부분은 세 가지다. P0에서 늦은 결과, 취소, 동시 Refresh, 끄기와 진행 중 조회, `StopAsync`와 웹 조회, await 없는 `Dispose` 시나리오를 고정한다.
  - `Succeeded`의 게이트 안 1회 규칙
  - 세대 번호로 늦은 결과를 버리는 규칙
  - 비동기 취소를 기다린 뒤 게이트를 놓는 규칙
- **로그아웃과 저장 경쟁:** 진행 중 저장과 캐시 삭제가 겹치는 경우를 P0에 넣는다.
- **검사 호출부 대량 수정:** 기존 검사와 smoke가 모니터 API를 직접 부른다(`RefreshOnceAsync` 189곳, `RefreshGeminiWebIfDueAsync` 27곳, `SetGeminiWebEnabled` 17곳, `LatestClaudeAuth` 12곳, `IsQueryEnabled` 35곳). 호환 팩토리와 계산 속성으로 P2 안에서 단계적으로 옮기고, 건수 증감을 PR에 적는다.
- **호스트 동시 수정:** P2와 P3는 공통과 두 호스트가 함께 바뀐다. 순서는 Windows 공통 PR → Mac 호스트 PR을 그 브랜치에 병합 → 합친 HEAD를 두 OS에서 확인 → main 병합이다.
- **Antigravity 계정 위험:** 약관 §6 위반으로 계정이 제한될 수 있다(5절). 기본 꺼짐과 1시간 주기로 줄이며, 문제가 생기면 사용자가 메뉴에서 바로 끌 수 있다. 같은 Google 세션을 쓰므로 Antigravity 페이지 로드가 Gemini 웹 세션에 영향을 주는지 운영에서 관찰한다.
- **Mac 투명 창과 메뉴바:** 매시간 창을 여는 동작이 MACOS_PORT 39절 증상(22회 반복 뒤 재발)과 겹칠 수 있다. 그래서 `fullScreenAuxiliary`를 빼고, 전체 화면 앱이 덮은 디스플레이에는 창을 두지 않으며, 모두 덮였으면 미룬다(4.7). 이 설정에서 앱 비활성 상태로 값이 나오는지는 구현 PR에서 확인한다.
- **문구 변화에 따른 smoke 기대값:** P1에서 문구가 바뀌면 Windows와 Mac smoke 기대값을 함께 바꾼다. 비교는 공통 상수로만 한다.

## 8. 미확인·조사 항목

- Codex CLI와 Grok CLI의 공식 로그인 상태 명령, 그리고 출력 형식(P3 조사).
- Claude CLI가 로그인 만료 상태에서 `-p /usage`에 내는 응답 형태. 다음 실제 만료 때 형식만(값 없이) 기록한다. 이를 근거로 `claude.no-usage-report` 등을 `LoginRequired`로 바로 분류할 수 있는지 판단한다.
- Gemini 웹 세션의 실제 유지 기간(관찰 중).
- Antigravity Models 화면의 남은 비율 100%·0% 문구, 단수형(`1 day`, `1 hour`), 영어 외 표시 언어. 실제로 나타나면 형식만(값 없이) 기록한다.
- Mac에서 `fullScreenAuxiliary` 없는 투명 창이 앱 비활성 상태로 보이는 상태로 판정되는지, Mission Control에 나타나지 않는지(구현 확인 항목).
- 로그아웃 상태에서 Antigravity 페이지가 Google 로그인 화면으로 이동하는지, 제자리에 로그인 안내를 띄우는지. 후자면 판정 순서 5의 `not-rendered`로 매시간 다시 시도한다. 첫 실제 사례에서 형식만(값 없이) 기록하고, 필요하면 판정 규칙을 더한다.
- Windows 세션 잠금·Mac 화면 잠금 중의 렌더링 여부. 설계는 이때 읽지 않으므로 확인하지 않아도 된다.

## 9. 이 문서의 범위 밖

- 사이트의 xAI 상태 파서 이중 구현. Windows가 판정 결과를 송신하는 구조로 정리할 예정이며, 별도로 설계한다.
- Windows 숨김 실행 직후 첫 창 표시 보장(BACKLOG).
- smoke PNG 긴 경로의 GDI+ 오류.
- 문서 정리(`MAINTENANCE_*.md` 통합).
