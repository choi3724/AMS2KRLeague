# AMS2 Compact Telemetry Protocol V1

Status: **local candidate / not released**

Protocol name: `AMS2_COMPACT_TELEMETRY`

Protocol version: `1`
Client base version: `0.2.2` (unchanged)

This document specifies the byte-level `A2CT` frame implemented by
`src/AMS2LeagueClient.Core/CompactTelemetry`. It does not authorize a version bump, Git operation,
GitHub release, or Cafe24 deployment.

Evidence and implementation:

- [C# protocol constants](../src/AMS2LeagueClient.Core/CompactTelemetry/CompactTelemetryProtocol.cs)
- [C# codec](../src/AMS2LeagueClient.Core/CompactTelemetry/CompactTelemetryCodec.cs)
- [immutable C# registry](../src/AMS2LeagueClient.Core/CompactTelemetry/CompactTelemetrySchema.cs)
- [final-product-v1 machine report](../work/p024/compact-proof-final-product-v1/p024-machine-report.json)
- [typed-string codec test](../tests/AMS2LeagueActivity.Tests/CompactTelemetryCodecTests.cs)
- [local PHP decoder](../../AMS2League/server/cafe24_telemetry014/app/CompactTelemetryProtocol.php)
- [local Server compact report](../../AMS2League/server/cafe24_telemetry014/docs/P024_SERVER_COMPACT_PROTOCOL_REPORT.md)

## 1. Layering

An `A2CT` frame is an uncompressed logical binary frame:

```text
88-byte fixed header
+ dictionary section (participants, then typed strings)
+ presence section
+ ordinal column payload
```

The final synthetic proof persists each frame as `<sequence>-<schema>.a2ct.gz` using .NET gzip
`CompressionLevel.SmallestSize`. The proof totals are:

- logical `A2CT` bytes: `2,781,797 B`
- persisted gzip / benchmark wire bytes: `465,279 B`
- frames: `78`

The gzip wrapper is not part of the `A2CT` header or body. A decoder must remove that wrapper before
checking the `A2CT` magic. The local PHP candidate accepts `Content-Encoding: gzip` (recommended) or
`identity`, validates the decoded A2CT bytes, and stores a lossless canonical `.a2ct.gz`. Transport,
decoded-content, and canonical-archive SHA-256 values are distinct.

### Typed-string proof coverage

The final-product-v1 candidate was regenerated with the current header and typed dictionaries. Its first Story
frame has eight typed entries and its Incident frame has two. `storyExact=true` compares Event Type,
Event ID, and Fact Code strings as well as numeric fields; the codec test also round-trips typed values
without field names. The PHP candidate implements the same layout. Its official replay accepted and
persisted all `78/78` frames, preserved `480` merged Replay rows, and re-inflated byte-exact A2CT for
every frame. That is local cross-language evidence only: private frames were admitted through a
trusted storage-test path, not the public ingest authorization path, and authorized MariaDB/Cafe24
staging remains a separate gate.

## 2. Scalar conventions

- Endianness: little-endian for every fixed-width integer.
- Integer widths: unsigned unless the table explicitly says signed.
- VarUInt: unsigned LEB128-style, seven payload bits per byte, continuation bit `0x80`, maximum ten
  bytes for `UInt64`.
- ZigZag: `signed -> unsigned` mapping, then VarUInt.
- Strings: strict UTF-8, no terminator, preceded by VarUInt byte length.
- Floating values never occur directly on the wire. A schema quantizes each present value to an
  integer.
- Null is represented only by the presence section. Numeric zero is a value and is never a null
  sentinel.

## 3. Fixed header

The header is exactly `88` bytes. Offsets are zero-based.

| Offset | Size | Type | Name | Contract |
|---:|---:|---|---|---|
| 0 | 4 | bytes | `magic` | ASCII `A2CT` (`0x41 0x32 0x43 0x54`) |
| 4 | 1 | `u8` | `protocolVersion` | `1` |
| 5 | 1 | `u8` | `headerBytes` | `88` |
| 6 | 2 | `u16` | `streamSchemaId` | Immutable ID from the V1 registry |
| 8 | 2 | `u16` | `flags` | Exactly `0x0007` or `0x000B` |
| 10 | 2 | `u16` | `stringDictionaryCount` | Typed string entries in the dictionary section |
| 12 | 4 | `u32` | `sessionLocalId` | Session-local compact identifier |
| 16 | 4 | `u32` | `attemptLocalId` | Attempt-local compact identifier |
| 20 | 4 | `u32` | `chunkSequence` | Monotonic attempt chunk sequence |
| 24 | 8 | `i64` | `baseElapsedMs` | Session elapsed-time base in milliseconds |
| 32 | 4 | `u32` | `cadenceMs` | Fixed interval; zero for irregular time |
| 36 | 4 | `u32` | `sampleCount` | Number of logical rows |
| 40 | 2 | `u16` | `fieldCount` | Must equal the registry field count |
| 42 | 2 | `u16` | `dictionaryCount` | Participant dictionary entries |
| 44 | 4 | `u32` | `dictionaryBytes` | Exact dictionary section length |
| 48 | 4 | `u32` | `presenceBytes` | Exact presence section length |
| 52 | 4 | `u32` | `payloadBytes` | Exact column-payload length |
| 56 | 32 | bytes | `bodySha256` | SHA-256 of dictionary + presence + payload |

No trailing frame bytes are permitted. The complete byte count must be:

```text
88 + dictionaryBytes + presenceBytes + payloadBytes
```

## 4. Flags and time reconstruction

`0x0003` is the mandatory V1 common flag set. Exactly one timestamp-mode bit is then present:

| Flags | Timestamp mode | Rule |
|---:|---|---|
| `0x0007` | fixed cadence | `elapsed[i] = baseElapsedMs + i * cadenceMs` |
| `0x000B` | irregular delta time | `cadenceMs = 0`; payload begins with `sampleCount` unsigned VarUInt deltas |

For irregular time, the first delta is relative to `baseElapsedMs`; every later delta is relative to
the preceding reconstructed timestamp. Deltas are non-negative, so equal timestamps are valid (for
example, several participants observed at the same capture instant). Reconstructed time must not
overflow `Int64`.

## 5. Dictionary section

The one section counted by `dictionaryBytes` contains all participant entries first, followed by all
typed string entries. `dictionaryCount` at offset 42 counts only participant entries;
`stringDictionaryCount` at offset 10 counts only typed string entries.

### Participant entries

Entries are ordered and their references must be contiguous from zero. Each entry is:

```text
VarUInt participantRef
VarUInt displayNameUtf8Bytes + displayName bytes
VarUInt vehicleNameUtf8Bytes + vehicleName bytes
VarUInt classNameUtf8Bytes + className bytes
```

Each string is limited to `4,096` UTF-8 bytes. The frame dictionary is optional; a schema may contain
zero entries. High-rate samples refer to dictionary ordinals and never repeat driver, vehicle, or
class strings.

### Typed string entries

Each typed string entry is:

```text
VarUInt dictionaryId
VarUInt valueRef
VarUInt valueUtf8Bytes + value bytes
```

Entries are ordered by numeric `dictionaryId`. Within each dictionary, `valueRef` is contiguous from
zero. Each value is strict UTF-8 and limited to `4,096` encoded bytes.

| ID | Dictionary | Referenced by / purpose |
|---:|---|---|
| 1 | `EVENT_TYPE` | `RACE_EVENT_V1.eventTypeRef` |
| 2 | `EVENT_ID` | `RACE_EVENT_V1.eventIdRef` |
| 3 | `FACT_CODE` | `RACE_EVENT_V1.factCodeRef` |
| 4 | `INCIDENT_CANDIDATE` | `INCIDENT_V1.candidateRef` |
| 5 | `INCIDENT_TRIGGER_CODE` | `INCIDENT_V1.triggerCodeRef` |
| 6 | `SESSION_TEXT` | reserved V1 category for session metadata text |
| 7 | `DRIVER_TEXT` | private driver text such as tyre-compound values |

The shipping compact adapter currently emits IDs 1–5 for Race Story/Incident and ID 7 for driver
text. The source-coverage matrix maps six current string sources to ID 6, but the shipping compact
runtime does not yet route those refs into session artifacts.

## 6. Presence and null contract

The presence section begins with two bits per immutable field ordinal. Bits are packed least
significant first.

| Two-bit state | Meaning |
|---:|---|
| `00` | all samples null; column carries zero values |
| `01` | all samples present |
| `10` | mixed; one sample bitmap follows |
| `11` | reserved and rejected |

State bytes occupy `ceil(fieldCount * 2 / 8)` bytes. For every mixed field, in ordinal order, append
`ceil(sampleCount / 8)` bitmap bytes. A set bit means present. Padding bits must be zero. A mixed
bitmap must contain at least one null and at least one present sample; canonical all-null/all-present
columns must use `00`/`01` instead.

Only present values enter the corresponding encoded column. During decode, values are placed back in
sample order according to the bitmap. This makes `null`, `0`, negative values, and raw enum zero
unambiguous.

## 7. Column encodings

Columns occur strictly in schema ordinal order. No field name or per-row schema appears in the binary
payload.

| Registry encoding | Byte rule |
|---|---|
| `FixedUnsigned` | 1, 2, 4, or 8 little-endian bytes per present value |
| `FixedSigned` | 1, 2, 4, or 8 little-endian two's-complement bytes per present value |
| `VarUInt` | unsigned VarUInt per present value |
| `ZigZag` | signed ZigZag VarUInt per present value |
| `DeltaZigZag` | first quantized value as ZigZag; subsequent differences as ZigZag |
| `RleUnsigned` | repeated `(VarUInt runLength, VarUInt value)` pairs |
| `RleZigZag` | repeated `(VarUInt runLength, ZigZag value)` pairs |

RLE runs must be non-zero and may not exceed the remaining number of present values. Delta arithmetic,
quantization, fixed-width conversion, and timestamp reconstruction are checked for overflow.

## 8. Quantization

For a present source value `x`, the C# encoder computes:

```text
q = round_away_from_zero((x - offset) / scale)
decoded = offset + q * scale
```

V1 currently uses offset `0` for every field. The schema fixes encoding, width, scale, and quantized
range. A value outside the range, or a NaN/infinity, is rejected. For ordinary rounding the declared
maximum quantization error is `scale / 2`; measured field-specific errors are in
[COMPACT_FIDELITY_REPORT.md](COMPACT_FIDELITY_REPORT.md).

## 9. Integrity

The 32 bytes at header offset `56` are SHA-256 of the complete body only:

```text
dictionary section || presence section || column payload
```

The decoder verifies this hash in constant time before interpreting the body. The upload boundary may
also carry `X-AMS2-Payload-SHA256` over the decoded complete A2CT frame and, for gzip,
`X-AMS2-Compressed-SHA256` over received transport bytes. Both are distinct from the body-only hash
in the `A2CT` header.

## 10. Limits and defensive decode

The C# wire limits are:

- maximum samples per block: `1,000,000`
- maximum participant dictionary entries: `4,096`
- maximum typed string dictionary entries: `65,535`
- maximum encoded bytes per dictionary string: `4,096`
- maximum body bytes: `64 MiB`

The PHP decoder additionally protects shared-hosting memory with:

- streaming validation up to the protocol limit of `1,000,000` samples per block without requiring
  all cells to be materialized
- on-demand materialized response decode: `250,000` cells
- combined participant + typed-string entries per frame/request decode budget: `8,192`

A decoder rejects at least: wrong magic/version/header size, unknown schema, unsupported flags,
field-count mismatch, invalid cadence, invalid section length, truncation, trailing bytes, hash
mismatch, invalid UTF-8, non-contiguous participant references, unknown or descending string
dictionary IDs, non-contiguous per-dictionary string references, malformed/non-canonical presence,
VarUInt/RLE/delta overflow, and out-of-range values.

## 11. Privacy classification

Privacy is fixed by schema, not chosen by a request header:

- `0x0030` through `0x0033`: `PRIVATE_DRIVER_ANALYTICS`
- all other V1 schemas: `PUBLIC_REPLAY`

AMS2 SHM v14 does not attest local driver ownership. The local policy therefore retains private
compact data as `LOCAL_PENDING_OWNER`; the PHP candidate rejects its upload with
`403 COMPACT_PRIVATE_UPLOAD_DENIED`. A bearer token, nickname match, viewed participant, or input
activity is not owner authority.

## 12. Compatibility and evolution

- A V1 schema ID fixes field ordinal, meaning, encoding, width, scale, and range permanently.
- Do not insert a field into an existing schema or change an ordinal's semantics.
- Additive evolution requires a new schema ID/version and a retained historical decoder.
- Unknown versions and schema IDs fail closed.
- The server candidate continues to recognize legacy P023 JSON/gzip chunks independently of compact
  ingest. A compact decoder is never used to reinterpret a legacy payload.
- The shipping runtime selects compact A2CT for high-rate artifacts, while low-rate session metadata
  remains a legacy JSON/gzip compatibility record until every metadata string has a typed V1 home.
- On acknowledged attempt close, the shipping runtime commits `0x0050 LOSS_LEDGER_V1` after regular
  data and then commits `0x0051 ATTEMPT_FINALIZE_V1` as the last reserved sequence. `0x0051` is the
  authoritative terminal ACK; loss-ledger conflict or integrity write failure prevents it and forces
  the diagnostic ledger to `PARTIAL`. Actual AMS2 v9 and the PHP decoder verify this order and shape.
- Debug JSON and fixture conversion are allowed, but high-rate JSON rows are not the compact source of
  truth.

The complete ordinal registry is [COMPACT_SCHEMA_REGISTRY.md](COMPACT_SCHEMA_REGISTRY.md). Raw-field
lineage and unresolved runtime mappings are tracked in
[P023_FIELD_TO_COMPACT_V1_MATRIX.md](P023_FIELD_TO_COMPACT_V1_MATRIX.md).
