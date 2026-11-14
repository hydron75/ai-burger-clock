# Windows 화면 검사 결정성·실패 순간 진단

2026-11-14. 착수 기준 main `fe84da2c5662b50bde189010e6c8eafc62e8c374`, 최종 확인 기준 main `b9e144d1bc5dc07ae24f4fe2a51216c65bacbfbb`(#154 포함).
별도 Windows 검사 전용 PR입니다. 앱 동작·버전·공통 원본·Mac·패키지·운영 EXE·DB·설정·등록은 바꾸지 않습니다.
Windows 2.9.3 후보/receipt는 다른 브랜치에서 준비했고 이 변경을 기다리지 않습니다.

## 보존한 실패와 확인 한계

| 실행 폴더 시각(KST) | 결과 |
|---|---|
| 2026-11-05 21:06:32 | 2.8.9 후보 첫 smoke: Control clipped, 19 PASS·exit 1 |
| 2026-11-14 10:46:21 | #145 첫 smoke: 첫 상태 팝업 WM_MOUSEWHEEL 후 조건 실패, 23 PASS·exit 1 |
| 2026-11-14 13:39:25 | #150 최종 HEAD 첫 smoke: quota WindowFromPoint 하위 카드 검사 실패, 688 PASS·exit 1 |
| 2026-11-14 13:48:21 | 사용자 승인 동일 HEAD e4b3e40·동일 런타임 파일 12개로 재실행 1회: 1,174 PASS·exit 0 |

세 실패의 공통 원인은 미확정입니다. 외부 창/토스트·실패 순간 실제 반환 HWND·전체 모니터 배치가 과거 로그에 없습니다.
#150 실행의 앞선 상세 캡처는 192 DPI였지만 다른 실패 시점의 DPI를 이 값으로 추정하지 않습니다.
과거 smoke.failure의 본체 포커스 값은 보조 fixture finally가 본체를 다시 표시한 뒤일 수 있어 실패 순간의 증거로 보지 않습니다.
원본 로그는 각 기존 artifacts 실행 폴더에 유지합니다. 이 PR에서 당시 실행을 다시 만들거나 성공으로 합산하지 않습니다.

## 검사 변경

- `SmokeUiProbe`: 실제 마우스 위치 대신 reference 창으로 작업 영역을 선택합니다. 독립 비활성화 fixture를 먼저 생성한 뒤 실제 DeviceDpi가 적용된 물리 Bounds를 한 번 지정하고 layout·UI queue sentinel 처리를 완료합니다. Windows DPI 설정이나 프로세스 DPI 모드를 바꾸지 않습니다. 96/144/192 DPI의 기존 합성 측정 검사는 유지합니다.
- 상태의 native 팝업 입력은 실제 StatusDetailController/QuotaDetailPopup을 별도 Form에서 검사해 StatusWindow의 Deactivate→Hide와 수명을 분리합니다. 실제 StatusWindow의 네 Provider×네 시나리오, 클릭·기록·접근성·수명/숨김/Dispose 경로는 유지합니다.
- 입력은 같은 target HWND에 한 번씩 PostMessage하고 같은 HWND 뒤에 게시한 sentinel이 처리될 때까지 기다립니다. 휠·down/up은 처리 완료 후 단언합니다. hover는 VisibleChanged 완료 이벤트를 기다립니다. 기존 4초 상한을 유지하며 입력/단언 재시도·임의 sleep 증가는 없습니다. 맨 끝까지 여러 휠 메시지를 보내는 것은 정상 스크롤 시퀀스이며 실패 재시도가 아닙니다.
- 비스크롤 팝업의 직접 WM_NCHITTEST→HTTRANSPARENT와 부모 Control 계층의 target 판정을 별도 계약으로 검사합니다. viewport 밖 점과 다른 카드의 negative 사례도 포함합니다. 전역 WindowFromPoint는 통과 조건이 아니라 실패 진단에서만 읽습니다.
- 작은 작업 영역의 의도된 AutoScroll은 DisplayRectangle을 내용 범위로 검사하고, 일반 창은 ClientRectangle을 사용합니다. 스크롤 내용 밖의 control은 여전히 실패합니다. 텍스트 실제 크기·하단 안내 여백 등 기존 잘림 조건은 유지합니다. 정상 표시 뒤 큐/레이아웃 완료를 한 번 확인하며 제품의 FitWorkArea를 재호출해 실패를 숨기지 않습니다.

## 실제 데스크톱 라우팅과 구분

[WindowFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint)는 부모/프로세스 인자를 받지 않습니다.
[HTTRANSPARENT](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest)는 같은 스레드의 창에 대한 규칙입니다.
부모 범위 target 확인과 직접 PostMessage만으로 다른 앱을 포함한 데스크톱 전체 클릭 관통을 증명하지 않습니다.

또한 [.NET 10.0.15 Control.WmMouseUp](https://github.com/dotnet/winforms/blob/v10.0.15/src/System.Windows.Forms/System/Windows/Forms/Control.cs#L10731)은 MouseClick 생성 조건으로 전역 WindowFromPoint를 사용합니다.
따라서 scoped native down/up 전달과 실제 StatusWindow의 기존 관리형 MouseClick→페이지/기록 검사를 분리합니다.
물리 마우스·전역 Z-order·다른 앱/토스트가 겹친 실제 클릭은 별도 통제 환경 검증이며 이 PR의 smoke 통과가 대신하지 않습니다. 외부 가림을 감지했다고 검사를 통과로 바꾸거나 자동 재실행하지 않습니다.

## 실패 진단·개인정보

- native fixture catch에서 Hide/Dispose/본체 복구 전에 `UI-FAILURE:` JSON을 표준 오류에 남깁니다. 레이아웃 잘림은 그 지점에서 바로 기록합니다. 기존 상위 smoke.failure도 유지해 앞뒤 상태를 구분합니다.
- 시각·검사 종류·호스트/대상/팝업의 Bounds/Client/Display 영역·DPI/자동 배율·스크롤 범위와 값·모니터 Bounds/WorkingArea를 기록합니다.
- 기대 host/target/popup과 실제 관측창/전경창의 HWND·PID·TID·RootHwnd·OwnerHwnd·표시 여부·관계를 허용된 메타데이터로만 기록합니다.
- **다른 앱의 제목·본문·클래스명·프로세스명은 읽거나 기록하지 않습니다.** GetWindowText/프로세스 명령행/UI Automation/화면 캡처를 사용하지 않습니다. Native WindowMetadata 필드 allowlist를 self-test로 고정하고 합성 제목 canary도 검사합니다.
- 진단 실패는 원래 검사 오류를 대체하지 않으며 오류 형식만 남깁니다. 정상 운영 앱에서 새 수집/기록을 시작하지 않습니다.

## 검증 기록

검증한 코드 커밋은 `42e1cd5520e2c752dfdd35553304167acc0df9f9`입니다. 이후 커밋은 이 실행 기록만 보완합니다.
기준 main에 이미 들어온 Windows 2.9.3과 Mac 버전 기록을 상속하며 이 PR 자체는 버전 필드·Mac 파일을 바꾸지 않습니다.

| 확인 | 결과 |
|---|---|
| SDK / build.ps1 | 10.0.404, 경고 0·오류 0 |
| --self-test | 293,225 PASS = 공통 292,094 + Windows 1,131. 기존 symlink fixture 1건 SKIP 유지 |
| 최종 고정 after 슬롯 smoke | 1회 1,179 PASS, exit 0, PNG 60개. 입력/단언 재시도 없음 |
| 실제 메시지 | Gemini/OpenAI 상태 상세와 Gemini 한도 상세의 휠로 맨 끝/역방향/범위 제한, 상태·한도 각각 부모 범위 target + 직접 WM_NCHITTEST + down/up 각 1회 통과 |
| 진단 | 외부 창 메타데이터 allowlist 2건, 부모 범위 negative 2건, 정리 전 표시 상태·제목 canary 검사 통과 |
| 2.9.3 후보 대비 | 일반 화면 PNG 58/58·레이아웃 JSON 41/41 SHA-256 동일. 나머지 PNG 2개는 독립 fixture로 옮긴 긴 상태 본문 합성 캡처 |
| 독립 정적 리뷰 | 기대 host/target/popup의 HWND/PID/소유 정보 누락 P2 보완 후 확정 P1/P2 없음. 리뷰어는 native 실행 결과를 독립 재실행하지 않음 |
| 운영 경계 | 실제 OS 알림 제출/운영 알림 등록/사이트 전송 0회. Codex의 운영 EXE·DB·설정 변경과 운영 앱 직접 실행 없음 |

smoke 건수는 기존 1,174에서 1,179로 바뀝니다. 부모 범위 negative 2건, 정리 전 상태/제목 제외 2건, 스크롤 내용 범위 2건을 더했습니다.
외부 가림에 좌우되는 native MouseClick→Claude 페이지 1건은 위의 native down/up 계약과 기존 실제 상태 카드의 관리형 페이지 경로로 분리했습니다. 전역 클릭 보장을 통과한 것으로 세지 않습니다.

### 이번 작업 중 보존한 실행

모든 경로는 이 PR의 별도 worktree 아래 artifacts입니다. 실패 로그를 지우거나 성공으로 합산하지 않았습니다.

| 코드 커밋 / 실행 폴더 | 결과 |
|---|---|
| 524ef93 / windows-smoke-diagnostics-final-20261114/windows-tests/after/20261114-145705-426-8c6cef | 28 PASS·PNG 1·exit 1. native 휠 통과 후 PNG 파일명 저장에서 GDI+ 오류. 정리 전 UI-FAILURE JSON 기록 확인 |
| 524ef93 / ui-diag/windows-tests/after/20261114-145833-325-51b70f | 119 PASS·PNG 11·exit 1. 뒤쪽의 더 긴 viewport 캡처 파일명에서도 같은 저장 오류 |
| 524ef93 / d/windows-tests/after/20261114-150026-471-547f53 | 결과 경로 단축 뒤 1,179 PASS·PNG 60·exit 0 |
| 42e1cd5 / d/build/20261114-150451-551 | 최신 main으로 재배치 후 build·self-test 통과 |
| 42e1cd5 / d/windows-tests/after/20261114-150557-228-679668 | 최종 HEAD 코드의 1회 smoke: 1,179 PASS·PNG 60·exit 0 |

첫 두 실패는 이 worktree의 긴 결과 경로에 따른 저장 오류입니다. 2×2 합성 PNG를 파일명 방식으로 저장해 245자 성공·278자 GDI+ 실패를 재현했고, 같은 278자 경로도 FileStream 방식이면 저장됐습니다.
첫 길이 계산에서 images 하위 폴더를 빠뜨렸으므로 두 번째 실행도 긴 이름에서 실패했습니다. 이후 기존 PNG 60개 전체 경로를 계산해 최장 255자인 ReportGroup `d`를 사용했습니다.
검사 코드·메시지·단언·대기 시간은 바꾸지 않았고, 이미지 저장 방식 변경도 이번 범위에 추가하지 않았습니다. 긴 경로에서도 파일명 방식 저장을 보장하지 않습니다.
재배치 전후 Windows/공통 C# 원문은 동일하며 main의 버전·문서·Mac 변경만 상속했습니다.

재배치 뒤 기본 샌드박스 build의 NU1900은 NuGet 취약성 데이터 접속 실패였습니다. 감사 설정을 끄지 않고 정상 네트워크 컨텍스트에서 `dotnet restore --force --no-cache -warnaserror` 후 다시 빌드해 통과했습니다.
각 최종 바이너리의 smoke는 1회이므로 반복 트레이 기록 무증가 판정은 미평가입니다.
물리 마우스·전역 Z-order·외부 앱/토스트 가림·다양한 실제 DPI/모니터 조합과 과거 세 실패의 공통 원인은 여전히 미확인입니다.
검사 뒤 현재 2.9.3 배포본의 외부 재실행과 트레이·상태 보기·한도 보기 정상은 사용자 확인입니다.
