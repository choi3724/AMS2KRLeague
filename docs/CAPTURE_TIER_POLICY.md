# Capture Tier Policy

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`

## 1. 정책 목표

정보 보존과 frame duplication을 구분한다. 연속 분석에 필요한 값만 정해진 rate로 저장하고, metadata와 discrete fact를 20/30 Hz로 반복하지 않는다. 모든 tier는 League 공식 여부와 무관하게 facts를 보존할 수 있으며 official/public 의미는 Server가 결정한다.

## 2. 기본값

| 설정 | 기본값 | 구현 bound/의미 |
|---|---:|---|
| chunk duration | 30 s | elapsed time bucket |
| Tier 3 replay | 5 Hz | 200 ms gate |
| Tier 4 local driver | 20 Hz | 50 ms gate |
| Tier 5 incident | 20 Hz | 50 ms ring/burst gate |
| incident pre/post | 3 s / 3 s | candidate 기준 |
| incident ring | 10 s | 최대 202 high-rate frames 기본 |
| input channel | 512 messages | full이면 non-blocking drop |
| participant cap | 64/frame | parser maximum보다 큰 input 거부 |
| incident vehicles | 8/candidate | trigger-related + 50 m 이내 거리순 nearby 최대 4명을 포함한 participant ref |
| active incident bursts | 4 | 초과 candidate drop/count |

sampling 설정은 `TelemetryArchiveOptions`에 명시되어 있다. 사용자 수동 개발자 설정을 요구하지 않으며 향후 signed/bootstrap policy를 적용하더라도 안전 range validation을 통과해야 한다.

## 3. Tier 1 — Session Metadata

capture 시점:

- capture/session start
- observed configuration/capability 변화
- participant dictionary/generation 변화
- session/attempt end

보존 후보:

- game build, SHM/client/parser version
- track/layout/length, session type
- duration/configured laps/mandatory pit/weather/temperature/privacy
- observed participant count와 compact dictionary
- supported/observed/not-exposed/unknown capability state
- scheduled event hint
- capture start/end/completeness
- clock source, mid-session join, start offset status

unsupported 값은 합성하지 않고 null과 capability status를 쓴다. metadata는 chunk당 최대 64 records, record당 최대 256 named fields다.

`fields.raceStory`, `fields.replay`, `fields.driverTelemetry`,
`fields.incidentHighRate`는 `TelemetryCapabilityValue.booleanValue`로 해당 attempt에서
stream input을 관측했는지 남긴다. 네 값은 attempt 내에서 false→true로만 변하며
새 attempt에서 초기화된다. false는 미지원이 아니라 “아직 관측하지 못함”이다.
이 metadata는 Runtime 관측 상태를 설명하지만 chunk의 atomic commit/upload 성공을
증명하지 않는다. 실제 durable stream 존재 판정은 Server raw chunk index를 따른다.
Schema 14 후보의 GET session index는 `DURABLE_CHUNK_INDEX` 기반 네 boolean을 별도로
반환하고, private driver 존재 여부도 요청 installation의 visibility를 적용한다.

## 4. Tier 2 — Race Story

UI 표시 여부가 아니라 detector fact를 저장한다. 지원 event type는 문자열 dictionary라 additive event를 수용한다. 최소 목표 목록:

`SESSION_START`, `SESSION_STATE`, `RACE_START`, `POSITION_CHANGE`, `LEADER_CHANGE`, `PODIUM_ENTRY`, `PODIUM_EXIT`, `LAP_COMPLETE`, `PERSONAL_BEST`, `SESSION_FASTEST_LAP`, `PIT_ENTRY`, `PIT_EXIT`, `DRIVE_THROUGH`, `STOP_GO`, `PENALTY_CLEARED`, `RETIREMENT`, `DNF`, `DISQUALIFICATION`, `YELLOW`, `DOUBLE_YELLOW`, `FULL_COURSE_YELLOW`, `RED`, `INCIDENT_CANDIDATE`, `FINISH`, `SESSION_END`.

collector는 event 의미를 재계산하지 않는다. Runtime adapter는 snapshot 변화와 기존 race-control fact를 persistence용 detector에 전달하며, presentation suppression 결과를 persistence gate로 사용하지 않는다. UI가 숨긴 fact도 Race Story archive에는 남을 수 있다.

후보 구현은 minimum 25 detector를 모두 제공하고 additive `FULL_COURSE_YELLOW_END`, participant active baseline/tombstone도 남긴다. 다만 모든 change-candidate raw field에 대해 generic `oldRaw/newRaw` journal을 별도로 만들지는 않는다. 그 값은 T1/T3/T4 raw archive를 재처리하는 설계이며, 이 선택은 P023 GREEN 전에 최종 승인해야 한다.

## 5. Tier 3 — Participant Replay

valid driving frame에서 최대 64 participant를 5 Hz로 보존한다. sample에는 world X/Y/Z, lap distance, heading, speed를 함께 둔다. position/race state/pit state는 raw integer를 유지한다. name/vehicle/class 문자열은 chunk dictionary로 한 번만 기록한다.

5 Hz보다 빠른 source frame은 정상 downsampling이며 drop이 아니다. due slot을 건너뛴 경우 `expectedSampleCount > actualSampleCount`와 `missingSamples`로 표시한다. 브라우저 60 fps는 future Web interpolation 책임이다.

single witness도 저장한다. 여러 witness stream은 Client가 merge하지 않고 각각의 `witnessId`로 보존한다.

## 6. Tier 4 — Viewed/Root Driver Candidate Telemetry

20 Hz 후보로 보존한다. Race, Time Attack, Practice/Testing에 사용할 수 있지만 League/public classification과 분리한다.

현재 구현 gate:

- `InGamePlaying`에서 viewed participant candidate resolution valid
- root/viewed telemetry가 해당 candidate participant와 일치
- `LocalParticipantResolved=true`
- `SourceParticipantRef == DriverRef`

gate 실패 시 replay는 계속되지만 private telemetry row는 없다. 다른 driver input/physics는 생성하지 않는다. 그러나 이 gate는 viewed/root 일치만 확인한다. 공식 v14 header에는 `mViewedParticipantIndex` 외 authoritative local-owner/spectator/player-ID signal이 없고 game state/input activity도 authority가 아니다. 현재 resolver가 spectator remote-follow를 배제하지 못하므로 owner privacy 증거가 아니다. authoritative attestation 전 release-safe 기본은 Tier 4 OFF/fail-closed이며 1인 session/Time Attack 허용도 heuristic일 뿐이다.

privacy는 항상 `PRIVATE_DRIVER_ANALYTICS`다. Server/Web의 braking point, corner loss, consistency, line variance, AI coach는 이 raw stream에서 나중에 계산한다.

## 7. Tier 5 — Incident Trace

상시 all-car 20 Hz upload가 아니다. 20 Hz bounded ring은 memory에만 있고 candidate가 발생하면 기본 -3 s~+3 s를 trigger-related refs 대상으로 chunk에 복사한다. latest/prior ring context의 related anchor world X/Z에서 50 m 이내 participant를 거리순 최대 4명 추가하되 전체 cap 8을 유지한다. near 포함/far 제외는 unit fixture로 검증됐고 실제 multiplayer 사고는 pending이다.

raw flag/reason/race/pit state, disappearance, position change와 world/lap location을 남긴다. candidate/trigger code는 저장하지만 fault/blame/contact causality는 만들지 않는다.

inventory 기준 T5 direct public burst는 17개 후보 중 10개다. 제외된 root-private physics/tyre context 7개(`R069,R071,R072,R073,R074,R077,R083`)는 public incident row로 복사하지 않는다. authoritative owner attestation을 통과한 future private T4가 있을 때만 elapsed-range로 Server-side join할 수 있다. 현재 candidate T4를 owner truth로 사용하면 안 되며 incident burst만으로 self-contained하지 않은 privacy placement 예외다.

candidate refs가 비어 있거나 cap/concurrency를 넘으면 burst는 생성하지 않고 drop을 기록한다. Tier 2에는 `INCIDENT_CANDIDATE` fact가 함께 생성된다.

## 8. clock와 gap

모든 tier primary time은 monotonic `sessionElapsedMs`다. `capturedAtUtc`는 evidence clock이다. current lap time은 primary session clock이 아니다.

rate gate의 quality 규칙:

```text
expectedSampleCount = elapsed cadence상 있어야 한 logical rows
actualSampleCount   = chunk에 실제 기록된 rows
missingSamples      = max(expected - actual, 0)
droppedSamples      = known overflow 또는 missing 수
droppedInputMessages= bounded channel/full 또는 invalid/out-of-order input 수
```

5 Hz/20 Hz보다 빠른 frame을 선택하지 않은 것은 `missing`이 아니다. elapsed gap으로 due slot을 놓친 경우만 missing이다.

이 규칙은 inner archive channel/accumulator가 관측한 손실에 한정된다. 현재 outer Runtime batch queue drop과 worker failure는 stream별 `quality`와 session `captureCompleteness`에 완전 전파되지 않는다. 그러므로 0 drop/`COMPLETE`를 end-to-end 무손실 증거로 사용하지 않으며 이 전파 누락은 release blocker다.

## 9. bounded-memory 규칙

- unbounded channel/list/ring 금지
- participant/string/field/array cap 검증
- 30 s 이전 chunk는 다음 시간 bucket 진입 때 background commit
- completed chunk는 memory에서 제거
- filesystem/gzip/hash는 worker만 수행
- hot-path `TryWrite`는 기다리지 않음
- inner channel full/disk fault를 성공으로 숨기지 않음; outer queue/worker failure 전파는 미완료

30초 active buffer는 crash 전에 아직 durable하지 않을 수 있다. 이미 atomic rename이 끝난 chunk는 recovery 대상으로 남는다.

## 10. 현재 acceptance

policy/rate/inner-quality/incident/viewed-root-gate fixture tests와 Runtime event/fact adapter는 PASS다. incident selection은 trigger-related participant와 50 m 이내 최대 4 nearby participant를 포함하고 먼 participant를 제외하는 unit fixture를 통과했다. 실제 AMS2 84.986초 1인 Practice에서 5 Hz replay 425 rows와 20 Hz viewed/root driver candidate 1,697 rows가 durable gzip으로 보존됐고 core 값 변화가 PASS했다. 이 1인 run은 authoritative owner/spectator privacy를 증명하지 않는다. synthetic 60분/32대에서는 284 chunks와 incident -3~+3초 20 Hz burst까지 persisted-only 재처리했다.

전체 useful inventory는 161 rows이며 raw leaf 160/160이 gzip까지 보존된다(`R008`은 participant array container). 그러나 tier 직접 배치는 T1 `30/39`, T3 `14/14`, T4 `73/73`, T5 `10/17`이다. T1의 9개 private/static 후보는 public metadata가 아니라 private T4에만 둔다. 따라서 raw coverage PASS와 tier policy 최종 승인 상태를 같은 의미로 보고하지 않는다.

남은 acceptance의 최우선은 authoritative local-owner/spectator 판정 또는 fail-closed Tier 4 policy, 그리고 outer batch/worker loss의 durable quality/completeness 전파다. 그 밖에 실제 60분/다수 차량 pressure, 실제 multi-car/incident scenario, clean multi-lap geometry, disk full/permission/antivirus lock fault injection이 남아 있다. 실제 짧은 run의 filtered steering은 고정 0이었고 unfiltered steering만 변했으며, tyre pressure/acceleration의 일부 semantics는 별도 검증이 필요하다. 전체 verdict는 **YELLOW/HOLD**이고 운영에는 배포하지 않았다.
