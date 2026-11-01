# AI Burger Clock 2.8.8 — Windows 모바일 사이트 한도 송신기 배포 기록

기록일: **2026-11-02 KST**. #111 병합 뒤 main을 `git pull --ff-only`로 갱신하고
`codex/release-windows-2-8-8`에서 Windows 버전 세 필드와 관련 문서만 갱신합니다.
공통·Windows C#·검사·Mac 파일·의존성·사이트 코드는 다시 고치지 않습니다.
사용자는 **검증 후 같은 dist EXE 교체까지** 승인했습니다. 외부 정상 종료·실제 운영 데이터 백업과
준비/단일 EXE 검사를 끝내고 **2026-11-02 00:16:35 KST**에 EXE만 교체했습니다.
**외부 등록 확인·운영 실행·기본 화면과 3단계 실제 연결의 아이폰 표시를 확인 완료**했으며,
각 확인 주체와 남은 미확인은 아래에 구분합니다.
운영 앱 실행·운영 등록·실제 해제·시험 알림·실제 사이트 전송은 Codex에서 하지 않습니다.
병합은 사용자가 결정합니다. 실제 연결은 아래 3단계 절차로 사용자가 외부 실행한 앱에서 확인합니다.

## 2.8.7 이후 포함하는 Windows 변경

| 출처 | 2.8.8에 포함하는 내용 |
|---|---|
| #111 · 설정 | 트레이 **모바일 사이트 연동…**, 기본 꺼짐, HTTPS 사이트 주소, 토큰 존재 표시·교체, 저장·테스트 전송, 마지막 성공·재시도 예정·일반화한 실패 사유 |
| #111 · 전송 | 실제 한도 조회 완료(성공·실패)와 15분 하트비트, 전송 중 1건/대기 최신 1건, 30초→2분→5분→15분 재시도, 종료 시 취소·추가 전송 없음 |
| #111 · 보관·계약 | Windows 전용 원자적 sourceId/seq 설정, 기존 자격 증명 대상 재사용, 허용 필드만 소스 생성 JSON 직렬화, HTTPS·15초·1MiB·리다이렉트 금지·정상 인증서 검증 |
| #111 · 격리 | 시작/캐시 로드 자체 제외, 배경 전송으로 UI·기존 조회·알림 대기 없음, OFF 자동 전송 0건, 실제 연결 전 합성 자료·가짜 HTTP/자격 증명으로 검사 |

기존 한도 조회 정책·알림 조건/문구/대기열·DB schema·상태/한도 창·운영 등록은 유지합니다.
상세 계약과 #111 구현 당시 범위는 [Windows 송신기 안내](WINDOWS_QUOTA_EXPORT.md)에 있습니다.
버전 PR 자체의 검사 증감은 0이며, #111에서 늘어난 Windows self-test **200건**·smoke **21건**과 구분합니다.

## Git 기준점과 단일 EXE

| 기준 | 커밋 |
|---|---|
| 이전 2.8.7 EXE 소스 | `464b00de4b7034196ec2445786e5e6c1974e6e80` |
| #111 최종 검증 HEAD | `33ae7e3e56b51d0a5ad31e0b49372af6696ef9e2` |
| #111 병합 / 이번 출발 main | `49c76de312444de3730cc8f2acaa6bd70c83eeb0` |
| Windows 버전 / 실제 EXE 소스 | **`73e70a1c6d0fe1f29cd594acc4a0b0a75496d2bd`** |
| 버전 PR 병합 | 미병합, 사용자 결정 |

실제 EXE는 위 미병합 버전 커밋의 산출물이며 이후 문서/PR 병합 커밋과 구분합니다.
FileVersion/AssemblyVersion은 **2.8.8.0**, ProductVersion은
**`2.8.8+73e70a1c6d0fe1f29cd594acc4a0b0a75496d2bd`**입니다.
크기는 **29,927,355 bytes**, SHA-256은
**`3EEC46E3CAFDA6C6E10CE4388C1C3DC7A0277D0AFA49B6FA4EC03E5FB88D75D0`**입니다.
framework-dependent 단일 EXE이며 설치된 .NET 10 Desktop Runtime x64를 사용합니다.
SDK는 **10.0.403**, Desktop Runtime은 **10.0.14**이며 런타임/SDK 설치·업데이트는 하지 않습니다.

이전 dist는 **2.8.7.0**, SHA-256
**`EB9AD42A65A3AA16BC34DB0A223334AEB4C7B30F5C53FF539DD605C1273C7BBD`**입니다.
배포 경로는 `dist/win-x64/AI Burger Clock.exe`로 유지합니다.

## 검사 결과

| 항목 | 준비 bin | 최종 Portable 단일 EXE |
|---|---|---|
| 빌드/게시 | `build.ps1 -ReportGroup release-2-8-8`, 경고 0 / 오류 0 | 별도 candidate 경로 publish, 경고·오류 출력 없음·종료 코드 0 |
| self-test | **292,188 PASS**, 종료 코드 0 | **292,188 PASS**, 종료 코드 0 |
| 공통 / Windows 전용 | **291,077 / 1,111** | **291,077 / 1,111** |
| 고정 after 슬롯 smoke | **별도 1회 990 PASS**, 종료 코드 0 | **별도 1회 990 PASS**, 종료 코드 0 |
| #111 최종 대비 합성 PNG / layout | **45/45 · 38/38 동일** | **45/45 · 38/38 동일** |

기존 symlink fixture 1개 SKIP은 유지합니다. 실제 계정/CLI 조회·사이트 전송·자격 증명 읽기/교체·
운영 알림/등록·Mac 빌드/검사는 하지 않습니다. 실제 연결은 합성 검사 통과와 구분합니다.
PNG/layout은 SHA-256 파일 바이트로 대조했습니다. PNG 바이트 일치는 픽셀 동일도 보장합니다.
이전 Python 비교기의 PIL이 없어 설치 없이 PowerShell 비교 도우미를 사용했고 앱 코드는 바꾸지 않았습니다.
각 smoke의 native OS 알림 제출·등록 명령·실제 사이트/자격 증명 호출은 0건입니다.
각 별도 1회이므로 반복 트레이 기록 증가 검증은 **NotEvaluated**이고 관측 신규 키는 각각 0개입니다.
두 산출물의 별도 검사를 반복 성공으로 합산하지 않습니다.

## 외부 백업 → 검사 → 승인된 EXE 교체

사용자가 기존 2.8.7을 트레이에서 정상 종료하고 Codex 밖 PowerShell에서 실행합니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.8-deployment-20261102/Backup-OutsideCodex.ps1'
~~~

정상 기대값: `BackupSucceeded=True`, `WalAndShmAbsent=True`, `ActualDataRootVerified=True`,
`AppStarted=False`, `RegistrationChanged=False`.
실제 기본 DB의 열린 경로·WAL/SHM·프로세스 부재·복사 해시를 외부 도우미가 확인합니다.
**2026-11-02 00:11:52 KST의 `BackupSucceeded=True`**를 사용자 답변과 외부 receipt로 대조했습니다.
백업은 **`../backups/release-2.8.8-20261102-001152-1bfe878c/`**에 새로 만들었습니다.
위 백업은 완료됐으므로 같은 도우미를 다시 실행하지 않습니다.
Codex가 가상화된 DB 사본으로 실제 운영 DB를 검증했다고 하지 않습니다. WAL을 임의로 지우지 않습니다.

사용자 승인과 외부 백업 receipt·최종 후보 smoke를 확인한 뒤 교체 직전 프로세스 부재·reparse 부재·
이전/후보/백업 해시와 버전을 다시 확인했습니다. **00:16:35 KST**에 `File.Replace`로 EXE만 교체했습니다.
이전 EXE/ZIP·DB·로그·이전/새 커밋 소스 ZIP과 교체된 EXE를 새 백업에 보존했습니다.
후보 = 실제 dist = 보존한 `deployed-2.8.8.exe`의 버전·크기·해시가 같으며,
`previous-2.8.7.exe`와 `replaced-2.8.7.exe`도 이전 EXE 해시와 같습니다.
기존 백업과 두 데이터 폴더는 삭제·이관·병합하지 않습니다. EXE 교체 후 버전·크기·해시를 대조하며
실패하면 아래 계획대로 이전 EXE만 복원합니다. 이번 교체에서 복원하지 않았습니다.

## 사용자 외부 등록 확인·운영 실행

같은 dist 경로의 바로가기·AUMID·CLSID·COM·자동 시작 대상을 확인하고 해제 **dry-run만** 실행합니다.
재등록·실제 해제·시험 알림은 하지 않습니다. **EXE 교체 완료 후** 먼저 다음 확인 명령만 실행합니다.

~~~powershell
& 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.8.8-deployment-20261102/Inspect-ProductionOutsideCodex.ps1'
~~~

정상 기대값:
`RegistrationSucceeded=True`, `RegistrationPerformed=False`, `AutoStartPathMatches=True`,
`FileVersion=2.8.8.0`, 위 SHA-256 일치, `DryRunExitCode=0`, `DryRunChangedNothing=True`,
`TestToastSent=False`, `NormalAppLaunched=False`.
마지막 값은 도우미 자체가 일반 앱을 시작하지 않았다는 뜻이며 다음 사용자 실행과 구분합니다.
등록이 없거나 경로가 다르면 멈추고 결과를 보고합니다. 자동 재등록하지 않습니다.
**위 확인이 모두 정상일 때만** 사용자가 외부 PowerShell에서 앱을 실행합니다.

~~~powershell
Start-Process -FilePath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/dist/win-x64/AI Burger Clock.exe'
~~~

외부 실행 뒤 단일 2.8.8 프로세스·트레이·상태/한도 보기·새 설정 메뉴가 정상이어야 합니다.
운영 DB/로그는 `%LOCALAPPDATA%\AIBurgerClock`를 계속 쓰며 LocalCache DB는 백업으로 보존합니다.
기본 OFF이므로 기존 토큰이 있어도 설정을 켜거나 테스트하기 전 자동 사이트 전송은 없습니다.

**외부 등록 확인 완료(2026-11-02 00:22:57 KST):** 사용자 외부 실행 receipt에서
`RegistrationSucceeded=True`, `RegistrationPerformed=False`, `AutoStartRunEntryPresent=True`,
`AutoStartPathMatches=True`, `FileVersion=2.8.8.0`, 위 EXE SHA-256 일치,
`DryRunExitCode=0`, `DryRunChangedNothing=True`, `TestToastSent=False`, `NormalAppLaunched=False`를 대조했습니다.
실제 바로가기·AUMID·CLSID·COM이 같은 dist EXE를 가리키며 재등록·실제 해제·시험 알림은 하지 않았습니다.

**외부 운영 실행·화면 정상은 사용자 확인:** 트레이·상태 보기·한도 보기와 ‘모바일 사이트 연동…’의
기본 꺼짐이 정상이라고 확인했습니다. 별도 읽기 전용 프로세스 확인에서도 **00:23:33 KST 시작**, 같은 dist 경로의
단일 **2.8.8.0** 프로세스와 위 SHA-256 일치를 대조했습니다. 제한된 파일/프로세스 보기에서의 부재를 운영 종료로
단정하지 않고 접근이 허용된 읽기 전용 프로세스 목록으로 확인했습니다. 이번 확인에서 운영 DB/로그 내용이나 갱신 시각은 읽지 않았습니다.
외부 앱의 정상 한도 조회는 검사 코드의 가짜 CLI 실행과 별개이며 실제 사이트 전송 성공을 뜻하지 않습니다.

## 3단계: 실제 연결 절차 — 아이폰 표시 사용자 확인 완료

이 절은 **실제 계정 한도 스냅샷을 개인 비공개 사이트에 전송하는** 사용자 절차입니다.
Codex는 실제 전송을 실행하지 않았습니다. 아래 공개 예시는 합성 주소 `https://quota.example.test`뿐이며
사용자는 자기 비공개 사이트 주소를 창에 직접 입력합니다. 토큰·한도 값·화면 캡처를 PR/문서/채팅에 붙이지 않습니다.

**사용자 확인(2026-11-02 KST):** 아이폰에서 ChatGPT(Work/Codex)·Claude·Gemini(Antigravity)
세 Provider의 **한도 행·사용률·리셋·사용량 막대·마지막 수신 시각이 모두 정상 표시**됐습니다.
실제 연결 결과에 대한 사용자 확인이며, Codex의 합성 검사/가짜 HTTP 결과와 구분합니다.
실제 수치·시각·계정 식별자·토큰·캡처는 이 기록에 포함하지 않습니다.
이번 답변에 포함되지 않은 다음 정기 조회/15분 하트비트 경과 관찰·송신 끊김 복구·장시간 운영은 확인 완료로 확대하지 않습니다.

| 순서 | 사용자 동작 | 정상 기대값 |
|---|---|---|
| 1 | 외부에서 2.8.8 실행, 기존 CLI 조회가 끝난 뒤 트레이 **모바일 사이트 연동…** 열기 | 기본 꺼짐. 기존 한도 화면은 정상이며 여는 것만으로 전송 없음 |
| 2 | HTTPS 사이트 **기본 주소**를 입력하고 **저장**. 꺼짐은 유지 | `/api/quota`·쿼리·토큰을 주소에 붙이지 않음. 미저장 변경 안내가 해제됨 |
| 3 | **토큰: 저장됨 (값은 표시하지 않음)** 확인 | 고정 대상 `Hydron75.AIBurgerClock.SitesQuotaExport.v1`. 토큰 값은 노출되지 않음. 없으면 별도 발급/안전한 저장 절차부터 확인 |
| 4 | **테스트 전송** 한 번 | 꺼짐을 유지하며 저장된 주소로 현재 스냅샷 1건. 마지막 성공 시각 갱신, 재시도 예정 없음, 최근 실패 사유 없음 |
| 5 | **모바일 사이트로 한도 스냅샷 보내기** 켠 뒤 **저장** | 연동 켜짐. 켜기/저장 자체의 추가 전송은 없으며 다음 실제 조회 완료 또는 15분 하트비트부터 자동 전송 |
| 6 | 아이폰에서 로그인한 소유자 사이트 열기/새로고침 | ChatGPT(Work/Codex)·Claude·Gemini(Antigravity) 행 이름·사용률·간략 리셋·사용량 막대, 마지막 성공/다음 조회, 마지막 수신 시각 표시. xAI는 기존 링크 박스 유지 |
| 7 | 다음 정기 조회 완료 뒤 사이트 재확인 | 앱 마지막 전송 성공과 사이트 마지막 수신 시각 갱신. 한도 값은 변하지 않아도 됨. 사이트 5분 조회/화면 복귀/Refresh로 최신값 확인 |
| 8 | 별도 강제 조회 없이 15분 하트비트 확인 | 값이 같아도 수신 시각 갱신. 1,800초 초과 무수신이면 사이트에 **송신 끊김** 표시 |

3단계 최초 구현·위 사용자 표시 확인 당시 막대는 사용량만 표시했으며 시간층/비교 문구를 보내거나 표시하지 않았습니다.
후속 사이트 표시 변경은 Windows 앱/전송 계약과 구분합니다.
`previous`/`none`은 실제 실패·이전 값·미조회 상태에 맞게 보이며 수신 시각을 한도 성공 시각으로 바꾸지 않습니다.
서로 다른 sourceId가 있으면 사이트는 생성 시각이 가장 최신인 스냅샷을 표시합니다.

### 실패했을 때 볼 곳

- **테스트 버튼 비활성:** 주소/토큰 입력을 먼저 저장하고 저장됨 표시를 확인합니다. 입력 중인 토큰을 복사해 보고하지 않습니다.
- **앱 설정 창:** 마지막 성공·재시도 예정·최근 실패 사유부터 봅니다. 주소/토큰/형식 거부(4xx)·리다이렉트는 해당 요청 재시도 없음,
  서버/네트워크/15초 시간 초과는 켜진 상태에서 30초→2분→5분→15분입니다. OFF 테스트 실패는 자동 재시도하지 않습니다.
- **Windows 전용 파일:** `%LOCALAPPDATA%\AIBurgerClock\mobile-sites-windows-v1.json`의 존재/쓰기 권한을 확인합니다.
  sourceId/seq를 수동 초기화·변경하거나 토큰을 파일에 적지 않습니다. 기존 파일 손상은 안전하게 송신 중지됩니다.
- **조회가 끝났는지:** 한도 화면의 성공/다음 조회 표시와 기존 `logs/quota-polling-diagnostics.json`을 로컬에서 확인합니다.
  진단의 완료 성공은 사이트 전송 성공과 별개입니다. 송신 상태는 설정 창 메모리에만 있으며 전용 본문/토큰 로그는 없습니다.
- **사이트:** 소유자 로그인, 저장한 주소 일치, 마지막 수신·송신 끊김·다른 source 선택을 확인합니다.
  앱과 사이트 시계가 5분 넘게 어긋나 미래 생성 시각이 되면 거부될 수 있습니다. 2xx 중복 무시는 수신 시각을 새로 쓰지 않습니다.
  해결되지 않으면 일반화한 실패 사유/성공 여부만 보고하고 실제 값·원문·토큰은 보내지 않습니다.

**남은 미확인:** 자격 증명 교체, 다음 정기 조회/15분 하트비트 경과에 따른 갱신,
송신 끊김 복구·절전/네트워크 복귀·장시간 송신입니다. 토큰 검증/실제 저장의 내부 과정을 Codex가 별도로 검사한 것은 아닙니다.
실제 연결의 아이폰 표시 정상은 위 사용자 확인에 근거하며 합성 self-test/smoke나 운영 등록 검사로 대신하지 않습니다.

## 2.8.7 EXE만 복원하는 계획

사용자가 외부에서 새 앱을 정상 종료한 뒤 새 백업의 `previous-2.8.7.exe`만 같은 dist 경로에 복사합니다.
FileVersion **2.8.7.0**, SHA-256 **`EB9AD42A65A3AA16BC34DB0A223334AEB4C7B30F5C53FF539DD605C1273C7BBD`**를 대조합니다.
DB·로그·캐시·운영 등록은 과거 백업으로 자동 복원하지 않습니다. 두 DB 폴더와 새 설정 `mobile-sites-windows-v1.json`·
자격 증명도 보존합니다. 2.8.7은 새 송신 설정을 읽지 않으므로 사이트 전송 기능은 없어집니다.
같은 경로의 알림 등록은 다시 만들지 않으며 복원 뒤 앱 실행도 사용자 외부 수행입니다. 실제 복원 시험은 미수행입니다.

## 로컬 근거

- 준비 build/self-test: `artifacts/release-2-8-8/build/20261102-000447-896/`
- 후보 publish/self-test·metadata·외부 도우미/receipt: `artifacts/release-2.8.8-deployment-20261102/`
- 준비 smoke: `artifacts/release-2-8-8/windows-tests/after/20261102-001237-407-a7425a/`
- 최종 단일 EXE smoke: `artifacts/release-2-8-8-final/windows-tests/after/20261102-001452-680-974571/`
- PNG/layout 비교: `artifacts/release-2.8.8-deployment-20261102/preparation-comparison.json`·`final-comparison.json`
- 단일 EXE·해시·백업 receipt는 로컬에 보관하고 DB 해시/원문 로그/계정 값은 공개하지 않습니다.

## 변경 파일

- 기존 수정: `AiBurgerClock.csproj`(Windows 버전 세 필드), `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`, `WINDOWS_QUOTA_EXPORT.md`(현재 반영 상태와 안내).
- 신규 문서: 이 파일. 제품/검사 C#·Mac·공통 원본·사이트·의존성 파일 변경은 없습니다.
