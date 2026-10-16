# Gemini / Antigravity 한도 — #30 구현·2.3.0 배포 기록

## 모델 전용 주간 행

**2026-10-16: #62는 Mac 확인과 사용자 승인 뒤 main `bbeb12eeaac62ee20b204ce6159f378f0fbf0846`에 병합했다. #63도 병합됐으며 두 변경은 Windows 2.7.0 배포 준비에 포함한다. 배포 EXE는 여전히 2.6.0, 미교체다.** [2.7.0 준비 기록](MAINTENANCE_2_7_0.md). 아래 #30 당시의 실제 두 창 기록을 세 창으로 고쳐 쓰지 않는다.

- 순서는 `5시간` → `주간` → `주간 {묶음 이름}`이다. 합성 예시는 `주간 Gemini Synthetic Model`이며 모델명을 고정하지 않는다. 공통 주간을 `주간 전체`로 바꾸거나 각 묶음의 비율을 더하지 않는다. 동시 차감 관계는 응답만으로 보장되지 않는다.
- 추가 묶음은 정확한 `Gemini ` 이름 영역, `gemini-…-weekly` ID 영역, **정확히 하나의 `weekly` 창**, 유효한 필드·비율·리셋·중복·길이 제한을 모두 만족해야 한다. Gemini처럼 보이는 잘못된 이름, 미지원 창, 중복·모순은 전체 Gemini 응답을 거부한다. 무관한 Claude/GPT·크레딧은 계속 제외한다. 기존 공통 묶음에서 한 창만 제공하는 응답도 유지한다.
- 원본 `(묶음 이름, bucket ID)`를 길이 접두 방식으로 합쳐 모델 행 ID를 만든다. 표시용 단축 이름을 정체성으로 쓰지 않으며, 합친 ID는 캐시·알림의 512자 한도를 넘으면 거부한다. 기존 공통 두 ID는 유지한다.
- 새 행은 10080분 주간 창으로 기존 시간층·경계선·행별 접근성 비교·Provider 상세에 연결된다. 회복 알림은 기본 꺼짐이며 첫 등장·이름/ID 변경·소실 후 재등장은 조용히 기준만 만든다. 같은 창의 정확한 소진→회복만 기존 엔진이 알린다.
- 버전 허용 범위·공식 CLI 명령·0턴/0토큰 보호·캐시 v1/DB schema 2·조회 정책 계산은 바꾸지 않는다. 새 행도 기존 Provider별 조회 주기 판정 대상이다. 공식 모델/scope 필드가 없는 응답에서 관측한 제한적 호환 규칙이지 미래의 모든 묶음 자동 지원 보장은 아니다.
- Windows 바깥 창 374×569 DIP·한도 viewport 342×214·전환 방식은 그대로다. 추가 행은 박스 내용 높이를 34 DIP 늘리고 내부 세로 스크롤로 표시한다. 문서·PNG는 합성 데이터만 사용한다.
- **Mac 확인 요청:** 같은 HEAD에서 공통 검사·`Mac/build.sh`·native smoke. 기존 두 행 시나리오는 보존하고 새 세 행 시나리오의 전체 막대 8개(기존 7개+1), 긴 모델명, 낮은 화면의 한도 스크롤·팝오버 높이, VoiceOver의 모델 행 이름·공통 `ComparisonText` 연결을 확인한다. `Mac/README.md`의 범위 설명도 Mac 담당이 갱신한다. 이 PR은 Mac 파일·버전·배포본을 수정하지 않는다.
- **Windows 실제 검증:** main `5706c2580721a4d94df6d33200455db5cd775029`와 후보에서 SDK 10.0.402·`build.ps1` 경고 0/오류 0, 실제 self-test ExitCode 0. 전체 **252,051 → 252,149건(+98)**, 공통 **251,744 → 251,842(+98)**, Windows 전용 **307 그대로**다. 새 모델 구조/표시/시간 비교/회복/임시 캐시 96건과 CLI 1.3.6/모델 행 전달 2건이 늘었다. 감소·중복 실행은 없다. 별도 `Shared.Tests`도 **251,842건·ExitCode 0**이다.
- 기준 smoke **572 PASS**, 최종 후보 **574 PASS**, 각각 1회·실제 ExitCode 0이다. 합성 막대·행 순서·접근성/Provider 상세·내부 스크롤·Refresh·합성 10초 조회 중 응답성·기존 회복 알림 연결을 확인했다. 단일 EXE publish·실제 회복 배너·Mac native 검사는 미수행이다.
- 후보의 실제 호환 확인은 설치 agy **1.3.6**에서 공식 `/usage` 1회·**5.54초·ExitCode 0**, 기존 0턴/0토큰 보호를 통과해 **공통 두 창+모델 전용 주간, 총 세 행**을 채택했다. 원문·계정 수치·시각·식별자는 저장/게시하지 않았고, probe는 사용자 DB·설정을 건드리지 않았다. EXE·검사 DLL·CLI 해시는 실행 전후 동일하다. 전체 계정 화면 육안 확인·실제 회복 알림 전달 검증을 대신하는 것은 아니다.
- 전후 합성 PNG 20개 중 **14개 SHA-256 동일**. 세 한도 스크롤 화면은 새 행 때문에 달라지고, 일반 한도/메모 화면은 스크롤바 76픽셀만 다르다. Gemini 상세는 폭 640px 그대로·높이 862→958px다. 바깥 client 748×1138px(192 DPI에서 374×569 DIP)는 동일하고 Gemini 박스만 264→332px(+34 DIP) 늘었다. [변경 전](docs/reviews/gemini-model-weekly-20261016/before/account-quotas-gemini.png) / [변경 후](docs/reviews/gemini-model-weekly-20261016/after/account-quotas-gemini.png), [이전 상세](docs/reviews/gemini-model-weekly-20261016/before/quota-tooltip-gemini.png) / [새 상세](docs/reviews/gemini-model-weekly-20261016/after/quota-tooltip-gemini.png). 모든 이미지·수치·시각은 합성 데이터다.
- 로컬 로그·PNG: ignored `artifacts/gemini-model-weekly-20261016/{before,after}/`. 검사 후 같은 **2.6.0 배포본을 다시 실행**했고 EXE SHA-256은 전후 동일하다. 배포 파일·자동 시작 설정을 바꾸지 않았다.

### #62 Mac 확인 완료

- [Mac 확인 코멘트](https://github.com/hydron75/ai-burger-clock/pull/62#issuecomment-6206650333): `bf9c72d290fcc809da5fea9c077768743b740a4c`에서 공통 **251,842건 PASS**, `Mac/build.sh` 경고 0/오류 0, native smoke **2회 PASS·ExitCode 0**. 새 모델 시나리오의 전체 막대 7→8→9, 세 행 팝오버 +14pt와 낮은 화면 내부 스크롤·행별 VoiceOver 문자열을 확인했다. 실제 VoiceOver 음성·OS 배너 육안 등은 그 코멘트의 미수행 구분을 따른다.
- 추가 커밋은 Mac 검사·기록만 바꿨으므로 사용자 지시에 따라 Windows 재검사를 하지 않고 병합했다. Mac 파일을 Windows 담당이 수정하지 않았다. 긴 모델명 단축/한 줄 꼬리 잘림은 별도 후속 표시 후보다.

## 후속 개발: 미사용 창의 잦은 조회 방지

**#63은 Windows·Mac 확인과 사용자 승인 뒤 main `3757a9eef93d21a1a3254a342a46a96b3baf5c8c`에 병합 완료했다. Windows 2.7.0 준비에 포함하며 배포 2.6.0은 미교체다.** 이 절이 아래 #30 당시 조회 정책의 후속 예외이며, 현재 설치된 배포본에 이미 적용됐다는 뜻은 아니다.

### 근거와 원인

- 사용자 제공 Mac 기록: **10월 9일부터 일주일간 앱이 agy를 2,313회 실행(하루 약 330회)**했다. 앱의 `--version`과 `/usage`는 서로 다른 프로세스이므로 `/usage` 2,313회라고 부르거나 원자료 없이 정확히 절반으로 환산하지 않는다. 실제 계정의 비율·시각·식별자나 로그 원문은 적지 않는다.
- 기존 실제 정책 코드로 한 합성 재현(가짜 시계·0 CLI 지연·모든 응답 성공·수동/절전/네트워크 신호 없음): 사용 0%인데 리셋이 응답마다 현재+5시간으로 이동하면 72시간에 **575회** 예약되며 뒤에는 5분 예약이 이어졌다. 현재+17분이면 다음 구간 진입이 현재+2분이어서 60분 동안 **31회** 예약됐다. 고정 리셋은 한 구간 뒤 정상 복귀했다. 이 재현이 개별 운영 실행 2,313회의 원인을 모두 증명하지는 않는다.
- `AccountQuotaPolicy`는 모든 리셋을 ±15분 후보로 삼았고, `AccountQuotaMonitor`는 이동한 이전·현재 리셋을 64개까지 보존했다. 이 두 경로가 결합해 빠른 조회가 다음 빠른 조회의 근거를 계속 만드는 순환이 생겼다.

### 변경·보존 계약

- **Gemini의 정확한 원본 사용 0% 창만** 새 빠른 조회 후보/보존 기준에서 제외한다. 5시간·공통 주간·모델 전용 주간에 같은 구조 규칙을 적용하며 모델명·ID로 예외 처리하지 않는다. 아주 작은 양수는 표시가 0%로 반올림돼도 계속 리셋 구간에 참여한다.
- Gemini의 다른 유효 창에 사용이 있으면 그 창의 리셋·일반 주기를 그대로 적용한다. Codex·Claude, 사용 90% 초과~100% 미만 1시간·정확히 100% 15분·기본 6시간·실패 재시도는 유지한다. 실제 사용 창의 구간 진입이 2분 뒤라면 그 예약까지 금지하는 하한을 새로 만들지는 않는다.
- `CurrentResetAnchors` 한 곳을 정책과 모니터가 함께 사용한다. 모니터가 기존 Reading을 다시 합칠 때도 미사용 리셋을 재유입하지 않는다. 이미 실제 사용에서 얻은 이전 리셋은 새 응답이 0%여도 +15분까지 유지한다.
- 재시작 시 **구형 Gemini 캐시만** 출처 불명 리셋 목록을 버리고 캐시에 있는 사용 양수 창의 리셋으로 재구성한다. **전환 제약:** 구형 목록은 창 출처가 없어 오염 기준과 정상 과거 기준을 구분할 수 없으므로, 근거를 입증할 수 없는 과거 +15분 구간은 한 번 포기한다. 시작 조회는 원래대로 즉시 실행하고 한도 값·성공 시각·회복 기준을 만들어내지 않는다.
- 새 Gemini 성공 캐시에 선택적 `ResetAnchorPolicyVersion=1`을 저장한다. 필드가 없는 기존 JSON은 0이며 기본 0은 직렬화에서 생략한다. 새 정책 캐시는 정상 이전 기준을 재시작 후에도 보존한다. JSON payload `Version=1`, `AccountQuota.v1.Gemini` 키, DB schema 2는 그대로이며 구형 reader는 추가 필드를 무시한다. 알 수 없는 정책 버전은 안전하게 거부한다. 캐시를 읽거나 조회에 실패했다는 이유만으로 새 성공 캐시를 쓰지 않는다.
- 상세에 `Gemini 사용 0% 창의 리셋은 빠른 조회 기준에서 제외합니다.` 한 줄만 추가한다. 사용량 행·막대·리셋 카운트다운·시간 비교·회복 알림 판정·CLI 명령/버전 보호·수동 Refresh/시작/절전/연결 복구는 변경하지 않는다. 화면을 열 때 새 조회를 추가하지 않는다.

### 추가 보완: Provider별 다음 조회 예약 진단

- Codex·Claude·Gemini가 조회를 마치고 다음 조회를 정할 때 **실제 예약에 사용한 판정 결과**를 공통 진단 파일에 남긴다. 별도 설명용 계산으로 예약 이유를 추정하지 않는다. 성공·실패 모두 기록하며 취소되어 다음 예약을 만들지 않은 조회는 기록하지 않는다.
- 모든 Provider를 합쳐 **최근 256건·최대 128KiB**만 보관한다. 재시작 후에도 이어 쓰며 같은 폴더의 임시 파일을 완성한 뒤 교체한다. 기존 파일이 손상됐거나 알 수 없는 형식·과대 크기·권한 문제·잠금 상태이면 원본을 덮어쓰지 않고 그 기록을 건너뛴다. 진단 실패가 조회 성공·회복 알림을 실패로 바꾸지는 않지만 이 경우 이력이 빠질 수 있다.
- 기본 위치는 Windows `%LOCALAPPDATA%\AIBurgerClock\logs\quota-polling-diagnostics.json`, Mac `~/Library/Application Support/AIBurgerClock/logs/quota-polling-diagnostics.json`이다. 실제 DB가 다른 위치이면 **그 DB 폴더의 `logs/`**를 쓴다. 임시 DB 검사도 해당 임시 폴더만 사용하며, store 없는 모니터는 명시적으로 주입하지 않는 한 파일을 만들지 않는다.
- JSON의 `Entries`는 기록된 순서다. `Provider`, `AttemptedAtUtc`(조회 시도 시작), `RecordedAtUtc`(예약 결정 시점), `NextCheckUtc`(다음 조회 예정), `Reason`, `SelectedResetUtc`(예약 근거 리셋, 없으면 null), `Succeeded`, `ConsecutiveFailures`, `ResetAnchorCount`(보관된 리셋 기준 수, 최대 64)를 기록한다. 모든 시각은 UTC다. `ResetAnchorCount`는 보관 목록 수이며 현재 창을 다시 합산한 정책의 전체 후보 수를 뜻하지 않는다.
- 이유는 `Regular`(기본 6시간), `LowRemaining`(사용 90% 초과~100% 미만, 1시간), `Exhausted`(정확히 100%, 15분), `ResetPeriod`(리셋 전후 구간, 5분), `ResetEntry`(리셋 15분 전 진입이 더 빠름), `FailureRetry`(실패 재시도), `Overdue`(이미 지난 예약의 즉시 조회)다. 실패 재시도와 다른 후보의 시각이 같으면 `FailureRetry`로 적고 예정 시각은 바꾸지 않는다.
- **이 파일은 다음 예약의 이력이지 실행 트리거의 이력이 아니다.** 수동 Refresh·시작·절전 복귀·연결 복구는 예정 대기를 앞당길 수 있으므로 실제 조회 간격을 모두 정기 조회로 해석하지 않는다. 기존 배포 2.6.0의 과거 기록을 소급해 복원하지 않는다.
- 요금제·사용률·계정/창 ID·CLI 원문·오류 원문은 저장하지 않는다. 다만 요청한 선택 리셋과 조회 시각은 계정 활동과 관련된 **로컬 비공개 정보**다. 파일 원문·실제 시각을 PR/코멘트에 올리지 말고 합성 자료나 일반화한 간격·이유별 건수만 공유한다.

Windows에서 최근 20건을 로컬로 확인하는 예:

```powershell
$quotaLog = Join-Path $env:LOCALAPPDATA 'AIBurgerClock\logs\quota-polling-diagnostics.json'
(Get-Content -LiteralPath $quotaLog -Raw | ConvertFrom-Json).Entries |
    Select-Object -Last 20 |
    Format-Table Provider, Reason, RecordedAtUtc, NextCheckUtc, SelectedResetUtc, Succeeded, ConsecutiveFailures, ResetAnchorCount
```

Mac에서는 위 경로의 JSON을 텍스트 편집기로 열어 `Entries`의 끝부분을 확인한다. 앱이 아직 이 PR 빌드가 아니거나 다음 예약 전이면 파일이 없는 것이 정상이다.

### 추가 보완: 1분 이내 리셋 흔들림 압축

- Mac이 보고한 Claude 기준 목록 상한 64개는 실제로는 두 리셋 주변의 약 1초 흔들림이었다. 단일 시각으로 반올림하면 빠른 조회 구간의 최초 진입이나 최종 종료를 줄일 수 있어, **전체 폭 1분 이내 묶음의 가장 이른 값·가장 늦은 값만** 보존한다. 완전히 같으면 하나만 남긴다. 인접 값의 간격만 보고 연쇄 병합하지 않으며 묶음 전체 폭은 1분을 넘지 않는다.
- 합성 예: 두 시각 주변 64개 → **두 묶음·네 끝점**. 각 묶음의 원래 리셋 ±15분 구간 합집합이 정확히 같아 최초 진입·최종 종료와 이전 리셋 +15분을 보존한다. 원래 최대 64개 제한과 timestamp 배열/캐시 JSON 형식은 유지하며 제한 때문에 끝점 쌍을 반만 남기지 않는다.
- **작은 예약 차이는 있다.** 이미 빠른 구간 안에서 중간 흔들림 값이 만들던 중복 진입 예약은 사라질 수 있어, 그 중간 예약 시각은 최대 1분 달라질 수 있다. 예를 들어 합성 리셋 `12:00:00/20/40`, 현재 `11:45:10`이면 다음 중복 진입은 `11:45:20` 대신 `11:45:40`이다. 빠른 구간 자체·최초 진입·최종 종료·기본/부족/실패 주기는 바뀌지 않는다.

### Windows·공통 검증

- SDK 10.0.402, 추가 보완 후 `build.ps1` 경고 0/오류 0, self-test **252,869건·ExitCode 0**. #63 최초 252,395건 대비 **+474**(진단 367 + 예약/압축/통합 107), #62의 252,149건 대비 **+720**이다. 공통 **252,562**, Windows 전용 **307 그대로**이며 별도 Shared.Tests도 **252,562건·ExitCode 0**이다. 기존 그룹 감소·중복 실행은 없다. 최초 보완 검사에서 잘못된 진단 파일의 `InvalidDataException`이 빠져나오는 오류를 발견해 예외 격리를 수정한 뒤 전체를 다시 실행했다.
- 새 `GeminiPollingPolicyTests`는 세 종류 0% 창의 경계·극소 양수·다른 Provider·혼합 사용·조회 지연·실패 재시도, 가짜 시계 7일×이동 300/17/10분(각각 29회·6시간 간격), 64개 구형 오염 기준과 첫 실패, 실제 사용→0% 뒤 새 캐시/재시작과 이전 +15분 만료, optional JSON/schema 호환을 확인한다. 가짜 CLI·합성 데이터·임시 DB만 쓰며 설치된 CLI·사용자 DB를 열지 않는다.
- Windows native smoke **1회 574 PASS·실패 0·실제 ExitCode 0(48.83초)**. 진단용 stderr는 기존처럼 보존했으며 비어 있다는 뜻이 아니다. #62 Windows 검증 소스와 같은 렌더 기준 대비 합성 PNG **19/20**, layout JSON **14/14 SHA-256 동일**다. 유일한 차이는 Gemini Provider 상세의 예외 안내로, 폭 640px 그대로·높이 **958→1022px**(192 DPI에서 두 줄 증가)다. 창·한도 행/막대 수·나머지 상태/통계/기록 화면은 같다. [변경 후 합성 상세](docs/reviews/gemini-idle-reset-polling-20261016/quota-tooltip-gemini.png).
- **진단·압축 보완 후 재검증:** native smoke 추가 **1회 574 PASS·실패 0·실제 ExitCode 0(49.03초)**. #63 최초 렌더 대비 합성 PNG **20/20**, layout JSON **14/14 SHA-256 동일**다. 이 보완에는 화면 변경이 없다. 새 검사는 예약 이유·선택 리셋·실패 재시도 우선·취소·저장 실패 격리·256건/128KiB·동시 쓰기/재시작·형식 오류, 1분 경계/연쇄 금지/구간 합집합/이전 구간 만료/캐시 재시작을 합성 응답·임시 DB로 확인한다.
- 검사 후 기존 **2.6.0 배포 EXE**를 다시 실행해 프로세스 응답 정상과 파일 SHA-256 불변을 확인했다. publish·자동 시작 등록/설정·앱 버전 변경은 하지 않았다. 일반 앱 재실행에 따른 원래 자체 조회·캐시 갱신은 사용자 DB 불변을 뜻하지 않는다.
- 로컬 증거는 ignored `artifacts/gemini-idle-reset-polling-20261016/`에 두며 추가 보완은 `diagnostics-followup/`의 build/self-test/shared-tests/smoke와 최초 실패 기록에 구분한다. 이 수정의 실제 장시간 자동 조회·운영 실행 수 감소 측정·실제 계정 조회는 **미수행**이다. CLI 자동 업데이트 준비 작업과 별개다.

### #63 Mac 확인 완료

- [최신 공통 변경 확인](https://github.com/hydron75/ai-burger-clock/pull/63#issuecomment-6207730733): `a1302e28066036ae5f45383038795d74281def7c`에서 공통 **252,562건 PASS**, `Mac/build.sh` 경고 0/오류 0, native smoke 실제 ExitCode 0. trimming/source-generated JSON을 사용하는 Mac Release에서 예약 이유·선택 리셋·시각·상한 256건/128KiB·1분 흔들림과 연속 이동 구간을 합성 응답·임시 DB로 확인했다.
- [Mac 검사·기록 추가](https://github.com/hydron75/ai-burger-clock/pull/63#issuecomment-6207898092): 최종 HEAD `4b3ea2b90dc43e15a8fb87b067e4d9ad38f38afe`는 Mac native smoke 시나리오·기록만 더했다. 공통 252,562건·빌드 경고/오류 0·smoke ExitCode 0을 다시 확인했고, 사용자 지시에 따라 이 Mac 전용 추가 때문에 Windows 재검사는 하지 않았다.
- **1시간 운영 관찰은 초기 HEAD `bf394a0`의 Mac 결과**다. 새 agy 로그 파일이 수정 전 1시간 약 12개에서 **1개**로 줄고 시작 뒤 추가 성공 조회가 없음을 확인했다. 로그 파일 수를 모든 agy 프로세스 수나 `/usage` 호출 수와 동일하다고 단정하지 않는다. 1시간은 6시간보다 짧으므로 정기 6시간 전체나 최신 진단/압축 코드의 장기 운영을 실측한 결과가 아니다.
- 최신 진단 파일의 실제 장시간 운영·Windows 운영 감소·실제 VoiceOver 음성/OS 배너는 미수행으로 남긴다. 실제 값·원문·캡처는 공개하지 않는다. 이 후속 배포 준비에서 공통 C#·Mac 파일을 다시 바꾸지 않는다.

## #30 구현·배포 당시 기록

**현재 상태(2026-10-09 KST):** #30은 Windows 재진단과 Mac 확인 뒤 사용자 승인으로 main에 병합됐고 #42의 Mac Gemini UI 후속도 병합됐다. #44 승인 뒤 main `019d3f3`에서 **Windows 2.3.0을 배포했다.** [2.3.0 배포 기록](MAINTENANCE_2_3_0.md)에 준비 self-test 251,377건·smoke 5회 모두 293 PASS/종료 코드 0과 agy **1.3.2 실제 조회(8.37초·두 창)**, 최종 단일 EXE 검사·해시·백업을 구분해 적었다. 최종 self-test 251,377건·smoke 293 PASS/종료 코드 0이며 새 트레이·상태 창·Gemini 값 정상 표시는 **사용자 확인**이다.

아래 절은 #30 구현 당시의 날짜·비교 기준·실행 결과다. 당시의 Mac 확인 요청·미수행 항목을 현재 상태로 읽지 않는다. 후속 Windows 진단·검사 전제 보완과 초기 오류 원인 미확정 관찰은 [BACKLOG](BACKLOG.md#관찰-windows-smoke의-뜻밖의-한도-모드-원인-미확정), [#30 재진단 결과](https://github.com/hydron75/ai-burger-clock/pull/30#issuecomment-6072308296), [Mac 확인](https://github.com/hydron75/ai-burger-clock/pull/30#issuecomment-6071703373)에 남긴다. smoke 진단은 현재 코드에도 유지한다.

초기 구현·검증일: **2026-10-08 KST**. 최신 main 통합·재검증일: **2026-10-09 KST**. 브랜치: `feature/gemini-agy-quotas`. 작업 전 main: `aa8dd50c2d6a66df951306f410537dbff42a525d`.

작업 중 main에 병합된 PR #29의 계획 문서 갱신만 fast-forward로 반영했다. PR base는 `612df8716e507733847204c5f3ce8214c6f1b357`이며 앱·검사 코드는 기존 비교 기준과 동일하다. 아래 비교 수치는 실제 실행한 `aa8dd50` 기준을 사용한다.

공통 코드와 Windows 한도 UI 변경이다. **이 구현 기록 작성 당시에는 Mac 검증·PR 병합 대기였으며 버전 변경·publish·dist 교체를 하지 않았다.** 아래는 당시의 구현 기록이고, 현재 상태는 상단과 별도 [2.3.0 배포 기록](MAINTENANCE_2_3_0.md)을 따른다.

## 조회 대상과 데이터

공식 Antigravity CLI `agy` **1.3.1 이상·2.0 미만의 안정 버전**에서 로컬 내장 명령을 사용한다. 이 PC의 설치 버전은 2026-10-09 KST에 **1.3.2**로 확인했다(`--version` 종료 코드 0). 버전 확인과 실제 계정 조회 성공은 별개다.

```text
agy --version
agy -p /usage --output-format json --print-timeout 20s
```

두 번째 명령은 버전 사전 확인이 성공한 경우에만 실행한다. 인수는 shell을 거치지 않고 `ProcessStartInfo.ArgumentList`로 전달한다.

| 응답 범위 | 표시 | 잔여량 | 리셋 |
|---|---|---|---|
| `command.data.groups`의 `Gemini Models` | Gemini / Antigravity · Gemini 모델 | 이 그룹만 사용 | 다른 모델 그룹과 합치지 않음 |
| `gemini-5h`, `window: 5h` | 5시간 | `remaining_fraction × 100` | `reset_time` ISO 8601 → UTC 저장 / KST 표시 |
| `gemini-weekly`, `window: weekly` | 주간 | 같은 방식 | 같은 방식 |

- Gemini Apps 웹·모바일의 전체 한도로 표시하지 않는다. Antigravity의 Claude/GPT 그룹과 크레딧도 제외한다.
- 사람이 읽는 `response` 문구를 재해석하지 않고 구조화된 `command.data`만 사용한다. 그룹 중복·알 수 없는 Gemini bucket·기간 불일치·잘못된 잔여율은 실패로 처리한다.
- 잔여율은 유한한 0~1 값이어야 한다. 한 창이 누락되어도 존재하는 창만 표시하며 그룹 전체 누락·빈 값은 조회 불가다. 리셋 시각의 누락·null은 미제공으로 표시하고 추정하지 않는다.
- 내부 `UsedPercent`로 변환하되 아주 작은 양수의 반올림 때문에 정확히 0% 잔여로 판정되지 않도록 처리했다. 화면 반올림과 조회 주기의 실제 0% 판정은 구분한다.
- CLI 응답 수신 시각과 서버 데이터 원래 생성 시각은 다르다. 공식 변경 기록의 `/usage` refresh 설명만으로 캐시 TTL·매번의 신선도·실시간 갱신을 보장하지 않는다.

## 실행 조건과 남은 제약

1. 설치·로그인된 공식 native `agy`를 사용한다. Windows는 절대 PATH 후보와 `%LOCALAPPDATA%/agy/bin/agy.exe`, Mac 공통 경로는 GUI PATH와 `~/.local/bin`, `/opt/homebrew/bin`, `/usr/local/bin` 후보를 사용한다. 앱이 설치·로그인·인증 갱신하지 않는다.
2. **1.3.1 이상·2.0 미만의 안정 버전**을 허용한다. 1.3.1 미만·2.x·배너·사전 배포/빌드 접미사·모호한 버전에는 `/usage`를 보내지 않는다. 지원 범위 안에서도 기존 JSON 구조·정상 종료·0턴/0토큰 검사를 모두 통과해야 결과를 채택한다. 오류 문구는 `agy CLI 1.3.1 이상이 필요합니다(2.0 미만 안정 버전).`이다. 모든 1.x 릴리스의 실제 계정 호환성을 미리 검증했다는 뜻은 아니다.
3. 자식 실행에만 공식 `AGY_CLI_DISABLE_AUTO_UPDATE=true`를 설정한다. 사용자 영구 환경·설정은 바꾸지 않는다. 표준 입력을 닫고 출력·오류를 동시에 읽는다. CLI print timeout은 20초, 버전 확인을 포함한 앱 deadline은 30초다. 종료·stdout/stderr 정리의 각 대기는 5초로 제한하지만 여러 정리 대기가 있으므로 전체 wall-clock을 30초 또는 35초 이내라고 보장하지 않는다. Monitor는 별도의 40초 취소 제한을 사용한다.
4. 인증 대기 표식을 발견하면 중단한다. 출력은 2 MiB·JSON 깊이 32 등으로 제한하며 시간 초과·취소 시 자신이 시작한 프로세스 트리만 종료한다. 원문 오류·OAuth URL·계정 정보는 화면이나 로그로 내보내지 않는다.
5. 정상 종료·`status: SUCCESS`·빈 `conversation_id`·`num_turns: 0`·`command.name: usage`를 확인한다. 필수 입력/출력/thinking/cache-read/total token 카운터는 모두 0이어야 하고 새 usage 카운터의 비영 값과 제공된 비용의 비영 값도 거부한다. 검증되지 않은 응답은 한도 캐시에 채택하지 않는다.
6. **설정 격리 제한:** 이 버전에서 실행별 사용자 hooks·MCP 제외 공식 옵션은 확인하지 못했다. 기존 설정을 읽거나 초기화할 수 있다는 제한을 설명했고 사용자가 자동 연결을 명시적으로 승인했다. 입력 닫기·제한시간·응답 검증이 hooks/MCP 초기화 자체를 막는 것은 아니다. 완전한 설정 격리·아무 부작용도 없음·모든 계정 실행의 절대 비소비로 확대하지 않는다.

앱은 인증 토큰·쿠키 파일을 직접 읽거나 비공개 usage HTTP를 호출하지 않는다. 리셋권 조회·사용, 결제·크레딧, 모델 작업 실행도 추가하지 않는다.

## 자동 조회와 캐시

`QuotaProvider.Gemini`를 기존 enum의 끝에 추가해 Codex/Claude의 저장 숫자 식별자를 유지했다. 기존 `AccountQuotaMonitor`의 Provider별 독립 루프·Refresh·시작·절전 복귀·연결 복구 경로에 연결한다.

- 기본 6시간 → 실제 잔여 0% 초과~10% 미만 1시간 → 정확히 0% 15분 → 리셋 ±15분 5분 중 가장 짧은 주기를 적용한다.
- 알려진 리셋 15분 전에는 긴 대기를 끝내고 진입하며, 이전 리셋의 +15분 구간도 보존한다. 기존 실패 재시도와 마지막 성공값 유지 규칙을 그대로 사용한다.
- 리셋 시각이 지났다고 100%를 가정하거나 한도 감소·회복만으로 리셋 원인을 확정하지 않는다. 회복 알림은 추가하지 않았다.
- 기존 SQLite **schema 2**를 유지한다. 정상 앱 실행에서는 `AppMetadata`의 `AccountQuota.v1.Gemini`에 정규화된 한도 정보만 저장한다. 전체 CLI 출력·계정 식별 정보·자격 증명은 저장하지 않는다.
- 이번 실제 조회 검증은 Gemini 클라이언트만 호출했고 사용자 DB를 읽거나 저장하지 않았다. 캐시·재시작은 임시 DB 검사로 확인했다.

## Windows UI

[MACOS_UI_PLAN.md](MACOS_UI_PLAN.md)의 결정 9/10을 따른다. Windows는 **상태 보기 ↔ 한도 보기**를 유지한다. 한도 제목은 바깥 기준선, 내용은 Provider별 흰 박스, 안쪽 글자 기준선은 기존 Provider 카드와 같다. 클릭할 수 없는 한도 박스에는 hover·메뉴를 추가하지 않았다.

기존 창 크기 374×518 logical px와 한도 viewport 342×214를 유지한다. 세 Provider가 늘어난 만큼 **한도 영역만 세로 스크롤**하며 가로 스크롤이나 창 전체 스크롤은 없다. 같은 행 구성의 매초 갱신은 컨트롤과 스크롤 위치를 보존한다. 범위 설명·정확한 KST 리셋·최근 성공/시도·다음 조회·실패 이유는 Tooltip에 남긴다.

## #30 main 통합 당시 Windows 검증 (2026-10-09 KST·과거 결과)

Git 비교 기준은 `main @ e9504bc3e119bec4faf3ebb2c2cef40147701964`(#41 포함)이다. 기존 PR HEAD `14cef69de9350803158da39090635167a799ae05`에 main을 merge했고, 검증한 C# 소스는 통합 커밋 `243468d337d2d02a2447c11abd02c5241fb767ce`와 같다. 후속 커밋은 검증 기록·PNG만 추가한다. main의 공통 검사 분리·StatisticsText·StatusTicker·Provider별 박스·메모 잘림 안내를 보존했다. Mac 전용 파일·AGENTS·계획·버전 파일은 main 대비 변경하지 않았다.

SDK **10.0.401**. 두 소스에서 `build.ps1`을 실행해 각각 **경고 0 / 오류 0**, 실제 WinExe `--self-test` 종료 코드 **0**을 확인했다. `Start-Process -Wait -PassThru`의 실제 ExitCode를 검사하는 스크립트 경로를 사용했다.

| 검사 그룹 | 최신 main | 통합 후 | 차이 |
|---|---:|---:|---:|
| Gemini 한도 파서/정책 | 0 | 85 | +85 |
| Gemini CLI 보호/버전/프로토콜 | 0 | 78 | +78 |
| 한도 Monitor/재시작/취소 | 80 | 98 | +18 |
| 경로/IANA/표시/CLI 후보 | 37 | 39 | +2 |
| PanelModel 문구/톤 | 249 | 261 | +12 |
| 나머지 공통 검사 | 250,509 | 250,509 | 0 |
| **공통 검사 합계** | **250,875** | **251,070** | **+195** |
| **Windows 전용 검사 합계** | **307** | **307** | **0** |
| **Windows self-test 전체** | **251,182** | **251,377** | **+195** |

감소·중복 실행한 그룹은 없다. Windows 전용 307건은 자동 시작 판정 229 + 트레이 픽셀 44 + CLI 경로 3 + 경고 로그 31이다. 이전 #30의 +179에서 버전 허용·거부 검사 16건을 더해 +195다. OS 독립 `Shared.Tests`도 실제 실행하여 **251,070건 PASS**, 종료 코드 **0**을 확인했다.

### 창·트레이·조회 중 응답성

- 최신 main Release EXE의 `--smoke-test --report-directory ...`: **255 PASS**, 종료 코드 **0**.
- 통합 후 최종 Release EXE: **278 PASS**, 종료 코드 **0**. 임시 DB·가짜 HTTP/CLI·주입 시계만 사용했으며 `--verify-autostart`는 사용하지 않았다.
- 실제 Windows 메시지 루프에서 Gemini 가짜 응답만 **10초** 지연했다. 트레이 Refresh handler가 즉시 반환했고, 대기 중 native 메뉴 표시·상태 창 닫기/재열기가 정상 동작했다.
- 최종 측정 **10.04초**, UI heartbeat **49회**, 실제 트레이 Tooltip/상태 카운트다운 각각 **12종**으로 갱신됐다. 다른 두 Provider는 독립 완료했고 Gemini 중복 조회는 없었다. 이 검사는 **합성 지연의 응답성**이며 설치된 agy 1.3.2의 실제 계정 조회는 이번에 실행하지 않았다.
- 초기 상태 하단 문구 검사 오류는 **2회**다. 뜻밖의 한도 모드가 기록된 것은 두 번째 **1회**이며 첫 번째 모드는 미기록이다. 당시 Click 경로도 없어 원인은 **미확정**이다. 당시 임시 호출 추적을 추가한 검사와 이를 제거한 검사 모두 통과했으며, 이후 별도의 정식 smoke 진단과 숨긴 창 클릭 전제 보완을 커밋했다. 그 보완을 이 초기 모드 오류의 확정 원인으로 적지 않으며 제품 상태 모드·알림 코드는 변경하지 않았다.
- BalloonTipShown **17건** 관측은 시각적 알림 전달 보장이 아니다.

### 전후 PNG 비교

동일 시각·가짜 데이터·DPI로 생성한 원본 PNG를 열어 확인하고 SHA-256 및 픽셀 차이를 비교했다.

- **7개 SHA-256 동일:** `burger-time`, `full-throttle`, `holiday-midweek`, `holiday-weekend`, `provider-outage`, `note-truncated`, `account-quotas-exhausted`.
- **3개 의도한 변경:** `account-quotas`, `account-quotas-previous`, `account-quotas-note-truncated`. 세 Provider로 늘어나 한도 영역 안에 세로 스크롤바가 생긴다. 창 374×518 / 한도 viewport 342×214 logical px, 제목 기준선·박스 패딩·상태 전환은 유지했다.
- **2개 포커스 상태 차이:** `statistics`는 183픽셀, (49,238)~(199,271)의 탭 점선 테두리만 다르고 `measurement-dialog`는 652픽셀, (46,91)~(665,124)의 선택 상자 점선 테두리만 다르다. 내용·수치·배치는 같고 해당 두 제품 화면 코드는 main과 동일하다. 원본 PNG가 모두 해시 동일하다고 기록하지 않는다.
- 추가 `account-quotas-two-providers.png`는 latest main의 `account-quotas.png`와 SHA-256까지 동일하다. `account-quotas-gemini`·`account-quotas-gemini-previous`는 Gemini 박스 정상/이전 값과 마지막 행에 접근하는 스크롤을 확인한다.
- 전후 첨부: [before](docs/reviews/gemini-agy-refresh-20261009/before), [after](docs/reviews/gemini-agy-refresh-20261009/after). 모두 합성 데이터이며 실제 계정 수치·원문·EXE·DB를 올리지 않는다.
- 최종 로그·PNG: ignored `artifacts/gemini-agy-refresh-20261009/{baseline,after}/`. 초기 구현 로그는 아래 과거 기록과 별개다.

### 배포본 보존·미수행

기존 2.2.4 EXE는 정상 종료 뒤 검사를 진행했으며 검사 후 같은 파일을 다시 실행했다. FileVersion **2.2.4.0**, SHA-256 **AF255AE4AD8642F149D07AFB6E819CC18CE3EBD76437182A4413A3204F62C06B**는 작업 전후 동일하다. 배포 EXE·사용자 DB·자동 시작 등록·공개 조사 자동화는 변경하지 않았다.

**이번 미수행:** agy 1.3.2 실제 계정 조회, Mac build/native smoke·팝오버 Gemini 박스, 실제 네트워크 전체 단절·재연결·절전·재부팅·재로그인, 장시간 자동 조회, hooks/MCP·모든 계정/플랜 조합, 시각적 알림 전달. 사용자 Mac에서 동일 PR HEAD의 agy **1.3.2 실제 조회와 팝오버 Gemini 박스**를 확인받기 전에는 병합하지 않는다.

## 초기 Windows 검증 (2026-10-08 KST·과거 결과)

SDK **10.0.401**. 기준 main과 변경 소스에서 `build.ps1`을 실행했다. 두 빌드 모두 **경고 0 / 오류 0**. WinExe 자체 검사·smoke는 `Start-Process -Wait -PassThru`의 실제 `ExitCode`로 확인했다.

| 검사 그룹 | 기준 main | 변경 후 | 차이 |
|---|---:|---:|---:|
| 자동 시작 판정 | 229 | 229 | 0 |
| Schedule/DST | 242,363 | 242,363 | 0 |
| US holidays | 1,399 | 1,399 | 0 |
| 공식 상태 파서 | 96 | 96 | 0 |
| Monitor/알림 정책 | 119 | 119 | 0 |
| SQLite/통계 | 132 | 132 | 0 |
| 공휴일 캐시 | 2 | 2 | 0 |
| 기존 한도 파서/정책 | 143 | 143 | 0 |
| Gemini 파서 | 0 | 85 | +85 |
| Gemini CLI 보호/프로토콜 (합성) | 0 | 62 | +62 |
| 한도 Monitor/캐시/재시작 | 80 | 98 | +18 |
| 경로/표시/CLI 후보 | 37 | 39 | +2 |
| 공통 패널 문구/톤 | 247 | 259 | +12 |
| 기록/피드백 | 40 | 40 | 0 |
| 트레이 | 6,130 | 6,130 | 0 |
| 기존 CLI 프로토콜 | 49 | 49 | 0 |
| **Windows 전체** | **251,066** | **251,245** | **+179** |

- Windows 자체 검사 종료 코드 **0**, 감소한 그룹 없음. 공통 검사 전체 **244,837건** 통과했다. 목록은 `SharedTestSuite` 한 곳에만 등록했다.
- Windows native smoke: **241 assertions**, 종료 코드 **0** (기준 220, +21). 상태·통계·한도·기록·공휴일·복귀·누락 CLI·이전 값·스크롤·리소스 정리를 확인했다. 자동 시작 변경 옵션은 사용하지 않았다.
- BalloonTipShown 이벤트 17건을 관측했다. 이는 실제 알림이 사용자에게 시각적으로 노출됐다는 보장은 아니다.
- PNG SHA-256 비교: `burger-time`, `full-throttle`, `holiday-midweek`, `holiday-weekend`, `measurement-dialog`, `provider-outage`, `statistics` **7개 동일**. `account-quotas`, `account-quotas-previous`, `account-quotas-exhausted` **3개만 의도한 박스 배치 변경**. `account-quotas-gemini`, `account-quotas-gemini-previous` **2개 추가**해 정상·이전 값을 직접 확인했다.
- 실제 설치된 agy **1.3.1**: 새 Release DLL의 Gemini `ReadAsync`만 호출해 **5시간·주간 두 창**, 정상 종료와 위 0턴/0토큰 조건 통과를 확인했다. 약 9.57초 소요, 응답 수신 `2026-10-08T13:39:53Z`. 실제 잔여율·계정 정보는 저장소에 올리지 않는다. 다른 Provider 계정 조회·사용자 DB 저장은 하지 않았다.
- 로그·PNG는 로컬 ignored `artifacts/gemini-agy-20261008/baseline/`, `after/`에 있다. 실계정 probe도 ignored artifact 안에만 있으며 앱 의존성을 추가하지 않았다.

**미수행:** Mac 빌드·native smoke, 실제 네트워크 전체 단절·재연결, 실제 절전·재부팅·재로그인, 새 앱의 장시간 자동 조회, 실제 hooks/MCP 설정의 모든 조합, 모든 플랜·계정, 새 AGY 버전, 알림의 시각적 전달. 모델 소비를 유도하는 검사는 하지 않았다.

## 변경 파일과 Mac 확인 요청

- 공통 원본: `AccountQuotaModels.cs`, `AccountQuotaClient.cs`, `AccountQuotaParsers.cs`, `AccountQuotaMonitor.cs`, `MacCliPaths.cs`, `QuotaPanelModel.cs`, `UsageStore.cs`(주석만).
- 공통 검사: `GeminiQuotaTests.cs`, `GeminiQuotaClientTests.cs` 신규; `AccountQuotaMonitorTests.cs`, `PortablePlatformTests.cs`, `PanelModelTests.cs`, `SharedTestSuite.cs` 수정.
- Windows 전용: `AccountQuotaView.cs`, `AccountQuotaUiChecks.cs`, `SmokeTest.cs`, `UIRegressionChecks.cs`(세 박스 통합 검사), `TrayApplicationContext.cs`(Refresh Tooltip 범위 설명).
- 문서: 이 문서와 `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`, `ACCOUNT_QUOTAS.md`.
- `Mac/`, `MACOS_PORT.md`, `MACOS_UI_PLAN.md`, `AGENTS.md`, 버전·의존성·dist는 수정하지 않았다.

공통 원본 변경이므로 동일 PR HEAD에서 Mac 담당의 `Mac/build.sh`와 native `--smoke-test` 결과를 PR 코멘트로 받기 전에는 병합하지 않는다. 확인할 항목:

1. 최신 PR HEAD에서 공통 검사 **251,070건**과 Mac Release/trimming 빌드, 기존 native smoke 통과 여부. 과거 244,837건은 이번 확인 기준이 아니다.
2. Mac의 **한도 항상 표시** 방식을 유지한 세 Provider 박스·Gemini 범위 문구·정상/이전/누락 한도·스크롤·짧은 화면 배치.
3. 기존 Claude/Codex 응답과 캐시 식별자 호환, Gemini 추가 캐시의 재시작·Provider별 실패 격리.
4. 설치·로그인된 **agy 1.3.2로 실제 독립 조회**하고 5시간·주간 응답·정상 종료·0턴/0토큰 조건을 확인한다. 1.3.1 이상·2.0 미만 안정 버전의 절대 경로, 1.3.1 미만·2.x·미설치·인증 필요 시 안전한 조회 불가 처리도 확인한다. raw 응답·계정 수치는 공개하지 않는다. 사용자 hooks/MCP 격리 제한은 Mac에도 같다.
5. 실제 Mac 계정 확인은 별도 사용자 허용 조건에서만 수행하고, raw 출력·OAuth 정보는 PR에 붙이지 않는다. 공통 자동 정책과 Mac Refresh·절전 복귀·연결 복구 연결도 구분해서 기록한다.

## 공식 근거

- [공식 `/usage` 명령](https://antigravity.google/docs/cli/commands/usage/), [Headless 설명](https://antigravity.google/docs/cli/headless/).
- [1.3.1 고정 공식 CHANGELOG](https://github.com/google-antigravity/antigravity-cli/blob/1.3.1/CHANGELOG.md): 1.1.11의 구조화된 로컬 `/usage`·모델 턴/대화 생성 없는 처리, 1.0.1의 usage refresh, 후속 timeout·headless 정리 변경. 공개 변경 기록과 이번 1.3.1 실제 계정 검증일은 구분한다.
- [1.3.2 고정 공식 CHANGELOG](https://github.com/google-antigravity/antigravity-cli/blob/1.3.2/CHANGELOG.md): 새 버전의 공개 기록은 실제 계정 조회 성공·독립 최신 데이터 보장과 구별한다.
- [공식 troubleshooting](https://antigravity.google/docs/cli/troubleshooting/): 자동 업데이트 차단 환경 변수.
- [플랜별 Antigravity 범위](https://antigravity.google/docs/plans/), [hooks](https://antigravity.google/docs/hooks), [MCP](https://antigravity.google/docs/mcp), [CLI settings](https://antigravity.google/docs/settings?tab=cli).
