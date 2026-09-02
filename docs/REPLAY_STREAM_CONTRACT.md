# Participant Replay Stream Contract

작성 기준: 2026-09-02 KST
stream: `PARTICIPANT_REPLAY`
schema: `ams2-telemetry-chunk-v1`
row schema: **36 fields = immutable 17-field prefix + append-only 19-field extension**

## 1. Purpose and authority

This is the 5 Hz, all-participant fact stream for a future server-side replay, position timeline, pit/race-state timeline, and multi-witness comparison. The client does not create a track image, canonical centreline, 60 fps source data, fault finding, or official classification.

The file body is the logical JSON envelope compressed with gzip. `data.fields` defines positional rows in `data.rows`; consumers must use the field names from the envelope rather than assuming an unlisted schema revision.

## 2. Compatibility rule

Fields 0–16 are the v1 prefix and will remain in this exact order. A reader that understands only the prefix may read those 17 cells and ignore trailing cells. New fields are appended only; no prefix field is renamed, reordered, or given a different unit by this contract.

`null` always means that this capture did not expose a usable value (for example unavailable participant data, non-finite source value, or unknown capability). It is not numeric zero and is not a server-side interpolation.

## 3. Exact row schema

### v1 prefix (0–16)

| index | field | meaning / unit |
|---:|---|---|
| 0 | `sessionElapsedMs` | monotonic milliseconds from this client capture attempt; never wall-clock ordering |
| 1 | `participantRef` | session-scoped compact participant identity |
| 2 | `slot` | raw AMS2 participant slot |
| 3 | `generation` | slot reuse/rejoin generation |
| 4 | `lap` | observed current lap ordinal |
| 5 | `lapDistanceMeters` | observed lap progress in metres when exposed |
| 6 | `racePosition` | raw observed race position, not official classification |
| 7–9 | `worldX`, `worldY`, `worldZ` | raw AMS2 world-coordinate components; coordinate convention/scale must not be inferred beyond parser inventory |
| 10 | `raceStateRaw` | raw integer state |
| 11 | `pitStateRaw` | raw integer pit state |
| 12 | `nameRef` | optional index into `dictionaries.names` |
| 13 | `vehicleRef` | optional index into `dictionaries.vehicles` |
| 14 | `vehicleClassRef` | optional index into `dictionaries.vehicleClasses` |
| 15 | `headingRadians` | current adapter projection from participant orientation Y; semantic axis/sign remains pending real multi-lap validation |
| 16 | `speedMetersPerSecond` | participant source speed in metres/second when usable |

The `(slot, generation)` pair and `participantRef` distinguish a slot reused after a rejoin. Dictionary strings are intentionally not repeated in every sample.

### Append-only extension (17–35)

| index | field | meaning / unit / caveat |
|---:|---|---|
| 17 | `lapsCompleted` | raw observed completed-lap counter |
| 18 | `sectorRaw` | raw current-sector value; not a translated display label |
| 19–21 | `currentSector1TimeSeconds`, `currentSector2TimeSeconds`, `currentSector3TimeSeconds` | participant current sector timings in source seconds; null when unavailable or non-finite |
| 22 | `lapInvalidated` | 0/1 raw participant invalidation flag |
| 23–25 | `orientationRawX`, `orientationRawY`, `orientationRawZ` | raw participant orientation vector/components; preserves information beyond the heading projection |
| 26 | `nationalityRaw` | raw nationality enum/integer; server owns display translation |
| 27 | `pitScheduleRaw` | raw participant pit-schedule enum/integer |
| 28–29 | `highestFlagColourRaw`, `highestFlagReasonRaw` | raw participant highest-flag colour/reason enums |
| 30 | `bestLapTimeSeconds` | participant best lap in source seconds |
| 31 | `lastLapTimeSeconds` | participant last completed lap in source seconds |
| 32–34 | `fastestSector1TimeSeconds`, `fastestSector2TimeSeconds`, `fastestSector3TimeSeconds` | participant fastest sector timings in source seconds |
| 35 | `isActive` | 0/1 participant active state observed in this frame; missing frames must still be interpreted with chunk quality and the story transition record |

Raw enums are intentionally retained without client-side sporting interpretation. Time values are raw source seconds, not rounded UI strings. The extension exposes no control, tyre, fuel, suspension, or other private driver-physics fields.

## 4. Cadence, chunking, and range use

- Target cadence is 5 Hz (200 ms); duplicate higher-rate input frames are downsampled rather than represented as source samples.
- Chunk duration is 30 seconds. `startElapsedMs`, `endElapsedMs`, lap range, quality counters, gzip hash, and payload hash are retained in the envelope/sidecar/index.
- A due-slot gap records the current usable frame once and records missing/dropped quality; it does not fabricate intermediate positions.
- A consumer may interpolate for presentation, but interpolated frames must remain distinguishable from persisted facts.

The synthetic 60-minute/32-car archive produced 120 replay chunks and 576,000 rows. Its replay gzip total is **68,595,629 B**. This is fixture evidence for persistence and replay plumbing, not a claim of real 60-minute AMS2 performance.

## 5. Privacy and visibility

`visibility: PUBLIC_REPLAY` is a candidate evidence class, not automatic publication or official approval. Server event, access, and League policy decide who can retrieve it. The stream must not contain driver controls, private root telemetry, secrets, tokens, IP addresses, Windows usernames, or repeated Steam identifiers.

## 6. Validation and release state

Fixture coverage verifies the 36 positional fields, prefix compatibility, dictionaries, cadence selection, gzip/hash, range metadata, and attempt separation. A short, one-participant AMS2 practice run established that replay rows persisted and that world-coordinate/speed values changed, but it did not establish a completed lap, multi-car geometry, rejoin behavior, orientation convention, or multi-witness merge.

The archive/release remains **HOLD/YELLOW** pending production raw-archive deployment plus the remaining real-runtime validation. No reader may promote raw position or timing facts to an official result.
