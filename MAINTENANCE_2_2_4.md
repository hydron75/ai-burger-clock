# 2.2.4 — Windows 누적 변경 배포 준비 (승인 대기)

2026-10-09 KST. #37·#38·#39까지 병합된 main `26ae908`에서 Windows 버전과 기록을 준비한다. **아직 배포 완료 기록이 아니다.** 현재 로컬 `dist/win-x64/AI Burger Clock.exe`는 2.2.3이며, 준비 PR 병합·교체 직전 백업·publish·배포 교체는 사용자 승인 후에만 진행한다.

Windows 11 x64 / WinForms / net10.0-windows / framework-dependent 단일 EXE, Microsoft.Data.Sqlite 10.0.12와 DB schema 2를 유지한다. Mac 폴더·MACOS_PORT.md·Mac 버전과 공통 원본 27개는 이 준비 PR에서 수정하지 않는다.

## 버전과 변경 범위

**2.2.4를 제안한다.** 새 Provider·DB 구조·조회 정책을 추가하는 릴리스가 아니라 기존 기능의 표시·메모 처리 개선과 공통화 반영이므로 2.3.0 대신 유지보수 버전으로 준비했다. AiBurgerClock.csproj의 Version은 2.2.4, AssemblyVersion·FileVersion은 2.2.4.0이다.

비교 범위는 실제 2.2.3 EXE의 소스 `7772fcbf2ed597ca83907a1ccbd03d68b6605079`부터 main `26ae90862a27a00a79d3152a4676bd2cf99ca568`까지다. Windows에 새로 배포할 내용은 다음과 같다.

| 항목 | 2.2.3 배포본 이후 Windows 변경 |
|---|---|
| 상태·한도 표시 공통화 (#21·#22·#25) | ProviderNames·StatusPanelModel·QuotaPanelModel로 본문·표시 판정을 공유. UI·색·클릭·메뉴 생성은 Windows에 유지 |
| 기록·피드백 공통화 (#23·#25) | UsageMeasurementFactory로 입력 전 시각·상태 캡처, 기록 메뉴와 메모 제한을 공유. FeedbackText 문구를 사용 |
| 공통 검사 정리 (#25·#31) | SharedTestSuite 한 목록을 Windows·Shared.Tests가 한 번 실행. OS 무관 트레이·CLI 검사는 공통으로 옮기고 Windows 아이콘 픽셀·경로 검사는 남김 |
| 통계 문구 (#32·#34) | StatisticsText를 사용. 기존 Windows 기록 위치 문장·화면·계산·기간·표는 유지 |
| 매초 판정 (#33·#36) | StatusTicker로 전환·Provider 알림·아이콘 변경 판정을 공유. RefreshStatus 감싸는 함수·알림 순서·색·초 단위 카운트다운 유지 |
| Windows 경고 로그 (#36) | 중복 Provider는 첫 값을 사용하고 크기 제한 파일에 경고. 정상 입력에는 파일을 만들지 않음 |
| 상세 Tooltip (#37) | 값이 없는 관련 구성요소·사건·사건 ID·마지막 알려진 상태·출처 줄 숨김. 조회·성공 시각과 값 있는 줄의 순서 유지 |
| Provider별 한도 박스 (#38) | ChatGPT·Claude를 흰 박스로 분리, 제목은 박스 밖 기준선, 본문은 상태 카드와 같은 들여쓰기. hover 없음 |
| 메모 잘림 안내 (#39) | 실제로 잘렸을 때 성공 저장 뒤 ‘앞부분 N자만 저장’ 피드백. 1,000자 이하·공백만 정리된 경우는 기존 안내 |

메모는 저장 시 앞뒤 공백을 정리하고, 1,000 UTF-16 단위 이하의 완전한 문자 요소 경계에서 제한한다. 이모지·결합 문자를 반으로 자르지 않으며 경계에 따라 999자 등으로 저장될 수 있다. 기존 입력 중 제한과 달리 전체 입력을 받은 뒤 저장 때 한 번만 정리한다. 기존 기록을 다시 쓰지는 않는다.

한도↔상태 전환과 논리 ClientSize **374×518**, 한도 영역 **342×214**는 그대로다. 표준 두 계정 한도는 스크롤 없이 표시하고 추가 모델별 한도는 내부 스크롤을 유지한다. #38의 한도 PNG 변경은 의도한 변경이며 공통화 자체의 기존 Windows 화면 보존과 구분한다.

### 바꾸지 않은 것

- AccountQuotaClient·Models·Parsers·Policy·Monitor, UsageStore·Phase2Models·AutoStartManager는 2.2.3 소스와 동일하다. CLI 인증·DB schema 2·저장 식별자·데이터 경로·자동 시작 계약을 유지한다.
- 기본 6시간 / 잔여 0% 초과~10% 미만 1시간 / 정확한 0% 15분 / 리셋 전후 15분 구간 5분, 실패 재시도와 기존 Refresh는 그대로다.
- NetworkRefreshScheduler의 5초 대기·1분 연기·유효 연결 없으면 건너뛰기, 공통 HTTP/알림 설정과 QuotaJsonContext는 **이미 2.2.3에 포함**됐다. 이번 신규 변경으로 다시 세지 않는다.
- 실제 main의 한도 Provider는 Codex·Claude 둘이다. Gemini/agy 한도 조회는 이 후보에 없으며, 공식 Gemini 서비스 상태와 기록·통계는 기존대로 유지한다.
- 크레딧·리셋권 처리·한도 회복 알림·공개 자료 모니터링 일정 변경은 추가하지 않는다. 7b·7c 공통화는 MACOS_UI_PLAN 결정 13에 따라 보류한다.

## 이 준비 PR의 변경 파일

기존 수정:

- AiBurgerClock.csproj: Windows 버전 세 값 함께 변경.
- UIRegressionChecks.cs: 한도 모드에서 트레이 기록 메뉴로 긴 메모를 실제 저장하고, 두 박스·잘림 피드백·상태 복귀를 함께 확인하는 native 검사 3건. 제품 동작은 변경하지 않음.
- README.md·CODE_GUIDE.md·BACKLOG.md: 후보/배포본 구분, 사용법·소스 지도·완료/보류 상태와 이 기록 연결.

신규:

- MAINTENANCE_2_2_4.md: 이 준비·검증·승인 후 교체 계획.

C# 지도는 루트·Properties 65개(앱 기능 36 + 검사·진단 28 + 명찰 1), Mac 호스트 7개, 공통 검사 입구 1개로 총 73개다. 새 의존성·공통 DLL·상주 도구는 추가하지 않았다. 범위는 **Windows만 변경 + 기록 문서**다. 이번 PR의 공통 코드·Mac 코드 변경은 없고 기록·공휴일·시작·종료의 제품 흐름도 바꾸지 않는다.

## 빌드와 검사

Windows 11 Pro x64 10.0.26300 / .NET SDK **10.0.401**에서 실행했다. WinExe의 종료 코드는 build.ps1 또는 직접 Start-Process -Wait -PassThru의 실제 ExitCode로 확인한다. 로그는 로컬 `artifacts/release-2.2.4-preparation-20261009/`에 보존한다. self-test는 임시 DB·가짜 HTTP/CLI, smoke는 같은 가짜 응답과 실제 WinForms 메시지 루프를 사용하며 실제 계정 조회·사용자 DB 저장·자동 시작 변경은 하지 않는다.

| 검증 | 실제 결과 | 로그 폴더 |
|---|---|---|
| 기존 dist 2.2.3 self-test | 250,779 assertions, ExitCode 0 | deployed-2.2.3/ |
| 최신 main 26ae908 build.ps1 | 경고 0 / 오류 0, self-test 251,182, ExitCode 0 | baseline/ |
| 2.2.4 후보 40cad7f build.ps1 | 경고 0 / 오류 0, self-test 251,182, ExitCode 0 | candidate/ |
| 최신 main native smoke / PNG | 앱 정상 종료 대기, 미실행 | baseline/ |
| 2.2.4 후보 native smoke / 통합 PNG | 앱 정상 종료 대기, 미실행 | candidate/ |
| 현재 자동 시작 등록 | 기존 dist 절대 경로 + --autostart, Run String·StartupApproved Binary `020000000000000000000000` 읽기 확인. 변경 없음 | 읽기 전용 |
| build.ps1 -Publish / 최종 dist 검사 | **사용자 승인 전 미수행** | 승인 후 기록 |

### assertion 수 비교

옮긴 검사 그룹은 합산하여 비교한다. 이번 main·후보 실행에서 공통 **250,875** + Windows 전용 **307** = **251,182**다.

| 검사 그룹 | 기존 dist 2.2.3 | main / 2.2.4 후보 | 차이 |
|---|---:|---:|---:|
| AutoStart | 229 | 229 | 0 |
| Schedule / DST | 242,363 | 242,363 | 0 |
| US holidays | 1,399 | 1,399 | 0 |
| 공식 Provider / HTTP | 96 | 96 | 0 |
| Monitor | 119 | 119 | 0 |
| Tray 합계 | 6,130 | 6,086 공통 + 44 Windows = 6,130 | 0 |
| StatusTicker | — | 63 | +63 |
| SQLite / statistics | 132 | 132 | 0 |
| Holiday metadata | 2 | 2 | 0 |
| quota schemas / CLI 합계 | 192 | 143 + 46 공통 + 3 Windows = 192 | 0 |
| QuotaMonitor | 80 | 80 | 0 |
| PortablePlatform | 37 | 37 | 0 |
| PanelModel | — | 249 | +249 |
| Recording | — | 40 | +40 |
| StatisticsText | — | 20 | +20 |
| Windows warning log | — | 31 | +31 |
| **전체** | **250,779** | **251,182** | **+403** |

줄어든 그룹은 없다. main 대비 후보 self-test는 모든 그룹이 동일하다. 이 PR에서 추가한 3건은 self-test가 아닌 native smoke의 UIRegressionChecks에만 들어간다.

### 통합 smoke 확인 항목

- 기존 상태·통계·트레이·공휴일·한도 검사와 #38의 박스/기준선/여백/hover 없음/스크롤 검사.
- #39의 실제 메모 저장 6가지: 1,000 초과, 앞뒤 공백 포함 초과, 공백 제거만으로 정확히 1,000, 한 번만 정규화, 이모지 경계, 정확히 1,000.
- 추가 3건: 한도 모드에서 트레이로 1,005자 메모 저장 → Codex/Claude 박스 유지·상태 카드 숨김 → ‘앞부분 1000자만 저장’ 안내 → 상태 모드 복귀 후 안내 유지.
- `account-quotas-note-truncated.png`는 통합 상태의 가짜 응답 렌더이며 실제 계정·바탕화면 캡처가 아니다.
- baseline과 후보에서 이름이 같은 PNG 전부를 해시·픽셀로 비교하고, 후보에만 있는 통합 PNG는 별도로 눈으로 확인한다.

### 미수행과 제한

- 앱 종료 대기 중인 native smoke·PNG 비교는 위 표대로 미실행이며 통과로 적지 않는다.
- 실제 전체 네트워크 단절·재연결·재부팅·로그인·절전·장기간 실시간 대기·실제 계정 소진/리셋·배너 전부의 눈에 보이는 노출은 미수행.
- --verify-autostart, 실제 레지스트리 변경, 별도 --check-quotas / --check-providers, Mac build/native smoke, 사용자 DB 열람·DB 검사/백업은 미수행.
- 초기 앱 정상 종료 메뉴 접근은 Computer Use에서 targetable window가 없어 사용자 종료를 요청했다. 강제 종료하지 않았다.

## Git 기준점과 백업

| 기준 | 전체 커밋 해시 / 상태 |
|---|---|
| 현재 배포 2.2.3 EXE 소스 | `7772fcbf2ed597ca83907a1ccbd03d68b6605079` |
| #37 main 병합 | `71a54b9f4f687e37a3fc8211e396a0c439b94836` |
| #38 main 병합 | `df8cba897f7128de49218e8667e3a26763d7b201` |
| #39 병합 후 작업 전 main | `26ae90862a27a00a79d3152a4676bd2cf99ca568` |
| 후보 버전·Windows 통합 검사 / 검증한 코드 HEAD | `40cad7f635619942751eee33c7e0135e30e0e895` |
| 준비 PR 최종 HEAD | PR 본문에 전체 해시 기록. 코드 검증 이후에는 문서·합성 PNG만 추가 |
| 준비 PR main 병합 | **승인 대기, 미수행** |
| 최종 배포 EXE 소스 | **승인 후 publish 때 ProductVersion과 함께 기록** |

로컬 release 백업은 **아직 만들지 않았다.** 준비 중 runtime 스냅샷은 main 비교 검사용일 뿐 교체 직전 release 백업을 대신하지 않는다.

승인·실제 교체 당일 KST 날짜로 `../backups/release-2.2.4-20261009/`를 사용할 계획이다. 날짜가 바뀌면 이름과 기록을 맞추며 기존 폴더가 있으면 덮어쓰지 않고 고유 이름을 사용한다. 워크스페이스 전체 경로는 `C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/backups/release-2.2.4-20261009/`다.

| 보관 대상 | 교체 승인 후 수행할 내용 |
|---|---|
| dist-2.2.3.zip | 기존 dist 전체 |
| replaced-2.2.3.exe | 기존 EXE 원본, 아래 2.2.3 SHA-256과 일치 확인 |
| source-2.2.3-7772fcb.zip | **기존 배포 EXE 소스** 7772fcb의 git archive. 현재 main을 2.2.3 원본으로 잘못 적지 않음 |
| burgerclock.db | 정상 종료·실제 프로세스 종료와 WAL/SHM 없음 확인 뒤 교체 직전 DB 복사 |
| 자동 시작/파일 manifest | 이 앱의 Run 원본 종류·값, StartupApproved 원본 종류·바이트, 각 백업 파일의 SHA-256 |
| 새 EXE 보관본 | 승인·publish 후에만 새 파일 버전·크기·SHA-256과 함께 보관 |

백업·개인 DB·배포 EXE는 GitHub에 올리지 않는다. 실제 DB의 WAL/SHM이 남으면 DB 파일 단독 복사를 진행하지 않고 원인을 확인한다. 이를 맞추기 위해 WAL을 삭제하지 않는다. 백업과 원본 DB 해시 비교는 일반 앱 재실행 전까지 유효하며, 새 조회 뒤 원본 캐시는 정상적으로 바뀔 수 있다.

## 실행 파일 — 후보와 현재 배포본 구분

| 항목 | 2.2.4 **일반 빌드 후보** | 기존 **배포 2.2.3** |
|---|---|---|
| 경로 | bin/Release/net10.0-windows/win-x64/AI Burger Clock.exe | dist/win-x64/AI Burger Clock.exe |
| FileVersion | 2.2.4.0 | 2.2.3.0 |
| ProductVersion | `2.2.4+40cad7f635619942751eee33c7e0135e30e0e895` | `2.2.3+7772fcbf2ed597ca83907a1ccbd03d68b6605079` |
| 크기 | 163,328 bytes | 3,040,491 bytes |
| SHA-256 | `34A164371640E618C382D3D53C749A2C8EB061E62987229A39338FB32EC17D7A` | `E166448B0BAD9822390F4A0A2EC23471497DA8083EACEC7336578B890C8F286D` |

후보의 bin EXE는 **폴더 전체가 필요한 apphost**로 최종 단일 EXE가 아니다. 이 후보 해시를 아직 만들지 않은 2.2.4 dist 해시로 적지 않는다. 최종 EXE의 경로·FileVersion·ProductVersion·크기·SHA-256은 승인 후 publish에서 다시 확인한다.

## 승인 후 교체와 자동 시작 확인 계획

1. 사용자가 준비 PR 병합과 교체를 승인한 뒤 정확한 병합 main을 받는다. 트레이 정상 종료가 진행 중 저장까지 기다렸는지 실제 프로세스 종료로 확인한다.
2. 위 release 백업을 만들고 해시를 확인한다. 실제 교체 직전에 다시 백업하며 준비용 검사 때의 오래된 DB 복사본으로 대신하지 않는다.
3. `build.ps1 -Publish`로 **기존 dist 경로**를 교체한다. 이것은 dist를 덮어쓰므로 이번 준비에서는 실행하지 않았다. 최종 dist EXE로 self-test·smoke를 실행하고 실제 종료 코드와 버전·소스 해시·크기·SHA를 기록한다.
4. 같은 dist EXE를 --autostart로 실행한다. framework-dependent이므로 기존 .NET 10 Desktop Runtime x64 요구사항은 유지한다.
5. HKCU Run의 `AI Burger Clock` 원본이 기존 dist 절대 경로와 --autostart를 가리키는지 읽기 전용으로 확인한다. 같은 경로 교체이면 **경로는 그대로지만 그 경로의 파일은 새 2.2.4**다. 실행 중 프로세스의 실제 ExecutablePath와 그 파일 FileVersion을 함께 확인한다.
6. StartupApproved의 종류·원본 바이트가 전후 동일한지도 비교한다. 현재는 Run String·StartupApproved Binary `020000000000000000000000`이다. 다른 경로·차단·해석 불가이면 그대로 보고하며 자동으로 체크박스나 레지스트리를 고치지 않는다. 실제 재로그인 성공은 별도 확인이다.

등록값은 `"<이 작업공간>/AiBurgerClock/dist/win-x64/AI Burger Clock.exe" --autostart` 형태다. 테스트 bin EXE가 ‘다른 경로’를 표시하는 것은 정상일 수 있고 최종 dist 경로 검증을 대신하지 않는다. --verify-autostart는 실제 레지스트리를 잠시 바꾸므로 이번 읽기 전용 확인에서는 제외했다.

## 2.2.3으로 되돌리는 방법

새 앱 정상 종료 → **현재 DB 별도 보존** → release 백업의 replaced-2.2.3.exe를 **원래 dist 경로**로 복사 → FileVersion 2.2.3.0·원본 SHA-256 확인 → 같은 경로에서 --autostart로 실행한다.

2.2.3과 이번 후보 모두 schema 2이며 저장 계약이 같으므로 EXE만 되돌릴 때 오래된 DB 백업으로 덮어쓸 필요가 없다. DB까지 되돌리려면 그 뒤 기록 유실을 설명하고 별도 승인받는다. 로그와 데이터 폴더를 통째로 삭제하거나 백업 EXE에서 자동 시작을 재등록하지 않는다.

**여기까지는 준비다. PR 병합·release 백업·publish·배포 교체는 승인 전 수행하지 않는다.**
