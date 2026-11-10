# AI Burger Clock 2.9.1 — 링크·상세·SQLite·외부 배포·사용자 확인 완료

## 배포·사용자 확인 결과 (2026-11-10 KST 기록)

버전 PR **#139**는 main **`d304a8a9059f9128ebb08a544765bbafa90ec24f`**에 병합됐습니다.
이후 사이트 배포 → 사용자 외부 Windows 배포·실행 순서로 진행했습니다.
후보 빌드·자체 검사·smoke 수치와 과거 준비 기록은 아래에 그대로 보존하며 현재 확인 결과는 이 절을 기준으로 합니다.

### 사이트 배포·아이폰 사용자 확인

- **2026-11-09 21:40:55 KST**, 사이트 **버전 13** 비공개 배포 성공. 소스는 `109535c82339c67b24b488928519a15a5259e683`, 화면/API 기준 커밋은 **`d304a8a`**입니다. 소유자 1명만 허용하는 범위를 유지했습니다.
- xAI 한도 박스의 사용량 링크를 제거하고 상태 카드 상세를 공통 틀로 맞췄습니다. 모바일 첫 줄은 승인된 `탭: 공식 상태 페이지 열기`입니다. 네 공식 상태 카드의 링크와 기존 한도 수신·계산·막대·시간층·경계선은 유지합니다.
- 병합된 main C# 상세 **47/47 일치**, 전체 합성 검사 **85/85**, typecheck·Worker build·ESM 검증 통과. 배포된 HTML/JavaScript가 검증 산출물과 같고 기준 커밋 표시도 맞음을 확인했습니다.
- 배포 후 실제 `GET /api/quota`와 `GET /api/status`가 각각 **4건 모두 HTTP 200**으로 처리된 기록을 확인했습니다. 기존 저장 레코드의 식별/순서 메타데이터도 유지됐습니다. 관리 도구가 스냅샷 본문을 잘라 반환하므로 이 결과를 실제 한도 수치 전체 대조로 해석하지 않습니다.
- **아이폰에서도 사이트가 정상인 것은 사용자 확인**입니다. 사이트 주소·서비스 토큰·실제 한도 값이나 캡처는 기록하지 않습니다.

### Windows 외부 교체 기록과 실제 EXE

사용자가 Codex 밖 MSI PowerShell로 실행한 receipt의
`artifacts/release-2.9.1-preparation-20261109/deployment-receipt.run/verified-before-launch.json`에는
**2026-11-09 21:46:45 KST** 기준 Receipt/Preflight/Backup/Replace/Inspect가 모두 **Passed**로 남아 있습니다.
프로세스 부재·실제 DB/WAL/SHM·백업/후보 신원과 기존 알림 등록·자동 시작 불변·해제 dry-run은 이 단계 기록으로 구분합니다.
운영 알림 재등록은 수행하지 않았습니다.

| 실제 배포 EXE 신원 | 확인 결과 |
|---|---|
| 경로 | `dist/win-x64/AI Burger Clock.exe` (열린 실제 경로와 요청 경로 일치) |
| FileVersion | **`2.9.1.0`** |
| ProductVersion | `2.9.1+be23038be09a43d76de610e321523bcea3a7f550` |
| 소스 커밋 | `be23038be09a43d76de610e321523bcea3a7f550` (PR 병합 커밋과 구분) |
| SHA-256 | **`1A90FD80631F6E2D0F541B73CD1A2AC956291D98510596CEFC24831625E85986`** — 후보/receipt와 일치 |
| 실제 백업 | `../backups/release-2.9.1.0-20261109-214642-9952c6ff/` |
| 백업 이전 EXE | `previous.exe` 존재, SHA-256이 기록된 2.9.0의 `17CB43B043541C103586BB884A7D1BF1BA8E303D4CBB5D8B3457B8A6C6046084`와 일치 |

위 파일은 **시작 직전에** 저장하므로 그 안의 `Launch=NotRun`은 실제 실행 실패의 증거가 아닙니다.
최초 배포 명령의 최종 Launch/Succeeded 콘솔 결과는 받지 않았으므로 이를 모두 Passed였다고 소급해 기록하지 않습니다.

2026-11-10에 같은 receipt를 다시 실행한 출력은 **Receipt/ReceiptAlreadyUsed**, 나머지 단계 **NotRun**이었습니다.
이 재실행에서는 새 백업·교체·등록·실행을 하지 않았고 `Checks=false`는 미실행 초기값입니다.
기존 `.run`·receipt·백업을 삭제하거나 새 receipt로 다시 교체하지 않았습니다.
Codex는 배포 EXE와 이전 EXE 백업의 신원을 읽기 전용으로 대조했습니다.

### 사용자 외부 실행·화면 확인

**2026-11-10 KST**, 사용자가 이미 교체된 **2.9.1**을 외부에서 실행한 뒤 다음을 확인했습니다. 정확한 실행 시각은 전달받지 않았습니다.

- 트레이·상태 보기·한도 보기 모두 정상.
- xAI 사용량 링크 제거 확인.
- 상세 팝업 확인.

이는 **사용자 화면 확인**이며 Codex가 운영 앱을 실행하거나 화면을 직접 조작해 확인한 결과가 아닙니다.
최초 배포의 Launch 출력과 별개로, 후속 외부 실행 및 현재 화면 정상은 위 사용자 확인으로 기록합니다.
이번 결과 갱신은 이 문서만 변경하며 코드·버전·패키지·사이트·운영 EXE·DB·설정·등록을 바꾸지 않습니다.
실제 복원·장기 상주·OS 알림 노출 및 새 실행의 단일 프로세스/기본 DB·로그 갱신 재확인은 별도로 수행하지 않았습니다.

## .NET 10.0.15 업데이트 후 읽기 전용 확인 (2026-11-11 KST)

사용자가 **Windows Update의 .NET 10.0.15와 Visual Studio 업데이트를 설치하고 앱을 다시 실행했다**고 알려줬습니다.
Codex는 설치 버전과 실행 중인 앱의 EXE 신원·로드 모듈만 읽기 전용으로 확인했습니다. 설치·업데이트·앱 종료/재실행·빌드는 수행하지 않았습니다.

| 항목 | 확인 결과 |
|---|---|
| 설치된 SDK x64 | **10.0.404**, **11.0.100** |
| Desktop Runtime x64 | 6.0.36, 8.0.34, **10.0.15**, 11.0.0 |
| Desktop Runtime x86 | 8.0.34, **10.0.15**, 11.0.0 |
| Visual Studio | **18.12.0** (`18.12.12405.243`) |
| 실행 중인 배포 앱 | **2.9.1.0**, 프로세스 **1개**, 응답 정상 |
| 프로세스 시작 | **2026-11-11 07:51:46 KST** |
| 실제 CLR 모듈 | `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.15\coreclr.dll` |
| 실제 WinForms 모듈 | `C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.15\System.Windows.Forms.dll` |

두 모듈의 파일 버전은 **10.0.1526.53008**입니다. .NET 11.0.0도 설치돼 있지만 이 앱이 실제로 로드한 CLR·WinForms는 모두 **10.0.15**입니다.
앱의 ProductVersion은 `2.9.1+be23038be09a43d76de610e321523bcea3a7f550`,
SHA-256은 **`1A90FD80631F6E2D0F541B73CD1A2AC956291D98510596CEFC24831625E85986`**로 기존 배포 기록과 같습니다.
따라서 같은 framework-dependent EXE에 재빌드·재배포 없이 공유 런타임 패치가 적용된 상태를 확인했습니다.

프로젝트 `global.json`은 `10.0.100`·`rollForward=latestFeature`·`allowPrerelease=false`를 유지합니다.
설정상 설치된 10.0 SDK 중 10.0.404를 선택하며, 이번 확인에서 SDK 명령으로 빌드/restore하거나 설정을 바꾸지는 않았습니다.
당시 후보를 SDK 10.0.403으로 빌드한 아래 검사 기록은 소급 수정하지 않습니다.
이번 PR은 이 문서만 변경합니다. 운영 EXE·DB·사이트·설정·등록은 변경하지 않았고 DB·실제 계정/한도 값·인증/토큰은 읽지 않았습니다.
새 런타임으로의 build·self-test·smoke·장기 운영·화면/알림 재검증은 실행하지 않았습니다.

## 후보 준비 당시 기록 (이하 보존)

아래의 미배포·미확인 표기는 후보 준비 당시 상태입니다. 이후 배포·사용자 확인 결과는 위 절에 구분했습니다.

2026-11-09 KST. main `9c687a5f6feb6116861741077221bff79aadbe79`에서 후보를 준비했고,
추가된 #137(Mac 버전/문서만)을 포함한 최신 main `972c20c23c5c0845dc773cb63f7d83f420b1113e`도 브랜치에 반영했습니다.
후보 소스와 현재 브랜치의 공통/Windows C#·Windows csproj·Shared.Tests csproj·공통 원본 목록 차이는 0입니다.
이번 PR은 **Windows 버전 세 필드와 문서만** 바꿉니다. 공통/Windows C#·Mac·패키지·배포 스크립트는 다시 바꾸지 않습니다.
운영 EXE 교체·등록·실행과 사이트 배포는 수행하지 않았습니다. smoke 후 기존 2.9.0 외부 재실행 완료는 사용자 확인입니다.

## 2.9.0 이후 포함 변경

- **#128:** xAI 상태 카드의 사용량 링크와 공통 주소/전용 핸들러 제거. `status.x.ai`로 가는 공식 상태 카드 클릭, 네 카드의 크기·간격과 한도 조회는 유지합니다. Mac 전환은 종속 #129에 포함됐습니다.
- **#130:** 네 Provider의 한도 상세 제목·범위·리셋·시간 비교·조회 상태/주기와 상태 상세 조작·현재 판정·대상·조회·사건·출처를 같은 틀로 맞춥니다. Windows는 같은 320 DIP 팝업·공백 우선 줄바꿈을 사용하고, 긴 본문을 작업 영역 안에서 스크롤합니다. 처음 긴 상세를 열 때 native scrollbar 폭으로 다시 측정해 마지막 문장이 잘리던 문제도 고쳤습니다. 문구의 의미·판정·조회·알림과 상태 카드 클릭/우클릭은 유지합니다. Mac 전환/높이 제한은 #131·#133에 포함됐습니다. [한도 상세](QUOTA_PROVIDER_DETAILS.md) · [상태 상세](STATUS_PROVIDER_DETAILS.md) · [넘침 재현·보정](DETAIL_OVERFLOW_VALIDATION.md).
- **#134:** Windows·Mac·Shared.Tests의 Microsoft.Data.Sqlite를 10.0.14로 맞췄습니다. 기본 SQLitePCLRaw 2.1.13을 사용하며 직접 지정하지 않습니다. 실제 엔진은 **3.53.3**입니다. 기존 DB 복사본 읽기·쓰기·온라인 백업·WAL 회귀와 단일 EXE native 로딩의 선행 검증은 [패키지 기록](SQLITE_PACKAGE_UPDATE.md)에 보존합니다. DB schema와 사용자 DB는 이번 준비에서 바꾸지 않습니다.
- **#135:** receipt가 없으면 `UnexpectedFailure` 대신 **`ReceiptNotFound`**로 첫 단계에서 중단합니다. 나머지 안전 검사·백업·교체·EXE만 복원하는 동작은 그대로입니다. 합성 before/after 각 36개 시나리오·208개 검사는 해당 PR의 선행 결과이며 이번 버전 PR에서 스크립트 수정/재실행은 하지 않았습니다. [배포 계약](WINDOWS_DEPLOYMENT.md#receipt-파일-누락의-실패-코드-2026-11-09-draft).
- **#136:** Mac smoke에서 상태 항목의 화면이 가려지면 여유 화면을 선택합니다. Windows 동작 변화는 없으며 이 버전 PR에서 Mac 파일·Mac 버전·Mac 빌드는 변경하지 않습니다.

## 후보·검사

후보는 `artifacts/release-2.9.1-preparation-20261109/candidate/AI Burger Clock.exe`입니다.
framework-dependent 단일 EXE이며 .NET 10 Desktop Runtime x64를 사용합니다. SDK **10.0.403**, 빌드/publish 경고·오류는 **0**입니다.

| 항목 | 준비 결과 |
|---|---|
| 기능 기준 main / 추가 반영 main | `9c687a5f6feb6116861741077221bff79aadbe79` / `972c20c23c5c0845dc773cb63f7d83f420b1113e` (#137은 Mac 버전/문서만) |
| 후보 소스·버전 커밋 | `be23038be09a43d76de610e321523bcea3a7f550` |
| FileVersion / AssemblyVersion | `2.9.1.0` |
| ProductVersion | `2.9.1+be23038be09a43d76de610e321523bcea3a7f550` |
| 후보 크기 | **30,054,331 bytes** |
| 후보 SHA-256 | **`1A90FD80631F6E2D0F541B73CD1A2AC956291D98510596CEFC24831625E85986`** |
| `build.ps1 -ReportGroup release-2-9-1`·bin self-test | **293,036 PASS**, 실제 종료 코드 0 |
| 별도 Portable publish·후보 self-test | **293,036 PASS**, 실제 종료 코드 0, stderr 0 bytes |
| 별도 Shared.Tests | **291,907 PASS**, 실제 종료 코드 0 |
| 고정 after 슬롯 후보 smoke 1회 | **1,174 PASS**, 합성 PNG 60장, 실제 종료 코드 0 |
| #130 최종 검사본 `3198a2769f9db897cdcce8e0c6a8a8eebadaa662` 대비 | PNG **60/60**·layout JSON **41/41** SHA-256 바이트 동일 |
| 새 C# 기준 데이터의 SQLite 메모리 DB 조회 | 엔진 **3.53.3**. 운영/백업 DB 접근 없음 |
| 공식 receipt 생성 | 이전 2.9.0 성공 receipt의 Candidate를 읽어 생성, 종료 코드 0 |
| Git 읽기 사전 확인 | `C:\Windows\System32`에서 두 커밋 모두 exit 0, 파일 변경/앱 실행 없음. 사용자 외부 확인은 별도 |

공통 291,907 + Windows 1,129 = self-test 293,036건입니다. 기존 symlink fixture 1건은 파일시스템 링크 생성 제한으로 SKIP입니다.
smoke는 1회이므로 트레이 기록 반복 무증가 판정은 **NotEvaluated**입니다. 실제 계정/CLI 조회·사이트 전송·OS 알림·운영 등록은 하지 않습니다.
고정 after 슬롯만 stage/실행했고 운영 dist·기본/LocalCache DB를 읽거나 변경하지 않았습니다.
후속 문서 커밋과 PR 병합 커밋은 후보 바이너리의 소스 커밋이 아닙니다. 후보 신원은 위 `be23038`로 고정합니다.

보고서:

- `artifacts/release-2-9-1/build/20261109-210406-443/` — build·bin self-test
- `artifacts/release-2-9-1/shared-tests.txt` — 공통 검사
- `artifacts/release-2.9.1-preparation-20261109/` — candidate·publish·단일 EXE self-test·render comparison·receipt·Git 읽기 helper
- `artifacts/release-2-9-1-candidate/windows-tests/after/20261109-210639-971-accfb2/` — smoke 요약·합성 PNG/레이아웃

## 사이트 준비 — 아직 배포하지 않음

기존 준비본의 xAI 한도 박스 링크 제거와 상태 카드 title 상세 통일을 유지합니다. 네 상태 카드의 공식 상태 페이지 이동은 그대로입니다.
모바일에는 우클릭 기록이 없어 첫 줄만 승인된 **`탭: 공식 상태 페이지 열기`**로 표시합니다. 나머지 상세는 최신 main C#과 같습니다.
한도 박스에는 같은 상세 팝업이 없으므로 한도 상세 계약을 새로 추가하지 않습니다.

- 최신 main의 실제 공통 원본에서 합성 기준 **47건**을 다시 생성해 사이트 결과와 **47/47 일치**했습니다. 제거된 공통 `XaiUsage`도 없음을 확인했습니다.
- 사이트 전체 합성 검사 **85/85**, TypeScript typecheck·Worker build·ESM 검증 통과입니다. 네 한도 머리글의 링크 없음, 네 공식 상태 링크 유지, v1 대기/v2 xAI 행·막대 유지도 포함합니다.
- 기존 회귀 기준 데이터로 시간표 19,972·상태 161·권고 12·freshness 47·Grok 표시 240·시간 비교 4,457·팔레트 28건도 다시 통과했습니다. 이 기존 fixture의 원본 커밋과 이번 새 상세 47건의 최신 main 기준을 구분합니다.
- 소스 동기화·게시 API·실제 사이트 조회·DB·서비스 토큰 접근은 하지 않았습니다. 기존 배포 사이트/기준 커밋 표시는 그대로입니다. 승인된 실제 배포 때 기준 표시는 최종 병합 main으로 맞춥니다.

**순서:** 이 버전 PR 병합 → 사용자가 **‘사이트 배포’** 지시 → 준비본을 비공개 배포·확인 → 사용자 외부 앱 배포 명령 실행.
PR 병합만으로 사이트를 배포하거나 앱을 교체하지 않습니다. 주소·토큰·실제 한도 값은 이 문서/PR에 남기지 않습니다.

## Git 기준점·이전 신원과 복원

이전 신원은 `artifacts/release-2.9.0-preparation-20261109/deployment-receipt-retry-1.json`의 **Candidate**를
공식 `New-WindowsDeploymentReceipt.ps1 -PreviousIdentityPath`로 읽었습니다. 이전 파일을 고치거나 값을 추정하지 않았습니다.

| 2.9.0 이전 신원 | 값 |
|---|---|
| FileVersion | `2.9.0.0` |
| ProductVersion | `2.9.0+3102e1646c9bf9fc0bf52a80fddc335787a37aab` |
| SourceCommit | `3102e1646c9bf9fc0bf52a80fddc335787a37aab` |
| SHA-256 | `17CB43B043541C103586BB884A7D1BF1BA8E303D4CBB5D8B3457B8A6C6046084` |

새 receipt는 `artifacts/release-2.9.1-preparation-20261109/deployment-receipt.json`입니다. `Repository`는 실제 앱 저장소이며
Candidate/Previous는 위 신원과 같습니다. `.run`과 2.9.1 백업은 **아직 만들지 않았습니다**.
과거 receipt·`.run`·완료/미완료 백업은 보존합니다. 이번 준비에서 운영 dist/DB 접근이나 새 백업을 하지 않았습니다.

사용자가 외부에서 앱을 정상 종료한 뒤 배포 스크립트가 실제 DB 경로·WAL/SHM·프로세스 부재와 이전 신원을 다시 확인합니다.
백업은 `../backups/release-2.9.1.0-<KST>-<임의값>/`에 이전 EXE·dist ZIP·DB·로그·이전/후보 소스 ZIP을 해시와 함께 보존합니다.
정확한 경로·외부 결과는 사용자 실행 뒤 별도 기록합니다. 교체 후 검사 실패면 검증된 이전 EXE만 복원하고 **DB는 복원하지 않습니다**.
기본/LocalCache DB 둘 다 보존하고 병합/이관하지 않습니다. 운영 등록이 같은 dist 경로를 가리키면 재등록하지 않습니다.

## 사용자 외부 명령 두 개

이미 설치한 MSI PowerShell **7.6.9**의 전체 경로를 사용합니다. Store/MSIX 판은 `PackagedProcessRefused`로 거부합니다.
아래 명령은 `C:\Windows\System32`에서도 실행할 수 있습니다. **사이트 배포 완료 뒤** 다음 순서로 진행합니다.

### 1. 앱 실행 중에도 가능한 읽기 전용 Git 사전 확인

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.1-preparation-20261109/Read-DeploymentSourceCommits.ps1'
```

새 후보 커밋은 사용자 외부에서 아직 확인하지 않았으므로 생략하지 않습니다.
helper는 receipt와 배포 코드의 같은 경로 정규화로 만든 **backslash 저장소 경로**를 사용해
`git -C <저장소> -c safe.directory=<동일 저장소> cat-file -e <커밋>^{commit}`만 호출합니다.
소스 archive와 같은 prefix/경로 표기이며 archive·fetch·파일 출력·DB/등록/프로세스 호출은 없습니다.

정상: `Succeeded=true`, Previous/Candidate 각각 `GitExitCode=0`·`Readable=true`, `FilesChanged=false`, `AppLaunched=false`와 exit 0.
실패: `Succeeded=false`와 `SourceCommitUnreadable` 등 실패 코드 또는 GitExitCode 비정상, exit 1. 실패하면 배포하지 않습니다.
Codex에서는 통과했지만 이를 사용자 외부 접근 확인으로 대신하지 않습니다.

### 2. Git 확인 성공 → 트레이 ‘종료’ → 외부 배포 한 줄

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/deploy-windows.ps1' -ReceiptPath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.1-preparation-20261109/deployment-receipt.json'
```

정상 기대값: 실제 exit **0**, `Succeeded=true`, Receipt/Preflight/Backup/Replace/Inspect/Launch 모두 **Passed**,
`FileVersion=2.9.1.0`, SHA-256은 위 후보와 같음, `CandidateMatches=true`, `BackupHashesMatch=true`,
`NotificationTargetsUnchanged=true`, `AutoStartPathMatches=true`, `UnregisterDryRunExitCode=0`, `DryRunChangedNothing=true`,
`RegistrationPerformed=false`, `AppLaunchRequested=true`, `Rollback=NotNeeded`, `DatabaseRestored=false`입니다.
실패하면 그 요약을 확인하고 후보/receipt를 임의 수정하거나 후속 단계를 수동 실행하지 않습니다.

## 미확인·사용자 확인

PR 병합·사이트 배포·외부 Git 접근·운영 백업/교체·실제 등록/자동 시작 불변·새 앱 실행/상주는 아직 미확인입니다.
실제 복원·장기 운영·OS 알림 노출·Mac 재빌드는 이번 Windows 버전 준비에서 수행하지 않았습니다.
배포 뒤 사용자는 트레이·상태/한도 보기, 네 상태 카드의 별도 사용량 링크 제거와 공식 상태 클릭,
네 Provider 상세 문구·고정 폭·긴 본문 끝까지 스크롤, 아이폰 xAI 링크 제거·상태 상세·마지막 수신 갱신을 확인합니다.
실제 계정 값이나 캡처는 공개 기록에 넣지 않습니다.
