# xAI 공식 상태 통합

## 네이티브 공개 JSON 전환 (2026-11-14 Draft · 미배포)

2027-01-31 sunset이 공지된 호환 JSON 세 개를 아래 두 공개 JSON으로 교체합니다.
공식 상태 페이지의 공개 JS가 실제로 읽는 주소를 관찰한 것이며, **문서화된 지원 API나 장기 제공 약속은 확인하지 못했습니다**.
RSS는 그대로 사건 판정 축으로 사용하고, 새 JSON 수신/형식 실패 시 RSS만으로 정상 판정하지 않습니다.
기존과 같이 확인된 장애가 있으면 그 장애를 우선 표시하면서 부분 실패/범위 미확인을 별도로 남깁니다.
버전·배포 EXE·한도·조회/알림 주기·24시간 소실 추적과 정상화 알림 억제는 바꾸지 않습니다. **12월 중순 전 배포가 목표**이며 병합·배포는 별도 사용자 지시로 진행합니다.

| 고정 출처 | 읽는 값 |
|---|---|
| <https://status.x.ai/feed.xml> | 기존 제품 경로·사건 ID·진행/해결·심각도 |
| <https://data.x.ai/status/summary.json> | 대상 구성 요소의 현재 상태·필수 slug 존재 여부 |
| <https://data.x.ai/status/incidents.json> | 대상 사건·현재 단계·심각도·해결 여부 |

앱은 호환 JSON이나 uptime.json을 읽지 않고, 피드에 포함된 URL·JS 파일을 따라 읽지 않습니다.
5분 주기·15초 요청 제한·4MiB 응답 제한·쿠키/인증 없는 공개 GET은 유지합니다.
한 번의 일반 상태 확인은 총 8개 주소이며 OpenAI 사건 이력이 필요할 때만 9개가 됩니다.

### 대상·필수 slug와 범위 처리

summary의 식별자 필드 이름은 **`components[].id`**이며 값이 제품 slug입니다.
incidents의 사건 제품 필드는 **`components[].slug`**입니다. 호환 JSON의 숫자형/불투명 ID나 `components[].id`로 사건 범위를 판단하지 않습니다.
다음 19개가 필수이며 하나라도 빠지면 CHECK입니다.

| 범위 | 필수 summary slug |
|---|---|
| Grok·API | `grok-com`, `ios-app`, `android-app`, `grok-in-x`, `voice`, `api-global`, `api-us` |
| Cursor | `cursor-desktop`, `cursor-ios`, `cursor-cli`, `cloud-agents`, `automations`, `agent-sdk`, `review-agents`, `third-party-models`, `cursor-com`, `origin`, `enterprise-dashboard` |
| Grok Bot | `grok-bot` |

RSS/사건의 기존 `grok-build`·`office-workspace-plugins`·`bugbot` 대응도 계속 인식합니다. 이번 summary에는 없으므로 이 세 slug를 필수로 요구하지 않습니다.
summary의 판정 allowlist도 위 19개이며, RSS에 알려진 세 legacy slug가 summary에 새로 등장해도 진단만 남깁니다. summary 범위 확장은 별도 결정입니다.
`agent-sdk`는 JSON/RSS 모두 **Cursor · Agent SDK**로 인식합니다.
명시적 대상 밖은 `imagine`, `api-eu-west-1`, `api-console`, `api-docs`, `single-sign-on`, `admin-apis`, `x-ai`입니다.
대상 밖으로 확인된 사건은 작업 장애·미해결 ID 추적·해결 처리에 포함하지 않습니다. RSS의 신뢰하지 않는 링크는 이 제외 규칙으로 우회하지 않습니다.

| 입력 | 처리 |
|---|---|
| summary의 처음 보는 slug | 판정에서 제외하고 slug만 진단. 상태 카드·CHECK·대상 목록에는 추가하지 않음 |
| JSON 진행 사건이 처음 보는 slug만 지목 | 범위 확인 불가 CHECK |
| JSON 진행 사건이 명시적 제외 slug만 지목 | 판정 제외 |
| JSON 진행 사건에 대상과 모르는 slug가 함께 있음 | 확인된 대상 장애 우선, 범위 미확인은 별도 표시 |
| RSS의 모르는 제품 경로 진행 사건 | 기존 CHECK |
| 신뢰하지 않는 RSS 링크 | 기존 CHECK; 임의 링크 조회 없음 |

Windows의 신규 summary slug 진단은 기존 `logs/status-ticker.log`에 남깁니다(64KiB·이전 파일 1개 순환).
원문·이름·계정값 대신 slug만 기록하고 1회에 정렬/중복 제거한 최대 32개·slug당 80자와 초과 표시로 제한합니다.
상태 클라이언트의 첫 확인 목록과 이후 목록 변경 때만 진단 콜백을 호출합니다. 순서·중복은 무시하고 출력 제한 전의 전체 slug 목록을 비교합니다.
목록이 비면 `없음`을 한 번 기록하며, summary 수신/파싱 실패는 마지막 확인 목록을 지우지 않습니다. 진단 콜백이 실패해도 같은 목록으로 재호출하지 않습니다.
진단 콜백/쓰기 실패는 판정에 영향을 주지 않습니다. Mac은 같은 공통 진단 콜백을 자기 로그에 연결해야 합니다.

### 관찰한 페이지 JS 스키마 근거

아래는 2026-11-14의 **관찰**입니다. 빌드 해시 파일명은 바뀔 수 있으며 앱이 이 JS를 런타임 의존성으로 사용하지 않습니다.

| 공개 JS | 위치·필드 근거 |
|---|---|
| [0ab1vv5w2aedy.js](https://status.x.ai/_next/static/chunks/0ab1vv5w2aedy.js) | 변수 `y`의 구성 요소 enum, `m`의 summary 스키마(문자 위치 약 1457부터), `v`/`A`의 data.x.ai fetch(약 2467부터) |
| [2tveg3bwn7u08.js](https://status.x.ai/_next/static/chunks/2tveg3bwn7u08.js) | 모듈 4489의 사건 스키마(약 5053부터), 모듈 66364의 단계·impact 변환(약 8202부터) |
| [0t1wzwsnl9ci6.js](https://status.x.ai/_next/static/chunks/0t1wzwsnl9ci6.js) | 구성 요소별 라벨·색. 앱의 색을 이 값으로 바꾸지는 않음 |
| [3j_53yv1rzqcj.js](https://status.x.ai/_next/static/chunks/3j_53yv1rzqcj.js) | `no_data`를 페이지 내부의 empty/No data로 바꾸는 처리 |

- summary: `status.indicator`·`status.description`은 문자열이며 indicator enum은 없습니다. 전역 indicator로 대상 밖 장애를 작업 장애에 포함하지 않습니다. `components[]`는 비어 있지 않은 id/name과 상태 enum입니다. 응답의 추가 `incidents` 필드는 스키마 밖이므로 무시합니다.
- 구성 요소 상태: `available` → 정상, `info` → 성능 저하, `disruption` → 일부 장애, `outage` → 주요 장애, `no_data` → 확인 불가 CHECK. 대상 구성 요소의 그 외 값은 형식 이상 CHECK입니다.
- incidents: id/name/status/impact, created_at/updated_at/resolved_at, incident_updates, components 배열을 검사합니다. `resolved_at`은 null을 허용합니다. 날짜는 페이지 스키마의 UTC ISO datetime이며 앱의 JSON 날짜 파서로 검사합니다.
- JSON 단계 enum은 investigating/monitoring/resolved뿐입니다. 호환 형식의 identified/postmortem/completed를 새 JSON의 유효 단계로 추정하지 않습니다. RSS의 기존 단계 인식은 그대로입니다.
- impact는 `critical` → outage, `major`·`minor` → disruption, `none` → info입니다. 따라서 새 JSON의 **minor도 일부 장애**이며 RSS·페이지 JS의 같은 심각도에 맞춥니다.
- 사건 제품은 `{slug,name}`입니다. update는 id/status/body/created_at을 검사하되 과거 본문으로 현재 상태를 판정하지 않습니다. `affected_components`는 관측 JSON에는 있지만 페이지 스키마/참조 코드에는 없으므로 무시합니다. 현재 사건의 `components[].slug`를 사용합니다.
- 진행 중 사건의 빈 제품 배열은 범위를 확인할 수 없으므로 앱은 기존 기준대로 CHECK입니다. 페이지가 빈 배열 사건을 목록에서 제외하는 동작과 구분합니다. 알 수 없는 추가 필드는 무시합니다.

위 스키마의 필수 필드 누락·잘못된 자료형/enum·잘못된 날짜·추가 필드·범위 조합을 합성 검사로 고정합니다.
실제 비정상 응답 모양이나 진행 사건의 resolved_at=null은 아직 관찰하지 않았으며 합성 검사와 구분합니다.

### 접근 확인과 검증

- 앱 User-Agent `AIBurgerClock/2.9.2`로 두 data.x.ai JSON 모두 HTTP 200, Sunset 헤더 없음. summary 구성 요소 26개/전부 available, 이력 사건 98건/전부 resolved였습니다. 이것을 영구적인 개수·완전성·가용성 보장으로 해석하지 않습니다.
- `https://data.x.ai/robots.txt`는 HTTP 404입니다. 명시적 허용/금지 지침을 확인한 것은 아닙니다.
- Windows에서 status.x.ai HTML은 앱 User-Agent로 HTTP 403이었으나 위 정확한 JS URL 네 개는 같은 User-Agent로 HTTP 200이었습니다. 로그인·차단 우회는 하지 않았습니다. 앱은 HTML/JS가 아니라 고정 RSS/JSON만 읽습니다.
- 합성 RED로 기존 파서가 새 정상 catalog를 CHECK로 판정함을 먼저 확인했습니다. 기존 호환 형식의 네 출처/결합 summary 사건 검사는 세 출처와 전용 incidents 입력으로 옮겼습니다. 폐기된 postmortem/completed 성공 조건은 새 JSON enum 거부 검사로 대체합니다.
- 진단 중복 억제 추가 전 전체 검증 HEAD는 `f6a6670cebdf0c010ade518b8dd6da3a1d14b41d`입니다. SDK **10.0.404**, `build.ps1` 경고 **0**·오류 **0**, self-test **293,214 PASS**, 별도 Shared.Tests **292,085 PASS**(기존 symlink fixture 1건 SKIP), xAI 합성 검사 **594 PASS**입니다. 공통 검사 입력은 Shared.Tests를 실행한 `a2a9130`과 동일하며 마지막 추가는 Windows smoke 진단의 data.x.ai → Xai 분류뿐입니다.
- #151 병합 전 진단 중복 억제 보완: 동일 목록 반복이 기존 코드에서 실패하는 RED를 확인한 뒤, xAI **603건**·공통 **292,094건**이 통과했습니다. 순서/중복 무관 비교·목록 변경·빈 목록·재생성·출력 제한 밖 변경·수신 실패·진단 예외를 합성 검사로 고정했습니다. 로그는 `artifacts/xai-diagnostic-changes-20261114/`에 보존하며, 병합 후 Windows 전체 검증과 사이트 C# 비교는 아직 하지 않았습니다.
- 고정 after 슬롯 smoke는 **첫 1회 1,174 PASS / PNG 60개 / 종료 코드 0**이며 실제 휠 검사도 통과했습니다. 재실행하지 않았고 반복 트레이 누적 억제는 1회여서 미평가입니다. OS 알림 제출·등록 명령은 0회입니다.
- **2026-11-14 13:02:45 KST**, 실제 `--check-providers`는 종료 코드 **0**, xAI **Operational / 확인 불가 사유 없음**이었습니다. 이 종료 코드는 다른 서비스까지 모두 정상이라는 뜻이 아니라 공식 응답 수신 결과이며 계정 한도는 조회하지 않았습니다.
- 독립 정적 리뷰에서 RSS-only legacy slug가 summary에 새로 들어와도 상태에 반영되는 경계를 찾아 보완했습니다. summary 19개 allowlist·legacy 3개 진단 전용 회귀 검사와 C#/TypeScript 보완 후 재검토에서 남은 확정 P1/P2는 없었습니다. 정적 검토와 실행 검증은 구분합니다.
- RED·첫 전체 검사 실패·최종 결과는 `../diagnostics/xai-native-json-20261114/`(저장소 밖 임시 작업 폴더)와 `artifacts/xai-native-json-20261114/`에 구분해 보존합니다. 초기 NuGet 감사 조회 NU1900은 감사 설정을 유지한 정상 restore로, 임시 DB 열기 오류는 기존 고정 테스트 임시 경로로 실행해 재확인했습니다. 이전 출처 기대 문구 이관 누락도 보완했고 합의한 범위 표·스키마 검사는 유지했습니다.

### 모바일 준비·Mac 후속 확인

- 같은 로컬 사이트 준비본을 고정 RSS + 새 JSON 두 개로 옮겼습니다. 위 HEAD의 실제 공통 C#에서 생성한 합성 상태 **314사례·333단계**가 일치하고, 사이트 **86/86**, TypeScript typecheck·Worker build·ESM 검증이 통과했습니다. 기존 시간표 19,972·권고 12·신선도/실패 47건 회귀도 통과했습니다.
- 사이트는 로컬 준비/비교뿐이며 소스 동기화·게시·배포·실제 한도 데이터/토큰 접근은 하지 않았습니다. 현재 운영 사이트와 기준 커밋 표시는 유지하고 승인된 배포 때 최종 main으로 맞춥니다. 수신 계약·한도 박스·DB는 바꾸지 않습니다.
- Mac은 삭제된 `XaiStatusFeed.ComponentsUrl`을 참조하는 `MacDetailTipSmoke.cs`를 새 SummaryUrl/세 출처로 바꿔야 합니다. Mac 파일을 이 PR에서 고치지 않았으므로 이 전환 전에는 Mac 전체 빌드를 통과했다고 보지 않습니다.
- `MacApplication`의 ProviderStatusClient 생성 시 새 선택적 진단 콜백을 Mac 로그로 연결해야 합니다. summary 신규 slug가 카드/CHECK에 섞이지 않고 로그에만 남는지, 진단 실패가 판정에 영향을 주지 않는지도 확인합니다.
- Mac 최종 HEAD의 공통 **292,094건**·xAI **603건**, 임시 사본의 `Mac/build.sh`·native smoke, 새 출처 User-Agent 수신, Agent SDK·no_data·필수 누락·범위 표·부분 실패·재시작/24시간 경계·정상화 오알림 방지를 확인해야 합니다. 최종 HEAD를 양쪽에서 확인한 뒤 main에 병합합니다.
- Windows 배포 EXE SHA-256은 기존 `E6AD2CC165BEF4A748E950152731C042A07B5FC8800E82D6C0D83D89ECF445CC`로 불변을 확인했습니다. 검사 후 기존 2.9.2 외부 재실행 완료는 사용자 확인입니다. 이번 PR에서 버전·배포·운영 등록은 바꾸지 않습니다.

## #98~#145 당시 대상·검증 기록 (이하 보존)

공통 원본·Windows 연결과 Mac 전환을 양쪽에서 확인한 뒤 2026-10-27 #98로 병합했습니다.
Windows 2.8.4의 버전·검사·EXE 교체와 외부 확인/실행 대기는 [배포 기록](MAINTENANCE_2_8_4.md)에 둡니다.
아래 #98 당시 검사·버전 미변경 기록은 과거 기준으로 보존하며 Mac 새 버전 배포는 별도 담당 범위입니다.

## 대상과 출처

카드는 **xAI 하나**이며 Grok·Cursor·Grok Bot의 확인된 관련 상태 중 가장 나쁜 상태를 표시합니다.
#98 당시에는 주간 한도를 조회하지 않았고 #122/#123에서 공식 상태 출처/판정과 분리한 [Grok 주간 크레딧](GROK_QUOTAS.md)을 추가했습니다.
이번 공통·Windows PR은 xAI 상태 카드의 별도 `사용량 보기` 링크를 제거하고, 다른 세 카드처럼 전체 카드 클릭을 <https://status.x.ai/>로 연결합니다.
한도 조회·표시·알림·공식 상태 판정은 바꾸지 않으며 버전은 올리지 않습니다. 다음 버전에 함께 배포하고, 사이트는 같은 링크 제거를 준비만 합니다. 아래 #98 당시 화면·검증 기록은 유지합니다.

2026-10-27 공개 GET에서 다음 경로를 확인했습니다. 로그인·쿠키·비공개 계정 API는 사용하지 않습니다.

| 출처 | 범위 |
|---|---|
| <https://status.x.ai/feed.xml> | Grok·Cursor·Grok Bot 공개 RSS 2.0 사건 |
| <https://status.x.ai/api/compat/summary.json> | Cursor/Grok Bot 현재 구성 요소와 진행 사건 |
| <https://status.x.ai/api/compat/components.json> | Cursor/Grok Bot 전체 구성 요소 보완 |
| <https://status.x.ai/api/compat/incidents.json> | Cursor/Grok Bot 사건 이력·해결 확인 |

기존 Cursor 공식 JSON 주소는 이 호환 경로로 연결됩니다. 2026-10-27 조사 당시에는 장기 제공·스키마 안정성을 약속한 공식 공지나 지원 기간을 확인하지 못했습니다.
2026-11-14 공개 GET에서는 summary/components/incidents 세 호환 JSON 모두
`Sunset: Sun, 31 Jan 2027 00:00:00 GMT`를 반환했습니다. RSS에는 이 헤더가 없습니다.
헤더로 공지된 sunset은 **2027-01-31**이며 대체 경로와 전환 방향은 [BACKLOG](BACKLOG.md)의 12월 중순 결정 항목으로 분리합니다.
이번 목록 보정에서 출처를 바꾸거나 sunset 처리·새 API를 구현하지 않습니다.
`page.updated_at` 또는 응답 수신 시각을 사건의 원래 갱신 시각으로 해석하지 않습니다.

| 제품 | RSS 서비스 경로 |
|---|---|
| Grok | `grok-com`, `ios-app`, `android-app`, `grok-in-x`, `grok-build`, `voice`, `office-workspace-plugins`, `api-global`, `api-us` |
| Cursor | `cursor-desktop`, `cursor-ios`, `cursor-cli`, `cloud-agents`, `automations`, `bugbot`, `third-party-models`, `review-agents`, `cursor-com`, `origin`, `enterprise-dashboard` |
| Grok Bot | `grok-bot` |
| 작업 판정 제외 | `imagine` (알려진 이미지 생성 경로이며 장애·심각도·미해결 사건 추적에 포함하지 않음) |

2026-11-05 목록 변경 대응(당시 기록): JSON의 `Review Agents`(`2x2chyqwmkzl`)는 필수 목록에서 빼고
`Third-party models`(`third-party-models`)를 Cursor 범위로 추가합니다. 두 제품을 같은 제품의 이름 변경으로 간주하지 않습니다.
RSS의 `review-agents`는 다시 등장할 때 인식하도록 유지하고, 표시 이름은 `Bugbot`으로 맞춥니다.
`imagine`은 고정 공식 호스트·정상 사건 경로와 ID가 확인된 경우 작업 판정에서 제외합니다.
형식이 깨진 `imagine` 항목도 작업 판정에서 제외하므로 `RSS 사건 필수 필드/상태 형식 이상` CHECK를 만들지 않습니다(신뢰하지 않는 링크는 그대로 CHECK).
같은 사건 ID가 다른 대상 제품에도 있으면 그 제품의 장애·해결·24시간 소실 판정은 그대로 적용합니다.

2026-11-14 목록 변경 대응: JSON에서 사라진 `bugbot`을 필수 목록에서 빼고,
다시 나타난 **`2x2chyqwmkzl`(Review Agents)**를 Cursor 범위로 추가합니다.
`third-party-models`를 포함한 나머지 10개와 **필수 항목이 빠지면 CHECK**인 기준은 유지합니다.
RSS의 `bugbot`·`review-agents` 대응은 모두 유지하며, 표시 이름은 `Review Agents`입니다.
공개 페이지에 추가된 Agent SDK는 이번 필수 목록·RSS 매핑에 추가하지 않고 범위 미확인 처리도 바꾸지 않습니다.

호환 JSON의 현행 필수 구성 요소는 다음 11개입니다. Grok 웹·앱·API·Build는 포함되지 않습니다.

| ID | 표시 범위 |
|---|---|
| `cursor-ios` | Cursor iOS |
| `rflc60xp5jp2` | Cursor Desktop (`IDE`) |
| `mwv1g9sc7kdh` | Cursor Cloud Agents |
| `vsny1qv7v86c` | Cursor CLI (Grok Build와 구분) |
| `k0trcq273dr6` | Cursor Automations |
| `2x2chyqwmkzl` | Cursor Review Agents |
| `third-party-models` | Cursor Third-party models |
| `sm5wkcnqkvr9` | Grok Bot |
| `jh0714rgjgt4` | cursor.com |
| `xwjpvdf81qh9` | Cursor Origin |
| `enterprise-dashboard` | Cursor Enterprise Dashboard |

## 판정과 실패 처리

- **승인된 정상 기준:** RSS를 정상 수신·해석했고 대상 제품의 진행 사건이 없으면
  `정상 · 진행 중 장애 없음(공식 사건 피드 기준)`으로 표시합니다. 구성 요소 기반 전체 가용성 보증이 아닙니다.
  Cursor/Grok Bot에는 호환 JSON도 정상 수신·해석되고 필수 구성 요소가 확인되어야 합니다.
- RSS 제품은 제목 키워드가 아니라 고정 HTTPS 호스트와 서비스 경로로 구분합니다.
  같은 GUID가 여러 제품에 반복돼도 영향 제품을 보존하고 카드 사건 ID를 중복하지 않습니다.
- 현재 상태는 RSS의 구조화된 `category`와 설명 앞의 `Status`·`Severity`가 일치해야 합니다.
  해결 사건의 제목·업데이트 본문에 남은 과거 `Investigating` 문구는 현재 장애가 아닙니다.
- RSS의 `info`는 성능 저하/HOLD, `disruption`은 일부 장애/STOP, `outage`는 주요 장애/STOP으로 매핑합니다.
  이는 앱 표시 정책이며 Statuspage `impact`와 같은 공식 등급 체계라는 뜻은 아닙니다.
- JSON 성능 저하·일부 장애·주요 장애는 기존 권고 규칙을 따릅니다. 건강한 출처 하나로 다른 출처의 확인된 장애를 지우지 않습니다.
  출처 간 진행/해결 상충 또는 모르는 상태·제품·심각도는 상세의 판정 사유로 남깁니다.
- 수신 실패·형식 이상·범위 미확인만 있으면 UNKNOWN/CHECK, 확인된 장애가 함께 있으면 장애를 우선 표시하고 불확실성을 별도 상세로 남깁니다.
  새 제품이 정상 상태로만 추가됐을 때는 장애를 만들지 않지만, 기존 필수 구성 요소가 합친 목록에서도 사라지면 CHECK입니다.
- 이전에 관측한 미해결 사건이 해결 표시 없이 빠지면 첫 소실 확인부터 **24시간 CHECK**를 유지합니다.
  미해결 ID는 실행 중 최대 256개까지 추적하고 다음 실행에서 복원합니다.
  현재 표시용 사건과 분리한 `XaiPendingIncidents.v1` 메타데이터(수신 시각과 최대 2048 UTF-8 bytes의 ID 목록)는
  전체 실패·STALE 캐시나 과대한 부가 진단 뒤에도 유지됩니다. `XaiPendingIncidents.v2`는 사건별 첫 소실 UTC,
  최신 해제 안내와 공식 해결 미확인 여부를 같은 트랜잭션에 저장하며 최대 32 KiB입니다. DB schema는 바꾸지 않습니다.
  v1 ID 전용 캐시에는 소실 시각이 없으므로 새 버전에서 처음 유효하게 소실을 확인한 때부터 24시간을 셉니다.
  추적/저장 한도를 넘으면 불완전 표식으로 CHECK를 유지합니다. 해결 표시가 확인되면 해당 ID를 제거합니다.
- **소실된 사건의 CHECK 해제:** 같은 사건 ID가 RSS의 `resolved` 또는 호환 JSON의
  `resolved`/`postmortem`/`completed`로 수신되고, 그 조회의 다른 출처에 같은 ID의 진행 관측이 없으면
  해당 ID를 그 조회에서 제거합니다. 다음 정기 조회는 보통 5분 뒤이며 Refresh도 같은 판정을 사용합니다.
  남은 미해결 ID나 수신/형식/범위 문제가 모두 없어야 카드가 정상으로 돌아갑니다.
  여러 사건 중 하나만 해결되거나 다른 출처 수신이 실패했으면 나머지 원인의 CHECK는 유지합니다.
  같은 사건이 진행 상태로 재등장하면 CHECK 대신 확인된 장애 상태를 표시하며 해결로 처리하지 않습니다.
- **24시간 소실 해제:** 네 출처를 정상 수신·해석해 미등장을 확인한 첫 시각을 ID별로 고정합니다.
  첫 소실부터 24시간 이상 지난 정상 판독에서도 해당 ID가 없으면 제거하고, 다른 문제가 없으면 정상으로 돌아갑니다.
  반복 조회·재시작은 시각을 뒤로 밀지 않습니다. 수신/형식/범위 문제 중에는 소실 시작·만료를 판정하지 않고 기존 시각을 보존합니다.
  그 사이 같은 ID가 진행 또는 미확인 상태로라도 재등장하면 타이머를 취소하고, 다시 사라질 때 새 24시간을 시작합니다.
  시계가 저장된 첫 소실 시각보다 뒤로 갔으면 현재 시각으로 재설정해 조기 만료를 막습니다.
- 시간 만료 뒤 상세에는 **`해결 표시 없이 피드에서 사라짐(24시간 경과로 해제)`**를 남깁니다.
  최신 안내는 다음 조회·재시작 뒤에도 보존하며 현재 장애나 판정 실패로 다루지 않습니다.
  이는 공식 해결 확인이 아니라 사건 피드 기준선 복귀입니다. 이 복귀의 정상화 알림은 보내지 않고 알림 기준만 갱신합니다.
  새 확인된 장애가 생기면 기존 장애·명시적 해결 알림 흐름을 다시 사용합니다.
- 네 출처를 병렬 조회합니다. 일부 출처 실패는 남은 응답과 실패 출처를 함께 표시하며,
  모두 수신하지 못하면 기존 모니터의 수신 실패·마지막 성공 유지·15분 STALE 처리를 따릅니다.
  요청 deadline과 앱 종료 취소를 구분해 일부 출처의 timeout/과대 본문도 다른 출처의 확인된 장애를 지우지 않습니다.
  정상 수신 후 범위를 못 정한 경우는 오래된 데이터가 아니라 UNKNOWN/CHECK입니다.
- XML은 DTD·외부 엔터티를 금지하고 기존 HTTP의 4 MiB 본문 제한을 적용합니다.
  외부 사건 링크를 명령·브라우저 대상으로 사용하지 않습니다. 카드 클릭은 항상 <https://status.x.ai/>입니다.

공식 상태 조회 주기는 기존 **5분**, 요청 제한은 **15초**입니다. 한도 CLI 조회 정책은 바꾸지 않습니다.
공식 상태 알림의 FULL 구간·이전 확인 상태·대기열·간격은 유지하며 제품명을 본문에 포함합니다.
UNKNOWN/CHECK 자체로 장애·정상화 알림을 보내지 않습니다. Windows 공식 상태 알림은 기존 풍선 방식입니다.

합성 예: `xAI STOP` / `Grok Bot, Cursor · CLI · 일부 장애`.
정상화 문구도 Grok·Cursor·Grok Bot 범위를 밝히고 공식 사건 피드 기준임을 표시합니다.

## #98 화면과 담당 범위 (당시 기록)

- Windows 상태 카드는 3→4개, 기본 client 크기는 374×569→374×658 (96 DPI)입니다.
  카드 높이·간격을 유지하고 하단 컨트롤을 89px 내립니다. 작은 작업 영역에서는 본문 스크롤로 접근합니다.
  한도 세 박스와 상태↔한도 전환은 유지합니다.
- Windows의 `사용량 보기` 링크는 클릭 불가능한 Tooltip과 별도로 xAI 카드 제목 오른쪽에만 둡니다.
  공통 상세에는 주소·조회 제외 안내를 넣습니다. 링크 클릭은 계정 조회·기록·알림 설정 변경을 하지 않습니다.
- #98의 Windows 담당은 Mac 전용 파일을 수정하지 않았고, Mac 담당이 네 번째 카드·사용량 링크,
  마지막 카드 뒤 간격, 팝오버 높이/스크롤, VoiceOver 접근 요소와 네 Provider native smoke를 전환했습니다.
  공통 검사·`Mac/build.sh`·최종 native smoke를 확인한 뒤 병합했습니다. 실제 마우스/VoiceOver 낭독 등 한계는 아래에 구분합니다.
- 기존 Provider 식별 값과 DB 스키마는 유지하고 `Xai`를 뒤에 추가합니다.
  통계·사용 경험 기록은 네 Provider를 다루며 `QuotaProvider`는 기존 세 개 그대로입니다.

## 검사

`XaiStatusTests`는 공통 실행 목록에 한 번 등록합니다. 합성 RSS/JSON과 가짜 HTTP로 제품 매핑,
중복·다중 제품 사건, 해결 사건의 옛 문구, 심각도 우선순위, 상충·부분/전체 실패,
필수 구성 요소 누락, DTD/악성 링크/형식 이상, 사건 소실·캐시 복원·정상화 오알림 방지를 검사합니다.
기존 모니터·저장·트레이·알림 순서·기록/통계 검사의 세 Provider 전제를 네 개로 갱신합니다.
실제 계정 정보는 fixture·문서·PR·이미지에 넣지 않습니다.

검증 결과와 합성 PNG 비교는 #98 본문에 기록했고, Mac 전환·확인 뒤 병합했습니다.

### #98 Windows 담당 당시 검증 (2026-10-27 KST, Mac 전환 전 기록)

기준 main은 `0ccd96fb1f388dbbc11be91a595344177f025350`, 최종 검사 코드 커밋은
`1787bb2d793badeaa464639159fd542371376df6`입니다. SDK 10.0.403으로 `build.ps1`을 실행했습니다.

| 검사 | 변경 전 | 변경 후 |
|---|---:|---:|
| 빌드 경고 / 오류 | 0 / 0 | 0 / 0 |
| 공통 assertion | 255,197 | 290,861 |
| Windows 전용 assertion | 705 | 705 |
| self-test 전체 | 255,902 PASS | 291,566 PASS |
| smoke | 858 PASS, 종료 코드 0 | 895 PASS, 종료 코드 0 |
| 합성 PNG | 22 | 25 |

공통 증가 35,664건은 공식 출처 +1, xAI 신규 +176, 모니터 +2, 트레이 전수 조합 +35,432,
저장 +5, 패널 문구 +48입니다. 트레이 조합은 네 Provider의 상태 조합까지 확장했습니다.
기존 임시 파일시스템의 symlink fixture 1개 SKIP은 전후 같습니다.

smoke는 고정 before/after 슬롯에서 각각 최종 1회 실행했고, OS 알림 전송은 0건입니다.
슬롯별 트레이 기록은 1개이고 새 키는 없었지만, 각 1회이므로 반복 실행 시 증가 억제는 미평가입니다.
개발 중 새 패널 집계에 한도 뷰가 섞인 검사, 200% DPI에서 충분히 크지 않았던 작업 영역 fixture,
상태 보기 복귀 때 세 카드를 기대한 검사는 실패 후 수정했습니다. 대기 시간을 늘리지는 않았습니다.
최종 로그는 `artifacts/xai-status/build/20261027-202259-197/`와
`artifacts/xai-status/windows-tests/after/20261027-202343-107-cd40c1/`에 보존합니다.

20:27 KST의 `--check-providers`는 종료 코드 0이었습니다. xAI 네 출처 정상 수신과
승인된 사건 피드 기준 정상 판정, Claude·Gemini 정상 수신, OpenAI의 범위 미확인 CHECK를 확인했습니다.
이는 공개 상태 조회이며 계정 한도·사용자 DB·운영 등록 검사가 아닙니다.

이미지는 모두 합성 데이터입니다. [전후 및 사건 상태 이미지](docs/reviews/windows-xai-status-20261027/README.md)를 봅니다.
200% DPI에서 주 창은 774×1209→774×1387 px이며 기존 세 카드 영역은 픽셀 단위로 동일합니다.
하단 컨트롤은 178 px 이동했고 Refresh 포커스 테두리 차이가 있습니다.
한도 박스의 행·막대·레이아웃 정보는 그대로이며 창 바깥 프레임과 하단 컨트롤 위치가 달라집니다.
계측 대화상자와 한도 Provider 상세 네 PNG는 픽셀 단위로 동일합니다.
통계는 xAI 기록 행을 추가하므로 해당 표 영역이 달라집니다.

Mac 전환·native 검증은 미실행이며 병합 전 Mac 담당이 완료해야 합니다.
Windows 버전 2.8.3, 배포 EXE, 운영 알림 등록은 변경하지 않았습니다.

### 이전 CHECK 해제 계약 검증 (2026-10-27 KST, 24시간 해제 추가 전)

기존 해제 경로를 확인하고 `XaiStatusTests.CheckMissingIncidentRelease`에 합성 검사 **87건**을 추가했습니다.
제품 판정·문구·Windows/Mac 화면 코드는 변경하지 않았으며 자동 만료도 추가하지 않았습니다.

- RSS `resolved`, summary `resolved`, incidents `resolved`/`postmortem`/`completed` 각각을
  실행 중과 CHECK 캐시 복원 뒤에 확인합니다.
- 30일 경과·다른 사건의 해결·다음 사건의 발생/해결·같은 사건의 진행 재등장은 미해결 ID를 지우지 않습니다.
- 같은 ID가 상충 없이 해결되면 그 조회에서 제거하고, 해결된 사건이 나중에 피드에서 빠져도 CHECK를 다시 만들지 않습니다.
- 상충 해소 뒤의 해결, 여러 소실 사건의 개별 해결, 사건 해결 뒤에도 남은 출처 수신 실패를 구분합니다.

검사 코드 커밋 `9bcca68`의 `build.ps1`은 경고 0 / 오류 0, self-test **291,653건 PASS**
(공통 **290,948** + Windows **705**), 종료 코드 0입니다. xAI 그룹은 176→**263건**입니다.
독립 `Shared.Tests`도 공통 **290,948건 PASS**, 종료 코드 0이며 기존 symlink fixture SKIP은 같습니다.
보고서는 `artifacts/xai-check-release/build/20261027-204133-451/`에 보존합니다.
이번에는 검사·문서만 바꿨으므로 smoke·PNG를 다시 실행하지 않았으며,
위의 895 PASS·25 PNG는 변경 전 제품 코드의 마지막 native 검증 결과로 유지합니다.
당시 공통 검사 기준은 **290,948건**이었습니다. 현재 24시간 해제 기준과 최신 검사 수는 아래 결과와 PR 본문을 따릅니다.

### 24시간 소실 해제 검증 (2026-10-27 KST, 현재 계약)

사용자 결정에 따라 위의 무기한 유지 계약을 24시간 해제로 대체했습니다.
검사한 소스는 `7312c1febe6fa3881d25dcf0c8001d0e4ebdd5bc`입니다.

| 검사 | 결과 |
|---|---|
| SDK / 빌드 | 10.0.403 / 경고 0 · 오류 0 |
| 공통 검사 (`Shared.Tests` 별도 실행 포함) | **291,074 PASS** (직전 대비 +126) |
| xAI 합성 검사 | **389 PASS** (직전 263 +126) |
| Windows 전용 / self-test 전체 | **705 / 291,779 PASS**, 종료 코드 0 |
| 고정 after 슬롯 smoke 최종 1회 | **897 PASS**, 종료 코드 0, 합성 PNG **26개** |

합성 검사는 24시간 직전/정각/직후, 첫 시각 고정, 재등장·재소실·ID별 독립 시각,
형식 불량이지만 같은 ID가 보이는 경우, 수신/형식/범위 문제, 역행 시계,
SQLite 저장·전체 실패·재시작·만료, 실제 v1-only 캐시, 손상/과대 v2의 보수적 복원을 포함합니다.
해제 안내의 정확한 문구·재시작 보존, 정상 카드·트레이 색, 시간 만료의 정상화 알림 억제와
다음 확인된 장애/명시적 해결 알림을 검사합니다. 기존 symlink fixture SKIP은 그대로입니다.

첫 공통 실행은 빈 추적 목록의 복원 뒤 record 동등성 검사에서 실패했습니다.
빈 목록을 같은 빈 배열로 복원하도록 보완한 뒤 기존 저장 검사와 최종 전체 검사가 통과했습니다.
독립 정적 리뷰가 발견한 동일 ID/필드 불량 재등장의 타이머 취소 누락도 보완하고 회귀 검사를 추가했습니다.
수정 후 재검토에서 남은 확정 P1/P2는 없었습니다.

최종 native 검사는 실제 `StatusWindow.UpdateProviders`의 정상 제목·색과 상세 Tooltip 안내를 확인합니다.
직전 #98의 **기존 PNG 25개와 layout JSON 모두 동일**하고, `xai-expired.png` 하나를 추가했습니다.
창 높이·카드 크기·행 수는 이 보완 전 #98과 같습니다. OS 알림 전송은 0건이며 슬롯 트레이 기록은 1개·새 키 없음입니다.
반복 증가 억제는 최종 1회라 미평가입니다. 운영 앱은 사용자가 외부에서 정상 종료했으며 Codex에서 재실행하지 않습니다.

결과는 `artifacts/xai-expiry/build/20261027-210956-532/`,
`artifacts/xai-expiry/windows-tests/after/20261027-211034-513-2120c4/`,
`artifacts/xai-check-release/shared-expiry-tests.txt`에 보존합니다.
Mac 담당은 최신 HEAD의 공통 **291,074건**, 재시작 시각 보존·상세 안내·정상화 알림 구분을 확인합니다.
Mac 파일·Windows 버전·배포 EXE·운영 등록은 변경하지 않았습니다.

### #98 Mac 전환·병합 완료 및 Windows 2.8.4

Mac 담당이 기존 Mac 파일만 전환하고 공통 **291,074건**, `Mac/build.sh` 경고·오류 0,
최종 native smoke **PASS / 종료 코드 0 / SKIP 없음**을 확인했습니다.
최신 PR HEAD `c80e73814a5549a663e020afe0232ee947a03f93`를 사용자가
main `ee9f2d07d0b827c0992b7c3b8155a9bc5ae15c71`로 병합했습니다.
[Mac 최종 결과와 한계](https://github.com/hydron75/ai-burger-clock/pull/98#issuecomment-6442146820)는
실제 마우스·VoiceOver 낭독·실피드/24시간 운영 관찰과 합성 검사를 구분합니다.

Windows 2.8.4 버전 PR은 버전 세 필드·문서만 변경합니다. 후보 검사·EXE 교체는 완료했고
운영 앱 실행·등록은 수행하지 않았습니다. [새 검사·해시·백업·외부 명령](MAINTENANCE_2_8_4.md).

## 사용량 링크 제거 PR의 Mac·사이트 전환

- 공통 `ProviderStatusPages.XaiUsage`/`XaiUsageLabel`과 공통 상세의 사용량 주소·옛 한도 조회 제외 문구를 제거합니다. 공식 상태 주소 `ProviderStatusPages.For(Xai)`는 유지합니다.
- Windows는 LinkLabel·전용 이벤트/핸들러와 제목 폭 예외를 없애고, 네 카드의 크기·레이블 위치/폭·간격을 같게 유지합니다. 카드/제목/본문 클릭의 공식 상태 페이지 연결과 기록 메뉴는 그대로입니다.
- 이번 PR은 Mac 파일을 수정하지 않습니다. Mac은 `MacApplication`의 `OpenXaiUsage`와 연결·검사, `MacStatusPanel`의 `UsageLink`·전용 클릭 영역/접근성·검사를 제거하고 제목 폭을 복원해야 합니다. 공통 멤버가 삭제되므로 이 전환 후 공통 검사·Mac/build.sh·native smoke 확인 전에는 병합하지 않습니다.
- 모바일 사이트의 xAI 한도 박스도 사용량 링크만 제거합니다. v1/v2 수신·행·막대·시간층·데이터/공식 상태 카드 링크는 유지합니다. 별도 체크아웃에서 준비·검사하며 PR 병합 뒤 메인 지시로만 배포합니다.

## 2026-11-14: Review Agents 필수 목록 복귀 (Draft)

- `components.json`과 summary의 구성 요소 11개에서 Bugbot이 빠지고 Review Agents가 다시 포함됨을 공개 GET으로 확인했습니다. RSS 서비스 경로 21종에 새 경로는 없고 Review Agents 15건·Bugbot 0건이었습니다. 이 관측 수치를 피드의 영구 상한이나 전체 제품 범위 보장으로 해석하지 않습니다.
- 제품 코드는 필수 JSON ID 한 곳과 확인 날짜 주석만 바꿉니다. RSS 대응·Agent SDK·출처·5분 주기·15초 제한·부분 실패·사건 소실/24시간 해제·알림·한도 정책은 유지합니다. Windows/Mac 버전·패키지·배포 EXE·운영 등록은 바꾸지 않습니다.
- 합성 현재 목록의 정상 판정이 수정 전 파서에서 실패함을 먼저 확인했습니다. 현재 목록 정상, 11-05 목록(Bugbot 있음·Review Agents 없음)의 CHECK, Review Agents JSON/RSS 진행 사건의 `Cursor · Review Agents` 장애, 전체 필수 ID 누락 CHECK와 기존 Bugbot RSS 대응을 검사합니다. 10-27 목록의 Third-party models 누락 검사도 보존합니다.
- 검증한 제품 코드 HEAD는 `1a01b28154c91815061461e327d8f6f68580090a`입니다. SDK 10.0.404 / Desktop Runtime 10.0.15에서 `build.ps1` 경고 0·오류 0, self-test **293,038건**, 별도 공통 검사 **291,909건**(기존 symlink fixture 1건 SKIP), xAI 집중 검사 **418건**이 통과했습니다. 실제 `--check-providers`는 종료 코드 0이며 xAI는 `Operational`, 필수 구성 요소 누락 사유 없음으로 확인했습니다.
- 고정 after 슬롯의 최종 smoke는 **1,174 PASS / PNG 60개 / 종료 코드 0**입니다. 첫 실행은 기존 상세 팝업의 `status/first-Gemini-long-error actual WM_MOUSEWHEEL` 검사에서 실패했고, 같은 빌드·같은 설정으로 재실행해 통과했습니다. UI·검사 코드를 바꾸지 않았으며 첫 실패의 원인은 미확인입니다. 두 실행 로그를 각각 `artifacts/xai-review-agents-20261114/`와 `artifacts/xai-review-agents-20261114-recheck/`에 보존합니다.
- 모바일 사이트 반영 필요: 같은 필수 ID와 RSS 대응을 로컬 준비본에 적용했습니다. 위 C# HEAD에서 생성한 합성 상태 **143사례·164단계**가 일치하고, 사이트 검사 **85건**, 타입 검사·로컬 빌드·산출물 검사가 통과했습니다. 기존 시간표 **19,972건** 등 회귀도 통과했습니다. RSS `bugbot`/`review-agents` 유지와 Agent SDK 미추가도 같습니다. 소스 동기화·사이트 배포는 PR 병합 뒤 메인 지시 전까지 하지 않습니다.
- Mac은 최종 PR HEAD에서 공통 **291,909건**, `Mac/build.sh`·native smoke, 현재 목록 정상·11-05 목록 CHECK·Review Agents 사건의 Cursor 분류를 확인해야 합니다. Mac 검증과 운영 앱/사이트의 실제 반영은 아직 수행하지 않았습니다. 검사 후 기존 운영 앱은 사용자가 Codex 밖에서 다시 실행했다고 확인했습니다.
