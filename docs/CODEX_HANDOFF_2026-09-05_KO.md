# Codex 인수인계 — 0.3.0 이후 남은 작업과 작업 방식

작성일: 2026-09-05 KST
작성: Claude Code
대상: 이 저장소를 이어서 작업할 Codex(또는 다른 에이전트)
공통 규칙: 저장소 루트 `AGENTS.md`를 먼저 읽는다. 이 문서는 그 규칙 위에서 "지금 어디까지 왔고 무엇을 어떻게 해야 하는지"를 적는다.

## 1. 현재 기준점

```text
Release: v0.3.0 (2026-09-05, Latest)
직전:    v0.2.3-beta.3 (HEAD 9303e33, 사용자 실게임 확인 완료)
안정 기준선: v0.2.2 (변경 금지)
Cafe24: Application 1.6.0 / DB schema 15 / release 20260902-001 (0.3.0에서 변경 없음)
```

0.3.0에 들어간 것:

1. F1 중계 스타일 오버레이 애니메이션 (Timing Tower, 전후방, 세션, 랩타임, 이벤트 카드, Race Control)
2. 오버레이별 켜기/끄기 상시 토글 + 즉시 저장 + 모두 켜기/끄기
3. Compact 리플레이 downsampling 상수 4종의 옵션화와 world cadence 기본값 5,000 → 500 ms
4. 위 항목의 테스트 11개(Client 10, Activity 1), 보고서 3편, 실측 도구 2종(`work/`, gitignored)

검증 상태(릴리스 시점): Release build 0/0, AMS2LeagueClient.Tests 64/64, AMS2LeagueActivity.Tests 97/97, `--demo-events` 스모크 예외 0. **실게임 시각 확인은 미실시**.

## 2. 반드시 알아야 할 결정과 근거

### world cadence 500 ms

- 5,000 ms에서는 서버 원본 기준 차량이 샘플 사이 중앙값 235 m를 이동해 2D 리플레이가 불가능했다.
- 500 ms의 비용은 추정이 아니라 제품 변환 코드로 60분/32대 fixture를 재인코딩해 측정했다: 리플레이 wire 302,317 → 860,539 B(+184.6 %). 실제 리그 세션은 약 +53 %, 레이스 attempt는 +21 %.
- P024 512 KiB fixture 제품 목표는 초과, 1 MiB 한계 안. 이 값은 사용자가 0.3.0 릴리스 지시로 수용했다.
- 후속 과제: `tools/AMS2CompactProof`의 `WriteReplay`는 아직 5,000 ms 고정 선택 로직이라 gate 수치(465,279 B)가 제품 경로와 다르다. 제품 경로(`CompactTelemetryChunkStore`)로 gate를 다시 재고 `docs/COMPACT_SIZE_BENCHMARK.md`/`P024_RELEASE_GATE_REPORT.md`를 갱신해야 한다.

### 애니메이션 제약

- 행 전체 opacity 애니메이션 금지(테스트 `Status changes never dim active rows`, `Position change flashes row and rolls number`가 고정).
- DataTemplate 안에서 선언한 transform은 WPF가 frozen으로 공유한다. 애니메이션 전에 `EnsureTranslate/EnsureScale`로 복제한다(안 하면 `InvalidOperationException: 개체가 봉인…`).
- 테스트에서 애니메이션 값은 다음 TimeManager 틱 전에 갱신되지 않는다. `HasAnimatedProperties`와 `GetAnimationBaseValue`로 검증한다.
- 이벤트 카드/Race Control의 종료 애니메이션은 `SetViewModel`이 반환하는 시간만큼 `OverlayWindow`가 창을 유지한다(`_eventExitDeadline`, `_raceControlExitDeadline`). 20 Hz 틱이 슬라이드 아웃을 끊지 않게 하기 위한 것이다.

### 동시 편집

- 2026-09-05에 Claude Code 세션과 Codex 세션이 같은 작업 트리를 동시에 편집했다. Codex가 `TelemetryArchiveOptions.DefaultReplayWorldIntervalMs`를 500으로 바꾸고 테스트에 기본값 고정 단언을 추가했다. 이런 변경은 되돌리지 말고 보고서에 사실대로 적는다. 편집 전 `git status --short`와 mtime을 본다.

## 3. 남은 작업 (우선순위 순)

### A. 실게임 검수 (사용자와 함께)

- 타워 빌드 stagger(38 ms × 행), 추월 플래시 강도(0.5), 퍼플 스윕(0.62)이 가독성을 해치지 않는지.
- 세션 전환(예선 → 레이스) 첫 프레임에서 여러 행이 동시에 플래시하면 `PlayerOverlayCoordinator`의 `SESSION_TRANSITION` 로그 시점에 `OverlayHudView`의 트래커 `Reset()`을 호출하는 경로를 추가한다(현재 미연결).
- 수정 후 재검증: Client 64 + Activity 97 테스트, 데모 스모크(`--demo-events --auto-exit-seconds 16 --log-dir <dir>`), 로그에 `EXCEPTION` 없음.

### B. 리플레이 업로드량 최적화

1. 세션 타입별 cadence: Practice/Qualifying은 5,000 ms 유지, Race만 500 ms. 예선이 증가분의 60 %를 차지한다(예선 world row 9.07 B, 레이스 4.43 B). 구현 위치는 `FutureTelemetryCaptureRuntime`의 archive factory에서 attempt 시작 시 세션 타입에 따라 `TelemetryArchiveOptions.ReplayWorldIntervalMs`를 결정하는 것이다. chunk store는 attempt 단위로 생성되므로 attempt 중간 변경은 반영되지 않는다.
2. `INCIDENT_TRACE`가 실제 세션 wire의 78 %다(S2 race 111,841 B / 143,147 B, 16,158 rows). trigger(`CRASH_STATE_CHANGE`, `POSITION_CHANGE` magnitude 등) 발생 빈도를 실제 로그로 점검해 burst 수를 줄이면 리플레이 증가분을 상쇄할 수 있다.
3. gate 재측정: `work/replay-cadence-cost` 결과를 근거로 `tools/AMS2CompactProof` 선택 로직을 제품 경로와 맞추고 P024 문서를 갱신한다.

### C. cadence miss 원인 분리 (인수인계 문서 12절의 기존 과제)

- 실제 세션 loss ledger의 cadence miss(S1 7,909, S2 6,014)는 `DRIVER_TELEMETRY`와 `INCIDENT_TRACE`에서만 발생했고 `PARTICIPANT_REPLAY`는 0이다. 20 Hz 게이트(50 ms)가 SHM 30 Hz 폴링 지터와 어긋나는지, UI 스레드 점유 때문인지 분리한다. `LocalDurableTelemetryArchive.TakeDue`와 `ProcessFrame`의 타이밍 로그를 추가해 실제 세션에서 측정한다.

### D. 서버/웹 측 리플레이 소비

- 서버 decoder는 irregular-time block을 이미 지원하므로 프로토콜 변경 없음. 웹 2D 리플레이는 world 500 ms 샘플을 60 fps로 보간하되 보간 프레임을 persisted fact와 구분해 표시한다(`REPLAY_STREAM_CONTRACT.md` 4절).
- Track geometry(20 m bin)는 cadence와 무관하게 충분하다.

### E. 기타

- Waiting 오버레이(멀티 대기 화면)는 애니메이션 미적용.
- README의 애니메이션/토글 설명은 0.3.0에 맞춰 갱신됨. 스크린샷은 없음.

## 4. 검증 명령

```powershell
cd <repo>
.\work\dotnet8\dotnet.exe build .\AMS2KRLeague.sln -c Release
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj -c Release --no-build

# 데모 스모크 (AMS2 불필요, 화면에 오버레이가 잠깐 표시됨)
.\src\AMS2LeagueClient\bin\Release\net8.0-windows\AMS2LeagueClient.exe --demo-events --auto-exit-seconds 16 --log-dir .\work\demo-smoke\logs

# 리플레이 밀도/전송량 실측 (work/ 아래, Git 미포함, 이 PC의 서버 원본과 fixture 필요)
.\work\dotnet8\dotnet.exe run --project .\work\replay-cadence-audit\replay-cadence-audit.csproj -c Release -- .\work\beta2-e2e\server-only .\work\replay-cadence-audit\replay-cadence-audit.json
.\work\dotnet8\dotnet.exe run --project .\work\replay-cadence-cost\replay-cadence-cost.csproj -c Release -- .\work\p024\p023-baseline-60m32\sessions\df32057030a86a7324152a1f6d17c3cf\chunks\participant_replay .\work\beta2-e2e\server-only .\work\replay-cadence-cost\replay-cadence-cost.json
```

`work/` 아래 도구와 증거(`beta2-e2e/server-only`, `p024/p023-baseline-60m32`, `replay-cadence-*`)는 이 PC에만 있다. 다른 PC에서는 서버 GET으로 원본을 다시 받아야 한다.

## 5. 이렇게 보고한다 (템플릿)

```text
작업: <한 줄>
변경 파일: <목록>
검증: Release build <경고/오류>, Client tests <n/n>, Activity tests <n/n>, 스모크 <예외 수>
측정: <수치와 근거 파일>
하지 않은 것: <실게임 확인 등>
결정 필요: <정책/gate에 영향을 주는 항목>
커밋: <미실행 | 요청에 따라 v… 커밋·릴리스>
```

## 6. 피해야 할 것

- 추정치를 실측처럼 쓰는 것. 추정이면 "추정"이라고 쓰고 근거 계수를 적는다.
- 사용자 결정 없이 기본값·프로토콜·gate를 바꾸는 것.
- 다른 에이전트의 변경을 조용히 되돌리는 것.
- 테스트 없이 동작을 바꾸는 것, 실패한 테스트를 지우는 것.
- `work/`, `artifacts/`, 자격증명, 운영 데이터를 Git이나 공개 패키지에 넣는 것.
