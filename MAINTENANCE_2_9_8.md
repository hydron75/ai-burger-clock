# Windows 2.9.8 후보 준비

## 변경

- Claude 한도 상세에 CLI 로그인 종료 상한과 마지막 확인 시각을 표시합니다. 종료 상한 7일 전부터 박스에 경고를 표시하고, 로그아웃·만료 상태에서는 `claude auth login`을 안내합니다. [조회와 표시 규칙](ACCOUNT_QUOTAS.md#claude-cli-로그인-종료-예고-298-후보)
- 새 한도 조회가 성공하면 오래된 로그인 필요 표시를 해제합니다. 로그인 필요·미확인 상태 뒤 한도 조회가 성공했거나 사용자가 Refresh를 선택하면 인증 상태도 즉시 다시 확인합니다.
- Windows 트레이의 로그인 만료 알림은 기본 꺼짐입니다. 켜면 로그인 종료 상한당 한 번 기존 한도 알림 경로로 요청합니다. #172의 Mac 알림 연결도 #171에 포함했습니다. [Mac 확인 기록](MACOS_PORT.md#85-claude-cli-로그인-만료-예고의-mac-확인과-알림-연결-pr-171-위)
- 후보 준비 커밋은 `AiBurgerClock.csproj`의 Windows 버전 세 필드와 `README.md`·`CODE_GUIDE.md`를 갱신하고 이 문서를 추가합니다.

## 후보 검사와 신원

검사 결과와 EXE 신원은 후보 빌드가 끝난 뒤 이 문서에 기록합니다.

## 외부 배포와 사용자 확인

1. 배포 승인 후 AI Burger Clock을 트레이에서 정상 종료하고 Codex 밖 MSI PowerShell 7에서 이 후보의 receipt로 `deploy-windows.ps1`을 실행합니다. 정상 결과는 `Succeeded=True`, 전 단계 `Passed`, `FileVersion=2.9.8.0`입니다. 배포 도구가 기존 EXE·DB·소스를 백업하고, 교체 이후 검사나 실행 요청이 실패하면 이전 EXE를 복원합니다. [외부 실행과 복원](WINDOWS_DEPLOYMENT.md)
2. 새 앱의 트레이와 상태·한도 화면을 엽니다. Claude 상세에서 로그인 상태와 마지막 확인을 보고 Refresh가 이를 갱신하는지 확인합니다. 인증 상태와 사용량의 실제 값은 남기지 않습니다.
3. 운영 배포와 실제 Windows 알림 표시 및 최종 HEAD의 Mac 검증은 확인 결과를 받은 뒤 기록합니다.
