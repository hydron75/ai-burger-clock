# 2.2.1 — 계정 한도 조회·새로고침 안정성

2026-09-30 KST. [PR #8](https://github.com/hydron75/ai-burger-clock/pull/8)을 Windows에서 검증하고 사용자 요청으로 병합했습니다. 버전을 2.2.1로 올린 뒤 최종 단일 EXE를 검사하고 기존 dist 경로에 적용했습니다.

Windows 11 x64 / WinForms / `net10.0-windows`를 유지합니다. 직접 NuGet 의존성은 `Microsoft.Data.Sqlite 10.0.12` 하나이며 새 패키지·DB migration·시간표 정책 변경은 없습니다. Gemini 계정 한도는 추가하지 않았습니다.

## 변경 내용

- **실패 후 재시도:** 한도 조회 실패는 15분 → 30분 → 1시간 → 2시간 → 4시간 → 6시간으로 간격을 늘립니다. 평소 주기가 1시간인 잔여 10% 미만 계정은 1시간을 넘지 않습니다. 리셋 전후의 빠른 조회가 더 이르면 그것을 우선합니다. 성공하면 실패 횟수를 초기화합니다.
- **Claude 실행 파일 탐색:** 특정 PC의 `hermes` 설치 폴더를 별도로 넣었던 후보를 제거했습니다. PATH의 native EXE 또는 npm 배치 구조와 사용자 `.local/bin`을 탐색합니다. 그 폴더가 PATH에 있으면 일반 PATH 경로로 계속 찾을 수 있습니다. Codex 탐색·인증 방식은 그대로입니다.
- **Refresh:** 한도 CLI가 응답을 기다리는 동안에도 공식 상태를 새로 고칠 수 있습니다. 이미 조회 중인 한도 Provider는 중복 실행하지 않습니다. 공식 상태 조회 중에는 메인 Refresh 버튼이 비활성화됩니다.
- **연결 복구:** 반복되는 네트워크 가용성 이벤트에 의한 재조회는 최대 1분에 한 번입니다. 수동 Refresh와 절전 복귀는 이 제한을 받지 않습니다.
- **메뉴:** 트레이의 ‘공식 상태 새로 고침’을 ‘상태·한도 새로 고침’으로 바꿨습니다.
- **검사 보강:** PR의 재시도 검사에 더해, 성공 후 다음 실패가 다시 15분부터 시작하는지 확인하는 assertion 1건을 추가했습니다. 기능 코드의 추가 변경은 없습니다.

기본 6시간 / 잔여 10% 미만 1시간 / 리셋 ±15분 동안 5분이라는 정책은 유지합니다. 한도와 서비스 장애·시간표·실측 통계는 계속 독립적입니다. API 키·인증 파일·쿠키 직접 접근, 브라우저 수집, 추가 모델 요청, 리셋권 사용 기능은 없습니다.

## 변경 파일

PR #8의 기존 파일 수정:

- 기능: `AccountQuotaClient.cs`, `AccountQuotaMonitor.cs`, `AccountQuotaPolicy.cs`, `AccountQuotaView.cs`, `StatusWindow.cs`, `TrayApplicationContext.cs`.
- 검사: `AccountQuotaMonitorTests.cs`, `AccountQuotaTests.cs`, `TrayPresentationTests.cs`, `UIRegressionChecks.cs`.
- 기록 목록: `AGENTS.md`에 2.2.0 기록 링크 추가. 이 배포 후속 작업에서는 지침 파일을 추가 수정하지 않았습니다.

배포 후속 작업의 기존 파일 수정:

- `AiBurgerClock.csproj`: Version 2.2.1, AssemblyVersion/FileVersion 2.2.1.0.
- `AccountQuotaMonitorTests.cs`: 성공 뒤 다시 실패하는 경우의 재시도 초기화 검사.
- `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`: 버전·동작 설명·검증 기록 연결.

새 파일은 이 문서 `MAINTENANCE_2_2_1.md`입니다. C# 소스는 기존 41개(기능 21, 검사·진단 19, AssemblyInfo 1)를 유지합니다. 생성된 진단 도구·로그·DB·PNG는 GitHub에 포함하지 않습니다.

## Git 기준점과 백업

| 기준 | 커밋 |
|---|---|
| 작업 전 원격 main / PR #7 병합 | `535d86530965d1782ba99f36664f8bd60557d114` |
| 작업 전 로컬 소스 / 백업 ZIP 기준 | `80cf7b794e63b5ed8a723704398b55153c0e6797` |
| 검증한 PR #8 HEAD | `1f8ca450ee3df3a813605b7cce68ad3d9e9250fc` |
| PR #8 병합 | `009be364f1234db388285b6089b7eb522a2a95a5` |
| 2.2.1 버전·회귀 검사 / 최종 EXE 소스 | `1882cf15e513223468317df0a834745d2a56c86e` |

최종 EXE는 마지막 행의 커밋에서 빌드했습니다. 이후 문서만 추가·수정했으며 실행 코드와 검사 코드는 바꾸지 않았습니다. ProductVersion에도 같은 전체 소스 커밋이 들어 있습니다. 기존 2.2.0 EXE의 소스 표기에 관한 제한은 [2.2.0 기록](ACCOUNT_QUOTAS.md)에 남겨 두었습니다.

앱 종료와 실제 DB에 활성 WAL이 없음을 확인한 뒤 아래 파일을 백업했습니다. 모두 이 PC에만 보관합니다.

| 로컬 백업 경로 | SHA-256 |
|---|---|
| `../backups/release-2.2.1-20260930/dist-2.2.0.zip` | `ABD353C47F845DAD094658D403684F1E86855D4FF9C0A640ADD23ADB8BA636BB` |
| `../backups/release-2.2.1-20260930/burgerclock.db` | `3F6086BADB6944DBB0B313B1BEE5E77DAE863584AA0B84331BDC6CD4BDCA0288` |
| `../backups/release-2.2.1-20260930/source-80cf7b7.zip` | `8E5DB713C07BAD722E1FA4FE4AD56B2CBFB93C5E4B4E95B6DC5447DB93D3D8D9` |
| `../backups/release-2.2.1-20260930/replaced-2.2.0.exe` | `21E462C8F7B834292EBAE47C67FBACF3822B707904FC6D4911EA9BB955EA6A6A` |

되돌릴 때는 앱을 정상 종료하고 현재 DB를 별도로 보존한 뒤 백업 EXE를 기존 `dist/win-x64/AI Burger Clock.exe`에 복원합니다. 두 버전 모두 schema 2이므로 버전만 되돌리는 경우 DB를 과거 백업으로 덮어쓸 필요가 없습니다. 백업 폴더의 EXE에서 자동 시작을 등록하지 마세요.

## 최종 빌드·검증

로컬 로그 위치: `artifacts/release-2.2.1/`. 실제 계정 잔여량·메모·인증 정보는 이 문서에 기록하지 않습니다.

| 검증 | 결과 | 로그 |
|---|---|---|
| Release build | .NET SDK 10.0.401, 경고 0 / 오류 0 | `final-build.txt` |
| Portable publish | warnings-as-errors, exit 0, 단일 EXE | `final-publish.txt` |
| 최종 EXE 자체 검사 | **250,666 assertions**, exit 0, stderr 0바이트 | `final-self-test.txt` |
| 최종 EXE WinForms 회귀 | **117 PASS**, exit 0, stderr 0바이트 | `final-smoke.txt`, `final-ui/` |
| 실제 DB 비교 | quick_check=ok, schema 2, 설정·실측 해시 동일 | `db-before.txt`, `db-after.txt` |
| 배포 실행 | EXE 해시·버전 일치, 정상 응답 | `deployment.txt` |

기존 dist를 먼저 덮어쓰지 않고 `artifacts/release-2.2.1/final-publish/`에 발행했습니다. 그 최종 EXE로 자체·UI 검사를 통과한 뒤 해시를 대조하고 기존 배포 경로에 교체했습니다.

### 자체 검사 범위

- 자동 시작 경로·승인값의 순수 판정: 229, 레지스트리 쓰기 없음.
- Schedule/DST: 242,363; 미국 공휴일·연장 구간: 1,399.
- 트레이 색상·독립 권고·Tooltip·아이콘·연결 복구 제한: 6,125.
- OpenAI/Claude/Gemini 파서와 모의 HTTP: 96.
- 공식 상태 권고·신선도·polling·취소: 99; 공휴일 정책 캐시: 2.
- SQLite·실측·통계: 132, 임시 10,000행 데이터 포함.
- 한도 스키마·주기·CLI 모의 프로토콜: 163.
- 한도 실패 격리·재시도·SQLite 캐시·재시작·취소: 58.

PR HEAD 검토 당시에는 250,665건이었고, 성공 뒤 재실패 검사를 추가해 최종 수치가 1 늘었습니다. 최종 EXE의 UI 검사는 시작·창 숨기기/열기·시간표 전환·알림 요청, 공식 페이지 클릭 라우팅, 메인/트레이 Refresh, 모의 Resume, Provider 3개 × 기록 4종·메모·DB 재오픈·Statistics 7일/30일/전체, 공휴일 설정, 한도 화면 전환·잔여량·이전 조회값·리셋 경과 표시와 정상 종료를 포함합니다. 시간은 주입하며 시스템 시계를 바꾸지 않습니다.

## 실제 배포 확인

2026-09-30 **03:12 KST**, 기존 dist EXE를 `--autostart`로 실행했습니다. PID 67700, Responding=True, FileVersion 2.2.1.0을 확인했습니다.

- 정상 시작 과정의 공식 CLI 조회가 성공해 Work / Codex와 Claude의 한도 캐시 성공 시각이 모두 새로 갱신됐습니다. 별도의 반복 계정 테스트나 추가 모델 요청은 실행하지 않았습니다.
- 같은 시작에서 세 Provider 공식 상태 캐시가 갱신됐습니다. 당시 OpenAI는 Degraded, Claude·Gemini는 Operational로 기록됐습니다. OpenAI의 서비스 문제와 한도 조회 성공은 서로 다른 정보입니다.
- DB 위치는 기존 `%LOCALAPPDATA%/AIBurgerClock/burgerclock.db` 그대로이며 무결성 검사와 schema 2를 확인했습니다.
- 배포 전후 사용자 실측·일반 설정 해시가 같습니다. 실제 실측은 0건이므로 비어 있지 않은 기록의 저장·재오픈·통계는 임시 테스트 데이터로 검증했습니다. 공식 상태 이력 건수는 24건으로 유지됐으며 전체 이력 행의 해시 비교를 뜻하지는 않습니다.
- HKCU Run은 기존 `dist/win-x64/AI Burger Clock.exe --autostart` 경로를 유지합니다. StartupApproved의 활성 승인값도 동일하며 레지스트리를 수정하지 않았습니다.

## 제한 및 수행하지 않은 검사

- 실제 재부팅·로그인·절전·네트워크 단절은 수행하지 않았습니다. 자동 시작 판정·등록 경로 확인과 모의 Resume/연결 복구 검사로 구분합니다. `--verify-autostart`도 실행하지 않았습니다.
- UI 검사의 Windows BalloonTipShown 이벤트는 15회였지만 알림이 사용자 화면에 모두 노출됐다는 보장은 아닙니다. PNG는 DrawToBitmap 렌더이며 데스크톱 캡처가 아닙니다.
- 1시간·6시간의 장시간 대기, 네트워크 복구 이벤트의 동시 다발 상황, 리셋 구간과 연속 실패를 함께 겪는 실제 서버 상황은 이번에 재현하지 않았습니다. 주기 계산·직렬화·실패 처리는 가짜 시계/응답으로 검사했습니다.
- 공식 CLI의 응답·인증·서버 지원 조건은 외부에서 바뀔 수 있습니다. 이번 성공이 모든 플랜·미래 버전의 성공이나 서버 측 절대 비소비를 보장하지 않습니다.
- 앱 설치·CLI 업데이트·로그인 변경·리셋권 사용·새 알림 정책·Gemini 한도·3단계 통계 기능은 이번 범위에 포함하지 않습니다.

## 최종 실행 파일

- 경로: `dist/win-x64/AI Burger Clock.exe`.
- FileVersion: **2.2.1.0**.
- ProductVersion: `2.2.1+1882cf15e513223468317df0a834745d2a56c86e`.
- 크기: **2,962,667 bytes**.
- SHA-256: `BA4B465444EC066D58CA327AAEC755C0AB90C368B01E58EE72BE47680B0B8134`.
- framework-dependent 단일 EXE, .NET 10 Desktop Runtime x64 필요.

GitHub에는 소스·문서만 반영합니다. EXE·개인 DB·백업·진단 파일·검사 로그는 업로드하지 않습니다.
