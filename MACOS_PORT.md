# macOS preview 0.1.0 — 소스 준비와 검증 기록

기록일: 2026-10-03 KST. Windows 기준 버전: 2.2.2.

Apple Silicon / macOS 27용 **native AppKit 메뉴바 호스트**를 별도 프로젝트로 준비했다. 기존 Windows WinForms UI·배포본은 유지한다. 현재 완료한 것은 소스 구현·Windows 회귀 검사·Mac 참조 API 컴파일이다. **실제 Mac Release 빌드와 실행은 아직 확인하지 않았다.**

Mac 개발 도구 준비와 실행 순서는 [Mac/README.md](Mac/README.md)를 따른다.

## 1. 한 저장소, 같은 기능 규칙

영구적인 Windows/Mac 브랜치를 따로 유지하지 않는다. `feature/macos-native`는 개발·검증용 임시 브랜치다. Mac 검증 후 main 병합 여부를 판단하고, 공통 기능 개선은 한 원본에서 두 호스트에 반영한다.

```text
루트의 공통 C# 원본 18개
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
| UsageStore.cs | 기본 DB 경로를 AppPaths에 위임. schema·migration·SQL은 변경 없음 |
| AccountQuotaClient.cs | Windows .exe 탐색 유지. Mac은 절대 경로·실행 권한·표준 폴더 탐색과 자식 PATH 구성 추가 |
| StatisticsWindow.cs | 기존 계산부를 StatisticsAnalysis.cs로 그대로 분리. Windows 통계 UI는 유지 |
| StatusWindow.cs, AccountQuotaView.cs | 기존 표시 함수를 DisplayFormatting에 위임. Windows 문구·배치·조작은 유지 |
| TrayPresentation.cs | WinForms 창의 표시 함수 의존을 제거하고 공통 formatter 사용 |
| AiBurgerClock.csproj | Mac·공통 검사·artifacts의 C# 파일 제외. Windows 버전·TFM·패키지는 유지 |
| SelfTest.cs | 경로·IANA 시간대·표시·Mac CLI 후보 검사 33건 연결 |
| README.md, CODE_GUIDE.md, BACKLOG.md | preview와 안정판 구분, 소스 지도·준비·미검증 항목 갱신 |

기존 `AccountQuotaPolicy`·파서·monitor·공식 Provider 판정·알림 정책은 변경하지 않았다. Windows의 `ChatGPT` 한도 제목 변경은 여전히 다음 Windows UI 수정 항목이다. Mac preview 소스에는 `ChatGPT` 아래 `Work/Codex`를 적용했다.

## 3. 새 파일

- 공통 보조: `AppPaths.cs`, `DisplayFormatting.cs`, `MacCliPaths.cs`, `StatisticsAnalysis.cs`.
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

SQLite schema 2와 migration은 그대로다. 공통 검사는 새 임시 DB에서 생성·저장·재오픈·이전 schema 이관·메모·상태·설정·한도 캐시와 1만 건 통계를 확인했다. **실제 Mac ARM64 SQLite 라이브러리 로딩은 아직 미확인**이다. 양쪽 사용자 DB를 읽거나 자동 동기화하지 않았다.

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

사용자가 전달한 읽기 전용 결과는 macOS 27.0.1 / arm64 / Xcode 27.0이며 `dotnet`은 PATH에서 발견되지 않았다. 이 확인과 실제 앱 빌드 성공은 별개다. 도구를 원격으로 설치하거나 Mac 설정을 변경하지 않았다.

1. .NET SDK 10.0.401 ARM64와 macos workload set 10.0.401.1 준비.
2. `bash Mac/build.sh`: Mac OS 시간대 데이터로 공통 검사, native Release 빌드, ad-hoc 서명·SQLite dylib 포함 확인.
3. 만들어진 실행 파일의 `--smoke-test`: 임시 DB 4종 이벤트·메모·통계·No data·한도 표시와 종료 코드 확인.
4. 실제 UI의 메뉴바·상태 창·한도 스크롤·기록·통계·공식 페이지 클릭 확인.
5. 별도 설치·로그인된 Mac CLI에서 실제 한도 수신과 실패 표시 확인. Windows 성공을 Mac 계정 조회 성공으로 간주하지 않음.
6. 사용자 승인하에 알림 권한·배너와 로그인 항목 등록/해제·재로그인 확인.
7. 절전·연결 복구·정상 종료·장시간 실행 확인.

native smoke는 별도의 GUID 임시 DB만 쓰며 HTTP·계정 CLI·브라우저·알림 권한 요청·로그인 항목 변경을 하지 않는다. 실제 알림/자동 실행을 검증한 것으로 표시하지 않는다.

## 8. 빌드와 배포의 제한

[Microsoft Xcode 27 지원 릴리스](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.0-10722)의 SDK 10.0.401 / workload set 10.0.401.1 / Xcode 27.0을 기준으로 준비했다. `build.sh`는 설치나 로그인 대신 기존 도구를 확인하고 필요한 경우 종료한다.

Mac은 self-contained `.app`를 목표로 한다. `TrimMode=copy`로 관리 코드 제거 없이 Apple SDK의 플랫폼 처리 경로를 사용한다. 로컬 ad-hoc 서명이며 Developer ID·notarization·설치 프로그램·자동 업데이트는 포함하지 않는다. 다른 Mac에 배포할 때 Gatekeeper/정식 서명을 따로 검토해야 한다.

예상 결과 위치는 다음과 같지만 **이번 Windows 작업에서 해당 `.app`를 만들지는 않았다.**

```text
Mac/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app
```

Mac UI 크기·알림 노출·로그인 등록과 이동된 bundle의 동작은 실제 화면에서 조정할 수 있다. 미검증 preview를 안정판으로 표시하거나 Windows dist를 교체하지 않는다.

## 9. Git 기준점과 Windows 보존

- 개발 브랜치: `feature/macos-native`.
- 작업 전 main: `fbcba72f9ad41e23ab2e668352f9f137c5803557`.
- Windows 배포본 버전: 2.2.2, 변경하지 않음.
- 보존한 `dist/win-x64/AI Burger Clock.exe` SHA-256: `56250BAF25C192314B513C02977A2A40A1E1D72836C43B0CF20803C2F98E2A10`.

사용자 DB·레지스트리·자동 시작 경로·실행 중인 앱을 변경하지 않았다. 이번 단계는 배포 교체가 아니므로 DB 백업/복원이나 기존 앱 종료를 요구하지 않았다. 원래 Windows 소스 상태는 위 기준 커밋으로 확인할 수 있다. 기존 변경을 강제로 되돌리는 명령은 실행하지 않는다.

실제 Mac 검증 결과를 받은 뒤 이 기록과 README·CODE_GUIDE·BACKLOG를 갱신하고, 병합·안정판 배포는 별도 판단한다.
