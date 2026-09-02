# GPT Next Task Handoff Report

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`
대상: 다음 GPT/Codex 작업자

이 문서의 상대 evidence/artifact 경로는 repository root (`outputs/AMS2KRLeague`) 기준이다.

## 1. 한 줄 상태

P023은 SHM useful field `161 / 161`을 parse하고, standalone raw leaf `160 / 160`을 gzip까지 full-shape로 보존한다. taxonomy는 analytic raw `159`, internal data-quality raw `1` (`R104`), internal container `1` (`R008`)이며 partial/derived-only/no-influence는 모두 `0`이다. 그러나 T1 9개와 T5 7개의 declared stream placement가 맞지 않고 T2 전체 raw change journal 정책도 확정되지 않았다. 더 중요하게, 공식 v14 header에는 `mViewedParticipantIndex` 외 spectator/local-owner/player-ID signal이 없고 game state와 input/control activity도 authority가 아니므로 viewed/root playing 일치만으로 spectator remote-follow를 배제할 수 없다. outer batch queue drop/worker failure도 chunk completeness에 완전 전파되지 않는다. 실제 full lap/multi-car/incident/staging gate도 남아 있으므로 **전체 verdict는 YELLOW이고 0.2.3 version bump/release는 보류**한다.

요청 형식의 최종 판정표는 `AMS2_FUTURE_TELEMETRY_ARCHIVE_FINAL_REPORT.md`에 있다.

## 2. 절대 경계

- 공개 기준 버전은 `0.2.2`다.
- 기준 commit은 `5f643cb5a63639d5e7681b8afe54f18bde03297d` (`Release v0.2.2`)다.
- 작업 tree의 P023 변경은 아직 commit/tag/release하지 않았다.
- `VERSIONING.md`, release metadata, GitHub tag/Release를 아직 올리지 않는다.
- Cafe24 후보 application은 `1.5.0`, schema `14`지만 **운영에 배포하지 않았다**.
- Production Portal과 운영 DB는 수정하지 않았다.
- 실제 controlled AMS2 run은 network upload를 끈 상태였다. PENDING sidecar를 운영 업로드 성공으로 보고하지 않는다.

## 3. 구현된 Client 계층

주요 변경 영역:

```text
src/AMS2LeagueClient.Core/Telemetry/
  SharedMemoryLayout.cs
  SharedMemoryParser.cs
  TelemetrySnapshot.cs
  TelemetryValueModels.cs

src/AMS2LeagueClient.Core/FutureTelemetry/
  TelemetryArchiveContracts.cs
  TelemetryArchiveOptions.cs
  TelemetrySessionClock.cs
  TelemetryChunkModels.cs
  TelemetryChunkAccumulator.cs
  TelemetryChunkSerializer.cs
  TelemetryChunkStore.cs
  TelemetryChunkUpload.cs
  LocalDurableTelemetryArchive.cs
  FutureTelemetrySnapshotAdapter.cs
  FutureTelemetryCaptureRuntime.cs

src/AMS2LeagueClient/Runtime/
  ActivityCaptureRuntime.cs
  Cafe24ActivityUploadTransport.cs
```

완료된 핵심:

- 공식 SHM v14 layout/header에 맞춰 world position, orientation, participant speed, root vehicle/input/physics/tyre/damage fields를 parser model에 연결했다.
- Tier 1~5의 공통 `sessionId/sessionFingerprint/witnessId/attemptId/attemptNumber` identity를 Session Witness와 공유한다.
- `SESSION_METADATA`, `RACE_STORY`, `PARTICIPANT_REPLAY`, `DRIVER_TELEMETRY`, `INCIDENT_TRACE`를 30초 compact JSON gzip chunk로 저장한다.
- replay 5 Hz, private driver 20 Hz, incident candidate -3~+3초 20 Hz를 bounded memory/background writer로 처리한다.
- viewed/root playing 일치 gate를 통과한 data만 `PRIVATE_DRIVER_ANALYTICS` 후보로 만든다. 공식 v14 header에는 owner attestation이 없으므로 이 gate는 spectator remote-follow를 배제하지 못한다. authoritative attestation 전 release-safe 기본은 `DRIVER_TELEMETRY` OFF/fail-closed이며, 1인 session/Time Attack 허용도 heuristic일 뿐이다.
- atomic chunk/sidecar write, SHA-256, orphan recovery, conflict quarantine, offline pending queue를 구현했다.
- Runtime upload loop가 activity upload와 telemetry chunk upload를 함께 처리한다.
- HTTP transport는 실제 gzip bytes, Bearer와 Cafe24 compatibility auth header, idempotency key, decoded/compressed SHA headers를 전송하고 response chunk/content hash를 검증한다.

기존 activity/result/witness payload는 additive compatibility를 유지한다. 새 archive identity optional fields가 없는 legacy JSON도 읽는다.

### 3.1 parser-ready와 durable capture 경계

| 분류 | 수치 | 의미 |
|---|---:|---|
| Parser-ready | `161 / 161` | typed snapshot에서 읽을 수 있음 |
| Analytic full-shape raw durable | `159 / 161` | source scalar/vector/array shape가 chunk에서 복원 가능 |
| Internal data-quality raw durable | `1 / 161` | `R104 mSequenceNumber`; quality/ordering audit용 |
| Raw leaf durable | `160 / 161` = `160 / 160` leaf rows | `R008` container를 제외한 모든 raw leaf |
| Partial/lossy raw durable | `0 / 161` | 없음 |
| Derived-only influence | `0 / 161` | 없음 |
| No durable influence | `0 / 161` | 없음 |
| Internal container | `1 / 161` | `R008 mParticipantInfo[64]`; P001-P008로 전개 |

정확 ID별 분류는 `SHM_FIELD_INVENTORY.md`의 **Durable archive coverage audit**를 단일 기준으로 사용한다. Analytic raw는 `P001-P008; R001-R007; R009-R079; R081; R083; R085-R103; R105-R145; R148-R158`, internal raw는 `R104`, container는 `R008`이다. 이는 raw leaf loss가 없다는 뜻이지 declared Tier cadence/visibility까지 모두 맞는다는 뜻은 아니다.

남은 policy gap은 T1 직접 배치 `30 / 39`와 T5 직접 배치 `10 / 17`이다. T1에서 빠진 `R053,R056,R062,R063,R075,R111,R133,R148,R149`, T5에서 빠진 `R069,R071,R072,R073,R074,R077,R083`은 모두 private T4에 full raw가 있어 재처리는 가능하지만 원래 선언한 stream과 다르다. T2는 요구 event detector 25/25를 구현했으나 59개 `T2/change` field의 generic raw old/new journal은 없다. 이 policy gap 때문에 무조건적인 `FUTURE WEB REQUIRES CLIENT UPDATE: NO` 또는 release GREEN을 보고하면 안 된다.

감사에서 발견된 연료 lineage 오류는 수정했다. `mFuelLevel`은 `fuelLevelRatio`, `mFuelCapacity`는 `fuelCapacityLiters`, 유효한 곱은 별도 `fuelLiters`로 저장하며 adapter 단위 테스트와 새 persisted synthetic fixture를 통과했다. 실제 차량/refuel에서 source 단위·semantics를 확인하는 gate는 남아 있다.

## 4. Capture tier 상태

| tier | stream | 현재 상태 | 실제/fixture 한계 |
|---|---|---|---|
| 1 | `SESSION_METADATA` | runtime PASS, direct policy placement `30 / 39` | 9개는 private T4에만 있음; real full Race settings/roster change 미검증 |
| 2 | `RACE_STORY` | 요구 detector 25/25 PASS + FCY exit | generic raw old/new journal 없음; 실제 flag/pit/penalty/full-race event density 미검증 |
| 3 | `PARTICIPANT_REPLAY` | policy `14 / 14`, 5 Hz, synthetic 32대 PASS, real 1대 425 rows | 실제 multi-car/position graph 미검증 |
| 4 | `DRIVER_TELEMETRY` | row/field implementation PASS, policy `73 / 73`, 20 Hz, real 1,697 rows/core fields | privacy release assurance FAIL: authority interface default DENY + shipping Runtime OFF 필요; full lap/semantics도 미검증 |
| 5 | `INCIDENT_TRACE` | ring/burst 및 unit fixture의 trigger-related + 50 m 이내 최대 4 nearby 선택 PASS, direct policy placement `10 / 17` | release assurance FAIL: 실제 contact/nearby/multi-witness incident 미검증; 7개 declared T5 field 미배치 |

## 5. Server 후보

위치:

```text
../AMS2League/server/cafe24_telemetry014/
```

상태:

- application `1.5.0`, schema `14`
- `POST/GET v1/telemetry/chunks`
- installation Bearer token, scope `telemetry:write`
- identity 또는 gzip body 수신, decoded/content와 wire hash 검증
- content/idempotency/chunk conflict 분리
- invalid non-sensitive JSON quarantine, privacy violation 미보존
- `PUBLIC_REPLAY`와 `PRIVATE_DRIVER_ANALYTICS` owner isolation
- elapsed/lap overlap range index/read
- session index의 `capabilitySource=DURABLE_CHUNK_INDEX` 및 visibility-aware `raceStory/replay/driverTelemetry/incidentHighRate`; Web availability의 유일한 권위이며 Client metadata의 같은 boolean은 관측 hint일 뿐임
- MariaDB `telemetry_chunk_ingests` raw canonical gzip와 `telemetry_chunks` searchable index 분리
- sample당 MariaDB row를 만들지 않음
- historical reprocess source는 immutable `payload_gzip`

로컬 verification은 telemetry 32, existing API 40, Portal 55, WEB-03 37로 총 164 checks PASS다. 새 두 check는 durable stream capability와 private driver visibility를 검증한다. 현재 후보와 dist를 포함한 PHP 82 files lint는 82/82 PASS이며 SQL migration checks도 PASS했다. 재생성한 local candidate zip은 다음에 있다.

```text
../AMS2League/server/cafe24_telemetry014/dist/ams2-cafe24-telemetry014-local-verify.zip
```

ZIP: 291,652 B, SHA-256 `DF044BD9E99582E7618280C62DB8082D0D24D8ACCBB8585B8B03897E3A63776E`.

이 결과는 local candidate GREEN이지 production deployment GREEN이 아니다. 2026-09-02 12:18 KST에 운영 health를 읽기 전용 확인한 결과 application `1.4.2`, schema `13`으로 telemetry candidate `1.5.0`/schema `14`가 아직 배포되지 않았다. 증거는 `../AMS2League/evidence/future-telemetry/production-health-readonly-20260902.json`이다. 운영 `/www/ams2`, 운영 MariaDB schema와 Portal은 건드리지 않았다.

## 6. 테스트 ledger

`outputs/AMS2KRLeague` 기준 현재 검증 결과:

| 대상 | 결과 |
|---|---|
| Release solution build | PASS, warnings 0 / errors 0 |
| `AMS2LeagueClient.Tests` | 37/37 PASS |
| `AMS2LeagueActivity.Tests` | 68/68 PASS |
| Future telemetry proof tool build | PASS, warnings 0 / errors 0 |
| Server telemetry suite | 32 PASS |
| Server existing API suite | 40 PASS |
| Server Portal suite | 55 PASS |
| Server WEB-03 suite | 37 PASS |
| Server aggregate | 164 PASS |

중요 Client regression에는 다음이 포함된다.

- real gzip magic/body와 content encoding
- Bearer + `X-AMS2-Authorization` compatibility header
- idempotency, decoded/compressed hash header
- response `chunkId/contentSha256` integrity mismatch 거부
- pre-existing PENDING chunk의 Runtime upload loop 자동 SENT 전환
- common witness/archive identity와 attempt restart
- viewed/root consistency gate와 Server private visibility 분리. authoritative local-owner/spectator 판정은 아직 없으므로 privacy release gate를 통과했다는 뜻은 아님
- crash recovery와 interrupted temp 보존

주의: 현재 outer Runtime batch queue의 drop과 worker exception은 process counter에는 남을 수 있지만 stream별 chunk `quality`와 session `captureCompleteness`에 전부 전파되지 않는다. 따라서 inner archive fixture의 drop accounting PASS를 end-to-end completeness PASS로 확대 해석하면 안 된다.

다음 작업자는 release 직전에 위 suite를 깨끗한 checkout에서 다시 실행해야 한다.

## 7. Synthetic 60분/32대 evidence

입력:

```text
../AMS2League/evidence/future-telemetry/synthetic-60min-32car-v5-stream-capabilities-20260902/
```

persisted-only output:

```text
../AMS2League/evidence/future-telemetry/offline-proof-60min-32car-v5-stream-capabilities-20260902/
```

실측:

| stream | chunks | samples | raw B | gzip B |
|---|---:|---:|---:|---:|
| `DRIVER_TELEMETRY` | 120 | 72,000 | 213,253,405 | 92,706,086 |
| `INCIDENT_TRACE` | 1 | 484 | 156,535 | 52,254 |
| `PARTICIPANT_REPLAY` | 120 | 576,000 | 162,735,772 | 68,595,629 |
| `RACE_STORY` | 42 | 45 | 62,801 | 35,397 |
| `SESSION_METADATA` | 1 | 1 | 7,142 | 1,230 |
| **합계** | **284** | **648,530** | **376,215,655** | **161,390,596** |

gzip ratio는 `0.42898426435763287`, 10 Client 단순 선형 추정은 `1,613,905,960 B`다. 2D replay, position, lap table, speed/brake/throttle/steering/G-force, driving line/centerline, incident animation 등 11개 renderer checks는 모두 PASS다. PHP 8.4 Server validator로 각 stream의 최대/대표 v5 gzip을 다시 검사했고 Story 23, Replay 36, Driver 222, Incident 47 fields를 모두 오류 없이 수락했다. Session Metadata의 `raceStory`, `replay`, `driverTelemetry`, `incidentHighRate`도 모두 `true`로 확인했다. 이 네 값은 synthetic attempt에서 input을 관측했다는 hint이며 durable availability가 아니다. Web은 Server index의 `capabilitySource=DURABLE_CHUNK_INDEX`와 visibility-aware `streamCapabilities`만 사용한다. 증거는 `../AMS2League/evidence/future-telemetry/server-validator-v5-stream-capabilities-20260902.json`이다.

이는 synthetic data에 대한 archive/reprocessing/capacity proof다. 실제 60분/32대 performance나 물리 값 정확성으로 바꿔 말하면 안 된다.

## 8. 실제 AMS2 evidence

위치:

```text
../AMS2League/evidence/future-telemetry/real-ams2-v023-candidate-20260902/controlled2/
```

핵심 결과:

- game build 3398, SHM 14, Interlagos GP, Aston Martin Vantage GT3 Evo, Practice, participant 1
- 84.986초, gzip chunk 10, raw 1,219,921 B, gzip 361,966 B
- driver 1,697 samples; target 20 Hz에서 missing/drop 3, input-message drop 0
- replay 425 rows; target 5 Hz에서 missing/drop 0
- world XYZ, lap distance, speed, throttle, brake, unfiltered steering, RPM, gear, acceleration 변화 PASS
- clean stop, archive failure 0
- persisted-only speed/brake/throttle/steering/G-force graph PASS
- completed lap/multi-car/incident가 없어 lap/position/2D/driving-line/centerline/incident proof는 해당 run에서 FAIL/not applicable

검증기는 filtered `steering` 0과 unfiltered `steering` -1..1을 구분한다. tyre pressure converted 값은 비정상 가능성이 있어 semantics pending이다. 자세한 수치와 artifact hash는 `docs/REAL_AMS2_VALIDATION.md`를 사용한다.

## 9. 실제 run에서 upload하지 않은 이유와 의미

controlled2 log:

```text
ACTIVITY_CAPTURE ... uploadConfigured=False
ACTIVITY_CONNECTION configPresent=False networkEnabled=False
```

따라서 10개 `.upload.json`은 모두 `PENDING`, `attemptCount=0`이다. 이 run은 local durable/field/offline proof를 위한 것이며 Server reject가 아니다. HTTP wire contract와 local Server archive는 자동 tests로 검증됐지만, 실제 captured chunk를 staging Server에 보낸 E2E는 별도 gate다.

### 9.1 후속 local HTTP replay

이후 원본 sidecar 상태를 변경하지 않고 controlled2의 실제 pending `DRIVER_TELEMETRY` chunk `chunk-be01d7222795c8dad9815f06138f9a9239225d5c`를 production `Cafe24ActivityUploadTransport`로 local PHP 8.4 harness에 replay했다. JSON 406,478 B / client gzip 114,932 B, payload SHA-256 `e9a4b6c79fe773e581964cb6a94929100e23c956c45a38edcb1eb06bf41f24c2`, compressed SHA-256 `c9a7c976c4387aad0522213451cce8e6abc52236d256582ddde68aca52ed54fd`였다. POST `201 STORED`, index/detail GET `200`, index count 1, canonical archive 115,850 B / SHA-256 `32bd7f5e8d88709febbacdbdf3babc4b466f1eda6ffe44cc14f424a1f65856e6`, decoded JSON byte/hash 일치를 확인했다.

증거는 repository root (`outputs/AMS2KRLeague`) 기준 `../../work/local-http-e2e/evidence/client-http-proof.json`, `index-response.json`, `get-response.json`, `server-store-proof.json`, `request-audit.ndjson`이다. 이는 실제 loopback HTTP이지 in-process controller test가 아니다. 그러나 HTTPS URI의 loopback-only HTTP rewrite와 serialized `InMemoryStore`를 사용했으므로 Cafe24 TLS/FastCGI, `PdoStore`, MariaDB 또는 staging E2E로 간주하지 않는다.

## 10. 남은 release gate

1. **Private source authority:** SHM/root의 viewed participant를 설치 사용자의 차량으로 단정하지 않는다. authoritative owner attestation 전 `DRIVER_TELEMETRY`를 기본 OFF/fail-closed로 하고 regression을 추가한다. 1인 session/Time Attack 예외를 두더라도 ownership proof가 아닌 명시적 heuristic/opt-in으로 제한한다.
2. **End-to-end completeness:** per-attempt/stream/chunk loss ledger를 두고 outer batch queue drop과 worker failure를 stream별 chunk `quality`/session `captureCompleteness`에 전파한다. close/finalize는 non-droppable acknowledged 경로로 만들고, loss-only `PARTIAL` chunk 및 validator support, terminal metadata의 last durable commit, sticky `Dispose` failure까지 구현한다. 위험도는 MEDIUM-HIGH로 보고 독립 regression을 둔다.
3. **Tier policy alignment:** raw leaf는 `160 / 160` 보존됐지만 T1 9개와 T5 7개가 declared stream에 없다. 목적/visibility에 맞게 옮기거나 field별 privacy/cost 근거와 함께 inventory policy를 수정한다. T2 generic old/new journal을 추가할지 T1/T3/T4 raw 재처리를 공식 contract로 삼을지도 확정한다.
4. **Fuel real semantics:** code/contract는 `fuelLevelRatio`, `fuelCapacityLiters`, derived `fuelLiters`로 수정했고 synthetic persisted proof와 단위 테스트를 통과했다. 실제 차량/refuel에서 ratio·capacity 단위를 검증한다.
5. **Real clean multi-lap:** clean lap 2개 이상으로 lap-distance progression, line/centerline, heading convention을 검증한다.
6. **Real multiplayer:** 2명 이상 실제 Race에서 position chart, 2D replay, slot generation/rejoin을 검증한다.
7. **Real incident:** 의도적으로 안전한 test에서 incident candidate -3~+3초, 20 Hz trigger-related + 50 m 이내 최대 4 nearby-car burst를 검증한다.
8. **Staging Server E2E:** local PHP 8.4 + `InMemoryStore` real-chunk POST/GET은 PASS했다. 별도 staging `PdoStore`/MariaDB에 같은 gzip을 POST하고 Server GET으로 같은 content hash를 돌려받아, Server-stored gzip만으로 renderer를 다시 실행한다.
9. **Semantics:** acceleration axis/unit, tyre pressure unit/scale, tyre wear direction, heading sign/component를 확정한다.
10. **Operational pressure:** 실제 60분/다수 차량 CPU/RAM/local disk/drop을 측정하고 disk-full/permission/AV-lock fault를 점검한다.
11. **Final regression:** clean build와 전체 Client/Server suite를 다시 실행한다.

이 gate가 모두 GREEN이기 전에는 `0.2.3`으로 version bump, tag, push 또는 GitHub Release를 만들지 않는다.

## 11. 다음 권장 작업 순서

1. authority interface의 default를 DENY로 두고 shipping Runtime에서 `DRIVER_TELEMETRY`를 OFF한다. authoritative attestation이 연결된 경우만 enable하고 privacy negative regression을 추가한다. 1인 session/Time Attack 허용은 heuristic임을 계약/UX에 명시한다.
2. per-attempt/stream/chunk loss ledger, acknowledged non-droppable close/finalize, loss-only `PARTIAL` chunk/validator, terminal metadata last commit, sticky `Dispose` failure로 outer loss를 durable quality/completeness에 전파한다. MEDIUM-HIGH risk 변경으로 분리 검증한다.
3. raw leaf coverage manifest는 유지하고, T1 9개/T5 7개 placement와 T2 change-journal contract를 닫는다. 이미 정리한 fuel lineage는 실제 차량에서 재검증한다.
4. completed gzip을 읽는 all-field sentinel test로 raw lineage를 검증한다.
5. Production과 분리된 local/staging schema 14를 준비한다.
6. 실제 multiplayer test session을 clean start부터 종료까지 capture한다.
7. capture 중 incident candidate와 최소 2 clean laps를 확보한다.
8. local archive hash/quality를 먼저 검증한다.
9. 동일 gzip을 staging endpoint로 upload하고 GET/detail로 다시 내려받는다.
10. 내려받은 raw gzip만 입력으로 full renderer와 field validator를 실행한다.
11. failure를 숨기지 말고 `REAL_AMS2_VALIDATION.md`와 capability matrix를 갱신한다.
12. 모든 gate GREEN이면 그때 `VERSIONING.md` 절차대로 `0.2.3`을 반영하고 commit/tag/release한다.

## 12. 최종 인계 판정

```text
BASE VERSION: 0.2.2
CANDIDATE VERSION: 0.2.3 (not applied)
IMPLEMENTATION: PARTIAL (archive/transport/shape PASS; privacy/completeness release blockers remain)
PARSER-READY SHM FIELDS: 161 / 161
DURABLE RAW LEAF FIELDS: 160 / 161 (160 / 160 leaf rows; R008 is a container)
ANALYTIC FULL-RAW FIELDS: 159 / 161
INTERNAL RAW DATA-QUALITY FIELDS: 1 / 161 (R104)
PARTIAL / DERIVED-ONLY / NO-INFLUENCE: 0 / 0 / 0
RAW-LEAF FUTURE-PROOF: PASS
DECLARED TIER FIELD PLACEMENT: FAIL (T1 30/39; T5 10/17; T2 journal policy open)
PRIVATE DRIVER SOURCE AUTHORITY: FAIL (viewed/root consistency is not authoritative local ownership)
END-TO-END CAPTURE COMPLETENESS: FAIL (outer queue/worker loss not fully propagated)
SYNTHETIC FULL-SESSION PROOF: PASS
REAL CORE FIELD CAPTURE: PASS
REAL FULL-LAP/MULTI-CAR/INCIDENT: PARTIAL
LOCAL SERVER CANDIDATE: PASS
LOCAL HTTP REAL-CHUNK ROUND TRIP: PASS (InMemoryStore; PDO/MariaDB pending)
PRODUCTION SERVER/PORTAL: UNCHANGED; LIVE 1.4.2 / SCHEMA 13
RELEASE: HOLD
OVERALL: YELLOW
```

다음 작업의 우선순위는 Web UI가 아니라 private source authority와 end-to-end completeness를 fail-closed로 닫는 것이다. raw leaf는 이미 모두 보존되므로 새로운 분석은 저장된 source를 재처리할 수 있지만, 현재 driver rows가 반드시 설치 사용자의 차량이라는 보장과 session 전체 무손실 보장은 없다. 이어서 T1/T5 visibility·cadence contract와 T2 change-journal 정책, 실제 full-session evidence와 staging gate를 닫은 뒤 Web Replay + Driver Telemetry Analysis + Race Coach로 진행한다.
