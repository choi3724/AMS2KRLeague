# Coaching Capability Matrix

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`

## 1. verdict 정의

| verdict | 의미 |
|---|---|
| `POSSIBLE` | 보존된 raw facts만으로 metric을 다시 계산할 수 있다. 계산 알고리즘/Web/AI가 이미 구현됐다는 뜻은 아니다. |
| `PARTIAL` | 핵심 raw fact 일부가 없거나 단위/축/runtime/access/reference 조건이 미검증이라 제한된 추정만 가능하다. |
| `NOT POSSIBLE` | SHM에 authoritative source가 없거나 capture 정책상 의도적으로 수집하지 않는다. |

`CURRENT VERDICT`는 Runtime/local archive/uploader와 local Server candidate가 구현되고, 실제 AMS2 short run에서 core signal이 보존됐지만 authoritative local-owner/spectator 판정, outer queue/worker completeness propagation, clean multi-lap과 일부 단위/축이 남은 현재 상태다. `AFTER P023 GREEN`은 이 privacy/completeness blocker와 실제 full-lap/multi-car/incident, semantic validation, staging round trip까지 모두 통과한 상태다. field별로 `REAL_SHORT_RUN_VERIFIED`와 `SEMANTICS_PENDING`을 구분한다.

## 2. 필수 coaching metric

| METRIC | CURRENT VERDICT | AFTER P023 GREEN | REQUIRED RAW FIELDS | SERVER 계산 방법/근거 | 제한 및 null 처리 | CLIENT UPDATE NEEDED AFTER GREEN? |
|---|---|---|---|---|---|---|
| Braking point | PARTIAL | POSSIBLE | `lap`, `lapValid`, `lapDistanceMeters`, `sessionElapsedMs`, `speedMetersPerSecond`, `brake`, `unfilteredBrake` | clean lap에서 brake threshold 최초 crossing을 interpolation하고 distance/time을 reference lap과 비교 | 20 Hz에서 1 sample=50 ms; 60 m/s이면 약 3 m 간격. gap/drop lap은 제외. pedal threshold/hysteresis는 analyzer version으로 기록 | NO |
| Minimum corner speed | PARTIAL | POSSIBLE | `lapDistanceMeters`, `worldX/Y/Z`, `speedMetersPerSecond`, `steering`/`unfilteredSteering`, longitudinal/lateral acceleration, `lapValid` | geometry curvature와 steering/speed trace로 corner zone을 검출하고 zone 내 minimum speed 계산 | SHM은 authoritative corner name/number를 주지 않는다. metric은 가능하지만 Turn 1 같은 label은 Server track metadata/derived mapping 필요 | NO |
| Throttle-on distance | PARTIAL | POSSIBLE | `lapDistanceMeters`, `sessionElapsedMs`, `speedMetersPerSecond`, `throttle`, `unfilteredThrottle`, `brake`, `lapValid` | braking/coast 구간 뒤 throttle threshold를 지속적으로 넘는 첫 crossing 계산 | traction-limited wheel slip의 authoritative 값은 없음. pedal application 위치는 계산 가능 | NO |
| Steering correction count | PARTIAL | POSSIBLE | `sessionElapsedMs`, `lapDistanceMeters`, `unfilteredSteering`, `steering`, `headingRadians`, lateral acceleration, speed, `lapValid` | speed-dependent deadband/low-pass 후 steering derivative sign reversal 또는 counter-steer episode를 계산 | hardware noise/deadzone과 정상 corner steering을 분리하는 algorithm/version 필요. 20 Hz 이상의 미세 oscillation은 복원 불가 | NO for 20 Hz correction metric |
| Lap consistency | PARTIAL | POSSIBLE | story `LAP_COMPLETE.lapTimeMs`, driver `lap`, `lapValid`, `currentLapTimeMs`, distance-normalized speed/control traces, weather/tyre context | valid lap time dispersion, sector/distance-bin delta, control trace variance를 계산 | mid-session join/partial chunk/drop은 quality로 제외 또는 가중. 다른 weather/vehicle setup은 같은 cohort로 자동 비교하지 않음 | NO |
| Driving line variance | PARTIAL | POSSIBLE | `lap`, `lapValid`, `lapDistanceMeters`, `worldX/Y/Z`, `headingRadians`, track/layout identity | clean laps를 common distance grid로 resample하고 center/median line 대비 lateral/world-coordinate dispersion 계산 | world-axis/scale와 track direction을 real runtime에서 확인해야 함. privacy gate를 닫은 20 Hz private driver stream을 사용하고 5 Hz public replay는 race visualization용 | NO |

현재 여섯 항목은 모두 DTO/row 계약과 SHM source가 있고, 실제 AMS2 84.986초 archive에서 world position, lap distance, speed, controls, heading candidate와 acceleration이 함께 보존됐다. 그러나 그 row가 authoritative하게 설치 사용자의 차량이라는 증거와 end-to-end completeness가 없고 completed clean lap/corner/line 반복 표본도 없으므로 여섯 coaching metric의 `CURRENT VERDICT`는 계속 PARTIAL이다. raw graph availability와 metric/privacy validity를 혼동하지 않는다.

## 3. 추가 future coaching/graph capability

| METRIC / OUTPUT | AFTER P023 GREEN | REQUIRED RAW FIELDS | 설명 / 제한 |
|---|---|---|---|
| Exit speed | POSSIBLE | corner boundary, `lapDistanceMeters`, `speedMetersPerSecond`, throttle, steering | Server가 정의한 apex/exit distance에서 speed와 acceleration을 비교 |
| Corner entry speed | POSSIBLE | braking point, speed, distance, steering, longitudinal acceleration | reference distance 또는 detected turn-in 지점 기준 |
| Brake/Throttle overlap | POSSIBLE | filtered/unfiltered brake/throttle + time/distance | threshold를 versioned metric으로 계산 |
| Gear/RPM trace | POSSIBLE | `gearRaw`, `rpm`, distance/time | shift point와 gear selection 비교; gear enum raw를 보존 |
| Longitudinal/Lateral G | POSSIBLE only after unit/axis validation | longitudinal/lateral acceleration | verified m/s²를 9.80665로 나눔. header comment ambiguity가 해소되지 않으면 PARTIAL 유지 |
| Vertical G | POSSIBLE only after unit/axis validation | vertical acceleration | kerb/bump context용; 같은 검증 gate 적용 |
| Lap delta vs reference | POSSIBLE | valid lap, time, distance, common track/layout/vehicle/class | distance grid에서 elapsed delta 계산; reference telemetry access/동의 필요 |
| Fuel-use trend | PARTIAL | `fuelLevelRatio`, `fuelCapacityLiters`, derived `fuelLiters`, lap/time/distance | raw lineage와 product test는 PASS; 실제 차량 단위와 refuel discontinuity를 live 확인해야 함 |
| Tyre temperature trend | POSSIBLE after live validation | four `tyreTemp*` fields, time/lap, ambient/track temperature | FL/FR/RL/RR bulk temperature 비교 |
| Tyre pressure trend | POSSIBLE after live validation | four `tyrePressure*Kpa` fields, tyre/ambient temperature | source PSI→kPa conversion을 runtime fixture와 실제 값으로 확인 |
| Tyre wear trend | PARTIAL | four `tyreWear*` fields, lap/time | source ratio 방향/차량별 지원을 실제 장거리 run에서 확인해야 절대 wear 의미 가능 |
| Setup effect comparison | PARTIAL | brake bias, tyre state, environment, vehicle/class, raw driving trace | P023 row는 전체 garage setup을 보존하지 않는다. 관측된 brake bias/tyre/environment 범위만 비교 가능 |
| Understeer/oversteer heuristic | PARTIAL | steering, heading, world line, yaw proxy, lateral acceleration, speed | heuristic은 가능하지만 tyre slip/steering-lock/vehicle model truth가 없어 authoritative 판정 불가 |
| Racecraft proximity analysis | PARTIAL | public replay/incident X/Y/Z, position, speed, heading | proximity/relative motion은 가능; other-driver controls와 exact car extents/contact point는 없음 |
| AI Race Coach text | POSSIBLE for listed metrics | 위 metric + track/layout/vehicle/class + user/reference context | AI는 Server-derived facts를 설명한다. raw에서 근거가 없는 원인/과실을 만들면 안 됨 |

## 4. metric별 raw field lineage

| archive field | v14 source | raw/normalized policy | verification |
|---|---|---|---|
| `lapDistanceMeters` | `ParticipantInfo.mCurrentLapDistance` | source metres | REAL_SHORT_RUN_VERIFIED for changing source; full-lap progression PENDING |
| `worldX/Y/Z` | `ParticipantInfo.mWorldPosition[3]` | source world coordinate, both replay and private driver retained | REAL_SHORT_RUN_VERIFIED, all axes changed |
| `speedMetersPerSecond` | root `mSpeed` for viewed/root driver candidate; `mSpeeds[slot]` for participants | source m/s | REAL_SHORT_RUN_VERIFIED for values; authoritative local owner PENDING |
| `brake`, `throttle`, `steering`, `clutch` | filtered root inputs | source normalized ratios | REAL_SHORT_RUN_VERIFIED for brake/throttle/clutch; filtered steering unchanged in this run |
| `unfilteredBrake`, `unfilteredThrottle`, `unfilteredSteering`, `unfilteredClutch` | unfiltered root inputs | source normalized ratios; coaching intent 우선 | REAL_SHORT_RUN_VERIFIED for brake/throttle/steering; unfiltered clutch unchanged |
| longitudinal/lateral/vertical acceleration | `mLocalAcceleration[3]` candidate components | m/s² field only after axes/unit are verified; otherwise null | real value changes/graph PASS; axis/unit SEMANTICS_PENDING |
| `headingRadians` | selected component of root/per-participant orientation | rad only after component/sign validation | real short-run change observed; sign/component SEMANTICS_PENDING |
| `velocityX/Y/Z` | `mWorldVelocity[3]` | source m/s | REAL_SHORT_RUN_VERIFIED for changing source |
| `lapValid` | inverse `mLapInvalidated` | boolean | captured real, but only one invalid/incomplete lap state observed |
| `currentLapTimeMs` | root `mCurrentTime` | seconds→ms; current lap only | HEADER_VERIFIED; completed-lap relation PENDING |
| tyre temperature | `mTyreTemp[4]` | source °C | real four-corner change observed, short-run only |
| tyre pressure | `mAirPressure[4]` | PSI×6.894757→kPa | captured, but approximately 1,114~1,130 kPa; unit/scale SEMANTICS_PENDING |
| tyre wear | `mTyreWear[4]` | raw ratio direction not assumed | captured; small rear change, direction/long-run semantics PENDING |

## 5. privacy/authority prerequisites

`DRIVER_TELEMETRY` candidate row는 현재 다음이 모두 true일 때만 가능하다.

```text
ActivityLocalParticipantResolver resolved viewed participant while InGamePlaying
AND root/viewed source matches that participant
AND LocalParticipantResolved == true
AND SourceParticipantRef == DriverRef
```

다른 participant의 throttle/brake/steering을 5 Hz replay에서 추정해 private driver data처럼 저장하지 않는다. 그러나 위 조건은 viewed/root consistency일 뿐 authoritative ownership이 아니다. 공식 v14 header에는 authoritative local-owner/spectator/player-ID signal이 없고 game state/input activity도 authority가 아니다. 따라서 authoritative attestation 전 `DRIVER_TELEMETRY`는 기본 OFF/fail-closed여야 하며 1인 session/Time Attack 허용도 heuristic이다. 그 뒤에도 comparative coaching은 두 owner가 각각 보존한 private chunks와 Server authorization을 사용하고 공개 League replay에서 개인 controls를 자동 공개하지 않는다.

## 6. NOT POSSIBLE / 의도적 비목표

| 항목 | verdict | 이유 |
|---|---|---|
| Authoritative corner name/number from SHM alone | NOT POSSIBLE | SHM은 Track/Variation과 좌표를 제공하지만 공식 corner semantic label은 제공하지 않음 |
| Incident fault/blame | NOT POSSIBLE | capture facts는 contact/proximity 후보일 뿐 steward truth가 아님; Client 판단 금지 |
| Other participants' pedal/steering telemetry | NOT POSSIBLE | detailed root telemetry는 viewed vehicle scope이며 authoritative owner attestation 없이는 private coaching source로 사용할 수 없음 |
| Exact tyre slip force/contact patch force | NOT POSSIBLE with current contract | header의 일부 slip/grip arrays는 obsolete이고 authoritative modern force channel이 아님 |
| Missing historical v0.2.1/v0.2.2 telemetry reconstruction | NOT POSSIBLE | 과거에 저장하지 않은 high-rate values를 결과 JSON에서 소급 생성할 수 없음 |
| Exact events inside a dropped sample gap | NOT POSSIBLE | interpolation은 estimate이며 raw measurement가 아님; quality/drop metadata로 표시 |

## 7. 현재와 목표 verdict

현재:

```text
COACHING READY: PARTIAL
REASON: runtime/local archive/core real signals/synthetic stored-only proof PASS; authoritative local-owner privacy, outer loss completeness, clean multi-lap, semantic validation and real staging upload PENDING
```

P023의 모든 required gate 통과 후:

```text
COACHING READY: YES (defined metrics)
CLIENT UPDATE NEEDED: NO
EXCEPTIONS: novel out-of-schema signals, new SHM versions, authoritative semantics not exposed by SHM
```

어떤 null channel을 synthetic 0으로 채워서 `POSSIBLE`을 만들지 않는다. capability/quality/access state를 함께 확인한 후 metric을 계산한다.

Client metadata의 `driverTelemetry=true`는 attempt-local input 관측 hint일 뿐 durable availability나 owner proof가 아니다. Web/coach availability는 Server의 `capabilitySource=DURABLE_CHUNK_INDEX` 및 visibility-aware `streamCapabilities`를 사용하되, 그것만으로 Client source authority가 해결됐다고 간주하지 않는다. 전체 P023은 **YELLOW/HOLD**이고 운영에는 배포하지 않았다.
