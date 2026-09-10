<BEGIN_PROMPT>
# 작업: {{작업 제목 한 줄}}

## 0. 먼저 읽을 것 (순서대로, 끝까지)
1. `AGENTS.md` — 절대 규칙 11개는 이 작업에서도 전부 유효하다.
2. `harness/README.md`
3. {{이 작업과 관련된 문서/파일 경로}}

## 1. 시작 절차
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\preflight.ps1` 를 실행하고 출력 전체를 보고서에 붙인다.
- 작업 트리에 미커밋 변경이 있으면 그것은 다른 에이전트 또는 사용자의 WIP다. 되돌리지 말고 그대로 두고, 보고서에 사실대로 적는다.
- 기준 커밋: {{HEAD 또는 태그. 예: v0.7.0}}

## 2. 목표
{{무엇을 달성해야 하는지. 사용자 관점의 결과로 적는다.}}

## 3. MUST
- {{반드시 해야 하는 것. 숫자·경로·버전은 정확히.}}
- 변경한 동작마다 테스트를 추가하거나 갱신한다.
- 작업 끝에 `.\harness\scripts\verify.ps1` 를 실행하고 `GATE: PASS` 를 확인한다. FAIL이면 고치고 다시 돌린다. 고칠 수 없으면 FAIL인 채로 보고한다.

## 4. MUST NOT
- commit / tag / push / GitHub Release 를 하지 않는다 (사용자가 이 프롬프트에서 명시적으로 요청하지 않는 한).
- `git reset --hard`, `git clean`, `git checkout -- .`, force-push 를 쓰지 않는다.
- {{이 작업에서 손대면 안 되는 파일/기능. 예: Compact 프로토콜, 업로드 gate, 서버 계약}}
- 통과하지 못한 검증을 PASS라고 쓰지 않는다. FAIL 또는 NOT RUN 으로 쓴다.
- 실제 AMS2 실행이 필요한 검증은 하지 않고 "NOT RUN (실게임 필요)" 로 남긴다.

## 5. 범위 밖
{{명시적으로 이번엔 안 하는 것}}

## 6. 완료 조건
- [ ] {{조건 1}}
- [ ] {{조건 2}}
- [ ] `verify.ps1` GATE: PASS (또는 FAIL 사유가 보고서에 있음)
- [ ] 보고서를 `harness/templates/REPORT.md` 형식으로 `docs/reports/{{YYYY-MM-DD}}_{{slug}}.md` 에 작성

## 7. 보고
보고서는 한국어로 쓴다. 실제/라이브 증거와 fixture/데모 증거를 구분해서 적는다.
<END_PROMPT>
