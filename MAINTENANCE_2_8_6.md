# AI Burger Clock 2.8.6 — 숨긴 한도 창 재열기·정상화 본문 배포 기록

기록일: **2026-10-29 KST**. #105·#106 병합 뒤 최신 main을 `git pull --ff-only`로 받고
`codex/release-windows-2-8-6`에서 Windows 버전 세 필드와 문서만 갱신했습니다.
범위는 **Windows만 변경(버전·문서)**입니다. 공통·Windows C#·검사·Mac 파일·의존성은 다시 고치지 않습니다.

사용자는 **검증 후 같은 dist EXE 교체까지** 승인했습니다. 외부 정상 종료·실제 운영 데이터 백업과 후보 검증 뒤
**00:26:16 KST**에 같은 dist 경로의 EXE만 2.8.6으로 교체했습니다. **Codex에서 운영 앱 실행·운영 등록·실제 해제·시험 알림을 하지 않았습니다.**
사용자가 외부에서 기존 등록·자동 시작 대상/해제 dry-run을 확인하고 앱을 실행했습니다. 트레이·상태 보기와 한도 보기의 숨김/재열기·스크롤·하단 컨트롤 정상은 **사용자 확인 완료**입니다.
경로가 같은 정상 등록을 다시 만들지 않았습니다. PR 병합은 사용자 결정입니다.

## 2.8.5 이후 포함하는 Windows 변경

| 출처 | 2.8.6에 포함하는 내용 |
|---|---|
| #105 · Windows 한도 창 | 유효 가시성과 독립적인 `quotaMode`로 한도 모드를 보관. 숨긴 창을 `ShowNearTray()`로 다시 열기 전에 작은/큰 작업 영역의 한도 높이·하단 위치를 맞춤 |
| #105 · 보존·검사 | 창·카드 크기, 문구, 상태 보기 레이아웃 유지. 한도→숨김→같은/다른 작은·큰 영역 재열기 및 상태→숨김→재열기 회귀 검사 |
| #106 · 공통 정상화 본문 | ChatGPT·Claude·Gemini 공식 상태 정상화 본문에 기존 표시 이름 추가. 예: `ChatGPT: 관련 서비스가 정상화되었습니다. 현재 FULL THROTTLE이므로 대규모 작업 재개 가능.` |
| #106 · 보존·검사 | xAI 문구·제목·장애 본문·알림 조건/간격 유지. 기본 이름/host 표시 이름과 xAI 예외 합성 기대값 확인 |

계정 조회 주기·한도 회복/조기 초기화 알림·설정 기본값·DB schema·자동 시작·운영 알림 등록은 유지합니다.
이 버전 PR에서 위 기능/검사를 다시 수정하지 않습니다. #105의 96/192 DPI 렌더·수정 전 실패 재현은 당시 PR 기록으로 구분합니다.

## Git 기준점과 최종 EXE

| 기준 | 커밋 |
|---|---|
| 이전 2.8.5 EXE 소스 | `d1656738c0a7f4665354429abc65f3317c9f95fc` |
| #105 병합 | `990bd43b7e68fa852b4c3e56d09d7b59271f36de` |
| #106 병합 / 이번 출발 main | `7c165aa3c537358f43629503bd8746ebc80ef411` |
| Windows 버전 / 실제 EXE 소스 | **`526388cda04a0ae80291817a53b1da2c5b0fcb04`** |
| 버전 PR 병합 | 미병합, 사용자 결정 |

| EXE 항목 | 결과 |
|---|---|
| 동일 배포 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion / AssemblyVersion | **2.8.6.0 / 2.8.6.0** |
| ProductVersion | **`2.8.6+526388cda04a0ae80291817a53b1da2c5b0fcb04`** |
| 크기 | **29,726,651 bytes** |
| SHA-256 | **`7D0133995CEE7912DD1630E99CD3A2CC89C2CB075A14BEF3BDCC9F228B31C0F2`** |
| 이전 2.8.5 SHA-256 | `48CF62E88B7AA3B115EB56916D314A57CBE48416D9AFA49CCF16067B38BB844D` |

이 EXE는 #105·#106 병합 main에 버전만 올린 **미병합 버전 커밋**의 산출물입니다. 향후 버전 PR 병합 커밋과 구분합니다.
framework-dependent 단일 EXE이며 설치된 .NET 10 Desktop Runtime x64를 사용합니다. SDK **10.0.403**, Desktop Runtime **10.0.14**입니다.
런타임·SDK 설치/업데이트는 하지 않았습니다.

## 실제 검사

| 항목 | 준비 bin | 최종 Portable 단일 EXE |
|---|---|---|
| 빌드/게시 | `build.ps1 -ReportGroup release-2-8-6`, 경고 0 / 오류 0 | 별도 candidate 경로 publish, 경고 0 / 오류 0 |
| self-test | **291,782 PASS**, 종료 코드 0 | **291,782 PASS**, 종료 코드 0 |
| 공통 / Windows 전용 | **291,077 / 705** | **291,077 / 705** |
| 고정 after 슬롯 smoke | **별도 1회 951 PASS**, 종료 코드 0 | **별도 1회 951 PASS**, 종료 코드 0 |
| #105 최종 대비 합성 PNG / layout | **44/44 · 38/38 동일** | **44/44 · 38/38 동일** |

기존 symlink fixture 1개 SKIP은 유지합니다. 2.8.5 대비 self-test **291,779→291,782(+3)**은 #106의 공통 표시 이름 검사 증가입니다.
smoke의 **927→951(+24)**과 PNG **34→44**는 #105의 숨김 재열기 검사입니다. 버전 PR 자체의 검사 증감은 0입니다.
각 최종 smoke는 `Repeat=1`이므로 반복 트레이 기록 증가 검증은 **NotEvaluated**이며 두 별도 실행을 반복 검증 성공으로 합산하지 않습니다. 관측된 신규 키는 각각 0입니다. 실제 OS 알림은 가짜 sink로 차단해 전송 0건입니다.
실제 계정 조회·CLI 업데이트·공식 상태 실시간 재조회·운영 토스트 시험·Mac 빌드/검사는 실행하지 않습니다.

## 외부 백업 → 검사 → EXE 교체

사용자가 기존 2.8.5를 정상 종료하고 Codex 밖 PowerShell에서 백업 도우미를 실행했습니다.
**2026-10-29 00:20:21 KST의 `BackupSucceeded=True`**를 사용자 답변과 외부 receipt로 대조했습니다.
실제 기본 DB의 열린 경로·WAL/SHM 부재·운영 프로세스 부재와 복사 해시 일치를 외부 도우미에서 확인했습니다.
새 백업은 **`../backups/release-2.8.6-20261029-002021-59649dc2/`**입니다. Codex가 운영 DB를 직접 열어 확인했다고 표현하지 않습니다.
최종 단일 EXE smoke 통과 뒤 교체 직전 이전/후보/백업 해시·프로세스 부재·경로 경계/reparse 부재를 다시 확인했습니다.
**00:26:16 KST**에 `File.Replace`로 EXE만 교체하고 직전 파일을 `replaced-2.8.5.exe`로 보존했습니다.
**후보 = 실제 dist = 백업한 새 EXE**의 버전·크기·SHA-256 일치를 확인했습니다. 실패 시 이전 EXE만 복원하는 보호 경로를 뒀으며 이번에 복원하지 않았습니다.
백업은 이전 EXE·dist ZIP·종료 DB·로그와 정확한 이전/새 소스 ZIP을 보존하며 기존 백업을 덮거나 삭제하지 않습니다.
DB 해시·원문 로그·실제 계정 값은 공개하지 않습니다. 활성 WAL을 삭제하지 않으며 기본/LocalCache DB를 이관·병합·삭제하지 않습니다.

백업의 `previous-2.8.5.exe`·`replaced-2.8.5.exe`·`deployed-2.8.6.exe`, `dist-2.8.5.zip`·
`source-before-2.8.5.zip`·`source-2.8.6.zip`, `data/`와 로컬 manifest는 비공개입니다.

사용자가 외부에서 실행하는 백업 명령입니다. 완료 receipt가 있으면 다시 실행하지 않습니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.6-deployment-20261029/Backup-OutsideCodex.ps1'
~~~

정상 기대값: `BackupSucceeded=True`, `WalAndShmAbsent=True`, `ActualDataRootVerified=True`, `AppStarted=False`, `RegistrationChanged=False`.

## 사용자 외부 확인·실행

EXE 교체 후 Codex 밖 64-bit PowerShell에서 기존 바로가기·AUMID·CLSID·COM·자동 시작 대상과
해제 **dry-run만** 확인합니다. 이 도우미는 등록/실제 해제/시험 알림/정상 앱 실행을 하지 않습니다.

~~~powershell
$burgerRepo = 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock'
& "$burgerRepo/artifacts/release-2.8.6-deployment-20261029/Inspect-ProductionOutsideCodex.ps1"
if ($?) { Start-Process -FilePath "$burgerRepo/dist/win-x64/AI Burger Clock.exe" }
~~~

정상 기대값: `RegistrationSucceeded=True`, `RegistrationPerformed=False`, `AutoStartRunEntryPresent=True`, `AutoStartPathMatches=True`,
`FileVersion=2.8.6.0`, 위 SHA-256 일치, `DryRunExitCode=0`, `DryRunChangedNothing=True`, `TestToastSent=False`, `NormalAppLaunched=False`.
마지막 값은 도우미 자체가 정상 앱을 시작하지 않았다는 뜻이며 이후 사용자 외부 실행과 구분합니다.

**외부 실제 확인 완료(2026-10-29 00:27:14 KST):** 로컬 receipt에서 `RegistrationSucceeded=True`, `RegistrationPerformed=False`,
`AutoStartRunEntryPresent=True`, `AutoStartPathMatches=True`, `DryRunExitCode=0`, `DryRunChangedNothing=True`를 대조했습니다.
기존 바로가기 대상/AUMID/CLSID와 실제 HKCU의 AUMID/COM 대상이 같은 dist EXE를 가리키며 FileVersion 2.8.6.0·SHA-256도 위 기록과 일치합니다.
재등록·실제 해제·시험 알림은 하지 않았습니다. 이 외부 검사 뒤 사용자가 같은 외부 PowerShell에서 앱을 시작했습니다.

외부 실행 후 기대값은 같은 dist 경로의 단일 2.8.6 프로세스, 트레이·상태 보기·한도 보기 정상입니다.
한도 보기로 숨긴 뒤 다시 열어도 모드가 유지되고 안내 앞 여백은 6 DIP 이하여야 합니다. 작은 작업 영역에서 내부 스크롤·하단 컨트롤에 접근합니다.
실제 정상화 알림을 시험 발송하거나 설정을 바꿀 필요는 없습니다. 기본 운영 DB/로그의 최신 갱신·사용자 화면 확인은 별도 근거로 구분합니다.

**사용자 외부 실행·화면 확인 완료:** 사용자가 위 외부 명령으로 실행하고 트레이·상태 보기와 한도 보기의 숨김/재열기·스크롤·하단 컨트롤이 정상이라고 확인했습니다.
읽기 전용 프로세스 확인에서도 같은 dist 경로의 단일 2.8.6.0 프로세스 응답과 기록한 해시 일치를 대조했습니다.
사용자 화면 확인을 여백의 픽셀 수치 재측정·모든 작업 영역/DPI 검증으로 확대하지 않습니다. 실제 계정 값/캡처는 수집·게시하지 않았고,
기본 운영 DB/로그의 최신 갱신 시각은 이번 추가 확인에서 읽지 않았습니다.

## 2.8.5 EXE만 복원하는 계획

문제가 생기면 사용자가 외부에서 새 앱을 정상 종료하고 새 백업의 `previous-2.8.5.exe`만 같은 dist 경로로 복사합니다.
FileVersion **2.8.5.0**, SHA-256 **`48CF62E88B7AA3B115EB56916D314A57CBE48416D9AFA49CCF16067B38BB844D`**를 대조합니다.
DB·로그·캐시·설정·운영 등록은 과거 백업으로 자동 복원하지 않습니다. 같은 경로의 등록을 다시 만들지 않습니다.
복원 뒤 실행도 사용자 외부 수행입니다. 2.8.5에는 숨긴 한도 창 재열기 문제가 남으며 이번에 실제 복원 시험은 하지 않습니다.

## 근거 위치와 미수행

- 준비 build: `artifacts/release-2-8-6/build/20261029-001821-801/`
- 준비 smoke: `artifacts/release-2-8-6/windows-tests/after/20261029-002130-921-c85c3e/`
- 최종 단일 EXE smoke: `artifacts/release-2-8-6-final/windows-tests/after/20261029-002233-319-9a2c94/`
- RGBA/레이아웃 비교: `artifacts/release-2.8.6-deployment-20261029/comparison.json`
- 후보·publish/self-test 로그·외부 도우미·로컬 manifest: `artifacts/release-2.8.6-deployment-20261029/`

이번 버전 검사는 기본 SystemAware 설정의 native 렌더입니다. #105의 96 DPI 검증과 물리 100% 모니터 사용자 조작을 같은 것으로 표현하지 않습니다.
다중 DPI 모니터 이동·재부팅·절전 복귀·장시간 운영·실제 복원·실제 OS 알림 노출/센터 보관은 이번 범위에서 미수행입니다.
