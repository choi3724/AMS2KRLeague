# Future Web Capability Matrix

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`

## 1. 판정 읽는 법

이 표의 `CURRENTLY CAPTURED?`는 P023 이전 v0.2.2 **공개 운영 payload**를 뜻한다. `AFTER THIS PHASE?`는 현재 working tree의 P023 candidate가 모든 실제 release gate를 통과했을 때의 계약이다. candidate 구현 완료와 production release는 구분한다.

1. Runtime `TelemetrySnapshot` → archive DTO adapter 연결 — **PASS**
2. 실제 AMS2에서 required core signal 변화 — **PASS, 84.986초 1인 Practice**
3. gzip/HTTPS/Server raw archive 계약 — **자동·local Server PASS**, real captured chunk staging round trip은 PENDING
4. stored raw data만 사용하는 offline reprocessing — **synthetic 60분/32대 PASS**, real short run graph subset PASS

현재 candidate는 layout/parser/runtime/local archive/uploader/local Server raw archive까지 구현됐다. 161 useful inventory row 중 scalar/array leaf 160/160은 raw gzip까지 보존되며, 나머지 `R008`은 participant array container다. 아래 `YES (GREEN 후)`는 “해당 기능에 필요한 raw field와 재처리 경로가 구현됐다”는 뜻이며, production에 이미 공개됐다는 뜻은 아니다. 실제 full lap/multi-car/incident/staging E2E와 아래 tier-placement 정책 gap이 남아 전체 release verdict는 YELLOW다.

현재 raw 손실은 없지만 stream 배치가 최종 정책과 완전히 같지는 않다. T1로 분류된 private/static leaf 9개는 public metadata가 아니라 private T4에만 있고, T5 후보 17개 중 root-private 7개는 public incident burst가 아니라 private T4 time-range join으로만 조회한다. T2 minimum detector 25개는 모두 있으나 모든 change-candidate field의 generic old/new journal은 없다. 이는 개인정보 경계를 지키기 위한 부분도 있지만, P023 GREEN 전에는 정책을 승인하거나 계약을 보강해야 한다.

## 2. 필수 matrix

| FEATURE | REQUIRED DATA | CURRENTLY CAPTURED? | AFTER THIS PHASE? | SERVER CAN REPROCESS? | CLIENT UPDATE NEEDED? |
|---|---|---|---|---|---|
| Race Timeline | `RACE_STORY.sessionElapsedMs`, `capturedAtUnixMs`, `eventTypeRef`, lap/sector/location, raw race/pit/flag/penalty/result state | PARTIAL — 기존 activity/witness/result에는 일부 lifecycle/result fact만 있고 독립 durable event stream은 없음 | YES (GREEN 후) — 25개 minimum detected fact 계약 | YES — raw events를 detector/analyzer version별로 다시 정렬·해석 | NO — 정의된 timeline 범위 |
| Position Graph | `PARTICIPANT_REPLAY.sessionElapsedMs`, `participantRef`, `lap`, `lapDistanceMeters`, `racePosition`, attempt identity | NO — dense all-participant history 없음 | YES (GREEN 후), 5 Hz | YES — position-vs-time/lap을 raw rows에서 재계산 | NO |
| 2D Replay | replay `worldX`, `worldY`, `worldZ`, `lapDistanceMeters`, `headingRadians`, `speedMetersPerSecond`, participant identity | NO | YES (GREEN 후), 5 Hz source + browser interpolation | YES — geometry/centerline/interpolated frames는 Server/Web 산출 | NO — exact source acceleration vector는 기본 2D replay에 불필요 |
| Incident Animation | `INCIDENT_TRACE.relativeTimeMs`, X/Y/Z, heading, speed, race/pit/flag state, related participant, trigger | NO | YES (GREEN 후), -3 s~+3 s 20 Hz candidate burst | YES — 후보 animation과 proximity를 raw trace에서 재생성 | NO — fault/blame 판정은 범위 밖 |
| Driver Lap Table | metadata track/layout/vehicle/class, story `LAP_COMPLETE`, `lapTimeMs`, driver `lap`, `lapValid`, `currentLapTimeMs` | PARTIAL — 결과/best-lap facts는 있으나 lap별 high-rate archive와 완전한 validity 연결은 없음 | YES (GREEN 후) | YES — clean/invalid lap table과 lap range 재생성 | NO |
| Fastest Lap | story `SESSION_FASTEST_LAP`, `participantRef`, `lap`, `lapTimeMs`; complete lap facts | PARTIAL — session witness/result에 best-lap snapshot 가능, event history는 제한 | YES (GREEN 후) | YES — valid lap events를 다시 비교 | NO |
| Speed Graph | driver `sessionElapsedMs`, `lap`, `lapDistanceMeters`, `speedMetersPerSecond`, `lapValid` | NO | YES (GREEN 후), private 20 Hz | YES | NO |
| Throttle Graph | driver `lapDistanceMeters`, `throttle`, `unfilteredThrottle`, time/lap/validity | NO | YES (GREEN 후), private 20 Hz | YES — filtered/raw demand를 구분 | NO |
| Brake Graph | driver `lapDistanceMeters`, `brake`, `unfilteredBrake`, speed, time/lap/validity | NO | YES (GREEN 후), private 20 Hz | YES | NO |
| Steering Graph | driver `lapDistanceMeters`, `steering`, `unfilteredSteering`, speed, heading | NO | YES (GREEN 후), private 20 Hz | YES | NO |
| G-force Graph | driver longitudinal/lateral/vertical acceleration + time/lap/distance | NO | YES **only after axis/unit real validation**; otherwise phase remains PARTIAL | YES — verified m/s²를 `/9.80665`로 G 변환 | NO after GREEN; YES if acceleration semantics cannot be validated and schema must change |
| Driving Line | driver/replay `worldX/Y/Z`, `lapDistanceMeters`, `lap`, `lapValid`, track/layout | NO | YES (GREEN 후) | YES — clean-lap polylines와 normalized track coordinates 재생성 | NO |
| Lap Comparison | private driver time/distance, world position, speed, controls, gear/RPM, validity + metadata identity | NO | YES (GREEN 후) | YES — distance resampling/delta/trace overlay | NO; reference-lap access/consent is Server policy |
| Braking Point | `lapDistanceMeters`, `unfilteredBrake`/`brake`, speed, lap validity | NO | YES (GREEN 후) | YES — threshold/hysteresis 알고리즘을 바꿔 재산출 | NO |
| Corner Minimum Speed | distance, speed, steering, acceleration, world line, validity | NO | YES (GREEN 후) | YES — corner zone을 geometry/speed/steering에서 검출 | NO; 공식 corner 이름/번호는 Server track metadata가 필요 |
| Exit Speed | distance, speed, throttle, steering, detected corner boundary | NO | YES (GREEN 후) | YES | NO |
| Consistency | valid lap times, distance-normalized speed/control traces, weather/tyre context | PARTIAL — 일부 lap/result facts만 존재 | YES (GREEN 후) | YES — lap-time 및 channel variance 재계산 | NO |
| Line Variance | clean-lap X/Y/Z + lap distance + track/layout/vehicle identity | NO | YES (GREEN 후) | YES — resampling/coordinate transform을 바꿔 재산출 | NO |
| Telemetry Comparison | two consented private driver streams; common track/layout/vehicle/class/lap/distance schema | NO | YES (GREEN 후) | YES — raw chunks를 common distance grid에 resample | NO; sharing/owner authorization is Server work |
| AI Race Coach | braking point, minimum/exit speed, throttle-on, steering corrections, consistency, line variance + context | NO | YES for the defined coaching metrics after GREEN; AI text itself is future Server/Web | YES — facts/derived metrics를 model version별로 재처리 | NO for listed metrics; novel future signal outside this schema is an explicit exception |

## 3. `CLIENT UPDATE NEEDED = NO`의 정확한 의미

`NO`는 다음 future work가 Client update 없이 가능하다는 뜻이다.

- raw gzip 보존분을 새 analyzer version으로 재처리
- position chart/track geometry/interpolation algorithm 변경
- distance/time resampling과 lap delta 계산
- braking/corner/consistency/line metric threshold 변경
- Web graph/replay UI 추가
- owner가 동의한 private telemetry 비교
- 위 metric을 입력으로 한 AI Coach 설명 생성

다음은 `NO`에 포함되지 않는다.

- P023의 남은 실제 full-lap/multi-car/incident/staging Server release gate를 끝내는 작업
- SHM에 없는 authoritative corner name, steward decision, fault/blame 생성
- 미래에 새 AMS2 SHM version 또는 현재 schema 밖의 signal을 요구하는 기능
- source가 미지원인 old v0.2.1/v0.2.2 session에 과거부터 없던 telemetry를 소급 생성
- T1/T5 privacy placement 또는 T2 generic journal의 최종 정책을 바꾸어 새로운 public/cadence 계약을 요구하는 기능

## 4. 원본 보존과 재처리 경계

Server가 reprocess할 수 있으려면 MariaDB에 row-per-sample만 남기는 방식이 아니라 다음을 보존해야 한다.

- canonical raw gzip chunk
- `schema`, stream/visibility, session/fingerprint/witness/attempt identity
- elapsed/lap range, chunk index, hashes, byte sizes
- field array와 string dictionaries
- quality/drop/completeness metadata

Server-derived centerline, corner, classification, graph point, coach conclusion은 cache/index일 수 있지만 raw를 대체하지 않는다. Client는 공식 분류, track image, corner label, 사고 책임을 payload에 hard-code하지 않는다.

## 5. privacy가 capability에 미치는 영향

Race timeline/replay/incident raw는 `PUBLIC_REPLAY` candidate다. throttle/brake/steering/physics/tyre를 포함한 driver stream은 `PRIVATE_DRIVER_ANALYTICS`다. private access가 거부되면 Web feature가 데이터 부족이 아니라 권한 부족으로 표시되어야 한다. 공개 비교에는 owner 동의 또는 명시적 Server policy가 필요하며, access policy 변경에 Client update는 필요하지 않다.

## 6. required live evidence 상태

| signal | parser/header | fixture row | REAL_RUNTIME_VERIFIED |
|---|---|---|---|
| World position | HEADER_VERIFIED | YES | PASS — persisted real XYZ 모두 변화 |
| Lap distance | HEADER_VERIFIED | YES | PASS for changing source; full-lap progression PENDING |
| Participant heading/speed | HEADER_VERIFIED | YES, additive replay/incident fields | PARTIAL — real 1-car short-run source 변화, heading convention/multi-car PENDING |
| Local speed | HEADER_VERIFIED | YES | PASS — 0.002252..2.843291 m/s |
| Throttle / Brake / Steering | HEADER_VERIFIED | YES, filtered + unfiltered | PASS for control sources — throttle/brake 0..1, unfiltered steering -1..1; filtered steering는 고정 0 |
| RPM / Gear | HEADER_VERIFIED | YES | PASS — RPM 1,132..7,365, gear -1/0/1 |
| Acceleration axes/units | HEADER_VERIFIED with header ambiguity | YES | PARTIAL — 세 axis 실제 변화와 G-force graph PASS, axis/unit 정상주행 확정 PENDING |

현재 raw-data contract 기준으로 표에 정의된 Web 기능은 `FUTURE WEB REQUIRES CLIENT UPDATE: NO (조건부)`다. stored-only synthetic full renderer는 11/11 PASS이고 real short run은 speed/brake/throttle/steering/G-force graph가 PASS했다. 다만 actual full-race evidence, production deployment, T1/T5 placement 승인과 T2 generic journal 결정이 없어 제품 release 판정은 YELLOW, `COACHING READY`는 clean multi-lap/semantic gate가 끝날 때까지 PARTIAL이다. 예외는 novel/out-of-schema signal, 새 SHM version, SHM이 제공하지 않는 authoritative semantics와 향후 public/cadence 정책 변경이다.
