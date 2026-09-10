# {{작업 제목}} — 작업 보고서

- 날짜: {{YYYY-MM-DD}}
- 에이전트: {{Codex / Claude Code / ...}}
- 기준 커밋: {{sha 또는 tag}} → 작업 후 HEAD: {{sha}} (커밋하지 않았으면 "미커밋, 작업 트리에 있음")

## 1. 한 줄 요약
{{사용자가 이 줄만 읽어도 되는 결론}}

## 2. 검증 게이트
```
GATE: {{PASS / FAIL}}
build:   errors={{n}} warnings={{n}}
test:Client   {{passed}}/{{total}}  exit={{n}}
test:Activity {{passed}}/{{total}}  exit={{n}}
versions: {{PASS/FAIL}}   secrets: {{PASS/FAIL}}
report: harness/reports/verify-{{stamp}}.md
```
증거 구분:
- 실제/라이브: {{실게임, 실서버로 확인한 것. 없으면 "없음"}}
- fixture/데모: {{테스트 fixture, 데모 모드로 확인한 것}}
- NOT RUN: {{실행하지 못한 검증과 이유}}

## 3. 한 것
{{변경 내용. 파일별로 짧게. 무엇을 왜.}}

## 4. 하지 않은 것 / 범위 밖으로 남긴 것
{{프롬프트에 있었지만 못 한 것, 의도적으로 안 한 것. 비어 있으면 "없음"}}

## 5. 발견한 문제 (이번 작업 범위 밖)
{{작업 중 눈에 띈 다른 문제. 고치지 않았음을 명시}}

## 6. 사용자 결정이 필요한 항목
{{정책·기본값·프로토콜에 영향을 주는 선택지. 각 선택지의 장단점}}

## 7. 다음 단계 제안
{{후임 에이전트가 바로 시작할 수 있는 다음 작업 1~3개}}

## 8. preflight 출력 (원문)
```
{{preflight.ps1 출력}}
```
