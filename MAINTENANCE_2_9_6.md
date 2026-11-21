# Windows 2.9.6 후보 · Gemini 로그인 창과 복귀

## 발생 경위

- 2.9.5 배포 직후 Gemini 로그인 창의 HWND와 WebView2 자식 창이 있었지만 Windows의 `WS_VISIBLE` 플래그가 꺼져 있었습니다. 사용자는 같은 EXE를 일반 실행하자 로그인 창이 보였다고 확인했습니다.
- 첫 로그인 뒤 Gemini 대화 화면에서 창이 열린 채였습니다. 사용자가 창을 닫고 로그인 메뉴를 다시 열자 사용량 페이지에 도달해 창이 닫혔고, 사용량 조회가 켜졌습니다.
- 운영 런처는 `Start-Process -WindowStyle Hidden`을 사용합니다. 실제 `STARTUPINFO`의 `SW_HIDE` 시작값으로 합성 WinForms의 첫 `Show()`가 숨겨지는 현상을 재현했습니다. [Microsoft ShowWindow 문서](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow)

## 변경

- 로그인 메뉴를 선택하면 트레이 메뉴와 하위 메뉴를 닫고 UI 스레드에 로그인 창 열기를 예약합니다. `Show()` 뒤 `IsWindowVisible`이 거짓이면 `ShowWindow(SW_SHOWNORMAL)`으로 다시 표시하고 전면 표시와 활성화를 요청합니다. 표시를 확인할 수 없으면 WebView2 초기화 전에 중단해 안내합니다. [창 표시](WindowsGeminiLoginWindow.cs) · [트레이 연결](TrayApplicationContext.GeminiWeb.cs)
- 로그인 중 Gemini 밖으로 이동했다가 Gemini의 다른 경로로 돌아오면 공식 사용량 페이지를 한 번 다시 엽니다. 체크가 다른 경로에 도달하면 창을 열어 둡니다. 사용량 페이지 도착 시에만 로그인 성공을 처리합니다.
- 최초 탐색 실패는 상태 창 피드백과 경고 로그에 단계와 WebView2 오류 코드만 남깁니다. 사용자가 창을 닫으면 로그인 미완료로 끝냅니다. [로그인 탐색](WindowsGeminiWebSession.cs)

## 다른 Windows 창 확인

상태 창의 `ShowNearTray()`는 `Show → BringToFront → Activate`를 사용합니다. 일반 실행의 시작 경로에서도 호출됩니다. 통계와 모바일 송신 창은 `Show → Activate`를 사용합니다. 세 창 모두 네이티브 표시 상태를 검사하거나 다시 표시하는 경로가 없으므로, 숨김 시작 정보가 적용되는 첫 창이면 같은 영향을 받을 수 있습니다. 코드 기준 확인이며 이번 수정 범위는 Gemini 로그인 창입니다. [상태 창](StatusWindow.cs) · [실행·통계·모바일 송신](TrayApplicationContext.cs)

## 검증

- .NET SDK 10.0.404에서 Windows Release 빌드와 Portable publish 성공. 빌드 경고 0건·오류 0건.
- 실제 `STARTUPINFO`를 읽는 합성 창 회귀 검사 성공. 일반 시작에서 기존 `Show()`는 표시, `SW_HIDE`에서 기존 `Show()`는 숨김, 같은 `SW_HIDE`에서 수정 후에는 표시였습니다. [독립 회귀 검사](tools/Test-WindowsGeminiLoginWindow.ps1)
- 단일 EXE self-test **292,789건·종료 코드 0**. Windows Gemini 검사 39건과 기존 symlink SKIP 1건을 포함합니다. 같은 탐색 ID의 외부 리디렉션과 별도 이동 모두를 검사하고, 사용량 페이지 재탐색 성공·대화 화면 복귀 후 대기 유지 및 최초 탐색 실패의 민감 값 제외를 확인했습니다.
- 고정 `after` 슬롯 smoke 1회 **1,208건·PNG 75개·종료 코드 0**. 가짜 로그인 세션이 트레이 메뉴와 하위 메뉴가 닫힌 뒤 시작되는지 검사했습니다. 보고 경로는 `artifacts/r296/windows-tests/after/20261121-184946-196-2d414e/`입니다.

## 배포 후보

| 구분 | 값 |
|---|---|
| 이전 EXE | `2.9.5.0` |
| 이전 소스 커밋 | `4b351b2cb67e8af298789a9be6c481b28ba78d0b` |
| 이전 SHA-256 | `28CF1F25A44B9F823EC2C6CFF686705DC534A0BC4C68922013D3FE4EEEAA3607` |
| 후보 파일 버전 | `2.9.6.0` |
| 후보 소스 커밋 | `15844784a24bc6ec56799c0eb66e166c618bfb0a` |
| 후보 SHA-256 | `9C1BAFC7E3A9313F4D56B0BB8B88117B873E79E016EE5E52159DE5D68314B5AF` |
| 후보 크기 | 31,122,829 바이트 |

후보와 receipt는 `artifacts/release-2.9.6-preparation-20261121/`에 있습니다. 배포 준비 당시의 2.9.5 EXE 버전·커밋·해시를 지난 `candidate.json`과 대조하고 이전 신원으로 사용했습니다. System32에서 읽기 전용 Git 사전 확인을 실행해 두 커밋 모두 `Readable=true`를 확인했습니다. 사용자는 기존 [Windows 외부 배포 절차](WINDOWS_DEPLOYMENT.md)에 따라 새 receipt로 2.9.6을 배포했습니다.

## 사용자 확인과 후속

**2.9.6 사용자 확인**

- 배포 직후 상태 창을 열기 전에 트레이에서 **로그아웃 → 로그인…**을 실행하자 로그인 창이 정상 표시됐습니다. 숨김 시작에서 창 표시 보정을 확인했습니다.
- Google 로그인 뒤 대화 화면에 머무르는 현상이 해소됐습니다. 로그인 창이 자동으로 닫히고 조회가 켜졌습니다.
- 일반 브라우저와 표시값 일치, 끄기와 다시 켜기, 재시작 후 조회 유지, 로그아웃 후 재로그인 요구를 확인했습니다.

**2.9.5 사용자 확인**

- 조회 중 입력 포커스가 이동하지 않았고 작업표시줄과 Alt+Tab에 조회 창이 표시되지 않았습니다.
- 15분 자동 갱신을 확인했습니다. 2.9.6에서도 같은 조회 주기와 숨김 WebView2 조회 경로를 사용합니다.

[#164](https://github.com/hydron75/ai-burger-clock/pull/164) 병합 후 같은 문구의 메뉴 상수 전환과 후보 소스·병합 HEAD의 차이를 기록합니다.
