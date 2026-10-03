# 2.2.2 — 잔여 0%의 15분 한도 조회

2026-10-03 KST. 사용자의 요청에 따라 Work / Codex·Claude 한도 조회에 **잔여 0%이면 15분마다 확인**하는 조건을 추가했다. 기존 2.2.1 배포본과 DB를 백업하고, 새 단일 EXE의 자체·WinForms 검사를 통과한 뒤 기존 dist 경로에 적용했다.

Windows 11 x64 / WinForms / `net10.0-windows`를 유지한다. 새 패키지, DB migration, 인증 방식 변경, 크레딧 처리, 한도 리셋 알림은 추가하지 않았다. Gemini 계정 한도도 이번 범위 밖이다.

## 변경 내용

Provider별로 마지막 성공 응답의 유효한 한도 창을 보고 가장 짧은 주기를 적용한다. 5시간·주간·모델별 창 중 하나가 소진돼도 해당 Provider에만 적용하며 다른 Provider의 주기는 바꾸지 않는다.

| 조건 | 조회 주기 |
|---|---|
| 알려진 리셋 시각 15분 전~15분 후 | 5분, 가장 우선 |
| 어느 유효 창이든 실제 잔여량이 정확히 0% | 15분 |
| 잔여량이 0% 초과~10% 미만 | 1시간 |
| 그 외 / 아직 확인된 한도 없음 | 기본 6시간 |

- 판정에는 원본 `UsedPercent == 100`을 사용한다. 화면에서 0%로 반올림된 작은 양수 잔여량은 1시간 조건이다. 정확히 10% 남았으면 기본 6시간이다.
- `AccountQuotaPolicy.RegularInterval`에만 새 우선순위를 넣었다. 기존 다음 조회·리셋 구간 진입·이전 리셋 앵커·실패 재시도 계산은 같은 함수를 재사용한다.
- 마지막 확인값이 0%이면 실패가 이어져도 재시도는 15분을 넘지 않는다. 리셋 구간의 5분 조회가 더 이르면 그것이 우선이다. 실패·잘못된 응답은 마지막 성공값을 덮어쓰지 않는다.
- 새 성공 응답에서 잔여량이 회복되면 확인된 잔여량에 따라 1시간 또는 6시간으로 복귀한다. 리셋 구간 안에서는 기존 5분 조회를 유지한다.
- 한도 화면의 Tooltip에도 0%의 15분 조건을 추가했다. 창 크기·트레이 색상·공식 상태·시간표·실측·통계는 변경하지 않았다.
- 기존 Refresh, 시작·절전 복귀·연결 복구의 즉시 조회, Provider별 직렬화·취소·종료 처리도 그대로다. 매초 인터넷 조회나 별도 타이머를 추가하지 않았다.

리셋권을 다른 공식 화면에서 사용한 결과는 다음 성공 조회에 반영된다. 이 앱에서 리셋권을 사용하거나 잔여 개수를 조회하지 않는다. 리셋 시각 경과만으로 100% 회복을 가정하지 않으며, 크레딧으로 작업을 이어갈 수 있는지도 판정하지 않는다. 앞서 논의한 한도 리셋 알림은 설계 후보이고 이번 버전에는 구현하지 않았다.

## 변경 파일

기존 파일 수정:

- `AccountQuotaPolicy.cs`: `ExhaustedInterval` 상수와 0% 우선 판정.
- `AccountQuotaView.cs`: 실제 조회 정책에 맞춘 Tooltip.
- `AccountQuotaTests.cs`: 정확한 0%·반올림·경계·리셋 구간·실패 상한 검사.
- `AccountQuotaMonitorTests.cs`: Provider 독립성, 마지막 0% 보존, 실패 상한·회복 후 주기 복귀 검사.
- `AccountQuotaUiChecks.cs`: 15분 Tooltip, 소진 표시와 다음 조회 시각 검사·렌더.
- `AiBurgerClock.csproj`: Version 2.2.2, AssemblyVersion/FileVersion 2.2.2.0.
- `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`, `ACCOUNT_QUOTAS.md`: 현재 정책·버전·검증 기록 연결. 과거 릴리스 당시의 검사 결과는 유지했다.

새 파일은 이 문서 `MAINTENANCE_2_2_2.md`다. C# 소스는 기존 41개, 직접 NuGet 의존성은 `Microsoft.Data.Sqlite 10.0.12` 하나, SQLite schema는 2를 유지한다. 지침 파일은 수정하지 않았다.

## Git 기준점과 백업

| 기준 | 커밋 / 상태 |
|---|---|
| 작업 전 로컬·원격 main / PR #10 병합 | `4641d926cf06167202e114afeb0d5406ab0f7f4b` |
| 기능·버전·검사 / 최종 EXE 소스 | `f456c6952829ee8646456cce3967a11035810930` |
| 작업 브랜치 | `feature/exhausted-quota-polling-2.2.2` |
| PR 병합 | 이 작업에서 main으로 병합하지 않았다 |

최종 EXE의 ProductVersion에 위 소스 커밋이 들어 있다. 이후 변경은 기록 문서뿐이며 실행 코드·검사 코드는 동일하다. GitHub에는 소스·문서만 반영하고 EXE·개인 DB·백업·검사 로그는 올리지 않는다.

사용자가 앱을 정상 종료한 뒤 실행 프로세스가 없음을 확인하고 아래 파일을 로컬에 보관했다. DB 복사본은 이후 읽기 전용 SQLite 검사에서 `quick_check=ok`를 확인했으며, 새 앱 시작 전 실제 DB와 복사본의 SHA-256도 같았다.

| 로컬 백업 경로 | SHA-256 |
|---|---|
| `../backups/release-2.2.2-20261003/dist-2.2.1.zip` | `0A45C6437A38719C22A849CA1985F2E1A5034B21955EF97EB63CD9323ABC16C1` |
| `../backups/release-2.2.2-20261003/replaced-2.2.1.exe` | `BA4B465444EC066D58CA327AAEC755C0AB90C368B01E58EE72BE47680B0B8134` |
| `../backups/release-2.2.2-20261003/source-4641d92.zip` | `CAA75456285F7F2D91E71FDF794450CF7F34DC00D3D8127C9A8CB1B33BDB59C3` |
| `../backups/release-2.2.2-20261003/burgerclock.db` | `CBBCAC31CCDEC6D32D03BA65196D9FB4A26534C59C756C46A815E7F976AD07CB` |

되돌릴 때는 앱을 정상 종료하고 현재 DB를 별도로 보존한 뒤 백업 EXE를 기존 dist 경로에 복원한다. 두 버전 모두 schema 2이므로 앱 버전만 되돌리는 경우 DB를 과거 백업으로 덮어쓸 필요가 없다. 백업 폴더의 EXE에서 자동 시작을 등록하지 않는다.

## 빌드·검증

로그 위치는 `artifacts/release-2.2.2/`다. 실제 계정 잔여량·인증 정보·사용자 메모는 이 기록에 포함하지 않는다.

| 검증 | 결과 | 로그 |
|---|---|---|
| Release build | .NET SDK 10.0.401, 경고 0 / 오류 0 | `initial-build.txt` |
| 최종 Portable publish | warnings-as-errors, exit 0, 단일 EXE | `final-publish.txt` |
| 최종 EXE 자체 검사 | **250,708 assertions**, exit 0, stderr 0바이트 | `final-self-test.txt` |
| 최종 EXE WinForms 회귀 | **121 PASS**, exit 0, stderr 0바이트 | `final-smoke.txt`, `final-ui/` |
| 배포 전 DB 보존 | 백업과 SHA-256 동일, 사용자 DB 쓰기 없음 | 로컬 복사 비교 |
| 백업 DB 무결성 | 읽기 전용 `quick_check=ok` | SQLite 복사본 확인 |
| dist 배포·실행 | EXE 해시와 2.2.2.0 일치, Responding=True | 실행 프로세스 확인 |

첫 샌드박스 빌드는 Windows 사용자의 NuGet.Config 읽기 권한 때문에 중단됐다. 정상 권한의 빌드는 통과했으며 NuGet 설정·의존성·계정 로그인을 바꾸지 않았다.

### 자체 검사 범위

- 자동 시작 경로·Windows 승인 판정: 229, 레지스트리 쓰기 없음.
- Schedule/DST: 242,363; 미국 공휴일·연장 구간: 1,399.
- 트레이 색상·독립 권고·Tooltip·아이콘·연결 복구 제한: 6,125.
- OpenAI/Claude/Gemini 파서·모의 HTTP: 96.
- 공식 상태 권고·신선도·polling·취소: 99; 공휴일 캐시: 2.
- SQLite·실측·통계: 132, 임시 10,000행 데이터 포함.
- 한도 스키마·주기·가짜 CLI 프로토콜: 192.
- 한도 monitor·실패 격리·재시도·캐시·재시작·취소: 71.

0%의 15분 주기, 작은 양수 잔여량의 1시간 주기, 5시간·주간·여러 창, 잘못된 숫자, 리셋 구간 진입·이탈, 이전 리셋 앵커, 실패 중 15분 상한과 실제 조회값 회복 후 1시간·6시간 복귀를 가짜 시계·응답으로 확인했다. 실제 계정을 소진하거나 리셋권을 사용하지 않았다.

WinForms 검사는 기존 시작·트레이·창 숨기기/열기·시간표 전환·Windows 알림 요청·공식 페이지 클릭·Refresh·모의 Resume·실측 입력·메모·임시 DB 재오픈·Statistics 7일/30일/전체·공휴일 옵션·한도 화면·정상 종료를 포함한다. 새 0% 화면은 `final-ui/account-quotas-exhausted.png`에 DrawToBitmap으로 렌더했다. 바탕화면 캡처나 실제 계정 화면은 아니다.

## 배포 확인과 제한

- 최종 검사는 별도 `artifacts/release-2.2.2/final-publish/`에서 수행했다. 통과한 동일 EXE의 해시를 대조한 뒤 기존 `dist/win-x64/AI Burger Clock.exe`를 교체했다.
- 교체 전 실제 DB의 SHA-256이 백업과 같음을 확인했다. DB migration과 사용자 데이터 변경은 수행하지 않았다. 정상 앱 시작 후 기존 조회 결과 캐시의 저장은 계속 동작한다.
- 기존 HKCU Run의 dist 경로와 `--autostart`, StartupApproved 승인값이 동일하다. 레지스트리는 수정하지 않았다.
- dist EXE를 `--autostart`로 다시 실행했다. PID 57676, Responding=True, FileVersion 2.2.2.0을 확인했다. 별도 실제 계정 검사 옵션은 실행하지 않았으며, 일반 시작의 자동 조회는 기존대로 동작한다.
- 실제 15분·1시간·6시간 대기, 재부팅·로그인·절전·네트워크 단절은 재현하지 않았다. 해당 주기·전환·취소는 가짜 시계와 응답으로 검사했다. `--verify-autostart`도 실행하지 않았다.
- Windows BalloonTipShown 이벤트는 15회였지만 사용자 화면에 모두 표시됐다는 보장은 아니다. Windows 알림 설정에 따라 달라질 수 있다.
- 공식 CLI와 서버의 응답·인증·신선도는 외부 조건이다. 이번 정책 검사가 새로운 CLI 버전의 실제 계정 성공이나 서버 측 절대 비소비를 보장하지 않는다.
- 공개 자료 모니터링의 일정·설정, 크레딧 처리, 한도 리셋 알림, 리셋권 조회·사용, Gemini 계정 한도와 3단계 기능은 변경하지 않았다.

## 최종 실행 파일

- 경로: `dist/win-x64/AI Burger Clock.exe`.
- 별도 publish 결과: `artifacts/release-2.2.2/final-publish/AI Burger Clock.exe`.
- FileVersion: **2.2.2.0**.
- ProductVersion: `2.2.2+f456c6952829ee8646456cce3967a11035810930`.
- 크기: **2,974,955 bytes**.
- SHA-256: `56250BAF25C192314B513C02977A2A40A1E1D72836C43B0CF20803C2F98E2A10`.
- framework-dependent 단일 EXE, .NET 10 Desktop Runtime x64 필요.
