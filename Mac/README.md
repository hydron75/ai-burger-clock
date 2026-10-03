# AI Burger Clock · macOS preview 0.1.2

Apple Silicon / macOS 27을 위한 C# AppKit 메뉴바 앱입니다. Windows 2.2.2 배포본과 별도 앱이지만, 시간표·공식 상태·권고·한도 조회·기록·통계 계산 코드는 같은 저장소의 원본 소스를 링크해서 사용합니다. Windows 코드를 복사해 따로 유지하지 않습니다.

이 폴더는 **일반 사용 검증을 진행 중인 preview 소스**입니다. 2026-10-03 KST **0.1.2의 실제 Mac 공통 검사 244,347건·Release `.app`·서명·ARM64 SQLite·native smoke PASS에 이어, 일반 실행의 커진 컬러 아이콘과 양쪽 메뉴막대 표시도 확인했습니다.** 앞선 0.1.1의 한 화면 배치·두 계정 한도 수신도 확인했습니다. 이후 이 Mac에서 직접 빌드해 보니 **이전 bundle은 `Info.plist` 버전이 0.1.0 / 1로 남아 있었습니다.** 소스 `66fb868`에서 버전을 csproj로 옮겨 0.1.2 / 3이 반영되도록 고쳤고, native smoke가 bundle 버전도 검사합니다. 절전 복귀·연결 복구 자동 조회와 재로그인 자동 실행·로그아웃 정상 종료도 실제로 확인했습니다. OS 알림·장기 사용은 남아 있습니다. [재로그인 확인](../MACOS_PORT.md#27-로그아웃-정상-종료와-재로그인-자동-실행-실제-확인), [절전·연결 복구 확인](../MACOS_PORT.md#26-연결-복구절전-복귀-실제-확인), [버전 수정과 로컬 검증](../MACOS_PORT.md#24-012-bundle-버전-미반영-수정과-첫-로컬-mac-검증), [실제 Mac 빌드](../MACOS_PORT.md#21-012-실제-mac-release-빌드-성공), [native 검사](../MACOS_PORT.md#22-012-아이콘-native-smoke-pass), [실제 메뉴막대](../MACOS_PORT.md#23-012-실제-컬러-아이콘과-양쪽-메뉴막대-확인)

## 0.1.2: 더 큰 컬러 메뉴바 아이콘

주변 아이콘과 비슷한 20-point 원 안에 흰색 F/B를 넣습니다. 기존 상태 정책대로 모든 서비스 정상+FULL은 초록, BURGER 또는 성능 저하는 주황, 공식 장애는 빨강, 확인 불가/STALE는 회색입니다. 글자는 Schedule, 색은 공식 서비스 상태까지 포함한 주의도를 나타내며 계정 잔여량으로 색을 바꾸지는 않습니다.

- 20×20px·40×40px 표현을 모두 20×20 point로 지정해 일반/Retina 화면에서도 같은 크기로 표시합니다.
- 버튼 tint나 시스템 심볼 대신 이미지 자체에 색상을 넣고, 표준 정사각 메뉴바 항목을 사용합니다. 모니터별 항목을 임의로 중복 생성하지 않습니다.
- native smoke에 8개 문자/색 조합의 실제 RGB·투명도·흰 글자·1x/2x 크기와 버튼 이미지 보존 검사를 추가했습니다. 이 검사는 듀얼 모니터의 실제 표시 여부를 대신하지 않습니다.
- **사용자 Mac에서는 새 버전의 크기·색상과 양쪽 메뉴막대 표시를 확인했습니다.** 기존 비활성 모니터 누락이 더 이상 관찰되지 않았습니다. 정확한 원인을 이미지 수명 하나로 확정하거나 모든 Mac 환경의 결과로 일반화하지는 않습니다.
- Windows 2.2.2, 기존 창·메뉴·조회·DB·로그인 설정과 `.app` 경로는 유지합니다.

실행 중인 Mac 앱을 메뉴바 → 종료로 닫은 뒤 저장소 루트에서 다음을 실행합니다. 이 변경은 C# 수정이므로 앞선 문서-only 갱신과 달리 재빌드가 필요합니다. Xcode 경로는 이번 실행에만 지정합니다.

```sh
git pull --ff-only
/usr/bin/env DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer /bin/bash Mac/build.sh
```

빌드 성공 후 아래 안전한 네이티브 검사를 실행하고 일반 앱을 엽니다. `20pt color menu icon/1x-2x pixels`를 포함한 PASS와 종료 코드 0을 확인한 뒤, 실제 크기·색상·양쪽 메뉴막대 표시를 별도로 확인합니다.

소스 `dd3f596`의 0.1.2 빌드와 새 native smoke가 PASS한 사용자 설치는 **재빌드·검사 반복 없이 방금 만든 bundle을 일반 실행**하여 메뉴막대를 확인합니다. 이후 결과를 기록한 문서-only 커밋 때문에 앱을 다시 만들 필요는 없습니다.

아래 명령은 **각 줄을 따로 복사해서 차례로 실행**합니다. `--smoke-test echo ...`처럼 한 줄로 붙이면 `echo`가 앱의 추가 인자로 전달되어 검사를 시작하지 않고 지원 옵션 안내만 출력합니다.

```sh
"/Users/hydron/ai-burger-clock/Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app/Contents/MacOS/AI Burger Clock" --smoke-test
echo "검사 종료 코드: $?"
open "/Users/hydron/ai-burger-clock/Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"
```

## 들어 있는 기능

- 메뉴바의 F/B 아이콘과 공식 상태에 따른 색상, KST 카운트다운.
- 미국 ET 09:00~PT 18:00 업무시간, DST, 미국 연방 공휴일 보정. Windows와 같은 정책입니다.
- OpenAI·Claude·Gemini 공식 상태와 Provider별 독립 권고. 클릭하면 기본 브라우저에서 공식 상태 페이지를 엽니다.
- ChatGPT 제목 아래 Work/Codex 한도, Claude 한도. 설치·로그인된 공식 CLI가 조회하며 앱은 토큰·Keychain·쿠키를 읽지 않습니다. Gemini **개인 계정 한도**는 포함하지 않습니다.
- 기본 6시간, 잔여 0% 초과~10% 미만 1시간, 정확히 0%는 15분, 리셋 전후 각 15분은 5분 조회. 조건이 겹치면 짧은 주기가 적용됩니다. 예정 리셋 시각 경과만으로 100% 회복을 가정하지 않습니다.
- 수동 Refresh, 절전 복귀 조회, 연결 복구 조회(네트워크 이벤트는 1분 제한), 정상 종료 시 작업 취소.
- 화면의 Provider 이름은 ChatGPT·Claude·Gemini 제품 이름으로 표시합니다. 저장되는 값과 공식 상태 주소는 그대로입니다(OpenAI 상태 페이지).
- 메뉴바 또는 Provider의 기록 메뉴에서 Success·Slow·Error·Interrupted를 기록. 메모는 선택 사항이며 Windows와 같이 최대 1,000자입니다.
- Statistics의 최근 7일·30일·전체 및 Provider·KST 시간대·Schedule/DST·공식 상태별 비교. n·No data·소표본을 표시합니다.
- macOS 네이티브 Schedule/공식 상태 변화 알림과 로그인 자동 실행 옵션. **계정 한도 리셋/회복 알림과 크레딧은 구현하지 않았습니다.**

## 0.1.1: 한 화면에 모은 상태 창

전체 화면 스크롤을 없애고 상태 창을 430×660 point로 줄였습니다. Schedule·US 시간·세 Provider 상태·ChatGPT/Claude의 일반 한도·조회 시각·버튼·옵션을 한 화면에 놓습니다. 한도 조회·저장·자동 실행 로직과 메뉴바는 바꾸지 않습니다.

- 긴 Provider 이유·공휴일·안내 메시지는 끝을 줄여 표시하고 마우스를 올리면 원문을 볼 수 있습니다.
- ChatGPT의 일반 5시간/주간+조회 정보와 Claude 세션/주간 전체/모델별 한도+조회 정보를 위한 높이를 확보했습니다. 반환된 모델별 한도가 더 많거나 오류 설명이 길면 해당 한도 상자 안에서만 스크롤합니다. 한도 행을 삭제하거나 수치를 추정하지 않습니다.
- 새 native smoke는 컨트롤 경계/겹침과 일반 3줄/4줄의 실제 글꼴 높이를 검사합니다. **Windows의 참조 컴파일은 실제 Mac 화면 검증을 대신하지 않습니다.**

현재 Mac 앱을 메뉴바 → **종료**로 닫은 뒤 저장소 루트에서 업데이트·빌드하세요. 기존 자동 실행 옵션은 변경하지 않으며 `.app` 위치도 그대로 둡니다.

```sh
git pull --ff-only
bash Mac/build.sh
```

당시 소스 `9f805f8`의 0.1.1 빌드 후 문서만 갱신했을 때에는 재빌드 없이 검사했습니다. **0.1.1에서 0.1.2로 처음 업데이트할 때는 아이콘 C# 코드가 달라 재빌드해야 합니다.** 이미 0.1.2를 빌드하고 새 native smoke가 PASS했다면 문서 갱신만으로 다시 빌드하지 않습니다.

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

### 출력 없이 종료 / CommandLineTools 선택

소스 `91c32c6`의 빌드에서 `xcodebuild -version` 단계가 실패했지만 오류를 숨겨 조용히 종료한 사례가 있습니다. 최신 스크립트는 시작과 도구 확인 단계를 즉시 표시하고, Xcode·SDK·workload 조회 실패 시 원래 오류를 보존한 채 종료 코드 2로 멈춥니다. 이 경우 공통 검사와 앱 컴파일은 시작하지 않습니다.

`active developer directory '/Library/Developer/CommandLineTools' is a command line tools instance`가 나오면 전체 Xcode 대신 독립 Command Line Tools가 선택된 상태입니다. Command Line Tools 27을 설치하는 것과 전체 Xcode 27을 빌드 도구로 선택하는 것은 다릅니다. 먼저 아래 두 줄을 **각각** 실행해 실제 오류와 선택 경로를 확인합니다.

```sh
/usr/bin/xcodebuild -version
/usr/bin/xcode-select -p
```

전체 Xcode가 기본 위치에 있다면 다음 조회로 확인합니다. 2026-10-03 사용자 Mac에서 이 경로의 Xcode 27.0 / build 27A266a를 확인했습니다.

```sh
/usr/bin/env DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer /usr/bin/xcodebuild -version
```

이 조회가 성공하면 실행 중인 앱을 종료한 뒤 저장소 루트에서 다음 두 줄을 각각 실행합니다. Xcode가 다른 위치에 설치됐다면 확인된 실제 경로로 바꿉니다.

```sh
git pull --ff-only
/usr/bin/env DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer /bin/bash Mac/build.sh
```

`DEVELOPER_DIR`는 이 빌드와 자식 프로세스에만 적용합니다. 전역 `xcode-select` 설정·라이선스·설치 상태를 바꾸지 않습니다. 스크립트가 Xcode를 자동 선택하거나 설치하지도 않습니다. [Apple의 개발 도구 선택 안내](https://developer.apple.com/documentation/xcode/configuring-command-line-tools-settings), [원인과 검증 기록](../MACOS_PORT.md#16-xcode-선택-경로와-조용한-빌드-중단-보완).

이 실행별 지정으로 소스 `9f805f8`의 실제 Mac 0.1.1 Release·공통 검사·서명·ARM64 SQLite 확인까지 성공했고 해당 bundle의 native smoke도 종료 코드 0으로 통과했습니다. 기본 선택 경로를 전역으로 바꾼 결과로 해석하지 않습니다.

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

수정 후 Windows의 Release/회귀·공통 검사, Mac 참조 코드의 trimming 분석과 reflection 비활성화 캐시 검사를 통과했습니다. 이어서 소스 `302aee8`의 **실제 Mac `.app` 빌드·서명 검사와 native smoke까지 통과**했고 CLI 설치 후 실제 한도 수신도 확인했습니다. 별도 UI 수정본 0.1.1의 소스 `9f805f8`도 새 Mac 빌드·서명·ARM64 SQLite와 새 native smoke를 통과했으며 일반 실행 한 화면 표시도 확인했습니다. [이전 native 검사 범위](../MACOS_PORT.md#13-실제-mac-native-smoke-통과), [새 빌드 결과](../MACOS_PORT.md#17-011-실제-mac-release-재빌드-성공), [새 native 검사](../MACOS_PORT.md#18-011-새-native-smoke-통과).

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

현재 소스의 `bash Mac/build.sh`가 성공한 뒤 아래 검사로 진행합니다. 저장소 루트에서 실행하세요. 실행 중인 일반 앱이 있으면 메뉴바에서 종료합니다.

```sh
"Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app/Contents/MacOS/AI Burger Clock" --smoke-test
echo "검사 종료 코드: $?"
```

메뉴바·창을 만들고 상태/통계 창의 닫기·재열기·Visible 상태를 확인합니다. 20-point 아이콘의 실제 1x/2x 색·투명도·흰 글자, 전체 상태 창 스크롤 부재·컨트롤 경계/겹침·일반 한도 3줄/4줄의 글꼴 높이, 새 임시 DB의 4종 실측/메모·표본수/No data와 주입 시각의 한도 카운트다운 감소를 검사한 뒤 종료합니다. 실제 계정 조회·HTTP·브라우저 열기·알림 권한 요청·자동 실행 변경을 하지 않습니다. 다중 모니터 메뉴막대·OS 알림·재로그인 및 장시간 절전 복귀는 별도 검증 대상입니다.

앱 아이콘은 `Mac/Assets.xcassets/AppIcon.appiconset`에 있습니다. 바꿀 때는 `swift Mac/tools/make-app-icon.swift`로 다시 생성합니다.

성공 기준은 `PASS: bundle version, menu-tracking countdown timer, 1,000-char note limit, native controls/window close-reopen...` 출력과 종료 코드 `0`입니다. 소스 `66fb868`부터 bundle의 `CFBundleShortVersionString`이 앱 버전과 다르면 `FAIL: Bundle version ...`으로 실패합니다. Mac 버전은 `Mac/AiBurgerClock.Mac.csproj`의 `Version`·`ApplicationVersion`에서만 바꾸고 `Info.plist`에는 적지 않습니다. SDK가 `Info.plist`만 바뀐 증분 빌드에서 bundle manifest를 다시 만들지 않기 때문입니다. 오류가 있으면 출력과 종료 코드를 보존하고 일반 실행·자동 시작 설정 전에 원인을 확인합니다.

2026-10-03 KST 사용자 Mac에서 이전 0.1.0뿐 아니라 새 0.1.1의 종료 코드 0도 확인했습니다. 0.1.1 성공 출력에는 `compact one-screen layout/standard quota rows`와 `injected quota countdown`이 포함됐습니다. 가짜 한도 데이터로 실행한 검사이므로 실제 CLI 계정 조회 성공이나 OS 알림 노출을 뜻하지 않습니다. [검사 기록](../MACOS_PORT.md#18-011-새-native-smoke-통과)

## 일반 실행 확인

현재 소스로 빌드한 bundle을 실행합니다. 저장소 루트에서 실행하세요.

```sh
open "Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"
```

일반 실행은 Mac 전용 사용자 DB를 생성/열고 공식 상태 페이지와 설치된 CLI의 한도를 조회합니다. 알림 권한 창이 나오면 사용자가 허용 여부를 선택합니다. 로그인 자동 실행은 현재 옵션을 유지합니다. 앱 위치를 나중에 변경하면 시스템 설정 → 일반 → 로그인 항목에서 등록 경로/승인 상태를 다시 확인하세요.

메뉴바 → 상태 창 열기에서 Schedule·Provider 공식 상태·ChatGPT/Claude 한도·버튼이 전체 화면 스크롤 없이 보이는지 확인합니다. 한도 카운트다운과 긴 이유의 Tooltip도 확인하세요. 사용자 0.1.1 전체 창/메뉴바 스크린샷에서 한 화면 표시와 두 계정 한도 수신을 확인했습니다. 실제 Tooltip·기록 메뉴·공식 링크 클릭, 추가 한도 내부 스크롤과 OS 동작까지 스크린샷으로 검증한 것은 아닙니다. 다른 Mac에서 CLI가 설치·로그인되지 않았다면 한도 조회 불가가 표시됩니다. Windows의 로그인은 자동 복사하지 않습니다. [일반 화면 기록](../MACOS_PORT.md#19-011-일반-상태-창과-메뉴바-확인)

## CLI를 찾지 못하는 경우

첫 일반 실행 스크린샷에서는 두 계정 한도에 `공식 CLI를 찾지 못했습니다`가 표시됐고, 이후 사용자가 CLI를 설치해 정상 수신했습니다. 이 메시지는 인증 응답이 아니라 실행 파일 탐색 실패입니다. 다른 설치에서 같은 문제가 생기면 미설치·GUI에 보이지 않는 설치 경로·실행 권한을 구분합니다.

앱은 GUI 프로세스의 PATH와 `~/.local/bin`, `/opt/homebrew/bin`, `/usr/local/bin`을 확인합니다. 터미널의 셸 프로필은 읽거나 실행하지 않으므로 터미널에서 명령이 보여도 GUI 앱에는 안 보일 수 있습니다. 먼저 맥 터미널에서 아래 읽기 전용 확인 결과를 확인하세요.

```sh
for task_cli in codex claude node npm
do
  printf '%s: ' "$task_cli"
  command -v "$task_cli" || printf '없음\n'
done
```

이 명령은 설치 경로만 읽으며 CLI 실행·계정 조회·로그인·설치·설정 변경을 하지 않습니다. 정상 수신 중이면 반복할 필요가 없습니다. `.app 위치 확인 필요`라는 자동 실행 문구는 별도 항목이며, 한도 조회 실패와 혼동하지 않습니다.

## 네이티브 구현 기준

UI와 시스템 연결은 다음 공식 바인딩을 사용합니다.

- [AppKit 바인딩](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/src/appkit.cs): NSStatusItem·NSWindow·NSWorkspace.
- [ServiceManagement 바인딩](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/src/servicemanagement.cs): SMAppService.
- [UserNotifications 바인딩](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/src/usernotifications.cs): UNUserNotificationCenter.

공통 기능 개선은 루트의 원본 소스에서 반영하며 Mac 고유 UI·로그인 항목·알림 코드는 이 폴더에서 관리합니다. 이 preview는 Windows 기능 변경이나 기존 배포본 교체를 동반하지 않습니다.
