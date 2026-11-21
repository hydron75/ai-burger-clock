# Windows 2.9.5 후보 — Gemini 앱 웹 사용량

2026-11-21 KST · PR B · 기준 main `034655a` (#163 병합). 바이너리는 후보 경로에 준비합니다. 실제 교체와 운영 실행은 사용자가 Codex 밖에서 진행합니다.

## Windows 변경

- 트레이의 **Gemini 사용량 → 로그인…**에서 사용자가 직접 로그인합니다. 공식 사용량 페이지에 도달하면 창을 닫고 활성 설정을 저장합니다. 시작할 때 저장한 설정을 적용하며, 새 설치는 꺼짐입니다. 끄기는 프로필과 마지막 웹 캐시를 보존합니다.
- 로그인 창과 숨김 조회는 `%LOCALAPPDATA%\AIBurgerClock\GeminiWeb` 전용 WebView2 환경을 공유합니다. 숨김 호스트는 `Show()`를 호출하지 않으며 `WS_EX_TOOLWINDOW`·`WS_EX_NOACTIVATE`를 적용합니다. 매 회 UI 스레드에서 컨트롤러를 만들고 닫습니다. 숨김 조회 전체 제한은 30초입니다.
- 메인 프레임 이동과 HTTPS 리디렉션을 허용합니다. `NavigationCompleted` 뒤 250ms 간격으로 최대 5초 기다리고 `/usage`의 두 구간을 파싱할 수 있으면 반환합니다. 다른 호스트에 머무르면 마지막 URL을 공용 파서에 넘겨 로그인 필요를 판정합니다. 조회 스크립트는 `/usage`에서 실행합니다.
- UA와 브라우저 인자는 기본 설정을 사용합니다. 음소거와 권한 거부, 새 창·다운로드 차단을 적용합니다. 숨김 조회에서는 대화상자와 외부 앱 실행 요청도 차단합니다. 상태 창의 `Refresh`와 트레이의 **상태·한도 새로 고침**이 Gemini 수동 조회를 요청합니다. 절전 복귀와 네트워크 복구는 기존 Provider 갱신을 수행하며, Gemini 자동 주기는 15분입니다.
- 로그아웃은 꺼짐·정리 예정 상태를 저장하고, 해당 웹 캐시 키를 지운 뒤 브라우저 종료와 리소스 해제를 기다려 전용 프로필을 삭제합니다. 실패 시 정리 예정 상태를 유지해 다음 시작 때 재시도합니다. 로그아웃 메뉴는 작업 실행 중을 제외하고 사용할 수 있습니다.
- WebView2 SDK 1.0.4314.35의 Core 어셈블리와 x64 로더를 사용합니다. 후보는 framework-dependent 단일 EXE이며 WebView2 Evergreen Runtime은 Windows의 설치본을 사용합니다. [공식 SDK 패키지](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35)

## 검증

- Windows Release 및 최종 Portable publish 경고 0·오류 0, .NET SDK 10.0.404. 새 단일 EXE의 self-test는 292,779건·종료 코드 0입니다. 공용 291,593건, Windows Gemini 29건, 모바일 송신 226건을 포함하며 기존 symlink fixture 1건은 SKIP입니다.
- 후보 폴더에는 EXE 한 개가 들어 있습니다. 단일 EXE에서 x64 `WebView2Loader.dll` 194,912바이트가 추출되어 로딩되는 것을 확인했습니다. 설치된 Runtime으로 WebView를 만드는 검사는 사용자 수동 확인에 해당합니다.
- 고정 `after` 슬롯의 최종 smoke는 **1,207건·PNG 75개·종료 코드 0**입니다. 트레이 툴팁, 트레이와 상태 창의 Gemini 갱신 각각 1회, 절전 복귀와 네트워크 스케줄러의 갱신 콜백 각각 0회를 확인했습니다. 꺼짐·로그인 필요·정상·형식 변경·로드 실패의 한도 박스, 상세, 메뉴 합성 PNG 15개를 포함합니다. [검사 구현](WindowsGeminiWebUiChecks.cs)
- 트레이 수정 후 첫 smoke(`b5c6112`)에서 트레이·복귀·네트워크 검사는 통과했습니다. 메뉴를 닫고 상태 창 버튼을 누르는 단계에서 `Click`이 발생하지 않아 시간 초과가 나고 1,194건과 PNG 72개에서 중단했습니다. 검사에서 창을 다시 열고 `Click` 전달을 확인한 뒤 최종 후보를 검증했습니다. 결과는 `artifacts/r295m/windows-tests/after/20261121-180149-391-e310bd/`와 `artifacts/r295m/windows-tests/after/20261121-180526-456-b7df83/`에 보존했습니다.
- 대체된 `b74879d` 후보의 이전 smoke는 긴 PNG 경로에서 GDI+ 오류로 중단된 뒤 경로를 줄여 1,203건 통과했습니다. 기록은 `artifacts/r295-gemini-web/windows-tests/after/20261121-174106-536-6f2375/`와 `artifacts/r295/windows-tests/after/20261121-174211-112-6b368d/`에 보존했습니다.
- DOM 합성 검사는 다른 호스트를 거쳐 돌아오는 경우, 다른 호스트에 머물러 로그인 필요가 되는 경우, 빈 영역과 첫 구간을 지나 두 구간이 나타나는 경우를 포함합니다.
- 로그아웃 합성 검사는 끄기 대상과 캐시 지우기 순서, 브라우저 해제 대기, 실패 기록 및 재시도, 기존 agy 캐시 보존을 확인합니다. 사용자 브라우저와 실제 전용 프로필은 수동 확인 항목입니다.

### 모바일 송신 가짜 HTTP 비교

| 상태 또는 완료 | 수정 전 | 수정 후 |
|---|---:|---:|
| Gemini 정상 | 1 | 0 |
| Gemini 형식 변경 | 1 | 0 |
| Gemini 로드 실패 | 1 | 0 |
| Gemini 로그인 필요 | 0 | 0 |
| Gemini 켜기·끄기·로그아웃 전환 | 각각 0 | 각각 0 |
| ChatGPT·Claude·xAI 완료, 송신 켜짐 | 각각 1 | 각각 1 |
| Gemini 또는 다른 Provider 완료, 송신 꺼짐 | 각각 0 | 각각 0 |

스키마 v2는 Gemini를 `reading=none`으로 표현합니다. 이에 따라 Windows 송신 연결부에서 Gemini 상태 전환을 완료 이벤트에서 제외했습니다.

## 후보 신원과 외부 배포

| 항목 | 값 |
|---|---|
| FileVersion | 2.9.5.0 |
| EXE 소스 커밋 | `4b351b2cb67e8af298789a9be6c481b28ba78d0b` |
| 크기 | 31118733 bytes |
| SHA-256 | `28CF1F25A44B9F823EC2C6CFF686705DC534A0BC4C68922013D3FE4EEEAA3607` |

후보 `artifacts/release-2.9.5-preparation-20261121-tray-refresh/candidate/AI Burger Clock.exe`. 이전 신원은 2.9.4의 `candidate.json`을 사용합니다. 이전 2.9.5 후보 `b74879d`의 EXE와 receipt는 보존하며, `deployment-receipt.run/retired.json`에 대체 사실을 기록했습니다. 이전 receipt를 사용하면 배포 스크립트가 Receipt 단계에서 `ReceiptAlreadyUsed`로 멈춥니다.

EXE 소스 커밋 이후에는 이 검증 기록만 추가합니다. 새 receipt와 사전 확인 스크립트의 경로는 `artifacts/release-2.9.5-preparation-20261121-tray-refresh/`입니다. MSI PowerShell 7로 `C:\Windows\System32`에서 실행한 준비 검증에서 이전·후보 커밋 모두 읽혔습니다. 사용자 외부 PowerShell에서 다음 순서로 확인합니다. [Windows 배포 절차](WINDOWS_DEPLOYMENT.md)

1. `Read-DeploymentSourceCommits.ps1`에서 `Succeeded=true`, 이전·후보 각각 `GitExitCode=0`과 `Readable=true`를 확인합니다.
2. 운영 앱을 트레이에서 정상 종료한 뒤, 같은 폴더의 `deployment-receipt.json`을 인자로 `deploy-windows.ps1`을 실행합니다.
3. `Succeeded=true`, 6단계 모두 `Passed`, `FileVersion=2.9.5.0`, `CandidateMatches=true`, `RegistrationPerformed=false`, `DryRunChangedNothing=true`, `AppLaunchRequested=true`를 확인합니다.

## 사용자 수동 확인

- 트레이의 **Gemini 사용량 → 로그인…**에서 직접 로그인하고, 사용량 페이지에 도달하면 창이 닫히는지 확인합니다. 첫 조회의 5시간·주간 행이 일반 브라우저 화면과 일치하는지 확인합니다. 후보의 숨김 호스트는 한 번도 표시되지 않으며, 앞선 probe는 창을 표시한 뒤 최소화했습니다. 브라우저에서 값이 보이는데 앱 첫 조회가 **형식 변경** 또는 **조회 실패**이면 결과를 보고하고 probe의 최소화 방식으로 전환합니다.
- Refresh를 누르지 않고 약 15분 뒤 자동 조회가 일어나는지 확인합니다. 조회 중 별도 창, 작업표시줄·Alt+Tab 항목, 입력 포커스 변화가 없는지 확인합니다.
- **끄기** 뒤 값이 사라지고, **로그인…**으로 다시 켜면 보존한 프로필로 돌아오는지 확인합니다. **로그아웃** 뒤에는 다음 조회를 위해 다시 로그인해야 합니다. 정리 실패가 표시되면 다음 시작 때 결과를 확인합니다.
- 실제 값·리셋 시각·계정·인증 정보와 화면 캡처는 공개 기록에 넣지 않습니다.
