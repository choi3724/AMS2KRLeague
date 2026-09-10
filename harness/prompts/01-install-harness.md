<BEGIN_PROMPT>
# 작업: 개발 하네스 설치 및 현재 코드 기준선 검증

## 0. 먼저 읽을 것 (순서대로, 끝까지)
1. `AGENTS.md` — 절대 규칙 11개 전부 유효.
2. `harness/README.md`
3. `harness/scripts/verify.ps1`, `check-versions.ps1`, `check-secrets.ps1`, `preflight.ps1`, `version-config.json`
4. `scripts/verify.ps1` (기존에 있으면), `scripts/build-release.ps1`, `.github/workflows/` 아래 모든 파일
5. `tests/AMS2LeagueClient.Tests/Program.cs`, `tests/AMS2LeagueActivity.Tests/Program.cs` — 테스트 출력이 어떤 형식인지 확인

## 1. 시작 절차
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\preflight.ps1` 실행, 출력 전체를 보고서에 붙인다.
- 미커밋 변경이 있으면 되돌리지 말고 보고서에 적는다.
- 기준 커밋: 현재 HEAD (v0.7.0).

## 2. 목표
`harness/` 폴더의 스크립트들은 이 저장소를 직접 실행해 보지 않고 작성된 초안이다. 이것을 실제 환경에서 동작하게 고치고, 현재 HEAD가 게이트를 통과하는지 확인한다. 이후 모든 작업은 이 게이트를 기준으로 검증한다.

## 3. MUST
1. `harness/scripts/version-config.json`의 파일 경로와 정규식을 실제 파일 내용에 맞게 수정한다. 특히:
   - `installer/AMS2LeagueOverlay.iss`에서 버전이 선언된 실제 줄
   - `scripts/build-release.ps1`에서 기본 버전이 선언된 실제 줄
   - `ClientStatusViewModel`(정확한 경로 확인)에 버전 문자열이 하드코딩되어 있는지. 있으면 그 사실만 보고하고 이번엔 고치지 않는다.
   - `release/RELEASE_NOTES_KO.md`가 없으면 항목을 삭제한다.
2. `harness/scripts/verify.ps1`의 `$TestSummaryRegex`를 두 테스트 프로젝트의 실제 출력 형식에 맞게 수정한다. 테스트가 실패했을 때 프로세스 종료 코드가 0이 아닌지 확인하고, 0이면 그 사실을 보고한다(고치는 것은 04번 작업).
3. `harness/scripts/check-secrets.ps1`를 돌려 나온 HIT를 전부 검토한다. 실제 비밀이면 즉시 보고하고(값은 보고서에 쓰지 않는다), 설명용 예시나 의도된 문서 경로면 `secret-allowlist.txt`에 추가한다.
4. 기존 `scripts/verify.ps1`/`verify.sh`가 있으면 `harness/scripts/verify.ps1`와 비교해 어느 쪽이 무엇을 더 검사하는지 표로 정리한다. 두 개를 하나로 합칠지 여부는 사용자 결정 항목으로 남긴다.
5. `.github/workflows/`의 CI가 `harness/scripts/verify.ps1`와 같은 항목(restore, Release build, 테스트 2종)을 도는지 확인하고 차이를 보고한다. CI 파일은 수정하지 않는다.
6. `harness/reports/`를 `.gitignore`에 추가한다.
7. 저장소 루트에 `global.json`이 없으면 만든다: `{ "sdk": { "version": "8.0.424", "rollForward": "latestPatch" } }`. 이미 있으면 내용을 보고한다.
8. 마지막에 `.\harness\scripts\verify.ps1`를 실행해 `GATE: PASS`를 확인한다.

## 4. MUST NOT
- commit / tag / push / Release 하지 않는다.
- `src/` 아래 코드를 수정하지 않는다. 이 작업은 하네스와 설정 파일만 다룬다.
- 기존 `scripts/*.ps1`, `.github/workflows/*`를 수정하지 않는다.
- 테스트 코드를 수정하지 않는다.
- reset/clean/force-checkout 금지.
- 통과 못 한 검증을 PASS로 쓰지 않는다.

## 5. 범위 밖
- 버전 단일화(02), 문서 정리(03), 테스트 러너 교체(04)는 별도 작업이다.

## 6. 완료 조건
- [ ] `preflight.ps1`, `check-versions.ps1`, `check-secrets.ps1`, `verify.ps1` 네 개가 모두 오류 없이 끝까지 실행됨
- [ ] `check-versions.ps1`의 모든 required 항목이 OK 또는 실제 불일치 FAIL(패턴 매치 실패가 아님)
- [ ] `verify.ps1` GATE: PASS, 테스트 숫자가 `passed=N total=N`으로 파싱됨(-1이 아님)
- [ ] `global.json` 존재
- [ ] `harness/reports/`가 `.gitignore`에 있음
- [ ] 보고서: `docs/reports/2026-09-10_harness-install.md` (형식: `harness/templates/REPORT.md`)

## 7. 보고
한국어. 실제 실행 출력(숫자)을 그대로 붙인다. 수정한 정규식은 전후 비교로 보여준다.
<END_PROMPT>
