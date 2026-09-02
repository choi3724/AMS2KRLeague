# Local Telemetry Chunk Storage Design

작성 기준: 2026-09-02 KST
route target: `POST v1/telemetry/chunks`

## 1. storage goals

- local durable first
- 30 s bounded chunks
- numeric-array JSON readability
- gzip compression
- uncompressed/compressed SHA-256
- atomic same-volume rename
- crash 후 pending upload reconstruction
- hot path disk/network block 없음
- immutable session/witness/attempt/chunk identity

이 계층 자체는 uploader가 아니라 durable archive/queue handoff이며 HTTP request를 보내지 않는다. Runtime의 인접 `TelemetryChunkUploadWorker`가 이 sidecar queue를 소비하고 `Cafe24ActivityUploadTransport`가 HTTPS request를 보낸다. capture hot path와 network I/O의 분리는 유지된다.

## 2. directory layout

raw identity를 directory name으로 사용하지 않는다. fingerprint+witness+attempt SHA-256 prefix로 session directory를 만든다.

```text
<archive-root>/
  sessions/<32-hex-session-key>/
    chunks/
      session_metadata/00000000.json.gz
      session_metadata/00000000.upload.json
      race_story/00000000.json.gz
      participant_replay/00000000.json.gz
      driver_telemetry/00000000.json.gz
      incident_trace/00000000.json.gz
    conflicts/
    recovery/
```

`.json.gz`가 gzip envelope고 `.upload.json`이 pending metadata다. conflict payload와 interrupted temp는 삭제하지 않고 별도 directory에 보존한다.

## 3. chunk identity/range

`chunkId`는 다음 stable inputs의 SHA-256에서 만든다.

```text
sessionFingerprint + witnessId + attemptId + streamType + chunkIndex
```

같은 key의 다른 content는 overwrite하지 않고 conflict로 보존한다.

`chunkIndex = floor(sessionElapsedMs / 30000)`이다. envelope에는 actual row의 `startElapsedMs/endElapsedMs`, optional `startLap/endLap`이 있어 partial range loading이 가능하다. incident chunk는 candidate trigger bucket이지만 actual range가 pre/post로 경계를 넘을 수 있다.

## 4. compact JSON

envelope schema는 `ams2-telemetry-chunk-v1`이다. high-rate streams은:

```json
"data": {
  "fields": ["sessionElapsedMs", "participantRef", "worldX", "worldZ"],
  "dictionaries": {"names": ["Driver A"]},
  "rows": [[1000, 1, 25.4, -19.2]]
}
```

field order가 row meaning을 정의한다. 문자열은 dictionary ref로 반복을 줄인다. custom opaque binary protocol은 사용하지 않는다. Tier 1은 sparse structured `records`를 사용한다.

## 5. serialization/hash/compression order

background worker:

1. canonical compact envelope JSON UTF-8 serialize
2. `payloadSha256 = SHA256(uncompressed JSON)`
3. gzip JSON (`CompressionLevel.SmallestSize`)
4. `compressedSha256 = SHA256(gzip bytes)`
5. chunk atomic write/rename
6. pending metadata atomic write/rename

hash를 envelope 내부에 넣으면 self-reference가 생기므로 두 hash는 sidecar에 둔다. Server는 content-encoding 해제 전 compressed hash, 해제 후 payload hash를 각각 검증할 수 있다.

local safety limits:

- compressed chunk: 64 MiB
- decompressed JSON: 256 MiB

기본 30 s bounds는 stream capability metadata까지 포함해 재생성한 synthetic 60분/32대 fixture에서 실측했다. 284 chunks의 uncompressed JSON은 376,215,655 B, gzip은 161,390,596 B다. 최대 32대 Driver chunk는 `1,792,585 B raw / 779,905 B gzip`, Replay chunk는 `1,374,379 B raw / 588,368 B gzip`이었다. 별도 64대 limit probe의 Replay 최대도 `2,725,970 B raw / 1,174,696 B gzip`로 Server의 `8 MiB decoded / 2 MiB gzip` 제한 이하였다. 이는 실제 AMS2 entropy/운영 overhead가 아니라 고정 fixture size budget이다.

## 6. atomic write

각 file write:

```text
target.tmp-<guid>
  -> CreateNew + FileShare.None + WriteThrough
  -> write all bytes
  -> Flush(true)
  -> File.Move(temp, final) on same volume
```

chunk를 먼저 durable하게 만든 뒤 sidecar를 쓴다. crash window별 결과:

| crash point | 재실행 상태 |
|---|---|
| temp write 전/중 | `.tmp-*`를 `recovery/`에 보존; final 아님 |
| chunk rename 후 metadata 전 | valid chunk parse/hash 후 PENDING sidecar 재생성 |
| metadata rename 후 | 정상 pending scan |
| duplicate same payload | `DUPLICATE`, 기존 metadata/status 유지 |
| same stream/index different payload | `CONFLICT_QUARANTINED`, overwrite 없음 |

recovery는 corrupt gzip/JSON/hash mismatch를 valid로 가장하지 않고 issue를 반환한다.

## 7. pending upload metadata

sidecar schema: `ams2-telemetry-upload-metadata-v1`.

최소:

- endpoint `v1/telemetry/chunks`
- chunk ID/type/visibility
- session/fingerprint/witness/attempt/index
- elapsed/lap range
- relative chunk path
- content type `application/json`
- content encoding `gzip`
- payload/compressed SHA-256와 byte sizes
- quality object
- `PENDING` status, attempt count/timestamps/next attempt/error

`ScanPending()`은 `PENDING`/`FAILED_RETRYABLE`이며 chunk file이 존재하는 item만 반환한다. bearer/API secret은 file에 저장하지 않는다. Runtime uploader integration은 이 item의 gzip/hash를 다시 검증하고 Bearer/compatibility auth, idempotency와 hash headers를 붙이며 `SENT`, retryable, conflict, quarantined 상태를 원자적으로 갱신한다.

Session Metadata의 `raceStory`, `replay`, `driverTelemetry`, `incidentHighRate`
boolean은 attempt 내 Runtime stream 관측 사실이지 저장 성공 marker가 아니다.
따라서 true metadata가 있어도 후속 atomic write 또는 upload이 실패할 수 있다.
Server/Web은 해당 stream의 durable 존재 여부를 metadata boolean으로 추론하지 않고
Server raw chunk index로 판정한다. Schema 14 후보의 session index 응답은
`capabilitySource=DURABLE_CHUNK_INDEX`와 visibility-aware `streamCapabilities`를 제공한다.
false는 unsupported가 아니라 아직 관측되지 않았다는 뜻이다.

## 8. bounded memory and background behavior

hot path는 bounded channel `TryWrite`만 한다. active memory upper structure는:

- channel 최대 512 DTO(default)
- stream당 현재 30 s accumulator
- replay 최대 5×30×64 = 9,600 rows/chunk
- driver 최대 20×30 = 600 rows/chunk
- incident ring 약 20×10 = 200 frames, 각 최대 64 participant
- emitted incident 최대 4 concurrent × 8 related refs × bounded pre/post
- metadata/story record caps

다음 bucket 관측 시 이전 completed bucket을 serialize/commit하고 accumulator를 제거한다. HTTP는 이 worker에도 없다.

incident related set은 trigger가 지정한 refs를 우선하고 latest/prior ring context의 anchor world X/Z에서 50 m 이내 participant를 거리순 최대 4명 추가한다. 전체 cap 8은 유지한다. near 포함/far 제외는 unit fixture로 확인했지만 실제 multiplayer 사고는 pending이다.

이 구조 설명은 inner archive worker의 bound다. 그 앞의 outer Runtime batch queue drop과 worker exception은 현재 stream별 chunk `quality`/session `captureCompleteness`에 완전 전파되지 않는다. process counter 또는 orderly end marker만으로 모든 accepted work의 durable commit을 증명할 수 없으므로 end-to-end completeness는 release blocker다.

이 산술은 implementation bound이지 실제 CLR allocation/CPU/disk 성능 증거가 아니다. 60분/32-car, multiple witnesses, disk slow/failure stress는 별도 acceptance가 필요하다.

## 9. lifecycle rules

- session start: 공통 identity/clock/archive 생성
- attempt restart: current archive final flush/close, `NextAttempt`, 새 archive
- normal end: `FlushAsync` 후 `DisposeAsync`; 현재 outer batch/worker의 모든 실패가 quality에 귀속됐다는 보장은 없음
- crash: 최대 current incomplete 30 s memory buffer는 잃을 수 있음; 완료/renamed chunks는 복구
- offline: pending sidecars를 유지; network availability가 capture gate가 아님

`FlushAsync`는 attempt-end finalization 용도로 사용한다. final flush 뒤 같은 stream/time bucket을 다시 열면 immutable key conflict가 되므로 Runtime은 새 attempt/archive lifecycle을 사용해야 한다.

## 10. tests and remaining work

fixture/regression/evidence가 확인한 것:

- all 5 stream files/sidecars
- gzip magic/decode와 양쪽 SHA-256
- exact envelope/endpoint/enum/join keys
- 30 s replay split, 32 participants/4,800 rows per full chunk
- no temp residue after normal commit
- orphan sidecar rebuild와 interrupted temp preservation
- pending scan, duplicate/range/quality basics
- Runtime lifecycle/common witness identity와 final flush
- upload worker의 pending→sent/retry/conflict/quarantine transition
- 실제 gzip HTTP body, auth/idempotency/양쪽 SHA와 response integrity
- local Cafe24 schema-14 raw canonical gzip/index/range/privacy contract
- synthetic persisted-only historical reprocess와 60분/32대 size budget
- 실제 AMS2 short run의 10 atomic chunks/sidecars, clean stop, integrity failure 0

위 quality/recovery PASS는 inner archive가 수신한 작업에 한정된다. outer queue drop/worker failure accounting과 authoritative local-owner privacy는 별도 FAIL gate다.

### 10.1 실제 chunk local HTTP 저장/조회 증거

controlled2의 실제 private driver chunk `chunk-be01d7222795c8dad9815f06138f9a9239225d5c`를 `Cafe24ActivityUploadTransport`가 실제 loopback HTTP로 전송했다. Source는 JSON 406,478 B / gzip 114,932 B, payload SHA-256 `e9a4b6c79fe773e581964cb6a94929100e23c956c45a38edcb1eb06bf41f24c2`, compressed SHA-256 `c9a7c976c4387aad0522213451cce8e6abc52236d256582ddde68aca52ed54fd`다. PHP `Application`은 POST `201 STORED`를 반환했고 raw canonical gzip 115,850 B와 index 1 row를 저장했다. index GET과 detail GET은 모두 `200`; 반환 archive SHA-256 `32bd7f5e8d88709febbacdbdf3babc4b466f1eda6ffe44cc14f424a1f65856e6` 및 복원 JSON은 저장값/원본과 일치했다.

증거는 repository root (`outputs/AMS2KRLeague`) 기준 `../../work/local-http-e2e/evidence/`에 있다. 이 harness는 PHP 8.4 + serialized `InMemoryStore`이며 production client transport가 생성한 gzip/Bearer/compatibility auth/idempotency/hash headers를 그대로 사용한다. 실제 Cafe24/PDO/MariaDB/staging이나 TLS wire를 통과하지 않았으므로 운영 raw blob/index persistence 증거로 승격하지 않는다.

아직 `PENDING` 또는 운영 미실행:

- outer Runtime batch queue drop/worker failure의 chunk quality와 session completeness 전파
- authoritative local-owner/spectator signal 또는 fail-closed private capture policy
- disk full/permission/antivirus lock fault injection
- 실제 AMS2 60분/다수 차량 size/CPU/RAM/drop pressure
- real captured chunk의 staging `PdoStore`/MariaDB POST→GET→stored-only reprocess; local PHP 8.4 + `InMemoryStore` round trip만 PASS
- Cafe24 raw blob/index schema-14 production migration/deployment
- 운영 quota/backup/retention과 multi-witness duplicate policy

controlled real run은 `uploadConfigured=False`였으므로 10 sidecar가 PENDING인 것이 정상이다. 이는 uploader failure가 아니다. 전체 판정은 **YELLOW/HOLD**이며 Production Portal은 수정하거나 배포하지 않았다.
