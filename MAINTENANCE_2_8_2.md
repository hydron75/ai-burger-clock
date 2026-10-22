# AI Burger Clock 2.8.2 — Windows 한도 토스트 배포 기록

기록일: **2026-10-22 KST**. #86 병합 뒤 main을 `git pull --ff-only`로 받고 `codex/windows-release-2-8-2`에서 버전·기록을 올렸다. 범위는 **Windows만 변경(버전·문서)**이다. #86의 Windows 구현을 포함하며 이번 버전 PR에서 공통·Windows C#·검사·Mac 코드를 다시 바꾸지 않았다. 지연 생성은 BACKLOG 성능 후보로만 기록했다.

사용자가 **PR 병합 전 백업·검사·EXE 교체·운영 등록·새 버전 실행까지 승인**했다. 따라서 실제 EXE 소스는 아래 **미병합 버전 브랜치 커밋**이며 병합 main이라고 적지 않는다. PR 병합은 사용자가 한다. 실제 EXE 소스와 이후 결과 문서 커밋은 구분한다. **20:25 KST 교체, 20:27 KST 정상 실행 확인**을 마쳤으며 트레이·상태 창·한도 화면 정상은 **사용자 확인**이다. 시험 알림은 보내지 않았다.

## 2.8.1 이후 포함 변경

| 출처 | 2.8.2에 포함한 내용 |
|---|---|
| #86 | 한도 회복·주간 조기 초기화만 알림 센터 보관용 Windows 토스트로 전환. 시간표·공식 상태 권고·공휴일 안내는 기존 풍선 유지 |
| #86 | 고정 AUMID·COM CLSID와 시작 메뉴 바로가기의 명시적 등록/해제·소유권 검사·dry-run. 일반 실행이 영구 등록을 자동 생성/복구하지 않음 |
| #86 | COM 전용 실행은 정상 mutex·트레이·조회·DB·자동 시작을 거치지 않고 무동작 콜백만 처리한 뒤 종료 |
| #86 | 등록/동기 제출 실패만 풍선 1회 대체. 확인 가능한 Windows 사용자/정책 차단은 우회하지 않고, 비동기 실패는 진단만 기록 |
| #86 | `logs/quota-notification-delivery-v1.jsonl`: 시각·종류·Provider·방식·결과·허용된 사유/HRESULT만 기록, 최근 256건/128KiB 제한. 본문·계정 값·원문 오류 제외 |
| #86 | Windows self-test/smoke에 가짜 풍선/토스트 전송기를 주입해 검사 알림 누적 방지. 실제 통합 시험은 별도 시험 ID에서만 수행 |

알림 문구·조건·독립 기본 꺼짐·기존 사용자 설정·Provider별 대기열·통합 알림·6.5초 간격·조회 정책·UI 크기·schema 2·캐시 v1은 유지한다. 등록은 알림 켜기 설정과 별개다. 발송 로그의 `Submitted`는 API 제출 성공이지 실제 배너 표시·열람·센터 보관 증명이 아니다. [등록·발송·해제·검사 계약](WINDOWS_QUOTA_NOTIFICATIONS.md).

## Git 기준점과 EXE

| 기준 | 전체 커밋 |
|---|---|
| 이전 2.8.1 실제 EXE 소스 | `d0cc64eb8004e37c39ecafb5afbaace47ef41076` |
| #86 병합 / 이번 출발 main | `d7c52b875ef9ee9c44e9b46100de428f553b3370` |
| 2.8.2 버전 / **실제 EXE 소스** | **`ea4d6b7d01ed19343fdeb2f0504c44a031be588d`** |
| 2.8.2 버전 PR 병합 | 미병합, 사용자 결정 |

| 최종 단일 EXE 항목 | 결과 |
|---|---|
| 실제 경로 | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion / AssemblyVersion | **2.8.2.0 / 2.8.2.0** |
| ProductVersion | **`2.8.2+ea4d6b7d01ed19343fdeb2f0504c44a031be588d`** |
| 크기 | **29,558,715 bytes (28.19 MiB)** |
| SHA-256 | **`88D440CF0C1117F9352A0895A47AF7D0270AE0DABCF08A8E70DE5BC3023AFF06`** |
| 이전 2.8.1 SHA-256 | `4BF374BB6B62867B50FB5AAC84A0EA0C647D68CABC30F628DB508F856DDD3DC2` |

framework-dependent 단일 EXE를 유지하며 설치된 .NET 10 Desktop Runtime x64를 사용한다. 별도 Windows App SDK 런타임·알림 Toolkit 패키지·설치 프로그램은 추가하지 않았다. Windows SDK 참조를 위해 TFM은 #86에서 `net10.0-windows10.0.19041.0`으로 바뀌었다.

## 이번 버전의 실제 검사

SDK **10.0.402**, Desktop Runtime **10.0.13 x64**. WinExe는 `Start-Process -Wait -PassThru`의 실제 ExitCode로 확인했다. 합성 응답·임시 DB만 사용하며 실제 계정 조회 명령이나 시험 알림을 실행하지 않았다.

| 항목 | 준비 bin | 배포할 Portable 단일 EXE |
|---|---|---|
| 빌드/게시 | `build.ps1 -ReportGroup local-release-2-8-2`, 경고 0 / 오류 0 | 별도 artifacts 경로로 Portable publish, 경고 0 / 오류 0·ExitCode 0 |
| self-test | **255,761건·ExitCode 0** | **255,761건·ExitCode 0** |
| 공통 / Windows 전용 | **255,062 / 699** | **255,062 / 699** |
| 고정 after 슬롯 native smoke | **1회 837 PASS·ExitCode 0, 53.80초** | **별도 1회 837 PASS·ExitCode 0, 52.21초** |
| 합성 화면 | PNG 22개 생성 | #86 최종 단일 EXE 대비 **PNG 22/22·layout JSON 16/16 SHA-256 동일** |
| 슬롯 트레이 기록 | 해당 슬롯 1개·새 키 0개 | 해당 슬롯 1개·새 키 0개 |

이번 버전 PR 자체의 검사 증감은 0이다. 2.8.1 대비 self-test **+392**는 #86의 Windows 전송/로그 323건 + 토스트/등록/COM/차단 69건이며 공통 검사는 같다. smoke **830→837(+7)**도 #86의 가짜 전송 경로 확인이다. 기존 symlink fixture 하나는 임시 파일시스템 링크 생성 제한으로 명시적 SKIP이며 통과로 세지 않는다.

배포 파일을 먼저 덮어쓰는 `build.ps1 -Publish` 대신, 이번에는 동일 Portable 프로필을 별도 후보 경로로 publish했다. **후보 self-test·단일 EXE smoke가 끝난 뒤** 해시가 같은 파일을 dist에 복사했다. 새 대기 시간·제품/검사 코드 변경·검사 반복 확대는 하지 않았다.

## #86에서 측정한 크기·시작·메모리

아래는 **이번 2.8.2에서 다시 측정한 값이 아니라** #86 병합 전 실제 2.8.1 복사본과 #86 단일 EXE의 비교다. 같은 PC·10.0.13 x64·고정 before/after 슬롯·동일한 외부 계측 hook·합성 트레이 경로를 사용했다. 워밍업 각 1회 뒤 각 5회를 번갈아 실행했고 강제 GC/작업 집합 비우기는 하지 않았다. 시작은 프로세스 생성부터 첫 UI Idle까지, 메모리는 합성 초기 조회 완료 30~44초의 1초 간격 15개 샘플 중앙값을 구한 뒤 회차별 중앙값 5개의 중앙값이다.

| 항목 | 2.8.1 | #86 | 차이 |
|---|---:|---:|---:|
| 첫 UI Idle | 343.97 ms | 370.89 ms | +26.92 ms (+7.8%) |
| 안정 구간 작업 집합 | 79.96 MiB | 88.61 MiB | +8.65 MiB (+10.8%) |
| Private Bytes (별도 보조 지표) | 25.39 MiB | 25.84 MiB | 반올림 전 +0.44 MiB |
| publish EXE | 4,076,779 bytes / 3.89 MiB | 29,558,715 bytes / 28.19 MiB | SDK projection 포함 |

실제 계정 응답 대기·첫 가시 프레임·cold start·운영 등록 후 COM 서버 상주 비용은 제외한 합성 비교다. 계측 비용은 양쪽에 공통으로 포함된다. 전체 작업 집합과 전용 커밋량인 Private Bytes는 다른 지표이며 증가를 전부 전용 힙/누수로 해석하지 않는다. 추가 로드 어셈블리는 `Microsoft.Windows.SDK.NET`, `WinRT.Runtime`이지만 페이지별 비용 귀속은 측정하지 않았다. [측정 코멘트](https://github.com/hydron75/ai-burger-clock/pull/86#issuecomment-6339275867).

**토스트 backend 첫 발송 시 지연 생성**은 [BACKLOG 성능 후보](BACKLOG.md#성능-최적화-후보-보류)로만 남긴다. 예상 절감량은 미검증이고 첫 발송 비용·COM 수명·풍선 대체 회귀를 검토해야 한다. 이 방법만으로 EXE 파일 크기가 줄어들지는 않는다. 이번 배포에는 구현하지 않았다.

## 승인된 백업 → 검사 → 교체 → 등록 → 실행

1. Computer Use에서 앱의 창·트레이 메뉴가 노출되지 않아 사용자가 2.8.1을 정상 종료했다. 실행 프로세스 0개·알려진 DB의 WAL/SHM 부재를 확인했다. DB는 요청 경로와 실제 열린 handle 경로를 대조해 현재 앱의 캐시 뷰를 구별했으며 행 내용은 출력하지 않았다.
2. **`../backups/release-2.8.2-20261022-201922/`**에 `replaced-2.8.1.exe`, `dist-2.8.1.zip`, 이전/신규 EXE 소스 ZIP, 종료한 현재 DB·로그·비공개 manifest를 보존했다. EXE 복사/ZIP 안 EXE와 DB 복사 해시를 대조했다. 기존 백업·DB·sidecar는 삭제하거나 덮어쓰지 않았다.
3. 준비 build/self-test/smoke와 Portable 단일 EXE self-test/smoke를 통과한 뒤, 앱 없음·DB 해시 불변·WAL/SHM 부재·자동 시작 값/종류/승인 바이트 불변·이전 EXE/백업 해시를 재확인했다. **20:25 KST** 검증된 후보를 실제 dist에 복사하고 버전·전체 소스·크기·SHA-256을 대조했다. 새 EXE도 백업의 `published-2.8.2.exe`로 보존해 같은 해시를 확인했다.
4. #86에서 확인한 HKCU 뷰 차이를 피하기 위해 **사용자가 Codex 밖 PowerShell에서 해시 고정 도우미를 실행**했다. 교체된 실제 dist EXE의 `--register-quota-notifications`를 **1회·ExitCode 0**으로 실행했다. 아래 운영 등록과 해제 dry-run을 같은 외부 Windows 뷰에서 확인했다. 시험 알림·조회·정상 앱 실행은 그 도우미에서 하지 않았다.
5. 도구의 정상 실행 컨텍스트에서도 외부 운영 AUMID/COM 값과 실제 dist 대상이 보이는지 읽기 전용으로 확인했다. 현재 DB 경로/기존 설정을 유지하도록 종전 실행 컨텍스트에서 같은 dist EXE를 `--autostart`로 시작했다. **20:27 KST**, 정확한 경로·2.8.2.0·해시·단일 정상 인스턴스·UI idle·Responding=True와 자동 시작 등록 불변을 확인했다. 정상 시작에 따른 평소 조회·캐시 쓰기는 실행 전 DB 불변 확인과 구분한다.
6. Computer Use 목록에는 실제 창이 계속 노출되지 않아 사용자가 **트레이·상태 창·한도 화면 정상**을 확인했다. 운영 토스트의 시험 발송/센터 보관/클릭은 요청에 따라 이번에는 시험하지 않았다.

현재 실행은 테스트 슬롯이 아니라 **실제 2.8.2 dist 배포본**이다. HKCU Run의 큰따옴표 절대 dist 경로 + ` --autostart`는 같은 위치의 새 EXE를 가리키며 Run·StartupApproved를 다시 쓰지 않았다. 알림 켜기/끄기 설정도 변경하지 않았다.

## 운영 등록과 해제 dry-run 결과

| 항목 | 외부 Windows 확인 결과 |
|---|---|
| 등록 EXE | 위 SHA-256의 실제 `dist/win-x64/AI Burger Clock.exe`, FileVersion 2.8.2.0 |
| 등록 명령 | `--register-quota-notifications`, **1회·실제 ExitCode 0** |
| 시작 메뉴 바로가기 | 사용자 Programs의 `AI Burger Clock - quota notifications.lnk` |
| 바로가기 대상/인자/작업 폴더 | 실제 dist EXE / 빈 인자 / 해당 win-x64 폴더, 모두 일치 |
| 바로가기 AUMID | `Hydron75.AIBurgerClock.Quota`, 일치 |
| 바로가기 activator CLSID | `{10B09E94-A6B4-4ABA-9B9F-C1551C7A409B}`, 일치 |
| HKCU AUMID/CLSID 소유권·실행 대상 | `AiBurgerClock.QuotaToast.v1` / 같은 실제 dist EXE, 모두 일치 |
| COM LocalServer32 | 큰따옴표 실제 dist EXE 경로 + ` --quota-toast-activated`, 일치 |
| 해제 dry-run | `--unregister-quota-notifications --dry-run`, **실제 ExitCode 0** |
| dry-run 확인 | **`Dry run: no registration changed.`**, 바로가기 SHA-256·대상 속성·레지스트리 값 전후 동일 |
| 하지 않은 것 | 실제 해제·시험 알림·설정/자동 시작 변경·알림 이력 삭제 |

일반 앱 실행이 이 등록을 만들거나 복구하지 않는다. 등록과 자동 시작은 별개이며 현재 사용자 범위만 사용한다. 운영 등록 결과만으로 실제 배너·알림 센터 보관 성공을 주장하지 않는다.

### 해제 방법

앱을 정상 종료한 뒤 **Codex 밖 PowerShell**에서 실제 배포 EXE로 실행한다. 첫 명령은 위에서 확인한 읽기 전용 dry-run이고 둘째가 실제 해제다. **이번에는 둘째 명령을 실행하지 않았다.**

~~~powershell
& '<저장소>/dist/win-x64/AI Burger Clock.exe' --unregister-quota-notifications --dry-run
& '<저장소>/dist/win-x64/AI Burger Clock.exe' --unregister-quota-notifications
~~~

올바른 실제 배포 경로·소유권이 확인된 전용 바로가기·AUMID·CLSID만 해제한다. 소유권/경로 불일치면 중단한다. 다른 앱·기존 알림·사용자 알림 설정·자동 시작·DB·발송 로그는 지우지 않고 운영 History.Clear도 호출하지 않는다. 등록 해제 후 일반 재실행이 등록을 재생성하지 않으며, 토스트를 다시 쓰려면 동일 실제 배포 EXE에서 등록 명령을 명시적으로 실행한다.

## 2.8.1 EXE만 복원하는 계획

1. 2.8.2를 정상 종료하고 새 DB·로그를 별도로 보존한다. DB를 이전 백업으로 되돌리지 않는다.
2. **아직 dist에 2.8.2 EXE가 있을 때** 위 실제 운영 등록을 해제한다. 2.8.1에는 이 명령이 없으므로 EXE를 먼저 바꾸면 안 된다. 소유권/경로 검증이 실패하면 무리하게 키를 지우지 말고 중단·보고한다.
3. 위 백업의 `replaced-2.8.1.exe`만 같은 dist 경로로 복원하고 **FileVersion 2.8.1.0 / SHA-256 `4BF374BB6B62867B50FB5AAC84A0EA0C647D68CABC30F628DB508F856DDD3DC2`**를 확인한다.
4. 같은 `--autostart` 인수로 다시 실행한다. 자동 시작·DB·캐시·새 진단 파일·사용자 설정은 그대로 두며 한도 알림은 기존 풍선 전송으로 돌아간다. 실제 복원은 수행하지 않았다.

## 근거 위치와 미수행

비공개 로컬 근거: 준비 `artifacts/local-release-2-8-2/build/20261022-201924-010/`, 준비 smoke `artifacts/local-release-2-8-2/windows-tests/after/20261022-202044-391-783ac4/`, 최종 smoke `artifacts/local-release-2-8-2-final/windows-tests/after/20261022-202307-116-545d53/`, publish·설치·외부 등록·dry-run·시작 결과 `artifacts/release-2.8.2-deployment-20261022/`다. 백업에는 설치/등록/시작 결과도 함께 보존한다. DB·원문 로그·개인 경로가 있는 상세 manifest는 커밋하지 않는다.

- 실제 운영 토스트 발송·센터 보관·종료 후 클릭은 **이번에 실행하지 않음**. #86 시험 ID의 기존 검증과 구분하며 사용자의 시험 알림 금지를 지켰다.
- 재부팅·로그인/로그아웃·절전·실제 Windows 권한 차단·장기 운영·복원 시험은 하지 않았다.
- 등록 후 COM 서버를 포함한 시작/메모리는 재측정하지 않았다. 위 #86 측정은 운영 미등록 조건이다.
- Windows 버전·문서 외의 제품/검사 동작이나 Mac 버전·코드는 변경하지 않았다. 과거 검증·배포 기록은 유지한다.
