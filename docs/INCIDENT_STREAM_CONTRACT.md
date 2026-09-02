# Incident Trace Contract

작성 기준: 2026-09-02 KST
stream: `INCIDENT_TRACE`
visibility: `PUBLIC_REPLAY` candidate evidence
row schema: **47 fields = immutable 22-field prefix + append-only 25-field extension**

## 1. Authority

This stream preserves candidate-incident movement and state around a bounded trigger. It never determines fault, blame, contact causality, sporting penalty, or official incident classification. Server-side review/analyzers may use the raw evidence later; a UI must not turn it into a definitive accusation.

The public-replay candidate visibility is not automatic public release. League approval, access policy, and incident-review policy remain server responsibilities. Private driver inputs and root physics are prohibited from this stream.

## 2. Collection boundaries

The collector samples incident frames at 20 Hz, maintains a 10-second in-memory ring, and writes only a related-participant burst: up to 3 seconds before and 3 seconds after a candidate trigger. Trigger-supplied related references are retained first. From the latest/prior ring context, the collector then measures world X/Z distance from related anchors and adds the nearest participants within 50 metres, up to four nearby additions while preserving the total related-participant cap of 8. It limits concurrent candidates to 4 and all participant input to 64 per frame. No candidate means no incident trace is written to disk or uploaded.

Candidate start also emits a separate story fact. At end of session, a missing post-roll is recorded as partial quality rather than fabricated rows. A 30-second bucket is selected by trigger time, but envelope elapsed bounds describe the actual pre/post rows and are authoritative for range reads.

## 3. Compatibility and null rule

The first 22 row cells are the stable v1 prefix. The next 25 are append-only. Consumers that understand only the prefix may ignore all trailing cells. `data.fields` is the source of truth for row positions.

`null` means that a particular source value was unavailable/non-finite or there was no applicable viewed-vehicle relationship. It is not zero, no-contact, or no-fault.

## 4. Exact row schema

### v1 prefix (0–21)

| index | field | meaning / unit |
|---:|---|---|
| 0 | `relativeTimeMs` | milliseconds before/after candidate trigger |
| 1 | `sessionElapsedMs` | monotonic client-attempt time in milliseconds |
| 2 | `capturedAtUnixMs` | UTC evidence timestamp in milliseconds |
| 3–4 | `candidateRef`, `triggerCodeRef` | chunk dictionary indices into `candidates` and `triggerCodes` |
| 5–7 | `participantRef`, `slot`, `generation` | compact identity plus raw slot/rejoin generation |
| 8–10 | `lap`, `lapDistanceMeters`, `racePosition` | observed progress/position, not official classification |
| 11–13 | `worldX`, `worldY`, `worldZ` | raw participant world-coordinate components |
| 14–15 | `raceStateRaw`, `pitStateRaw` | raw participant states |
| 16–17 | `flagColourRaw`, `flagReasonRaw` | raw participant flag values |
| 18 | `participantDisappeared` | 0/1 observed disappearance transition flag |
| 19 | `positionChangeMagnitude` | observed position-change magnitude, not collision severity |
| 20 | `headingRadians` | orientation-Y projection; convention pending incident-scenario validation |
| 21 | `speedMetersPerSecond` | participant source speed in m/s when usable |

### Append-only extension (22–46)

| index | field | meaning / caveat |
|---:|---|---|
| 22 | `lapsCompleted` | raw participant completed-lap counter |
| 23 | `sectorRaw` | raw current sector value |
| 24–26 | `currentSector1TimeSeconds`, `currentSector2TimeSeconds`, `currentSector3TimeSeconds` | source sector times in seconds |
| 27 | `lapInvalidated` | 0/1 participant invalidation flag |
| 28–30 | `orientationRawX`, `orientationRawY`, `orientationRawZ` | raw orientation components, retained in addition to heading projection |
| 31 | `nationalityRaw` | raw nationality integer/enum |
| 32 | `pitScheduleRaw` | raw participant pit-schedule integer/enum |
| 33–34 | `highestParticipantFlagColourRaw`, `highestParticipantFlagReasonRaw` | raw participant highest-flag values |
| 35 | `bestLapTimeSeconds` | observed best lap in source seconds |
| 36 | `lastLapTimeSeconds` | observed last lap in source seconds |
| 37–39 | `fastestSector1TimeSeconds`, `fastestSector2TimeSeconds`, `fastestSector3TimeSeconds` | observed fastest sectors in source seconds |
| 40 | `isActive` | 0/1 participant activity observed in this incident frame |
| 41 | `yellowFlagStateRaw` | raw session yellow-flag-state evidence; do not translate/label client-side |
| 42 | `viewedParticipantRef` | optional current viewed-participant candidate reference for the captured frame; not local-owner proof |
| 43 | `collisionOpponentSlotRaw` | optional raw viewed-vehicle collision-opponent slot |
| 44 | `collisionOpponentRef` | optional resolved session reference for that slot in the current generation |
| 45 | `collisionMagnitude` | optional source-reported collision magnitude; not a fault/severity verdict |
| 46 | `crashStateRaw` | optional raw viewed-vehicle crash state |

The last five fields belong to the frame context, so they can repeat across related participant rows. They do not assert that every row's participant collided, that the opponent relationship is causal, or that a missing field means no collision.

## 5. Quality, witness, and release state

Expected/actual/missing/dropped counters preserve inner-archive capture gaps, selected participant absence, channel pressure, burst caps, and incomplete post-roll. Outer Runtime batch-queue drops and worker failures are not yet fully propagated into chunk quality/session completeness, so these counters are not end-to-end completeness proof. Each witness persists its own candidate trace; the client does not make a multi-witness canonical incident.

Fixtures cover candidate retention between cadence gates, bounded pre/post roll, participant filtering, dictionaries, raw-state extension cells, gzip/hash, and synthetic persisted-only visualization. A unit fixture verifies that a participant inside 50 m is included and a far participant is excluded while trigger-related refs remain present. A one-participant AMS2 practice run produced no candidate, so real crash/contact, real multiplayer nearby selection, collision-field semantics, heading convention during an incident, live multi-witness merge, and server visualization remain unvalidated. Incident release assurance is therefore **FAIL** and the archive/release remains **HOLD/YELLOW**; production was not deployed.
