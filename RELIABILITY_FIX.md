# 2.1.1 — 저장 오류·절전 복귀 안정성 개선

2026-09-29 KST. [PR #2](https://github.com/hydron75/ai-burger-clock/pull/2)를 검증 후 main에 병합하고, 기존 배포 경로에 2.1.1을 적용했습니다.

Windows 11 x64 / WinForms / `net10.0-windows` 유지. 직접 NuGet 의존성은 `Microsoft.Data.Sqlite 10.0.12` 하나이며 추가 패키지는 없습니다. SQLite schema 2, 시간표·공휴일 정책, 자동 시작 코드도 그대로입니다.

## 달라진 동작

- **절전 복귀:** Windows Resume 이벤트를 받으면 공식 상태의 즉시 재조회를 요청합니다. 절전 전에 시작한 조회가 아직 진행 중이면 완료 직후 추가 한 번을 실행합니다. 중복 대기 요청은 합치고, 종료 시 전원 이벤트 구독을 해제합니다. 네트워크 복구나 조회 성공 자체를 보장하지는 않습니다.
- **정기 조회:** 상태 변경을 전달받는 함수 하나가 예외를 내도 다른 수신자와 polling이 계속되도록 격리합니다. 화면은 기존 타이머에서도 현재 snapshot을 읽습니다.
- **저장 오류 안내:** 같은 오류가 매초 최신 저장·설정 안내를 덮어쓰지 않습니다. 오류가 해소되면 남아 있는 해당 오류만 지우고 다른 안내는 보존합니다. DB 자동 복구 기능을 추가한 것은 아닙니다.
- **DB 업그레이드 보호:** 백업 검증 실패인 `InvalidDataException`도 준비 실패로 기억합니다. 실패 후 매 조회마다 백업을 반복 생성하지 않고 원본·백업 확인과 재시작을 안내합니다.

계정 사용량·한도 조회 기능은 이번 버전에 포함하지 않았습니다. 기존 공개 서비스 상태 조회 주기는 약 5분입니다.

## 변경 파일

PR #2의 기존 파일 수정:

- `StatusMonitor.cs`, `TrayApplicationContext.cs`, `StatusWindow.cs`, `UsageStore.cs`.
- 검사: `MonitorTests.cs`, `HolidayStorageTests.cs`, `UIRegressionChecks.cs`.

이번 배포 정리의 기존 파일 수정:

- `AiBurgerClock.csproj`: Version 2.1.1, AssemblyVersion/FileVersion 2.1.1.0.
- `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`: 변경 동작·기록 링크와 현재 버전 반영.

새 파일은 이 문서 `RELIABILITY_FIX.md`입니다. C# 파일은 기존 30개를 유지합니다. `AGENTS.md`와 `CLAUDE.md`는 앞서 병합된 PR #1의 지침이며 이번 배포 작업에서 수정하지 않았습니다.

## Git 기준점과 백업

| 기준 | 커밋 |
|---|---|
| 작업 전 로컬 main / 기존 2.1.0 소스 | `1d82e2d695e980eb4b9dfdcb20e14fa3b08c6e2d` |
| 검증한 PR #2 HEAD | `24b65d78959dd38a9e23825c6d8491c0f06730a8` |
| PR #2 병합 | `84182f5eaf2f12a16b0ca243479d5c12542fd568` |
| 최종 2.1.1 EXE의 소스·버전 커밋 | `048ecf5032e0125b4658e5a145f933d9dd38f32d` |

앱이 종료되고 현재 DB에 WAL 파일이 없는 것을 확인한 뒤 백업했습니다. 다음은 이 PC에만 보관하며 GitHub에 업로드하지 않습니다.

| 로컬 백업 경로 | SHA-256 |
|---|---|
| `../backups/release-2.1.1-20260929/dist-2.1.0.zip` | `ED30970424DEB2B1F9F05A31420AFDE26CF2FF8193732A0415E2B7818A427705` |
| `../backups/release-2.1.1-20260929/burgerclock.db` | `96C0EE7555620D5786FCDA479789E246DAECDDB831A4E0260914645DF3699AF7` |
| `../backups/release-2.1.1-20260929/source-84182f5.zip` | `A5BBD4C8762864B21BB0C2B736F50BAA3F65F17424CFC5B19B40FA6D8FF59010` |
| `../backups/release-2.1.1-20260929/replaced-2.1.0.exe` | `A35D7826F582E9CDEDB054E59FACBD52EAA3F974F4A8451E929FA966023E5D2B` |

소스 ZIP은 PR #2 병합 직후의 소스입니다. 구버전 EXE와 같은 소스 기준은 첫 번째 Git 커밋을 참고하세요.

2.1.0으로 되돌릴 때는 앱을 정상 종료하고 현재 DB를 먼저 별도로 보존한 뒤, 기존 `dist/win-x64/AI Burger Clock.exe`에 백업 EXE를 복원합니다. 2.1.0과 2.1.1은 같은 schema 2이므로 이 버전 차이만으로 DB를 과거 백업으로 덮어쓸 필요는 없습니다. 백업 폴더의 EXE를 직접 실행해 자동 시작을 다시 등록하지 마세요. 2.0.x로의 복원은 [이전 DB 호환성 안내](HOLIDAYS_TRAY.md)를 따릅니다.

## 빌드·실행 검증

최종 로그는 로컬 `artifacts/release-2.1.1/`에 있습니다. 로그와 렌더 이미지는 GitHub에 포함하지 않습니다.

| 검증 | 결과 | 로그 |
|---|---|---|
| Release build | SDK 10.0.401, 경고 0 / 오류 0, exit 0 | `build.txt` |
| Portable publish | warnings-as-errors, exit 0, 단일 EXE 생성 | `publish.txt` |
| 최종 단일 EXE 자체 검사 | 250,437 assertions, exit 0, stderr 0바이트 | `self-test.txt` |
| 최종 단일 EXE WinForms 회귀 | PASS 99건, exit 0, stderr 0바이트 | `smoke.txt`, `ui/` |
| 실제 공개 Provider 피드 | 세 Provider 응답 파싱 성공, exit 0 | `live-status.txt` |
| 배포 전후 DB 비교 | 무결성·schema·설정·사용 기록 보존 통과 | `db-before.txt`, `db-after.txt` |
| 배포 파일·실행·자동 시작 경로 | 해시 일치, 정상 프로세스, 등록값 유지 | `deployment.txt` |

`dotnet build`와 Portable profile의 `dotnet publish`에 모두 `-warnaserror`를 적용했습니다. 기존 dist를 먼저 덮어쓰지 않도록 PublishDir/PublishUrl을 `artifacts/release-2.1.1/publish/`로 지정했습니다. 이 폴더의 EXE로 자체 검사와 smoke 검사를 통과한 뒤 해시를 확인하고 기존 경로의 EXE를 교체했습니다.

### 자체 검사 범위

- 자동 시작 경로·Windows 승인 순수 판정: 229 assertions, 레지스트리 변경 없음.
- Schedule/DST: 242,363; 공휴일·대체휴일·연장 구간: 1,399.
- 트레이 색상·독립 권고·Tooltip·아이콘: 6,122.
- OpenAI/Claude/Gemini 파서·모의 HTTP: 95.
- 권고·신선도·polling·취소·구독자 예외 격리·추가 refresh 예약: 98.
- SQLite·기록 메타데이터·통계: 129, 10,000행 데이터 포함.
- 백그라운드 캐시의 공휴일 정책: 2.

### UI·저장·실제 실행

임시 DB와 모의 HTTP에서 트레이/창 열기·숨기기, 카운트다운·전환·알림 요청, 상태 페이지 클릭 라우팅, Refresh, Resume handler, 오류 표시·해소, Provider 3개 × 이벤트 4종, Unicode 메모, DB 재오픈, Statistics 7일/30일/전체의 5개 표, 공휴일 토글, 정상 종료를 확인했습니다. 폼 렌더에서 레이아웃을 확인했습니다.

2026-09-29 23:26 KST 최종 공개 상태 조회에서는 OpenAI·Gemini는 Operational, Claude는 PartialOutage였습니다. Claude의 `Elevated errors on claude.ai, Claude Code and Claude Cowork` 사건을 반영했습니다. 장애 상태의 정상 파싱은 테스트 실패가 아닙니다. PR 사전 검사 시각인 23:10 KST에는 세 Provider 모두 Operational이었으므로 두 관측 시각을 혼동하지 않습니다.

23:28 KST 기존 dist 경로에서 `--autostart`로 실행했습니다. PID 64960, Responding=True, FileVersion 2.1.1.0, 원래 경로의 실행을 확인했습니다. 같은 시각 실제 DB 캐시에 세 Provider의 새 성공 조회가 저장됐고 Claude는 PartialOutage로 반영됐습니다. 재시작 전후 quick_check=ok, schema 2, 설정과 사용자 실측 데이터 해시가 같았습니다. 실제 사용자 실측은 0건이므로 비어 있지 않은 기록 보존은 별도의 테스트 데이터로 검증했습니다. 공식 상태 이력은 18→19건으로, 기존 이력을 유지하고 변화를 추가했습니다.

HKCU Run의 명령은 기존 `dist/win-x64/AI Burger Clock.exe --autostart` 경로를 유지합니다. StartupApproved의 이 앱 승인값도 전후 동일하며 이번 배포에서 레지스트리를 편집하지 않았습니다.

## 수행하지 않은 검증

- 실제 Windows 절전·복귀 및 복귀 직후 네트워크 지연은 미검증입니다. Resume handler와 진행 중 조회의 추가 요청은 모의 조건에서 검증했습니다.
- 실제 재부팅·재로그인은 하지 않았고 `--verify-autostart`도 사용하지 않았습니다.
- Windows BalloonTipShown 이벤트 15회는 관측했으나 실제 사용자 화면의 알림 노출을 보장하지 않습니다.
- 백업 검증 실패의 예외 분류는 검사했으나 실제 quick_check 실패를 강제로 만드는 추가 통합 검사는 하지 않았습니다. 기존 migration/rollback 검사는 통과했습니다.
- UI PNG는 DrawToBitmap 결과로 실제 데스크톱 스크린샷이 아닙니다. 일부 native control의 렌더에는 차이가 있으며 Statistics 기간 선택값은 속성 assertion으로 별도 확인했습니다.
- 계정 사용량·인증·쿠키·토큰 조회, SDK 설치, PC 전원 변경은 하지 않았습니다.

## 사용자가 마저 확인할 것

다른 작업에 지장이 없을 때 PC를 한 번 절전했다가 깨운 뒤 트레이 창을 여세요. 네트워크가 복구된 상태에서 ‘최근 조회 시도’가 갱신되는지, Provider Tooltip의 마지막 성공 조회 시각도 새로 바뀌는지 확인하면 됩니다. 복귀 직후 인터넷이 없으면 실패가 정상일 수 있습니다. 연결 복구 후 Refresh를 눌러 비교하고, 실패가 계속되면 표시된 오류를 확인하세요. 앱이 자동으로 절전·재부팅을 시키지는 않습니다.

## 최종 배포 파일

- `dist/win-x64/AI Burger Clock.exe`
- FileVersion: **2.1.1.0**
- ProductVersion: `2.1.1+048ecf5032e0125b4658e5a145f933d9dd38f32d`
- 크기: **2,794,731 bytes**
- SHA-256: `AE0C9A16F749598C6CC7AF34FF55A2AC095703581A7E3C77F76A734DFD71F33C`
- framework-dependent 단일 EXE이며 .NET 10 Desktop Runtime x64가 필요합니다.

GitHub에는 소스·문서만 반영하며 EXE, 개인 DB, 백업, 테스트 로그는 올리지 않습니다.
