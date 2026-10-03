# macOS preview 0.1.0 — 소스 준비와 검증 기록

기록일: 2026-10-03 KST. Windows 기준 버전: 2.2.2.

Apple Silicon / macOS 27용 **native AppKit 메뉴바 호스트**를 별도 프로젝트로 준비했다. 기존 Windows WinForms UI·배포본은 유지한다. 소스 구현·Windows 회귀 검사·Mac 참조 API 컴파일에 이어 **실제 Mac의 공통 검사 244,347건과 Release `.app` 생성·로컬 서명 검사·ARM64 SQLite 포함을 확인**했다. 네이티브 앱 실행·알림·로그인 항목·실제 CLI 조회는 아직 확인 전이다. 최신 결과는 12절에 별도로 기록한다.

Mac 개발 도구 준비와 실행 순서는 [Mac/README.md](Mac/README.md)를 따른다.

## 1. 한 저장소, 같은 기능 규칙

영구적인 Windows/Mac 브랜치를 따로 유지하지 않는다. `feature/macos-native`는 개발·검증용 임시 브랜치다. Mac 검증 후 main 병합 여부를 판단하고, 공통 기능 개선은 한 원본에서 두 호스트에 반영한다.

```text
루트의 공통 C# 원본 19개
  ├─ Windows: 기존 WinForms 프로젝트가 직접 컴파일
  ├─ macOS: SharedSources.props로 링크 → AppKit 호스트
  └─ 공통 검사: 같은 원본과 기존 검사들을 링크

UI·알림·자동 실행: OS별 호스트
DB·CLI 로그인: 각 컴퓨터에 따로 유지
```

새 공통 DLL이나 public API를 만들지 않고 소스 링크를 사용했다. 원본 파일을 복사해 독립적인 두 시간표·한도 정책을 유지하지 않는다. UI 프레임워크를 통합하기 위한 MAUI·Avalonia·Electron·WebView는 추가하지 않았다.

## 2. 변경한 기존 파일

| 파일 | 변경 |
|---|---|
| AgentSchedule.cs | Windows ID 유지, macOS에서는 IANA ID를 선택. 업무 구간·공휴일·UTC 비교·캐시 계산은 유지 |
| UsageStore.cs | 기본 DB 경로를 AppPaths에 위임. 한도 캐시는 QuotaJsonContext 사용. schema·migration·SQL은 변경 없음 |
| AccountQuotaClient.cs | Windows .exe 탐색 유지. Mac은 절대 경로·실행 권한·표준 폴더 탐색과 자식 PATH 구성 추가 |
| StatisticsWindow.cs | 기존 계산부를 StatisticsAnalysis.cs로 그대로 분리. Windows 통계 UI는 유지 |
| StatusWindow.cs, AccountQuotaView.cs | 기존 표시 함수를 DisplayFormatting에 위임. Windows 문구·배치·조작은 유지 |
| TrayPresentation.cs | WinForms 창의 표시 함수 의존을 제거하고 공통 formatter 사용 |
| AiBurgerClock.csproj | Mac·공통 검사·artifacts의 C# 파일 제외. Windows 버전·TFM·패키지는 유지 |
| SelfTest.cs | 경로·IANA 시간대·표시·Mac CLI 후보 검사 33건 연결 |
| README.md, CODE_GUIDE.md, BACKLOG.md | preview와 안정판 구분, 소스 지도·준비·미검증 항목 갱신 |

기존 `AccountQuotaPolicy`·파서·monitor·공식 Provider 판정·알림 정책은 변경하지 않았다. Windows의 `ChatGPT` 한도 제목 변경은 여전히 다음 Windows UI 수정 항목이다. Mac preview 소스에는 `ChatGPT` 아래 `Work/Codex`를 적용했다.

## 3. 새 파일

- 공통 보조: `AppPaths.cs`, `DisplayFormatting.cs`, `MacCliPaths.cs`, `StatisticsAnalysis.cs`, `QuotaJsonContext.cs`.
- 검사: `PortablePlatformTests.cs`, `Shared.Tests/Program.cs`, `Shared.Tests/AiBurgerClock.Shared.Tests.csproj`.
- 원본 연결: `Shared/SharedSources.props`.
- Mac 호스트: `Mac/Program.cs`, `Mac/MacApplication.cs`, `Mac/MacStatusWindow.cs`, `Mac/MacStatisticsWindow.cs`, `Mac/MacServices.cs`.
- Mac 빌드·설명: `Mac/AiBurgerClock.Mac.csproj`, `Mac/Info.plist`, `Mac/build.sh`, `Mac/README.md`.
- 이번 기록: `MACOS_PORT.md`.

Windows는 `net10.0-windows` / win-x64, Mac은 `net10.0-macos27.0` / osx-arm64다. 공통 검사 프로젝트는 `net10.0`이다. 직접 NuGet 패키지는 두 호스트와 검사 모두 **Microsoft.Data.Sqlite 10.0.12** 하나다.

## 4. Mac 호스트의 구현 범위

- native F/B 메뉴바 아이콘·공식 상태에 따른 색상·카운트다운.
- Schedule/DST/미국 연방 공휴일, Provider별 공식 상태·독립 권고·공식 페이지 열기.
- ChatGPT의 Work/Codex 및 Claude 공식 CLI 한도·모델별 창·리셋 카운트다운.
- 기존 조회 주기: 기본 6시간, 잔여 0% 초과~10% 미만 1시간, 정확히 0% 15분, 리셋 전후 각 15분 5분. 실패 재시도·수동 Refresh·절전·연결 복구 유지.
- 4종 사용 경험과 선택적 메모, 최근 7일·30일·전체 및 Provider/시간대/Schedule/DST/공식 상태 통계.
- native Schedule/권고 변화 알림, 사용자가 켜는 로그인 실행 옵션.
- 상태·통계 창을 X로 닫은 후 다시 열 수 있도록 native 창 수명을 유지하고 smoke에 재열기 검사를 연결.
- 종료 시 초기화·조회·통계 읽기를 취소/마무리하고 이미 받은 사용자 저장은 기다림.

인증은 각 공식 CLI가 처리한다. Mac 호스트가 Keychain·인증 파일·쿠키·토큰을 읽거나 비공개 usage HTTP를 호출하지 않는다. CLI가 없으면 한도만 조회 불가로 표시한다. Gemini 개인 계정 한도·한도 회복 알림·크레딧·리셋권 사용은 추가하지 않았다.

## 5. 시간대와 데이터

Windows의 `Eastern Standard Time`, `Pacific Standard Time`, `Korea Standard Time`은 유지한다. Mac에서는 `America/New_York`, `America/Los_Angeles`, `Asia/Seoul`을 사용한다. 각 업무일 ET 09:00과 PT 18:00을 해당 OS의 `TimeZoneInfo`로 UTC 변환한다. KST 10/22시 등을 원천 규칙으로 하드코딩하지 않는다.

Windows DB는 기존 `%LOCALAPPDATA%\AIBurgerClock\burgerclock.db`, Mac DB는 다음 위치다.

```text
~/Library/Application Support/AIBurgerClock/burgerclock.db
```

SQLite schema 2와 migration은 그대로다. 공통 검사는 새 임시 DB에서 생성·저장·재오픈·이전 schema 이관·메모·상태·설정·한도 캐시와 1만 건 통계를 확인했다. 11절의 실제 Mac 공통 검사에서도 SQLite 저장·통계가 통과했지만, **최종 `.app`에 포함되는 ARM64 SQLite 라이브러리 로딩은 아직 미확인**이다. 양쪽 사용자 DB를 읽거나 자동 동기화하지 않았다.

## 6. 이번에 실제로 수행한 검증

검증 환경은 Windows / .NET SDK 10.0.401이다. 실제 계정 조회·AI 모델 요청·인증 변경은 하지 않았다.

이번 Windows 회귀 검사는 빌드와 `--self-test`다. 새 WinForms smoke·실제 알림·재로그인을 다시 실행한 것은 아니다. 기존 Windows UI 배치와 자동 시작 코드는 변경하지 않았다.

| 검사 | 결과 | 범위 |
|---|---|---|
| Windows Release, -warnaserror | 경고 0 / 오류 0 | 기존 WinForms 프로젝트 |
| Windows --self-test | 250,741 assertions, exit 0 | 기존 검사 + portable 33건, 임시 DB·가짜 통신 |
| 공통 검사 Release, -warnaserror | 경고 0 / 오류 0 | net10.0 공통 원본 |
| 공통 검사 실행 | 244,338 assertions, exit 0 | Schedule/DST/공휴일·공식 파서·monitor·SQLite·통계·한도 정책·종료 |
| Mac C# 참조 API 컴파일, -warnaserror | 경고 0 / 오류 0 | Mac C# 5개 + 공통 18개, Windows에서 의미/API 검사 |
| Info.plist / build.sh | XML 구문 정상 / UTF-8 no BOM, LF | 파일 형식 검사 |

portable 33건에는 두 OS 데이터 경로, 60시간·음수 카운트다운, offset, 리셋 미제공/경과, IANA 시간대 변환과 Mac CLI 후보 검사가 있다. 2026년 1월/7월, DST 시작 직전 금요일/직후 월요일, 종료 직전 금요일/직후 월요일의 ET/PT 경계를 가짜 시각으로 비교했다. 시스템 시계는 바꾸지 않았다.

Mac 참조 패키지는 공식 `Microsoft.macOS.Ref.net10.0_27.0` 27.0.10722다. Microsoft repository commit `d813e2baef17cd3a2bb5adc1e37610258d01cda3`이 지원 릴리스와 일치하는지 확인했다. 패키지 SHA-256은 `1A307EC95B1EDB28B99289B87B68FFF81050E84B54C37524560C7BE250240614`다. 참조 DLL만 읽어 컴파일했고 앱 프로젝트에 직접 참조·새 패키지·workload를 추가하지 않았다.

이 API 검사에서 nullable·AppKit 바인딩 명칭·obsolete API 문제를 고쳤다. **registrar·네이티브 링크·서명·`.app` 생성·native runtime 검증은 포함하지 않는다.**

로컬 로그는 Git에서 제외한 `artifacts/mac-port/`의 `windows-build-initial.txt`, `windows-self-test.txt`, `shared-build-initial.txt`, `shared-self-test.txt`, `host-api-compile-final.txt`에 있다. 진단용 참조 DLL·C# probe·빌드 결과도 커밋하지 않는다.

## 7. Mac에서 남은 필수 확인

첫 읽기 전용 결과는 macOS 27.0.1 / arm64 / Xcode 27.0이며 `dotnet`은 PATH에서 발견되지 않았다. 이후 사용자가 SDK 10.0.401을 준비했고 `build.sh`의 macos workload 확인과 공통 검사 실행까지 통과했다. 도구 준비와 최종 앱 빌드 성공은 별개이며, 이 Windows 작업에서 도구를 원격으로 설치하거나 Mac 설정을 변경하지 않았다.

1. 완료: .NET SDK 10.0.401 ARM64 / Xcode 27.0 준비와 build.sh의 macos workload preflight 통과. 정확한 설치 workload set의 별도 출력은 받지 않았다.
2. 완료: `bash Mac/build.sh`에서 Mac OS 시간대 공통 검사 244,347건, native Release 빌드, ad-hoc 서명 검사·ARM64 SQLite dylib 포함 확인.
3. 만들어진 실행 파일의 `--smoke-test`: 임시 DB 4종 이벤트·메모·통계·No data·한도 표시와 종료 코드 확인.
4. 실제 UI의 메뉴바·상태 창·한도 스크롤·기록·통계·공식 페이지 클릭 확인.
5. 별도 설치·로그인된 Mac CLI에서 실제 한도 수신과 실패 표시 확인. Windows 성공을 Mac 계정 조회 성공으로 간주하지 않음.
6. 사용자 승인하에 알림 권한·배너와 로그인 항목 등록/해제·재로그인 확인.
7. 절전·연결 복구·정상 종료·장시간 실행 확인.

native smoke는 별도의 GUID 임시 DB만 쓰며 HTTP·계정 CLI·브라우저·알림 권한 요청·로그인 항목 변경을 하지 않는다. 실제 알림/자동 실행을 검증한 것으로 표시하지 않는다.

## 8. 빌드와 배포의 제한

[Microsoft Xcode 27 지원 릴리스](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.0-10722)의 SDK 10.0.401 / workload set 10.0.401.1 / Xcode 27.0을 기준으로 준비했다. `build.sh`는 설치나 로그인 대신 기존 도구를 확인하고 필요한 경우 종료한다.

Mac은 self-contained `.app`를 목표로 한다. `TrimMode=copy`로 관리 코드 제거 없이 Apple SDK의 플랫폼 처리 경로를 사용한다. 로컬 ad-hoc 서명이며 Developer ID·notarization·설치 프로그램·자동 업데이트는 포함하지 않는다. 다른 Mac에 배포할 때 Gatekeeper/정식 서명을 따로 검토해야 한다.

사용자 Mac에서 실제 생성한 bundle 위치는 다음과 같다. **이 Windows 환경에서 만든 결과가 아니라 사용자가 전달한 Mac 빌드 출력으로 확인했다.**

```text
Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app
```

Mac UI 크기·알림 노출·로그인 등록과 이동된 bundle의 동작은 실제 화면에서 조정할 수 있다. 미검증 preview를 안정판으로 표시하거나 Windows dist를 교체하지 않는다.

## 9. Git 기준점과 Windows 보존

- 개발 브랜치: `feature/macos-native`.
- 검토: [Draft PR #12](https://github.com/hydron75/ai-burger-clock/pull/12). 실제 Mac 검증을 기다리는 상태이며 병합하지 않았다.
- 작업 전 main: `fbcba72f9ad41e23ab2e668352f9f137c5803557`.
- preview 구현·검증 소스 커밋: `2c7e360ff6a9d8cde59a84c38c7ca2cf4faeae89`. 이후 PR 번호·기록만 갱신한 문서 커밋은 코드 변경이 아니다.
- Windows 배포본 버전: 2.2.2, 변경하지 않음.
- 보존한 `dist/win-x64/AI Burger Clock.exe` SHA-256: `56250BAF25C192314B513C02977A2A40A1E1D72836C43B0CF20803C2F98E2A10`.

사용자 DB·레지스트리·자동 시작 경로·실행 중인 앱을 변경하지 않았다. 이번 단계는 배포 교체가 아니므로 DB 백업/복원이나 기존 앱 종료를 요구하지 않았다. 원래 Windows 소스 상태는 위 기준 커밋으로 확인할 수 있다. 기존 변경을 강제로 되돌리는 명령은 실행하지 않는다.

실제 Mac 검증 결과를 받은 뒤 이 기록과 README·CODE_GUIDE·BACKLOG를 갱신하고, 병합·안정판 배포는 별도 판단한다.

## 10. 첫 Mac 빌드의 HTTP 캐시 권한 오류

2026-10-03 KST 첫 사용자 전달 출력에서 SDK 10.0.401 / Xcode 27.0의 빌드 시작을 확인했다. 공통 검사 프로젝트의 NuGet 복원 단계에서 사용자 홈의 `NuGet/http-cache` 접근 거부로 NU1900이 발생했고, 경고를 오류로 처리하는 규칙에 따라 중단됐다. **이 첫 시도에서는 공통 검사의 Mac 실행이나 native 앱 빌드 성공 결과가 없었다.** 이후 재시도 결과는 11절에 기록한다.

직접 확인된 원인은 캐시 경로 접근 거부다. 앞선 관리자 권한 workload 설치가 캐시 소유권에 영향을 줬을 가능성은 있으나 소유자/ACL은 확인하지 않았으므로 확정하지 않는다.

`Mac/build.sh`의 기본 HTTP 캐시를 저장소의 `artifacts/mac-build/nuget-http-cache`로 분리했다. 기존 `NUGET_HTTP_CACHE_PATH`가 지정되어 있으면 보존하고, 캐시 폴더 쓰기 가능 여부를 확인한다. root 실행은 거부하고 일반 사용자로 빌드하도록 안내한다. 이 설정은 빌드 프로세스에만 적용하며 기존 캐시·전역 NuGet 설정·패키지 저장소·권한을 수정하거나 삭제하지 않는다.

취약성 검사와 경고를 오류로 처리하는 정책은 유지했다. Windows 코드·배포본·DB·Mac runtime 소스·앱 버전은 변경하지 않았다. 사용법과 소스 지도, BACKLOG도 같은 수정 범위에 맞췄다. 재시도는 [Mac 안내](Mac/README.md#nu1900--http-캐시-접근-거부)를 따른다. [공식 HTTP 캐시 설정](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders).

로컬 검증은 Windows의 Git Bash 5.3.15에서 수행했다. `bash -n Mac/build.sh`와 실제 스크립트의 캐시 블록을 이용한 기본 경로·명시적 경로(공백 포함)·빈 override 3가지 검사, 자식 프로세스 전달을 통과했다. SDK 10.0.401의 `dotnet nuget locals http-cache --list`에서도 지정 경로를 인식했다. 종료 코드는 모두 0이며 로그는 Git에서 제외한 `artifacts/mac-cache/`에 있다. C# 변경이 없어 기존 전체 앱 검사를 반복하지 않았다.

같은 Draft PR #12에 빌드 보완과 문서를 반영했다. 이후 실제 Mac 재시도에서 캐시 오류가 재발하지 않은 것을 확인했고, 최종 빌드 성공 결과는 12절에 남긴다.

## 11. 실제 Mac 공통 검사 통과와 JSON IL2026 보완

2026-10-03 KST 사용자가 `ba908a6`을 받아 재시도한 출력에서 SDK 10.0.401 / Xcode 27.0과 빌드 전용 캐시 사용, **공통 검사 244,338건의 Mac 실행 통과**를 확인했다. NU1900은 재발하지 않았고 managed C# 컴파일은 진행됐지만, 이후 `UsageStore.cs` 26/44행의 reflection 기반 `JsonSerializer.Serialize/Deserialize`가 Apple SDK의 trimming 분석에서 IL2026 두 건으로 실패했다. managed DLL 출력은 최종 `.app` 완성을 의미하지 않는다.

### 변경과 호환성

- `QuotaJsonContext.cs`를 추가하고 `QuotaCache` 및 하위 타입의 직렬화 metadata를 빌드 때 생성한다. `UsageStore`의 저장·읽기는 `JsonTypeInfo`를 받는 overload로 변경했다.
- `Shared/SharedSources.props`에도 연결해 Mac·Windows·공통 검사 모두 같은 원본을 사용한다. 공통 원본은 19개다.
- JSON 필드명·숫자 enum·nullable 값·Unicode·리셋 anchor를 보존한다. `AccountQuota.v1.*` metadata 키와 SQLite schema 2, 데이터 검증·길이 제한은 변경하지 않았다. DB migration·캐시 삭제는 필요하지 않다.
- `AccountQuotaMonitorTests`에 양쪽 Provider의 이전 JSON 읽기, 새 JSON을 이전 reader로 읽기, 저장·재오픈 및 schema/키 보존 검사 9건을 추가했다.
- 새 직접 NuGet 패키지·경고 억제·trimming 비활성화는 없다. Windows 2.2.2와 미출시 Mac preview 0.1.0의 버전은 유지한다.

[Microsoft의 JSON source generation 안내](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)에 따라 타입 정보를 명시적으로 제공했다. 진단 프로젝트의 ILLink 분석 도구는 SDK의 검사 의존성이며 앱의 직접 패키지 추가가 아니다.

### 이번 수정 후 검증

다음은 **Windows / SDK 10.0.401에서 실행한 결과**다. 위의 사용자 전달 Mac 공통 검사 결과와 구분한다.

| 검사 | 결과 |
|---|---|
| Windows Release, `-warnaserror` | 경고 0 / 오류 0 |
| Windows `--self-test` | 250,750 assertions, exit 0 |
| 공통 Release 실행, warnings-as-errors | 244,347 assertions, exit 0 |
| Mac 호스트 참조 C# 컴파일 + `EnableTrimAnalyzer=true`, `-warnaserror` | 경고 0 / 오류 0; 호스트 5개 + 공통 19개 |
| JSON reflection 비활성화 + trim analyzer 진단 | 10 assertions, exit 0; 이전 JSON→실제 UsageStore→SQLite 저장→새 store 재오픈 |
| 기존 Windows dist EXE SHA-256 | 9절의 해시와 동일 |

reflection 비활성화 진단 프로젝트는 Git에서 제외한 `artifacts/mac-json-check/`에 두었다. 계정 조회·사용자 DB·인증 파일·레지스트리·Windows 배포본은 변경하지 않았다. 이 검증은 macOS native registrar/linker/signing·ARM64 runtime을 실행한 결과가 아니다.

이 수정본의 Mac 재빌드는 이후 성공했으며 결과를 12절에 기록한다. 다음은 7절의 native smoke와 실제 UI·알림·로그인 항목 검증이다. Draft PR #12는 유지하고 main 병합은 하지 않는다.

## 12. 실제 Mac Release bundle 빌드 성공

2026-10-03 KST 사용자가 `302aee8a9d2dda73c9dd79c0612123a25504bac2`를 받아 `bash Mac/build.sh`로 빌드한 출력으로 다음을 확인했다. 계정 테스트나 앱 실행 출력은 아직 받지 않았으며 빌드 성공과 구분한다.

| 확인 항목 | 사용자 Mac의 결과 |
|---|---|
| SDK / Xcode | .NET 10.0.401 / Xcode 27.0, build 27A266a |
| 공통 검사 | 244,347 assertions, PASS ALL SHARED |
| native Release | `net10.0-macos27.0` / `osx-arm64`, 성공; `-warnaserror`, 경고·오류 출력 없음 |
| bundle 생성 | `Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app` |
| 로컬 서명 검사 | build.sh의 `codesign --verify --deep --strict` 이후 Bundle 출력까지 도달해 통과 확인 |
| bundle의 SQLite | `Contents/MonoBundle/libe_sqlite3.dylib`, Mach-O 64-bit / arm64 포함 |
| 이전 실패 | NuGet 캐시 접근 거부와 JSON IL2026 두 건 모두 재발하지 않음 |

서명 검사는 로컬 ad-hoc 서명에 한정되며 Developer ID·Apple notarization을 검증한 것이 아니다. SQLite dylib 포함도 최종 `.app`의 runtime 로딩 검증과 다르다. 공통 검사에서는 임시 SQLite DB 생성·저장·재오픈·이관·1만 건 통계와 가짜 Provider/CLI 응답을 검증했으며, 실제 계정 한도 조회가 성공했다는 뜻은 아니다.

이번 후속 작업은 README·CODE_GUIDE·BACKLOG·Mac/README와 이 기록에 확인된 결과를 반영한 **문서 변경만**이다. 앱 코드·버전·패키지·사용자 DB·자동 실행·Windows 배포본은 바꾸지 않았고 추가 설치나 계정 접근도 하지 않았다. 문서만 변경했으므로 통과한 전체 빌드·검사를 Windows에서 반복하지 않았다. PR #12는 실행 검증이 남아 있어 Draft를 유지하며 병합하지 않는다.

다음은 이미 만들어진 실행 파일로 임시 DB native smoke를 실행하는 것이다. 재빌드 없이 저장소 루트에서 다음을 실행하고 출력과 종료 코드를 확인한다.

```sh
"Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app/Contents/MacOS/AI Burger Clock" --smoke-test
echo "검사 종료 코드: $?"
```

성공 기준은 `PASS: native controls/window close-reopen...`와 종료 코드 0이다. native smoke의 실제 결과는 아직 대기 중이며, 성공한 뒤 일반 앱 실행·메뉴바·알림·로그인 항목·Mac CLI·절전/연결 복구를 순서대로 확인한다.
