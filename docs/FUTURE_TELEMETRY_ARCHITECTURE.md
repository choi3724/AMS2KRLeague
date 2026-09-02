# Future Telemetry Architecture

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`
범위: Client의 **Local Durable Telemetry Archive** 계층
현재 판정: **archive/HTTP contract/로컬 Server raw archive/synthetic reprocessing과 실제 core field short-run은 PASS, 그러나 private source authority 및 end-to-end completeness FAIL, production·real full-race E2E PENDING (overall YELLOW/HOLD)**

## 1. 책임 경계

```text
AMS2 Shared Memory v14
        |
stable TelemetrySnapshot                # 다른 계층의 parser/layout 책임
        |
Runtime fact/incident adapter           # ActivityCaptureRuntime에 연결됨
        |
LocalDurableTelemetryArchive.TryCapture*
        |  bounded channel / TryWrite only
        v
single background consumer
        |
30-second compact chunk -> JSON -> gzip -> SHA-256
        |
atomic local durable write -> pending upload metadata
        |
POST v1/telemetry/chunks                 # gzip/auth/hash/idempotency transport 연결
        |
Cafe24 raw archive / future analyzer     # local schema-14 candidate 구현; 운영 미배포
```

Client는 fact capture만 한다. Server/Web는 normalization, multi-witness merge, public classification, graph, replay, coaching, incident interpretation을 담당한다. Client는 사고 과실, 코너 손실, PB/Track Record, League 공식 여부를 계산하지 않는다.

30 Hz SHM raw memory를 Server로 streaming하지 않는다. 정보 성격에 따라 metadata/change event, 5 Hz replay, local-only 20 Hz driver telemetry, candidate-only 20 Hz incident burst로 보존한다.

## 2. 구현된 독립 계층

namespace는 `AMS2LeagueClient.Core.FutureTelemetry`다. 기존 `SharedMemoryLayout`, `SharedMemoryParser`, `TelemetrySnapshot`을 참조하거나 수정하지 않는다.

주요 공개 API:

- `TelemetryArchiveIdentityFactory.StartSession(...)`
- `TelemetryArchiveIdentityFactory.NextAttempt(...)`
- `TelemetrySessionClock.Capture(...)`
- `LocalDurableTelemetryArchive.TryCaptureSessionMetadata(...)`
- `LocalDurableTelemetryArchive.TryCaptureRaceStory(...)`
- `LocalDurableTelemetryArchive.TryCaptureFrame(...)`
- `LocalDurableTelemetryArchive.FlushAsync(...)`
- `LocalDurableTelemetryArchive.ScanPending()`
- `TelemetryChunkStore.Recover()` / `ReadChunk(...)` / `ScanPending()`
- `TelemetryChunkSerializer.Serialize(...)` / `Gzip(...)` / `Gunzip(...)` / `Sha256(...)`

입력 DTO는 parser 모델과 분리되어 있다. Runtime adapter가 현재/향후 snapshot 필드를 이 DTO로 복사한다. channel에 성공적으로 들어간 DTO와 participant collection은 worker가 소비할 때까지 caller가 변경하지 않는 immutable ownership 계약이다.

## 3. 공통 identity와 attempt

모든 stream은 다음 join key를 공유한다.

| key | 의미 |
|---|---|
| `sessionId` | session 시작 시 한 번 발급한 공통 `captureSessionId` |
| `sessionFingerprint` | 기존 session/witness와 연결하는 observed fingerprint |
| `witnessId` | 이 Client 관측자 identity |
| `attemptId` | Race restart마다 달라지는 attempt identity |
| `attemptNumber` | 1부터 증가하는 attempt ordinal |

`StartSession`은 stream별 random ID를 만들지 않고 공통 session/witness/attempt identity 하나를 만든다. `NextAttempt`는 session/fingerprint/witness를 유지하고 attempt ID와 number만 바꾼다. 따라서 Tier 1~5를 같은 Race/attempt로 join할 수 있다.

`SessionWitnessCaptureEngine`은 archive identity를 optional additive field로 공유한다. restart에서는 session/fingerprint/witness를 유지하고 attempt만 바꾸며, 두 계층 identity가 불일치하면 함께 닫아 혼합을 막는다. legacy payload는 해당 optional field가 없어도 계속 읽는다.

## 4. primary clock

`sessionElapsedMs`는 `TelemetrySessionClock`의 monotonic `Stopwatch` 기준이다. `mCurrentTime`은 current-lap time이므로 session clock으로 사용하지 않는다.

- primary timeline: `sessionElapsedMs`
- evidence clock: `capturedAtUtc`
- timed-session 보조값: `timedSessionDurationMs`, `eventTimeRemainingMs`
- 중간 참가: `joinedMidSession=true`, `sessionStartOffsetStatus=UNKNOWN` 또는 실제 근거가 있는 값

clock source는 chunk quality와 Tier 1에 `MONOTONIC_CAPTURE_CLOCK`으로 기록한다. collector는 frame/metadata/story별 elapsed 역행을 거부하고 drop count에 반영한다. timed session duration/remaining으로 과거 start offset을 추측하지 않는다.

## 5. 다섯 capture tier

| tier | streamType | 기본 cadence | 데이터 | visibility |
|---|---|---:|---|---|
| 1 | `SESSION_METADATA` | session start/end/change | build/version/track/settings/capability/participant dictionary | `PUBLIC_REPLAY` candidate |
| 2 | `RACE_STORY` | detected event | session/lap/position/pit/flag/penalty/finish fact | `PUBLIC_REPLAY` candidate |
| 3 | `PARTICIPANT_REPLAY` | 5 Hz | all participant world position + lap distance + heading/speed + raw state | `PUBLIC_REPLAY` candidate |
| 4 | `DRIVER_TELEMETRY` | 20 Hz | viewed/root driver candidate input/physics/tyre/damage; authoritative owner proof pending | `PRIVATE_DRIVER_ANALYTICS` |
| 5 | `INCIDENT_TRACE` | candidate -3 s~+3 s, 20 Hz | trigger-related + 50 m 이내 최대 4 nearby participant high-rate raw trace | `PUBLIC_REPLAY` candidate |

Tier 2 event persistence는 Overlay presentation/suppression과 독립이어야 한다. 예를 들어 UI가 yellow 중 PB popup을 숨겨도 detector fact가 발생했다면 story adapter는 이를 보낼 수 있다.

Tier 1 `fields` 내 `raceStory`, `replay`, `driverTelemetry`, `incidentHighRate`는
`TelemetryCapabilityValue` boolean으로 기록된다. 이 값은 attempt별 Runtime 관측 사실이며,
한 번 true가 되면 같은 attempt에서 false로 돌아가지 않는다. false는 아직 해당
stream input을 보지 못했다는 뜻이며 unsupported 판정이 아니다. 또한 true는
atomic commit/upload 성공을 보증하지 않는다. Server/Web의 durable availability 판정은
Server raw chunk index가 권위를 갖는다. Local schema 14 후보는 session index API에
`capabilitySource=DURABLE_CHUNK_INDEX`와 visibility-aware `streamCapabilities`를 추가해
Web이 관측 hint를 실제 archive 존재로 오해하지 않게 한다.

## 6. local-driver consistency gate와 미해결 authority

새 parser 모델의 `snapshot.ViewedVehicleTelemetry`는 root-scoped이고 local player라고 가정할 수 없다. 현재 Runtime adapter는 `DRIVER_TELEMETRY` 후보를 만들기 전에 다음 일관성 조건을 확인한다.

1. `ActivityLocalParticipantResolver`가 `InGamePlaying`에서 현재 viewed participant를 유효한 candidate로 돌려준다.
2. viewed/root vehicle source가 그 candidate participant와 일치한다.
3. DTO의 `LocalParticipantResolved=true`다.
4. `SourceParticipantRef == DriverRef`다.

collector는 3~4가 성립하지 않으면 private driver row를 만들지 않는다. 그러나 이 조건은 viewed/root 일관성만 증명한다. 공식 v14 header에는 `mViewedParticipantIndex` 외 authoritative local-owner/spectator/player-ID signal이 없고 game state/input activity도 authority가 아니다. 현재 resolver는 spectator가 원격 차량을 따라가는 `InGamePlaying` 상황을 배제하지 못한다. fixture와 실제 1인 Practice는 row 생성/shape를 확인했을 뿐 owner privacy를 증명하지 않는다. authoritative attestation 전 release-safe 기본은 `DRIVER_TELEMETRY` OFF/fail-closed다. 1인 session/Time Attack 허용도 heuristic일 뿐이다.

## 7. hot-path와 memory 경계

`TryCapture*`는 다음을 하지 않는다.

- file/directory write
- JSON serialization
- gzip
- hash
- HTTP/network/DB
- wait/await 또는 channel space 대기

bounded channel의 `TryWrite`만 사용하며 full이면 `false`를 반환하고 drop counter를 증가시킨다. single background consumer가 cadence selection, compact row accumulation, serialization과 durable write를 수행한다.

단, 전체 Runtime에는 이 inner archive channel 바깥의 batch queue/worker 경계도 있다. 현재 outer queue drop과 worker exception은 process diagnostics에만 남을 수 있고 stream별 chunk `quality.droppedInputMessages` 및 session `captureCompleteness`에 완전 전파되지 않는다. orderly end marker도 모든 outer work의 durable commit을 증명하지 않는다. 그러므로 현재 quality는 inner archive가 알고 있는 손실만 설명하며 end-to-end completeness gate는 FAIL이다.

기본 bounds:

- input messages: 512
- participants/frame: 64
- chunk: 30,000 ms
- metadata records/chunk: 64
- story events/chunk: 4,096
- incident ring: 10 s at 20 Hz
- related participants/burst: 8
- concurrent bursts: 4

각 accepted DTO 자체도 participant/string/field/tyre-array cap을 검증한다. 이는 finite bound이며 실제 gameplay CPU/RAM acceptance를 대신하지 않는다.

## 8. Server envelope

확정 logical transport contract:

- route: `POST v1/telemetry/chunks`
- schema: `ams2-telemetry-chunk-v1`
- encoding: current local archive는 `gzip`
- stream: `SESSION_METADATA | RACE_STORY | PARTICIPANT_REPLAY | DRIVER_TELEMETRY | INCIDENT_TRACE`
- visibility: `PUBLIC_REPLAY | PRIVATE_DRIVER_ANALYTICS`
- join: session/fingerprint/witness/attempt
- range: chunk index, elapsed range, optional lap range
- quality: rate, expected/actual/missing/dropped/input-drop/completeness

local archive 자체는 HTTP transport를 호출하지 않고 `.upload.json` sidecar를 `PENDING`으로 만든다. 인접한 `TelemetryChunkUploadWorker`와 `Cafe24ActivityUploadTransport`가 gzip/auth/idempotency/hash/response-integrity/retry state를 처리하도록 Runtime에 연결됐다. Server schema-14 local candidate는 canonical raw gzip과 searchable index를 분리 보존하고 partial range read를 제공한다. controlled real run은 의도적으로 network를 꺼 sidecar가 PENDING이며, production deployment/운영 historical reprocessing은 별도 gate다.

### 8.1 실제 chunk의 local HTTP round trip

controlled2의 실제 `DRIVER_TELEMETRY` chunk `chunk-be01d7222795c8dad9815f06138f9a9239225d5c` 하나를 production `Cafe24ActivityUploadTransport`로 local PHP 8.4 endpoint에 전송했다. Client gzip은 114,932 B, decoded JSON은 406,478 B이며 payload SHA-256은 `e9a4b6c79fe773e581964cb6a94929100e23c956c45a38edcb1eb06bf41f24c2`, wire gzip SHA-256은 `c9a7c976c4387aad0522213451cce8e6abc52236d256582ddde68aca52ed54fd`다. POST는 `201 STORED`, index와 detail GET은 각각 `200`이었고, Server canonical gzip 115,850 B의 SHA-256 `32bd7f5e8d88709febbacdbdf3babc4b466f1eda6ffe44cc14f424a1f65856e6`와 복원 JSON의 byte/hash가 모두 일치했다.

이는 mock handler나 in-process dispatch가 아니라 실제 loopback HTTP를 거친 증거다. 단, HTTPS URI를 test handler가 loopback HTTP로만 rewrite했고 Server store는 serialized `InMemoryStore`였다. 따라서 Cafe24 TLS/FastCGI, `PdoStore`, MariaDB migration/transaction 또는 staging/production을 검증한 결과가 아니다. 증거는 repository root (`outputs/AMS2KRLeague`) 기준 `../../work/local-http-e2e/evidence/client-http-proof.json`, `index-response.json`, `get-response.json`, `server-store-proof.json`, `request-audit.ndjson`에 있다.

## 9. compatibility

기존 v0.2.1/v0.2.2 activity/result/witness payload를 변경하지 않았다. 새 telemetry chunks는 additive stream이다. 새 stream metadata가 없는 과거 session은 capability를 추론하지 않고 `UNKNOWN`/missing으로 처리해야 하며, 명시적 `false`는 해당 attempt에서 아직 stream input이 관측되지 않았다는 뜻으로만 해석한다. 이 차이로 기존 result를 실패시키면 안 된다.

Directory build version `0.2.2`와 git/release metadata는 이 작업에서 변경하지 않았다.

## 10. 검증 상태

현재 regression/evidence가 확인한 범위:

- 5개 stream envelope/visibility/join key
- 30 s split, 5/20/20 Hz rate gates
- compact numeric rows/dictionaries
- gzip magic/decompression
- uncompressed payload SHA-256와 compressed SHA-256
- atomic chunk/sidecar write
- chunk는 있고 sidecar가 없는 crash window recovery
- interrupted `.tmp-*` 보존
- pending upload scan
- cadence gap, elapsed rollback, input drop metadata
- viewed/root consistency gate와 incident trigger-related + 50 m/최대 4 nearby participant filtering unit fixture. authoritative local ownership은 미검증
- current `TelemetrySnapshot` → fact/replay/private-driver/incident adapter와 common witness identity
- upload worker state transition과 실제 gzip HTTP request/auth/idempotency/hash/response integrity
- local Server schema 14 raw archive/index/privacy/range/quarantine 계약
- synthetic 60분/32대 284 chunks, 648,530 samples의 persisted-only full renderer
- 실제 AMS2 build 3398/SHM 14의 84.986초 capture: 10 chunks, core world/input/speed/RPM/gear/acceleration 변화

다음은 아직 `PENDING` 또는 실제 시나리오 미실행이다.

- authoritative local-owner/spectator 판정 또는 fail-closed private capture policy
- outer batch queue drop/worker failure의 stream quality와 session completeness 전파
- 실제 clean multi-lap/multi-car Race와 실제 incident burst
- 실제 60분/32대 Client CPU/RAM/disk/drop pressure; 현재 byte budget은 synthetic 실측
- real captured chunk의 local PHP 8.4 + `InMemoryStore` HTTP POST→index/detail GET은 PASS; staging `PdoStore`/MariaDB round trip과 Server-stored-only renderer는 미실행
- acceleration axis/unit, heading convention, tyre pressure/wear semantics의 정상 주행 확정
- disk full/permission/antivirus lock fault injection
- Cafe24 schema 14 production migration/deployment와 운영 backup/retention 계측

따라서 individual archive/transport/local/synthetic proof는 GREEN이지만 private source authority와 end-to-end completeness가 FAIL이므로 전체 Future Telemetry Phase와 release gate는 YELLOW/HOLD다. Production Portal은 수정하거나 배포하지 않았다.
