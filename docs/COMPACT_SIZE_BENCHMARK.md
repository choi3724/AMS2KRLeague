# P024 Compact Telemetry Size Benchmark

Size verdict: **PRODUCT TARGET PASS**

Fidelity prerequisite: **PASS**

Release implication: **size alone is GREEN; the overall release remains HOLD**

Primary evidence:

- [final-product-v1 machine report](../work/p024/compact-proof-final-product-v1/p024-machine-report.json)
- [post-real-fix proof rerun](../work/p024/compact-proof-post-real-fixes-v1/p024-machine-report.json)
- [post-integrity-runtime proof rerun](../work/p024/compact-proof-post-integrity-v1/p024-machine-report.json)
- [final-product-v1 proof summary](../work/p024/compact-proof-final-product-v1/proof-summary.json)
- [final-product-v1 offline proof](../work/p024/compact-proof-final-product-v1/compact-offline-proof.html)
- [rejected v6 report](../work/p024/compact-proof-v6/p024-machine-report.json)
- [Server compact report](../../AMS2League/server/cafe24_telemetry014/docs/P024_SERVER_COMPACT_PROTOCOL_REPORT.md)

The official client proof was generated at `2026-09-02T07:29:16.7325687Z` and is the current client
size authority. Earlier final-v2/final-v3/replay-quant experiment totals are superseded. The frozen
client report records `serverStorageMeasurementStatus=REQUIRES_SERVER_REPLAY`; the subsequent Server
report closes that local replay with all `78/78` frames. The proof rerun after the real runtime fixes
reproduced the same raw/wire totals and 11/11 fidelity result. Cafe24/MariaDB physical measurement
remains open.

The second rerun after the shipping `0x50/0x51` close-path integration again produced exactly
`2,781,797 B` raw and `465,279 B` wire with 11/11 and fidelity PASS.

## 1. Fixed acceptance workload

| Item | Value |
|---|---:|
| duration | 60 minutes |
| participants | 32 |
| clients | 1 |
| semantic source | P023 synthetic archive replay |
| fixed P023 raw baseline | `376,215,655 B` |
| fixed P023 JSON/gzip acceptance baseline | `161,390,596 B` |
| measured regenerated P023 gzip | `161,390,982 B` |

The `386 B` regenerated-gzip difference comes from regenerated fixture identifiers. Gate decisions
use the fixed `161,390,596 B` acceptance baseline.

## 2. Official compact result

| Measurement | Value | Interpretation |
|---|---:|---|
| logical compact A2CT | `2,781,797 B` | uncompressed sum of 78 frames |
| gzip / benchmark wire | `465,279 B` | sum of persisted `.a2ct.gz` files |
| decoded samples | `224,625` | rows reconstructed by the proof analyzer |
| largest logical frame | `161,929 B` | measured `peakWorkingChunkBytes` |
| DB index estimate | `19,968 B` | model only; not measured MariaDB allocation |
| local PHP canonical gzip | `465,449 B` | exact 78-frame trusted storage replay |
| deterministic application-index JSONL | `63,422 B` | test surrogate, not MariaDB/InnoDB allocation |
| local proof archive + JSONL | `528,871 B` | exact sum of two local test artifacts; not production storage |
| modeled canonical + DB estimate | `485,417 B` | `465,449 + 19,968`; model only |
| Cafe24/MariaDB physical storage | **NOT MEASURED** | staging required |

`465,279 B` is `454.374 KiB` (`0.443725 MiB`). It is a `99.71170625084004%` reduction from
the fixed P023 JSON/gzip acceptance baseline. Gzip leaves `16.725843%` of the logical A2CT bytes,
an `83.274157%` reduction at the wrapper layer.

The local PHP replay accepted and persisted `78/78` frames: 30 public and 48 private frames. Private
frames were admitted only by the trusted storage test; normal ingest must still deny them without
owner authority. Reinflated A2CT was byte-exact for every frame, unique content/archive hashes were
`78/78`, and `480` merged Replay rows were retained. Canonical gzip is `170 B` larger than client gzip;
`77/78` source gzip files are byte-identical and one valid recompression accounts for the difference.

The `63,422 B` JSONL is a deterministic application-index surrogate, not InnoDB. Its `8,346 B` of
storage-key text is already inside that JSONL and must not be added again. Likewise, `485,417 B` is a
model, not a measured production total. Cafe24 filesystem allocation and MariaDB physical bytes remain
unknown.

## 3. Target verdict

| Target | Limit | Final wire | Margin | Verdict |
|---|---:|---:|---:|---|
| absolute failure boundary | `>= 1,048,576 B` | `465,279 B` | `583,297 B` below | not failed |
| minimum acceptable | `< 1,048,576 B` | `465,279 B` | `583,297 B` below | PASS |
| product target | `<= 524,288 B` | `465,279 B` | `59,009 B` below | **PASS** |
| stretch target | `<= 262,144 B` | `465,279 B` | `203,135 B` over | FAIL |
| experimental target | `<= 131,072 B` | `465,279 B` | `334,207 B` over | FAIL |

The official result uses `88.7449%` of the 512 KiB cap. The `59,009 B` margin is real but not large
enough to excuse real-session measurement: participant dictionaries, event mix, and runtime chunking
can differ from the fixed synthetic workload.

## 4. Codec tournament

Times are aggregate isolated proof-tool timings, not gameplay CPU or FPS measurements.

| Method | Raw bytes | gzip/wire bytes | Reduction vs P023 gzip | Encode ms | Decode ms | Working chunk | Chunks | Round trip |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| P023 JSON/gzip acceptance | `376,215,655` | `161,390,596` | reference | - | - | - | 284 | reference |
| P023 regenerated replay | `376,215,655` | `161,390,982` | reference replay | - | - | - | 284 | reference |
| fixed binary rows | `330,701,432` | `143,325,257` | `11.193551%` | `716.4265` | `327.0519` | `1,555,207` | 284 | PASS |
| column binary | `298,551,482` | `127,450,255` | `21.029937%` | `1,161.6730` | `331.6894` | `1,404,007` | 284 | PASS |
| delta column Q1e-6 | `96,718,727` | `41,436,714` | `74.325199%` | `983.2079` | `241.0310` | `498,312` | 284 | PASS |
| delta + RLE column Q1e-6 | `88,955,014` | `45,832,194` | `71.601695%` | `981.5288` | `252.6256` | `392,298` | 284 | PASS |
| cadence split + delta/RLE + quantization + adaptive A2CT V1 | `2,781,797` | `465,279` | `99.711706%` | `197.7389` | `115.7976` | `161,929` | 78 | 11/11 PASS |

RLE lowered raw delta-column bytes but made gzip output larger than delta alone. The decisive gains
came from immutable ordinals, cadence separation, sparse/change capture, replay-only quantization,
and merging same-timestamp Replay progress/world/extension facts—not from replacing gzip.

## 5. Final wire breakdown

| Family | Frames | Samples | Logical bytes | gzip/wire bytes | Wire share |
|---|---:|---:|---:|---:|---:|
| Session | 1 | 1 | `1,216` | `289` | `0.062%` |
| Race Story | 13 | 45 | `3,726` | `2,915` | `0.627%` |
| Replay (merged) | 12 | 94,023 | `1,577,456` | `216,971` | `46.632%` |
| Track geometry | 1 | 290 | `2,102` | `1,452` | `0.312%` |
| Driver Fast | 12 | 72,000 | `790,345` | `125,891` | `27.057%` |
| Driver Motion | 12 | 18,000 | `144,280` | `36,265` | `7.794%` |
| Driver Slow | 12 | 3,600 | `8,369` | `1,350` | `0.290%` |
| Driver Change/catalog | 12 | 36,180 | `218,702` | `70,184` | `15.084%` |
| Incident | 1 | 484 | `35,407` | `9,779` | `2.102%` |
| Integrity | 2 | 2 | `194` | `183` | `0.039%` |
| **Total** | **78** | **224,625** | **2,781,797** | **465,279** | **100%** |

Raw fixed headers account for `78 * 88 = 6,864 B`. Because each complete frame is independently
gzip-compressed, no exact compressed-header attribution is claimed.

Replay is now one schema and artifact family. Observations for the same timestamp and participant
are merged without dropping their present fields. Replay lap distance/world coordinates use `0.1 m`,
heading `0.002 rad`, and speed `0.1 m/s` public-display precision. Non-discrete Replay extension and
generic Driver Change snapshots use `20 s` (`0.05 Hz`); the 31 declared discrete Driver Change fields
remain input-cadence transition-only.

## 6. Fidelity-controlled optimization history

The smaller size was not obtained by deleting Race Story, incident, coaching, or generic catalog
facts. Final-product-v1 still reports:

- analyzer outputs `11/11 PASS`;
- `storyExact=true` for `45/45` events, including typed semantic strings;
- `201` generic Driver ordinals;
- incident participant set exact;
- position mismatches `0`;
- progress RMS `0.023794716978120398 m` and world RMS `0.4449389370947096 m`.

The older v6 experiment is retained only as a rejection record. It produced `665,881 B`, but its
world RMS was `1.14931886392784 m` (maximum `12.165854583258 m`), exceeding the predeclared
`1.0 m` RMS gate. Final-product-v1 is both smaller and passing because it uses a different combination
of Replay precision, merged rows, and sparse-context cadence. The tolerance was not relaxed.

## 7. Operational interpretation

- Measured client wire projection: `465,279 B/hour/client` for the fixed fixture.
- Measured logical A2CT: `2,781,797 B/hour/client` before gzip.
- Modeled DB index payload: `19,968 B/hour/client`; not a MariaDB measurement.
- Measured local PHP canonical gzip: `465,449 B/hour/client` for the fixed fixture.
- Measured deterministic local index JSONL: `63,422 B`; local two-artifact total `528,871 B`.
- Modeled canonical plus DB estimate: `485,417 B`; not a MariaDB measurement.
- Actual Cafe24 filesystem and MariaDB physical allocation: not measured.
- Real 49-car v6 short run: `46,272 B` Compact wire plus `6,706 B` legacy metadata over about
  150 seconds. Start burst, pre-grid time and 49 participants make linear 60-minute extrapolation
  invalid for the fixed 32-car acceptance gate.
- Real 49-car v9 active-Race close: `5,354 B` Compact wire plus `3,240 B` legacy metadata over
  `119.976 s`. The newly connected integrity pair is only `194 B` wire (`95 B` loss ledger and
  `99 B` final ACK). v9 attached mid-session and had no Driver stream, so it is an integrity E2E
  fixture, not a replacement 60-minute size workload.
- Active-race Client CPU averaged `2.996%`; matched P023 CPU/FPS, upload throughput, and 60-minute
  local disk high watermark remain unmeasured.

High-rate Race Story, Replay, Driver, and Incident runtime artifacts use A2CT. Low-rate
`SESSION_METADATA` remains lossless legacy JSON/gzip compatibility while its text/capability
semantics lack complete compact routing. The real AMS2 transition run persisted all eight runtime
Compact schemas, but remained `PARTIAL` because of cadence gaps and did not include clean laps or
actual multiplayer. That compatibility boundary, staging, and comparative performance keep the
overall release at **HOLD** even though the product size gate is now **PASS**.
