# 한도 조회 구조 재설계 — 조회원·실패 분류·로그인 상태

- **상태:** 설계 확정(2026-11-25, 메인). 독립 검토(opus) 지적 2건 차단·11건 중요·6건 경미를 반영했다. 단계별 PR은 6절 순서로 진행한다.
- **범위:** 계정 한도 조회(`AccountQuota*`, Gemini 웹, Claude 로그인 확인)와 그 표시·알림·송신 연결.
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
   - `nextCheckUtc`는 값을 싣는 상태에서만 보낸다.
   - 다른 Provider 완료와 Gemini 변화가 같은 처리 안에서 겹치면 한 번만 보낸다.
   - 다른 Provider가 완료될 때마다 Gemini 비교 기준을 갱신한다. Gemini 조회 중에도 그렇다.
   - 취소된 조회는 송신하지 않는다.
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
    QuotaExportMode Export);         // Completion / OnChange

internal sealed record QuotaPresentationPolicy(
    IReadOnlySet<QuotaFailureKind> HideReadingInPanel,       // 웹: {LoginRequired, FormatChanged}, CLI: {}
    IReadOnlySet<QuotaFailureKind> HideReadingInExport,      // 웹: {LoginRequired}, CLI: {}
    bool ClearSuccessOnLoginRequired,                        // 웹: 참
    Func<QuotaReading, bool>? AcceptForDisplay,              // 웹: IsWebReading
    bool ShowEarlyResetDetail);                              // 웹: 거짓

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
  - `SetMode(QuotaProvider, QuotaSourceMode)`: 켜기·끄기를 일반화한다. 세대 번호 증가, 진행 중 조회의 비동기 취소, 켤 때 상태 비우기(3.1-7)를 포함한다.
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

## 5. 설계 결정 기록

- **`QuotaProvider` 열거형은 유지한다.** 캐시·송신 식별자이고, Provider당 조회원은 하나면 충분하다. 경로가 바뀌면(agy → 웹) 조회원을 바꾼다.
- **실패 종류는 닫힌 열거형이다.** 새 원인은 리뷰를 거쳐 추가한다. 모르는 경우는 `Rejected`나 `FormatChanged`로 두고, 로그인을 추정하지 않는다.
- **로그인 확인은 공식 명령만 쓴다.** 인증 파일을 읽으면 Provider 내부 형식에 묶이고 개인정보 위험이 커진다. 자동 재로그인과 장기 토큰 보관은 하지 않는다(2026-11-24 결정).
- **플러그인 구조는 쓰지 않는다.** Provider는 손으로 등록하는 4개이고, 탐색 구조는 이득보다 검사 부담이 크다.
- **문구는 공통에만 둔다.** 클라이언트 예외 메시지에 섞인 문구는 P1에서 없앤다.
- **웹과 CLI의 표시 차이는 정책 객체로 표현한다.** 불리언 하나로는 박스·송신·성공 시각 처리의 차이를 담을 수 없다(검토 지적).

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
| P3 | **로그인 상태 일반화.** P2가 운영에서 3일 이상 회귀 없이 돈 뒤 시작한다. `QuotaAuth`, `IQuotaAuthProbe`, Claude 확인 이전, `LatestAuth`, 호스트 알림의 API 변경, Gemini 웹 로그인 상태 연결, Codex·Grok 공식 상태 명령 조사(있을 때만 추가), 박스 상세 "로그인 상태" 절 공통화 | Gemini와 조사 결과에 따라 Codex·xAI에도 로그인 상태 표시 | Windows·Mac |
| P4 | **송신 일반화.** `Traits.Export` 기반 송신, (선택) v2에 실패 종류 선택 필드 추가. 사이트가 허용 목록 밖 필드를 지우므로 **사이트를 먼저 배포하고 앱을 그다음 배포한다** | 사이트에 로그인 필요 표시(선택 시) | 사이트 → Windows |

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
- **문구 변화에 따른 smoke 기대값:** P1에서 문구가 바뀌면 Windows와 Mac smoke 기대값을 함께 바꾼다. 비교는 공통 상수로만 한다.

## 8. 미확인·조사 항목

- Codex CLI와 Grok CLI의 공식 로그인 상태 명령, 그리고 출력 형식(P3 조사).
- Claude CLI가 로그인 만료 상태에서 `-p /usage`에 내는 응답 형태. 다음 실제 만료 때 형식만(값 없이) 기록한다. 이를 근거로 `claude.no-usage-report` 등을 `LoginRequired`로 바로 분류할 수 있는지 판단한다.
- Gemini 웹 세션의 실제 유지 기간(관찰 중).

## 9. 이 문서의 범위 밖

- 사이트의 xAI 상태 파서 이중 구현. Windows가 판정 결과를 송신하는 구조로 정리할 예정이며, 별도로 설계한다.
- Windows 숨김 실행 직후 첫 창 표시 보장(BACKLOG).
- smoke PNG 긴 경로의 GDI+ 오류.
- 문서 정리(`MAINTENANCE_*.md` 통합).
