# 리플레이 데이터 전송 충분성 · cadence별 전송량 실측 보고서

작성일: 2026-09-05 KST (전송량 실측 추가 갱신)
작성: Claude Code
기준 Client: `v0.2.3-beta.3` (HEAD `9303e33`) 작업 트리
분석 입력:
- Cafe24에서 GET으로 내려받은 실제 멀티플레이 2회분 서버 저장 원본 (`work/beta2-e2e/server-only`, 72 chunk)
- P024 크기 gate에 쓰인 60분/32대 P023 baseline 5 Hz 리플레이 chunk 120개 (`work/p024/p023-baseline-60m32/.../participant_replay`, 576,000 rows)

분석 도구(`.gitignore`의 `work/` 아래):
- `work/replay-cadence-audit` → 서버 원본의 샘플 밀도 실측 (`replay-cadence-audit.json`)
- `work/replay-cadence-cost` → cadence별 wire 바이트 실측 (`replay-cadence-cost.json`)

## 1. 결론

```text
전송·저장 무결성:      PASS   (업로드 손실 0, 해시 일치, PARTICIPANT_REPLAY cadence miss 0)
순위/랩/피트 타임라인:  충분   (참가자당 최소 2초, 순위·피트 변화 시 즉시, 근접 배틀 시 0.5초)
2D 트랙 리플레이:       불충분 (월드 좌표는 스타트 10초 이후 참가자당 5초에 1회, 샘플 간 이동 거리 중앙값 235 m)
원인:                   전송 데이터 손실이 아니라 Compact 변환 단계의 의도된 downsampling
world 500 ms 전환 비용: 60분/32대 fixture 리플레이 wire +558,222 B (+184.6 %), 실제 리그 세션 약 +53 %
```

"리플레이 데이터가 서버까지 잘 도착하는가"는 beta.2 E2E에서 72/72 chunk 해시 일치로 확인되었고, 이번 실측에서도 `PARTICIPANT_REPLAY` 스트림은 8개 attempt 모두 `cadenceMissedSamples=0`이었다. 손실은 `DRIVER_TELEMETRY`(비공개, 서버 미전송)와 `INCIDENT_TRACE`에서만 발생했다.

그러나 "서버에 도착한 데이터가 리플레이를 만들기에 충분한가"는 용도에 따라 답이 다르다. 순위 변화, 랩, 피트, Race Story 재구성은 충분하지만, F1 중계식 2D 트랙 맵 리플레이를 만들기에는 월드 좌표 밀도가 부족하다.

## 2. 데이터 흐름과 downsampling 위치

```text
SHM 30 Hz
  → FutureTelemetrySnapshotAdapter (모든 참가자 frame)
  → LocalDurableTelemetryArchive.ProcessFrame: 5 Hz gate (ReplayIntervalMs=200)   ← 손실 0
  → TelemetryChunkAccumulator.AddReplay: 30초 chunk, 5 Hz × 참가자 수 rows (36 필드)
  → CompactTelemetryChunkStore.BuildReplayArtifacts                              ← 여기서 downsample
      progress 필드 (lap, lapDistance, racePosition, raceState, pitState, isActive): ReplayProgressIntervalMs (2,000)
      world 필드 (worldX/Y/Z, heading, speed):                                     ReplayWorldIntervalMs (계약 5,000)
      extension 필드 (섹터·베스트·오리엔테이션 등):                               ReplayExtensionIntervalMs (20,000)
      close-battle (순위 인접 + 20 m 이내) 참가자 progress:                        ReplayBattleIntervalMs (500)
      순위/피트 상태 변화 참가자 progress:                                          즉시
      start burst (elapsed < 10 s):                                                5 Hz 전부
  → PARTICIPANT_REPLAY_V1 (A2CT gzip) → Cafe24 canonical .a2ct.gz
```

이 정책은 `docs/COMPACT_SCHEMA_REGISTRY.md`와 `docs/P024_RELEASE_GATE_REPORT.md`의 Replay 행에 문서화된 설계다. 2026-09-05 작업으로 네 상수를 `TelemetryArchiveOptions`의 `ReplayProgressIntervalMs`, `ReplayWorldIntervalMs`, `ReplayExtensionIntervalMs`, `ReplayBattleIntervalMs`로 옵션화했고 `CompactTelemetryChunkStore(root, identity, options)` 생성자와 `FutureTelemetryCaptureRuntime`의 archive factory가 이를 전달한다. 범위 검증은 `ReplayIntervalMs`(200) 이상이며, 단위 테스트 "Compact replay world cadence option changes world row density only"가 5,000/500 ms에서 world row 수(3대×30초: 162 vs 270)와 progress row 불변, 200 ms 미만 거부를 고정한다.

inner archive에는 5 Hz 위치가 모두 있었지만 서버로 보낸 것은 5초당 1회이며, inner 5 Hz JSON chunk는 Compact 변환 후 로컬에도 남지 않는다.

## 3. 서버 원본 실측 (샘플 밀도)

세션 2 Race attempt(`capture-93172740b5da4fa5ba2719cc98e498ae`, 255.0 s, 참가자 15명):

| 항목 | 값 |
|---|---:|
| replay rows | 6,409 |
| world 좌표 포함 rows | 1,397 (약 절반은 스타트 10초 burst) |
| 참가자당 world 샘플 빈도 | 0.365 /s (전체), 스타트 이후 **0.2 /s** |
| world 샘플 간격 (스타트 이후) | median 4,997 ms / p95 5,023 ms / max 5,029 ms |
| world 샘플 간 이동 거리 (스타트 이후) | median **235.1 m** / p95 452.3 m / max 471.6 m |
| progress 샘플 간격 | median 419 ms / p95 1,643 ms / max 2,026 ms |
| track geometry rows | 288 (20 m bin) |
| replay gzip | 22,509 B (총 143,147 B의 16 %) |
| incident gzip | 111,841 B (총의 78 %) |

| capture | span s | 참가자 | world/참가자/s | world 간격 median (스타트 후) | 이동거리 median / p95 m | progress 간격 median |
|---|---:|---:|---:|---:|---:|---:|
| 48e5… (S1 practice 진입) | 122.4 | 14 | 0.596 | 5,005 ms | 28.4 / 265.5 | 433 ms |
| b212… (S1 qualifying) | 759.4 | 15 | 0.240 | 4,994 ms | 166.5 / 334.2 | 1,007 ms |
| 7794… (S1 race 초반) | 84.6 | 13 | 0.768 | 5,005 ms | 190.0 / 322.0 | 403 ms |
| cb27… (S2 practice) | 315.0 | 14 | 0.356 | 4,997 ms | 209.6 / 448.1 | 992 ms |
| 5c1e… (S2 qualifying) | 519.0 | 14 | 0.293 | 4,997 ms | 180.0 / 443.0 | 992 ms |
| 9317… (S2 race) | 255.0 | 15 | 0.365 | 4,997 ms | 235.1 / 452.3 | 419 ms |

## 4. 용도별 판정

| 서버/웹 용도 | 필요한 것 | 현재(5,000 ms) 데이터 | 판정 |
|---|---|---|---|
| 순위 타임라인, 포지션 차트 | racePosition 변화 시점 | 변화 즉시 + 2초 base | 충분 |
| 랩/섹터/피트 타임라인 | lap, pitState, 섹터 타임 | 변화 즉시 / 20초 extension | 충분 |
| Race Story, 사고 후보 | RACE_EVENT, INCIDENT | 별도 스트림 | 충분 |
| 트랙 진행률 기반 간이 맵 | lapDistance + geometry | 2초 base(배틀 0.5초) + 20 m geometry | 보통 |
| F1 중계식 2D 리플레이 (실제 궤적) | worldX/Z 최소 2 Hz | 0.2 Hz, 235 m 간격 | **불충분** |
| 다중 witness 위치 대조 | 동일 시각 world 좌표 | 5초 격자 | 불충분 |

## 5. cadence별 클라이언트 전송량 실측

### 5.1 방법

P023 baseline의 5 Hz `PARTICIPANT_REPLAY` chunk 120개(30초, 32대, 576,000 rows)를 제품 변환 코드 `CompactTelemetryChunkStore`에 설정별 `TelemetryArchiveOptions`로 다시 통과시키고, 생성된 `.a2ct.gz`(리플레이 스트림)의 바이트를 합산했다. 추정이 아니라 실제 인코딩 결과다. track geometry는 1,878 B로 설정과 무관했다.

### 5.2 60분/32대 fixture 결과 (리플레이 스트림)

| 설정 progress / world ms | world rows | replay wire B | Δ B | Δ % | B/대·분 |
|---|---:|---:|---:|---:|---:|
| **2,000 / 5,000 (beta.3 계약)** | 24,576 | **302,317** | 0 | 0 | 157.5 |
| 2,000 / 2,000 | 59,040 | 404,738 | +102,421 | +33.9 | 210.8 |
| 2,000 / 1,000 | 116,480 | 573,845 | +271,528 | +89.8 | 298.9 |
| **2,000 / 500** | 231,360 | **860,539** | **+558,222** | **+184.6** | 448.2 |
| 2,000 / 200 (world 5 Hz 전부) | 576,000 | 1,669,686 | +1,367,369 | +452.3 | 869.7 |
| 1,000 / 1,000 | 116,480 | 571,678 | +269,361 | +89.1 | 297.8 |
| 500 / 500 | 231,360 | 855,426 | +553,109 | +183.0 | 445.6 |
| 200 / 200 (5 Hz 전부) | 576,000 | 1,672,467 | +1,370,150 | +453.2 | 871.1 |

fixture에서 world row 하나를 더 보낼 때의 한계 비용은 2.5~3.0 B(gzip)다. progress cadence는 world와 같은 값으로 맞추면 행이 합쳐져 오히려 조금 줄어든다(500/500이 2,000/500보다 5 KB 작음).

참고: 공식 P024 proof(`tools/AMS2CompactProof`)는 자체 선택 로직과 300초 프레임으로 리플레이 216,971 B를 기록했다. 제품 경로(30초 chunk, battle/변화 행 포함)는 같은 데이터에서 302,317 B를 만든다. 즉 465,279 B gate는 제품 packing 기준으로는 리플레이를 약 85 KB 적게 잡고 있다. 아래 gate 대비 수치는 이 점을 감안해 Δ만 더한 값이다.

### 5.3 P024 크기 gate와 공개 업로드량

공식 총량 465,279 B에는 비공개 `DRIVER_*` 4개 스트림 233,690 B가 포함되며 이는 현재 서버로 전송되지 않는다(`LOCAL_PENDING_OWNER`). 실제 공개 업로드 baseline은 약 231,589 B다.

| world ms | gate 합계 (465,279 + Δ) | 512 KiB 제품 목표 | 1 MiB 한계 | 공개 업로드 (231,589 + Δ) |
|---:|---:|---|---|---:|
| 5,000 | 465,279 | PASS | PASS | 231,589 |
| 2,000 | 567,700 | FAIL (+43 KB 초과) | PASS | 334,010 |
| 1,000 | 736,807 | FAIL | PASS | 503,117 |
| 500 | 1,023,501 | FAIL | PASS (25 KB 여유) | 789,811 |
| 200 | 1,832,648 | FAIL | FAIL | 1,598,958 |

### 5.4 실제 데이터 한계 비용과 세션 단위 증가량

서버 원본 리플레이 chunk를 world 행 없이 다시 인코딩해 실제 world row 하나의 gzip 비용을 구했다.

| capture | 유형 | span s | B / world row |
|---|---|---:|---:|
| 7794… | S1 race 초반 | 84.6 | 2.32 |
| 48e5… | S1 practice 진입 | 122.4 | 3.28 |
| 9317… | S2 race | 255.0 | **4.43** |
| cb27… | S2 practice | 315.0 | 6.27 |
| 5c1e… | S2 qualifying | 519.0 | 9.04 |
| b212… | S1 qualifying | 759.4 | 9.07 |

실제 주행은 fixture보다 불규칙해 1.5~3배 비싸고, 특히 qualifying(피트 정차와 아웃랩이 섞여 delta 압축이 나쁨)이 가장 비싸다. 각 capture의 실측 B/row와 world row 증가분(스타트 10초 이후 참가자 × 초 × cadence)으로 계산한 증가량:

| world ms | S2 practice (315 s, 14대, 80.7 KB) | S2 qualifying (519 s, 14대, 143.0 KB) | S2 race (255 s, 15대, 143.1 KB) | **S2 세션 합계 (366.8 KB)** |
|---:|---:|---:|---:|---:|
| 2,000 | +7.9 KB | +19.3 KB | +5.3 KB | **+32.5 KB (+9 %)** |
| 1,000 | +21.3 KB | +51.5 KB | +13.4 KB | **+86.2 KB (+24 %)** |
| 500 | +48.1 KB | +115.9 KB | +29.7 KB | **+193.7 KB (+53 %)** |
| 200 | +128.4 KB | +309.2 KB | +78.5 KB | **+516 KB (+141 %)** |

Race만 보면 500 ms 전환은 attempt당 +21 %, 대·분당 약 470 B다. 60분/32대 레이스를 실제 race 비용(4.43 B/row)으로 환산하면 world 500 ms는 약 +916 KB, 1,000 ms는 +407 KB, 2,000 ms는 +153 KB, 200 ms는 +2.44 MB다.

### 5.5 해석

- 500 ms 전환은 fixture 리플레이 wire를 2.85배(+558 KB)로 늘리고 P024 512 KiB 제품 목표를 넘긴다. 1 MiB 절대 한계 아래에는 남는다.
- 실제 리그 세션(연습+예선+레이스) 기준 총 공개 업로드는 약 +53 % 늘어난다. 증가분의 60 %가 qualifying에서 나온다.
- Cafe24 저장은 파일시스템 gzip이라 세션당 +0.2 MB 수준이며 1.4 GB quota의 0.01 %대다. 병목은 저장이 아니라 gate 정책과 업로드 총량 합의다.
- 비용을 줄이면서 2D 리플레이를 얻으려면 (a) Race 세션에서만 500 ms를 적용하고 Practice/Qualifying은 5,000 ms 유지(세션 합계 +8 % 수준), (b) 1,000 ms 절충(+24 %, 직선 60~70 m 간격), (c) 5,000 ms 유지 중 하나를 고르면 된다. (a)는 `FutureTelemetryCaptureRuntime`에서 세션 타입에 따라 `ReplayWorldIntervalMs`를 바꿔 archive를 만들면 되지만 attempt 중간 변경은 chunk store가 attempt 단위로 생성되므로 세션 전환 시점에만 반영된다.

## 6. 작업 트리 기본값에 대한 주의

갱신(같은 날 이후): 사용자가 위 비용을 보고 0.3.0 커밋·Latest 릴리스를 지시했으므로 world 500 ms 기본값은 0.3.0에 포함되었다. 아래 문단은 그 결정 이전의 기록이다.

이 세션은 옵션화만 하고 기본값을 계약값 5,000 ms로 두었다. 그러나 2026-09-05 13:29 KST에 이 세션 밖에서 `TelemetryArchiveOptions.DefaultReplayWorldIntervalMs`가 **500**으로 바뀌고 "사용자 결정"이라는 주석이 추가되었다. 이 대화에서 그 결정은 확인되지 않았으므로 되돌리지 않고 그대로 두었다. 현재 작업 트리를 빌드·배포하면 클라이언트는 world 500 ms로 업로드한다. 위 5.3/5.4의 비용을 보고 유지·되돌림·절충을 결정해야 하며, 유지할 경우 `tools/AMS2CompactProof`의 `WriteReplay` 선택 로직(5,000 ms 고정)을 제품 경로와 맞추고 P024 gate를 다시 승인해야 한다.

## 7. 부수 관찰

- `INCIDENT_TRACE`가 실제 세션 wire의 대부분(S2 race 78 %)을 차지한다. 리플레이 밀도를 올리기 전에 incident burst trigger 빈도를 점검하면 총량을 상쇄할 여지가 있다.
- Track geometry는 5 Hz row에서 20 m bin으로 만들어지므로 밀도가 충분하고 cadence와 무관하다.
- 재실행:

```powershell
cd <repo>
.\work\dotnet8\dotnet.exe run --project .\work\replay-cadence-audit\replay-cadence-audit.csproj -c Release -- .\work\beta2-e2e\server-only .\work\replay-cadence-audit\replay-cadence-audit.json
.\work\dotnet8\dotnet.exe run --project .\work\replay-cadence-cost\replay-cadence-cost.csproj -c Release -- .\work\p024\p023-baseline-60m32\sessions\df32057030a86a7324152a1f6d17c3cf\chunks\participant_replay .\work\beta2-e2e\server-only .\work\replay-cadence-cost\replay-cadence-cost.json
```

## 8. 요약

```text
REPLAY UPLOAD / STORAGE:     PASS (loss 0, hash match, cadence miss 0)
REPLAY CONTENT FOR TIMELINE: SUFFICIENT
REPLAY CONTENT FOR 2D MAP:   INSUFFICIENT at 5,000 ms (world 0.2 Hz, 235 m median gap)
WORLD 500 ms COST (fixture): replay wire 302,317 -> 860,539 B (+184.6 %), gate 465,279 -> 1,023,501 B (512 KiB target FAIL, 1 MiB PASS)
WORLD 500 ms COST (real):    S2 session +193.7 KB (+53 %), race attempt +21 %, ~470 B per car-minute
WORLD 1,000 ms COST:         fixture +271,528 B (+89.8 %), real session +24 %
WORLD 2,000 ms COST:         fixture +102,421 B (+33.9 %), real session +9 %
CODE CHANGED:                cadence constants -> TelemetryArchiveOptions (4 options), store/runtime plumbing, 1 unit test
DEFAULT IN WORKING TREE:     500 ms, changed outside this session, user confirmation required
```
