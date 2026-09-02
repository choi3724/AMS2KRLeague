# Web Handoff: Compact Telemetry

Audience: future Server/Web Replay, Driver Analysis, and Race Coach work

Status: **local candidate contract; not deployed**

Web code does not need to parse `A2CT`. The PHP server owns registry selection, integrity validation,
dequantization, timestamp reconstruction, null reconstruction, and range filtering. Web consumers use
decoded named rows returned by the API or a future canonical service built above that decoder.

Implementation references:

- [PHP compact decoder](../../AMS2League/server/cafe24_telemetry014/app/CompactTelemetryProtocol.php)
- [HTTP routing and on-demand decode](../../AMS2League/server/cafe24_telemetry014/app/Application.php)
- [index/canonical gzip storage](../../AMS2League/server/cafe24_telemetry014/app/PdoStore.php)
- [schema-15 migration](../../AMS2League/server/cafe24_telemetry014/migrations/015_compact_telemetry_protocol.sql)
- [server archive contract](../../AMS2League/server/cafe24_telemetry014/docs/TELEMETRY_ARCHIVE_CONTRACT.md)
- [official client machine report](../work/p024/compact-proof-final-product-v1/p024-machine-report.json)
- [local Server compact report](../../AMS2League/server/cafe24_telemetry014/docs/P024_SERVER_COMPACT_PROTOCOL_REPORT.md)
- [V1 schema registry](COMPACT_SCHEMA_REGISTRY.md)

Local candidate: Application `1.6.0`, schema `15`. Production remains Application `1.4.2`, schema
`13`; do not assume the compact API exists in production.

## 1. Responsibility boundary

```text
Client / archive
  A2CT fixed-schema bytes
        |
        v
Server decoder
  validate -> participant/typed-string dictionaries -> presence -> timestamps -> dequantize
        |
        v
Server services
  range selection -> replay facts -> analytics inputs -> small normalized/cache outputs
        |
        v
Web
  charts, replay, event timeline, incident view, coaching UI
```

Do not copy the binary registry into JavaScript. Do not persist a second giant decoded JSON archive.
Small derived objects such as a lap summary, Race Story index, fastest lap, or cached chart series may
be normalized separately, but they never replace or rewrite the original compact archive.

## 2. Current read API

Endpoint:

```text
GET /ams2/api.php?route=v1/telemetry/chunks
Authorization: Bearer <installation token with telemetry:write>
```

The current route requires installation authentication. A browser must not receive or embed an
installation bearer token. Portal/Web work should call the store/decoder server-side or introduce an
appropriately authorized user-facing endpoint.

### Index-only reads

```text
GET ...&sessionId=<id>
GET ...&sessionId=<id>&streamType=PARTICIPANT_REPLAY
GET ...&sessionId=<id>&startElapsedMs=45000&endElapsedMs=50000
GET ...&sessionId=<id>&startLap=4&endLap=6&limit=64
```

Supported optional filters are `witnessId`, `attemptId`, `streamType`, `visibility`, elapsed range,
lap range, and `limit`. Chunk range matching uses overlap semantics. Default and maximum page size are
64. Index responses do not decode samples and include:

```json
{
  "rangeMode": "OVERLAP",
  "capabilitySource": "DURABLE_CHUNK_INDEX",
  "streamCapabilities": {
    "raceStory": true,
    "replay": true,
    "driverTelemetry": false,
    "incidentHighRate": true
  },
  "count": 3,
  "chunks": []
}
```

Capabilities are derived from durable index rows, not from client-declared feature flags. Visibility
filtering must happen before capability reporting so another installation cannot infer private data.

### One-chunk detail and decode

```text
GET ...&chunkId=<id>
GET ...&chunkId=<id>&decode=1
```

A compact detail includes `archiveEncoding: "compact-gzip"`, canonical
`payloadCompactGzipBase64`, and byte-exact decoded `payloadBinaryBase64`. With `decode=1`, the server
also returns a `decoded` object:

```json
{
  "chunk": {
    "chunkId": "...",
    "archiveEncoding": "compact-gzip",
    "payloadCompactGzipBase64": "...",
    "payloadBinaryBase64": "...",
    "decoded": {
      "protocolName": "AMS2_COMPACT_TELEMETRY",
      "protocolVersion": 1,
      "flags": 7,
      "sessionLocalId": 1,
      "attemptLocalId": 1,
      "chunkSequence": 12,
      "baseElapsedMs": 600000,
      "cadenceMs": 50,
      "sampleCount": 6000,
      "schemaId": 48,
      "schemaName": "DRIVER_FAST_V1",
      "streamType": "DRIVER_TELEMETRY",
      "visibility": "PRIVATE_DRIVER_ANALYTICS",
      "participantDictionaryCount": 0,
      "stringDictionaryCount": 0,
      "participants": [],
      "strings": [],
      "rows": [
        {
          "elapsedMs": 600000,
          "throttle": 0.8,
          "brake": 0,
          "steering": -0.02,
          "speedMetersPerSecond": 61.23,
          "lapDistanceMeters": 1200.45,
          "longitudinalAccelerationMetersPerSecondSquared": 0.1,
          "lateralAccelerationMetersPerSecondSquared": 2.4
        }
      ],
      "lapStart": null,
      "lapEnd": null,
      "startElapsedMs": 600000,
      "endElapsedMs": 899950,
      "bodySha256": "...",
      "bodyBytes": 12345
    }
  }
}
```

The shape is illustrative; a private Driver Fast chunk cannot currently be uploaded because authority
fails closed. Public schemas are the presently ingestible compact data.

For schemas with semantic references, `strings` entries have this server-decoded shape:

```json
{
  "dictionaryId": 1,
  "dictionaryName": "EVENT_TYPE",
  "valueRef": 0,
  "value": "LAP_COMPLETED"
}
```

The fixed dictionary IDs are Event Type `1`, Event ID `2`, Fact Code `3`, Incident Candidate `4`,
Incident Trigger Code `5`, Session Text `6`, and Driver Text `7`. Resolve a numeric `*Ref` only within
its declared dictionary. Do not infer a label from a numeric ref alone.

### Indexed range decode

```text
GET ...&sessionId=<id>&startElapsedMs=45000&endElapsedMs=50000&decode=1
GET ...&sessionId=<id>&startLap=4&endLap=6&decode=1
```

The server first selects overlapping chunks from the index and validates every compact column. For a
bounded elapsed/lap request it computes selected indexes directly for fixed-cadence columns and walks
RLE runs without materializing unselected rows, then returns only those rows in `decodedChunks`. This
is chunk-indexed partial decode, not a random byte seek inside one compressed frame. A real
`13,226 × 35` Replay frame therefore remains fully validated while a `2..1,000 ms` request returns
exactly 245 selected rows.

The PHP decoder accepts the protocol's streaming validation bound of up to 1,000,000 samples per
block. Materialized response output remains capped at 250,000 cells, with 8,192 combined
participant/typed-string entries per frame and aggregate response. Web services should request narrow
ranges and paginate chunk metadata rather than asking for every stream at once.

## 3. Feature-to-schema routing

| Web feature | Compact source | Current decoded facts |
|---|---|---|
| Race Story / lap table / flag timeline | `0x0010 RACE_EVENT_V1` + typed dictionaries 1–3 | event type/ID/fact labels, participant, lap/sector/distance, state, flag, penalty, result, lap time |
| Position chart / lap progress | `0x0020 PARTICIPANT_REPLAY_V1` | participant, lap, distance, race position, pit/race state |
| 2D replay | `0x0020` + `0x0021 TRACK_GEOMETRY_V1` | sparse world keyframes, heading/speed, track centerline, browser interpolation |
| Participant labels | frame dictionary + `0x0020` refs | display name, vehicle, class |
| Incident view/animation | `0x0040 INCIDENT_V1` + typed dictionaries 4–5 | candidate/trigger labels, related participants, -3..+3 s raw state/motion/collision facts |
| Session/integrity display | `0x0001`, `0x0002`, `0x0050`, `0x0051` plus compatibility metadata | compact numeric session/integrity facts; low-rate textual/capability metadata remains legacy JSON |
| Driver input charts | `0x0030 DRIVER_FAST_V1` | throttle, brake, steering, speed, distance, longitudinal/lateral acceleration |
| Driving line / RPM | `0x0031 DRIVER_MOTION_V1` | world position, heading, RPM |
| Fuel/damage/conditions | `0x0032 DRIVER_SLOW_V1` | fuel, engine/aero damage, track temperature |
| Future raw driver analytics | `0x0033 DRIVER_CHANGE_V1` + private dictionary 7 | catalog ordinal/value and typed driver strings; resolve catalog meaning server-side |

The current generic read endpoint returns decoded chunks; it does not yet expose specialized Replay,
Race Story, Incident, or Coach DTOs. Build those as server-side projections so Web code does not join
schemas, dictionaries, and attempt state independently on every page. Final-product-v1 contains typed
Story and Incident dictionaries, and its C# analyzer resolves Event Type, Event ID, and Fact Code with
`storyExact=true` for `45/45` events. It stores Replay progress, world, and sparse-extension facts in
one `PARTICIPANT_REPLAY_V1` artifact family, merging facts with the same timestamp and participant.
The fresh local PHP storage/decoder replay accepted and persisted all `78/78` official frames,
preserved `480` merged Replay rows, and re-inflated byte-exact A2CT. Specialized Web DTOs and
authorized MariaDB/Cafe24 deployment remain unproven.

## 4. Recommended canonical service outputs

These are handoff targets, not existing endpoints:

| Service | Suggested input | Suggested output |
|---|---|---|
| Replay Range | session/attempt + elapsed range | participant positions, lap progress, interpolatable world keyframes, flags/pit state |
| Race Story | session/attempt + optional lap range | exact ordered events with resolved participant/event labels |
| Incident | candidate ref + ±time window | raw related-participant frames and trigger facts; never blame |
| Driver Lap Telemetry | authorized driver + lap | controls, speed, line, RPM, slow conditions |
| Coaching Input | two authorized laps | aligned numeric sources and quality metadata, not client-written advice |
| Integrity | attempt | loss categories, durable ACK, final completeness |

Every output should include protocol/schema version, attempt identity, source chunk hashes, capture
completeness, and applied range. That permits deterministic cache invalidation and later V2 support.

## 5. Upload contract for backend/client integration

Current local candidate upload:

```text
POST /ams2/api.php?route=v1/telemetry/chunks
Content-Type: application/vnd.ams2.compact-telemetry-v1
Content-Encoding: gzip
Authorization: Bearer <installation token with telemetry:write>
Idempotency-Key: <stable 8..128 chars>
```

Required source headers:

- `X-AMS2-Chunk-Id`
- `X-AMS2-Session-Id`
- `X-AMS2-Session-Fingerprint`
- `X-AMS2-Witness-Id`
- `X-AMS2-Attempt-Id`
- `X-AMS2-Attempt-Number`
- `X-AMS2-Captured-At-Start`
- `X-AMS2-Captured-At-End`

Optional bounded headers: `X-AMS2-Client-Version`, `X-AMS2-Game-Build`, and a visibility value that
must agree with the immutable schema. Gzip is recommended; `identity` is also accepted.
`X-AMS2-Payload-SHA256` covers the decoded A2CT bytes. For gzip transport,
`X-AMS2-Compressed-SHA256` covers the received gzip wire bytes. The `A2CT` header separately contains
SHA-256 of its dictionary/presence/payload body. These three hash scopes must not be interchanged.

## 6. Storage contract

Compact source bytes are not stored as a MariaDB BLOB and are not expanded into sample rows. The local
candidate:

1. inflates gzip when present and validates the exact `A2CT` frame;
2. creates a lossless canonical gzip and writes it atomically under the private compact archive root;
3. stores a server-generated relative key plus searchable metadata in MariaDB;
4. re-inflates and validates the content hash again on retrieval;
5. decodes only when `decode=1` or a future server service requests facts.

The storage key form is:

```text
<installation-id>/<32-hex session hash>/<64-hex content hash>.a2ct.gz
```

Schema 15 adds protocol version, schema ID, flags, local IDs, participant/string dictionary counts,
body/content/archive/transport hashes, archive format/encoding, and storage key to the existing chunk
index. The legacy non-null gzip BLOB column receives an empty compatibility value for compact rows.
There is no row-per-sample table. Canonical gzip bytes may differ from the received gzip while
inflating to byte-identical A2CT.

The official client proof measures `2,781,797 B` logical A2CT and `465,279 B` gzip/wire across 78
frames. The subsequent local PHP replay persisted `465,449 B` canonical gzip; `77/78` received gzip
files were already byte-identical and one valid recompression added `170 B`. Its deterministic
application-index JSONL is `63,422 B`, making the exact local archive-plus-JSONL test total
`528,871 B`. The JSONL already includes `8,346 B` of storage-key text and is not MariaDB/InnoDB.
The client `19,968 B` DB estimate gives a `485,417 B` canonical-plus-estimate model only. Actual
Cafe24 filesystem allocation and MariaDB physical allocation remain `NOT MEASURED`.

The trusted replay persisted 30 public and 48 private frames to test the storage contract. That does
not weaken normal authorization: schemas `0x0030..0x0033` must still receive
`403 COMPACT_PRIVATE_UPLOAD_DENIED` without authoritative owner proof.

The candidate writes the content-addressed file before the final chunk-row insert. A DB/storage
failure after that write can leave an unreferenced hash-addressed file; immediate deletion is unsafe
because a concurrent transaction may reference the same content. A grace-period reconciliation job
is still required before production operations.

## 7. Privacy and authorization

Schemas `0x0030..0x0033` are private driver analytics. SHM v14 cannot attest that the viewed driver is
the installation owner, so the current server always returns:

```text
403 COMPACT_PRIVATE_UPLOAD_DENIED
```

Those bytes must remain client-local as `LOCAL_PENDING_OWNER`. Web must not show “no telemetry exists”
as if it were a capture failure; use a distinct “private authority unavailable / local only” state.
Do not use a nickname match, viewed slot, steering activity, or bearer token as owner proof.

Public replay is an access classification, not automatic publication approval. Existing publication
state and session/league workflow remain independent of compact ingest.

## 8. Legacy and version compatibility

- Legacy P023 chunks retain `archiveEncoding: "gzip"` and `payloadGzipBase64`.
- Legacy `decode=1` returns the decoded P023 payload under `legacySchema`/`payload`.
- Compact details use `payloadCompactGzipBase64`, `payloadBinaryBase64`, typed `strings`, and
  registry-backed named rows.
- A future V2 must add a decoder; never reinterpret a V1 ordinal with V2 meaning.
- Unknown compact versions/schema IDs are rejected rather than guessed.
- Result-session normalization and GENERAL/LEAGUE classification remain independent of telemetry
  archive format.

## 9. Error handling relevant to Web

| Condition | Expected behavior |
|---|---|
| invalid/corrupt compact frame | 400 with stable `COMPACT_*` code, no valid chunk index |
| private compact upload | 403 `COMPACT_PRIVATE_UPLOAD_DENIED`, not archived/quarantined |
| idempotency reused with different bytes/routing | 409 `IDEMPOTENCY_CONFLICT` |
| chunk identity/sequence conflict | 409 `CHUNK_CONFLICT` |
| inaccessible private/missing detail | 404 |
| stored file missing/hash mismatch | server 500; never return unverified rows |
| decoder cell limit exceeded | compact resource-limit error; caller narrows range/frame size |

Web UI should surface archive availability and quality without exposing filesystem keys, raw bearer
tokens, internal exception text, or private existence across owners.

## 10. Handoff acceptance before Web implementation

Do not treat the local contract as production-ready until all of these have evidence:

- a real shipping-client run confirms high-rate A2CT-only output while documenting low-rate metadata
  JSON compatibility;
- gzip/identity HTTP transport, canonical archive integrity, and actual Cafe24/MariaDB
  wire/storage/index accounting are measured in staging;
- local PHP fixture and legacy regression suites continue to pass after any contract change (current
  local evidence: `235/235` PHP assertions, lint `87/87`, official replay `78/78`, REAL v6 and v9
  replay PASS);
- Cafe24 staging schema 15 migration/preflight pass;
- public compact upload, duplicate retry, byte-exact retrieval, `decode=1`, elapsed/lap range, and
  legacy decode pass on staging;
- terminal `0x50 → 0x51` ordering, retry idempotency, and a durable server receipt/terminal upload
  ledger are proven across the real network boundary;
- private upload deny is confirmed on staging;
- real AMS2 Race Story/replay/incident data can drive the specialized server projections;
- driver analytics remains unavailable server-side until authoritative ownership exists.

Current release status: **HOLD**.
