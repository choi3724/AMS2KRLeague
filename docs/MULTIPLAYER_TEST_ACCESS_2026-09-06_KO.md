# 수정 후보 실제 테스트 접근 / 사전 관측

2026-09-06 KST. 사용자 요청: 멀티플레이 방에서 직접 키보드 테스트.

## 수행 범위와 차단 원인

- 실행 중인 PID 9276의 경로가 `src/AMS2LeagueClient/bin/Archive403Hotfix/AMS2LeagueClient.exe`인 것을 확인했다. 시작 시각 13:15:08. 이전 후보를 재실행하지 않았다.
- Computer Use 스킬의 지원 API로 AMS2 게임 창을 선택했으나 화면 접근 요청이 `Computer Use app approval timed out`으로 종료됐다.
- 게임 화면을 받지 못했으며 키 입력, 주행, 방 설정 변경, 레이스 시작/재시작은 수행하지 않았다. 별도 입력 우회 수단을 사용하지 않았다.
- 이 보고서의 데이터는 이미 실행 중인 후보가 13:16경 생성한 짧은 Practice 기록을 읽기 전용으로 확인한 결과다. 에이전트가 직접 주행한 테스트나 5분 이상 멀티플레이 E2E PASS가 아니다.

## 후보가 이미 생성한 짧은 기록

근거 루트: `%LOCALAPPDATA%/AMS2KRLeague/`.

- 로그: `logs/client-20260906-131508.log`.
- 세션 디렉터리: `activity/future-telemetry/sessions/8bea65914b6926ae4417b345ab275f02`.
- sessionId: `capture-8ebf5762d13a408f9a79e268638dad84`.
- attemptId: `attempt-881f4b15bf0243f190dbe322ece5d116` (1).
- witnessId: `witness-c2079b4a413d4eb6a3c2203a331cffb9`.
- 로컬 관측 구간: 13:16:00.862 Practice 진입 → 13:16:04.557 FrontEnd 복귀. 약 3.7초.
- 로컬 attempt ledger: `closeRequested=true`, `finalizeAcknowledged=true`, `durableAck=true`, `completeness=COMPLETE`, `knownLossCount=0`.
- acceptedWorkUnits: metadata 4, story 2, replay 71, incident 71, private 0. Incident 작업 수는 실제 Incident chunk 존재를 뜻하지 않는다.
- Compact 6개: SESSION 810 B, STORY 762 B, REPLAY 916 B, TRACK_GEOMETRY 98 B, INTEGRITY 95 B + 98 B. 합계 **2,779 B**.
- 별도 legacy metadata gzip 3,042 B. Compact와 구분하며 서버 저장량으로 간주하지 않는다.
- 업로드 sidecar 7개 모두 `SENT`, `attemptCount=1`. 로그 attempted 4+3 / sent 4+3.
- Witness payload 12,722 B, SHA256 `2f16e2e6becc41e82eb45696c6e6e995a6c4d20a22fda70ef604167edb119541`.
- Witness queue `b56c2316b7b40bc5bd799f363ad924f821f620e6c2afd5aa6850cf41030a1eab`: `SENT`, HTTP **201**, `STORED`, attemptCount **1** (13:16:05).
- 이전 HTTP 403 queue `bd2c9f95bde410ab2f9d8f158838c3f7f479f2de2ca3f76d439fa5f4cd0b8f75`: 여전히 `QUARANTINED`, attemptCount **1**. 재전송/변경하지 않았다.

`COMPLETE`는 짧은 로컬 관측 구간의 archive 상태다. 전체 경기 포착, 정상 완주, 멀티플레이 여부, 서버 보존 완료를 보증하지 않는다. 새 Witness의 201은 해당 요청의 전송 성공 근거이며 이전 403 원인이 해결됐다는 증명도 아니다.

## 판정 / 다음 단계

| 항목 | 결과 |
|---|---|
| 수정 후보 실행 경로 | 확인 |
| 기존 짧은 Practice 로컬 저장 / finalize | 확인 |
| 동일 짧은 기록 Client 전송 ACK | 확인 |
| 직접 키보드 주행 | NOT RUN — 앱 접근 승인 시간 초과 |
| 5분 이상 새 멀티플레이 검증 | NOT RUN |
| Server GET / decode / 원본 hash 대조 | NOT RUN |
| 최종 E2E 판정 | YELLOW — 미완료 |

사용자가 게임 앱 접근을 승인하면 현재 창을 다시 관측한 후 직접 테스트를 계속한다. 기존 격리 항목 재전송, 공식 결과 생성, 서버 배포/DB 변경은 범위 밖이다. 이번 확인에서는 제품 코드를 변경하지 않았고 빌드/테스트를 재실행하지 않았다. 직전 후보 검증은 별도 `ARCHIVE_FAILURE_WITNESS_403_2026-09-06_KO.md`를 참고한다. 커밋/릴리스는 하지 않았다.
