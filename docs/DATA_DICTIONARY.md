# Future Telemetry Data Dictionary

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`
payload schema: `ams2-telemetry-chunk-v1`

## 1. 이 문서의 판정 범위

이 문서는 `TelemetryArchiveContracts.cs`, `TelemetryChunkModels.cs`, `TelemetryChunkAccumulator.cs`의 실제 직렬화 계약을 기준으로 한다. HTTP body는 `TelemetryChunkEnvelope` JSON을 gzip한 값이다. JSON property는 camelCase이며, object의 null property는 생략되고 numeric row의 미지원 값은 명시적인 JSON `null`로 남는다.

검증 표기는 다음처럼 구분한다.

| 표기 | 의미 |
|---|---|
| `HEADER_VERIFIED` | 설치된 AMS2 v14 `SharedMemory.h`에서 type/order/offset을 확인하고 synthetic layout fixture가 parser 값을 확인했다. |
| `FIXTURE_VERIFIED` | collector/chunk/gzip/hash fixture에서 해당 계약을 확인했다. |
| `REAL_SHORT_RUN_VERIFIED` | 실제 AMS2 short run의 persisted gzip에서 값 존재/변화를 확인했다. full-lap·multi-car·모든 unit 의미까지 확정했다는 뜻은 아니다. |
| `SEMANTICS_PENDING` | 실제 값은 보존됐지만 단위/축/방향/차량별 의미를 정상 장거리 시나리오에서 더 확인해야 한다. |
| `ADAPTER_WIRED` | 현재 Overlay runtime snapshot → archive DTO/row 연결과 durable capture가 구현됐다. |

따라서 아래의 `captured`는 local durable archive layer의 계약을 뜻하고, verification 열은 실제 근거 범위를 별도로 표시한다. 실제 AMS2 84.986초/1인 Practice에서 core field capture와 durable gzip은 확인됐지만 authoritative driver ownership, end-to-end completeness, completed lap, multi-car, incident와 production upload는 없었다. v14 header에 field가 있다는 사실만으로 모든 161 useful field/unit을 real GREEN으로 선언하지 않는다.

## 2. 공통 cadence, source, scope, privacy

| streamType | 기록 주기 | source scope | visibility | null 원칙 |
|---|---:|---|---|---|
| `SESSION_METADATA` | session start/end/change; 30 s chunk | global/session + participant dictionary | `PUBLIC_REPLAY` candidate | 미지원/미관측/의미 미확정은 null 또는 capability state |
| `RACE_STORY` | detected event; 30 s chunk | global/event + 관련 participant | `PUBLIC_REPLAY` candidate | event와 무관하거나 source가 없으면 null |
| `PARTICIPANT_REPLAY` | 5 Hz/active participant | all-participant facts | `PUBLIC_REPLAY` candidate | slot fact 미노출/무효면 null; identity 필드는 필수 |
| `DRIVER_TELEMETRY` | 20 Hz/viewed-root candidate | viewed/root consistency gate를 통과한 vehicle; authoritative ownership 미확인 | `PRIVATE_DRIVER_ANALYTICS` | consistency gate 실패 시 row 자체를 만들지 않음; 개별 미지원 채널은 null |
| `INCIDENT_TRACE` | candidate -3 s~+3 s, 20 Hz | 관련 participant 후보 trace | `PUBLIC_REPLAY` candidate | 관련 없는 participant는 row 없음; optional fact는 null |

`PUBLIC_REPLAY`는 공개를 자동 승인한다는 뜻이 아니다. Server의 event/classification/access policy가 실제 공개 여부를 결정한다. `PRIVATE_DRIVER_ANALYTICS`는 Server access가 uploading installation owner에 묶이지만 Client source ownership은 별도 미해결 gate다.

## 3. 공통 envelope

| JSON field | 의미 | 단위/형식 | 주기 | source | scope/privacy | null/누락 의미 | 검증 |
|---|---|---|---|---|---|---|---|
| `schema` | payload 계약 이름 | literal `ams2-telemetry-chunk-v1` | chunk당 1 | serializer | all / stream visibility | 금지 | FIXTURE_VERIFIED |
| `chunkId` | 동일 attempt/stream/index의 안정적 content identity | `chunk-` + stable SHA-256 prefix | chunk당 1 | Client identity factory | all / stream visibility | 금지 | FIXTURE_VERIFIED |
| `streamType` | 5개 capture tier 식별 | enum string | chunk당 1 | accumulator | all / stream visibility | 금지 | FIXTURE_VERIFIED |
| `visibility` | Server access-policy 후보 | enum string | chunk당 1 | accumulator policy | all | 금지 | FIXTURE_VERIFIED |
| `sessionId` | 다섯 stream이 공유하는 capture session ID | opaque string | session당 1 | Client session identity | session | 금지 | FIXTURE_VERIFIED |
| `sessionFingerprint` | 기존 witness/result와 상관시킬 observed session fingerprint | opaque string | session당 1 | existing session identity adapter | session | 금지 | ADAPTER_WIRED; REAL_SHORT_RUN_VERIFIED |
| `witnessId` | 이 설치/관측자의 session witness ID | opaque string | witness당 1 | Client session identity | witness | 금지 | FIXTURE_VERIFIED |
| `attemptId` | restart를 분리하는 attempt ID | opaque string | attempt당 1 | Client attempt identity | attempt | 금지 | FIXTURE_VERIFIED |
| `attemptNumber` | 같은 session 내 attempt ordinal | integer, 1부터 | attempt당 1 | Client attempt identity | attempt | 금지 | FIXTURE_VERIFIED |
| `scheduledEventHint` | 일정과 연결할 비권위 hint | string | session당 0..1 | schedule/session adapter | session/public candidate | 생략 = hint 없음 | ADAPTER_WIRED; real short run은 schedule hint 없음 |
| `chunkIndex` | stream/attempt 내 chunk ordinal | integer, 0부터 | chunk당 1 | accumulator | stream | 금지 | FIXTURE_VERIFIED |
| `startElapsedMs` | chunk 첫 sample의 capture elapsed | ms | chunk당 1 | monotonic capture clock | attempt | 금지 | FIXTURE_VERIFIED |
| `endElapsedMs` | chunk 마지막 sample의 capture elapsed | ms | chunk당 1 | monotonic capture clock | attempt | 금지 | FIXTURE_VERIFIED |
| `startLap` | chunk에서 관측된 최소 lap ordinal | lap ordinal | chunk당 0..1 | row lap index | attempt | 생략 = lap을 알 수 없거나 metadata-only | FIXTURE_VERIFIED |
| `endLap` | chunk에서 관측된 최대 lap ordinal | lap ordinal | chunk당 0..1 | row lap index | attempt | 생략 = lap을 알 수 없거나 metadata-only | FIXTURE_VERIFIED |
| `firstCapturedAtUtc` | chunk 첫 wall-clock evidence | ISO-8601 UTC | chunk당 1 | Client UTC clock | witness | 금지 | FIXTURE_VERIFIED |
| `lastCapturedAtUtc` | chunk 마지막 wall-clock evidence | ISO-8601 UTC | chunk당 1 | Client UTC clock | witness | 금지 | FIXTURE_VERIFIED |
| `quality` | sample-rate/drop/completeness object | object | chunk당 1 | accumulator | stream | 금지 | FIXTURE_VERIFIED |
| `data` | compact row/dictionary 또는 metadata records | object | chunk당 1 | accumulator | stream | 금지 | FIXTURE_VERIFIED |

`capturedAtUtc`/`capturedAtUnixMs`는 evidence clock이며 정렬의 primary clock이 아니다. primary timeline은 witness-local, nondecreasing `sessionElapsedMs`다. PC clock 차이가 있는 multi-witness merge에서 UTC만으로 순서를 확정하면 안 된다.

## 4. `quality` object

| field | 의미 | 단위/형식 | 주기/source | scope/privacy | null 의미 | 검증 |
|---|---|---|---|---|---|---|
| `clockSource` | elapsed clock authority | `MONOTONIC_CAPTURE_CLOCK` | chunk당 1 / Client clock | stream visibility | 금지 | FIXTURE_VERIFIED |
| `targetSampleRateHz` | stream 목표 cadence | Hz; metadata/story는 event 정책값 | chunk당 1 / options | stream visibility | 금지 | FIXTURE_VERIFIED |
| `expectedSampleCount` | cadence/participant 기준 기대 logical rows | count | chunk당 1 / accumulator | stream visibility | 금지 | FIXTURE_VERIFIED |
| `actualSampleCount` | 실제 보존 rows/records | count | chunk당 1 / accumulator | stream visibility | 금지 | FIXTURE_VERIFIED |
| `missingSamples` | `max(expected-actual,0)` | count | chunk당 1 / accumulator | stream visibility | 금지; 0은 missing 없음 | FIXTURE_VERIFIED |
| `droppedSamples` | cap/gap/filter 때문에 알 수 있는 drop | count | chunk당 1 / accumulator | stream visibility | 금지; 0은 known drop 없음 | FIXTURE_VERIFIED |
| `droppedInputMessages` | bounded input channel이 거절한 message | count | chunk당 1 / runtime counter | stream visibility | 금지; 0은 known input drop 없음 | FIXTURE_VERIFIED |
| `captureCompleteness` | chunk의 완전성 판정 | `COMPLETE`, `PARTIAL` 등 | chunk당 1 / accumulator | stream visibility | 금지 | FIXTURE_VERIFIED |
| `sourceWitnessCount` | 이 raw chunk를 만든 witness 수 | count; raw chunk는 현재 1 | chunk당 1 / collector | stream visibility | 금지 | FIXTURE_VERIFIED |

`expectedSampleCount`의 replay/incident 값은 sample instant 수가 아니라 participant row 기대 수를 포함한다. Server는 streamType별 정의를 적용해야 한다. 현재 `quality`는 inner `LocalDurableTelemetryArchive`가 관측한 cadence/cap/channel 손실만 완전히 표현한다. 그 바깥 Runtime batch queue drop과 worker failure는 process counter에만 남을 수 있고 이 object 및 session `captureCompleteness`에 완전 전파되지 않으므로, `COMPLETE`나 0 drop을 end-to-end 무손실 증거로 사용하면 안 된다.

## 5. `data` object와 dictionary reference

| field | 의미 | 단위/형식 | null/누락 의미 |
|---|---|---|---|
| `fields` | numeric row의 positional schema | ordered string array | metadata stream은 빈 배열 |
| `dictionaries` | 반복 문자열을 compact integer ref로 변환한 table | map<string,string[]> | 해당 chunk에 문자열 ref가 없으면 빈 map |
| `rows` | `fields`와 같은 길이의 numeric/null arrays | `double?[][]` | row 안 null은 unsupported/invalid/not observed; 0과 다름 |
| `records` | Tier 1 structured metadata records | object array | metadata 이외 stream에서 생략 |

`nameRef`, `eventTypeRef` 같은 값은 해당 chunk의 dictionary index다. 다른 chunk의 같은 숫자와 직접 비교하지 않는다. dictionary string은 snapshot/hint이며 영구 identity가 아니다.

| dictionary key | 참조 field | 값의 의미 | stream |
|---|---|---|---|
| `eventTypes` | `eventTypeRef` | detected event type code | `RACE_STORY` |
| `eventIds` | `eventIdRef` | idempotent event ID | `RACE_STORY` |
| `factCodes` | `factCodeRef` | detector reason/raw fact code | `RACE_STORY` |
| `names` | `nameRef` | participant name snapshot | `PARTICIPANT_REPLAY` |
| `vehicles` | `vehicleRef` | participant vehicle snapshot/ref | `PARTICIPANT_REPLAY` |
| `vehicleClasses` | `vehicleClassRef` | participant class snapshot/ref | `PARTICIPANT_REPLAY` |
| `candidates` | `candidateRef` | incident candidate ID | `INCIDENT_TRACE` |
| `triggerCodes` | `triggerCodeRef` | incident raw-evidence trigger code | `INCIDENT_TRACE` |

## 6. `SESSION_METADATA` record

| field | 의미 | 단위/형식 | 주기 | SHM/Client source | scope/privacy | null/누락 의미 | 검증 |
|---|---|---|---|---|---|---|---|
| `capturedAtUtc` | metadata 관측 시각 | ISO-8601 UTC | start/end/change | Client UTC clock | session/public candidate | 금지 | FIXTURE_VERIFIED |
| `sessionElapsedMs` | metadata 관측 capture elapsed | ms | start/end/change | monotonic clock | session/public candidate | 금지 | FIXTURE_VERIFIED |
| `gameBuild` | AMS2 build number | integer | start/change | `mBuildVersionNumber` | global/public candidate | 0은 source value; unknown은 capability로 표시 | REAL_SHORT_RUN_VERIFIED=`3398` |
| `sharedMemoryVersion` | SHM schema version | integer | start | `mVersion` | global/public candidate | 0은 invalid source | REAL_SHORT_RUN_VERIFIED=`14` |
| `clientVersion` | Overlay Client version | semver string | start | assembly/config | installation/public candidate | 금지 | FIXTURE_VERIFIED |
| `parserVersion` | field/layout parser contract | string | start | Client parser build | installation/public candidate | 금지 | FIXTURE_VERIFIED |
| `track` | stable untranslated track key/snapshot | string | start/change | `mTrackLocation` | session/public candidate | 생략 = not exposed/unset | REAL_SHORT_RUN_VERIFIED=`인터라고스` |
| `layout` | stable untranslated variation key/snapshot | string | start/change | `mTrackVariation` | session/public candidate | 생략 = not exposed/unset | REAL_SHORT_RUN_VERIFIED=`인터라고스 GP` |
| `trackLengthMeters` | configured track length | m | start/change | `mTrackLength` | session/public candidate | 생략 = invalid/unset | REAL_SHORT_RUN_VERIFIED=`4294.947754` |
| `sessionType` | observed practice/qualifying/race/time-attack type | normalized/raw string | start/change | `mSessionState` adapter | session/public candidate | 생략 = unknown enum/state | ADAPTER_WIRED; REAL_SHORT_RUN_VERIFIED=`PRACTICE` |
| `clockSource` | record primary elapsed clock | string | every record | Client clock | session/public candidate | 금지 | FIXTURE_VERIFIED |
| `timedSessionDurationMs` | normalized configured timed duration | ms | start/change | `mSessionDuration` (minutes) after conversion | session/public candidate | 생략 = lap race/unknown/not verified | HEADER_VERIFIED; not exercised in real short Practice metadata |
| `eventTimeRemainingMs` | normalized observed remaining time | ms | change | `mEventTimeRemaining`; known build unit exception | session/public candidate | 생략 = unavailable or unit not safely normalized | HEADER_VERIFIED; not exercised in real short Practice metadata |
| `joinedMidSession` | capture began after session start | boolean | start | session adapter evidence | witness/public candidate | false means evidence says start capture; not same as unknown | ADAPTER_WIRED; real short run=`false` |
| `sessionStartOffsetMs` | evidence-backed offset from official session start | ms | start | session adapter/server correlation | witness/public candidate | 생략 = offset unknown; never guessed | ADAPTER_WIRED; real run omitted with `NOT_EXPOSED` status |
| `sessionStartOffsetStatus` | offset capability/evidence state | enum string | start | Client capability policy | witness/public candidate | 금지; default `UNKNOWN` | FIXTURE_VERIFIED |
| `sessionDurationMinutes` | source configured duration value | minutes | start/change | `mSessionDuration` | session/public candidate | 생략 = unknown/not observed | HEADER_VERIFIED; not exercised in real short Practice metadata |
| `configuredLaps` | configured lap count | laps | start/change | `mLapsInEvent` | session/public candidate | 생략 = timed race/unset/unknown | HEADER_VERIFIED; not exercised in real short Practice metadata |
| `observedParticipants` | active/announced participant count | count | start/change | `mNumParticipants` + roster validation | session/public candidate | 생략 = unavailable | REAL_SHORT_RUN_VERIFIED=`1` |
| `vehicleClass` | viewed/root class snapshot | string | start/change | `mCarClassName` after viewed/context gate | session/public candidate | 생략 = mixed/unknown/unresolved | ADAPTER_WIRED; REAL_SHORT_RUN_VERIFIED=`GT3_Gen2`; local ownership proof 아님 |
| `sessionPrivacyRaw` | observed private-session fact without policy inference | string | start/change | `mSessionIsPrivate` | session/access policy | 생략 = unknown/not exposed | REAL_SHORT_RUN_VERIFIED=`NOT_MARKED_PRIVATE` |
| `captureStarted` | this record marks capture start | boolean | lifecycle | Client collector | witness/public candidate | false = not a start marker | FIXTURE_VERIFIED |
| `captureEnded` | this record marks orderly capture end | boolean | lifecycle | Client collector | witness/public candidate | false = no orderly end marker | FIXTURE_VERIFIED |
| `captureCompleteness` | session-level completeness reason | string | lifecycle/change | Client collector/recovery | witness/public candidate | 금지; `UNKNOWN` allowed | FIXTURE_VERIFIED |
| `fields` | extensible capability/settings/weather/pit map | object | start/change | Runtime adapter | scope depends on key; never secrets | empty = no extra facts | ADAPTER_WIRED; real weather/pit/flag/capability keys archived |
| `participants` | session participant dictionary snapshots | object array | start/roster change | participant arrays | public replay candidate | empty = no roster snapshot in this record | REAL_SHORT_RUN_VERIFIED, 1 participant/vehicle/class snapshot |

### 6.1 `fields.<key>` capability value

| nested field | 의미 | 형식 | null/누락 의미 |
|---|---|---|---|
| `state` | `CAPTURED`, `OBSERVED_ONLY`, `NOT_EXPOSED`, `NOT_SUPPORTED`, `UNKNOWN` | enum string | 금지; default `UNKNOWN` |
| `unit` | numeric/text semantic unit | string | 생략 = unit 없음/미확정 |
| `numericValue` | numeric fact | number | 생략 = 이 key는 numeric이 아니거나 값 없음 |
| `textValue` | text/enum-normalized fact | string | 생략 = text 값 없음 |
| `booleanValue` | boolean fact | boolean | 생략 = boolean 값 없음; false와 다름 |
| `rawEnumValue` | source raw enum/flags | integer | 생략 = raw enum 없음 |

고정 DTO 밖의 key registry는 Runtime adapter가 명시적으로 만든다. 실제 run에는 ambient/track temperature, rain/snow, wind, mandatory pit lap, raw pit schedule/yellow state, world-position/lap-distance capability가 기록됐다. 임의 spelling을 future Web의 안정 계약으로 사용하지 않고 이 registry와 schema version을 따른다.

#### 6.1.1 attempt-local stream observation key

아래 네 key는 `TelemetryCapabilityValue` object이며 `state=CAPTURED`,
`booleanValue=<true|false>`로 직렬화된다. 각 값은 현재 capture attempt에서 Runtime
adapter가 해당 stream input을 한 번이라도 관측했는지를 나타내는 monotonic fact다.
한 번 `true`가 되면 같은 attempt에서 `false`로 돌아가지 않고, 새 attempt에서
다시 관측을 시작한다.

| exact `fields` key | `booleanValue=true` 의미 | `booleanValue=false` 의미 |
|---|---|---|
| `raceStory` | 이 attempt에서 `RACE_STORY` event input을 관측함 | 아직 story event를 관측하지 못함 |
| `replay` | 이 attempt에서 `PARTICIPANT_REPLAY` participant input을 관측함 | 아직 replay participant input을 관측하지 못함 |
| `driverTelemetry` | viewed/root consistency gate를 통과한 `DRIVER_TELEMETRY` candidate input을 관측함 | 아직 gate를 통과한 driver input을 관측하지 못함 |
| `incidentHighRate` | 이 attempt에서 `INCIDENT_TRACE` candidate high-rate input을 관측함 | 아직 incident high-rate input을 관측하지 못함 |

`false`는 해당 stream을 지원하지 않는다는 capability 판정이 아니다. 또한
`true`는 Runtime 관측 사실이지 local atomic commit이나 Server upload 성공 증거가 아니다.
저장·전송 실패가 개입할 수 있으므로 Server/Web은 실제 durable stream 존재 여부를
Server의 raw chunk index로 판정해야 한다. Schema 14 후보의 session index 응답은
`capabilitySource=DURABLE_CHUNK_INDEX`와 별도 `streamCapabilities` object를 반환한다.
그 object의 같은 네 key는 요청 installation에 실제로 보이는 durable chunk에서 계산되며,
Web availability 판정은 `SESSION_METADATA.fields`가 아니라 이 값을 사용한다.

특히 metadata `driverTelemetry=true`는 local ownership proof가 아니다. 공식 v14 header에는 authoritative spectator/local-owner/player-ID signal이 없고 game state/input activity도 authority가 아니다. 현재 resolver는 viewed/root playing 일치만 확인하므로 private driver release는 별도 FAIL gate이며 authoritative attestation 전 기본 OFF/fail-closed가 안전하다. 1인 session/Time Attack 예외도 heuristic이다.

### 6.2 `participants[]`

| field | 의미/source | 단위/형식 | null 의미 | privacy/검증 |
|---|---|---|---|---|
| `participantRef` | session-scoped compact ref / Client dictionary | integer | 금지 | public replay candidate; FIXTURE_VERIFIED |
| `slot` | raw SHM participant slot | integer 0..63 | 금지 | public replay candidate; HEADER_VERIFIED |
| `generation` | slot reuse/rejoin generation | integer 1+ | 금지 | ADAPTER_WIRED; real short run generation 1, rejoin scenario는 PENDING |
| `nameSnapshot` | `mParticipantInfo[].mName` snapshot | string | 생략 = name absent | public candidate; HEADER_VERIFIED; permanent identity 아님 |
| `vehicleRef` | `mCarNames[]` compact/stable session ref | string | 생략 = vehicle absent | public candidate; HEADER_VERIFIED |
| `vehicleClassRef` | `mCarClassNames[]` compact/stable session ref | string | 생략 = class absent | public candidate; HEADER_VERIFIED |

## 7. `RACE_STORY` row (`data.fields` order)

기본 sample rate는 event-driven이다. 모든 field의 privacy는 `PUBLIC_REPLAY` candidate이며 runtime detector/adapter가 연결됐다. 실제 short run은 session start/state/end 계열 3 story records를 남겼고, pit/flag/penalty/incident event 종류는 해당 시나리오에서 발생하지 않았다.

| field | 의미 | 단위/형식 | source | null 의미 |
|---|---|---|---|---|
| `sessionElapsedMs` | event primary time | ms | monotonic capture clock | 금지 |
| `capturedAtUnixMs` | event wall-clock evidence | Unix epoch ms | Client UTC clock | 금지 |
| `eventTypeRef` | `dictionaries.eventTypes[]` index | integer ref | RaceEventEngine/fact adapter | 금지 |
| `eventIdRef` | idempotent event ID dictionary index | integer ref | detector/Client ID | 금지 |
| `factCodeRef` | detector reason/fact code dictionary index | integer ref | detector | null = no additional code |
| `participantRef` | related session participant | integer ref | participant dictionary | null = global/session event |
| `lap` | related observed lap | ordinal | participant timing | null = not relevant/unknown |
| `sector` | related observed sector | integer/raw ordinal | participant timing | null = not relevant/unknown |
| `lapDistanceMeters` | event track progress | m | `mCurrentLapDistance` | null = not relevant/unavailable |
| `worldX` | event world-space X | AMS2 world coordinate | `mWorldPosition[0]` | null = location unavailable |
| `worldY` | event world-space Y | AMS2 world coordinate | `mWorldPosition[1]` | null = location unavailable |
| `worldZ` | event world-space Z | AMS2 world coordinate | `mWorldPosition[2]` | null = location unavailable |
| `positionBefore` | position immediately before transition | ordinal | event detector history | null = not a position event/unknown |
| `positionAfter` | position immediately after transition | ordinal | participant `mRacePosition` | null = not a position event/unknown |
| `lapTimeMs` | completed/PB/fastest lap duration | ms | lap event adapter from seconds timing | null = not a lap-time event/invalid |
| `raceStateRaw` | source race-state enum | raw integer | `mRaceStates[]`/`mRaceState` | null = no scoped source |
| `pitStateRaw` | source pit-mode enum | raw integer | `mPitModes[]`/`mPitMode` | null = no scoped source |
| `flagColourRaw` | observed flag colour enum | raw integer | `mHighestFlagColours[]`/root | null = no flag observation |
| `flagReasonRaw` | observed flag reason enum | raw integer | `mHighestFlagReasons[]`/root | null = no reason observation |
| `penaltyTypeRaw` | detector's raw penalty/pit-schedule code | raw integer | pit schedule/Race Control adapter | null = no penalty code |
| `resultStateRaw` | observed finish/DNF/RET/DSQ result-state code | raw integer | race-state/result adapter | null = event has no result state |

Normative minimum `eventTypeRef` dictionary values are: `SESSION_START`, `SESSION_STATE`, `RACE_START`, `POSITION_CHANGE`, `LEADER_CHANGE`, `PODIUM_ENTRY`, `PODIUM_EXIT`, `LAP_COMPLETE`, `PERSONAL_BEST`, `SESSION_FASTEST_LAP`, `PIT_ENTRY`, `PIT_EXIT`, `DRIVE_THROUGH`, `STOP_GO`, `PENALTY_CLEARED`, `RETIREMENT`, `DNF`, `DISQUALIFICATION`, `YELLOW`, `DOUBLE_YELLOW`, `FULL_COURSE_YELLOW`, `RED`, `INCIDENT_CANDIDATE`, `FINISH`, `SESSION_END`. UI popup suppression 여부와 무관하게 detected fact를 저장하는 것이 계약이다.

## 8. `PARTICIPANT_REPLAY` row (`data.fields` order)

모든 row는 5 Hz all-participant public replay candidate다. row cadence/encoding은 fixture와 실제 short run에서 확인됐고, real archive는 3 chunks/425 rows/missing 0이었다. world position/speed/heading candidate는 변했지만 participant 1명과 completed lap 없음 때문에 full-race replay semantics는 PARTIAL이다.

| field | 의미 | 단위/형식 | SHM/Client source | null 의미 |
|---|---|---|---|---|
| `sessionElapsedMs` | sample primary time | ms | monotonic capture clock | 금지 |
| `participantRef` | session compact participant identity | integer | Client dictionary | 금지 |
| `slot` | raw participant slot | integer 0..63 | array index | 금지 |
| `generation` | slot reuse/rejoin generation | integer 1+ | Client roster lifecycle | 금지 |
| `lap` | observed current lap | ordinal | `mCurrentLap` | null = unavailable/unset |
| `lapDistanceMeters` | current lap progress | m | `mCurrentLapDistance` | null = invalid/not exposed |
| `racePosition` | observed raw race position | ordinal | `mRacePosition` | null = unset/unknown |
| `worldX` | world-space X | AMS2 world coordinate | `mWorldPosition[0]` | null = invalid/not exposed |
| `worldY` | world-space Y | AMS2 world coordinate | `mWorldPosition[1]` | null = invalid/not exposed |
| `worldZ` | world-space Z | AMS2 world coordinate | `mWorldPosition[2]` | null = invalid/not exposed |
| `raceStateRaw` | participant race-state enum | raw integer | `mRaceStates[slot]` | contract currently requires a raw value |
| `pitStateRaw` | participant pit-mode enum | raw integer | `mPitModes[slot]` | contract currently requires a raw value |
| `nameRef` | `dictionaries.names[]` index | integer ref | `mName` snapshot | null = absent/empty name |
| `vehicleRef` | `dictionaries.vehicles[]` index | integer ref | `mCarNames[slot]` | null = absent vehicle |
| `vehicleClassRef` | `dictionaries.vehicleClasses[]` index | integer ref | `mCarClassNames[slot]` | null = absent class |
| `headingRadians` | participant world heading/yaw | rad | selected component of `mOrientations[slot]` after convention validation | real short-run change observed; component/sign SEMANTICS_PENDING |
| `speedMetersPerSecond` | participant source speed | m/s | `mSpeeds[slot]` | REAL_SHORT_RUN_VERIFIED; null = unavailable |

`headingRadians`와 `speedMetersPerSecond`는 기존 15개 field 뒤에 append된 additive field다. X/Y/Z만으로 계산한 이동 벡터와 source heading/speed를 구분해서 보존한다. short run의 heading 변화만으로 component/sign을 최종 확정하지 않으므로 analyzer는 semantic status를 확인한다.

## 9. `DRIVER_TELEMETRY` row (`data.fields` order)

모든 row는 20 Hz viewed/root driver candidate이고 visibility는 `PRIVATE_DRIVER_ANALYTICS`다. `LocalParticipantResolved=true`이고 `SourceParticipantRef==DriverRef`일 때만 row를 만들지만, 이 두 값은 현재 resolver 내부의 viewed/root 일관성 proof다. SHM에 authoritative local-owner/spectator signal이 없어 spectator remote-follow를 배제하지 못하므로 실제 owner privacy를 보장하지 않으며 release blocker다. gate flag 자체는 매 row에 반복하지 않는다.

| field | 의미 | 단위/형식 | SHM/Client source | null 의미 / 검증 주의 |
|---|---|---|---|---|
| `sessionElapsedMs` | primary capture time | ms | monotonic clock | 금지; FIXTURE_VERIFIED |
| `capturedAtUnixMs` | wall-clock evidence | Unix epoch ms | Client UTC clock | 금지; FIXTURE_VERIFIED |
| `driverRef` | session-local viewed/root candidate ref | integer | local participant resolver | 금지; ADAPTER_WIRED; real short run=`64`; 영구 account ID/authoritative owner proof 아님 |
| `lap` | current lap | ordinal | local `mCurrentLap` | null = unknown/unset |
| `sector` | current sector | raw ordinal | local `mCurrentSector` | null = unknown/unset |
| `lapDistanceMeters` | current-lap progress | m | local `mCurrentLapDistance` | null = invalid/unavailable |
| `worldX` | local car world X | AMS2 world coordinate | local `mWorldPosition[0]` | null = unavailable |
| `worldY` | local car world Y | AMS2 world coordinate | local `mWorldPosition[1]` | null = unavailable |
| `worldZ` | local car world Z | AMS2 world coordinate | local `mWorldPosition[2]` | null = unavailable |
| `speedMetersPerSecond` | viewed/local vehicle speed | m/s | `mSpeed` | REAL_SHORT_RUN_VERIFIED; 0.002252..2.843291 |
| `rpm` | engine speed | RPM | `mRpm` | REAL_SHORT_RUN_VERIFIED; 1,132..7,365 |
| `gearRaw` | reverse/neutral/gear source value | raw integer (`-1`,`0`,`1+`) | `mGear` | REAL_SHORT_RUN_VERIFIED; -1/0/1 |
| `throttle` | filtered throttle | ratio 0..1 | `mThrottle` | REAL_SHORT_RUN_VERIFIED; 0..1 |
| `brake` | filtered brake | ratio 0..1 | `mBrake` | REAL_SHORT_RUN_VERIFIED; 0..1 |
| `steering` | filtered steering | ratio -1..1 | `mSteering` | captured real but unchanged 0 in this run; use unfiltered channel for proven input change |
| `clutch` | filtered clutch | ratio 0..1 | `mClutch` | REAL_SHORT_RUN_VERIFIED; 0..1 |
| `unfilteredThrottle` | raw driver throttle demand | ratio 0..1 | `mUnfilteredThrottle` | REAL_SHORT_RUN_VERIFIED; 0..1 |
| `unfilteredBrake` | raw driver brake demand | ratio 0..1 | `mUnfilteredBrake` | REAL_SHORT_RUN_VERIFIED; 0..1 |
| `unfilteredSteering` | raw driver steering demand | ratio -1..1 | `mUnfilteredSteering` | REAL_SHORT_RUN_VERIFIED; -1..1 |
| `unfilteredClutch` | raw driver clutch demand | ratio 0..1 | `mUnfilteredClutch` | captured real but unchanged 0 in this run |
| `longitudinalAccelerationMetersPerSecondSquared` | vehicle-local longitudinal acceleration | m/s² after axis/unit validation | `mLocalAcceleration` candidate component | real change observed; axis/unit SEMANTICS_PENDING |
| `lateralAccelerationMetersPerSecondSquared` | vehicle-local lateral acceleration | m/s² after axis/unit validation | `mLocalAcceleration` candidate component | real change and graph PASS; axis/unit SEMANTICS_PENDING |
| `verticalAccelerationMetersPerSecondSquared` | vehicle-local vertical acceleration | m/s² after axis/unit validation | `mLocalAcceleration` candidate component | real change observed; axis/unit SEMANTICS_PENDING |
| `headingRadians` | local car world heading/yaw | rad | selected `mOrientation` component after convention validation | real short-run change; component/sign SEMANTICS_PENDING |
| `velocityX` | local car world velocity X | m/s | `mWorldVelocity[0]` | REAL_SHORT_RUN_VERIFIED for changing source |
| `velocityY` | local car world velocity Y | m/s | `mWorldVelocity[1]` | REAL_SHORT_RUN_VERIFIED for changing source |
| `velocityZ` | local car world velocity Z | m/s | `mWorldVelocity[2]` | REAL_SHORT_RUN_VERIFIED for changing source |
| `fuelLevelRatio` | raw remaining-fuel ratio | normalized source scalar | `mFuelLevel` | lineage/unit contract test PASS; actual vehicle/refuel semantics PENDING |
| `fuelCapacityLiters` | raw declared tank capacity | header-declared L | `mFuelCapacity` | lineage/unit contract test PASS; vehicle cross-check PENDING |
| `fuelLiters` | derived estimated remaining fuel | L candidate | `fuelLevelRatio * fuelCapacityLiters` when both values are finite and capacity > 0 | synthetic product test PASS; actual vehicle/refuel semantics PENDING |
| `brakeBias` | observed brake bias | normalized source scalar | `mBrakeBias` | captured real=`0.41`; vehicle/setup cross-check PENDING |
| `engineDamage` | engine damage | source ratio 0..1 | `mEngineDamage` | captured real, but no controlled damage event |
| `aeroDamage` | aerodynamic damage | source ratio 0..1 | `mAeroDamage` | captured real 0, but no controlled damage event |
| `suspensionDamage` | single local suspension damage summary | source ratio 0..1 | candidate aggregate of `mSuspensionDamage[4]` | null until aggregation policy is fixed; four-corner source is HEADER_VERIFIED |
| `tyreTempFrontLeftCelsius` | FL tyre bulk temperature | °C | `mTyreTemp[0]` | real change observed, short-run only |
| `tyreTempFrontRightCelsius` | FR tyre bulk temperature | °C | `mTyreTemp[1]` | real change observed, short-run only |
| `tyreTempRearLeftCelsius` | RL tyre bulk temperature | °C | `mTyreTemp[2]` | real change observed, short-run only |
| `tyreTempRearRightCelsius` | RR tyre bulk temperature | °C | `mTyreTemp[3]` | real change observed, short-run only |
| `tyrePressureFrontLeftKpa` | FL tyre air pressure | kPa | `mAirPressure[0]` PSI × 6.894757 | captured ~1,114..1,115; unit/scale SEMANTICS_PENDING |
| `tyrePressureFrontRightKpa` | FR tyre air pressure | kPa | `mAirPressure[1]` PSI × 6.894757 | captured ~1,114..1,115; unit/scale SEMANTICS_PENDING |
| `tyrePressureRearLeftKpa` | RL tyre air pressure | kPa | `mAirPressure[2]` PSI × 6.894757 | captured ~1,129..1,130; unit/scale SEMANTICS_PENDING |
| `tyrePressureRearRightKpa` | RR tyre air pressure | kPa | `mAirPressure[3]` PSI × 6.894757 | captured ~1,129..1,130; unit/scale SEMANTICS_PENDING |
| `tyreWearFrontLeft` | FL remaining/wear source scalar | source ratio 0..1 | `mTyreWear[0]` | null = unavailable; wear direction needs live check |
| `tyreWearFrontRight` | FR remaining/wear source scalar | source ratio 0..1 | `mTyreWear[1]` | null = unavailable; wear direction needs live check |
| `tyreWearRearLeft` | RL remaining/wear source scalar | source ratio 0..1 | `mTyreWear[2]` | null = unavailable; wear direction needs live check |
| `tyreWearRearRight` | RR remaining/wear source scalar | source ratio 0..1 | `mTyreWear[3]` | null = unavailable; wear direction needs live check |
| `trackTemperatureCelsius` | track temperature | °C | `mTrackTemperature` | REAL_SHORT_RUN_VERIFIED=`39.687851` |
| `ambientTemperatureCelsius` | ambient temperature | °C | `mAmbientTemperature` | REAL_SHORT_RUN_VERIFIED=`29.449003` |
| `rainDensity` | rain density | ratio 0..1 | `mRainDensity` | REAL_SHORT_RUN_VERIFIED=`0` |
| `pitStateRaw` | local pit-mode enum | raw integer | `mPitMode`/`mPitModes[local]` | null = unresolved/unavailable |
| `lapValid` | whether current lap remains valid | boolean encoded `1`/`0` | inverse of `mLapInvalidated` | null = validity unknown |
| `currentLapTimeMs` | current lap elapsed | ms | `mCurrentTime` seconds × 1000 | null = invalid/unavailable; **session elapsed가 아님** |

FL/FR/RL/RR는 header array order 0/1/2/3이다. 0은 valid 측정일 수 있으므로 null과 혼동하지 않는다.

## 10. `INCIDENT_TRACE` row (`data.fields` order)

모든 row는 incident candidate 주변 관련 participant의 20 Hz public replay candidate다. trigger가 명시한 related refs에 더해 latest/prior ring context의 anchor world X/Z 기준 50 m 이내 participant를 거리순으로 최대 4명 추가하고, 전체 related cap 8을 넘지 않는다. near participant 포함과 먼 participant 제외는 unit fixture로 검증됐지만 실제 multiplayer 사고는 pending이다. capture는 충돌 후보 raw fact를 남길 뿐 fault/blame을 만들지 않는다.

| field | 의미 | 단위/형식 | source | null 의미 |
|---|---|---|---|---|
| `relativeTimeMs` | trigger 기준 상대 시간 | ms; 음수=pre-roll | monotonic elapsed 차이 | 금지 |
| `sessionElapsedMs` | attempt primary time | ms | monotonic clock | 금지 |
| `capturedAtUnixMs` | wall-clock evidence | Unix epoch ms | Client UTC clock | 금지 |
| `candidateRef` | `dictionaries.candidates[]` index | integer ref | incident detector candidate ID | 금지 |
| `triggerCodeRef` | `dictionaries.triggerCodes[]` index | integer ref | raw-evidence detector reason | 금지 |
| `participantRef` | related participant identity | integer ref | session dictionary | 금지 |
| `slot` | raw participant slot | integer 0..63 | array index | 금지 |
| `generation` | slot generation | integer 1+ | roster lifecycle | 금지 |
| `lap` | observed lap | ordinal | `mCurrentLap` | null = unknown |
| `lapDistanceMeters` | observed progress | m | `mCurrentLapDistance` | null = unavailable |
| `racePosition` | observed raw position | ordinal | `mRacePosition` | null = unset/unknown |
| `worldX` | world-space X | AMS2 world coordinate | `mWorldPosition[0]` | null = unavailable |
| `worldY` | world-space Y | AMS2 world coordinate | `mWorldPosition[1]` | null = unavailable |
| `worldZ` | world-space Z | AMS2 world coordinate | `mWorldPosition[2]` | null = unavailable |
| `raceStateRaw` | participant race-state enum | raw integer | `mRaceStates[slot]` | current contract requires raw value |
| `pitStateRaw` | participant pit-mode enum | raw integer | `mPitModes[slot]` | current contract requires raw value |
| `flagColourRaw` | observed flag colour | raw integer | participant/root flag source | current contract requires raw value; 0 may mean none |
| `flagReasonRaw` | observed flag reason | raw integer | participant/root reason source | current contract requires raw value; 0 may mean none |
| `participantDisappeared` | detector saw roster disappearance near trigger | boolean encoded `1`/`0` | roster lifecycle detector | 금지 |
| `positionChangeMagnitude` | absolute observed ordinal jump near trigger | positions | event detector history | 금지; 0 means no jump |
| `headingRadians` | participant world heading/yaw | rad | selected component of `mOrientations[slot]` after convention validation | source changed in real replay, but actual incident convention PENDING |
| `speedMetersPerSecond` | participant source speed at incident sample | m/s | `mSpeeds[slot]` | source changed in real replay; actual incident scenario PENDING |

incident v1은 exact participant velocity vector를 제공하지 않지만, source heading/speed와 20 Hz X/Y/Z를 함께 남긴다. Server는 X/Y/Z 차분으로 world movement vector를 계산할 수 있고, source heading은 저속/정지 자세에 사용할 수 있다. 충돌 impulse나 fault는 이 값만으로 단정하지 않는다.

## 11. local-only upload sidecar

`TelemetryPendingUploadMetadata`는 queue/recovery용이며 gzip HTTP body 안에는 들어가지 않는다. 주요 local-only 값은 `endpoint`, `relativeChunkPath`, `contentType`, `contentEncoding`, `payloadSha256`, `compressedSha256`, `uncompressedBytes`, `compressedBytes`, `status`, `attemptCount`, `createdAtUtc`, `updatedAtUtc`, `lastAttemptAtUtc`, `nextAttemptAtUtc`, `lastError`다. token, DB credential, Windows username, IP를 저장하지 않는다. Server가 받은 raw chunk의 무결성/index에는 hash/size를 별도 기록할 수 있으나 그것은 Server contract다.

## 12. 명시적 비수집 데이터

- bearer/pairing/HMAC/API/DB secret
- Windows username, IP/network identifier
- SteamID를 sample마다 반복한 값
- raw controller button mask (`mJoyPad0`, `mDPad`)
- Client가 추정한 incident fault/blame
- Client가 계산한 corner loss, coaching text, PB rank 또는 official League classification

이 항목은 null로 대체해 올릴 대상도 아니다. 애초에 row/schema에 포함하지 않는다.

## 13. capture input에는 있으나 그대로 upload하지 않는 값

| input/local field | upload representation / reason |
|---|---|
| `eventId`, `eventType`, `factCode` | 문자열 반복 대신 `eventIdRef`, `eventTypeRef`, `factCodeRef`와 `eventIds`, `eventTypes`, `factCodes` dictionary로 전송 |
| `candidateId`, `triggerCode` | `candidateRef`, `triggerCodeRef`와 `candidates`, `triggerCodes` dictionary로 전송 |
| `relatedParticipantRefs` | 관련 participant row를 고르는 capture filter; array 자체는 전송하지 않음 |
| `localParticipantResolved`, `sourceParticipantRef` | private row 생성 전 viewed/root consistency gate; 통과한 `driverRef`만 row에 남지만 authoritative owner proof는 아님 |
| `tyreTemperaturesCelsius`, `tyrePressuresKpa`, `tyreWear` | FL/FR/RL/RR의 12개 named numeric row field로 펼쳐 전송 |
| `localDriver`, `incidentCandidate` | `TelemetryFrameSample` transport input wrapper; 해당 stream row로 분해되며 wrapper 자체는 전송하지 않음 |
| runtime counters `acceptedMessages`, `droppedMessages`, `committedChunks`, `commitFailures` | process diagnostics; upload payload field가 아님. inner archive가 관측한 drop만 chunk `quality`에 반영되며 outer batch queue drop/worker failure는 현재 완전 전파되지 않음 |
| recovery/store outcome (`chunkPath`, `metadataPath`, `disposition`, `validChunks`, `rebuiltPendingMetadata`, `preservedTemporaryFiles`, `issues`, `path`, `code`, `detail`) | local durability/recovery diagnostics; raw telemetry HTTP body가 아님 |

## 14. Append-only 확장 field 계약 (현재 TelemetryFieldCatalog)

이 절은 base prefix 뒤에만 추가된 field의 완전한 계약이다. numeric compact row의 null은
JSON null이고, 0/false는 유효한 관측값이다. 새 field는 fixture/계약 wiring 범위에서만
확인됐으며 실제 AMS2 short-run으로 의미·단위가 검증됐다는 주장은 하지 않는다.

### 14.1 기계 대조 field-count summary

| stream | base | appended | 현재 catalog 총계 | 대조 대상 |
|---|---:|---:|---:|---|
| RACE_STORY | 21 | 2 | **23** | RaceStoryFields |
| PARTICIPANT_REPLAY | 17 | 19 | **36** | ParticipantReplayFields |
| DRIVER_TELEMETRY | 52 | 82 scalar + 88 wheel | **222** | DriverTelemetryFields |
| INCIDENT_TRACE | 22 | 25 | **47** | IncidentTraceFields |
| **합계** | **112** | **216** | **328** | four ordered arrays |

기계 대조 규칙: StoryBase + {yellowFlagStateRaw, participantIsActiveRaw}; ReplayBase +
ReplayExtension; DriverBase + DriverScalarExtension + BuildWheelFields(); IncidentBase +
IncidentExtension. 이 절의 exact name 목록은 그 네 ordered catalog 배열과 일치해야 한다.

### 14.2 RACE_STORY 추가 2 field

두 field는 event-driven, 관련 participant 또는 session scope, PUBLIC_REPLAY candidate다.
source는 FutureTelemetrySnapshotAdapter.Event 및 ParticipantActiveStateEvent이며
SEMANTICS_PENDING이다.

| exact field | 의미 / type | 정확한 SHM·Client source | null / 해석 주의 |
|---|---|---|---|
| yellowFlagStateRaw | yellow/FCY state raw enum, integer | root mYellowFlagState → TelemetrySnapshot.YellowFlagStateRaw | source가 없는 event면 null; colour/reason과 별개 state이며 UI 문구나 fault가 아님 |
| participantIsActiveRaw | lifecycle boolean, 1/0 | Client roster lifecycle: initial/active=1, disappeared tombstone=0; input mParticipantInfo[slot].mIsActive | lifecycle event가 아니면 null; 0은 삭제가 아닌 tombstone evidence |

### 14.3 PARTICIPANT_REPLAY 추가 19 field

모든 field는 active participant별 5 Hz PUBLIC_REPLAY candidate이고
BuildReplayParticipant가 mParticipantInfo[slot]에서 보존한다. null은 unavailable/non-finite,
raw enum 0은 null과 다르다. 완료 lap, multi-car, enum 및 orientation 축은
SEMANTICS_PENDING이다.

| exact field | 의미 / unit·raw type | 정확한 SHM source | null / 주의 |
|---|---|---|---|
| lapsCompleted | completed laps, integer | mLapsCompleted | unavailable이면 null |
| sectorRaw | current sector raw ordinal | mCurrentSector | unavailable이면 null |
| currentSector1TimeSeconds, currentSector2TimeSeconds, currentSector3TimeSeconds | current S1/S2/S3, seconds | mCurrentSector1Time, mCurrentSector2Time, mCurrentSector3Time | non-finite=null; sentinel pending |
| lapInvalidated | current lap invalid, 1/0 | mLapInvalidated | 0=유효/미무효 관측값 |
| orientationRawX, orientationRawY, orientationRawZ | orientation vector, raw float | mOrientation[0], [1], [2] | non-finite=null; yaw axis/sign pending |
| nationalityRaw | nationality enum/code | mNationality | raw 0도 유효할 수 있음 |
| pitScheduleRaw | pit schedule enum | mPitSchedule | enum mapping pending |
| highestFlagColourRaw, highestFlagReasonRaw | participant flag colour/reason enum | mHighestFlagColour, mHighestFlagReason | 0도 none/raw value일 수 있음 |
| bestLapTimeSeconds, lastLapTimeSeconds | participant best/last lap, seconds | mBestLapTime, mLastLapTime | non-finite=null; sentinel pending |
| fastestSector1TimeSeconds, fastestSector2TimeSeconds, fastestSector3TimeSeconds | participant fastest S1/S2/S3, seconds | mFastestSector1Time, mFastestSector2Time, mFastestSector3Time | non-finite=null; invalid-lap policy pending |
| isActive | snapshot active flag, 1/0 | mIsActive | roster는 active만 emit; lifecycle proof는 story field 사용 |

### 14.4 DRIVER_TELEMETRY 추가 scalar 82 field

모든 scalar는 20 Hz, viewed/root consistency gate를 통과한 driver candidate만 기록하는
PRIVATE_DRIVER_ANALYTICS다. 이 gate는 authoritative local ownership을 증명하지 않으며 privacy release blocker다. source는 AddDriverRawValues: root=TelemetrySnapshot,
participant=matched mParticipantInfo[slot], vehicle=viewed/root vehicle SHM이다. finite
실패=null, raw enum/boolean 0=유효값이며 모든 새 unit/axis/enum은 SEMANTICS_PENDING이다.

| exact field | 의미 / unit·raw type | 정확한 SHM·Client source | null / 주의 |
|---|---|---|---|
| rootLapInvalidated | root lap invalid, 1/0 | root mLapInvalidated | 현재 snapshot만 의미 |
| participantLapInvalidated | participant lap invalid, 1/0 | mParticipantInfo[slot].mLapInvalidated | root와 다를 수 있음 |
| bestLapTimeSeconds, lastLapTimeSeconds | root best/last lap, s | mBestLapTime, mLastLapTime | sentinel pending |
| splitTimeAheadSeconds, splitTimeBehindSeconds, splitTimeSeconds | root split/gap, s raw | mSplitTimeAhead, mSplitTimeBehind, mSplitTime | signed direction/basis pending |
| personalFastestLapTimeSeconds, worldFastestLapTimeSeconds | personal/world best lap, s | mPersonalFastestLapTime, mWorldFastestLapTime | official classification 아님 |
| currentSector1TimeSeconds, currentSector2TimeSeconds, currentSector3TimeSeconds | root current S1/S2/S3, s | mCurrentSector1Time, mCurrentSector2Time, mCurrentSector3Time | sentinel pending |
| fastestSector1TimeSeconds, fastestSector2TimeSeconds, fastestSector3TimeSeconds | root fastest S1/S2/S3, s | mFastestSector1Time, mFastestSector2Time, mFastestSector3Time | invalid-lap policy pending |
| personalFastestSector1TimeSeconds, personalFastestSector2TimeSeconds, personalFastestSector3TimeSeconds | personal fastest S1/S2/S3, s | mPersonalFastestSector1Time, mPersonalFastestSector2Time, mPersonalFastestSector3Time | private only |
| worldFastestSector1TimeSeconds, worldFastestSector2TimeSeconds, worldFastestSector3TimeSeconds | world fastest S1/S2/S3, s | mWorldFastestSector1Time, mWorldFastestSector2Time, mWorldFastestSector3Time | official record 아님 |
| rootPitModeRaw, rootPitScheduleRaw | root pit mode/schedule enum | mPitMode, mPitSchedule | enum mapping pending |
| participantPitScheduleRaw | participant pit schedule enum | mParticipantInfo[slot].mPitSchedule | root와 구별 |
| highestFlagColourRaw, highestFlagReasonRaw | root flag colour/reason enum | mHighestFlagColour, mHighestFlagReason | UI 표시 보장 아님 |
| participantHighestFlagColourRaw, participantHighestFlagReasonRaw | participant flag colour/reason enum | mParticipantInfo[slot].mHighestFlagColour, mHighestFlagReason | root와 다를 수 있음 |
| carFlagsRaw | vehicle flags bit/enum | mCarFlags | bit meaning pending |
| oilTemperatureCelsius, waterTemperatureCelsius | oil/water temperature, °C | mOilTempCelsius, mWaterTempCelsius | calibration pending |
| oilPressureKPa, waterPressureKPa, fuelPressureKPa | oil/water/fuel pressure, kPa | mOilPressureKPa, mWaterPressureKPa, mFuelPressureKPa | scale/support pending |
| maxRpm, numGears | max RPM / gear count | mMaxRPM, mNumGears | vehicle/setup dependent |
| odometerKilometres | vehicle odometer, km | mOdometerKM | session distance와 다를 수 있음 |
| antiLockActive, boostActive | ABS/boost active, 1/0 | mAntiLockActive, mBoostActive | 0=inactive observation |
| lastOpponentCollisionIndex | opponent slot/index raw integer | mLastOpponentCollisionIndex | -1/0 sentinel pending |
| lastOpponentCollisionMagnitude | collision magnitude scalar | mLastOpponentCollisionMagnitude | impulse/culpability 아님 |
| boostAmount | boost amount scalar | mBoostAmount | range pending |
| orientationRawX, orientationRawY, orientationRawZ | orientation vector raw float | mOrientation[0..2] | yaw axis/sign pending |
| localVelocityRawX, localVelocityRawY, localVelocityRawZ | local velocity vector raw float | mLocalVelocity[0..2] | axis/unit pending |
| worldVelocityRawX, worldVelocityRawY, worldVelocityRawZ | world velocity vector raw float | mWorldVelocity[0..2] | named velocity와 다른 raw preservation |
| angularVelocityRawX, angularVelocityRawY, angularVelocityRawZ | angular velocity vector raw float | mAngularVelocity[0..2] | unit/axis pending |
| localAccelerationRawX, localAccelerationRawY, localAccelerationRawZ | local acceleration vector raw float | mLocalAcceleration[0..2] | derived G candidate와 구별 |
| worldAccelerationRawX, worldAccelerationRawY, worldAccelerationRawZ | world acceleration vector raw float | mWorldAcceleration[0..2] | unit/axis pending |
| extentsCentreRawX, extentsCentreRawY, extentsCentreRawZ | extents-centre vector raw float | mExtentsCentre[0..2] | coordinate convention pending |
| engineSpeedRadiansPerSecond | engine angular speed, rad/s | mEngineSpeed | RPM conversion/scale pending |
| engineTorqueNewtonMetres | engine torque, N·m | mEngineTorque | sign/availability pending |
| frontWingRaw, rearWingRaw | wing setting/state raw float | mFrontWing, mRearWing | setup unit pending |
| handBrake | hand-brake scalar | mHandBrake | range pending |
| crashStateRaw | crash state raw enum | mCrashState | fault conclusion 금지 |
| turboBoostPressure | turbo boost pressure raw float | mTurboBoostPressure | unit/scale pending |
| drsStateRaw | DRS state raw enum | mDrsState | enum mapping pending |
| antiLockSetting, tractionControlSetting | ABS/TC setting numeric | mAntiLockSetting, mTractionControlSetting | enabled/level convention pending |
| ersDeploymentModeRaw, ersAutoModeEnabled | ERS deployment enum / auto boolean | mErsDeploymentMode, mErsAutoModeEnabled | hybrid-only; unsupported value 추정 금지 |
| clutchTemperatureKelvin | clutch temperature, K | mClutchTemperature | support/scale pending |
| clutchWear | clutch wear scalar | mClutchWear | wear direction pending |
| clutchOverheated, clutchSlipping | clutch state, 1/0 | mClutchOverheated, mClutchSlipping | threshold semantics pending |
| launchStageRaw | launch-control stage enum | mLaunchStage | enum mapping pending |
| currentTimeSecondsRaw | root current time raw, s | root mCurrentTime | currentLapTimeMs raw evidence; session elapsed 아님 |
| sequenceNumberRaw | SHM snapshot sequence | root mSequenceNumber | ordering 보조; monotonic clock primary |

### 14.5 DRIVER_TELEMETRY four-wheel 확장 88 field

아래 22 prefix는 각각 정확히 FrontLeft, FrontRight, RearLeft, RearRight suffix로 확장된다.
numeric exact name은 <prefix>FrontLeft, <prefix>FrontRight, <prefix>RearLeft,
<prefix>RearRight다. tyreCompound만 text dictionary ref이므로 exact name은
tyreCompoundFrontLeftRef, tyreCompoundFrontRightRef, tyreCompoundRearLeftRef,
tyreCompoundRearRightRef다. wheel index는 각각 SHM tyre [0], [1], [2], [3]이다.
88 field는 모두 20 Hz/viewed-root candidate/PRIVATE_DRIVER_ANALYTICS이며 authoritative local-owner 보장은 아직 없다. null은 wheel
미노출/non-finite source다. 모든 raw unit·enum은 SEMANTICS_PENDING이다.

| exact prefix (위 네 exact expansion) | 의미 / unit·raw type | 정확한 per-wheel SHM source |
|---|---|---|
| tyreFlags | tyre flags bit/enum raw | mTyreFlags[i] |
| tyreTerrain | terrain enum raw | mTerrain[i] |
| tyreLocalY | tyre local Y raw float | mTyreLocalY[i] |
| tyreRevolutionsPerSecond | rotation rate, rev/s | mTyreRPS[i] |
| tyreHeightAboveGround | height above ground raw float | mTyreHeightAboveGround[i] |
| tyreBrakeDamage | brake damage scalar | mBrakeDamage[i] |
| tyreSuspensionDamage | suspension damage scalar | mSuspensionDamage[i] |
| tyreBrakeTemperatureCelsius | brake temperature, °C | mBrakeTempCelsius[i] |
| tyreTreadTemperatureKelvin | tread temperature, K | mTyreTreadTemp[i] |
| tyreLayerTemperatureKelvin | layer temperature, K | mTyreLayerTemp[i] |
| tyreCarcassTemperatureKelvin | carcass temperature, K | mTyreCarcassTemp[i] |
| tyreRimTemperatureKelvin | rim temperature, K | mTyreRimTemp[i] |
| tyreInternalAirTemperatureKelvin | internal-air temperature, K | mTyreInternalAirTemp[i] |
| wheelLocalPositionY | wheel local Y raw float | mWheelLocalPositionY[i] |
| tyreSuspensionTravelMetres | suspension travel, m | mSuspensionTravel[i] |
| tyreSuspensionVelocity | suspension velocity raw float | mSuspensionVelocity[i] |
| tyreAirPressurePsi | air pressure, PSI | mAirPressure[i]; base kPa is separate conversion |
| tyreCompound | compound text dictionary ref | mTyreCompound[i] |
| tyreLeftTemperatureCelsius | left temperature, °C | mTyreTempLeft[i] |
| tyreCenterTemperatureCelsius | centre temperature, °C | mTyreTempCenter[i] |
| tyreRightTemperatureCelsius | right temperature, °C | mTyreTempRight[i] |
| rideHeightCentimetres | ride height, cm | mRideHeight[i] |

### 14.6 INCIDENT_TRACE 추가 25 field

모든 field는 incident candidate -3 s~+3 s participant trace, 20 Hz, PUBLIC_REPLAY
candidate다. participant timing/pose는 mParticipantInfo[slot], session flag는 root,
collision fact는 viewed vehicle과 Client participant dictionary join이다. null은 관련
source 없음/non-finite다. candidate는 evidence-only이며 fault, blame, penalty가 아니다.
새 values의 unit/enum/incident semantics는 SEMANTICS_PENDING이다.

| exact field | 의미 / unit·raw type | 정확한 SHM·Client source | null / 주의 |
|---|---|---|---|
| lapsCompleted | participant completed laps, integer | mLapsCompleted | unavailable=null |
| sectorRaw | participant current sector raw | mCurrentSector | unavailable=null |
| currentSector1TimeSeconds, currentSector2TimeSeconds, currentSector3TimeSeconds | current S1/S2/S3, s | mCurrentSector1Time, mCurrentSector2Time, mCurrentSector3Time | sentinel pending |
| lapInvalidated | lap invalid, 1/0 | mLapInvalidated | 0=false observation |
| orientationRawX, orientationRawY, orientationRawZ | orientation vector raw float | mOrientation[0..2] | yaw axis/sign pending |
| nationalityRaw | nationality enum/code | mNationality | 0 may be valid raw |
| pitScheduleRaw | pit schedule enum | mPitSchedule | mapping pending |
| highestParticipantFlagColourRaw, highestParticipantFlagReasonRaw | flag colour/reason enum | mHighestFlagColour, mHighestFlagReason | root와 다를 수 있음 |
| bestLapTimeSeconds, lastLapTimeSeconds | best/last lap, s | mBestLapTime, mLastLapTime | sentinel pending |
| fastestSector1TimeSeconds, fastestSector2TimeSeconds, fastestSector3TimeSeconds | fastest S1/S2/S3, s | mFastestSector1Time, mFastestSector2Time, mFastestSector3Time | invalid-lap policy pending |
| isActive | active flag, 1/0 | mIsActive | trace roster filter상 exceptional/stale input |
| yellowFlagStateRaw | root yellow/FCY enum | root mYellowFlagState | colour/reason과 별개 |
| viewedParticipantRef | viewed participant compact ref | Client dictionary lookup of root mViewedParticipantIndex | lookup failure=null; permanent identity 아님 |
| collisionOpponentSlotRaw | viewed last-opponent slot/index | viewed mLastOpponentCollisionIndex | sentinel pending |
| collisionOpponentRef | opponent compact ref | Client dictionary join of collisionOpponentSlotRaw | roster join failure=null |
| collisionMagnitude | viewed collision magnitude scalar | viewed mLastOpponentCollisionMagnitude | impulse/fault/blame 아님 |
| crashStateRaw | viewed crash-state enum | viewed mCrashState | damage/fault conclusion 금지 |

## 15. 확장 field visibility와 interpretation 경계

추가 replay/incident rows는 PUBLIC_REPLAY candidate일 뿐 Server access policy 전에는 공개가
아니다. 82 scalar와 88 wheel field는 private driver telemetry에만 있으며 public replay나
incident payload로 복사되지 않는다. controls, tyre, suspension, engine, collision raw 값은
coaching/replay 재처리 input evidence이지 race-control 자동 판정이나 official
classification 생성 근거가 아니다.

Server의 owner-bound visibility는 다른 installation의 조회를 막지만 잘못 선택된 source를 고치지는 못한다. authoritative attestation 전에는 private driver capture를 기본 OFF/fail-closed로 하고 archive를 사용자 공개/분석 release에 사용하지 않는다. 1인 session/Time Attack 허용은 ownership proof가 아닌 heuristic이다.
