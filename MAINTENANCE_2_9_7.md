# Windows 2.9.7 후보 준비

## 변경

- 최신 main `5f9795468e6a804f378571d11de19aa9ee4ead5c`의 #167을 포함합니다. xAI는 정보성 사건을 정상·공지로 표시하고 RSS `ACTIVE`를 인식합니다.
- Windows 송신은 Gemini의 검증된 두 웹 한도 행과 `webState`를 기존 스키마 v2에 포함합니다. 사용률·초기화 시각·확정 상태가 바뀔 때만 Gemini 단독 송신을 예약합니다. 다른 Provider의 완료와 15분 하트비트의 전체 스냅샷에도 포함합니다.
- 변경 파일은 Windows 버전 필드, `WindowsQuotaExportSnapshot.cs`, `WindowsQuotaExportChecks.cs` 및 관련 기록 문서입니다. [송신 계약](WINDOWS_QUOTA_EXPORT.md)

## 사이트 v18

비공개 사이트를 `6bcef437cfd00aab97c6f721f5b997c2e1b1d3b9`에서 배포했습니다. 운영 Worker의 페이지와 `/api/status`가 HTTP 200으로 응답했고 기준 커밋 `5f97954`와 네 Provider 응답을 확인했습니다. xAI는 세 출처에서 공지를 수신했고 장애 및 판정 오류는 0건이었습니다.

## 외부 배포와 사용자 확인

1. 앱을 트레이에서 정상 종료한 뒤 MSI PowerShell 7의 전체 경로로 검증된 receipt와 `deploy-windows.ps1`을 실행합니다. 성공 기대값은 `Succeeded=True`와 전 단계 `Passed`입니다. 스크립트가 이전 EXE와 DB·소스를 백업하고 알림 등록·자동 시작·해제 dry-run을 확인합니다. [배포 도구와 복원](WINDOWS_DEPLOYMENT.md)
2. 모바일 연동이 켜진 상태에서 PC와 사이트의 Gemini `5시간`·`주간` 두 행을 대조합니다. 사이트를 Refresh해 최신 스냅샷을 확인합니다.
3. PC의 트레이 메뉴에서 **Gemini 사용량 → 끄기**를 선택합니다. 사이트를 Refresh해 **꺼짐 · 앱에서 로그인하면 표시** 안내와 빈 Gemini 박스를 확인합니다. 사용량 수치와 계정 정보 없이 확인 결과만 기록합니다.
