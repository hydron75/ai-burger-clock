# 조회 당시 시간·사용량 겹침 표시

2026-10-15 KST. 범위: **공통 코드 변경 + Windows 막대**. #52는 Windows/Mac 확인을 마친 뒤 main `9716a1c7c0559f6128c5f384756179f0367fed01`에 병합됐다. Mac 막대와 VoiceOver 연결은 Mac 담당의 별도 후속 PR이다. #52 자체는 버전과 배포 EXE를 바꾸지 않았으며 Windows 배포 준비는 [2.5.0 기록](MAINTENANCE_2_5_0.md)에서 구분한다.

이 문서의 예시·PNG·검사 값은 모두 **합성 데이터**다. 실제 계정의 요금제·사용률·리셋 시각·식별자, 사용자 DB·원본 CLI 출력은 공개하지 않는다.

## #51 통합과 행별 접근성 문구 추가

- 최신 main `8dee13abc4e5ed225ec723855cdf15738f75ed55`(#51)을 merge 커밋 `005bd8c7d78c511a0d0138773bf076e8d6ddae3f`로 반영했다. BACKLOG 충돌은 #51의 성능 최적화 후보 전체와 #52의 근무 시간표 후보를 모두 보존해 정리했다. #51은 BACKLOG만 바뀌므로 기존 main C# 검증 기준과 같다.
- 공통 `QuotaLine.ComparisonText`는 선택적 `string?` 필드다. 합성 예는 `시간 경과 40% · 균등 사용 기준보다 15%p 빠름 · 17분 전 기준`이다. 해당 행의 비교만 포함하며, 비교를 숨기는 경우와 제목·범위·메타데이터·조회 대기 행에서는 `null`이다.
- Windows 값 행과 막대의 `AccessibleDescription`은 이 필드만 직접 사용한다. `null`이면 설명을 비워 이전 비교가 남지 않게 한다. 보이는 사용률·간략 리셋은 기존 접근성 이름으로 제공하며, Provider 전체 상세를 행별로 파싱하지 않는다.
- **Provider Tooltip 문구·레이아웃·계산·조회 정책은 그대로다.** 기존 `ReferenceAge`와 새 행 문구가 같은 순수 나이 포맷터를 사용한다.
- Mac의 [최초 확인](https://github.com/hydron75/ai-burger-clock/pull/52#issuecomment-6186916389)은 `03b2e2f`의 공통 251,439건·Mac 빌드·native smoke 결과다. [최신 HEAD 78e6666 재확인](https://github.com/hydron75/ai-burger-clock/pull/52#issuecomment-6187337048)에서는 공통 **251,519건**·빌드 경고 0/오류 0·native smoke 종료 코드 0과 새 필드 호환성을 확인했다. 그 사이 브랜치를 추가 push하지 않았으며 사용자 승인 뒤 #52를 병합했다.

## 표시와 계산

- 회색 track → 연한 시간층 → 기존 진한 사용층 → 시간 위치 경계선 순서다. 경계선은 비교 가능한 창에서 항상 마지막에 그리므로 사용층에 가려지지 않는다. 창 높이·행 수·막대 높이·Provider별 박스와 상태/한도 전환은 그대로다.
- `T=LastSuccessfulCheckUtc`, `R=ResetsAtUtc`, `D=WindowMinutes`일 때 시간 비율은 `100 × (D − (R − T)) / D`다. 사용률과 시간 비율은 모두 T에 고정하며, 차이는 `사용률 − 시간 비율`(%p)이다.
- 합성 예: 창 5시간, T 당시 리셋까지 3시간이면 시간 비율 40%. 사용률 55%이면 `균등 사용 기준보다 15%p 빠름`이다. 사용률을 시간 비율로 나눈 상대 백분율이나 소진 예측이 아니다.
- 막대에는 반올림 전 값을 전달하고 상세 문구만 소수 1자리로 반올림한다. 표시상 차이 0은 `균등 사용 기준과 비슷함`이다.
- Provider별 상세 하나를 유지한다. `마지막 조회 기준, 방금 전` / `17분 전` / `1시간 5분 전`처럼 기준 시각의 나이만 현재에 맞춰 갱신한다. 분은 내림하며 0 단위를 생략한다. 마지막 성공·다음 조회·정확한 KST 리셋과 서버 데이터 생성 시각 비보장 설명은 보존한다.
- 리셋에서 역산한 단순 참고선이며 실제 창 시작·활성 여부를 확인했다는 뜻이 아니다. 근무 시간표는 적용하지 않는다([BACKLOG](BACKLOG.md)의 후속 후보).

### 시간 비교를 숨기는 경우

성공 시각 없음/미래, 리셋 미제공/경과, 창 길이 미확인/잘못된 값, 기간 모순, 이전 조회값, 잘못된 사용률이면 시간층·경계선·%p 비교를 숨기고 상세에 사유를 적는다. 5시간 창의 0%도 시작을 확인할 수 없으므로 비교만 숨긴다. 주간 0%는 유효한 기간 정보가 있으면 비교할 수 있다.

이전·경과 사용량은 기존대로 회색으로 남긴다. 리셋 경과만으로 사용률을 0%로 만들지 않는다. 새 성공 응답이 오면 그 응답의 리셋·사용률·T로 다시 계산한다.

### 색과 접근성

- 25% 고정 농도를 쓰지 않는다. 실제 합성색 기준 연한 면↔track 및 연한 면↔사용층 각각 **1.5:1 자체 목표**, 경계선은 각 인접 면에서 **3:1 목표**다. Windows 기본 세 Provider·합성 밝은/어두운 배경에서 모두 검사했다.
- 경계선은 pixel-aligned 1 DIP이며, 명암 하나로 부족하면 반대 명암 테두리로 보완한다. 사용량이 시간 비율보다 크거나 같아도 표시하고 끝점에서 사라지지 않게 한다.
- 고대비는 시스템색과 경계선을 우선한다. 목표 대비의 중간색을 만들 수 없는 사용자 색 조합은 연한 면을 생략하되 경계선과 문구는 유지한다. 표준 세 Provider 팔레트는 이 fallback에 해당하지 않는다.
- Windows 전체 다크 모드를 새로 구현한 것은 아니다. 합성 어두운 배경의 그리기·팔레트 검사와 실제 OS 테마 전환은 구분한다.
- 박스·제목·범위·메타데이터는 Provider 상세를 유지하고, 값 행·막대는 공통 `ComparisonText`만 접근성 설명에 연결한다. Mac은 후속에서 이 필드로 행의 VoiceOver 비교 설명을 연결하며 Tooltip을 파싱하지 않는다.

## 변경 범위

| 구분 | 파일·내용 |
|---|---|
| 공통 원본 | 신규 `QuotaPacing.cs` 계산·문구·행별 나이, `QuotaPanelModel.cs` 선택적 `QuotaLine.ElapsedPercent`·`ComparisonText`와 상세/기존 안내 문구, `Shared/SharedSources.props` 원본 27→28개 |
| 공통 검사 | 신규 `QuotaPacingTests.cs` 207건을 `SharedTestSuite`에 한 번 등록. `QuotaPresentationTests` 74→146건, `PanelModelTests`는 새 문구 기대값만 갱신 |
| Windows | `QuotaBalanceBar.cs`, 신규 `QuotaBarPalette.cs`, `AccountQuotaView.cs` 바인딩. 색이 같으면 대비 팔레트를 재사용 |
| Windows 검사 | 신규 `QuotaBarUiChecks.cs` 83건과 UI 연결/조회 불변/접근성 검사 15건 추가. smoke가 정확한 막대/안내 영역과 박스·행 정보를 `.layout.json`으로 저장 |
| 문서 | 근무 시간표 가중치를 BACKLOG 후보로만 추가하고 CODE_GUIDE 소스 지도를 갱신 |

**변경하지 않은 것:** `AccountQuotaMonitor`, `AccountQuotaPolicy`, CLI·파서·DB·네트워크·알림, 화면 열기 자동 조회, `Mac/`, `MACOS_PORT.md`, 버전, 배포 EXE·자동 시작 설정. 공통 API의 새 인자는 선택적이므로 기존 Mac 호출은 그대로 컴파일할 수 있다. Mac의 이번 공통 변경 확인과 후속 막대 구현을 구분한다.

## Windows 실제 검증

- 최초 검증 기준 main: `d8fe96bbbeebd1a51a2196dad79d8f0c29d7f5e1`. 최신 main `8dee13a`까지는 #51의 BACKLOG 문서만 추가됐다.
- 최초 #52 C# 소스는 `82d94d74b80e785ad6e03741e37449fbf5980aa4`이고, 이번 **최종 검증 C# 소스는 `90ac5f046f4a189b7e32751cb4caf674a6d36934`**다. 뒤의 문서 커밋에는 C# 변경이 없다.
- SDK **10.0.402**. 전후 `build.ps1` 경고 **0**, 오류 **0**. 최종 별도 `--self-test`도 `Start-Process -Wait -PassThru`의 **ExitCode 0**을 확인했다.

| 항목 | main | 최초 #52 | 이번 #52 | 최초 #52 대비 |
|---|---:|---:|---:|---:|
| Windows self-test 전체 | 251,547 | 251,746 | **251,826** | +80 |
| 공통 검사 | 251,240 | 251,439 | **251,519** | +80 |
| Windows 전용 self-test | 307 | 307 | **307** | 0 |
| quota usage/countdown/provider detail | 74 | 107 | **146** | +39 |
| quota snapshot time/usage comparison | 없음 | 166 | **207** | +41 |
| smoke PASS | 394 | 485 | **492** | +7 |

기존 검사 그룹 감소는 없다. main 대비 공통 +279건은 계산·문구 207건과 연결 +72건이다. 이번 행별 문구 보완으로는 공통 +80건(연결 +39, 계산·나이·숨김 +41)이 늘었다. 최종 Release EXE의 `--smoke-test --report-directory ...`는 **1회 492 PASS·ExitCode 0**이며, 추가 7건은 세 Provider의 Tooltip/접근성 분리·행별 기준 나이와 경과 시 설명 비우기다. main 대비 smoke +98건은 그리기·팔레트 83건과 UI 연결 15건이다. 합성 10초 Gemini 대기 중 UI·트레이 응답 검사도 기존대로 통과했다. `Shared.Tests` 별도 실행은 **251,519건·종료 코드 0**, 명시적으로 실행한 Windows `--self-test`도 **251,826건·ExitCode 0**이다.

### 렌더 비교

PNG **20개**를 같은 합성 데이터로 비교했다.

- 일반 상태·공휴일·장애·통계·기록·일반 메모 안내와 인공 긴 상세 **10개는 픽셀 동일**.
- 한도 화면 **7개는 막대 내부와 기존 안내 문구 영역 밖의 차이 0픽셀**. 행의 문자·위치와 박스 배치, 창 크기는 그대로다.
- Provider 상세 **3개는 고정 폭 유지**, 내용에 맞춰 아래로 확장. 비교 문구·나이·불가 사유만 추가됐다.
- 상태 창 PNG는 전후 **774×1209**, 클라이언트 **748×1138** 픽셀이다. 한도 값 행과 막대는 각각 2 Provider에서 5개, 3 Provider에서 7개이며 추가 줄이 없다. 6-DIP 막대의 96/144/192 DPI 픽셀 검사도 통과했다.
- 공개 합성 예시와 결과: [변경 전](docs/reviews/quota-time-overlay-20261015/before-two-providers.png), [변경 후](docs/reviews/quota-time-overlay-20261015/after-two-providers.png), [Gemini](docs/reviews/quota-time-overlay-20261015/after-gemini.png), [Provider 상세](docs/reviews/quota-time-overlay-20261015/provider-detail.png), [픽셀 비교 결과](docs/reviews/quota-time-overlay-20261015/render-comparison.json).

로컬 상세 증거는 `artifacts/quota-time-overlay-20261015/`의 `before/`, `after/`, `render-comparison.json`이다. 원본 진단 로그·DB·실계정 자료는 공개하지 않는다.

**이번 행별 문구 보완 전후:** 최초 #52의 `after/render/`와 이번 `artifacts/quota-row-comparison-20261015/render/`의 PNG **20개 모두 SHA-256이 동일**하다. Provider Tooltip 3개와 긴 상세도 동일하며, layout JSON **14개도 모두 동일**하다. 따라서 이번 보완은 막대 내부·문구·Tooltip·창 크기·행 수에 시각적 변경이 없다. 새 빌드·공통 검사·명시적 self-test·smoke 원본은 `artifacts/quota-row-comparison-20261015/`에만 보관한다.

## Mac 확인 완료 — #52 HEAD 78e6666, Mac 담당 결과

- [x] 공통 검사 **251,519건**, `Mac/build.sh`, native `--smoke-test` 통과. 최초 확인의 251,439건보다 **+80건**이며 Mac 담당 코멘트에 기록했다.
- [x] `QuotaLine.ElapsedPercent`와 새 선택적 `ComparisonText`가 기존 Mac 코드와 호환된다. 합성 두 행의 서로 다른 빠름/느림·기준 나이와 숨김 상태의 `null`을 확인했다.
- [x] Provider 상세의 기존 문구·길이·신선도 안내와 행/막대 값은 그대로다. UI 연결은 Mac native smoke로 확인했으며 실제 hover 관찰은 별도다.
- [x] 이번 PR에서 Mac 막대는 기존 사용량만 표시하고 팝오버 380×821pt를 유지한다. 후속 시간층 구현·VoiceOver 실기까지 완료한 것으로 적지 않는다.

### Mac 후속 PR

- `UsageBarView.Set/DrawRect`가 공통 `ElapsedPercent`를 받아 track→시간→사용→경계선 순서로 그리기. 계산·문구를 Mac에서 중복 구현하지 않기.
- 기존 5pt 높이·정렬·한도 항상 표시를 유지하고 라이트·다크의 합성색 대비, 1x/2x·고대비에서 경계선 확인.
- 막대 자체는 현재 AX 요소에서 제외돼 있다. 기존 행의 읽을 수 있는 접근성 설명에 `QuotaLine.ComparisonText`를 직접 연결하고, `null`이면 이전 설명을 비운다. Provider Tooltip 문장을 파싱하지 않으며 VoiceOver로 확인한다.
- 극소 사용량의 기존 최소 2pt 표시가 시간 위치 비교를 오해하게 하지 않는지 확인.
- 합성 사용<시간/같음/사용>시간, 0·100%, 이전·경과·미제공, 동일 창 높이·행 수를 검사.

## 미수행과 배포 보존

이 Windows 환경에서는 Mac 빌드·native 실행·VoiceOver, 실제 Windows OS 테마·고대비·화면 배율 전환·다중 모니터·스크린리더 실기는 **미수행**이다. 위 Mac 빌드/native는 Mac 담당이 별도로 실행한 결과다. #52 후보 코드의 실제 계정 조회 검증과 알림 배너의 시각적 전달 확인도 당시 수행하지 않았다. 기존 배포본의 평상시 조회와 후속 2.5.0 준비의 실제 화면 확인은 별도다.

배포 EXE의 SHA-256은 전후 `E610CD39DB17EBDC48EC7A6E0349B2A2B2993DEBE4DE555E41F6E1C10FE104D1`로 같다. publish·배포 교체·버전 변경은 하지 않았으며, 검사 후 같은 2.4.1 배포본을 다시 실행하고 프로세스 응답을 확인했다.
