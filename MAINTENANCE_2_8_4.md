# AI Burger Clock 2.8.4 — xAI 공식 상태 카드 배포 기록

기록일: **2026-10-27 KST**. #98 병합 뒤 최신 main을 `git pull --ff-only`로 받고
`codex/release-windows-2-8-4`에서 Windows 버전 세 필드와 문서만 갱신했습니다.
범위는 **Windows만 변경(버전·문서)**입니다. 공통·Windows C#·검사·Mac 파일·의존성은 다시 고치지 않습니다.

사용자는 **검증 후 배포 EXE 교체까지** 승인했습니다. 외부 정상 종료·실제 운영 데이터 백업 뒤 검증한 단일 EXE를
**22:01:29 KST**에 같은 dist 경로로 교체했습니다. **운영 앱 실행·운영 등록·해제·시험 알림은 하지 않았습니다.**
외부 등록 대상 확인/해제 dry-run과 앱 실행·화면 확인은 사용자 수행 대기입니다. 버전 PR 병합도 사용자가 합니다.

## 2.8.3 이후 포함하는 Windows 변경

| 출처 | 2.8.4에 포함하는 내용 |
|---|---|
| #98 · 출처·판정 | xAI 카드 하나에 Grok·Cursor·Grok Bot을 묶고 확인된 관련 장애 중 최악 상태를 표시. Grok은 RSS, Cursor/Bot은 RSS와 호환 summary/components/incidents JSON 병행 |
| #98 · 정상·불확실 | 대상 진행 사건이 없으면 공식 사건 피드 기준 정상. 수신/형식/범위 미확인은 CHECK, 확인된 장애는 우선. 제품명은 상세와 공식 상태 알림에 표시 |
| #98 · 사건 소실 | 첫 유효 소실 확인부터 24시간 CHECK, 계속 미등장이면 해제. ID별 소실 UTC와 정확한 해제 안내를 v2 메타데이터로 저장해 재시작 뒤에도 이어짐. 명시 해결은 즉시 처리하고 시간 만료는 공식 해결 확인 알림으로 보내지 않음 |
| #98 · Windows 화면 | 네 번째 상태 카드와 `사용량 보기` 링크(`https://grok.com/dashboard`). 한도 값은 조회·표시하지 않음. 기본 client 크기 374×569→374×658 DIP, 작은 작업 영역에서는 스크롤. 기록·통계도 xAI 포함 |
| #98 · 보호·검사 | DTD/외부 엔터티 금지·4 MiB 응답 제한·제품 경로/필드 검사, 부분 실패 증거 보존·전체 실패/재시작 복원, 합성 parser/저장/알림/화면 회귀 검사 |
| #95 · 기존 배포 규칙 | 실제 기본 AppData와 Codex LocalCache DB를 분리 보존하며 운영 실행·등록은 사용자가 Codex 밖에서 수행. 이 규칙을 이번 교체에도 적용 |

#96의 xAI 카드/한도 보류 결정은 #98에 반영됐습니다. #97 등 Mac 전용 버전·빌드 변경을 Windows 신규 기능으로 세지 않습니다.
계정 한도 세 Provider·1시간 정책·한도 알림·기본값·자동 시작 설정과 DB schema 2는 유지합니다.
기존 `XaiPendingIncidents.v1` ID 목록과 새 `v2`(최대 32 KiB)의 공존은 #98 변경이며 이 버전 PR에서 추가한 schema 변경이 아닙니다.

## Git 기준점과 최종 EXE

| 기준 | 커밋 |
|---|---|
| 이전 2.8.3 EXE 소스 | `d671647c10a29aea9fa7bda96d2d02e1b2bb02fb` |
| #98 병합 / 이번 출발 main | `ee9f2d07d0b827c0992b7c3b8155a9bc5ae15c71` |
| Windows 버전 / 실제 EXE 소스 | **`5055831bff9f5dd37be295cde4f97b9205a9da86`** |
| 버전 PR 병합 | 미병합, 사용자 결정 |

| EXE 항목 | 결과 |
|---|---|
| 실제 배포 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion / AssemblyVersion | **2.8.4.0 / 2.8.4.0** |
| ProductVersion | **`2.8.4+5055831bff9f5dd37be295cde4f97b9205a9da86`** |
| 크기 | **29,710,267 bytes** |
| SHA-256 | **`1A708ACBCC231F7D12DBC659F001EEC189E20F6667AE5D149D6C4D189147AF3C`** |
| 이전 2.8.3 SHA-256 | `0E1B7D4BE3E21CF24718E4BBEA2E5431273DF24595C8F804D1D7BCD04A889E39` |

이 EXE는 #98 병합 main에 버전만 올린 **미병합 버전 커밋**의 산출물입니다. 버전 PR 병합 main에서 다시 만든 EXE라고 표현하지 않습니다.
framework-dependent 단일 EXE이며 설치된 .NET 10 Desktop Runtime x64를 사용합니다. SDK **10.0.403**,
설치된 Desktop Runtime **10.0.14 x64**를 확인했습니다. 런타임·SDK를 이 작업에서 설치하거나 설정하지 않았습니다.

## 실제 검사

| 항목 | 준비 bin | 최종 Portable 단일 EXE |
|---|---|---|
| 빌드/게시 | `build.ps1 -ReportGroup release-2-8-4`, 경고 0 / 오류 0 | 별도 candidate 경로 Portable publish, 경고 0 / 오류 0 |
| self-test | **291,779 PASS**, 실제 종료 코드 0 | **291,779 PASS**, 실제 종료 코드 0 |
| 공통 / Windows 전용 | **291,074 / 705** | **291,074 / 705** |
| 고정 after 슬롯 smoke | **별도 1회 897 PASS**, 종료 코드 0 | **별도 1회 897 PASS**, 종료 코드 0 |
| #98 최종 대비 합성 PNG/layout | **26/26 · 20/20 동일** | **26/26 · 20/20 동일** |
| 반복 트레이 기록 증가 검증 | **NotEvaluated**, 새 키 관측 0개 | **NotEvaluated**, 새 키 관측 0개 |

검사 수는 #98 Windows 최종과 같으며 이 버전 PR 자체의 증감은 0입니다. 2.8.3 대비 self-test **+35,877**은
공통 xAI·네 Provider 트레이 전수 조합 확장 등 #98에서 증가했고 Windows 전용은 705건 그대로입니다.
smoke는 858→897(+39)이며 버전 PR에서 검사를 추가한 것이 아닙니다. 기존 symlink fixture 1개 SKIP은 유지합니다.
각 smoke는 `Repeat=1`이므로 두 별도 실행을 반복 검증 성공으로 합산하지 않습니다. 실제 OS 알림 전송은 0건입니다.
실제 계정 조회·공식 상태 실시간 재조회·운영 토스트 시험은 추가 실행하지 않았습니다.

## 외부 백업 → 검사 → EXE 교체

1. 사용자가 기존 2.8.3을 정상 종료한 뒤 Codex 밖 PowerShell에서 백업 도우미를 실행했습니다.
   `GetFinalPathNameByHandle`로 실제 열린 DB가 기본 `%LOCALAPPDATA%\AIBurgerClock\burgerclock.db`임을 확인하고
   프로세스·WAL/SHM 부재와 원본/백업 해시 일치를 검사했습니다. **21:56:16 KST의 `BackupSucceeded=True`**를 외부 receipt와 사용자 답변으로 확인했습니다.
2. 백업은 **`../backups/release-2.8.4-20261027-215616-4a235bf0/`**입니다. 이전 EXE·dist ZIP·종료 DB·로그를 복사했고
   이전/새 EXE 소스 ZIP도 지정 Git 커밋에서 `git archive` 종료 코드 0으로 보존했습니다. 기존 백업을 덮거나 지우지 않았습니다.
3. candidate는 dist와 분리해 게시하고 self-test·최종 smoke를 마친 뒤 검증했습니다. 교체 직전 앱/검사 프로세스 부재,
   기존 dist/백업 EXE의 2.8.3 해시와 후보 해시를 재확인했습니다. **22:01:29 KST**에 `File.Replace`로 EXE만 교체했습니다.
4. **후보 = 실제 dist = 백업한 새 EXE**의 버전·크기·SHA-256 일치를 확인했습니다. 교체 직전 파일도
   `replaced-2.8.3.exe`로 보존했습니다. 오류 시 이전 EXE를 복원하는 보호 경로를 뒀으나 이번에 복원하지 않았습니다.
5. 운영 앱·COM 활성화·등록/해제·해제 dry-run을 Codex에서 실행하지 않았습니다. 자동 시작과 운영 등록도 변경하지 않았습니다.
   Codex는 실제 운영 DB를 열지 않았습니다. LocalCache DB는 백업 용도로 보존하며 이관·병합·삭제하지 않았습니다.

백업 안의 `previous-2.8.3.exe`·`replaced-2.8.3.exe`·`deployed-2.8.4.exe`,
`dist-2.8.3.zip`·`source-before-2.8.3.zip`·`source-2.8.4.zip`, `data/`와 로컬 manifest는 비공개입니다.
DB 해시·원문 로그·실제 계정 값은 저장소/PR에 올리지 않습니다.

## 사용자 외부 확인·실행 (대기)

Codex 밖 **64-bit PowerShell**에서 아래 확인 도우미를 먼저 실행합니다. 같은 배포 경로의 기존
시작 메뉴 바로가기·AUMID·CLSID·COM·자동 시작 Run 경로를 읽고 해제 **dry-run만** 수행합니다.
등록을 만들거나 고치지 않으며 시험 알림·정상 앱 실행도 하지 않습니다. 실제 해제 명령이 아닙니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.4-deployment-20261027/Inspect-ProductionOutsideCodex.ps1'
~~~

기대값은 `RegistrationSucceeded=True`, `RegistrationPerformed=False`, `AutoStartPathMatches=True`,
`DryRunExitCode=0`, `DryRunChangedNothing=True`입니다. Run 항목이 없으면 별도로 표시하며 자동 생성하지 않습니다.
경로가 같고 등록이 정상이면 재등록할 필요가 없습니다. 이 문서 시점의 **외부 등록 대상/해제 dry-run 실제 결과는 미확인**입니다.

확인이 정상이라면 같은 외부 PowerShell에서 다음 명령으로 운영 앱을 시작합니다.

~~~powershell
Start-Process -FilePath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/dist/win-x64/AI Burger Clock.exe'
~~~

**아직 실행하지 않았습니다.** 외부 시작 후 트레이·네 상태 카드·xAI 사용량 링크·한도 세 박스를 확인하고,
기본 운영 데이터 폴더의 갱신을 외부 컨텍스트에서 확인합니다. 이때의 정상 조회·캐시 쓰기·실제 알림은 읽기 전용 검사가 아닙니다.
실제 계정 값/캡처 대신 정상 여부만 기록합니다.

## 2.8.3 EXE만 복원하는 계획

문제가 생기면 외부에서 2.8.4를 정상 종료하고 위 백업의 `previous-2.8.3.exe`만 같은 dist 경로로 복사해
FileVersion **2.8.3.0**, SHA-256 **`0E1B7D4BE3E21CF24718E4BBEA2E5431273DF24595C8F804D1D7BCD04A889E39`**를 대조합니다.
DB·로그·캐시·설정·운영 등록을 과거 상태로 자동 복원하지 않습니다. 2.8.3은 xAI 카드를 표시하지 않습니다.
복원 뒤 실행도 사용자가 외부에서 수행합니다. 이번 작업에서 실제 복원 시험은 하지 않았습니다.

## 근거 위치와 미수행

- 준비 build: `artifacts/release-2-8-4/build/20261027-215353-784/`
- 준비 smoke: `artifacts/release-2-8-4/windows-tests/after/20261027-215719-817-946ec3/`
- 최종 단일 EXE smoke: `artifacts/release-2-8-4-final/windows-tests/after/20261027-215834-882-d80de4/`
- 후보·게시/self-test 로그·외부 preflight·교체 manifest·외부 확인 도우미: `artifacts/release-2.8.4-deployment-20261027/`

Mac 전용 코드는 변경/빌드/실행하지 않았습니다. #98의 Mac 전환·공통 291,074건·최종 native smoke
SKIP 없는 PASS는 [Mac 담당 코멘트](https://github.com/hydron75/ai-burger-clock/pull/98#issuecomment-6442146820)의 기존 결과입니다.
실제 마우스 링크·VoiceOver 낭독·Mac 실계정/실피드·실제 재시작 24시간 관찰은 확인된 것으로 확대하지 않습니다.
Windows 실제 화면·xAI 실피드 새 조회·운영 알림 표시, 재부팅·절전·장시간 운영·실제 복원도 이번에 확인하지 않았습니다.
