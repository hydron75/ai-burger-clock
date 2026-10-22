# AI Burger Clock 2.8.3 — 공통·Windows 코드 리뷰 수정 배포 기록

기록일: **2026-10-22 KST**. #93 병합 뒤 main을 `git pull --ff-only`로 받고 `codex/windows-release-2-8-3`에서 Windows 버전·기록만 올렸다. 범위는 **Windows만 변경(버전·문서)**이다. #91·#93에서 이미 병합한 수정이 포함되며 이번 버전 PR에서 공통·Windows C#·검사·Mac 코드나 의존성을 다시 변경하지 않았다. 과거 검증·배포 기록은 그대로 유지한다.

사용자가 **PR 병합 전 로컬 백업 → 검사 → 배포 EXE 교체 → 새 버전 실행까지 승인**했다. 실제 EXE 소스는 아래 **미병합 버전 브랜치 커밋**이며 병합 main 빌드가 아니다. 버전 PR 병합은 사용자가 한다. **백업·준비/단일 EXE 검사 뒤 21:45:02 KST에 실제 dist 교체, 외부 운영 등록 대상·해제 dry-run 확인 후 21:46:03 KST에 정상 실행**했다. **실제 트레이·상태 창·한도 보기 정상은 사용자 확인**이다. 운영 재등록·시험 알림은 실행하지 않았다.

## 2.8.2 이후 Windows에 포함하는 변경

| 출처·항목 | 2.8.3에 포함하는 내용 |
|---|---|
| #91 · CORE-01 | OpenAI 사건 제목의 voice/image/billing 제외보다 관련 agent mode·ChatGPT search 등의 범위를 우선해 혼합 제목의 관련 장애를 놓치지 않음 |
| #91 · CORE-02 | Gemini의 관련 구성 요소가 명시된 확인된 장애와 범위 미확인 사건을 따로 보존. 미확인 사건의 배열 순서 때문에 확인된 장애를 UNKNOWN으로 덮지 않음 |
| #91 · CORE-04 | 정상 schema 읽기의 일시 SQLite 잠금이 영구 migration 실패로 고정되지 않게 분리. 잠금 해제 뒤 같은 저장소에서 재시도하며 실제 migration/backup 실패의 안전 중단은 유지 |
| #91 · WIN-07 | Provider·행 수·scope 유무·원본 창 ID를 길이 prefix로 구별하는 공통 layout key. ID 안 구분자 때문에 다른 행 집합을 같은 레이아웃으로 오인하던 예외를 Windows/Mac 모두 수정 |
| #91 · TEST-02/03 | HTTP 요청 시작 횟수 대신 특정 조회 완료 barrier로 검증. 수동 Refresh는 자동 주기가 긴 별도 fixture로 검사해 자동 polling의 거짓 통과를 차단 |
| #93 · WIN-03 | 같은 tick의 공식 Provider 경고를 제한된 Provider별 대기열에서 순서대로 풍선 전달. native 닫힘/클릭과 6.5초 최소 간격·이벤트 누락의 30초 fallback을 구분하고 더 최신 상태·UNKNOWN·FULL 종료 시 오래된 후보를 무효화 |
| #93 · BUILD-01 | 고정 dist를 바로 덮지 않고 임시 Portable 후보의 self-test가 성공한 뒤 교체. 실패 때 기존 정상 EXE를 보존하고 교체/복원 실패의 복구 사본은 삭제하지 않음 |
| #93 · BUILD-05 | `-Repeat 1`은 smoke 성공만 보고하고 반복 트레이 키 증가 억제는 `NotEvaluated`로 기록. 최소 두 회 성공과 후속 비교가 있어야 반복 검증 `Passed` |
| #93 · TEST-01/04 | smoke의 await 중 다음 timer 단계 재진입을 막고, Gemini 합성 pending 조회는 고정 10초 대신 명시적 gate로 보류/완료해 응답성과 완료 타이밍을 분리 |
| #93 · 최종 회귀 보완 | 공식 풍선의 순차 전송을 기다리는 동안에도 한도 대기 알림의 상태 재검증·무효화를 계속 수행 |

한도 알림의 문구·조건·독립 기본 꺼짐·사용자 설정·공통 판정·1시간 조회 정책·UI 크기·schema 2·캐시 v1은 유지한다. 공식 상태의 관련 장애 판정과 동시 공식 풍선 전달은 위 항목에 명시한 변경이다. #92 등 Mac 전용 버전·빌드 도구 변경은 Windows 신규 변경으로 세지 않는다. #91의 Mac 확인 완료와 이번 Windows 버전 작업을 구분한다.

## Git 기준점과 EXE

| 기준 | 전체 커밋 |
|---|---|
| 이전 2.8.2 실제 EXE 소스 | `ea4d6b7d01ed19343fdeb2f0504c44a031be588d` |
| #91 공통 리뷰·Windows/Mac 연결 병합 | `8d2882b5acce11f8bdcb343105d1e505ae91f24d` |
| #93 Windows 리뷰 병합 / 이번 출발 main | `d22f40926dc6d067f819cdb352200889b5c8797d` |
| 2.8.3 버전 / **실제 EXE 소스** | **`d671647c10a29aea9fa7bda96d2d02e1b2bb02fb`** |
| 2.8.3 버전 PR 병합 | 미병합, 사용자 결정 |

| 최종 단일 EXE 항목 | 결과 |
|---|---|
| 실제 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion / AssemblyVersion | **2.8.3.0 / 2.8.3.0** |
| ProductVersion | **`2.8.3+d671647c10a29aea9fa7bda96d2d02e1b2bb02fb`** |
| 크기 | **29,611,963 bytes** |
| SHA-256 | **`0E1B7D4BE3E21CF24718E4BBEA2E5431273DF24595C8F804D1D7BCD04A889E39`** |
| 이전 2.8.2 SHA-256 | `88D440CF0C1117F9352A0895A47AF7D0270AE0DABCF08A8E70DE5BC3023AFF06` |

framework-dependent 단일 EXE를 유지하며 설치된 .NET 10 Desktop Runtime x64를 쓴다. Windows TFM `net10.0-windows10.0.19041.0`, 직접 패키지와 trimming/NativeAOT 설정은 바꾸지 않는다. 과거 #86의 크기·시작 시간·메모리 측정은 [2.8.2 기록](MAINTENANCE_2_8_2.md#86에서-측정한-크기시작메모리)에 그대로 보존하며 이번에 다시 측정한 결과로 표시하지 않는다.

## 이번 버전의 실제 검사

SDK **10.0.402**, Desktop Runtime **10.0.13 x64**. WinExe의 종료 코드는 `Start-Process -Wait -PassThru`로 실제 값을 확인했다. 합성 응답·임시 DB만 사용했고 실제 계정 조회 명령이나 시험 알림을 추가 실행하지 않았다.

| 항목 | 준비 bin | 배포할 Portable 단일 EXE |
|---|---|---|
| 빌드/게시 | `build.ps1 -ReportGroup local-release-2-8-3` — 경고 0 / 오류 0·실제 ExitCode 0 | 별도 후보 경로 Portable publish — 경고 0 / 오류 0·실제 ExitCode 0 |
| self-test | **255,902건·실제 ExitCode 0** | **255,902건·실제 ExitCode 0** |
| 공통 / Windows 전용 | **255,197 / 705** | **255,197 / 705** |
| 고정 after 슬롯 native smoke | **1회 858 PASS·실제 ExitCode 0, 46.09초** | **별도 1회 858 PASS·실제 ExitCode 0, 46.19초** |
| 합성 PNG/layout 비교 | #93 최종 대비 **PNG 22/22·layout JSON 16/16 바이트 동일** | #93 최종 대비 **PNG 22/22·layout JSON 16/16 바이트 동일** |
| 반복 트레이 키 증가 검증 | **`NotEvaluated` / Comparisons 0**, 새 키 관측 0개 | **`NotEvaluated` / Comparisons 0**, 새 키 관측 0개 |

이번 실제 검사 수는 #93 최종 self-test **255,902건(공통 255,197 + Windows 705)**·smoke **858 PASS**와 같아 버전 PR 자체의 검사 증감은 0이다. 2.8.2 대비 self-test **+141 = 공통 +135 + Windows +6**은 #91에서 증가했고, #93은 self-test 수를 바꾸지 않고 smoke를 **837→858(+21)**로 보완했다. 기존 symlink fixture 한 건의 환경 제한 SKIP은 그대로이며 PASS로 세지 않는다. 과거 #91/#93의 재현 실패·보완 과정은 이번 배포 성공으로 소급 변경하지 않는다.

준비와 최종 단일 EXE smoke는 각각 `-Repeat 1`인 별도 실행이다. 새 트레이 키 관측은 각각 0개지만 후속 반복 비교는 각각 0건이므로 **반복 실행의 트레이 기록 증가 억제는 미평가**다. 두 별도 실행을 하나의 반복 검증 성공으로 합산하지 않는다.

## 승인된 백업 → 검사 → 교체 → 실행

1. 기존 2.8.2 정상 종료 뒤 정확한 dist 실행 프로세스 부재와 활성 WAL/SHM 부재를 확인했다. 강제 종료는 하지 않았다. 현재 앱 DB의 요청 경로와 실제 열린 경로를 대조해 AppData 보관 뷰를 구별했고 DB 행·계정 값은 출력하지 않았다.
2. **`../backups/release-2.8.3-20261022-214043/`**에 이전 2.8.2 EXE·dist ZIP, 이전/새 EXE 소스 ZIP, 종료한 현재 DB 1개·로그·비공개 manifest를 보존했다. EXE·ZIP 안 EXE·DB의 원본/백업 해시를 대조했고 이전/새 지정 커밋의 소스 ZIP은 `git archive` 종료 코드 0으로 생성했다. 소스 ZIP의 별도 기대 해시를 대조한 것은 아니다. 기존 백업을 덮거나 지우지 않았고 이전 EXE의 SHA-256은 위 2.8.2 기록과 같았다.
3. 버전 브랜치 소스 `d671647c10a29aea9fa7bda96d2d02e1b2bb02fb`를 고정해 위 준비 build/self-test·고정 after 슬롯 smoke를 통과했다. Portable 후보도 별도 경로에서 publish/self-test/smoke를 끝낸 뒤에만 실제 dist를 교체했다. #93의 build 스크립트도 후보 self-test 전에 dist를 바꾸지 않지만, 이번에는 **최종 후보 smoke까지 교체 전에** 확인했다.
4. 교체 직전 앱 부재·DB 해시 불변·WAL/SHM 부재·백업 EXE 해시·자동 시작 경로/종류/값/승인 바이트 불변을 재확인했다. **21:45:02 KST** 같은 dist 경로에 검증 후보를 복사하고 FileVersion·ProductVersion·전체 소스·크기·SHA-256을 대조했다. **후보 = 실제 dist = 백업한 새 EXE**의 SHA-256이 위 `0E1B7D4B…`로 같았다.
5. 사용자가 Codex 밖 PowerShell에서 해시 고정 **Inspect 도우미**를 실행했다. 기존 운영 한도 알림의 바로가기·AUMID·COM이 이 실제 배포 경로를 가리킨다는 receipt와 새 EXE 해제 dry-run **실제 ExitCode 0·등록 전후 불변**을 확인했다. `RegistrationSucceeded=True`는 대상 검사 성공이며 `RegistrationPerformed=False`로 **재등록하지 않았다**. 시험 알림·History.Clear·실제 운영 등록/해제는 실행하지 않았다.
6. 실행 전 DB 불변을 재확인한 뒤 기존 실행 컨텍스트·DB·설정을 유지해 같은 dist EXE를 `--autostart`로 시작했다. **21:46:03 KST** 정확한 dist 경로·FileVersion 2.8.3.0·SHA-256 일치·단일 정상 인스턴스·InputIdle 성공·Responding=True·자동 시작 불변을 확인했다. 이후 사용자가 **트레이·상태 창·한도 보기 정상**을 확인했다. 도구에서도 실행 프로세스 1개·동일 dist·2.8.3 FileVersion/AssemblyVersion·SHA-256과 정확한 HKCU Run 명령 일치를 다시 확인했으며 실제 화면 육안 확인은 사용자 근거로 구분한다. 정상 시작에 따른 평소 조회·캐시 쓰기는 위 실행 전 DB 불변 확인과 별개다.

현재 실행은 테스트 슬롯이 아니라 **실제 2.8.3 dist 배포본**이다. HKCU Run의 큰따옴표 절대 dist 경로 + ` --autostart`는 같은 위치의 새 EXE를 가리키며 Run 값/종류·승인 바이트는 그대로다. 자동 시작 값이나 기존 한도 알림 설정을 다시 쓰지 않았다.

## 운영 등록 대상 확인과 해제 dry-run

2.8.2에서 만든 아래 운영 등록을 재사용한다. **등록 명령 `--register-quota-notifications`는 이번에 실행하지 않았다.** 사용자 외부 PowerShell의 Inspect receipt로 실제 Windows 대상 검사 성공, `RegistrationPerformed=False`, `TestToastSent=False`와 해제 dry-run을 확인했다. Codex 보관 뷰의 값만으로 실제 등록 확인을 완료했다고 쓰지 않는다.

| 항목 | 이번 확인 결과 |
|---|---|
| 시작 메뉴 바로가기 | `AI Burger Clock - quota notifications.lnk` — 실제 dist EXE 대상 일치 (`ShortcutTargetMatches=True`) |
| 바로가기 AUMID / activator CLSID | `Hydron75.AIBurgerClock.Quota` / `{10B09E94-A6B4-4ABA-9B9F-C1551C7A409B}`, 모두 일치 |
| HKCU AUMID/CLSID 소유권·실행 대상 | 소유권 검사 통과, 같은 실제 dist EXE 일치 (`AumidExecutableMatches=True`) |
| COM LocalServer32 | 실제 dist EXE + ` --quota-toast-activated` 일치 (`ComExecutableMatches=True`) |
| 새 EXE 해제 dry-run | **실제 ExitCode 0**, `Dry run: no registration changed.` |
| dry-run 전후 불변 | **`DryRunChangedNothing=True`**, 바로가기 SHA-256/속성·레지스트리 값 전후 불변 |

해제가 필요할 때는 앱을 정상 종료하고 Codex 밖 PowerShell에서 실제 배포 EXE로 실행한다. 첫 명령은 읽기 전용이며 둘째만 실제 해제다. **이번에는 둘째 명령을 실행하지 않는다.**

~~~powershell
& '<저장소>/dist/win-x64/AI Burger Clock.exe' --unregister-quota-notifications --dry-run
& '<저장소>/dist/win-x64/AI Burger Clock.exe' --unregister-quota-notifications
~~~

소유권/대상 경로 불일치면 등록을 지우지 않고 중단한다. 전용 등록만 해제하며 다른 앱·기존 알림·사용자 알림 설정·자동 시작·DB·발송 로그를 지우지 않는다. 일반 재실행이 등록을 자동 복구하지 않는다. 확인된 등록만으로 실제 배너 표시·센터 보관·클릭 성공을 주장하지 않는다.

## 2.8.2 EXE만 복원하는 계획

1. 문제가 생기면 새 2.8.3을 정상 종료하고 새 DB·로그를 별도로 보존한다. DB·캐시를 과거 백업으로 되돌리지 않는다.
2. 위 백업의 `replaced-2.8.2.exe`만 **같은 dist 경로**에 복원하고 FileVersion **2.8.2.0**, SHA-256 **`88D440CF0C1117F9352A0895A47AF7D0270AE0DABCF08A8E70DE5BC3023AFF06`**를 대조한다.
3. 2.8.2도 같은 AUMID·COM·바로가기를 사용하므로 운영 등록을 해제하거나 다시 등록하지 않는다. 자동 시작·설정도 그대로 둔다. 같은 `--autostart` 인수로 실행해 경로·응답을 확인한다.
4. 복원한 이유와 실제 결과를 기록한다. **현재까지 문제나 복원 수행은 없다. 실제 복원 시험도 하지 않았다.**

## 근거 위치와 미수행

비공개 로컬 근거: 준비 build `artifacts/local-release-2-8-3/build/20261022-214054-175/`, 준비 smoke `artifacts/local-release-2-8-3/windows-tests/after/20261022-214145-546-74e431/`, 최종 단일 EXE smoke `artifacts/local-release-2-8-3-final/windows-tests/after/20261022-214353-617-d9a58f/`다. publish·백업/교체 manifest·등록 대상 Inspect 도우미/외부 receipt·정상 시작 결과는 `artifacts/release-2.8.3-deployment-20261022/`에 있다. 화면 정상은 이 대화의 사용자 확인으로 기록했다. 전체 백업·DB·실제 계정 값이 있는 원문 로그·개인 경로 상세 manifest는 로컬 비공개로만 보존하며 저장소/PR에 올리지 않는다. 공개 렌더는 합성 데이터만 사용한다.

- 실제 운영 토스트·풍선 시험 발송, 알림 센터 보관·종료 후 클릭 재시험은 하지 않는다. 가짜 발송 smoke와 실제 OS 노출을 혼동하지 않는다.
- 재부팅·로그인/로그아웃·절전·다중 모니터·고대비/작은 화면·장시간 운영·실제 복원 시험은 이번 범위에 추가하지 않는다.
- CORE-03/05·WIN-02/05/06·MAC-02·TEST-06/07·BUILD-02와 지연 생성 성능 후보는 BACKLOG에 계속 보류한다.
- Mac 코드를 다시 빌드하거나 수정하지 않고, #91에서 완료한 Mac 확인을 이번 Windows 배포의 Mac 신규 실기 검사로 표현하지 않는다.
