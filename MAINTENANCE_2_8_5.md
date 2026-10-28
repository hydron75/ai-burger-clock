# AI Burger Clock 2.8.5 — 한도 영역 높이 보완 배포 기록

기록일: **2026-10-28 KST**. #103 병합 뒤 최신 main을 `git pull --ff-only`로 받고
`codex/release-windows-2-8-5`에서 Windows 버전 세 필드와 문서만 갱신했습니다.
범위는 **Windows만 변경(버전·문서)**입니다. 공통·Windows C#·검사·Mac 파일·의존성은 다시 고치지 않습니다.

사용자는 **검증 후 같은 dist EXE 교체까지** 승인했습니다. 외부 정상 종료·실제 운영 데이터 백업과 후보 검증 뒤
**22:04:13 KST**에 같은 dist 경로의 EXE만 2.8.5로 교체했습니다. **Codex에서 운영 앱 실행·운영 등록·실제 해제·시험 알림을 하지 않았습니다.**
이후 사용자가 외부에서 기존 등록 대상/해제 dry-run을 확인하고 앱을 실행했습니다. 트레이·상태 보기와 한도 화면의 빈 띠·스크롤·하단 컨트롤 정상은 **사용자 확인 완료**입니다. PR 병합은 사용자 결정입니다.

## 2.8.4 이후 포함하는 Windows 변경

| 출처 | 2.8.5에 포함하는 내용 |
|---|---|
| #103 · 큰 작업 영역 | 한도 보기의 스크롤 영역이 안내 문구 바로 위까지 남는 높이를 사용. 안내 앞 빈 띠 146 DIP → 6 DIP |
| #103 · 작은 작업 영역 | 같은 창 크기 안에서 한도 영역을 줄이고 내부 세로 스크롤과 하단 안내·버튼·체크박스 접근 유지. 헤더/하단이 함께 들어갈 수 없는 극단적으로 짧은 영역은 기존 전체 스크롤 유지 |
| #103 · 상태 모드 보존 | 상태 보기와 한도→상태 복귀의 위치·픽셀, 창 크기·카드 크기·간격·문구 유지. 조회·알림·설정·DB schema 변경 없음 |
| #103 · 검사 | 세 Provider·Gemini 모델 주간 행 포함 8개 막대 합성 fixture. 96/192 DPI 큰/작은 영역·6 DIP 이하 여백·마지막 행까지 스크롤·하단 접근·복귀 검사 |
| #102 · 기존 작업 규칙 | 기획·우선순위·범위·동작 기준은 메인 채팅, 작업 채팅은 전달된 지시대로 구현·검증·기록. 이 버전에서 기능을 추가한 것으로 세지 않음 |

[#103 기능 검증·합성 전후 PNG](docs/reviews/windows-quota-viewport-20261028/README.md)는 당시 기록으로 유지합니다.
이번 PR은 위 기능/검사를 재구현하지 않습니다. 계정 한도 세 Provider·1시간 정책·한도 알림·기본값·운영 등록·자동 시작·schema 2를 유지합니다.

## Git 기준점과 최종 EXE

| 기준 | 커밋 |
|---|---|
| 이전 2.8.4 EXE 소스 | `5055831bff9f5dd37be295cde4f97b9205a9da86` |
| #103 병합 / 이번 출발 main | `2aeb1a982257bb89b5470711b01d383db6854e1e` |
| Windows 버전 / 실제 EXE 소스 | **`d1656738c0a7f4665354429abc65f3317c9f95fc`** |
| 버전 PR 병합 | 미병합, 사용자 결정 |

| EXE 항목 | 결과 |
|---|---|
| 동일 배포 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion / AssemblyVersion | **2.8.5.0 / 2.8.5.0** |
| ProductVersion | **`2.8.5+d1656738c0a7f4665354429abc65f3317c9f95fc`** |
| 크기 | **29,722,555 bytes** |
| SHA-256 | **`48CF62E88B7AA3B115EB56916D314A57CBE48416D9AFA49CCF16067B38BB844D`** |
| 이전 2.8.4 SHA-256 | `1A708ACBCC231F7D12DBC659F001EEC189E20F6667AE5D149D6C4D189147AF3C` |

이 EXE는 #103 병합 main에 버전만 올린 **미병합 버전 커밋**의 산출물입니다. 버전 PR 병합 main에서 만든 것으로 표현하지 않습니다.
framework-dependent 단일 EXE로 설치된 .NET 10 Desktop Runtime x64를 사용합니다. SDK 10.0.403, Desktop Runtime 10.0.14입니다.
런타임·SDK 설치/업데이트는 하지 않았습니다.

## 실제 검사

| 항목 | 준비 bin | 최종 Portable 단일 EXE |
|---|---|---|
| 빌드/게시 | `build.ps1 -ReportGroup release-2-8-5`, 경고 0 / 오류 0 | 별도 candidate 경로 publish, 경고 0 / 오류 0 |
| self-test | **291,779 PASS**, 종료 코드 0 | **291,779 PASS**, 종료 코드 0 |
| 공통 / Windows 전용 | **291,074 / 705** | **291,074 / 705** |
| 고정 after 슬롯 smoke | **별도 1회 927 PASS**, 종료 코드 0 | **별도 1회 927 PASS**, 종료 코드 0 |
| #103 최종 대비 합성 PNG / layout | **34/34 · 28/28 동일** | **34/34 · 28/28 동일** |
| 반복 트레이 기록 증가 검증 | **NotEvaluated**, 새 키 관측 0개 | **NotEvaluated**, 새 키 관측 0개 |

기존 symlink fixture 1개 SKIP은 유지합니다. 검사 수는 main/#103과 동일하며 버전 PR 자체의 증감은 0입니다.
2.8.4 대비 self-test는 동일하고 smoke **897→927(+30)**은 #103에서 한도 여백·작업 영역·DPI/복귀 검사를 추가한 결과입니다.
각 smoke는 `Repeat=1`이므로 두 별도 실행을 반복 검증 성공으로 합산하지 않습니다. 실제 OS 알림 전송은 0건입니다.
이번 native 렌더는 실제 `DeviceDpi=192`의 기본 SystemAware 배포 설정이며 96 DPI 새 검증으로 확대하지 않습니다.

실제 계정 조회·CLI 업데이트·공식 상태 실시간 재조회·운영 토스트 시험·등록을 추가 실행하지 않습니다.
Mac 전용 코드·빌드·검사도 이번 범위에서 실행하지 않습니다.

## 외부 백업 → 검사 → EXE 교체

1. 사용자가 기존 2.8.4를 정상 종료한 뒤 Codex 밖 64-bit PowerShell에서 아래 백업 도우미를 실행했습니다.
   **22:00:31 KST의 `BackupSucceeded=True`**를 사용자 답변과 외부 receipt로 대조했습니다. 실제 기본 DB 경로·WAL/SHM·프로세스 부재를 확인했고
   이전 EXE·백업 EXE와 복사 DB의 해시 일치를 확인했습니다. Codex가 실제 운영 DB를 직접 열어 확인했다고 표현하지 않습니다.
2. 새 백업은 **`../backups/release-2.8.5-20261028-220031-fbfca05e/`**입니다. 이전 EXE·dist ZIP·종료 DB·로그와
   정확한 이전/새 소스 커밋의 ZIP을 보존했습니다. 기존 백업을 덮거나 삭제하지 않았습니다.
3. candidate를 dist와 분리해 게시·검사하고 최종 smoke가 실제 종료 코드 0으로 끝난 뒤, 교체 직전 앱/검사 프로세스 부재와 이전/후보 해시를 다시 확인했습니다.
   **22:04:13 KST**에 `File.Replace`로 EXE만 교체했습니다. 교체 직전 파일은 `replaced-2.8.4.exe`로 보존했습니다.
4. **후보 = 실제 dist = 백업한 새 EXE**의 버전·크기·SHA-256 일치를 확인했습니다. 실패 시 이전 EXE만 복원하는 보호 경로를 뒀으며 이번에 복원하지 않았습니다.
5. 운영 앱·COM 활성화·운영 등록/실제 해제·해제 dry-run을 Codex에서 실행하지 않았습니다. 자동 시작과 운영 등록도 변경하지 않았습니다.

사용자가 외부에서 실행한 백업 명령입니다. 완료한 백업을 다시 실행하거나 덮어쓰기 위한 명령이 아닙니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.5-deployment-20261028/Backup-OutsideCodex.ps1'
~~~

결과: `BackupSucceeded=True`, `WalAndShmAbsent=True`, `ActualDataRootVerified=True`, `AppStarted=False`, `RegistrationChanged=False`.
실제 기본 `%LOCALAPPDATA%\AIBurgerClock\burgerclock.db`의 열린 파일 경로와 WAL/SHM 부재를 확인하고 이전 EXE·DB·로그를 새 백업 폴더에 복사했습니다.
활성 WAL을 삭제하지 않습니다. LocalCache DB는 보존하며 이관·병합·삭제하지 않습니다. DB 해시·원문 로그·실제 계정 값은 공개하지 않습니다.

백업의 `previous-2.8.4.exe`·`replaced-2.8.4.exe`·`deployed-2.8.5.exe`, `dist-2.8.4.zip`·
`source-before-2.8.4.zip`·`source-2.8.5.zip`, `data/`와 로컬 manifest는 비공개입니다.

## 사용자 외부 확인·실행

사용자가 다음 확인 도우미를 **Codex 밖**에서 실행했습니다. 기존 바로가기·AUMID·CLSID·COM·자동 시작 대상과
해제 **dry-run만** 검사하며 등록/실제 해제/시험 알림/정상 앱 실행을 하지 않습니다. 경로가 같고 정상이면 재등록하지 않습니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.5-deployment-20261028/Inspect-ProductionOutsideCodex.ps1'
~~~

**외부 실제 확인 완료(2026-10-28 22:59:56 KST):** 로컬 receipt에서 `RegistrationSucceeded=True`, `RegistrationPerformed=False`,
`AutoStartRunEntryPresent=True`, `AutoStartPathMatches=True`, `DryRunExitCode=0`, `DryRunChangedNothing=True`를 대조했습니다.
기존 바로가기 대상/AUMID/CLSID와 실제 HKCU의 AUMID/COM 대상이 같은 dist EXE를 가리키며, FileVersion 2.8.5.0·SHA-256도 위 기록과 일치합니다.
재등록·실제 해제·시험 알림은 하지 않았습니다. 도우미의 `NormalAppLaunched=False`는 도우미 자체가 일반 앱을 시작하지 않았다는 뜻이며 이후 사용자 외부 실행과 구분합니다.

확인에 성공한 뒤 사용자가 같은 외부 PowerShell에서 운영 앱을 시작했습니다. 이 명령은 새 조회·캐시 쓰기·설정에 따른 실제 알림을 발생시킬 수 있습니다.
Codex에서 대신 실행하지 않습니다.

~~~powershell
Start-Process -FilePath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/dist/win-x64/AI Burger Clock.exe'
~~~

외부 실행 후 기대값은 단일 2.8.5 프로세스, 트레이/상태 창 정상, 큰 작업 영역에서 한도 안내 앞 여백 6 DIP,
작은 영역의 한도 내부 스크롤·하단 접근, 기본 운영 DB/로그의 갱신입니다. 상태 보기·카드 크기/간격은 전과 같아야 합니다.
**사용자 외부 실행·화면 확인 완료:** 사용자가 위 외부 명령으로 실행하고 트레이·상태 보기와 한도 화면의 빈 띠·스크롤·하단 컨트롤이 정상이라고 확인했습니다.
읽기 전용 프로세스 확인에서도 같은 dist 경로의 단일 2.8.5.0 프로세스 응답과 기록된 SHA-256 일치를 대조했습니다. 사용자 화면 확인을 픽셀 수치 재측정이나 모든 창 크기 검증으로 확대하지 않습니다.
실제 계정 값/캡처는 수집·게시하지 않았으며, 기본 운영 DB/로그의 최신 갱신 시각은 이번 추가 확인에서 읽지 않았습니다.

## 2.8.4 EXE만 복원하는 계획

문제가 생기면 사용자가 외부에서 새 앱을 정상 종료하고 새 백업의 `previous-2.8.4.exe`만 같은 dist 경로로 복사합니다.
FileVersion **2.8.4.0**, SHA-256 **`1A708ACBCC231F7D12DBC659F001EEC189E20F6667AE5D149D6C4D189147AF3C`**를 대조합니다.
DB·로그·캐시·설정·운영 등록은 과거 백업으로 자동 복원하지 않습니다. 같은 경로의 등록을 다시 만들지 않습니다.
복원 뒤 실행도 사용자 외부 수행입니다. 2.8.4에는 한도 영역의 빈 띠 문제가 남으며 이번에 실제 복원 시험은 하지 않았습니다.

## 근거 위치와 미수행

- 준비 build: `artifacts/release-2-8-5/build/20261028-213401-886/`
- 준비 smoke: `artifacts/release-2-8-5/windows-tests/after/20261028-220105-317-018d1f/`
- 최종 단일 EXE smoke: `artifacts/release-2-8-5-final/windows-tests/after/20261028-220213-928-05c8d0/`
- 후보·publish/self-test 로그·외부 도우미·로컬 manifest: `artifacts/release-2.8.5-deployment-20261028/`

이번 버전 PR에서 물리 100% 모니터 조작·다중 DPI 모니터 이동·재부팅·절전 복귀·장시간 운영·실제 복원은 미수행입니다.
#103의 96/192 DPI 검증은 별도 테스트 프로세스의 실제 DeviceDpi/DrawToBitmap과 합성 작업 영역 결과이며, Windows 설정을 바꾼 육안 검사가 아닙니다.
