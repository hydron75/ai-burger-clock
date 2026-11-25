# Windows 2.9.9 후보 기록

## 변경

- [한도 조회 재설계 P1](QUOTA_SOURCES_DESIGN.md#41-실패-분류)의 7가지 실패 분류를 적용합니다. 조회 경로에서 원인을 분류하고, 공통 화면 모델에서 문구와 색을 정합니다. 첫 CLI 조회 전에는 “한도 조회 대기”를 표시하며 Gemini 웹 로그인 필요에는 경고색을 씁니다.
- 상세와 `--check-quotas` 실패 출력에 고정 진단 코드를 표시합니다. 예외 원문과 경로는 출력하지 않습니다. Claude는 최근 한도 성공을 반영한 기존 로그인 상태 표시 기준을 따릅니다.
- #180의 Mac 합성 화면 검사 보완을 포함합니다. 후보 준비 커밋은 `AiBurgerClock.csproj`의 Windows 버전 세 필드와 `README.md`, `CODE_GUIDE.md`를 갱신하고 이 문서를 추가합니다.
- 모바일 사이트 동기화 대상은 CLI 첫 조회 전 문구와 Gemini 웹 로그인 필요 색입니다. 송신 스키마 v2는 유지합니다.

## 후보 검사와 신원

| 검사 | #180 병합 HEAD | 2.9.9 후보 |
|---|---|---|
| .NET SDK | `10.0.404` | `10.0.404` |
| Windows 빌드 | Release 경고 0·오류 0 | Portable publish 경고 0·오류 0 |
| Shared.Tests | 291,812건 통과 | 291,812건 통과 |
| Windows self-test | 293,082건 통과 | 단일 EXE 293,082건 통과 |
| 고정 after 슬롯 smoke | 1회 1,214건 통과 · 합성 PNG 75개 | 단일 EXE 1회 1,214건 통과 · 합성 PNG 75개 |

모든 실행의 종료 코드는 0입니다. Windows의 기존 symlink fixture 1건은 임시 파일시스템 제약으로 건너뛰었습니다. 각 빌드의 smoke는 한 번씩 실행했으므로 같은 빌드를 반복 실행할 때의 트레이 기록 증가는 평가하지 않았습니다. #180의 Mac 공통 검사와 native smoke는 [Mac 기록](MACOS_PORT.md#87-한도-조회-실패-분류p1-pr-179의-mac-확인)에 있으며 최종 PR HEAD의 Mac 확인은 대기 중입니다.

## Git 기준점과 백업

| 항목 | 값 |
|---|---|
| PR #179의 main 기준점 | `cd8a819bd4ae552a4db165440f502b6d3edc34f0` |
| #180 병합 HEAD | `92de0be1691096557b296dcdfbd87ec246113d34` |
| Windows 2.9.9 후보 소스 | `502a4a63e5d0388f25441aa896be8830032e331f` |
| 이전 2.9.8 EXE 소스 | `7bd5fa1e0a248b8f51fcc68a153b474cb3cfc48a` |
| 후보 EXE | `artifacts/release-2.9.9-preparation-20261125/candidate/AI Burger Clock.exe` |
| FileVersion | `2.9.9.0` |
| ProductVersion | `2.9.9+502a4a63e5d0388f25441aa896be8830032e331f` |
| 크기 | `31315341` bytes |
| SHA-256 | `36D0FA9BB71BCA946E7BBC881D89015FB1FDC43ED0A58D7939F9291ACC2B93DD` |
| Receipt | `artifacts/release-2.9.9-preparation-20261125/deployment-receipt.json` |

공식 receipt 생성기에 직전 배포의 `artifacts/release-2.9.8-preparation-20261124/candidate.json`을 사용했습니다. 후보 파일과 receipt, 고정 슬롯 manifest의 해시를 대조했습니다. System32에서 배포 스크립트와 같은 Git 저장소 지정으로 두 소스 커밋을 읽었습니다. 후보를 빌드한 뒤 이 기록만 별도 커밋으로 추가했습니다. 검증 기록은 후보 준비 폴더의 `publish.txt`, `shared-tests.txt`, `candidate-self-test.txt`, `verification/`에 있습니다.

## 외부 배포와 복원

1. 배포 승인 후 운영 앱을 트레이에서 정상 종료하고, Codex 밖 MSI PowerShell 7에서 이 후보의 receipt로 `deploy-windows.ps1`을 실행합니다. 기대 결과는 `Succeeded=True`, 전 단계 `Passed`, `FileVersion=2.9.9.0`입니다.
2. 외부 도구가 이전 EXE·DB·소스를 백업합니다. 교체 후 검사 또는 실행 요청이 실패하면 이전 EXE를 복원합니다. [외부 실행과 복원](WINDOWS_DEPLOYMENT.md)
3. 실제 외부 배포와 트레이·상태 보기·한도 보기 사용자 확인은 대기 중입니다. 계정별 한도 값과 원본 응답은 기록하지 않습니다.
