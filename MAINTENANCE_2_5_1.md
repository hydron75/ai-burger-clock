# AI Burger Clock 2.5.1 — Windows Provider 상세 닫힘 보완 배포

기록일: **2026-10-15 KST**. 상태: **#58 승인·병합 / 정상 종료·WAL 확인·백업 후 2.5.1 배포 완료 / 최종 단일 EXE self-test·smoke 통과 / 트레이·상태 창·상세 닫힘 정상 사용자 확인**.

#57은 main `a1d99e5a79cbda471a79a87a80a0894da1073990`에 병합된 상태를 확인했다. main에서 `git pull --ff-only` 후 `feature/windows-release-2-5-1`을 만들었다. 처음에는 버전·기록만 올렸고, 후속 사용자 요청으로 smoke 진단·검사 창 수명 보완을 추가했다. 범위는 **Windows만 변경(버전·Windows 검사·기록)**이다. 준비 검사 뒤 사용자 승인으로 #58 Draft를 해제·병합하고 main을 fast-forward했다. **깨끗한 병합 main `2897aea54141f9ed55054847a0268f73d4f79eaa`에서 publish한 2.5.1이 현재 배포본**이다. 아래에는 초기 실패·준비 검사와 실제 배포 결과를 구분해 남긴다.

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

2.5.0 실제 EXE 소스 `af1c831` 이후 main의 Windows C# 차이는 #57의 두 파일뿐이다. `55e37f5`는 이전 배포 기록, #53·#55·#56은 Mac 및 Mac 기록 변경으로 Windows 신규 기능에 세지 않는다. 이번 PR에서는 제품 C#를 바꾸지 않고 Windows 검사 `QuotaToolTipUiChecks`의 수명 진단·fixture 보호만 추가했다. 공통 원본 28개·공통 검사·Mac 파일/버전·MACOS_PORT·AGENTS·조회 정책·CLI·알림·DB schema 2·자동 시작 설정은 변경하지 않는다.

## 변경 파일

기존 수정:

- `AiBurgerClock.csproj`: Version / AssemblyVersion / FileVersion 함께 변경.
- `QuotaToolTipUiChecks.cs`: 검사 전용 창 수명 진단, 수동 닫힘 보호와 버튼/Close 회귀 검사 3건(Close 호출 2회).
- `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`: 새 닫힘 사용법·준비/배포 검사 결과와 백업·복원 기록 연결.

신규:

- `MAINTENANCE_2_5_1.md`: 준비 검증·Git 기준점·실제 EXE·백업·교체 결과와 복원 계획.

소스 파일은 추가하지 않는다. 루트·Properties C# **76개(기능 40 + 검사·진단 35 + 명찰 1)**, Mac 호스트 7개, 공통 검사 입구 1개로 총 **84개**다. 검증용 로컬 스크립트·바이너리·로그는 ignored `artifacts/`에만 둔다.

## 초기 준비 검사 — 보완 전 이력

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

**이 단계에서는 실패 2/5회이므로 배포 준비를 보류했다.** 통과한 회차의 smoke는 기존 2.5.0의 492 → **527(+35)**이다. +35는 #57의 Windows UI 검사이며 self-test 증가는 아니다. 통과 회차에서 상태·통계·공휴일·알림 요청·한도/시간층·기록·Refresh·상세 닫힘과 합성 Gemini 10초 지연 중 응답성 검사가 통과했다. 실패 회차는 중간 종료했으므로 뒤의 검사를 통과로 세지 않는다. 이후 진단·보완·최종 검사는 아래에서 구분한다.

### 당시 실패의 확인 범위

- 두 오류 모두 **`System.InvalidOperationException: Sequence contains no matching element`**다. `QuotaToolTipUiChecks.CheckDismissalAsync`의 `Box()`가 독립 검사 패널에서 `CodexQuotaBox`를 찾지 못했다(`QuotaToolTipUiChecks.cs:214`).
- 3회는 실제 만료 검사 뒤 `CheckSuppressed("real timeout")` → 255행/283행, 4회는 9초 유지 루프의 `Move(Box())` → 270행이다. 대기 시간 초과가 아니며, 만료/조회 대기를 늘려 해결했다고 적지 않는다.
- `SmokeDiagnostics`의 실패 시점에서 기존 상태 창과 한도 화면은 보이고 Refresh·Statistics·상태 전환 버튼은 활성 상태였다. 공식/한도 및 가짜 조회의 **진행 건수는 0**, 합성 10초 Gemini 조회는 **시작 전**, 바깥 smoke 타이머의 최대 `callbacksInFlight`는 **1**이다. 조회 지연이나 바깥 타이머 재진입을 뒷받침하는 기록은 없다.
- 독립 fixture의 합성 입력은 Codex를 계속 포함한다. 정상 `UpdateQuotas` 재생성은 같은 동기 호출에서 박스를 다시 만든다. 기존 앱 context의 매초/조회 갱신은 다른 패널을 대상으로 한다. **당시에는 fixture가 박스를 잃은 원인과 제품 결함 여부가 미확정**이었다. 초기 진단에는 이 fixture의 Dispose/종료 이벤트·Controls 목록이 없어 둘을 확정 구분할 수 없었다.
- 당시에는 fixture의 `IsDisposed`·`Disposing`·자식 이름 목록·부모 종료/Dispose·레이아웃 재생성 진입/종료 진단이 필요하다고 보고하고 버전·기록 PR을 Draft로 올렸다. 초기 실패를 재실행으로 덮지 않았으며, 사용자가 후속 진단·보완·최종 smoke 5회를 요청했다.

### 합성 렌더 비교

동일한 bin 실행 경로의 #57 최종 smoke 렌더와 SHA-256을 비교했다. 통과한 **1·2·5회는 PNG 20/20개·layout JSON 14/14개가 전부 동일**하다. 실패한 **3·4회는 생성된 PNG 14/14개·layout 9/9개만 동일**하며 나머지는 생성 전 종료했다. 이 회차를 전체 화면 동일로 적지 않는다. 일부만 캡처된 실패도 모두 로컬 증거에 보관한다. 실제 계정 화면이나 dist/bin 실행 경로 차이를 비교에 섞지 않는다.

## #58 후속 진단·검사 창 수명 보완

**판단: 이번 박스 누락은 검사 전용 창의 수명 관리 문제다. 제품의 한도 조회 지연이나 상세 팝업 동작을 수정한 것이 아니다.**

1. 진단 커밋 `42605a90448f706423745ed65c9c189af42764d9`에서 FormClosing/FormClosed·Disposed·ControlAdded/Removed, cleanup/레이아웃 갱신 중 여부, thread/SynchronizationContext·handle·자식 목록·layoutKey를 남겼다. 박스가 없으면 이 상태와 스택을 기록한 뒤 **여전히 실패**한다. 초기 예외 뒤 정상 정리와 원인인 선행 Dispose를 구분한다.
2. 진단만 추가한 전체 smoke는 **5회 모두 527 PASS·ExitCode 0**이었다(47.76/47.89/48.11/48.03/47.77초). 이 결과만으로 해결됐다고 판단하지 않았다.
3. 사용자가 **앞선 검사 중 검사 창을 직접 닫은 적 있음**을 확인했다. 일반 Form인 `DetailTestHost`에는 수동 닫기 보호가 없어 await 중에도 사용자 닫기가 가능했다.
4. ignored 합성 probe에서 **이 fixture에만 `Form.Close()` 요청을 주입**했다. `host-closing(UserClosing)` → `host-closed` → `view/host-disposed(cleaningFixture=false)` → `box-missing(Disposed=true, Controls=0)` 순서로 같은 Box 누락 경로가 재현되고 **ExitCode 1**이었다. UI thread 2 / WindowsFormsSynchronizationContext로, 스레드 이탈이나 조회 지연 증거는 없었다. 실제 계정·DB·배포 앱은 probe에 쓰지 않았다.
5. 보완 커밋 **`50bde2f4c3b96562ffa893832f92d276587df5fd`**에서 검사 창 제목을 명확히 하고 닫기 버튼을 없앴다. `OnFormClosing`은 **UserClosing만 취소**하며 정상 using/Dispose·OS/application 종료 경로는 막지 않는다. 실제 `host.Close()`를 상세 표시 중과 닫힌 상태에 호출하는 회귀 검사 등 **3건**을 추가했다.
6. 같은 외부 Close 주입 probe는 보완 후 **38 PASS·ExitCode 0**으로 완료됐다. `Close()` 뒤에도 fixture가 보이고 Dispose되지 않았으며, 마지막 정상 cleanup의 Dispose 기록은 남았다. 제품 타이머 10,000ms·hover 대기·9초 유지·검사 timeout은 그대로이고, 실패하는 박스를 새로 만들거나 예외를 삼키거나 재시도하지 않는다. OS/application 종료를 취소하지 않는다는 것은 소스 조건 확인이며 실제 OS 종료 시험은 아니다.

초기 두 회차에는 수명 진단이 없어 각각의 닫기 행동 시점을 소급해 확정할 수는 없다. 다만 **사용자 확인 + 동일한 종료/박스 누락 재현 + 같은 주입의 보완 후 통과**를 근거로 수동 닫기에 취약했던 검사 fixture를 보완했다. 모든 종류의 제품 오류가 없다는 보증으로 확대하지 않는다. #30의 뜻밖의 한도 모드 관찰은 별개로 유지한다.

### 보완 후 준비 후보 최종 검사

SDK **10.0.402**, 코드 커밋 **`50bde2f4c3b96562ffa893832f92d276587df5fd`**에서 `build.ps1`을 다시 실행해 **경고 0·오류 0**, self-test **251,826건(공통 251,519 + Windows 307)·실제 ExitCode 0**을 확인했다. 기존 dist 2.5.0 및 보완 전과 출력 전체가 같고 증감은 0이다. 제품/공통 C#와 조회·문구는 바꾸지 않았다.

같은 최종 Release bin으로 `--smoke-test --report-directory`를 **정확히 5회** 실행했다. 앞의 진단 5회·의도적 실패 probe와 별개이며 실패 제외·자동 재시도는 없다.

| 최종 회차 | 실제 ExitCode | PASS | FAIL 표시 | 소요 시간 |
|---|---:|---:|---:|---:|
| 1 | 0 | 530 | 0 | 47.87초 |
| 2 | 0 | 530 | 0 | 48.22초 |
| 3 | 0 | 530 | 0 | 48.21초 |
| 4 | 0 | 530 | 0 | 47.96초 |
| 5 | 0 | 530 | 0 | 47.99초 |

**최종 실패 0/5회**다. 직전 #58의 527 → **530(+3)**은 닫기 버튼 없음·활성 상세 중 Close 보호·상세가 닫힌 상태의 Close 보호 검사다. 실제 `host.Close()` 호출은 회차당 **2회**다. 기존 제품 10초 만료는 **9.98~10.01초**에 관측됐고, 클릭/Esc·재등장 방지·숨김/재열기·다른 Provider 전환·시간층/접근성·기록·Refresh·합성 Gemini 지연 중 응답성 등 기존 검사도 통과했다. 매회 cleanup=true에서 view/host Dispose 기록 2건이 남고 정상 메시지 루프 종료 검사도 통과했다. 실제 OS 종료 시험은 하지 않았다.

**5회 모두 PNG 20/20개·layout JSON 14/14개가 같은 bin 경로의 #57 최종 렌더와 SHA-256 동일**하다. 검사 창만 바뀌었고 제품 화면·막대·문구·배치는 같다. 이 코드 커밋 뒤 준비 PR에서는 Markdown 기록만 변경했다. 승인 후 병합 main의 실제 publish·단일 EXE 검사는 아래 별도 배포 절에 기록한다.

## Git 기준점과 실행 파일

| 기준 | 전체 커밋 해시 / 상태 |
|---|---|
| 기존 배포 2.5.0 EXE 소스 | `af1c831e70f0a956804255771a434f8103e1f5ea` |
| 기존 2.5.0 배포 결과 문서 | `55e37f556cced0452f4d54d5d047757d5e1a3dbd` — EXE 소스가 아님 |
| #57 검증·사용자 확인 HEAD | `229b3d43d5d81e9428e8c3c82377596952370696` |
| #57 main 병합 / 준비 브랜치 출발점 | `a1d99e5a79cbda471a79a87a80a0894da1073990` |
| 버전 변경 / 초기 검증 코드 | `12b9103a600e5837add7d8c8181c6fa7650ef403` |
| 검사 수명 진단 코드 | `42605a90448f706423745ed65c9c189af42764d9` |
| 검사 보완 / 준비 후보 최종 검증 코드 | `50bde2f4c3b96562ffa893832f92d276587df5fd` |
| #58 승인·병합한 HEAD | `9838b6ea3be26f47b4caf7b5bd62ef8c62232fd3` |
| #58 main 병합 | `2897aea54141f9ed55054847a0268f73d4f79eaa` |
| 최종 2.5.1 단일 EXE 소스 | **`2897aea54141f9ed55054847a0268f73d4f79eaa`** — 병합 main, 깨끗한 작업 트리 |

| 항목 | 교체 전·백업한 2.5.0 |
|---|---|
| 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion | `2.5.0.0` |
| ProductVersion | `2.5.0+af1c831e70f0a956804255771a434f8103e1f5ea` |
| 크기 | **3,450,091 bytes** |
| SHA-256 | `42EBF164BAB599B49D20CCB2E7DADB8A8A5FAE2395094770D00EBC4AF8ACB4E9` |

후보 bin EXE는 주변 DLL이 필요한 apphost다. **배포 단일 EXE가 아니며 dist에 복사하지 않았다.** 다음 표는 준비 당시 결과이며, 실제 배포는 그 아래의 병합 main 단일 EXE다.

| 항목 | 준비 당시 후보 bin apphost |
|---|---|
| 경로 | `bin/Release/net10.0-windows/win-x64/AI Burger Clock.exe` |
| FileVersion | `2.5.1.0` |
| ProductVersion | `2.5.1+50bde2f4c3b96562ffa893832f92d276587df5fd` |
| 크기 | **163,328 bytes** |
| SHA-256 | `31947D3194A1707BF5648B752DDEB3F5B4F7277F334D6523635E7C5B73D433A7` |

### 최종 배포 단일 EXE

| 항목 | 현재 배포 2.5.1 |
|---|---|
| 경로 | `dist/win-x64/AI Burger Clock.exe` |
| 소스 커밋 | `2897aea54141f9ed55054847a0268f73d4f79eaa` |
| FileVersion | `2.5.1.0` |
| ProductVersion | `2.5.1+2897aea54141f9ed55054847a0268f73d4f79eaa` |
| 크기 | **3,474,667 bytes** |
| SHA-256 | `0276257DE59E8BDBD95B157BC44A640A4C5664C22DF868EE9CE867DBAA768B72` |

이 배포 결과를 적는 후속 문서 커밋은 **EXE의 소스 커밋이 아니다.** 문서 반영 뒤 다시 publish하지 않는다.

## 실제 백업·교체와 최종 검증

1. 사용자 승인으로 #58의 승인 HEAD `9838b6e`를 지정해 Draft를 해제·병합했다. main에서 `git pull --ff-only`하고 깨끗한 작업 트리의 전체 해시 `2897aea54141f9ed55054847a0268f73d4f79eaa`를 확정했다. 승인 HEAD와 병합 main 사이 제품/검사 C#·csproj·공통 원본 차이는 없다.
2. 사용자가 기존 2.5.0을 **정상 종료**한 뒤 앱 프로세스 0개와 실제 DB의 `-wal`·`-shm` 부재를 확인했다. sidecar를 삭제하거나 강제 종료하지 않았다.
3. **2026-10-15 21:30 KST**, 존재하지 않던 **`../backups/release-2.5.1-20261015/`**를 새로 만들었다. 이전 EXE·dist ZIP·실제 이전 EXE 소스의 ZIP·닫힌 DB를 보관했고, 원본/복사본 및 ZIP 안 EXE 해시 일치와 백업 중 프로세스/WAL/SHM 부재를 확인했다. DB 내용과 DB 해시는 공개하지 않는다.
4. 같은 병합 main에서 `build.ps1 -Publish`를 실행했다. SDK **10.0.402**, **경고 0·오류 0**, publish 성공과 최종 단일 EXE self-test **251,826건(공통 251,519 + Windows 307)·실제 ExitCode 0**을 확인했다. 준비 후보·기존 2.5.0 대비 검사 수 변화는 0이다.
5. 실제 dist 단일 EXE로 별도 `--smoke-test --report-directory`를 **1회** 실행해 **530 PASS·실패 표시 0·실제 ExitCode 0, 47.29초**를 확인했다. 준비 후보의 최종 5회와 합쳐 세지 않는다. 제품의 실제 10초 만료는 **9.99초**로 관측됐고 클릭/Esc·재등장 방지·기록/Refresh·시간 비교·합성 Gemini 지연 중 응답성과 정상 메시지 루프 종료도 통과했다. BalloonTipShown 이벤트 19건은 네이티브 이벤트 확인이지 실제 배너를 눈으로 확인한 것은 아니다. 임시 DB·가짜 HTTP/CLI이며 `--verify-autostart`는 사용하지 않았다.
6. 정상 앱 재실행 전까지 실제 DB는 백업 직전과 같은 해시이며 WAL/SHM도 없었다. 이후 일반 실행은 정상 캐시를 갱신할 수 있으므로 **전체 작업 동안 사용자 DB가 불변이었다고 주장하지 않는다.** 새 단일 EXE를 백업의 `published-2.5.1.exe`에도 보관해 dist와 버전·크기·해시 일치를 확인했다.
7. 자동 시작 등록은 읽기 전용으로 전후 비교했다. HKCU Run `AI Burger Clock`은 **String / 큰따옴표로 감싼 같은 절대 dist 경로 + ` --autostart`**, StartupApproved/Run은 **Binary / `020000000000000000000000`**으로 종류·값이 모두 유지됐다. 등록 경로가 가리키는 실제 EXE가 위 2.5.1 버전·해시와 일치한다. 등록·설정은 쓰지 않았고 실제 재부팅/재로그인 검사는 하지 않았다.
8. **21:32 KST** 새 dist를 인자 없이 일반 실행해 상태 창을 열고 앱 한 개·정확한 dist 실행 경로·응답/입력 대기 준비를 확인했다. 조작 도구가 실제 창을 제공하지 못해 최종 화면은 사용자에게 확인받았다. **21:35 KST 사용자 확인: 트레이·상태 창 및 상세의 10초 자동 닫힘·클릭/Esc 닫힘·Provider 박스 이탈 전 재등장 방지 모두 정상.** 확인 뒤에도 dist 해시는 같았다. 현재 실행 중인 앱은 **2.5.1 배포본**이다.
9. 배포 검증에서 복원이 필요한 문제는 없었다. **2.5.0 복원은 적용하지 않았고 복원 시험도 하지 않았다.** Mac·공통 코드와 자동 시작 등록은 변경하지 않았다.

### 실제 로컬 백업

경로는 **`../backups/release-2.5.1-20261015/`**다. ZIP·DB·manifest·원본 로그·바이너리는 로컬에만 보관하고 GitHub에 올리지 않는다.

| 백업 파일 | 크기 | SHA-256 / 확인 |
|---|---:|---|
| `replaced-2.5.0.exe` | 3,450,091 bytes | `42EBF164BAB599B49D20CCB2E7DADB8A8A5FAE2395094770D00EBC4AF8ACB4E9` |
| `dist-2.5.0.zip` | 1,701,098 bytes | `3EBF7A68F9EEB1EC11BF436D547B0F40131242B1C3346BEDD9E7ED48BE55ACB4` |
| `source-2.5.0-af1c831.zip` | 3,294,160 bytes | `5B77855F5D2352CEC8FEDFCFD912DB6588EF67CFD88AE17EB05A71629FDE7AEB` |
| `burgerclock.db` | 비공개 | 정상 종료 뒤 원본/복사본 해시 일치 확인. 내용·해시는 공개하지 않음 |
| `published-2.5.1.exe` | 3,474,667 bytes | `0276257DE59E8BDBD95B157BC44A640A4C5664C22DF868EE9CE867DBAA768B72` |

소스 ZIP은 실제 이전 EXE 소스 `af1c831e70f0a956804255771a434f8103e1f5ea`의 `git archive`이며 csproj의 2.5.0도 확인했다. 문서 커밋 `55e37f5`의 ZIP이 아니다. 백업은 같은 이름의 폴더를 덮어쓰지 않고 새로 만들었다.

## 승인받은 교체 계획 — 위와 같이 실행, 복원 미적용

1. 위 보완·최종 검증 결과를 확인한 뒤 사용자가 새 준비 PR 병합·배포 교체를 승인하면 승인 HEAD를 지정해 병합하고 main에서 `git pull --ff-only`한다. 깨끗한 작업 트리와 병합 main 전체 해시를 **최종 EXE 소스**로 기록한다. 후속 문서 커밋을 EXE 소스로 적지 않는다.
2. 그때 실행 중인 앱을 **정상 종료**한다. 프로세스 0개와 `%LOCALAPPDATA%/AIBurgerClock/burgerclock.db-wal`·`-shm` 부재를 확인한다. 준비 검사의 종료로 대신하지 않는다. WAL/SHM이 남으면 삭제·강제 덮어쓰기를 하지 않고 원인을 확인한다.
3. 새 **`../backups/release-2.5.1-<교체일>/`**에 기존 dist ZIP·`replaced-2.5.0.exe`·실제 EXE 소스 `af1c831e70f0a956804255771a434f8103e1f5ea`의 source ZIP·닫힌 DB를 보관한다. 같은 폴더가 있으면 덮어쓰지 않는다. 원본/복사본·ZIP 안 EXE 해시 일치와 백업 중 프로세스/WAL 부재를 재확인한다. DB·manifest·백업은 로컬에만 보관한다.
4. **병합 main에서 `build.ps1 -Publish`**를 실행한다. 경고 0·오류 0, 최종 단일 EXE의 self-test 실제 ExitCode 0과 별도 smoke를 확인한다. 새 EXE도 백업 폴더의 `published-2.5.1.exe`에 보관하고 최종 소스 해시·버전·크기·SHA-256·실제 백업 경로를 이 문서에 적는다.
5. 자동 시작은 등록을 새로 쓰지 않고 **읽기 전용으로 전후 비교**한다. HKCU Run `AI Burger Clock`이 같은 큰따옴표 절대 dist 경로 + `--autostart`인지, 값 종류와 StartupApproved/Run 값이 유지되는지 확인한다. 등록 경로가 가리키는 실제 파일의 버전·해시가 새 2.5.1과 일치해야 한다. `--verify-autostart`·체크박스 조작은 하지 않는다.
6. 새 dist를 실행해 앱 한 개·실행 경로·응답·트레이·상태 창·한도/시간 비교·상세 10초 만료·클릭/Esc·재등장 방지·Refresh를 확인한다. 실제 화면을 조작 도구가 제공하지 않으면 사용자 확인과 자동 검사를 구분한다. 문제가 생기면 아래 계획으로 복원하고 알린다.

준비 시작·기존 배포본 재실행 뒤·최종 교체 전후의 읽기 전용 확인에서 자동 시작 Run은 **String / 같은 절대 dist 경로 + `--autostart`**, StartupApproved는 **Binary / `020000000000000000000000`**으로 동일했다. 최종 등록 경로가 가리키는 실제 파일은 새 2.5.1이다. 이 확인을 재부팅/로그인 시험으로 적지 않는다.

### 2.5.0으로 되돌리기

새 앱을 정상 종료하고 프로세스·WAL/SHM 상태를 확인한다. 교체 후의 현재 DB를 별도로 보존하며 WAL·로그·기록을 삭제하지 않는다. 새 백업의 `replaced-2.5.0.exe` 또는 기존 dist ZIP에서 **EXE만 원래 dist 경로로 복원**한다. FileVersion **2.5.0.0**, ProductVersion `2.5.0+af1c831...`, 위 SHA-256을 확인하고 `--autostart`로 실행한다. 같은 자동 시작 등록·경로와 트레이·창을 확인한다.

#57은 schema 2·한도 캐시·기록 형식을 바꾸지 않는다. 2.5.0으로 돌아가면 상세의 새 10초·클릭/Esc·재표시 차단 규칙이 없어지는 것이 예상 차이다. 백업 DB를 자동으로 덮어써 새 기록을 잃게 하지 않는다. DB 복원이 별도로 필요하면 현재 데이터를 보존하고 유실 위험과 범위를 설명한 뒤 승인받는다. **실제 복원 시험과 복원 적용은 미수행**이다.

## 실제 사용자 확인·로컬 증거·미수행

- **#57 테스트 앱의 기존 사용자 확인(2026-10-15 KST):** 10초 자동 닫힘·클릭/Esc·박스를 벗어나기 전 재등장 방지 모두 정상. 이 과거 확인과 아래 최종 배포본의 새 확인을 구분한다.
- 준비 단계에서는 사용자가 #57 테스트 앱을 정상 종료했고 프로세스 부재를 확인한 뒤 빌드·smoke를 진행했다. 준비 검사 후 변경하지 않은 **2.5.0 dist를 `--autostart`로 다시 실행**해 앱 한 개·기존 dist 경로·응답/입력 대기 준비·당시 EXE 버전/크기/해시와 자동 시작 등록 불변을 확인했다. 그 단계는 새 2.5.1 배포나 2.5.0 복원이 아니었다.
- 로컬 검증 증거: ignored `artifacts/release-2.5.1-preparation-20261015/`의 `build.txt`, `candidate-self-test.txt`·errors, `dist-2.5.0-self-test.txt`·errors, `smoke/run-01~05/`의 출력·오류·렌더, `smoke-results.json`, `render-results.json`, `preparation-completion.json`. #57의 초기 실패/보완·실제 10.01초 확인은 #57과 `artifacts/quota-tooltip-dismissal-20261015/`의 별도 과거 기록이다. 원본 로그·스크립트·바이너리·개인 자료는 커밋하지 않는다.
- 후속 진단/보완 증거는 ignored `artifacts/release-2.5.1-smoke-repair-20261015/`의 `diagnostic-build.txt`·self-test, `diagnostic/run-01~05/`, `close-before.txt`·errors와 `close-after.txt`·errors, `final-build.txt`·self-test, `final/run-01~05/`, `final-smoke-results.json`, `final-render-results.json`, `completion.json`에 별도 보관한다. 정상 정리와 예상치 않은 Dispose는 `INFO: tooltip fixture lifetime`의 cleanup/updating 플래그와 이벤트 순서로 구분한다. 합성 probe 프로젝트·원본 로그는 공개하지 않는다.
- 후속 준비 검사 뒤에도 기존 **2.5.0 dist를 다시 실행**하고 버전/해시·자동 시작 등록 유지를 확인했다. 이후 #58 승인·정상 종료·백업·실제 publish와 최종 검증을 거쳐 **현재 실행 중인 앱은 2.5.1 배포본**이다.
- **최종 2.5.1 배포본의 새 사용자 확인(2026-10-15 21:35 KST):** 트레이·상태 창과 상세의 10초 자동 닫힘·클릭/Esc·박스 이탈 전 재등장 방지 모두 정상. 이는 자동 조작 도구의 화면 관찰이 아니라 사용자가 실제 화면을 확인한 결과다.
- 실제 배포 증거는 ignored `artifacts/release-2.5.1-deployment-20261015/`의 `backup-before.json`, `publish-build.txt`, `final-self-test.txt`·errors, `final-smoke/smoke.txt`·errors·render, `final-smoke-result.json`, `validation.json`, `completion.json`에 별도로 둔다. 백업의 manifest와 원본 로그·실제 계정 값·DB 내용/해시는 공개하지 않는다.
- 실제 재부팅/재로그인·절전 복귀·전체 네트워크 단절/재연결·장시간 조회·배너의 시각적 노출·실제 OS 테마/고대비/DPI·다중 모니터·스크린리더 실기는 미수행이다.
- 이번 준비·배포에서 Mac 빌드/native·별도 Shared.Tests·실제 공개 상태/독립 계정 CLI probe는 수행하지 않았다. 공통/Mac 코드가 그대로인 Windows 버전·검사·기록 PR이므로 Mac 재검증을 요청하지 않았다. 일반 앱 실행에 따른 정상 계정 조회와 별도 probe를 혼동하지 않는다.
- **#58 병합·배포 백업·publish·EXE 교체·최종 단일 EXE 검사·실제 화면 사용자 확인은 완료**했다. 자동 시작 등록은 그대로다. **실제 2.5.0 복원 시험과 복원 적용은 미수행**이다.
