# Offline Telemetry Reprocessing Proof

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`

## 1. 판정

**Synthetic 60분 / 32대 archive에 대한 persisted-chunk-only 재처리는 PASS**다.

Reference renderer는 완료된 `.json.gz` telemetry chunk만 열어 다음 결과를 다시 만들었다.

- lap/event table
- position chart
- animated 2D participant replay
- speed graph
- brake / throttle / steering graph
- longitudinal/lateral acceleration 기반 g-force graph 판정
- driving line와 track centerline
- incident candidate animation

이 증거는 raw archive가 미래 renderer/analyzer의 입력으로 사용될 수 있음을 확인한다. 다만 입력이 synthetic fixture이므로 **실제 AMS2 Shared Memory 값의 정확성이나 운영 Server E2E를 GREEN으로 만들지는 않는다.**

## 2. evidence set

아래 경로는 모두 repository root (`outputs/AMS2KRLeague`) 기준 상대 경로다.

입력 archive:

```text
../AMS2League/evidence/future-telemetry/synthetic-60min-32car-v5-stream-capabilities-20260902/
```

출력 artifact:

```text
../AMS2League/evidence/future-telemetry/offline-proof-60min-32car-v5-stream-capabilities-20260902/
  proof-summary.json
  telemetry-proof.html
```

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `fixture-manifest.json` | 1,743 | `A26EE8534EF91FC3ABA8C9A9F4F9E864F6F98178A9F392A132EB81B772992EF4` |
| `proof-summary.json` | 618 | `FB84D53EC818B9218FB9262536562DD4D38A73DCAE38EDFF53FA2D9749D76F0E` |
| `telemetry-proof.html` | 13,520,732 | `C4CC1A88D74901322F5CB42923EF1C916BF43C7461541C47906231DF196D232D` |

fixture identity:

| Key | Value |
|---|---|
| session ID | `capture-4eb7459b53c4465b85f2ec4e51b41858` |
| session fingerprint | `fixture-60min-32car-v1` |
| witness ID | `witness-fixture-offline-proof-v1` |
| attempt ID | `attempt-7a0c06ae806746e9b4246dbdb8191ac6` |

## 3. 재현 명령

`outputs/AMS2KRLeague`를 current directory로 사용한다.

```powershell
dotnet build .\tools\AMS2TelemetryProof\AMS2TelemetryProof.csproj -c Release

dotnet run --project .\tools\AMS2TelemetryProof\AMS2TelemetryProof.csproj -c Release -- generate "<new-archive-root>" 60 32

dotnet run --project .\tools\AMS2TelemetryProof\AMS2TelemetryProof.csproj -c Release -- render "<new-archive-root>" "<new-proof-output-directory>"
```

기존 고정 evidence를 다시 읽는 render 명령은 다음과 같다. 재현 시 별도 output directory를 사용하면 고정 evidence hash를 보존할 수 있다.

```powershell
dotnet run --project .\tools\AMS2TelemetryProof\AMS2TelemetryProof.csproj -c Release -- render "..\AMS2League\evidence\future-telemetry\synthetic-60min-32car-v5-stream-capabilities-20260902" "<new-proof-output-directory>"
```

`generate`는 새 session/attempt identity를 발급하므로 exact identity/hash 재현 명령이 아니라 **동일 profile 재생성 명령**이다. 고정 evidence root에 다시 생성하면 여러 session이 섞일 수 있으므로 빈 root를 사용한다.

## 4. persisted-chunk-only 조건

renderer의 input loader는 archive root 아래 `*.json.gz`만 재귀 열거한다.

```text
Directory.EnumerateFiles(archiveRoot, "*.json.gz", AllDirectories)
  -> gzip decode
  -> ams2-telemetry-chunk-v1 JSON deserialize
  -> data.fields 이름으로 numeric row 해석
  -> derived proof model/HTML/summary 생성
```

이 실행 경로의 경계는 다음과 같다.

- AMS2 process/Shared Memory를 열지 않는다.
- live Client process나 overlay state를 읽지 않는다.
- `.upload.json` sidecar를 renderer input으로 사용하지 않는다.
- `fixture-manifest.json`의 결과 값을 renderer success로 복사하지 않는다.
- Server API, MariaDB, network를 호출하지 않는다.
- persisted `SESSION_METADATA.records`, `RACE_STORY`, `PARTICIPANT_REPLAY`, `DRIVER_TELEMETRY`, `INCIDENT_TRACE`를 독립적으로 해석한다.

`proof-summary.json`에도 이를 다음처럼 고정했다.

```json
{
  "inputSource": "PERSISTED_GZIP_CHUNKS_ONLY",
  "sharedMemoryRead": false
}
```

HTML 크기를 제한하기 위해 renderer는 replay를 2,000 ms 간격, driver trace를 250 ms 간격으로 표시용 downsample한다. 이는 원본 archive의 5 Hz replay / 20 Hz driver chunk를 변경하지 않는다.

## 5. 입력 chunk inventory

| Stream | Chunks | Archive samples | Renderer에서 사용하는 핵심 facts |
|---|---:|---:|---|
| `SESSION_METADATA` | 1 | 1 | session/track/participant metadata |
| `RACE_STORY` | 42 | 45 | event type, participant, lap, lap time |
| `PARTICIPANT_REPLAY` | 120 | 576,000 | elapsed, participant, position, world X/Z, lap distance |
| `DRIVER_TELEMETRY` | 120 | 72,000 | lap/distance/world X/Z, speed, controls, acceleration |
| `INCIDENT_TRACE` | 1 | 484 | relative time, participant, world X/Z |
| **Total** | **284** | **648,530** | five joined streams |

`proof-summary.json`의 chunk total과 stream별 count가 위 inventory와 일치한다.

## 6. 결과 matrix

| Derived output | Primary persisted source | 최소 검증 근거 | 결과 |
|---|---|---|---|
| Lap times / event table | `RACE_STORY`, driver lap fallback | `LAP_COMPLETE` 존재 또는 둘 이상의 lap | PASS |
| Position chart | `PARTICIPANT_REPLAY` | 둘 이상의 participant와 충분한 position samples | PASS |
| Animated 2D replay | `PARTICIPANT_REPLAY` | world X/Z가 있는 multi-participant timeline | PASS |
| Speed graph | `DRIVER_TELEMETRY` | 충분한 samples와 0보다 큰 speed | PASS |
| Brake graph | `DRIVER_TELEMETRY` | brake > 0.1 관측 | PASS |
| Throttle graph | `DRIVER_TELEMETRY` | throttle > 0.1 관측 | PASS |
| Steering graph | `DRIVER_TELEMETRY` | absolute steering > 0.1 관측 | PASS |
| G-force graph capability | `DRIVER_TELEMETRY` | absolute lateral acceleration > 0.1 관측 | PASS |
| Driving line | `DRIVER_TELEMETRY` | 둘 이상의 lap과 non-zero world coordinate | PASS |
| Track centerline | `DRIVER_TELEMETRY` | driving-line coordinate sequence로 복원 | PASS |
| Incident animation | `INCIDENT_TRACE` | 둘 이상의 participant와 충분한 burst samples | PASS |

기록된 summary 원문 값:

```text
lapTimes=PASS
positionChart=PASS
replay2D=PASS
speedGraph=PASS
brakeGraph=PASS
throttleGraph=PASS
steeringGraph=PASS
gForceGraph=PASS
drivingLine=PASS
trackCenterline=PASS
incidentAnimation=PASS
```

CLI의 `FINAL=PASS`는 필수 proof인 lap times, position chart, 2D replay, speed, brake, throttle, driving line, incident animation이 모두 PASS일 때만 출력된다. 이번 evidence는 추가 steering/g-force/centerline 항목도 PASS다.

## 7. 무엇을 증명했는가

1. 다섯 stream의 완료된 gzip chunk 284개만으로 session을 다시 열 수 있다.
2. replay에 `WorldPosition` 계열 좌표와 `LapDistance`를 함께 보존하므로 position chart와 2D trajectory를 서로 독립적으로 만들 수 있다.
3. private driver stream으로 speed/input/acceleration graph와 multi-lap driving line을 만들 수 있다.
4. incident burst의 상대 시간과 관련 participant 좌표로 사고 전후 animation을 재생할 수 있다.
5. capture 시점의 overlay UI나 Server normalized row가 없어도 derived output을 다시 만들 수 있다.
6. raw chunk를 immutable source로 보존하면 향후 analyzer가 같은 schema decoder를 통해 새로운 파생 결과를 계산할 수 있다.

## 8. 증명하지 않은 것

- 실제 AMS2에서 world position, lap distance, speed, throttle, brake, steering, RPM, gear, acceleration 값이 변화했다는 증거
- 실제 track geometry와 2D replay 선의 지리적 정확도
- 실제 multiplayer 32대에서 5/20 Hz cadence, drop, CPU/RAM/disk latency
- Server upload/auth/idempotency/raw retention/DB normalization
- 여러 witness의 session identity merge 또는 canonical replay 생성
- private driver telemetry의 운영 authorization/visibility enforcement
- 실제 사고 감지 recall/precision, 과실 판정이나 coaching 해석의 정확도
- schema migration 후 장기 호환성

따라서 판정은 다음처럼 분리한다.

| Scope | Verdict |
|---|---|
| Synthetic persisted chunk readability | GREEN |
| Offline derived-output feasibility | GREEN |
| Real AMS2 telemetry correctness | 별도 `REAL_AMS2_VALIDATION.md` 필요 |
| Production Server E2E | 별도 검증 필요 |
| Long-term migration compatibility | v1 decoder/fixture regression 유지 필요 |

## 9. 향후 regression 기준

- `ams2-telemetry-chunk-v1` reader 또는 명시적 migration layer를 유지한다.
- 고정 evidence의 284 chunk inventory와 proof summary를 regression fixture로 보존한다.
- renderer는 live SHM/client fallback을 추가하지 않는다. raw field가 없으면 명시적으로 FAIL/UNAVAILABLE 처리한다.
- 새 schema version은 기존 fixture를 읽는 historical reprocessing test와 함께 도입한다.
- 실제 AMS2 capture가 확보되면 동일 renderer로 별도 real-evidence proof를 만들고 synthetic 결과와 구분한다.

현재 결론은 **offline architecture proof GREEN, real-world validation은 이 문서 범위 밖**이다.
