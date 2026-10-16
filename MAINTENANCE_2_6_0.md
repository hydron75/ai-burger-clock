# AI Burger Clock 2.6.0 — Windows 한도 회복 알림 배포 준비

기록일: **2026-10-16 KST**. 상태: **준비 빌드·self-test·smoke 5회 통과 / Draft PR 승인 대기 / PR 병합·publish·배포 EXE 교체 미수행**. 현재 배포본은 **Windows 2.5.1**이다.

#59가 Windows·Mac 확인 뒤 main `92f589e975a27648617f0fc1683c03762b37c5ce`에 병합된 상태에서 `git pull --ff-only`하고 `feature/windows-release-2-6-0`을 만들었다. 범위는 **Windows만 변경(버전·Windows 제품/검사·문서)**이다. 이미 병합한 #59의 공통 기능을 배포 대상으로 삼지만 **이번 PR에서 공통 로직·공통 검사·Mac 파일/버전·MACOS_PORT는 바꾸지 않는다.** Mac 네이티브 한도 회복 알림은 후속 **#60 진행 중**이다.

문서·PR·렌더에는 합성 데이터와 일반화한 결과만 사용한다. 실제 계정 요금제·사용률·리셋 시각·식별자·화면 캡처·CLI 원문·사용자 DB·원본 진단 로그는 게시하지 않는다.

## 버전과 2.5.1 이후 Windows 변경

Version **2.6.0**, AssemblyVersion·FileVersion **2.6.0.0**으로 준비한다. 한도 회복 알림이라는 선택 기능이 추가돼 minor 버전을 올린다. Windows 11 x64 / WinForms / net10.0-windows / framework-dependent 단일 EXE와 .NET 10 Desktop Runtime x64 요구사항은 유지한다.

| 출처 | Windows에 반영할 내용 |
|---|---|
| #59 | 검증된 새 성공 응답의 동일 창 사용 100% → 100% 미만을 확인할 때만 회복 알림. 기본 OFF, 캐시/첫 성공/예정 시각 경과는 무알림 |
| #59 | 공통 `QuotaRecoveryNotifications`·`QuotaRecoveryText`, `AppMetadata` 한 키, Provider별 성공 순번과 Windows 메뉴/풍선 연결. 추가 조회·schema 변경 없음 |
| #57 리뷰 후속 | Alt+Esc를 상세 팝업 닫기로 소비하지 않고 OS 창 순환에 넘김. 일반 Esc·클릭·10초 자동 닫힘·박스 이탈 전 재등장 방지는 유지 |
| 문서 | #59의 Draft·Mac 확인 대기 문구를 병합 완료·2.6.0 반영 예정으로 정리하고 Mac 연결은 #60 진행 중으로 구분 |
| #57 README 리뷰 | #58 준비 문서와 후속 배포 기록에서 10초로 수정한 사항을 답변하고 해당 스레드를 해결 처리했다. 새 동작 변경은 아님 |

이전 EXE 소스 `2897aea` 뒤의 `f1a0d96`은 2.5.1 배포 결과 문서이고 EXE 소스가 아니다. #59의 Windows/Mac 검증 이력은 [회복 알림 기록](QUOTA_RECOVERY_NOTIFICATIONS.md)에 유지하며 이번 후보의 실제 검사를 대신하지 않는다. #30의 원인 미확정 smoke 관찰과 기존 진단·#58 검사 창 보호를 유지한다. 성능 최적화·근무 시간표 가중치·요금제/CLI 경로·7b/7c는 재개하지 않는다.

## 설정 위치와 콜백 계약

Windows의 **한도 회복 알림**은 **트레이 아이콘 우클릭 메뉴의 Statistics 바로 다음, Windows 시작 시 자동 실행 바로 이전**에 있는 체크 메뉴다. **기본 OFF**이며 상태 창·한도 보기 화면 안에 별도 체크박스는 없다. 메뉴·문구·저장 키는 #59의 공통 정의를 사용하므로 Mac 위치 조정 때도 이 배치를 기준으로 삼을 수 있다.

`Succeeded`는 검증된 live 응답만 개별 전달하며 Provider 게이트를 잡은 채 동기 호출된다. Windows는 빠르게 비동기 UI 전달한 뒤 반환한다. 콜백 안의 Stop/Dispose 동기 대기는 금지하고 같은 Provider Refresh는 게이트 때문에 건너뛰며 느린 콜백은 해당 조회를 지연시킨다. #59 최종 주석 보완은 이 계약을 명시했을 뿐 동작 변경이 아니다.

## 변경 파일

기존 수정:

- `AiBurgerClock.csproj`: Version / AssemblyVersion / FileVersion 함께 변경.
- `AccountQuotaView.cs`: Alt+Esc를 팝업 닫기 메시지 처리에서 제외.
- `QuotaToolTipUiChecks.cs`: 합성 메시지의 Alt+Esc 비소비·상세 유지와 일반 Esc 회귀 확인.
- `QUOTA_RECOVERY_NOTIFICATIONS.md`, `BACKLOG.md`: #59 병합·Mac 확인 완료·Windows 2.6.0 반영 예정 및 Mac #60 진행 상태.
- `README.md`, `CODE_GUIDE.md`: 미배포 후보/현재 배포 분리, 메뉴 위치와 소스 기준·검증 기록 연결.

신규:

- `MAINTENANCE_2_6_0.md`: 준비 검증·Git 기준점·승인 후 교체/복원 계획.

소스 파일 추가는 없다. 루트·Properties C# **80개(기능 41 + 검사·진단 38 + 명찰 1)**, Mac 호스트 7개, 공통 검사 입구 1개로 총 **88개**다. 공통 원본 목록은 #59가 추가한 회복 엔진을 포함한 **29개**이며 이번 준비에서 바꾸지 않는다. 로컬 검증 스크립트·바이너리·로그·렌더는 ignored `artifacts/`에만 둔다.

## 준비 검사 — 실제 실행 결과

코드 커밋 **`f2e0296f5ab4a4a8f6ad8e6e1a936a4c72eaac2d`**에서 `build.ps1`을 **Publish 없이** 실행했다. WinExe 종료 코드는 `$LASTEXITCODE`가 아닌 **`Start-Process -Wait -PassThru`의 실제 ExitCode**로 확인했다. 아래 결과 문서의 후속 변경은 Markdown뿐이며 C#·csproj는 검증 시점과 같다.

| 항목 | 이번 2.6.0 준비 결과 |
|---|---|
| 검증한 전체 코드 HEAD | `f2e0296f5ab4a4a8f6ad8e6e1a936a4c72eaac2d` |
| .NET SDK | **10.0.402** |
| 최종 빌드 경고 / 오류 | **0 / 0** |
| self-test 전체 / 실제 ExitCode | **252,051건 / 0** |
| 공통 / Windows 전용 | **251,744 / 307건** — #59 대비 각각 증감 0 |
| smoke 정확히 5회 | **매회 572 PASS·실패 0·ExitCode 0**, 실패 0/5회 |
| 합성 PNG / layout 비교 | PNG 4·5회 **20/20 동일**, 1~3회 **19/20 동일**(Refresh 버튼만 캡처 타이밍 차이). layout는 매회 **14/14 동일** |
| 기존 2.5.1 재실행 / dist 불변 | 검사 후 같은 dist 한 개·정확한 실행 경로·응답·입력 대기 준비 확인, 버전·크기·SHA-256 불변 |

### 초기 NuGet 접근 실패와 최종 빌드

제한 환경의 첫 `build.ps1`은 취약성 데이터 소스 접근 실패 **NU1900**으로 경고 0·오류 1, 자체 검사 실행 전에 중단됐다. 네트워크 허용 환경에서 같은 명령을 실행한 두 번째 시도도 앞선 복원 오류가 캐시되어 경고 0·오류 1로 중단됐다. 이후 같은 환경에서 `dotnet restore AiBurgerClock.csproj --force --no-cache --nologo -warnaserror`를 성공시키고 **같은 `build.ps1`**을 실행해 위 최종 결과를 확인했다. 취약성 감사를 끄거나 경고를 무시하거나 의존성·프로젝트/사용자 NuGet 설정을 바꾸지 않았다. 두 실패와 강제 복원·최종 빌드 로그를 구분 보관한다.

### 정확히 5회 smoke와 검사 수

사용자가 기존 2.5.1 앱을 정상 종료한 뒤 앱 프로세스 0개를 확인했다. 같은 Release bin으로 `--smoke-test --report-directory`를 **5회 순차 실행**했다. 실제 WinForms 메시지 루프·합성 응답·임시 DB이며 실제 계정 probe·자동 시작 검사 옵션은 사용하지 않았다. 실패 제외·자동 재시도는 없다.

| 회차 | 실제 ExitCode | PASS | FAIL 표시 | 시간 |
|---|---:|---:|---:|---:|
| 1 | 0 | 572 | 0 | 48.53초 |
| 2 | 0 | 572 | 0 | 48.73초 |
| 3 | 0 | 572 | 0 | 49.08초 |
| 4 | 0 | 572 | 0 | 49.00초 |
| 5 | 0 | 572 | 0 | 48.72초 |

각 회차에 추가된 **Alt+Esc 검사 2건**이 통과했다. 실제 등록된 `Application.FilterMessage`에 Alt context-bit 29가 있는 `WM_SYSKEYDOWN`을 합성 주입해 (1) 표시 중 상세를 닫거나 메시지를 소비하지 않음, (2) hover 대기를 취소하거나 메시지를 소비하지 않음을 확인했다. 기존 일반 Esc 닫힘·재등장 방지·클릭 비소비·실제 10초 타이머·검사 창 보호와 회복 알림 UI 40건도 통과했다. 제품 타이머·hover 대기·검사 timeout은 바꾸지 않았고 사용자의 실제 키 입력이나 OS 창 순환은 조작하지 않았다.

| 검사 범위 | 배포 2.5.1 | #59 | 이번 2.6.0 후보 |
|---|---:|---:|---:|
| 공통 self-test | 251,519 | 251,744 | **251,744** |
| Windows 전용 self-test | 307 | 307 | **307** |
| 전체 self-test | 251,826 | 252,051 | **252,051** |
| smoke | 530 | 570 | **572** |

기존 배포 대비 self-test **+225**는 #59의 monitor +19·회복 판정/문구 +188·설정 저장 +18이다. 이번 PR 자체는 self-test 증감 0이며, smoke **#59 대비 +2 / 배포 2.5.1 대비 +42**다. 과거 결과를 이번에 실행한 것으로 섞거나 Mac의 별도 합성 63건을 이 합계에 더하지 않는다.

### 합성 렌더의 Refresh 버튼 차이

같은 bin 경로의 #59 기준 렌더와 비교했다. 1~3회는 `account-quotas-exhausted.png` 하나만 다르고, 4~5회는 PNG **20/20개가 SHA-256 동일**하다. layout JSON은 **5회 모두 14/14개 동일**하다.

결론은 **앱 동작 변경이 아닌 기존 공식 상태 조회와 캡처 시점 차이**다. 기준/1회차 PNG를 직접 보고 픽셀 비교했다. 두 이미지 모두 774×1209이며 차이 2,197픽셀은 Refresh 버튼 사각형 **x46·y975·폭210·높이54** 안에만 있고 바깥은 완전히 같다. 기존 Resume 검사는 한도 조회 완료만 기다린다. DIAG에서 기준의 Resume 대기 종료(37,037.53ms)는 공식 조회 완료·Refresh 활성, 후보(37,217.16ms)는 공식 조회 진행·Refresh 비활성·한도 조회 모두 완료였다. 후보 버튼은 캡처 후 `RefreshStatus(false)` 시점(37,295.71ms)에 다시 활성화됐다. 기존 `StatusWindow.UpdateProviders`의 `Enabled = !refreshing`과 캡처 경로는 이번 PR에서 바꾸지 않았다. 합성 Gemini 10초 조회는 이 캡처 뒤이므로 원인으로 삼지 않는다. 같은 바이너리의 4·5회가 모두 동일한 점도 이를 뒷받침한다. 이 차이를 없애려고 기다림이나 제품 코드를 바꾸지 않았다.

### #57 README 리뷰와 기존 배포본 복귀

[README 리뷰 스레드](https://github.com/hydron75/ai-burger-clock/pull/57#discussion_r4282336796)에 #58 준비 문서와 후속 배포 기록에서 반영한 사항이라고 답변하고 **해결 처리**했다. 당시 배포 2.5.0의 30초 안내와 후보 2.5.1의 10초 안내는 구분되어 있었으며 현재 README는 배포 2.5.1의 10초·클릭/Esc·재등장 방지를 설명한다. Alt+Esc 별도 스레드는 이번 새 PR 병합 전이므로 닫지 않았다.

검사 종료 후 **2026-10-16 09:25 KST** 기존 dist 2.5.1을 `--autostart`로 다시 실행했다. 앱 한 개·정확한 dist 경로·`Responding=True`·입력 대기 준비, 아래 기존 ProductVersion·크기·SHA-256 일치를 확인했다. 테스트 2.6.0을 실행해 둔 것이 아니며 새 화면/OS 배너를 육안 확인했다고 적지 않는다. 배포 EXE·사용자 설정·자동 시작 등록은 교체/변경하지 않았다.

**과거 기준(이번 실행이 아님):** #59의 Windows 빌드는 경고 0·오류 0, self-test **252,051건 = 공통 251,744 + Windows 전용 307**, 실제 ExitCode 0이었다. 기존 배포 2.5.1 self-test **251,826건 = 251,519 + 307** 대비 **+225**이며 monitor +19·회복 판정/문구 +188·설정 저장 +18이다. #59 smoke는 별도 **1회 570 PASS·ExitCode 0**, 기존 배포 준비 530에 회복 알림 UI 40건이 늘었다. 이번 Alt+Esc 회귀 검사는 smoke에 추가되므로 새 합계와 증가 사유를 실제 실행 뒤 적는다. 공통 동작은 그대로이므로 공통/Windows 자체 검사 증감은 별도로 비교한다.

#59의 Mac 확인은 HEAD `e467b0d798ca266f06e5586f48ad34a309104d09`, Xcode 27.0(27A266a)·SDK 10.0.401, 공통 251,744건·빌드 경고/오류 0·native smoke ExitCode 0과 별도 합성 프로브 63건이다. 최종 주석 5줄 변경 뒤 재검사는 하지 않았다. 이번 준비는 Windows만 변경하므로 Mac 검사를 새로 실행한 것으로 기록하지 않는다.

## Git 기준점과 실행 파일

| 기준 | 전체 커밋 해시 / 상태 |
|---|---|
| 기존 배포 2.5.1 EXE 소스 | `2897aea54141f9ed55054847a0268f73d4f79eaa` |
| 기존 2.5.1 배포 결과 문서 | `f1a0d963741e31e3a3089695601152ba39fa0892` — EXE 소스가 아님 |
| #59 Mac 검증 HEAD | `e467b0d798ca266f06e5586f48ad34a309104d09` |
| #59 병합 / 이번 브랜치 출발점 | `92f589e975a27648617f0fc1683c03762b37c5ce` |
| 2.6.0 버전·Alt+Esc 보완 / 실제 검증 HEAD | `f2e0296f5ab4a4a8f6ad8e6e1a936a4c72eaac2d` |
| 이번 준비 PR 병합 | **미수행·사용자 승인 대기** |
| 최종 2.6.0 단일 EXE 소스 | **아직 없음** — 승인 후 깨끗한 병합 main에서 publish할 커밋 |

| 항목 | 현재 배포 2.5.1 — 기존 배포 기록 기준 |
|---|---|
| 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion | `2.5.1.0` |
| ProductVersion | `2.5.1+2897aea54141f9ed55054847a0268f73d4f79eaa` |
| 크기 | 3,474,667 bytes |
| SHA-256 | `0276257DE59E8BDBD95B157BC44A640A4C5664C22DF868EE9CE867DBAA768B72` |

준비에서 실행한 다음 파일은 주변 DLL이 필요한 **후보 apphost**다. dist의 framework-dependent 단일 EXE와 다르며 dist에 복사하지 않았다.

| 항목 | 검사한 후보 bin apphost |
|---|---|
| 경로 | `bin/Release/net10.0-windows/win-x64/AI Burger Clock.exe` |
| FileVersion | `2.6.0.0` |
| ProductVersion | `2.6.0+f2e0296f5ab4a4a8f6ad8e6e1a936a4c72eaac2d` |
| 크기 | 163,328 bytes |
| SHA-256 | `2E7DF640A14BE0EEC32A36F7C178DD252CED93648BD47C5A1A595E582E032F37` |

**최종 2.6.0 단일 EXE와 실제 배포 커밋·시각·해시·백업 경로는 아직 없다.** 승인 후 병합 main에서 따로 만든다.

## 승인 후 교체 계획 — 아직 실행하지 않음

1. 사용자가 준비 PR 병합·교체를 승인하면 승인 HEAD를 지정해 Draft를 해제·병합하고 main에서 `git pull --ff-only`한다. 깨끗한 작업 트리와 병합 main의 **전체 해시를 최종 EXE 소스**로 기록한다. 후속 배포 결과 문서 커밋과 구분한다.
2. 실행 중인 앱을 **정상 종료**하고 프로세스 0개와 `%LOCALAPPDATA%/AIBurgerClock/burgerclock.db-wal`·`-shm` 부재를 확인한다. 준비 검사 전 종료를 이때 확인으로 대신하지 않는다. sidecar가 남으면 삭제·강제 덮어쓰기하지 않고 원인을 확인한다.
3. **`../backups/release-2.6.0-<교체일>/`**을 새로 만든다. 같은 이름이 있으면 덮어쓰지 않는다. `replaced-2.5.1.exe`·이전 dist ZIP·실제 이전 EXE 소스 **`2897aea54141f9ed55054847a0268f73d4f79eaa`**의 `git archive` ZIP·닫힌 DB를 보관한다. 원본/복사본 및 ZIP 안 EXE 해시 일치, 백업 중 프로세스/WAL/SHM 부재를 재확인한다. DB·manifest·원본 로그는 로컬에만 보관한다.
4. **깨끗한 병합 main에서 `build.ps1 -Publish`**한다. 빌드/publish 경고 0·오류 0, 최종 단일 EXE의 self-test 실제 ExitCode 0과 별도 smoke를 확인한다. 새 EXE를 백업의 `published-2.6.0.exe`에도 보관하고 경로·FileVersion·ProductVersion·크기·SHA-256과 실제 백업 경로를 이 문서에 기록한다.
5. 자동 시작 등록은 **읽기 전용으로 전후 비교**한다. HKCU Run `AI Burger Clock`이 같은 큰따옴표 절대 dist 경로 + `--autostart`인지, 값 종류와 StartupApproved/Run 값이 유지되는지 확인한다. 등록이 가리키는 실제 파일의 버전/해시가 새 2.6.0과 일치해야 한다. `--verify-autostart`나 체크박스 조작으로 등록을 다시 쓰지 않는다.
6. 새 dist를 실행해 앱 한 개·정확한 경로·응답, 트레이·상태 창·한도/시간 비교·상세 닫힘·Refresh와 회복 알림 메뉴를 확인한다. 실제 화면을 조작 도구가 제공하지 못하면 사용자 확인과 자동 검사를 구분한다. 임의로 실제 계정 소진/회복을 만들거나 OS 배너 노출을 표시 요청 검사로 대신하지 않는다. 문제가 생기면 아래 계획으로 복원하고 알린다.

## 2.5.1로 되돌리기

새 앱을 정상 종료하고 프로세스/WAL/SHM을 확인한다. 교체 후 DB를 별도로 보존하고 WAL·로그·새 기록을 삭제하지 않는다. 백업의 `replaced-2.5.1.exe` 또는 dist ZIP에서 **EXE만 기존 dist 경로로 복원**한다. FileVersion **2.5.1.0**, ProductVersion **`2.5.1+2897aea54141f9ed55054847a0268f73d4f79eaa`**, 위 SHA-256을 확인하고 `--autostart`로 실행한다. 같은 자동 시작 경로와 트레이·창·한도를 확인한다.

#59는 schema 2와 기존 한도 캐시/기록 형식을 유지하고 `AppMetadata` 한 키만 추가했다. 2.5.1로 돌아가면 회복 알림 메뉴·동작과 Alt+Esc 보완이 없는 것이 예상 차이다. 새 설정 키를 2.5.1이 사용하지 않는다고 DB를 삭제하거나 백업 DB를 자동 덮어쓰지 않는다. DB 복원이 별도로 필요하면 현재 데이터를 보존하고 유실 위험·범위를 설명한 뒤 승인받는다. **복원 시험·실제 복원은 미수행**이다.

## 미수행과 남은 승인

- 준비 단계에서 **PR 병합·새 배포 백업·publish·dist 교체·실제 복원은 하지 않는다.** 최종 단일 EXE 검사도 승인 뒤 별도다.
- 회복 알림은 기본 OFF이며 실제 사용자 DB의 ON/OFF 설정을 준비 검사를 위해 바꾸지 않는다. 임시 DB·합성 성공 응답으로 메뉴·저장·중복·대기열을 검사한다.
- 실제 OS 배너의 육안 노출, Alt+Esc의 실제 OS 창 순환, 실제 계정/CLI probe, 재부팅/재로그인·절전·장시간 운영·DPI/고대비·다중 모니터·스크린리더 실기, 별도 Shared.Tests·Mac 실행은 **미수행**이다. self-test 안의 공통 실행과 별도 Shared.Tests 실행을 구분한다.
- 기존 네트워크 단절·재연결 정상은 **사용자 확인**으로 BACKLOG에 남아 있으며 이번 작업이 직접 재측정한 결과가 아니다.
- Mac 파일·버전·native 검사는 이번 작업 대상이 아니다. #60 진행과 #59 공통 검증 완료를 구분한다. 새 준비 검사 원본은 ignored `artifacts/release-2.6.0-preparation-20261016/`의 `build-restricted-network-failed.txt`·`build-escalated-cached-failed.txt`·`restore-force.txt`·`build.txt`·`self-test.txt`·errors, `smoke/run-01`~`run-05`·`smoke-results.json`·`restart-2.5.1.json`에만 로컬 보관한다.
