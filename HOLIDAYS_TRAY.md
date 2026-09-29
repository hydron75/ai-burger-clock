# 2.1.0 — 트레이 색상, 미국 공휴일, 기록 구분

대상: Windows 11 x64, WinForms, net10.0-windows. 직접 패키지 Microsoft.Data.Sqlite 10.0.12 유지. 새 의존성 없음.

## 동작

- F/B는 시간표이며 색상은 `일부/주요 장애(빨강) > 성능 저하(주황) > UNKNOWN/STALE(회색) > 모두 정상(F 초록/B 주황)` 순서입니다. Provider별 권고를 바꾸지 않습니다.
- Tooltip에 전체 시간표·전환까지 시간·3개 Provider 각각의 권고를 담습니다. 127자 이하 검사, 표현이 바뀔 때만 아이콘 교체, 이전 네이티브 아이콘 해제를 유지합니다. 기존 1초 UI 타이머/5분 공식 조회를 재사용합니다.
- 공휴일 보정 기본 ON, 앱 하단과 트레이 메뉴에서 ON/OFF 가능. 설정 저장에 성공한 뒤만 정책을 적용합니다. 시작할 때 저장된 설정을 읽은 뒤 공식 조회를 시작합니다. 실측 입력도 초기 설정 읽기를 기다립니다.
- 공휴일 업무일의 ET 09:00~같은 날짜 PT 18:00 구간을 생략합니다. 한국 날짜 전체를 휴일로 처리하지 않습니다. 다음 실제 업무 구간 시작까지 카운트다운을 이어갑니다.
- 주말과 공휴일 연장은 별도 플래그입니다. 연속 FULL에 둘 다 포함될 수 있으며 평일 단독 공휴일은 주말로 분류하지 않습니다. HolidayNames는 해당 연속 FULL에서 생략된 관측일의 명칭입니다.
- 시간 경계에서만 기존 Schedule 알림을 보냅니다. 생략한 경계는 알리지 않습니다. 수동 정책 변경은 별도 정책 알림이며 서비스 복구로 알리지 않습니다. 같은 Schedule 중 대기 중인 실제 Provider 장애 알림은 유지합니다.

## 공휴일 정책 범위

OPM 정기 공휴일: New Year's Day, Martin Luther King Jr., Washington's Birthday, Memorial Day, Juneteenth, Independence Day, Labor Day, Columbus Day, Veterans Day, Thanksgiving, Christmas.

월~금 연방 근무 일정의 관측 규칙을 사용합니다. 고정 날짜의 토요일 휴일은 앞 금요일, 일요일 휴일은 다음 월요일입니다. 인접 연도 명목 날짜를 함께 계산하므로 12월 31일의 다음 해 신정 관측일도 처리합니다. Juneteenth는 2021년부터입니다. 현대적 연방 규칙의 작업 정책이며 역사적 모든 법률 개정을 재현하는 달력은 아닙니다.

제외: 지역/주/회사 휴무, 취임식, 임시 행정명령 휴무, Thanksgiving 다음 금요일이나 반일 휴무. 공휴일이 실제 AI 서비스 수요/품질을 보장하지 않습니다. 휴일 자체의 규칙 변경은 앱 업데이트가 필요하지만 DST는 OS/.NET TimeZoneInfo 규칙을 계속 따릅니다.

근거: [OPM Federal Holidays](https://www.opm.gov/policy-data-oversight/pay-leave/federal-holidays/), [OPM holiday definitions](https://www.opm.gov/frequently-asked-questions/pay-and-leave-faq/pay-administration/what-are-federal-holidays/).

## 기록과 호환성

- OFF 정책: `us-business-et09-pt18-v1`; ON 정책: `us-business-et09-pt18-holidays-v2`.
- SchemaVersion 1→2: UsageEvents에 `HolidayAdjustmentEnabled`(NULL 가능), `HolidayExtendedFullThrottle`(기본 0), `HolidayNames`(기본 빈 문자열) 추가.
- 기존 UsageEvents/ProviderStatusHistory/ProviderStatusCache 값을 재계산하거나 덮어쓰지 않습니다. 이전 설정값 미기록은 명시적인 OFF와 별개로 표시합니다.
- AppMetadata의 `UsFederalHolidaysEnabled`는 `1`/`0`; 없으면 ON. 알 수 없는 값은 오류 표시 후 임의로 덮어쓰지 않습니다.
- 업그레이드 전에 같은 DB 폴더에 `burgerclock.pre-schema2-<UTC시각>-<고유ID>.db` 일관성 백업을 만듭니다. WAL 커밋도 보존하며 정상 DB 검사를 통과해야 마이그레이션합니다. DDL과 두 버전 표시는 한 트랜잭션입니다. 실패 시 롤백하고 반복 백업 생성을 막습니다.
- Statistics: 공휴일 ON/OFF/이전 미기록, 공휴일 연장 FULL, 공휴일 제외 주말, 일반 FULL, 정책별 FULL/BURGER. 표본이 없으면 No data. 정책이 섞인 전체 행만으로 정책 효과를 비교하지 않습니다.
- 공식 상태 이력은 기존대로 조회 당시 Schedule 및 권고를 보존합니다. 백그라운드 조회도 UI와 같은 공휴일 설정을 사용합니다. 사용자 실측 이벤트는 더 상세한 공휴일/정책 정보를 보존합니다.

## 기준점 및 복원

Git 저장소가 아니므로 변경 전 소스/설정/배포 EXE ZIP으로 기준점을 보존했습니다.

- `../backups/AiBurgerClock-before-holidays-20260926-003017.zip`, 38개 항목.
- SHA256: `E8C0BB4B5E4F232A5BAD5D39F4D08650060E28390EE33C824938FE4930F5B469`.
- 사용자 DB 별도 사전 백업: `../backups/burgerclock-before-holidays-20260926-004139.db`, schema 1, quick_check=ok, 실측 0건, 상태 이력 15건.
- 정상 종료 후 구버전을 복원할 때는 schema 1 DB 백업도 함께 복원해야 합니다. 실제 사용 중 생긴 새 기록을 버리지 않도록 현재 DB도 먼저 보존하세요. 자동 복원/삭제는 하지 않습니다.

## 검증 범위

Release 빌드와 배포 EXE의 자체 검사/WinForms 메시지 루프 검사를 사용합니다. 최종 로그는 `artifacts/holidays-tray/`에 저장합니다.

- 기존 Schedule/DST, Provider 파서/실패/STALE, 알림 중복 방지, SQLite 1만 건, 통계, 자동 시작 순수 판정 회귀.
- 11종 휴일, 관측일, 연도 경계, 금/월/평일 휴일, 정확한 경계, 59/61시간 DST 주말, ON/OFF 캐시, 불변 snapshot.
- 432가지 시간표/공식 상태 조합, 독립 Provider 권고, 127자 Tooltip, 16/32px 아이콘 렌더, 변경 없는 틱의 핸들 재사용.
- 실제 v1 데이터/WAL 포함 백업, 원래 필드 보존, ON/OFF 재시작, 실패 롤백/반복 제한, 미래 schema 거부.
- 실제 UI 이벤트 핸들러로 공휴일 ON/OFF·기록·통계·공식 페이지 링크·Refresh·정상 종료 확인. 시스템 시계는 바꾸지 않고 임시 DB/가짜 응답만 사용합니다.
- 앱 렌더 PNG를 확인합니다. Windows 알림의 사용자 화면 노출은 시스템 설정에 영향을 받으며 실제 재부팅/절전 복귀를 수행했다는 뜻은 아닙니다. 시작 프로그램 레지스트리는 이번 작업에서 변경하지 않습니다.

## 변경 파일

기존: AgentSchedule.cs, Phase2Models.cs, UsageStore.cs, StatisticsWindow.cs, StorageTests.cs, StatusMonitor.cs, StatusWindow.cs, TrayApplicationContext.cs, SelfTest.cs, UIRegressionChecks.cs, SmokeTest.cs, AiBurgerClock.csproj, README.md, BACKLOG.md.

신규: UsFederalHolidays.cs, HolidayScheduleTests.cs, HolidayStorageTests.cs, HolidayMonitorTests.cs, HolidayUiChecks.cs, TrayPresentation.cs, TrayPresentationTests.cs, HOLIDAYS_TRAY.md.

## 최종 결과 (2026-09-26 KST)

- SDK 10.0.401, Release build/publish: 경고 0 / 오류 0. NuGet 설정 접근이 제한된 격리 실행은 실패했지만 일반 사용자 접근으로 같은 빌드 스크립트를 재실행하여 성공했습니다. 코드를 우회하거나 패키지를 추가하지 않았습니다.
- 최종 단일 EXE 자체 검사: 250,433 assertions, 실제 프로세스 ExitCode=0. `artifacts/holidays-tray/publish-self-test.txt`.
- 최종 배포 EXE WinForms smoke: ExitCode=0. 트레이/카운트다운/링크/Refresh/알림 요청/실측·메모/통계/공휴일 설정/정상 종료 통과. `publish-smoke.txt` 및 렌더 PNG. 이 환경에서 BalloonTipShown=0이므로 Windows 바탕화면에서 알림이 실제 노출됐다고 주장하지 않습니다.
- 실제 공식 조회: 00:46 KST OpenAI/Claude/Gemini 모두 Operational로 해석, ExitCode=0. 격리 실행의 SSL 오류는 일반 사용자 환경 재검증에서 재현되지 않았습니다. 이 상태는 조회 시각의 결과입니다.
- 00:47 KST 기존 dist 경로의 새 버전을 `--autostart`로 다시 실행, PID 65852/Responding=True 확인. 시작 프로그램 값 변경 없음.
- 실제 사용자 DB: schema 1→2, quick_check=ok. 이전 실측 0건 및 원래 필드 해시 유지(`4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945`). 실제 기존 데이터가 비어 있으므로 비어 있지 않은 기록 보존은 별도의 populated-v1 fixture와 WAL 테스트로 검증했습니다.
- 자동 schema 1 백업: `%LOCALAPPDATA%/AIBurgerClock/burgerclock.pre-schema2-20260925T154703575Z-6c199399c3444e5e9c30157f73f4cb71.db`, quick_check=ok, 원래 실측 일치.
- 실제 새 앱의 DB 캐시에서 세 Provider 모두 Operational, 현재 BurgerTime 정책·권고 및 새 성공 조회 시각을 확인했습니다. 공휴일 설정 미지정은 기본 ON입니다.
- 실제 재부팅/재로그인, 물리적인 절전 복귀는 수행하지 않았습니다. 자동 시작은 기존 경로·코드를 유지하고 순수 판정 229개 검사만 재실행했습니다. 이번 작업에서 자동 시작 레지스트리·트레이 기록은 편집하지 않았습니다.

최종 EXE: `dist/win-x64/AI Burger Clock.exe`, FileVersion 2.1.0.0, 2,786,539 bytes, framework-dependent 단일 EXE.

SHA256: `A35D7826F582E9CDEDB054E59FACBD52EAA3F974F4A8451E929FA966023E5D2B`.
