# AI Burger Clock 2.9.3 — xAI 네이티브 공개 JSON 전환 후보

2026-11-14 KST. 기준 main은 `fe84da2c5662b50bde189010e6c8eafc62e8c374`(#150 병합)입니다.
이번 PR은 Windows Version·AssemblyVersion·FileVersion과 문서만 변경합니다.
공통/Windows C#·Mac·패키지·배포 스크립트는 바꾸지 않습니다. 화면 검사 진단 개선은 별도 Draft PR이며 이 후보에 포함하지 않습니다.

## 2.9.2 이후 포함 변경

- #150/#151: xAI 호환 JSON 세 주소 대신 data.x.ai summary·incidents 두 주소를 사용하고 RSS는 유지합니다. 필수 summary slug 19개와 Agent SDK, 사건의 components[].slug, 명시적 제외/범위 미확인·부분 실패를 구분합니다.
- 새 summary slug 진단은 실행 중 첫 목록과 이후 목록 변경에만 호출합니다. 24시간 소실 추적·조회/알림 주기·한도·화면은 유지합니다. [관찰한 JS 스키마·합성 검사·범위](XAI_STATUS.md).

## 사이트 선행 배포

- main 기준 `fe84da2`, 사이트 버전 16, 소스 `6fb4fd694da918b215a8fd2cb562233576505237`을 기존 소유자 전용 범위로 배포했습니다. 외부 공유는 추가하지 않았습니다.
- 최신 main 실제 C# 원본으로 만든 314사례·333 상태 단계가 일치했고, 사이트 87/87·typecheck·Worker 빌드·ESM 검증이 통과했습니다. 기존 시간표 19,972·권고 12·신선도/실패 47건 회귀를 포함합니다.
- 첫 사이트 버전 15 점검에서 Worker와 페이지/클라이언트 기준 커밋 불일치를 발견했습니다. 페이지를 맞추고 클라이언트는 공통 BASE_COMMIT을 사용하도록 정정해 버전 16으로 다시 배포했습니다. 기준 불일치 회귀 검사와 브라우저 합성 fixture도 갱신했으며 첫 배포를 최종 성공으로 합산하지 않습니다.
- 2026-11-14 14:21:27 KST, 배포 HTML·공식 상태 API HTTP 200, 기준 커밋 일치, xAI Operational·확인 불가 사유 없음, 상세에 data.x.ai summary·incidents 두 출처가 표시됨을 확인했습니다. Worker 응답과 사이트 판정/상세 함수로 확인한 결과이며 아이폰 실기 확인과 구분합니다.
- /api/quota·실제 계정 값은 읽지 않았습니다. 기존 사이트 인증은 숨긴 표준 입력으로만 사용하고 토큰·사이트 주소는 응답·로그·이 저장소에 적지 않습니다.

## 후보·검사·receipt

후보는 `artifacts/release-2.9.3-preparation-20261114/candidate/AI Burger Clock.exe`입니다.

| 항목 | 결과 |
|---|---|
| 후보 소스 커밋 | `516a9e14d8d2ce7dc34bac26327b9a7f22477e3b` (검증 기록 커밋과 구분) |
| SDK | 10.0.404 (저장소 global.json 적용) |
| build.ps1 | 경고 0·오류 0, self-test 293,223 PASS·exit 0 |
| 후보 Portable publish·단일 EXE self-test | 성공, 293,223 PASS·exit 0 |
| 공통 / Windows 검사 | 292,094 + 1,129, 기존 symlink fixture 1건 SKIP |
| smoke | 고정 after 슬롯 첫 1회 1,174 PASS·PNG 60개·exit 0 |
| 렌더 비교 | #150 최종 성공 실행과 PNG 60/60·layout JSON 41/41 SHA-256 동일 |
| 실제 OS 알림 제출·등록 명령 | 0회, 가짜 발송기만 사용 |
| FileVersion / ProductVersion | `2.9.3.0` / `2.9.3+516a9e14d8d2ce7dc34bac26327b9a7f22477e3b` |
| 크기 / SHA-256 | 29,874,107 bytes / `C0E5F5E3E608A264D23D498C06B73C7A3F460FAE77C56AF2DAE03239064A1EF0` |

이번 smoke는 실제 휠·부모 카드 hit-test를 포함해 첫 1회 통과했으므로 승인된 재실행은 사용하지 않았습니다.
앞선 #150의 실패/재실행 기록은 그대로 보존하며, 이번 성공을 원인 해결로 해석하지 않습니다. 트레이 기록 18개·after 슬롯 1개·새 키 0, 1회여서 반복 누적 억제는 미평가입니다.

검증 로그:

- `artifacts/release-2.9.3-20261114/build/20261114-142347-975/`
- `artifacts/release-2.9.3-20261114/windows-tests/after/20261114-142557-885-32e443/`
- `artifacts/release-2.9.3-preparation-20261114/`의 restore·publish·candidate-self-test·candidate.json·render-comparison.json·receipt·Git 사전 확인

이전 신원은 `artifacts/release-2.9.2-preparation-20261114/candidate.json`이며 공식 receipt 생성기로 읽습니다.
기존 receipt·.run·백업과 운영 dist·DB·등록을 바꾸지 않습니다. 병합 뒤 사용자가 외부 MSI PowerShell에서 사전 확인 → 정상 종료 → 배포 명령 순서로 진행합니다.

이전 2.9.2 신원은 소스 `98320e7fb598a7b0fb32c659b44bfa65efce7ccb`, ProductVersion `2.9.2+98320e7fb598a7b0fb32c659b44bfa65efce7ccb`, SHA-256 `E6AD2CC165BEF4A748E950152731C042A07B5FC8800E82D6C0D83D89ECF445CC`입니다. 운영 EXE 해시도 같은 값으로 불변입니다.

공식 `New-WindowsDeploymentReceipt.ps1`로 `artifacts/release-2.9.3-preparation-20261114/deployment-receipt.json`을 새로 생성했습니다. 이 receipt는 아직 실행하지 않았습니다. 읽기 전용 Git 사전 확인은 Codex 컨텍스트에서 두 커밋 모두 Readable=true이며 외부 사용자 컨텍스트 확인과 구분합니다.

## 사용자가 외부에서 실행할 명령

PR 병합 뒤, Store 판이 아닌 **C:\Program Files\PowerShell\7\pwsh.exe**로 실행합니다. 사전 확인은 C:\Windows\System32에서도 동작하며 파일·앱을 변경하지 않습니다.

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.3-preparation-20261114/Read-DeploymentSourceCommits.ps1'
```

정상 기대값은 Succeeded=true, Previous/Candidate 각각 GitExitCode=0·Readable=true, FilesChanged=false·AppLaunched=false입니다. 실패하면 배포하지 않고 오류를 확인합니다.
정상이면 기존 앱을 트레이 → 종료로 닫고 다음 한 줄을 실행합니다.

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/deploy-windows.ps1' -ReceiptPath 'C:/Users/mc_bl/.codex/.chatgpt-projects/g-p-6aa5eb198fd88191b7382af0eb114af4/AiBurgerClock/artifacts/release-2.9.3-preparation-20261114/deployment-receipt.json'
```

정상 기대값은 Succeeded=true, Receipt/Preflight/Backup/Replace/Inspect/Launch 모두 Passed, FileVersion=2.9.3.0, CandidateMatches=true, RegistrationPerformed=false, DryRunChangedNothing=true, AppLaunchRequested=true입니다.
배포 경로가 같고 알림 등록 대상이 정상이면 재등록하지 않습니다. 실패하면 다음 단계로 가지 않고 교체 뒤 실패는 이전 EXE만 복원하며 DB는 복원하지 않습니다. 실패한 receipt·.run·백업을 지우거나 같은 receipt로 임의 재실행하지 않습니다.

## 미확인·사용자 확인 항목

- 실제 기본 데이터 폴더의 프로세스 부재·WAL/SHM·백업·교체·등록 대상·해제 dry-run·실행은 아직 하지 않았습니다. 외부 배포 스크립트가 확인합니다.
- 배포 뒤 2.9.3 트레이·상태 보기·한도 보기, xAI 정상과 상세의 새 출처는 사용자 화면 확인이 필요합니다. 사이트 아이폰 실기·localStorage 복원도 별도 사용자 확인입니다.
- Mac 빌드/배포는 별도 담당 범위입니다. 이 PR은 Mac 파일을 수정하지 않습니다.
- 검사 후 기존 2.9.2 외부 재실행 완료는 사용자 확인입니다. 이번 후보 검증에서 운영 EXE 교체·실행·등록은 하지 않았습니다.
