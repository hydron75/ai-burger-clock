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
| Windows 버전 / 후보 EXE 소스 | 후보 생성 뒤 기록 |
| 이번 버전 PR 병합 | 미병합, 사용자 결정 |

후보: `artifacts/release-2.8.9-preparation-20261105/candidate/AI Burger Clock.exe`.
FileVersion/AssemblyVersion **2.8.9.0**, framework-dependent 단일 EXE이며 .NET 10 Desktop Runtime x64가 필요합니다.
후보 크기·ProductVersion·SHA-256은 검증 뒤 기록합니다.
기존 dist는 **2.8.8.0**, SHA-256
`3EEC46E3CAFDA6C6E10CE4388C1C3DC7A0277D0AFA49B6FA4EC03E5FB88D75D0`로 보존합니다.

## 검사 결과

build·bin self-test·Portable 후보 self-test·고정 after 슬롯 smoke·공개 `--check-providers` 결과를 완료 뒤 기록합니다.
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

## 2.8.8으로 되돌리기

문제가 있으면 앱을 정상 종료하고 외부 백업의 `previous-2.8.8.exe` 해시가 위 기존 해시와 같은지 확인한 뒤
같은 dist 경로의 EXE만 복원합니다. 버전 2.8.8.0·해시와 기존 등록 경로를 확인한 뒤 사용자가 외부에서 실행합니다.
DB·캐시·송신 설정·알림 등록은 자동 복원하거나 삭제하지 않습니다.

## 모바일 사이트

#115와 같은 규칙의 준비본을 병합 확인 뒤 별도로 배포합니다. 화면 기준 커밋은 #115 병합 커밋으로 표시합니다.
C# 기준 비교·사이트 검사·배포 결과와 실제 브라우저의 xAI 카드 상태는 확인 뒤 계정 값/주소 없이 기록합니다.
Windows 버전 후보의 소스·검사·해시와 사이트 소스·검증은 구분합니다.
