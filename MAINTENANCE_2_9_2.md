# AI Burger Clock 2.9.2 — Review Agents 필수 목록 보정 후보

2026-11-14 KST. main `ba23cb21acc227988a78f9123a24fda430efb6ec`에서 준비합니다.
이번 버전 PR은 Windows `Version`·`AssemblyVersion`·`FileVersion`과 문서만 변경합니다.
공통/Windows C#·Mac 파일·패키지·배포 스크립트는 바꾸지 않습니다.

## 2.9.1 이후 포함 변경

- **#145:** xAI 호환 JSON 필수 11개 중 `bugbot` 대신 `2x2chyqwmkzl`(Review Agents, Cursor)을 요구합니다. RSS의 `bugbot`·`review-agents` 대응과 필수 항목 누락 CHECK를 유지하며 Agent SDK는 추가하지 않습니다. 출처·조회·한도·알림 조건은 같습니다. [목록·변경 이력](XAI_STATUS.md).
- 세 호환 JSON의 sunset 2027-01-31과 대체 경로/전환 방향을 12월 중순까지 정할 후속 항목을 BACKLOG에 유지합니다. 이번 버전에서 API 전환은 하지 않습니다.

## 사이트 선행 배포

- #145 병합 main을 기준으로 준비본을 **사이트 버전 14**로 비공개 배포했습니다. 소스 커밋은 `f31712e68434a3c6e4c2706a06ad23ed14670d78`, 화면/API 기준 커밋은 **`ba23cb2`**입니다. 기존 소유자 전용 범위를 유지했습니다.
- 최신 main의 실제 공통 C# 원본에서 생성한 합성 상태 **143사례·164단계**가 일치했습니다. 전체 사이트 합성 검사 **85/85**, 타입 검사·로컬 빌드·ESM 산출물 검사가 통과했습니다. 기존 시간표 19,972·권고 12·신선도/실패 47건 회귀도 통과했습니다.
- **2026-11-14 11:16:55 KST**, 배포된 HTML과 상태 API는 각각 HTTP 200이며 기준 커밋이 맞았습니다. 실제 Worker의 xAI reading을 사이트와 같은 판정기로 계산한 결과 **Operational / 누락 사유 없음**입니다. 아이폰의 기존 localStorage 상태까지 확인한 결과는 아닙니다.
- `/api/quota`와 실제 계정 값은 읽지 않았습니다. 한도 API·DB·수신 계약·화면은 바꾸지 않았으며 사이트 주소·토큰·실제 한도 값은 기록하지 않습니다.

## 후보·검증·외부 배포

후보 빌드·self-test·최종 smoke·receipt 결과를 완료 뒤 아래에 기록합니다.
SDK 선택은 준비 시 `dotnet --version`으로 확인한 **10.0.404**입니다.
후보는 `artifacts/release-2.9.2-preparation-20261114/candidate/`에 만들고 운영 dist는 바꾸지 않습니다.
이번 smoke에서 기존 휠 검사가 다시 실패하면 재실행하지 않고 로그와 원인 조사 BACKLOG 항목을 남깁니다.

실제 앱 교체·백업·등록 확인·실행은 버전 PR 병합 뒤 사용자가 Codex 밖 MSI PowerShell로 수행합니다.
이전 2.9.1 신원은 기존 receipt의 Candidate를 공식 생성기의 `-PreviousIdentityPath`로 읽습니다.
과거 receipt·`.run`·완료/미완료 백업·기본/LocalCache DB는 그대로 보존합니다.
