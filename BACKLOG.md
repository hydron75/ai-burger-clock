# 다음 개선 항목

## 보류 후보: 한도 박스 요금제 표시와 CLI 설치 경로 지정

- 기록일: 2026-10-10 KST(Mac 담당). 둘 다 **보류**이며 구현하지 않았다. 조사 기록은 [MACOS_PORT 41절](MACOS_PORT.md#41-chatgpt-한도-박스에-주간-행만-표시되는-원인과-요금제-필드-확인). 계정별 값은 공개 저장소 규칙에 따라 적지 않는다.
- **한도 박스 요금제 표시(보류)**
  - 사실: 조회 응답에 요금제 필드가 있는 곳은 Codex(`planType`)뿐이다. Claude `/usage`와 agy `/usage`에는 없다. 앱 파서는 아직 `planType`을 읽지 않는다.
  - 제약: Claude 요금제는 별도 명령(`claude auth status`)이 필요한데 계정 정보를 읽어 "토큰·Keychain·쿠키를 읽지 않는다" 원칙과 충돌할 수 있다. Codex 요금제 문자열의 가능한 값은 한 계정으로 알 수 없어, 표시한다면 받은 문자열을 그대로 쓰는 쪽이 안전하다.
  - 반영한다면 공통 원본 변경이다: `QuotaReading` 선택 필드, 파서, 캐시 JSON(null 허용이면 기존 값과 호환), `QuotaPanelModel` 제목 줄. Windows `AccountQuotaView`도 따라가야 해서 PR에 Windows 확인 항목이 필요하다. ChatGPT만 표시되는 비대칭이 생긴다.
- **CLI 설치 경로를 앱 설정으로 지정(보류)**
  - 문제: Finder·로그인 항목으로 실행한 Mac 앱은 `~/.local/bin`, `/opt/homebrew/bin`, `/usr/local/bin` 밖의 `codex`·`claude`·`agy`를 찾지 못한다([Mac/README](Mac/README.md#설치-경로-제약-중요)). 터미널 실행에서는 드러나지 않는다.
  - 방향: 셸 프로필을 읽지 않는 원칙을 유지한 채 사용자가 CLI 폴더를 앱 설정으로 지정한다. 절대 경로·`.`/`..` 거부 규칙은 `MacCliPaths`와 같게 둔다.
  - 정할 것: 저장 위치(DB metadata 등), 설정 UI(Mac 메뉴), Windows와의 관계(Windows는 해당 문제가 없어 Mac 전용).

## 공식 상태 응답 수신·범위 판정 분리 — PR 준비, 미배포

- 2026-10-10 KST. ChatGPT 상태가 계속 STALE로 보이는 원인 중 하나였던, 정상 HTTP 응답의 범위 미확인을 수신 실패로 처리하던 흐름을 분리한다. 수신 성공은 확인 시각만 갱신하며 상태 정상/서버 데이터 최신성을 보증하지 않는다.
- OpenAI `summary.json`에 `components.json`의 누락 구성 요소를 보완한다. 같은 ID의 상충 관측은 모두 평가해 정상 값이 확인된 장애를 지우지 못하게 한다. Codex Web/API/CLI·VS Code extension을 포함한 기존 관련 범위이며 Atom/RSS 대조는 이번 범위에 없다. [출처·관련 목록·판정 기준](PROVIDER_SOURCES.md).
- 범위 미확인 사건만 있으면 FULL에서 UNKNOWN / CHECK를 유지한다. 확인된 관련 장애가 있으면 기존 HOLD/STOP 우선이며 미확인 사건은 별도 상세로 남긴다. 수신 실패는 확인 시각을 갱신하지 않고 15분 뒤 STALE가 된다.
- 카드 마지막 확인, 상세의 수신 실패/판정 실패·마지막 판정 상태와 시각·확인된 사건/미확인 사건 구분을 추가한다. 지난 장애 제목은 수신 실패/STALE 카드의 현재 장애로 남기지 않는다. Windows 카드에 확인 시각 한 줄을 추가해 창 높이는 518→569 DIP, 폭 374 DIP이며 한도 전환·박스 내용은 유지한다.
- SQLite schema 2를 유지하고 AppMetadata에 Provider별 최대 8KiB의 진단 한 건만 저장한다. 캐시 시각이 일치하는 유효한 메타만 읽고 레거시 데이터는 읽기 결과에서만 안전하게 분리한다. 계정 한도·인증 값과는 별개다.
- Windows 최종 빌드 경고 0·오류 0, self-test 251,539건(main 251,451 대비 +88, 공통 251,232 + Windows 307), 종료 코드 0. 실제 공개 상태 조회는 세 Provider 응답 수신 성공, ChatGPT는 범위 미확인 사건으로 CHECK, Claude·Gemini는 관련 범위 정상이다. 조회 성공은 세 Provider 모두 GO라는 뜻이 아니다.
- Mac 원본·버전과 기존 Windows 2.4.0 배포 EXE는 변경하지 않는다. Mac `Mac/build.sh`·native smoke와 새 카드 문구/시각·범위 미확인 상세·Codex 장애 판정 확인 전에는 병합하지 않는다. 긴 실제 운영·네트워크 전체 단절·Mac 실기는 미수행이다.

## Windows 2.4.0에 반영: 계정 사용량 막대·Provider 상세

- 2026-10-10 KST. 사용한 비율·간략 리셋 시간·Provider별 하나의 상세 문구를 공통 모델로 정리하고 Windows 네이티브 막대·흰색 고정 폭 팝업에 연결했다. [범위·Windows 검증·화면·Mac 반영 요청](QUOTA_USAGE_UI.md).
- Mac smoke 기대값 커밋 `5f0c4e5`를 받은 뒤 간략 리셋의 앞뒤 0 단위를 모두 생략하도록 보완했다. 올림·이월 뒤 0 단위 검사 4건을 추가했다. Windows 최신 재검증: 경고 0·오류 0, self-test 251,451건(공통 251,144 + Windows 307, 직전 #45보다 +4), smoke 387 PASS·종료 코드 0. 앞의 0 단위·사용 기준 안내 보완과 초기 smoke의 오래된 개별 상세 검사 실패는 기록에 남겼다.
- **#45 양쪽 확인·main 병합 완료, Windows 2.4.0에 반영·배포 완료.** Mac 기대값 커밋 `e85a439`에서 공통 251,144건·빌드 경고 0·오류 0·native smoke 종료 코드 0을 확인했고 사용자 승인으로 #45를 병합했다. #46의 Mac 0.4.0 UI 후속도 main에 병합됐다. 이번 Windows 배포에서 Mac 코드·버전은 변경하지 않았다. 조회 주기·주의/위험 판정·DB는 그대로다.
- 2.4.0 준비 검증: SDK 10.0.401, 빌드 경고 0·오류 0, 기존 dist 2.3.0과 후보 self-test **251,377 → 251,451건(+74)**, 공통 **251,070 → 251,144**, Windows **307 그대로**, 실제 종료 코드 모두 0. smoke **5회 모두 387 PASS·종료 코드 0, 실패 0/5회**. 후보의 실제 계정 막대·간략 리셋·Provider 상세 정상은 사용자 확인이며 계정 값은 게시하지 않는다.
- **배포 완료:** 사용자 승인 뒤 #47의 Draft를 해제·병합하고 main `296f524`로 fast-forward했다. 정상 종료·WAL/SHM 부재 확인 → `../backups/release-2.4.0-20261010/`에 기존 2.3.0 EXE·dist·소스·DB 백업 → 병합 main publish·최종 단일 EXE 검사 → 새 2.4.0 일반 실행 순서다. 최종 build/publish 경고 0·오류 0, self-test **251,451건·ExitCode 0**, 별도 smoke **1회 387 PASS·ExitCode 0**이다. 자동 시작 종류·값은 전후 같고 같은 경로가 새 EXE를 가리킨다. 최종 **트레이·상태 창·세 Provider 사용량 막대·Provider 상세 정상은 사용자 확인**이다. [배포 기준 커밋·EXE 해시·백업·복원 기록](MAINTENANCE_2_4_0.md).
- 새 버전의 실제 재부팅·재로그인·절전 복귀·전체 네트워크 단절·장시간 조회·실제 다중 모니터/DPI 전환·2.3.0 복원 시험은 미수행이다. 초기 smoke의 원인 미확정 관찰과 진단, 위 #46 보류 후보는 그대로 유지한다.

## Windows 2.3.0에 반영

- 기록일: 2026-10-09 KST. #30·#42가 병합된 main `4c344e2`에서 Windows 2.3.0을 준비하고, 사용자 승인 뒤 #44를 병합해 main `019d3f3`에서 배포했다. [변경·검증·최종 EXE·백업·교체·복원 기록](MAINTENANCE_2_3_0.md).
- **2.3.0에 반영·배포 완료:** #30의 Antigravity Gemini 모델 한도, 세 번째 Provider 박스·독립 조회·캐시, smoke 클릭 전제 보완·진단 유지. #41·#42는 Mac 전용 후속으로 Windows 신규 변경에 세지 않는다. #44와 배포 후 기록은 버전·문서만 변경한다.
- SDK 10.0.401, `build.ps1` 경고 0·오류 0. 실제 기존 dist 2.2.4와 후보의 self-test는 **251,182 → 251,377건(+195)**, 공통 **250,875 → 251,070**, Windows 전용 **307 그대로**, 실제 ExitCode 모두 0이다. 감소한 검사 그룹은 없다.
- 준비 후보 smoke **5회 모두 293 PASS·ExitCode 0, 실패 0/5회**. 병합 main의 최종 단일 EXE도 build/publish 경고 0·오류 0, self-test **251,377건·ExitCode 0**, smoke **293 PASS·ExitCode 0**이다. Gemini 박스·메모 잘림 통합과 합성 10초 대기 중 응답성을 확인했다. 아래 원인 미확정 초기 관찰과 진단은 계속 남긴다.
- 이 PC의 agy **1.3.2**로 후보 Gemini 클라이언트만 실제 조회해 **8.37초·5시간/주간 두 창·ExitCode 0**을 확인했다. 사용자 DB와 다른 Provider는 probe에서 조회하지 않았으며 계정 수치는 게시하지 않는다. 설정 격리 제한과 신선도·절대 비소비 비보장도 유지한다.
- **배포 완료:** 정상 종료·WAL/SHM 부재 확인 → `../backups/release-2.3.0-20261009/`에 기존 2.2.4 EXE·dist·소스·DB 백업 → 병합 main publish·최종 검사 → 새 2.3.0 일반 실행. 자동 시작 종류·값은 전후 동일하며 같은 등록 경로가 새 EXE를 가리킨다. **트레이·상태 창·Gemini 값 모두 정상은 사용자 확인**이다. 새 버전의 실제 재부팅·재로그인·절전·전체 네트워크 단절·장시간 자동 조회·복원 시험은 미수행이다.

## Gemini / Antigravity 한도 표시 (PR #30, 2.3.0에 반영)

- 기록일: 2026-10-09 KST. Windows 재검증과 Mac 확인 뒤 사용자 승인으로 [PR #30](https://github.com/hydron75/ai-burger-clock/pull/30)의 Draft·병합 보류를 해제하고 main에 병합했다(검증 HEAD `6a12acb98dcaf5769dbfb5977288afa49ae424f8`, 병합 `4695a55dc00dd90e95a93aa9ba6f80200fb23a0c`). **Windows 2.3.0 배포본에 반영했다.** 초기 구현·검증과 main 통합 뒤 결과는 [구현·검증 기록](GEMINI_ANTIGRAVITY_QUOTAS.md), 준비·최종 배포 검증은 [2.3.0 기록](MAINTENANCE_2_3_0.md)에서 구분한다.
- 공식 `agy` CLI **1.3.1 이상, 2.0 미만**의 `-p /usage --output-format json`에서 `Gemini Models` 그룹의 5시간·주간 잔여율과 리셋을 읽는다. 1.3.1 미만·2.x·해석할 수 없는 버전은 거부하며 Gemini Apps 전체 한도·Claude/GPT 그룹·크레딧은 제외한다.
- 사용자 hooks·MCP 설정 격리 제한을 사용자가 수용한 조건으로 자동 조회에 연결했다. 버전 사전 확인, 자동 업데이트 차단, 입력 닫기, 제한시간, 0턴·0토큰 검증을 적용한다. 완전한 설정 격리·모든 실행의 절대 비소비·서버 데이터 신선도 보장은 아니다.
- Windows는 한도 보기 전환·기존 창 크기를 유지하며 Provider별 박스와 한도 영역 내부 스크롤을 사용한다. 제목 바깥 기준선·클릭되지 않는 박스의 hover 없음은 UI 계획 결정 9/10을 따른다.
- SDK 10.0.401, main·통합 후 모두 경고 0·오류 0. Windows self-test **251,182 → 251,377건(+195)**, 공통 **250,875 → 251,070**, Windows 전용 **307 그대로**, self-test 종료 코드 0. 진단만 추가한 #30 20회, 검사 전제 보완 후 #30 20회, 변경하지 않은 main 20회 모두 smoke 실패 0회·종료 코드 0이다. 보완 후 smoke는 **main 255 / #30 293 PASS**이며 합성 10초 Gemini 대기 중 화면·트레이 메뉴·매초 카운트다운이 계속 응답했다. [Windows 재진단 기록](https://github.com/hydron75/ai-burger-clock/pull/30#issuecomment-6072308296).
- [Mac 확인](https://github.com/hydron75/ai-burger-clock/pull/30#issuecomment-6071703373): `2c7478a`에서 공통 251,070건, `Mac/build.sh` 경고 0·오류 0, native smoke 종료 코드 0, agy 1.3.2 실제 조회를 확인했다. 이후 `18528ac`·`6a12acb`는 Windows smoke만 변경해 공통 원본·Mac 실행 코드는 그대로이며 사용자가 Mac 확인 완료를 근거로 병합을 승인했다. Windows의 실제 네트워크 전체 단절·재연결, 재부팅·절전 실기와 이번 재진단의 실제 agy 계정 조회는 미수행이다.

## 관찰: Windows smoke의 뜻밖의 한도 모드 (원인 미확정)

- 기록일: 2026-10-09 KST. 원인은 미확정으로 남긴다. 사용자 승인으로 #30 병합 보류는 해제했지만, 보완 후 실패 0회가 초기 오류의 원인 확정이나 해결 보장은 아니다.
- 증상·발생 횟수: 초기 smoke에서 예정된 한도 전환 전에 상태 하단의 `최근 조회 시도:` Label 검사(`UIRegressionChecks.RunAsync`)가 **2회 실패**했다. 오류는 두 번 모두 `UI integration: UI labels attempted time explicitly`, 종료 코드 1 / PASS 74개다. 두 번째 기록은 `toggle=상태 보기`와 한도 안내 문구를 보여 **뜻밖의 한도 모드가 확인된 1회**이며, 첫 번째는 모드가 기록되지 않았다. [초기 오류 원문·별도 반복 결과](https://github.com/hydron75/ai-burger-clock/pull/30#issuecomment-6071780940).
- 두 초기 실패는 합성 10초 Gemini 조회 시작 전이다. 실제 agy·계정은 호출하지 않는 smoke이며, 실패 순간 일반 가짜 조회가 진행 중이었는지는 당시 기록이 없어 확정하지 않는다. 단순 창 숨김만으로 한도 모드 전환을 설명하지 않는다.
- 별도 10회 반복에서 나온 Refresh 대기 실패 1회와 상태 전환 실패 1회는 위 초기 오류와 구분한다. 숨긴 창의 `PerformClick()`이 실제 Click을 발생시키지 않는 검사 전제 누락을 조건 재현으로 확인하고, 정상 `ShowWindow()`·버튼 선택 가능 여부·실제 Click 증가를 검사하도록 보완했다. 기존 5초 대기 / 25ms poll과 초기 Label 검사는 유지했다. 과거 자연 발생 당시 창 숨김 여부는 미기록이며 초기 모드를 강제로 되돌리는 처리는 넣지 않았다.
- 추가한 진단은 [SmokeDiagnostics.cs](SmokeDiagnostics.cs)·[SmokeTest.cs](SmokeTest.cs)·[UIRegressionChecks.cs](UIRegressionChecks.cs)·[AccountQuotaUiChecks.cs](AccountQuotaUiChecks.cs)에 계속 둔다. 실패 검사 직전과 실패 시 창 표시·포커스·선택 모드, 버튼 Visible/Enabled/CanSelect, 실제 Click 횟수·한도 Click 스택, Provider별 공식/한도 조회와 가짜 조회의 진행 목록·경과 시간, 타이머 진입/종료를 표준 오류의 `DIAG:` JSON으로 남긴다. 제품 실행 코드와 공통 원본은 이번 진단·보완에서 변경하지 않았다.
- 보존 위치: 로컬 `artifacts/pr30-smoke-diagnostics-20261009/{diagnostic,fixed-pr30,main,probe-refresh,probe-toggle}/`의 `results.csv`, 실행별 `smoke.txt`·`smoke-errors.txt`·PNG. 진단만 추가한 #30 20회·보완 후 #30 20회·main 20회는 모두 자연 발생 실패 0회다. 두 `probe-*`는 숨김 조건을 의도적으로 넣은 별도 재현이므로 자연 발생 실패 횟수에 포함하지 않는다. 초기 실패 원문은 위 PR 코멘트에 남겼으며 이 새 진단 폴더에서 발생한 오류로 해석하지 않는다.
- 재발 시 새 실행 폴더에 stdout/stderr·PNG와 실제 ExitCode를 함께 보존한다. `--report-directory`는 PNG 저장 옵션이므로 표준 오류도 `smoke-errors.txt`로 리디렉션해야 진단이 남는다. 해당 파일의 `ui.status-footer-before-check`·`ui.check-failed`·`quota.click`(스택)·`window.visible-changed`·`window.deactivated`·`query.begin/end`·`smoke.failure`를 확인해 의도하지 않은 전환과 클릭 무동작·진행 중 조회를 구분한다. `quota.text-changed`는 핸들러 중간 상태일 수 있어 실제 Click 완료 뒤 기록과 비교한다. 모드 초기화나 검사 삭제로 실패를 숨기지 않는다.

## Windows 2.2.4에 반영

- 기록일: 2026-10-09 KST. #37·#38·#39를 모두 포함한 준비 PR #40을 승인 뒤 병합하고 main `670f45a`를 Windows 2.2.4로 배포했다. [변경·검증·최종 EXE·백업](MAINTENANCE_2_2_4.md).
- **2.2.4 배포본에 반영:** Provider별 한도 박스(#38), 메모 잘림 안내(#39), 상세 Tooltip의 빈 선택 항목 숨기기(#37). 한도↔상태 전환·창 크기는 유지한다.
- **2.2.4 배포본에 반영:** 상태·한도 본문과 기록·피드백·통계 문구의 공통화, 공통 검사 일괄 실행, StatusTicker 적용과 크기 제한 Windows 경고 로그. 이번 릴리스 준비에서는 공통 원본을 다시 수정하지 않았다.
- **배포 완료:** 정상 종료·WAL/SHM 부재 확인 → 2.2.3 EXE·dist·소스·DB 백업 → `build.ps1 -Publish` → 최종 dist self-test 251,182건(+403), smoke 255 PASS·종료 코드 0 → 새 2.2.4 실행. 자동 시작 등록은 전후 동일하며 새 트레이·상태 창 정상은 사용자 확인.
- 시간표·공휴일·계정 조회 주기·DB schema 2·자동 시작 경로는 유지한다. Gemini/agy 한도는 2.2.4 이후 PR #30으로 main에 병합했지만 **2.2.4 배포본에는 포함하지 않는다.** 한도 회복 알림은 미구현이다.
- 7b(기록·공휴일 흐름)·7c(수명 주기) 공통화는 [결정 13 / 4-4절](MACOS_UI_PLAN.md#4-4-7b7c-재판단-2026-10-09-7a-완료-뒤)에 따라 보류한다. 이번 PR은 버전·Windows 통합 검사·기록만 변경한다.

## macOS 팝오버 UI와 공통 표시 로직

- 기록일: 2026-10-08 KST.
- 상태: 공통 표시·기록·통계·StatusTicker와 Windows 전환 PR이 main에 병합됐고 Windows 2.2.4 배포본에 반영했다. 최신 Mac 구현·실기 결과는 [Mac 안내](Mac/README.md)·[계획](MACOS_UI_PLAN.md)·[Mac 기록](MACOS_PORT.md)에서 관리한다.
- Mac 메뉴바 왼쪽 클릭은 Windows 패널과 같은 구성의 팝오버를 연다. 오른쪽 클릭은 Refresh·로그인 항목 설정 열기…·종료만 있는 짧은 메뉴다. "상태 창 열기"는 제거한다.
- 흰 바탕에 글자 색만 바뀌는 표시로 바꾼다. 다크 모드에서는 바탕만 시스템 색을 따른다. Mac 버전은 0.2.0으로 올린다.
- 패널 본문 문구·표시 판정·사용 경험 기록 생성은 공통 원본으로 옮겼고 Windows 전환도 완료했다. 공통화 자체의 기존 화면 보존과 이후 #38 한도 박스의 의도한 변경을 구분한다.

## macOS 데스크탑 위젯 (향후)

- 기록일: 2026-10-08 KST.
- 상태: 향후 별도 개발. 이번 팝오버 작업에는 포함하지 않는다.
- Mac 상태 패널은 메뉴바를 클릭했을 때만 보인다. 고정 표시가 필요하면 Mac 전용 데스크탑 위젯으로 따로 만든다. Windows에는 해당하지 않는다.

## Windows 2.2.3에 반영

- 기록일: 2026-10-04 KST. [PR #18](https://github.com/hydron75/ai-burger-clock/pull/18)의 Windows 전용 변경을 main에 병합하고 2.2.3으로 배포했다. [검증·백업·Git 기준점](MAINTENANCE_2_2_3.md).
- **한도 제목 후보:** `ChatGPT` 제목과 아래의 `Work/Codex` 행을 2.2.3에 반영했다. 표준 한도 구성은 기존 창 크기에 들어가며 값·기간·카운트다운·조회 주기는 그대로다.
- **Provider 표시 이름 후보:** 화면·메뉴·기록 대화상자/피드백·통계·Tooltip·Provider 알림 제목의 OpenAI를 ChatGPT로 맞췄다. `ProviderKind.OpenAI`, DB 값, 공식 상태 URL은 그대로다.
- **연결 복구 공통 개선 후보:** main의 5초 대기·연속 변화 합침·1분 연기·유효 연결 없으면 건너뛰기를 Windows 배포본에 반영했다. 공통 원본은 이번 PR에서 다시 수정하지 않았다. 수동 Refresh·절전 복귀의 즉시 조회는 유지한다.
- **Tooltip 분 단위 후보:** 이번에는 보류했다. 기존 초 단위 카운트다운과 Windows NotifyIcon 길이 검사를 통과했으며 실제 장시간 hover 가독성 문제는 재현하지 않았다. Mac의 증상만으로 Windows를 변경하지 않는다.
- 실제 Windows 네트워크 전체 단절·재연결은 **미수행**이다. 현재 실행 세션에 어댑터를 비활성화할 관리자 권한이 없으며, 모의 시계·연결 판정 검사와 실제 단절 시험을 구분한다. 네트워크가 끊긴 동안 정기 조회까지 중단하는 기능은 이번 변경에 포함하지 않는다.

## macOS native preview 0.1.5

- 기록일: 2026-10-03 KST.
- 이후 작업 분담: Mac 구현·빌드·실제 실행 검증은 사용자 Mac의 Claude 로컬 환경에서 진행하고 GitHub 업데이트는 계속한다. Windows 검토·회귀 검증은 요청 시 이 환경에서 수행한다. 상대 OS 전용 파일은 각 담당에게 맡기고 공통 원본은 분리 복사하지 않으며 양쪽 모두 PR로 수정한다. 작업 후 버전·README·CODE_GUIDE·BACKLOG·검증 기록을 맞춘다. 현재 Mac 작업은 기존 `feature/macos-native` / Draft PR #12를 이어간다. 역할 분담 지침은 [PR #13](https://github.com/hydron75/ai-burger-clock/pull/13)으로 main에 병합됐다. [분담 규칙](AGENTS.md#작업-분담-windows와-mac).
- 상태: Apple Silicon / macOS 27용 AppKit 앱(preview 0.1.5)을 [PR #12](https://github.com/hydron75/ai-burger-clock/pull/12)로 2026-10-04 main에 병합했다(merge commit `46a80f89066b5417faa3c9a6e4d9f6905f6c5d9b`). 실제 알림 배너·장기 사용·재부팅 자동 실행은 미확인이며, 문제가 생기면 별도로 고친다. 작업 브랜치는 병합 뒤 정리했다. 아래 항목은 그 과정의 기록이다. [최종 기록](MACOS_PORT.md#35-2-main-대비-windows-최종-검증과-병합), [Mac 빌드 안내](Mac/README.md).
- 0.1.2 실제 Mac 빌드: 소스 `dd3f596bf58f40016b2095867de428bf0b9b80a9`, SDK 10.0.401 / Xcode 27.0 / build 27A266a, 공통 검사 244,347건, native Release 성공(16.4초), `.app`·ad-hoc 서명·ARM64 SQLite 확인. 이어서 새 아이콘 1x/2x 픽셀·크기, 창·임시 SQLite·4종 이벤트/메모·통계·주입 카운트다운의 native smoke PASS를 받았다. shell 종료 코드는 별도 출력되지 않았다. 이후 **사용자 스크린샷에서 커진 초록색 원과 흰색 F, 사용자 직접 확인으로 양쪽 메뉴막대 표시까지 확인**했다. 이번 후속 갱신은 문서만 변경하므로 재빌드·검사 반복은 필요 없다.
- main 병합: PR #12 최종 HEAD를 main과 비교한 Windows 검증을 통과했다(자체 검사 +63, smoke 종료 코드 0, Windows 화면 PNG 10쌍 동일). 지적된 공통 검사의 실제 네트워크 의존을 고친 뒤 main에 병합한다. 최종 Mac bundle은 0.1.5 / 6. [기록](MACOS_PORT.md#35-2-main-대비-windows-최종-검증과-병합).
- main 병합 준비: 사용자 결정으로 실제 알림 배너·장기 사용·재부팅 자동 실행은 미확인 상태로 PR #12 병합 절차를 진행한다. 문제가 생기면 별도로 수정한다. PR #12 최종 HEAD의 main 대비 Windows 검증을 요청한다. [기록](MACOS_PORT.md#35-main-병합-준비).
- 0.1.5 / build 6: PR #15(연결이 없을 때 연결 복구 조회 건너뛰기)를 병합했다. 연결 판정은 macOS utun 때문에 `GetIsNetworkAvailable()` 대신 "링크 로컬이 아닌 주소가 있는 Up 인터페이스"로 한다. Mac 실제 확인에서는 끊긴 동안 Unknown이 없었고 재연결 5초 뒤 회복했다. Windows 자체 검사 +8·smoke 종료 코드 0. 소스 `53ae21fea7d9ebc7a3796ed7aa1d197a21aa53d4`. [기록](MACOS_PORT.md#34-015-정리).
- 0.1.4 / build 5: PR #14(공통 HTTP 설정·알림 문구, 연결 복구 5초 대기·1분 연기)를 Windows 검증(자체 검사 +8, smoke 종료 코드 0) 뒤 병합했다. Mac 알림 제목도 ChatGPT로 유지된다. 소스 `63d9ec769550f6140fac2af448bdceec74d12588`, 공통 검사 244,361건, smoke 종료 코드 0. 공통 개선 후보였던 "연결 복구 직후 지연 조회"는 이것으로 반영했다. [기록](MACOS_PORT.md#32-pr-14-병합과-014-정리).
- 0.1.3 / build 4: 0.1.2 이후 리뷰 반영·Tooltip·ChatGPT 표시 이름·구간 구분·한도 ⓘ 팝오버·앱 아이콘을 묶어 버전을 올렸다. 소스 `8e8d9b8edb0af8c7c88914dfb207c1581072f8cc`, bundle 0.1.3 / 4, 공통 검사 244,349건, smoke 종료 코드 0. [기록](MACOS_PORT.md#31-013-버전-정리).
- UI 확인·표시 이름·앱 아이콘: 공식 상태 링크, Statistics 재열기, 기록 저장은 정상이었다. 상태 창 Tooltip이 금방 사라지던 문제(매초 다시 지정)를 고쳤다. Mac 화면의 "OpenAI"를 제품 이름 "ChatGPT"로 바꿨다(저장 값은 그대로). 앱 아이콘(초록 시계·F)을 추가했다. **Windows 후보 → 2.2.3에 반영:** Windows 화면의 "OpenAI" 표시도 "ChatGPT"로 맞췄으며 공통 `TrayPresentation.Tooltip`의 `displayName` 인자를 사용한다. [Windows 기록](MAINTENANCE_2_2_3.md), [Mac 기록](MACOS_PORT.md#30-ui-조작-확인과-tooltip표시-이름앱-아이콘-수정).
- 알림 권한·앱 아이콘: 시스템 설정에서 알림이 허용돼 있음을 확인했다(배너, 데스크탑·알림 센터·잠금 화면). 실제 배너는 미확인이다(PR #12 병합 때 미확인 상태로 진행, 문제가 생기면 별도 수정). 확인 당시 bundle에 앱 아이콘이 없어 알림 설정·Finder·로그인 항목에 빈 아이콘으로 보였다. **0.1.3에 반영:** `Mac/Assets.xcassets/AppIcon.appiconset`(생성 스크립트 `Mac/tools/make-app-icon.swift`)으로 초록 시계·F 앱 아이콘을 추가했다([30절](MACOS_PORT.md#30-ui-조작-확인과-tooltip표시-이름앱-아이콘-수정)). [기록](MACOS_PORT.md#29-macos-알림-권한-확인).
- 공통 원본 정리·연결 복구 지연 조회: HTTP 설정과 알림 문구를 공통 함수로 옮기고, 새 공통 `NetworkRefreshScheduler`로 연결 복구 뒤 5초 대기·1분 제한(연기)·연속 변화 합침을 적용했다. 이후 PR #14·#15의 Windows 실행 검증을 거쳐 main에 합쳤고 **Windows 2.2.3에 반영**했다. 당시 Mac 공통 검사 244,358건, Windows 대상 컴파일 경고·오류 0. [Windows 배포 기록](MAINTENANCE_2_2_3.md), [당시 Mac 기록](MACOS_PORT.md#28-공통-원본-정리와-연결-복구-지연-조회-별도-pr).
- 재로그인 확인: 2026-10-04 로그아웃과 다시 로그인을 두 번 했다. 두 번 모두 `loginwindow`가 로그인 항목 경로로 앱을 자동 실행했고, 로그아웃 때는 quit 이벤트를 받아 약 0.1초 만에 정상 종료했다. 실행 직후 공식 상태·계정 한도 조회도 성공했다. 자동 실행 해제·재등록은 사용자가 별도로 확인했다. 재부팅은 확인하지 않았다. [기록](MACOS_PORT.md#27-로그아웃-정상-종료와-재로그인-자동-실행-실제-확인).
- 연결 복구·절전 복귀 확인: 2026-10-04 사용자 Mac에서 Wi-Fi·유선 LAN을 끊고 다시 연결하고, 잠자기 후 깨워 봤다. 두 경우 모두 1~2초 안에 공식 상태와 계정 한도를 다시 조회했다. 깨울 때의 조회는 네트워크 변화 없이 일어나 절전 복귀 경로로 판단했다. **공통 개선 후보 → Windows 2.2.3에 반영:** 유선 재연결 직후 계정 한도 조회 한 번의 성공 기록이 없던 문제를 계기로 공통 지연 조회를 PR #14·#15에서 추가했고 Windows 배포본에도 반영했다. 당시 DNS 준비 전 실패는 추정이며 Windows 실제 단절·재연결은 미수행이다. [Windows 기록](MAINTENANCE_2_2_3.md), [Mac 당시 기록](MACOS_PORT.md#26-연결-복구절전-복귀-실제-확인).
- PR #12 리뷰 반영: 별도 리뷰 10건을 소스 `50668fa2b6cf35410a2177e369dcab66562b9cd9`에서 Mac 파일만으로 반영했다. smoke 초기화 실패 시 멈춤, DB 실패 시 조회 미시작, 공휴일 저장 시 장애 알림 소비, 메뉴를 연 동안 타이머 정지, HTTP 설정, 한도 표시 매초 재작성, 알림 문구, 버전 기본값, 메모 1,000자, 추가 인수 처리가 대상이다. 공통 검사 244,347건, 경고·오류 0, smoke 종료 코드 0을 확인했고 타이머 음성 검사도 실패로 구분됐다. **HTTP 설정·알림 문구·연결 복구 제한을 공통 파일로 옮기는 작업은 Windows 파일도 바뀌므로 보류**했다. [기록](MACOS_PORT.md#25-pr-12-코드-리뷰-지적-사항-반영).
- 0.1.2 bundle 버전 수정: 사용자 Mac에서 직접 빌드한 결과, 이전 bundle의 `Info.plist`가 0.1.0 / 1로 남아 있었다. macOS SDK가 원본 `Info.plist`를 manifest 재생성 입력으로 보지 않기 때문이다. 소스 `66fb8689043154e8bf6b1fafe04f9c317b2f0ec1`에서 버전을 csproj의 `ApplicationDisplayVersion`/`ApplicationVersion`으로 옮기고 native smoke에 bundle 버전 비교를 넣었다. 증분 빌드로 0.1.2 / 3 반영, 공통 검사 244,347건, 경고·오류 0, 서명 통과, smoke PASS·**종료 코드 0을 직접 관측**했고 버전 불일치 음성 검사는 종료 코드 1로 실패했다. 버전은 0.1.2를 유지한다. [기록](MACOS_PORT.md#24-012-bundle-버전-미반영-수정과-첫-로컬-mac-검증).
- 0.1.2 아이콘 수정: 기존 작은 검은 F와 비활성 모니터 누락 보고를 받아, 20-point 색상 원+흰색 F/B를 20px/40px bitmap에 직접 그린다. 정사각 status item·1x/2x 해상도·명시적 이미지 수명을 사용하고 tint에 의존하지 않는다. native smoke에 8개 문자/색 조합의 1x/2x 실제 RGB·투명도·흰 글자와 버튼 이미지 크기 검사를 연결했다. **사용자 Mac에서 크기·색상·듀얼 모니터 표시 문제의 해소를 확인했다.** [수정 기록](MACOS_PORT.md#20-012-메뉴바-아이콘-크기색상-수정과-다중-모니터-재검증), [실제 화면 확인](MACOS_PORT.md#23-012-실제-컬러-아이콘과-양쪽-메뉴막대-확인).
- Schedule/DST/공휴일, 공식 상태·권고, CLI 한도·조회 주기, SQLite schema 2와 통계는 루트의 같은 원본을 빌드한다. Windows WinForms와 Mac AppKit UI·알림·자동 실행은 각각 관리한다.
- 새 0.1.2 소스에서도 Windows Release 빌드 경고·오류 0, 자체 검사 250,750건과 공통 검사 244,347건을 통과했다. Mac 호스트 6개와 공통 원본 19개의 참조 C# 컴파일/trimming 분석도 경고·오류 0이지만 `.app` 생성·네이티브 실행 검증을 대신하지 않는다.
- Mac SDK 10.0.401 / Xcode 27.0에서 기존 공통 검사 244,347건, Release `.app`·로컬 서명·ARM64 SQLite와 native smoke가 통과했다. 이후 Schedule/US DST, 세 Provider 정상 상태, ChatGPT 주간·Claude 세션/주간 전체/모델별 한도 수신과 메뉴바를 확인했다. 로그인 자동 실행 체크와 등록 성공 안내는 보였지만 실제 재로그인 실행은 확인 전이다. [최신 화면 기록](MACOS_PORT.md#15-cli-조회-성공과-011-한-화면-배치-수정).
- 0.1.1 배치 수정: 창 720→660 point, 전체 스크롤 제거, Provider 간격 91→62 point, 한도 상자 108/111→54/74 point. 일반 한도는 한 화면에 두고 추가 모델 한도만 내부 스크롤을 유지한다. 긴 설명·자동 실행 상태는 Tooltip으로 보존하고 기록·상태 페이지·Refresh·Statistics 동작은 유지한다.
- 새 native smoke에 컨트롤 경계/겹침, 일반 Codex 3줄/Claude 4줄의 실제 텍스트 높이와 주입 시각의 카운트다운 감소 검사를 연결했고 **실제 Mac 0.1.1에서 종료 코드 0으로 통과**했다. 임시 SQLite·4종 이벤트/메모·통계·창 재열기도 함께 확인했다. 이후 일반 화면에서 Schedule·세 Provider·두 한도·하단 버튼/옵션이 스크롤 없이 함께 보이는 것까지 확인했다. [새 빌드 결과](MACOS_PORT.md#17-011-실제-mac-release-재빌드-성공), [새 native 검사](MACOS_PORT.md#18-011-새-native-smoke-통과), [일반 화면](MACOS_PORT.md#19-011-일반-상태-창과-메뉴바-확인).
- 빌드 중단 보완: NU1900은 빌드 전용 HTTP 캐시로, IL2026 두 건은 `QuotaJsonContext` source generation으로 해결됐다. 기존 캐시 호환성 검사 9건과 reflection 비활성화 검사 10건도 통과했다. [실제 Mac 빌드 성공 기록](MACOS_PORT.md#12-실제-mac-release-bundle-빌드-성공).
- 조용한 빌드 중단 보완: Xcode·SDK·workload 진행 단계와 원래 오류를 표시하도록 `Mac/build.sh`를 수정했다. 격리된 가짜 도구 7개 검사와 shell 구문 검사를 통과했고, 소스 `9f805f8`에서 실행별 전체 Xcode 27 경로 지정으로 실제 Mac 공통 검사 244,347건과 native Release도 성공했다. Windows 2.2.2 / Mac 0.1.1 버전은 유지한다. [원인과 검증 기록](MACOS_PORT.md#16-xcode-선택-경로와-조용한-빌드-중단-보완).
- Windows 2.2.2 dist·자동 시작·사용자 DB는 교체하지 않았다. Mac/Windows DB 동기화, Gemini 개인 한도, 한도 회복 알림, 정식 배포 서명·공증은 이번 preview 범위에 포함하지 않는다.

## 2.2.3에 반영: ChatGPT 한도 영역 표시 정리

- 기록일: 2026-10-03 KST.
- 상태: **Windows 2.2.3에 반영**했다. [검증 기록](MAINTENANCE_2_2_3.md). Mac preview에는 먼저 `ChatGPT` 제목과 그 아래 `Work/Codex`를 적용했고 실제 0.1.0 화면에서 확인했다.
- 한도 영역의 상단 제목 `Work / Codex`를 `ChatGPT`로 바꾼다.
- 제목 아래 한도가 표시되는 부분에 `Work/Codex`를 넣어 실제 조회 범위를 구분한다.
- 조회 대상·한도 값·리셋 카운트다운·조회 주기는 유지하고 표시 문구와 배치만 변경한다.

## 잔여 0%의 15분 재조회

- 상태: 로컬 2.2.2 배포에 반영하고 2026-10-03 KST에 [PR #11](https://github.com/hydron75/ai-burger-clock/pull/11)을 main으로 병합했다. [검증 기록](MAINTENANCE_2_2_2.md)에 결과를 남겼다.
- 마지막 성공 응답의 유효한 한도 창 중 하나라도 실제 잔여량이 0%이면 해당 Provider를 15분마다 조회한다. 화면 반올림으로 보이는 0%와는 구분한다.
- 리셋 전후 15분 구간의 5분 조회가 가장 우선이다. 그 밖에는 0%의 15분 → 0% 초과~10% 미만의 1시간 → 기본 6시간 순서다.
- 0%에서 조회가 계속 실패해도 실패 재시도는 15분을 넘지 않는다. 새 성공값에서 회복되면 확인된 잔여량에 맞게 1시간 또는 6시간으로 돌아간다.
- 기존 Refresh·시작·절전 복귀·연결 복구, Provider별 독립 판정과 SQLite schema 2를 유지한다. 크레딧 처리나 한도 리셋 알림은 추가하지 않았다.

## 계정 한도 조회·새로고침 안정성

- 상태: 2.2.1에 반영. [PR #8](https://github.com/hydron75/ai-burger-clock/pull/8) 및 [검증·배포 기록](MAINTENANCE_2_2_1.md).
- 조회 실패는 15분부터 지수적으로 재시도하고 평소 주기를 넘지 않는다. 리셋 구간의 빠른 조회를 우선하며 성공 시 실패 횟수를 초기화한다.
- 한도 조회 중에도 공식 상태 Refresh를 사용할 수 있다. 트레이 메뉴는 ‘상태·한도 새로 고침’으로 표시한다.
- 연결 복구 이벤트는 최대 1분에 한 번 재조회를 요청한다. 수동 Refresh·절전 복귀는 이 제한을 받지 않는다.
- Claude의 특정 PC 전용 설치 경로를 제거했다. PATH의 native/npm 배치 경로와 사용자 `.local/bin`을 탐색한다.
- 기존 6시간·잔여 10% 미만 1시간·리셋 전후 15분 동안 5분 정책, SQLite schema 2와 기존 기능은 유지한다.

## 예외 처리·저장·화면·자원 정리

- 상태: 2.1.2에 반영. [PR #3](https://github.com/hydron75/ai-burger-clock/pull/3) 및 [검증·배포 기록](MAINTENANCE_2_1_2.md).
- 초기화 실패·메모창 중 종료 처리, Provider별 저장 오류 분리, 해석할 수 없는 일부 DB 행의 통계 제외 건수 표시.
- 메모 이벤트 초기 선택, 통계·공식 파서의 앱 시계 사용, 자동 시작 오류창의 표시 유지와 복원 실패 원인 보존.
- 중복 UI 갱신 합치기, Tooltip 변경 시만 설정, 폰트·메뉴·HttpClient 소유권 정리와 반복 Dispose 처리.
- 시간·상태 표시, User-Agent, DB INSERT 열/값 및 통계 정책 목록의 중복 제거. DB schema·의존성·시간표 정책은 유지.
- 동시 Provider 알림 합치기, 로그오프 시 추가 비동기 대기, 요청 제한시간 구조 변경은 이번 범위에서 보류. 실제 효과·교착 위험과 기존 역할 구분을 먼저 검증해야 함.
- 계정 사용량 기능은 아래 보류·후보 상태를 유지하며 이번 릴리스에 포함하지 않음.

## 저장 오류·절전 복귀 안정성

- 상태: 2.1.1에 반영. [PR #2](https://github.com/hydron75/ai-burger-clock/pull/2) 병합 및 [검증·배포 기록](RELIABILITY_FIX.md).
- 절전 복귀 시 공식 상태 재조회 요청. 진행 중인 조회가 있으면 완료 직후 추가 1회 실행.
- 화면 갱신 구독자의 예외가 정기 조회를 중단하지 않도록 격리.
- 저장 오류 안내의 반복 덮어쓰기 방지와 해소 시 표시 정리.
- DB 업그레이드 백업 검증 실패도 같은 실행 중 반복 시도를 중단.
- 실제 PC 절전·복귀 및 재로그인 검증은 자동 검사와 별개로 남음. 계정 사용량 기능은 이번 버전에 포함하지 않음.

## 2.2.0: Work / Codex·Claude 계정 한도 및 리셋 카운트다운

- 기록일: 2026-09-26
- 상태: 2026-09-30 사용자 요청으로 구현. 실제 조회 결과·제한은 [ACCOUNT_QUOTAS.md](ACCOUNT_QUOTAS.md). Gemini 계정 한도는 제외한다.
- 대상: ChatGPT 계정으로 로그인한 Codex의 공식 `account/rateLimits/read`에서 실제 반환되는 한도. 앞선 실계정 조회에서는 `codex` 한도가 확인됐다. 일반 ChatGPT의 모든 모델/기능 한도를 조회하는 것으로 표시하지 않는다.
- 남은 사용률, 제공되는 한도 기간, 다음 리셋 시각과 카운트다운, 마지막 성공 조회 시각을 표시한다. 5시간/주간 한도는 응답에 실제 존재할 때만 표시하고, 누락된 값을 0% 또는 무제한으로 해석하지 않는다.
- 한도 자동 조회 정책(2026-09-27 결정, 2.2.0 구현, 2026-10-03의 2.2.2에서 0% 보완): 기본 6시간마다, 남은 한도가 0% 초과~10% 미만이면 1시간마다, 정확히 0%이면 15분마다, 표시된 리셋 시각 15분 전부터 15분 후까지는 5분마다 조회한다. 조건이 겹치면 가장 짧은 주기(5분 → 15분 → 1시간 → 6시간)를 적용한다. 기존 매시간 정각/30분 정책을 대체한다.
- 마지막 성공 응답에서 확인한 유효한 한도 창(예: 5시간/주간) 중 하나라도 해당 조건이면 Provider에 가장 짧은 주기를 적용한다. Provider별로 독립 판정하고, 누락된 값은 0%로 취급하지 않는다.
- 기본 조회 간격이 남아 있어도 알려진 리셋 시각 15분 전에 5분 조회 구간으로 진입하도록 다음 조회 시각을 계산한다. 리셋 후 15분 구간이 끝나면 확인된 잔여량에 따라 15분·1시간·6시간으로 돌아가며, 오래된 리셋 시각 때문에 5분 조회를 무기한 유지하지 않는다. 리셋 시각이 지났다는 사실만으로 회복을 확정하지 않는다.
- 앱 시작·절전 복귀 시 즉시 한 번 재조회하고, 누락된 주기만큼 요청을 몰아서 보내지 않는다. 2.2.3의 연결 복구는 공통 스케줄러의 5초 대기·1분 연기·오프라인 건너뛰기를 따른다. 카운트다운은 로컬 계산으로 표시한다.
- 기존 Refresh를 재사용하며 사용량 전용 새 버튼은 우선 추가하지 않는다. 기존 Provider 공식 상태의 5분 갱신과는 분리한다.
- 자연 리셋, 사용자가 다른 공식 화면에서 사용한 리셋권, 시스템 측 초기화는 다음 조회 결과에 반영한다. 리셋 시각 경과만으로 100% 회복을 가정하거나 사용률 감소만으로 리셋 원인을 확정하지 않는다.
- 리셋권 개수는 공식 응답이 제공하는 범위에서만 다룬다. 이 앱에서 리셋권을 사용하는 기능은 범위 밖이다.
- 인증은 공식 Codex의 로그인 관리를 이용한다. API 키 요구, 쿠키·토큰 직접 추출, AI 작업 실행을 통한 사용량 소비는 하지 않는다.
- Schedule, 공식 서비스 상태, 개인 계정 한도, 사용자 실측 데이터를 서로 혼동하지 않는다. 기존 가벼운 WinForms 앱의 동작을 보존한다.
- 공식 조회 계약: [Codex App Server — Auth endpoints / Rate limits](https://learn.chatgpt.com/docs/app-server).
- Claude는 공식 native CLI의 headless `/usage`에서 `assistant.usage_report.rate_limits.limits[]`를 받는다. 사용자 제공 2.1.284의 정상 출력으로 구조를 확인했지만 실행 환경·서버 상태에 따라 `limits:null`이 나올 수 있다. 누락은 조회 불가로 처리하며 보안 제약을 풀거나 모델 호출로 보충하지 않는다.

## 보류: Gemini 개인 사용량 / Claude 지원 안정화 추적

- 기록일: 2026-09-26
- 공개 조사 기록 갱신일: 2026-10-04 KST. 당시 앱 버전은 2.2.3이며 공개 조사 일정은 변경하지 않았다. 현재 Windows 배포는 위 2.3.0 기록과 구분한다.
- 상태: **Gemini 개인 Apps 한도** 구현은 보류. 별도 제품인 Antigravity Gemini 모델 한도는 Mac 확인 뒤 PR #30으로 main에 병합했고 **Windows 2.3.0에 배포했다.** Claude 공식 CLI 경로는 위 기능으로 추가하되 null 응답·신선도·공식 지원 안정화는 추적 대상이다. 브라우저 확장/화면 추출 방식은 구현하지 않는다.
- Gemini 개인 Apps 한도의 공식 독립 조회 경로는 조사한 공개 자료에서 확인하지 못했다. 그런 경로가 절대 없다는 뜻은 아니다.
- Claude Code statusline의 수동적 데이터와 interactive `/usage`, Gemini CLI·Code Assist·Antigravity·API 과금 한도를 개인 Gemini Apps 한도와 혼동하지 않는다.
- 공식 API·CLI JSON 명령·로컬 앱 인터페이스 등 새로운 조회 방식 또는 중요한 지원 범위 변화가 생기는지 이 대화에 연결된 모니터링으로 정기 확인한다.
- 모니터링 이름: Claude·Gemini 공식 사용량 조회 방식 확인. 자동화 ID: `claude-gemini`.
- 현재 확인 일정: **매일 08:00 KST**, ACTIVE(2026-10-03 저장된 자동화 설정 확인). 이전 문서의 매주 월요일 10:00 표기는 갱신했다. 이번 문서 수정으로 자동화 일정·상태·알림 선호를 바꾸지는 않았다.
- 적용 가능한 의미 있는 변화가 있거나 조사를 계속하려면 사용자 조치가 필요한 경우에만 알린다. 단순 버전 증가나 이미 알려진 방식은 반복 알리지 않는다.
- 이 일정은 공개 자료 조사용이다. 앱의 계정 한도 조회는 6시간 / 잔여 0% 초과~10% 미만 1시간 / 잔여 0% 15분 / 리셋 전후 15분 동안 5분으로 구분한다.
- 최근 공개 조사: **2026-10-02 KST**, [Claude SDK 0.3.287 배포 타입](https://unpkg.com/@anthropic-ai/claude-agent-sdk@0.3.287/sdk.d.ts)의 실험 usage 메서드·get_usage 요청/응답·SDKUsageReport·assistant.usage_report 선언과 설명 주석은 직전 0.3.286과 동일했다. 초기 공개 비교 기준 0.3.283 이후 의미 있는 한도 계약 변경을 확인하지 못했다. [공식 변경 기록](https://github.com/anthropics/claude-agent-sdk-typescript/blob/main/CHANGELOG.md)도 함께 추적한다.
- 공개 SDK 버전과 실제 계정 성공 검증은 구분한다. 앱의 Claude 성공 기준은 **2026-09-30 KST / native CLI 2.1.284**이며, SDK 0.3.287의 계정 조회 성공을 새로 검증한 것은 아니다. 자세한 조건과 제한은 [ACCOUNT_QUOTAS.md](ACCOUNT_QUOTAS.md)를 참고한다.
- Gemini는 같은 공개 조사에서 새 개인 Apps 독립 조회 경로를 찾지 못했다. [공식 도움말](https://support.google.com/gemini/answer/16275805?hl=en)은 기존 웹 Usage Limits 확인을 안내한다. 이를 새 해결책이나 다른 Google 제품과의 한도 공유 근거로 해석하지 않는다.
- 모니터링은 공개 공식 자료 조사만 수행한다. 계정 조회 실험, 토큰·쿠키 접근, 로그인·설정 변경, 설치, 리셋권 사용, 코드 구현/배포를 자동으로 진행하지 않는다. 발견 후 실제 계정 재검증·적용 여부는 사용자의 후속 요청을 기다린다.

## 트레이 아이콘에 Provider 상태 반영

- 기록일: 2026-09-22
- 상태: 2.1.0에 반영 — 2026-09-26 사용자 요청으로 구현. 검증/배포 기록은 HOLIDAYS_TRAY.md 참조.
- 목적: FULL THROTTLE 중 Provider가 HOLD/STOP인데도 아이콘이 초록색으로 남는 혼동을 줄이기.

### 보관한 개선안

- 문자 F/B는 기존 Schedule을 나타내고, 색상은 Provider 상태까지 반영한 주의도를 나타낸다.
- 아래 우선순위로 색상을 결정한다.
  1. 하나라도 공식 일부/주요 장애: 빨강.
  2. 위 장애 없이 하나라도 공식 성능 저하: 주황.
  3. 확인된 서비스 문제 없이 UNKNOWN/STALE가 존재: 회색.
  4. 모든 Provider 정상: FULL은 초록, BURGER는 주황.
- 예: FULL + OpenAI HOLD + Claude/Gemini GO → 주황 F.
- Tooltip에 Schedule·카운트다운과 OpenAI/Claude/Gemini 각각의 권고를 표시한다.
- 트레이 경고는 확인할 Provider가 있다는 뜻이며, 다른 Provider까지 STOP으로 변경하지 않는다.
- 공식 정상이 BURGER를 FULL/GO로 승격시키지 않는 기존 원칙을 유지한다.
- Schedule 또는 Provider 상태가 달라져 아이콘 표현이 바뀔 때만 아이콘을 교체한다. 불필요한 추가 polling이나 매초 아이콘 재생성은 하지 않는다.

### 반영 시 확인할 것

- Schedule과 Provider 상태 조합 및 색상 우선순위.
- UNKNOWN/STALE와 확인된 장애가 함께 있을 때의 표시.
- 실제 16/32px 트레이 가독성, Tooltip 길이 제한, 아이콘 핸들 정리.
- 기존 클릭/우클릭·카운트다운·알림·Provider별 독립 권고 회귀.

위 항목은 구현 완료되었습니다. 3단계 데이터 내보내기·히트맵·실측 기반 추천/자동 보정은 이번 범위에 포함하지 않았습니다.
