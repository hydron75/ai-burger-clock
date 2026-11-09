# AI Burger Clock 2.9.0 — Grok 주간 크레딧·외부 배포 준비

2026-11-09 KST. #122/#123을 병합한 main `526e6c91c59219cf38f083333c62f0c993349342`에서 준비합니다.
이 버전 PR은 **Windows 버전 세 필드와 문서만** 바꿉니다. 공통/Windows C#·Mac 파일·패키지·배포 스크립트는 고치지 않습니다.
후보는 별도 artifacts 경로에 만들고 검사하며 운영 dist·DB·등록·자동 시작은 변경하지 않습니다.
병합·외부 백업·EXE 교체·운영 실행은 아직 수행하지 않았습니다.

## 2.8.9 이후 포함 변경

- #122 공통·Windows와 종속 #123 Mac 전환의 Grok 주간 크레딧입니다. 공식 CLI ACP의 고정 initialize → cached_token headless authenticate → billing만 사용하고 모델 호출·재로그인은 하지 않습니다.
- xAI 한도 박스/주간 크레딧 한 행, 0~100 사용률·정확한 10,080분 주간 검증, 45초 제한, 실패 시 이전 값을 유지합니다. 예약만 최소 10분이며 시작·Refresh·복귀·네트워크 즉시 조회는 기존대로입니다.
- 기존 회복·주간 조기 초기화·시간 비례 표시를 적용합니다. 상태 카드의 사용량 링크는 유지합니다. 모바일 송신은 wire v2, 조회/판정 진단은 v2, Windows 발송은 v3 새 파일을 사용하고 구버전 파일은 보존합니다. [조회·표시·개인정보·진단 계약](GROK_QUOTAS.md).
- Microsoft.Data.Sqlite **10.0.12**와 기본 SQLitePCLRaw **2.1.13**은 유지합니다. 10.0.14는 다음 버전에 세 프로젝트를 함께 갱신하며 SQLite 엔진 갱신은 별도 보류합니다([BACKLOG](BACKLOG.md)).

## 사이트 선행 배포

- 개인 소유자만 접근하는 기존 범위를 유지해 사이트 버전 **12**, 소스 `b4c4f6bb598cc55361f438466bab766b7d3d44d3`를 먼저 배포했습니다. 화면/API 기준 커밋은 **526e6c9**입니다.
- v1 세 Provider와 v2 네 Provider를 동시에 받습니다. v1에는 xAI 행·빈 시각·대기 문구를 추가하지 않고 기존 링크 박스를 유지합니다. v2 수신 뒤에만 xAI 주간 크레딧과 기존 링크를 함께 표시합니다.
- 합성 검사 **83/83**, typecheck·Worker build·ESM validation PASS, C# 표시·시간 비교 **240/240 일치**입니다. 병합 `526e6c9`의 소스 트리는 최종 검증 `e42c0f2`와 같습니다.
- **09:22 KST 배포 완료 뒤 약 3분 안에**, 기존 2.8.9의 새 v1 송신이 같은 source의 증가한 seq로 저장됨을 읽기 전용으로 확인했습니다. 강제 Refresh·테스트 전송을 실행하지 않았습니다.
- 배포된 앱 영역·스타일과 클라이언트 파일이 검증한 산출물과 같고 기준 커밋이 표시되는지 확인했습니다. 실제 한도 값·원문 스냅샷·사이트 주소·토큰은 기록하지 않습니다. 아이폰 실기와 실제 v2 전송은 앱 배포 뒤 사용자 확인입니다.

## 후보·검사

후보: `artifacts/release-2.9.0-preparation-20261109/candidate/AI Burger Clock.exe`.
FileVersion/AssemblyVersion **2.9.0.0**, framework-dependent 단일 EXE이며 .NET 10 Desktop Runtime x64가 필요합니다.
운영 `dist/win-x64/AI Burger Clock.exe`는 기존 **2.8.9.0** 그대로입니다.

| 항목 | 준비 결과 |
|---|---|
| Git 기능 기준 | `526e6c91c59219cf38f083333c62f0c993349342` (#122 main 병합) |
| 후보의 소스 커밋·ProductVersion·크기·SHA-256 | 후보 빌드 뒤 기록 |
| build·bin self-test | 검사 뒤 기록 |
| Portable 후보 publish·self-test | 검사 뒤 기록 |
| 고정 after 슬롯 native smoke | 검사 뒤 기록 |
| receipt | 후보 검사 뒤 공식 생성기로 생성 |

검사에는 가짜 CLI/HTTP/알림 발송기·임시 DB만 사용합니다. 실제 계정 조회·사이트 전송·OS 알림·운영 등록은 하지 않습니다.
공통 코드·검사 기대값은 #122 병합 뒤 바꾸지 않습니다. 기존 symlink fixture 1건의 파일시스템 권한 제한은 별도로 기록합니다.

## 이전 신원과 외부 백업·복원

`New-WindowsDeploymentReceipt.ps1`의 `-PreviousIdentityPath`는 기존
`artifacts/release-2.8.9-preparation-20261105/candidate.json`입니다. 신원을 추정해 새로 만들지 않습니다.

| 2.8.9 이전 신원 | 값 |
|---|---|
| FileVersion | `2.8.9.0` |
| ProductVersion | `2.8.9+1ca68a2d7dd37b31a86da58ed8701c0def0a188c` |
| SourceCommit | `1ca68a2d7dd37b31a86da58ed8701c0def0a188c` |
| SHA-256 | `D400468B1163A7C0738D42EE75FFD2AC90C22DFC42EB9044A833B3F99252B399` |

새 백업은 **아직 생성하지 않았습니다**. 사용자 외부 실행에서 실제 기본 DB 경로·프로세스 부재·WAL/SHM 부재·이전 신원을 확인하고
`../backups/release-2.9.0.0-<KST>-<임의값>/`에 이전 EXE·dist ZIP·DB·로그·두 소스 ZIP을 해시와 함께 보관합니다.
정확한 새 백업 경로는 외부 결과에서 받아 이 기록에 덧붙입니다. 기본/LocalCache DB는 둘 다 보존하며 병합·이관하지 않습니다.
교체 뒤 검사 실패 시 검증한 이전 EXE만 복원하고 DB·캐시·로그·등록은 복원하지 않습니다.
[단계별 검사·실패 코드·복원 계약](WINDOWS_DEPLOYMENT.md).

## 사용자 외부 실행 — PR 병합 뒤 한 줄

MSI PowerShell **7.6.9**가 이미 설치·확인된 전체 경로를 사용합니다. Store/MSIX 판은 `PackagedProcessRefused`로 거부합니다.
사용자는 PR 병합 뒤 기존 앱을 트레이 → 종료로 정상 종료한 다음 **Codex 밖 PowerShell**에서 실행합니다.
receipt 파일을 임의 편집하거나 후보·해시를 직접 입력하지 않습니다. 실패하면 다음 단계를 수동으로 진행하지 말고 요약 결과를 확인합니다.

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/deploy-windows.ps1' -ReceiptPath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.0-preparation-20261109/deployment-receipt.json'
```

이 한 줄이 receipt → 사전 확인 → 백업 → 교체 → 기존 등록·자동 시작 불변/해제 dry-run → 실행 요청을 처리합니다.
정상 기대값은 실제 종료 코드 **0**, 모든 단계 **Passed**, 후보 **2.9.0.0/기록 해시 일치**, `AppLaunchRequested=true`, 복원 미수행입니다.
운영 등록이 기존 배포 경로를 가리키면 재등록하지 않습니다. 시험 알림·토큰 변경·테스트 송신은 없습니다.
실행 요청 성공과 실제 상주/화면은 구분합니다. 트레이·상태 보기·한도 보기(xAI 주간 크레딧·현재/이전 구분·스크롤),
아이폰의 xAI 한도와 마지막 수신 시각 갱신은 사용자가 확인합니다. 실제 값이나 캡처는 공개 기록에 넣지 않습니다.

## 아직 하지 않은 것

사용자 외부 배포와 실제 백업·복원·운영 앱의 새 Grok 조회·모바일 v2 수신·아이폰 실기는 미확인입니다.
배포 스크립트의 `ReceiptNotFound` 추가 등 동작 변경은 이번에 하지 않습니다. 패키지·Mac 버전·운영 등록도 변경하지 않습니다.
