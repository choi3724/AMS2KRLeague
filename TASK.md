# docs/TASK.md — v0.7.0 안정화 감사 및 핵심 회귀 방지

## 0. 작업 성격

이 작업은 새 기능 개발이 아니다.

현재 공개 `v0.7.0`을 기준으로,
실제 레이스에서 데이터 손실·업로드 보류·HUD 끊김으로 이어질 가능성이 높은 부분을
좁은 범위에서 조사하고 필요한 최소 수정만 수행한다.

현재 기준:

- Release: `v0.7.0`
- Commit: `48b2881f1f6f4b6f758763ef9556e383db086e70`

반드시 `AGENTS.md`와 `PROJECT.md`를 먼저 읽는다.

## 1. 최우선 규칙

- 요청하지 않은 UI/기능/설정 추가 금지.
- 디자인 변경 금지.
- 현재 0.7.0의 정상 HUD/레이아웃/Compact 계약을 최대한 보존.
- 원인을 확인하지 않고 threshold만 확대하는 식의 수정 금지.
- Client/Protocol 변경은 실제 필요성이 증명된 경우에만 수행.
- 서버/Cafe24 운영 배포는 이번 작업에서 수행하지 않는다.
- commit/tag/push/release는 사용자 별도 승인 없이는 수행하지 않는다.
- 자동 테스트만으로 GREEN 판정 금지.
- 실제 AMS2 검증이 필요한 항목은 `NOT RUN` 또는 `YELLOW`로 남긴다.

## 2. 범위 밖

다음은 이번 작업에서 하지 않는다.

- 새로운 Overlay 디자인
- 새로운 Dashboard/Telemetry UI 추가
- Web Portal 기능 개발
- Race Coach 개발
- 새로운 통계 기능
- 새로운 Race Story 기능
- Compact 전체 재설계
- Replay cadence 임의 증가
- Private Driver Telemetry 서버 업로드 활성화
- Steam/OpenXR 지원 범위 확대
- 기존 V1 schema ordinal 변경
- Cafe24 DB migration
- 사용자 요청 없는 폰트/색상/배치 변경

# 요구사항

## REQ-DOC-01 — 저장소 문서 정합성 확정

### 목표

현재 `PROJECT.md`, `docs/TASK.md`, `README.md`, `VERSIONING.md`, `AGENTS.md`가
0.7.0의 실제 상태와 충돌하지 않는지 확인한다.

### 수용 조건

- `PROJECT.md` 기준선이 v0.7.0 / 현재 commit과 일치.
- `docs/TASK.md`가 과거 0.4.x/0.5.x 현재 작업으로 남아 있지 않음.
- AGENTS의 과거 handoff 문서가 현재 정책보다 우선하지 않도록 확인.
- README/VERSIONING의 Current version이 0.7.0과 일치.
- 충돌 발견 시 현재 사실로 수정하되 제품 동작은 변경하지 않음.

## REQ-SPEED-01 — 690~705 m/s 비정상 속도 원인 추적

### 배경

과거 장거리 기록 실패 조사 중 다른 attempt에서
약 690~705 m/s 범위의 speed 값이 관측되었다.

이 값은 정상 차량 속도로 간주하지 않는다.

### 조사 대상

- SHM 원본 speed
- viewed participant 전환
- slot/generation 변경
- session transition
- race restart
- teleport/reset
- snapshot sequence consistency
- parser offset/field 의미
- fast display read와 full snapshot read의 차이
- Compact quantization 전 입력

### 금지

원인 확인 없이:

- speed upper bound만 1000m/s 등으로 확대
- 이상값 clamp 후 정상값처럼 저장
- 해당 sample을 조용히 0으로 변경

금지.

### 수용 조건

다음 중 하나를 근거와 함께 확정:

A. parser/read 오류  
B. valid SHM transient but invalid vehicle telemetry  
C. participant/session transition artifact  
D. 기존 저장자료만으로 원인 확정 불가

A~C이면 최소 수정 + 회귀 테스트.

D이면 진단 로그/validation hook만 추가하고 실제 AMS2 재현을 다음 단계로 남긴다.

## REQ-SPEED-02 — Display/Archive validation policy 정합성

현재 HUD와 Compact Archive에서 속도 유효 범위 정책이 다를 수 있다.

### 수용 조건

- Display path와 Archive path의 현재 validation을 표로 작성.
- 서로 다른 이유가 명확하면 유지.
- 이유 없이 다른 경우 공통 semantic validator 또는 명시적 정책으로 정리.
- 정상 고속 차량 데이터를 임의 차단하지 않음.
- 이상값 때문에 전체 chunk가 실패하는 구조가 필요한지 재검토.
- raw evidence 보존 원칙 유지.

## REQ-UPD-01 — 경기 중 자동 업데이트 설치 차단

### 목표

새 버전 다운로드 자체는 가능하더라도,
실제 Race/Capture attempt가 진행 중이거나 종료 durable finalize가 끝나지 않은 상태에서는
Updater가 Overlay를 종료하고 설치하지 않게 한다.

### 필수 조사

현재:

```text
GitHubAutoUpdater
→ PrepareInstaller
→ exit callback
→ ExitClient
→ Coordinator Dispose
→ ActivityCapture Dispose/finalize
```

순서와 capture 상태를 추적한다.

### 요구 동작

- Capture/active race가 없는 상태: 기존 자동 설치 가능.
- Capture가 진행 중: 다운로드/검증은 가능, 설치는 보류.
- stable race result + durable finalize 완료 후: 설치 가능.
- 프로그램 종료을 강제하거나 race capture를 중간 분할하지 않음.
- 업데이트 보류 상태는 상태창에 이해 가능한 문구로 표시.

### 수용 조건

자동 테스트에서:

1. Idle update → install handoff 가능
2. Active Practice/Qualify/Race capture → install handoff 금지
3. Race result 관측됐지만 finalize ACK 전 → 금지
4. Finalize ACK 후 → 허용
5. 기존 installer SHA/size/URL 검증 유지

## REQ-ARCH-01 — 5분 local durable loss window 분석

### 목표

현재 product 구성의 `ChunkDurationMs = 300_000`이
비정상 종료 시 최대 얼마의 미커밋 데이터를 잃을 수 있는지 실제 코드로 증명한다.

### 우선 조사

- LocalDurableTelemetryArchive commit 시점
- Chunk flush 시점
- Process crash / power loss / taskkill 시 보존 범위
- Finalize 이전 memory-only data
- Retry source preservation과 정상 chunk의 차이

### 필수 산출

다음 profile을 동일 60분/32대 fixture 또는 기존 production-equivalent fixture에서 비교:

- 300s
- 60s
- 30s

각각:

- 로컬 파일 개수
- 총 gzip bytes
- 평균 chunk bytes
- encoding CPU
- peak working set
- 정상 종료 write 횟수
- crash 시 이론상 최대 미커밋 시간

### 변경 조건

측정 결과 30~60초 durable commit이 실용적이면
**로컬 commit 단위만** 축소하는 최소안을 적용할 수 있다.

단 네트워크 HTTP 요청 수를 함께 증가시키지 않는다.
필요하면 local chunk와 upload batch를 분리한다.

### 수용 조건

측정 없이 기본값 변경 금지.

## REQ-MP-01 — Multiplayer 15초 UNKNOWN 전환 감사

### 목표

실제 Multiplayer 세션에서 `online.log`에 새 이벤트가 15초 이상 없다는 이유만으로
MULTIPLAYER가 UNKNOWN이 되어 최종 업로드가 보류될 가능성을 검증한다.

### 필수 시나리오

1. Join evidence 발생
2. 60초 이상 네트워크 관련 새 로그 없음
3. Race 계속 진행
4. Leave evidence 없음

현재 구현 결과와 원하는 보안 의미를 비교한다.

### 변경 원칙

- participant count만으로 multiplayer 판정 금지.
- SHM에 없는 online 여부를 추정 금지.
- 명시적 Join evidence를 무한정 신뢰하는 것도 금지.
- 실제 Leave/process restart/session boundary를 고려.

### 후보

필요하면:

```text
Join evidence
→ bounded multiplayer lease/latch
→ explicit leave/process/session boundary
→ clear
```

형태를 검토한다.

lease 길이는 임의 결정하지 말고 실제 online.log 패턴/기존 실제 로그를 측정한다.

### 수용 조건

- 정상 멀티가 단순 log silence 때문에 업로드 불가 상태가 되는지 명확히 판정.
- 수정할 경우 false-positive multiplayer upload를 늘리지 않는 테스트 포함.

## REQ-HUD-01 — 60Hz Driving HUD transient gap 복원력

### 배경

현재 fast display read에서 약 150ms 이상 sample을 얻지 못하면 `null`이 전달될 수 있고,
DrivingTelemetryHistory는 `null` 입력 시 전체 history를 Clear한다.

### 목표

짧은 display-only read 실패가 10초 그래프 전체 초기화로 이어지는지 확인한다.

### 수용 조건

- 16~150ms 일시 누락
- 150~500ms 누락
- participant 실제 변경
- session generation 변경
- game detach

를 각각 fixture로 검증.

정상 요구:

- 일시 누락: 기존 history 유지 또는 시각적 gap
- 실제 participant/session 변경: history 초기화
- stale 데이터를 새 telemetry fact로 합성하지 않음

현재 동작이 이미 적절하면 수정하지 않는다.

## REQ-FAULT-01 — UI Tick 예외 fault boundary 조사

### 목표

Dashboard/Telemetry/Event 등 한 패널 오류가 전체 Overlay hide로 이어지는 현재 경로를 검증한다.

### 수용 조건

- `UiTick` exception → `_overlay.HideOverlay()` 경로를 재현 fixture로 확인.
- 개별 패널 오류를 안전하게 격리 가능한지 검토.
- 최소한 last-good-frame 유지가 가능한지 판단.
- 광범위 try/catch 추가로 실제 오류를 숨기지 않는다.

수정은 실제 재현과 최소 안전 경계가 명확한 경우에만 한다.

## REQ-LOG-01 — Unbounded telemetry log channel 위험 평가

### 목표

`Channel.CreateUnbounded<TelemetryLogEntry>`가
장시간 disk stall/failure에서 메모리를 무제한 늘릴 가능성을 측정한다.

### 수용 조건

- 정상 log rate
- synthetic burst
- logger I/O delay
- logger IOException

조건에서 queue/memory behavior 측정.

필요할 경우:

- bounded queue
- low-priority log drop counter
- WARN/ERROR 우선 보존

을 적용한다.

Telemetry/Capture fact 자체를 log queue와 함께 drop하지 않는다.

## REQ-V2-01 — Long-track V1/V2 Client 호환 회귀

### 목표

0.7.0에서 추가된 long-track V2가 기존 V1을 깨지 않았는지 Client 측 회귀 고정.

### 수용 조건

- 20,000m 이하 → V1
- 20,000m 경계 → 정의된 계약대로 V1
- 20,815.41m → 필요한 block만 V2
- 100km 이하 → 지원
- 지원범위 초과 → 명확한 실패
- V1/V2 mixed attempt decode/metadata 정상
- V2 `COMPACT_SCHEMA_UNKNOWN` → retryable
- 다른 invalid 400 → 기존 정책 유지

Cafe24 운영 수신 확인은 이번 작업의 배포 범위 밖이며
실제 서버 검증이 없으면 `NOT RUN`으로 보고한다.

## REQ-VERIFY-01 — 전체 회귀 및 실제 검증 분리

### 자동 검증

최소:

```powershell
.\scripts\verify.ps1
```

또는 이에 해당하는:

- Release build
- Client tests
- Activity tests

전부 수행.

### 실제 검증 상태를 별도 보고

다음은 자동 테스트와 섞지 않는다.

- 실제 AMS2 주행 60Hz HUD
- 실제 Multiplayer upload
- 실제 Nordschleife 20km+ upload
- 실제 Quest 3 / Virtual Desktop
- 실제 game-load CPU/GPU

실행하지 않았으면 `NOT RUN`.

### GREEN 조건

GREEN은 다음을 모두 만족할 때만 가능:

- 자동 회귀 전부 PASS
- 발견한 P0/P1 코드 결함 해결 또는 명확히 무해함 증명
- 데이터 손실 위험 증가 없음
- 기존 Compact V1 호환 유지
- 요청 외 UI 변경 0
- 문서 정합성 정상

실게임이 필요한 핵심 문제가 남으면 최종은 YELLOW.

# 3. 변경 전 반드시 작성할 조사표

코드 변경 전에 아래 표를 먼저 채운다.

| ID | 현재 동작 | 재현 여부 | 위험 | 수정 필요 | 수정 파일 후보 |
|---|---|---|---|---|---|
| SPEED | | | | | |
| UPDATER | | | | | |
| DURABLE | | | | | |
| MULTIPLAYER | | | | | |
| HUD GAP | | | | | |
| UI FAULT | | | | | |
| LOG QUEUE | | | | | |
| V2 | | | | | |

원인을 모르는 항목을 추측으로 수정하지 않는다.

# 4. 보호 대상

이번 작업 중 반드시 그대로 유지:

- 0.7.0 Overlay 선택 화면
- 기본/개량 Timing Tower
- 기본/개량 Telemetry
- Pedal Gauge
- Racing Dashboard
- ABS 표시
- Steering 표시
- Behind Battle event
- Waiting overlay
- Position animation
- Relative gap 색상 규칙
- False LAP gap 수정
- Safety Car 제외
- 60Hz display-only read와 recorder 분리
- Existing Activity/Witness upload idempotency
- DPAPI credential
- A2CT V1 schema
- Long-track additive V2
- gzip upload
- installer verification
- click-through
- layout persistence

# 5. 최종 보고 형식

```text
AMS2 0.7.0 STABILITY AUDIT

BASE:
v0.7.0
48b2881f1f6f4b6f758763ef9556e383db086e70

FINAL:
GREEN / YELLOW / RED

DOCUMENT CONSISTENCY:
PASS / FAIL

ANOMALOUS SPEED:
ROOT CAUSE:
FIX:
TEST:

AUTO UPDATE DURING CAPTURE:
CURRENT:
FIX:
ACTIVE CAPTURE BLOCK:
PASS / FAIL

LOCAL DURABILITY:
300s:
60s:
30s:
SELECTED:
REASON:

MULTIPLAYER LOG SILENCE:
CURRENT:
FIX/NO FIX:
FALSE POSITIVE RISK:

DRIVING HUD GAP:
PASS / FAIL

UI FAULT ISOLATION:
PASS / FAIL / NOT CHANGED

LOG QUEUE:
PASS / FAIL / NOT CHANGED

LONG TRACK V2:
PASS / FAIL

RELEASE BUILD:
warnings <n>
errors <n>

CLIENT TESTS:
<n>/<n>

ACTIVITY TESTS:
<n>/<n>

REAL AMS2:
PASS / FAIL / NOT RUN

REAL MULTIPLAYER:
PASS / FAIL / NOT RUN

REAL NORDSCHLEIFE SERVER E2E:
PASS / FAIL / NOT RUN

QUEST3/VD:
PASS / FAIL / NOT RUN

REQUEST-OUTSIDE CHANGES:
0 / <count>

COMMIT:
NOT PERFORMED unless explicitly approved

TAG:
NOT PERFORMED unless explicitly approved

PUSH:
NOT PERFORMED unless explicitly approved

RELEASE:
NOT PERFORMED unless explicitly approved
```

# 0.7.1 릴리스 지시 — 2026-09-10

사용자가 안정화 개선 사항을 포함해 0.7.1 커밋·태그·푸시·GitHub Latest 릴리스를 명시적으로 승인했다. 공개 안내는 적용된 개선 사항 중심으로 작성하고, 검증 안내는 VR 실기기 미검증만 표시한다. 기존 상세 감사 보고서는 당시 조사 기록으로 보존한다. 아래 승인 전 금지 규칙은 이번 릴리스에 대한 사용자 승인으로 충족됐다.

# 6. 완료 후

필요한 코드 수정과 자동 검증까지 완료하면 멈춘다.

사용자의 별도 요청 없이:

- 버전 증가
- commit
- tag
- push
- GitHub Release
- Cafe24 배포

를 진행하지 않는다.
