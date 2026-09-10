<BEGIN_PROMPT>
# 작업: 테스트 러너를 기계가 읽을 수 있게 표준화 (자체 Program.cs → xunit + dotnet test)

## 0. 먼저 읽을 것
1. `AGENTS.md` — 특히 "WPF 애니메이션은 HasAnimatedProperties/base value로 검증" 규칙
2. `tests/AMS2LeagueClient.Tests/` 와 `tests/AMS2LeagueActivity.Tests/` 전체. 테스트가 어떻게 등록·실행·집계되는지, WPF 테스트가 STA 스레드를 어떻게 만드는지, fixture를 어디서 읽는지.
3. 두 `.csproj`
4. `harness/scripts/verify.ps1`, `.github/workflows/`

## 1. 시작 절차
- `preflight.ps1` 실행.
- 작업 전 `verify.ps1`를 한 번 돌려 현재 숫자를 기록한다: Client N/N, Activity N/N. 이 숫자가 작업 후에도 같아야 한다.

## 2. 목표
지금은 `dotnet run`으로 자체 Program.cs를 실행하고 사람이 "113/113"을 출력에서 읽는다. 이것을 `dotnet test`로 실행되고 TRX 결과 파일이 나오며, 개별 테스트를 이름으로 골라 돌릴 수 있게 바꾼다. **테스트 개수와 검증 내용은 하나도 줄지 않는다.**

## 3. MUST
1. 먼저 현재 구조를 보고서에 정리한다: 테스트 등록 방식, 총 개수(두 스위트 각각), STA/WPF 테스트 개수, 외부 fixture 파일 의존, 실패 시 종료 코드가 0이 아닌지.
2. **단계 A (안전망, 코드 변경 최소):** 자체 러너가 마지막에 `TEST-SUMMARY name=<suite> passed=<n> failed=<n> total=<n>` 한 줄을 고정 형식으로 출력하고, failed>0 이면 종료 코드 1을 반환하게 한다. `harness/scripts/verify.ps1`의 `$TestSummaryRegex`를 이 형식에 맞춘다. 이 단계만으로도 GATE가 신뢰할 수 있게 된다. 단계 A를 끝내고 `verify.ps1` PASS를 확인한 뒤 단계 B로 간다.
3. **단계 B (xunit 전환):** 각 테스트 프로젝트에 `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`를 추가하고 기존 테스트를 `[Fact]`/`[Theory]`로 옮긴다.
   - WPF 테스트는 STA가 필요하다. 기존 방식(직접 STA 스레드)을 유지하는 헬퍼를 만들어 각 Fact에서 호출하거나, 검증된 STA xunit 패키지를 쓴다. 어느 쪽이든 기존과 동일한 스레드 모델임을 확인한다.
   - 테스트 이름은 기존 러너의 이름을 그대로 쓴다(추적 가능성).
   - 기존 Program.cs 러너는 단계 B가 GREEN이 된 뒤에만 제거한다. 그 전엔 둘 다 유지.
4. `dotnet test --logger trx --results-directory harness/reports` 로 TRX가 생성되는지 확인한다.
5. `harness/scripts/verify.ps1`의 테스트 단계를 `dotnet test`로 바꾸고 TRX에서 passed/failed/total을 읽도록 수정한다. `dotnet run` 방식은 폴백으로 남기지 않는다(혼동 방지).
6. `README.md` 소스 빌드 절, `AGENTS.md` 4절, `scripts/build-release.ps1`, CI 워크플로의 테스트 명령을 `dotnet test`로 갱신한다. `build-release.ps1`은 이번 작업에서 예외적으로 수정 허용(테스트 호출 줄만).
7. 작업 후 총 테스트 개수가 작업 전과 같거나 많아야 한다. 적으면 FAIL.
8. `verify.ps1` GATE: PASS.

## 4. MUST NOT
- commit / tag / push / Release 금지.
- 테스트의 검증 내용(assert)을 약화·삭제하지 않는다. 옮기기만.
- `src/` 코드 수정 금지. 테스트를 위해 `src/`를 바꿔야 한다면 멈추고 보고한다.
- `dotnet test`가 통과 못 하는 테스트를 `[Skip]` 처리하지 않는다. 실패 원인을 보고한다.
- fixture 파일 이동/수정 금지.
- reset/clean/force-checkout 금지.

## 5. 범위 밖
- 새 테스트 추가, 커버리지 측정.

## 6. 완료 조건
- [ ] 단계 A 완료 및 GATE PASS 기록 (단계 B 실패 시에도 A는 남아 있어야 함)
- [ ] `dotnet test .\AMS2KRLeague.sln -c Release --no-build` 로 두 스위트 모두 실행됨
- [ ] TRX 생성, verify.ps1이 TRX에서 숫자를 읽음
- [ ] 작업 전후 테스트 개수 표: Client 전/후, Activity 전/후
- [ ] `verify.ps1` GATE: PASS
- [ ] 보고서: `docs/reports/2026-09-10_test-runner.md`

## 7. 보고
한국어. 단계 A/B를 나눠 쓴다. STA 처리 방식을 명시한다.
<END_PROMPT>
