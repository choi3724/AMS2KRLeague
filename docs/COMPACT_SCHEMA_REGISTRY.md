# AMS2 Compact Telemetry V1 Schema Registry

Status: **immutable local candidate / not released**

Protocol: `AMS2_COMPACT_TELEMETRY`, version `1`

The authoritative current definitions are the
[C# registry](../src/AMS2LeagueClient.Core/CompactTelemetry/CompactTelemetrySchema.cs) and the
[local PHP registry](../../AMS2League/server/cafe24_telemetry014/app/CompactTelemetryProtocol.php).
The tables below reflect those definitions as inspected on 2026-09-02. A schema is identified by its
numeric ID; its field names are documentation/decoded-output names and do not occur in high-rate
binary frames.

Raw AMS2 SHM lineage is normative in
[P023_FIELD_TO_COMPACT_V1_MATRIX.md](P023_FIELD_TO_COMPACT_V1_MATRIX.md). “Source” below names the
canonical P023 fact family; the matrix supplies the exact `Pxxx`/`Rxxx` raw field mapping and calls
out the low-rate compatibility routing that is not compact-only or release-closed.
Synthetic evidence is in the
[final-product-v1 machine report](../work/p024/compact-proof-final-product-v1/p024-machine-report.json); cross-language
storage/decoder evidence is in the
[local Server replay report](../../AMS2League/server/cafe24_telemetry014/docs/P024_SERVER_COMPACT_PROTOCOL_REPORT.md).

## Registry rules

- Ordinals are zero-based, contiguous, and immutable within V1.
- `decoded = quantized * scale`; all current offsets are zero.
- Quantized range is inclusive.
- Width is present only for fixed encodings; `-` means VarUInt-based.
- Null is the frame presence bitmap, never a numeric sentinel.
- Privacy is fixed by schema. Request metadata cannot promote a private schema to public.
- A semantic, encoding, scale, range, or ordinal change requires a new schema ID/version.

### Typed string dictionary registry

String dictionaries are immutable protocol IDs separate from numeric schema IDs. Header bytes
`10..11` count their entries; the body places them after the participant dictionary. Entries are
ordered by ID and references are contiguous from zero within each ID.

| ID | Name | Current compact-adapter source | Numeric reference consumer |
|---:|---|---|---|
| 1 | `EVENT_TYPE` | Race Story `eventTypes` | `RACE_EVENT_V1.eventTypeRef` |
| 2 | `EVENT_ID` | Race Story `eventIds` | `RACE_EVENT_V1.eventIdRef` |
| 3 | `FACT_CODE` | Race Story `factCodes` | `RACE_EVENT_V1.factCodeRef` |
| 4 | `INCIDENT_CANDIDATE` | Incident `candidates` | `INCIDENT_V1.candidateRef` |
| 5 | `INCIDENT_TRIGGER_CODE` | Incident `triggerCodes` | `INCIDENT_V1.triggerCodeRef` |
| 6 | `SESSION_TEXT` | six matrix sources documented; shipping routing pending | future session ref |
| 7 | `DRIVER_TEXT` | Driver `tyreCompounds` | source catalog refs carried by private driver facts |

The C# codec unit test round-trips typed dictionary values, and the local PHP decoder implements the
same ID/order/reference rules. Final-product-v1 contains Story and Incident typed entries;
`storyExact=true` resolves and compares Event Type, Event ID, and Fact Code strings as part of its
45/45 check. The fresh local PHP storage/decoder replay accepted all `78/78` official frames and
re-inflated byte-exact A2CT. MariaDB/Cafe24 staging is still pending.

## Schema summary and measured cadence

Cadence describes the final-product-v1 synthetic proof. The shipping `ActivityCaptureRuntime` now selects the
compact adapter for high-rate artifacts, but real AMS2 cadence/fidelity remains unverified.

| ID | Schema | Canonical stream | Privacy | Fields | Final proof cadence/policy |
|---:|---|---|---|---:|---|
| `0x0001` | `SESSION_STATIC_V1` | `SESSION_METADATA` | public | 2 | attempt start, one sample |
| `0x0002` | `SESSION_CHANGE_V1` | `SESSION_METADATA` | public | 2 | irregular change events; no final-proof frame |
| `0x0010` | `RACE_EVENT_V1` | `RACE_STORY` | public | 21 | exact irregular events |
| `0x0020` | `PARTICIPANT_REPLAY_V1` | `PARTICIPANT_REPLAY` | public | 35 | adaptive progress; sparse world/context keyframes |
| `0x0021` | `TRACK_GEOMETRY_V1` | `PARTICIPANT_REPLAY` | public | 4 | one derived first-lap centerline, about 20 m bins |
| `0x0030` | `DRIVER_FAST_V1` | `DRIVER_TELEMETRY` | private | 7 | 20 Hz / 50 ms |
| `0x0031` | `DRIVER_MOTION_V1` | `DRIVER_TELEMETRY` | private | 5 | 5 Hz / 200 ms |
| `0x0032` | `DRIVER_SLOW_V1` | `DRIVER_TELEMETRY` | private | 4 | 1 Hz / 1,000 ms |
| `0x0033` | `DRIVER_CHANGE_V1` | `DRIVER_TELEMETRY` | private | 2 | discrete fields transition-only at input cadence; remainder 0.05 Hz / 20 s |
| `0x0040` | `INCIDENT_V1` | `INCIDENT_TRACE` | public | 45 | 20 Hz source window, -3 s through +3 s, irregular multi-participant rows |
| `0x0050` | `LOSS_LEDGER_V1` | `SESSION_METADATA` | public | 3 | final integrity sample |
| `0x0051` | `ATTEMPT_FINALIZE_V1` | `SESSION_METADATA` | public | 4 | acknowledged final sample |

The adaptive replay proof uses a 0.5 Hz all-participant progress base, 2 Hz close-battle checks, 5 Hz
start/incident/end bursts, and exact position/pit transitions. World coordinates are normally sampled
every 5 seconds, with 5 Hz start, incident, and end windows. Replay extension context is sampled every
20 seconds. Progress, world, and extension facts for the same timestamp/participant are merged into one
`PARTICIPANT_REPLAY_V1` row and one replay artifact family; they are not repeated as three frames. These
rates passed the final synthetic analyzer but remain subject to real AMS2 validation.

## `0x0001 SESSION_STATIC_V1`

Meaning/source: attempt-level numeric facts from P023 session metadata. Privacy: `PUBLIC_REPLAY`.
Low-rate textual session metadata remains in the legacy JSON/gzip compatibility record. The matrix
maps six source strings to `SESSION_TEXT`, but the shipping adapter does not yet route those refs into
compact session artifacts.

| Ord | Field / meaning | Unit | Encoding | Width | Scale | Quantized range |
|---:|---|---|---|---:|---:|---|
| 0 | `trackLengthMeters` — circuit length | m | `FIXED_UNSIGNED` | 4 | `0.01` | `0..2,000,000` |
| 1 | `maxRpm` — observed engine limit | rpm | `FIXED_UNSIGNED` | 2 | `1` | `0..65,535` |

## `0x0002 SESSION_CHANGE_V1`

Meaning/source: generic P023 session/global value changes. Privacy: `PUBLIC_REPLAY`. The ordinal/value
shape and scale are defined; the migration matrix identifies shipping adapter mappings that still
remain in the low-rate metadata compatibility record.

| Ord | Field / meaning | Unit | Encoding | Width | Scale | Quantized range |
|---:|---|---|---|---:|---:|---|
| 0 | `fieldOrdinal` — source registry ordinal | index | `RLE_UNSIGNED` | - | `1` | `0..65,535` |
| 1 | `rawValue` — quantized generic numeric value | source-specific | `ZIGZAG` | - | `0.001` | `Int64 min..max` |

## `0x0010 RACE_EVENT_V1`

Meaning/source: irregular P023 Race Story facts. Privacy: `PUBLIC_REPLAY`. The current adapter carries
the three semantic label catalogs through typed dictionaries 1–3. Final-product-v1's `45/45`
`storyExact=true` result compares the resolved strings and numeric values.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `eventTypeRef` | dictionary ref | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 1 | `eventIdRef` | dictionary ref | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 2 | `factCodeRef` | dictionary ref | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 3 | `participantRef` | participant ref | `RLE_UNSIGNED` | `1` | `0..4,095` |
| 4 | `lap` | lap ordinal | `RLE_ZIGZAG` | `1` | `-1..65,535` |
| 5 | `sector` | sector ordinal | `RLE_ZIGZAG` | `1` | `-1..3` |
| 6 | `lapDistanceMeters` | m | `DELTA_ZIGZAG` | `0.01` | `0..2,000,000` |
| 7 | `worldX` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 8 | `worldY` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 9 | `worldZ` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 10 | `positionBefore` | race ordinal | `RLE_ZIGZAG` | `1` | `-1..4,096` |
| 11 | `positionAfter` | race ordinal | `RLE_ZIGZAG` | `1` | `-1..4,096` |
| 12 | `lapTimeMs` | ms | `DELTA_ZIGZAG` | `1` | `0..2,147,483,647` |
| 13 | `raceStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 14 | `pitStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 15 | `flagColourRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 16 | `flagReasonRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 17 | `penaltyTypeRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 18 | `resultStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 19 | `yellowFlagStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 20 | `participantIsActiveRaw` | raw boolean | `RLE_ZIGZAG` | `1` | `Int32 min..max` |

## `0x0020 PARTICIPANT_REPLAY_V1`

Meaning/source: P023 participant replay facts. Privacy: `PUBLIC_REPLAY`. Progress, world-keyframe, and
sparse extension observations share the same immutable schema. The shipping adapter merges observations
with an identical timestamp and participant into one row; presence states identify which ordinal
groups that row carries.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `participantRef` | participant ref | `RLE_UNSIGNED` | `1` | `0..4,095` |
| 1 | `slot` | participant slot | `RLE_UNSIGNED` | `1` | `0..4,095` |
| 2 | `generation` | slot generation | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 3 | `lap` | lap ordinal | `RLE_ZIGZAG` | `1` | `-1..65,535` |
| 4 | `lapDistanceMeters` | m | `DELTA_ZIGZAG` | `0.1` | `0..200,000` |
| 5 | `racePosition` | race ordinal | `RLE_ZIGZAG` | `1` | `-1..4,096` |
| 6 | `worldX` | m | `DELTA_ZIGZAG` | `0.1` | `Int32 min..max` |
| 7 | `worldY` | m | `DELTA_ZIGZAG` | `0.1` | `Int32 min..max` |
| 8 | `worldZ` | m | `DELTA_ZIGZAG` | `0.1` | `Int32 min..max` |
| 9 | `raceStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 10 | `pitStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 11 | `nameRef` | dictionary ref | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 12 | `vehicleRef` | dictionary ref | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 13 | `vehicleClassRef` | dictionary ref | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 14 | `headingRadians` | rad | `DELTA_ZIGZAG` | `0.002` | `-3,142..3,142` |
| 15 | `speedMetersPerSecond` | m/s | `DELTA_ZIGZAG` | `0.1` | `0..6,554` |
| 16 | `lapsCompleted` | laps | `RLE_ZIGZAG` | `1` | `-1..65,535` |
| 17 | `sectorRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 18 | `currentSector1TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 19 | `currentSector2TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 20 | `currentSector3TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 21 | `lapInvalidated` | raw boolean | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 22 | `orientationRawX` | rad | `DELTA_ZIGZAG` | `0.00001` | `-100,000,000..100,000,000` |
| 23 | `orientationRawY` | rad | `DELTA_ZIGZAG` | `0.00001` | `-100,000,000..100,000,000` |
| 24 | `orientationRawZ` | rad | `DELTA_ZIGZAG` | `0.00001` | `-100,000,000..100,000,000` |
| 25 | `nationalityRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 26 | `pitScheduleRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 27 | `highestFlagColourRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 28 | `highestFlagReasonRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 29 | `bestLapTimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 30 | `lastLapTimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 31 | `fastestSector1TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 32 | `fastestSector2TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 33 | `fastestSector3TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 34 | `isActive` | raw boolean | `RLE_ZIGZAG` | `1` | `Int32 min..max` |

## `0x0021 TRACK_GEOMETRY_V1`

Meaning/source: sparse centerline derived from first-lap driver/replay world observations. Privacy:
`PUBLIC_REPLAY`.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `lapDistanceMeters` | m | `DELTA_ZIGZAG` | `0.01` | `0..2,000,000` |
| 1 | `worldX` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 2 | `worldY` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 3 | `worldZ` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |

## `0x0030 DRIVER_FAST_V1`

Meaning/source: P023 private viewed-driver control and motion facts. Privacy:
`PRIVATE_DRIVER_ANALYTICS`; upload is denied without authoritative owner attestation.

| Ord | Field / meaning | Unit | Encoding | Width | Scale | Quantized range |
|---:|---|---|---|---:|---:|---|
| 0 | `throttle` — unfiltered preferred | ratio | `FIXED_UNSIGNED` | 1 | `1/255` | `0..255` |
| 1 | `brake` — unfiltered preferred | ratio | `FIXED_UNSIGNED` | 1 | `1/255` | `0..255` |
| 2 | `steering` — unfiltered preferred | ratio | `FIXED_SIGNED` | 2 | `1/32767` | `-32,767..32,767` |
| 3 | `speedMetersPerSecond` | m/s | `DELTA_ZIGZAG` | - | `0.01` | `0..65,535` |
| 4 | `lapDistanceMeters` | m | `DELTA_ZIGZAG` | - | `0.01` | `0..2,000,000` |
| 5 | `longitudinalAccelerationMetersPerSecondSquared` | m/s² | `ZIGZAG` | - | `0.01` | `-32,768..32,767` |
| 6 | `lateralAccelerationMetersPerSecondSquared` | m/s² | `ZIGZAG` | - | `0.01` | `-32,768..32,767` |

## `0x0031 DRIVER_MOTION_V1`

Meaning/source: P023 private viewed-driver line, heading, and engine motion. Privacy:
`PRIVATE_DRIVER_ANALYTICS`.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `worldX` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 1 | `worldY` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 2 | `worldZ` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 3 | `headingRadians` | rad | `DELTA_ZIGZAG` | `0.0001` | `-62,832..62,832` |
| 4 | `rpm` | rpm | `DELTA_ZIGZAG` | `1` | `0..65,535` |

## `0x0032 DRIVER_SLOW_V1`

Meaning/source: selected P023 private slow-changing vehicle condition facts. Privacy:
`PRIVATE_DRIVER_ANALYTICS`.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `fuelLiters` | L | `DELTA_ZIGZAG` | `0.01` | `0..20,000` |
| 1 | `engineDamage` | ratio | `RLE_UNSIGNED` | `1/255` | `0..255` |
| 2 | `aeroDamage` | ratio | `RLE_UNSIGNED` | `1/255` | `0..255` |
| 3 | `trackTemperatureCelsius` | °C | `DELTA_ZIGZAG` | `0.1` | `-500..1,500` |

## `0x0033 DRIVER_CHANGE_V1`

Meaning/source: generic ordinal/value representation for remaining P023 driver telemetry catalog
facts. Privacy: `PRIVATE_DRIVER_ANALYTICS`. The precise source ordinal is the immutable
`TelemetryFieldCatalog.DriverTelemetryFields` ordinal; the wire contains no field name. The current
adapter can attach `tyreCompounds` through typed dictionary 7 (`DRIVER_TEXT`).

The shipping adapter divides this schema into two policies. Thirty-one discrete catalog fields are
inspected at every input row and emitted only when their value changes: driver/lap/sector identity,
gear and clutch states, pit/lap-validity states, flag states, car/ABS/TC/boost/DRS/ERS states,
collision/crash states, clutch-health states, launch stage, and hand brake. All remaining catalog
fields not already represented by `DRIVER_FAST_V1`, `DRIVER_MOTION_V1`, or `DRIVER_SLOW_V1` are 0.05 Hz
snapshots. A regression fixture proves that gear `4 -> 5 -> 4` within 100 ms survives as three ordered
`DRIVER_CHANGE_V1` samples; it is not collapsed by the 20-second snapshot cadence. The final candidate
groups the synthetic Driver Change archive into 12 five-minute frames and reports all 201 generic ordinals
at 0.05 Hz. The transition regression is separate from that stable-value size workload; real archive
size/cadence remains a release gate.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `fieldOrdinal` | catalog index | `RLE_UNSIGNED` | `1` | `0..65,535` |
| 1 | `rawValue` | source-specific | `ZIGZAG` | `0.001` | `Int64 min..max` |

## `0x0040 INCIDENT_V1`

Meaning/source: P023 incident-candidate raw facts for related participants. Privacy:
`PUBLIC_REPLAY`. It records evidence, not fault, blame, or culprit inference. The current adapter
carries candidate and trigger labels through typed dictionaries 4 and 5. Final-product-v1 carries those
typed values and separately reports the incident participant set exact.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `relativeTimeMs` | ms from candidate | `DELTA_ZIGZAG` | `1` | `-60,000..60,000` |
| 1 | `candidateRef` | dictionary ref | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 2 | `triggerCodeRef` | dictionary ref | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 3 | `participantRef` | participant ref | `RLE_UNSIGNED` | `1` | `0..4,095` |
| 4 | `slot` | participant slot | `RLE_UNSIGNED` | `1` | `0..4,095` |
| 5 | `generation` | slot generation | `RLE_UNSIGNED` | `1` | `0..2,147,483,647` |
| 6 | `lap` | lap ordinal | `RLE_ZIGZAG` | `1` | `-1..65,535` |
| 7 | `lapDistanceMeters` | m | `DELTA_ZIGZAG` | `0.01` | `0..2,000,000` |
| 8 | `racePosition` | race ordinal | `RLE_ZIGZAG` | `1` | `-1..4,096` |
| 9 | `worldX` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 10 | `worldY` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 11 | `worldZ` | m | `DELTA_ZIGZAG` | `0.01` | `Int32 min..max` |
| 12 | `raceStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 13 | `pitStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 14 | `flagColourRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 15 | `flagReasonRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 16 | `participantDisappeared` | raw boolean | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 17 | `positionChangeMagnitude` | m | `ZIGZAG` | `0.01` | `0..2,147,483,647` |
| 18 | `headingRadians` | rad | `DELTA_ZIGZAG` | `0.0001` | `-62,832..62,832` |
| 19 | `speedMetersPerSecond` | m/s | `DELTA_ZIGZAG` | `0.01` | `0..65,535` |
| 20 | `lapsCompleted` | laps | `RLE_ZIGZAG` | `1` | `-1..65,535` |
| 21 | `sectorRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 22 | `currentSector1TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 23 | `currentSector2TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 24 | `currentSector3TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 25 | `lapInvalidated` | raw boolean | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 26 | `orientationRawX` | rad | `DELTA_ZIGZAG` | `0.00001` | `-100,000,000..100,000,000` |
| 27 | `orientationRawY` | rad | `DELTA_ZIGZAG` | `0.00001` | `-100,000,000..100,000,000` |
| 28 | `orientationRawZ` | rad | `DELTA_ZIGZAG` | `0.00001` | `-100,000,000..100,000,000` |
| 29 | `nationalityRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 30 | `pitScheduleRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 31 | `highestParticipantFlagColourRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 32 | `highestParticipantFlagReasonRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 33 | `bestLapTimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 34 | `lastLapTimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 35 | `fastestSector1TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 36 | `fastestSector2TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 37 | `fastestSector3TimeSeconds` | s | `DELTA_ZIGZAG` | `0.001` | `-1,000..2,147,483,647` |
| 38 | `isActive` | raw boolean | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 39 | `yellowFlagStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |
| 40 | `viewedParticipantRef` | participant ref | `RLE_ZIGZAG` | `1` | `-1..4,095` |
| 41 | `collisionOpponentSlotRaw` | participant slot | `RLE_ZIGZAG` | `1` | `-1..4,095` |
| 42 | `collisionOpponentRef` | participant ref | `RLE_ZIGZAG` | `1` | `-1..4,095` |
| 43 | `collisionMagnitude` | source magnitude | `ZIGZAG` | `0.001` | `0..2,147,483,647` |
| 44 | `crashStateRaw` | raw enum | `RLE_ZIGZAG` | `1` | `Int32 min..max` |

## `0x0050 LOSS_LEDGER_V1`

Meaning/source: per-attempt/per-stream loss classification from the client capture pipeline. Privacy:
`PUBLIC_REPLAY`.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `lossSourceCode` | enum code | `RLE_UNSIGNED` | `1` | `0..255` |
| 1 | `lossCount` | occurrences | `VAR_UINT` | `1` | `0..2,147,483,647` |
| 2 | `reasonCode` | enum code | `RLE_UNSIGNED` | `1` | `0..65,535` |

`lossSourceCode` is immutable in V1: `0 NONE`, `1 SHM_SOURCE_GAP`,
`2 OUTER_QUEUE_DROP`, `3 ARCHIVE_INPUT_DROP`, `4 CADENCE_MISSED`,
`5 SERIALIZATION_FAILURE`, `6 DISK_WRITE_FAILURE`, `7 WORKER_EXCEPTION`,
`8 UPLOAD_FAILURE`, `9 FINALIZE_FAILURE`, `10 COMMIT_CONFLICT`.

`reasonCode` identifies the affected stream: `0 NONE`, `1 SESSION_METADATA`,
`2 RACE_STORY`, `3 PARTICIPANT_REPLAY`, `4 DRIVER_TELEMETRY`,
`5 INCIDENT_TRACE`. A clean attempt carries one `NONE / 0 / NONE` row rather
than omitting the ledger. Current runtime accounting has no independent SHM
source-gap counter, so code `1` is reserved and is not inferred from cadence
loss.

## `0x0051 ATTEMPT_FINALIZE_V1`

Meaning/source: acknowledged close and durable completion fact. Privacy: `PUBLIC_REPLAY`.

| Ord | Field / meaning | Unit | Encoding | Scale | Quantized range |
|---:|---|---|---|---:|---|
| 0 | `acceptedWork` | work items | `VAR_UINT` | `1` | `0..2,147,483,647` |
| 1 | `durableCommitAck` | work items | `VAR_UINT` | `1` | `0..2,147,483,647` |
| 2 | `knownLoss` | occurrences | `VAR_UINT` | `1` | `0..2,147,483,647` |
| 3 | `completenessCode` | enum code | `RLE_UNSIGNED` | `1` | `0..255` |

`completenessCode` is immutable in V1: `0 IN_PROGRESS`, `1 PARTIAL`,
`2 COMPLETE`. `acceptedWork` counts accepted runtime work units across streams.
`durableCommitAck` counts the accepted work units covered by a durable-processing
acknowledgement, so the two counts are equal only when every accepted unit was
acknowledged. The runtime writes this frame last, after the loss ledger; its
presence is the non-droppable compact close acknowledgement. For a compact
attempt, `0x0051` is authoritative and the local `attempt-loss.json` is a
post-ack diagnostic mirror; this ordering prevents a durable JSON `COMPLETE`
from appearing before the compact final acknowledgement.

## Current release boundary

The registry and synthetic proof are not proof of shipping capture coverage. The migration matrix
accounts for `161/161` useful inventory rows and closes the earlier SCH/string/Incident schema-shape
blockers. It records the implemented Driver Change transition/snapshot policy and the remaining
session-string compatibility boundary.
`ActivityCaptureRuntime` is now wired to
`COMPACT_A2CT_V1` for high-rate artifacts, with low-rate session metadata retained as legacy JSON/gzip
compatibility. The typed-string codec/store path has isolated tests and final-product-v1 synthetic
evidence. The Driver Change transition/snapshot policy is implemented and regression-tested, but its
full-race cadence and size are not measured. Actual AMS2 v6 emitted all eight runtime data schemas,
v5 emitted Incident, and post-fix v9 emitted real `0x0050 → 0x0051` terminal frames that the PHP
decoder reproduced exactly. Compact-only metadata coverage, physical-input clean laps, multiplayer,
full pre-roll incident, and Cafe24/MariaDB staging remain required before this candidate contract can
be released.
