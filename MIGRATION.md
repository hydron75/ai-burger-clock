# .NET 10 마이그레이션 결과

검증일: 2026-09-19 KST. 대상: Windows 11 x64.

## 1. 보존 기준점 및 구조 변화

마이그레이션 전 전체 프로젝트를 `../backups/AiBurgerClock-net48-20260919-124701.zip`에 보존했다.
28개 파일을 원본과 SHA-256으로 대조했다. ZIP 체크섬:

```text
FF9B4FD5821AFA2FAD9728ABF98F24037A13CB1FB0358FF83EEDD8842EA5DC54
```

원래 솔루션과 WinForms를 유지했다. 구형 MSBuild 프로젝트의 수동 파일/어셈블리 목록과
Framework 내장 컴파일러 fallback은 SDK 자동 항목 및 dotnet build/publish로 교체했다.
Assembly 메타데이터는 SDK가 생성하며 COM 관련 속성만 기존 AssemblyInfo에 남겼다.
별도의 사용자 설정 파일이나 데이터베이스는 원래 없었다.
설정 계약인 HKCU Run 항목 이름, `--autostart`, 중복 실행 mutex 이름은 보존했다.

## 2. 변경한 파일

| 파일 | 변경 내용 |
|---|---|
| AiBurgerClock.csproj | SDK-style, WinForms, .NET 10, x64, nullable/implicit usings, Assembly 메타데이터, 기본 글꼴/DPI 명시 |
| Properties/AssemblyInfo.cs | SDK와 중복되는 속성 제거, COM 속성 유지 |
| Program.cs | ApplicationConfiguration.Initialize, 컨텍스트 Dispose, smoke-test 진입점과 중복 실행 시 검사 실패 코드 |
| AgentSchedule.cs | 기존 월요일 다음 전환 00:22 오타를 22:00으로 수정 |
| StatusWindow.cs | nullable 이벤트, 기존 292×190 논리 크기/배치를 DPI 배율에 맞춰 확장해 상태명 잘림 수정 |
| TrayApplicationContext.cs | WinForms Timer 명시, nullable 대응, 자원 정리, 창 열기 시 전환 알림 누락 수정, 검사 시각 주입 |
| AutoStartManager.cs | nullable 레지스트리 핸들 처리; 같은 등록 경로/항목/명령 형식 유지 |
| app.manifest | 중복 DPI 선언 제거; 실행 권한과 OS 호환성 유지 |
| SelfTest.cs | 주간 모든 분, 전환 경계, 시간대, 월말/연말/윤일 검증 및 실패 상세 출력 |
| build.ps1 | dotnet build/publish, 경고 실패 처리, WinExe 종료 코드 정확히 확인, 검증 로그 |
| .gitignore | SDK/배포/검증/Visual Studio 출력 제외 |
| README.md | 런타임 요구사항, 새 출력 경로, 빌드·배포·검증·자동실행 안내 |
| global.json (신규) | 안정 버전 .NET 10 SDK 선택 |
| Properties/PublishProfiles/Portable.pubxml (신규) | win-x64 framework-dependent 단일 EXE 배포 |
| SmokeTest.cs (신규) | 실제 WinForms/트레이/알림/창 수명, 선택적 자동실행 등록·복원, 렌더 검사 |
| MIGRATION.md (신규) | 이 결과 보고서 |

기존 `AiBurgerClock.sln` 및 프로젝트 GUID는 유지했다.
`bin/Release/AI Burger Clock.exe`는 구버전 산출물이다. 새 배포 경로는 `dist/win-x64/AI Burger Clock.exe`다.

## 3. 대상 및 실행 조건

- TargetFramework: `net10.0-windows`
- SDK: `Microsoft.NET.Sdk`
- UI: `UseWindowsForms=true`, 기존 Windows Forms 유지
- CPU/배포: `PlatformTarget=x64`, `RuntimeIdentifier=win-x64`
- Nullable / ImplicitUsings: `enable`
- 외부 PackageReference: 없음
- 실행 시 .NET 10 Desktop Runtime x64 필요. 런타임 포함 배포가 아니다.
- 기본 글꼴은 Framework 시절과 같은 Microsoft Sans Serif 8.25pt, DPI 모드는 SystemAware.
  기존 화면 내 Segoe UI 글꼴, 색, 컨트롤 순서, 메뉴를 유지하고 DPI 자동 배율을 명시했다.

## 4. 빌드 및 실행 결과

- 현장 확인: Visual Studio Community 2026 18.10.1, .NET SDK 10.0.401, MSBuild 18.9.11.
- Release 솔루션 빌드 및 최종 프로젝트 빌드/단일 EXE publish: 성공, 경고 0 / 오류 0.
- 경고를 오류로 취급하는 `-warnaserror`로 검사했다.
- 최종 EXE: 208,593 bytes (약 204 KiB).
- SHA-256: `678E9DC837B2D7D8FC30E2BA8F4B85C5073A46161CB914637C22956DB31EA808`
- 배포 EXE 직접 실행: 응답 정상, x64 런타임 `Microsoft.NETCore.App/10.0.12/coreclr.dll` 로드 확인.
- 실행 직후 working set 약 49 MB. 순간 측정치이며 장시간 메모리 안정성 수치는 아니다.
- 최종 버전을 `--autostart` 모드로 트레이에 실행해 두었다.

## 5. 기존 기능 회귀 검사

| 항목 | 결과 및 근거 |
|---|---|
| KST 시간표/카운트다운 | 50,452개 assertion 통과. 주간 10,080분, 정확한 다음 날짜/시간, 전환 직전 1 tick, UTC/다른 offset, 월말/연말/윤일 포함 |
| 주말 연속 구간 | 토 10:00 → 월 22:00, 60:00:00 확인 |
| 자동실행 모드 | 상태 창이 숨겨지고 실제 NotifyIcon이 유지됨 |
| 트레이 상태 | 두 상태에서 아이콘 교체 및 tooltip/카운트다운 갱신 확인 |
| 상태 창 | 열기, 닫아 숨기기, 다시 열기, 종료 시 Dispose 확인 |
| Windows 알림 | FULL/BURGER 양방향 제목/내용 호출, Windows BalloonTipShown 이벤트 2회 확인 |
| 경계 시각에 창 열기 | 알림 1회 발생, 후속 갱신에서 중복 알림 없음 |
| 절전 복귀에 해당하는 시간 점프 | 주입 시각을 월 22:00으로 이동해 BURGER 전환/12시간 카운트다운 확인 |
| 자동실행 옵션 | 실제 로그인 계정의 Run 등록/삭제, 현재 EXE 경로 따옴표 및 --autostart, 메뉴↔체크박스 동기화, 기존 값 복원 확인 |
| 중복 실행 | 기존 인스턴스는 계속 응답하고 두 번째 인스턴스는 종료 코드 0으로 종료 |
| 검사 오판 방지 | 앱 실행 중 --smoke-test는 명시적인 오류와 종료 코드 2 반환 |
| UI 배치 | 두 상태 폼 렌더를 직접 확인. DPI에 따른 상태명 잘림 수정 후 텍스트/컨트롤 경계 검사 통과 |

검증 로그:

- `artifacts/self-test.txt`
- `artifacts/smoke-test.txt`
- `artifacts/smoke-test-already-running.txt`
- `artifacts/full-throttle.png`, `artifacts/burger-time.png`

기존 버전의 월요일 시간 오타 때문에 기존 자체 테스트는 실제로 종료 코드 1이었다.
이전 빌드 스크립트는 GUI 프로세스 종료를 확실히 기다리지 않아 통과로 잘못 보고했다.
새 스크립트는 정확한 프로세스의 ExitCode를 확인한다.

자동실행 상태 정정: 초기 샌드박스 HKCU 조회는 실제 로그인 사용자의 HKCU가 아니었다.
실제 로그인 계정에서는 자동실행이 이미 켜져 있었다. 검사 후 기존 값을 복원한 다음,
기존 활성화 상태를 유지하면서 구버전 EXE 경로만 새 dist EXE로 갱신하고 레지스트리를 다시 읽어 확인했다.
변경 전 값과 백업 정보는 `../backups/AiBurgerClock-net48-20260919-124701.txt`에 있다.

## 6. 2단계 이전 남은 사항

마이그레이션을 막는 확인된 결함은 없다. Provider 상태/실측 통계는 아직 구현하지 않았다.
현재 시간표는 실시간 서비스 가용성 지표가 아니며, 2단계에서 Provider 종류,
조회 API, 관측 지연의 정의와 저장/보존 정책을 별도로 정하면 된다.

남은 검증 범위:

- 실제 로그아웃/재로그인 또는 재부팅은 수행하지 않았다. Run 명령과 --autostart 직접 실행은 검증했다.
- 실제 PC 절전/재개는 수행하지 않았다. 동일 경로의 시각 점프/상태 전환을 검증했다.
- 다른 모니터/DPI 조합 전체에 대한 수동 검사는 하지 않았다.
- 데스크톱 자동화 도구에서 이 작업표시줄 비표시 창을 열거하지 못했다.
  PNG는 실행 중 폼의 DrawToBitmap 렌더이며 데스크톱 스크린샷은 아니다.
  알림은 Windows 표시 이벤트를 확인했으나 배너의 시각적 배치를 직접 캡처하지 않았다.
- EXE 이동 시 자동실행 옵션을 다시 등록해야 한다. 새 배포 경로를 기준으로 유지하는 것이 좋다.

## 참고한 공식 문서

- [WinForms .NET 마이그레이션](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/migration/)
- [Desktop SDK 속성](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop)
- [.NET 단일 파일 배포](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
