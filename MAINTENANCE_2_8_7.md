# AI Burger Clock 2.8.7 — Windows 시간표·상태 알림 토스트 배포 기록

기록일: **2026-10-29 KST**. #109 병합 뒤 최신 main을 `git pull --ff-only`로 받고
`codex/release-windows-2-8-7`에서 Windows 버전 세 필드와 문서만 갱신했습니다.
범위는 **Windows만 변경(버전·문서)**입니다. 공통·Windows C#·검사·Mac 파일·의존성은 다시 고치지 않습니다.

사용자는 **검증 후 같은 dist EXE 교체까지** 승인했습니다. 외부 정상 종료·실제 운영 데이터 백업과 후보 검증 뒤
**21:00:10 KST**에 같은 dist 경로의 EXE만 2.8.7로 교체했습니다. **Codex에서 운영 앱 실행·운영 등록·실제 해제·시험 알림을 하지 않았습니다.**
같은 경로의 기존 등록은 다시 만들지 않고, 외부 PowerShell에서 기존 대상 확인과 해제 dry-run만 수행하도록 안내합니다.
버전 PR 병합은 사용자 결정입니다. 외부 등록 확인·운영 실행과 실제 OS 알림 표시 확인은 아래에 각각 구분합니다.

## 2.8.6 이후 포함하는 Windows 변경

| 출처 | 2.8.7에 포함하는 내용 |
|---|---|
| #109 · 전송 | 시간표 FULL/BURGER 시작·공식 상태 문제/정상화·공휴일 설정 안내를 기존 한도 토스트 경로에 연결. 기존 AUMID·CLSID·바로가기 재사용 |
| #109 · 묶음·보관 | Group/Header `status`/`시간표·상태`, `quota`/`한도`. 시간표 2개·Provider별 문제/정상화 8개·공휴일 1개 고정 Tag, 한도는 개별 보관 |
| #109 · 그림 | 상태 계열만 고정 정보/경고 `appLogoOverride` 이미지. 트레이 상태 그림은 쓰지 않고 한도의 이미지 없는 구성 유지 |
| #109 · 순서·실패 | 문구·조건·큐·순서·6.5초 간격·공휴일 뒤 4.5초·배너 종료/30초 안전 상한 유지. 발송별 완료 대조. 등록/동기 제출 오류만 풍선 1회 대체, 사용자/정책 차단·비동기 실패는 기록만 |
| #109 · 진단 | 모든 종류를 새 `logs/notification-delivery-v2.jsonl`에 최근 256건/128KiB로 기록. 본문·계정 값 제외, 기존 v1은 읽기/쓰기/이관/삭제하지 않음 |
| #109 · 클릭·검사 | 본문/Header 모두 기존 무동작 COM 콜백. 가짜 발송기·합성 데이터로 검증하며 기본 검사에서 실제 OS 알림·등록 호출 없음 |

계정 조회 주기·회복/조기 초기화 판정·설정 기본값·DB schema·자동 시작·운영 등록은 유지합니다.
구현 계약과 #109 당시 검증은 [Windows 알림 안내](WINDOWS_QUOTA_NOTIFICATIONS.md)에 보존합니다.
2.8.7 버전 PR 자체의 검사 증감은 0이며 #109의 검사 추가와 구분합니다.

## Git 기준점과 최종 EXE

| 기준 | 커밋 |
|---|---|
| 이전 2.8.6 EXE 소스 | `526388cda04a0ae80291817a53b1da2c5b0fcb04` |
| #109 검증 HEAD | `5432c288d63f030e89026678e4a5d9c421e275ee` |
| #109 병합 / 이번 출발 main | `1693eae34fe72ca24dd3136d2015c01d70cbffb4` |
| Windows 버전 / 실제 EXE 소스 | **`464b00de4b7034196ec2445786e5e6c1974e6e80`** |
| 버전 PR 병합 | 미병합, 사용자 결정 |

실제 EXE는 #109 병합 main에 버전만 올린 **미병합 버전 커밋**의 산출물입니다. 이후 문서 커밋이나 향후 PR 병합 커밋과 구분합니다.
배포 경로는 `dist/win-x64/AI Burger Clock.exe`로 유지합니다. 후보 FileVersion/AssemblyVersion은 **2.8.7.0**,
ProductVersion은 **`2.8.7+464b00de4b7034196ec2445786e5e6c1974e6e80`**입니다.
최종 단일 EXE 크기는 **29,759,419 bytes**, SHA-256은 **`EB9AD42A65A3AA16BC34DB0A223334AEB4C7B30F5C53FF539DD605C1273C7BBD`**입니다.
이전 2.8.6 SHA-256은 **`7D0133995CEE7912DD1630E99CD3A2CC89C2CB075A14BEF3BDCC9F228B31C0F2`**입니다.

framework-dependent 단일 EXE이며 설치된 .NET 10 Desktop Runtime x64를 사용합니다.
검증 도구는 SDK **10.0.403**, Desktop Runtime **10.0.14**이며 런타임/SDK 설치·업데이트는 하지 않습니다.

## 검사 결과

| 항목 | 준비 bin | 최종 Portable 단일 EXE |
|---|---|---|
| 빌드/게시 | `build.ps1 -ReportGroup release-2-8-7`, 경고 0 / 오류 0 | 별도 candidate 경로 publish, 경고 0 / 오류 0 |
| self-test | **291,988 PASS**, 종료 코드 0 | **291,988 PASS**, 종료 코드 0 |
| 공통 / Windows 전용 | **291,077 / 911** | **291,077 / 911** |
| 고정 after 슬롯 smoke | **별도 1회 969 PASS**, 종료 코드 0 | **별도 1회 969 PASS**, 종료 코드 0 |
| #109 최종 대비 합성 PNG / layout | **44/44 · 38/38 동일** | **44/44 · 38/38 동일** |

2.8.6 대비 self-test **291,782→291,988(+206)**·smoke **951→969(+18)**은 #109의 Windows 알림 검사 추가입니다.
공통 검사 건수는 그대로이며 이번 버전 PR 자체의 검사 증감은 0입니다. PNG는 전체 RGBA 픽셀, layout은 파일 바이트로 대조했습니다.
각 smoke의 실제 OS 토스트 제출·풍선 표시는 0회이며 등록 API 호출도 0회입니다.
기존 symlink fixture 1개 SKIP은 유지합니다. 각 smoke 1회이므로 반복 트레이 기록 증가 검증은 **NotEvaluated**이고 두 별도 실행을 반복 성공으로 합산하지 않습니다. 관측 신규 키는 각 0개입니다.
실제 계정 조회·CLI 업데이트·공식 상태 실시간 재조회·운영 토스트 시험·Mac 빌드/검사는 하지 않습니다.

## 외부 백업 → 검사 → EXE 교체

사용자가 기존 2.8.6을 정상 종료하고 Codex 밖 PowerShell에서 백업 도우미를 실행했습니다.
**2026-10-29 20:55:20 KST의 `BackupSucceeded=True`**를 사용자 답변과 외부 receipt로 대조했습니다.
실제 기본 DB의 열린 경로·WAL/SHM 부재·운영 프로세스 부재와 복사 해시 일치는 외부 도우미가 확인했습니다.
백업은 **`../backups/release-2.8.7-20261029-205520-a9db8c9f/`**입니다. Codex가 운영 DB를 직접 열어 검증했다고 표현하지 않습니다.
완료 receipt가 있으므로 다음 백업 명령을 다시 실행하지 않습니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.7-deployment-20261029/Backup-OutsideCodex.ps1'
~~~

백업 정상 기대값: `BackupSucceeded=True`, `WalAndShmAbsent=True`, `ActualDataRootVerified=True`, `AppStarted=False`, `RegistrationChanged=False`.
최종 단일 EXE smoke 통과 뒤 교체 직전 이전/후보/백업 해시·운영/테스트 프로세스 부재·경로 경계/reparse 부재를 다시 확인했습니다.
**21:00:10 KST**에 `File.Replace`로 EXE만 교체하고 직전 파일을 `replaced-2.8.6.exe`로 보존했습니다.
후보 = 실제 dist = 보존한 `deployed-2.8.7.exe`의 버전/크기/해시가 같으며, `previous-2.8.6.exe`와 `replaced-2.8.6.exe`도 이전 해시와 같습니다.
같은 백업에 `dist-2.8.6.zip`, 정확한 이전/새 커밋의 `source-before-2.8.6.zip`·`source-2.8.7.zip`을 보존했습니다.
EXE 교체 후 검증 실패 시 이전 EXE만 복원하는 보호 경로를 뒀으며 이번에 복원하지 않았습니다.
기존 백업을 덮거나 삭제하지 않았으며 DB·로그·캐시는 복원하거나 이관·병합·삭제하지 않았습니다.
백업의 DB 해시·원문 로그·실제 계정 값은 공개하지 않습니다. 활성 WAL을 지우지 않습니다.

## 사용자 외부 확인·실행

EXE 교체 후 Codex 밖 64-bit PowerShell에서 기존 바로가기·AUMID·CLSID·COM·자동 시작 대상과
해제 **dry-run만** 확인합니다. 이 도우미는 재등록/실제 해제/시험 알림/정상 앱 실행을 하지 않습니다.

~~~powershell
$burgerRepo = 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock'
& "$burgerRepo/artifacts/release-2.8.7-deployment-20261029/Inspect-ProductionOutsideCodex.ps1"
if ($?) { Start-Process -FilePath "$burgerRepo/dist/win-x64/AI Burger Clock.exe" }
~~~

정상 기대값: `RegistrationSucceeded=True`, `RegistrationPerformed=False`, `AutoStartRunEntryPresent=True`, `AutoStartPathMatches=True`,
`FileVersion=2.8.7.0`, 기록한 SHA-256 일치, `DryRunExitCode=0`, `DryRunChangedNothing=True`, `TestToastSent=False`, `NormalAppLaunched=False`.
마지막 값은 도우미 자체가 정상 앱을 시작하지 않았다는 뜻이며 이후 사용자 외부 실행과 구분합니다.
등록이 없거나 대상이 다르면 도우미가 멈춥니다. 승인 없이 재등록하지 않습니다.

**외부 실제 대상 확인 완료(2026-10-29 21:04:06 KST):** 외부 receipt에서 `RegistrationSucceeded=True`, `RegistrationPerformed=False`,
`AutoStartRunEntryPresent=True`, `AutoStartPathMatches=True`, `DryRunExitCode=0`, `DryRunChangedNothing=True`를 대조했습니다.
실제 시작 메뉴 바로가기 대상/AUMID/CLSID와 HKCU의 AUMID/COM 대상이 같은 dist EXE를 가리키며 FileVersion **2.8.7.0**과 위 SHA-256도 일치합니다.
재등록·실제 해제·시험 알림은 하지 않았습니다. 도우미의 `NormalAppLaunched=False`와 이후 사용자의 외부 실행을 구분합니다.

**사용자 외부 실행·화면 확인 완료:** 사용자가 같은 외부 PowerShell에서 앱을 실행하고 트레이·상태 보기·한도 보기가 정상이라고 확인했습니다.
읽기 전용 프로세스 확인에서도 **21:04:07 KST 시작·같은 dist 경로·단일 2.8.7.0 프로세스·응답 정상**과 위 SHA-256 일치를 대조했습니다.
이번 추가 확인에서 운영 DB/로그의 내용이나 최신 갱신 시각은 읽지 않았습니다.
이 답변은 아래 실제 OS 알림의 앱 이름·두 묶음·200% 그림·보관·종료 후 본문/Header 클릭 확인을 뜻하지 않습니다. 해당 항목은 미확인으로 유지합니다.

외부 실행 후 기대값은 같은 dist 경로의 단일 2.8.7 프로세스, 트레이·상태 보기·한도 보기 정상입니다.
기본 운영 DB/로그는 `%LOCALAPPDATA%\AIBurgerClock`를 계속 쓰고 LocalCache DB는 백업으로 보존합니다.
일반 실행은 새 조회·캐시 쓰기·조건에 맞는 실제 알림을 일으킬 수 있으므로 읽기 전용 검사로 취급하지 않습니다.

## 배포 뒤 사용자 확인 항목 — 실제 OS 미확인

아래 항목은 self-test·smoke의 가짜 발송기 결과와 구분합니다. 이번 배포 작업에서 시험 알림을 보내거나 운영 알림 등록을 바꾸지 않습니다.

- 알림 센터에 표시되는 **앱 이름**.
- **`시간표·상태` / `한도` 두 묶음** 표시. 같은 AUMID의 OS 설정·보관 한도는 공유합니다.
- 정보·경고 그림의 **200% DPI 선명도**, 라이트/다크 배경에서 구분 가능 여부. 한도 그림 구성은 기존과 같아야 합니다.
- **배너가 닫힌 뒤**에도 센터에 보관되고, **앱 재시작 뒤**에도 남는지. 고정 Tag의 같은 종류는 최근 1개로 교체되며 Provider 문제/정상화는 별개입니다.
- **앱 종료 뒤 본문·Header 클릭 시 무동작**: 짧은 COM 확인 실행 후 종료하며 상태 창·트레이·조회·DB·자동 시작 처리를 새로 시작하지 않아야 합니다.
- 실제 Windows 사용자 끔·정책 차단을 풍선으로 우회하지 않는지.

실제 노출·보관·클릭의 사용자 답변이 없으면 통과로 기록하지 않습니다. 기존 2.8.2의 한도 알림 통합 검증을 새 상태/Header/그림의 실제 검증으로 확대하지 않습니다.

## 2.8.6 EXE만 복원하는 계획

문제가 생기면 사용자가 외부에서 새 앱을 정상 종료하고 위 백업의 `previous-2.8.6.exe`만 같은 dist 경로에 복사합니다.
FileVersion **2.8.6.0**, SHA-256 **`7D0133995CEE7912DD1630E99CD3A2CC89C2CB075A14BEF3BDCC9F228B31C0F2`**를 대조합니다.
DB·로그·캐시·설정·운영 등록은 과거 백업으로 자동 복원하지 않습니다. 같은 경로의 등록을 다시 만들지 않습니다.
v1/v2 진단 파일은 둘 다 보존하며 병합하지 않습니다. 2.8.6은 기존 v1을 계속 쓰고 시간표·상태·공휴일은 풍선 방식으로 돌아갑니다.
복원 뒤 앱 실행도 사용자 외부 수행입니다. 이번에 실제 복원 시험은 하지 않습니다.

## 근거 위치와 미수행

- 준비 build: `artifacts/release-2-8-7/build/20261029-205514-405/`
- 준비 smoke: `artifacts/release-2-8-7/windows-tests/after/20261029-205557-865-d41951/`
- 최종 단일 EXE smoke: `artifacts/release-2-8-7-final/windows-tests/after/20261029-205845-862-ec4c9a/`
- RGBA/레이아웃 대조: `artifacts/release-2.8.7-deployment-20261029/preparation-comparison.json`·`final-comparison.json`
- 후보·publish/self-test 로그·외부 도우미·백업 receipt·로컬 manifest: `artifacts/release-2.8.7-deployment-20261029/`

이번 버전 검사는 기본 SystemAware 설정의 native 렌더입니다. 실제 OS 토스트의 200% 표시나 물리 100% 모니터 조작 검증으로 확대하지 않습니다.
다중 DPI 모니터 이동·재부팅·절전 복귀·장시간 운영·실제 복원·실제 OS 알림 노출/센터 보관·종료 후 클릭은 미수행입니다.
