# macOS preview 0.1.4 — 소스 준비와 검증 기록

기록일: 2026-10-03 KST. Windows 기준 버전: 2.2.2.

Apple Silicon / macOS 27용 **native AppKit 메뉴바 호스트**를 별도 프로젝트로 준비했다. 기존 Windows WinForms UI·배포본은 유지한다. 앞선 0.1.1의 한 화면 배치·두 계정 한도 수신을 확인한 뒤, 작은 검은 F와 비활성 모니터의 아이콘 누락 보고를 받아 0.1.2에서 20-point 컬러 bitmap으로 수정했다. **0.1.2의 실제 Mac 공통 검사 244,347건·Release `.app`·서명·ARM64 SQLite·native smoke에 이어 일반 컬러 아이콘과 양쪽 메뉴막대 표시까지 확인**했다. OS 알림·재로그인·절전 복귀와 장기 사용은 남아 있다. 앞선 검증은 17~19절, 아이콘 수정은 20절, 새 실제 빌드는 21절, native 검사는 22절, 실제 메뉴막대는 23절에 구분한다. 24절은 이전 빌드에서 bundle 버전이 0.1.0으로 남던 문제의 수정과, 사용자 Mac에서 직접 실행한 첫 로컬 빌드·검사 기록이다. 25절은 PR #12 코드 리뷰 10건의 반영 기록, 26절은 연결 복구·절전 복귀, 27절은 로그아웃 정상 종료와 재로그인 자동 실행 실제 확인, 29절은 알림 권한 확인, 30절은 UI 조작 확인과 Tooltip·ChatGPT 표시 이름·앱 아이콘 수정, 31절은 0.1.3 버전 정리, 32절은 PR #14 병합과 0.1.4 정리다(28절은 PR #14의 공통 원본 정리).

Mac 개발 도구 준비와 실행 순서는 [Mac/README.md](Mac/README.md)를 따른다.

## 1. 한 저장소, 같은 기능 규칙

영구적인 Windows/Mac 브랜치를 따로 유지하지 않는다. `feature/macos-native`는 개발·검증용 임시 브랜치다. Mac 검증 후 main 병합 여부를 판단하고, 공통 기능 개선은 한 원본에서 두 호스트에 반영한다.

```text
루트의 공통 C# 원본 20개
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
- Mac 호스트: `Mac/Program.cs`, `Mac/MacApplication.cs`, `Mac/MacStatusIcon.cs`, `Mac/MacStatusWindow.cs`, `Mac/MacStatisticsWindow.cs`, `Mac/MacServices.cs`.
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

SQLite schema 2와 migration은 그대로다. 공통 검사는 새 임시 DB에서 생성·저장·재오픈·이전 schema 이관·메모·상태·설정·한도 캐시와 1만 건 통계를 확인했다. 실제 Mac 공통 검사에 이어 13절의 최종 `.app` native smoke에서도 SQLite 임시 DB의 생성·기록·읽기와 통계가 통과했다. 일반 사용자 DB의 재시작/장기 사용 검증이나 양쪽 DB 자동 동기화는 별개이며 수행하지 않았다.

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
2. 완료: 0.1.0과 0.1.1의 실제 Mac 공통 검사 244,347건, native Release 빌드, ad-hoc 서명 검사·ARM64 SQLite dylib 포함 확인. 새 빌드는 17절 참조.
3. 완료: 0.1.0과 새 compact layout 검사가 포함된 0.1.1의 `--smoke-test` 종료 코드 0을 확인했다. 새 검사의 임시 DB·4종 이벤트/메모·통계·창 재열기·주입 시각 카운트다운 결과는 18절에 기록한다.
4. 완료: 0.1.1 일반 화면의 한 화면 배치·메뉴바·Schedule·공식 상태·두 계정 한도 표시 확인. 실제 기록/통계 UI 조작·Tooltip·공식 페이지 클릭은 추가 확인.
5. 완료: CLI 설치 후 Mac에서 실제 ChatGPT/Claude 한도 수신 확인. CLI 미발견 실패 표시도 앞선 화면에서 확인했으며 설치 CLI 버전 출력은 받지 않음.
6. 완료: 로그인 옵션 ON·등록 성공 안내, 실제 재로그인 자동 실행과 로그아웃 정상 종료(27절), 등록 해제(사용자 확인), 알림 권한 허용(29절). 실제 알림 배너는 남음.
7. 완료: 연결 복구·절전 복귀 자동 조회(26절), 로그아웃 시 정상 종료(27절). 장시간 실행은 남음.

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

이 수정본의 Mac 재빌드는 이후 성공했으며 결과를 12절에 기록한다. native smoke 결과는 13절에 남긴다. 일반 사용 UI·알림·로그인 항목 검증은 별도로 이어가며 Draft PR #12는 유지하고 main 병합은 하지 않는다.

## 12. 실제 Mac Release bundle 빌드 성공

2026-10-03 KST 사용자가 `302aee8a9d2dda73c9dd79c0612123a25504bac2`를 받아 `bash Mac/build.sh`로 빌드한 출력으로 다음을 확인했다. 이 빌드 시점에는 계정 테스트나 앱 실행 출력을 받지 않았으며, 이후 별도로 전달받은 native smoke 결과는 13절에 기록한다.

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

성공 기준은 `PASS: native controls/window close-reopen...`와 종료 코드 0이다. 이후 사용자 Mac에서 통과한 결과를 13절에 기록했다. 일반 앱 실행·화면·알림·로그인 항목·Mac CLI·절전/연결 복구는 아직 별도 확인이 필요하다.

## 13. 실제 Mac native smoke 통과

2026-10-03 KST 사용자가 위 bundle의 실행 파일에 `--smoke-test`를 붙여 실행한 뒤 다음 출력을 전달했다. 재빌드 출력은 없으며 검사 대상은 12절에서 생성한 소스 `302aee8a9d2dda73c9dd79c0612123a25504bac2`의 `.app`다.

```text
PASS: native controls/window close-reopen, temporary SQLite, four events/notes, statistics, quota countdown; no account/network/settings changes.
검사 종료 코드: 0
```

### 확인한 범위

- native 메뉴바와 상태/통계 창 생성, 창 닫기 후 메뉴바 동작으로 재열기, Visible 상태와 창 수명 유지.
- 실제 `.app` 실행에서 새 GUID 임시 SQLite DB에 Success/Slow/Error/Interrupted 및 메모 저장·읽기.
- 통계 표본수 `n=4`와 빈 시간대 `No data`, 통계 창 갱신·닫기/재열기.
- 테스트 한도 데이터의 0% 표시. PASS 메시지는 quota countdown을 포함하지만 실제 서버 리셋이나 시간이 흐를 때의 카운트다운 갱신을 검증한 결과는 아니다.
- 종료 경로를 거쳐 프로세스 종료 코드 0 반환.

### 남은 범위와 다음 단계

이 smoke는 임시 DB와 테스트 한도만 사용하며 실제 HTTP·계정 CLI·브라우저·알림 권한 요청·로그인 항목 변경을 하지 않는다. 실제 Provider 응답·Mac CLI 계정 한도·알림 노출·자동 실행·절전/연결 복구·일반 사용자 DB 재시작·장기 사용·화면 가독성은 검증하지 않았다.

이번에도 README·CODE_GUIDE·BACKLOG·Mac/README와 이 기록만 갱신한다. 버전은 Windows 2.2.2 / Mac preview 0.1.0 유지, 소스·패키지·배포본·설정은 변경하지 않았다. 문서 검사 후 같은 `feature/macos-native`와 Draft PR #12에 반영하고 병합하지 않는다. 통과한 빌드/검사를 다시 실행할 필요는 없다.

다음은 `open`으로 기존 `.app`를 일반 실행해 메뉴바 클릭·화면·카운트다운과 Provider/CLI 조회 결과를 확인하는 것이다. 일반 실행은 Mac 전용 사용자 DB와 공식 상태/CLI 조회를 사용한다. 알림 권한은 사용자가 선택하고, 로그인 자동 실행은 `.app`를 고정 위치로 옮긴 뒤 별도 확인한다. 새 소프트웨어를 자동 설치하거나 Windows 인증을 복사하지 않는다.

## 14. 일반 실행 스크린샷과 CLI 탐색 진단

2026-10-03 KST 사용자가 일반 실행 상태 창의 스크린샷을 전달했다. 사용자 이미지 자체는 저장소에 추가하지 않고 다음 관찰만 기록한다.

| 관찰 | 확인 범위 |
|---|---|
| 일반 상태 창 | macOS dark appearance로 렌더링됨. Refresh/Statistics·기록 버튼과 설정 checkbox 표시 |
| Claude / Gemini | 각각 GO, Official 정상 및 관련 서비스 정상/진행 중 장애 없음으로 표시 |
| Status 시각 | Checked 20:19:52 KST / Next check 20:24:52 KST 표시. 이후 반복 조회나 버튼 클릭까지 검증한 것은 아님 |
| ChatGPT / Claude 한도 | 둘 다 공식 CLI 미발견, 성공 시각 없음, 다음 조회 예정 시각 표시. 계정 한도 수신 성공이 아님 |
| 자동 실행 | off, `.app 설치 위치 확인 필요` 표시. 로그인 항목 등록은 아직 별도 확인 전 |
| 공휴일 옵션 | 화면에서는 on으로 표시. 이번 Windows 작업에서 설정을 변경하지 않음 |

`AccountQuotaClient.FindExecutable`과 `MacCliPaths`를 확인했다. GUI 앱의 PATH와 `~/.local/bin`, `/opt/homebrew/bin`, `/usr/local/bin`에서 실행 가능한 절대 경로를 찾으며, 후보가 없으면 인증/사용량 조회 전에 실패한다. 설치 자체가 없거나 셸 전용 설치 경로가 GUI에 안 보이거나 실행 권한이 부족할 수 있어 실제 Mac의 `command -v` 결과가 필요하다. Windows에서 Mac의 설치 상태를 추정해 확정하지 않는다. 토큰·쿠키·인증 파일을 읽거나 소프트웨어를 설치하지 않았다.

상단 Schedule·US 시간과 OpenAI 제목은 이 첫 스크린샷에 보이지 않아 당시에는 판정하지 않았다. 한도 상자의 큰 빈 영역은 기존 고정 높이(108/111 point)와 관련됐다. 이후 CLI 설치 후 화면과 메뉴바를 받아 15절에서 배치 문제를 확인하고 수정했다.

위 표는 CLI 설치 전 관찰이다. 현재 한도 조회가 여전히 실패한다고 해석하거나 CLI 진단을 반복하지 않는다. 일반 창 표시만으로 모든 기능의 회귀 검증 완료나 PR 병합 승인으로 판단하지 않는다.

## 15. CLI 조회 성공과 0.1.1 한 화면 배치 수정

2026-10-03 KST 사용자가 CLI 설치 후 정상 조회된다고 알리고 상태 창의 상단/하단 및 메뉴바 스크린샷을 전달했다. 계정 인증 파일이나 사용자 DB를 읽지 않고 화면과 현재 소스를 비교했다. 이미지 파일은 GitHub에 추가하지 않는다.

### 사용자 화면에서 확인한 기존 0.1.0

| 항목 | 관찰과 한계 |
|---|---|
| Schedule | FULL THROTTLE, 월 22:00 KST 다음 전환, US DST ET UTC-4 / PT UTC-7, Weekend / Extended 표시 |
| 공식 상태 | OpenAI·Claude·Gemini 모두 GO / Official 정상 |
| ChatGPT | Work/Codex 주간 91% 남음, 성공 10-03 20:29 / 다음 10-04 02:29 KST |
| Claude | 세션 91%, 주간 전체 63%, 주간 Fable 98% 남음, 성공 20:29 / 다음 21:54 KST |
| 조회 주기 | Claude 다음 조회는 화면의 약 1시간 38분 리셋 카운트다운에서 15분 전 진입과 맞는 값. 단순 1시간 주기 오류로 판단하지 않음 |
| 메뉴바 | F 아이콘, 상태 창 열기·Refresh·Statistics·Provider 기록/공식 페이지·로그인 설정·종료 메뉴 표시 |
| 로그인 옵션 | 상태 창/메뉴바에서 ON, `자동 실행 설정을 변경했습니다` 안내. 실제 재로그인 실행이나 등록 해제를 검증한 결과는 아님 |
| 문제 | 상단 Schedule·Provider와 하단 전체 한도를 동시에 보려면 전체 창을 스크롤해야 함 |

설치 CLI의 정확한 버전과 실제 OS 알림·절전/연결 복구·장기 실행은 아직 확인하지 않았다. 이 성공을 모든 계정·향후 CLI 버전에서의 동작 보장으로 확대하지 않는다.

### 수정 범위

- 원인: 860 point의 document를 628 point viewport 안에 넣은 전체 scroll 구조, 91 point의 Provider 간격과 108/111 point의 한도 상자.
- 전체 document scroll을 제거하고 **430×660 point** 고정 AppKit 창에 필수 정보를 배치했다. 기존 창 높이 720보다 60 point 작다. 카운트다운과 다음 전환, Checked/Next check는 각각 같은 행에 둔다.
- Provider 간격은 62 point, 이유는 두 줄로 제한하되 Tooltip과 상태 페이지 링크로 전체 내용을 확인한다. 기록 버튼은 유지한다.
- 한도 상자는 폭 402 / 높이 54·74 point, inset 2 point. 일반 Codex 2개 창+조회 정보(3줄)와 Claude 3개 창+조회 정보(4줄)를 위한 공간을 두며 추가 모델별 행/긴 오류만 해당 상자 내부에서 스크롤한다. 한도 행·원문 캐시·조회 시각을 삭제하지 않는다.
- 긴 공휴일·피드백·자동 실행 상태의 원문은 Tooltip에 보존한다. 자동 실행 승인/위치 안내의 짧은 제목만 적용하고 등록 로직/설정 값은 바꾸지 않는다.
- 메뉴바·Refresh·Statistics·기록/페이지 연결·조회 주기·Schedule 정책·SQLite schema 2·의존성은 유지한다. Windows 2.2.2 dist와 설정은 변경하지 않는다.
- Mac만 0.1.1로 올렸다. csproj의 Version/AssemblyVersion/FileVersion과 plist의 0.1.1 / bundle build 2를 맞춘다. Windows 안정판 버전은 유지한다.

### 검증

| 현재 Windows 환경에서 수행 | 결과 |
|---|---|
| `build.ps1` Release / warnings-as-errors | 경고 0 / 오류 0, exit 0 |
| Windows 자체 검사 | 250,750 assertions, exit 0 |
| 공통 검사 Release / warnings-as-errors | 244,347 assertions, exit 0 |
| Mac 참조 C# API + trim analyzer | 경고 0 / 오류 0, exit 0; `.app` 생성이나 native 실행은 아님 |
| 기존 Windows dist | SHA-256 `56250BAF25C192314B513C02977A2A40A1E1D72836C43B0CF20803C2F98E2A10`, 변경 없음 |

native smoke에 `VerifyCompactLayout`을 연결했다. 전체 화면 scroll 부재, 모든 root 컨트롤의 창 경계/겹침, 표준 한도 행을 실제 `NSLayoutManager`로 계산한 높이와 viewport를 비교한다. 기존 임시 DB·통계·창 닫기/재열기 검사는 유지하고, 테스트 시각 1분 경과에서 한도 문자열이 00:15:00→00:14:00으로 줄어드는 검사도 추가했다. 이 새 검사를 Windows에서 실행한 것은 아니며 **이후 실제 Mac에서 통과한 결과는 18절**에 별도로 기록한다.

수정 파일: `Mac/MacStatusWindow.cs`, `Mac/MacApplication.cs`, `Mac/AiBurgerClock.Mac.csproj`, `Mac/Info.plist`, `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`, `Mac/README.md`, `MACOS_PORT.md`. 새 패키지/소스 파일은 없고 Git 기준점은 작업 전 `bee047f0a67249df67231efa72c69651deba1345`다. 로그·빌드 출력·사용자 DB는 커밋하지 않는다.

### 다음 Mac 확인

현재 앱을 메뉴바 → 종료로 닫고 기존 저장소 루트에서 실행한다. 이미 켜 둔 로그인 자동 실행 옵션과 `.app` 경로는 변경하지 않는다.

```sh
git pull --ff-only
bash Mac/build.sh
```

빌드가 통과한 경우에만 새 bundle의 검사를 실행한다.

```sh
"Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app/Contents/MacOS/AI Burger Clock" --smoke-test
echo "검사 종료 코드: $?"
```

`compact one-screen layout/standard quota rows`가 포함된 PASS와 종료 코드 0을 확인한 뒤 `open`으로 실행해 모든 주요 정보가 함께 보이는지 확인한다. 이 배치 수정본의 재빌드와 새 native smoke는 이후 성공했으며 결과는 17·18절, 일반 상태 창의 한 화면 표시는 19절에 기록한다. 실제 알림/재로그인·절전 복귀는 아직 미완료다. 같은 feature 브랜치와 Draft PR #12에서 진행하며 병합하지 않는다.

## 16. Xcode 선택 경로와 조용한 빌드 중단 보완

2026-10-03 KST 사용자가 소스 `91c32c67e277a5550efd77ca667654ca9cac67b3`을 받은 뒤 빌드 결과가 나오지 않는다고 알렸다. 실제 trace는 `xcodebuild -version`의 빈 출력과 종료 코드 1에서 끝났다. 공통 검사·앱 컴파일을 시작하기 전이므로 새 0.1.1의 빌드 실패를 C# 오류나 앱 실행 실패로 판정하지 않는다.

### 확인된 두 원인

- 환경: Apple 오류는 현재 개발 도구 경로 `/Library/Developer/CommandLineTools`가 전체 Xcode가 아니라는 내용이었다. Xcode 앱 자체의 누락·라이선스 문제나 선택 경로가 바뀐 이유는 이 오류로 확정하지 않는다.
- 스크립트: `task_xcode_version="$(/usr/bin/xcodebuild -version 2>/dev/null)"`가 Apple 오류를 버렸고, `set -e`가 첫 안내 출력보다 먼저 종료했다.

이후 사용자가 `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer`를 **버전 조회 한 번에만** 적용한 출력에서 Xcode 27.0 / build 27A266a를 확인했다. 따라서 전체 Xcode는 이 경로에 있다. 사용자는 별도로 Command Line Tools 27을 새로 설치했다고 알렸으나 재설치 후 기본 선택 경로 출력은 받지 않았다. 재설치가 기본 선택을 고쳤다고 가정하지 않는다.

### 수정과 경계

`Mac/build.sh`의 시작·Xcode 경로/버전·SDK·workload 단계를 즉시 표시한다. 명령 조회 실패는 원래 stderr와 캡처한 stdout을 보존해 출력하고 명시적 종료 코드 2로 끝낸다. 성공하면 기존 캐시·공통 검사·Release·서명/SQLite 검사로 이어진다.

Xcode 설치·전역 `xcode-select` 변경·라이선스 승인은 하지 않는다. 이미 확인된 전체 Xcode 경로를 호출 시 `DEVELOPER_DIR`로 지정하는 대안을 안내하며 이 실행과 자식 프로세스에만 적용한다. 새 의존성이나 C#·DB schema·UI·한도 조회·배포본 변경은 없다. Windows 2.2.2와 미출시 Mac preview 0.1.1 버전은 그대로다.

수정 파일은 `Mac/build.sh`, `Mac/README.md`, `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`, 이 기록 문서다. Git 기준점은 위의 `91c32c6`이며 동일 feature 브랜치/Draft PR #12에 반영한다. 검사 도구와 산출물은 Git에서 제외한 `artifacts/mac-port/`에만 둔다.

### 이번 로컬 검증

- Git Bash에서 `bash -n Mac/build.sh` 구문 검사 통과.
- 실제 스크립트를 source한 격리 fixture에서 Xcode 경로 조회 실패, Xcode 버전 조회 실패/원래 Apple 오류 보존, 잘못된 Xcode 버전, SDK 조회 실패/stdout 보존, workload 조회 실패/stdout 보존, macos workload 없음, 정상 사전 확인의 **7개 경우** 통과.
- 실패 경우는 종료 코드 2이고 공통 검사에 도달하지 않는다. 정상 경우는 가짜 `dotnet run` 경계까지 도달한 뒤 의도적 종료 코드 88로 멈춰 실제 검사·컴파일·네트워크·계정 접근을 막았다. 이 88은 fixture에서만 쓰는 값이며 제품 스크립트 성공 코드가 아니다.
- `build.sh`의 UTF-8 no BOM/LF와 `git diff --check` 확인. C# 변경이 없으므로 이전 15절의 전체 앱 검사를 불필요하게 반복하지 않는다.

이 단계의 로컬 결과는 실제 macOS 빌드가 아니다. 이후 사용자 재빌드가 성공했으며 17절에 구분해 기록한다. 아래는 당시 안내한 재빌드 절차이고, 이미 성공한 설치에서 반복할 필요는 없다.

```sh
git pull --ff-only
/usr/bin/env DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer /bin/bash Mac/build.sh
```

빌드 성공 이후에만 15절의 새 bundle native smoke와 일반 실행으로 이어진다. Command Line Tools를 다시 설치하거나 전역 설정을 바꾸는 단계는 이 대안에 필요하지 않다. [Apple 실행별 개발 도구 선택 안내](https://developer.apple.com/documentation/xcode/configuring-command-line-tools-settings).

## 17. 0.1.1 실제 Mac Release 재빌드 성공

2026-10-03 KST 사용자가 `git pull --ff-only`로 `91c32c6`에서 **`9f805f8fe32d69f088170744cd1cbc7e6ac3ab4d`**를 받은 뒤 실행별 `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer`를 지정한 `Mac/build.sh` 전체 출력을 전달했다. 새 compact layout 코드가 포함된 0.1.1의 실제 Mac 빌드 결과이며, 앞선 0.1.0 성공 기록과 구별한다.

| 항목 | 사용자 출력에서 확인한 결과 |
|---|---|
| 도구 | Xcode 27.0 / build 27A266a, .NET SDK 10.0.401, macos workload 사전 확인 통과 |
| 해당 실행의 개발 경로 | `/Applications/Xcode.app/Contents/Developer`; 기본 전역 선택 변경 확인은 아님 |
| NuGet HTTP cache | 저장소의 `artifacts/mac-build/nuget-http-cache` 사용 |
| 실제 Mac 공통 검사 | `PASS ALL SHARED: 244,347 assertions` |
| native Release | `net10.0-macos27.0` / `osx-arm64`, `-warnaserror` 성공, 경고·오류 출력 없음; 전체 빌드 18.2초 |
| 로컬 서명 | `codesign --verify --deep --strict` 다음의 SQLite·Bundle 안내까지 도달하여 통과 확인 |
| SQLite | bundle의 `libe_sqlite3.dylib`, Mach-O 64-bit / arm64 포함 |
| 기존 빌드 오류 | 조용한 Xcode 중단·NU1900 캐시 접근 거부·JSON IL2026 재발 없음 |

공통 검사는 Schedule/DST·미국 공휴일·가짜 HTTP 공식 상태·권고/알림 정책·SQLite/실측/통계 1만 건·캐시·한도 파서/조회 주기·가짜 CLI 모니터/재시작/취소·Mac 경로/IANA를 포함한다. 실제 Provider 장애 조회나 계정 조회를 이번 빌드에서 검증한 것은 아니다.

생성된 최종 bundle:

```text
/Users/hydron/ai-burger-clock/Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app
```

빌드는 정상 완료됐다. 이 빌드 결과를 받은 시점에는 native smoke가 아직 다음 단계였으며, 이후 새 native smoke가 통과한 결과는 18절에 기록한다. 당시 안내한 아래 검사는 이미 생성된 bundle을 사용하므로 문서 업데이트나 재빌드가 필요하지 않았다.

```sh
"/Users/hydron/ai-burger-clock/Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app/Contents/MacOS/AI Burger Clock" --smoke-test
echo "검사 종료 코드: $?"
```

`compact one-screen layout/standard quota rows`를 포함한 PASS / 종료 코드 0을 확인하면 일반 실행으로 주요 정보의 동시 표시와 가독성을 확인한다. 이후 일반 화면까지 확인한 결과는 19절에 기록한다. 실제 알림·재로그인·절전 복귀·장기 사용은 계속 별도 항목이다. 이번 기록 갱신은 Markdown 문서만 변경하며 Windows 2.2.2 / Mac 0.1.1 버전, C#·DB·설정·배포본은 바꾸지 않는다. OS 동작 검증은 아직 남아 있어 Draft PR #12를 유지하며 main 병합은 하지 않는다.

## 18. 0.1.1 새 native smoke 통과

2026-10-03 KST 사용자가 17절에서 만든 bundle에 `--smoke-test`를 붙여 실행한 결과를 전달했다. 검사 대상 소스는 **`9f805f8fe32d69f088170744cd1cbc7e6ac3ab4d`**, Mac preview 0.1.1이다. 문서 갱신 때문에 다시 빌드한 결과가 아니며 이전 0.1.0 검사 성공을 대신 기록한 것도 아니다.

```text
PASS: native controls/window close-reopen, compact one-screen layout/standard quota rows, temporary SQLite, four events/notes, statistics, injected quota countdown; no account/network/settings changes.
검사 종료 코드: 0
```

실제 AppKit 메뉴바·상태/통계 창의 생성과 닫기/재열기, 전체 상태 창 스크롤 부재·컨트롤 경계/겹침, 일반 Codex 3줄/Claude 4줄의 실제 글꼴 높이를 확인했다. GUID 임시 SQLite의 4종 이벤트/메모·표본수/No data, 가짜 0% 한도와 주입 시각 1분 경과 카운트다운도 통과했다.

계정 CLI·HTTP·인증 파일·브라우저·알림 권한 요청·로그인 항목 설정 변경을 하지 않는 검사다. 따라서 실제 계정 조회, 실제 한도 리셋·시간 경과, OS 알림 노출·재로그인 자동 실행·절전 복귀를 새로 검증한 것으로 해석하지 않는다.

다음으로 이미 만든 bundle을 일반 실행하도록 안내했다. 이 실행은 Mac 전용 사용자 DB와 기존 설정에 따른 공식 상태/설치 CLI 조회를 사용한다. 이후 새 일반 화면을 받아 주요 정보가 함께 보이는 것을 확인한 결과는 19절에 기록한다.

```sh
open "/Users/hydron/ai-burger-clock/Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"
```

새 코드·패키지·버전·DB schema·Windows dist 변경 없이 README·CODE_GUIDE·BACKLOG·Mac 안내와 이 기록만 갱신한다. Draft PR #12는 남은 OS 동작 검증 대기 상태로 유지한다.

## 19. 0.1.1 일반 상태 창과 메뉴바 확인

2026-10-03 KST 사용자가 0.1.1의 새 native smoke 통과 후 일반 앱의 전체 상태 창과 메뉴바 스크린샷을 전달했다. 검사/빌드 대상과 같은 `9f805f8` bundle이며 이미지는 관찰 근거로만 읽고 GitHub에는 올리지 않는다.

전체 창에서 Schedule·전환 카운트다운/다음 전환·US DST/ET/PT offset·Weekend 표시, OpenAI/Claude/Gemini 세 Provider의 GO/Official 정상·이유·기록 버튼, ChatGPT 제목/Work-Codex 한도와 Claude 세션/주간 전체/모델별 한도, 마지막/다음 조회·Refresh/Statistics·공휴일/자동 실행 checkbox를 **상하 스크롤 없이 함께 확인**했다. 기존 0.1.0처럼 상단과 하단을 나눠 확인해야 하는 문제는 이 일반 화면에서 재현되지 않았다.

메뉴바의 F 아이콘, 현재 Schedule·카운트다운, 상태 창/Refresh/Statistics, Provider 기록/공식 상태 링크, 공휴일·로그인 자동 실행·로그인 항목 설정·종료 메뉴도 표시됐다. 공휴일·로그인 자동 실행은 상태 창과 메뉴바 모두 체크 상태였다. 자동 실행 체크가 보인다는 사실만으로 실제 재로그인 실행까지 검증한 것으로 보지 않는다.

새 일반 화면에서 두 계정 한도와 성공 조회 시각도 표시되었다. 이번 화면의 계정 사용률 수치는 추가로 공개 기록하지 않으며 스크린샷·계정 데이터 파일도 업로드하지 않는다. 정상/GO 표시 역시 해당 시점의 앱 화면 관찰이며 별도의 장애 시나리오 재검증은 아니다.

이번 배치 수정의 **실제 Mac Release·공통 검사·새 native smoke·일반 한 화면 표시 확인을 완료**했다. 실제 링크/기록 메뉴/Tooltip 조작, 일반 사용자 DB 재시작·OS 알림·재로그인·절전/연결 복구·장기 사용은 별도 항목이다. 추가 코드 변경이나 재빌드는 하지 않고 확인 결과만 동일 feature 브랜치/Draft PR #12와 안내 문서에 반영한다.

## 20. 0.1.2 메뉴바 아이콘 크기·색상 수정과 다중 모니터 재검증

2026-10-03 KST 사용자는 듀얼 모니터에서 다른 아이콘은 양쪽 메뉴막대에 흰색으로 보이지만 Burger Clock만 활성 모니터에 표시된다고 알렸다. 비활성 모니터에서는 실제로 아이콘이 없다고 추가 확인했으므로 단순 대비 부족으로 설명하지 않는다. 메뉴막대 이미지에서도 주변보다 작은 검은 F가 보였고 크기 확대를 요청했다. 이미지는 관찰 자료로만 읽으며 저장소에 올리지 않는다.

### 확인한 코드와 수정

- 기존 코드는 `f.circle.fill`/`b.circle.fill` 시스템 심볼과 `ContentTintColor`를 사용했지만 화면에서는 작은 검은 F가 보였다. 심볼 부재 시 title fallback인지, tint/메뉴막대 복제 문제인지 native 관측 없이 확정하지 않는다. 모니터 포커스에 따라 항목을 숨기는 코드도 없다.
- 새 `MacStatusIcon`은 20-point 이미지에 지름 19-point 상태색 원과 중앙의 흰색 F/B를 CoreGraphics/CoreText로 직접 그린다. 바깥 배경은 투명하다. 20×20px와 40×40px 표현 모두 논리 크기 20×20 point이며 `Template=false`다. 색상은 기존 `TrayPresentation`을 그대로 사용한다.
- 표준 정사각 status item을 만들고 `ImageOnly`/`NSImageScale.None`으로 표시한다. 버튼 tint・시스템 심볼에는 의존하지 않는다. 화면별 중복 항목이나 모니터 polling・OS 설정 변경은 추가하지 않는다.
- 앱이 최종 NSImage를 보유하고 상태색/문자가 바뀔 때만 교체한다. 새 그림을 버튼에 넣은 뒤 이전 그림을 Dispose하고 종료 때 마지막 그림도 정리한다. 공식 retain 계약상 기존 `using`만으로 원인을 확정할 수는 없다.
- 새 native smoke는 두 문자×네 색의 1x/2x 픽셀 RGB・alpha・흰 글자・크기와 버튼 이미지 보존을 검사한다. 기존 창・임시 SQLite・4종 이벤트・메모・통계・카운트다운 검사를 유지한다. 새 PASS에는 `20pt color menu icon/1x-2x pixels`가 포함된다.
- Mac 버전만 0.1.2 / bundle build 3으로 맞췄다. Windows 2.2.2・공통 원본・DB schema 2・NuGet 의존성・조회 주기・자동 실행 경로・dist는 변경하지 않는다.

기존 수정 파일은 `Mac/MacApplication.cs`, `Mac/AiBurgerClock.Mac.csproj`, `Mac/Info.plist`, `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`, `Mac/README.md`, `MACOS_PORT.md`이며 새 소스는 `Mac/MacStatusIcon.cs`다. SDK-style Mac 프로젝트가 새 파일을 자동 포함하고 Windows 프로젝트는 기존 `Mac/**/*.cs` 제외 규칙을 유지한다. 작업 전 Git 기준점은 `be29b06421007cda935b4849869857eaeb0faecd`다. 검증 소스는 동일 `feature/macos-native` / Draft PR #12의 이번 변경이며, GitHub 갱신 때 실제 커밋을 확인한다. 기존 배포본 교체가 없으므로 사용자 DB・EXE 백업 작업도 하지 않는다.

### 여기서 수행한 검증

SDK 10.0.401, Windows 환경:

| 검사 | 결과 |
|---|---|
| Windows Release, warnings-as-errors | 경고 0 / 오류 0 / exit 0 |
| Windows 자체 검사 | 250,750 assertions / exit 0 |
| net10.0 공통 검사 | 244,347 assertions / exit 0 |
| 공식 Microsoft.macOS 27.0.10722 참조 API + trim analyzer | Mac 호스트 6개 + 공통 19개, 경고 0 / 오류 0 / exit 0 |

로그 위치는 Git에서 제외한 `artifacts/mac-icon/`이며 실제 계정・사용자 DB・레지스트리・Mac 설정・앱 배포본은 건드리지 않는다. 참조 컴파일에서 발견한 RGB 인수의 double/NFloat 불일치는 float 입력으로 수정하고 일반 NuGet audit 설정으로 다시 컴파일해 통과했다. Mac workload를 설치하거나 참조 DLL을 실행하지 않았다.

### 남은 실제 Mac 확인

이 수정의 최초 준비 당시에는 실제 Mac 빌드와 native pixel smoke를 실행하지 않았다. **이후 21절에서 0.1.2의 실제 `.app` 빌드, 22절에서 새 native pixel smoke PASS, 23절에서 일반 컬러 아이콘과 양쪽 메뉴막대 표시를 확인했다.** 앞선 0.1.1 성공과 별도의 새 실행 근거다. 사용자 Mac에서 누락 해소는 확인했지만 정확한 원인까지 확정하지는 않는다.

1. 완료: 소스 `dd3f596`에서 기존과 같은 bundle 경로로 0.1.2를 빌드했다. 전체 Xcode 경로는 실행별 `DEVELOPER_DIR`로 지정했다. 문서-only 후속 갱신 때문에 다시 빌드하지 않는다.
2. 완료: 새 bundle의 `--smoke-test`가 위 새 PASS 문구를 출력했다. shell 종료 코드의 별도 출력은 없었으며 실제 메뉴막대 확인은 이 검사와 구분한다. 계정・HTTP・설정 변경 없는 임시 데이터 검사다.
3. 아이콘 확인 완료: 일반 앱의 커진 초록색 원/흰색 F와 양쪽 메뉴막대 표시를 사용자 화면과 직접 확인으로 검증했다. 메뉴 클릭/링크 조작은 별도 항목이며 이번 화면만으로 새 검증 완료로 바꾸지 않는다.
4. 이번 아이콘 문제는 사용자 Mac에서 검증 완료다. 별도 OS 알림・재로그인・절전/연결 복구・장기 사용과 PR 병합은 아직 완료하지 않았으므로 Draft PR #12는 유지한다.

공식 API 근거: [Retina 이미지 표현](https://developer.apple.com/library/archive/documentation/GraphicsAnimation/Conceptual/HighResolutionOSX/Optimizing/Optimizing.html), [NSImageRep logical size](https://developer.apple.com/documentation/appkit/nsimagerep/size), [representation retain](https://developer.apple.com/documentation/appkit/nsimage/addrepresentation(_:)), [사용 SDK 바인딩](https://github.com/dotnet/macios/blob/d813e2baef17cd3a2bb5adc1e37610258d01cda3/src/appkit.cs). 이 계약은 실제 메뉴막대 복제 버그 해결을 보증하지 않는다.

## 21. 0.1.2 실제 Mac Release 빌드 성공

2026-10-03 KST 사용자가 `git pull --ff-only`로 소스 **`dd3f596bf58f40016b2095867de428bf0b9b80a9`**를 받은 뒤 `Mac/build.sh`의 전체 결과를 전달했다. 새 `MacStatusIcon.cs`와 0.1.2 버전이 들어 있는 실제 Mac 빌드이며, 앞선 0.1.1의 성공이나 Windows 참조 컴파일과 구별한다.

| 확인 항목 | 실제 출력에서 확인한 결과 |
|---|---|
| 개발 도구 | 실행별 `/Applications/Xcode.app/Contents/Developer`, Xcode 27.0 / build 27A266a |
| .NET | 안정판 SDK 10.0.401, macos workload 사전 확인 통과 |
| NuGet 캐시 | 기존 빌드 전용 `artifacts/mac-build/nuget-http-cache` 사용 |
| 공통 검사 | `PASS ALL SHARED: 244,347 assertions` |
| native Release | `net10.0-macos27.0` / `osx-arm64`, warnings-as-errors 성공, 경고·오류 출력 없음, 전체 빌드 16.4초 |
| bundle/서명 | 최종 `.app` 경로 출력. `set -e` 스크립트가 `codesign --verify --deep --strict` 이후 단계까지 정상 도달 |
| SQLite | bundle의 `libe_sqlite3.dylib`, Mach-O 64-bit dynamically linked shared library / arm64 |

이 빌드 출력으로 `.app` 생성·서명·ARM64 SQLite 포함을 확인했다. 빌드만으로 새 아이콘 픽셀·실제 메뉴막대 외관·비활성 모니터 표시를 검증한 것은 아니다. 픽셀 검사의 별도 실행 결과는 이후 22절에 기록한다. 빌드 스크립트의 공통 검사는 가짜 HTTP/CLI와 임시 SQLite를 사용하며 실제 계정 조회 성공이나 사용자 DB 동작 확인으로 확대하지 않는다.

현재 bundle은 `/Users/hydron/ai-burger-clock/Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app`이다. 앱이 실행 중이면 종료하고 이 bundle의 `--smoke-test`를 실행한다. `20pt color menu icon/1x-2x pixels`가 들어 있는 PASS와 종료 코드 0을 받은 뒤 일반 실행의 외관·양쪽 모니터 표시를 확인한다. 이 단계에서 코드·계정·로그인 설정을 자동 변경하지 않는다.

최초 검사 시도는 실행 명령 뒤에 `echo` 명령이 같은 줄로 붙어 지원 옵션 안내만 출력했다. native smoke가 시작된 결과가 아니므로 성공/실패 검증으로 집계하지 않는다. 실행 명령만 별도 한 줄로 다시 요청했다.

이 빌드 결과를 기록한 후속 커밋은 문서만 갱신했다. Windows 2.2.2 / Mac 0.1.2 버전, C#·DB·배포본은 그대로여서 bundle을 다시 빌드하지 않았다. 당시 다음 단계였던 native pixel smoke는 이후 22절에서 PASS했으며 다중 모니터와 별도 OS 동작 확인이 남아 Draft PR #12를 유지한다.

## 22. 0.1.2 아이콘 native smoke PASS

2026-10-03 KST 사용자가 21절에서 생성한 bundle의 실행 명령만 별도 한 줄로 실행해 다음 출력을 전달했다. 재빌드나 추가 코드 변경 없이 새 `dd3f596` 아이콘 코드의 실제 Mac 실행 근거를 받았다.

```text
PASS: native controls/window close-reopen, 20pt color menu icon/1x-2x pixels, compact one-screen layout/standard quota rows, temporary SQLite, four events/notes, statistics, injected quota countdown; no account/network/settings changes.
```

두 Schedule 문자×네 상태색의 1x/2x RGB·투명도·흰 글자 픽셀·논리/픽셀 크기와 버튼 이미지, compact layout·창 재열기·임시 SQLite·4종 이벤트/메모·통계·주입 카운트다운 검사를 통과했다. shell의 종료 코드 숫자는 별도로 출력되지 않았다. 소스의 PASS 경로는 `ExitCode = 0`으로 설정하지만 이를 별도로 관측한 종료 코드로 기록하지 않는다.

**일반 메뉴막대의 외관·모니터별 항목 표시·실제 OS 알림/재로그인/절전 복귀 검증은 아니다.** 검사에서는 HTTP·계정 CLI·사용자 DB·로그인 항목을 사용하거나 변경하지 않았다. 실제 계정 수치를 추가로 공개하지 않는다.

이 PASS 이후 일반 실행의 외관·양쪽 메뉴막대를 별도로 확인했고 그 결과는 23절에 기록한다. 새 실패나 소스 변경 없이 native smoke·빌드 검사를 반복하지 않는다. 이번에도 문서와 기존 Draft PR #12의 기록만 갱신하며 Windows 2.2.2 / Mac 0.1.2 버전·C#·DB·배포본은 변경하지 않는다.

## 23. 0.1.2 실제 컬러 아이콘과 양쪽 메뉴막대 확인

2026-10-03 KST 새 native smoke PASS 후 사용자가 일반 실행 스크린샷과 "정상적으로 아이콘 표시되는 것 확인....양쪽다 표시됨"이라는 확인을 전달했다. 새 코드를 다시 수정하거나 빌드하지 않고 21·22절의 0.1.2 bundle을 사용한 후속 확인이다.

- 스크린샷에서 주변 아이콘과 비슷한 크기의 초록색 원과 중앙의 흰색 F를 확인했다. 작은 검은 F였던 종전 모습과 구별된다.
- 양쪽 모니터 메뉴막대 표시를 사용자가 직접 확인했다. 첨부 한 장은 한 메뉴막대의 외관 근거이며 양쪽 표시의 근거는 사용자 확인이다.
- 사용자 Mac mini / macOS 27.0.1 / arm64 환경에서 기존 크기·색상·비활성 모니터 누락 문제의 해소를 확인했다. 정확한 내부 원인을 이미지 수명 하나로 확정하거나 모든 Mac 환경에 일반화하지 않는다.
- 일반 화면에서 관찰한 상태는 초록색 F다. B·주황·빨강·회색의 실제 서비스 전환을 이번 화면으로 검증한 것은 아니며 8개 문자/색 조합은 22절의 주입 native 검사로 구분한다.

이번 요청의 아이콘 수정은 이 환경에서 검증 완료다. 스크린샷 원본·계정 값은 GitHub에 올리지 않고 확인 사실만 문서와 기존 Draft PR #12에 기록한다. Windows 2.2.2 / Mac 0.1.2 버전, 앱 코드·의존성·사용자 DB·자동 실행 설정·배포본은 바꾸지 않으며 재빌드·검사 반복은 필요 없다. 실제 OS 알림·재로그인·절전/연결 복구·장기 사용과 PR 병합은 별도 남은 항목이다.

## 24. 0.1.2 bundle 버전 미반영 수정과 첫 로컬 Mac 검증

2026-10-03 KST부터 사용자 Mac mini(macOS 27.0.1 / arm64)에서 Claude Code로 직접 빌드·검사한다. 이전 절까지는 사용자가 전달한 출력을 기록했지만, 이 절은 **같은 Mac에서 명령을 직접 실행하고 종료 코드를 관측한 결과**다. 저장소는 GitHub `hydron75` 계정 인증으로 push까지 가능함을 `git push --dry-run`으로 확인했다.

### 발견한 문제

작업 전 HEAD `ddae35d780c7c83bfa81e6d6026894122822600c`를 그대로 빌드한 bundle의 `Contents/Info.plist`는 **`CFBundleShortVersionString` 0.1.0 / `CFBundleVersion` 1**이었다. 소스의 `Mac/Info.plist`는 0.1.2 / 3이었고, DLL·실행 파일·`_CodeSignature`는 새로 갱신됐지만 `Info.plist`와 `obj/.../AppManifest.plist`만 0.1.0 첫 빌드 시각에 머물러 있었다.

- 원인: Microsoft.macOS.Sdk 27.0.10722의 `_CompileAppManifest`는 `_CompileAppManifest.inputs`(MSBuild 속성 목록)·글꼴·partial manifest만 Inputs로 쓴다. 원본 `Info.plist` 파일은 Inputs에 없어서, plist의 버전만 바꾼 증분 빌드는 "최신 상태"로 건너뛴다. `-v:d` 빌드 로그에서 `_CompileAppManifest`와 `_WriteAppManifest`가 모두 건너뛰어지는 것을 확인했다.
- 영향: 같은 `bin/Release` 경로를 재사용한 **0.1.1·0.1.2 bundle은 Finder·시스템 정보에서 0.1.0 / 1로 표시됐을 것**이다. 17·21절의 빌드 출력은 버전 값을 출력하지 않아 당시에는 드러나지 않았다. plist에서 바뀐 키는 두 버전 값뿐이므로 아이콘·한 화면 배치·조회 등 C# 코드의 검증 결과에는 영향이 없다. 실측 기록의 앱 버전은 assembly 버전(0.1.2)을 쓰므로 DB에도 영향이 없다.

### 수정 (소스 `66fb8689043154e8bf6b1fafe04f9c317b2f0ec1`)

| 파일 | 변경 |
|---|---|
| `Mac/AiBurgerClock.Mac.csproj` | `ApplicationDisplayVersion=$(Version)`, `ApplicationVersion=3` 추가. SDK가 inputs 파일에 기록하는 속성이라 값이 바뀌면 manifest를 다시 만든다. 버전은 csproj 한 곳에서 관리한다 |
| `Mac/Info.plist` | `CFBundleShortVersionString`·`CFBundleVersion` 제거 |
| `Mac/MacApplication.cs` | native smoke가 `NSBundle.MainBundle`의 `CFBundleShortVersionString`과 assembly 버전을 비교하고, 다르면 FAIL. PASS 문구 앞에 `bundle version` 추가 |

Mac 버전은 **0.1.2 / build 3 그대로**다. 원래 의도한 값이 이번에 실제 bundle에 반영된 것이며 기능 변경은 없다. Windows 2.2.2·공통 원본 19개·DB schema·NuGet 의존성·자동 실행 경로는 바꾸지 않았다. 새 소스 파일이 없어 CODE_GUIDE의 파일 수도 그대로다.

### 직접 수행한 검증

Xcode 27.0 / build 27A266a(실행별 `DEVELOPER_DIR`), .NET SDK 10.0.401(`/usr/local/share/dotnet`), macos workload 27.0.10722. 실행 중이던 일반 앱은 정상 종료(Quit) 후 검사했고 검사 뒤 새 bundle로 다시 실행했다.

| 검사 | 수정 전 `ddae35d` | 수정 후 `66fb868` |
|---|---|---|
| `Mac/build.sh` 종료 코드 | 0 | 0 (clean 없이 증분 빌드) |
| 공통 검사 | `PASS ALL SHARED: 244,347 assertions` | 같음 |
| native Release | 경고 0 / 오류 0, 15.6초 | 경고 0 / 오류 0, 14.8초 |
| `codesign --verify --deep --strict` | 통과 | 통과 |
| SQLite | `libe_sqlite3.dylib` Mach-O 64-bit arm64 | 같음 |
| bundle 버전 | **0.1.0 / 1** | **0.1.2 / 3** |
| `--smoke-test` | PASS, 종료 코드 0 | `PASS: bundle version, native controls/...`, **종료 코드 0** |

음성 검사: 수정 후 bundle을 scratch 폴더에 복사해 `CFBundleShortVersionString`을 0.1.0으로 바꾸고 ad-hoc 재서명한 뒤 실행하자 `FAIL: Bundle version 0.1.0 does not match app version 0.1.2.` / 종료 코드 1이 나왔다. 복사본은 검사 뒤 삭제했다. 로그는 Git에서 제외한 `artifacts/mac-local/`에 있다.

같은 자리에서 읽기 전용으로 로그인 항목 등록도 확인했다. `sfltool dumpbtm`에 `com.hydron75.aiburgerclock`가 `enabled, allowed`로 등록돼 있다. 등록 상태 확인일 뿐 실제 재로그인 실행 검증은 아니다.

### 남은 항목

OS 알림 노출·실제 재로그인 자동 실행·절전/연결 복구·장기 사용과 main 병합은 7절 그대로 남아 있다. Draft PR #12를 유지한다.

## 25. PR #12 코드 리뷰 지적 사항 반영

2026-10-04 KST, 별도 Claude Code 세션이 `ddae35d` 기준으로 PR #12를 코드만 읽고 리뷰해 10건을 지적했다. 이 Mac에서 현재 코드(`9a678ec`)와 대조해 10건 모두 해당함을 확인했다. Mac 파일만으로 고칠 수 있는 범위는 소스 **`50668fa2b6cf35410a2177e369dcab66562b9cd9`**에서 반영했다. 공통 원본으로 옮기는 부분(5·7번)은 Windows 파일도 바꿔야 해서 AGENTS.md의 작업 분담 규칙에 따라 보류했다.

| # | 지적 | 처리 |
|---|---|---|
| 1 | smoke 초기화 실패 catch에서 `Terminate(null)`를 바로 호출해, 초기화를 기다리는 종료 처리와 서로 기다리며 멈출 수 있음 | 종료 요청을 메인 스레드에 예약(`BeginInvokeOnMainThread`). `RunSmokeAsync`와 같은 방식 |
| 2 | DB·공휴일 설정 읽기 실패 시 `StatusMonitor`·`AccountQuotaMonitor`를 만들지 않아 세 Provider가 UNKNOWN으로 남음 | Windows처럼 공휴일 보정 OFF + "공휴일 설정 확인 실패 · 보정 OFF" 안내 후 조회는 계속. smoke에서는 그대로 실패 처리 |
| 3 | 공휴일 설정 저장 직후 `RefreshDisplay(false)`가 동시에 들어온 권고 변화를 알림 없이 소비 | `RefreshDisplay(false, notifyProviders: true)`. Windows `RefreshStatus(false, notifyProviders: true)`와 같은 규칙 |
| 4 | 1초 타이머가 기본 run-loop 모드에만 있어 메뉴바 메뉴를 연 동안 카운트다운·전환 알림이 멈춤 | `NSRunLoopMode.Common`에 등록 |
| 5 | Mac은 기본 `HttpClient` | Windows와 같은 `SocketsHttpHandler`(쿠키 끔, 연결 재사용 10분, 연결 제한 5초, gzip/deflate)와 요청 제한 `StatusMonitor.RequestTimeout`. **공통 파일로 옮기는 것은 보류** |
| 6 | 한도 표시를 매초 다시 써서 스크롤·선택이 풀림 | 내용이 바뀔 때만 다시 쓰고 내부 스크롤 위치 유지. 24시간 미만 리셋 카운트다운은 초 단위라 그 동안은 매초 바뀜(Windows와 같음) |
| 7 | 알림 문구·연결 복구 1분 제한을 Windows에서 복사, 문구가 이미 달라짐 | 문구를 Windows와 같게 맞춤. 1분 제한 값은 원래 같음. **공통 파일로 옮기는 것은 보류** |
| 8 | 버전이 csproj·`Info.plist`·코드의 `"0.1.2"` 기본값 세 곳에 있음 | `Info.plist`는 24절에서 제거. 코드 기본값을 없애고 `AppVersion` 한 곳에서 assembly 버전을 읽음 |
| 9 | 메모 2,000자, 알림 없이 잘리고 이모지 중간이 잘릴 수 있음 | Windows와 같은 1,000자(UTF-16), 문자 경계에서 자르고 잘렸으면 상태 창에 안내 |
| 10 | `--smoke-test`가 아닌 인수가 있으면 종료 코드 2로 조용히 끝남 | 일반 실행은 추가 인수를 무시. smoke는 지금처럼 `--smoke-test` 하나만 허용 |

변경 파일은 `Mac/MacApplication.cs`, `Mac/MacStatusWindow.cs`, `Mac/Program.cs`다. 새 파일·패키지·공통 원본 변경은 없고, Windows 2.2.2와 Mac 0.1.2 / 3 버전도 그대로다.

### 직접 수행한 검증

Xcode 27.0 / 27A266a, .NET SDK 10.0.401. 실행 중이던 앱은 Quit 후 검사했고 검사 뒤 다시 실행했다.

| 검사 | 결과 |
|---|---|
| `Mac/build.sh` | 종료 코드 0, 공통 검사 244,347건, native Release 경고 0 / 오류 0, 15.2초 |
| 서명·버전 | `codesign --verify --deep --strict` 통과, bundle 0.1.2 / 3 |
| `--smoke-test` | `PASS: bundle version, menu-tracking countdown timer, 1,000-char note limit, native controls/...`, **종료 코드 0** |
| 타이머 음성 검사 | 타이머를 기존 `CreateRepeatingScheduledTimer`로 임시로 되돌리면 smoke가 `FAIL: The countdown timer stopped while a menu was tracking events.` / 종료 코드 1. 원래 코드로 복원 후 위 PASS |
| 인수 처리 | `--smoke-test echo`는 안내 출력 후 종료 코드 2. `open … --args -AppleLanguages "(ko)"` 일반 실행은 정상 시작 |

새 smoke 검사는 실제 메뉴를 클릭하지 않는다. 대신 메인 run loop를 메뉴 추적과 같은 `EventTracking` 모드로만 돌려 타이머가 실행되는지 본다. 메모 검사는 999자+🍔(1,001 UTF-16) → 999자, 1,000자 유지, 짧은 메모 유지를 확인한다.

**실행으로 재현하지 않은 것:** 1·2번의 DB 초기화 실패, 3번의 공휴일 저장과 장애 알림이 겹치는 상황, 6번의 실제 스크롤 위치 유지, 9번의 안내 문구 표시. 실패를 주입할 경로가 없어 코드와 Windows 구현의 대조로 확인했다.

### 보류: 공통 원본으로 옮기기 (5·7번)

HTTP 설정과 알림 문구·연결 복구 제한을 루트 공통 파일로 옮기면 Windows의 `TrayApplicationContext.cs`도 바뀐다. AGENTS.md의 작업 분담 규칙에 따라 별도 PR의 "공통 원본 변경" 절로 알리고 Windows 검증을 받는 작업으로 남긴다. 지금은 두 버전의 값과 문구가 같다.

## 26. 연결 복구·절전 복귀 실제 확인

2026-10-04 KST 00:15~00:22, 사용자 Mac mini에서 소스 `cb0d42e`(코드는 `50668fa`와 같음)의 0.1.2 bundle을 일반 실행한 상태로 확인했다. 사용자가 네트워크와 잠자기를 직접 조작했다. 판정에는 다음 세 가지를 썼다.

- 시스템 로그: `configd`, `pmset -g log`
- 앱 프로세스의 `com.apple.network` 로그
- 사용자 DB 복사본: 공식 상태 `ProviderStatusCache.CheckedAtUtc`, 계정 한도 `AccountQuota.v1.*`의 `SuccessfulAtUtc`

DB는 원본을 직접 열지 않고 scratch 폴더로 복사해 읽었다. 이 Mac은 유선 USB LAN(`en8`, 기본 경로)과 Wi-Fi(`en1`)가 동시에 연결돼 있다. 정상 조회 주기는 공식 상태 5분, 계정 한도 6시간이다.

### 연결 복구

| 시각 | 네트워크 (`configd`) | 앱 반응 (DB) |
|---|---|---|
| 00:15:47.7 | Wi-Fi 끔, 유선은 연결 유지 | 00:15:48 공식 상태·계정 한도 모두 성공 |
| 00:16:09.0 | 유선 LAN 해제 → 모든 연결 끊김 | 조회 없음 (`IsAvailable=false`는 무시하는 설계) |
| 00:17:07.8 | 유선 재연결, IP·DNS 수신 | 00:17:09 공식 상태 성공. 계정 한도 성공 기록은 없음 |
| 00:18:47.0 | Wi-Fi 재연결 | 00:18:47 공식 상태·계정 한도 모두 성공 |

- **결과: 통과.** 네트워크 변화 뒤 1~2초 안에 다시 조회했다. 연속 이벤트 간격은 80초·99초로 1분 제한 안쪽에서 막힌 경우는 없었다.
- **관찰 1:** .NET의 `NetworkAvailabilityChanged`는 연결이 모두 끊겼다가 돌아올 때만이 아니라 인터페이스 하나가 바뀔 때도 `IsAvailable=true`로 발생했다. Wi-Fi만 꺼도 조회가 일어났다.
- **관찰 2:** 유선 재연결 직후의 계정 한도 조회는 성공 기록이 없다. 같은 시각(00:17:08)에 codex·claude 프로세스 로그가 몰려 있어 CLI 조회는 시도된 것으로 보인다. DNS가 막 잡힌 시점이라 실패했다고 추정한다. 실패 내용은 메모리 상태에만 있고 DB에 남지 않아 원인은 확인하지 못했다. 다음 Wi-Fi 연결 때 바로 성공했다.
- **개선 후보:** 연결 복구 직후 몇 초 기다린 뒤 조회하면 관찰 2를 줄일 수 있다. Windows도 같은 로직이라 공통 개선 후보로 BACKLOG에 남긴다.

### 절전 복귀

| 시각 | 전원 (`pmset -g log`) | 앱 반응 |
|---|---|---|
| 00:20:50 | 사용자가 Apple 메뉴 → 잠자기 | — |
| 00:21:04 | `Entering Sleep state` | — |
| 00:21:16 | `DarkWake` (Wi-Fi/네트워크 패킷), 유선 링크 재협상 → `network changed` | 앱이 네트워크 요청 (연결 복구 경로) |
| 00:22:05 | `DarkWake to FullWake … due to UserActivity` (사용자가 깨움) | 00:22:05~07 공식 상태 세 곳·계정 한도 둘 모두 성공 |

- **결과: 통과.** 화면을 깨운 직후 0~2초 안에 공식 상태와 계정 한도를 모두 다시 조회했다.
- **절전 복귀 경로로 판단한 근거:**
  - 00:22:00~00:22:15 사이 `configd`의 `network changed`가 0건이다.
  - 직전 네트워크 이벤트(00:21:16)에서 49초밖에 지나지 않아, 연결 복구 경로였다면 1분 제한에 걸렸을 시점이다.
  - 공식 상태 5분·계정 한도 6시간 주기와도 맞지 않는다.
  - 따라서 이 조회는 `NSWorkspace.DidWakeNotification` 경로로 판단한다.
- **관찰:** Mac이 잠자기 중에도 네트워크 DarkWake로 잠깐 깨어나 연결 복구 경로가 실행될 수 있다(00:21:16). DarkWake 때는 `DidWakeNotification`이 오지 않고, 사용자가 화면을 깨울 때(FullWake) 온다.

### 남은 항목

- **확인 범위:** 이번 확인은 1분 남짓의 짧은 절전과 수동 네트워크 조작 한 번씩이다. 긴 절전(수 시간), VPN 전환, 덮개 닫기는 확인하지 않았다.
- **아직 남은 OS 확인:** OS 알림 노출과 실제 재로그인 자동 실행·해제, 정상 종료 메뉴, 장기 사용.
- **변경 없음:** 이번 기록은 문서만 바꾼다. 앱 코드·버전·DB는 바꾸지 않았다.

## 27. 로그아웃 정상 종료와 재로그인 자동 실행 실제 확인

2026-10-04 KST 00:25~00:28, 사용자가 로그아웃과 다시 로그인을 두 번 했다. 대상은 26절과 같은 0.1.2 bundle(코드 `50668fa`)이다. 판정에는 `loginwindow`·`launchservicesd` 시스템 로그, 앱 PID와 시작 시각, 사용자 DB 복사본의 조회 시각을 썼다. 로그인 항목은 시험 전후 모두 `sfltool dumpbtm`에서 `enabled, allowed`였다.

| 단계 | 1회차 | 2회차 |
|---|---|---|
| 로그아웃 시작 (`StartGUIAppsQuit`) | 00:25:44.3 | 00:27:43.6 |
| 앱 종료 요청 | 00:25:47.5 (PID 29808) | 00:27:46.303 (PID 34880) |
| 앱 종료 | 로그아웃 완료(00:25:47.975) 전 | 00:27:46.35~46.46 (`QUITTING: pid=34880`) |
| 로그아웃 완료 (`LogoutComplete`) | 00:25:47.975 | 00:27:46.830 |
| 로그인 인증 완료 | 00:26:18.5 | 00:28:08.6 |
| 앱 자동 실행 | 00:26:28.3 | 00:28:09.7 (PID 35748, 시작 00:28:09) |

- **로그인 시 자동 실행: 통과.**
  - 두 번 모두 `loginwindow`가 로그인 항목 경로(`LaunchItemsInSharedFileListRef … performAutolaunch, launching: …/AI Burger Clock.app`)로 앱을 띄웠다.
  - macOS의 "윈도우 다시 열기" 재실행 목록에는 이 앱이 없었다. 따라서 이번 실행은 `SMAppService.MainApp` 로그인 항목 때문이다.
  - 사용자도 메뉴바에 앱이 다시 표시되는 것을 확인했다.
- **로그아웃 때 정상 종료: 통과.**
  - `loginwindow`는 앱을 강제 종료(kill)하지 못하는 앱으로 판정하고 quit 이벤트를 보냈다(`Could not kill AI Burger Clock, sending a quit event`). 앱은 `ApplicationShouldTerminate` → `StopAsync` 경로로 약 0.05~0.15초 만에 스스로 종료했다.
  - 로그아웃 지연·중단 메시지는 없었다. 이 결과는 일반 메뉴의 "종료"와 같은 종료 경로를 실제 OS 이벤트로 확인한 것이다.
- **시작 직후 조회: 통과.** 2회차 실행 2~5초 뒤인 00:28:12~14에 공식 상태 세 곳과 계정 한도 두 곳 모두 성공 기록이 남았다.

### 확인하지 않은 것

- 재부팅 후의 자동 실행. 이번 확인은 로그아웃과 다시 로그인뿐이다.
- 로그인 항목 해제: 사용자가 별도로 자동 실행 체크를 끄고 다시 로그인하면 실행되지 않고, 다시 켜면 실행되는 것을 확인했다고 알려 왔다(2026-10-04). 지난 1일의 `backgroundtaskmanagementd` 로그에서 이 앱의 등록 해제 기록은 찾지 못해, 시스템 로그로 따로 확인하지는 못했다.
- 앱을 다른 위치로 옮겼을 때 등록이 유지되는지.
- 로그아웃 때 진행 중인 저장 작업이 남아 있는 경우의 종료. 이번에는 대기 작업이 없을 때의 종료만 봤다.

이번 기록은 문서만 바꾼다.

## 28. 공통 원본 정리와 연결 복구 지연 조회 (별도 PR)

25절에서 보류한 공통 파일 정리와 26절의 공통 개선 후보를 `feature/macos-native` 대상의 별도 PR로 진행했다. Windows 전용 파일의 호출부는 사용자 승인에 따라 이번에 한해 Mac 담당이 수정했고, 실제 Windows 검사는 AGENTS.md 규칙대로 Windows 담당에 요청한다.

### 공통 원본 변경

| 파일 | 변경 | 동작 변화 |
|---|---|---|
| `NetworkRefreshScheduler.cs` (신규, 공통 원본 20번째) | 네트워크 변화 뒤 재조회 스케줄러 | **있음.** 아래 참조 |
| `ProviderStatusClient.cs` | `CreateHttpHandler()` / `CreateHttpClient()` 추가 | 없음. 두 OS가 이미 같은 값을 쓰던 것을 한 곳으로 옮김 |
| `TrayPresentation.cs` | `TransitionNotification()` / `ProviderNotification()` 추가 | 없음. 25절에서 맞춘 문구를 한 곳으로 옮김 |
| `MonitorTests.cs` (공통 검사) | HTTP 설정·알림 문구·스케줄러 검사 11건 추가 | — |
| `Shared/SharedSources.props` | 새 공통 원본 연결 | — |

연결 복구 조회의 바뀐 동작:

- **이전:** 네트워크가 사용 가능해지면 즉시 조회했다. 직전 조회에서 1분이 지나지 않았으면 그 변화는 버렸다.
- **이후:**
  - 변화 뒤 **5초 기다렸다가** 조회한다.
  - 기다리는 동안 또 바뀌면 마지막 변화에서 다시 5초를 센다.
  - 조회는 여전히 1분에 한 번이다. 다만 1분 안에 생긴 변화도 버리지 않고 1분이 되는 시점으로 미뤄 한 번 조회한다. 그래서 연속 변화가 끝난 마지막 네트워크 상태는 항상 조회된다.
- **이유:** 26절에서 유선 재연결 직후의 계정 한도 CLI 조회가 성공하지 못했다. 1분 안의 변화를 버리면, 재연결 직후 실패한 조회를 다음 정기 조회 때까지 회복하지 못할 수 있었다.
- **변하지 않는 것:** 절전 복귀(Windows `PowerModeChanged`, Mac `DidWakeNotification`)와 수동 Refresh는 지연 없이 즉시 조회한다.

### 호스트 변경

- Windows `TrayApplicationContext.cs`:
  - `CreateHttpClient()`와 두 알림 문구 함수를 사용한다.
  - `TryClaimNetworkRefresh`·`NetworkRefreshMinimumInterval`·`lastNetworkRefreshTick`을 지우고 스케줄러를 사용하며, 종료 때 스케줄러를 정리한다.
  - 공휴일 보정 알림 문구는 Windows 전용이라 그대로 둔다.
- Windows `TrayPresentationTests.cs`: 기존 1분 제한 검사 3건을 지운다. 같은 내용은 공통 `MonitorTests`가 확인한다.
- Mac `MacApplication.cs`: Windows와 같은 공통 함수와 스케줄러를 사용하도록 바꾼다.

### 이 Mac에서 수행한 검증

| 검사 | 결과 |
|---|---|
| Windows 대상 컴파일 `dotnet build AiBurgerClock.csproj -c Release -warnaserror -p:EnableWindowsTargeting=true` | 경고 0 / 오류 0 (실행 검사 아님) |
| `Mac/build.sh` | 종료 코드 0, 공통 검사 **244,358건**(+11), native Release 경고 0 / 오류 0 |
| 서명·smoke | `codesign` 통과, `--smoke-test` PASS / 종료 코드 0 |
| 변형 검사 | 5초 대기를 빼면 `First network change refreshes after the settle delay` 실패. 1분 안 변화를 버리는 이전 방식으로 되돌리면 `A change within a minute is deferred to the one-minute limit, not dropped` 실패. 원복 후 244,358건 통과 |

스케줄러 검사는 가짜 시계와 가짜 대기를 쓴다. 첫 변화 5초 뒤 조회, 1분 안 변화는 1분 시점으로 연기, 대기 중 변화는 마지막 변화 5초 뒤 한 번으로 합침, 종료 뒤에는 조회 없음을 확인한다.

### Windows에서 확인할 항목

1. `build.ps1`: 경고·오류 0, `--self-test` 종료 코드 0. Windows 검사 수는 공통 +11, `TrayPresentationTests` −3으로 바뀐다.
2. `--smoke-test`: 전환·Provider 풍선 알림 문구와 기존 `HolidayUiChecks`(공휴일 문구 포함)가 그대로 통과하는지.
3. 실제 Windows에서 네트워크 어댑터를 끊었다 다시 연결하면 약 5초 뒤 공식 상태·계정 한도가 다시 조회되는지(선택).

### Mac 실제 확인 (2026-10-04 00:47)

PR 코드로 빌드한 앱(PID 39587)으로 확인했다. 사용자가 Wi-Fi를 먼저 끄고 유선을 뽑았다가, 다시 연결했다.

| 시각 | 네트워크 (`configd`) | 앱 반응 |
|---|---|---|
| 00:47:20.5 | Wi-Fi 끔 (유선은 연결 유지) | 변화 감지, 5초 대기 시작 |
| 00:47:22.1 | 유선 해제 → 모든 연결 끊김 | — |
| 00:47:27.1 | — | 대기 끝, 조회. 연결이 없어 공식 상태 세 곳 DNS 실패(Unknown), 계정 한도 성공 기록 없음 |
| 00:47:28.6~34.6 | 유선·Wi-Fi 재연결, DNS 복구 | 1분 제한 안이라 버리지 않고 00:48:27로 연기 |
| **00:48:27~28** | — | **공식 상태 세 곳·계정 한도 두 곳 모두 성공** |

- **결과:** 재연결 뒤 변화가 버려지지 않고 1분 시점에 한 번 조회되어 모두 회복했다. 사용자도 화면에서 갱신을 확인했다.
- **이전 방식이었다면:** 00:47:20 변화에서 즉시 조회한 뒤, 재연결 변화는 1분 안이라 버렸을 것이다. 그러면 다음 정기 조회까지 Unknown과 이전 한도가 남는다(공식 상태 최대 5분, 계정 한도는 실패 재시도 15분).
- **남는 약점:** 첫 변화가 "일부 연결이 끊긴 것"이면, 5초 뒤 실제로는 연결이 하나도 없어 조회가 실패하고 약 1분 동안 Unknown으로 보일 수 있다.
- **추가 개선 후보(이번 PR 범위 밖):** 대기 끝에 연결이 없으면 조회하지 않고 1분 제한도 쓰지 않는 방법이 있다. 그러면 재연결 5초 뒤 바로 조회한다.

### 아직 하지 않은 것

- Windows 실행 검사 전체.

### Windows 검증과 병합

- **Windows 검증(ChatGPT, 2026-10-04):** PR HEAD `7abc69a46dcb908d77c96088ca37ef27ea4f6c2f`에서 실행했다. [PR 코멘트](https://github.com/hydron75/ai-burger-clock/pull/14#issuecomment-5971116094)
  - 기준 `a60fefd`와 비교했다.
  - SDK 10.0.401, Release 경고 0 / 오류 0.
  - 자체 검사 250,750 → 250,758건(+8: MonitorTests +11, TrayPresentationTests −3), 두 커밋 모두 종료 코드 0.
  - `--smoke-test` 종료 코드 0. HolidayUiChecks·전환/Provider 알림 경로·Refresh·모의 절전 복귀 통과.
  - 실제 어댑터 차단·재연결과 배너의 시각적 노출은 미수행.
- **병합 충돌 정리:** 그사이 바뀐 `feature/macos-native`를 PR 브랜치에 병합했다(rebase 없음). `BACKLOG.md`·`MACOS_PORT.md`는 양쪽 내용을 모두 살렸다. `Mac/MacApplication.cs`는 공통 `ProviderNotification`을 쓰도록 정리했다.
- **표시 이름 인자 추가:** 30절의 ChatGPT 표시 이름이 Mac 알림 제목에서 유지되도록, 공통 `TrayPresentation.ProviderNotification`에 선택 인자 `displayName`을 추가했다. 기본값은 enum 이름이라 Windows 동작은 같다. 공통 검사 1건을 추가했다.
- **병합 전 검증(이 Mac):** Windows 대상 컴파일 경고 0 / 오류 0, `Mac/build.sh` 종료 코드 0(공통 검사 244,361건), `codesign` 통과, smoke PASS / 종료 코드 0.
- **Windows 재검증 범위:** 병합 정리 뒤 Windows 쪽 변경은 공통 함수의 선택 인자 1개와 공통 검사 1건뿐이다. Windows 실행 검사는 PR #12 최종 검증 때 함께 한다.

## 29. macOS 알림 권한 확인

2026-10-04 KST 00:5x, 시스템 설정 → 알림을 읽기 전용으로 확인했다. 설정은 바꾸지 않았다.

| 항목 | 값 |
|---|---|
| 응용 프로그램 알림 목록 | `AI Burger Clock` 등록됨 |
| 알림 허용 | 켬 |
| 표시 위치 | 데스크탑·알림 센터·잠금 화면 모두 체크 |
| 알림 스타일 | 임시(배너) |
| 미리보기 / 그룹 | 기본 설정 / 자동 |
| 알림 요약 | 켬 (macOS 기본값으로 보임) |

- **결과:** 앱의 `UNUserNotificationCenter` 권한 요청이 허용된 상태다. 실제 배너가 화면에 뜨는지는 다음 Schedule 전환(2026-10-05 월 22:00 KST)이나 Provider 권고 변화 때 확인한다.
- **관찰:** 알림 목록의 앱 아이콘이 빈 아이콘이다. bundle에 앱 아이콘이 없어서다(`CFBundleIconFile`·`Resources`의 아이콘 없음). 메뉴바 아이콘과는 별개이며, Finder와 로그인 항목 목록에도 같은 빈 아이콘이 보인다. 개선 후보로 BACKLOG에 남긴다.

## 30. UI 조작 확인과 Tooltip·표시 이름·앱 아이콘 수정

2026-10-04 KST 00:58, 사용자가 상태 창을 직접 조작해 확인했다. 화면 제어 도구는 Dock에 없는 메뉴바 전용 앱을 대상으로 잡지 못해 사용자가 조작했다.

| 항목 | 결과 |
|---|---|
| Provider 설명 Tooltip | **문제:** 나타났다 금방 사라져 내용 확인이 어려움 |
| Provider 제목 `↗` 공식 상태 링크 | 정상으로 브라우저에서 열림 |
| Statistics 열기·닫기·재열기 | 정상 |
| 사용 경험 기록 | 실제 사용자 DB에 1건 저장(OpenAI · Slow), 상태 창에 `OpenAI · Slow 저장됨 (00:58 KST)` 표시 |

사용자 요청: 화면의 Provider 이름 중 "OpenAI"만 회사 이름이고 Claude·Gemini는 제품 이름이니 "ChatGPT"로 바꾼다. 앱 아이콘도 추가한다(29절 관찰).

### 수정

| 항목 | 원인 / 내용 | 파일 |
|---|---|---|
| Tooltip이 금방 사라짐 | 상태 창은 1초마다 `Update`되며, 그때마다 바뀌지 않은 `ToolTip`·텍스트도 다시 지정했다. AppKit은 Tooltip을 다시 지정하면 떠 있던 Tooltip을 닫는다. 표시 시간 문제가 아니므로, 값이 바뀔 때만 지정하도록 고쳤다(`SetText`·`SetTip`). 메뉴바 아이콘 Tooltip도 바뀔 때만 지정한다. 다만 메뉴바 Tooltip은 카운트다운이 들어 있어 매초 바뀐다 | `Mac/MacStatusWindow.cs`, `Mac/MacApplication.cs` |
| OpenAI → ChatGPT | 화면에 보이는 이름만 바꾼다. 상태 창 제목, 메뉴바 메뉴, 기록 창 제목, 저장 안내, Provider 알림 제목, 통계 창 행, 메뉴바 Tooltip이 대상이다. `ProviderKind.OpenAI`, DB에 저장되는 값, 공식 상태 주소는 그대로다 | `Mac/MacStatusWindow.cs`(`ProviderName`), `Mac/MacApplication.cs`, `Mac/MacStatisticsWindow.cs` |
| 공통 Tooltip 표시 이름 | 공통 `TrayPresentation.Tooltip`에 선택 인자 `displayName`을 추가했다. 기본값은 enum 이름이라 Windows 동작은 같다 | `TrayPresentation.cs`, 검사 `PortablePlatformTests.cs` +2 |
| 앱 아이콘 | 초록 그라데이션 바탕에 흰 시계 테두리와 F를 그렸다. 10개 크기를 `Mac/tools/make-app-icon.swift`로 생성해 `Mac/Assets.xcassets/AppIcon.appiconset`에 두고 csproj에 `<AppIcon>AppIcon</AppIcon>`을 지정했다. `actool`이 만든 부분 Info.plist가 매니페스트 입력이라, 증분 빌드에서도 `CFBundleIconFile`·`CFBundleIconName`이 반영됐다 | `Mac/AiBurgerClock.Mac.csproj`, 신규 `Mac/tools/make-app-icon.swift`, `Mac/Assets.xcassets/**` |

### 검증

| 검사 | 결과 |
|---|---|
| `Mac/build.sh` | 종료 코드 0. 공통 검사 244,349건(+2, Tooltip 표시 이름). native Release 경고 0 / 오류 0 |
| bundle | `Resources/AppIcon.icns`·`Assets.car`, Info.plist `CFBundleIconFile`/`CFBundleIconName` = `AppIcon`. 버전 0.1.2 / 3. `codesign` 통과 |
| `--smoke-test` | PASS / 종료 코드 0. 레이아웃 검사에 OpenAI 행 제목이 "ChatGPT "로 시작하는지 추가 |
| 시스템 아이콘 | `NSWorkspace.icon(forFile:)`로 추출하니 macOS가 둥근 사각형으로 마스킹한 초록 시계·F 아이콘이 나왔다 |

**실행으로 확인하지 않은 것:**
- Tooltip이 이제 사라지지 않는지는 사용자가 다시 확인해야 한다. 원인 코드는 고쳤지만, Tooltip이 떠 있는 상태를 자동 검사로 재현할 방법은 없다.
- 알림 설정·Finder·로그인 항목에 새 아이콘이 반영되는 시점은 macOS 아이콘 캐시에 따라 늦을 수 있다.

**공통 PR #14와의 관계:** PR #14는 Provider 알림 문구를 공통 함수 `ProviderNotification(ProviderKind, …)`로 옮긴다. 이 브랜치에 병합할 때 Mac 알림 제목이 다시 "OpenAI"가 되지 않도록 표시 이름을 넘기게 맞춘다.

### 30-1. 한도 Tooltip과 구간 구분 (후속)

사용자 재확인 결과, Provider 설명의 Tooltip은 이제 유지됐다. 개인 한도 상자의 Tooltip은 여전히 금방 사라졌다. 또 서비스 상태와 한도 구간을 구분하고, 제목을 크게 해 달라는 요청을 받았다.

- **한도 Tooltip:**
  - 원인: 한도 상자는 24시간 미만 리셋 카운트다운이 초 단위라 텍스트가 매초 실제로 바뀐다. 그때마다 텍스트 교체와 스크롤 위치 복원이 일어나 텍스트 뷰에 붙은 Tooltip이 닫힌다.
  - 수정: Tooltip을 바뀌지 않는 바깥 상자(`NSScrollView`)와 "ChatGPT"·"Claude" 소제목으로 옮겼다. 스크롤 위치는 실제로 움직였을 때만 복원한다.
- **구간 구분:**
  - 창 높이를 660 → 720 point로 늘렸다.
  - Schedule, 서비스 상태, 개인 계정 잔여 한도, 하단 버튼 사이에 구분선(`NSBox` separator) 3개를 넣었다.
  - "서비스 상태" 제목을 새로 넣었고, "개인 계정 잔여 한도" 제목은 12 → 15 point로 키웠다(두 제목 모두 15 point 굵게).
- **검증:** `Mac/build.sh` 종료 코드 0(공통 검사 244,349건, 경고·오류 0), `codesign` 통과. smoke PASS / 종료 코드 0이며, 구분선을 포함한 모든 컨트롤의 경계·겹침 검사와 한도 행 높이 검사가 새 배치에서도 통과했다.
- **사용자 확인 필요:** 한도 Tooltip이 이제 유지되는지와 새 배치의 외관.

### 30-2. 한도 설명을 ⓘ 버튼으로 이동 (후속)

30-1의 상자 Tooltip은 사용자 재확인에서도 금방 사라졌다. 상자 안 텍스트가 매초 바뀌는 동안에는 상자 위 어느 위치의 Tooltip도 유지되지 않는 것으로 판단했다. 그래서 설명을 바뀌지 않는 컨트롤로 옮겼다.

- "ChatGPT"·"Claude" 한도 소제목 옆에 ⓘ 버튼(`info.circle` 심볼)을 두었다.
- ⓘ에 마우스를 올리면 Tooltip이 나오고, 누르면 같은 설명이 팝오버로 뜬다. 팝오버는 바깥을 누를 때까지 유지된다. 소제목에도 같은 Tooltip을 둔다.
- 효과가 없던 상자 Tooltip은 지웠다.
- smoke 레이아웃 검사에 다음을 추가했다: 두 ⓘ 버튼의 심볼·설명 존재, 팝오버 열림·닫힘. PASS 문구에 `quota info popover`가 붙는다.
- 검증: `Mac/build.sh` 종료 코드 0(공통 검사 244,349건, 경고·오류 0), `codesign` 통과, smoke PASS / 종료 코드 0.
- 사용자 확인 필요: ⓘ의 Tooltip과 팝오버가 실제로 보기 좋은지.

## 31. 0.1.3 버전 정리

2026-10-04 KST, 사용자가 한도 ⓘ의 Tooltip과 팝오버가 잘 보인다고 확인했다(30-2절). 0.1.2 이후 사용자에게 보이는 변경을 묶어 Mac 버전을 **0.1.3 / build 4**로 올렸다. Windows 2.2.2·DB schema·NuGet 의존성은 그대로다.

0.1.3에 들어간 변경:

| 절 | 내용 |
|---|---|
| 24 | bundle 버전을 csproj에서 관리하고 smoke에서 확인 |
| 25 | PR #12 리뷰 10건 반영: 초기화 실패, 공휴일 저장 알림, 메뉴 타이머, HTTP 설정, 한도 표시, 알림 문구, 메모 1,000자, 추가 인수 |
| 30 | 상태 창 Tooltip 유지, 화면 표시 이름 ChatGPT, 서비스 상태·한도 구간 제목과 구분선(창 430×720), 한도 ⓘ 팝오버, 앱 아이콘 |

PR #14(28절)의 공통 원본 정리와 연결 복구 지연 조회는 아직 이 브랜치에 병합되지 않아 0.1.3에 포함되지 않는다.

### 빌드와 검사 (이 Mac)

| 항목 | 결과 |
|---|---|
| 소스 | `8e8d9b8edb0af8c7c88914dfb207c1581072f8cc` (`feature/macos-native`) |
| 도구 | Xcode 27.0 / 27A266a, .NET SDK 10.0.401, macos workload 27.0.10722 |
| `Mac/build.sh` | 종료 코드 0, 공통 검사 244,349건, native Release 경고 0 / 오류 0, 15.4초. clean 없이 증분 빌드 |
| bundle | `Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app`, `CFBundleShortVersionString` 0.1.3, `CFBundleVersion` 4, `CFBundleIconFile` AppIcon, 크기 124M |
| 서명 | `codesign --verify --deep --strict` 통과(ad-hoc) |
| `--smoke-test` | PASS / 종료 코드 0 (bundle 버전 검사 포함) |
| SHA-256 실행 파일 | `21dc4094e52580f05801559a83862db2fe3f7d0755c25109198394b87218a477` (`Contents/MacOS/AI Burger Clock`) |
| SHA-256 앱 DLL | `2e12980246365a1b63c06a8a8b8d53e86d58bfa41a2909f0d20f2e21c4c4467d` (`Contents/MonoBundle/AI Burger Clock.dll`) |

0.1.2에서 csproj 대신 `Info.plist`만 바꾸던 방식이었다면, 이번 증분 빌드에서도 버전이 반영되지 않았을 것이다. 24절 수정 뒤에는 증분 빌드에서 바로 0.1.3 / 4가 반영됐다.

앱은 같은 경로의 새 bundle로 다시 실행했다. 로그인 항목은 경로 기준이라 그대로 유지된다.

## 32. PR #14 병합과 0.1.4 정리

2026-10-04 KST, Windows 검증(28절)을 확인한 뒤 PR #14를 `feature/macos-native`에 병합했다(merge commit `efdbf5d204bf5fa766663db01fc2a8226aebff46`). 병합된 bundle은 PR #14를 포함하지 않는 0.1.3 기록(31절)과 구분되도록 Mac 버전을 **0.1.4 / build 5**로 올렸다.

0.1.4에 추가된 것(0.1.3 대비):
- HTTP 설정과 전환·Provider 알림 문구가 공통 함수로 옮겨졌다. 동작 변화는 없다.
- 연결 복구 조회가 5초 대기, 연속 변화 합침, 1분 제한 안 변화의 연기로 바뀌었다(26·28절의 실제 Mac 확인 포함).
- Mac Provider 알림 제목이 공통 함수를 거쳐서도 "ChatGPT"로 나온다( 인자).

| 항목 | 결과 |
|---|---|
| 소스 | `63d9ec769550f6140fac2af448bdceec74d12588` |
| `Mac/build.sh` | 종료 코드 0, 공통 검사 244,361건, native Release 경고 0 / 오류 0, 15.2초(증분) |
| Windows 대상 컴파일 | 병합 정리 직후(`dfd599c`) 경고 0 / 오류 0. 실행 검사는 아님 |
| bundle | 0.1.4 / 5, `CFBundleIconFile` AppIcon, 124M, `codesign` 통과 |
| `--smoke-test` | PASS / 종료 코드 0 |
| SHA-256 실행 파일 | `b2342350e64860cb3cf5e41e26054dfde7d88b6e5ffc2c22e097a80b84252f31` |
| SHA-256 앱 DLL | `fa0e37c190800006182ae1f44e4c2a28b23d7df367bec8b10ab3580c86831073` |

앱은 같은 경로의 0.1.4 bundle로 다시 실행했다. 장기 사용 확인은 이 bundle로 이어 간다.
