# Windows 2.10.0 배포 기록

## 변경 범위

- [한도 조회 재설계 P2](QUOTA_SOURCES_DESIGN.md)의 조회원 인터페이스와 공통 루프를 배포했습니다. Codex·Claude·xAI CLI, Gemini 앱 웹, 리더 미등록 상태를 각 조회원으로 표현해 예약·캐시·표시 연결을 한 루프에서 처리합니다.
- 조회 조건과 주기, 화면 문구·톤·레이아웃, 알림 판정, 송신 스키마 v2를 유지했습니다. 기존 P0 assertion은 그대로이며 합성 송신 스냅샷 9개는 P1과 바이트 단위로 일치했습니다. 모바일 사이트는 기존 버전을 사용합니다.
- #185/#186은 공통 원본과 두 OS 호스트·검사를 이전했습니다. 주요 추가 파일은 `QuotaSources.cs`, `QuotaCacheAdapter.cs`, `QuotaSourceContractTests.cs`, `QuotaCacheFixtureTests.cs`입니다. `AccountQuotaMonitor.cs`가 공통 루프를 맡고 `GeminiWebQuotaMonitor.cs`는 제거했습니다.
- #188은 `AiBurgerClock.csproj`의 Windows 버전 필드 세 개를 바꿨습니다. 이 기록 PR은 새 문서와 `README.md`, `CODE_GUIDE.md`, `BACKLOG.md`를 갱신합니다.

## 빌드와 검사

| 검사 | Windows 2.9.9 | Windows 2.10.0 |
|---|---:|---:|
| .NET SDK | `10.0.404` | `10.0.404` |
| Windows 빌드·publish | 경고 0 · 오류 0 | 경고 0 · 오류 0 |
| 공통 검사 | 291,812건 | 291,817건 (+5) |
| 단일 EXE self-test | 293,082건 | 293,087건 (+5) |
| 고정 after 슬롯 smoke | 1,214건 · 합성 PNG 75장 | 1,214건 · 합성 PNG 75장 |

#188의 Windows 빌드·self-test와 병합 후 `build.ps1 -Publish` 및 단일 EXE self-test·smoke 종료 코드는 0입니다. 첫 publish의 NuGet 감사 조회 오류(`NU1900`)는 감사를 유지한 네트워크 재실행으로 해소했습니다. Windows 임시 파일시스템의 기존 symlink fixture 1건을 건너뛰었습니다. P0 공통 92건·Windows 송신 25건이 통과했고, 옛 Gemini agy 캐시 키 보존 검사도 통과했습니다. smoke는 1회 실행했으므로 반복 실행 때의 트레이 기록 증가는 평가하지 않았습니다.

검사 로그는 격리 작업트리 `review-worktrees/windows-2.10.0-release`의 `artifacts/release-2.10.0/build/20261126-172529-001/`과 `artifacts/release-2.10.0/windows-tests/after/20261126-172649-536-df92b7/`에 보관합니다. Mac 호스트 검증은 [Mac 작업 기록 89절](MACOS_PORT.md#89-한도-조회-원천-통합p2-pr-185의-mac-호스트-이전)에 있습니다.

## Git 기준점과 백업

| 항목 | 값 |
|---|---|
| P2 작업 전 main | `068d68541d0db7ccc9ac27f45d91585ee36bbae9` |
| #185 Windows 검증 HEAD | `db96fa30c99a9cdd1c74fbed4f622d2ca880d480` |
| #186을 포함한 #185 최종 브랜치 HEAD | `ac383af4d35a2898fcdfbabafb512f5c73bdbe25` |
| #185 병합 | `e4831bcac404885746239a9b43b41624e4509be1` |
| 버전 PR #188 HEAD | `4915e5937a4d2bfc309894b63f383b9524939b27` |
| #188 병합 및 최종 EXE 소스 | `666611b960b1c60d40b1de3afc06b20d77c079e7` |
| 이전 2.9.9 EXE 소스 | `502a4a63e5d0388f25441aa896be8830032e331f` |
| 운영 EXE | `dist/win-x64/AI Burger Clock.exe` |
| FileVersion | `2.10.0.0` |
| ProductVersion | `2.10.0+666611b960b1c60d40b1de3afc06b20d77c079e7` |
| 크기 | `31,335,823` bytes |
| SHA-256 | `5602939E9EE0C31DE025E502FF47CD2B298DE88C5CEFFACA1C78B1AEB7DA90CB` |
| Receipt | `artifacts/release-2.10.0-preparation-20261126/deployment-receipt.json` |

배포 전 후보·고정 after 슬롯·receipt의 해시를 대조했습니다. 배포 후 운영 경로의 파일을 읽기 전용으로 확인해 버전·크기·해시 일치를 확인했습니다. 배포 스크립트가 쓰는 저장소 지정 방식으로 이전·후보 소스 커밋도 읽었습니다.

로컬 백업은 `../backups/release-2.10.0.0-20261126-173222-be93749c/`에 있습니다. 이전 EXE와 교체된 EXE, 이전 dist ZIP, 양쪽 소스 ZIP, 새 EXE, DB·로그 복사본을 보존합니다. 데이터 파일의 해시는 외부 스크립트가 검증한 로컬 `data-manifest.json`에 있습니다.

| 로컬 백업 파일 | SHA-256 |
|---|---|
| `previous.exe` | `36D0FA9BB71BCA946E7BBC881D89015FB1FDC43ED0A58D7939F9291ACC2B93DD` |
| `previous-dist.zip` | `9F4DA6C86D8A166D7F0D12A2A2449D1B34C1EFCF9D9B7EAE939625C62164AC0A` |
| `source-before.zip` | `D1F2DB47C3BD220F9AB2A8465A87A33CC0390D97E40B363D9392774AE55FB976` |
| `source-candidate.zip` | `4BE3ADA4E91F7EE1E8B83C130843AB57E33B94E8ED601D94D66F90326B98F5E5` |
| `deployed.exe` | `5602939E9EE0C31DE025E502FF47CD2B298DE88C5CEFFACA1C78B1AEB7DA90CB` |

복원이 필요하면 Codex 밖에서 운영 앱을 정상 종료하고, 백업의 `previous.exe` 해시를 확인한 뒤 같은 운영 경로의 EXE만 교체합니다. 교체 후 `2.9.9.0`과 이전 해시를 대조하고 외부에서 실행합니다. DB·캐시와 등록은 보존합니다. [외부 실행과 복원](WINDOWS_DEPLOYMENT.md)

## 외부 배포와 사용자 확인

- **2026-11-26 KST 사용자 확인.** Codex 밖 MSI PowerShell 7에서 `deploy-windows.ps1`을 실행했습니다. `Succeeded=True`, 여섯 단계 `Passed`, 알림 등록·자동 시작 대상 불변, 해제 dry-run 종료 코드 0 및 무변경이 기대값과 일치했습니다. 기존 등록을 사용했습니다.
- 종료·실제 `%LOCALAPPDATA%\AIBurgerClock` 데이터 경로·WAL/SHM 부재·백업 해시·교체 검사는 외부 스크립트에서 수행했습니다. 로컬 실행 전 검증 기록에서도 Receipt부터 Inspect까지 다섯 단계 통과를 확인했습니다. 운영 앱 실행과 화면 확인의 근거는 사용자 보고입니다.
- 실제 재부팅·절전 복귀, OS 알림 노출·알림 센터 보관, 수동 복원은 이번 배포에서 확인하지 않았습니다.

## 운영 관찰

배포 직후부터 3일간 다음 항목을 확인합니다.

| 관찰 항목 | 확인할 동작 |
|---|---|
| 자동 갱신 | Codex·Claude·xAI CLI와 Gemini 앱 웹의 확인 시각 갱신 |
| 수동 Refresh | 한 번 눌렀을 때 켜진 조회원의 확인 시각 갱신 |
| 실패 문구·진단 코드 | 실제 실패가 생기면 해당 Provider의 문구·진단 코드 확인 |
| Gemini 켜기·끄기 | 끈 뒤 안내 표시, 다시 켠 뒤 한도 갱신 |
| 모바일 송신과 사이트 | 활성 연동의 수신 시각 갱신과 기존 표시 유지 |

**첫 확인 · 2026-11-26 KST, 사용자 확인.** 외부 스크립트 기대값이 일치했고 트레이·상태 창·한도 박스 4개가 정상입니다. 수동 Refresh 한 번으로 확인 시각이 갱신됐습니다. 3일 관찰은 진행 중입니다. Gemini 로그아웃은 관찰을 위해 실행하지 않으며 실제로 필요한 경우에만 합니다.
