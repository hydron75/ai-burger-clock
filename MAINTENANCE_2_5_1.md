# AI Burger Clock 2.5.1 — Windows Provider 상세 닫힘 보완 준비

기록일: **2026-10-15 KST**. 상태: **버전·기록 Draft 준비 / smoke 2/5회 실패로 배포 준비 보류 / 배포 교체 미승인**.

#57은 main `a1d99e5a79cbda471a79a87a80a0894da1073990`에 병합된 상태를 확인했다. main에서 `git pull --ff-only` 후 `feature/windows-release-2-5-1`을 만들었다. 이번 버전 PR의 범위는 **Windows만 변경(버전·기록)**이다. **publish·dist 교체·새 버전 PR 병합은 하지 않는다. 현재 배포 EXE는 2.5.0이다.**

실제 계정 요금제·사용률·리셋 시각·식별자·화면 캡처·CLI 원문·사용자 DB·원본 진단 로그는 게시하지 않는다. 검사와 렌더에는 합성 데이터만 사용한다.

## 버전과 2.5.0 이후 변경

Version **2.5.1**, AssemblyVersion·FileVersion **2.5.1.0**이다. 기존 상세 팝업의 닫힘 수정이므로 패치 버전을 올린다. Windows 11 x64 / WinForms / net10.0-windows / framework-dependent 단일 EXE, .NET 10 Desktop Runtime x64 요구사항과 SQLite 의존성은 유지한다.

| 항목 | #57에서 반영한 Windows 변경 |
|---|---|
| 자동 닫힘 | 30초 → **10초**. 박스 안 이동·상세 문구 갱신·휠 스크롤로 표시 시간을 연장하지 않음 |
| 클릭·Esc | 표시 중이거나 표시를 기다리는 상세를 즉시 닫음. 클릭은 소비하지 않아 Refresh 등 원래 대상에 전달하고, Esc만 처리 중 소비 |
| 재등장 방지 | 만료·클릭·Esc 뒤 같은 Provider 박스에서 자식 컨트롤 이동·모델/레이아웃 갱신으로 다시 열리지 않음. 박스를 벗어나야 새 표시 가능 |
| 창 숨김·정리 | 부모 Form의 숨김을 직접 감지해 세션을 초기화. 포인터 감시·입력 필터·이벤트 구독 정리 |
| 기존 표시 유지 | 흰 배경·320 DIP 고정 폭·줄바꿈·포인터 근처 배치·Provider별 하나의 상세·사용량/시간층·창 높이·행 수 유지 |

긴 상세의 스크롤바 **클릭도 닫힘 대상**이며 휠 스크롤은 표시된 10초 안에서 가능하다. 2.5.0 당시 모든 환경에서 타이머가 멈췄다는 원인을 확정한 것은 아니다. 사용자가 보고한 박스 안 유지 증상에 맞춰 새 닫힘·재표시 규칙을 구현했다.

2.5.0 실제 EXE 소스 `af1c831` 이후 Windows C# 차이는 #57의 두 파일뿐이다. `55e37f5`는 이전 배포 기록, #53·#55·#56은 Mac 및 Mac 기록 변경으로 Windows 신규 기능에 세지 않는다. 이번 PR에서는 이 C#를 다시 바꾸지 않는다. 공통 원본 28개·공통 검사·Mac 파일/버전·MACOS_PORT·AGENTS·조회 정책·CLI·알림·DB schema 2·자동 시작 설정은 변경하지 않는다.

## 변경 파일

기존 수정:

- `AiBurgerClock.csproj`: Version / AssemblyVersion / FileVersion 함께 변경.
- `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`: 준비 상태·새 닫힘 사용법·검사 결과와 교체 계획 연결.

신규:

- `MAINTENANCE_2_5_1.md`: 이번 준비 검증·Git 기준점·미승인 교체/복원 계획.

소스 파일은 추가하지 않는다. 루트·Properties C# **76개(기능 40 + 검사·진단 35 + 명찰 1)**, Mac 호스트 7개, 공통 검사 입구 1개로 총 **84개**다. 검증용 로컬 스크립트·바이너리·로그는 ignored `artifacts/`에만 둔다.

## 빌드·자체 검사·smoke

SDK **10.0.402**. 버전 커밋 **`12b9103a600e5837add7d8c8181c6fa7650ef403`**에서 `build.ps1`을 Publish 없이 실행해 **경고 0 / 오류 0**, 내장 self-test의 **실제 ExitCode 0**을 확인했다. 기존 dist 2.5.0도 별도로 `--self-test`를 실행해 **실제 ExitCode 0**을 확인했다. WinExe는 `Start-Process -Wait -PassThru`의 ExitCode를 사용한다.

| 검사 | 기존 dist 2.5.0 | 후보 2.5.1 | 차이 |
|---|---:|---:|---:|
| 공통 | 251,519 | 251,519 | 0 |
| Windows 전용 | 307 | 307 | 0 |
| **전체** | **251,826** | **251,826** | **0** |

두 자체 검사 출력은 전체 줄이 동일하다. 공통 19개 그룹과 Windows 4개 그룹에 증감이 없다. Windows는 자동 시작 229 + 트레이/픽셀 44 + CLI 경로 3 + 경고 로그 31이다. 별도 Shared.Tests는 실행하지 않았다.

Release bin의 `--smoke-test --report-directory`를 **정확히 5회 순차 실행**했다. 임시 DB·가짜 HTTP/CLI·실제 메시지 루프를 사용했다. `--verify-autostart`는 실행하지 않았으며 실패를 제외하거나 자동 재시도하지 않았다.

| 회차 | 실제 ExitCode | PASS | FAIL 표시 | 소요 시간 |
|---|---:|---:|---:|---:|
| 1 | 0 | 527 | 0 | 47.79초 |
| 2 | 0 | 527 | 0 | 47.75초 |
| 3 | **1** | 404 | **1** | 31.41초 |
| 4 | **1** | 402 | **1** | 26.84초 |
| 5 | 0 | 527 | 0 | 47.59초 |

**실패 2/5회이므로 배포 가능한 상태로 확정하지 않는다.** 통과한 회차의 smoke는 기존 2.5.0의 492 → **527(+35)**이다. +35는 #57의 Windows UI 검사이며 self-test 증가는 아니다. 통과 회차에서 상태·통계·공휴일·알림 요청·한도/시간층·기록·Refresh·상세 닫힘과 합성 Gemini 10초 지연 중 응답성 검사가 통과했다. 실패 회차는 중간 종료했으므로 뒤의 검사를 통과로 세지 않는다.

### 새 실패의 확인 범위와 다음 진단

- 두 오류 모두 **`System.InvalidOperationException: Sequence contains no matching element`**다. `QuotaToolTipUiChecks.CheckDismissalAsync`의 `Box()`가 독립 검사 패널에서 `CodexQuotaBox`를 찾지 못했다(`QuotaToolTipUiChecks.cs:214`).
- 3회는 실제 만료 검사 뒤 `CheckSuppressed("real timeout")` → 255행/283행, 4회는 9초 유지 루프의 `Move(Box())` → 270행이다. 대기 시간 초과가 아니며, 만료/조회 대기를 늘려 해결했다고 적지 않는다.
- `SmokeDiagnostics`의 실패 시점에서 기존 상태 창과 한도 화면은 보이고 Refresh·Statistics·상태 전환 버튼은 활성 상태였다. 공식/한도 및 가짜 조회의 **진행 건수는 0**, 합성 10초 Gemini 조회는 **시작 전**, 바깥 smoke 타이머의 최대 `callbacksInFlight`는 **1**이다. 조회 지연이나 바깥 타이머 재진입을 뒷받침하는 기록은 없다.
- 독립 fixture의 합성 입력은 Codex를 계속 포함한다. 정상 `UpdateQuotas` 재생성은 같은 동기 호출에서 박스를 다시 만든다. 기존 앱 context의 매초/조회 갱신은 다른 패널을 대상으로 한다. **fixture가 박스를 잃은 원인과 제품 결함 여부는 미확정**이다. 현재 진단에는 이 fixture의 Dispose/종료 이벤트·Controls 목록이 없어 둘을 확정 구분할 수 없다.
- 다음에는 fixture의 `IsDisposed`·`Disposing`·자식 이름 목록·부모 종료/Dispose·레이아웃 재생성 진입/종료를 기록해 누가 박스를 제거했는지 분리해야 한다. 이번 버전·기록 PR에서는 제품/검사 C#를 추가로 고치거나 재실행으로 실패를 덮지 않았다. 원인 확인·필요한 수정·재검증 전까지 Draft를 유지한다.

### 합성 렌더 비교

동일한 bin 실행 경로의 #57 최종 smoke 렌더와 SHA-256을 비교했다. 통과한 **1·2·5회는 PNG 20/20개·layout JSON 14/14개가 전부 동일**하다. 실패한 **3·4회는 생성된 PNG 14/14개·layout 9/9개만 동일**하며 나머지는 생성 전 종료했다. 이 회차를 전체 화면 동일로 적지 않는다. 일부만 캡처된 실패도 모두 로컬 증거에 보관한다. 실제 계정 화면이나 dist/bin 실행 경로 차이를 비교에 섞지 않는다.

## Git 기준점과 실행 파일

| 기준 | 전체 커밋 해시 / 상태 |
|---|---|
| 기존 배포 2.5.0 EXE 소스 | `af1c831e70f0a956804255771a434f8103e1f5ea` |
| 기존 2.5.0 배포 결과 문서 | `55e37f556cced0452f4d54d5d047757d5e1a3dbd` — EXE 소스가 아님 |
| #57 검증·사용자 확인 HEAD | `229b3d43d5d81e9428e8c3c82377596952370696` |
| #57 main 병합 / 준비 브랜치 출발점 | `a1d99e5a79cbda471a79a87a80a0894da1073990` |
| 버전 변경 / 이번 검증 코드 | `12b9103a600e5837add7d8c8181c6fa7650ef403` |
| 이번 준비 PR 병합 | **미승인·미수행** |
| 최종 2.5.1 단일 EXE 소스 | **미정**. 승인 뒤 새 PR을 병합한 main 전체 해시로 확정 |

| 항목 | 현재 배포 2.5.0 |
|---|---|
| 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion | `2.5.0.0` |
| ProductVersion | `2.5.0+af1c831e70f0a956804255771a434f8103e1f5ea` |
| 크기 | **3,450,091 bytes** |
| SHA-256 | `42EBF164BAB599B49D20CCB2E7DADB8A8A5FAE2395094770D00EBC4AF8ACB4E9` |

후보 bin EXE는 주변 DLL이 필요한 apphost다. **배포 단일 EXE가 아니며 dist에 복사하지 않는다.** 최종 EXE의 경로·FileVersion·ProductVersion·크기·SHA-256은 승인 후 실제 publish가 끝나면 별도로 기록한다.

| 항목 | 이번 후보 bin apphost |
|---|---|
| 경로 | `bin/Release/net10.0-windows/win-x64/AI Burger Clock.exe` |
| FileVersion | `2.5.1.0` |
| ProductVersion | `2.5.1+12b9103a600e5837add7d8c8181c6fa7650ef403` |
| 크기 | **163,328 bytes** |
| SHA-256 | `06E493493228A4340DD13AE1437507996D4B98A428EC0A68A933EEA588B957BA` |

## 교체 계획 — 승인 후에만 실행

1. 먼저 이번 smoke 실패 원인 확인·필요한 수정·재검증을 완료한다. 사용자가 새 준비 PR 병합·배포 교체를 승인하면 승인 HEAD를 지정해 병합하고 main에서 `git pull --ff-only`한다. 깨끗한 작업 트리와 병합 main 전체 해시를 **최종 EXE 소스**로 기록한다. 후속 문서 커밋을 EXE 소스로 적지 않는다.
2. 그때 실행 중인 앱을 **정상 종료**한다. 프로세스 0개와 `%LOCALAPPDATA%/AIBurgerClock/burgerclock.db-wal`·`-shm` 부재를 확인한다. 준비 검사의 종료로 대신하지 않는다. WAL/SHM이 남으면 삭제·강제 덮어쓰기를 하지 않고 원인을 확인한다.
3. 새 **`../backups/release-2.5.1-<교체일>/`**에 기존 dist ZIP·`replaced-2.5.0.exe`·실제 EXE 소스 `af1c831e70f0a956804255771a434f8103e1f5ea`의 source ZIP·닫힌 DB를 보관한다. 같은 폴더가 있으면 덮어쓰지 않는다. 원본/복사본·ZIP 안 EXE 해시 일치와 백업 중 프로세스/WAL 부재를 재확인한다. DB·manifest·백업은 로컬에만 보관한다.
4. **병합 main에서 `build.ps1 -Publish`**를 실행한다. 경고 0·오류 0, 최종 단일 EXE의 self-test 실제 ExitCode 0과 별도 smoke를 확인한다. 새 EXE도 백업 폴더의 `published-2.5.1.exe`에 보관하고 최종 소스 해시·버전·크기·SHA-256·실제 백업 경로를 이 문서에 적는다.
5. 자동 시작은 등록을 새로 쓰지 않고 **읽기 전용으로 전후 비교**한다. HKCU Run `AI Burger Clock`이 같은 큰따옴표 절대 dist 경로 + `--autostart`인지, 값 종류와 StartupApproved/Run 값이 유지되는지 확인한다. 등록 경로가 가리키는 실제 파일의 버전·해시가 새 2.5.1과 일치해야 한다. `--verify-autostart`·체크박스 조작은 하지 않는다.
6. 새 dist를 실행해 앱 한 개·실행 경로·응답·트레이·상태 창·한도/시간 비교·상세 10초 만료·클릭/Esc·재등장 방지·Refresh를 확인한다. 실제 화면을 조작 도구가 제공하지 않으면 사용자 확인과 자동 검사를 구분한다. 문제가 생기면 아래 계획으로 복원하고 알린다.

준비 시작과 검사 후 기존 배포본 재실행 뒤의 읽기 전용 확인에서 자동 시작 Run은 **String / 같은 절대 dist 경로 + `--autostart`**, StartupApproved는 **Binary / `020000000000000000000000`**으로 동일하다. 최종 교체 때 다시 확인하며 이번 확인을 재부팅/로그인 시험으로 적지 않는다.

### 2.5.0으로 되돌리기

새 앱을 정상 종료하고 프로세스·WAL/SHM 상태를 확인한다. 교체 후의 현재 DB를 별도로 보존하며 WAL·로그·기록을 삭제하지 않는다. 새 백업의 `replaced-2.5.0.exe` 또는 기존 dist ZIP에서 **EXE만 원래 dist 경로로 복원**한다. FileVersion **2.5.0.0**, ProductVersion `2.5.0+af1c831...`, 위 SHA-256을 확인하고 `--autostart`로 실행한다. 같은 자동 시작 등록·경로와 트레이·창을 확인한다.

#57은 schema 2·한도 캐시·기록 형식을 바꾸지 않는다. 2.5.0으로 돌아가면 상세의 새 10초·클릭/Esc·재표시 차단 규칙이 없어지는 것이 예상 차이다. 백업 DB를 자동으로 덮어써 새 기록을 잃게 하지 않는다. DB 복원이 별도로 필요하면 현재 데이터를 보존하고 유실 위험과 범위를 설명한 뒤 승인받는다. **실제 복원 시험과 복원 적용은 미수행**이다.

## 실제 사용자 확인·로컬 증거·미수행

- **#57 테스트 앱의 기존 사용자 확인(2026-10-15 KST):** 10초 자동 닫힘·클릭/Esc·박스를 벗어나기 전 재등장 방지 모두 정상. 이번 2.5.1 후보를 새로 사용자 확인한 것으로 옮겨 적지 않는다.
- 사용자가 현재 #57 테스트 앱을 정상 종료했고 프로세스 부재를 확인한 뒤 빌드·smoke를 진행했다. 검사 후 변경하지 않은 **2.5.0 dist를 `--autostart`로 다시 실행**했다. 앱 한 개·기존 dist 경로·응답/입력 대기 준비·위 EXE 버전/크기/해시와 자동 시작 등록 불변을 확인했다. 새 2.5.1을 배포하거나 2.5.0으로 복원한 것이 아니다. 일반 실행은 정상 캐시를 갱신할 수 있어 DB 전체 무변경을 주장하지 않는다.
- 로컬 검증 증거: ignored `artifacts/release-2.5.1-preparation-20261015/`의 `build.txt`, `candidate-self-test.txt`·errors, `dist-2.5.0-self-test.txt`·errors, `smoke/run-01~05/`의 출력·오류·렌더, `smoke-results.json`, `render-results.json`, `preparation-completion.json`. #57의 초기 실패/보완·실제 10.01초 확인은 #57과 `artifacts/quota-tooltip-dismissal-20261015/`의 별도 과거 기록이다. 원본 로그·스크립트·바이너리·개인 자료는 커밋하지 않는다.
- 실제 재부팅/재로그인·절전 복귀·전체 네트워크 단절/재연결·장시간 조회·배너의 시각적 노출·실제 OS 테마/고대비/DPI·다중 모니터·스크린리더 실기는 미수행이다.
- 이번 준비에서 Mac 빌드/native·별도 Shared.Tests·실제 공개 상태/독립 계정 CLI probe는 수행하지 않는다. 공통/Mac 코드가 그대로인 Windows 버전·기록 PR이므로 Mac 재검증을 요청하지 않는다.
- **새 준비 PR 병합·배포 백업·publish·EXE 교체·최종 단일 EXE 검사·복원은 미승인·미수행**이다. 기존 배포 EXE와 자동 시작 설정을 유지한다.
