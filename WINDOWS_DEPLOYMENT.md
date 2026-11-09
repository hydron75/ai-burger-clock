# Windows 외부 배포 실행 묶음

2026-11-05 [AGENTS.md의 Windows 배포](AGENTS.md#windows-배포) 절을 구현한 Windows 전용 PowerShell 도구입니다. 버전·앱 동작·운영 등록은 바꾸지 않습니다. 다음 배포부터 기존 버전별 `Backup-OutsideCodex` → `Replace-VerifiedExe` → `Inspect-ProductionOutsideCodex` → 실행 대신 저장소의 [deploy-windows.ps1](deploy-windows.ps1)을 재사용합니다. 아래 경로·버전은 **합성 예시**입니다.

## 준비와 사용자 실행

1. Codex는 승인된 후보를 별도 `artifacts/<배포>/candidate/AI Burger Clock.exe`에 빌드·검사합니다. `build.ps1 -Publish`로 운영 dist를 바꾸는 대신 해당 배포의 후보 경로를 사용합니다. 빌드·self-test·smoke 통과 근거는 해당 버전 기록에 남깁니다.
2. Codex가 [New-WindowsDeploymentReceipt.ps1](tools/New-WindowsDeploymentReceipt.ps1)로 영수증을 만듭니다. 후보의 FileVersion·ProductVersion·소스 커밋·SHA-256·크기를 파일에서 읽고, 이전 배포의 `candidate.json` 또는 이 계약의 영수증에서 이전 EXE 신원을 가져옵니다. 생성기는 운영 dist·DB·레지스트리를 읽거나 앱을 실행하지 않습니다. 이전 기록을 잃었으면 임의 추정하지 않고 준비를 중단합니다.
3. 사용자는 운영 앱을 트레이 → 종료로 정상 종료합니다. Codex가 제공한 **명령 한 줄**을 Codex 밖 비패키지 64비트 MSI PowerShell 7에서 실행합니다. 후보 경로·버전·해시를 직접 입력하지 않습니다. 관리자 권한은 필요 없습니다.

준비 시 Codex가 사용하는 합성 예시:

```powershell
pwsh -NoLogo -NoProfile -File ./tools/New-WindowsDeploymentReceipt.ps1 -CandidatePath 'C:/Example/AiBurgerClock/artifacts/release-next/candidate/AI Burger Clock.exe' -PreviousIdentityPath 'C:/Example/AiBurgerClock/artifacts/release-previous/candidate.json' -ReceiptPath 'C:/Example/AiBurgerClock/artifacts/release-next/deployment-receipt.json'
```

사용자에게 제공할 외부 명령의 합성 예시:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File 'C:/Example/AiBurgerClock/deploy-windows.ps1' -ReceiptPath 'C:/Example/AiBurgerClock/artifacts/release-next/deployment-receipt.json'
```

**PowerShell 실행 경로(2026-11-09):** 사용자 PC에는 `C:\Program Files\PowerShell\7\pwsh.exe`의 MSI PowerShell 7.6.9가 설치·확인돼 있다. 배포 명령은 이 전체 경로를 사용하며 추가 설치를 안내하지 않는다. Microsoft Store/MSIX 판은 `PackagedProcessRefused`로 거부한다. `pwsh`라는 이름만 쓰면 PATH에 따라 Store 판을 고를 수 있으므로 사용자에게는 항상 전체 경로를 제공한다. Store 판은 그대로 둔다. 이 문서 갱신은 검사 동작이나 스크립트 코드를 바꾸지 않는다.

이 명령을 **Codex가 실행하지 않습니다**. 운영 앱 실행은 새 조회·캐시 쓰기·설정에 따른 실제 알림·사이트 전송을 일으킬 수 있습니다. 등록·실제 해제·시험 알림 명령은 포함하지 않습니다. 알림 등록이 없거나 다른 경로를 가리키면 자동으로 고치지 않고 실패합니다.

## 영수증 계약

`Schema=aiburgerclock.windows-deployment`, `SchemaVersion=1`, `Repository`, `Candidate`, `Previous`만 생성합니다.

| 필드 | 내용 |
|---|---|
| Candidate | `Path`, `FileVersion`, `ProductVersion`, `SourceCommit`, `SHA256`, `Bytes` |
| Previous | `FileVersion`, `ProductVersion`, `SourceCommit`, `SHA256` |

소스 커밋은 ProductVersion의 `+<40자리 커밋>`에서 읽습니다. 이전 EXE도 배포 시 같은 FileVersion·ProductVersion·SHA-256이어야 합니다. 버전 증가·후보 크기·해시를 재검사합니다. 영수증은 해당 저장소의 `artifacts` 아래, 후보는 영수증이 있는 폴더의 하위여야 합니다. 교체 대상은 영수증에서 받지 않고 저장소의 `dist/win-x64/AI Burger Clock.exe`로 고정합니다. DB는 실제 사용자 `LocalApplicationData/AIBurgerClock/burgerclock.db`로 고정합니다. 재분석 지점(junction·symlink)과 열린 파일의 경로 리디렉션은 거부합니다.

영수증은 서명이 아니므로 로컬 파일을 변조할 수 있는 공격자를 인증하지 않습니다. Codex가 생성한 영수증·스크립트를 검토한 사용자만 외부에서 실행합니다. 토큰·사이트 주소·계정 값은 계약에 넣지 않습니다.

## 단계별 확인과 보존물

| 단계 | 수행 내용 |
|---|---|
| Receipt | 비패키지·대화형·64비트 외부 PowerShell 확인, 영수증/후보 신원 확인, dist별 배포 잠금, 영수증 재사용 금지 |
| Preflight | 운영/시험 앱 프로세스 부재, DB·WAL/SHM, 실제 열린 경로 확인, 이전 EXE 신원, 기존 바로가기·AUMID·COM·자동 시작 대상 확인 |
| Backup | `../backups/release-<FileVersion>-<KST>-<임의값>`에 이전 EXE, dist ZIP, DB, 모든 로그, 이전/후보 소스 ZIP 보존. EXE·DB·로그 해시, ZIP 속 EXE, 원본 데이터 불변 확인 |
| Replace | 프로세스·WAL/SHM·데이터 불변·등록 불변·이전/후보 신원 재확인 후 같은 볼륨의 staging 파일로 `File.Replace`. 교체된 이전 EXE와 저장한 새 EXE의 버전·해시 확인 |
| Inspect | 실제 시작 메뉴 바로가기 대상·인자·작업 폴더·AUMID·CLSID, HKCU 소유 표식·AUMID·COM 대상·명령, 자동 시작 대상 불변 확인. 실제 배포 EXE로 **해제 dry-run만** 실행하고 종료 코드 0·확인 문구·등록/자동 시작/DB/로그 불변 확인 |
| Launch | 모든 검사와 실행 전 증거 저장이 끝난 뒤 같은 dist EXE를 외부 컨텍스트에서 한 번 실행 요청 |

자동 시작이 원래 꺼져 Run 항목이 없으면 그대로 허용합니다. 있으면 정확한 `"<배포 EXE>" --autostart`여야 합니다. 바로가기 파일 해시, AUMID/COM의 모든 값·하위 키, 해당 Run 값과 StartupApproved 값은 교체 전·후·dry-run 후 동일해야 합니다. 레지스트리 원문은 기록하지 않고 비교용 해시만 내부에서 사용합니다. 기존 등록을 다시 만들지 않습니다.

WAL/SHM은 0바이트라도 존재하면 중단하며 지우지 않습니다. 원본 DB·로그와 복사본의 상대 경로·크기·해시를 대조합니다. 백업은 덮어쓰지 않는 고유 폴더이며 소스 ZIP은 영수증의 두 커밋으로 `git archive`합니다. Git이 없거나 커밋을 찾지 못하면 교체 전에 멈춥니다.

## 실패·복원·결과

- 첫 실패 단계에서 멈추고 이후 단계는 `NotRun`으로 출력합니다. 단계별 `Passed/Failed/NotRun`, `Checks`, 실패 코드, 백업 위치, 후보 신원과 실행 요청 여부를 JSON으로 출력합니다. 성공 종료 코드 0, 실패 1입니다. 예외 원문·알림 본문·계정 값·토큰·사이트 주소는 출력하지 않습니다.
- 교체 시도 뒤 실패하면 검증한 `previous.exe`로 **EXE만 복원**하고 다시 버전·해시를 검사합니다. DB·캐시·로그·WAL/SHM·등록은 복원/수정하지 않습니다. 정상 EXE 복원 결과는 `Rollback=RestoredPreviousExe`입니다. 새 앱은 실행하지 않습니다.
- 외부에서 앱을 동시에 켰거나 파일 잠금/권한/디스크 문제로 복원이 막히면 강제 종료하지 않습니다. `Rollback=FailedPreserveBackupAndStop`을 출력하고 검증한 이전 EXE·백업을 보존합니다. 성공으로 처리하거나 DB를 되돌리지 말고 사용자에게 수동 복구가 필요함을 보고합니다.
- 해제 dry-run은 30초를 넘기면 **이 스크립트가 시작한 dry-run 자식만** 종료한 뒤 실패·EXE 복원을 처리합니다. 운영 앱 프로세스는 종료하지 않습니다.
- 영수증당 한 번만 실행합니다. `<영수증 이름>.run/verified-before-launch.json`은 실행 요청 전 증거이며 실행 성공/화면 확인을 뜻하지 않습니다. 실패한 영수증·백업은 남기고, 원인을 확인한 뒤 Codex가 새 이름의 영수증을 준비합니다. dist의 `.deployment.lock` 파일 자체는 남아도 잠금은 열린 핸들 동안만 유지됩니다.
- `AppLaunchRequested=true`는 `Start-Process`의 실행 요청 성공입니다. 이후 상주·트레이·상태 창·한도 화면은 사용자 확인입니다. 사용자 외부 컨텍스트에서 단일 프로세스·버전·해시와 기본 DB/로그 갱신도 대조하고, 화면 확인과 구분해 버전 기록에 적습니다. 시작 후 앱이 스스로 종료한 것을 이 결과만으로 정상 상주라고 하지 않습니다.

## 검사 경계

[Test-WindowsDeployment.ps1](tools/Test-WindowsDeployment.ps1)은 고정 `~/.ai-burger-clock-tests/before|after/deployment-fixtures/<임의값>` 아래 가짜 저장소·텍스트 EXE·가짜 DB/로그로 실행합니다. 슬롯의 기존 앱/산출물을 덮어쓰지 않고, fixture를 자동 삭제하지 않습니다. 실제 파일 복사·해시·WAL/SHM·ZIP·원자적 교체·EXE 복원을 실행하되 프로세스·등록·dry-run·버전 읽기·소스 archive 경계는 가짜로 대체합니다. 운영 dist/DB 읽기·쓰기, 실제 앱 실행·운영 등록·OS 알림 호출은 0건입니다. 프로덕션 스크립트에는 `-SkipChecks`·테스트 우회 옵션이 없습니다.

```powershell
pwsh -NoLogo -NoProfile -File ./tools/Test-WindowsDeployment.ps1 -Slot before
pwsh -NoLogo -NoProfile -File ./tools/Test-WindowsDeployment.ps1 -Slot after
```

검사 결과는 `artifacts/windows-external-deployment/summary-before.json`·`summary-after.json`에 남깁니다. 실제 외부 배포·AppData/HKCU 뷰·운영 EXE dry-run·앱 실행은 이번 PR에서 수행하지 않으며 다음 승인된 배포의 사용자 외부 실행으로 확인합니다.

2026-11-05 검증: before/after 각각 **33개 시나리오·186개 검사 PASS·종료 코드 0**. 실행 중 앱, WAL/SHM, 영수증 경로/신원/재사용, 후보/이전 해시·버전, 잠금, reparse, 백업 손상, 로그 변경, 소스 ZIP 실패, 등록 변경, dry-run 실패/DB 쓰기/WAL 생성, 실행 요청 실패, EXE 복원 및 복원 불가, 오류 원문 비노출을 확인했습니다. 빈 등록 키·같은 텍스트의 값 유형 변경·영수증 생성/덮어쓰기 거부도 포함합니다. PowerShell 파서 **4개 파일·오류 0**이며 운영 경로 접근·실제 앱 실행·운영 등록·OS 알림은 0건입니다. PowerShell 7.0 API 호환성은 정적 검토이며 해당 구버전 런타임 실행은 하지 않았습니다. C#·UI·버전 변경이 없어 앱 build/self-test/native smoke는 실행하지 않았습니다.
