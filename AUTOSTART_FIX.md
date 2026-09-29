# 2.0.2 자동 시작 수정 — 2026-09-23

## 확인한 원인

- 현재 사용자가 실행한 dist EXE는 2.0.1.0이었고, 남아 있던 bin/Release/AI Burger Clock.exe는 1.0.0.0이었다.
- 직접 읽은 HKCU Run 등록은 dist 경로였지만 Win32_StartupCommand 관리 조회에는 구버전 bin/Release 경로가 남아 있었다. 이 관리 조회만으로 중복 등록이나 직전 부팅 프로세스를 확정하지 않았다.
- 이 앱의 StartupApproved/Run 값은 03-00-00-00-5E-F4-18-5E-D1-4A-DD-01로 비활성화 상태였다. 포함된 시각은 2026-09-23 05:31:37 KST였다.
- 기존 IsEnabled는 Run 값 존재 여부만 확인했다. 경로 불일치와 Windows 별도 비활성화 상태를 구분하지 못했다.
- 다른 Run 위치, 시작프로그램 폴더 2곳, 예약 작업에서 관련 중복 항목은 발견되지 않았다.

## 수정 범위

- AutoStartManager.cs: 등록 없음 / 현재 파일 사용 / 다른 경로 / Windows 차단 / 확인 불가를 분리. 절대 경로 정규화와 대소문자 구분 없는 비교, --autostart 인수 검사, Windows 승인 상태 판정.
- StatusWindow.cs: 상태별 짧은 문구와 등록/현재 경로 툴팁. 프로그램이 표시를 동기화할 때 설정 변경 이벤트를 발생시키지 않음.
- TrayApplicationContext.cs: 메뉴와 창 활성화 시 재조회. 추가 타이머나 네트워크 polling 없음.
- AutoStartTests.cs(신규), SelfTest.cs: 레지스트리를 건드리지 않는 판정 검사 229개.
- SmokeTest.cs: 실제 UI 핸들러로 차단 해제/경로 복구/외부 변경 감지/미지 형식 거부를 검사. Run과 StartupApproved의 원래 값 및 형식을 finally에서 복원.
- AiBurgerClock.csproj: 2.0.2.0으로 버전 갱신. net10.0-windows / WinForms / x64와 기존 의존성 유지.
- README.md: 현재 자동 시작 동작과 검증 방법 반영.

일반 실행이나 창/메뉴 열기는 읽기 전용이다. 사용자가 체크박스/메뉴로 명시적으로 켤 때만 현재 실행 파일로 재등록하고, 이 앱의 알려진 Windows 비활성화 값만 제거한다. 해석할 수 없는 승인 형식은 덮어쓰지 않는다. 중간 오류가 나면 원래 등록값으로 복원을 시도한다.

Windows의 StartupApproved는 공개된 안정적 앱 계약이 아닌 내부 형식이다. 알려진 상태만 판정하고 향후 알 수 없는 형식은 확인 필요로 표시한다. 명시적 활성화 시 disable override를 제거하는 동작은 [Electron의 Windows 구현](https://github.com/electron/electron/blob/main/shell/browser/browser_win.cc)도 참고했다. Electron 등의 새 패키지는 추가하지 않았다.

## 이번 PC에 적용한 상태

- HKCU/Software/Microsoft/Windows/CurrentVersion/Run의 AI Burger Clock 값: dist/win-x64/AI Burger Clock.exe의 큰따옴표 포함 절대 경로 + --autostart.
- 같은 사용자의 Explorer/StartupApproved/Run에서 이 앱의 비활성화 값만 제거. 별도 프로세스에서 다시 읽어 등록과 허용 상태 확인.
- 다른 시작 항목과 구버전 파일은 수정/삭제하지 않음.
- 2.0.2 배포본을 다시 실행했으며 경로와 단일 실행 프로세스를 확인.
- 기존 숨겨진 트레이 앱을 컴퓨터 제어 도구로 종료할 수 없어 일관된 SQLite 백업 후 확인된 앱 프로세스만 종료해 교체했다.
- 실제 UI 입력 도구로 숨겨진 앱을 조작하지 못했으므로 아래 UI 결과는 명시적 개발 검사에서 실행한 실제 WinForms 메시지 루프/핸들러 및 렌더 검증 결과이다.

## 검증 결과

- Release build/publish: 오류 0, 경고 0. 최초 제한 환경의 NuGet 설정 읽기 권한 오류 후 허용된 사용자 환경에서 성공.
- 최종 단일 EXE 자체 검사: 실제 프로세스 종료 코드 0, 총 242,853 assertions.
  - 자동 시작 판정 229, Schedule/DST 242,363, Provider 파서/HTTP 95, 권고/모니터 96, SQLite/통계 70.
- 최종 EXE --smoke-test --verify-autostart: 종료 코드 0, 오류 출력 없음.
- 차단 상태 표시, 다른 경로 표시, 두 UI의 명시적 복구, 메뉴 열기 시 외부 설정 반영, 미지 형식 보존, 읽기 전용 표시, 두 레지스트리 값 정확 복원: 통과.
- 트레이 전용 시작, Schedule/카운트다운, 전환/Provider 알림, 중복 알림 방지, 공식 페이지 클릭, 실측 입력/메모/재오픈, 통계 기간/표: 통과.
- BalloonTipShown 10회 관찰. 실제 알림 표시 여부는 Windows 알림 설정에도 의존.
- 기존 150% DPI 렌더에서 새 체크박스 문구와 화면 경계 확인.
- 실제 사용자 DB: 교체 전/재실행 후 quick_check=ok, schema=1, UsageEvents=0, 실측 내용 SHA256 동일.
- 실제 재부팅/재로그인은 수행하지 않았다. 다음 로그인 시 자동 실행되는지 사용자 확인이 남아 있다.
- Win32_StartupCommand 조회는 수정 후에도 구버전 경로를 반환했다. 직접 레지스트리 읽기와는 불일치하며 캐시 여부를 확정하지 않았다. 이를 없애기 위한 Windows 관리 서비스 재시작 등 범위 밖 변경은 하지 않았다.

## 결과물 및 복구 자료

- 실행 파일: dist/win-x64/AI Burger Clock.exe
- FileVersion: 2.0.2.0
- 크기: 2,712,811 bytes
- SHA256: FCE69025F6B0E2421A131A6FA6CFC7A4A20DF10F6B7FCE6B27C006AF683CB98B
- 검사 로그: artifacts/self-test.txt, artifacts/autostart-ui/smoke.txt
- UI 렌더: artifacts/autostart-ui/autostart-disabled.png, autostart-other-path.png, autostart-unknown.png 등.
- 수정 전 소스/배포본: ../backups/AiBurgerClock-before-autostart-20260923-054109.zip
  - SHA256: C8C82FE59423F3F082EB4BABA63E85DEAB1D5CC4105534175A0F78A145456E0F
- 이 앱만의 원래 레지스트리 값: ../backups/autostart-before-20260923-0542.reg
  - 원래의 비활성화 상태를 복원하는 파일이므로 문제 해결 후 임의로 가져오지 않는다.
- 교체 전 SQLite 일관 백업: ../backups/burgerclock-before-autostart-20260923.db

보류 중인 Provider 상태 반영 트레이 색상 개선과 iOS 위젯은 이번 수정에 포함하지 않았다.
