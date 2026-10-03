# 2.2.3 — Windows ChatGPT 표시와 공통 연결 복구 반영

2026-10-04 KST. main에 병합된 공통 개선을 Windows 배포본에 반영하고, Windows의 제품 표시 이름을 ChatGPT로 맞췄다. Windows 전용 변경은 [PR #18](https://github.com/hydron75/ai-burger-clock/pull/18)로 main에 병합했다. [Windows 비교 검증 코멘트](https://github.com/hydron75/ai-burger-clock/pull/18#issuecomment-5974201892)를 남긴 뒤 버전 기록·백업·publish·최종 EXE 검사를 진행했다.

Windows 11 x64 / WinForms / net10.0-windows / framework-dependent 단일 EXE를 유지한다. Mac 폴더·MACOS_PORT.md·Mac 버전, 공통 원본 20개와 공통 검사는 이번 변경에서 수정하지 않았다. 직접 패키지는 Microsoft.Data.Sqlite 10.0.12 하나, SQLite schema는 2를 유지한다.

## 변경 내용과 결정

- **한도 제목:** ChatGPT 제목 아래 별도 Work/Codex 행을 넣었다. 실제 조회 대상은 그대로 Codex의 공식 계정 한도이며 일반 ChatGPT 채팅의 모든 모델 한도를 뜻하지 않는다. Codex 2창+Claude 3창의 표준 구성은 기존 popup 크기 안에서 스크롤 없이 들어간다. 추가 모델별 한도가 많으면 기존 내부 스크롤을 사용한다.
- **Provider 표시 이름:** 상태 창·접근성 설명·기록 메뉴·기록 대화상자·저장 피드백·통계 5개 탭·트레이 Tooltip·Provider 알림 제목의 OpenAI를 ChatGPT로 바꿨다. 신규 WindowsProviderNames는 표시만 바꾼다. ProviderKind.OpenAI, DB의 OpenAI 값, 공식 상태 URL과 판정 범위는 유지한다. 기존 기록을 다시 쓰거나 migration하지 않았다.
- **공통 개선 배포:** main의 PR #14·#15·#16 개선을 함께 빌드했다. 연결 복구 뒤 5초 기다리고, 대기 중 변화는 마지막 변화 기준으로 합친다. 직전 연결 복구 조회 후 1분 안의 변화는 버리지 않고 1분 시점으로 연기한다. 대기 끝에 쓸 수 있는 연결이 없으면 조회를 건너뛰고 1분 제한도 소비하지 않는다. 공통 HTTP 설정·전환/Provider 알림 본문도 main의 함수를 그대로 사용한다.
- **연결 판정의 범위:** Up이고 루프백·터널이 아니며 비링크로컬 IPv4/IPv6 주소가 있는 인터페이스를 찾는다. 인터페이스 목록 조회 실패 때는 이전처럼 조회한다. 이것은 인터넷·DNS·서비스 서버가 실제로 응답한다는 보장이 아니며, 주소를 가진 가상 어댑터도 통과할 수 있다.
- **유지한 동작:** 수동 Refresh와 절전 복귀는 즉시 조회한다. 계정 한도는 기본 6시간 / 잔여 0% 초과~10% 미만 1시간 / 정확한 0% 15분 / 리셋 전후 15분 구간 5분으로 Provider별 독립 판정한다. 시간표·공휴일 정책·색상·개인 기록·DB 구조·CLI 인증 방식은 바꾸지 않았다.
- **Tooltip:** Windows는 초 단위를 유지했다. 실제 NotifyIcon 설정과 432개 조합·STALE/공휴일 최장 사례의 127자 제한·초 단위 카운트다운을 검사했다. 장시간 실제 마우스 hover 가독성은 미수행이며, Mac의 Tooltip 문제만으로 Windows를 분 단위로 바꾸지 않았다.

오프라인일 때 건너뛰는 것은 **연결 복구 이벤트가 예약한 조회**다. 기존 정기 조회·수동 Refresh·절전 복귀까지 모두 중단하는 기능을 추가한 것은 아니므로 장기 오프라인에서 정기 조회가 실패할 수 있다. 조회 실패를 정상이나 회복으로 꾸며 표시하지 않는다.

## 변경 파일

기존 수정:

- AccountQuotaView.cs: ChatGPT 제목과 Work/Codex 행, 표시 범위 설명.
- StatusWindow.cs, TrayApplicationContext.cs: 화면·메뉴·Tooltip·알림·저장 피드백의 Windows 이름 적용.
- MeasurementDialog.cs, StatisticsWindow.cs: 대화상자 제목·통계의 화면 이름 적용.
- AccountQuotaUiChecks.cs, HolidayUiChecks.cs, TrayPresentationTests.cs, UIRegressionChecks.cs: 새 표시·배치·저장 식별자 불변 검사와 기존 UI 기대 이름 갱신.
- AiBurgerClock.csproj: 병합 후 Version 2.2.3, AssemblyVersion / FileVersion 2.2.3.0.
- README.md, CODE_GUIDE.md, BACKLOG.md: 현재 사용법·소스 지도·완료/보류 후보·검증 기록 연결.

신규:

- WindowsProviderNames.cs: Windows 전용 표시 변환. SharedSources.props에 추가하지 않았다.
- MAINTENANCE_2_2_3.md: 이 버전의 검증·배포 기록.

C# 지도는 루트·Properties 49개 + Mac 호스트 6개 + 공통 검사 입구 1개 = 총 56개다. 새 의존성·새 백그라운드 도구·공통 DLL·인증 파일 직접 읽기·비공개 usage API 호출은 추가하지 않았다.

## 빌드와 검사

Windows 11 Pro x64 10.0.26300 / .NET SDK 10.0.401에서 실행했다. WinExe의 종료 코드는 build.ps1의 **Start-Process -Wait -PassThru.ExitCode**로 확인했다. 로그와 가짜 응답 PNG는 로컬 artifacts/release-2.2.3/에 보존하며 GitHub에 올리지 않는다.

| 검증 | 결과 | 로그 폴더 |
|---|---|---|
| 기준 main Release build / self-test | 경고 0 / 오류 0, 250,771 assertions, exit 0, stderr 0바이트 | baseline/ |
| PR HEAD Release build / self-test | 경고 0 / 오류 0, 250,779 assertions, exit 0, stderr 0바이트 | pr-verified/ |
| 기준 main native smoke | 121 PASS, exit 0, stderr 0바이트 | baseline/ |
| PR HEAD native smoke | 136 PASS, exit 0, stderr 0바이트 | pr-verified/ |
| 최종 build.ps1 -Publish | 경고 0 / 오류 0, publish exit 0, 단일 EXE | publish/ |
| 최종 dist EXE self-test | 250,779 assertions, exit 0, stderr 0바이트 | publish/ |
| 최종 dist EXE native smoke | 136 PASS, exit 0, stderr 0바이트 | final/ |
| 배포 전 DB 보존 | 시작 전까지 백업과 SHA-256 동일, 활성 WAL/SHM 없음 | 백업 복사·해시 비교 |
| 일반 실행 | 기존 dist EXE 실행, Responding=True, FileVersion 2.2.3.0 | 2026-10-04 07:44 KST 실행 확인 |

### main 대비 assertion 수

| 검사 그룹 | main f172630 | PR HEAD / 2.2.3 | 차이 |
|---|---:|---:|---:|
| AutoStart | 229 | 229 | 0 |
| Schedule / DST | 242,363 | 242,363 | 0 |
| US holidays | 1,399 | 1,399 | 0 |
| TrayPresentation | 6,122 | 6,130 | +8 |
| ProviderStatus / HTTP | 96 | 96 | 0 |
| Monitor | 119 | 119 | 0 |
| SQLite / statistics | 132 | 132 | 0 |
| HolidayMonitor | 2 | 2 | 0 |
| quota schemas / CLI | 192 | 192 | 0 |
| QuotaMonitor | 80 | 80 | 0 |
| PortablePlatform | 37 | 37 | 0 |
| 전체 | **250,771** | **250,779** | **+8** |

줄어든 그룹은 없다. 추가 8건은 Windows 표시 이름 변환·저장 이름 유지·공통 기본값 불변·ChatGPT 알림 제목과 본문 보존이다. 공통 스케줄러의 타이밍·오프라인·연결 판정 검사는 main의 가짜 시계·인터페이스 자료를 그대로 검사했으며 실제 네트워크 시험으로 보고하지 않는다.

Native smoke는 상태 창·통계 5개 탭·한도 화면·메뉴·기록 대화상자·임시 DB 재오픈·Refresh·모의 Resume·전환/Provider 알림 요청·공휴일·종료를 포함한다. 추가 15건은 표시 이름, 알림, 메뉴·DB 식별자 불변, 통계 5개 탭, 한도 제목·배치·이전값·갱신중 검사다. BalloonTipShown은 15회였으나 모든 배너의 사용자 화면 노출을 보장하지 않는다.

기준 main과 PR HEAD의 PNG 10쌍은 크기가 모두 같았다. 상태·장애·공휴일 Provider 이름, 기록 대화상자 제목, 통계 이름, 한도 제목·한 줄 배치가 의도대로 달라졌다. Claude 소진만 표시한 account-quotas-exhausted.png는 바이트까지 같았다. PNG는 DrawToBitmap으로 생성한 **가짜 응답 테스트 화면**이지 실제 계정·바탕화면 캡처가 아니다. 최종 배포본 PNG는 final/ui/에 있다. 자동 시작 표시는 테스트 EXE 경로에 따라 '다른 경로'와 '현재 경로'가 달라질 수 있으며 등록값 변경을 뜻하지 않는다.

개발 중 추가한 이전 Codex 값 검사 fixture에 IsPrevious=true를 빠뜨려 첫 smoke가 실패했다. 해당 fixture를 바로잡고 최종 검사를 통과했으며 최초 로그는 development-fixture-failure/에 보존했다. 앱의 조회 로직 결함을 수정한 것은 아니다.

### 수행하지 않은 검증

- **실제 Windows 네트워크 전체 단절·재연결: 미수행.** 현재 실행 세션은 관리자 권한이 아니어서 어댑터 비활성화를 실행하지 않았다. 목록에는 실제 이더넷이 Up, Wi-Fi가 내려간 상태였고 Tailscale/가상 인터페이스는 당시 유효한 비링크로컬 주소가 없었다. Hyper-V vEthernet 이름의 어댑터는 보이지 않았다. 실제 끊긴 동안 Unknown 없음·재연결 약 5초 뒤 재조회를 확인했다고 주장하지 않는다.
- 실제 재부팅·로그인·절전, 장기간 사용, 15분·1시간·6시간 실시간 대기, 장시간 Tooltip hover: 미수행.
- --verify-autostart, 레지스트리 변경, 리셋권·크레딧·계정 소진 시험: 미수행.
- 별도 --check-quotas / --check-providers 실행: 미수행. 일반 앱 시작의 기존 자동 조회만 동작시켰으며 이번 작업이 새 CLI/서버의 계정 조회·비소비 보장을 검증한 것은 아니다.
- Mac build/native smoke와 백업 DB의 별도 SQLite quick_check: 미수행. 이번 PR은 Windows 전용이고 DB 백업은 앱 종료·WAL 없음·해시 일치로 확인했다.

## Git 기준점과 백업

| 기준 | 전체 커밋 해시 |
|---|---|
| 작업 전 main | `f17263050f945a7ec51201252ccc8ca94f9834d9` |
| 검증한 PR #18 HEAD | `4b3b617555b72f3af381f05a5b40d6f4331b38bf` |
| PR #18 main 병합 | `a87540aa9861ac4483b9ebb2f835a5e553c2b0fb` |
| 병합 후 Windows 버전 변경 | `7772fcbf2ed597ca83907a1ccbd03d68b6605079` |
| 최종 EXE 소스 | `7772fcbf2ed597ca83907a1ccbd03d68b6605079` |
| 교체한 2.2.2 EXE의 원래 소스 | `f456c6952829ee8646456cce3967a11035810930` |

PR #18은 2026-10-04 07:35 KST에 main으로 병합했다. 버전은 별도 커밋으로 올렸고 최종 EXE의 ProductVersion에 그 소스 해시를 확인했다. 이후 README·CODE_GUIDE·BACKLOG·이 기록의 변경은 문서뿐이다. 문서 커밋을 EXE 소스 커밋으로 바꿔 적거나 문서 때문에 재publish하지 않는다.

앱 프로세스가 없고 실제 DB의 WAL·SHM 파일도 없는 상태에서 ../backups/release-2.2.3-20261004/에 아래 자료를 보관했다. 교체된 EXE의 해시는 이전 dist와 같으며 DB 복사본은 최종 smoke 뒤 **일반 앱 시작 전까지** 실제 DB와 동일했다. 일반 앱 시작 뒤 조회 캐시는 기존대로 저장될 수 있다. 백업·개인 DB·실행 파일은 GitHub에 올리지 않는다.

| 로컬 백업 경로 | SHA-256 |
|---|---|
| `../backups/release-2.2.3-20261004/dist-2.2.2.zip` | `3AD0F0782E1C26C03004008E1F7D6EE512D302CE7BA73C17355EB1A505036C7A` |
| `../backups/release-2.2.3-20261004/replaced-2.2.2.exe` | `56250BAF25C192314B513C02977A2A40A1E1D72836C43B0CF20803C2F98E2A10` |
| `../backups/release-2.2.3-20261004/source-2.2.2-f456c69.zip` | `B48A3D853CD225E1839B0E1684C6F8A9F394493A2F2C0C7E394C4B33A1CC0C55` |
| `../backups/release-2.2.3-20261004/burgerclock.db` | `83ACCBDB2B659A071D07ECCD3876613B8CE62335E933ED3324C3BDF95D5DA6A0` |

복원할 때는 트레이에서 앱을 정상 종료하고 현재 DB를 별도로 보존한 뒤 replaced-2.2.2.exe를 기존 dist/win-x64/AI Burger Clock.exe 경로로 복사한다. 두 버전 모두 schema 2이므로 앱 버전만 되돌리면서 DB를 오래된 백업으로 덮어쓸 필요는 없다. DB도 되돌리려면 그 이후 기록이 사라지는 점을 확인하고 보존본을 먼저 만든다. 백업 폴더의 EXE에서 자동 시작을 새로 등록하지 않는다.

## 최종 실행 파일과 배포

- 경로: `dist/win-x64/AI Burger Clock.exe` (기존 작업공간의 AiBurgerClock 폴더, 자동 시작 경로 동일).
- FileVersion: **2.2.3.0**.
- ProductVersion: **`2.2.3+7772fcbf2ed597ca83907a1ccbd03d68b6605079`**.
- 크기: **3,040,491 bytes**.
- SHA-256: **`E166448B0BAD9822390F4A0A2EC23471497DA8083EACEC7336578B890C8F286D`**.
- framework-dependent 단일 EXE. .NET 10 Desktop Runtime x64 필요.

build.ps1 -Publish로 기존 dist를 교체하고 **그 파일**로 자체 검사와 native smoke를 통과했다. 일반 앱도 기존 경로에서 --autostart로 다시 실행했다. HKCU Run의 EXE 경로·인수와 StartupApproved 원본 바이트는 교체 전후 동일하며 레지스트리는 수정하지 않았다.

크레딧 처리, 한도 회복 알림, 리셋권 조회·사용, Gemini 개인 한도, 공개 자료 모니터링 설정, 3단계 기능은 이번 버전에 추가하지 않았다.
