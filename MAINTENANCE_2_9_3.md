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
- /api/quota·실제 계정 값·토큰은 읽거나 기록하지 않았습니다. 사이트 주소는 이 저장소에 적지 않습니다.

## 후보·검사·receipt

후보는 `artifacts/release-2.9.3-preparation-20261114/candidate/`에 준비합니다.
build·bin/단일 EXE self-test·고정 after 슬롯 smoke·신원·receipt 확인 결과는 실행 후 이 절에 기록합니다.
화면 검사가 일회성으로 실패하면 같은 HEAD·바이너리에서 한 번만 재실행하고 두 결과를 보존합니다.

이전 신원은 `artifacts/release-2.9.2-preparation-20261114/candidate.json`이며 공식 receipt 생성기로 읽습니다.
기존 receipt·.run·백업과 운영 dist·DB·등록을 바꾸지 않습니다. 병합 뒤 사용자가 외부 MSI PowerShell에서 사전 확인 → 정상 종료 → 배포 명령 순서로 진행합니다.
