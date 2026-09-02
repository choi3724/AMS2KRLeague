# P024 Compact Telemetry Fidelity Report

Verdict: **PASS for the current typed-string 60-minute synthetic archive; real AMS2 fidelity is not
yet proven**

Primary evidence:

- [final-product-v1 machine report](../work/p024/compact-proof-final-product-v1/p024-machine-report.json)
- [final-product-v1 compact-only proof summary](../work/p024/compact-proof-final-product-v1/proof-summary.json)
- [final-product-v1 offline proof renderer](../work/p024/compact-proof-final-product-v1/compact-offline-proof.html)
- [rejected v6 machine report](../work/p024/compact-proof-v6/p024-machine-report.json)
- [analyzer implementation](../tools/AMS2CompactProof/CompactProofAnalyzer.cs)
- [local Server compact report](../../AMS2League/server/cafe24_telemetry014/docs/P024_SERVER_COMPACT_PROTOCOL_REPORT.md)

Generated evidence timestamp: `2026-09-02T07:29:16.7325687Z` (`2026-09-02 16:29:16.7325687 KST`). The reference workload is the same
60-minute, 32-participant P023 semantic fixture used for the fixed acceptance baseline. The analyzer
read `78` persisted `.a2ct.gz` frames (`224,625` decoded samples) and did not read AMS2 shared
memory (`sharedMemoryRead=false`).

## 1. Acceptance criteria

The predeclared aggregate fidelity predicate in `CompactProofAnalyzer` is:

| Metric | Gate |
|---|---:|
| every measured quantization field | observed max <= declared max |
| replay position history | `0` mismatches |
| replay progress RMS | <= `2.0 m` |
| reconstructed replay world RMS | <= `1.0 m` |
| braking-point maximum difference | <= `2.0 m` |
| throttle-on maximum difference | <= `2.0 m` |
| minimum-speed maximum difference | <= `0.02 m/s` |
| driving-line RMS | <= `0.02 m` |

Race Story semantic-string/numeric exactness, incident participant-set exactness, 11/11 output checks,
and lap consistency are also reported separately. The tolerance was not enlarged to accept a smaller
candidate.

## 2. Quantization results

All measured values remained within the schema's declared half-step error.

| Field | Samples | Resolution | Declared max error | Observed max error | Result |
|---|---:|---:|---:|---:|---|
| throttle | 72,000 | `1/255` | `0.001960784314` | `0.001954863275` | PASS |
| brake | 72,000 | `1/255` | `0.001960784314` | `0.001958765219` | PASS |
| steering | 72,000 | `1/32767` | `0.000015259255` | `0.000015241320` | PASS |
| speed | 72,000 | `0.01 m/s` | `0.005 m/s` | `0.004998013888 m/s` | PASS |
| lap distance | 72,000 | `0.01 m` | `0.005 m` | `0.004943820240 m` | PASS |
| longitudinal acceleration | 72,000 | `0.01 m/s²` | `0.005 m/s²` | `0.004999159634 m/s²` | PASS |
| lateral acceleration | 72,000 | `0.01 m/s²` | `0.005 m/s²` | `0.004991806512 m/s²` | PASS |
| driver world X | 18,000 | `0.01 m` | `0.005 m` | `0.004999858961 m` | PASS |
| driver world Y | 18,000 | `0.01 m` | `0.005 m` | `0.004966044786 m` | PASS |
| driver world Z | 18,000 | `0.01 m` | `0.005 m` | `0.004991358054 m` | PASS |
| driver heading | 18,000 | `0.0001 rad` | `0.00005 rad` | `0.000049922430 rad` | PASS |
| RPM | 18,000 | `1 rpm` | `0.5 rpm` | `0.499509402860 rpm` | PASS |

The sample counts reflect the selected cadence split: fast driver facts at 20 Hz and driver motion at
5 Hz. Quantization PASS does not by itself validate downsampling; replay and coaching comparisons
below cover the practical outputs.

Replay uses its own public-display precision, distinct from the private Driver Motion schema:

| Replay field | Resolution | Quantization half-step |
|---|---:|---:|
| lap distance / world X/Y/Z | `0.1 m` | `0.05 m` |
| heading | `0.002 rad` | `0.001 rad` |
| speed | `0.1 m/s` | `0.05 m/s` |

The final report does not publish a separate maximum for every replay column. The aggregate progress,
world-reconstruction, and position comparisons below are the acceptance evidence for this replay-only
precision change.

## 3. Race, replay, and incident fidelity

| Check | Final result |
|---|---:|
| Race Story reference event count | `45` |
| Race Story compact event count | `45` |
| Race Story resolved strings + numeric rows | `45/45` exact / PASS |
| incident participant set | exact / PASS |
| position mismatches | `0` / PASS |
| replay comparison samples | `576,000` |
| lap-progress RMS | `0.023794716978 m` |
| lap-progress maximum | `0.049999999988 m` |
| reconstructed world RMS | `0.4449389370947096 m` |
| reconstructed world maximum | `9.321372112546586 m` |

The world RMS includes reconstruction of the 5 Hz, 32-participant reference from sparse world
keyframes and the derived track centerline. `0.4449389370947096 m` passes the predeclared `1.0 m` RMS
limit. The `9.321372112546586 m` maximum is reported as a diagnostic; the current analyzer has no
world-maximum gate. It must therefore remain visible in future visual review and real-track testing
rather than being described as a bounded maximum-error guarantee.

### Semantic-string evidence

Final-product-v1 was regenerated with typed dictionaries. The inspected first Story frame has eight typed
entries and the Incident frame has two. The analyzer resolves Event Type, Event ID, and Fact Code and
includes their equality in `storyExact=true`; all 45 Story events pass. Incident candidate/trigger
typed values survive the codec round trip, while the machine report separately records the incident
participant set exact. The fresh PHP storage/decoder replay accepted all `78/78` frames and
re-inflated byte-exact A2CT. It proves the local cross-language wire/storage boundary, not semantic
Web projections or an authorized MariaDB/Cafe24 deployment.

### Discrete Driver Change evidence

The shipping adapter inspects its 31 discrete Driver Change fields at input cadence and emits only
transitions. The [runtime regression](../tests/AMS2LeagueActivity.Tests/FutureTelemetryRuntimeAdapterTests.cs)
injects gear `4 -> 5 -> 4` within 100 ms and verifies that all three values survive in order. The final
candidate also uses the shipping five-minute Driver Change frame boundary (12 frames over the hour)
and 20-second snapshots for the non-discrete remainder. This closes
the specific short-transition collapse regression; it does not replace a real AMS2 cadence/CPU run.

## 4. Coaching metric comparison

The same analyzer compared `41` reference/compact laps.

| Coaching output | Final difference | Gate | Result |
|---|---:|---:|---|
| braking point | `0.001910112361 m` maximum | `2.0 m` | PASS |
| throttle-on distance | `0.000561797757 m` maximum | `2.0 m` | PASS |
| corner minimum speed | `0.003039787348 m/s` maximum | `0.02 m/s` | PASS |
| driving-line variance proxy | `0.004982608736 m` RMS | `0.02 m` | PASS |
| lap consistency | exact | exact | PASS |

These are analyzer-output comparisons, not a claim that a Race Coach model has been implemented or
validated. The client records sources; coaching interpretation remains a server/analyzer concern.

## 5. Persisted compact-only offline proof

The proof source was `PERSISTED_COMPACT_A2CT_GZIP_ONLY`; no P023 JSON rows or live SHM were used by the
decode/render phase.

| Required output | Result |
|---|---|
| Lap Table | PASS |
| Position Chart | PASS |
| 2D Replay | PASS |
| Speed Graph | PASS |
| Brake Graph | PASS |
| Throttle Graph | PASS |
| Steering Graph | PASS |
| G-force Graph | PASS |
| Driving Line | PASS |
| Track Centerline | PASS |
| Incident Animation | PASS |

Total: **11/11 PASS** for final-product-v1. Binary inspection found `0` registered field names on
wire. The generic driver
archive covered `201` distinct driver catalog ordinals in addition to the dedicated fast/motion/slow
schemas.

The 11 output checks prove that each analyzer path can be regenerated from the persisted compact
fixture. They are not a substitute for visual quality review of a real clean lap, multiplayer replay,
or real incident.

## 6. Deliberately rejected smaller candidate

The rejected v6 candidate recorded:

- wire: `665,881 B` (`200,602 B` larger than the current final candidate)
- reconstructed world RMS: `1.14931886392784 m`
- reconstructed world maximum: `12.165854583258 m`

Because `1.14931886392784 m` exceeded the predeclared `1.0 m` RMS limit, v6 had
`fidelityPass=false` and was rejected. The current `465,279 B` candidate is smaller through
replay-specific quantization, merged replay rows, and 20-second sparse context—not by reusing v6's
failed world policy. It produces `0.4449389370947096 m` world RMS with no field deletion and no
tolerance relaxation.

## 7. Scope and remaining evidence

The current typed-string synthetic archive passes its declared fidelity gate. The shipping runtime is
now wired to the compact adapter, but P024 release fidelity remains incomplete until compact-only
outputs are checked from:

- a real AMS2 short run containing throttle, brake, steering, speed, RPM, gear, world position, and
  lap distance;
- preferably at least two clean laps;
- a multiplayer race with two or more real participants;
- an incident burst captured from a safe test.

Until those real runs exist, the appropriate release decision is **HOLD**, even though the synthetic
fidelity predicate, semantic Story check, 11/11 proof, and local PHP `78/78` byte-exact replay pass.
MariaDB/Cafe24 staging and real AMS2 evidence remain separate gates.
