# xAI 공식 상태 통합 (구현 Draft · Mac 전환/확인 대기)

Windows 2.8.3 이후 작업이며 버전·배포 EXE·운영 등록은 바꾸지 않습니다.
공통 원본과 Windows 연결을 먼저 제공하고 Mac 담당이 전환·검증한 뒤 사용자가 병합합니다.

## 대상과 출처

카드는 **xAI 하나**이며 Grok·Cursor·Grok Bot의 확인된 관련 상태 중 가장 나쁜 상태를 표시합니다.
주간 한도 조회는 구현하지 않습니다. `사용량 보기`는 고정 공식 주소
<https://grok.com/dashboard>로 연결하는 링크이며 값을 읽거나 표시하지 않습니다.

2026-10-27 공개 GET에서 다음 경로를 확인했습니다. 로그인·쿠키·비공개 계정 API는 사용하지 않습니다.

| 출처 | 범위 |
|---|---|
| <https://status.x.ai/feed.xml> | Grok·Cursor·Grok Bot 공개 RSS 2.0 사건 |
| <https://status.x.ai/api/compat/summary.json> | Cursor/Grok Bot 현재 구성 요소와 진행 사건 |
| <https://status.x.ai/api/compat/components.json> | Cursor/Grok Bot 전체 구성 요소 보완 |
| <https://status.x.ai/api/compat/incidents.json> | Cursor/Grok Bot 사건 이력·해결 확인 |

기존 Cursor 공식 JSON 주소는 이 호환 경로로 연결됩니다. 현재 제공 사실은 확인했지만,
장기 제공·스키마 안정성을 약속한 공식 공지나 지원 기간은 확인하지 못했습니다.
`page.updated_at` 또는 응답 수신 시각을 사건의 원래 갱신 시각으로 해석하지 않습니다.

| 제품 | RSS 서비스 경로 |
|---|---|
| Grok | `grok-com`, `ios-app`, `android-app`, `grok-in-x`, `grok-build`, `voice`, `office-workspace-plugins`, `api-global`, `api-us` |
| Cursor | `cursor-desktop`, `cursor-ios`, `cursor-cli`, `cloud-agents`, `automations`, `bugbot`, `review-agents`, `cursor-com`, `origin`, `enterprise-dashboard` |
| Grok Bot | `grok-bot` |

호환 JSON의 필수 구성 요소는 다음 11개입니다. Grok 웹·앱·API·Build는 포함되지 않습니다.

| ID | 표시 범위 |
|---|---|
| `cursor-ios` | Cursor iOS |
| `rflc60xp5jp2` | Cursor Desktop (`IDE`) |
| `mwv1g9sc7kdh` | Cursor Cloud Agents |
| `vsny1qv7v86c` | Cursor CLI (Grok Build와 구분) |
| `k0trcq273dr6` | Cursor Automations |
| `bugbot` | Cursor BugBot (Grok Bot과 구분) |
| `2x2chyqwmkzl` | Cursor Review Agents |
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
- 이전에 관측한 미해결 사건이 해결 표시 없이 빠지면 CHECK를 유지해 정상화 오알림을 막습니다.
  미해결 ID는 실행 중 최대 256개까지 추적하고 다음 실행에서 복원합니다.
  현재 표시용 사건과 분리한 `XaiPendingIncidents.v1` 메타데이터(수신 시각과 최대 2048 UTF-8 bytes의 ID 목록)는
  전체 실패·STALE 캐시나 과대한 부가 진단 뒤에도 유지됩니다. DB schema는 바꾸지 않습니다.
  추적/저장 한도를 넘으면 불완전 표식으로 CHECK를 유지합니다. 해결 표시가 확인되면 해당 ID를 제거합니다.
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

## 화면과 담당 범위

- Windows 상태 카드는 3→4개, 기본 client 크기는 374×569→374×658 (96 DPI)입니다.
  카드 높이·간격을 유지하고 하단 컨트롤을 89px 내립니다. 작은 작업 영역에서는 본문 스크롤로 접근합니다.
  한도 세 박스와 상태↔한도 전환은 유지합니다.
- Windows의 `사용량 보기` 링크는 클릭 불가능한 Tooltip과 별도로 xAI 카드 제목 오른쪽에만 둡니다.
  공통 상세에는 주소·조회 제외 안내를 넣습니다. 링크 클릭은 계정 조회·기록·알림 설정 변경을 하지 않습니다.
- Mac 전용 파일은 이번 Windows 작업에서 수정하지 않습니다. Mac 담당은 네 번째 카드·사용량 링크,
  마지막 카드 뒤 간격, 팝오버 높이/스크롤, VoiceOver, 네 Provider를 전제한 native smoke를 전환합니다.
  공통 검사·`Mac/build.sh`·native smoke와 작은 화면의 전체 컨트롤 접근을 확인해야 합니다.
- 기존 Provider 식별 값과 DB 스키마는 유지하고 `Xai`를 뒤에 추가합니다.
  통계·사용 경험 기록은 네 Provider를 다루며 `QuotaProvider`는 기존 세 개 그대로입니다.

## 검사

`XaiStatusTests`는 공통 실행 목록에 한 번 등록합니다. 합성 RSS/JSON과 가짜 HTTP로 제품 매핑,
중복·다중 제품 사건, 해결 사건의 옛 문구, 심각도 우선순위, 상충·부분/전체 실패,
필수 구성 요소 누락, DTD/악성 링크/형식 이상, 사건 소실·캐시 복원·정상화 오알림 방지를 검사합니다.
기존 모니터·저장·트레이·알림 순서·기록/통계 검사의 세 Provider 전제를 네 개로 갱신합니다.
실제 계정 정보는 fixture·문서·PR·이미지에 넣지 않습니다.

검증 결과와 합성 PNG 비교는 Draft PR 본문에 기록합니다. Mac 확인 전에는 병합하지 않습니다.
