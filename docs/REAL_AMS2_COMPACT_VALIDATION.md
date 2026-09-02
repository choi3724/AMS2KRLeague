# Real AMS2 Compact Telemetry Validation

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P024-COMPACT-TELEMETRY`

## 1. 판정

| 범위 | 판정 |
|---|---|
| 실제 SHM v14 → Compact A2CT → local durable archive | **PASS** |
| pre-grid → Drive → Race 전환 후 Driver compact finalization | **PASS** |
| persisted-only decode와 chunk 무결성 | **PASS** |
| 종료/finalize acknowledgement | **PASS** |
| 실제 `LOSS_LEDGER_V1` → `ATTEMPT_FINALIZE_V1` wire 종료 | **PASS** — v9 active Race에서 실제 생성·PHP decode |
| zero-loss completeness | **PASS (v9 public attempt)** / **PARTIAL (v6 Driver-rich attempt)** |
| 실제 Driver 동적 field | **PARTIAL** — 위치·속도·브레이크·RPM·가속도 변화 확인, throttle·steering·gear 변화 없음 |
| 실제 Incident compact encoding | **PARTIAL** — `CRASH_STATE_CHANGE` burst 확인, 완전한 pre-roll 없음 |
| clean lap 2개 | **NOT RUN** |
| 실제 multiplayer | **NOT RUN** — 49대 중 48대는 AI인 single-player Race |
| Production/Cafe24 upload | **NOT RUN** — network disabled |

실제 49대 그리드에서 세션을 재시작한 뒤 `Drive`로 진입한 v6 run은 P024에서 발견한
두 runtime exception을 재현하지 않았다. `2,926`개 SHM batch를 처리하고 Compact schema
8개를 만든 뒤 `workerExceptions=0`, `serializationFailures=0`, `diskWriteFailures=0`,
`finalizeFailures=0`, `finalizeAcknowledged=true`, `durableAck=true`로 종료했다.

그러나 source cadence gap `674`개가 loss ledger에 남았으므로 이 attempt는 의도대로
`PARTIAL`이다. 실제로 바뀌지 않은 control 값을 synthetic으로 만들거나 `COMPLETE`로
승격하지 않았다.

이후 runtime close 경로에 실제 `LOSS_LEDGER_V1 (0x50)`과 `ATTEMPT_FINALIZE_V1 (0x51)`
durable commit을 연결하고 v9 active-Race attempt를 새로 실행했다. v9는 약 `119.976 s` 동안
`2,390`개 SHM batch를 처리한 뒤 sequence `30 → 31`에 두 integrity frame을 실제로 남겼다.
PHP Server Decoder로 다시 읽은 최종 값은 accepted/durable work `4,783 / 4,783`, known loss
`0`, completeness `COMPLETE(2)`였고 JSON diagnostic ledger도 동일했다. 이 결과는 v6에
소급한 것이 아니라 수정 후 생성한 별도 real fixture다.

Primary evidence:

- [v6 attempt loss ledger](../work/p024/real-ams2-capture-product-v6/future-telemetry/attempt-ledgers/25b75431997c4f1f678cd9da6fe96d97.attempt-loss.json)
- [v6 persisted-only validation](../work/p024/real-ams2-capture-product-v6/compact-validation.json)
- [v6 client log](../work/p024/real-ams2-capture-product-v6/logs/client-20260902-174408.log)
- [v6 performance samples](../work/p024/real-ams2-capture-product-v6/performance.csv)
- [v5 attempt loss ledger](../work/p024/real-ams2-capture-product-v5/future-telemetry/attempt-ledgers/c5fba95387eada598b99d1166c7bd502.attempt-loss.json)
- [v9 attempt loss ledger](../work/p024/real-ams2-capture-product-v9/activity/future-telemetry/attempt-ledgers/7b4474ba8607d0e568813dea279018af.attempt-loss.json)
- [v9 client log](../work/p024/real-ams2-capture-product-v9/logs/client-20260902-185029.log)
- [v9 active-Race screen](../work/p024/real-v9-game-state-04.jpg)
- [v9 local Server E2E validation](../../AMS2League/server/cafe24_telemetry014/docs/P024_REAL_AMS2_V9_FINALIZE_SERVER_VALIDATION.md)

## 2. 실제 실행 조건

| 항목 | 값 |
|---|---|
| client version | `0.2.2` — unchanged |
| AMS2 build / SHM | `3398` / official Shared Memory `14` |
| display | `3440×1440` |
| track/layout | Bathurst / Bathurst 2020 |
| vehicle/class | Aston Martin Vantage GT3 Evo / GT3 Gen2 |
| session | single-player Race |
| participants | raw `49`, Safety Car `1`, League `48` |
| v6 capture | 약 `150 s`, pre-grid → Drive → active Race |
| network | `--activity-upload-disabled` |
| private upload status | `LOCAL_PENDING_OWNER` |
| public upload status | local queue `PENDING`, transport absent |

AMS2와 Client는 테스트 후 정상 종료했다. Steam이나 Production Server는 종료·변경하지 않았다.

### 2.1 v9 integrity 재검증 조건

| 항목 | 값 |
|---|---|
| attach point | 49-car active single-player Race |
| duration / SHM batches | `119.976 s` / `2,390` |
| player input | stationary; actual control exercise로 간주하지 않음 |
| network | `--activity-upload-disabled` |
| compact artifacts | public `6` + legacy metadata `1` |
| compact / total wire | `5,354 B` / `8,594 B` |
| runtime failures / drops | `0 / 0` |
| attempt | `COMPLETE`, known loss `0` |

v9는 Driver source가 resolved되지 않아 Driver accepted work가 `0`이었다. 따라서 v9의 목적은
실제 Race runtime의 public close/finalize E2E이며, Driver fidelity나 clean-lap gate를 대신하지
않는다.

## 3. 실제 run에서 발견하고 닫은 두 오류

### 3.1 Participant timing sentinel

첫 실제 run의 starting-grid participant 값에는 아직 기록이 없는 timing field가 `-123`으로
들어왔다. 유한 숫자라는 이유만으로 이를 lap/sector time으로 전달해 Replay quantization이
실패했다.

수정 후 다음 unavailable timing source는 `0`이 아니라 `null`이다.

- current sector 1/2/3;
- best/last lap;
- fastest sector 1/2/3.

실제 v3에서는 이 수정 후 Replay가 durable commit에 성공했다.

### 3.2 Driver transition domains

v3는 Replay를 저장했지만 pre-grid/gameplay 경계에서 Driver Fast worker가 중단됐다. 실패 row는
commit 전에 격리되어 단일 offending ordinal을 사후 확정할 수 없었다. Driver Fast가 artifact
생성의 첫 단계이고, 같은 session의 고정 상태 v4가 성공한 점 및 AMS2의 `-123` 관례를 근거로
wire schema의 의미 범위를 adapter 경계에 적용했다.

| source family | accepted domain | invalid/unavailable handling |
|---|---|---|
| lap distance, speed, RPM | `>= 0` | `null` |
| throttle, brake, clutch, fuel/damage/rain ratios | `[0, 1]` | `null` |
| steering | `[-1, 1]` | `null` |
| heading | `[-2π, 2π]` | `null` |
| Driver acceleration | schema range `[-327.68, 327.67]` | `null` |

클램프는 사용하지 않는다. 이후의 정상 sample은 그대로 보존한다. 회귀 테스트는 unavailable
sample 다음 valid sample, 네 Driver artifact 생성, null/zero 구분, worker/finalize failure 0을
검사한다.

최종 client test 결과:

```text
solution build: 0 warnings, 0 errors
AMS2LeagueActivity.Tests: 92/92 PASS
AMS2LeagueClient.Tests:   38/38 PASS
```

## 4. v6 persisted archive inventory

| Compact schema | Actual samples | Source gap | Raw A2CT B | Wire B | Visibility/status |
|---|---:|---:|---:|---:|---|
| `SESSION_STATIC_V1` | 1 | 0 | `2,629` | `1,104` | public / `PENDING` |
| `RACE_EVENT_V1` | 423 | 0 | `20,790` | `8,099` | public / `PENDING` |
| `PARTICIPANT_REPLAY_V1` | 13,226 | 0 | `171,833` | `31,684` | public / `PENDING` |
| `TRACK_GEOMETRY_V1` | 1 | 0 | `101` | `102` | public / `PENDING` |
| `DRIVER_FAST_V1` | 1,517 | 674 | `13,954` | `2,274` | private / `LOCAL_PENDING_OWNER` |
| `DRIVER_MOTION_V1` | 454 | 674 | `3,279` | `911` | private / `LOCAL_PENDING_OWNER` |
| `DRIVER_SLOW_V1` | 105 | 674 | `515` | `311` | private / `LOCAL_PENDING_OWNER` |
| `DRIVER_CHANGE_V1` | 1,063 | 674 | `6,097` | `1,787` | private / `LOCAL_PENDING_OWNER` |
| **Compact total** | - | **674 source slots** | **`219,198`** | **`46,272`** | 8 frames |
| low-rate legacy `SESSION_METADATA` | - | 0 | - | `6,706` | compatibility |
| **validation input total** | - | - | - | **`52,978`** | 9 chunks |

Driver의 `674`는 하나의 source-stream cadence gap이며 네 파생 schema마다 같은 quality fact가
복사된다. 이를 `2,696`개의 독립 source loss로 합산하면 안 된다. Attempt ledger의 authoritative
known loss는 `674`다.

이 150초 run은 49대, start burst, pre-grid 40초와 one-time dictionaries/events가 섞여 있으므로
단순 선형 환산을 60분/32대 acceptance 크기로 사용하지 않는다. 크기 gate의 authority는 고정
60분/32대 fixture의 `465,279 B`다.

## 5. Persisted-only field validation

분석기는 AMS2 process나 SHM을 다시 읽지 않고 v6의 9개 persisted chunk만 읽었다.

| check | result | measured evidence |
|---|---|---|
| chunk/hash integrity | PASS | integrity failure 0 |
| world position change | PASS | X range `2.45 m`, Z range `0.60 m` |
| lap distance change | PASS | range `2.51 m` |
| speed change | PASS | `0.00..0.12 m/s` |
| brake change | PASS | `0..1` |
| RPM change | PASS | `0..1,480` |
| acceleration change | PASS | longitudinal `-23.99..9.77 m/s²`, lateral `-1.60..0.88 m/s²` |
| throttle change | NOT EXERCISED | persisted value `0` |
| steering change | NOT EXERCISED | persisted value `0` |
| gear change | NOT EXERCISED | persisted value `0` |
| bounded input queue drop | PASS | `0` |

Windows synthetic key events were sent in a separate v7 run, but AMS2 did not accept them as vehicle
input: all control values remained unchanged. This is not reported as codec PASS. A physical keyboard,
controller, or a safely configured in-game AI/driver-swap run is still required for clean-lap control
validation.

## 6. 실제 Incident evidence

v5 post-race transition produced one `INCIDENT_V1` frame:

| item | value |
|---|---|
| trigger | `CRASH_STATE_CHANGE` |
| samples | `114` |
| involved participant refs observed | `0`, `22` |
| elapsed range | `1..2,959 ms` |
| raw / wire | `4,403 B` / `648 B` |
| expected / actual | `114 / 114` |
| source cadence gap | `6` |
| worker/finalize failure | `0 / 0` |

Client가 trigger 시점에 attach되어 -3초 pre-roll 전체는 존재하지 않는다. 따라서 실제 Compact
Incident encoding과 typed trigger 보존은 PASS지만, 요구된 완전한 `-3..+3 s` burst gate는
`PARTIAL`이다.

## 7. Completeness와 성능

v6 source-stream ledger:

```text
outer queue losses:       0
archive input losses:     0
worker exceptions:        0
serialization failures:   0
disk write failures:      0
commit conflicts:         0
finalize failures:        0
finalize acknowledged:    true
durable processing ACK:   true
cadence missed:           674
attempt completeness:     PARTIAL
```

수정 후 v9 integrity ledger와 A2CT decode 결과:

```text
runtime batches / dropped:             2,390 / 0
runtime failures:                      0
LOSS_LEDGER_V1 sequence:               30
loss source / count / reason:          0 / 0 / 0
ATTEMPT_FINALIZE_V1 sequence:           31 (last)
accepted / durable work:               4,783 / 4,783
known loss:                            0
wire completeness:                     COMPLETE (2)
JSON finalize / durable ACK:            true / true
```

두 frame은 모두 `PUBLIC_REPLAY / PENDING` sidecar로 생성됐고 gzip/payload hash 및 크기가
일치했다. PHP decoder는 `0x50` raw/wire `95/95 B`, `0x51` raw/wire `97/99 B`를 정확히
복원했다. 로컬 Server candidate에는 같은 attempt의 앞선 public frame까지 먼저 넣은 뒤
integrity frame을 넣는 replay fixture를 유지한다. Network, live PDO, Cafe24 Production은
사용하지 않았다.

Active-race performance log 10 samples:

| metric | measured |
|---|---:|
| Client CPU average | `2.996%` |
| Client CPU min/max | `2.799% / 3.357%` |
| Client RAM average | `227.2 MB` |
| Client RAM min/max | `202.6 / 250.0 MB` |
| SHM average | `28.77 Hz` |
| UI average | `18.96 Hz` |

이 값은 동일 session의 P024 관측치다. 동일 장면의 P023 CPU/FPS baseline을 같은 방법으로
재측정하지 않았으므로 성능 regression PASS를 주장하지 않는다.

## 8. 남은 실제 gate

1. 실제 control source가 변하는 clean lap 2개 이상으로 throttle/brake/steering/gear, braking
   point, driving line, minimum speed와 lap comparison을 검증한다.
2. 실제 2명 이상 multiplayer에서 participant join/rejoin, position chart와 2D Replay를 검증한다.
3. trigger 전부터 Client가 실행된 안전한 incident run으로 full `-3..+3 s` burst를 확인한다.
4. v6 cadence gap 원인과 허용 정책을 결정한다. v9 zero-loss public attempt로 v6의 Driver
   loss를 덮어쓰지 않는다.
5. 동일 장면의 P023/P024 CPU, frame time와 FPS를 계측한다.
6. Cafe24 staging/PDO/MariaDB와 physical storage를 검증한다. Production은 계속 변경하지 않는다.
7. finalize 이후 발생할 수 있는 upload failure를 다음 서버 ACK 또는 별도 terminal receipt에
   연결하는 후속 계약을 확정한다.

결론: **실제 Compact codec과 public loss/finalize wire E2E는 PASS, 실제 full-session product
gate는 PARTIAL이며 Release는 HOLD다.**
