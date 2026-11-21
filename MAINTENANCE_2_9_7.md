# Windows 2.9.7 후보 준비

## 변경

- 최신 main `5f9795468e6a804f378571d11de19aa9ee4ead5c`의 #167을 포함합니다. xAI는 정보성 사건을 정상·공지로 표시하고 RSS `ACTIVE`를 인식합니다.
- Windows 송신은 Gemini의 검증된 두 웹 한도 행과 `webState`를 기존 스키마 v2에 포함합니다. 사용률·초기화 시각·확정 상태가 바뀔 때만 Gemini 단독 송신을 예약합니다. 다른 Provider의 완료와 15분 하트비트의 전체 스냅샷에도 포함합니다.
- 변경 파일은 Windows 버전 필드, `WindowsQuotaExportSnapshot.cs`, `WindowsQuotaExportChecks.cs` 및 관련 기록 문서입니다. [송신 계약](WINDOWS_QUOTA_EXPORT.md)

## 사이트 v18

비공개 사이트를 `6bcef437cfd00aab97c6f721f5b997c2e1b1d3b9`에서 배포했습니다. 운영 Worker의 페이지와 `/api/status`가 HTTP 200으로 응답했고 기준 커밋 `5f97954`와 네 Provider 응답을 확인했습니다. xAI는 세 출처에서 응답을 받았습니다. 공지를 확인했고 장애 및 판정 오류는 0건이었습니다.

## 후보 검사

| 검사 | 결과 |
|---|---|
| .NET SDK | `10.0.404` |
| Release 빌드와 Portable publish | 경고·오류 0, 종료 코드 0 |
| bin·단일 EXE self-test | 각각 292,844 PASS, 종료 코드 0. 임시 파일 시스템의 symlink 검사 1건 SKIP |
| Windows 모바일 송신 합성 검사 | 255 PASS. 무변화 0회, 사용률·초기화 시각 변경 각 1회, 확정 상태 전환 각 1회, 다른 Provider 완료는 Gemini를 포함한 기존 1회 |
| 고정 after 슬롯 smoke | 1회 1,208 PASS, 종료 코드 0, 합성 PNG 75개 |
| Git 읽기 확인 | System32에서 배포 스크립트와 같은 `git -C`·`safe.directory` 경로로 이전·후보 커밋 모두 읽음 |

로그와 후보 파일은 `artifacts/release-2.9.7-preparation-20261121/`에 보관했습니다. 실제 Gemini 로그인·PC 값과 사이트 값 대조는 사용자 확인 단계입니다. 트레이 기록 증가 추세는 smoke 2회 이상이 필요한 항목입니다.

## Git 기준점과 후보 신원

| 항목 | 값 |
|---|---|
| main 기준 | `5f9795468e6a804f378571d11de19aa9ee4ead5c` |
| 후보 소스 커밋 | `4bf66112f4e44f58292edfee38213aa627699ecd` |
| 이전 2.9.6 EXE 소스 | `15844784a24bc6ec56799c0eb66e166c618bfb0a` |
| 후보 | `artifacts/release-2.9.7-preparation-20261121/candidate/AI Burger Clock.exe` |
| FileVersion | `2.9.7.0` |
| ProductVersion | `2.9.7+4bf66112f4e44f58292edfee38213aa627699ecd` |
| 크기 | `31143309` bytes |
| SHA-256 | `910A9604258942230A13D2C83EE04C7D54FE2B68029837A81CD32269DCC60F16` |
| Receipt | `artifacts/release-2.9.7-preparation-20261121/deployment-receipt.json` |

기존 2.9.6의 `candidate.json`으로 공식 receipt 생성기를 실행했습니다. 후보 소스 이후 이 검사·신원 기록만 추가했습니다. 실제 백업과 복원 지점은 외부 배포 도구가 만듭니다.

## 외부 배포와 사용자 확인

1. 앱을 트레이에서 정상 종료한 뒤 MSI PowerShell 7의 전체 경로로 검증된 receipt와 `deploy-windows.ps1`을 실행합니다. 성공 기대값은 `Succeeded=True`와 전 단계 `Passed`입니다. 스크립트가 이전 EXE와 DB·소스를 백업하고 알림 등록·자동 시작·해제 dry-run을 확인합니다. [배포 도구와 복원](WINDOWS_DEPLOYMENT.md)
2. 모바일 연동이 켜진 상태에서 PC와 사이트의 Gemini `5시간`·`주간` 두 행을 대조합니다. 사이트를 Refresh해 최신 스냅샷을 확인합니다.
3. PC의 트레이 메뉴에서 **Gemini 사용량 → 끄기**를 선택합니다. 사이트를 Refresh해 **꺼짐 · 앱에서 로그인하면 표시** 안내와 빈 Gemini 박스를 확인합니다. 사용량 수치와 계정 정보 없이 확인 결과만 기록합니다.
