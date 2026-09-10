<BEGIN_PROMPT>
# 작업: 문서 드리프트 제거 및 단일 인수인계 문서 체계

## 0. 먼저 읽을 것 (전부, 끝까지)
1. `AGENTS.md`, `PROJECT.md`, `README.md`, `VERSIONING.md`, `CHANGELOG.md`
2. `docs/` 아래 모든 `.md` 파일 목록과 각 파일의 첫 20줄
3. `docs/CODEX_HANDOFF_2026-09-05_KO.md`, `docs/CLAUDE_CODE_HANDOFF_2026-09-05_KO.md`, `docs/TASK.md`(있으면)
4. `harness/README.md`

## 1. 시작 절차
- `preflight.ps1` 실행, 출력 보고.

## 2. 목표
문서끼리 서로 다른 말을 하는 상태를 없앤다. 확인된 불일치:
- `PROJECT.md`는 기준선을 v0.4.0이라 하고 README는 0.7.0.
- `PROJECT.md`는 `scripts/verify.ps1`, `docs/TASK.md`, "Ponytail 사용 중단"을 언급하지만 `AGENTS.md`는 이를 모른다.
- 핸드오프 문서가 에이전트·날짜별로 분리되어 어느 것이 최신인지 불명확.
- README에 사용자 설명서와 버전별 개발 이력이 섞여 매우 길다.

## 3. MUST
1. **단일 인수인계 문서** `docs/HANDOFF.md`를 만든다. 내용: 현재 상태, 남은 작업, 알려진 미해결 문제(cadence PARTIAL, 종료 경계 capture, 403 격리 비재전송, 120 FPS 미보장, VR 실기 미검증, 혼자 연 멀티방 미확인 등), 사용자 정책. 기존 두 핸드오프 문서의 내용 중 아직 유효한 것만 옮기고, 옮긴 원본 문서 상단에 "→ docs/HANDOFF.md 로 통합됨 (날짜)" 한 줄을 추가한다. 원본은 삭제하지 않는다.
2. `AGENTS.md`의 "6. 문서 읽는 순서" 1·2번을 `docs/HANDOFF.md` 하나로 바꾼다. "4. 빌드·테스트·릴리스"에 `harness/scripts/verify.ps1`를 표준 게이트로 추가한다. 기존 `scripts/verify.ps1`가 있으면 01번 보고서의 비교표를 근거로 둘 중 하나를 표준으로 지정한다(사용자 결정이 없었으면 harness 쪽을 표준으로 하고 그 사실을 보고).
3. `PROJECT.md`를 현재 사실로 갱신한다: 기준선 v0.7.0, 작업 경로, 표준 게이트, 서버 범위 밖. `docs/TASK.md`가 없으면 그 언급을 삭제한다. "Ponytail" 언급은 무엇을 뜻하는지 저장소 내에서 찾아보고, 근거가 없으면 삭제하고 보고한다.
4. `README.md`를 둘로 나눈다:
   - `README.md`: 사용자용. 무엇인지, 설치·실행, 안전 경계, 현재 제한사항, 소스 빌드. 버전별 "0.4.2에서는…", "0.4.5 추가 기능…" 같은 이력 문장은 제거한다.
   - `docs/FEATURE_HISTORY_KO.md`: README에서 제거한 버전별 기능 설명을 옮긴다. CHANGELOG와 중복이면 CHANGELOG 링크로 대체한다.
   README 길이를 현재의 절반 이하로 줄이되, "안전 경계"와 "현재 제한사항"은 한 항목도 빼지 않는다.
5. `docs/` 파일이 20개를 넘으면 `docs/README.md`에 파일별 한 줄 설명 목록(index)을 만든다.
6. 모든 문서 변경 후 `check-versions.ps1`가 여전히 PASS인지 확인한다(README 패턴을 바꿨으면 `version-config.json`도 맞춘다).
7. `verify.ps1` GATE: PASS.

## 4. MUST NOT
- commit / tag / push / Release 금지.
- `src/`, `tests/`, `scripts/`, `installer/` 수정 금지. 문서와 `harness/scripts/version-config.json`만.
- 기존 문서를 삭제하지 않는다. 통합됨 표시만.
- "안전 경계", "현재 제한사항", AGENTS.md "절대 규칙"의 문장을 약화시키거나 삭제하지 않는다.
- 사실을 지어내지 않는다. 확인 안 되는 내용은 "확인 필요"로 남긴다.

## 5. 범위 밖
- 코드 변경 전부.

## 6. 완료 조건
- [ ] `docs/HANDOFF.md` 존재, AGENTS.md가 이를 가리킴
- [ ] PROJECT.md / README.md / VERSIONING.md의 버전·기준선·경로 언급이 서로 일치
- [ ] README 줄 수가 작업 전의 50% 이하
- [ ] `check-versions.ps1` PASS, `verify.ps1` GATE: PASS
- [ ] 보고서: `docs/reports/2026-09-10_docs-consolidation.md` — 작업 전후 README 줄 수, 통합한 문서 목록, 삭제한 언급과 근거

## 7. 보고
한국어.
<END_PROMPT>
