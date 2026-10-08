# macOS 팝오버 UI와 공통 표시 로직 — 분석·계획

작성: 2026-10-08 KST. 상태: 구현 중. PR 1·2·3(#21·#22·#23)과 공통 검사 단일 실행(#25)은 병합됐고, PR 4는 #24로 진행 중입니다. 진행 중 조정은 [4-1](#4-1-진행-중-조정-2026-10-08)을 봅니다.

## Context
- 목표: macOS 메뉴바 UI를 Windows 트레이 패널과 같은 구성의 팝오버로 바꾸고, Windows 변경을 macOS에 쉽게 반영하는 구조를 만든다.
- 분석 기준: `main` = `1384177`. Windows 2.2.3, Mac 0.1.5(build 6).
- 이 문서는 클라우드 세션에서 소스만 읽고 작성했습니다. 빌드·실행은 하지 않았습니다. "추정"으로 표시한 항목은 실제 Mac에서 확인이 필요합니다.

---

## 1) 현재 상태 요약

**macOS 버전의 위치**
- 별도 저장소·별도 브랜치가 아니라 **main의 `Mac/` 폴더**입니다. PR #12(10-03 병합)로 들어왔고 이후 #13~#19도 병합됨. `feature/macos-native`는 main에 모두 포함.
- 공통 코드는 별도 라이브러리가 아니라 `Shared/SharedSources.props`가 루트 원본 20개를 Mac 프로젝트·`Shared.Tests`에 **링크**하는 방식입니다. Windows는 루트 `*.cs` glob으로 같은 파일을 컴파일합니다.
- AGENTS.md 분담: Windows = ChatGPT(WinForms·Windows csproj), macOS = 사용자 Mac의 Claude Code(`Mac/`), 공통 원본은 양쪽이 PR로 수정.

---

## 2) 구조 분석과 공통 라이브러리 분리안

### 2-1. macOS 기술 스택
- `net10.0-macos27.0`, `osx-arm64`, .NET macOS 바인딩의 **네이티브 AppKit**(NSStatusItem·NSWindow·NSMenu·NSAlert). 알림 UserNotifications, 로그인 항목 SMAppService. Avalonia·MAUI·WebView 없음.
- NuGet은 `Microsoft.Data.Sqlite 10.0.12` 하나. 자체 서명(ad-hoc), 샌드박스 없음.

### 2-2. 코드 분류

| 구분 | 파일 |
|---|---|
| 공유(링크 20개) | AgentSchedule, UsFederalHolidays, Phase2Models, ProviderStatusClient, ProviderStatusPages, StatusMonitor, UsageStore, QuotaJsonContext, AccountQuota{Models,Client,Parsers,Policy,Monitor}, RecommendationNotifications, NetworkRefreshScheduler, TrayPresentation, StatisticsAnalysis, DisplayFormatting, AppPaths, MacCliPaths |
| Windows 전용 | Program, TrayApplicationContext, StatusWindow, AccountQuotaView, StatisticsWindow, MeasurementDialog, AutoStartManager, WindowsProviderNames, LiveStatusProbe, LiveQuotaProbe, SelfTest·SmokeTest·UI 검사 |
| Mac 전용 | Mac/Program, MacApplication, MacStatusWindow, MacStatisticsWindow, MacStatusIcon, MacServices |

### 2-3. 복사해서 따로 고친 부분 (이미 어긋남)
화면과 무관한데 양쪽 호스트에 따로 있는 로직입니다. Windows 변경이 Mac에 안 넘어가는 주원인입니다.

| 로직 | Windows | Mac | 현재 차이 |
|---|---|---|---|
| Provider 표시명(OpenAI→ChatGPT) | `WindowsProviderNames` | `MacStatusWindow.ProviderName` | 같음(중복만) |
| 시간표 영역 문구 | `StatusWindow.UpdateStatus` | `MacStatusWindow.Update` | Windows는 "다음:"에 " · 주말+공휴일" 등 접미사·상세 Tooltip, Mac은 별도 줄. 공휴일+주말 겹치면 Mac은 공휴일만 |
| Provider 카드 문구·Tooltip | `StatusWindow.UpdateProviders` | `MacStatusWindow.Update` | 제목 공백·"↗" 차이, Tooltip 항목 수 차이 |
| 조회 시각 문구 | "최근 조회 시도 / 다음 조회 / 공식 상태 확인 중…" | "Checked: / Next check:" | 문구·조건 다름 |
| 저장 오류 표시 | 바뀔 때만 표시, 복구 시 지움 | 매초 덮어쓰고 지우지 않음 | 동작 다름 |
| 한도 문구·색 | `AccountQuotaView` | `MacStatusWindow.QuotaText` | 구분자, " KST", "이전 조회값" 표시, 잔여량별 색 유무 |
| 사용 경험 기록 | `TrayApplicationContext.RecordMeasurement` | `MacApplication.RecordAsync`/`SaveMeasurementAsync` | `UsageMeasurement` 생성 코드 복사. Windows는 메모 창 **전** 시각·상태 캡처, Mac은 **후**. 오류 문구 다름 |
| 기록 메뉴 항목 | `StatusWindow.CreateRecordingMenu` | `MacStatusWindow.RecordMenu` | 구조 같음 |
| 공휴일 토글 흐름·문구 | `SetHolidayAdjustmentAsync` | `ChangeHolidayAsync` | 문구 다름 |
| 통계 요약·설명 문구 | `StatisticsWindow` | `MacStatisticsWindow` | 문구 다름 |
| 시작·매초 갱신·종료 순서 | `TrayApplicationContext` | `MacApplication` | 큰 흐름 같음, 세부 다름 |

PR #14에서 HTTP 설정, 알림 문구, 네트워크 재조회 제한은 이미 공통으로 옮겼습니다.

### 2-4. 검사 공유 현황
- 공통 검사 파일 10개(Schedule, HolidaySchedule, ProviderStatus, Monitor, Storage, HolidayStorage, HolidayMonitor, AccountQuota, AccountQuotaMonitor, PortablePlatform)는 **이미 Windows `SelfTest`와 `Shared.Tests`(net10.0)가 함께 실행**합니다.
- Windows만 실행하는 것:
  - `AutoStartTests`: 레지스트리를 쓰므로 Windows 전용이 맞습니다.
  - `TrayPresentationTests`: 대부분 플랫폼 무관이지만 `StatusWindow.FormatRemaining`, `WindowsProviderNames`, System.Drawing 아이콘에 걸려 있습니다. 둘로 나누면 공통 부분을 함께 쓸 수 있습니다.
  - `AccountQuotaClientTests`: Windows 경로를 예시로 씁니다. OS별 예시로 나누면 공통으로 쓸 수 있습니다.
- Mac UI 검사는 `MacApplication.RunSmokeAsync`(앱 안 `--smoke-test`) 하나뿐입니다.

### 2-5. 분리 기준: "필수 로직은 공통, UI는 각 OS 네이티브"

**결론: 이 기준을 따릅니다.** 현재 구조도 이미 이 방향입니다. Windows는 WinForms, Mac은 AppKit으로 UI가 나뉘어 있고, 핵심 로직 20개는 공통입니다. 남은 문제는 **UI가 아닌 로직이 아직 각 UI 호스트 파일 안에 남아 있다는 점**입니다(2-3 표). 이것들을 공통 영역으로 내리고, UI 파일에는 "그리기와 OS 연동"만 남기는 것이 이번 분리의 범위입니다.

**경계 정의**

| 층 | 들어가는 것 | 위치 |
|---|---|---|
| A. 핵심 로직(이미 공통) | 시간표·DST·공휴일, 공식 상태 조회·판정, 권고, 계정 한도 조회·주기, SQLite, 통계 계산, 알림 문구, 네트워크 재조회 제한 | 루트 공통 원본 20개 |
| B. 앱 흐름 로직(지금 호스트에 중복, 공통으로 이동) | 시작 순서(DB 초기화→공휴일 설정 읽기→모니터 시작), 매초 갱신과 알림 판정, 공휴일 토글 저장 흐름, 사용 경험 기록 생성(`UsageMeasurement`)과 메모 제한, Refresh·절전 복귀·네트워크 재조회, 종료 순서 | 새 공통 원본 |
| C. 화면 상태 데이터(경계 영역, 결정 5-1) | 패널에 무엇을 보일지: 상태·권고·**톤(enum)**·카운트다운·조회 시각, 그리고 표시 문구 | 공통 또는 각 OS |
| D. 네이티브 UI(각 OS) | 레이아웃, 컨트롤, 색·글꼴, 클릭·우클릭·팝오버·트레이, 대화상자, 자동 실행(레지스트리/SMAppService), OS 알림 API, 아이콘 그리기 | Windows: `StatusWindow` 등 / Mac: `Mac/` |

**C층(표시 문구)의 판단: 공통에 두는 것을 권장합니다.**
- 공통에 둘 때:
  - 문구는 "정상", "GO", "다음 조회" 같은 **판정 결과를 사람이 읽는 말로 바꾼 것**입니다. 그래서 의미상 로직에 가깝습니다.
  - 지금 Mac과 Windows가 어긋난 지점 대부분이 바로 이 문구입니다(2-3 표). 공통에 두면 Windows 문구 변경이 Mac에 자동으로 따라옵니다.
  - 색은 공통에 두지 않고 톤 이름만 둡니다. Mac은 다크 모드용 시스템 색, Windows는 기존 RGB로 각자 매핑합니다.
- 각 OS에 둘 때:
  - OS 관례에 맞춘 문구(예: "로그인 시 자동 실행", 메뉴 항목 이름)를 자유롭게 쓸 수 있습니다.
  - 대신 지금처럼 문구가 계속 어긋나고, 변경을 손으로 옮겨야 합니다.
- 절충안:
  - 패널 본문 문구(시간표·카드·조회 시각·한도)는 공통으로 둡니다.
  - OS 고유 항목(자동 실행 문구, 메뉴 구성, 단축키, Tooltip/팝오버 방식)은 각 OS에 둡니다.
  - 이 안을 권장합니다.

**공통 영역의 형태(링크 vs 라이브러리)**
- 권장: 지금의 링크 방식(`Shared/SharedSources.props`)을 유지합니다.
  - `Shared.Tests`가 net10.0으로 공통 원본을 컴파일하므로, 공통 코드에 WinForms/AppKit이 섞이면 바로 빌드가 실패합니다. 기준이 이미 강제되고 있습니다.
- 실제 `AiBurgerClock.Core`(net10.0) 라이브러리로 바꾸려면 다음 비용이 듭니다(확인된 사실).
  - 공통 타입이 전부 `internal`이라 `InternalsVisibleTo` 또는 `public` 전환이 필요합니다.
  - `ProviderStatusClient`(34행)·`AccountQuotaClient`(207행)가 `typeof(...).Assembly` 버전을 User-Agent·CLI 클라이언트 버전으로 보냅니다. 라이브러리로 옮기면 라이브러리 버전이 나가므로, 호스트가 앱 버전을 넘기게 바꿔야 합니다.
  - Windows csproj(ChatGPT 담당) 변경과 단일 EXE publish 재검증이 필요합니다.
- 경계를 지키는 효과는 링크 방식과 같으므로, 라이브러리 전환은 마지막 선택 단계로 둡니다.

**분리 순서**
1. **B층 일부 + C층**: `ProviderNames`(표시명), `StatusPanelModel`·`QuotaPanelModel`(표시 데이터 + 톤 + 본문 문구, 현재 Windows 문구 기준 골든 검사), `UsageMeasurementFactory`(메모 창 **전에** 캡처하는 Windows 방식)와 메모 1,000자 제한.
2. **B층 나머지**: 새 공통 원본 `AppCoordinator`(가칭)로 옮깁니다.
   - 대상: 시작·갱신·공휴일 토글·기록 저장·종료 순서.
   - 호스트와는 이벤트/콜백으로 연결합니다. UI 호스트는 "표시해", "알림 띄워", "메모 물어봐"만 구현합니다.
   - Windows `TrayApplicationContext`를 크게 바꿔야 하므로 1단계 뒤에 별도로 진행합니다.
3. **통계 문구**: 요약·No data·설명 문구를 `StatisticsAnalysis` 쪽으로 옮깁니다.
4. **검사 정리**
   - `TrayPresentationTests`를 공통 부분과 Windows 아이콘 부분으로 나눕니다.
   - `AccountQuotaClientTests`는 OS별 예시로 나눕니다.
   - 새 공통 검사는 `Shared.Tests`와 Windows `SelfTest` 양쪽에 등록합니다. 그러면 Windows self-test의 공통 부분은 Mac에서도 같은 검사로 돌아갑니다.
5. (선택) `AiBurgerClock.Core` 라이브러리 전환.

**각 OS에 남는 것(분리 후)**
- Windows:
  - `StatusWindow`·`AccountQuotaView`·`StatisticsWindow`·`MeasurementDialog`(그리기만)
  - `TrayApplicationContext`(NotifyIcon·메뉴·Windows 이벤트 연결)
  - `AutoStartManager`, 아이콘 그리기
- Mac:
  - 팝오버 패널, 통계 창, 메모 NSAlert
  - NSStatusItem·메뉴, `MacStatusIcon`, `MacServices`(알림·SMAppService)

---

## 3) UI 구현 계획 (macOS)

### 3-1. 팝오버 구현 방식 (현재 AppKit 그대로 가능)
- **클릭 구분**
  - 지금은 `statusItem.Menu = menu`라서 왼쪽 클릭에도 메뉴가 뜹니다. `Menu`를 비우고 `statusItem.Button`에 Action과 `SendActionOn(LeftMouseUp | RightMouseUp)`을 지정합니다.
  - 동작 시점의 `NSApplication.SharedApplication.CurrentEvent`로 왼쪽·오른쪽을 구분합니다. control-클릭은 오른쪽으로 취급합니다.
- **왼쪽 클릭**: `NSPopover`를 `Behavior = Transient`로 띄우고 버튼 아래에 붙입니다. 바깥을 클릭하면 닫힙니다. Accessory 앱이라 표시 직전에 `NSApplication.Activate()`가 필요합니다(현재 `Show()`와 같은 방식).
- **오른쪽 클릭**: Refresh / 로그인 항목 설정 열기… / 종료(⌘Q)만 있는 짧은 NSMenu를 버튼 아래에 띄웁니다.
- **패널 내용**: 새 `MacStatusPanel`(NSViewController)이 Windows 순서 1)~8)을 `NSStackView`로 세로 배치합니다.
  - 현재 절대 좌표 방식(`MacStatusWindow`)을 대체합니다.
  - 기존 `MacStatusWindow`(NSWindow)와 "상태 창 열기" 메뉴는 제거합니다. 클릭했을 때만 표시합니다(결정 5-3).
  - 고정 표시가 필요한 경우는 **Mac 전용 데스크탑 위젯**으로 향후 별도 개발합니다(BACKLOG 등록 대상, 이번 범위 밖).
- **Provider 카드**
  - 둥근 배경 `NSBox`(Custom)에 시스템 배경색을 씁니다.
  - 카드 전체 왼쪽 클릭은 `NSClickGestureRecognizer`로 받아 `ProviderStatusPages.For`를 엽니다.
  - 카드 `NSView.Menu`에 기록 메뉴를 지정하면 AppKit이 오른쪽 클릭과 control-클릭을 자동 처리합니다.
- **문구·글꼴**: 자동 실행 문구는 "로그인 시 자동 실행", 글꼴은 시스템 글꼴을 씁니다.
- **색(결정 5-4)**: 색 바탕에 흰 글자를 쓰지 않습니다. **흰 바탕에 글자 색만 바뀌는 형태**로 합니다.
  - 바탕: 라이트 모드는 흰색, 다크 모드는 시스템 배경색입니다. `NSColor.ControlBackground` 계열 의미 색으로 자동 전환됩니다.
  - 글자색: 공통 톤(enum)을 Mac에서 색으로 매핑합니다. 다크 모드에서도 읽히도록 `SystemGreen`·`SystemOrange`·`SystemRed`·`SecondaryLabel` 사용을 권장합니다(추정, 실제 대비 확인 필요).
  - 적용 범위(결정 5-4): 팝오버의 상태 제목·Provider 카드·한도 행, 그리고 **메뉴바 아이콘**입니다. 메뉴바 아이콘은 지금 색 원 안에 흰 F/B입니다. 이를 흰(다크 모드는 어두운) 원 바탕에 색 글자 F/B로 바꿉니다.
- **메뉴바 아이콘 주의**
  - 메뉴바 밝기는 시스템 모드와 배경화면에 따라 바뀝니다. 그래서 아이콘을 외형(appearance)에 맞춰 다시 그려야 합니다.
  - 방법은 두 가지입니다(추정): `NSImage`를 그리기 핸들러로 만들어 외형마다 다시 그리게 하거나, 버튼의 `EffectiveAppearance` 변경을 감지해 다시 만듭니다.
  - 0.1.2에서 겪은 "비활성 메뉴바에 아이콘이 안 보이는 문제"가 재발하지 않는지 실제 Mac 두 화면에서 확인해야 합니다. 현재 `MacStatusIcon.VerifyImages` 픽셀 검사도 새 색 규칙에 맞게 바꿉니다.

### 3-2. 제약·주의 (추정 포함)
- **기록 메모 창**: transient 팝오버는 모달 NSAlert(기록 메모 창)이 뜨면 닫힙니다(추정). 메모 기록은 팝오버를 먼저 닫고 NSAlert를 띄운 뒤 저장 후 다시 여는 흐름으로 합니다. Windows는 저장 후 `ShowNearTray`로 다시 엽니다.
- **팝오버 크기 고정**: 현재 Mac 창은 430×720pt입니다. Windows 구성(카드 3개 + 버튼 + 체크박스 2개)은 약 380×560pt로 추정합니다(실측 필요). 한도 화면은 같은 영역 안에서 스크롤합니다.
- **듀얼 모니터**: 팝오버는 클릭한 메뉴바의 버튼에 붙습니다. 0.1.2에서 겪은 비활성 메뉴바 표시 문제와는 별개로 실제 확인이 필요합니다.
- **매초 갱신**: 팝오버가 열려 있을 때만 패널을 갱신하고, 같은 문구면 다시 쓰지 않습니다(Windows와 같은 방식). 이렇게 해야 한도 영역 스크롤과 선택이 유지됩니다.

### 3-3. "한도 보기"
- Mac에는 **전환 버튼이 없고 한도를 항상 표시**합니다.
- 필요한 작업:
  - 카드 영역과 한도 뷰를 같은 자리에서 전환합니다.
  - 버튼 문구 "한도 보기"↔"상태 보기", 안내 문구 교체(Windows 2.2.3 문구)를 맞춥니다.
  - 한도 행을 톤별 글자색으로 표시합니다(5-4 규칙).
  - 기존 ⓘ 팝오버 설명은 Windows처럼 Tooltip으로 옮깁니다(결정 5-5). 팝오버 안의 팝오버도 피할 수 있습니다.
- 한도 데이터(`AccountQuotaMonitor`)는 이미 공통입니다. 문구만 `QuotaPanelModel`로 맞추면 됩니다.

### 3-4. 패널 데이터가 Windows와 같은 로직에서 나오는지

| 요소 | 데이터 출처 | 차이 |
|---|---|---|
| 상태·전환까지·US 오프셋 | 공통(`AgentSchedule`, `TrayPresentation`, `DisplayFormatting`) | 같음. Mac은 "●" 없음 |
| 다음 전환 + 주말/공휴일 | 공통 snapshot, 문구는 각자 | 위 2-3 참조 |
| 권고·Official 라벨 | 공통(`RecommendationPolicy`) | 같음. 카드 제목 형식·Tooltip은 다름 |
| 조회 시각 | 공통(`StatusMonitor`) | 문구·조건 다름 |
| 한도 | 공통(`AccountQuotaMonitor`, `DisplayFormatting.ResetCountdown`) | 문구·색 다름 |
| 체크박스 | 공휴일은 공통 DB 설정, 자동 실행은 OS별 | 정상적인 차이 |

공통 모델(2-5의 1)을 쓰면 표의 "문구 다름"은 모두 없어지고, 색·글꼴만 OS별로 남습니다.

---

## 4) PR 단위와 순서

**작업 흐름(결정 5-2)**
- Windows·Mac 구분 없이, 먼저 개선안을 낸 쪽이 그 개선을 **공통 영역 PR**로 올립니다.
- 상대 플랫폼 담당은 그 PR을 받아 자기 UI에 맞춰 반영합니다.
- 반영할 수 없는 부분은 이유와 대안을 담아 "재검토 회신"으로 PR 코멘트에 남깁니다.
- 이 규칙은 AGENTS.md "작업 분담" 절에 추가합니다(아래 PR 0).

| # | PR | 작성 | 확인 |
|---|---|---|---|
| 0 | 문서 PR(이 문서 포함). 1) AGENTS.md에 위 작업 흐름·분리 원칙 추가 2) "별도 테스트 프로젝트는 없습니다" 문구를 Shared.Tests 현황에 맞게 수정 3) Mac 버전 규칙(csproj만 수정) 링크 4) BACKLOG에 이 작업과 "Mac 데스크탑 위젯(향후)" 추가 | 클라우드 세션 | 문서만 |
| 1 | 공통: `ProviderNames`, `StatusPanelModel`, `QuotaPanelModel`(데이터·톤·본문 문구) + 골든 검사(현재 Windows 문구). 아직 어느 호스트도 사용 안 함 | Mac 로컬 챗 | Shared.Tests, `-p:EnableWindowsTargeting=true` Windows 컴파일. PR 본문에 "공통 원본 변경"과 Windows 확인 요청 |
| 2 | Windows: `StatusWindow`·`AccountQuotaView`가 PR1 모델 사용. 화면 변화 없음 | ChatGPT | Windows self-test·smoke, 렌더 PNG 비교, 재검토 회신 |
| 3 | 공통(B층 일부): `UsageMeasurementFactory`, 메모 제한, 공휴일 피드백 문구, 기록 메뉴 항목 목록 | Mac 로컬 챗 | PR1과 같음 |
| 4 | Mac: 상태 아이콘 왼쪽 클릭→팝오버, 오른쪽 클릭→짧은 메뉴(Refresh/로그인 항목 설정 열기…/종료 ⌘Q), "상태 창 열기"와 상태 NSWindow 제거. 팝오버 안은 PR1 모델로 Windows 순서 배치. **(앞당김)** 카드 오른쪽/control-클릭 기록 메뉴(PR3 공통 코드)와 메모 흐름, 한도 ⓘ→줄별 Tooltip, 불투명 배경과 대비 4.5:1 이상의 톤 색 | Mac 로컬 챗 | build.sh, smoke(새 레이아웃 검사), 실제 클릭·바깥 클릭·듀얼 모니터 |
| 5 | Mac: 카드 **전체** 왼쪽 클릭(공식 페이지). PR 4는 Provider 이름 버튼만 공식 페이지를 엽니다. 기록 메뉴·메모 흐름은 PR 4로 옮겼습니다 | Mac 로컬 챗 | 실제 조작(카드 클릭·기록·메모 흐름) |
| 6 | Mac 0.2.0: "한도 보기" 전환, 메뉴바 아이콘의 흰 바탕+색 글자와 다크 모드 연동, MACOS_PORT 기록, 버전 0.2.0(`Version`·`AssemblyVersion`·`FileVersion`·`ApplicationVersion` 함께). ⓘ→Tooltip과 팝오버의 불투명 배경·톤 색은 PR 4로 옮겼습니다 | Mac 로컬 챗 | 라이트/다크·두 화면 스크린샷, 다크 모드 메뉴바의 아이콘 표시(바탕·글자 대비, 비활성 화면 포함), 아이콘 픽셀 검사 갱신 |
| 7 | 공통(B층 나머지): `AppCoordinator`(가칭)로 시작·갱신·공휴일 토글·기록 저장·종료 흐름 이동. 호스트는 아직 사용 안 함 | 먼저 착수하는 쪽 | Shared.Tests, Windows 컴파일 |
| 7w | Windows: `TrayApplicationContext`를 `AppCoordinator`로 전환 | ChatGPT | Windows self-test·smoke |
| 7m | Mac: `MacApplication`을 `AppCoordinator`로 전환 | Mac 로컬 챗 | build.sh, smoke |
| 8 | 공통 검사 정리: `TrayPresentationTests`의 공통 부분과 `AccountQuotaClientTests`의 OS별 예시를 새 공통 검사로 추가 | 먼저 착수하는 쪽 | Shared.Tests |
| 8w | Windows: 기존 `TrayPresentationTests`·`AccountQuotaClientTests`에서 옮긴 부분 정리, `SelfTest` 등록 | ChatGPT | Windows self-test |

- PR1이 들어가면 PR2(Windows)와 PR4~6(Mac)은 서로 기다리지 않고 함께 진행할 수 있습니다.
- PR7 앱 흐름 공통화는 범위가 커서 팝오버 작업 뒤로 둡니다(가정: 5-1 "권장안"에 포함된 것으로 해석).
- 각 담당은 상대 OS 전용 파일을 고치지 않습니다(AGENTS.md). 그래서 공통 PR과 Windows·Mac 전환 PR을 나눴습니다.
- 공통 영역은 링크 방식을 유지합니다(5-1). `AiBurgerClock.Core` 라이브러리 전환은 이번 계획에서 뺍니다.

### 4-1. 진행 중 조정 (2026-10-08)

계획과 달라진 점입니다. 위 표에는 이미 반영했습니다.

- **기록 메뉴를 PR 5에서 PR 4로 앞당김.**
  - 이유: PR 4에서 메뉴바의 기록 하위 메뉴와 상태 창 "기록" 버튼이 없어집니다. 카드 메뉴가 없으면 기록할 방법이 남지 않습니다.
  - PR 4 범위: 카드의 오른쪽/control-클릭 기록 메뉴(`UsageMeasurementFactory.MenuItems`), 메모 창 전 캡처(`Capture`/`WithNote`), 메모 흐름(팝오버 닫기 → NSAlert → 저장·취소 후 재오픈), `FeedbackText` 문구.
- **한도 ⓘ → Tooltip(결정 5-5)을 PR 6에서 PR 4로 앞당김.**
  - 이유: PR 4에서 한도 줄을 `QuotaPanelModel`로 다시 만들면서, 모델이 주는 줄별 상세를 바로 Tooltip으로 붙였습니다.
  - 매초 카운트다운이 갱신돼도 Tooltip이 닫히지 않는 것을 사용자가 확인했습니다.
- **팝오버 배경과 톤 색(결정 5-4의 팝오버 부분)도 PR 4에서 처리.**
  - 이유: 기본 팝오버 배경이 반투명이라, 뒤가 밝으면 시스템 녹색 글자가 거의 읽히지 않았습니다(흰 바탕에서 약 2:1).
  - 처리: 팝오버를 불투명 `ControlBackground`(라이트 흰색, 다크 시스템 어두운 색)로 칠합니다. 톤 색은 `Assets.xcassets`의 라이트/다크 이름 색으로 바꿔 양쪽 모드에서 4.5:1 이상을 맞췄습니다. smoke가 이 대비를 검사합니다.
- **남은 범위**
  - PR 5: 카드 전체 왼쪽 클릭으로 공식 페이지 열기(PR 4는 Provider 이름 버튼만 엶). 카드 클릭·기록·메모 흐름의 실제 조작 확인. Tooltip 첫 줄 "클릭: 공식 상태 페이지 열기"는 이때 실제 동작과 맞춰집니다.
  - PR 6: "한도 보기" 전환, 메뉴바 아이콘의 흰(다크 모드는 어두운) 원 바탕+색 글자 F/B와 픽셀 검사 갱신, 다크 모드·두 화면 확인, 0.2.0, MACOS_PORT 기록.

---

## 5) 결정 사항 (사용자 회신)
1. 표시 문구: 절충안. 패널 본문 문구는 공통, OS 고유 항목은 각 OS. 공통 영역은 링크 방식 유지.
2. 작업 흐름: 먼저 개선한 쪽이 공통 PR로 올리고, 상대가 자기 플랫폼에 맞춰 반영합니다. 반영할 수 없는 부분은 재검토 회신.
3. Mac은 클릭했을 때만 팝오버를 표시합니다. 고정 표시는 Mac 전용 데스크탑 위젯으로 향후 별도 개발.
4. 색: 흰 바탕에 글자 색만 바꿉니다. 다크 모드에서는 바탕만 시스템 다크 색으로 바뀝니다.
   - 메뉴바 아이콘에도 적용합니다(2026-10-08 확정). 흰(다크 모드는 어두운) 원 바탕에 색 글자 F/B입니다. 다크 모드 메뉴바에서의 표시는 PR6 확인 항목입니다.
5. 한도 ⓘ 설명은 Tooltip으로 옮깁니다.
6. Mac 버전은 0.2.0으로 올립니다.
7. Mac에 기존 로컬 챗은 없습니다. 구현은 **Mac에서 새 로컬 Claude Code 챗**으로 진행합니다.
8. 기록 메뉴 항목(2026-10-08, #23 리뷰 P1 2번): 메뉴 항목의 종류·순서·문구는 공통(`UsageMeasurementFactory.MenuItems` 등)에 둡니다. 메뉴를 만들고 띄우는 코드·단축키·위치·열리는 방식은 각 OS가 맡습니다. AGENTS.md "분리 원칙"의 예외로 적었습니다.

## 6) 진행 방법 (Mac 새 로컬 챗)
- Mac에서 저장소 폴더를 열고 `git switch main && git pull`로 최신 main을 받습니다. 기존 Mac 로컬 챗은 없으므로 새 Claude Code 챗을 엽니다.
- 이 문서와 [AGENTS.md](AGENTS.md), [Mac/README](Mac/README.md)를 먼저 읽고 4) 표의 PR 1부터 순서대로 진행합니다. PR마다 main에서 새 브랜치를 만듭니다.
- 공통 원본을 고친 PR은 본문에 "공통 원본 변경" 절을 두고, Mac에서 `dotnet build -c Release -warnaserror -p:EnableWindowsTargeting=true`로 Windows 컴파일까지 확인합니다. 실제 Windows 검사는 ChatGPT에 요청합니다.
- Mac UI PR은 `bash Mac/build.sh`와 `--smoke-test` 결과, 실제 화면 확인 범위를 PR과 [MACOS_PORT](MACOS_PORT.md)에 기록합니다.

## 검증(구현 단계 기준)
- Mac: `bash Mac/build.sh` 실행 후 `"…/AI Burger Clock.app/Contents/MacOS/AI Burger Clock" --smoke-test`. 그다음 실제 팝오버 열기·바깥 클릭·카드 클릭·우클릭 기록·한도 전환을 확인하고, 라이트/다크와 두 화면 메뉴바에서 확인합니다.
- 공통: `dotnet run --project Shared.Tests/AiBurgerClock.Shared.Tests.csproj -c Release --property:TreatWarningsAsErrors=true`, `dotnet build -c Release -warnaserror -p:EnableWindowsTargeting=true`.
- Windows: ChatGPT가 PR HEAD로 `build.ps1`, `--self-test`, `--smoke-test`를 실행하고 결과를 PR 코멘트로 남깁니다.
