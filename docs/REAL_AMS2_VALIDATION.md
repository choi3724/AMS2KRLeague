# Real AMS2 Telemetry Validation

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`

이 문서의 evidence 경로는 모두 repository root (`outputs/AMS2KRLeague`) 기준 상대 경로다.

## 1. 판정

**실제 AMS2 Shared Memory v14 → Runtime adapter → local durable gzip archive의 필수 동적 field 검증은 PASS다.**

다만 이 실행은 약 85초의 1인 Practice/Test Day이며 완주 lap, multi-car position 변화, 실제 incident candidate를 포함하지 않는다. 따라서 실제 AMS2 전체 세션 replay/coaching 검증은 `PARTIAL`, P023 전체 release gate는 이 문서만으로 GREEN이 아니다.

| 검증 범위 | 판정 | 근거 |
|---|---|---|
| SHM v14 attach/layout | PASS | game build `3398`, SHM `14`, parser `AMS2_SHM_V14` |
| Runtime snapshot adapter | PASS | 실제 snapshot 1,698 batch가 Future Telemetry runtime에 전달됨 |
| 필수 동적 field 변화 | PASS | persisted gzip만 읽은 `real-field-validation.json`의 required checks 전체 true |
| Local durable chunk/gzip/hash | PASS | 10/10 gzip와 sidecar 무결성 검증, integrity failure 0 |
| Clean shutdown/final flush | PASS | `CLIENT_STOP clean=true`, committed chunks 10, background failure 0 |
| 실제 full lap | NOT EXERCISED | lap 1의 짧은 이동만 존재 |
| 실제 all-participant replay | NOT EXERCISED | participant 1명 |
| 실제 incident burst | NOT EXERCISED | incident candidate가 발생하지 않음 |
| 실제 Server upload | NOT EXERCISED | 이 run은 의도적으로 `uploadConfigured=False`; 10 sidecar는 `PENDING` |

`PASS`는 이 evidence가 실제 AMS2 값을 보존했다는 뜻이다. 차량 물리의 정확한 해석, 완전한 track geometry, 운영 Server E2E 또는 multi-car 결과까지 증명한다는 뜻은 아니다.

## 2. 실행 조건

| 항목 | 실제 값 |
|---|---|
| 실행 시각 | 2026-09-02 11:01:27~11:02:52 KST |
| archive elapsed | `0..84,986 ms` |
| Client 표시 버전 | `0.2.2` — P023 후보는 아직 version bump하지 않음 |
| AMS2 build | `3398` |
| Shared Memory | version `14`, mapping `$pcars2$`, read-only |
| session | `PRACTICE` / Test Day |
| track/layout | `인터라고스` / `인터라고스 GP` |
| track length | `4,294.94775390625 m` |
| vehicle/class | `Aston Martin Vantage GT3 Evo` / `GT3_Gen2` |
| participant | 1명, local slot 0 |
| display | `3440×1440`, foreground |
| capture | background Client, local durable enabled |
| network | disabled for this controlled run |

Client log는 `SHM_ATTACH`, participant count 1, foreground/window state, 30 Hz SHM polling과 clean stop을 기록한다. 마지막 카운터는 다음과 같다.

```text
FUTURE_TELEMETRY_STOP attempts=1 batches=1698 dropped=0 chunks=10 archiveDropped=0 failures=0
```

동일 로그의 sequence consistency counter에는 15 retries와 3 dropped snapshots가 있었고, archive quality에는 driver logical sample 3개가 missing/dropped로 남았다. bounded input channel drop은 0이다. 이 값을 숨겨 `COMPLETE`로 만들지 않았다.

## 3. Archive identity

다섯 stream이 공유하도록 생성된 실제 identity는 다음과 같다.

| key | value |
|---|---|
| session directory key | `c83c8e75832ab8cc7ccb187c0c2a7ec5` |
| `sessionId` | `capture-4f09094c4c1d4207ac771237b6628997` |
| `sessionFingerprint` | `b6a00c271190faa276f94eb83220b2f0337531de898c433434c0449b22e720f7` |
| `witnessId` | `witness-6c59bdbb00914504ae53ad2a004bb4ba` |
| `attemptId` | `attempt-af00d882450e44e3b485f651c9f25768` |
| `attemptNumber` | `1` |

Tier 1 metadata의 participant dictionary에는 session-scoped `participantRef=64`, slot 0, generation 1, vehicle/class snapshot이 남았다. 이는 영구 계정 identity가 아니다.

## 4. Persisted archive inventory

분석기는 live process나 Shared Memory를 열지 않고 archive root 아래 완료된 `*.json.gz`만 읽었다.

| stream | chunks | actual rows/records | expected | missing/dropped | raw JSON B | gzip B | visibility |
|---|---:|---:|---:|---:|---:|---:|---|
| `DRIVER_TELEMETRY` | 3 | 1,697 | 1,700 | 3 / 3 | 1,145,858 | 336,045 | `PRIVATE_DRIVER_ANALYTICS` |
| `PARTICIPANT_REPLAY` | 3 | 425 | 425 | 0 / 0 | 64,649 | 21,952 | `PUBLIC_REPLAY` candidate |
| `RACE_STORY` | 2 | 3 | 3 | 0 / 0 | 2,868 | 1,554 | `PUBLIC_REPLAY` candidate |
| `SESSION_METADATA` | 2 | 3 | 3 | 0 / 0 | 6,546 | 2,415 | `PUBLIC_REPLAY` candidate |
| **합계** | **10** | **2,128** | **2,131** | **3 / 3** | **1,219,921** | **361,966** | mixed |

이 시나리오에서는 incident candidate가 없어 `INCIDENT_TRACE` 파일이 생기지 않았다. 이는 stream 오류가 아니라 candidate-only 정책 결과다.

모든 `.upload.json` status가 `PENDING`, `attemptCount=0`인 이유는 controlled run에서 upload를 끈 것이다. `PENDING`을 전송 실패나 Server reject로 해석하면 안 된다.

## 5. 실제 동적 field 검증

아래 값은 세 개의 persisted `DRIVER_TELEMETRY` gzip chunk, 1,697 samples만 읽어 집계한 값이다.

| field | min | max | range | distinct | 판정 |
|---|---:|---:|---:|---:|---|
| `worldX` | -409.601349 | -398.125916 | 11.475433 | 1,002 | PASS |
| `worldY` | 15.132179 | 15.340466 | 0.208286 | 1,393 | PASS |
| `worldZ` | -173.207779 | -170.195358 | 3.012421 | 961 | PASS |
| `lapDistanceMeters` | 49.430168 | 49.480705 | 0.050537 | 749 | PASS for changing source; full-lap mapping unproven |
| `speedMetersPerSecond` | 0.002252 | 2.843291 | 2.841039 | 1,695 | PASS |
| `throttle` | 0 | 1 | 1 | 33 | PASS |
| `brake` | 0 | 1 | 1 | 13 | PASS |
| `steering` filtered | 0 | 0 | 0 | 1 | unchanged in this run |
| `unfilteredSteering` | -1 | 1 | 2 | 51 | PASS |
| `clutch` filtered | 0 | 1 | 1 | 1,348 | PASS |
| `unfilteredClutch` | 0 | 0 | 0 | 1 | unchanged in this run |
| `rpm` | 1,132.269043 | 7,364.776855 | 6,232.507813 | 1,417 | PASS |
| `gearRaw` | -1 | 1 | 2 | 3 | PASS |
| longitudinal acceleration | -13.776029 | 117.454758 | 131.230786 | 1,693 | PASS for change |
| lateral acceleration | -0.327974 | 2.210174 | 2.538148 | 1,696 | PASS for change |
| vertical acceleration | -9.955537 | 1.566439 | 11.521976 | 1,696 | PASS for change |

`real-field-validation.json`의 `steeringChanged`는 filtered 또는 unfiltered steering 중 하나의 실제 변화가 있을 때 true다. 이 run에서는 filtered `steering`이 0으로 고정됐고 `unfilteredSteering`이 -1~1로 변했다. 따라서 “조향 source 보존 PASS”는 맞지만 “filtered steering 변화 PASS”라고 보고하면 안 된다.

acceleration은 실제 값 변화와 graph 생성 가능성을 증명했다. 그러나 AMS2 header 주석의 단위 모호성과 차량이 짧은 거리에서 벽/garage 주변을 움직인 조건 때문에 각 axis의 물리적 정확도와 정상 주행 범위는 별도 clean-lap validation이 필요하다.

## 6. Tyre, damage와 추가 channel

짧은 run에서도 tyre temperature, pressure-converted 값, wear, 당시 `mFuelLevel` source, damage와 velocity row는 non-null로 보존됐다. 그러나 다음 이유로 core dynamic PASS와 분리한다.

- tyre temperature는 네 corner 모두 약 31.1~38.5 °C로 변화했다.
- `mAirPressure × 6.894757` 결과는 약 1,114~1,130 kPa로 기록됐다. 일반적인 해석과 맞지 않을 가능성이 있으므로 source unit/scale 또는 layout 의미를 다시 확인하기 전 `SEMANTICS_PENDING`이다.
- rear tyre wear는 작은 변화가 있었지만 wear scalar의 방향/차량별 의미는 장거리 clean run으로 확인하지 않았다.
- 이 run의 pre-fix `fuelLiters` 열은 실제로 normalized `mFuelLevel` source였으므로 리터 값으로 사용하면 안 된다. 후보 코드는 이후 `fuelLevelRatio`, `fuelCapacityLiters`, derived `fuelLiters`로 수정됐고 synthetic/단위 테스트는 PASS했지만, 새 계약의 real AMS2 capture는 아직 없다.
- engine damage의 작은 변화가 기록됐지만 실제 damage event를 의도적으로 재현하지 않았다.
- heading은 1.315282~1.323605 rad로 변했으나 한 방향의 짧은 이동뿐이어서 yaw component/sign convention을 최종 확정하지 않는다.

null이나 비정상 가능성이 있는 channel을 synthetic 0으로 채우지 않았다. Server/Web analyzer는 capability와 semantic-verification 상태를 확인해야 한다.

## 7. Offline renderer 결과

동일한 10개 gzip만 읽어 `telemetry-proof.html`과 `proof-summary.json`을 만들었다.

| derived output | real short run | 해석 |
|---|---|---|
| Lap table | FAIL / not applicable | 완료 lap 없음 |
| Position chart | FAIL / not applicable | participant 1명 |
| 2D replay | FAIL / not applicable | reference gate가 multi-participant를 요구 |
| Speed graph | PASS | 1,697 driver samples |
| Brake graph | PASS | brake 0..1 |
| Throttle graph | PASS | throttle 0..1 |
| Steering graph | PASS | renderer는 coaching source인 unfiltered steering 사용 |
| G-force graph capability | PASS | lateral acceleration 변화 존재 |
| Driving line | FAIL / not applicable | 둘 이상의 lap 없음 |
| Track centerline | FAIL / not applicable | clean multi-lap geometry 없음 |
| Incident animation | FAIL / not applicable | incident trace 없음 |

여기서 `FAIL`은 renderer의 정해진 acceptance predicate를 충족하지 않았다는 기계적 결과다. 짧은 1인 Test Day에 존재하지 않는 lap/multi-car/incident fact를 만들어내지 않은 것이며, synthetic full-session proof의 PASS를 대체하거나 무효화하지 않는다.

별도 60분/32대 synthetic archive에서는 284개 persisted gzip, 648,530 samples로 위 11개 derived output이 모두 PASS다. 즉 full-session reprocessing architecture는 synthetic으로 GREEN이고, 실제 AMS2에서는 core signal lineage가 GREEN이며, 둘을 결합한 실제 full-race proof는 아직 필요하다.

## 8. Evidence artifact

evidence root:

```text
../AMS2League/evidence/future-telemetry/real-ams2-v023-candidate-20260902/controlled2/
```

| artifact | bytes | SHA-256 |
|---|---:|---|
| `real-field-validation.json` | 4,882 | `EE6E8E37F02E2EFB459741D7B0EB4AA7CC3DAF9C84E57AA9B739B379FB9391D5` |
| `offline-render/proof-summary.json` | 586 | `A50A756A8C8D16FFEF89A50B09E7D069BE7AE4B23FF9DA2CDB2FF84034C0D504` |
| `offline-render/telemetry-proof.html` | 101,595 | `CEBDCFD27A38BE7FB110E5A22AE7AEB3269BB0839322C2DEDA7B6F84A93266C3` |
| `logs/client-20260902-110126.log` | 4,258 | `77CB848F5943247DD069F0F760B3F4707D592DA8681E3FF574BA0B0B1AD2B962` |

### 8.1 실제 archive chunk의 local HTTP replay

controlled2 archive의 `DRIVER_TELEMETRY` chunk `chunk-be01d7222795c8dad9815f06138f9a9239225d5c` 하나를 production `Cafe24ActivityUploadTransport`로 local PHP 8.4 `Application`에 전송했다. Source JSON은 406,478 B, client gzip은 114,932 B이며 payload SHA-256은 `e9a4b6c79fe773e581964cb6a94929100e23c956c45a38edcb1eb06bf41f24c2`, compressed SHA-256은 `c9a7c976c4387aad0522213451cce8e6abc52236d256582ddde68aca52ed54fd`다.

POST는 `201 STORED`, index/detail GET은 각각 `200`, index count는 1이었다. 반환된 Server canonical gzip은 115,850 B / SHA-256 `32bd7f5e8d88709febbacdbdf3babc4b466f1eda6ffe44cc14f424a1f65856e6`이며, 다시 푼 JSON은 원본과 byte-exact이고 payload SHA도 일치했다. `../../work/local-http-e2e/evidence/`의 `client-http-proof.json`, `index-response.json`, `get-response.json`, `server-store-proof.json`, `request-audit.ndjson`에 기록했다.

이것은 실제 loopback HTTP 경로의 PASS다. 다만 local HTTPS URI를 test handler가 loopback HTTP로만 rewrite했고 Server는 serialized `InMemoryStore`를 사용했다. Cafe24/PDO/MariaDB/staging/TLS/FastCGI 검증이 아니며 production Portal/DB는 변경하지 않았다.

## 9. 재현 명령

`outputs/AMS2KRLeague`를 current directory로 사용한다.

```powershell
dotnet build .\tools\AMS2TelemetryProof\AMS2TelemetryProof.csproj -c Release

dotnet run --project .\tools\AMS2TelemetryProof\AMS2TelemetryProof.csproj -c Release -- validate "..\AMS2League\evidence\future-telemetry\real-ams2-v023-candidate-20260902\controlled2\activity\future-telemetry" "<new-real-validation.json>"

dotnet run --project .\tools\AMS2TelemetryProof\AMS2TelemetryProof.csproj -c Release -- render "..\AMS2League\evidence\future-telemetry\real-ams2-v023-candidate-20260902\controlled2\activity\future-telemetry" "<new-render-directory>"
```

고정 evidence의 hash를 보존하려면 `<new-...>` 경로를 사용한다.

## 10. 남은 실제 검증 gate

1. clean lap 2개 이상으로 lap-distance progression, heading convention, driving line와 centerline을 검증한다.
2. 실제 multiplayer에서 2명 이상 position chart/2D replay와 participant join/rejoin을 검증한다.
3. 실제 incident candidate로 -3~+3초, 20 Hz 관련 차량 trace와 animation을 검증한다.
4. tyre pressure unit/scale, wear direction과 acceleration axis/unit을 정상 주행 조건에서 확정한다.
5. local PHP 8.4 + `InMemoryStore` real-chunk POST/GET은 PASS했다. upload가 허용된 staging `PdoStore`/MariaDB에서 pending gzip → HTTPS → raw archive → GET → persisted-only reprocess를 별도로 검증한다.
6. 실제 60분/다수 차량에서 Client CPU/RAM/disk와 drop high-water mark를 측정한다.

결론은 **실제 source와 local archive PASS, 실제 full-session/multi-car/incident/Server E2E는 PARTIAL**이다. Production Portal은 수정하지 않았다.
