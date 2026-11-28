# Windows 2.11.0 배포 기록

## 변경 범위

- #191/#192의 [Antigravity 웹 한도 조회원](QUOTA_SOURCES_DESIGN.md#47-antigravity-웹-조회원-p25)을 배포했습니다. 트레이의 `Gemini 사용량` 메뉴에서 별도로 켜며 기본은 꺼짐입니다. 켜면 Gemini 박스 안에 Gemini 앱과 Antigravity 두 부분이 표시됩니다. Antigravity 5시간·주간 행은 추정 리셋을 ‘약’으로 표시합니다.
- Antigravity 값과 조회 이벤트는 기존 네 Provider의 모바일 송신 및 알림 대상에서 제외합니다. #191의 P0 합성 송신 9개는 Windows 2.10.0과 바이트 단위로 일치했습니다.
- 공통 신규 파일은 `AntigravityUsagePage.cs`와 `AntigravityUsageTests.cs`, Windows 신규 파일은 `WindowsAntigravitySession.cs`입니다. `AccountQuotaMonitor.cs`·`QuotaSources.cs`·`QuotaPanelModel.cs`와 Windows 세션·설정·화면 코드가 새 조회원을 연결합니다. #193에서는 `AiBurgerClock.csproj`의 버전 필드 세 개를 올렸습니다. 이 기록 PR은 새 문서와 `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`를 갱신합니다.

## P2 선행 관찰 결정

설계 6.2는 P2 운영 관찰 3일을 기준으로 잡았습니다. 이번에는 사용자가 P2의 운영 약 **43시간** 뒤 P2.5를 배포하기로 결정했습니다. Windows 2.11.0은 그 결정에 따라 2026-11-28 KST에 외부 배포했습니다.

## 빌드와 검사

| 검사 | Windows 2.10.0 | Windows 2.11.0 |
|---|---:|---:|
| .NET SDK | `10.0.404` | `10.0.404` |
| Windows 빌드·publish | 경고 0 · 오류 0 | 경고 0 · 오류 0 |
| 공통 검사 | 291,817건 | 291,869건 (+52) |
| 단일 EXE self-test | 293,087건 | 293,164건 (+77) |
| 고정 after 슬롯 합성 smoke | 1,214건 · PNG 75장 | 1회 1,219건 · PNG 76장 |

#191 최종 HEAD에서 Windows 빌드·self-test와 고정 after 합성 smoke 1회가 통과했습니다. 병합된 최신 main에서 다시 `build.ps1 -Publish`를 실행했고, 단일 EXE self-test와 고정 after 합성 smoke 1회 모두 종료 코드 0이었습니다. 기존 Windows symlink fixture 1건은 파일시스템 제약으로 건너뛰었습니다. 후보·슬롯·receipt 해시는 일치했고, 이전·후보 소스 커밋은 배포와 같은 Git 경로 지정으로 읽었습니다.

후보 검사 로그는 격리 작업트리 `review-worktrees/windows-2.11.0-release`의 `artifacts/release-2.11.0/build/20261128-131617-797/`과 `artifacts/release-2.11.0/windows-tests/after/20261128-131744-699-2067a3/`에 보관합니다. 트레이 기록의 반복 실행 시 증가는 단일 실행으로 평가할 수 없는 항목입니다.

## Git 기준점과 백업

| 항목 | 값 |
|---|---|
| P2.5 작업 전 main | `65a8c9438f3d7b776a0fcc9672da90f083259815` |
| #192 병합 및 #191 최종 검증 HEAD | `d4d5dc87f31b9aedbc546ca93689c905d7c91e5e` |
| #191 병합 | `1c38a966754bad85059a03216ae9112818374c43` |
| 버전 PR #193 HEAD | `085fd8584b2b670c9f144159a84253ab2ab422cb` |
| #193 병합 | `f2c5e7e460e1471ef27e4c5e96cfb21d54b81e51` |
| 후보 및 최종 EXE 소스(#194 포함 main) | `40687b23e2efd7442f8348067117ce5b8675a44d` |
| 이전 2.10.0 EXE 소스 | `666611b960b1c60d40b1de3afc06b20d77c079e7` |
| 운영 EXE | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion | `2.11.0.0` |
| ProductVersion | `2.11.0+40687b23e2efd7442f8348067117ce5b8675a44d` |
| 크기 | `31,425,935` bytes |
| SHA-256 | `F815E75DDE7750DCC80D74FFA6FFFE6B23413A64F6909A3C2111E9BF373BB5AD` |
| Receipt | `artifacts/release-2.11.0-preparation-20261128/deployment-receipt.json` |

배포 후 운영 EXE를 읽기 전용으로 확인해 FileVersion과 SHA-256이 후보와 일치했습니다. 외부 실행에서 생성한 로컬 백업 `../backups/release-2.11.0.0-20261128-132525-1a1068e0/`에는 양쪽 EXE·소스 ZIP, 이전 dist ZIP, DB·로그 복사본 및 해시 manifest가 있습니다.

| 로컬 백업 파일 | SHA-256 |
|---|---|
| `previous.exe` | `5602939E9EE0C31DE025E502FF47CD2B298DE88C5CEFFACA1C78B1AEB7DA90CB` |
| `previous-dist.zip` | `ED2CC20D93A9B5D20C3AC5F123C94C99D13E35BEF3E99E8A587CD51B678D8651` |
| `source-before.zip` | `4BE3ADA4E91F7EE1E8B83C130843AB57E33B94E8ED601D94D66F90326B98F5E5` |
| `source-candidate.zip` | `8FEF1E258E04E8FC8CEC868DB3BC9FB19D4E818738ABCA4C056BF96A858567B1` |
| `deployed.exe` | `F815E75DDE7750DCC80D74FFA6FFFE6B23413A64F6909A3C2111E9BF373BB5AD` |

복원이 필요하면 Codex 밖에서 운영 앱을 정상 종료한 뒤 `previous.exe`의 버전·해시를 확인하고 같은 경로의 운영 EXE를 교체합니다. 교체 뒤 `2.10.0.0`과 이전 해시를 대조합니다. DB·캐시·등록은 보존합니다. [외부 배포와 복원 절차](WINDOWS_DEPLOYMENT.md)

## 외부 배포와 Antigravity 첫 조회

- **2026-11-28 KST 사용자 확인.** Codex 밖 PowerShell로 실행한 외부 배포가 정상 완료됐습니다. 로컬 실행 전 검증 기록에서 Receipt부터 Inspect까지 다섯 단계 통과를 확인했습니다. 여기에는 기본 `%LOCALAPPDATA%\AIBurgerClock` 데이터 경로와 WAL/SHM, 백업 해시, 알림 등록·자동 시작 대상 불변, 해제 dry-run 검사가 포함됩니다.
- **첫 켜기 사용자 확인.** 기존 Gemini 세션으로 트레이에서 `Antigravity 사용량 조회`를 켜니 **1분 안에** Gemini 박스가 Gemini 앱·Antigravity 두 부분으로 나뉘었고, Antigravity 두 행에 **‘약’**이 표시됐습니다. 작업 표시줄과 Alt+Tab에 새 창은 보이지 않았습니다.
- **새 프로필 로그인 금지.** 별도 시험 프로필을 쓰는 사전 확인에서 Google 로그인 시도가 제한됐습니다. 실제 페이지 확인은 기존 로그인 세션을 이용한 배포 후 첫 켜기로 대체했습니다.
- 세션 잠금 시 조회 미루기, 1시간 자동 갱신, 재부팅 뒤 설정 유지, 실제 OS 알림 표시는 이번 사용자 확인 범위에서 미확인입니다.
