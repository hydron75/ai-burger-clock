# 2.0.1 — 공식 상태 페이지 클릭 및 OpenAI 판정 보완

완료일: 2026-09-22 KST. 기존 WinForms/.NET 10, Schedule, DB schema 1, 패키지는 유지했습니다.

## 사용자 동작

- OpenAI/Claude/Gemini 행의 이름, 공식 상태, 이유 또는 여백을 왼쪽 클릭하면 공식 상태 페이지를 Windows 기본 브라우저로 엽니다.
- OpenAI: https://status.openai.com/
- Claude: https://status.claude.com/
- Gemini: https://www.google.com/appsstatus/dashboard/
- 손 모양 커서, 화면 아래 안내, 툴팁으로 클릭 동작을 표시합니다.
- 우클릭은 기존 사용 경험 기록 메뉴입니다. 가운데/오른쪽 클릭으로 브라우저를 열지 않으며, 페이지 열기로 실측 이벤트를 만들지 않습니다.
- 주소는 코드에 고정된 HTTPS 목록입니다. 공식 JSON이나 사용자 메모에 포함된 URL을 실행하지 않습니다. 기본 브라우저 실행 실패 시 앱에 안내합니다.

## 함께 보완한 판정과 표시

- OpenAI가 component를 생략하고 `Plus and Pro users`라고만 알리는 이번 장애를 제한적인 제목 규칙으로 ChatGPT Plus/Pro에 연결합니다. 일반적인 Plus/Pro 단어는 근거로 쓰지 않습니다.
- 관련 없는 Sora/voice/images/billing-only 및 명시적으로 관련 없는 component 연결은 기존대로 제외합니다. 불명확한 범위는 여전히 UNKNOWN입니다.
- UNKNOWN인 현재 incident도 ID/제목을 보존하며, 네트워크 실패 시 과거 incident를 방금 조회한 것처럼 표시하지 않습니다.
- `Checked`를 `최근 조회 시도`로 바꿨습니다. Provider 툴팁은 개별 조회 시도와 마지막 상태 확인 성공, 실패 이유를 분리합니다. STALE는 마지막 성공 15분 기준을 그대로 유지합니다.
- GO→UNKNOWN/STALE→HOLD/STOP도 마지막 확정 권고와 비교해 알립니다. 같은 HOLD로 돌아오는 경우는 중복 알리지 않습니다. 시작 및 Schedule 전환에서는 비교 기준을 초기화합니다.

## 검증

- Release 및 단일 EXE publish: 경고 0 / 오류 0.
- 최종 배포 EXE 자체 검사: 242,624 assertions 통과 (Schedule/DST 242,363 + Provider/HTTP 95 + 권고/갱신/링크 96 + SQLite/통계 70).
- 실제 WinForms 메시지 루프에서 3개 Provider × 4개 클릭 영역의 정확한 URL 전달, 우클릭 메뉴 보존, 사용 기록 미생성을 검증했습니다.
- 링크 테스트는 브라우저 실행 함수를 테스트용 기록기로 대체합니다. 반복 탭을 만들지 않으며, 실제 기본 브라우저의 페이지 렌더를 수동 확인했다는 뜻은 아닙니다. HTTPS URL과 Windows 기본 브라우저 연결용 UseShellExecute 설정을 별도 검사했습니다.
- 트레이/카운트다운/전환/알림, UNKNOWN 중간 경유 알림, Refresh, 12종 이벤트·메모, 통계, 정상 종료가 최종 publish smoke에서 통과했습니다.
- 자동 시작 등록/해제 검사는 원래 켜짐 상태와 dist 경로로 복원했습니다. 실제 사용자 DB는 변경 전 schema 1, integrity ok, 실측 0건을 확인했습니다. 테스트 기록은 임시 DB에만 생성합니다.
- 19:22 KST 실제 공식 조회에서 OpenAI는 ChatGPT Plus/Pro 성능 저하, Claude/Gemini는 정상으로 해석됐습니다. 이는 그 시각의 조회 결과이며 이후 상태는 바뀔 수 있습니다.
- 150% DPI에서 폼 렌더 및 텍스트 잘림 검사 통과. 브라우저 실행 실패·Windows 알림 노출은 사용자 OS 설정에 영향을 받습니다.

로그: `artifacts/status-links/`의 self-test.txt, smoke-test.txt, publish-smoke.txt, live-status.txt 및 PNG.
최종 publish 자체 검사 로그는 `artifacts/self-test.txt`입니다.

## 변경 파일

새 파일: ProviderStatusPages.cs, RecommendationNotifications.cs, STATUS_LINKS.md.
기존 수정: StatusWindow.cs, TrayApplicationContext.cs, StatusMonitor.cs, ProviderStatusClient.cs, ProviderStatusTests.cs,
MonitorTests.cs, UIRegressionChecks.cs, SmokeTest.cs, AiBurgerClock.csproj, README.md, PROVIDER_SOURCES.md.
사용자 데이터 구조/기존 기록과 Schedule 구현은 변경하지 않았습니다.

## 배포와 기준점

- 실행 파일: `dist/win-x64/AI Burger Clock.exe`, 버전 2.0.1.0, 2,692,331 bytes.
- EXE SHA-256: DF6F72332EC8424C33D2559501198F05443E54105F4F2FCE306B4D5DD5E8A745
- .NET 10 Desktop Runtime x64가 필요합니다. SQLite는 단일 EXE에 포함됩니다.
- 변경 전 소스/배포본 백업: `../backups/AiBurgerClock-before-status-links-20260922-191637.zip`
- 백업 SHA-256: C421D3CBC9020A2A1C25512399F16B3F26325A480DDE86EF6DB2013E5686F7CB
- 첫 빌드는 사용자가 bin/Release 경로에서 실행 중인 앱이 EXE를 잠가 복사에 실패했습니다. 해당 경로와 PID를 확인해 종료한 후 최종 빌드는 정상 통과했습니다. 완료 후 dist 경로의 새 버전을 실행했습니다.
