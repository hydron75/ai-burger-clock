# AI Burger Clock 2.9.4 — Gemini 한도 자동 조회 중단

2026-11-16 KST. 기준 main `5fbfc6c7b1872ace712871710a3d42b464682442`(#156 병합).
#160은 Windows Version/AssemblyVersion/FileVersion과 문서만 변경했다. 공통/Windows C#·Mac·패키지·배포 스크립트는 바꾸지 않았다.
PR 생성 중 main에 #158이 병합되어 `3ec83eb29808d4c8dac89cdb64e06d78fa509605`가 됐다. 추가 차이는 Mac 버전 필드·Mac/README 두 파일뿐이며 Windows/공통 원본 차이는 0건이다. 검증한 후보·receipt를 유지하고 새 main과 병합 가능함을 확인했다.

## 2026-11-16 외부 배포 완료 · 사용자 확인

- #160 병합을 GitHub에서 확인했다. 병합 커밋은 `c30635fe7b2ad230a12549312640ef50f46d4ee2`이며 이 문서 PR은 해당 main에서 시작했다.
- 사용자가 Codex 밖 PowerShell에서 읽기 전용 Git 사전 확인과 외부 배포를 실행했고, 두 절차 모두 통과했다고 알려 주었다. Codex가 운영 앱을 실행하거나 배포를 대신 수행한 결과가 아니다.
- **Windows 사용자 확인:** 2.9.4 트레이·상태 보기·한도 보기, Gemini 조회 안 함 안내와 ChatGPT·Claude·xAI가 모두 정상이다. 새 버전 배포 전까지 앱을 종료 유지하던 단계는 완료됐다.
- 이 기록은 사용자의 통과·화면 정상 보고를 근거로 한다. 이번 문서 PR에서 운영 EXE 해시·개별 검사 값·DB 경로를 재조회하거나 빌드·배포·운영 앱 실행·등록을 수행하지 않았다. 아래 후보 신원·검증 기록은 그대로 보존한다.
- 실제 계정 값·토큰·사이트 주소·캡처는 기록하지 않는다. 기존 사이트 버전 17의 아이폰 사용자 확인도 아래에 유지한다.

## 2.9.3 이후 포함 변경

- #155: Windows 화면 검사의 독립 합성 host·DPI/레이아웃 완료·부모 범위 hit-test·실패 순간 진단. 다른 앱 제목/본문은 기록하지 않는다.
- #156/#157: agy 실행·버전·프로토콜·후보 탐색과 미사용 Gemini 파서를 제거한다. 시작/예약/Refresh/복귀/네트워크 조회를 차단하고 Gemini 한도는 안내만 표시한다. 짧은 4줄 상세, 기존 cache 보존·사용 안 함, 회복/조기 초기화 차단, enum/송신 v2 Provider 4개 유지와 Gemini none/null/null/[]를 포함한다. 공식 Gemini 상태 카드/장애 알림은 유지한다.
- 정책 근거와 당시 검증은 [Gemini 기록](GEMINI_ANTIGRAVITY_QUOTAS.md), 검사 계약은 [화면 검사 기록](WINDOWS_SMOKE_DIAGNOSTICS.md)을 따른다.

## 사이트 선행 배포

- 기존 소유자 전용 범위를 유지해 버전 **17**, 소스 `3d8bd0bf6d6bd9eec97038a72e434defb148275e`, 기준 표시 `5fbfc6c`로 비공개 배포했다. 외부 공유는 추가하지 않았다.
- 최신 main 실제 C# 원본의 Gemini 16사례·다른 세 Provider 180사례가 일치했다. 사이트 test 89/89·typecheck·Worker build·ESM 검증 통과.
- 배포 HTML/JS/공식 상태 API HTTP 200, HTML/API 기준 커밋 일치, 배포 JS와 검증한 Worker 내장 JS의 SHA-256 일치를 확인했다. Gemini 안내·행/시각 없음과 다른 세 Provider 표시 규칙은 합성 입력으로 확인했다. `/api/quota`·실제 계정 값은 읽지 않았다.
- **아이폰 사용자 확인:** Gemini 박스가 조회 안 함 안내만 보이며 수치·막대가 없고, ChatGPT·Claude·xAI의 기존 한도 표시가 정상이다. 실제 값·캡처는 기록하지 않았다.

## 후보·검증·receipt

후보: `artifacts/release-2.9.4-preparation-20261116/candidate/AI Burger Clock.exe`.

| 항목 | 확인 결과 |
|---|---|
| SDK | 10.0.404 (`global.json` 적용) |
| 빌드 | `build.ps1 -ReportGroup r294`, 경고 0·오류 0 |
| self-test | 일반 빌드·후보 단일 EXE 각각 292,635 assertions, exit 0·stderr 0 bytes |
| 공통 검사 | self-test에 공통 291,495 + Windows 1,140 포함. symlink fixture 1건은 파일 시스템 제한으로 기존과 같은 SKIP |
| smoke | 고정 after 슬롯, 최종 후보 1회, 1,173 PASS·PNG 60·exit 0·59.91초. 재실행 없음 |
| 합성 화면 비교 | #156 최종 검증본 대비 PNG 60/60·레이아웃 JSON 41/41 SHA-256 일치 |
| 운영 영향 | 실제 CLI/계정 조회·사이트 전송·native toast 제출·등록 명령 0건. 운영 dist/DB 접근·EXE 교체·앱 실행 없음 |
| 트레이 기록 | 시험 슬롯 1개·새 키 0개. 1회이므로 반복 실행 간 기록 증가 없음은 미검증 |
| FileVersion | 2.9.4.0 |
| ProductVersion | `2.9.4+a0f5a4b812a8882db65e7627f2bd0dcb71edba5f` |
| EXE 소스 커밋 | `a0f5a4b812a8882db65e7627f2bd0dcb71edba5f` (이후 검증 기록만 별도 커밋) |
| 크기 | 29,857,723 bytes |
| SHA-256 | `E5173E20B9EBEB2333A4555ABF06BAD98E7BB0C8E2CB1B7E31467BF357F59BCC` |

최초 빌드는 NuGet 취약성 데이터 접속 실패(NU1900)로 중단됐다. 공식 NuGet 연결에서 `dotnet restore --force --no-cache -warnaserror`를 성공한 뒤 빌드·검사했고 audit/경고 검사를 끄지 않았다. 최종 검증 로그는 다음과 같다.

- 빌드·self-test: `artifacts/r294/build/20261116-071840-853/`.
- 후보 publish·단일 EXE self-test: `artifacts/release-2.9.4-preparation-20261116/`.
- smoke·합성 PNG/진단: `artifacts/r294/windows-tests/after/20261116-072028-830-3fc719/`.
- 화면 비교: `artifacts/release-2.9.4-preparation-20261116/render-comparison.json`.

`candidate.json`을 생성하고 공식 `tools/New-WindowsDeploymentReceipt.ps1`로 새 `artifacts/release-2.9.4-preparation-20261116/deployment-receipt.json`을 만들었다. 준비 당시 receipt는 미사용이었으며 `.run` 폴더가 없었다. 기존 사전 확인 도구를 새 준비 폴더에 그대로 복사했다. Codex 컨텍스트에서 MSI PowerShell·C:\Windows\System32를 사용한 읽기 전용 검사에서 두 커밋 모두 GitExitCode=0·Readable=true였다. 이후 사용자 외부 컨텍스트 확인도 통과했으며 실행 명령은 아래 당시 절차로 보존한다.
이전 신원은 `artifacts/release-2.9.3-preparation-20261114/candidate.json`으로 공식 `New-WindowsDeploymentReceipt.ps1`에 전달한다.
이전 FileVersion은 2.9.3.0, 소스는 `516a9e14d8d2ce7dc34bac26327b9a7f22477e3b`, SHA-256은 `C0E5F5E3E608A264D23D498C06B73C7A3F460FAE77C56AF2DAE03239064A1EF0`이다.
준비 단계에서는 이전 receipt/.run/백업을 보존하고 새 receipt도 실행하지 않았다. 이후 외부 배포 통과는 위 사용자 확인에 기록했다.

## 사용자 외부 절차 · 배포 당시 기록

아래는 배포 준비 당시 안내한 절차를 보존한 것이며 같은 receipt의 재실행 안내가 아니다. PR 병합 뒤 읽기 전용 사전 확인을 먼저 실행하는 순서였다. MSI PowerShell 전체 경로이며 C:\Windows\System32에서도 동작한다.

~~~powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.4-preparation-20261116/Read-DeploymentSourceCommits.ps1'
~~~

기대값: Succeeded=true, Previous/Candidate 각각 GitExitCode=0·Readable=true, FilesChanged=false·AppLaunched=false.
통과하고 앱이 정상 종료 상태이면 다음 한 줄을 실행하는 절차였다. 당시 앱은 사용자 방침에 따라 새 배포 전까지 종료 유지했고 구버전 재실행은 안내하지 않았다.

~~~powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/deploy-windows.ps1' -ReceiptPath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.4-preparation-20261116/deployment-receipt.json'
~~~

기대값: Succeeded=true, 6단계 Passed, FileVersion=2.9.4.0, CandidateMatches=true, RegistrationPerformed=false, DryRunChangedNothing=true, AppLaunchRequested=true.
실제 기본 DB/WAL/SHM·백업·교체·알림 등록 대상/자동 시작 불변·해제 dry-run·운영 실행은 외부 스크립트가 확인한다. 배포 경로가 같고 등록이 정상이면 재등록하지 않는다. 교체 뒤 실패는 이전 EXE만 복원하며 DB는 복원하지 않는다.
실패한 receipt/.run/백업을 지우거나 같은 receipt로 임의 재실행하지 않는다.

## 미확인·사용자 확인

- 외부 Git 사전 확인·배포 통과와 2.9.4 트레이·상태/한도 보기·Gemini 안내·다른 세 Provider 정상은 위 사용자 확인으로 기록했다. Gemini 짧은 상세와 2.9.4의 모바일 송신 갱신은 이번 사용자 보고에서 별도로 확인되지 않았다.
- 실제 OS 알림 노출·재부팅/절전 복귀·반복 트레이 기록 억제는 이번 1회 후보 smoke가 대신하지 않는다.
- Mac 빌드/배포는 별도 담당 범위이며 이 PR에서 Mac 파일을 변경하지 않는다.
