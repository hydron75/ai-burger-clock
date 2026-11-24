# Windows 2.9.8 후보 준비

## 변경

- Claude 한도 상세에 CLI 로그인 종료 상한과 마지막 확인 시각을 표시합니다. 종료 상한 7일 전부터 박스에 경고를 표시하고, 로그아웃·만료 상태에서는 `claude auth login`을 안내합니다. [조회와 표시 규칙](ACCOUNT_QUOTAS.md#claude-cli-로그인-종료-예고-298-후보)
- 새 한도 조회가 성공하면 오래된 로그인 필요 표시를 해제합니다. 로그인 필요·미확인 상태 뒤 한도 조회가 성공했거나 사용자가 Refresh를 선택하면 인증 상태도 즉시 다시 확인합니다.
- Windows 트레이의 로그인 만료 알림은 기본 꺼짐입니다. 켜면 로그인 종료 상한당 한 번 기존 한도 알림 경로로 요청합니다. #172의 Mac 알림 연결도 #171에 포함했습니다. [Mac 확인 기록](MACOS_PORT.md#85-claude-cli-로그인-만료-예고의-mac-확인과-알림-연결-pr-171-위)
- 후보 준비 커밋은 `AiBurgerClock.csproj`의 Windows 버전 세 필드와 `README.md`·`CODE_GUIDE.md`를 갱신하고 이 문서를 추가합니다.

## 후보 검사와 신원

| 검사 | #172 병합 HEAD | 2.9.8 후보 |
|---|---|---|
| .NET SDK | `10.0.404` | `10.0.404` |
| 빌드 | Release 경고 0·오류 0 | Portable publish 경고 0·오류 0 |
| Shared.Tests | 291,675건 통과 | 291,675건 통과 |
| Windows self-test | 292,918건 통과 | 단일 EXE 292,918건 통과 |
| 고정 after 슬롯 smoke | 동일 바이너리 재실행 1,214건 통과 · 합성 PNG 75개 | 단일 EXE 1회 1,214건 통과 · 합성 PNG 75개 |

공용 Claude 검사 56건을 포함합니다. 임시 파일시스템에서 symlink fixture 1건을 건너뛰었습니다. 병합 HEAD 첫 smoke는 261자 PNG 출력 경로에서 GDI+ 저장 오류가 나서 종료 코드 1로 중단됐습니다. 같은 바이너리로 출력 경로를 줄인 재실행과 후보 smoke는 종료 코드 0입니다. 검사에 사용한 알림·조회는 합성이며 트레이 기록 증가 추세는 smoke를 2회 이상 실행해야 확인할 수 있습니다.

## Git 기준점과 백업

| 항목 | 값 |
|---|---|
| main 기준 | `6cc48d05707b4d808c0c337ff88899d8d38ffdaf` |
| #172 병합 HEAD | `a5add3575d3246a69d0d1afec4df1ddd3331439e` |
| Windows 2.9.8 후보 소스 | `7bd5fa1e0a248b8f51fcc68a153b474cb3cfc48a` |
| 이전 2.9.7 EXE 소스 | `4bf66112f4e44f58292edfee38213aa627699ecd` |
| 후보 EXE | `artifacts/release-2.9.8-preparation-20261124/candidate/AI Burger Clock.exe` |
| FileVersion | `2.9.8.0` |
| ProductVersion | `2.9.8+7bd5fa1e0a248b8f51fcc68a153b474cb3cfc48a` |
| 크기 | `31196557` bytes |
| SHA-256 | `388DDB824CF6C56AD198B98EB79F1A4EBC33BD9AE7196C5369FD818F68D29DE6` |
| Receipt | `artifacts/release-2.9.8-preparation-20261124/deployment-receipt.json` |

공식 생성기에 이전 2.9.7의 `candidate.json`을 사용했습니다. 생성한 receipt를 후보 버전·해시와 대조하고 System32에서 배포 도구와 같은 Git 저장소 지정 방식으로 두 커밋을 읽었습니다. 이후 커밋은 후보 검사 결과를 이 문서에 기록합니다. 로그는 후보 폴더의 `publish.txt`, `shared-tests.txt`, `candidate-self-test.txt`, `verification/`에 보관합니다.

배포 시 외부 도구가 실제 백업 폴더와 복원 지점을 만듭니다. Mac은 #172에서 별도로 검증했고 #171 최종 HEAD의 Mac 확인을 기다립니다.

## 외부 배포와 사용자 확인

1. 배포 승인 후 AI Burger Clock을 트레이에서 정상 종료하고 Codex 밖 MSI PowerShell 7에서 이 후보의 receipt로 `deploy-windows.ps1`을 실행합니다. 정상 결과는 `Succeeded=True`, 전 단계 `Passed`, `FileVersion=2.9.8.0`입니다. 배포 도구가 기존 EXE·DB·소스를 백업하고, 교체 이후 검사나 실행 요청이 실패하면 이전 EXE를 복원합니다. [외부 실행과 복원](WINDOWS_DEPLOYMENT.md)
2. 새 앱의 트레이와 상태·한도 화면을 엽니다. Claude 상세에서 로그인 상태와 마지막 확인을 보고 Refresh가 이를 갱신하는지 확인합니다. 인증 상태와 사용량의 실제 값은 남기지 않습니다.
3. 운영 배포와 실제 Windows 알림 표시 및 최종 HEAD의 Mac 검증은 확인 결과를 받은 뒤 기록합니다.
