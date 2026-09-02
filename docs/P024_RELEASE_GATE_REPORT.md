# AMS2 P024 Compact Telemetry Release Gate Report

## Final decision

| Item | Result |
|---|---|
| **FINAL VERDICT** | **YELLOW** |
| **STABLE RELEASE** | **HOLD** |
| **CLOSED BETA EXCEPTION** | **GO — explicit operator authorization, `0.2.3-beta.1` Pre-release only** |
| product size gate | **GREEN — `465,279 B <= 512 KiB`** |
| synthetic fidelity | **GREEN — 11/11 and fidelity PASS** |
| stable baseline | `0.2.2` (unchanged) |
| Closed Beta candidate | `0.2.3-beta.1` |
| local Server candidate | Application `1.6.0`, schema `15` |
| Cafe24 beta-compatible production | Application `1.6.0`, schema `15`, release `20260902-001` |
| commit / push / tag / GitHub Release | authorized for Pre-release only |
| Cafe24 deployment | authorized for schema-15 beta compatibility with backup, dry-run and rollback |

## Required final report snapshot

```text
AMS2 P024 COMPACT TELEMETRY FINAL REPORT

FINAL VERDICT: YELLOW
BASE VERSION: 0.2.2
CANDIDATE: 0.2.3-beta.1 CLOSED BETA; STABLE RELEASE NOT APPROVED

PROTOCOL NAME: AMS2_COMPACT_TELEMETRY
PROTOCOL VERSION: 1
ENCODING: fixed-schema little-endian A2CT binary
COMPRESSION: gzip transport/canonical archive

FIXED SCHEMA: PASS
FIELD NAMES ON WIRE: 0
SHM USEFUL FIELDS ACCOUNTED: 161/161

HIGH RATE DRIVER FIELD COUNT: 7
OLD: 222 @ 20 Hz
FAST RATE: 20 Hz
MOTION RATE: 5 Hz
SLOW RATE: 1 Hz

REPLAY MODE: adaptive
REPLAY BASE RATE: 0.5 Hz progress
REPLAY BATTLE RATE: 2 Hz
REPLAY BURST RATE: 5 Hz

REFERENCE SIZE: 161,390,596 B gzip
COMPACT RAW: 2,781,797 B
COMPACT WIRE: 465,279 B
SERVER STORED: 465,449 B local canonical gzip; MariaDB physical bytes NOT MEASURED
REDUCTION: 99.71170625084004%

<1 MiB: YES
<=512 KiB: YES
<=256 KiB: NO
<=128 KiB: NO

STREAM WIRE BREAKDOWN
SESSION: 289 B
STORY: 2,915 B
REPLAY: 216,971 B
DRIVER_FAST: 125,891 B
DRIVER_MOTION: 36,265 B
DRIVER_SLOW: 1,350 B
INCIDENT: 9,779 B
OTHER (track geometry + driver change + integrity): 71,819 B

OFFLINE PROOF
LAP TABLE / POSITION / 2D REPLAY / SPEED / BRAKE / THROTTLE / STEERING: PASS
G-FORCE / DRIVING LINE / TRACK CENTERLINE / INCIDENT: PASS
TOTAL: 11/11

COACHING FIDELITY
BRAKING POINT: 0.001910112361 m maximum difference
THROTTLE ON: 0.000561797757 m maximum difference
MIN SPEED: 0.003039787348 m/s maximum difference
LINE VARIANCE: 0.004982608736 m RMS
CONSISTENCY: PASS, exact

PRIVATE DRIVER AUTHORITY: UNATTESTED; LOCAL_PENDING_OWNER
UNAUTHORIZED PRIVATE UPLOAD: BLOCKED
END-TO-END COMPLETENESS: FAIL for release; actual public v9 close path PASS
OUTER QUEUE LOSS PROPAGATED: YES in durable client loss-ledger/fault regressions
FINALIZE ACK: PASS locally; post-finalize server receipt contract remains open

T1 POLICY: PARTIAL/HOLD — compact static/change facts plus lossless legacy metadata compatibility
T2 POLICY: PASS synthetic — declared events and transition/snapshot policy fixed
T5 POLICY: PASS synthetic / PARTIAL real — full schema, real burst lacks full pre-roll

REAL AMS2 SHORT RUN: PASS
REAL AMS2 CLEAN LAP: NOT RUN
REAL AMS2 MULTIPLAYER: NOT RUN
REAL AMS2 INCIDENT: PARTIAL

CPU OLD: NOT MEASURED on a matched P023 scene
CPU NEW: 2.996% active-race short-run average
LOCAL DISK/HOUR OLD: 153.9 MiB
LOCAL DISK/HOUR NEW: 0.443725 MiB fixed-fixture wire projection;
                     real 60-minute high-water NOT MEASURED

EXTERNAL STORAGE REQUIRED: NO
CAFE24 PRODUCTION: APP 1.6.0 / SCHEMA 15 / RELEASE 20260902-001; COMPACT E2E PASS
STABLE RELEASE: HOLD
CLOSED BETA PRE-RELEASE: GO BY EXPLICIT OPERATOR AUTHORIZATION
```

Known limitations and the next recommendation are release actions below; the recommended direction
remains Server/Web Replay + Driver Analysis + Race Coach only after the listed real-runtime, authority,
receipt, performance, and staging gates are closed.

Final-product-v1 clears the 512 KiB product target, passes the compact-only synthetic analyzer, and
passes the local PHP decode/storage replay for all `78/78` frames. A subsequent real AMS2 run also
closed the two transition-time encoder exceptions and persisted all eight Compact runtime schemas
with durable/finalize acknowledgement. A later v9 active-Race run then emitted the previously missing
runtime `LOSS_LEDGER_V1` and terminal `ATTEMPT_FINALIZE_V1` frames and reproduced them through the PHP
Server Decoder. That does **not** make P024 releasable. Low-rate
`SESSION_METADATA` still uses legacy JSON/gzip compatibility, the real run retained cadence loss and
did not include clean laps or actual multiplayer, and comparative gameplay performance remains
unproven. The later authorized Closed Beta deployment did close one real-v9 Compact
Client/HTTPS/PHP/PDO/MariaDB/filesystem round trip, but it does not satisfy the remaining stable-release gates.

Primary evidence:

- [official final-product-v1 machine report](../work/p024/compact-proof-final-product-v1/p024-machine-report.json)
- [post-real-fix proof rerun](../work/p024/compact-proof-post-real-fixes-v1/p024-machine-report.json)
- [post-integrity-runtime proof rerun](../work/p024/compact-proof-post-integrity-v1/p024-machine-report.json)
- [official proof summary](../work/p024/compact-proof-final-product-v1/proof-summary.json)
- [compact protocol](COMPACT_PROTOCOL_V1.md)
- [schema registry](COMPACT_SCHEMA_REGISTRY.md)
- [161-field migration matrix](P023_FIELD_TO_COMPACT_V1_MATRIX.md)
- [fidelity report](COMPACT_FIDELITY_REPORT.md)
- [size report](COMPACT_SIZE_BENCHMARK.md)
- [real AMS2 compact validation](REAL_AMS2_COMPACT_VALIDATION.md)
- [v9 runtime integrity ledger](../work/p024/real-ams2-capture-product-v9/activity/future-telemetry/attempt-ledgers/7b4474ba8607d0e568813dea279018af.attempt-loss.json)
- [Server archive contract](../../AMS2League/server/cafe24_telemetry014/docs/TELEMETRY_ARCHIVE_CONTRACT.md)
- [Server compact report](../../AMS2League/server/cafe24_telemetry014/docs/P024_SERVER_COMPACT_PROTOCOL_REPORT.md)

The official machine report was generated at `2026-09-02T07:29:16.7325687Z`. Its frozen
`serverStorageMeasurementStatus=REQUIRES_SERVER_REPLAY` records the state at client-proof generation;
the subsequent Server report closes that local replay. Earlier final-v2/final-v3 totals remain
superseded. After the real sentinel/domain fixes, the complete proof was rerun and reproduced exactly
`2,781,797 B` raw, `465,279 B` wire, 11/11 and fidelity PASS. After connecting the runtime
`0x50/0x51` close path, it was rerun again with the same exact size and fidelity result.

## Gate matrix

| Gate | Evidence | Status | Release interpretation |
|---|---|---|---|
| Fixed binary protocol | `A2CT` V1, fixed 88-byte header | GREEN implementation | exact header/flags/body-hash contract documented |
| Immutable ordinals | 12 schema IDs; typed string IDs 1–7 | GREEN | unknown schema/field-count mismatch fail closed |
| Field names on wire | `0` | GREEN | final archive scan |
| SHM useful-field lineage | `161/161` | GREEN lineage | matrix accounts for useful inventory rows |
| Typed semantic strings | Race Story `45/45` exact | GREEN C# synthetic | final archive contains typed Story/Incident dictionaries |
| Race Story | `storyExact=true` | GREEN synthetic | numeric rows and Event Type/ID/Fact Code exact |
| Replay | RMS gates pass; position mismatches `0` | GREEN synthetic / YELLOW real | real 49-car AI Race persisted `13,226` rows; actual multiplayer/visual comparison absent |
| Driver coaching inputs | declared differences pass | GREEN synthetic / YELLOW real | real position/speed/brake/RPM/acceleration changed; controls and clean laps incomplete |
| Incident | participant set exact; typed labels preserved | GREEN synthetic / YELLOW real | real `CRASH_STATE_CHANGE` 114-sample burst; full pre-roll absent |
| Compact-only analyzer | `11/11` | GREEN synthetic | reads persisted A2CT/gzip only, no SHM |
| Compact size `<1 MiB` | `465,279 B` | GREEN | `583,297 B` below limit |
| Product size `<=512 KiB` | `465,279 B` | **GREEN** | `59,009 B` below cap |
| Stretch `<=256 KiB` | `465,279 B` | not achieved | `203,135 B` over |
| High-rate runtime routing | Story/Replay/Driver/Incident A2CT | GREEN implementation and short run | v6 produced schema `0x01,0x10,0x20,0x21,0x30..0x33`; v5 produced `0x40` |
| Compact-only metadata | `SESSION_METADATA` legacy compatibility remains | **HOLD** | data preserved, but full compact-only coverage not achieved |
| Driver transition policy | 31 discrete fields transition-only at input cadence | GREEN regression | gear `4 -> 5 -> 4` within 100 ms retained |
| Generic Driver policy | non-dedicated remainder `0.05 Hz` / 20 s | GREEN declared synthetic / YELLOW real | real cadence/size unmeasured |
| Completeness/loss/finalize | v9 actual `0x50 → 0x51`, fault injection | GREEN local wire / **YELLOW release** | v9 accepted/durable `4,783/4,783`, loss 0, COMPLETE; v6 Driver-rich attempt still has 674 cadence loss and post-finalize upload-failure receipt is open |
| Private authority | fail closed, `LOCAL_PENDING_OWNER` | GREEN safety / YELLOW capability | authoritative owner proof unavailable |
| Server decode/storage | official `78/78`, real v6 and v9 replay | GREEN local and beta E2E | actual beta Client upload 201, detail GET 200, raw hash exact |
| Cafe24/MariaDB beta deployment | release `20260902-001` | **GREEN for Closed Beta** | backup/dry-run/migrations 014+015/schema verification PASS; DB compact payload 0 B, canonical `.a2ct.gz` 2,887 B |
| Real AMS2 validation | 49-car grid → Drive → Race | **YELLOW / HOLD** | compact/durable PASS; clean laps, control changes and actual multiplayer absent |
| Gameplay CPU/FPS/disk | short-run Client CPU measured | **HOLD** | active CPU avg `2.996%`; comparable P023 CPU/FPS baseline and 60-minute high-water mark absent |

## Protocol and runtime contract

```text
NAME: AMS2_COMPACT_TELEMETRY
VERSION: 1
LOGICAL ENCODING: fixed-schema, little-endian A2CT
HEADER: 88 bytes
VALID FLAGS: 0x0007 fixed cadence, 0x000B irregular delta time
DICTIONARIES: participant entries, then typed strings IDs 1..7
NULLS: per-ordinal two-bit presence states plus mixed bitmaps
TRANSPORT: gzip recommended or identity
AT REST: validated canonical .a2ct.gz plus index metadata
```

Header byte `10..11` is the `u16 stringDictionaryCount`; byte `42..43` is the participant count.
The body contains participant dictionaries first, typed dictionaries second, then presence and
ordinal columns. Numeric zero is not null. Header body SHA-256, decoded-content SHA-256, received-gzip
SHA-256, and canonical-archive SHA-256 are distinct scopes.

High-rate runtime artifacts use A2CT:

| Family | Shipping/final policy |
|---|---|
| Driver Fast | 7 compact fields at 20 Hz |
| Driver Motion | 5 fields at 5 Hz |
| Driver Slow | 4 fields at 1 Hz |
| Driver Change | 31 discrete fields input-cadence transition-only; remainder 0.05 Hz / 20 s |
| Replay | progress 0.5 Hz base, 2 Hz battle, 5 Hz bursts; world 5 s base + bursts; extension 20 s |
| Replay artifact shape | same timestamp/participant progress, world, and extension facts merged into one row |
| Incident | 20 Hz source evidence, -3 s through +3 s |

The old 222-field-at-20-Hz JSON shape is not retained. `SESSION_METADATA` is deliberately preserved
as low-rate legacy JSON/gzip compatibility alongside compact Session Static because several session
text and capability semantics do not yet have complete schema routing. This is a compatibility/HOLD
boundary, not missing data.

## Official size result

```text
REFERENCE P023 JSON/GZIP: 161,390,596 B
COMPACT RAW:                 2,781,797 B
COMPACT GZIP/WIRE:             465,279 B
FRAMES / SAMPLES:                    78 / 224,625
DB INDEX ESTIMATE:               19,968 B (model only)
LOCAL PHP CANONICAL GZIP:        465,449 B
LOCAL INDEX JSONL:                63,422 B (not MariaDB)
LOCAL ARCHIVE + JSONL:           528,871 B (not production storage)
CANONICAL + DB MODEL:            485,417 B (model only)
REDUCTION:                    99.71170625084004%
```

| Size target | Result |
|---|---|
| `<1 MiB` | PASS |
| `<=512 KiB` | **PASS** |
| `<=256 KiB` | FAIL |
| `<=128 KiB` | FAIL |

The local PHP replay accepted and persisted all `78/78` frames, preserved `480` merged Replay rows,
and re-inflated byte-exact A2CT. Canonical gzip is `170 B` above client gzip; `77/78` received gzip
files are byte-identical to canonical storage. The deterministic JSONL is a test index surrogate and
already contains `8,346 B` of storage-key text. It is not MariaDB/InnoDB, so actual Cafe24 filesystem
and database physical allocation remain unknown. Product size is the `465,279 B` client wire gate;
the local two-artifact test total is a different measurement.

## Wire breakdown

| Family | Frames | Samples | Wire bytes |
|---|---:|---:|---:|
| Session | 1 | 1 | `289` |
| Story | 13 | 45 | `2,915` |
| Replay (merged) | 12 | 94,023 | `216,971` |
| Track geometry | 1 | 290 | `1,452` |
| Driver Fast | 12 | 72,000 | `125,891` |
| Driver Motion | 12 | 18,000 | `36,265` |
| Driver Slow | 12 | 3,600 | `1,350` |
| Driver Change | 12 | 36,180 | `70,184` |
| Incident | 1 | 484 | `9,779` |
| Integrity | 2 | 2 | `183` |
| **Total** | **78** | **224,625** | **`465,279`** |

## Fidelity and semantic proof

| Check | Official result |
|---|---:|
| offline outputs | `11/11 PASS` |
| Race Story | `45/45` exact, including typed strings |
| generic Driver ordinals | `201` |
| incident participant set | exact |
| replay position mismatches | `0` |
| progress RMS / maximum | `0.023794716978120398 m` / `0.04999999998835847 m` |
| world RMS / maximum | `0.4449389370947096 m` / `9.321372112546586 m` |
| braking-point maximum difference | `0.0019101123606901638 m` |
| throttle-on maximum difference | `0.0005617977569727373 m` |
| minimum-speed maximum difference | `0.003039787348448897 m/s` |
| driving-line RMS | `0.0049826087357102465 m` |
| lap consistency | exact |

The world maximum is diagnostic, not a declared maximum-error guarantee. The rejected v6 history
produced `665,881 B` but world RMS `1.14931886392784 m` (maximum `12.165854583258 m`), above the
predeclared `1.0 m` gate. It remains rejected. Final-product-v1 passes without deleting evidence or
relaxing tolerance.

## Real AMS2 short-run result

The final real run used AMS2 build `3398`, SHM v14, Bathurst 2020, 49 raw participants including one
Safety Car (`48` League participants), and Client version `0.2.2`. Network upload remained disabled.
The Client was attached before the grid, entered Drive, observed the Race transition, and stopped
cleanly after about 150 seconds.

| Measurement | v6 result |
|---|---:|
| SHM batches | `2,926` |
| Compact frames / legacy metadata chunks | `8 / 1` |
| Compact raw / wire | `219,198 B / 46,272 B` |
| validation input wire including metadata | `52,978 B` |
| real Replay rows | `13,226` |
| Driver Fast / Motion / Slow / Change rows | `1,517 / 454 / 105 / 1,063` |
| outer/archive queue loss | `0 / 0` |
| worker/serialization/disk/finalize failures | `0 / 0 / 0 / 0` |
| finalize / durable ACK | `true / true` |
| cadence missed | `674` source slots |
| attempt completeness | `PARTIAL` |

Persisted-only validation passed chunk integrity, world position, lap distance, speed, brake, RPM,
acceleration, and bounded-input checks. Throttle, steering, and gear did not change. A separate v7
attempt confirmed that Windows synthetic keyboard events were not accepted as AMS2 vehicle input;
the missing control exercise is not reported as codec success.

The earlier v5 transition persisted an `INCIDENT_V1` `CRASH_STATE_CHANGE` burst with `114` samples and
two participant refs. It began at the trigger because the Client attached at that point, so full
three-second pre-roll remains unproven. See [real AMS2 compact validation](REAL_AMS2_COMPACT_VALIDATION.md)
for the complete run sequence, field ranges, loss accounting, and performance samples.

### Runtime integrity follow-up (v9)

After wiring the integrity schemas into the shipping close path, a new network-disabled client was
attached to the same 49-car active Race for `119.976 s`. It processed `2,390` SHM batches with no
runtime drop or failure and produced six public Compact frames plus one legacy metadata chunk.

| Measurement | v9 result |
|---|---:|
| `LOSS_LEDGER_V1` | sequence `30`, one clean row `0 / 0 / 0` |
| `ATTEMPT_FINALIZE_V1` | sequence `31`, final artifact |
| accepted / durable work | `4,783 / 4,783` |
| known loss / completeness | `0 / COMPLETE(2)` |
| integrity raw / wire | `192 B / 194 B` |
| all Compact / total wire | `5,354 B / 8,594 B` |
| JSON finalize / durable ACK | `true / true` |

The PHP decoder reproduced both exact rows, hashes, sequences and sidecars. The local Server replay
ingests preceding public chunks before the integrity pair, returns `201`, and verifies idempotent
retry. v9 does not replace the v6 Driver evidence: its local Driver source was unresolved and accepted
work for that stream was zero. It proves the actual public attempt-close path only.

## Private authority

AMS2 SHM v14 does not provide authoritative local-owner/spectator/player-ID attestation. Safe policy:

```text
private capture -> local durable archive -> LOCAL_PENDING_OWNER -> upload denied
public story/replay/incident -> independently eligible for upload
```

Schemas `0x0030..0x0033` remain private. Nickname equality, viewed participant, input activity, and
an installation bearer token are not ownership proof. The Server must reject private upload before
archive creation until an authoritative mechanism exists.

## Server boundary

The candidate contract requires the Server to:

1. accept gzip (recommended) or identity transport;
2. inflate and validate exact A2CT, header/body sizes, registry ordinals, presence, dictionaries, and hashes;
3. validate optional decoded and compressed transport hashes in their correct scopes;
4. canonical-gzip the exact logical frame into private `.a2ct.gz` storage;
5. store only bounded searchable metadata and hashes in MariaDB;
6. revalidate before on-demand decode/range output;
7. preserve legacy P023 decode independently;
8. fail closed for private schemas.

The official local replay accepted/persisted `78/78` frames: 30 public and 48 private. The 48 private
frames entered a trusted storage-test path only; the normal HTTP authorization contract remains
fail-closed. Content and archive hashes were unique `78/78`, duplicate ingest was idempotent, and
re-inflated bytes matched every original logical frame. Final regression totals are compact `69/69`,
telemetry `34/34`, run `40/40`, Web03 `37/37`, portal `55/55` (`235/235` PHP assertions total), plus
PHP lint `87/87` and the separate official replay `78/78`.

The real v6 replay additionally proves public `4/4` HTTP `201`, private `4/4` HTTP `403` without
retention, trusted isolated storage `8/8`, and byte-exact A2CT reinflate. Its `13,226 × 35` Replay
frame remains protected by `413` for unfiltered full-detail output, while the actual `2..1,000 ms`
range returns exactly `245` selected rows with HTTP `200`. The decoder validates full columns but
materializes only selected indexes; it does not raise the immutable response-cell guard. P023 legacy
sequence `0` and P024 Session Static sequence `0` are keyed independently by schema name.

The real v9 replay then proves the complete public attempt in capture order: legacy Session Metadata
`1/1` and compact `6/6` received HTTP `201`, all compact detail was byte-exact, and the seven-artifact
index placed `0x51` last. The terminal rows decoded as loss source/count/reason `0/0/0` followed by
accepted/durable/loss/completeness `4,783/4,783/0/2`; byte-identical `0x50` and `0x51` retries returned
HTTP `200` duplicate. Full Replay detail remained protected by `413`, while `1..1,000 ms` returned
exactly `245` rows with HTTP `200`. See the
[v9 local server report](../../AMS2League/server/cafe24_telemetry014/docs/P024_REAL_AMS2_V9_FINALIZE_SERVER_VALIDATION.md).

Production remains unchanged. The candidate holds the archive shard lock through DB commit and removes
only a canonical file created by the failed transaction before releasing that lock. Source and static
tests cover that rollback compensation; an abrupt process/host failure can still leave a crash-only
orphan, so grace-period reconciliation and authorized PDO failure injection remain staging work.

## Remaining blockers

1. Low-rate `SESSION_METADATA` remains legacy JSON/gzip compatibility, so full compact-only coverage is not proven.
2. Real Compact short-run and a partial incident burst now exist, but two clean laps, changing
   throttle/steering/gear, actual multiplayer, and full incident pre-roll are absent.
3. Cafe24 staging migration, gzip/identity upload, retrieval, range decode, private deny, filesystem
   permissions, rollback, and physical MariaDB/filesystem bytes are unproven.
4. Real public zero-loss close/finalize is proven in v9. Full Driver-rich zero-loss completeness is
   not: v6 retained `674` cadence-missed source slots and correctly classified that attempt `PARTIAL`.
5. Active-race Client CPU averaged `2.996%`, but matched P023 CPU/FPS, background upload throughput,
   and 60-minute disk high-water measurements are absent.
6. Private owner attestation is unavailable; private upload remains intentionally disabled.
7. Grace-period reconciliation for the residual crash-between-file-and-commit orphan case is not
   implemented; normal transaction rollback compensation is implemented but lacks PDO staging proof.
8. Upload failure can occur after the client writes terminal `0x51`; a later server receipt or
   terminal upload ledger contract is still required for post-finalize delivery failures.

## Required actions before release

1. Preserve the official 78-frame client proof and local PHP replay as immutable regression fixtures.
2. Complete typed session metadata/capability routing, or obtain an explicit product decision to
   retain the compatibility exception; never call the current archive fully compact-only.
3. Preserve v6 as the real transition regression, then run two physical-input clean laps, actual
   multiplayer, and a full pre/post-roll incident; visually compare Replay.
4. Re-run P023 and P024 on the same scene to compare CPU/FPS, then measure 60-minute local disk high
   watermark and staging upload behavior.
5. Validate schema 15 on non-production Cafe24 staging, including MariaDB and filesystem accounting.
6. Keep the client at `0.2.2`. Only after every required gate is GREEN may version/release work be
   considered under separate authorization.

Current conclusion: **the product-size problem and real transition encoder failures are solved for
their fixed evidence; P024 release readiness is not.**
