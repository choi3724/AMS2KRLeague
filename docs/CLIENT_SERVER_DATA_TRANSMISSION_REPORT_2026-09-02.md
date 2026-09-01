# AMS2 League Overlay 클라이언트 → 서버 전송 데이터 분석 보고서

- 작성일: 2026-09-02 KST
- 분석 대상: AMS2 League Overlay 공개 클라이언트 0.2.1 및 0.2.2 작업 트리
- 서버 대상: Cafe24 AMS2 API, DB migration 013 기준
- 분석 방법: 클라이언트의 실제 HTTP 전송 코드와 JSON 생성 코드, 서버 validator/normalizer/store 및 MariaDB schema를 교차 확인

## 1. 결론

클라이언트가 Cafe24 서버에 보내는 애플리케이션 데이터는 크게 세 종류다.

1. **익명 설치 등록**: 설치본을 구별하고 업로드 권한 토큰을 발급받기 위한 최소 식별 정보
2. **개인 활동 기록**: 해당 사용자의 Race 결과 또는 Time Attack 1랩 기록
3. **Session Witness**: 여러 참가자가 보인 세션의 예선·출발 순서·레이스 전체 순위와 제한된 사건/날씨 기록

오버레이가 화면에 표시하기 위해 읽는 30 Hz 공유 메모리 전체가 그대로 서버로 스트리밍되지는 않는다. 클라이언트는 로컬에서 최종 결과와 의미 있는 상태 변화만 묶고, 완료된 JSON을 HTTPS로 전송한다. 다만 Session Witness에는 **다른 참가자의 AMS2 닉네임, 차량, 클래스, 순위, 랩 및 상태**가 포함되므로 단순한 개인 랩타임 전송보다 범위가 넓다.

공개 클라이언트가 보낸 Witness는 서버에 들어온 즉시 공식 리그 결과가 되지 않는다. 서버는 원본을 보존하고 결과표를 정규화하지만, 최초 상태는 `UNCLASSIFIED / PENDING_APPROVAL / INTERNAL`이다. 관리자의 별도 승인 전에는 공식 `LEAGUE` 결과 또는 공개 `GENERAL` 결과가 아니다.

중요한 제한도 있다. AMS2 Shared Memory v14에는 신뢰할 수 있는 `multiplayer=true/false` 값이 없다. 현재 Witness 수집 시작 조건은 “Practice/Test/Qualifying/Formation/Race 중이며 참가자가 2명 이상이고 Front End가 아님”이다. 따라서 **멀티플레이어일 가능성이 높은 세션**은 선별하지만 AI가 여러 대인 싱글플레이어를 기술적으로 완전히 배제하지는 못한다. 이것은 현재 계약에서 가장 중요한 판정 한계다.

## 2. 전송 흐름

```text
AMS2 Shared Memory v14 (읽기 전용)
        │
        ├─ 화면 표시용 실시간 값 ───────────────> 오버레이 UI
        │
        └─ 클라이언트 내부 집계
              ├─ 개인 Race / Time Attack 완료 기록
              └─ 다중 참가자 Session Witness
                        │
                        ├─ 로컬 불변 JSON/재전송 큐 저장
                        └─ HTTPS POST + 설치 토큰 + 중복방지 키
                                      │
                                      v
                              Cafe24 API validator
                                      │
                        ┌─────────────┴─────────────┐
                        │                           │
                 gzip 원문 보존              MariaDB 정규화
                        │                           │
                        └──── 관리자 검토/승인 ────┘
```

네트워크 오류가 나도 게임 읽기 루프가 서버 응답을 기다리지 않는다. 완성된 항목은 먼저 로컬 큐에 저장되고, 별도 업로드 작업이 약 5초 간격으로 재시도한다. 동일 항목의 재전송은 `Idempotency-Key`와 본문 SHA-256으로 중복 저장 및 변조를 검사한다.

## 3. 사용 API와 실제 역할

| 동작 | HTTP 경로 | 클라이언트가 보내는 것 | 목적 |
|---|---|---|---|
| 익명 등록 | `POST v1/player/enroll` | schema, installationId, clientVersion | 계정 로그인이 없어도 설치본별 업로드 권한 발급 |
| 리그 일정 확인 | `GET v1/bootstrap` | JSON 본문 없음 | 현재 예정 경기·수집 가능 시간·예상 클래스 수신 |
| 서버 상태 확인 | `GET v1/health` | JSON 본문 없음 | API/DB schema 상태와 서버 버전 확인 |
| 개인 기록 | `POST v1/player/activities` | 개인 Race 요약 또는 Time Attack 랩 | 사용자 개인 활동 및 일반 기록 후보 저장 |
| 세션 증거 | `POST v1/session/witness` | 세션 결과표, 참가자 명단, 사건, 날씨 | 여러 클라이언트 관찰값 대조 및 결과표 정규화 |

현재 공개 클라이언트에는 별도의 지속적인 `presence`, 위치 추적 또는 프레임 단위 telemetry 업로드 호출이 없다.

## 4. 익명 설치 등록 데이터

### 4.1 전송 필드

| 필드 | 실제 의미 | 서버 사용처 |
|---|---|---|
| `schema` | 등록 요청 계약 버전 (`ams2-anonymous-enrollment-v1`) | 잘못된 형식/구버전 요청 식별 |
| `installationId` | 설치 시 생성되는 `client-<GUID>` 형태의 임의 ID | 같은 PC 사용자 설치본의 업로드 소유권 및 중복 방지 기준 |
| `clientVersion` | 실행 중인 오버레이 버전 | 호환성·장애 분석 |

`installationId`는 Steam ID나 Windows 사용자명이 아니다. 계정 연결 전에는 서버 `driver_id`가 비어 있으며 기록은 설치 ID에 귀속된다. 향후 신원 연결 시 서버가 설치본과 드라이버를 조정할 수 있도록 만든 구조다.

### 4.2 인증 토큰

서버가 돌려준 bearer token은 이후 두 POST 요청의 `Authorization` 헤더와 Cafe24 FastCGI 호환용 `X-AMS2-Authorization` 헤더에 실린다. 토큰은 JSON 기록에 포함되지 않으며 로그에도 평문으로 쓰지 않는다. 로컬에서는 Windows DPAPI `CurrentUser`로 보호한 `pairing-token.dat`에 저장한다.

통신은 HTTPS만 허용하며 HTTP redirect는 꺼져 있다. 이는 토큰이 다른 호스트로 자동 전달되는 것을 막기 위한 조치다.

## 5. 개인 활동 기록 (`ams2-player-activity-v2`)

개인 활동은 “한 사용자의 결과”다. 전체 경기 결과표를 공식 결과처럼 주장하는 payload가 아니다.

### 5.1 공통 정보

| 정보 | 필드 | 무엇에 해당하는가 | 서버에서 하는 일 |
|---|---|---|---|
| 활동 식별 | `activityId` | 특정 Race 시도 또는 Time Attack 랩 묶음의 불변 ID | 재전송 중복 및 충돌 검사 |
| 활동 종류 | `activityType` | `RACE` 또는 `TIME_ATTACK` | Race 요약/TA 랩 저장 경로 선택 |
| 분류 힌트 | `recordScope` | 클라이언트는 항상 `UNCLASSIFIED`로 제출 | 클라이언트가 공식성을 결정하지 못하게 함. 개인 활동 DB에는 서버 정책상 `GENERAL`로 정규화 |
| 일정 힌트 | `scheduledEventHint` | bootstrap에서 받은 예정 경기 ID와 관찰 세션의 연결 후보 | 서버 자동 매칭 참고값일 뿐 공식 판정 아님 |
| 세션 연결 | `sessionId`, `sessionFingerprint` | 같은 트랙·차량·시도에 속하는 기록을 연결하는 해시 기반 키 | 중복/재시작/세션 연결 |
| 증거 무결성 | `evidenceSha256` | 로컬에서 만든 근거의 내용 해시 | 동일 ID에 다른 내용이 들어오는지 검사 |
| 사용자 표시명 | `observedName` | AMS2가 공유 메모리로 보여 준 로컬 플레이어 닉네임 | 사용자 기록 표시 및 추후 드라이버 매칭 보조 |
| 장소 | `track`, `layout` | 서킷과 레이아웃 이름 | 트랙별 기록 검색·표시 |
| 차량 | `vehicle`, `vehicleClass` | 로컬 플레이어의 차량명과 클래스 | 차량/클래스별 기록 검색·표시 |
| 모드 | `raceMode` | `MULTIPLAYER`, `SINGLE_PLAYER`, `UNKNOWN` | 기록 성격 분류. SHM 한계 때문에 `UNKNOWN`일 수 있음 |
| 시간 범위 | `startedAtUtc`, `endedAtUtc` | 관찰된 활동 시작/종료 UTC | 경기 시각, 소요 범위, 중복 판정 |
| 설정 | `configuredSettings` | 세션 시간/랩 수/의무 피트 등 “설정값”과 각 값의 신뢰 상태 | 실제로 노출된 값과 추정 불가 값을 구분해 보존 |
| 관찰 조건 | `observedConditions` | 세션 종류, 실제 시작/종료, 비공개 여부, 날씨 변화 | 경기 환경 설명 및 검토 자료 |
| 버전 | `captureVersion`, `gameVersion`, `clientVersion` | 수집기 계약, AMS2 build, 오버레이 버전 | 파서 호환성 및 오류 추적 |

`configuredSettings`의 각 값에는 `ConfirmedLive`, `ObservedOnly`, `NotExposed`, `NotSupported`, `Unknown` 같은 상태가 붙는다. 예를 들어 공유 메모리에서 날씨 슬롯 구성 자체를 알 수 없으면 임의의 값을 만들지 않고 `NotExposed`로 보낸다. “값이 없음”과 “기능이 꺼짐”을 구분하기 위한 장치다.

`observedConditions.weatherTimeline`은 매 프레임이 아니라 약 1분 간격 또는 온도·강우가 의미 있게 바뀐 시점의 관측값이다. 항목은 시각, 세션 경과 시간, 기온, 노면 온도, 강우, 풍속/방향, 구름 밝기, 적설 밀도를 담는다.

### 5.2 Race 개인 결과

| 필드 | 의미 | 주의점 |
|---|---|---|
| `position` | 로컬 플레이어의 종료 시점 Race position | 전체 공식 순위가 아니라 본인 위치 |
| `participantCount` | 해당 시도에서 관찰한 고유 참가자 수 | 현재 개인 기록 builder에서는 Safety Car 포함 여부를 따로 정규화하지 않음 |
| `rawParticipantCount` | 원본 참가자 수를 표현하려는 필드 | 현재 구현에서는 `participantCount`와 같은 값을 보냄. 별도 League 분모가 아님 |
| `lapsCompleted` | 로컬 플레이어 완료 랩 수 | 종료/이탈 시점 기준 |
| `bestLapSeconds` | 로컬 플레이어 최고 랩 | AMS2 값이 유효할 때만 전송 |
| `resultState` | Finished, DNF, Retired, Disqualified 등의 AMS2 종료 상태 | 개인 결과 상태 표시/검토 |

서버는 공통 메타데이터를 `player_activities`, 이 요약을 `personal_race_summaries`에 저장한다.

### 5.3 Time Attack 랩

| 필드 | 의미 | 서버 사용처 |
|---|---|---|
| `lapId` | 해당 완주 랩의 불변 ID | 중복/충돌 검사 |
| `lapNumber` | 세션 내 랩 순번 | 기록 맥락 표시 |
| `completedAtUtc` | 랩 완료 UTC | 최신 기록 및 시각 정렬 |
| `lapTimeSeconds` | 전체 랩타임 | 유효 랩 PB 비교 |
| `sector1/2/3Seconds` | 각 섹터 시간 | 섹터 분석 |
| `valid` | AMS2 무효화 latch를 반영한 유효 여부 | 무효 랩의 PB 제외 |
| `invalidReason` | 무효일 때 클라이언트가 보존한 사유 | 사용자 설명/검토 |

서버는 이를 `time_attack_laps`에 저장한다. 계정/드라이버와 연결된 설치인 경우 서버가 유효 랩 중 개인 최고 기록을 계산할 수 있다.

## 6. Session Witness (`ams2-session-witness-v1`)

Witness는 다른 클라이언트가 같은 세션을 어떻게 보았는지 비교하기 위한 “관찰 증언”이다. 개인 활동보다 전송 범위가 크다.

### 6.1 세션 및 그룹 식별 정보

| 필드 | 실제 의미 | 사용 목적 |
|---|---|---|
| `witnessId` | 이 클라이언트가 완성한 Witness 한 건의 ID | 같은 설치본의 중복/충돌 방지 |
| `sessionFingerprint` | 트랙·레이아웃·클래스·구성·일정 힌트로 만든 세션 해시 | 서로 다른 클라이언트의 동일 세션 후보 그룹화 |
| `eventFingerprint` | 이벤트 수준의 해시 | 재시작을 포함한 같은 경기 묶음 보조 |
| `rosterSignature` | 정규화·정렬한 참가자명 목록의 해시 | 같은 로스터를 관찰했는지 비교 |
| `rosterNames` | 실제 관찰된 참가자 AMS2 닉네임 목록 | 이름 매칭, 로스터 검사, 분쟁 확인 |
| `sourceClientId` | 익명 설치 ID | 어느 설치본이 보낸 관찰인지 확인. bearer 소유 설치와 같아야 함 |
| `sourceRole` | 공개 클라이언트는 `PLAYER` | Host/Player 관찰 출처 구분 |
| `captureStarted/EndedAtUtc` | 실제 관찰 구간 | 완전 수집/중간 진입 판단 |
| `estimatedSessionStartedAtUtc` | 첫 Race 관측의 세션 경과 시간을 역산한 시작 후보 | ±시간 창으로 같은 세션 Witness 그룹화 |
| `captureCompleteness` | `FULL_SESSION`, `MID_SESSION`, `END_ONLY`, `UNKNOWN` | 결과 신뢰도와 대표 Witness 선정 보조 |
| `qualityScore` | 완전성, Q/Grid/Race 보유 여부, 로스터 수를 반영한 클라이언트 점수 | 서버는 그대로 신뢰하지 않고 validator에서 재계산/제한 |
| `scheduledEventHint` | 현재 예정 이벤트 후보 | 자동 분류 제안. 공식 결정권 없음 |
| `vehicleClass` | 관찰 세션의 대표 클래스 | 그룹화 및 결과 표시 |

Fingerprint와 signature는 비밀번호가 아니라 그룹화/비교용 SHA-256 값이다. 닉네임 원문을 대신하는 익명화 장치로만 볼 수는 없다. `rosterNames`와 결과표에 원문 닉네임도 함께 전송되기 때문이다.

### 6.2 세션 결과 본문

`session`에는 다음이 들어간다.

- `sourceSessionId`: 로컬 recorder가 만든 세션 ID
- `parserVersion`, `sharedMemoryVersion`, `ams2Build`: 어떤 파서와 게임 데이터 계약으로 읽었는지
- `startedAtUtc`, `endedAtUtc`: 로컬 recorder 기준 세션 범위
- `track`, `layout`: 서킷 정보
- `sessionTypesObserved`: Practice/Qualifying/Formation/Race 중 실제로 목격한 단계
- `reliability`: Verified/Provisional/Quarantined 판정
- `evidenceSha256`: 세션 결과 근거의 내용 해시
- `closingReason`, `attemptStatus`: 정상 종료, 재시작, 게임 종료, 이탈 등의 완료 맥락
- `qualifying`: 예선 최종 분류표가 확보되었을 때의 전체 참가자 결과
- `startingGrid`: Race 직전 출발 순서 스냅샷
- `raceResult`: Race 종료 시 전체 참가자 결과
- `issues`: recorder가 발견한 누락·불안정·판정 문제

Qualifying, Starting Grid, Race Result의 참가자 행에는 아래 값이 포함될 수 있다.

| 참가자 값 | 무엇에 해당하는가 |
|---|---|
| `slot`, `generation`, `active`, `disappeared` | AMS2 참가자 배열 슬롯의 재사용과 출현/이탈 추적 |
| `nameSnapshot` | 그 시점의 AMS2 닉네임 |
| `position` | 해당 단계의 순위/그리드 위치 |
| `lapsCompleted`, `currentLap`, `currentSector` | 진행 및 결과 복원용 랩 상태 |
| `lastLapSeconds`, `bestLapSeconds` | 마지막 랩과 최고 랩 |
| `resultState` + raw | Finished/DNF/DSQ 등 해석값과 원본 enum |
| `pitState` + raw | 피트 진입/정차/이탈 상태와 원본 enum |
| `vehicle`, `vehicleClass` | 참가자별 차량과 클래스 |
| `firstSeenUtc`, `lastSeenUtc` | 관찰 범위와 중도 참가/이탈 판단 |

AMS2 Shared Memory가 공식 전체 Race time과 최종 gap을 안정적으로 제공하지 않는 경우, `officialTotalRaceTimeSeconds`와 `officialFinalGapSeconds`는 비어 있고 source가 `NOT_SUPPORTED`로 기록된다. 값이 없는데 추정값을 공식 기록처럼 만들지 않는 설계다.

Safety Car는 Witness 원문에 참가자 행으로 남을 수 있지만 서버의 classification signature와 리그 정규화 결과에서는 Safety Car로 판정된 행을 제외한다.

### 6.3 사건 타임라인

`events`는 프레임 로그가 아니라 상태 변화 기록이며 최대 4,096행이다.

| 사건 | 의미 |
|---|---|
| `SESSION_START` | Witness 수집 시작 |
| `SESSION_STATE` | Practice/Qualifying/Formation/Race 등 단계 변화 |
| `RACE_STATE` | Not Started/Racing/Finished 등의 Race 상태 변화 |
| `PARTICIPANT_SNAPSHOT` | 새 슬롯 또는 이름 변경 참가자 최초 관찰 |
| `LAP_COMPLETE` | 참가자 완료 랩 카운터 증가 |
| `PARTICIPANT_STATUS` | 참가자의 Race result/state 변화 |
| `PIT_TRANSITION` | 피트 상태 변화 |
| `PARTICIPANT_MISSING` | 기존 참가자가 관찰 목록에서 사라짐 |

각 행은 발생 UTC, 참가자 슬롯/그때의 이름, 랩, 이전·현재 raw state 및 세부 설명을 가질 수 있다.

### 6.4 날씨 타임라인

`weather`는 최대 1,440행이며, 첫 관찰 후 60초 간격 또는 강우 0.05, 기온/노면 온도 1도 이상의 변화 때 기록한다.

- 발생 UTC 및 세션 경과 초
- 기온/노면 온도
- 강우 밀도
- 풍속과 X/Y 방향
- 구름 밝기
- 적설 밀도

따라서 “현재 날씨의 변화 과정”은 복원할 수 있지만 매 프레임의 날씨 원본은 아니다.

## 7. 서버가 받은 후 저장하는 방식

### 7.1 개인 활동

1. `player_activity_ingests`에 설치 ID, endpoint, idempotency key, 본문 SHA-256, 응답, 수신 시각과 gzip 원문을 저장한다.
2. 공통 활동 메타데이터는 `player_activities`에 정규화한다.
3. Race이면 `personal_race_summaries`, Time Attack이면 `time_attack_laps`에 세부 결과를 저장한다.
4. 개인 활동의 DB `record_scope`는 서버 정책으로 `GENERAL`이다. 이것은 개인 일반 기록 범주이며 Session Witness의 공식 경기 승인과는 별개다.

### 7.2 Session Witness

1. `session_witnesses`에 설치 ID, Witness/세션 ID, 품질, roster/classification signature, 관찰 시각과 gzip 원문을 저장한다.
2. 같은 session fingerprint와 근접한 시작 시각의 관찰을 `session_witness_groups`로 묶는다.
3. 여러 Witness의 단계별 결과 signature를 비교해 `SINGLE_SOURCE`, `CONSISTENT`, `PARTIAL`, `CONFLICT` 중 하나를 계산한다.
4. Race 결과가 정규화 가능한 Witness라면 canonical `sessions`, `session_participants`, `session_results`를 만든다.
5. Qualifying과 Starting Grid는 `session_classifications`와 `classification_results`에 별도 저장한다. 일반/리그 분류가 결과 행을 대신 생성하지 않는다.
6. 결과의 초기 범위는 `UNCLASSIFIED`, 승인 상태는 `PENDING_APPROVAL`, 노출은 `INTERNAL`이다.
7. 관리자가 검토 후 `LEAGUE` 또는 `GENERAL`로 승인해야 해당 공개 화면/통계에 들어간다.

서버는 원본 JSON을 gzip으로 보존하므로 정규화 테이블에 복사되지 않은 사건·날씨·원본 enum도 감사/재처리에 사용할 수 있다. 즉 “정규화된 몇 개 열만 남는다”는 구조는 아니다.

## 8. 보내지 않는 정보

현재 애플리케이션 JSON 계약에는 다음 정보가 없다.

- Windows 로그인 사용자명
- 이메일 주소, 사이트 비밀번호, Cafe24/FTP/DB 비밀번호
- Steam ID/SteamID64
- PC 하드웨어 serial, MAC 주소
- 키보드·마우스·휠·패드 입력
- 화면 캡처, 영상, 음성, 채팅
- 임의의 Documents/AppData 파일
- 연료량, 스로틀, 브레이크, 스티어링 등 30 Hz 주행 telemetry 원본 스트림
- GPS/실제 위치
- 오버레이 UI 위치/크기 설정

단, HTTPS 연결 자체 때문에 Cafe24 웹서버/프록시는 일반적인 웹 요청과 마찬가지로 접속 IP, TLS/HTTP 메타데이터, User-Agent, 접근 시각을 볼 수 있다. 현재 애플리케이션 DB schema가 IP를 활동/결과 열로 의도적으로 저장하지는 않지만, Cafe24 또는 웹서버 access log의 보존 정책은 이 소스 저장소만으로 단정할 수 없다.

## 9. 데이터별 개인정보·운영 영향

| 데이터 | 민감도 | 이유 | 권장 취급 |
|---|---|---|---|
| installationId | 중간 | 실명은 아니지만 한 설치본의 장기 활동을 연결 가능 | 공개 화면 비노출, 운영자 접근 제한 |
| bearer token | 높음 | 해당 설치본의 업로드 권한 | 헤더로만 전송, DPAPI 보관, 로그/보고서 금지 |
| 본인 AMS2 닉네임 | 중간 | 온라인 가명이나 개인 식별에 사용될 수 있음 | 사용자 고지, 정정/연결 절차 필요 |
| 전체 roster 닉네임 | 중간 | 다른 참가자의 온라인 가명 포함 | 결과 검증 목적 한정, 승인 전 내부 보관 |
| 경기 결과·차량·랩타임 | 낮음~중간 | 공개 리그 활동 정보가 될 수 있음 | 공식/일반/내부 범위 분리 |
| 사건·피트·이탈 시각 | 중간 | 경기 행동을 상세 복원 가능 | 분쟁/검증 목적, 보존 기간 정의 권장 |
| 날씨 | 낮음 | 게임 내 환경 정보 | 결과 맥락용 |
| gzip 원문 | 높음 | 위 데이터를 한 번에 재구성 가능 | DB backup 포함 접근 통제·보존 정책 필요 |

## 10. 확인된 주의점과 권고

### A. “멀티플레이어 Race에서만 증거 수집”은 아직 완전 증명되지 않는다

현재 Witness 시작은 참가자 2명 이상을 멀티플레이어의 대리 조건으로 사용한다. AI가 포함된 싱글플레이어도 같은 조건을 충족할 수 있다. 또한 하나의 경기 흐름을 복원하기 위해 Practice/Test/Qualifying/Formation/Race 단계부터 수집할 수 있다. 실제 Race 결과를 확보하기 위한 설계이지만 문구 그대로의 “Race 단계에서만”과는 다르다.

권고: AMS2 자체에서 authoritative multiplayer flag를 얻을 수 없다면 다음 중 하나를 추가해야 한다.

- 사용자/호스트가 참여한 서버 세션을 확인할 수 있는 별도 Steam/서버 식별 신호
- bootstrap의 capture window + scheduled event + roster 조건을 모두 만족할 때만 League Witness 활성화
- 일반 Witness는 내부 보관하되 공식/공개 후보는 서버측 다중 Witness 합의 또는 관리자 승인 필수

현재의 `UNCLASSIFIED / PENDING_APPROVAL` 안전장치는 오인 수집이 곧 공식 기록이 되는 것을 막지만, 수집 자체를 막는 장치는 아니다.

### B. 개인 Race 참가자 수의 `raw`/정규화 구분이 없다

개인 활동 payload의 `participantCount`와 `rawParticipantCount`는 현재 동일한 값이다. Safety Car 제외 League 분모가 필요한 화면에는 개인 활동 요약이 아니라 서버가 Safety Car를 제외해 정규화한 Session Witness 결과를 사용해야 한다.

### C. 익명 설치는 곧 회원 계정이 아니다

익명 등록 성공은 업로드 권한이 생겼다는 뜻이지 로그인/회원/드라이버 연결이 끝났다는 뜻이 아니다. 계정 연결 전의 `driver_id`는 null이며 웹 개인 기록 기능은 설치 기록과 회원 신원을 조정하는 절차가 추가로 필요할 수 있다.

### D. 원문 보존 정책을 문서화해야 한다

`payload_gzip`에는 닉네임·전체 결과·사건·날씨가 남는다. 운영 전 관리자 접근 범위, backup 포함 보존 기간, 삭제/정정 절차를 정책으로 정하는 것이 좋다.

## 11. 추적 근거

이 보고서는 필드 이름만 검색해 작성하지 않고 다음 경로를 실제 데이터 흐름 순서로 대조했다.

- 클라이언트 전송 계약: `ActivityUploadPayloadBuilder.cs`, `SessionWitnessUploadPayloadBuilder.cs`
- 수집 조건과 의미: `ActivityCaptureEngine.cs`, `SessionWitnessCaptureEngine.cs`
- 로컬 큐와 전송 시점: `ActivityCaptureRuntime.cs`
- HTTPS/인증/endpoint 제한: `Cafe24ActivityUploadTransport.cs`
- 서버 검증: `ActivityValidator.php`, `SessionWitnessValidator.php`, `ResultValidator.php`
- 서버 정규화/저장: `PdoStore.php`
- DB 구조: migration `006_p1d4_activity_capture`, `007_p1d4_official_workflow`, `013_distributed_session_witness`

본 보고서의 “현재 서버 동작”은 위 저장소의 서버 release 013 계약을 기준으로 한다. Cafe24 운영 서버의 access log 보존 설정이나 저장소 밖의 호스팅 정책은 별도 관리 화면/호스팅 약관 확인이 필요하다.
