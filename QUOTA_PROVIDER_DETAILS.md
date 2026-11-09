# 한도 Provider 상세 · 공통 틀과 Windows 고정 폭 (Draft)

이번 PR은 `QuotaPanelModel.ProviderDetail`의 문구 배치·빈 구역과 Windows 검사만 변경합니다. 버전·패키지·조회 주기·회복/조기 초기화 알림·송신 계약·배포 EXE는 변경하지 않습니다.

## 공통 상세 틀

1. `{이름} 한도 · 모든 시각 KST`
2. `범위:` 한 줄
3. `리셋·한도 복원 예정`
4. `시간·사용량 비교`
5. 최근 조기 초기화 기록이 있으면 기존 원문 그대로
6. `조회 상태`: 마지막 성공·다음 조회·최근 시도, 오류·캐시 오류는 이 구역 끝
7. `조회 주기`: 기존 수치를 같은 형식 한 줄로 표시. `참고:`는 Gemini 0% 창 제외 또는 xAI 예약 최소 10분이며 다른 Provider는 `—`
8. 기존 공식 CLI 응답 수신 시각 안내 한 줄

조회 값이 없거나 창 목록이 비어도 리셋·비교 구역을 유지하고 `조회 값 없음`으로 채웁니다. 조회 상태의 시각이 없으면 `—`, 개별 리셋 시각이 없으면 기존대로 `제공되지 않음`입니다. 기존 리셋 시각·비교 수치·해석·오류 원문은 바꾸지 않습니다. `QuotaLine`의 Detail 외 필드(Text·ID·이름/값·행 접근성·비교 필드 등)와 카드의 별도 Scope 행은 변경하지 않습니다.

## 범위 문구의 근거

- ChatGPT·Gemini: 기존 `CodexScopeDetail`·`GeminiScopeDetail` 원문 유지.
- Claude: `Claude Code CLI의 /usage 응답으로 받은 구독 사용 한도입니다.` 실제 `AccountQuotaClient`는 `claude --print --output-format stream-json --verbose … -- /usage`로 구조화된 구독 한도만 받습니다. 모델 턴·토큰·비용 검증과 보호 인자는 그대로입니다. 공식 [사용량 문서의 /usage 설명](https://code.claude.com/docs/en/costs#using-the-usage-command)도 구독 사용량 표시를 설명합니다.
- xAI: `Grok CLI의 ACP _x.ai/billing 응답으로 받은 주간 크레딧 한도입니다.` 실제 명령은 `grok --no-auto-update agent --no-leader stdio`, RPC는 `initialize → authenticate(cached_token, headless) → _x.ai/billing`입니다. `config.creditUsagePercent`·`currentPeriod`의 검증된 주간 데이터만 정규화합니다. 공식 [Headless & Scripting](https://docs.x.ai/build/cli/headless-scripting)은 ACP·headless 인증·업데이트 억제를 설명하며, 구체적인 billing 요청/정규화는 이 저장소의 `GrokQuotaProtocol`·`AccountQuotaParsers` 계약을 근거로 합니다. 별도 계정 API나 모델 요청을 추가하지 않습니다.

## Windows 폭 점검

기존 `AccountQuotaView.MeasureDetail`도 이미 모든 Provider에 320 DIP 외곽 폭을 적용하며, 작업 영역이 더 좁으면 화면 안에 들도록 줄입니다. 높이만 본문으로 측정하고 긴 상세에는 기존 세로 스크롤을 씁니다.

다만 기존 실제 팝업 검사는 ChatGPT·Claude와 별도 Gemini 경로만 호출하고 xAI를 제외했습니다. 측정 함수의 DPI 검사는 ChatGPT 본문만 사용했습니다. 이번에는 네 Provider × 정상/이전/없음/오류 16상태를 실제 팝업으로 확인하고, 각 상태의 96/144/192 DPI 측정과 긴→짧은 본문 전환도 검사합니다.

변경 전 합성 16건의 외곽 폭은 192 DPI에서 모두 640px(320 DIP)이었습니다. **보고된 실제 외곽 폭 차이는 이 조건에서 재현되지 않았으며 원인을 확정하지 않았습니다.** 재현되지 않은 크기 보정을 추가하지 않고 기존 폭·배치·스크롤 코드를 유지합니다. Provider별 범위 줄/빈 구역의 유무로 내용과 높이가 달랐던 것은 새 공통 틀로 정리합니다.

## 검증·전후 화면

전후 화면과 최종 검사 결과는 PR 본문에 첨부합니다. 모두 합성 데이터이며 실제 계정 화면·한도 값은 포함하지 않습니다. 공통 16상태의 전체 상세와 구역 순서를 고정하고, Detail을 지운 나머지 모든 필드는 변경 전 합성 해시와 대조합니다. 조기 초기화 기록은 기존 원문·삽입 위치를 확인합니다.

## Mac·사이트와 다음 배포

- Mac 파일은 수정하지 않습니다. Mac 담당은 공통 상세 16상태의 동일 문구, 기존 NSView ToolTip 경로의 Provider별 폭 고정, 긴 내용의 화면 내 접근·VoiceOver, 공통 검사·`Mac/build.sh`·native smoke를 확인해야 합니다. Mac의 native ToolTip 폭 제어에 전환이 필요하면 담당자가 별도로 반영합니다.
- **모바일 사이트 반영 불필요:** 현재 한도 박스에는 이 Provider 상세 팝업이 없으며 송신 계약에도 Detail이 없습니다. 사이트 공식 상태 카드의 상세는 별도 기능입니다. 행의 값·접근성·비교 필드는 그대로라 이번 문구를 TypeScript로 옮기지 않습니다.
- 다음 버전과 함께 배포합니다. 이번에는 병합·운영 배포·사이트 배포를 하지 않습니다.
