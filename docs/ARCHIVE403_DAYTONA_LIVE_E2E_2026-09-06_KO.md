# Archive403Hotfix — Daytona 실멀티 종료 후 저장·전송 검증

작성: 2026-09-06 KST. 사용자 직접 주행/종료 후 읽기 전용 조사.

## 결론

**저장·전송·Compact 원본 무결성 PASS, 전체 판정 YELLOW.**

- 새 공개 Compact **36/36**개가 서버 인덱스에 존재하며, 서버 GET → 압축 해제 → 로컬 A2CT/메타데이터 SHA256 대조 **36/36 일치**.
- PHP 서버 decode **36/36**, 서버에서 받은 원본의 제품 C# decoder 재처리 **36/36 성공**. HTTP 성공 응답만으로 PASS 처리하지 않았다.
- 새 Witness 4개와 개인 Race activity 1개는 모두 첫 시도 HTTP **201 / STORED**. 이번 세션에서 403, archive serialization/disk/worker/finalize 실패, commit conflict는 0.
- Practice/Qualifying/본 Race는 각 기록의 loss ledger와 finalize가 서버까지 보존됐다. 다만 cadence 누락 카운터 때문에 모두 **PARTIAL**이며 COMPLETE로 승격시키지 않는다.
- 세션 종료 순간 **31 ms의 추가 EndOnly capture**가 생겼다. 예선→레이스 전환에도 불안정 Race classification이 이전 capture에 붙었다. 이 두 전환 문제는 미수정 후속 항목이다.

사용자가 말한 ‘세션 정상 종료’와 ‘플레이어 완주’는 별개다. 본 Race의 실제 관측 종료 상태는 **12명 FINISHED + 플레이어 1명 DNF**이며 이를 FINISHED로 고치지 않았다.

## 실행 기준

- Client 표시 버전: 0.3.1, `src/AMS2LeagueClient/bin/Archive403Hotfix/AMS2LeagueClient.exe`.
- PID 9276, 시작 13:15:08. 검사 도중 계속 살아 있고 SHM 약 30 Hz 로그가 갱신됨.
- 실행 후보 Core DLL SHA256: `3bbdab4470a5f7536ff9c1ca56401eb6bf5c0b3e7cf95492ac14da57364c6dfe`.
- Git 기준 HEAD: `427459312aa2d79d9518a72b1e2b234c9e7feff8` + 기존 미커밋 수정 후보. 본 조사에서 제품 코드는 변경하지 않았다.
- 실서버 health GET: `ok`, Application **1.10.2**, schema **18**, 응답 시각 `2026-09-06T05:34:59.592Z`.
- 트랙: Daytona / `Daytona_Speedway_Tri_Oval`. F-USA_Gen3_SS 차량.
- 사용자가 멀티플레이 방에서 직접 테스트했다. 본 Race 명단은 플레이어와 AI 12명이며, 다른 PC의 독립 witness를 포함한 다중 클라이언트 검증은 아니다.

## 시간대와 capture 식별

| 구간 | 관측 시간 KST | 길이 | 로컬 폴더 | sessionId |
|---|---|---:|---|---|
| A Practice | 14:11:24.939–14:17:23.767 | 358.829초 | `0d6a1ac7f8fcdcf3e0706ee96b205801` | `capture-e9b10999f584477c8dd7b4d4b86e77a9` |
| B 주로 Qualifying, 경계 상태 포함 | 14:17:23.846–14:23:27.664 | 363.817초 | `212e28e757655df002811563363ac9db` | `capture-8c5e8091c18546609693e1f99a28eed1` |
| C 본 Race | 14:23:27.823–14:25:53.718 | 145.895초 | `f9bf38508a5c8c03ffcc4782b03e9251` | `capture-c7e672e5d17c423495558eace6f17129` |
| D 종료 경계 추가 기록 | 14:25:53.892–14:25:53.923 | 0.031초 | `08551a676d0e60f2ce066a9ac3cbe1ce` | `capture-2af7689dacb04d2e9f9cf4fa0904e311` |

이는 네 경기를 뜻하지 않는다. 한 번의 테스트 진행에서 나뉜 세션/전환 capture 네 개다. 전체 테스트는 약 14분 29초이며 본 Race만 5분 이상 주행한 테스트는 아니다.

| 구간 | witnessId | attemptId |
|---|---|---|
| A | `witness-ca92ac9e86974653b50a8e7d0d070ea3` | `attempt-8ba8ef5088364f0ca091ec5edb8add7a` |
| B | `witness-97244c5e0e3f402396d9c11451e61233` | `attempt-3e98e701752b48158d178395c38c18a7` |
| C | `witness-5327e4fc897f47dcb970f0d7a6b7dfdc` | `attempt-efb29e365ef44aeca9e02c9f0c3ee915` |
| D | `witness-2b81a52a76e144ff8149b0d4d691b53b` | `attempt-c38fae4c93684badbd9d424c16262bfc` |

모두 attemptNumber=1. Witness payload의 captureSessionId/attemptId와 해당 archive identity가 대응한다. A/B fingerprint는 `ec407208e7841c59d8d7a3a3810561ce6de77bf19ba582d55d35c934342dc3af`, C/D는 `41b7672e7f87cce4491a01cfaa1eadb756ec0cda18a66fe8235b1a52aca835b8`.

## Compact 서버 대조

프로토콜 `AMS2_COMPACT_TELEMETRY_V1`. 공개 schema ID 1 SESSION, 16 RACE STORY, 32 PARTICIPANT REPLAY, 33 TRACK GEOMETRY, 64 INCIDENT, 80 LOSS LEDGER, 81 FINALIZE를 검사했다.

| 구간 | 공개 chunk / 서버 index | hash / PHP decode / C# decode | Client wire B | Server Compact gzip B | finalize |
|---|---:|---|---:|---:|---|
| A | 11 / 11 | 11 / 11 / 11 | 184,568 | 185,141 | PASS, PARTIAL |
| B | 11 / 11 | 11 / 11 / 11 | 164,686 | 165,239 | PASS, PARTIAL |
| C | 7 / 7 | 7 / 7 / 7 | 67,346 | 67,494 | PASS, PARTIAL |
| D | 7 / 7 | 7 / 7 / 7 | 2,135 | 2,135 | PASS, PARTIAL |
| 합계 | **36 / 36** | **36 / 36 / 36** | **418,735** | **420,009** | 4 / 4 |

- 각 capture의 로컬 공개 SENT chunk ID 집합과 서버 인덱스 집합이 정확히 일치한다. chunkSequence/schema를 PHP decode 결과와 대조했다. 전역 sequence 번호는 schema별 예약 구조이므로 단순 0,1,2 연속성으로 검사하지 않았다.
- 모든 공개 로컬 sidecar의 attemptCount=1. 새 conflict/quarantine/pending 공개 chunk 없음.
- 압축 해제한 Compact 내용은 바이트 단위로 동일하다. 일부 gzip 원본 바이트/크기는 다르므로 ‘압축 파일 자체가 36개 모두 byte-identical’이라고 주장하지 않는다.
- 마지막 실제 chunk를 포함한 로컬 공개 집합 전체가 서버에 있고 schema 80/81도 포함된다. 이는 보존된 capture 범위의 최종 chunk 검증이며, SHM 관측 이전/사이의 미수집 원본까지 없었다는 뜻은 아니다.
- 서버에서 반환한 `payloadCompactGzipBase64` 원본 크기 420,009 B = 약 **410.2 KiB**. 큰 JSON으로 팽창한 값을 Compact 저장량으로 세지 않았다.
- 이 수치는 **공개 Compact payload만**이다. Witness JSON, legacy metadata, MariaDB index/행 overhead, 파일시스템 allocation은 별도다. DB 물리 증가량과 서버 전체 저장공간 잔량은 미측정이다. 서버가 다른 경로에 확장 JSON을 중복 영속 저장하는지까지 GET만으로 전수 입증하지 않았다.

## 손실과 authority 정책

| 구간 | Private driver cadence missed | Incident cadence missed | 합계 knownLoss |
|---|---:|---:|---:|
| A | 1,407 | 97 | 1,504 |
| B | 5,493 | 504 | 5,997 |
| C | 1,004 | 7 | 1,011 |
| D | 0 | 360 | 360 |
| 합계 | **7,904** | **968** | **8,872** |

- 로컬 ledger, 서버 loss ledger, 서버 finalize의 knownLoss 수치가 구간별 일치한다. 손실을 0으로 지우거나 COMPLETE로 표시하지 않았다.
- 이 값은 목표 cadence 대비 missing sample 카운터다. **전송 중 chunk 8,872개가 사라졌다는 뜻이 아니다.** accepted 데이터의 archiveInputLosses, outerQueueLosses, serializationFailures, workerExceptions, diskWriteFailures, finalizeFailures, commitConflicts, uploadFailures는 네 capture 모두 0.
- Replay stream 자체의 cadence miss는 0. Driver/Incident miss의 세부 원인(메뉴/데이터 부재/스케줄 지터/구간 계산 과대계상)은 별도 재현·측정 필요. 특히 31 ms짜리 D의 Incident miss 360은 정상 장시간 cadence 손실로 곧바로 해석하지 않는다.
- Private driver schema 48/49/50/51: A 4개 44,021 B, B 8개 23,508 B, C 4개 19,511 B. 합계 **16개 / 87,040 B**, 모두 `LOCAL_PENDING_OWNER`. 현재 정책에 따른 로컬 보존이며 업로드 실패가 아니다. 이를 서버로 전송하지 않았다.
- 각 capture 폴더에서 failed-chunks/tmp/quarantine 파일은 발견되지 않았다.

## 서버 저장 원본만으로 재처리

GET으로 저장한 `.a2ct`만 제품 decoder로 읽었다. 재처리 단계는 SHM/로컬 capture 원본을 읽지 않는다. 비교 단계에서만 로컬 원본을 사용했다.

| 구간 | 명단 수 | 최대 관측 lap 값 | 순위 변화 수 | Story rows | Replay rows | world X/Z 보유 rows | Incident rows |
|---|---:|---:|---:|---:|---:|---:|---:|
| A | 14 | 6 | 122 | 289 | 10,123 | 10,003 | 5,777 |
| B | 13 | 6 | 110 | 252 | 9,977 | 9,854 | 5,340 |
| C | 13 | 3 | 42 | 131 | 4,216 | 4,186 | 2,629 |
| D | 13 | 3 | 0 | 17 | 13 | 13 | 6 |

- C에서 RaceState raw 3 FINISHED 12명, raw 6 DNF 1명. 로컬 안정 Race Result 13명과 대응한다.
- 최대 lap 값은 current lap/lapsCompleted 중 관측 최댓값이다. ‘3랩 완주’를 뜻하지 않는다. 로컬 안정 Race Result에서 AI는 lapsCompleted=2, 플레이어는 0이다.
- A/B/C는 명단·Lap·Position History·Race Story·이동 좌표를 서버 원본만으로 확인했다. D는 단일 snapshot 수준이라 움직임 재구성 PASS로 세지 않는다.
- 2D 재생에 사용할 좌표 데이터의 존재와 decode를 확인한 것이며, 웹 2D renderer의 시각 품질까지 검사한 것은 아니다. 공식 total race time/final gap은 생성하지 않았다.

## 결과 Witness 업로드

네 Witness payload와 개인 activity는 모두 첫 시도 `SENT`, HTTP `201`, `STORED`.

| 대상 | payload SHA256 |
|---|---|
| A Witness | `73d65f128ad682926967e8b9ba71116e3a5dee831a9ef1a9ed6761f1b1baeb3c` |
| B Witness | `addef7e76990eab372d1be8664f8b6365bca7025c8759e74250337dc4cd88271` |
| C Witness | `14a9b866b647f574d7f8661f18e31a3034bf2737edfe5d32a7bd414ae54a3908` |
| D Witness | `6080aa533b6fad511c44979d3d4ab0d35d9ebfd4d64a2699578cbe48a9bb8198` |
| 개인 activity 전송 body | `c057e0d5c0f67cde625ca19fbab889bb45450e744078e1bd240063f74638b49a` |

이번 201만으로 과거 403의 정확한 원인이나 대형 payload/WAF 한계가 모두 해결됐다고 단정하지 않는다. Witness 자체의 서버 JSON 재조회·포털 정규화 결과 상세 UI는 이번 검사에서 확인하지 않았다. 원본 GET/hash PASS는 Compact에 대한 것이다.

## 발견된 전환 문제 — 수정하지 않음

1. **종료 직후 추가 capture**: 로그 14:25:53.862 `session=INVALID/count=0` → 14:25:53.892 `RACE/count=13` → 14:25:53.989 `FRONTEND`. 중간 RACE snapshot으로 새 witness/attempt가 시작되어 31 ms EndOnly 결과가 하나 더 저장·업로드됐다. 이 기록을 새 경기로 간주하면 안 된다. 이전 403 격리 항목 재전송과는 무관한 신규 capture다.
2. **예선→Race 경계 오염**: B의 observed types가 PRACTICE/QUALIFYING/RACE를 포함한다. 실제 Qualifying 13명 외에 raceStable=false인 Grid/Race 13명이 붙고 `GRID_NOT_STABLE`, `RESULT_FINALIZED_ON_RESET` 경고가 있다. C의 stable=true인 본 Race 결과와 구분해야 한다. B의 Race 행을 공식 결과로 사용하지 않는다.
3. **cadence 품질 후속 분석**: 8,872 카운트는 전부 cadence 분류이고 예외/디스크/업로드 손실은 아니다. 이를 낮추려면 unavailable/menu/clock 경계를 fixture와 실관측으로 분리해야 하며, 임의로 ledger를 초기화하거나 프로토콜을 바꾸면 안 된다.

전환의 정확한 로그 순서는 확보했다. debounce/terminal 재진입 차단 등의 제품 수정은 이번 읽기 전용 검증에서 하지 않았다.

## 사용자에게 나타난 dotnet.exe 오류창

- 첫 서버 조회 도구 실행이 제한된 실행 환경에서 Windows DPAPI 보호 credential을 열지 못해 `InvalidDataException: The protected pairing credential cannot be opened by this Windows user.`로 종료됐다. 해당 시도는 HTTP 요청 전 실패했다.
- 사용자 권한으로 승인된 재실행은 기존 자격을 메모리에서만 사용해 GET에 성공했다. 새 token 발급/재등록/권한 확대는 하지 않았고 자격값을 출력하지 않았다.
- 사용자 오류창의 프로세스명 `dotnet.exe`와 시점은 이 검증 도구 실패와 부합한다. 별도 crash dump/Windows 이벤트로 동일 오류창의 예외를 완전히 대조하지는 못했다. WPF 오버레이 PID 9276은 계속 실행 중이며 로그가 갱신됨을 별도로 확인했다.
- 진단 도구에 해당 실패를 잡아 고정 메시지와 exit 3으로 끝내도록 추가했고, 같은 제한 환경에서 **unhandled exception 없이 exit 3**을 확인했다. 오류 응답 body도 출력하지 않도록 했다. 이 변경은 `work/` 진단 도구뿐이며 제품 프로그램 재시작/교체를 하지 않았다.

## 검증·산출물

- Release solution build: 경고 **0**, 오류 **0**. 실행 중인 후보와 다른 `bin/Archive403Verification/` 출력 사용.
- Client tests **89/89**, Activity tests **102/102**.
- 기존 서버 audit/reprocess 도구 재사용: reprocess에 capture 목록 인수와 chunkSequence 정렬만 추가. Ponytail 원칙에 따라 새 검증 시스템/의존성을 만들지 않았다.
- `git diff --check` PASS. 기존 사용자/다른 작업의 수정은 유지. commit/tag/release 없음.

이 PC의 재현 산출물(비공개, `work/` Git 제외):

- `work/archive403-live-20260906-1426/server-only/server-e2e.json`: chunk별 hash/bytes/schema/decode와 서버 index 대조.
- `work/archive403-live-20260906-1426/server-only/capture-*/`: 서버 GET 원본 Compact와 gzip.
- `work/archive403-live-20260906-1426/server-reprocess.json`: 서버 원본만 재처리한 명단/샘플/종료/손실 요약.
- `%LOCALAPPDATA%/AMS2KRLeague/logs/client-20260906-131508.log` 및 위 네 attempt ledger/sidecar/Witness/queue state.

```text
CLIENT CAPTURE: PASS (accepted 데이터 저장; cadence 손실은 별도)
UPLOAD: PASS (새 공개 Compact 36, Witness 4, 개인 activity 1)
SERVER RAW: PASS (Compact GET)
HASH: PASS 36/36 (압축 해제 Compact 기준)
FINALIZE: PASS 4/4
COMPLETENESS: PARTIAL
SERVER-STORED-ONLY REPROCESS: PASS A/B/C, D는 단일 snapshot으로 제한
DATA LOSS: 공개 chunk 전송 누락 0 / cadence 카운터 8,872
CONFLICT/QUARANTINE: 신규 0 (이전 403 격리 항목 유지)
CAFE24 STORAGE IMPACT: Compact payload 420,009 B / DB overhead 미측정
FINAL VERDICT: YELLOW
```

게임 조작, 과거 운영 데이터 삭제/초기화, 격리 항목 재전송, 공식 Race 생성/승격, 서버 배포/API/DB 변경은 하지 않았다.
