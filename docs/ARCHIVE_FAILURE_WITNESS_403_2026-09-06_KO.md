# 관측 저장 실패·Witness 403 진단 및 수정 인계

작성: 2026-09-06 13:04 KST. 범위: Overlay Client 로컬 코드/fixture 검증.

## 1. 최종 판정

```text
ARCHIVE ROOT CAUSE: 과거 Buskerud 최초 예외 NOT CONFIRMED.
  별도 재현 확정: Story 음수 거리의 unsigned Compact 변환 실패,
  만료 실패 청크가 후속 정상 입력을 막는 경로, 부분 저장 재시도의 artifact 재생성 문제.
ARCHIVE FIX: CONDITIONAL — 재현/회귀 PASS, 과거 최초 예외와 실제 신규 capture 미검증
FAILURE DIAGNOSTICS: PASS — 로컬 예외/403 fixture, 정제·집계·로그 실패 격리
WITNESS 403 CAUSE: NOT CONFIRMED — 당시 응답 본문/Content-Type/requestId 없음
WITNESS CLIENT FIX: WAITING_FOR_SERVER — 진단/403 재시도 방지 PASS, 실제 거부 사유 확인 필요
RESTART / ATTEMPT MAPPING: PASS — 로컬 재시작 fixture; 기존 서버 ID 수정 없음
RELEASE BUILD: warnings 0 / errors 0
CLIENT TESTS: 89 passed / 0 failed
ACTIVITY TESTS: 102 passed / 0 failed
CADENCE / WIRE COMPATIBILITY / SIZE GATES:
  cadence 기본값 보존; 60분/32대 기존 30초 fixture 647/647 Compact 파일 해시·크기 동일
  wire 1,999,401 -> 1,999,401 B (차이 0 B)
  이 packing의 전체 raw-source gzip은 512 KiB 및 1 MiB 초과(수정 전부터 동일)
  현행 300초 shipping packing의 정식 size gate 재측정은 NOT RUN; gate 완화 없음
REAL NEW CAPTURE UPLOAD: NOT RUN
PAST BUSKERUD RECOVERY:
  결과 witness: 25명 보존, 전송은 QUARANTINED
  replay: 좌표 원본 없음, 조사한 증거만으로 2D 복구 불가
  story: Compact Story 없음; witness 543개 이벤트/33개 상태 스냅샷으로 부분 타임라인 가능
SERVER / WEB FOLLOW-UP: 403 응답/installation scope 확인; 80/81 미수신 판정 및 상세 표 잘림 별도 수정
PRODUCTION QUEUE MUTATION: NO
DEPLOYED / RUNNING CLIENT VERSION: 수정본 NOT DEPLOYED.
  실행 중 0.3.1 PrestartProximityHotfix (PID 21716), AMS2 PID 19968 유지
OVERALL: YELLOW — 운영 수집/전송 성공 판정 아님
```

이번 지시서가 허용하지 않은 commit/tag/push/release/배포/게임 조작/실제 전송/운영 큐 변경은 하지 않았다.

## 2. 기준점과 작업 분리

- 저장소 HEAD: `427459312aa2d79d9518a72b1e2b234c9e7feff8` (`v0.3.1`).
- 착수 당시 AI timing, 첫 랩 무효 표시, 레이아웃/글꼴, Relative Gap, 이벤트 flash, 출발 전 proximity 수정이 미커밋 상태였다. 모두 보존했다. 이번 변경을 UI 수정 전체로 오인하지 말 것.
- 실행 중 앱: `src/AMS2LeagueClient/bin/PrestartProximityHotfix/AMS2LeagueClient.exe`, 시작 12:03:54 KST. AMS2는 10:17:57 KST 시작한 PID 19968이다. 종료/재시작하지 않았다.
- 검증용 새 출력: `src/AMS2LeagueClient/bin/Archive403Hotfix/`. 실행 중 경로와 분리했다. 버전 문자열은 올리지 않았다.
- 실행 EXE는 공통 .NET apphost이므로 EXE 해시만으로 managed 코드 버전을 구별하면 안 된다. 비교에는 실제 `AMS2LeagueClient.Core.dll` 해시를 사용했다.
- 웹 운영 버전/상태는 웹 담당 인계에 따른 `1.10.2 / schema 18 / 20260906-002`이며 이번 작업에서 운영 서버를 직접 재검증하거나 배포하지 않았다.

## 3. 과거 Buskerud 증거 — 읽기 전용 재확인

아래 경로의 기준은 `C:/Users/User/AppData/Local/AMS2KRLeague/`다.

### 실패한 Compact attempt

`activity/future-telemetry/sessions/20790722ed9b221de257203bbb324a41/`

```text
sessionId: capture-e9369e55191346668ac2cde8f2f354a9
sessionFingerprint: 9cc49c2cec4fbdb3d43fd31cf4403e50ed28581e90bbc9a272e94a226e16a265
witnessId: witness-2b262c3660644927ad34447563a7ac6f
attemptId: attempt-52f64ee5bc0f46438c8f95c90219475c
attemptNumber: 1
```

파일은 여전히 네 개다. 수정 시각은 모두 10:55:51 KST다.

| 파일 | bytes |
|---|---:|
| `chunks/compact/session/00000000-0001.a2ct.gz` | 619 |
| 위 Compact upload sidecar | 1,839 |
| `chunks/session_metadata/00000000.json.gz` | 11,975 |
| 위 metadata upload sidecar | 1,671 |

619 B Compact SHA256: `6a77c6bc090b58d529d14b4b2f23315351912e99e85b3e5f697b08f77443d2eb`.

해당 `attempt-ledgers/...attempt-loss.json`: `PARTIAL`, closeRequested=true, finalizeAcknowledged=false, durableAck=false, knownLossCount=48,317. 이 값은 실패 카운터 합계이지 고유 유실 행 수가 아니다.

| 스트림 | acceptedWorkUnits | workerExceptions | durableCommitAcks | finalizeFailures |
|---|---:|---:|---:|---:|
| SESSION_METADATA | 765 | 418 | 1 | 0 |
| RACE_STORY | 696 | 13,858 | 0 | 1 |
| PARTICIPANT_REPLAY | 17,705 | 11,903 | 0 | 1 |
| DRIVER_TELEMETRY | 13,816 | 9,948 | 0 | 1 |
| INCIDENT_TRACE | 17,705 | 11,903 | 0 | 1 |

uploadFailures=0은 저장 성공 또는 전송 성공의 증거가 아니다. replay/story에 전송할 파일 자체가 없다. 기존 로그에 최초 worker 예외의 메시지/stack이 없으므로 당시 최초 throw 지점은 확정하지 않았다.

비교 정상 세션 `67f1861c9dd7e7f632937e0f779c1a2b`는 public schema 1/16/32/33/64/80/81이 각각 1개씩 SENT다. replay 48,685 B, story 3,710 B, incident 27,277 B가 있었다. Driver schema 48–51은 LOCAL_PENDING_OWNER로 남아 있으며 정상적인 정책 차단이다.

### 결과 witness / 복구 가능 범위

`activity/witness/sessions/witness-5935d7cdb28e4eaa849905b5eb49de1f/`

- `upload-payload.json`: 227,673 B, SHA256 `04873250865119366f49adb5b86bc21f05cd6f6b669507dc4fd6b3157e8bcd5f`.
- `source-evidence.json.gz`: 45,443 B. 메모리에서만 압축 해제하여 구조를 확인했고 새 운영 파일을 쓰지 않았다.
- startingGrid 25명, raceResult 25명, 양수 bestLapSeconds 23명. 원본 25명에는 Safety Car가 포함된다.
- ENG-IceBlasT: position=23, lapsCompleted=4, bestLapSeconds=71.423706.
- MID_SESSION / RACE_RESTART / RESTARTED다. 완주 확정·공식 결과로 승격하지 않는다.
- witness 이벤트 543개: LAP_COMPLETE 268, PARTICIPANT_SNAPSHOT 25, PARTICIPANT_STATUS 75, PIT_TRANSITION 169, RACE_STATE 4, SESSION_START 1, SESSION_STATE 1.
- source-evidence에는 10:50:51.0729489~11:06:16.9684888 KST의 간헐적 스냅샷 33개, 참가자 상태 행 825개가 있다. 순위/랩/섹터 번호/직전·최고 랩/피트 상태를 부분 재구성할 수 있다.
- 그 참가자 행에는 worldPosition/worldX/Z/lapDistance가 없다. 따라서 좌표 보간으로 누락된 주행 경로를 만들어 복구됐다고 할 수 없다. 543개 witness 이벤트도 원래 Compact Race Story 전체와 동일하다는 보장은 없다.
- 별도 증거의 복구 가능성을 평가했을 뿐, 과거 자료를 새 Compact capture로 재포장하거나 서버 기록과 병합하지 않았다.

## 4. 저장 경로 재현과 수정

### 4.1 음수 거리의 변환 경계 불일치

`FutureTelemetrySnapshotAdapter.Event()`의 모든 참가자 Story 생성 호출부를 확인했다. Replay/Driver는 `NonNegativeFiniteOrNull(CurrentLapDistance)`를 사용하지만 Story만 `FiniteOrNull`을 사용했다. 음수 거리(재현값 -123)가 관측된 상태에서 순위 변경 Story가 생성되면, unsigned Compact distance 필드가 표현할 수 없는 값을 받는다.

Snapshot→Adapter fixture를 먼저 추가했고 **101 PASS / 1 FAIL**로 불일치를 재현했다. 공통 Story 생성부의 변환 한 줄을 기존 Replay 정책과 맞췄다. raw `TelemetrySnapshot`은 그대로 -123을 유지하고 Story/Replay의 표현 불가능한 거리만 null이 된다. 이벤트 자체·좌표·순위 변화는 보존한다. schema/범위/압축 설정을 바꾸지 않았다.

이것은 재현된 코드 결함이다. 과거 witness의 시간 필드에 -123이 존재한다는 사실만으로, 저장되지 않은 과거 Story의 **거리**도 -123이었다고 단정하지 않았다.

### 4.2 실패한 만료 청크가 다른 스트림을 막음

기존 흐름은 새 frame/story 추가 전에 `FlushExpired()`를 호출한다. Commit 실패 청크는 제거되지 않는데 FlushExpired는 첫 예외에서 종료됐다. 다음 입력에서도 같은 청크에 먼저 실패하여 정상 새 frame까지 처리되지 않는 구조다.

raw Story DTO의 distance=-123으로 encoder 실패를 주입했다. 수정 전 Activity **97 PASS / 1 FAIL**, 실패 사유는 `A failed expired Story blocked the later Replay frame.`였다. 이후 정상 Story와 Replay를 각각 1,100 ms에 넣어 저장되는지 검사했다.

변경:

- expired chunk별 실패를 격리하여 다른 청크와 현재 입력 처리를 계속한다.
- 실패 청크는 메모리에 유지하고 active 중 재시도 간격은 capture elapsed 기준 5초로 제한한다.
- flush/finalize는 모든 청크를 시도한 뒤 최초 예외를 전달한다. 정상 청크를 저장했다고 실패 attempt를 성공으로 바꾸지 않는다.
- 첫 실패 원본은 해당 세션의 `failed-chunks/<stream>/<index>-<sourceSHA>.source.json.gz`에 보존한다. 아직 유효 Compact가 되지 못한 DTO의 **로컬 복구용** 기존 P023 envelope다. 정상 Compact를 JSON으로 풀어 장기 저장하는 변경이 아니다.
- 복구 파일에는 upload sidecar가 없고 자동 전송하지 않는다. private 자료의 전송 권한도 확대하지 않는다. 보존 쓰기까지 실패하면 별도 진단을 남기며 성공 ACK를 만들지 않는다.
- 재시도 때 이미 누적한 dropped-input 수를 다시 되돌려 중복 가산하던 경로를 제거했다. 실패·loss 카운터를 0으로 지우지 않는다.

알려진 한계: 디스크 장애가 지속되면 실패 청크를 종료 시까지 메모리에 유지하므로 장시간 장애에 메모리 증가 가능성이 남는다. 이번에는 임의 drop/eviction이나 새 복구 서비스·큐를 도입하지 않았다. 디스크까지 가득 찬 상황에서 무손실 보존을 보장하지 않는다.

### 4.3 부분 파일 쓰기 이후 재시도

Compact 변환은 cadence cursor, 참가자 dictionary 전송 revision, geometry bin 등을 진행시킨다. 이전에는 payload 쓰기 후 sidecar 쓰기가 실패하면 재시도에서 artifact를 다시 만들어, 기존 payload와 다른 내용 또는 누락된 dictionary가 생길 수 있었다.

변환된 artifact를 해당 실패 source chunk에 보관하여 같은 바이트로 재시도한다. 실패한 artifact가 dictionary 운반자였으면 이후 정상 청크에서도 dictionary를 다시 보낼 수 있게 한다. 실제 fixture 디렉터리의 sidecar 위치를 임시 **빈 디렉터리**로 막아 실패를 주입하고, 그 테스트 디렉터리만 제거한 뒤 동일 source를 재시도했다. 기존 Compact 바이트 동일, decoder 성공, 참가자 dictionary 3명 유지, conflict 없음 PASS.

보존 원본/재시도 성공은 서로 다른 상태다. 원본을 보존했다고 durable Compact ACK를 먼저 기록하지 않는다. 최종 충돌 원본도 로컬 복구 영역에 남긴다.

## 5. 진단과 종료 상태

기존 `FileLogger.Info` 경로에 `ARCHIVE_FAILURE`, `UPLOAD_FORBIDDEN` 이벤트를 연결했다. 새로운 로깅 프레임워크는 없다.

Archive 진단은 attemptId, stage, stream, chunk index, 아는 경우 Compact schema/sequence, exception type/HResult, 안전한 codec field/range 메시지, 파일 경로/인수 없는 최대 8개 stack method를 기록한다. 일반 예외 메시지는 토큰·경로·원본 JSON 노출을 막기 위해 `UNTRUSTED_MESSAGE_OMITTED`로 표시한다. stack/오류 타입/HResult로 호출 위치와 OS 오류 종류를 추적한다.

같은 attempt/stage/stream/exception 종류는 최초 및 2의 거듭제곱 횟수에만 출력한다. 1,024회 실패 → 로그 11행 fixture PASS. 로그 쓰기 자체가 실패해도 수집/격리 처리를 깨지 않는다.

기존 계약을 유지했다:

- 실패는 로컬 attempt-loss ledger에 PARTIAL/finalize=false로 남는다. 다음 restart attempt에는 이전 loss/ID를 재사용하지 않는다.
- 성공 종료에만 기존 `LOSS_LEDGER_V1(80) → ATTEMPT_FINALIZE_V1(81)` 순서로 기록한다.
- archive finalize가 실패하면 현재 계약/구현상 80/81 모두 없을 수 있다. 성공을 가장하는 81을 만들지 않았다. 실패 종료를 전달할 새 schema/endpoint도 추가하지 않았다.
- 서버/웹은 81 미수신을 COMPLETE 또는 accepted/durable/loss=0의 근거로 쓰면 안 된다. 현재는 수신 종료 확인 없음으로 표시하고 운영자가 로컬 ledger와 진단을 대조해야 한다. 실패 ledger만 별도로 전달할 필요가 있으면 기존 80 단독 수신 의미를 웹 담당과 합의한 뒤 별도 작업할 것.
- Dispose 실패에도 그 전에 저장된 정상 청크 수가 runtime 진단 카운터에 반영되도록 했다.

## 6. 재시작 ID 연결

`sourceSessionId`는 Host Recorder가 생성한 결과 식별자여서 Compact `sessionId`와 다른 것 자체는 정상이다. 단, 현행 연결 계약에서는 witness 상단의 `captureSessionId/attemptId/attemptNumber`가 함께 제공되어야 동일 archive attempt를 명시적으로 연결할 수 있다.

이번 과거 payload에는 세 필드가 **아예 없다**. fingerprint만 같고 witnessId도 다르므로 시간·트랙만으로 강제 병합해서는 안 된다.

`client-20260906-101741.log`의 전이:

1. 10:50:50.521: InGameRestarting / Session Invalid / 참가자 0.
2. 10:50:51.010: InGameRestarting 중 Race/참가자 25가 먼저 나타남. witness가 HOST_SESSION_STARTED.
3. 10:50:51.064: 짧은 witness가 RACE_RESTART로 닫힘.
4. 10:50:51.073: 여전히 restarting 중 새 witness가 먼저 시작됨.
5. 10:50:51.105: InGameMenuTimeTicking에서 Future archive가 시작되는 경계.

기존 witness canStart는 FrontEnd만 제외해서 Restarting을 허용했지만 Future archive는 Restarting을 제외했다. 이미 시작한 witness에 `BeginArchiveIdentity`를 적용하면 예외가 나고 미연결 payload를 만들 수 있었다. 기존 구체 예외 로그는 없어 역사적 예외 발생 자체를 확정 로그라고 표현하지 않는다.

witness 시작에도 기존 `FutureTelemetryCaptureRuntime.IsCaptureScope`를 재사용했다. 정상 시작 후 restart 감지는 계속 유지한다. production의 `ReconcileWitnessArchiveIdentity/SynchronizePendingRestartIdentity` 호출 순서를 반영한 fixture에서 반복 Restarting → MenuTimeTicking → Playing → Restarting → 다음 attempt를 검사했다. 두 결과와 두 archive attempt가 각각 일치하고 identity notification failure=0이다. 과거 witness/서버 ID는 변경하지 않았다.

## 7. HTTP 403 진단 및 서버 인계

격리 항목:

```text
queueItemId: bd2c9f95bde410ab2f9d8f158838c3f7f479f2de2ca3f76d439fa5f4cd0b8f75
endpoint: v1/session/witness
idempotencyKey: witness:6e5cc07f2fbf6616e54a4e1e9ec972c8ef0974b13064ae0a58017ce52d535006
status: QUARANTINED
attemptCount: 1
lastHttpStatus: 403
lastResult: HTTP_403
lastAttemptAtUtc: 2026-09-06T02:06:34.0623394Z
```

작업 종료 확인에서도 이 상태와 파일 mtime은 변하지 않았다. 과거 응답 본문이 없어 앱 scope 거부인지 WAF/호스팅 거부인지 **NOT CONFIRMED**다.

로컬 서버 코드 읽기 확인: `Application.php`의 witness API는 `witnesses:write`를 요구한다. `Auth.php`는 scope 부족 시 JSON `SCOPE_FORBIDDEN`을 반환하고 Application은 16자리 hex requestId를 붙인다. migration 013은 기존 installation의 scopes에 witnesses:write를 보완한다. 이는 **운영 대상 installation에 실제 적용됐다는 확인이 아니다**.

과거 transport는 정상 JSON 오류의 error 문자열을 lastResult에 보존했으므로 `HTTP_403`만 남은 점은 HTML/빈 본문/다른 형식 가능성을 시사한다. 그러나 WAF 확정 증거는 아니다. payload 크기 227,673 B도 서버 수신 제한과 함께 확인할 정보이지 403 원인을 증명하지는 않는다.

이번 Client 변경:

- 403 본문 읽기 최대 4,096 B. JSON/HTML/OTHER 구분 및 allowlist 앱 오류 코드만 기록.
- Content-Type, requestId(제한된 hex/UUID 형식), 큐 항목 ID와 endpoint만 정제 기록. 전체 페이지/본문/임의 message는 복사하지 않는다. 자격증명과 같은 requestId도 제외한다.
- 이미 HTTP 403을 받은 뒤 본문 읽기가 실패/timeout하더라도 일반 network retry로 바꾸지 않는다. 사용자 취소는 전파한다.
- witness 403은 기존처럼 QUARANTINED. Compact 전송의 403도 영구 오류로 분류하여 무한 재시도를 막는다. 인증 scope를 넓히거나 토큰을 다시 만들지 않는다.
- `duplicate=true`라는 오류 본문만으로 403/5xx를 SENT로 오판하지 않도록 duplicate를 성공 HTTP에 한정했다.
- JSON scope 오류 / HTML / oversized HTML / 자격증명 echo / 읽을 수 없는 본문을 fixture로 검증했다. 각각 두 번째 worker poll에서 attempted=0, 재등록=0, 원본 payload/hash/멱등성 키 유지. 기존 201 성공/일시 오류 테스트도 PASS.

서버 담당이 먼저 할 읽기 전용 확인(토큰 또는 token hash 조회 불필요):

```sql
SELECT id, installation_uuid, enrollment_kind, scopes
FROM client_installations
WHERE installation_uuid = 'client-02de771497ea449082c44c38d58a9955';

SELECT version, applied_at FROM schema_migrations
WHERE version = '013_distributed_session_witness';
```

같은 UTC 시각/route에 대해 PHP application과 Cafe24/WAF 로그를 대조하고, PHP 도달 여부·응답 Content-Type·오류 code/requestId를 확보한다. scope가 없을 때만 서버 담당의 승인된 migration/권한 보완 절차를 따를 것. `results:write`/공식 결과 권한은 요청하지 않는다. 이번에는 위 SQL도 운영에서 실행하지 않았다.

### 승인 후 단일 항목 재전송 절차 — 이번에는 실행하지 않음

1. 서버 원인 해결과 사용자 재전송 승인을 먼저 확보한다. 위 큐 항목의 payload/metadata/state를 별도 백업하고 본문 SHA256을 다시 확인한다.
2. 서버 담당이 installation + 기존 witnessId/idempotencyKey/body hash로 이미 저장된 결과가 있는지 확인한다. 동일 본문이면 재전송도 같은 키를 사용한다. 다른 본문이면 중단한다.
3. 승인된 단일 항목 재전송 도구에서 해당 항목만 읽는다. 새 witness/payload/키를 생성하지 않는다. 현재 credential이 없으면 중단하며 pairing 해제·토큰 재발급·anonymous 재등록하지 않는다.
4. 동일 endpoint/body/멱등성 키로 한 번만 요청한다. QUARANTINED 전체를 PENDING으로 초기화하거나 전체 queue worker를 강제로 돌리지 않는다.
5. 정제된 응답과 서버 저장 row/body hash를 확인한다. 200/201만으로 완료 판정하지 않는다. 서버 결과 상세의 원본 25명과 값·중복 행 없음도 대조한다.
6. 실제 확인된 단일 요청 결과만 그 큐 항목의 기존 상태 전이로 기록한다. 성공 시 SENT, 계속 403이면 QUARANTINED 유지. 자동 무한 재시도 금지. 기존 GENERAL/LEAGUE/공식 승인 상태는 건드리지 않는다.

단일 재전송 도구의 실행/제작은 후속 승인 범위다. 이 문서는 운영 state.json을 수동 편집하라는 지시가 아니다.

## 8. 검증 및 비용 측정

실행 중 앱과 분리한 Release 전체 빌드 및 두 suite:

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/Archive403Hotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/Archive403Hotfix/AMS2LeagueClient.Tests.dll
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/Archive403Hotfix/AMS2LeagueActivity.Tests.dll
```

최종 build warnings/errors=0/0, Client=89/89, Activity=102/102. 기존 UI 관련 미커밋 변경까지 포함한 전체 suite 결과다. `git diff --check` PASS.

추가/강화한 검사는 음수 Story domain, expired 실패 격리, 원본 복구 파일, 실제 부분 파일 쓰기/동일 바이트 재시도, 진단 rate-limit/비밀 비노출, 재시작 witness 연결, 실패 후 다음 attempt 독립, 403 분류/격리/멱등성 및 성공·일시 오류 유지다.

### 수정 전/후 동일 입력 바이트 대조

기존 `work/replay-cadence-cost`의 production store 호출 방식을 재사용한 `work/archive403-proof/ArchiveProbe.csproj`다. 기존 P023 60분/32대 fixture의 284개 source chunk를 원본 수정 없이 각각의 DLL로 변환했다. 네트워크/SHM을 사용하지 않는다.

| 항목 | 수정 전 | 수정 후 |
|---|---:|---:|
| source chunks | 284 | 284 |
| Compact files, 모두 decode 성공 | 647 | 647 |
| Compact gzip 전체 | 1,999,401 B | 1,999,401 B |
| 공개 스트림 대상 바이트(실제 업로드 아님) | 882,891 B | 882,891 B |
| private Driver 바이트(정책상 미전송) | 1,116,510 B | 1,116,510 B |
| Replay gzip | 860,579 B | 860,579 B |
| Replay rows / world rows | 231,360 / 231,360 | 231,360 / 231,360 |
| gzip SHA/content SHA/크기/행 수 차이 | — | 0 / 647 |

Core DLL SHA256:

- 전: `5cf46f7b56cd7bbb4f9056574fc5cdb575ca298887d078a097fbb8f7d175aa0f` (`PrestartProximityHotfix`).
- 후: `3bbdab4470a5f7536ff9c1ca56401eb6bf5c0b3e7cf95492ac14da57364c6dfe` (`Archive403Hotfix`).

cadence는 progress/world/extension/battle=2,000/500/20,000/500 ms, inner replay=200 ms, driver/incident=50 ms 그대로다. cadence fixture에서도 3대×30초 world rows=270(500 ms), 162(5,000 ms), progress 불변을 확인했다.

측정 JSON: `work/archive403-proof/before-v2/measurement.json`, `work/archive403-proof/after-verified/measurement.json`. 이 비교는 기존 source의 **30초 packing**이며, shipping `ActivityCaptureRuntime`의 300초 packing과 같다고 주장하지 않는다. 저빈도 legacy metadata와 close 80/81을 포함한 현재 제품 전체 비용 게이트의 대체 측정도 아니다. 해당 close 계약은 별도 runtime regression으로 검증했다.

위 fixture 전체는 512 KiB/1 MiB 기준 모두 초과한다. 공개 부분만은 1 MiB 미만이지만 512 KiB 초과다. 수정 전후 차이는 0이며, 기존 `465,279 B`의 오래된 proof 수치를 현재 제품 PASS 근거로 재사용하지 않았다. `world 500 ms / shipping packing`으로 정식 size gate를 정렬하는 기존 후속 과제는 남아 있다. 이번 수정을 숨기기 위한 cadence 축소·압축 품질 변경·gate 완화는 없다. 실제 성능/CPU 고수위는 NOT RUN이다.

재현 probe는 `CoreDll` MSBuild 속성에 비교할 DLL 절대 경로를 넣어 `bin/before` 또는 `bin/after`에 build하고, 아래처럼 반드시 새 출력 디렉터리를 지정한다(기존 결과 덮어쓰기 거부).

```powershell
.\work\dotnet8\dotnet.exe work/archive403-proof/bin/after/ArchiveProbe.dll `
  work/p024/p023-baseline-60m32/sessions/df32057030a86a7324152a1f6d17c3cf/chunks `
  work/archive403-proof/another-new-output
```

## 9. 이번 변경 파일과 다음 단계

```text
src/AMS2LeagueClient.Core/Diagnostics/ArchiveFailureDiagnostics.cs
src/AMS2LeagueClient.Core/FutureTelemetry/LocalDurableTelemetryArchive.cs
src/AMS2LeagueClient.Core/FutureTelemetry/CompactTelemetryChunkStore.cs
src/AMS2LeagueClient.Core/FutureTelemetry/FutureTelemetryCaptureRuntime.cs
src/AMS2LeagueClient.Core/FutureTelemetry/FutureTelemetrySnapshotAdapter.cs
src/AMS2LeagueClient.Core/SessionWitness/SessionWitnessCaptureEngine.cs
src/AMS2LeagueClient/Runtime/ActivityCaptureRuntime.cs
src/AMS2LeagueClient/Runtime/Cafe24ActivityUploadTransport.cs
tests/AMS2LeagueClient.Tests/Program.cs (기존 변경에 추가)
tests/AMS2LeagueActivity.Tests/FutureTelemetryArchiveTests.cs
tests/AMS2LeagueActivity.Tests/FutureTelemetryRuntimeAdapterTests.cs
docs/ARCHIVE_FAILURE_WITNESS_403_2026-09-06_KO.md
work/archive403-proof/* (로컬 측정 도구/결과, gitignored)
```

Ponytail 지침에 따라 기존 domain helper, logger, archive/queue/codec 계약을 재사용했다. 새 저장 서비스·재전송 큐·서버 endpoint는 만들지 않았다.

다음 운영 검증은 사용자 승인 후에만 한다. 사용자가 주행을 끝낸 뒤 기존 앱을 정상 종료하고 수정 후보로 교체하는 절차가 필요하다. 이후 새 capture의 같은 attemptId로 local artifact/ledger → 실제 HTTPS → 서버 schema/원본 GET hash → 웹 결과를 대조해야 한다. 이번 코드 변경만으로 실행 중 앱이 수정되지는 않는다.

웹 담당은 별도로 80/81 미수신의 “전체 기록/0 loss” 오판과 관리자 2열 결과표 잘림을 처리한다. 운영 `20260906-002`의 복구된 정상 reader와 로컬 실패 후보를 혼동하여 배포하지 말 것.
