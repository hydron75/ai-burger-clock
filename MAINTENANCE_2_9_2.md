# AI Burger Clock 2.9.2 — Review Agents 필수 목록 보정 후보

2026-11-14 KST. main `ba23cb21acc227988a78f9123a24fda430efb6ec`에서 준비합니다.
이번 버전 PR은 Windows `Version`·`AssemblyVersion`·`FileVersion`과 문서만 변경합니다.
공통/Windows C#·Mac 파일·패키지·배포 스크립트는 바꾸지 않습니다.

## 2.9.1 이후 포함 변경

- **#145:** xAI 호환 JSON 필수 11개 중 `bugbot` 대신 `2x2chyqwmkzl`(Review Agents, Cursor)을 요구합니다. RSS의 `bugbot`·`review-agents` 대응과 필수 항목 누락 CHECK를 유지하며 Agent SDK는 추가하지 않습니다. 출처·조회·한도·알림 조건은 같습니다. [목록·변경 이력](XAI_STATUS.md).
- 세 호환 JSON의 sunset 2027-01-31과 대체 경로/전환 방향을 12월 중순까지 정할 후속 항목을 BACKLOG에 유지합니다. 이번 버전에서 API 전환은 하지 않습니다.

## 사이트 선행 배포

- #145 병합 main을 기준으로 준비본을 **사이트 버전 14**로 비공개 배포했습니다. 소스 커밋은 `f31712e68434a3c6e4c2706a06ad23ed14670d78`, 화면/API 기준 커밋은 **`ba23cb2`**입니다. 기존 소유자 전용 범위를 유지했습니다.
- 최신 main의 실제 공통 C# 원본에서 생성한 합성 상태 **143사례·164단계**가 일치했습니다. 전체 사이트 합성 검사 **85/85**, 타입 검사·로컬 빌드·ESM 산출물 검사가 통과했습니다. 기존 시간표 19,972·권고 12·신선도/실패 47건 회귀도 통과했습니다.
- **2026-11-14 11:16:55 KST**, 배포된 HTML과 상태 API는 각각 HTTP 200이며 기준 커밋이 맞았습니다. 실제 Worker의 xAI reading을 사이트와 같은 판정기로 계산한 결과 **Operational / 누락 사유 없음**입니다. 아이폰의 기존 localStorage 상태까지 확인한 결과는 아닙니다.
- `/api/quota`와 실제 계정 값은 읽지 않았습니다. 한도 API·DB·수신 계약·화면은 바꾸지 않았으며 사이트 주소·토큰·실제 한도 값은 기록하지 않습니다.

## 후보와 검증

| 항목 | 결과 |
|---|---|
| 후보 소스 커밋 | `98320e7fb598a7b0fb32c659b44bfa65efce7ccb` (검증 결과 문서 커밋과 구분) |
| SDK | **10.0.404** — 저장소에서 `dotnet --version`으로 확인 |
| `build.ps1` | 경고 **0**·오류 **0**, self-test **293,038건**·종료 코드 **0** |
| Portable 단일 EXE publish | 성공·종료 코드 **0**, 경고/오류 없음 |
| 후보 단일 EXE `--self-test` | **293,038건**·종료 코드 **0** (공통 291,909 + Windows 1,129, 기존 symlink fixture 1건 SKIP) |
| 후보 smoke | 고정 after 슬롯 **1회**, **1,174 PASS / 종료 코드 0**, PNG **60개** |
| 렌더 비교 | #145 최종 검증과 PNG **60/60**, layout JSON **41/41** SHA-256 동일 |
| 실제 OS 알림·운영 등록 호출 | smoke의 가짜 발송 경로 검사 통과, 실제 제출/등록 명령 **0회** |

이번 smoke는 첫 실행에서 실제 `WM_MOUSEWHEEL` 검사를 포함해 통과했습니다. 재실행은 하지 않았습니다.
#145의 첫 실행에서 발생했던 휠 실패가 두 번째로 재현되지 않았으므로 이번에는 원인 조사 BACKLOG 항목을 추가하지 않습니다.
이 결과를 앞선 간헐 실패의 원인 확인이나 결함 수정으로 해석하지 않습니다.
트레이 기록은 전체 18개·after 슬롯 1개·새 키 없음이며 **1회뿐이므로 반복 누적 억제는 미평가**입니다.

검증 로그:

- `artifacts/release-2.9.2-20261114/build/20261114-111908-244/`
- `artifacts/release-2.9.2-20261114/windows-tests/after/20261114-112113-546-d8e1ab/`
- `artifacts/release-2.9.2-preparation-20261114/` (publish·후보 self-test·SDK·렌더 비교·receipt·Git 사전 확인)

## 후보 신원·이전 신원과 receipt

| 새 후보 | 값 |
|---|---|
| 경로 | `artifacts/release-2.9.2-preparation-20261114/candidate/AI Burger Clock.exe` |
| FileVersion | **`2.9.2.0`** |
| ProductVersion | `2.9.2+98320e7fb598a7b0fb32c659b44bfa65efce7ccb` |
| 크기 | **30,054,331 bytes** |
| SHA-256 | **`E6AD2CC165BEF4A748E950152731C042A07B5FC8800E82D6C0D83D89ECF445CC`** |

공식 생성기 `tools/New-WindowsDeploymentReceipt.ps1`를 사용했습니다.
`-PreviousIdentityPath`는 `artifacts/release-2.9.1-preparation-20261109/deployment-receipt.json`이며
공식 생성기가 그 receipt의 **Candidate**를 이전 신원으로 읽었습니다. 이전 값을 추정하거나 기존 파일을 고치지 않았습니다.

| 이전 2.9.1 | 값 |
|---|---|
| FileVersion | `2.9.1.0` |
| ProductVersion | `2.9.1+be23038be09a43d76de610e321523bcea3a7f550` |
| SourceCommit | `be23038be09a43d76de610e321523bcea3a7f550` |
| SHA-256 | `1A90FD80631F6E2D0F541B73CD1A2AC956291D98510596CEFC24831625E85986` |

새 receipt는 `artifacts/release-2.9.2-preparation-20261114/deployment-receipt.json`입니다.
그 Candidate 신원은 같은 폴더의 `candidate.json`에도 그대로 보존했습니다.
새 `.run`이나 운영 백업은 아직 만들지 않았으며 receipt를 배포 실행으로 소비하지 않았습니다.
과거 receipt·`.run`·완료/미완료 백업·기본/LocalCache DB는 그대로 보존합니다.

## 사용자 외부 명령

버전 PR 병합 뒤 **Git 사전 확인 → 앱 정상 종료 → 배포 명령** 순서입니다.
설치된 MSI PowerShell의 전체 경로를 사용하며 `C:\Windows\System32`에서 실행해도 됩니다.
Store/MSIX PowerShell은 기존 `PackagedProcessRefused` 검사를 유지합니다. 운영 앱 실행·등록은 Codex에서 하지 않습니다.

### 1. 읽기 전용 Git 사전 확인

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.2-preparation-20261114/Read-DeploymentSourceCommits.ps1'
```

receipt의 두 커밋을 배포 스크립트와 같은 경로 정규화/backslash 표기로
`git -C <저장소> -c safe.directory=<동일 저장소> cat-file -e <커밋>^{commit}`으로 읽습니다.
archive·fetch·파일 출력·DB·등록·앱 실행은 없습니다. 이번 후보 커밋은 외부에서 처음 확인하므로 생략하지 않습니다.
Codex 컨텍스트의 `C:\Windows\System32`에서 두 커밋 모두 exit 0/Readable=true를 확인했지만 사용자 외부 접근을 대신하지 않습니다.

정상: `Succeeded=true`, 두 `GitExitCode=0`, 두 `Readable=true`, `FilesChanged=false`, `AppLaunched=false`, 실제 exit 0.
실패하면 배포하지 않고 출력된 실패 코드만 확인합니다.

### 2. Git 확인 성공 → 트레이 ‘종료’ → 외부 배포 한 줄

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/deploy-windows.ps1' -ReceiptPath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.2-preparation-20261114/deployment-receipt.json'
```

정상 기대값: 실제 exit **0**, `Succeeded=true`, Receipt/Preflight/Backup/Replace/Inspect/Launch 모두 **Passed**,
`FileVersion=2.9.2.0`, 위 후보 SHA-256과 일치, `BackupHashesMatch=true`, `CandidateMatches=true`,
`NotificationTargetsUnchanged=true`, `AutoStartPathMatches=true`, `UnregisterDryRunExitCode=0`, `DryRunChangedNothing=true`,
`RegistrationPerformed=false`, `AppLaunchRequested=true`, `Rollback=NotNeeded`, `DatabaseRestored=false`입니다.

스크립트는 외부에서 실제 프로세스 부재·기본 DB 경로·WAL/SHM·이전 신원을 확인한 뒤
`../backups/release-2.9.2.0-<KST>-<임의값>/`에 이전 EXE·dist ZIP·DB·로그·두 소스 archive와 해시를 보존합니다.
백업 검증 → 후보 확인/교체 → 기존 등록·자동 시작 불변/해제 dry-run → 앱 실행을 순서대로 수행합니다.
같은 경로의 운영 알림 등록을 다시 만들지 않으며 시험 알림도 보내지 않습니다.
교체 뒤 검사 실패면 검증한 **이전 EXE만** 복원하고 **DB는 복원하지 않습니다**.
실패하면 receipt나 `.run`을 지우거나 같은 receipt를 다시 실행하지 말고 결과를 확인합니다.

## 미확인·사용자 확인

버전 PR 병합·사용자 외부 Git 접근·운영 백업/교체·실제 등록/자동 시작 불변·2.9.2 실행/상주·실제 화면은 아직 미확인입니다.
기존 2.9.1의 smoke 후 외부 재실행은 사용자에게 요청했으며 실행 결과는 별도 확인합니다.
배포 뒤 트레이·상태 보기·한도 보기·xAI 카드와 Provider 상세를 확인합니다. 아이폰의 재조회/기존 로컬 기록에서 정상 복귀는 사용자 확인 항목입니다.
실제 복원·장기 운영·OS 알림 노출·Mac 재빌드는 수행하지 않았습니다.
실제 계정 값·토큰·사이트 주소·캡처는 기록하지 않습니다.
