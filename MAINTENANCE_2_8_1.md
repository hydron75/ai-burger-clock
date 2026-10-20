# AI Burger Clock 2.8.1 — Windows 배포 기록

기록일: **2026-10-20 KST**. #82 병합 뒤 main을 `git pull --ff-only`로 받고 `codex/windows-release-2-8-1`을 만들었다. 이번 범위는 **Windows만 변경(버전·문서)**이다. 공통/Windows C#·검사·Mac 코드·Mac 버전을 다시 고치지 않았다.

이번에는 **사용자가 버전 PR 생성과 로컬 EXE 교체·실행을 함께 승인**했다. 따라서 배포 소스는 아래 **미병합 버전 브랜치 커밋**이며 병합 main이라고 기록하지 않는다. PR은 사용자가 병합한다. 뒤의 결과 문서 커밋과 실제 EXE 소스도 구분한다. **22:49 KST 교체·실행 완료**, 자동 시작 등록 불변·새 EXE 경로·프로세스 응답 정상이다. 실제 화면 육안 확인과 합성 smoke는 구분한다.

## 2.8.0 이후 포함 변경

| 출처 | 2.8.1에 포함하는 Windows 변경 |
|---|---|
| #74 | 대기 회복/조기 초기화 알림의 표시 직전 재검증을 공통 `QuotaPendingAlertPolicy`로 전환. 호스트 큐·설정·6.5초 간격·OS 전달과 회복 알림 판정은 유지 |
| #80 | `QuotaLine`의 이름 / 사용률·리셋 / 전체 읽기 문장 분리. 오른쪽 폭을 먼저 확보한 뒤 이름만 가운데 생략하며 이모지·결합문자 보호. 기존 Text·Label·ID, 창 높이·행 수 유지 |
| #82 | 주간/모델 주간만 직전 성공의 사용률 감소와 이전 예정까지 5분 초과로 판정. 20%p 감소·새 10% 이하·90분 상한·리셋 시각 변경 조건 제거. 5시간은 조기 초기화에서만 제외 |
| #82 | 캐시의 마지막 성공도 재시작 후 첫 live 성공과 비교. 기존 CID·창 집합/기간·시계 정합성은 유지하며 캐시 로드 자체는 알림이 아님 |
| #82 | 조회별 판정/제외 사유를 별도 `logs/quota-early-reset-decisions-v1.jsonl`에 최근 256건/128KiB로 기록. 사용률·리셋·계정값·원문은 제외. Alert는 OS 발송 증거가 아님 |
| #82 리뷰 보완 | 주입 시계 예외에도 Provider gate 반환. 통합 검사의 초기/재시작 엔진 접근 직렬화, Gemini fixture의 실제 완료 대기. Mac 늦은 종료 콜백 보완은 Mac 담당이 추가했고 Windows 런타임 수정으로 세지 않음 |

이름 충돌 번호·전체 이름 별도 보존/캐시 변경·80/180자 제한 변경·알림 40자 축약, smoke 알림 누적 개선, 조회 트리거 v2 기록은 구현하지 않았다. 기본 1시간·고사용량 1시간·소진 15분·리셋 구간 5분·실패 상한 1시간·Gemini 0% 제외 규칙, 알림 독립 기본 OFF·문구·회복 판정·큐/간격·DB schema 2·캐시 v1은 유지한다.

**한계:** Provider별 한 계정 전제다. CID는 실제 로그인/요금제 ID가 아닌 실패 세대이고 재시작 전 실패 세대는 복원하지 않는다. 작은 원본 감소도 후보가 될 수 있으며 서버 재계산/요금제 변경과 실제 초기화 원인을 구별하지 못한다. 같은/이전 응답·0→0은 재생하지 않지만 영속 발송 ledger는 없고 별개 감소가 다시 후보가 될 수 있다. 설정 전환 후 첫 성공 무알림과 저장된 ON으로 재시작한 캐시 비교를 구분한다.

## Git 기준점

| 기준 | 전체 커밋 |
|---|---|
| 이전 2.8.0 EXE 소스 | `01d82bf3bf26d28b7e9bfac02b2b0e684a051b1f` |
| #74 병합 | `8529df48d75bafe0e136e5a894e70bcb1364509c` |
| #80 병합 | `a42cefdd40a9bb386e23f6093273417d2888366d` |
| #82 병합 / 이번 출발 main | `b45e221eacecb7b74cd706dba92c2fab403f8e09` |
| 2.8.1 버전 / 실제 EXE 소스 | **`d0cc64eb8004e37c39ecafb5afbaace47ef41076`** |
| 2.8.1 버전 PR 병합 | **미병합, 사용자 결정 대기** |

Version **2.8.1**, AssemblyVersion/FileVersion **2.8.1.0**. framework-dependent 단일 EXE이며 .NET 10 Desktop Runtime x64가 별도로 필요하다. 소스 지도는 루트·Properties C# 96개(제품 45 + 검사·진단 50 + 명찰 1), Mac 14개, 공통 검사 입구 1개로 총 111개이며 공통 제품 원본은 33개다. 이번 버전 PR이 새 C#를 추가한 것은 아니다.

## 실제 검증

SDK **10.0.402**, 위 버전 커밋의 동일 C#·csproj를 사용했다. 이후 문서만 수정했다. WinExe는 `Start-Process -Wait -PassThru`의 실제 ExitCode로 판정한다. 테스트 응답·DB·이미지는 모두 합성 데이터이며 실제 계정 값이나 진단 원문은 게시하지 않는다.

| 항목 | 준비 bin 후보 | 최종 단일 EXE |
|---|---|---|
| 명령 | `build.ps1 -ReportGroup local-release-2-8-1` | `build.ps1 -Publish -ReportGroup local-release-2-8-1-final` |
| 빌드 | 경고 0 / 오류 0 | 경고 0 / 오류 0, publish 성공 |
| self-test | **255,369건·실제 ExitCode 0** | **255,369건·실제 ExitCode 0** |
| 공통 / Windows 전용 | **255,062 / 307** | **255,062 / 307** |
| native smoke | **830 PASS·실제 ExitCode 0**, 별도 1회·53.49초 | **830 PASS·실제 ExitCode 0**, 별도 1회·52.60초 |
| 합성 PNG / layout | 22 / 16, #82 대비 38/38 SHA-256 동일 | 22 / 16, #82 대비 38/38 SHA-256 동일 |

2.8.0 대비 self-test **+760**, Windows 전용은 307건 그대로다. #74+#80 공통 +241, #82 최종 +519이며 버전 PR 자체의 검사 증감은 0이다. smoke는 634→830(+196)이며 새 버전에서 검사/대기시간을 바꾸지 않았다. 준비 smoke와 최종 단일 EXE smoke를 서로 다른 결과로 구분하고 고정 after 슬롯만 사용한다.

기존 진단 symlink fixture 1개는 임시 파일시스템의 링크 생성 제한으로 명시적 SKIP이며 실제 링크 보호까지 확인했다고 주장하지 않는다. 이번에는 실제 계정 조회 명령·리셋권 사용·알림 설정 전환·자동 시작 등록 쓰기·알림 이력 삭제를 실행하지 않았다. 일반 앱 시작에 따른 평소 조회/캐시 저장은 별개다.

## 승인된 교체 및 백업

1. Computer Use에 기존 앱의 종료 메뉴가 노출되지 않아 사용자가 2.8.0을 정상 종료했다. 앱 프로세스 0개와 DB WAL/SHM 부재를 확인했다. 알려진 DB 경로를 실제 열린 handle 경로로 대조해 가상화 사본을 구별했으며 DB 행은 읽어 출력하지 않았다.
2. 새 **`../backups/release-2.8.1-20261020-224154/`**에 `replaced-2.8.0.exe`, `dist-2.8.0.zip`, 이전 실제 소스 ZIP, 정상 종료한 DB·로그·비공개 manifest를 보존했다. EXE 복사본/ZIP 안 EXE와 DB 복사본의 해시를 원본과 대조했다. 기존 백업을 덮어쓰거나 DB/sidecar를 삭제하지 않았다.
3. 준비 빌드/self-test/smoke 성공 뒤 앱/WAL/DB 해시를 재확인하고 `build.ps1 -Publish`로 위 소스 커밋의 단일 EXE를 교체했다. 최종 self-test도 같은 수로 통과했다. 최종 EXE의 별도 smoke도 830건/종료 코드 0이며 새 트레이 슬롯 키는 0개다.
4. Run 값·종류와 StartupApproved/Run의 바이트·종류를 백업 스냅샷과 읽기 전용으로 대조해 불변을 확인했다. 큰따옴표 절대 dist 경로 + ` --autostart`가 새 2.8.1 EXE를 가리킨다. 등록·설정은 다시 쓰지 않았고 `--verify-autostart`도 실행하지 않았다.
5. 실행 직전까지 DB 해시·WAL/SHM 부재를 재확인하고 같은 dist EXE를 `--autostart`로 실행했다. 정확한 프로세스 경로·한 개 실행·Responding=True를 확인했다. 새 EXE를 백업의 `published-2.8.1.exe`로 보존하고 해시도 대조했다. Computer Use의 실제 창 목록에는 앱이 노출되지 않아 트레이·상태/한도 창 육안은 사용자 확인을 별도로 요청했다. 정상 시작에 따른 평소 계정 조회/캐시 저장은 실행 전 DB 불변 확인과 구분한다.

### 최종 단일 EXE

| 항목 | 결과 |
|---|---|
| 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion | **2.8.1.0** |
| ProductVersion | **`2.8.1+d0cc64eb8004e37c39ecafb5afbaace47ef41076`** |
| 크기 | **4,076,779 bytes** |
| SHA-256 | **`4BF374BB6B62867B50FB5AAC84A0EA0C647D68CABC30F628DB508F856DDD3DC2`** |
| 이전 EXE SHA-256 | `233A197498A7F88C9D68C7D6EDCAA3E957665CEBE4BF6A6E516575B1D92D0041` |

백업의 DB·원문 로그·manifest는 비공개 로컬 자료이며 커밋하지 않는다. 새 EXE도 백업에 보존하고 동일 해시를 확인한다. 문서에 실제 계정의 요금제·비율·리셋·ID나 진단 원문을 넣지 않는다.

로컬 증거(ignored): 준비 build/self-test `artifacts/local-release-2-8-1/build/20261020-224029-038/`, 준비 smoke `artifacts/local-release-2-8-1/windows-tests/after/20261020-224218-321-66abec/`, 최종 build/publish/self-test `artifacts/local-release-2-8-1-final/build/20261020-224551-289/`, 최종 smoke `artifacts/local-release-2-8-1-final/windows-tests/after/20261020-224728-248-12e8b6/`. 실제 경로·시각·해시를 대조한 백업/자동 시작 자료는 위 백업의 비공개 manifest/result에 보관한다.

## 복원 계획 — EXE만 2.8.0으로

문제가 있으면 앱을 정상 종료하고 최신 DB·로그를 별도로 보존한 뒤 위 백업의 `replaced-2.8.0.exe`만 같은 dist 경로로 복원한다. FileVersion 2.8.0.0, ProductVersion `2.8.0+01d82bf3bf26d28b7e9bfac02b2b0e684a051b1f`, 위 이전 SHA-256을 확인하고 같은 `--autostart` 인수로 실행한다. 자동 시작 등록은 수정하지 않고 DB를 옛 사본으로 덮어쓰지 않는다. 캐시 v1/schema 2는 그대로이고 별도 새 판정 파일은 이전 버전이 사용하지 않는다. 복원하면 #74/#80/#82가 없는 이전 표시·판정으로 돌아간다. 실제 복원 시험은 수행하지 않았다.

## 미수행 및 결과 구분

- 새 배포본의 실제 트레이·상태/한도 창은 사용자 육안 확인 대기다. 합성 native smoke와 프로세스 응답을 실제 화면 관찰로 확대하지 않는다.
- 실제 조기 초기화 발생·Windows 배너 육안/알림 센터 보존·장시간 운영·재부팅·절전·복원 시험은 하지 않았다.
- Mac의 검증은 기존 PR 담당자 기록이며 이번 Windows 버전 작업에서 새로 Mac 빌드/배포를 실행하지 않았다. 과거 `MAINTENANCE_*`, `ACCOUNT_QUOTAS.md`, `MACOS_PORT.md`의 기록은 유지한다.
