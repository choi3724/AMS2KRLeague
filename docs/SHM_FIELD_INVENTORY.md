# AMS2 Shared Memory v14 field inventory

Task: `AMS2-P023-FUTURE-TELEMETRY`
Inventory date: 2026-09-02 KST
Scope: the complete `SharedMemory` root struct and complete `ParticipantInfo` struct. Enum members are not counted as fields.

## Authoritative source and verification level

- Primary evidence is the header shipped with this AMS2 installation: `E:\SteamLibrary\steamapps\common\Automobilista 2\Support\SharedMemory\AMS2_SharedMemoryExampleApp\SharedMemory.h`.
- File metadata: 26,811 bytes; modified 2024-02-04 01:40:39 KST; SHA-256 `2ABAF09901883F80C6393AC065577FB6229FA0DB6784DABCDC502EEDA1A3674E`.
- The header declares `SHARED_MEMORY_VERSION = 14`, `STORED_PARTICIPANTS_MAX = 64`, `sizeof(ParticipantInfo) = 100` under the shipped MSVC layout, and `sizeof(SharedMemory) = 20700`.
- Reiza's support forum identifies the game-installed example header as the documentation source: <https://forum.reizastudios.com/threads/shared-memory-documentation.31241/>.
- A public v14 mirror useful for independent review is: <https://github.com/viper4gh/CREST2-AMS2/blob/master/SharedMemory.h>.

The installed header is the offset authority. The forum and mirror are corroborating URLs, not a replacement for the installed SDK artifact.

P023 candidate는 useful inventory `161 / 161`을 parse한다. `R008 mParticipantInfo[64]`는 자체 scalar leaf가 없는 container이고, 이를 제외한 raw leaf `160 / 160`은 `FutureTelemetrySnapshotAdapter`에서 compact row/metadata record를 거쳐 gzip envelope까지 full-shape로 보존된다. 정책 taxonomy는 analytic raw `159`, internal data-quality raw `1` (`R104 mSequenceNumber`), internal container `1` (`R008`)이며 partial, derived-only, no-influence는 모두 `0`이다.

이 raw-leaf 보존 완료가 곧 전체 future-proof gate의 GREEN을 뜻하지는 않는다. Inventory에 선언한 Tier와 실제 stream 배치가 다른 T1 `9`개와 T5 `7`개가 있고, T2는 최소 event detector를 모두 구현했지만 모든 `T2/change` field를 generic raw old/new journal로 남기지는 않는다. 또한 T4 viewed/root source를 authoritative local owner로 판정할 SHM signal이 없고 outer batch/worker loss가 durable completeness에 완전 전파되지 않는다. 이 policy/cadence/visibility/privacy/completeness gap은 release blocker이며, **0.2.3 release는 계속 HOLD**다.

All newly added fields in this phase are at least `HEADER_VERIFIED`: their type, order, alignment, offset and fixture parsing are verified. A real AMS2 84.986-second one-participant Practice archive subsequently proved changing world position, lap distance, speed, throttle, brake, unfiltered steering, RPM, gear and acceleration from persisted gzip chunks. That evidence is `REAL_SHORT_RUN_VERIFIED` for those core signals only; it is not a full clean lap and does not validate all 161 useful fields, units or vehicle-specific semantics.

## Mechanical counts

| Metric | Count |
|---|---:|
| `SharedMemory` root declarations | 158 |
| `ParticipantInfo` declarations | 8 |
| Total inventory rows | 166 |
| Header-useful rows | 161 |
| Intentionally excluded rows | 5 |
| v0.2.2 parser rows (root container and nested fields counted) | 77 |
| P023 parser-ready useful rows | 161 / 161 |
| Newly parser-ready rows | 84 |
| Newly durable/uploaded by the parser-only sub-change | 0; the separate P023 tier/runtime layer now archives the policy-selected subset |
| Analytic full-shape raw durable | 159 / 161 |
| Internal data-quality raw durable | 1 / 161 (`R104`) |
| Raw leaf durable | 160 / 161; 160 / 160 leaf rows |
| Partial/lossy raw durable | 0 / 161 |
| Derived-only durable influence; raw value lost | 0 / 161 |
| No durable influence | 0 / 161 |
| Internal container, no standalone leaf | 1 / 161 (`R008`) |

The five exclusions are the header's three explicitly obsolete tyre arrays (`mTyreSlipSpeed`, `mTyreGrip`, `mTyreLateralStiffness`) and two raw controller button masks (`mJoyPad0`, `mDPad`). The button masks are not needed for the stated analytics, have no stable semantic mapping in the header, and are unnecessarily sensitive. Everything else is represented in `TelemetrySnapshot`, `ParticipantSnapshot`, or `ViewedVehicleTelemetrySnapshot` after P023.

## Durable archive coverage audit

이 표는 typed snapshot property가 아니라 `FutureTelemetrySnapshotAdapter`에서 `TelemetryChunkAccumulator`의 실제 field/record로 전달되어 gzip envelope에 남는지를 기준으로 한다. `full-shape`는 source scalar/vector/array shape를 복원할 수 있다는 뜻이며, 해당 field에 선언된 모든 Tier cadence가 구현됐다는 뜻은 아니다.

| Durable class | Count | Exact inventory IDs |
|---|---:|---|
| Analytic full-shape raw value | 159 | `P001-P008; R001-R007; R009-R079; R081; R083; R085-R103; R105-R145; R148-R158` |
| Internal data-quality raw value | 1 | `R104` |
| Internal container; nested fields counted separately | 1 | `R008` |
| Partial/lossy raw value | 0 | none |
| Derived-only influence; raw lost | 0 | none |
| No durable influence | 0 | none |

`P001 mIsActive`는 `PARTICIPANT_ACTIVE_STATE` Race Story fact의 initial `true` baseline, inactive-to-active `true`, active-to-inactive `false` tombstone으로 직접 보존된다. `R104 mSequenceNumber`는 torn-read 방지에 내부 사용하면서 metadata와 private T4의 `sequenceNumberRaw`로도 보존하므로 `INTERNAL_RAW_DURABLE`이다. `R008`만 standalone raw leaf가 없는 container다.

Raw lineage와 Tier 배치는 서로 다른 감사 축이다. 현재 stream별 field placement는 다음과 같다.

- T1: inventory policy 39개 중 `30 / 39`가 T1 metadata stream에 직접 배치된다. `R053,R056,R062,R063,R075,R111,R133,R148,R149`는 raw를 잃지는 않지만 private T4 20 Hz에만 있어 T1 cadence/visibility와 불일치한다.
- T2: 요구된 25개 Race Story detector가 모두 있고 `FULL_COURSE_YELLOW_END`도 추가됐다. 그러나 59개 `T2/change` inventory field 전체를 raw old/new journal로 내보내지는 않고 T1/T3/T4 raw의 후처리에 의존한다.
- T3: inventory policy `14 / 14`를 public replay 5 Hz row에 직접 보존한다.
- T4: inventory policy `73 / 73`을 viewed/root consistency gate 뒤 private driver candidate 20 Hz row에 직접 보존한다. SHM에는 authoritative local-owner/spectator signal이 없어 privacy assurance는 FAIL이다.
- T5: inventory policy 17개 중 `10 / 17`을 public incident burst row에 직접 보존한다: `P003,R042,R043,R065,R066,R094,R124,R125,R135,R136`. trigger-related refs와 50 m 이내 거리순 최대 4 nearby refs 선택은 unit fixture를 통과했지만 실제 multiplayer incident는 pending이다. `R069,R071,R072,R073,R074,R077,R083`은 private T4에는 full raw지만 T5 incident gzip에는 없다.

따라서 raw leaf coverage는 완료됐지만 declared Tier policy coverage는 완료되지 않았다. T1 9개와 T5 7개는 적절한 sparse/private metadata 또는 incident context로 옮기거나, privacy/cost 근거와 함께 inventory policy를 명시적으로 수정해야 한다. T2 generic raw change journal을 추가할지 T1/T3/T4 재처리를 공식 contract로 삼을지도 확정해야 한다. 이 세 항목을 닫기 전에는 policy gate를 GREEN으로 판정하지 않는다.

추가 lineage 확인 결과, untranslated/translated track·layout은 별도 metadata field로, `R027` event time은 raw float와 interpreted milliseconds로, root/participant orientation과 local/world acceleration은 raw 3축으로 보존된다. 모든 useful four-corner tyre/chassis 배열과 participant timing 배열도 compact row에서 복원 가능하다. `R065/R066/R094` collision/crash raw는 private T4와 public incident row에 있고, `R156` FCY raw와 FCY exit event도 보존된다.

연료 lineage 오류는 감사 중 수정했다. `R052 mFuelLevel`은 `fuelLevelRatio`, `R053 mFuelCapacity`는 `fuelCapacityLiters`, 두 값의 유효한 곱은 별도 `fuelLiters`로 저장한다. adapter 단위 테스트와 새 60분 fixture의 persisted row 직렬화를 통과했다. 실제 차량별 단위·refuel semantics는 계속 `SEMANTICS_PENDING`이다.

이 감사는 모든 161개 raw field를 모든 sample에 복제하라는 뜻이 아니다. 같은 raw source라도 T1 sparse metadata, T2 change fact, T3 replay, T4 private driver, T5 incident 중 목적과 visibility에 맞는 stream에 배치해야 한다.

## Column legend

- `SOURCE` for every inventory row is the installed v14 `SharedMemory.h` identified above; the root/slot offset in each field cell is calculated from that exact artifact's native MSVC layout.
- `Scope`: `G` global/session, `V` viewed-participant root data, `A` all-participant array, `I` integrity/transport, `P` one `ParticipantInfo` slot.
- `Parsed`: `v0.2.2 -> P023`; `Y` means a typed snapshot value exists, not merely that bytes are copied.
- `Upload`: **P023 이전 v0.2.2 payload behavior**. `Y` is directly persisted/uploaded, `P` contributes to derived result/event evidence, `N` is not uploaded in that baseline. P023의 별도 tier archive contract는 이 열을 덮어쓰지 않고 `Policy`에 따라 additive gzip stream을 만든다.
- `Policy`: `T1` metadata on start/change, `T2` discrete event/change, `T3-5Hz` public all-participant replay candidate, `T4-20Hz-P` private viewed/local coaching candidate, `T5-BURST` high-rate incident ring candidate, `INTERNAL`, or `NONE`.
- `Status/reason`: all included rows are `HEADER_VERIFIED`. 실제 short run에서 값/변화를 확인한 일부 core row만 `REAL_SHORT_RUN_VERIFIED`를 추가하며, full-lap/unit/axis 의미가 남으면 caveat를 함께 쓴다.
- Root `V` values belong to the *viewed participant*. `LocalParticipantResolver`는 `InGamePlaying` viewed/root 일치만 확인하며 ownership을 증명하지 않는다. 공식 v14 header에는 `mViewedParticipantIndex` 외 authoritative local-owner/spectator/player-ID signal이 없고 game state/input activity도 authority가 아니다. 따라서 authoritative attestation 전에는 Tier 4를 기본 OFF/fail-closed로 두며 1인 session/Time Attack 허용도 heuristic으로만 취급한다.
- Header comments call `mEventTimeRemaining` milliseconds, but this client previously observed AMS2 build 3398 emitting seconds. The raw float is preserved; live interpretation remains a documented runtime exception.

## `ParticipantInfo` — 8/8 fields

| ID | Field @ slot offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| P001 | `mIsActive @ 0` | `bool` | active flag | P | IDENTITY | Y -> Y | P | roster lifecycle | T2 | HEADER_VERIFIED; P023 raw active baseline/tombstone durable |
| P002 | `mName @ 1` | `char[64]` | UTF-8/ASCII name snapshot | P | IDENTITY | Y -> Y | Y | replay dictionary/result identity snapshot | T1 | HEADER_VERIFIED; never permanent identity |
| P003 | `mWorldPosition @ 68` | `float[3]` | world-space X/Y/Z | P | POSITION | N -> Y | N | 2D replay, line, incident location | T3-5Hz + T5-BURST | REAL_SHORT_RUN_VERIFIED; all axes changed, full-lap geometry pending |
| P004 | `mCurrentLapDistance @ 80` | `float` | metres | P | POSITION | Y -> Y | N | progress, replay alignment, corner index | T3-5Hz + T4-20Hz-P | REAL_SHORT_RUN_VERIFIED for changing source; full-lap progression pending |
| P005 | `mRacePosition @ 84` | `uint32` | ordinal; 0 unset | P | TIMING | Y -> Y | Y | position graph/result | T2 + T3-5Hz | HEADER_VERIFIED |
| P006 | `mLapsCompleted @ 88` | `uint32` | completed laps | P | TIMING | Y -> Y | Y | lap table/replay clock | T2 + T3-5Hz | HEADER_VERIFIED |
| P007 | `mCurrentLap @ 92` | `uint32` | current lap | P | TIMING | Y -> Y | Y | sample-to-lap index | T2 + T3-5Hz + T4-20Hz-P | HEADER_VERIFIED |
| P008 | `mCurrentSector @ 96` | `int32` | sector; -1 unset | P | TIMING | Y -> Y | Y | sector/corner context | T2 + T3-5Hz | HEADER_VERIFIED |

Participant slot stride is exactly 100 bytes. `mWorldPosition` component offsets are X=68, Y=72, Z=76. Root `mParticipantInfo` begins at offset 28, so slot `n` begins at `28 + n*100`.

## `SharedMemory` root — 158 fields

### Header, state, participants, input, vehicle and event (R001-R018)

| ID | Field @ root offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| R001 | `mVersion @ 0` | `uint32` | schema version | I | GLOBAL_SESSION | Y -> Y | Y | capability/schema | T1 | HEADER_VERIFIED |
| R002 | `mBuildVersionNumber @ 4` | `uint32` | game build | G | GLOBAL_SESSION | Y -> Y | Y | compatibility/data quality | T1 | HEADER_VERIFIED |
| R003 | `mGameState @ 8` | `uint32 enum` | game state | G | GLOBAL_SESSION | Y -> Y | Y | capture gating/timeline | T2 | HEADER_VERIFIED |
| R004 | `mSessionState @ 12` | `uint32 enum` | session state | G | GLOBAL_SESSION | Y -> Y | Y | attempt/session classification | T2 | HEADER_VERIFIED |
| R005 | `mRaceState @ 16` | `uint32 enum` | viewed/global race state | G | GLOBAL_SESSION | Y -> Y | Y | start/finish state | T2 | HEADER_VERIFIED |
| R006 | `mViewedParticipantIndex @ 20` | `int32` | viewed slot; -1 unset | V | IDENTITY | Y -> Y | P | viewed candidate linkage; authoritative owner proof 아님 | T1/change | HEADER_VERIFIED |
| R007 | `mNumParticipants @ 24` | `int32` | 0..64; -1 unset | G | PARTICIPANT_ARRAY | Y -> Y | Y | roster/capture quality | T1/change | HEADER_VERIFIED |
| R008 | `mParticipantInfo @ 28` | `ParticipantInfo[64]` | slots, 6400 bytes | A | PARTICIPANT_ARRAY | Y -> Y | P | replay/result source | tiered by nested field | HEADER_VERIFIED; INTERNAL_CONTAINER, nested P001-P008 counted separately |
| R009 | `mUnfilteredThrottle @ 6428` | `float` | 0..1 | V | INPUT | N -> Y | N | pedal trace/braking exit | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, 0..1 |
| R010 | `mUnfilteredBrake @ 6432` | `float` | 0..1 | V | INPUT | N -> Y | N | braking point/pressure trace | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, 0..1 |
| R011 | `mUnfilteredSteering @ 6436` | `float` | -1..1 | V | INPUT | N -> Y | N | line/correction analysis | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, -1..1 |
| R012 | `mUnfilteredClutch @ 6440` | `float` | 0..1 | V | INPUT | N -> Y | N | start/shift analysis | T4-20Hz-P | captured real but unchanged 0 in short run |
| R013 | `mCarName @ 6444` | `char[64]` | viewed vehicle | V | VEHICLE | Y -> Y | Y | dictionary/comparison key | T1/change | HEADER_VERIFIED |
| R014 | `mCarClassName @ 6508` | `char[64]` | viewed class | V | VEHICLE | Y -> Y | Y | class/result metadata | T1/change | HEADER_VERIFIED |
| R015 | `mLapsInEvent @ 6572` | `uint32` | configured laps; 0 unset | G | GLOBAL_SESSION | Y -> Y | Y | lap-race contract | T1/change | HEADER_VERIFIED |
| R016 | `mTrackLocation @ 6576` | `char[64]` | untranslated track | G | IDENTITY | Y -> Y | Y | track key | T1 | HEADER_VERIFIED |
| R017 | `mTrackVariation @ 6640` | `char[64]` | untranslated layout | G | IDENTITY | Y -> Y | Y | layout key | T1 | HEADER_VERIFIED |
| R018 | `mTrackLength @ 6704` | `float` | metres | G | POSITION | Y -> Y | P | progress normalization/track geometry | T1 | HEADER_VERIFIED |

### Timing, flags and pit state (R019-R045)

| ID | Field @ root offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| R019 | `mNumSectors @ 6708` | `int32` | count; -1 unset | G | TIMING | Y -> Y | P | sector schema | T1 | HEADER_VERIFIED |
| R020 | `mLapInvalidated @ 6712` | `bool` | viewed lap invalid | V | TIMING | Y -> Y | Y | clean-lap/coaching filter | T2 + T4-20Hz-P | HEADER_VERIFIED |
| R021 | `mBestLapTime @ 6716` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | PB progression | T2 | HEADER_VERIFIED |
| R022 | `mLastLapTime @ 6720` | `float` | seconds; 0 unset | V | TIMING | Y -> Y | P | lap table | T2 | HEADER_VERIFIED |
| R023 | `mCurrentTime @ 6724` | `float` | seconds | V | TIMING | Y -> Y | Y | live lap trace | T4-20Hz-P | HEADER_VERIFIED |
| R024 | `mSplitTimeAhead @ 6728` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | N | relative display/event evidence | T2/change | HEADER_VERIFIED |
| R025 | `mSplitTimeBehind @ 6732` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | N | relative display/event evidence | T2/change | HEADER_VERIFIED |
| R026 | `mSplitTime @ 6736` | `float` | seconds | V | TIMING | N -> Y | N | split progression | T4-20Hz-P | HEADER_VERIFIED; live semantics pending |
| R027 | `mEventTimeRemaining @ 6740` | `float` | header says ms; live build observed seconds | G | TIMING | Y -> Y | Y | session/replay clock | T1/change | HEADER_VERIFIED; runtime unit exception |
| R028 | `mPersonalFastestLapTime @ 6744` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | personal benchmark | T2/change | HEADER_VERIFIED |
| R029 | `mWorldFastestLapTime @ 6748` | `float` | seconds; -1 unset | G | TIMING | Y -> Y | P | session benchmark | T2/change | HEADER_VERIFIED |
| R030 | `mCurrentSector1Time @ 6752` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | lap reconstruction | T4-20Hz-P | HEADER_VERIFIED |
| R031 | `mCurrentSector2Time @ 6756` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | lap reconstruction | T4-20Hz-P | HEADER_VERIFIED |
| R032 | `mCurrentSector3Time @ 6760` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | lap reconstruction | T4-20Hz-P | HEADER_VERIFIED |
| R033 | `mFastestSector1Time @ 6764` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | driver sector benchmark | T2/change | HEADER_VERIFIED |
| R034 | `mFastestSector2Time @ 6768` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | driver sector benchmark | T2/change | HEADER_VERIFIED |
| R035 | `mFastestSector3Time @ 6772` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | driver sector benchmark | T2/change | HEADER_VERIFIED |
| R036 | `mPersonalFastestSector1Time @ 6776` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | personal sector comparison | T2/change | HEADER_VERIFIED |
| R037 | `mPersonalFastestSector2Time @ 6780` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | personal sector comparison | T2/change | HEADER_VERIFIED |
| R038 | `mPersonalFastestSector3Time @ 6784` | `float` | seconds; -1 unset | V | TIMING | Y -> Y | P | personal sector comparison | T2/change | HEADER_VERIFIED |
| R039 | `mWorldFastestSector1Time @ 6788` | `float` | seconds; -1 unset | G | TIMING | Y -> Y | P | reference sector | T2/change | HEADER_VERIFIED |
| R040 | `mWorldFastestSector2Time @ 6792` | `float` | seconds; -1 unset | G | TIMING | Y -> Y | P | reference sector | T2/change | HEADER_VERIFIED |
| R041 | `mWorldFastestSector3Time @ 6796` | `float` | seconds; -1 unset | G | TIMING | Y -> Y | P | reference sector | T2/change | HEADER_VERIFIED |
| R042 | `mHighestFlagColour @ 6800` | `uint32 enum` | viewed/highest flag | V | FLAG | Y -> Y | P | race story/incident context | T2 + T5-BURST | HEADER_VERIFIED |
| R043 | `mHighestFlagReason @ 6804` | `uint32 enum` | flag reason | V | FLAG | Y -> Y | P | incident evidence | T2 + T5-BURST | HEADER_VERIFIED |
| R044 | `mPitMode @ 6808` | `uint32 enum` | viewed pit mode | V | PIT | Y -> Y | P | pit entry/exit | T2 + T4-20Hz-P | HEADER_VERIFIED |
| R045 | `mPitSchedule @ 6812` | `uint32 enum` | viewed pit schedule | V | PIT | Y -> Y | P | penalty/mandatory stop | T2 | HEADER_VERIFIED |

### Viewed vehicle state and motion (R046-R075)

| ID | Field @ root offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| R046 | `mCarFlags @ 6816` | `uint32 flags` | lights/engine/ABS/TCS etc. | V | VEHICLE | N -> Y | N | aid/state context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R047 | `mOilTempCelsius @ 6820` | `float` | Celsius | V | VEHICLE | N -> Y | N | engine health | T4-20Hz-P | HEADER_VERIFIED |
| R048 | `mOilPressureKPa @ 6824` | `float` | kPa | V | VEHICLE | N -> Y | N | engine health | T4-20Hz-P | HEADER_VERIFIED |
| R049 | `mWaterTempCelsius @ 6828` | `float` | Celsius | V | VEHICLE | N -> Y | N | engine health | T4-20Hz-P | HEADER_VERIFIED |
| R050 | `mWaterPressureKPa @ 6832` | `float` | kPa | V | VEHICLE | N -> Y | N | engine health | T4-20Hz-P | HEADER_VERIFIED |
| R051 | `mFuelPressureKPa @ 6836` | `float` | kPa | V | VEHICLE | N -> Y | N | engine/fuel diagnosis | T4-20Hz-P | HEADER_VERIFIED |
| R052 | `mFuelLevel @ 6840` | `float` | normalized 0..1 | V | VEHICLE | N -> Y | N | stint/fuel analysis | T4-20Hz-P | HEADER_VERIFIED |
| R053 | `mFuelCapacity @ 6844` | `float` | litres (header range comment is inconsistent) | V | VEHICLE | N -> Y | N | fuel normalization | T1/change + T4-20Hz-P | HEADER_VERIFIED |
| R054 | `mSpeed @ 6848` | `float` | metres/second | V | PHYSICS | N -> Y | N | speed graph/minimum/exit speed | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, 0.002252..2.843291 |
| R055 | `mRpm @ 6852` | `float` | rpm | V | VEHICLE | N -> Y | N | gear/engine analysis | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, 1,132..7,365 |
| R056 | `mMaxRPM @ 6856` | `float` | rpm | V | VEHICLE | N -> Y | N | RPM normalization | T1/change | HEADER_VERIFIED |
| R057 | `mBrake @ 6860` | `float` | filtered 0..1 | V | INPUT | N -> Y | N | braking graph | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, 0..1; prefer unfiltered for intent |
| R058 | `mThrottle @ 6864` | `float` | filtered 0..1 | V | INPUT | N -> Y | N | throttle graph | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, 0..1; prefer unfiltered for intent |
| R059 | `mClutch @ 6868` | `float` | filtered 0..1 | V | INPUT | N -> Y | N | clutch/start graph | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, 0..1 |
| R060 | `mSteering @ 6872` | `float` | filtered -1..1 | V | INPUT | N -> Y | N | steering graph | T4-20Hz-P | captured real but unchanged 0; unfiltered steering proved input change |
| R061 | `mGear @ 6876` | `int32` | -1 reverse, 0 neutral, 1+ gear | V | VEHICLE | N -> Y | N | shift/corner analysis | T4-20Hz-P | REAL_SHORT_RUN_VERIFIED, -1/0/1 |
| R062 | `mNumGears @ 6880` | `int32` | gear count; -1 unset | V | VEHICLE | N -> Y | N | vehicle capability | T1/change | HEADER_VERIFIED |
| R063 | `mOdometerKM @ 6884` | `float` | kilometres; -1 unset | V | VEHICLE | N -> Y | N | completeness/distance cross-check | T1/change | HEADER_VERIFIED |
| R064 | `mAntiLockActive @ 6888` | `bool` | ABS currently active | V | VEHICLE | N -> Y | N | braking/aids context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R065 | `mLastOpponentCollisionIndex @ 6892` | `int32` | participant slot; -1 unset | V | DAMAGE | N -> Y | N | incident candidate | T2/change + T5-BURST | HEADER_VERIFIED; not fault evidence |
| R066 | `mLastOpponentCollisionMagnitude @ 6896` | `float` | magnitude; header has no unit | V | DAMAGE | N -> Y | N | incident severity candidate | T2/change + T5-BURST | HEADER_VERIFIED; UNKNOWN unit |
| R067 | `mBoostActive @ 6900` | `bool` | boost active | V | VEHICLE | N -> Y | N | power delivery context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R068 | `mBoostAmount @ 6904` | `float` | 0..100 | V | VEHICLE | N -> Y | N | power delivery context | T4-20Hz-P | HEADER_VERIFIED |
| R069 | `mOrientation @ 6908` | `float[3]` | Euler angles | V | PHYSICS | N -> Y | N | heading/line reconstruction | T4-20Hz-P + T5-BURST | real short-run change observed; component/sign convention pending |
| R070 | `mLocalVelocity @ 6920` | `float[3]` | metres/second | V | PHYSICS | N -> Y | N | longitudinal/lateral motion | T4-20Hz-P | HEADER_VERIFIED |
| R071 | `mWorldVelocity @ 6932` | `float[3]` | metres/second | V | PHYSICS | N -> Y | N | incident/replay interpolation | T4-20Hz-P + T5-BURST | HEADER_VERIFIED |
| R072 | `mAngularVelocity @ 6944` | `float[3]` | radians/second | V | PHYSICS | N -> Y | N | rotation/spin/correction | T4-20Hz-P + T5-BURST | HEADER_VERIFIED |
| R073 | `mLocalAcceleration @ 6956` | `float[3]` | header says metres/second; semantically acceleration | V | PHYSICS | N -> Y | N | longitudinal/lateral/vertical acceleration | T4-20Hz-P + T5-BURST | real three-axis change observed; axis/unit semantics pending |
| R074 | `mWorldAcceleration @ 6968` | `float[3]` | header says metres/second; semantically acceleration | V | PHYSICS | N -> Y | N | incident dynamics | T4-20Hz-P + T5-BURST | HEADER_VERIFIED; header unit ambiguity |
| R075 | `mExtentsCentre @ 6980` | `float[3]` | local-space X/Y/Z | V | PHYSICS | N -> Y | N | vehicle geometry/collision context | T1/change | HEADER_VERIFIED |

### Tyres, damage and weather (R076-R103)

| ID | Field @ root offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| R076 | `mTyreFlags @ 6992` | `uint32[4] flags` | attached/inflated/on-ground | V | TYRE | N -> Y | N | contact/health context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R077 | `mTerrain @ 7008` | `uint32[4] enum` | surface material per tyre | V | TYRE | N -> Y | N | off-track/line/incident context | T2/change + T4-20Hz-P + T5-BURST | HEADER_VERIFIED |
| R078 | `mTyreY @ 7024` | `float[4]` | local-space Y | V | TYRE | N -> Y | N | wheel movement context | T4-20Hz-P | HEADER_VERIFIED |
| R079 | `mTyreRPS @ 7040` | `float[4]` | revolutions/second | V | TYRE | N -> Y | N | lock/spin/slip inference | T4-20Hz-P | HEADER_VERIFIED |
| R080 | `mTyreSlipSpeed @ 7056` | `float[4]` | obsolete | V | TYRE | N -> N | N | none | NONE | EXCLUDED_OBSOLETE; header says backward compatibility only |
| R081 | `mTyreTemp @ 7072` | `float[4]` | Celsius | V | TYRE | N -> Y | N | tyre operating window | T4-20Hz-P | HEADER_VERIFIED |
| R082 | `mTyreGrip @ 7088` | `float[4]` | obsolete | V | TYRE | N -> N | N | none | NONE | EXCLUDED_OBSOLETE; header says backward compatibility only |
| R083 | `mTyreHeightAboveGround @ 7104` | `float[4]` | local-space Y | V | TYRE | N -> Y | N | airborne/contact analysis | T4-20Hz-P + T5-BURST | HEADER_VERIFIED |
| R084 | `mTyreLateralStiffness @ 7120` | `float[4]` | obsolete | V | TYRE | N -> N | N | none | NONE | EXCLUDED_OBSOLETE; header says backward compatibility only |
| R085 | `mTyreWear @ 7136` | `float[4]` | 0..1 | V | TYRE | N -> Y | N | degradation/consistency | T4-20Hz-P | HEADER_VERIFIED; direction semantics live-check pending |
| R086 | `mBrakeDamage @ 7152` | `float[4]` | 0..1 | V | DAMAGE | N -> Y | N | brake health/incident context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R087 | `mSuspensionDamage @ 7168` | `float[4]` | 0..1 | V | DAMAGE | N -> Y | N | handling/incident context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R088 | `mBrakeTempCelsius @ 7184` | `float[4]` | Celsius | V | TYRE | N -> Y | N | braking analysis | T4-20Hz-P | HEADER_VERIFIED |
| R089 | `mTyreTreadTemp @ 7200` | `float[4]` | Kelvin | V | TYRE | N -> Y | N | tyre thermal analysis | T4-20Hz-P | HEADER_VERIFIED |
| R090 | `mTyreLayerTemp @ 7216` | `float[4]` | Kelvin | V | TYRE | N -> Y | N | tyre thermal analysis | T4-20Hz-P | HEADER_VERIFIED |
| R091 | `mTyreCarcassTemp @ 7232` | `float[4]` | Kelvin | V | TYRE | N -> Y | N | tyre thermal analysis | T4-20Hz-P | HEADER_VERIFIED |
| R092 | `mTyreRimTemp @ 7248` | `float[4]` | Kelvin | V | TYRE | N -> Y | N | tyre thermal analysis | T4-20Hz-P | HEADER_VERIFIED |
| R093 | `mTyreInternalAirTemp @ 7264` | `float[4]` | Kelvin | V | TYRE | N -> Y | N | pressure/thermal analysis | T4-20Hz-P | HEADER_VERIFIED |
| R094 | `mCrashState @ 7280` | `uint32 enum` | crash/off-track/spin state | V | DAMAGE | N -> Y | N | incident candidate/context | T2 + T5-BURST | HEADER_VERIFIED; never blame attribution |
| R095 | `mAeroDamage @ 7284` | `float` | 0..1 | V | DAMAGE | N -> Y | N | post-incident performance | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R096 | `mEngineDamage @ 7288` | `float` | 0..1 | V | DAMAGE | N -> Y | N | retirement/performance context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R097 | `mAmbientTemperature @ 7292` | `float` | Celsius | G | WEATHER | Y -> Y | Y | session/weather context | T1/change | HEADER_VERIFIED |
| R098 | `mTrackTemperature @ 7296` | `float` | Celsius | G | WEATHER | Y -> Y | Y | grip/tyre context | T1/change | HEADER_VERIFIED |
| R099 | `mRainDensity @ 7300` | `float` | 0..1 | G | WEATHER | Y -> Y | Y | wetness/weather timeline | T1/change | HEADER_VERIFIED |
| R100 | `mWindSpeed @ 7304` | `float` | header range 0..100; no unit stated | G | WEATHER | Y -> Y | Y | weather/aero context | T1/change | HEADER_VERIFIED; UNKNOWN unit |
| R101 | `mWindDirectionX @ 7308` | `float` | normalized X | G | WEATHER | Y -> Y | Y | weather vector | T1/change | HEADER_VERIFIED |
| R102 | `mWindDirectionY @ 7312` | `float` | normalized Y | G | WEATHER | Y -> Y | Y | weather vector | T1/change | HEADER_VERIFIED |
| R103 | `mCloudBrightness @ 7316` | `float` | non-negative scalar | G | WEATHER | Y -> Y | Y | weather/light context | T1/change | HEADER_VERIFIED |

### Integrity and PCars2 v8 vehicle additions (R104-R112)

| ID | Field @ root offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| R104 | `mSequenceNumber @ 7320` | `volatile uint32` | odd while writer active, even when stable | I | GLOBAL_SESSION | Y -> Y | P | torn-read prevention/data quality | INTERNAL | HEADER_VERIFIED; INTERNAL_RAW_DURABLE in metadata/private T4 |
| R105 | `mWheelLocalPositionY @ 7324` | `float[4]` | local-space Y | V | TYRE | N -> Y | N | suspension geometry | T4-20Hz-P | HEADER_VERIFIED |
| R106 | `mSuspensionTravel @ 7340` | `float[4]` | metres | V | PHYSICS | N -> Y | N | kerb/load/ride analysis | T4-20Hz-P | HEADER_VERIFIED |
| R107 | `mSuspensionVelocity @ 7356` | `float[4]` | pushrod deflection rate; no explicit unit | V | PHYSICS | N -> Y | N | damping/kerb analysis | T4-20Hz-P | HEADER_VERIFIED; UNKNOWN unit |
| R108 | `mAirPressure @ 7372` | `float[4]` | PSI | V | TYRE | N -> Y | N | pressure/thermal analysis | T4-20Hz-P | HEADER_VERIFIED |
| R109 | `mEngineSpeed @ 7388` | `float` | radians/second | V | VEHICLE | N -> Y | N | engine analysis/cross-check RPM | T4-20Hz-P | HEADER_VERIFIED |
| R110 | `mEngineTorque @ 7392` | `float` | newton-metres | V | VEHICLE | N -> Y | N | acceleration/power delivery | T4-20Hz-P | HEADER_VERIFIED |
| R111 | `mWings @ 7396` | `float[2]` | normalized front/rear | V | VEHICLE | N -> Y | N | setup/aero context | T1/change | HEADER_VERIFIED; index convention follows header order |
| R112 | `mHandBrake @ 7404` | `float` | 0..1 | V | INPUT | N -> Y | N | rally/drift input | T4-20Hz-P | HEADER_VERIFIED |

### All-participant PCars2 v8 arrays (R113-R127)

| ID | Field @ root offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| R113 | `mCurrentSector1Times @ 7408` | `float[64]` | seconds; -1 unset | A | TIMING | Y -> Y | P | participant live timing | T3-5Hz/change | HEADER_VERIFIED |
| R114 | `mCurrentSector2Times @ 7664` | `float[64]` | seconds; -1 unset | A | TIMING | Y -> Y | P | participant live timing | T3-5Hz/change | HEADER_VERIFIED |
| R115 | `mCurrentSector3Times @ 7920` | `float[64]` | seconds; -1 unset | A | TIMING | Y -> Y | P | participant live timing | T3-5Hz/change | HEADER_VERIFIED |
| R116 | `mFastestSector1Times @ 8176` | `float[64]` | seconds; -1 unset | A | TIMING | Y -> Y | P | sector table/benchmark | T2/change | HEADER_VERIFIED |
| R117 | `mFastestSector2Times @ 8432` | `float[64]` | seconds; -1 unset | A | TIMING | Y -> Y | P | sector table/benchmark | T2/change | HEADER_VERIFIED |
| R118 | `mFastestSector3Times @ 8688` | `float[64]` | seconds; -1 unset | A | TIMING | Y -> Y | P | sector table/benchmark | T2/change | HEADER_VERIFIED |
| R119 | `mFastestLapTimes @ 8944` | `float[64]` | seconds; -1 unset | A | TIMING | Y -> Y | Y | best-lap/result table | T2/change | HEADER_VERIFIED |
| R120 | `mLastLapTimes @ 9200` | `float[64]` | seconds; -1 unset | A | TIMING | Y -> Y | Y | lap completion/story | T2/change | HEADER_VERIFIED |
| R121 | `mLapsInvalidated @ 9456` | `bool[64]` | invalid flags | A | TIMING | Y -> Y | P | valid-lap filtering | T2/change + T3-5Hz | HEADER_VERIFIED |
| R122 | `mRaceStates @ 9520` | `uint32[64] enum` | race state per slot | A | PARTICIPANT_ARRAY | Y -> Y | Y | finish/DNF/RET/DSQ/story | T2/change + T3-5Hz | HEADER_VERIFIED |
| R123 | `mPitModes @ 9776` | `uint32[64] enum` | pit mode per slot | A | PIT | Y -> Y | Y | pit transitions/replay | T2/change + T3-5Hz | HEADER_VERIFIED |
| R124 | `mOrientations @ 10032` | `float[64][3]` | Euler angles | A | POSITION | N -> Y | N | replay heading/incident animation | T3-5Hz + T5-BURST | real one-car change observed; component/sign and multi-car pending |
| R125 | `mSpeeds @ 10800` | `float[64]` | metres/second | A | PHYSICS | N -> Y | N | replay interpolation/incident context | T3-5Hz + T5-BURST | REAL_SHORT_RUN_VERIFIED for one participant |
| R126 | `mCarNames @ 11056` | `char[64][64]` | vehicle per slot | A | VEHICLE | Y -> Y | Y | participant dictionary/result | T1/change | HEADER_VERIFIED |
| R127 | `mCarClassNames @ 15152` | `char[64][64]` | class per slot | A | VEHICLE | Y -> Y | Y | class result/replay dictionary | T1/change | HEADER_VERIFIED |

### Remaining PCars2 additions (R128-R138)

| ID | Field @ root offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| R128 | `mEnforcedPitStopLap @ 19248` | `int32` | mandatory-stop lap; -1 unset | G | PIT | Y -> Y | Y | session rule/strategy context | T1/change | HEADER_VERIFIED |
| R129 | `mTranslatedTrackLocation @ 19252` | `char[64]` | localized track | G | IDENTITY | N -> Y | N | display-only label | T1 | HEADER_VERIFIED; untranslated fields remain stable keys |
| R130 | `mTranslatedTrackVariation @ 19316` | `char[64]` | localized layout | G | IDENTITY | N -> Y | N | display-only label | T1 | HEADER_VERIFIED; untranslated fields remain stable keys |
| R131 | `mBrakeBias @ 19380` | `float` | normalized; -1 unset | V | VEHICLE | N -> Y | N | braking/setup context | T4-20Hz-P/change | HEADER_VERIFIED |
| R132 | `mTurboBoostPressure @ 19384` | `float` | header range comment 0..1+, no unit | V | VEHICLE | N -> Y | N | engine/power context | T4-20Hz-P | HEADER_VERIFIED; UNKNOWN unit |
| R133 | `mTyreCompound @ 19388` | `char[4][40]` | compound per tyre | V | TYRE | N -> Y | N | stint/tyre comparison | T1/change | HEADER_VERIFIED |
| R134 | `mPitSchedules @ 19548` | `uint32[64] enum` | pit schedule per slot | A | PIT | Y -> Y | P | penalties/mandatory stops | T2/change | HEADER_VERIFIED |
| R135 | `mHighestFlagColours @ 19804` | `uint32[64] enum` | flag per slot | A | FLAG | Y -> Y | P | participant incident/blue-flag context | T2/change + T5-BURST | HEADER_VERIFIED |
| R136 | `mHighestFlagReasons @ 20060` | `uint32[64] enum` | flag reason per slot | A | FLAG | Y -> Y | P | incident evidence | T2/change + T5-BURST | HEADER_VERIFIED |
| R137 | `mNationalities @ 20316` | `uint32[64]` | nationality table id; 0 SP/unset | A | IDENTITY | N -> Y | N | optional participant metadata | T1/change | HEADER_VERIFIED; never identity key |
| R138 | `mSnowDensity @ 20572` | `float` | 0..1 | G | WEATHER | Y -> Y | Y | winter/weather timeline | T1/change | HEADER_VERIFIED |

### AMS2 v10+ additions (R139-R158)

| ID | Field @ root offset | Raw type/shape | Unit / meaning | Scope | Category | Parsed | Upload | Future value | Policy | Status/reason |
|---|---|---|---|---|---|---|---|---|---|---|
| R139 | `mSessionDuration @ 20576` | `float` | minutes; 0 means lap race | G | GLOBAL_SESSION | Y -> Y | Y | timed/lap session contract | T1/change | HEADER_VERIFIED |
| R140 | `mSessionAdditionalLaps @ 20580` | `int32` | timed-race extra complete laps | G | GLOBAL_SESSION | Y -> Y | Y | finish/replay completeness | T1/change | HEADER_VERIFIED |
| R141 | `mTyreTempLeft @ 20584` | `float[4]` | Celsius | V | TYRE | N -> Y | N | contact patch thermal/pressure analysis | T4-20Hz-P | HEADER_VERIFIED |
| R142 | `mTyreTempCenter @ 20600` | `float[4]` | Celsius | V | TYRE | N -> Y | N | contact patch thermal/pressure analysis | T4-20Hz-P | HEADER_VERIFIED |
| R143 | `mTyreTempRight @ 20616` | `float[4]` | Celsius | V | TYRE | N -> Y | N | contact patch thermal/pressure analysis | T4-20Hz-P | HEADER_VERIFIED |
| R144 | `mDrsState @ 20632` | `uint32 flags` | installed/rules/available/active | V | VEHICLE | N -> Y | N | straight-line/driver-aid context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R145 | `mRideHeight @ 20636` | `float[4]` | centimetres | V | PHYSICS | N -> Y | N | aero/suspension/kerb analysis | T4-20Hz-P | HEADER_VERIFIED |
| R146 | `mJoyPad0 @ 20652` | `uint32 mask` | raw button mask | V | INPUT | N -> N | N | no required analytics | NONE | EXCLUDED_PERSONAL_SENSITIVE + NO_KNOWN_SEMANTICS |
| R147 | `mDPad @ 20656` | `uint32 mask` | raw button mask | V | INPUT | N -> N | N | no required analytics | NONE | EXCLUDED_PERSONAL_SENSITIVE + NO_KNOWN_SEMANTICS |
| R148 | `mAntiLockSetting @ 20660` | `int32` | garage ABS setting; -1 unset | V | VEHICLE | N -> Y | N | aids/setup context | T1/change | HEADER_VERIFIED; valid under player control only |
| R149 | `mTractionControlSetting @ 20664` | `int32` | garage TC setting; -1 unset | V | VEHICLE | N -> Y | N | aids/setup context | T1/change | HEADER_VERIFIED; header comment mistakenly says ABS |
| R150 | `mErsDeploymentMode @ 20668` | `int32 enum` | ERS mode | V | VEHICLE | N -> Y | N | energy strategy/power context | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R151 | `mErsAutoModeEnabled @ 20672` | `bool` | auto ERS mode | V | VEHICLE | N -> Y | N | ERS control context | T2/change | HEADER_VERIFIED |
| R152 | `mClutchTemp @ 20676` | `float` | Kelvin; -273.16 unset | V | VEHICLE | N -> Y | N | launch/clutch health | T4-20Hz-P | HEADER_VERIFIED |
| R153 | `mClutchWear @ 20680` | `float` | 0..1 | V | DAMAGE | N -> Y | N | clutch health | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R154 | `mClutchOverheated @ 20684` | `bool` | degraded by heat | V | DAMAGE | N -> Y | N | performance/health event | T2/change | HEADER_VERIFIED |
| R155 | `mClutchSlipping @ 20685` | `bool` | clutch slipping | V | DAMAGE | N -> Y | N | launch/shift/health event | T2/change + T4-20Hz-P | HEADER_VERIFIED |
| R156 | `mYellowFlagState @ 20688` | `int32 enum` | FCY state | G | FLAG | Y -> Y | P | safety-car/FCY story | T2/change | HEADER_VERIFIED |
| R157 | `mSessionIsPrivate @ 20692` | `bool` | private-session flag | G | GLOBAL_SESSION | Y -> Y | Y | privacy/capture policy | T1/change | HEADER_VERIFIED |
| R158 | `mLaunchStage @ 20696` | `int32 enum` | launch-control stage | V | VEHICLE | N -> Y | N | race start/launch coaching | T2/change + T4-20Hz-P | HEADER_VERIFIED |

## Parser representation after P023

- `ParticipantSnapshot.WorldPosition`: `mParticipantInfo[n].mWorldPosition`.
- `ParticipantSnapshot.Orientation`: `mOrientations[n]`.
- `ParticipantSnapshot.SpeedMetresPerSecond`: `mSpeeds[n]`.
- `ParticipantSnapshot.NationalityRaw`: `mNationalities[n]`.
- `TelemetrySnapshot.SplitTime`, `TranslatedTrackLocation`, and `TranslatedTrackVariation`: the corresponding root values.
- `TelemetrySnapshot.ViewedVehicleTelemetry`: every header-useful viewed/root input, car-state, motion, tyre, damage and AMS2-extension field. Its four `TyreTelemetrySnapshot` entries preserve FL/FR/RL/RR array order.

The parser deliberately preserves raw enums/flags and raw physical units. It does not calculate blame, corner labels, G-force, normalized track coordinates, or coaching judgements.

## Exact layout checkpoints

| Checkpoint | Offset / size |
|---|---:|
| root `mParticipantInfo` | 28 |
| participant stride | 100 |
| participant `mWorldPosition` | 68 within slot |
| root unfiltered input start | 6428 |
| root car state start | 6816 |
| root motion start | 6908 |
| root tyre arrays start | 6992 |
| `mSequenceNumber` | 7320 |
| per-participant orientation array | 10032 |
| per-participant speed array | 10800 |
| PCars2 translated/additional block | 19248 |
| AMS2 additions start (`mSessionDuration`) | 20576 |
| `mYellowFlagState` (padding after two bools) | 20688 |
| `mLaunchStage` | 20696 |
| required mapped bytes | 20700 |

## Capture and privacy conclusions

1. `Upload` 열은 P023 이전 baseline이다. 현재 P023 candidate는 policy-selected world/replay/private-driver/incident facts를 별도 durable gzip stream과 upload queue로 보존한다. 161 useful field 전체를 매 sample에 그대로 보내는 방식은 아니다.
2. Public replay candidates are limited to participant position/progress/orientation/speed and discrete race facts. Detailed viewed/root controls, powertrain, tyre and physics values default to private driver analytics.
3. Root/viewed data must be rejected for personal coaching in replay, spectator, front-end, or unresolved states. 현재 resolver는 spectator remote-follow를 authoritative하게 식별하지 못하므로 private capture/release는 fail-closed 보강 전까지 blocker다.
4. Raw button masks are excluded. Steam ID, IP address, Windows username, tokens and credentials are not present in this header inventory and must never be added to telemetry samples.
5. 실제 short run의 persisted chunks는 changing world position, lap distance, speed, throttle, brake, unfiltered steering, RPM, gear and acceleration을 증명했다. 이는 source ownership이나 end-to-end completeness proof가 아니다. 다음 gate는 authoritative owner/fail-closed policy와 outer loss propagation을 먼저 닫고 clean full lap/multi-car/incident에서 heading/acceleration/tyre semantics와 replay geometry를 검증하는 것이다. 나머지 useful fields는 per-field evidence 없이 일괄 `REAL_VERIFIED`로 올리지 않는다.
