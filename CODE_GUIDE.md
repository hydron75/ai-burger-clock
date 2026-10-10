# 소스코드, 쉬운 말로 읽기

이 문서는 **Windows 2.4.1 준비 소스 기준**입니다. 양쪽 확인을 마친 #48을 main `b5461ec`에 병합했으며 버전·기록을 올리는 새 PR의 병합·교체 승인을 기다립니다. 현재 dist는 #47 병합 main `296f524`에서 만든 **2.4.0 그대로**입니다. 후보 검사·교체/복원 계획은 [2.4.1 준비 기록](MAINTENANCE_2_4_1.md), 현재 배포의 실제 EXE·백업은 [2.4.0 배포 기록](MAINTENANCE_2_4_0.md)을 봅니다. Windows 사용법은 [README](README.md), 최신 Mac 버전·검증은 [Mac 안내](Mac/README.md)로 이동하세요. 아래 실행·UI 설명은 Windows 기준이며 Mac의 차이는 12절에 정리했습니다.

코드를 한 줄씩 번역한 문서는 아닙니다. **각 파일이 무엇을 맡고, 서로 어떻게 연결되는지** 설명합니다. 현재 소스의 루트·Properties C# 72개, Mac 호스트 7개, 공통 검사 입구 1개로 총 80개와 빌드 설정을 다룹니다. `SharedTestSuite.cs`는 루트의 검사 파일에 이미 포함하고, 별도 공통 검사 입구는 `Shared.Tests/Program.cs` 한 개만 셉니다. 컴퓨터가 만든 `bin`·`obj`와 로컬 검증용 `artifacts`는 대상에서 뺍니다.

**2.3.0에 반영:** [Gemini/Antigravity 한도](GEMINI_ANTIGRAVITY_QUOTAS.md)는 공식 `agy` 1.3.1 이상·2.0 미만에서 Gemini 모델 그룹만 조회합니다. #30은 Mac 확인 뒤 main에 병합됐으며, 이 PC의 agy 1.3.2 실제 조회와 새 배포본의 Gemini 값 정상 표시도 확인했습니다(후자는 사용자 확인). Windows 준비·최종 검사 결과와 이전 #30 결과는 [2.3.0 배포 기록](MAINTENANCE_2_3_0.md)에서 구분합니다.

공식 조회 방식의 변경을 조사하는 외부 모니터링 현황은 [BACKLOG](BACKLOG.md)에 있습니다. 이 조사는 아래 앱 코드의 계정 한도 조회 루프와 별개이며, 새 SDK가 공개됐다고 앱의 CLI나 의존성을 자동으로 바꾸지는 않습니다.

**2.4.0에 반영:** #45의 공통 사용률·간략 시간·Provider 상세를 Windows의 막대와 흰색 고정 폭 팝업에 연결합니다. 공통 원본은 #45에서 양쪽 확인 뒤 병합됐으며 이번 배포에서는 C# 동작·검사 코드를 바꾸지 않았습니다. #46의 요금제 표시·Mac CLI 설치 경로 지정 후보는 보류 상태로 유지합니다.

**2.4.1 준비에 반영, 미배포:** #48은 `ProviderStatusClient`의 OpenAI 전체 구성 요소 보완과 응답 수신/범위 판정 분리, `StatusMonitor`의 수신·과거 판정 유지, `UsageStore`의 크기 제한 보조 진단, `StatusPanelModel`의 공통 카드·Tooltip 문구를 연결합니다. 범위 미확인만 있으면 UNKNOWN / CHECK이며 오래된 장애를 현재 카드에 남기지 않습니다. Windows 상태 카드의 마지막 확인 한 줄로 창은 374×569 DIP입니다. 이번 버전 준비 PR은 이 C#를 다시 바꾸지 않고 버전·기록만 갱신합니다.

## 1. 작은 안내소라고 생각해 보세요

이 앱에는 다섯 가지 역할이 있습니다.

- **시간표 담당:** 미국 업무시간과 공휴일을 보고 FULL/BURGER를 정합니다.
- **공지 확인 담당:** OpenAI·Claude·Gemini가 올린 공식 상태를 읽습니다.
- **안내 담당:** 두 정보를 합쳐 Provider별 GO/HOLD/STOP/CHECK를 보여줍니다.
- **기록 담당:** 사용자가 “느렸어요”, “잘 끝났어요”라고 남긴 경험을 저장합니다.
- **한도 담당:** 공식 Codex·Claude·agy CLI에 읽기 전용 조회를 부탁합니다. 2.4.0 화면은 사용률과 간략 리셋 시간을 표시하며 조회 범위·인증·정책은 유지합니다. 인증은 CLI가 맡습니다.

내가 오류를 기록했다고 공식 상태를 장애로 바꾸지 않습니다. 공식 장애라고 내 경험을 자동으로 Error로 적지도 않습니다.

~~~text
현재 시각 + 미국 시간대 + 공휴일 옵션
                  ↓
             AgentSchedule ─── 시간표·다음 전환 ─┐
                                                ↓
공식 공지 → ProviderStatusClient → StatusMonitor → 권고 계산
                                                ↓
                                      화면·트레이·알림

내가 누른 결과 + 당시 시간표·공식 상태
                  ↓
             UsageStore → SQLite 파일 → StatisticsWindow
~~~

`TrayApplicationContext`가 이 담당자들을 연결합니다. 시간을 판단하는 규칙, 공지를 읽는 규칙, 기록을 저장하는 규칙은 다른 파일에 나눠 두었습니다.

## 2. 먼저 알아둘 단어

| 코드에서 보이는 말 | 여기서는 이런 뜻 |
|---|---|
| class | 관련된 일들을 모아 놓은 담당자 |
| method / 함수 | 담당자에게 시킬 수 있는 한 가지 일 |
| enum | 정해진 선택지. 예: Success / Slow / Error / Interrupted |
| record | 여러 정보를 한 묶음으로 주고받는 기록 카드 |
| snapshot | 특정 순간의 상태를 찍어 둔 사진 같은 데이터 |
| UTC / KST | 저장·계산에 쓰는 세계 기준 시각 / 한국에서 읽는 시각 |
| async / await | 느린 일을 기다리는 동안 화면이 멈추지 않도록 연결하는 방식 |
| cancellation | 종료할 때 진행 중인 작업에 멈추라고 알리는 신호 |

비유일 뿐 C# 문법 전체를 설명한 것은 아닙니다. 우선 “어떤 정보를 넣으면 무엇이 나오는가?”를 보면 됩니다.

## 3. 앱을 켜면 어떤 일이 생기나요?

먼저 [Program.cs](Program.cs)가 실행됩니다. 앱의 현관입니다.

1. 특별한 검사 명령을 붙였는지 확인합니다.
2. 일반 실행이면 같은 Windows 세션에 앱이 이미 있는지 확인합니다.
3. 창과 버튼을 다루는 WinForms를 준비합니다.
4. [TrayApplicationContext.cs](TrayApplicationContext.cs)를 만들고, 클릭과 타이머를 기다립니다.

중복 실행 방지에는 `Mutex`를 씁니다. “이 앱이 이미 자리를 차지했다”는 표지판과 비슷합니다.

`--autostart`는 **창 없이 트레이로 시작하라는 뜻**입니다. 이 옵션으로 실행한다고 Windows 자동 시작 등록을 새로 켜는 것은 아닙니다.

연결 담당자는 트레이와 화면을 준비한 뒤 DB에서 공휴일 설정을 읽습니다. 그 설정을 반영하고 나서 공식 상태의 정기 조회를 시작합니다. 새 설정이 없으면 공휴일 보정은 ON입니다. 읽기에 실패하면 확인되지 않은 정책을 적용하지 않도록 OFF와 오류 안내를 사용합니다.

소스의 `AgentSchedule.GetSnapshot` 함수에는 기본 인수가 OFF로 적혀 있지만, **실제 앱은 읽어 온 설정을 명시적으로 전달합니다.** 함수의 기본 인수와 앱의 기본 설정은 다릅니다.

## 4. 시간표 담당은 어떻게 계산하나요?

핵심은 [AgentSchedule.cs](AgentSchedule.cs)입니다.

입력은 **지금의 UTC 시각과 공휴일 보정 ON/OFF**입니다. 출력인 `ScheduleSnapshot`에는 현재 FULL/BURGER, 다음 전환과 남은 시간, 미국 현지 시각과 DST 여부, 주말·공휴일 연장 여부, 정책 버전이 들어 있습니다.

계산 순서는 다음과 같습니다.

1. 현재 시각을 미국 동부 날짜로 바꿉니다.
2. 주변 날짜들의 미국 업무 구간을 만듭니다.
3. 토요일·일요일은 건너뜁니다.
4. 공휴일 보정이 ON이면 대상 공휴일도 건너뜁니다.
5. 각 업무일의 **동부 09:00과 같은 날짜 서부 18:00을 각각 UTC로 변환**합니다.
6. 지금이 그 사이이면 BURGER, 아니면 FULL입니다.
7. BURGER이면 현재 업무 종료, FULL이면 다음 업무 시작이 다음 전환입니다.

업무 시작 순간은 BURGER에 포함하고, 업무 종료 순간부터 FULL입니다. 코드에서 말하는 `[start, end)`가 이 뜻입니다.

### 한 시간 차이를 직접 외우지 않습니다

Windows 시간대 이름 `Eastern Standard Time`, `Pacific Standard Time`을 사용합니다. 이름에 Standard가 들어 있지만 코드가 받는 시간대 규칙에는 DST도 들어 있습니다.

“지금 DST니까 모든 날짜에서 4시간을 빼자”가 아니라 **각 경계 날짜에 맞는 규칙**을 적용합니다. 그래서 시계가 바뀌는 주말도 실제 시간으로 셉니다.

`Korea Standard Time`은 한국 시각 표시와 통계에 사용합니다. KST 10시·22시는 계산 결과이지 원래 규칙이 아닙니다.

주변 업무 구간은 잠시 기억해 둡니다. 이것이 캐시입니다. 동부 날짜나 공휴일 옵션이 달라질 때 다시 만들므로 매초 한 달치 달력을 새로 계산하지 않습니다.

### 공휴일 달력은 따로 있습니다

[UsFederalHolidays.cs](UsFederalHolidays.cs)는 “그해 관측하는 정기 연방 공휴일은 언제인가?”에 답합니다. 고정 날짜와 “몇 번째 월요일” 같은 규칙을 사용합니다.

토요일→앞 금요일, 일요일→다음 월요일의 대체휴일과 다음 해 신정이 전년도 12월 31일에 관측되는 경우도 처리합니다. 회사별 휴가, 지역 휴일, 임시 휴무, 반일 휴무를 모두 아는 달력은 아닙니다. [정책 범위](HOLIDAYS_TRAY.md)

**공휴일 규칙은 앱 코드가 관리하고, DST는 Windows/.NET 시간대 정보가 관리합니다.** 두 가지를 같은 자동 갱신 정보로 보면 안 됩니다.

주말과 공휴일 플래그는 독립적입니다. 긴 주말에 공휴일이 붙으면 둘 다 true가 될 수 있습니다. 평일 공휴일만 있으면 공휴일 플래그만 true입니다.

“공휴일 연장”은 **그 공휴일 때문에 길어진 연속 FULL 구간 전체**입니다. 지금 미국 날짜 자체가 반드시 공휴일이라는 뜻은 아닙니다.

## 5. 공식 상태는 어떻게 알아오나요?

[ProviderStatusClient.cs](ProviderStatusClient.cs)가 회사의 공개 상태 데이터를 읽습니다.

브라우저 화면을 그림처럼 읽거나 HTML 모양에 의존하지 않습니다. 항목 이름과 값으로 정리된 **JSON**을 읽습니다. JSON은 컴퓨터끼리 주고받기 쉬운 목록 형식이라고 생각하면 됩니다.

| Provider | 이 코드가 읽는 정보 |
|---|---|
| OpenAI | 공식 summary + 전체 components 목록의 합집합. incident 목록이 생략되어 있으면 공식 incident history도 추가 확인 |
| Claude | 공식 summary의 구성 요소와 incident |
| Gemini | Workspace의 제품 목록과 장애 이력에서 Gemini를 선택 |

component는 서비스 안의 기능·구성 요소, incident는 회사가 공개한 장애 사건입니다. 모든 제품의 문제를 이 앱의 관심 서비스 문제로 취급하지 않습니다. 범위를 확실히 판단할 수 없으면 UNKNOWN으로 남깁니다.

예를 들어 Sora 문제만 있다면 ChatGPT 작업 장애로 곧바로 바꾸지 않습니다. 반대로 관련 사건이 확인되면 전체 요약이 정상처럼 보여도 문제를 반영할 수 있습니다.

Gemini는 Workspace의 Gemini 제품 범위입니다. 모든 Gemini API·Vertex AI·지역별 서비스까지 검사하는 것은 아닙니다. OpenAI Work도 별도 구성 요소가 항상 따로 있는 것은 아니므로 관련 기능과 사건 정보를 사용합니다.

정확한 주소·선정 기준·예외는 [PROVIDER_SOURCES.md](PROVIDER_SOURCES.md)에 있습니다. 과거 조회 기록은 현재 정상이라는 보증이 아닙니다.

### 읽는 사람과 정기적으로 확인하는 사람은 다릅니다

[StatusMonitor.cs](StatusMonitor.cs)는 정기적으로 `ProviderStatusClient`를 부릅니다.

- 세 Provider를 독립적으로 조회합니다. 한 곳의 실패가 다른 두 곳의 실패로 번지지 않습니다.
- 보통 조회 묶음이 끝난 뒤 약 5분을 기다립니다.
- 한 Provider의 조회 제한은 15초입니다.
- 필수 공식 응답의 마지막 수신 성공부터 15분 이상 지나면 STALE로 표시합니다. JSON·사건 범위를 판정할 수 없는 응답도 수신 성공 시각은 갱신하지만 UNKNOWN / CHECK로 남습니다. 수신 성공은 서버 데이터의 최신성 보장이 아닙니다.
- 성공한 적이 없으면 오래 기다렸다는 이유만으로 STALE가 되지 않고 UNKNOWN으로 남습니다.
- 수동 Refresh와 종료 시 취소도 처리합니다.
- 절전 복귀 시 즉시 재조회를 요청합니다. 이전 조회가 진행 중이면 완료 직후 추가 조회하고, 같은 대기 요청은 합칩니다.
- 상태 변경을 전달받는 함수 하나가 예외를 내더라도 다른 수신자와 정기 조회가 멈추지 않도록 나눠 처리합니다.

상태 카드에는 마지막 응답 확인 시각을 표시합니다. Tooltip은 최근 수신 실패·판정 실패, 마지막 판정 가능 상태와 그 시각, 확인된 사건·범위 미확인 사건을 구분합니다. 과거 판정 시각이 없으면 `(시각 미상)`을 붙이고, 수신 시각과 같은 과거 판정 시각은 반복하지 않습니다. 현재 판정과 같은 판정 실패 문장도 한 번만 표시합니다. 지난 장애 제목은 수신 실패/STALE 카드의 현재 장애로 남기지 않습니다. 진단은 기존 schema 2의 AppMetadata에 Provider별 최대 8KiB 한 건으로 저장하며 캐시 조회 시각이 다르거나 형식이 손상되면 무시합니다. 조회 실패와 범위 미확인은 서로 다른 이유입니다.

통신 창구인 `HttpClient`는 재사용합니다. 매초 새 인터넷 연결을 시도하지 않습니다.

2.1.2에서는 공식 데이터 해석도 앱에 주입한 시각을 사용합니다. 요청의 User-Agent는 앱 버전으로 한 번 정하고, 다른 Provider 저장이 성공해도 해당 Provider의 저장 오류가 사라지지 않도록 오류를 나눠 기억합니다.

## 6. 권고와 트레이 색상은 어디서 정하나요?

[Phase2Models.cs](Phase2Models.cs) 안의 `RecommendationPolicy`가 권고 규칙입니다.

~~~text
FULL + 정상       → GO
FULL + 성능 저하  → HOLD
FULL + 장애       → STOP
FULL + 확인 불가  → CHECK

BURGER + 정상     → BURGER 유지
BURGER + 문제     → BURGER + ISSUE
~~~

UNKNOWN/STALE인 BURGER는 BURGER + CHECK입니다. **공식 상태로 시간표를 승격시키지 않으며, Provider를 따로 계산합니다.**

[TrayPresentation.cs](TrayPresentation.cs)는 작은 표지판의 표현을 정합니다.

- F/B는 시간표에서 가져옵니다.
- 색은 장애 빨강 → 성능 저하 주황 → 확인 불가 회색 순서입니다.
- 모두 정상이면 FULL은 초록, BURGER는 주황입니다.
- 마우스를 올렸을 때의 도움말에도 세 Provider를 각각 적습니다.

트레이 색을 정하는 일이 Provider의 권고를 다시 바꾸지는 않습니다. 표시와 판단을 분리한 것입니다.

### 알림이 계속 반복되면 곤란하겠죠?

[RecommendationNotifications.cs](RecommendationNotifications.cs)는 마지막 확정 권고를 기억합니다.

- GO → HOLD처럼 의미 있는 변경은 알립니다.
- HOLD → HOLD는 다시 알리지 않습니다.
- GO → UNKNOWN → HOLD라면 마지막에 확인했던 GO와 비교합니다.
- HOLD → UNKNOWN → HOLD라면 같은 경고를 반복하지 않습니다.

실제 Windows 알림을 요청하는 곳은 `TrayApplicationContext`입니다. 시간 경계 알림과 공휴일 설정 변경 알림도 구분합니다.

2.2.4의 [StatusTicker.cs](StatusTicker.cs)는 매초 시간표 전환·Provider 알림·아이콘 변경의 **판정만** 공통으로 맡습니다. 입력 이유는 초기화·타이머·Provider 변경·정책 변경입니다. `TrayApplicationContext.RefreshStatus(bool, bool)`는 기존 호출 지점을 받는 감싸는 함수로 남고, UI·알림 적용 순서와 색상은 Windows가 유지합니다.

중복 Provider 입력은 첫 값을 사용하고 [WindowsWarningLog.cs](WindowsWarningLog.cs)에 경고를 보냅니다. 앱 데이터의 `logs/status-ticker.log`와 이전 로그 1개는 각각 최대 64 KiB이며, 정상 입력에는 파일을 만들지 않습니다. 관리자 권한·이벤트 로그 원본 등록은 필요 없고 로그 실패가 판정·화면을 중단시키지 않습니다.

이는 알림을 **요청하는 로직**입니다. Windows 설정에 따라 실제 풍선이 보이지 않을 수도 있습니다. STOP 역시 안내일 뿐 다른 프로그램을 중단시키지 않습니다.

## 7. 내 기록은 어떻게 저장되나요?

ChatGPT 행을 우클릭하고 Slow를 고른 경우입니다. 화면 이름은 [WindowsProviderNames.cs](WindowsProviderNames.cs)에서 바꾸지만 전달하는 식별자는 여전히 `ProviderKind.OpenAI`입니다.

1. [StatusWindow.cs](StatusWindow.cs)가 클릭을 알아챕니다.
2. `TrayApplicationContext`에 “OpenAI, Slow를 기록해 주세요”라고 알립니다.
3. [UsageMeasurementFactory.cs](UsageMeasurementFactory.cs)가 당시 시각·시간표·공식 상태를 `UsageMeasurement` 카드로 묶습니다.
4. 메모를 선택했다면 [MeasurementDialog.cs](MeasurementDialog.cs)를 엽니다.
5. [UsageStore.cs](UsageStore.cs)가 SQLite에 저장합니다.
6. 성공하면 [FeedbackText.cs](FeedbackText.cs)의 “저장됨” 또는 메모 잘림 안내를 Windows 피드백 영역에 표시합니다.

메모를 쓰더라도 기준 시각과 상태는 **메모 창을 열기 전**에 잡습니다. 저장 버튼을 누른 순간으로 바뀌지는 않습니다.

메모창은 요청받은 결과 종류를 미리 선택합니다. 초기화가 실패했거나 메모창이 열린 동안 종료가 시작되면 새로운 저장을 진행하지 않도록 처리합니다.

메모는 앞뒤 공백 정리 후 1,000 UTF-16 단위를 넘지 않는 완전한 문자 요소 경계에서 제한합니다. 입력창은 전체 입력을 받고 `UsageMeasurementFactory.WithNote`가 저장 시 한 번만 정리하므로 이모지·결합 문자를 반으로 자르지 않습니다. 경계 때문에 999자 등으로 저장될 수 있고, 실제 잘렸으면 `FeedbackText.NoteTruncated(N)`의 “앞부분 N자만 저장” 안내를 표시합니다. 1,000자 이하나 공백 정리만 한 경우는 기존 피드백입니다. 앱이 대화를 수집하지는 않지만, 직접 입력한 메모는 저장됩니다. 민감정보는 적지 않아야 합니다.

### 기록장 안에는 네 칸이 있습니다

| SQLite 테이블 | 무엇을 담나요? |
|---|---|
| UsageEvents | 사용자가 남긴 경험과 당시 상태 |
| ProviderStatusHistory | 공식 상태·권고·사건 등의 변화 이력 |
| ProviderStatusCache | Provider별 가장 최근 조회 정보 |
| AppMetadata | DB 구조 버전, 공휴일 설정, 마지막 계정 한도 조회값 캐시 |

Cache는 “마지막으로 읽은 메모”, History는 “중요한 변화 기록”입니다. 매 5분마다 같은 정상 상태를 History에 계속 추가하지 않습니다. **공식 이력은 모든 조회를 빠짐없이 모은 로그가 아닙니다.**

시간은 UTC로 저장하고 화면에서는 KST로 바꿉니다.

### 기록장 구조가 바뀌면요?

현재 DB 구조 버전은 2입니다. 앱 버전 2.4.0과는 다른 번호이며, 2.1.0 이후 바뀌지 않았습니다. 2.4.0도 기존 기록을 다시 쓰거나 DB를 migration하지 않습니다. 2.3.0에 추가한 Gemini 캐시는 별도 metadata key를 씁니다.

기존 구조 1을 열면 먼저 SQLite 백업 기능으로 복사본을 만듭니다. 본체 옆의 WAL에 이미 저장된 내용도 포함합니다. WAL은 기록을 안전하게 반영하기 위한 보조 파일입니다.

백업 확인 뒤 공휴일 관련 세 항목을 한 묶음으로 추가합니다. 이것이 트랜잭션입니다. 중간에 실패하면 일부만 바뀐 채로 두지 않고 되돌립니다.

- `HolidayAdjustmentEnabled`: 그때 보정이 ON이었나, OFF였나.
- `HolidayExtendedFullThrottle`: 공휴일 때문에 연장된 FULL이었나.
- `HolidayNames`: 어떤 공휴일이 반영됐나.

예전 기록에는 설정을 적지 않았으므로 `null`, 즉 **미기록**으로 둡니다. 미기록을 OFF였다고 추측하지 않고, 과거 경험을 새 규칙으로 재분류하지도 않습니다.

일부 실패를 만난 뒤에는 같은 실행 중 계속 백업하며 재시도하지 않도록 실패를 기억합니다. 원본과 백업 확인 후 재시작을 안내합니다. 더 최신 DB를 자동으로 구버전으로 바꾸지도 않습니다.

2.1.1에서는 백업 검증 실패(`InvalidDataException`)도 이 재시도 중단 대상으로 포함합니다. `StatusWindow`는 같은 저장 오류가 새 사용자 안내를 덮어쓰지 않도록 마지막으로 표시한 오류를 기억하고, 오류가 해소되면 해당 표시만 지웁니다.

2.1.2의 `ReadUsageWithSkippedAsync`는 알 수 없는 enum 이름·숫자 문자열 등으로 `FormatException`이 발생한 행을 건너뛰고 건수를 돌려줍니다. Statistics는 제외 건수를 표시하고 읽은 행만으로 통계를 계산합니다. 원본 행을 지우거나 모든 종류의 DB 손상을 복구하지는 않습니다. 읽을 수 없는 공식 상태 캐시 행도 건너뛰고 다음 조회를 사용합니다.

실측 INSERT의 열과 값은 `UsageColumns` 한 목록으로 짝을 맞춥니다. DB 구조를 바꾸는 작업이 아니라 저장 코드의 중복을 줄인 것입니다.

## 8. 통계는 AI가 판단하나요?

아니요. [StatisticsAnalysis.cs](StatisticsAnalysis.cs)의 `StatisticsAnalysis`가 기록을 골라 세고 비율을 계산합니다. 기존 계산식을 그대로 UI 밖으로 옮겨 Windows와 Mac이 함께 사용하며, [StatisticsWindow.cs](StatisticsWindow.cs)는 Windows의 통계 화면만 맡습니다.

본문·기간·탭·표 제목·No data·소표본 안내는 [StatisticsText.cs](StatisticsText.cs)를 씁니다. Windows의 기존 “어디서 기록하는지” 문장은 호스트에서 그대로 전달하며 화면 배치와 계산식은 바꾸지 않습니다.

ChatGPT 기록 열 개 중 Slow가 두 개면 그 열 개 안에서 Slow는 20%입니다. **관찰하지 않은 작업까지 포함한 ChatGPT 전체의 속도 통계가 아닙니다.** 기존 `OpenAI`로 저장한 기록도 통계 화면에서만 ChatGPT로 표시하며 원래 값은 수정하지 않습니다.

- 최근 7일·30일은 지금부터 거슬러 올라간 7×24시간·30×24시간입니다.
- 통계 창도 앱과 같은 시계를 받으므로 테스트용 시각을 넣었을 때 실제 PC 날짜가 섞이지 않습니다.
- 시간대와 요일은 한국 시각으로 분류합니다.
- 데이터가 없으면 0% 대신 No data입니다.
- n은 해당 행의 기록 수입니다.
- n < 30은 소표본 표시이며, 그 이상이면 결론이 확실하다는 보증이 아닙니다.

공휴일 ON/OFF/이전 미기록, 주말·공휴일, 정책별 FULL/BURGER를 분리해 봅니다. 같은 기록이 여러 비교 행에 등장할 수 있으므로 **여러 행의 n을 무조건 더하면 안 됩니다.**

통계로 시간표를 자동 변경하지는 않습니다. 추천 점수·히트맵·CSV/JSON 내보내기·자동 지연시간 측정도 아직 구현하지 않았습니다.

## 9. 왜 화면이 계속 반응하나요?

일을 종류별로 나눴기 때문입니다.

- 1초 타이머는 카운트다운과 표시를 갱신합니다.
- 인터넷은 기다리는 시간이 긴 별도 작업으로 처리합니다.
- SQLite는 화면 담당 스레드 바깥에서 실행하고 한 저장소 안에서는 순서를 지킵니다.
- 새 상태가 오면 `BeginInvoke`로 화면 담당에게 갱신을 요청합니다.

2.1.2는 화면 갱신 요청이 이미 대기 중이면 추가 요청을 합칩니다. 실행 시 최신 snapshot을 읽으며, 내용이 같은 Tooltip은 다시 설정하지 않습니다.

스레드는 “일을 수행하는 흐름” 정도로 이해하면 됩니다. 느린 일을 화면 담당에게 직접 시키지 않는 구조입니다. SQLite 호출 자체를 비동기 기능으로 바꾸는 것이 아니라, `Task.Run`을 통해 화면 바깥에서 실행합니다.

매초 HTTP 조회나 DB 저장을 하지는 않습니다. 아이콘도 문자·색상이 바뀔 때만 교체하고 이전 그림 자원은 해제합니다.

종료할 때는 타이머를 멈추고 트레이를 숨긴 뒤, 초기화·조회 취소·진행 중 저장의 마무리를 기다립니다. 그 뒤 창·통신·그림 자원을 정리합니다.

절전 복귀는 `SystemEvents.PowerModeChanged`로 받습니다. `TrayApplicationContext`가 구독하고 종료 때 해제합니다. 실제 전원 상태를 바꾸는 기능은 없습니다.

폰트와 직접 만든 기록 하위 메뉴도 소유한 쪽에서 정리합니다. 통신 객체는 앱이 만든 경우에만 해제하고, 테스트 등에서 전달받은 `HttpClient`는 만든 쪽에 맡깁니다. `StatusMonitor.Dispose`는 반복 호출을 허용합니다.

## 10. 자동 시작과 공식 페이지 클릭

[AutoStartManager.cs](AutoStartManager.cs)는 현재 실행 파일 경로와 Windows 허용 상태를 함께 봅니다.

- 등록 경로: `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.
- 허용 상태 참고: `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`.
- 이 앱의 값 이름: `AI Burger Clock`.

창이나 메뉴를 여는 것만으로 설정을 바꾸지 않습니다. 사용자가 체크하거나 메뉴를 눌렀을 때 현재 경로를 등록·해제합니다.

2.1.2는 자동 시작 설정 변경 실패 뒤 원상복구까지 실패하면 두 원인을 함께 표시합니다. 상태 창이 띄운 경고창 때문에 상태 창까지 자동으로 숨겨지지 않도록 잠시 숨김을 억제합니다.

Windows 내부 승인 형식은 알려진 경우만 해석합니다. 낯선 값은 억지로 수정하지 않습니다. **코드가 읽은 값과 실제 Windows 화면·재로그인 결과는 구분해서 검증해야 합니다.**

트레이 표시 목록의 옛 항목은 별도 이력입니다. 자동 시작 항목과 같은 것이 아니며, 다른 프로그램의 트레이 이력을 일괄 초기화하지 않습니다.

[ProviderStatusPages.cs](ProviderStatusPages.cs)는 공식 페이지 주소록입니다. 미리 정해 둔 HTTPS 주소를 Windows 기본 브라우저로 엽니다. 외부 공지의 아무 주소나 명령으로 실행하는 것이 아닙니다.

## 11. 전체 소스 파일 지도

이름을 눌러 소스를 열 수 있습니다. 역할을 알고 필요한 파일부터 읽으면 됩니다.

### 실제 앱 기능: 38개

| 파일 | 맡은 일 |
|---|---|
| [Program.cs](Program.cs) | 실행 입구, 검사 모드 분기, 중복 실행 방지 |
| [TrayApplicationContext.cs](TrayApplicationContext.cs) | 트레이·타이머·조회·저장·알림·절전 복귀·종료 연결 |
| [StatusWindow.cs](StatusWindow.cs) | 작은 상태 창, 클릭과 설정 변경 전달, 저장 오류 안내 |
| [MeasurementDialog.cs](MeasurementDialog.cs) | 기록 종류와 선택 메모 입력 |
| [StatisticsWindow.cs](StatisticsWindow.cs) | Windows 통계 창 |
| [StatisticsAnalysis.cs](StatisticsAnalysis.cs) | 두 OS가 함께 쓰는 통계 계산, 표본수와 No data |
| [DisplayFormatting.cs](DisplayFormatting.cs) | 두 UI가 함께 쓰는 카운트다운·UTC offset·간략 리셋 시간. 올림·이월 후 앞뒤 0 단위 생략 |
| [AppPaths.cs](AppPaths.cs) | Windows AppData / Mac Application Support의 앱 전용 경로 |
| [MacCliPaths.cs](MacCliPaths.cs) | Mac GUI의 제한된 PATH와 표준 설치 폴더에서 절대 CLI 경로 후보 생성 |
| [AgentSchedule.cs](AgentSchedule.cs) | 미국 업무시간과 다음 전환 계산 |
| [UsFederalHolidays.cs](UsFederalHolidays.cs) | 정기 연방 공휴일의 관측 날짜 계산 |
| [Phase2Models.cs](Phase2Models.cs) | 공통 상태·기록 카드·권고 규칙 |
| [ProviderStatusClient.cs](ProviderStatusClient.cs) | 공식 JSON을 읽고 관련 상태로 해석. 두 OS가 함께 쓰는 HTTP 연결 설정 |
| [ProviderStatusPages.cs](ProviderStatusPages.cs) | 공식 상태 페이지의 고정 주소와 열기 |
| [StatusMonitor.cs](StatusMonitor.cs) | 정기 조회, 실패 격리, 오래된 정보 판정 |
| [RecommendationNotifications.cs](RecommendationNotifications.cs) | 마지막 확정 권고 기억, 중복 알림 방지 |
| [TrayPresentation.cs](TrayPresentation.cs) | 트레이 문자·색상·짧은 도움말 결정. 두 OS가 함께 쓰는 전환·Provider 알림 문구 |
| [WindowsProviderNames.cs](WindowsProviderNames.cs) | Windows 화면·메뉴·기록·통계·Tooltip·알림에서 OpenAI를 ChatGPT로 표시. 저장 식별자와 공식 URL은 유지 |
| [ProviderNames.cs](ProviderNames.cs) | 두 OS의 공통 제품 표시 이름. 식별자·DB 값과 구분 |
| [StatusPanelModel.cs](StatusPanelModel.cs) | 시간표·Provider 상태 카드의 공통 본문·톤·상세 Tooltip. 빈 선택 항목 숨김 |
| [QuotaPanelModel.cs](QuotaPanelModel.cs) | 공통 사용량 제목·범위·사용률·간략 시간·조회 시각·톤·Provider별 하나의 상세. UI 색·박스·전환은 호스트가 결정 |
| [UsageMeasurementFactory.cs](UsageMeasurementFactory.cs) | 기록 메뉴 목록·상태 캡처·메모의 공백/문자 요소 경계 제한 |
| [FeedbackText.cs](FeedbackText.cs) | 기록·공휴일 피드백과 메모 잘림 안내 문구 |
| [StatisticsText.cs](StatisticsText.cs) | 통계 본문·기간·탭·표·주의사항의 공통 문구 |
| [StatusTicker.cs](StatusTicker.cs) | 매초 전환·Provider 알림·아이콘 변경 판정. UI 적용은 OS별 |
| [WindowsWarningLog.cs](WindowsWarningLog.cs) | 중복 Provider 경고의 Windows 파일 기록. 각 64 KiB, 이전 파일 1개, 실패 격리 |
| [NetworkRefreshScheduler.cs](NetworkRefreshScheduler.cs) | 네트워크 변화 뒤 5초 대기 후 재조회, 1분에 한 번 제한, 연속 변화는 마지막 변화 기준 한 번으로 합침. 대기 끝에 쓸 수 있는 연결(링크 로컬이 아닌 주소)이 없으면 건너뜀 |
| [UsageStore.cs](UsageStore.cs) | SQLite 생성·업그레이드·백업·설정·저장·일부 해석 불가 행 구분 |
| [QuotaJsonContext.cs](QuotaJsonContext.cs) | 한도 캐시를 JSON으로 읽고 쓰는 타입 정보를 빌드 때 생성. Mac trimming 검사와 기존 캐시 호환성 유지 |
| [AutoStartManager.cs](AutoStartManager.cs) | Windows 자동 시작 등록과 상태 판정 |
| [AccountQuotaModels.cs](AccountQuotaModels.cs) | 한도 Provider·기간·사용률·리셋 시각과 조회 약속 |
| [AccountQuotaParsers.cs](AccountQuotaParsers.cs) | 서로 다른 공식 CLI JSON을 검증한 공통 한도로 변환 |
| [AccountQuotaClient.cs](AccountQuotaClient.cs) | 표준 경로의 native CLI 실행, 제한시간·출력 크기·취소·응답 검증. 2.3.0의 Gemini는 agy 1.3.1 이상·2.0 미만 사전 확인 후 `/usage`, 기존 hooks·MCP 설정 격리 제한 있음 |
| [AccountQuotaPolicy.cs](AccountQuotaPolicy.cs) | 6시간·1시간·잔여 0%의 15분·리셋 전후 5분 규칙, 리셋 15분 전 진입, 실패 재시도 상한 계산 |
| [AccountQuotaMonitor.cs](AccountQuotaMonitor.cs) | Provider별 독립 조회 루프, 마지막 성공값·실패 횟수·다음 조회·재시작 캐시. 2.3.0은 Gemini도 포함 |
| [AccountQuotaView.cs](AccountQuotaView.cs) | 공통 모델을 그대로 바인딩하는 한도 보기. Provider별 흰 박스·사용률 막대·상세 연결·내부 스크롤과 상태 전환 유지 |
| [QuotaBalanceBar.cs](QuotaBalanceBar.cs) | Windows 네이티브 사용률 막대. 서비스별 색과 이전/경과 값의 회색, 원본 사용률·접근성 바인딩 |
| [QuotaDetailPopup.cs](QuotaDetailPopup.cs) | Windows 비활성화 상세 팝업. 흰색·고정 폭·줄바꿈·포인터 근처/화면 경계 보정·긴 상세 세로 스크롤 |

### 검사와 진단: 33개

미완성 임시 코드가 아닙니다. **특별한 검사 명령 때만 쓰는 정식 검사 코드**입니다.

| 파일 | 확인하는 것 |
|---|---|
| [SelfTest.cs](SelfTest.cs) | SharedTestSuite를 한 번 실행한 뒤 Windows 전용 검사를 더해 결과 반환 |
| [SharedTestSuite.cs](SharedTestSuite.cs) | 공통 검사 실행 목록 한 곳. Windows와 Shared.Tests가 RunAllAsync를 각각 한 번 호출 |
| [ScheduleTests.cs](ScheduleTests.cs) | 공휴일 OFF 시간표, DST, 정확한 경계 |
| [HolidayScheduleTests.cs](HolidayScheduleTests.cs) | 공휴일, 대체휴일, 연도 경계, 연장 구간 |
| [TrayPresentationTests.cs](TrayPresentationTests.cs) | Windows 표시 이름 어댑터와 네이티브 아이콘 픽셀 |
| [SharedTrayPresentationTests.cs](SharedTrayPresentationTests.cs) | OS 무관 색상·F/B·권고·알림 문구·짧은 도움말 조합 |
| [StatusTickerTests.cs](StatusTickerTests.cs) | 입력 이유·전환·알림 우선 순서·아이콘 변경·중복 Provider 판정 |
| [WindowsWarningLogChecks.cs](WindowsWarningLogChecks.cs) | 임시 파일로 경고 크기·회전·Unicode·쓰기 실패 격리 검사 |
| [PanelModelTests.cs](PanelModelTests.cs) | 공통 상태·한도 본문과 톤, 상세 Tooltip 골든 검사 |
| [QuotaPresentationTests.cs](QuotaPresentationTests.cs) | 공통 사용률·간략 시간·0 단위 생략·리셋 경계·Provider 상세 74건. 공통 실행 목록에 한 번 등록 |
| [RecordingTests.cs](RecordingTests.cs) | 공통 기록 캡처·메뉴·메모 제한·피드백 골든 검사 |
| [StatisticsTextTests.cs](StatisticsTextTests.cs) | 공통 통계 본문·표 제목·기간·주의 문구 골든 검사 |
| [ProviderStatusTests.cs](ProviderStatusTests.cs) | 정상·장애·잘못된 JSON·통신 실패의 해석 |
| [MonitorTests.cs](MonitorTests.cs) | 권고, 갱신, STALE, 취소, 알림 기억. 가짜 HTTP 응답 담당도 포함 |
| [StorageTests.cs](StorageTests.cs) | 임시 DB 저장·재열기·빈 기록·1만 건·통계 |
| [HolidayStorageTests.cs](HolidayStorageTests.cs) | 이전 DB 보존·WAL 백업·설정·업그레이드 실패 |
| [HolidayMonitorTests.cs](HolidayMonitorTests.cs) | 백그라운드 저장도 같은 공휴일 정책을 쓰는지 |
| [AutoStartTests.cs](AutoStartTests.cs) | 가짜 경로·승인값으로 자동 시작 판정만 검사 |
| [SmokeTest.cs](SmokeTest.cs) | 실제 WinForms 실행 흐름의 검사 입구 |
| [SmokeDiagnostics.cs](SmokeDiagnostics.cs) | smoke 실패 직전/시점의 창·버튼·Click·Provider별 가짜/monitor 조회와 경과 시간을 표준 오류의 DIAG JSON으로 기록. 일반 앱 실행 로그와 구분 |
| [UIRegressionChecks.cs](UIRegressionChecks.cs) | 클릭·새로고침·기록·메모·통계 연결. 한도 모드에서 실제 잘림 메모 저장·박스·피드백·상태 복귀 통합 검사 |
| [HolidayUiChecks.cs](HolidayUiChecks.cs) | 공휴일 옵션·색상·카운트다운·기록·알림 연결 |
| [LiveStatusProbe.cs](LiveStatusProbe.cs) | 공식 상태를 실제 인터넷으로 조회해 출력 |
| [AccountQuotaTests.cs](AccountQuotaTests.cs) | 한도 파서·잘못된 값·리셋 경계·조회 주기 |
| [AccountQuotaClientTests.cs](AccountQuotaClientTests.cs) | Windows 전용 CLI 경로 예시 |
| [SharedQuotaProtocolTests.cs](SharedQuotaProtocolTests.cs) | POSIX 경로·가짜 CLI 입출력·명령·크기 제한·취소·0턴 검증 |
| [GeminiQuotaTests.cs](GeminiQuotaTests.cs) | 합성 agy 응답의 Gemini 그룹·5시간/주간·잔여율·리셋·잘못된 값 검사 |
| [GeminiQuotaClientTests.cs](GeminiQuotaClientTests.cs) | 합성 입출력으로 agy 고정 명령·버전·0턴/0토큰·인증 대기·출력 제한 검사. 설치된 CLI는 실행하지 않음 |
| [AccountQuotaMonitorTests.cs](AccountQuotaMonitorTests.cs) | 실패 격리·조회 합치기·종료·SQLite 캐시·재시작 |
| [AccountQuotaUiChecks.cs](AccountQuotaUiChecks.cs) | 가짜 한도로 창 전환·사용률/막대·대표색·실패·공통 상세/접근성·Refresh·기존 상태 복귀 |
| [QuotaToolTipUiChecks.cs](QuotaToolTipUiChecks.cs) | 실제 WinForms에서 상세 고정 폭·흰색·줄바꿈·포인터 배치·닫힘·DPI·긴 상세 스크롤 검사 |
| [LiveQuotaProbe.cs](LiveQuotaProbe.cs) | 공식 CLI 실제 계정 조회 결과 중 한도 정보만 출력 |
| [PortablePlatformTests.cs](PortablePlatformTests.cs) | 데이터 경로·IANA 시간대 경계·표시 형식·Mac CLI 경로 후보 검사 |

검사의 고정 날짜와 가짜 장애는 정답을 비교하기 위한 문제지입니다. 실제 시간표나 공식 상태를 그 값으로 고정하지 않습니다.

### 프로그램 명찰: 1개

[Properties/AssemblyInfo.cs](Properties/AssemblyInfo.cs)는 프로그램 식별 정보 일부를 담습니다. 버전은 여기 아닌 프로젝트 파일에서 관리하고, 나머지 정보는 SDK가 생성합니다.

### Mac 호스트: 7개 / 공통 검사 입구: 1개

| 파일 | 맡은 일 |
|---|---|
| [Mac/Program.cs](Mac/Program.cs) | Mac 실행 입구, 중복 실행 잠금, 임시 DB native smoke 분기 |
| [Mac/MacApplication.cs](Mac/MacApplication.cs) | AppKit 메뉴바와 공통 조회·저장·알림·복귀·종료 연결 |
| [Mac/MacStatusIcon.cs](Mac/MacStatusIcon.cs) | 20-point 상태색 원과 흰색 F/B의 1x·2x 이미지 생성. native smoke에서 실제 색·투명도·글자 픽셀 확인 |
| [Mac/MacStatusPanel.cs](Mac/MacStatusPanel.cs) | 공통 본문을 표시하는 AppKit 상태·한도 팝오버와 카드 기록 메뉴 |
| [Mac/MacControls.cs](Mac/MacControls.cs) | Mac 네이티브 컨트롤·글꼴·톤 색·카드 표현 도우미 |
| [Mac/MacStatisticsWindow.cs](Mac/MacStatisticsWindow.cs) | 공통 계산 결과를 보여주는 Mac 통계 창 |
| [Mac/MacServices.cs](Mac/MacServices.cs) | macOS 알림 권한과 로그인 항목 등록 |
| [Shared.Tests/Program.cs](Shared.Tests/Program.cs) | OS UI 없이 SharedTestSuite.RunAllAsync를 한 번 실행하는 입구 |

## 12. 빌드 파일과 폴더 지도

| 파일·폴더 | 쉬운 설명 |
|---|---|
| [AiBurgerClock.csproj](AiBurgerClock.csproj) | 제작 사양서. WinForms, net10.0-windows, x64, 버전, SQLite 패키지 지정 |
| [AiBurgerClock.sln](AiBurgerClock.sln) | Visual Studio에서 여는 프로젝트 묶음 |
| [global.json](global.json) | SDK 범위. 10.0.100 기준에서 같은 10.0의 최신 기능 밴드 허용 |
| [build.ps1](build.ps1) | 빌드와 자체 검사를 실행하는 순서 |
| [Portable.pubxml](Properties/PublishProfiles/Portable.pubxml) | 배포용 단일 EXE 설정 |
| [app.manifest](app.manifest) | Windows 권한·호환 설정. 관리자 권한으로 자동 상승하지 않음 |
| [Shared/SharedSources.props](Shared/SharedSources.props) | 공통 C# 원본 27개를 Mac과 검사 프로젝트에 연결하는 목록. 검사 실행 목록은 SharedTestSuite에 별도 관리 |
| [Shared.Tests/AiBurgerClock.Shared.Tests.csproj](Shared.Tests/AiBurgerClock.Shared.Tests.csproj) | net10.0 공통 검사, 임시 DB와 가짜 HTTP·CLI 사용 |
| [Mac/AiBurgerClock.Mac.csproj](Mac/AiBurgerClock.Mac.csproj) | native AppKit, net10.0-macos27.0, osx-arm64, preview 버전과 bundle 버전(`ApplicationDisplayVersion`/`ApplicationVersion`) 지정 |
| [Mac/tools/make-app-icon.swift](Mac/tools/make-app-icon.swift) | Mac 앱 아이콘 10개 크기를 코드로 생성해 `Mac/Assets.xcassets/AppIcon.appiconset`에 저장. 아이콘을 바꿀 때만 실행 |
| [Mac/Info.plist](Mac/Info.plist) | Mac 앱 식별자·메뉴바 앱 설정·최소 OS. 버전은 적지 않음(증분 빌드에 반영되지 않음) |
| [Mac/build.sh](Mac/build.sh) | 진행 단계·도구 오류 표시 → 공통 검사 → Mac Release 빌드 → bundle 서명·SQLite 포함 확인 |
| bin | 일반 빌드 결과 |
| obj | 중간 결과와 자동 생성 코드 |
| dist | 사용자에게 전달할 배포 결과 |
| artifacts | 검사 출력과 화면 렌더 |

빌드는 경고도 실패로 취급합니다. `-Publish`는 배포 파일을 갱신하므로 실행 중인 앱을 먼저 종료해야 합니다.

단일 EXE여도 모든 .NET 부품이 들어 있다는 뜻은 아닙니다. **.NET 10 Desktop Runtime x64는 별도로 필요**합니다. SQLite 네이티브 부품은 포함하고 실행 시 임시 위치에 풀릴 수 있습니다. WinForms 호환성을 위해 코드 잘라내기(trimming)나 NativeAOT는 사용하지 않습니다.

### Windows와 Mac은 무엇을 같이 쓰나요?

시간표·Provider 파서·권고·조회 주기·저장·통계·패널 본문/톤·기록 생성·매초 판정의 원본은 루트에 하나만 둡니다. Mac과 공통 검사는 `SharedSources.props`로 그 파일들을 링크해 컴파일합니다. 같은 파일을 두 벌로 복사하거나 WinForms를 다른 프레임워크로 바꾸지 않습니다. 기록·공휴일·시작·종료 흐름은 호스트에 남으며 7b·7c 공통화는 [결정 13](MACOS_UI_PLAN.md#4-4-7b7c-재판단-2026-10-09-7a-완료-뒤)에 따라 보류합니다.

개발도 역할을 나눕니다. Mac 구현·네이티브 빌드/실행 검증은 사용자 Mac의 Claude 로컬 환경에서, Windows 검토·빌드/회귀 검증은 사용자가 요청할 때 이 환경에서 진행합니다. 상대 OS 전용 파일은 각 담당에게 맡기며 **공통 원본은 양쪽 모두 PR로 수정**할 수 있습니다. PR에는 바뀐 공통 파일·동작·상대 OS 확인 항목을 적고 Mac 검사와 Windows 회귀 결과를 구분합니다. OS UI 변경은 다른 UI에 자동 복제되지 않으며 DB·로그인도 동기화되지 않습니다. GitHub로 코드와 검증 기록을 연결하는 흐름이지 자동 감시나 다른 Claude 세션으로의 직접 명령 전달을 설정한 것은 아닙니다. [자세한 분담 및 검증 규칙](AGENTS.md#작업-분담-windows와-mac).

Windows는 원래 `net10.0-windows` 프로젝트를 유지하고 Mac·공통 검사·진단 폴더의 C# 파일은 제외합니다. Mac은 `net10.0-macos27.0` AppKit 호스트입니다. 새 공통 DLL·MAUI·WebView·Electron·Node 사이드카는 추가하지 않았습니다.

Mac의 시간대 ID는 `America/New_York`, `America/Los_Angeles`, `Asia/Seoul`입니다. Windows의 기존 ID는 유지합니다. 두 경우 모두 OS가 제공하는 `TimeZoneInfo` 규칙으로 각 미국 업무 경계를 UTC로 계산합니다. KST 시간표를 별도로 복사하지 않습니다.

Mac은 `~/Library/Application Support/AIBurgerClock`에 별도 DB를 쓰고 `SMAppService.MainApp`으로 로그인 항목을 관리합니다. CLI는 절대 실행 경로와 실행 권한을 확인하고, shell 프로필·Keychain·인증 파일은 읽지 않습니다. 기능 규칙을 함께 써도 **두 컴퓨터의 DB와 로그인은 자동 동기화되지 않습니다.**

Windows 배포 EXE는 2.4.0입니다. #45는 공통 표시 계약·Windows UI, #46은 Mac 0.4.0 UI 후속으로 main에 병합됐습니다. 이번 2.4.0 배포 PR은 Windows 버전·문서만 바꾸며 공통 원본이나 Mac 버전은 변경하지 않습니다. `MacCliPaths`는 GUI 앱의 PATH와 `~/.local/bin`, `/opt/homebrew/bin`, `/usr/local/bin`에서 실행 가능한 공식 명령을 찾으며 앱이 인증 파일을 직접 읽지는 않습니다. 현재 Mac 빌드·아이콘·실기 검증은 [Mac 안내](Mac/README.md)·[Mac 기록](MACOS_PORT.md)에서 관리합니다. 기존 Mac 결과를 이번 Windows 배포에서 다시 실행한 검사로 적지 않습니다.

Mac의 상태 UI는 `MacStatusPanel`과 `MacControls`를 쓰는 팝오버입니다. Windows는 `StatusWindow` 안에서 ‘한도 보기 ↔ 상태 보기’로 카드 영역을 전환합니다. 한도 박스·제목 기준선·클릭되는 카드만 hover라는 규칙은 [결정 9](MACOS_UI_PLAN.md)에 따르며, Mac 한도 항상 표시와 Windows 전환 방식의 차이는 결정 10에 남깁니다. 두 호스트의 실제 창 배치·픽셀 검사는 각각의 native smoke가 맡습니다.

Mac 빌드 스크립트는 일반 사용자로 실행하고, 기본 NuGet HTTP 캐시는 Git에서 제외한 `artifacts/mac-build/nuget-http-cache`에 둡니다. 이 설정은 빌드와 그 자식 프로세스에만 적용됩니다. 기존 사용자 캐시의 권한·전역 설정·취약성 검사는 바꾸지 않습니다. 앱 자체의 DB 경로나 실행 기능과도 별개입니다.

Xcode·SDK·workload 확인 단계는 바로 표시하고, 조회가 실패하면 원래 오류와 종료 코드 2를 돌려줍니다. 전체 Xcode 대신 CommandLineTools가 선택된 경우에도 오류를 숨기지 않습니다. 설치된 Xcode를 이번 빌드에서만 쓰려면 호출할 때 `DEVELOPER_DIR`를 지정하며, 스크립트는 전역 개발 도구 선택이나 라이선스를 변경하지 않습니다. [실행별 경로 지정](Mac/README.md#출력-없이-종료--commandlinetools-선택).

한도 캐시의 `QuotaJsonContext`는 빌드할 때 JSON 타입 정보를 미리 준비합니다. 실행 중 타입을 찾아내는 reflection에 의존하지 않아 Mac의 IL2026 검사를 피할 수 있습니다. JSON 필드·숫자 enum·리셋 시각·SQLite schema 2와 metadata 키는 그대로이며, 기존 캐시 읽기와 재시작 호환성을 따로 검사합니다. 경고를 숨기거나 새 패키지를 추가한 변경이 아닙니다.

## 13. 검사는 어떻게 실행하나요?

일반 사용자는 건너뛰어도 됩니다. 문서만 읽으려면 명령을 실행할 필요가 없습니다.

프로젝트 폴더의 PowerShell에서 자체 검사를 하려면:

~~~powershell
$exe = (Resolve-Path '.\dist\win-x64\AI Burger Clock.exe').Path
New-Item -ItemType Directory -Force '.\artifacts' | Out-Null
$result = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput '.\artifacts\self-test.txt' -RedirectStandardError '.\artifacts\self-test-errors.txt'
Get-Content '.\artifacts\self-test.txt'
$result.ExitCode
~~~

마지막 종료 코드 0은 성공입니다. 이 명령은 이전 자체 검사 로그를 갱신합니다. Windows 앱은 일반적인 명령줄 종료 코드 변수만 보면 잘못 판단할 수 있어 실제 프로세스를 기다립니다.

| 옵션 | 하는 일 | 실제 환경에 미치는 영향 |
|---|---|---|
| `--self-test` | 시간표·공휴일·파서·권고·저장·통계 검사 | 임시 DB·가짜 통신. 사용자 DB·자동 시작 등록은 변경하지 않음 |
| `--smoke-test` | 실제 창·트레이·알림·버튼 연결 검사 | 테스트 UI·알림을 만들 수 있음. 임시 DB·가짜 통신. 실행 중인 일반 앱을 먼저 종료해야 함 |
| `--smoke-test --verify-autostart` | 자동 시작 UI까지 검사 | **실제 사용자 레지스트리의 이 앱 값을 잠시 변경 후 복원. 일반 사용 중 실행하지 않는 개발 전용 검사** |
| `--smoke-test --report-directory 경로` | 검사 중 창을 PNG로 저장 | 지정 폴더에 이미지 생성. 바탕화면 전체 스크린샷은 아님 |
| `--check-providers` | 세 공식 소스를 지금 조회해 출력 | 실제 인터넷 사용. 사용자 DB에 기록하지 않음 |
| `--check-quotas` | 로그인된 Codex·Claude·agy CLI에서 한도만 조회 | 실제 계정 조회. 모델 요청·리셋권 사용·사용자 DB 저장 없음. CLI가 자체 인증을 관리 |

검사는 가짜 현재 시각을 전달하므로 Windows 시스템 시계를 바꾸지 않습니다. UI 검사에서 공식 페이지 열기는 실제 브라우저 대신 주소를 받는 함수로 확인합니다.

검사 통과와 실제 재부팅 성공, 사용자 화면의 알림 노출은 다른 증거입니다. 최신 준비/최종 검사·사용자 화면 확인·교체 결과는 [2.4.0 배포 기록](MAINTENANCE_2_4_0.md), 이전 배포 검증은 [2.3.0 기록](MAINTENANCE_2_3_0.md)·[2.2.4 기록](MAINTENANCE_2_2_4.md)·[2.2.3 기록](MAINTENANCE_2_2_3.md)·[2.2.2 기록](MAINTENANCE_2_2_2.md)·[2.2.1 기록](MAINTENANCE_2_2_1.md)을 참고하세요.

2.4.0 준비 후보에서 `build.ps1` 경고 0·오류 0, Windows self-test **251,451건**(공통 251,144 + Windows 전용 307)·실제 종료 코드 0, **smoke 5회 모두 387건·종료 코드 0**을 확인했습니다. 같은 PC에서 기존 dist 2.3.0 self-test도 직접 실행해 **251,377건·종료 코드 0**, 차이 **+74건**을 확인했습니다. 새 그룹 외에는 검사 수가 같고 공통 조회 주기·파서와 Windows 검사는 유지됩니다. 승인 뒤 병합 main `296f524`의 `build.ps1 -Publish`도 경고 0·오류 0, 최종 단일 EXE self-test **251,451건·종료 코드 0**, 별도 smoke **1회 387건·종료 코드 0**입니다. 정상 종료·WAL 확인·기존 2.3.0 백업 뒤 교체했고 자동 시작 등록은 그대로입니다. 최종 배포본의 트레이·상태 창·세 Provider 사용량 막대·상세 정상은 **사용자 확인**이며 실제 계정 값은 게시하지 않습니다. 합성 응답성·PNG·실제 화면 확인과 미수행 항목은 [2.4.0 기록](MAINTENANCE_2_4_0.md)에 구분합니다.

공통 검사만 실행하려면 OS와 관계없이 다음 명령을 사용합니다. 가짜 응답과 임시 DB만 사용하며 계정·사용자 DB·자동 실행 설정은 건드리지 않습니다. Windows WinForms 검사를 대체하는 것은 아닙니다.

~~~sh
dotnet run --project Shared.Tests/AiBurgerClock.Shared.Tests.csproj -c Release --property:TreatWarningsAsErrors=true
~~~

Mac native smoke는 실제 Mac에서만 실행하며 Windows의 옵션을 그대로 지원하지 않습니다. [Mac 검사 안내](Mac/README.md#안전한-네이티브-검사)를 따르세요. Windows에서 Mac 참조 DLL을 사용한 C# 컴파일은 실제 `.app` 빌드·실행 검사와 구별합니다.

## 14. 어디부터 읽으면 좋을까요?

- **앱 전체 흐름:** Program → TrayApplicationContext.
- **왜 지금 FULL/BURGER인지:** AgentSchedule → UsFederalHolidays → ScheduleTests / HolidayScheduleTests.
- **왜 ChatGPT만 STOP인지:** ProviderStatusClient → Phase2Models → RecommendationNotifications. WindowsProviderNames는 표시만 바꾸며 판정은 바꾸지 않습니다.
- **왜 트레이가 이 색인지:** TrayPresentation → TrayApplicationContext.
- **기록이 어떻게 쌓이는지:** Phase2Models → UsageStore → StatisticsAnalysis → OS별 통계 창.
- **버튼이 하는 일:** StatusWindow → TrayApplicationContext.
- **잔여량 조회와 다음 갱신:** AccountQuotaClient → AccountQuotaParsers → AccountQuotaMonitor / AccountQuotaPolicy → QuotaPanelModel → AccountQuotaView. 사용자 실측 통계와는 별개이며 캐시는 UsageStore의 AppMetadata에 Provider별 한 항목을 사용합니다. 2.3.0의 Gemini 연결은 기존 schema 2를 유지하며 `AccountQuota.v1.Gemini`만 추가합니다.

색상은 표시 문제, 공휴일은 시간표 정책, 경험 기록은 관찰 데이터, 추천 점수는 데이터 해석 문제입니다. 이 구분이 작은 앱의 가장 중요한 구조입니다.
