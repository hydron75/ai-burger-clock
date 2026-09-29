# 2.1.2 — 예외 처리·저장·화면·자원 정리

2026-09-30 KST. [PR #3](https://github.com/hydron75/ai-burger-clock/pull/3)의 Windows 검증 결과를 기록하고 main에 병합했습니다. 최종 2.1.2를 검사한 뒤 기존 배포 경로에 적용했습니다.

Windows 11 x64 / WinForms / `net10.0-windows` 유지. 직접 NuGet 의존성은 `Microsoft.Data.Sqlite 10.0.12` 하나입니다. 추가 패키지, DB schema 변경, 시간표·공휴일 정책 변경은 없습니다. 계정 사용량·한도 조회 기능도 이번 버전에 포함하지 않습니다.

## 달라진 동작과 범위

- **Provider별 저장 오류:** 한 Provider의 저장이 실패하면 다른 Provider의 성공으로 그 오류를 지우지 않습니다. 해당 Provider의 저장이 회복돼야 해소합니다. 공식 서비스 장애와 로컬 저장 문제는 별개입니다.
- **읽을 수 없는 일부 기록:** 알 수 없는 enum 이름·숫자 문자열 등으로 `FormatException`이 발생한 행을 통계에서 제외하고 건수를 표시합니다. 원본 기록을 삭제하지 않으며 모든 DB 손상을 복구하는 기능은 아닙니다. 읽을 수 없는 공식 상태 캐시 행도 건너뜁니다.
- **메모·초기화·종료:** 요청한 이벤트 종류를 메모창에 미리 선택합니다. 초기화 실패 또는 메모창이 열린 동안 시작된 종료를 처리해 새 저장을 진행하지 않도록 합니다.
- **시각 일관성:** Provider 파서와 통계도 앱에 주입한 시각을 사용합니다. 실제 PC 날짜와 테스트 시각이 섞이지 않습니다.
- **자동 시작 오류 안내:** 설정 변경 뒤 원상복구도 실패하면 두 원인을 보존합니다. 경고창이 열렸다는 이유로 상태 창이 자동 숨김되지 않도록 합니다. 등록 경로를 임의로 바꾸는 기능은 추가하지 않았습니다.
- **화면·자원:** 대기 중인 UI 갱신은 합치고 최신 snapshot을 읽습니다. 같은 Tooltip은 다시 설정하지 않습니다. 폰트·하위 메뉴를 정리하고, 외부에서 전달받은 HttpClient는 만든 쪽에서 해제하도록 구분합니다. Monitor의 반복 Dispose도 허용합니다.
- **중복 코드 정리:** 시간·상태 표시, 버전 기반 User-Agent, 실측 INSERT의 열/값 목록, 통계 정책 목록을 정리했습니다. 새 프레임워크나 별도 서비스는 없습니다.

동시 Provider 알림 합치기, 로그오프 시 추가 비동기 대기, 요청 제한시간 구조 변경은 이번 범위에서 보류했습니다. 계정 사용량 기능의 보류 상태와 기존 공개 상태 조회 주기(약 5분)도 유지합니다.

## 변경 파일

PR #3의 기존 파일 수정:

- `AgentSchedule.cs`, `AutoStartManager.cs`, `MeasurementDialog.cs`, `Phase2Models.cs`, `ProviderStatusClient.cs`, `StatisticsWindow.cs`, `StatusMonitor.cs`, `StatusWindow.cs`, `TrayApplicationContext.cs`, `TrayPresentation.cs`, `UsageStore.cs`.
- 검사: `MonitorTests.cs`, `ProviderStatusTests.cs`, `StorageTests.cs`, `UIRegressionChecks.cs`.

이번 배포 작업의 기존 파일 수정:

- `AiBurgerClock.csproj`: Version 2.1.2, AssemblyVersion/FileVersion 2.1.2.0.
- `UIRegressionChecks.cs`: Refresh 클릭 전 창을 다시 열고 버튼이 클릭 가능한지 검사. 아래 실패·재검증 기록 참고.
- `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`: 현재 동작·버전·검증 기록 링크 반영.

새 파일은 이 문서 `MAINTENANCE_2_1_2.md`입니다. C# 파일 30개, 기존 지침 파일 `AGENTS.md`·`CLAUDE.md`는 그대로입니다. 임시 진단 출력은 최종 소스에 남기지 않았습니다.

## Git 기준점과 백업

| 기준 | 커밋 |
|---|---|
| 작업 전 main / 2.1.1 문서 포함 소스 | `2958dcb893a529ab9f8ef594fc20074239ead6e8` |
| 검증한 PR #3 HEAD | `499532b4c5f3bf02bf26cfc5254f9aad83aec10f` |
| PR #3 병합 | `27f45938432d6bcab85d3f95dafd7ba1a50782b5` |
| 2.1.2 버전 변경 | `a7f5b549509888ae0496b2b37d6b8d3e840c3151` |
| 최종 EXE의 소스·검사 코드 기준 | `cdc0fa010141d9886706617946898ca61de5ced4` |

사용자가 앱을 종료했고 실제 DB에 활성 WAL이 없는 것을 확인한 뒤 백업했습니다. 다음 파일은 이 PC에만 보관하며 GitHub에 올리지 않습니다.

| 로컬 백업 경로 | SHA-256 |
|---|---|
| `../backups/release-2.1.2-20260930/dist-2.1.1.zip` | `2C6F31BEE8D58CE870F4A17448B5648B78E1AE13B3CD008108E6B8E54367B08C` |
| `../backups/release-2.1.2-20260930/burgerclock.db` | `E9ADF6FF372CEB34308CFF2AB6667F4A84742D92139B03CBAA34004D9801C8A7` |
| `../backups/release-2.1.2-20260930/source-2958dcb.zip` | `2219D96C31696844259F363B21B7955BB1E67612A0C0C3719703207386BC343C` |
| `../backups/release-2.1.2-20260930/replaced-2.1.1.exe` | `AE0C9A16F749598C6CC7AF34FF55A2AC095703581A7E3C77F76A734DFD71F33C` |

소스 ZIP은 2.1.1 문서를 포함한 기준점입니다. 구버전 EXE의 빌드 코드 기준은 `048ecf5032e0125b4658e5a145f933d9dd38f32d`이며 [2.1.1 기록](RELIABILITY_FIX.md)에 있습니다.

2.1.1로 되돌릴 때는 앱을 정상 종료하고 현재 DB를 별도로 보존한 뒤 기존 `dist/win-x64/AI Burger Clock.exe`에 백업 EXE를 복원합니다. 두 버전 모두 schema 2이므로 버전 차이만으로 DB를 과거 백업으로 덮어쓸 필요는 없습니다. 백업 폴더의 EXE에서 자동 시작을 등록하지 마세요. 2.0.x로의 복원은 [기존 DB 호환성 안내](HOLIDAYS_TRAY.md)를 따릅니다.

## 최종 빌드·검증 결과

최종 로그는 로컬 `artifacts/release-2.1.2/`에 있습니다. 로그·DB·렌더 이미지는 GitHub에 포함하지 않습니다.

| 검증 | 결과 | 로그 |
|---|---|---|
| Release build | SDK 10.0.401, 경고 0 / 오류 0, exit 0 | `final-build.txt` |
| Portable publish | warnings-as-errors, exit 0, 단일 EXE 생성 | `final-publish.txt` |
| 최종 단일 EXE 자체 검사 | 250,442 assertions, exit 0, stderr 0바이트 | `final-self-test.txt` |
| 최종 단일 EXE WinForms 회귀 | PASS 103건, exit 0, stderr 0바이트 | `final-smoke.txt`, `final-ui/` |
| 실제 공개 Provider 조회 | OpenAI·Claude·Gemini 파싱 성공, exit 0 | `final-live-status.txt` |
| 배포 전후 DB 비교 | 무결성·schema·설정·실측 기록 보존, 이력 건수 유지 | `db-before.txt`, `db-after.txt` |
| 배포 파일·실행·자동 시작 | 해시 일치, 정상 프로세스, 등록값 유지 | `deployment.txt`, `startup-before.txt` |

`dotnet build`와 `dotnet publish` 모두 `-warnaserror`를 사용했습니다. Portable profile의 PublishDir/PublishUrl을 `artifacts/release-2.1.2/final-publish/`로 지정해 기존 dist를 먼저 덮어쓰지 않았습니다. 이 폴더의 최종 EXE로 검사한 뒤 해시를 대조하고 같은 dist 경로에 교체했습니다.

### 자체 검사 범위

- 자동 시작 경로·Windows 승인 순수 판정: 229 assertions, 레지스트리 변경 없음.
- Schedule/DST: 242,363; 공휴일·대체휴일·연장 구간: 1,399.
- 트레이 색상·독립 권고·Tooltip·아이콘: 6,122.
- OpenAI/Claude/Gemini 파서·모의 HTTP: 96.
- 권고·신선도·polling·취소·Dispose: 99.
- SQLite·실측 메타데이터·통계: 132, 10,000행 데이터 포함.
- 백그라운드 캐시의 공휴일 정책: 2.

빈 DB·재오픈·migration/rollback·No data, DST/Standard 경계와 주말·공휴일은 주입된 시각과 임시 데이터로 검사합니다. 시스템 시계는 바꾸지 않았습니다.

### UI 검사에서 발견한 문제와 보강

최초 2.1.2 후보의 smoke 검사는 메인 Refresh 클릭 후 요청을 기다리다 timeout으로 실패했습니다(`smoke.txt`, `smoke-errors.txt`). 배포는 진행하지 않고 해당 로그를 보존했습니다. 진단용 재실행에서는 버튼의 Visible/Enabled/CanSelect가 모두 true였고 전체 검사가 통과했습니다. 따라서 첫 실패 당시의 상태나 원인을 직접 확정한 것은 아닙니다.

코드 확인 결과, 비동기 검사 중 상태 창이 자동 숨김되거나 화면 갱신이 대기할 수 있는데 기존 검사는 클릭 직전 창·버튼 상태를 보장하지 않았습니다. 테스트를 명시적으로 숨긴 뒤 정상 tray 경로로 다시 열고, 버튼이 활성화·선택 가능한지 확인한 다음 `PerformClick`하도록 보강했습니다. 네트워크 요청 발생과 완료를 기다리는 기존 판정은 그대로이며 timeout도 늘리지 않았습니다. 실제 앱 Refresh 로직은 변경하지 않았습니다.

이 보강으로 검사 1건이 추가되어 최종 PNG 생성 포함 103건을 통과했습니다. 임시 진단 출력은 제거했고 최종 EXE를 새로 빌드했습니다. 이전 실패 후보나 진단용 EXE는 배포하지 않았습니다.

### 실측·통계·저장 오류 격리

UI 검사는 임시 DB와 모의 HTTP에서 트레이 시작·창 열기/숨기기·전환·카운트다운, Provider별 독립 권고·색상·알림 요청, 공식 페이지 클릭 라우팅, 메인/트레이 Refresh, 모의 Resume, 저장 오류 표시, Provider 3개 × 이벤트 4종, Unicode 메모, DB 재오픈, Statistics 7일/30일/전체의 5개 표, 공휴일 설정 유지와 정상 종료를 확인했습니다. 장애·메모·통계의 렌더 이미지도 확인했습니다.

PR 병합 전 별도 진단에서 **8개 추가 검사**도 통과했습니다. 새 fixture DB에 OpenAI 캐시 INSERT만 실패시키는 trigger를 넣고 다른 Provider의 응답을 늦춰, Claude/Gemini 저장 성공이 OpenAI 오류를 지우지 않음을 확인했습니다. OpenAI 캐시와 같은 트랜잭션의 이력 rollback, trigger 제거 후 저장 회복·오류 해소, 세 Provider 캐시·사용자 이벤트 0건·schema 2도 확인했습니다. 이는 위 자체 검사 합계와 별도이며 최종 배포 단계에서 재실행한 검사는 아닙니다. 이후 실제 앱 코드 변경은 버전뿐이고 나머지는 UI 테스트 준비 조건 보강입니다.

추가 진단·PR 결과는 로컬 `../diagnostics/pr3-windows-test-20260930-499532b/artifacts/PR3-VALIDATION.md`와 `storage-isolation.txt`에 있습니다. GitHub에는 [검증 요약 댓글](https://github.com/hydron75/ai-burger-clock/pull/3#issuecomment-5893116206)을 남겼습니다.

## 실제 배포 확인

2026-09-30 **00:28:10~11 KST** 최종 공개 상태 조회에서는 OpenAI·Gemini가 Operational, Claude가 PartialOutage였습니다. Claude의 `Elevated errors on claude.ai, Claude Code, Claude Cowork and the Claude API` 사건을 반영했습니다. 장애 상태를 정상 파싱한 것은 검사 실패가 아닙니다.

**00:29 KST** 기존 dist 경로에서 `--autostart`로 새 버전을 실행했습니다. PID 50108, Responding=True, FileVersion 2.1.2.0을 확인했습니다. 같은 시각 실제 DB의 세 Provider 캐시에 새 성공 조회 시각이 저장됐습니다.

- DB: `%LOCALAPPDATA%\AIBurgerClock\burgerclock.db`.
- 읽기 전용 비교 결과 quick_check=ok, schema 2, AppMetadata 설정과 UsageEvents 해시가 배포 전후 같습니다.
- 실제 사용자 실측은 0건이므로 비어 있지 않은 기록 보존은 테스트 데이터로 검증했습니다.
- 공식 상태 이력은 20→20건으로 건수를 유지했습니다. 이 비교는 전체 이력 행의 해시 비교는 아닙니다.
- HKCU Run은 기존 `dist/win-x64/AI Burger Clock.exe --autostart` 경로를 유지합니다. StartupApproved 승인값도 동일하며 레지스트리는 편집하지 않았습니다.

## 수행하지 않은 검증과 제한

- 실제 Windows 절전·복귀·재부팅·재로그인은 하지 않았고 `--verify-autostart`도 실행하지 않았습니다. Resume handler와 자동 시작의 순수 판정·등록값 유지까지만 확인했습니다.
- 실제 레지스트리 쓰기·복원 실패, 초기화 Task 실패, 메모창 중 종료 경합은 별도 실패 주입하지 않았습니다. 경고창 자동 숨김 억제는 Deactivate 이벤트 검사이며 실제 오류 MessageBox를 강제로 띄운 검사는 아닙니다.
- 외부 HttpClient 소유권, 갱신 합치기의 정확한 횟수, 폰트·메뉴의 장시간 누수는 별도 계측하지 않았습니다. 일반 실행·종료와 반복 Dispose 검사는 통과했습니다.
- 잘못된 행 제외는 제한된 FormatException 처리입니다. DB 손상·정수 overflow 전반의 복구를 보장하지 않으며 제외 건수 문구를 실제 잘못된 행과 함께 보는 추가 UI 검사는 하지 않았습니다.
- Windows BalloonTipShown 이벤트는 최종 smoke에서 15회 관측했지만 실제 사용자 화면에 모든 알림이 노출됐음을 보장하지 않습니다.
- PNG는 DrawToBitmap 결과로 데스크톱 스크린샷이 아닙니다. Statistics 기간 선택 등 일부 native control의 렌더 차이는 속성 assertion으로 별도 확인했습니다.
- 계정 사용량·인증·쿠키·토큰 조회, SDK 설치, PC 전원 변경은 하지 않았습니다.

## 최종 배포 파일

- 경로: `dist/win-x64/AI Burger Clock.exe`.
- FileVersion: **2.1.2.0**.
- ProductVersion: `2.1.2+cdc0fa010141d9886706617946898ca61de5ced4`.
- 크기: **2,807,019 bytes**.
- SHA-256: `AE79C4BBEDF6B3629828D20ED7A7626591E78BA836A0BC2CC992140B8AA28013`.
- framework-dependent 단일 EXE이며 .NET 10 Desktop Runtime x64가 필요합니다.

GitHub에는 소스·문서만 반영합니다. EXE, 개인 DB, 백업, 진단 파일, 테스트 로그는 업로드하지 않습니다.
