# Windows 한도 알림의 알림 센터 보관

## 범위와 배포 상태

이 문서는 Windows 전용 한도 알림 전송과 등록 관리 계약이다. 공통 판정·문구·Mac 알림은 바꾸지 않는다. **#86 병합 뒤 Windows 2.8.2를 사용자 승인으로 로컬 배포·실행하고, 실제 배포 경로의 운영 등록을 외부 Windows에서 1회 완료했다.** 바로가기·AUMID·COM 경로 일치와 해제 dry-run의 무변경을 확인했다. 시험 알림은 보내지 않았고 버전 PR의 병합은 사용자에게 남긴다. 실제 검사·백업·교체·등록/해제 dry-run·최종 EXE 해시는 [2.8.2 배포 기록](MAINTENANCE_2_8_2.md)에 둔다. 아래 #86의 운영 미등록·미배포 검증 결과는 당시 범위를 그대로 보존한다.

- 한도 회복·주간 조기 초기화 알림만 Windows 토스트로 보낸다.
- 시간표 전환·공식 상태 권고·공휴일 설정 안내는 기존 풍선 알림을 유지한다.
- 제목·본문·판정 조건·기본 꺼짐·설정 위치·Provider별 대기열·통합 알림·기존 간격은 유지한다.
- 알림을 클릭해도 상태 창 열기, 새 조회, 설정 변경 등 새 동작은 하지 않는다.
- 알림 센터 보관은 무기한 저장 보장이 아니다. 사용자 삭제, Windows 정책·설정, OS 보관 한도 등으로 사라질 수 있다.

## 풍선과 토스트의 차이

`NotifyIcon.ShowBalloonTip`은 일시적인 풍선 표시용 API다. 인자로 준 시간은 Windows가 실제로 표시하는 시간을 보장하지 않으며, 알림 센터 보관 계약도 아니다. 화면에 보였다는 사실과 나중에 알림 센터에서 찾을 수 있다는 사실을 구분한다. 트레이 아이콘 그림 변경만으로 이력이 삭제된다고 단정하지 않는다. [Microsoft API 문서](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.showballoontip?view=windowsdesktop-10.0)

Windows 비패키지 데스크톱 앱의 토스트 표시에는 AUMID가 지정된 시작 메뉴 바로가기가 필요하다. 이 앱은 센터 보관과 종료 후 클릭 처리를 위해 별도 COM activator도 등록한다. AUMID 바로가기만으로 센터 보관까지 보장된다고 취급하지 않는다. [시작 메뉴 AUMID 등록](https://learn.microsoft.com/en-us/windows/win32/shell/enable-desktop-toast-with-appusermodelid), [Microsoft 데스크톱 activator 지침](https://github.com/MicrosoftDocs/windows-dev-docs/blob/2e559c1c7064af00d993650b1eb2f380a7153c94/hub/apps/design/shell/tiles-and-notifications/send-local-toast-desktop-cpp-wrl.md)

## 운영 등록: 배포 때 한 번, 명시적으로 실행

운영 식별자는 버전·빌드 경로에 따라 바꾸지 않는다.

| 항목 | 값 |
|---|---|
| AUMID | `Hydron75.AIBurgerClock.Quota` |
| activator CLSID | `{10B09E94-A6B4-4ABA-9B9F-C1551C7A409B}` |
| 레지스트리 범위 | 현재 사용자의 `HKCU\Software\Classes` 아래 앱 전용 키 |
| 실행 대상 | 승인된 실제 배포 경로의 `dist\win-x64\AI Burger Clock.exe` |

운영 등록 명령은 다음과 같다. **기본 self-test·smoke와 테스트 슬롯에서는 실행하지 않으며, 사용자 승인된 실제 배포 단계에서만 한 번 실행한다.**

~~~powershell
& '<실제 배포 경로>\AI Burger Clock.exe' --register-quota-notifications
~~~

이 명령만 다음 항목을 만들거나 검증한다.

- 현재 사용자 시작 메뉴의 앱 전용 바로가기와 `System.AppUserModel.ID`, `System.AppUserModel.ToastActivatorCLSID` 속성.
- 전용 `HKCU\Software\Classes\AppUserModelId\Hydron75.AIBurgerClock.Quota` 등록.
- 전용 `HKCU\Software\Classes\CLSID\{10B09E94-A6B4-4ABA-9B9F-C1551C7A409B}\LocalServer32`의 인용된 실제 EXE 경로와 COM 전용 실행 인자.

관리자 권한·HKLM·서비스·예약 작업·별도 설치 프로그램은 사용하지 않는다. 시작 메뉴 바로가기는 자동 시작 등록과 별개다. 기존 HKCU Run·StartupApproved 설정은 변경하지 않는다.

실행 파일 이름만 같다는 이유로 운영 등록을 허용하지 않는다. 실제 프로세스 EXE의 정규화 경로가 `dist\win-x64\AI Burger Clock.exe`로 끝나는지 확인하며, `bin`·`obj`·`artifacts`·`.ai-burger-clock-tests` 경로 세그먼트와 reparse 경로를 거부한다. 이것은 외부 승인 목록에 저장된 특정 저장소 경로를 인증하는 장치가 아니라 배포 경로 모양과 테스트 경로를 구분하는 방어다. 별도의 `dist\win-x64` 복사본에서 운영 등록을 실행하면 안 된다. #86 개발 PR에서는 운영 등록을 실행하지 않았고, 실제 배포본의 등록은 사용자 승인된 2.8.2 배포 단계로 분리한다. 기존 바로가기·레지스트리가 다른 대상이나 소유자로 되어 있으면 보존하고 오류를 알린다.

일반 앱 실행·알림 발송·COM 활성화·self-test·smoke는 운영 등록을 자동 생성하거나 복구하지 않는다. 등록을 지운 뒤 앱을 다시 실행해도 등록이 조용히 재생성되지 않는다. 런타임의 `CoRegisterClassObject`는 이미 허용된 COM 객체를 현재 프로세스에서 받기 위한 것으로, 영구 레지스트리 쓰기와 구분한다.

## 클릭과 COM 전용 실행

Windows가 `--quota-toast-activated`로 EXE를 실행하면 일반 시작보다 먼저 전용 분기로 들어간다.

- 정상 앱의 단일 인스턴스 mutex를 획득하거나 충돌 검사로 조기 종료하지 않는다.
- WinForms 트레이·상태 창·타이머·조회 모니터·DB·자동 시작 처리를 만들거나 실행하지 않는다.
- COM class factory를 준비하고 `INotificationActivationCallback`만 받아 처리한다. 본문 인자는 명령으로 실행하지 않는다.
- 무동작 콜백이 끝나면 COM 등록을 해제하고 정상 종료한다. 콜백이 오지 않는 경우에도 제한된 대기 뒤 종료한다.
- 이미 정상 앱이 실행 중이어도 이를 다시 시작하거나 종료하지 않는다. 실행 중인 앱에 전달되는 콜백도 같은 무동작 계약을 따른다.

따라서 앱 종료 후 알림 클릭은 짧은 COM 전용 EXE 실행을 만들 수 있지만 정상 앱 상주 실행을 시작하지 않는다. 운영 활성화는 DB·조회·설정·발송 로그를 쓰지 않는다. 통합 검사에서만 고정 시험 폴더에 민감하지 않은 활성화 증거를 남길 수 있다.

## 발송 결과와 풍선 대체

등록을 정상으로 검증하고 Windows 토스트 API에 제출한다. 등록 누락·잘못된 등록·제출 예외 등 전송 경로 오류이면 동일한 제목·본문으로 기존 풍선을 한 번 요청한다. 풍선 대체 때문에 대기열·판정·재시도·간격을 새로 만들지 않는다.

사용자가 Windows에서 알림을 껐거나 정책으로 차단한 것으로 확인되면 풍선으로 우회하지 않는다. 방해 금지 때문에 배너가 보이지 않는 것, 화면 표시 여부를 확인할 수 없는 것, 센터에서 이미 사라진 것은 전송 예외가 아니며 중복 발송 사유로 쓰지 않는다.

Windows는 한 번도 알림을 보내지 않은 비패키지 ID의 `ToastNotifier.Setting` 조회에서 `ERROR_NOT_FOUND`를 반환할 수 있다. 이 경우에만 상태 미확인으로 실제 알림을 한 번 제출하고 Windows의 정책 집행을 따른다. 설정 확인을 위한 숨은 준비용 알림은 보내지 않는다. 다른 조회 오류는 전송 오류이며 확인된 Windows 차단은 COM 서버 오류보다 먼저 처리한다. 제출 후 비동기 `Failed`는 진단만 남기고 재발송하지 않는다. [Microsoft Toolkit 공식 구현의 첫 발송 전 Setting 처리](https://github.com/CommunityToolkit/WindowsCommunityToolkit/blob/main/Microsoft.Toolkit.Uwp.Notifications/Toasts/Compat/ToastNotifierCompat.cs).

## 한도 알림 발송 진단

파일: `%LOCALAPPDATA%\AIBurgerClock\logs\quota-notification-delivery-v1.jsonl`

| 기록 항목 | 내용 |
|---|---|
| 시각 | UTC ISO 8601 |
| 종류 | 회복 / 조기 초기화 / 통합 |
| Provider | `Codex`(화면 이름 ChatGPT) / `Claude` / `Gemini` |
| 방식 | 토스트 / 풍선 대체 |
| 결과 | API 제출, 확인 가능한 차단, 실패, 풍선 대체 요청 등 정해진 값 |
| 오류 | 필요한 경우 허용 목록의 사유·수치 오류 코드만 |

제목·본문·XML·사용률·남은 비율·리셋 시각·요금제·계정 식별자·원문 예외 메시지는 저장하지 않는다. 최근 256건, 최대 128KiB로 제한하며 오래된 기록부터 버린다. 파일 쓰기 실패로 알림·화면·한도 조회가 멈추지 않는다.

`Submitted`와 같은 제출 성공은 **Windows API 호출이 받아들여졌다는 뜻**이며 실제 배너 표시·사용자 열람·센터 보관을 증명하지 않는다. 풍선 요청 성공 역시 실제 표시의 증거가 아니다. 배너 종료 이벤트를 센터 이력 삭제로 해석하지 않는다.

이 파일은 조기 초기화 후보 판정 파일 `quota-early-reset-decisions-v1.jsonl`, 다음 조회 예약 파일 `quota-polling-diagnostics.json`과 별개다. 판정이 `Alert`였다는 사실만으로 발송 성공을 단정하지 않는다. 공개 PR에는 원문 파일이나 실제 계정 화면을 올리지 않는다.

Windows AppData 가상화가 있는 도구에서 파일을 열 때는 요청 경로와 실제 열린 경로를 대조한다. 운영 확인은 필요하면 Codex 밖 PowerShell에서 수행하며 오래된 보관 사본을 현재 앱 기록으로 보고하지 않는다.

## 등록 해제와 재등록

앱을 정상 종료한 뒤 실제 배포 EXE에서 먼저 대상을 확인한다.

~~~powershell
& '<실제 배포 경로>\AI Burger Clock.exe' --unregister-quota-notifications --dry-run
& '<실제 배포 경로>\AI Burger Clock.exe' --unregister-quota-notifications
~~~

dry-run은 경로·소유권을 검사하고 제거 대상을 보여 주지만 쓰지 않는다. 실제 해제도 이 앱이 소유하고 올바른 배포 경로를 가리키는 전용 바로가기·AUMID·CLSID만 제거한다. 소유권·경로가 맞지 않으면 삭제하지 않는다.

- 다른 앱·기존 NotifyIcon 생성 AUMID·사용자 알림 설정·자동 시작·DB·진단 파일은 지우지 않는다.
- 운영 `History.Clear()`를 호출하지 않고, 기존 운영 알림을 일괄 삭제하지 않는다.
- 등록을 제거하면 이미 남은 알림의 표시·클릭 동작에도 Windows의 영향을 받을 수 있다. 제거 뒤 기존 이력의 사용 가능성을 보장하지 않는다.
- 해제 후 활성화된 한도 알림은 등록 오류 경로의 풍선 대체를 사용한다. 토스트를 다시 쓰려면 동일한 실제 배포 EXE에서 등록 명령을 명시적으로 실행한다.

## 검사와 정리 경계

### 기본 자체 검사·smoke

가짜 전송기를 사용한다. 토스트뿐 아니라 검사 시나리오가 요청하는 기존 풍선도 가로채므로 실제 배너·알림 센터 항목·운영 등록을 만들지 않는다. 데이터 검사는 합성 알림과 임시 파일로 수행한다. `SuppressPopup=true`는 센터에 조용히 추가하는 옵션이므로 시험 이력 방지 수단으로 사용하지 않는다.

검사 대상은 등록 경로·소유권 거부, 해제 dry-run, 발송/차단/실패/풍선 대체, 로그 허용 필드·상한·쓰기 실패, COM 전용 분기와 단일 인스턴스 분리다. 공통 판정과 Mac 파일은 바꾸지 않는다.

### 명시적 Windows 통합 검사

기본 smoke와 분리된 `--quota-toast-test <register|send|history|unregister|unregister-dry-run|host|stop-host|invoke|invoke-unknown>` 모드를 둔다. 이 중 `send`만 합성 Windows 알림을 요청하며, 나머지는 시험 등록·이력 확인·호스트 수명·COM 연결 확인에 사용한다. 이 모드는 아래 시험 전용 ID만 사용하며 임의 AUMID·CLSID 또는 운영 ID를 선택할 수 없다.

| 항목 | 시험 전용 값 |
|---|---|
| AUMID | `Hydron75.AIBurgerClock.Quota.Test` |
| activator CLSID | `{5FEEEA89-C21D-45B1-9946-4A69A7BD3625}` |
| 데이터 | 실제 계정과 무관한 합성 제목·본문 |

검증에는 Portable 프로필로 만든 framework-dependent **publish 단일 EXE**를 별도 시험 위치에서 사용한다. 현재 운영 dist EXE를 덮어쓰지 않는다. 일반 bin 실행 결과를 단일 EXE publish 검증으로 대신하지 않는다.

1. 시험 전용 등록 뒤 합성 알림을 보내 배너 표시와 종료를 관찰한다.
2. 배너 종료 뒤 시험 AUMID의 센터 이력이 남는지 API와 실제 화면으로 구분해 확인한다.
3. 트레이·DB·조회가 없는 시험 콜백 호스트를 `host`·`stop-host`로 종료·재실행한 뒤에도 그 이력이 남는지 확인한다. 이 검사를 일반 앱의 실제 재시작으로 표현하지 않으며, 일반 앱 재시작도 검증했다면 그 결과를 따로 구분한다.
4. 정상 호스트 실행 중 클릭이 기존 단일 인스턴스를 방해하지 않는지 확인한다.
5. 호스트 종료 뒤 클릭으로 COM 전용 프로세스가 잠깐 실행되고 정상 종료하는지, 트레이·조회·DB·자동 시작 경로가 실행되지 않는지 확인한다.
6. 시험 AUMID와 해당 알림 tag/group의 이력, 시험 전용 바로가기·레지스트리만 정리한다. 운영 ID에는 이력 삭제 API를 호출하지 않는다.

알림 센터 확인, 실제 클릭, 프로세스 종료를 각각 관찰한 결과만 성공으로 적는다. `invoke`는 실제 `CoCreateInstance`와 COM 콜백을 검사하지만 알림 클릭은 아니므로, 이것이나 API `Show` 성공만으로 실제 배너·클릭 검증을 통과했다고 쓰지 않는다. UI 도구에 실제 앱이 노출되지 않으면 사용자에게 시험 알림의 표시·클릭 확인을 요청한다. 결과·미수행 항목·정리 완료 여부는 개발 PR에 별도로 기록한다.

## 단일 EXE와 개발 의존성

Windows 버전이 포함된 .NET TFM의 SDK 참조로 `Windows.UI.Notifications`를 사용한다. Windows App SDK 런타임·알림 Toolkit 패키지·별도 설치 프로그램은 추가하지 않는다. 배포 형식은 framework-dependent 단일 EXE를 유지한다. 실제 publish에서 WinRT projection과 COM 콜백이 동작하는지는 위 통합 검사로 확인한다. [Microsoft .NET 6+ WinRT 호출 지침](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)

단일 EXE 내부 어셈블리 위치나 추출 폴더를 COM 실행 경로로 등록하지 않는다. 실제 프로세스 EXE 경로를 사용하고 인용 처리한다. [.NET 단일 파일 배포 지침](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

## 2026-10-22 Windows 검증 (운영 미등록·미배포)

아래는 #86 개발 PR 당시 검증 기록이며 이후 2.8.2 운영 배포와 구분한다. 제품 코드 기준 커밋은 `c5d426108b00c4f25a88bb674d76ec35b713bf3e`다. 버전 상수는 2.8.1 그대로였고, 이 개발 산출물을 운영 2.8.1로 교체하지 않았다. SDK 10.0.402에서 확인했다.

| 검사 | 결과 |
|---|---|
| `build.ps1` / 별도 Portable publish | 경고 0 / 오류 0 |
| self-test (일반 빌드와 publish 단일 EXE 각각) | 255,761건 PASS / 실제 ExitCode 0 |
| 공통 / Windows 전용 | 255,062건 그대로 / 307 → 699건(+392) |
| 새 Windows 자체 검사 | 전송·제한 로그 323건 + XML·등록 guard·COM·Windows 차단 우선순위 69건 |
| 최종 publish smoke | 837 PASS / 실제 ExitCode 0, 기준 830 대비 +7 |
| 합성 렌더 비교 | PNG 22개·layout JSON 16개 모두 SHA-256 동일 |
| 기본 smoke 알림 | 모든 Windows 검사 호스트에 가짜 전송기 주입, native BalloonTipShown 0 |
| 일반 mutex 점유 + 명시적 callback-only 프로세스 | 20회 모두 콜백/프로세스 ExitCode 0 (약 0.23~0.26초) |
| 일반 mutex 점유 + 실제 COM cold activation | 20회 모두 호출 ExitCode 0 (약 0.21~0.25초), 잔류 프로세스 0 |
| 기존 배포 앱 실행 중 cold activation | 시험 콜백 성공, 기존 배포 프로세스 유지, 시험 프로세스 잔류 없음 |

기준 화면 사본은 main `82ecdce4750b15dfda9d3f11a30ba687163779c7`에서 만들었다. **기준 사본에서도 기존 풍선 API 호출 4곳만 막아** 알림 누적 없이 기존 문구·필드·UI 검사와 PNG를 비교했다. 이 기준 사본 수정은 로컬 artifacts에만 있으며 제품 공통 코드 변경으로 포함하지 않는다. 공통 기존 symlink fixture 하나는 임시 파일시스템의 링크 생성 제한으로 SKIP이었고 이 부분을 통과로 세지 않는다.

### 실제 시험 알림과 종료 후 클릭

- 계정과 무관한 시험 알림을 별도 AUMID로 한 건 전송했다. 배너 종료 뒤 알림 센터 보관은 **사용자 확인**, History API에서도 1건이었다.
- 발송 프로세스 종료와 동일 publish EXE의 실제 WinForms 합성 smoke 호스트 재실행 뒤에도 1건이 유지됐다. 시험 콜백 호스트 수명과 운영 ID의 배포 검증을 혼동하지 않는다.
- 시험 프로세스가 모두 종료된 뒤 사용자가 센터 항목 본문을 클릭했다. 새로운 시험 콜백 기록과 잔류 프로세스 없음, 사용자 확인상 새 창·트레이 상주 없음, 클릭 후 시험 History 0건을 확인했다.
- 운영 등록/새 버전 배포는 하지 않았다. 정상 배포 경로의 기존 2.8.1을 같은 EXE로 다시 실행했고 SHA-256도 기존 기록과 같았다.

### 등록 뷰 차이와 정리

처음에는 도구 쪽에서 시험 CLSID가 보였지만 Codex 밖 PowerShell의 같은 `Test-Path`는 False였다. 이 상태에서 이미 실행 중인 COM 서버에 대한 연결은 성공했으나 cold activation은 즉시 `0x80040154`였다. **사용자가 외부 PowerShell에서 시험 ID만 등록**한 뒤 실제 COM cold activation이 성공했다. 정확한 격리 기전은 단정하지 않으며 도구의 같은 뷰 read-back만으로 Windows broker 등록 완료를 보장하지 않는다. 2.8.2의 운영 등록도 실제 사용 환경에서 외부 확인한다.

해제 dry-run은 쓰지 않고 대상을 출력했다. 실제 정리는 외부 PowerShell과 도구 양쪽의 **시험 ID만** 대상으로 수행했다. 시험 History 0, 시험 바로가기·AUMID·CLSID 없음과 운영 AUMID·CLSID 미등록을 확인했다. 운영 알림 이력을 지우거나 Windows 알림 설정을 변경하지 않았다. 개인 값 없는 로컬 검사 근거는 `artifacts/quota-toast/`, `artifacts/quota-toast-final/`에 보관하며 원문을 공개 PR에 올리지 않는다.

초기 NuGet 접근/오류 캐시 문제는 정상 네트워크 복원으로 해결했고, 새 로그의 `InvalidDataException` 격리 누락은 회귀 검사 중 수정했다. 첫 발송 전 Setting 조회 특성은 위의 한 번 제출 규칙으로 보완했다. 최종 결과는 이 보완 뒤의 결과다.

### 배포 크기와 남은 경계

Windows SDK projection이 단일 EXE에 포함되어 산출물은 기존 약 3.89MiB에서 약 28.19MiB로 커졌다. 별도 Windows App SDK 런타임이나 명시적 알림 NuGet 패키지는 추가하지 않았고 framework-dependent 단일 EXE 형식은 그대로다. 이 크기 영향도 2.8.2 배포 검토에 포함한다.

운영 ID 등록·운영 토스트·2.8.2 정상 실행, 재부팅/로그인/절전·알림 권한 차단의 실기·장기 운영은 이번에 수행하지 않았다. 차단/오류/풍선 대체는 가짜 전송기와 합성 검사로 확인했다. 실제 운영 승인 때의 등록·클릭·보관 확인은 이 시험 결과와 별개로 기록한다.

## 2.8.1과 #86의 동일 조건 측정

2026-10-22, 동일 테스트 슬롯 조건의 합성 트레이 경로를 각 버전 5회 실행한 중앙값이다. 두 버전에 같은 측정 hook을 주입했고 실제 계정 조회 대기·등록된 COM 서버 비용·cold start는 제외했다. 이 결과를 일반 앱의 전체 시작 시간이나 장기 메모리 누수 증거로 확대하지 않는다. [PR #86 측정 코멘트](https://github.com/hydron75/ai-burger-clock/pull/86#issuecomment-6339275867).

| 항목 | 2.8.1 | #86 | 차이 |
|---|---:|---:|---:|
| 시작(firstIdle) | 344ms | 371ms | +27ms |
| 안정 구간 작업 집합 | 79.96MiB | 88.61MiB | +8.65MiB, +10.8% |
| 안정 구간 Private Bytes | 25.39MiB | 25.84MiB | 반올림 전 차이 +0.44MiB |
| publish 단일 EXE | 3.89MiB | 28.19MiB | Windows SDK projection 포함 |

첫 한도 알림 때 토스트 처리 부분을 지연 생성하는 방안은 [BACKLOG 성능 후보](BACKLOG.md#성능-최적화-후보-보류)로만 기록했다. 2.8.2에는 구현하지 않으며 예상 절감량은 아직 확인하지 않았다. 지연 생성이 publish 파일 크기까지 줄여 주는 것은 아니다.
