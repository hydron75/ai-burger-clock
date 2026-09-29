# AI Burger Clock 2단계 — 재개 및 완료 기록

검증일: 2026-09-19 KST. 최종 버전 2.0.0. 이 문서는 이번 재개에서 복구·추가 검증한 결과를 중심으로 기록합니다.

## 1. 중단 지점 확인과 보존

- 현재 작업 폴더와 AiBurgerClock에는 .git이 없어 Git status/diff를 제공할 수 없었습니다. 새 Git 이력으로 기존 이력을 대체하지 않았습니다.
- 기존 기준점 ZIP과 SHA-256 비교를 사용했습니다: ../backups/AiBurgerClock-before-phase2-20260919-134234.zip.
- 기준점 SHA-256: **A21429BAEFA2F2319F24C3E432FA2565310DB49BBCDF16FEBFF357B59AB4E1AC**.
- 재개 첫 Release는 컴파일 가능한 상태였습니다. Schedule, Provider 파서, SQLite, UI/통계의 주요 파일은 이미 존재했습니다. 이들을 다시 작성하거나 .NET Framework로 되돌리지 않았습니다.
- 중단된 부분은 새 기능을 실행하는 통합 검증 진입점, 실제 공식 feed 재검증, UI/알림/DB 연동 검증, 최종 publish와 문서화였습니다. 기존 SelfTest는 이전 고정 KST 정책만 검사하고 있었습니다.
- SDK Compile 항목을 실제 평가하여 총 20개 C# 파일 포함을 확인했습니다. 새 파일 누락, 패키지만 추가된 상태, UI 이벤트 미연결, 일부 Provider 미구현은 최종 검사에서 발견되지 않았습니다.
- 소스의 TODO / FIXME / NotImplementedException / placeholder / stub 검색 결과 없음. 테스트 파일은 명시적 검사 명령 전용이며 정상 사용에서 실행되지 않습니다.
- sources/ 및 지시 파일은 변경하지 않았습니다.

## 2. 이번 재개에서 복구·보완한 것

1. SelfTest를 새 Schedule/Provider/Monitor/SQLite/Statistics 검사와 연결했습니다.
2. StatusMonitor의 STALE 재계산 시 원래 조회 실패 이유가 덮이는 문제를 보완했습니다.
3. 1초 Schedule 타이머가 네트워크 완료 콜백보다 먼저 실행될 때 Provider 권고 변화를 알림 없이 소비할 수 있던 경쟁 조건을 수정했습니다.
4. OpenAI summary가 incidents를 생략하는 실제 응답을 발견했습니다. incidents.json 추가 조회로 보완하고 구조가 잘못되면 UNKNOWN을 유지합니다. 누락을 무조건 정상으로 처리하지 않습니다.
5. 실제 WinForms 메시지 루프에서 Refresh, 기록 메뉴, 메모 저장, 통계 기간 변경, Provider 알림과 종료를 검증하는 UIRegressionChecks를 추가했습니다.
6. 통계·메모 창의 150% DPI 배율 문제를 고쳤습니다. 통계 설명/Provider 열 잘림을 화면 렌더로 확인하고 레이아웃 검사를 추가했습니다.
7. LiveStatusProbe, 실제 DB 읽기 전용 점검, publish EXE 검증, README/공식 source 문서를 완성했습니다.

## 3. 변경 파일과 프로젝트 구조

기준점 대비 수정한 기존 파일:

- AgentSchedule.cs — 미국 업무일 UTC 구간, DST 메타데이터, 다음 경계.
- AiBurgerClock.csproj — 버전 2.0.0, SQLite 의존성, 설명.
- Program.cs — 진단 진입점, UTF-8 검사 출력.
- SelfTest.cs / SmokeTest.cs — 통합 검사와 임시 DB 기반 실제 UI 검사.
- StatusWindow.cs / TrayApplicationContext.cs — 작은 Provider UI와 실측/통계/갱신/알림 연결.
- README.md — 2단계 사용·배포·검증 안내.

새 파일:

- Phase2Models.cs, ProviderStatusClient.cs, StatusMonitor.cs
- UsageStore.cs, MeasurementDialog.cs, StatisticsWindow.cs
- ScheduleTests.cs, ProviderStatusTests.cs, MonitorTests.cs, StorageTests.cs, UIRegressionChecks.cs
- LiveStatusProbe.cs, PROVIDER_SOURCES.md, PHASE2.md

AutoStartManager.cs, 매니페스트, 솔루션, 기존 배포 프로필, .NET 마이그레이션 기록은 유지했습니다.
기존 간단한 단일 WinForms 프로젝트 구조를 유지하고 서비스 컨테이너, ORM, WebView, 별도 서버를 추가하지 않았습니다.

- TargetFramework: net10.0-windows
- UseWindowsForms: true
- x64 / win-x64, nullable 및 implicit usings 활성화
- 직접 NuGet: Microsoft.Data.Sqlite 10.0.12
- 전이 런타임 의존성: Microsoft.Data.Sqlite.Core 10.0.12 및 SQLitePCLRaw 2.1.12 계열
- publish 도구 자산 Microsoft.NET.ILLink.Tasks는 SDK가 사용하는 빌드 자산이며 trimming은 비활성입니다.

## 4. Schedule / DST

정책 원천은 미국 월~금 Eastern 09:00 ~ 같은 업무일 Pacific 18:00입니다.
기존 KST 10:00/22:00 하드코딩 대신 날짜별 각 경계를 독립적으로 UTC로 변환합니다.

Windows Time Zone ID:

- Eastern Standard Time
- Pacific Standard Time
- Korea Standard Time — 표시 및 통계 시간대용

DateTimeOffset.UtcNow를 기본 입력으로 사용하고 검사에서는 특정 DateTimeOffset을 주입합니다.
현재 Eastern 날짜 주변 -7~+8일의 평일 구간을 생성해 날짜별로 캐시합니다.
각 구간은 [시작, 종료)입니다. 현재가 구간 안이면 BURGER/다음 경계=종료, 밖이면 FULL/다음 경계=다음 시작입니다.
직전 금요일 종료와 다음 월요일 시작 사이면 Weekend/Extended입니다.
DST 날짜나 오프셋은 하드코딩하지 않습니다. Windows/.NET 시간대 데이터를 사용하며 기존 타이머 경로에서 24시간마다 시간대 캐시를 재확인합니다.
ET/PT가 잠깐 서로 다른 DST 상태가 되는 전환 순간도 실제 offset과 Mixed DST로 표현합니다.
미국 연방 공휴일은 업무일 그대로 처리합니다.

검증:

- DST 평일 KST 22:00~다음날 10:00, Standard 평일 23:00~다음날 11:00.
- 금요일 종료, 토/일, 월요일 시작, 경계 직전 1 tick/정확한 경계, 연도 경계·윤년.
- 2026/2027 DST 시작 주말 59시간, 종료 주말 61시간, 일반 주말 60시간.
- 2026-03-08, 2026-11-01 ET/PT 전환 순간의 offset/DST 메타데이터.
- 총 242,363 assertions 통과. 시스템 시각은 변경하지 않았습니다.

## 5. 공식 상태와 Provider별 권고

실제로 조회한 공식 source:

- OpenAI: [summary](https://status.openai.com/api/v2/summary.json), incidents 누락 시 [incident history](https://status.openai.com/api/v2/incidents.json).
- Claude: [summary](https://status.claude.com/api/v2/summary.json).
- Gemini: [공식 catalog](https://www.google.com/appsstatus/dashboard/products.json) + [incident history](https://www.google.com/appsstatus/dashboard/incidents.json).

OpenAI의 예시 unresolved endpoint는 404여서 사용하지 않습니다. HTML scraping은 없습니다.
OpenAI는 ChatGPT/Work·Agent·Research·파일·Apps·Codex 관련 의미명과 검증된 ID를 사용합니다.
Claude는 claude.ai·Claude Code·API·Cowork가 대상입니다. Gemini는 Workspace catalog의 Gemini 제품입니다.
Sora/voice/images/billing-only, Claude Console/Government-only, Gemini Notebook 등 관련 없는 장애는 제외합니다.
전체 roll-up은 검증에 사용하지만 관련 component가 정상인 Provider를 일괄 강등하지 않습니다.
활성 관련 incident를 함께 평가하고, 해석할 수 없는 구조/범위는 UNKNOWN으로 표시합니다.
정확한 매핑·source 범위는 [PROVIDER_SOURCES.md](PROVIDER_SOURCES.md)에 기록했습니다.

내부 상태: Operational, Degraded, PartialOutage, MajorOutage, Unknown, Stale.
공식 상태와 Recommendation은 별도 필드입니다.

| Schedule | Operational | Degraded | Partial/Major | Unknown/Stale |
|---|---|---|---|---|
| FULL | GO | HOLD | STOP | CHECK |
| BURGER | BURGER TIME | BURGER + ISSUE | BURGER + ISSUE | BURGER + CHECK |

각 Provider를 독립 계산하며 정상이라고 BURGER를 승격하지 않습니다.
공식 상태는 실제 사용자 오류/성공을 자동 생성하지 않습니다.

갱신: 재사용 HttpClient, 비동기 5분 주기, 수동 Refresh, 15초 timeout, 응답당 4 MiB 제한, 종료 취소.
Google 요청은 병렬이며 보통 총 4회, OpenAI fallback 시 5회 요청합니다.
성공이 없는 최초 실패는 UNKNOWN, 마지막 성공 후 15분 이상 새 성공이 없으면 STALE입니다.
마지막 성공 시각과 마지막 알려진 상태를 캐시로 보존합니다. 네트워크 실패는 다른 Provider/시간표를 중지시키지 않습니다.

알림: Schedule 변경은 기존처럼 알립니다. Provider는 FULL 중 GO/HOLD/STOP 간 실제 변경만 알리고 동일 상태/동일 incident update는 반복 알림하지 않습니다.
실제 WinForms 검사에서 Windows BalloonTipShown 8회가 관측됐습니다. 화면 노출/소리는 Windows 방해 금지·알림 정책에 따릅니다.

## 6. SQLite와 실측

실제 생성 위치: `%LOCALAPPDATA%\AIBurgerClock\burgerclock.db`

SchemaVersion=1: PRAGMA user_version과 AppMetadata에 기록합니다.
최초 생성은 transaction으로 수행합니다. 향후 버전별 migration 진입점이 있고 현재보다 새 schema는 거부합니다.
1단계에는 SQLite DB가 없으므로 과거 DB 데이터 변환은 필요하지 않았습니다.
WAL, parameterized SQL, 작업별 연결, 5초 잠금 timeout, background I/O를 사용합니다.

| 테이블 | 저장 내용 |
|---|---|
| UsageEvents | EventId, Provider, EventType, TimestampUtc, ScheduleState, WeekendExtendedFullThrottle, ET/PT offset minutes, ET/PT DST, SchedulePolicyVersion, OfficialStatus, EffectiveRecommendation, RelevantComponent, IncidentId, UserNote, AppVersion |
| ProviderStatusHistory | 상태/incident/권고 변경 이력, CheckedAtUtc, ScheduleStateAtCheck, 마지막 성공, source/reason/last-known |
| ProviderStatusCache | Provider별 최신 조회 및 마지막 성공 상태, 재시작 복원 |
| AppMetadata | SchemaVersion |

시간·Provider/시간·상태 이력 조회 인덱스 4개. KST 날짜/요일/시간은 UTC에서 계산해 중복 저장을 피합니다.
매 5분 동일 정상 상태를 history에 추가하지 않고 cache만 갱신합니다.
삭제된 DB는 다음 작업에서 빈 schema로 재생성합니다. 손상된 DB나 미래 schema를 자동 삭제하지 않습니다.

사용법: Provider 행 우클릭 후 이벤트 선택(2클릭), 또는 트레이 메뉴. 메모는 선택입니다.
기록 시점의 Schedule/정책/DST/공식 상태/권고를 함께 저장하고 UTC로 보존합니다.
API 키, 인증, 쿠키, 프롬프트, 응답, 패킷 등을 자동 수집하거나 요구하지 않습니다. 메모에도 이런 정보를 입력하지 않도록 안내합니다.

## 7. Statistics

기간 선택: 최근 7일 / 최근 30일 / 전체. 7/30일은 현재 UTC 기준 rolling 기간입니다.
5개 탭:

1. Provider 비교 — 4종 이벤트 수와 비율, n.
2. KST 시간대 — Provider별 00~23시, Slow/Error/Interrupted 및 합계 비율.
3. Schedule / DST — FULL/BURGER, Weekend/일반 FULL, DST/Standard/Mixed, DST×Schedule, 정책 버전.
4. 공식 상태 × 체감 — 공식 정상인데 Slow/Error, 공식 장애인데 Success 등을 분리 비교.
5. 공식 정상 시간대 — Operational로 기록된 이벤트의 KST 시간대별 체감.

n=0이면 No data, n<30이면 소표본 주의. 비율은 각 행 n이 분모이며 겹치는 집단은 합산하지 않습니다.
자발적 기록에 의한 선택 편향이 있으므로 전체 가용성·인과효과로 해석하지 않습니다. 통계로 Schedule을 자동 변경하지 않습니다.

## 8. 최종 검증 결과

| 필수 항목 | 결과·근거 |
|---|---|
| 1. Release build | SDK 10.0.401, 경고 0 / 오류 0, warnaserror. publish 성공 |
| 2. 기존 기능 회귀 | 트레이 아이콘/tooltip, 카운트다운, 전환, 창 숨김/재열기, native 알림, 자동시작 등록/해제/원상복원, 정상 종료 통과 |
| 3. DST/Schedule | 날짜 주입 242,363 assertions 통과 |
| 4. OpenAI/Claude/Gemini | JSON/HTTP 72 + 권고/STALE/polling/취소 78 통과; 실제 세 source 조회 성공 |
| 5. SQLite 저장/재시작 | storage/statistics 70 assertions, 1만 건, transaction rollback, 삭제 재생성, cache/history dedup 통과. 실제 앱 종료·재실행 후 schema 1, integrity ok, WAL, cache 3개 유지 |
| 6. 실측 입력 | 실제 WinForms Provider/트레이 핸들러로 3×4종 저장, 한글 메모 1건, 메타데이터 및 재열기 확인. 임시 DB만 사용 |
| 7. Statistics | 7/30/전체, 5탭, 데이터 없음/소표본/Provider·시간·Schedule·DST·공식 상태 교차 집계, 150% DPI 검증 통과 |
| 8. 최종 실행 파일 | dist/win-x64/AI Burger Clock.exe, 2,675,947 bytes, 단일 파일 |

통합 자체 검사 총 242,583 assertions가 최종 publish EXE에서 통과했습니다.
최종 EXE SHA-256: 491B7AF6FDB7A8C813A1940D85543D3B2CA451E14EA6B31E7502078D9199E0FE

실제 배포본을 --autostart로 실행한 뒤 비정상 프로세스 종료/재시작도 확인했습니다.
실제 DB의 UsageEvents는 0건으로 테스트 데이터가 섞이지 않았고, 상태 history는 3건 그대로입니다.
실제 프로세스는 .NET 10.0.12와 EXE에서 추출한 e_sqlite3.DLL을 사용합니다.
이 PC에서 초기 조회 후 작업 집합 약 80.7 MiB가 관측됐습니다(참고값이며 고정 보장치 아님).
원래 켜져 있던 자동 시작 경로는 그대로이며 최종 앱을 트레이에 실행해 두었습니다.

검증 파일:

- artifacts/self-test.txt — 최종 배포본 자체 검사.
- artifacts/phase2-publish-smoke.txt — 최종 EXE WinForms 검사.
- artifacts/phase2-smoke-test.txt — 자동 시작 원상복원 포함 검사.
- artifacts/phase2-live-status.txt — 실제 HTTP probe (enum 숫자 0은 Operational).
- artifacts/phase2-live-db-before-restart.txt / phase2-live-db-after-restart.txt — 읽기 전용 실제 DB 확인.
- artifacts/full-throttle.png / burger-time.png / provider-outage.png / measurement-dialog.png / statistics.png — 실제 폼 DrawToBitmap 렌더.

UI 입력 검증은 폼의 실제 메뉴/버튼 이벤트를 프로그램으로 작동시킨 자동 검사입니다.
사람의 마우스 클릭 검사나 데스크톱 전체 스크린샷으로 표현하지 않습니다.

## 9. 남은 제한과 3단계 후보

필수 2단계 기능의 미완성 stub은 없습니다. 다음은 검증/설계 범위의 한계입니다.

- 실제 Windows 재로그인/재부팅·실제 절전 복귀는 수행하지 않았습니다. HKCU 값과 autostart 실행, 날짜 주입 복귀 로직으로 확인했습니다.
- 공식 feed는 서비스 품질 측정이 아닙니다. 지연 공지·component 명칭/구조 변경·계정/지역별 문제는 다를 수 있습니다.
- OpenAI Work는 현재 summary에서 별도 component가 없어 공유 관련 capability와 사건 제목으로 판단합니다. 두 Login component의 부모 서비스 정보도 summary에서 명확하지 않습니다.
- Gemini는 Workspace dashboard의 Gemini 제품 범위이며 모든 Vertex/Gemini API backend를 뜻하지 않습니다.
- 테스트는 이 PC의 150% DPI 기준입니다. 다양한 모니터 배율 조합을 모두 수동 검증한 것은 아닙니다.
- schema v1의 최초 생성/삭제 재생성 경로를 구현했으며 아직 존재하지 않는 미래 schema의 migration 자체를 구현한 것은 아닙니다.
- 고정 KST 참고 분석은 선택 기능이므로 추가하지 않았습니다. 실제 DST Schedule과 혼동하지 않습니다.
- CSV/JSON export, heatmap, 사용자 정의/공휴일 Schedule, 자동 latency/synthetic 검사, reliability score, 실측 기반 자동 정책/추천 알림은 3단계 후보로 남겼습니다.
