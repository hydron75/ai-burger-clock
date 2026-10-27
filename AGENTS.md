# 작업 안내

AI Burger Clock 저장소에서 코드를 고치는 사람과 에이전트를 위한 요약입니다. 사용법은 [README](README.md), 소스 구조는 [CODE_GUIDE](CODE_GUIDE.md)를 봅니다.

## 대상 환경

- 본체는 Windows 11 x64용 WinForms 앱입니다. `net10.0-windows`, `PlatformTarget` x64, `RuntimeIdentifier` win-x64.
- macOS 27 / Apple Silicon용 AppKit 메뉴바 앱은 `Mac/`의 별도 프로젝트입니다. `net10.0-macos27.0`, `osx-arm64`이며 공통 원본은 `Shared/SharedSources.props`로 링크합니다. 빌드와 검사는 [Mac/README](Mac/README.md)를 따릅니다.
- SDK는 `global.json` 기준 10.0.100 이상, 같은 10.0의 최신 기능 밴드를 허용합니다. 미리보기 SDK는 쓰지 않습니다.
- 직접 NuGet 의존성은 `Microsoft.Data.Sqlite 10.0.12` 하나입니다. 새 의존성은 추가하지 않는 것을 기본으로 합니다.
- 배포본은 framework-dependent 단일 EXE입니다. .NET 10 Desktop Runtime x64가 별도로 필요하며 trimming과 NativeAOT는 쓰지 않습니다.
- Windows EXE의 `--self-test`·`--smoke-test`는 Linux·macOS에서 실행할 수 없습니다. Mac 앱의 `--smoke-test`는 macOS에서 실행합니다([Mac/README](Mac/README.md)). 컴파일 확인은 `dotnet build -c Release -warnaserror -p:EnableWindowsTargeting=true`로 할 수 있습니다(Linux에서 확인).

## 빌드

프로젝트 폴더의 PowerShell에서 실행합니다.

~~~powershell
.\build.ps1            # Release 빌드 + --self-test
.\build.ps1 -Publish   # 위 과정 + dist\win-x64\AI Burger Clock.exe 단일 EXE
~~~

- `dotnet build`와 `dotnet publish`에 모두 `-warnaserror`를 붙입니다. 경고 하나도 빌드 실패입니다.
- 빌드 뒤 결과 EXE로 `--self-test`를 실행합니다. 출력은 `artifacts\self-test.txt`, 오류는 `artifacts\self-test-errors.txt`에 남고, 종료 코드가 0이 아니면 실패합니다.
- WinExe는 `$LASTEXITCODE`가 신뢰할 수 없으므로 `Start-Process -Wait -PassThru`로 실제 프로세스의 `ExitCode`를 봅니다.
- `-Publish`는 `dist`의 EXE를 덮어씁니다. 실행 중인 앱을 먼저 종료합니다.
- `bin/`, `obj/`, `dist/`, `artifacts/`, `*.db`는 `.gitignore` 대상입니다. 커밋하지 않습니다.
- **Mac 빌드:** 저장소 경로에서 `dotnet build Mac/AiBurgerClock.Mac.csproj`를 직접 실행하지 않습니다. 같은 bundle(`Mac/bin/Release/…`)로 실행 중인 앱의 디스크 번들이 덮어써집니다. Mac 컴파일·빌드·`--smoke-test` 확인은 임시 사본(`git worktree`·`rsync`)에서 `Mac/build.sh`로 하고, 실제로 쓸 앱만 사용자 허락 뒤 저장소 경로에서 `Mac/build.sh`(실행 중이면 `--quit-running`)로 빌드합니다. 자세한 내용은 [Mac/README](Mac/README.md)의 "실행 중인 앱이 있을 때"를 봅니다.
- **Mac smoke 실행 횟수(2026-10-22):** `--smoke-test`는 상태 항목·팝오버·통계 창을 열어 사용자의 화면에 나타납니다. 전체 화면 앱 위에서 반복하면 메뉴바 표시가 남아 로그아웃으로만 풀린 적이 있습니다([MACOS_PORT 39절](MACOS_PORT.md#39-메뉴바-강제-표시-증상-원인-미확정-재부팅-후-해소)). 한 작업에서 마지막 확인 1~2회만 실행하고, 반복 디버깅은 Shared.Tests·컴파일처럼 앱을 띄우지 않는 검사로 합니다.
- **임시 사본 LaunchServices 등록(2026-10-26 측정):** `Mac/build.sh` 빌드만으로는 같은 번들 ID(`com.hydron75.aiburgerclock`) 등록이 늘지 않고, 임시 사본의 앱을 실행(`--smoke-test`)하면 그 사본 경로가 등록됩니다(측정: 빌드만 1→1, smoke 뒤 1→2). 등록이 쌓이면 알림 왼쪽 아이콘이 옛 것으로 남을 수 있으므로, 임시 사본은 지우기 전에 `/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "<사본>/Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"`로 해제합니다. 등록 수는 `lsregister -dump | grep -c 'identifier:.*com.hydron75.aiburgerclock'`로 세며 정상은 1입니다.

## 검사

Windows 앱 검사는 앱 EXE에 옵션을 붙여 실행합니다. 분기는 [Program.cs](Program.cs)에 있습니다. 공통 원본의 검사는 OS와 무관한 `net10.0` 프로젝트 [Shared.Tests](Shared.Tests/Program.cs)로도 실행합니다(`dotnet run --project Shared.Tests/AiBurgerClock.Shared.Tests.csproj -c Release --property:TreatWarningsAsErrors=true`). Mac 앱 검사는 [Mac/README](Mac/README.md)의 `--smoke-test`를 따릅니다.

| 옵션 | 하는 일 | 실제 환경에 미치는 영향 |
|---|---|---|
| `--self-test` | 시간표·DST·공휴일·Provider 파서·권고·SQLite·통계·자동 시작 판정·계정 한도(파서·조회 주기·CLI 프로토콜·재시도) 검사 | 임시 DB와 가짜 HTTP·CLI 응답만 사용. 사용자 DB·레지스트리·실제 계정은 건드리지 않음 |
| `--smoke-test` | 실제 WinForms 메시지 루프에서 트레이·창·버튼·알림 요청·기록·통계·한도 화면 연결 검사 | 테스트 창과 알림이 생길 수 있음. 앱이 실행 중이면 종료 코드 2로 거부 |
| `--smoke-test --verify-autostart` | 위 검사 + 자동 시작 UI | **실제 HKCU Run·StartupApproved 값을 잠시 바꾼 뒤 복원.** 개발 전용 |
| `--smoke-test --report-directory 경로` | 검사 중 폼을 PNG로 저장 | 지정 폴더에 이미지 생성 |
| `--check-providers` | OpenAI·Claude·Gemini·xAI 공식 상태를 지금 조회해 출력 | 실제 인터넷 사용. 사용자 DB에는 기록하지 않음 |
| `--check-quotas` | 로그인된 Codex·Claude CLI에서 계정 한도만 조회해 출력 | 실제 계정 조회. 모델 요청·리셋권 사용·사용자 DB 저장 없음. 한 Provider라도 실패하면 종료 코드 1 |

- 검사는 가짜 현재 시각을 주입합니다. 시스템 시계를 바꾸지 않습니다.
- 공통 검사 실행 목록은 [SharedTestSuite.cs](SharedTestSuite.cs) 한 곳에서 관리합니다. [Shared.Tests](Shared.Tests/Program.cs)와 Windows [SelfTest.cs](SelfTest.cs)는 `SharedTestSuite.RunAllAsync()`를 각각 한 번만 호출하며, 공통 검사를 개별 호출하지 않습니다.
- 새 공통 검사는 루트의 `*Tests.cs` 파일로 만들고 `SharedTestSuite` 목록에만 등록합니다. 두 호스트 모두 이 파일들을 자동으로 컴파일합니다. 이미 다른 검사에서 호출하는 하위 검사는 다시 등록하지 않습니다. 예: `HolidayStorageTests`는 `StorageTests`를 거쳐 호출됩니다.
- Windows 전용 검사는 `SelfTest`에서 별도로 실행하고 `Shared.Tests/AiBurgerClock.Shared.Tests.csproj`의 공통 검사 컴파일 목록에서는 제외합니다. `SharedTestSuite`는 검사 전용 원본이므로 앱의 공통 원본 목록(`Shared/SharedSources.props`)에는 넣지 않습니다. 네이티브 UI 검사는 [SmokeTest.cs](SmokeTest.cs)에서 호출되는 경로에 연결합니다.
- 실행 명령 예시는 [CODE_GUIDE 13절](CODE_GUIDE.md#13-검사는-어떻게-실행하나요)에 있습니다.

## 작업 분담: Windows와 Mac

2026-10-03부터 두 버전을 다음과 같이 나눠 작업합니다.

| 대상 | 담당 | 범위 |
|---|---|---|
| Windows 버전 | ChatGPT | WinForms UI, `build.ps1`, Windows csproj, Windows 버전 기록 문서 |
| macOS 버전 | 사용자 Mac의 Claude Code | `Mac/` 폴더, Mac 버전(`Mac/AiBurgerClock.Mac.csproj`), [MACOS_PORT](MACOS_PORT.md) 기록 |

- macOS 버전은 PR #12로 2026-10-04 main에 병합됐습니다. 이후 Mac 작업도 main에서 새 브랜치를 만들어 PR로 올립니다. Mac 쪽 검증과 기록은 MACOS_PORT.md에 절을 이어 씁니다.
- 각 담당은 상대 버전 전용 파일을 고치지 않습니다.
- **공통 원본은 양쪽 모두 고칠 수 있습니다.** Windows와 Mac이 함께 컴파일하는 루트의 공통 C# 원본(`Shared/SharedSources.props` 목록)과 그 검사가 대상입니다. 고칠 때는 반드시 PR로 올립니다.
- 공통 원본을 고친 PR 본문에는 "공통 원본 변경" 절을 두어 바뀐 파일, 동작 변화, 상대 버전에서 확인할 항목을 적습니다.
- Mac 쪽에서 공통 원본을 고치면 `dotnet build -c Release -warnaserror -p:EnableWindowsTargeting=true`로 Windows 컴파일까지 확인합니다. 실제 Windows 검사는 위 "Windows 검증" 방식으로 ChatGPT가 진행합니다.
- Windows 쪽에서 공통 원본을 고치면 Mac 공통 검사·native smoke 확인을 PR에 요청 항목으로 적습니다. Mac 담당이 그 PR HEAD로 `Mac/build.sh`와 `--smoke-test`를 실행하고 결과를 PR 코멘트로 남깁니다.
- **분리 원칙(2026-10-08):** 필수 로직은 공통 원본에 두고, UI는 각 OS 네이티브(Windows WinForms, Mac AppKit)로 만듭니다. 패널 본문 문구와 표시 판정(톤)도 공통에 둡니다. 색·글꼴·레이아웃·클릭 동작·자동 실행 문구·메뉴 구성은 각 OS가 정합니다. 상세: [MACOS_UI_PLAN](MACOS_UI_PLAN.md).
  - **흐름 변경 규칙(2026-10-09):** 기록·공휴일·시작·종료 흐름을 바꾸는 PR은 상대 호스트 확인 항목을 반드시 적습니다. 이 흐름은 아직 두 호스트(`TrayApplicationContext`, `MacApplication`)에 따로 있어 한쪽만 고쳐질 수 있기 때문입니다(MACOS_UI_PLAN 4-4).
  - **메뉴 항목 예외(2026-10-08, 범위 명확화 2026-10-18):** 상태 창·팝오버의 **Provider 기록 메뉴**처럼 두 OS가 같은 항목을 같은 순서로 보여 줘야 하는 메뉴는 항목의 종류·순서·문구를 공통(`UsageMeasurementFactory.MenuItems` 등)에 둡니다. 메뉴를 만들고 띄우는 코드·단축키·위치·열리는 방식은 각 OS가 맡습니다.
  - **트레이·메뉴바 메뉴(2026-10-18):** Windows 트레이 메뉴와 Mac 메뉴바(오른쪽 클릭) 메뉴의 **구성과 순서는 각 OS가 정하고, 문구 상수(`QuotaRecoveryText.MenuLabel` 등)만 공통**에 둡니다. 새 항목을 더할 때는 공통 문구 상수를 쓰고, 양쪽 호스트가 항목을 따로 만들어도 됩니다. 항목의 의미·기본값(예: 기본 꺼짐)은 PR 본문에 양쪽 확인 항목으로 적습니다.
- **개선 전달:** Windows·Mac 구분 없이 먼저 개선안을 낸 쪽이 그 개선을 공통 원본 PR로 올립니다. 상대 담당은 자기 UI에 맞춰 반영하고, 반영할 수 없는 부분은 이유와 대안을 PR 코멘트로 회신합니다(재검토 회신).
- Mac 버전은 `Mac/AiBurgerClock.Mac.csproj`에서만 바꿉니다. `Version`·`AssemblyVersion`·`FileVersion`·`ApplicationVersion`을 함께 바꿉니다. `AssemblyVersion`은 공식 상태 조회 User-Agent와 CLI 클라이언트 버전에 쓰입니다. `Info.plist`에는 적지 않습니다([Mac/README](Mac/README.md)).

## 작업 흐름: PR과 Windows 검증

Windows 빌드와 검사는 ChatGPT에서 진행합니다. 코드를 고치는 쪽은 PR을 올리고, ChatGPT에서 그 PR로 빌드와 검사를 합니다.

- **PR을 올리는 쪽:** 브랜치에 커밋·푸시하고 PR을 만듭니다. PR 본문에는 자기 환경에서 실제로 확인한 것만 적고, Windows에서 확인할 항목(새 검사, UI 변경 등)을 따로 적습니다.
- **Windows 검증:** ChatGPT에서 PR HEAD로 `build.ps1`, `--self-test`, `--smoke-test` 등을 실행하고 결과를 PR 코멘트로 남깁니다. 후속 수정은 이 결과를 기준으로 판단합니다.
- 아래 버전 기록은 Windows 검증 결과(SDK 버전, assertion 수, 최종 EXE 해시 등)가 필요하므로 PR을 올리는 단계에서는 하지 않습니다.

### PR 확인 범위

| 변경 범위 | 병합 전 확인 |
|---|---|
| 공통 코드 변경 | Windows(`build.ps1`)와 Mac(`Mac/build.sh`) 양쪽에서 빌드·검사 |
| Mac UI만 변경 | Mac에서 빌드·검사 |
| Windows UI만 변경 | Windows에서 빌드·검사 |
| 문서만 변경 | 내용 검토 |

- PR 본문에 위 범위 중 어디에 해당하는지와 실제로 실행한 확인 항목을 적습니다.
- 클라우드처럼 빌드할 수 없는 환경에서 만든 PR은 "빌드·검사 미실행"이라고 명시합니다.

## Windows 배포

배포 준비와 실제 교체·운영 실행은 사용자가 승인한 범위를 구분합니다. **운영 앱 실행·재실행과 운영 한도 알림 등록·해제는 사용자가 Codex 밖 PowerShell에서 실제 배포 EXE로 수행합니다.** Codex 셸·배포 도우미에서 운영 앱을 직접 시작하거나 운영 등록을 대신 실행하지 않습니다. 테스트 빌드는 기존 고정 before/after 슬롯을 사용하며 운영 등록을 덮어쓰지 않습니다.

1. **Codex의 준비:** 승인된 범위에서 후보 빌드·검사·해시 확인과 배포 문서를 준비하고, 사용자가 외부에서 실행할 정확한 경로·명령·정상 기대값을 제공합니다. 배포 준비만 승인된 경우 EXE 교체·운영 실행·운영 등록을 진행하지 않습니다.
2. **외부 종료·백업 확인:** 사용자가 운영 앱을 정상 종료하고, Codex 밖에서 실제 운영 프로세스 부재와 기본 데이터 폴더의 DB·WAL/SHM 상태를 확인합니다. 기존 승인된 백업 절차로 이전 EXE·DB·로그를 보존합니다. Codex 파일 뷰의 요청 경로만으로 실제 사용자 DB를 확인했다고 하지 않으며 활성 WAL을 임의로 삭제하지 않습니다.
3. **승인된 EXE 교체:** 검증한 후보를 같은 실제 배포 경로에 교체하고 버전·소스 커밋·SHA-256을 대조합니다. 문제가 있으면 기록한 계획대로 이전 EXE를 복원하며 DB·캐시를 과거 백업으로 자동 복원하지 않습니다.
4. **외부 알림 등록 확인:** 사용자가 외부에서 실제 `%APPDATA%` 시작 메뉴 바로가기의 대상·AUMID·CLSID와 실제 HKCU의 AUMID·COM 실행 대상을 확인합니다. 배포 경로가 같고 등록이 정상이면 재등록하지 않습니다. 등록·해제가 필요할 때만 승인된 명령을 실제 배포 EXE로 실행하고, 해제 dry-run과 실제 해제를 구분합니다. 알림 등록과 자동 시작 등록은 별개입니다.
5. **외부 운영 실행:** 사용자가 Codex 밖 PowerShell 또는 탐색기에서 실제 배포 EXE를 정상 실행합니다. 기존 정상 인스턴스가 남아 있으면 단일 인스턴스 처리로 외부 실행이 종료될 수 있으므로 먼저 확인합니다. 정상 시작은 새 조회·캐시 쓰기·실제 알림을 일으킬 수 있으며 읽기 전용 검사로 취급하지 않습니다.
6. **실제 경로·화면 확인:** 외부 컨텍스트에서 단일 프로세스·버전·해시·실제 HKCU 자동 시작 대상을 확인하고, 기본 데이터 폴더의 DB/로그가 갱신되는지 확인합니다. 트레이·상태 창·한도 화면과 필요한 알림 확인은 사용자 근거로 구분합니다. Codex가 시작한 프로세스의 응답 성공만으로 외부 실행·기본 DB 사용·Codex 종료 후 상주를 확인했다고 하지 않습니다.
7. **기록:** 배포 문서에 실행·등록의 주체와 컨텍스트, 요청 경로와 실제 데이터 경로, 등록 대상 검사와 실제 알림 노출·센터 보관의 확인 여부를 각각 적습니다. 실제 계정 값·DB·원문 로그는 공개하지 않습니다.

**운영 데이터 경로 결정(2026-10-23):** `%LOCALAPPDATA%\AIBurgerClock`를 운영 경로로 계속 사용합니다. 기본 폴더와 `%LOCALAPPDATA%\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\AIBurgerClock`의 DB는 둘 다 보존하고, LocalCache 쪽은 백업으로만 둡니다. 별도 요청 없이 이관·병합·삭제하거나 운영 경로로 다시 선택하지 않습니다. 작업 디렉터리 변경만으로 데이터 경로가 선택되는 것은 아니며, AppData 가상화·리디렉션 가능성이 있으면 외부 사용자 컨텍스트에서 실제 경로를 확인합니다.

## 공개 저장소: 계정 정보 보호

- 이 저장소는 공개 상태입니다. Windows·Mac 담당 모두 PR 본문·코멘트·문서와 첨부 이미지·공개 로그에 **실제 계정의 요금제, 사용률·남은 비율, 리셋·복원 시각, 식별자(계정 ID·이메일 등)를 적지 않습니다.**
- 표시 예시와 렌더 이미지는 실제 계정과 무관한 합성 데이터를 사용하고, **합성 데이터임을 명시합니다.** 실제 계정 화면 캡처나 CLI 원본 출력을 그대로 게시하지 않습니다.
- 실제 조회 검증은 성공/실패, 응답 형식·조회 범위 등 계정 정보가 드러나지 않는 일반화한 결과로 기록합니다. 게시 전 본문·댓글·문서·이미지·로그를 확인하고, 오류 출력 등에 포함된 계정 정보도 제외합니다.

## 작업을 마칠 때: 버전 기록

지금까지의 작업(2.0.0, 2.0.1, 2.0.2, 2.1.0, 2.1.1, 2.1.2, 2.2.0, 2.2.1)은 다음 방식으로 기록했습니다.

1. **버전 올리기:** [AiBurgerClock.csproj](AiBurgerClock.csproj)의 `Version`, `AssemblyVersion`, `FileVersion`을 함께 바꿉니다. 버전은 이 파일에서만 관리합니다.
2. **작업 기록 문서 만들기:** 루트에 작업별 Markdown을 새로 만듭니다. 예: [PHASE2.md](PHASE2.md)(2.0.0), [STATUS_LINKS.md](STATUS_LINKS.md)(2.0.1), [AUTOSTART_FIX.md](AUTOSTART_FIX.md)(2.0.2), [HOLIDAYS_TRAY.md](HOLIDAYS_TRAY.md)(2.1.0), [RELIABILITY_FIX.md](RELIABILITY_FIX.md)(2.1.1), [MAINTENANCE_2_1_2.md](MAINTENANCE_2_1_2.md)(2.1.2), [ACCOUNT_QUOTAS.md](ACCOUNT_QUOTAS.md)(2.2.0), [MAINTENANCE_2_2_1.md](MAINTENANCE_2_2_1.md)(2.2.1). 제목에 버전을 적고 다음을 담습니다.
   - 동작 또는 수정 범위, 원인.
   - 변경 파일: 기존 수정 / 신규로 나눈 목록.
   - 검증 결과: SDK 버전, 경고·오류 수, 자체 검사 assertion 수와 종료 코드, smoke 결과, 로그 위치(`artifacts/<작업명>/`).
   - 수행하지 않은 검증: 실제 재부팅·절전 복귀·알림 노출 등 확인하지 못한 것을 명시합니다.
   - 기준점과 복원: “Git 기준점과 백업” 절에 다음을 적습니다.
     - Git 기준점 표: 작업 전 main, 검증한 PR HEAD, PR 병합, 버전 변경(별도 커밋이면), 최종 EXE의 소스 커밋. 전체 커밋 해시로 적습니다.
     - 로컬 백업: 앱 종료와 DB의 활성 WAL 없음을 확인한 뒤 `../backups/release-<버전>-<날짜>/`에 이전 dist ZIP, DB, 소스 ZIP, 교체된 EXE를 두고 경로와 SHA-256을 적습니다. 백업은 GitHub에 올리지 않습니다.
     - 이전 버전으로 되돌리는 방법.
   - 최종 EXE: 경로, FileVersion, ProductVersion(`+커밋 해시` 포함), 크기, SHA-256.
3. **README 갱신:** 제목의 버전, 상단의 “최신 변경과 검사 결과” 링크, 사용법 변경, 끝의 “더 자세히 보기” 목록에 새 문서를 반영합니다.
4. **CODE_GUIDE 갱신:** 상단의 “N.N.N 기준” 표기와 소스 파일 지도(파일 수 포함)를 맞춥니다.
5. **BACKLOG 갱신:** 반영한 항목의 상태를 “N.N.N에 반영”으로 바꾸고 기록 문서를 가리킵니다. 보류한 항목은 이유와 함께 남깁니다.

2.1.0까지의 기록은 Git 도입 전이라 백업 ZIP(`../backups/…`)만으로 기준점을 남겼습니다. 2.1.1부터는 위와 같이 Git 커밋을 기준점으로 적고 로컬 백업을 함께 남깁니다. 예: [RELIABILITY_FIX.md](RELIABILITY_FIX.md)(2.1.1), [MAINTENANCE_2_1_2.md](MAINTENANCE_2_1_2.md)(2.1.2)의 “Git 기준점과 백업” 절.
