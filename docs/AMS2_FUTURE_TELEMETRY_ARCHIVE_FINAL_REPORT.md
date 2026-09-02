# AMS2 FUTURE TELEMETRY ARCHIVE FINAL REPORT

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`

FINAL VERDICT:
YELLOW

BASE VERSION:
0.2.2

NEW VERSION:
0.2.2 — `0.2.3` candidate는 release gate 미완료로 version bump/tag/release 보류

SHM FIELD INVENTORY:
166 rows — useful 161, excluded 5

USEFUL FIELDS CAPTURED:
161/161 represented — 160/160 raw leaf rows full-shape durable + `R008` participant-array container

SESSION METADATA:
FAIL — four observed-stream booleans는 attempt-local 관측 hint로 구현/검증됐지만 durable availability 증거가 아니다. declared T1 direct placement가 30/39이며 9 fields는 private T4에만 존재하고, outer batch queue drop/worker failure도 chunk quality/completeness에 완전 전파되지 않는다

RACE STORY:
PASS — minimum detector 25/25 + `FULL_COURSE_YELLOW_END` + participant active baseline/tombstone. 모든 T2/change field의 generic old/new journal은 별도 policy gap

ALL-PARTICIPANT REPLAY:
PASS — code/fixture; 실제 multi-car full-race proof는 미수행

REPLAY RATE:
5 Hz

WORLD POSITION:
PASS — header/parser/fixture 및 실제 short-run X/Y/Z 변화 확인

LAP DISTANCE:
PASS — 실제 source 변화 확인; clean full-lap progression은 미검증

LOCAL DRIVER TELEMETRY:
FAIL for privacy assurance — 공식 v14 header에는 `mViewedParticipantIndex` 외 spectator/local-owner/player-ID signal이 없고 game state도 spectator-playing을 구분하지 못하며 input/control 값도 authority가 아니다. viewed/root playing 일치 gate와 Server private visibility는 구현됐지만 spectator remote-follow를 배제하지 못한다

LOCAL TELEMETRY RATE:
20 Hz

THROTTLE:
PASS

BRAKE:
PASS

STEERING:
PASS — unfiltered steering -1..1 변화; filtered steering은 실제 short run에서 0 고정

CLUTCH:
PASS — filtered source 변화; unfiltered clutch는 실제 short run에서 0 고정

SPEED:
PASS

RPM:
PASS

GEAR:
PASS

LONG ACCEL:
PASS for raw capture/change; axis/unit semantics PARTIAL

LAT ACCEL:
PASS for raw capture/change; axis/unit semantics PARTIAL

TYRE DATA:
PARTIAL — all useful raw arrays durable; pressure scale, wear direction과 장거리 의미 검증 미완료

DAMAGE:
PARTIAL — raw channels durable; controlled real damage event 미수행

INCIDENT BURST:
FAIL for release assurance — bounded ring/synthetic proof와 unit fixture의 trigger-related + 50 m 이내 최대 4 nearby participant 선택은 PASS지만 실제 multiplayer incident는 미수행이고 declared T5 direct placement는 10/17

INCIDENT RATE:
20 Hz, candidate 기준 -3 s~+3 s

OFFLINE FULL SESSION:
PASS — synthetic persisted-gzip-only 60분/32대, 11/11 outputs

CLIENT CRASH RECOVERY:
PASS — atomic/recovery/restart tests; disk-full/permission/antivirus-lock 실 fault injection은 미완료

END-TO-END CAPTURE COMPLETENESS:
FAIL — outer batch queue drop/worker failure가 stream별 chunk quality와 session completeness에 완전 전파되지 않고 close/finalize acknowledgement도 release-grade가 아님

CHUNKED UPLOAD:
PASS — client contract와 local actual HTTP round trip; local index의 durable/visibility-aware stream capability도 PASS. Cafe24/PDO/MariaDB staging은 미검증

GZIP:
PASS

## 60 MIN / 32 CARS

RACE STORY:
62,801 B raw / 35,397 B gzip

REPLAY:
162,735,772 B raw / 68,595,629 B gzip

LOCAL TELEMETRY:
213,253,405 B raw / 92,706,086 B gzip

INCIDENT:
156,535 B raw / 52,254 B gzip

SESSION METADATA:
7,142 B raw / 1,230 B gzip

TOTAL:
376,215,655 B raw (358.787208 MiB)

COMPRESSED:
161,390,596 B gzip (153.914066 MiB)

10 CLIENT EVENT ESTIMATE:
1,613,905,960 B gzip (1,539.140663 MiB), 단순 선형/중복제거 전

30HZ FULL SHM STREAM:
NO

SERVER RAW ARCHIVE:
FAIL for production — live application 1.4.2/schema 13. Local 1.5.0/schema 14 candidate, InMemoryStore round trip과 `DURABLE_CHUNK_INDEX` capability summary는 PASS

HISTORICAL REPROCESSING:
PASS for local immutable gzip archive; production telemetry archive는 아직 없음

## CLIENT-OFFLINE WEB PROOF

POSITION CHART:
PASS

2D REPLAY:
PASS

SPEED GRAPH:
PASS

BRAKE GRAPH:
PASS

THROTTLE GRAPH:
PASS

DRIVING LINE:
PASS

INCIDENT ANIMATION:
PASS

COACHING READY:
PARTIAL — raw signal은 보존됐지만 authoritative local-owner 판정과 end-to-end completeness가 보장되지 않음

FUTURE WEB REQUIRES CLIENT UPDATE:
NO for the listed already-stored public-schema features. 그러나 private coaching 공개 전에는 authoritative owner 또는 fail-closed capture policy와 outer drop/failure quality propagation을 Client에 추가해야 한다. 다른 예외는 final T1/T5 cadence/visibility policy changes, a generic T2 journal requirement, novel out-of-schema signals, a new SHM version, or semantics AMS2 does not expose다.

KNOWN SHM LIMITATIONS:

1. Actual clean multi-lap, multi-car/rejoin and real incident evidence is absent; heading, acceleration axes/units, tyre pressure scale and wear direction remain semantic gates.
2. Other participants' pedal/steering telemetry, authoritative corner labels and incident fault/blame are not exposed as trustworthy SHM facts and are not fabricated.
3. 공식 v14 header에는 `mViewedParticipantIndex` 외 spectator/local-owner/player-ID signal이 없고 game state나 input/control activity도 authority가 아니다. 현재 resolver는 spectator remote-follow를 배제하지 못하며, 1인 session/Time Attack 허용 역시 heuristic일 뿐이다. 아울러 outer batch queue drop/worker failure가 durable chunk quality에 전부 귀속되지 않아 orderly end marker만으로 전체 capture를 COMPLETE라고 판정할 수 없다. Production도 application 1.4.2/schema 13이며 Cafe24 TLS/FastCGI/PDO/MariaDB captured-gzip round trip이나 real 60-minute pressure/fault test는 없다.

NEXT RECOMMENDATION:
Authority interface의 default를 DENY로 두고 authoritative attestation이 생기기 전에는 shipping Runtime의 `DRIVER_TELEMETRY`를 OFF/fail-closed로 둔다. 1인 session/Time Attack 예외도 ownership proof가 아닌 명시적 heuristic으로만 취급한다. outer batch/worker drop을 stream별 chunk quality와 session completeness에 전파한 뒤 T1/T5 placement와 T2 journal policy를 닫고 production-isolated schema-14 staging full multiplayer capture/raw-gzip round trip을 실행한다. 모든 gate가 GREEN인 뒤에만 Web Replay + Driver Telemetry Analysis + Race Coach로 진행한다.

## Verification ledger

| Check | Result |
|---|---|
| Release solution build | PASS, warnings 0 / errors 0 |
| `AMS2LeagueActivity.Tests` | 68/68 PASS |
| `AMS2LeagueClient.Tests` | 37/37 PASS, Windows DPAPI included |
| Server suites | 164/164 PASS |
| PHP lint | 82/82 PASS |
| v5 Server validator | five stream shapes PASS; Story 23, Replay 36, Driver 222, Incident 47 fields; four metadata booleans true는 observation hint일 뿐 |
| Synthetic offline renderer | 11/11 PASS |
| Production mutation | NONE |

## Evidence

아래 경로는 모두 repository root (`outputs/AMS2KRLeague`) 기준 상대 경로다.

- `../AMS2League/evidence/future-telemetry/synthetic-60min-32car-v5-stream-capabilities-20260902/fixture-manifest.json`
- `../AMS2League/evidence/future-telemetry/offline-proof-60min-32car-v5-stream-capabilities-20260902/proof-summary.json`
- `../AMS2League/evidence/future-telemetry/offline-proof-60min-32car-v5-stream-capabilities-20260902/telemetry-proof.html`
- `../AMS2League/evidence/future-telemetry/server-validator-v5-stream-capabilities-20260902.json`
- `../AMS2League/evidence/future-telemetry/production-health-readonly-20260902.json`
- `../AMS2League/evidence/future-telemetry/real-ams2-v023-candidate-20260902/controlled2/`
- `../../work/local-http-e2e/evidence/`

`FINAL VERDICT: YELLOW`이므로 version bump, commit, tag, push, GitHub Release 및 production deploy를 수행하지 않았다.
