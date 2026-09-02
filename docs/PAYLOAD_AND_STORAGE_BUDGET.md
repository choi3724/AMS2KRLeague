# Future Telemetry Payload and Storage Budget

작성 기준: 2026-09-02 KST
작업번호: `AMS2-P023-FUTURE-TELEMETRY`

## 1. 판정

**60분 / 32대 synthetic fixture의 직렬화·gzip 크기 실측은 PASS**다.

- 1 Client, 60분, 32대: uncompressed JSON **376,215,655 B (358.787208 MiB)**
- 같은 payload의 gzip: **161,390,596 B (153.914066 MiB)**
- 전체 compression ratio: **0.42898426435763287 (42.8984264358%)**
- gzip으로 줄어든 비율: **57.1014343340%**
- full Shared Memory 30 Hz dump: **사용하지 않음**

이 값은 생성된 fixture 파일을 대상으로 한 **측정값**이다. 실제 AMS2 데이터의 entropy, incident 빈도, 참가자 변동, 파일시스템·HTTP·DB overhead까지 포함한 운영 용량 보증은 아니다.

## 2. 측정 증거와 단위

아래 경로는 모두 repository root (`outputs/AMS2KRLeague`) 기준 상대 경로다.

측정 원본:

```text
../AMS2League/evidence/future-telemetry/synthetic-60min-32car-v5-stream-capabilities-20260902/
  fixture-manifest.json
  sessions/<session-key>/chunks/**/*.json.gz
  sessions/<session-key>/chunks/**/*.upload.json
```

`fixture-manifest.json` SHA-256:

```text
A26EE8534EF91FC3ABA8C9A9F4F9E864F6F98178A9F392A132EB81B772992EF4
```

manifest identity:

| 항목 | 값 |
|---|---|
| schema | `ams2-telemetry-budget-v1` |
| fixture duration | 60 minutes |
| participants | 32 |
| session ID | `capture-4eb7459b53c4465b85f2ec4e51b41858` |
| session fingerprint | `fixture-60min-32car-v1` |
| witness ID | `witness-fixture-offline-proof-v1` |
| attempt ID | `attempt-7a0c06ae806746e9b4246dbdb8191ac6` |

단위는 다음처럼 고정한다.

- `B`: 정확한 byte 수
- `MiB`: `B / 1,048,576`
- 표의 MiB는 소수점 6자리 반올림 표시이며, 계산 원본은 byte 정수다.
- compression ratio는 `gzip bytes / uncompressed JSON bytes`다.

측정기는 각 `.upload.json` sidecar에 기록된 `uncompressedBytes`, `compressedBytes`, `quality.actualSampleCount`를 stream별로 합산한다. 따라서 아래 수치는 compact envelope JSON과 최종 `.json.gz` payload 크기다.

## 3. fixture capture profile

| Stream | 생성 cadence / 범위 | 비고 |
|---|---|---|
| `SESSION_METADATA` | session-level 1 record | session/participant dictionary |
| `RACE_STORY` | sparse fact event | 45 events, 빈 30초 bucket은 파일 없음 |
| `PARTICIPANT_REPLAY` | 5 Hz × 32 participants × 3,600 s | 모든 참가자의 공개 replay facts |
| `DRIVER_TELEMETRY` | 20 Hz × local driver 1명 × 3,600 s | private driver analytics |
| `INCIDENT_TRACE` | 20 Hz, -3 s~+3 s, 관련 4명 | fixture의 incident candidate 1건 |

일반 high-rate stream은 30초 chunk다. Replay/Driver는 각각 120개 chunk가 생성된다. 이 fixture는 Shared Memory 전체 구조를 30 Hz로 그대로 저장하지 않는다.

## 4. stream별 실측

| Stream | Chunks | Samples | Uncompressed B | Uncompressed MiB | Gzip B | Gzip MiB | Gzip / raw |
|---|---:|---:|---:|---:|---:|---:|---:|
| `DRIVER_TELEMETRY` | 120 | 72,000 | 213,253,405 | 203.374295 | 92,706,086 | 88.411413 | 43.4722653% |
| `INCIDENT_TRACE` | 1 | 484 | 156,535 | 0.149283 | 52,254 | 0.049833 | 33.3816718% |
| `PARTICIPANT_REPLAY` | 120 | 576,000 | 162,735,772 | 155.196926 | 68,595,629 | 65.417890 | 42.1515369% |
| `RACE_STORY` | 42 | 45 | 62,801 | 0.059892 | 35,397 | 0.033757 | 56.3637522% |
| `SESSION_METADATA` | 1 | 1 | 7,142 | 0.006811 | 1,230 | 0.001173 | 17.2220666% |
| **Total measured** | **284** | **648,530** | **376,215,655** | **358.787208** | **161,390,596** | **153.914066** | **42.8984264%** |

gzip payload에서 `DRIVER_TELEMETRY`가 57.442062%, `PARTICIPANT_REPLAY`가 42.502866%를 차지한다. 따라서 현재 profile에서 운영 용량을 좌우하는 것은 metadata/event가 아니라 local driver trace와 32대 replay다.

한 시간의 gzip 총량을 시간 전체에 균등하게 나눈 참고값은 **44,830.721 B/s (358.646 kbit/s)**다. 실제 전송은 chunk 단위이므로 이 값은 peak bandwidth나 request overhead를 의미하지 않는다.

30초 청크 최대값은 32대 fixture에서 Driver `1,792,585 B raw / 779,905 B gzip`, Replay `1,374,379 B raw / 588,368 B gzip`이었다. 별도 1분/64대 limit probe에서도 Replay 최대 `2,725,970 B raw / 1,174,696 B gzip`, Driver 최대 `1,769,822 B raw / 778,882 B gzip`로 Server validator의 `8 MiB decoded / 2 MiB gzip` 제한 이하였다. 두 수치는 synthetic bound이며 실제 운영 entropy 보증이 아니다.

PHP 8.4.25 validator의 stream별 대표/최대 청크 결과는 `../AMS2League/evidence/future-telemetry/server-validator-v5-stream-capabilities-20260902.json`에 고정했다. 다섯 stream 모두 오류 0이고 metadata의 네 stream capability도 `true`로 확인됐지만, 이는 local direct validator 증거이며 Cafe24 FastCGI/TLS/PDO/MariaDB staging 증거는 아니다.

## 5. Client 수별 정확한 선형 계산

아래에서 1 Client는 위 fixture의 **실측 baseline**이고, 5/10/20 Client는 그 exact byte 값을 Client 수로 곱한 **산술 추정치**다.

| Clients | 구분 | Uncompressed B | Uncompressed MiB | Gzip B | Gzip MiB |
|---:|---|---:|---:|---:|---:|
| 1 | measured baseline | 376,215,655 | 358.787208 | 161,390,596 | 153.914066 |
| 5 | linear estimate | 1,881,078,275 | 1,793.936038 | 806,952,980 | 769.570332 |
| 10 | linear estimate | 3,762,156,550 | 3,587.872076 | 1,613,905,960 | 1,539.140663 |
| 20 | linear estimate | 7,524,313,100 | 7,175.744152 | 3,227,811,920 | 3,078.281326 |

20 Client gzip 합계 `3,227,811,920 B`는 decimal 기준 약 `3.227812 GB`다. 이 표는 중복 제거 전 client-originated raw archive를 단순 합산한 값이다.

## 6. 추정 전제

1. 모든 Client가 동일한 60분과 동일한 32대 field density를 기록한다.
2. 각 Client의 `DRIVER_TELEMETRY`는 권한이 확인된 local driver 1명만 기록한다.
3. 각 witness가 32대 `PARTICIPANT_REPLAY`를 독립 업로드하며, Server의 witness dedup/merge/retention 절감은 적용하지 않는다.
4. fixture는 incident candidate 1건, 관련 참가자 4명, -3초~+3초 burst 1개다. 실제 사고 빈도가 높으면 `INCIDENT_TRACE`가 선형 baseline보다 커진다.
5. `RACE_STORY`의 45 events는 synthetic event density다. 실제 flag/pit/penalty/position event 수에 따라 달라진다.
6. gzip 구현, JSON numeric representation과 dictionary reuse가 현재 코드와 동일하다고 가정한다.
7. Client가 만든 gzip을 Server가 canonical re-gzip하면 Server-side blob 크기는 달라질 수 있다.

## 7. 포함하지 않은 비용

다음은 위 byte 합계에 포함되지 않는다.

- `.upload.json`, manifest, directory entry, filesystem allocation unit
- HTTPS header/TLS/TCP와 retry/re-upload traffic
- Server raw index row, normalized tables, DB index, transaction log
- backup/replica/object-versioning/retention copy
- conflict/quarantine/recovery file
- derived graph, 2D replay HTML, cache와 thumbnail
- application binary, local log, crash dump
- CPU, RAM, disk latency와 gzip 처리 시간

따라서 Cafe24 quota 계획은 측정 baseline `161,390,596 B × Client × 보존 경기 수`만으로 확정하면 안 된다. raw archive 보존 정책, 중복 witness 정책, DB/backup 배수를 별도로 더해야 한다.

## 8. 운영 해석과 다음 실측

- 이 fixture는 고정 schema와 bounded cadence로 raw 30 Hz dump보다 용량을 제한할 수 있음을 증명한다.
- 공개 replay 중복을 그대로 장기 보존할지, verified witness raw는 보존하되 serving용 canonical replay만 병합할지 Server 정책이 필요하다.
- 실제 AMS2 60분/32대 Race에서 값 변화·문자열 entropy·참가/이탈·pit/flag/event 밀도를 포함한 동일 manifest 측정이 필요하다.
- Cafe24에서는 raw blob, index/normalized DB, backup을 분리 계측해야 한다.
- upload retry와 장시간 offline backlog를 포함한 local disk high-water mark도 별도 측정해야 한다.

현재 결론은 **fixture payload sizing GREEN, production capacity sizing PENDING**이다.
