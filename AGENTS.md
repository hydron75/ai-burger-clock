# 작업 안내

AI Burger Clock 저장소에서 코드를 고치는 사람과 에이전트를 위한 요약입니다. 사용법은 [README](README.md), 소스 구조는 [CODE_GUIDE](CODE_GUIDE.md)를 봅니다.

## 대상 환경

- 본체는 Windows 11 x64용 WinForms 앱입니다. `net10.0-windows`, `PlatformTarget` x64, `RuntimeIdentifier` win-x64.
- macOS 27 / Apple Silicon용 AppKit 메뉴바 앱은 `Mac/`의 별도 프로젝트입니다. `net10.0-macos27.0`, `osx-arm64`이며 공통 원본은 `Shared/SharedSources.props`로 링크합니다. 빌드와 검사는 [Mac/README](Mac/README.md)를 따릅니다.
- SDK는 `global.json` 기준 10.0.100 이상, 같은 10.0의 최신 기능 밴드를 허용합니다. 미리보기 SDK는 쓰지 않습니다.
- 직접 NuGet 의존성은 `Microsoft.Data.Sqlite 10.0.12` 하나입니다. 새 의존성은 추가하지 않는 것을 기본으로 합니다.
- 배포본은 framework-dependent 단일 EXE입니다. .NET 10 Desktop Runtime x64가 별도로 필요하며 trimming과 NativeAOT는 쓰지 않습니다.
- Linux·macOS에서는 `--self-test`·`--smoke-test`를 실행할 수 없습니다. 컴파일 확인은 `dotnet build -c Release -warnaserror -p:EnableWindowsTargeting=true`로 할 수 있습니다(Linux에서 확인).

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

## 검사

별도 테스트 프로젝트는 없습니다. 앱 EXE에 옵션을 붙여 실행합니다. 분기는 [Program.cs](Program.cs)에 있습니다.

| 옵션 | 하는 일 | 실제 환경에 미치는 영향 |
|---|---|---|
| `--self-test` | 시간표·DST·공휴일·Provider 파서·권고·SQLite·통계·자동 시작 판정·계정 한도(파서·조회 주기·CLI 프로토콜·재시도) 검사 | 임시 DB와 가짜 HTTP·CLI 응답만 사용. 사용자 DB·레지스트리·실제 계정은 건드리지 않음 |
| `--smoke-test` | 실제 WinForms 메시지 루프에서 트레이·창·버튼·알림 요청·기록·통계·한도 화면 연결 검사 | 테스트 창과 알림이 생길 수 있음. 앱이 실행 중이면 종료 코드 2로 거부 |
| `--smoke-test --verify-autostart` | 위 검사 + 자동 시작 UI | **실제 HKCU Run·StartupApproved 값을 잠시 바꾼 뒤 복원.** 개발 전용 |
| `--smoke-test --report-directory 경로` | 검사 중 폼을 PNG로 저장 | 지정 폴더에 이미지 생성 |
| `--check-providers` | OpenAI·Claude·Gemini 공식 상태를 지금 조회해 출력 | 실제 인터넷 사용. 사용자 DB에는 기록하지 않음 |
| `--check-quotas` | 로그인된 Codex·Claude CLI에서 계정 한도만 조회해 출력 | 실제 계정 조회. 모델 요청·리셋권 사용·사용자 DB 저장 없음. 한 Provider라도 실패하면 종료 코드 1 |

- 검사는 가짜 현재 시각을 주입합니다. 시스템 시계를 바꾸지 않습니다.
- 새 검사는 [SelfTest.cs](SelfTest.cs) 또는 [SmokeTest.cs](SmokeTest.cs)에서 호출되는 경로에 연결해야 실행됩니다. 예: `HolidayStorageTests`는 `StorageTests`를 거쳐 호출됩니다.
- 실행 명령 예시는 [CODE_GUIDE 13절](CODE_GUIDE.md#13-검사는-어떻게-실행하나요)에 있습니다.

## 작업 분담: Windows와 Mac

2026-10-03부터 두 버전을 다음과 같이 나눠 작업합니다.

| 대상 | 담당 | 범위 |
|---|---|---|
| Windows 버전 | ChatGPT | WinForms UI, `build.ps1`, Windows csproj, Windows 버전 기록 문서 |
| macOS 버전 | 사용자 Mac의 Claude Code | `Mac/` 폴더, Mac 버전(`Mac/AiBurgerClock.Mac.csproj`), [MACOS_PORT](MACOS_PORT.md) 기록 |

- macOS 버전은 PR #12로 2026-10-04 main에 병합됐습니다. 이후 Mac 작업도 main에서 새 브랜치를 만들어 PR로 올립니다. Mac 쪽 검증과 기록은 MACOS_PORT.md에 절을 이어 씁니다.
- 각 담당은 상대 버전 전용 파일을 고치지 않습니다.
- **공통 원본은 양쪽 모두 고칠 수 있습니다.** Windows와 Mac이 함께 컴파일하는 루트의 공통 C# 원본(Mac 브랜치의 `Shared/SharedSources.props` 목록)과 그 검사가 대상입니다. 고칠 때는 반드시 PR로 올립니다.
- 공통 원본을 고친 PR 본문에는 "공통 원본 변경" 절을 두어 바뀐 파일, 동작 변화, 상대 버전에서 확인할 항목을 적습니다.
- Mac 쪽에서 공통 원본을 고치면 `dotnet build -c Release -warnaserror -p:EnableWindowsTargeting=true`로 Windows 컴파일까지 확인합니다. 실제 Windows 검사는 위 "Windows 검증" 방식으로 ChatGPT가 진행합니다.
- Windows 쪽에서 공통 원본을 고치면 Mac 공통 검사·native smoke 확인을 PR에 요청 항목으로 적습니다. Mac 담당이 그 PR HEAD로 `Mac/build.sh`와 `--smoke-test`를 실행하고 결과를 PR 코멘트로 남깁니다.

## 작업 흐름: PR과 Windows 검증

Windows 빌드와 검사는 ChatGPT에서 진행합니다. 코드를 고치는 쪽은 PR을 올리고, ChatGPT에서 그 PR로 빌드와 검사를 합니다.

- **PR을 올리는 쪽:** 브랜치에 커밋·푸시하고 PR을 만듭니다. PR 본문에는 자기 환경에서 실제로 확인한 것만 적고, Windows에서 확인할 항목(새 검사, UI 변경 등)을 따로 적습니다.
- **Windows 검증:** ChatGPT에서 PR HEAD로 `build.ps1`, `--self-test`, `--smoke-test` 등을 실행하고 결과를 PR 코멘트로 남깁니다. 후속 수정은 이 결과를 기준으로 판단합니다.
- 아래 버전 기록은 Windows 검증 결과(SDK 버전, assertion 수, 최종 EXE 해시 등)가 필요하므로 PR을 올리는 단계에서는 하지 않습니다.

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
