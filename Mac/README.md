# AI Burger Clock · macOS preview 0.1.0

Apple Silicon / macOS 27을 위한 C# AppKit 메뉴바 앱입니다. Windows 2.2.2 배포본과 별도 앱이지만, 시간표·공식 상태·권고·한도 조회·기록·통계 계산 코드는 같은 저장소의 원본 소스를 링크해서 사용합니다. Windows 코드를 복사해 따로 유지하지 않습니다.

이 폴더는 **일반 사용 검증을 진행 중인 preview 소스**입니다. 2026-10-03 KST 실제 Mac에서 공통 검사 244,347건, Release `.app` 생성, 로컬 서명 검사와 ARM64 SQLite 포함에 이어 native smoke도 종료 코드 0으로 통과했습니다. 일반 사용 화면·알림 노출·로그인 자동 실행·절전/네트워크 복구·Mac의 Codex/Claude 계정 조회는 아직 별도 확인이 필요합니다. 임시 DB 실행 검사를 실제 계정 사용 성공으로 간주하지 않습니다. [native smoke 기록](../MACOS_PORT.md#13-실제-mac-native-smoke-통과)

## 들어 있는 기능

- 메뉴바의 F/B 아이콘과 공식 상태에 따른 색상, KST 카운트다운.
- 미국 ET 09:00~PT 18:00 업무시간, DST, 미국 연방 공휴일 보정. Windows와 같은 정책입니다.
- OpenAI·Claude·Gemini 공식 상태와 Provider별 독립 권고. 클릭하면 기본 브라우저에서 공식 상태 페이지를 엽니다.
- ChatGPT 제목 아래 Work/Codex 한도, Claude 한도. 설치·로그인된 공식 CLI가 조회하며 앱은 토큰·Keychain·쿠키를 읽지 않습니다. Gemini **개인 계정 한도**는 포함하지 않습니다.
- 기본 6시간, 잔여 0% 초과~10% 미만 1시간, 정확히 0%는 15분, 리셋 전후 각 15분은 5분 조회. 조건이 겹치면 짧은 주기가 적용됩니다. 예정 리셋 시각 경과만으로 100% 회복을 가정하지 않습니다.
- 수동 Refresh, 절전 복귀 조회, 연결 복구 조회(네트워크 이벤트는 1분 제한), 정상 종료 시 작업 취소.
- 메뉴바 또는 Provider의 기록 메뉴에서 Success·Slow·Error·Interrupted를 기록. 메모는 선택 사항입니다.
- Statistics의 최근 7일·30일·전체 및 Provider·KST 시간대·Schedule/DST·공식 상태별 비교. n·No data·소표본을 표시합니다.
- macOS 네이티브 Schedule/공식 상태 변화 알림과 로그인 자동 실행 옵션. **계정 한도 리셋/회복 알림과 크레딧은 구현하지 않았습니다.**

## Mac에서 빌드

먼저 현재 설치 상태를 확인하세요. 빌드 스크립트는 SDK·workload·Xcode·CLI를 설치하거나 로그인하지 않습니다.

```sh
sw_vers
uname -m
xcodebuild -version
command -v dotnet
```

기준 도구는 .NET SDK **10.0.401**, Xcode **27.0**, macos workload set **10.0.401.1**입니다. [Microsoft 공식 Xcode 27 지원 릴리스](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.0-10722)에 맞춘 조합입니다. 프로젝트는 `net10.0-macos27.0` / `osx-arm64`를 대상으로 합니다.

`dotnet`을 찾지 못한다면 [.NET SDK 10.0.401 macOS Arm64 설치 파일](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-10.0.401-macos-arm64-installer)을 수동 설치하세요. Runtime만 설치하거나 x64 버전을 선택하지 않습니다. 설치 후 새 터미널에서 버전을 확인합니다.

```sh
dotnet --version
```

`10.0.401`이 확인되면 macOS 앱 빌드에 필요한 workload만 설치합니다. 이 명령은 개발 도구를 설치하며 계정 로그인이나 앱 자동 실행 설정을 바꾸지 않습니다. 시스템에 설치한 SDK는 관리자 권한이 필요할 수 있어 `sudo`를 사용합니다.

```sh
sudo dotnet workload install macos --version 10.0.401.1
dotnet workload list
```

Xcode가 설치되어 있지만 개발 도구 경로·라이선스 오류가 나오면 실제 메시지를 확인한 뒤 처리합니다. 이 앱의 스크립트가 Xcode 설정을 임의 변경하거나 라이선스를 대신 승인하지 않습니다.

소스가 아직 없으면 preview 브랜치를 받습니다. 이미 저장소가 있다면 변경사항을 보존하고 해당 브랜치로 이동하세요.

```sh
git clone --branch feature/macos-native https://github.com/hydron75/ai-burger-clock.git
cd ai-burger-clock
bash Mac/build.sh
```

스크립트는 공통 코드의 가짜 응답/임시 DB 검사를 먼저 실행하고, 경고를 오류로 취급해 네이티브 Release를 빌드합니다. 이후 bundle의 서명 검증과 SQLite 네이티브 라이브러리 포함 여부를 확인합니다. 앱·계정 조회를 자동 실행하지는 않습니다.

앱 빌드는 **sudo 없이** 실행합니다. 기본 NuGet HTTP 캐시는 저장소의 `artifacts/mac-build/nuget-http-cache`에 두고 Git에서는 제외합니다. 사용자가 `NUGET_HTTP_CACHE_PATH`를 지정했다면 그 경로를 그대로 사용합니다. 취약성 검사·패키지 저장소 설정·전역 패키지 캐시는 변경하지 않습니다.

### NU1900 / HTTP 캐시 접근 거부

첫 Mac 빌드에서 사용자 홈의 NuGet HTTP 캐시에 접근하지 못해 취약성 데이터 조회가 실패한 사례가 있습니다. 관리자 권한으로 개발 도구를 설치하면서 캐시 소유권이 달라졌을 가능성이 있지만, 소유권을 확인하기 전에는 확정할 수 없습니다.

최신 preview를 받아 일반 사용자로 다시 빌드하세요. 저장소 루트에서 실행합니다.

```sh
git pull --ff-only
bash Mac/build.sh
```

업데이트 전에 한 번만 캐시 경로를 지정해서 실행할 수도 있습니다.

```sh
NUGET_HTTP_CACHE_PATH="$PWD/artifacts/mac-build/nuget-http-cache" bash Mac/build.sh
```

이 방법은 기존 캐시를 삭제하거나 권한을 바꾸지 않습니다. `NuGetAudit=false`·경고 무시·`sudo bash Mac/build.sh`로 문제를 숨기지 않습니다. 접근 거부가 다른 경로에서도 나오면 해당 오류와 경로를 확인한 뒤 별도로 진단합니다. 2026-10-03 KST 재시도에서 공통 검사 244,338건이 통과해 이 캐시 문제 해결은 확인했습니다. [NuGet 캐시 경로 공식 안내](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders)

### IL2026 / 한도 캐시 JSON

공통 검사 이후 `UsageStore.cs`의 JSON 저장·읽기에서 IL2026이 나오면 최신 `feature/macos-native` 소스를 받아 위와 같이 다시 빌드하세요. Apple SDK의 trimming 검사에서 런타임 reflection 기반 직렬화를 경고하는 문제였으며, `QuotaJsonContext`로 타입 정보를 미리 생성하도록 수정했습니다. JSON 형식·기존 캐시·DB schema 2는 유지하고, 경고 억제나 새 NuGet 패키지는 추가하지 않았습니다. [Microsoft source generation 안내](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)

수정 후 Windows의 Release/회귀·공통 검사, Mac 참조 코드의 trimming 분석과 reflection 비활성화 캐시 검사를 통과했습니다. 이어서 소스 `302aee8`의 **실제 Mac `.app` 빌드·서명 검사와 native smoke까지 통과**했습니다. 일반 사용·실제 계정 조회는 아직 확인 전입니다. [정확한 검증 범위](../MACOS_PORT.md#13-실제-mac-native-smoke-통과).

### 빌드 결과

빌드하면 기본적으로 다음 bundle이 생성됩니다.

```text
Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app
```

Finder나 `open`으로 `.app`를 실행합니다. 사용 중인 앱을 덮어쓰지 말고 종료한 뒤 교체하세요. .NET 실행 환경은 bundle에 포함하며 WebView·MAUI·Avalonia·Electron은 사용하지 않습니다. 직접 NuGet 의존성은 `Microsoft.Data.Sqlite 10.0.12` 하나입니다. Apple SDK의 필수 trimmer pipeline은 `TrimMode=copy`로 코드 제거 없이 동작합니다.

로컬 사용용 ad-hoc 서명만 설정되어 있습니다. Apple Developer 배포 서명·notarization·설치 프로그램·자동 업데이트는 포함하지 않습니다. 다른 Mac에 배포할 때의 Gatekeeper 처리와 정식 서명은 별도 과제입니다.

## 데이터와 자동 실행

DB는 아래 Mac 전용 위치에 생성됩니다. Windows의 기존 DB·레지스트리·배포 경로는 건드리지 않습니다.

```text
~/Library/Application Support/AIBurgerClock/burgerclock.db
```

공휴일 옵션과 정규화한 최근 한도 캐시는 기존 schema 2의 metadata에 저장됩니다. Prompt·AI 답변·인증 정보는 저장하지 않습니다. 로그인 자동 실행을 켜기 전에 `.app`를 `/Applications` 등 고정된 위치에 두세요. macOS의 `SMAppService.MainApp`로 등록하고, 승인이 필요하면 시스템 설정 → 일반 → 로그인 항목에서 확인합니다. 다른 경로로 앱을 옮겼으면 해당 설정을 다시 확인하세요.

알림은 최초 실행 시 macOS 권한을 요청합니다. 허용하지 않아도 시간표와 조회는 계속 동작합니다. 시스템 설정 → 알림에서 AI Burger Clock을 관리할 수 있습니다. 집중 모드·알림 정책으로 배너가 보이지 않을 수 있습니다.

Mac에서 사용하는 Codex와 Claude는 **그 Mac에 별도로 설치·로그인되어 있어야** 합니다. Windows의 로그인 상태는 자동 복사하지 않습니다. CLI가 없거나 조회에 실패하면 앱은 이전값/조회 불가를 표시하고 다른 기능은 유지합니다.

## 안전한 네이티브 검사

빌드된 bundle의 실행 파일을 직접 실행합니다.

이미 `bash Mac/build.sh`가 성공한 경우 재빌드 없이 아래 검사로 진행합니다. 저장소 루트에서 실행하세요.

```sh
"Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app/Contents/MacOS/AI Burger Clock" --smoke-test
echo "검사 종료 코드: $?"
```

메뉴바·창을 만들고 상태/통계 창의 닫기·재열기·Visible 상태를 확인합니다. 새 임시 DB에서 4종 실측/메모·표본수/No data·한도 카운트다운 표시를 검사한 뒤 종료합니다. 실제 계정 조회·HTTP·브라우저 열기·알림 권한 요청·자동 실행 변경을 하지 않습니다. 이 검사가 통과해도 실제 OS 알림, 로그인 항목 등록, 계정별 CLI 응답 및 장시간 절전 복귀는 별도 검증 대상입니다.

성공 기준은 `PASS: native controls/window close-reopen...` 출력과 종료 코드 `0`입니다. 오류가 있으면 출력과 종료 코드를 보존하고 일반 실행·자동 시작 설정 전에 원인을 확인합니다.

2026-10-03 KST 사용자 Mac에서 이 검사와 종료 코드 0을 확인했습니다. 이미 통과한 경우 반복하지 않고 아래 일반 실행으로 진행하세요.

## 일반 실행 확인

기존 bundle을 재빌드 없이 실행합니다. 저장소 루트에서 실행하세요.

```sh
open "Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"
```

일반 실행은 Mac 전용 사용자 DB를 생성/열고 공식 상태 페이지와 설치된 CLI의 한도를 조회합니다. 알림 권한 창이 나오면 사용자가 허용 여부를 선택합니다. **로그인 자동 실행은 `.app`를 고정 위치로 옮긴 뒤 확인하며 지금은 켜지 않습니다.**

메뉴바 아이콘 클릭으로 상태 창이 열리는지, Schedule과 카운트다운이 표시되는지, Provider 공식 상태·ChatGPT/Claude 한도 조회 결과를 확인합니다. 그 Mac에 공식 CLI가 설치·로그인되어 있지 않으면 한도 조회 불가가 나올 수 있습니다. Windows의 로그인은 자동 복사하지 않습니다. 이 안내 자체는 일반 실행·계정 조회 검증을 완료했다는 뜻이 아닙니다.

## 네이티브 구현 기준

UI와 시스템 연결은 다음 공식 바인딩을 사용합니다.

- [AppKit 바인딩](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/src/appkit.cs): NSStatusItem·NSWindow·NSWorkspace.
- [ServiceManagement 바인딩](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/src/servicemanagement.cs): SMAppService.
- [UserNotifications 바인딩](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/src/usernotifications.cs): UNUserNotificationCenter.

공통 기능 개선은 루트의 원본 소스에서 반영하며 Mac 고유 UI·로그인 항목·알림 코드는 이 폴더에서 관리합니다. 이 preview는 Windows 기능 변경이나 기존 배포본 교체를 동반하지 않습니다.
