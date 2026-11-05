# AI Burger Clock 2.8.9 — Windows xAI 카탈로그 수정 배포 준비

기록일: **2026-11-05 KST**. #115 병합 main을 받은 뒤 Windows 버전 세 필드와 문서만 올립니다.
공통·Windows `.cs`와 Mac 파일은 수정하지 않습니다. `XaiStatusFeed.cs` 주석의 `BugBot`도 그대로 둡니다.
**후보 EXE 준비만 수행하며 실제 dist EXE 교체·운영 실행·기존 등록 확인은 사용자가 Codex 밖에서 직접 합니다.**
이번 준비에서 운영 등록·해제·시험 알림·실제 계정 조회·DB/캐시/설정 변경은 하지 않습니다.
개인 비공개 사이트 주소·토큰·실제 한도 값은 이 문서와 공개 PR에 적지 않습니다.

## 2.8.8 이후 포함하는 Windows 변경

| 출처 | 2.8.9에 포함하는 내용 |
|---|---|
| #115 · JSON 카탈로그 | 필수 Review Agents ID를 Third-party models ID로 교체하고 Cursor로 분류. 필수 ID가 빠지면 CHECK 유지 |
| #115 · RSS | Third-party models를 Cursor로 추가하고 Review Agents 경로는 유지. Bugbot 표시 이름 적용 |
| #115 · Imagine | 신뢰할 수 있는 공식 경로/ID의 Imagine은 작업 판정과 Seen·진행·해결 집계에서 제외. 같은 ID의 작업 제품 장애/소실/24시간 만료·재시작 복원은 유지 |
| 이번 문서 보완 | 형식이 깨진 Imagine 항목도 RSS 필수 필드/상태 형식 CHECK를 만들지 않으며 신뢰하지 않는 링크는 CHECK임을 명시 |

조회 주기·알림 문구/조건/대기열·모바일 한도 송신 계약·DB schema·화면 배치는 유지합니다.
상세 판정 계약은 [XAI_STATUS](XAI_STATUS.md)에 있습니다.

## Git 기준점과 후보 EXE

| 기준 | 커밋 |
|---|---|
| 기존 2.8.8 EXE 소스 | `73e70a1c6d0fe1f29cd594acc4a0b0a75496d2bd` |
| #115 검증 HEAD | `593f916d8620490afb75e0c01afb6dec0d6f05aa` |
| #115 병합 / 이번 출발 main | `1257baac698cc1ff0e9c76c23826738e9ff98e9a` |
| Windows 버전 / 후보 EXE 소스 | `1ca68a2d7dd37b31a86da58ed8701c0def0a188c` |
| 이번 버전 PR 병합 | 미병합, 사용자 결정 |

후보: `artifacts/release-2.8.9-preparation-20261105/candidate/AI Burger Clock.exe`.
FileVersion/AssemblyVersion **2.8.9.0**, framework-dependent 단일 EXE이며 .NET 10 Desktop Runtime x64가 필요합니다.
ProductVersion **`2.8.9+1ca68a2d7dd37b31a86da58ed8701c0def0a188c`**, 크기 **29,931,451 bytes**,
SHA-256 **`D400468B1163A7C0738D42EE75FFD2AC90C22DFC42EB9044A833B3F99252B399`**입니다.
후속 기록 커밋과 이 실제 EXE 소스 커밋은 구분합니다.
기존 dist는 **2.8.8.0**, SHA-256
`3EEC46E3CAFDA6C6E10CE4388C1C3DC7A0277D0AFA49B6FA4EC03E5FB88D75D0`로 보존합니다.

## 검사 결과

| 항목 | 결과 |
|---|---|
| `build.ps1 -ReportGroup release-2-8-9` | 경고 0·오류 0 |
| bin / Portable 단일 EXE self-test | 각각 **292,215 PASS**, 실제 ExitCode 0 |
| 공통 / Windows 전용 | **291,104 / 1,111**. xAI 그룹 416건이며 버전 PR의 검사 증감은 0 |
| 별도 candidate 경로 Portable publish | 경고 0·오류 0·종료 코드 0. dist를 쓰지 않음 |
| 후보 최종 고정 after 슬롯 smoke | **990 PASS**, 실제 ExitCode 0, 합성 PNG 45개. 별도 1회이므로 트레이 증가 억제는 NotEvaluated |
| 공개 `--check-providers` | 실제 ExitCode 0. OpenAI·Claude·Gemini·xAI 모두 Operational. xAI는 네 출처 응답 수신·공식 사건 피드 기준 정상·판정 문제 없음 |

SDK **10.0.403**, Desktop Runtime **10.0.14 x64**입니다. 설치/업데이트는 하지 않았습니다.
후보 최종 PNG **45/45**·레이아웃 JSON **38/38**은 기존 2.8.8의 고정 before 슬롯 결과와 SHA-256 바이트가 같습니다.

기존 합성 symlink fixture 1개 SKIP은 유지합니다. 로그는 `artifacts/release-2-8-9/`와
`artifacts/release-2.8.9-preparation-20261105/`, 최종 smoke는 `artifacts/release-2-8-9-candidate-final/`에 둡니다.

**초기 smoke 실패도 보존:** 첫 후보 검사에서 `Windows 자동 실행 · 다른 경로` 컨트롤의
`Control clipped` 오류로 19 PASS 뒤 종료 코드 1이었습니다. 기존 2.8.8을 고정 before 슬롯에서 비교하면
990 PASS·종료 코드 0이었고, **후보/검사 코드/대기 시간/Windows 설정을 바꾸지 않은 단독 재검사**는
990 PASS·종료 코드 0으로 통과했습니다. 최초 실패 원인은 미확정이며 확인한 실패를 성공으로 합산하지 않습니다.
공통/Windows `.cs`를 고치거나 레이아웃 검사를 건너뛰지 않았습니다. 운영 화면 검증과 초기 실패의 원인 조사는 구분합니다.

버전/문서만 바꾸므로 #115 이후 검사 기대값은 늘리지 않습니다. 합성 검사와 실제 공개 상태 조회를 구분합니다.
실제 한도/CLI 계정 조회·사이트 전송·운영 알림/등록·Mac 빌드/검사는 이번 준비에서 하지 않습니다.

## 사용자 외부 백업·교체·확인

아래 도우미는 준비 후 제공하며 **Codex에서 실행하지 않습니다.** 사용자만 Codex 밖 PowerShell에서 순서대로 실행합니다.

1. 기존 2.8.8을 트레이 → 종료로 정상 종료합니다.
2. `artifacts/release-2.8.9-preparation-20261105/Backup-OutsideCodex.ps1`을 실행합니다. 실제 기본 DB 경로와 프로세스 부재·WAL/SHM 부재, 이전 EXE·DB·로그 백업 및 해시 일치를 확인합니다. `BackupSucceeded=True`여야 하며 WAL을 임의로 삭제하지 않습니다.
3. `Replace-VerifiedExe.ps1`을 실행합니다. 외부 백업 receipt와 2.8.8/후보 버전·해시·실행 부재를 다시 확인한 뒤 같은 dist 경로의 EXE만 교체합니다. 앱 실행·등록·DB 변경은 하지 않습니다. 실패하면 2.8.8 EXE만 복원합니다.
4. `Inspect-ProductionOutsideCodex.ps1`로 기존 바로가기·AUMID·CLSID·COM·자동 시작 대상과 해제 dry-run 무변경을 확인합니다. 경로가 같고 등록이 정상이면 재등록하지 않습니다.
5. 위 단계가 모두 정상일 때만 기존 dist EXE를 외부에서 실행하고 트레이·상태 보기·한도 보기, xAI 카드와 공식 페이지 링크를 확인합니다.

외부 백업의 예상 새 위치는 `../backups/release-2.8.9-<KST 시각>-<고유값>/`입니다.
실제 백업 경로·교체·운영 실행은 **미실행**이며 사용자 결과가 오기 전 완료로 기록하지 않습니다.
기본 `%LOCALAPPDATA%/AIBurgerClock`과 LocalCache의 두 DB는 보존하고 병합·이관·과거 백업 복원을 하지 않습니다.

정상 기대값: 후보/교체 EXE 버전 **2.8.9.0**, 기록 SHA-256 일치, `RegistrationSucceeded=True`,
`RegistrationPerformed=False`, `AutoStartPathMatches=True`, `DryRunExitCode=0`, `DryRunChangedNothing=True`.
기존 모바일 사이트 연동 설정은 유지하며 토큰 교체나 실제 테스트 전송은 이번 절차에 포함하지 않습니다.

### 외부 PowerShell 명령

현재 앱을 정상 종료한 뒤 **한 단계씩** 실행합니다. 오류가 있으면 다음 단계로 넘어가지 않습니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.9-preparation-20261105/Backup-OutsideCodex.ps1'
~~~

`BackupSucceeded=True`와 실제 백업 경로를 확인한 뒤 교체합니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.9-preparation-20261105/Replace-VerifiedExe.ps1'
~~~

`ExeReplaced=True`, `FileVersion=2.8.9.0`, 위 SHA-256 일치를 확인한 뒤 기존 운영 등록을 검사합니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.9-preparation-20261105/Inspect-ProductionOutsideCodex.ps1'
~~~

위 등록/자동 시작/dry-run 기대값이 모두 정상이면 사용자가 운영 앱을 실행합니다.

~~~powershell
Start-Process -FilePath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/dist/win-x64/AI Burger Clock.exe'
~~~

도우미 3개는 기존 2.8.8 절차의 버전·소스·해시 pin만 2.8.9에 맞춰 준비했으며 PowerShell 구문 오류 0입니다.
Codex에서는 실행하지 않았고 사용자 백업/교체/등록 확인 결과도 아직 받지 않았습니다.

## 2.8.8으로 되돌리기

문제가 있으면 앱을 정상 종료하고 외부 백업의 `previous-2.8.8.exe` 해시가 위 기존 해시와 같은지 확인한 뒤
같은 dist 경로의 EXE만 복원합니다. 버전 2.8.8.0·해시와 기존 등록 경로를 확인한 뒤 사용자가 외부에서 실행합니다.
DB·캐시·송신 설정·알림 등록은 자동 복원하거나 삭제하지 않습니다.

## 모바일 사이트

#115 병합 확인 뒤 사이트 **버전 11**, 소스 **`654c295cb3b7a963b104a1525cce6c64fee593ba`**를
개인 소유자만 접근하는 기존 범위 그대로 배포했습니다. 화면/API 기준 커밋은 **`1257baa`**입니다.
새/옛 구성 요소 목록, Third-party models·Review Agents RSS, Imagine 진행/해결·깨진 필드·신뢰하지 않는 링크,
동일 ID의 작업 제품과 24시간 소실/복원 경계를 포함해 실제 C# 원본과 **상태 161단계 중 xAI 118단계 전부 일치**했습니다.
시간표·권고·신선도까지 네 그룹 **20,192건 일치**, 사이트 Node **74/74**, typecheck·Worker build·ESM validation 통과입니다.
기준 C# 원본 사본 5개는 병합 main 파일과 해시가 같고 합성 기준 자료는 저장소 밖에만 보관합니다.
**배포 후 실제 브라우저 확인(2026-11-05 21:08 KST):** xAI 카드가 **GO**, **Official: 정상**,
`정상 · 진행 중 장애 없음(공식 사건 피드 기준)`으로 표시되고 하단 `기준 커밋 1257baa`, Refresh 활성 상태를 확인했습니다.
카드/하단만 읽었으며 실제 한도 값·사이트 주소·토큰·화면 캡처는 기록하지 않았습니다. 아이폰 실기 재확인은 미실행입니다.
Windows 버전 후보의 소스·검사·해시와 사이트 소스·검증은 구분합니다.
