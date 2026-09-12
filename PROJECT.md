# 0.7.2 릴리즈

사용자 승인으로 누적 N 계기판·공통 RPM90/97·Monitor 갱신 개선·업데이트 안내를0.7.2로 배포한다. 실게임·성능 NOT TESTED 및 Monitor RED 유지. [릴리즈 검증](docs/reports/2026-09-13-release-0.7.2.md). 아래는 이전 단계 이력이다.

# 최신 결과 — 최종 승인 공통 RPM90/97 정책

내장 경고 프로필을 제거하고 모든 미보정 차량에 mMaxRPM90% 노랑/97% 빨강·200ms 점멸·외곽5쌍 완등을 적용했다. 저장된 수동 보정 우선/해제 시 공통 복귀, 차량 기준 수명과 일반·확장 WPF 검증. Client158/158·Activity111/111·Release warning/error0. 실제 게임/성능 NOT TESTED, 기존 Monitor RED 유지. 아래 이전 정책은 이력이며 최신 승인 정책이 우선한다. [보고서](docs/reports/2026-09-12-avante-common-rpm-policy.md).

# 최신 결과 — 외곽 완등 / 차량 경고

최대 눈금과 외곽 완등 분리(알려진 redStart에서5쌍), ARC 사용자참조 빨강11300/눈금12000 추가. Client157/157·Activity111/111·Release warning/error0, 활성 WPF 전환 캡처 확인. 별도 테스트 실행. 전체 차량 미확인값 자동 fallback은 사용자 허용 확인 대기이며 완료 아님. 기존 Monitor RED 유지. [보고서](docs/reports/2026-09-12-avante-outer-gauge-and-vehicle-warning.md).

# 최신 결과 — 영상으로 확인한 레드존 점멸 재진입 문제

사용자191147242 영상의 거의 지속 점등 확인, 경계 왕복에서125ms 대기 반복 초기화 재현. 위상 보존/공유 표시 시계/200ms 부드러운 밝기 전환 적용, 기존 보간 색상·경고 즉시 종료·정적 자원 유지. Release warning/error0, Client156/156(기존155+1), Activity111/111. Lola 수동최대16000 유지, 새 별도 테스트 Client 실행. 실제 게임 수정 후 수용/VR NOT TESTED, 기존 Monitor RED 유지. [상세 보고](docs/reports/2026-09-12-avante-redzone-flash.md).

# 최신 결과 — 영상 디테일 검토 / 속도 표시 정렬

FLNtQZ2MWi8 실제 프레임 비교, 수정 전454개 소스·자산·별도 실행본 백업/SHA256 검증. 세 효과는 개선 후보 보고만. 속도 중심(1024,562), km/h17/(1168,589) 적용, 숫자 크기/원본 자산/렌더 구조 유지. Release warning/error0, 최종Client155/155·Activity111/111. 최초Race batch 실패/단독 및 전체 재확인 내역 보존. 실게임 정렬 수용 NOT TESTED, Monitor RED 유지. docs/reports/2026-09-12-avante-video-detail-review.md 참조.

# 최신 결과 — 참조 차량 경고 복구 / 숫자 통과 / 정렬

Lola·Iveco 참조 프로필로 노랑/빨강/점멸 복구(75% 미복원, 노랑은 명시적 대체값). 빠른 RPM 통과1~15 숫자 확대 보완, 원본 윤곽 발광선/흰 눈금 안쪽 붉은 띠/속도 광학 정렬. 최종 Release warning/error0, Client155/155, Activity111/111. 별도 테스트 실행, 설치/commit/tag/push/release 없음. 실제 시각 대조와 전체 차량 경고 자동 일치는 미완료. 기존 Monitor RED 유지. 통합 보고서 최신 절 참조.

# 최신 결과 — 75% 제거 / N 원본 동작

미확인 레드존75% 추정·중복 판정 제거. 숫자±100RPM에서최대120% retained 확대, 원본 기어 윤곽 발광선, 어두워지는 붉은 배경/레드존 눈금 적용. Client154/154·Activity111/111·Release warning/error0. 별도 테스트 실행, 설치/commit/tag/push/release 없음. 실게임 대조 NOT TESTED; 기존 Monitor RED 유지. 통합 보고서 최신 절 참조.

# 최신 결과 — RPM 자동 범위와 N 표시 수정

레드존보다 큰 다음1000 RPM 단위 자동 최대눈금 적용(7400→8000,8000→9000). 기존 수동 최대값/설정 키 보존. 눈금 띠 r349 정렬, 속도 중심·고정 자릿수 및 원본5단계 진행 띠 복원. 최종 통합 gate153/153+111/111, 경고/오류0. 기존 Monitor RED, 실게임/VR NOT TESTED 유지. 별도 테스트 실행, 설치/commit/tag/push/release 없음. 통합 보고서 최신 절 참조.

# 2026-09-12 16:47 RPM 실제 HUD 참조 적용 수정

동일 차량의 기본 HUD임을 사용자 확인. 자동 프로필 누락을 수정해 Aston Martin Vantage GT3 Evo - Low Downforce에만 12000눈금/약7400빨강을 적용한다. 노랑6166.667은 명시적 N 비율 대체값. 기존 사용자 보정 우선. 최종 gate PASS61.5초, warning/error0, Client152/152, Activity111/111. 기존151건 유지. 별도 테스트 Client 실행(PID9852), 설치/commit/tag/push/release 없음. 수정 후 재실게임 대조 NOT TESTED, Monitor RED 유지. 보고서 상단 추가 기록 참조.

# 2026-09-12 실제 Monitor 검증·Avante RPM 추가 수정 — 현재 미커밋

- Default WPF hardware / Monolith 유지. 사용자 게임 종료 후 재실행·설치 교체·commit/tag/push/release 없음.
- 실제 Client 그래프 전달 p95 20.845ms/p99 27.781ms/>33ms131회와 사용자 끊김 관찰: Monitor 목표 FAIL, 전체 RED. 물리 FPS/30분 연속 주행/동일 실게임3쌍 미완료.
- 중단 후 동일 target 복구, 차량별 N RPM 보정과 단일 좌표, 안쪽 눈금·배경 중복 제거를 적용. SHM에 계기판 최대/경계 필드가 없어 실제 차량 정확 일치는 사용자 보정이 필요하며 fallback은 명시적으로 불확실하다.
- 최종 gate verify-20260912-152957: PASS62.3초, warning/error0, Client151/151, Activity111/111. 기존149/111 유지.
- 실제 수집 queue/Archive 종료 dropped0과 별도로 ledger knownLoss8457이 존재하므로 무손실 PASS로 보고하지 않음. Capture/Upload 보호 코드·cadence 유지, 운영 테스트 업로드0.
- [현재 통합 보고서](docs/reports/2026-09-12-monitor-live-integration-validation.md): 실제 실행과 RPM 합성 하네스3쌍을 별도 비교. VR 실기기 NOT TESTED.

# PROJECT.md — AMS2KRLeague Overlay Client

## 2026-09-12 Monitor structural remediation — current working tree

- Confirmed WPF PathGeometry bounds serialization/allocation and repeated value layer reconstruction; implemented compiled retained geometry, independent Avante values, retained pedal interpolation and clock lifetime/coalescing. Default WPF hardware / Monolith retained.
- Final 3 alternating pairs, CPU median 5.243%→3.121%, allocation 78.922→8.396MiB/s. Private/WS increased slightly. After Avante/graph/pedal >33ms=0 in all 3 runs; physical FPS NOT MEASURED.
- Gate PASS `verify-20260912-121719.md`: errors=0 warnings=0, Client149/149, Activity111/111. Protected hashes 110 unchanged.
- YELLOW: harness improvement verified, real game/physical presentation and equivalent native GPU delivery metrics unverified. Minimal direct GPU plot POC retained in harness only; no product port/process/IPC. VR NOT TESTED; no install/commit/tag/push/release.
- [Current report](docs/reports/2026-09-12-monitor-structural-remediation.md).

## 2026-09-12 Monitor renderer matrix — current working tree

- Selected Default WPF hardware for Monitor and shared visible-only presentation clock; Monolith retained. Previous retained graph/motion/lifetime improvements and all dirty Avante assets preserved.
- Gate B PASS: Release errors=0 warnings=0, Client 149/149, Activity 111/111; `verify-20260912-110137.md`. Protected Core/Runtime/Assets hashes: 110 unchanged.
- Performance **YELLOW / not complete**: four-scene CPU 5.975%→4.215%, Avante native DWM 90.221→104.666Hz, but allocation 65.466→69.336MiB/s and Private 122.910→223.660MiB. Pedal 59.908Hz/p95 27.775ms/>33ms 6. Physical FPS NOT MEASURED.
- Additional CPU profile: 60.287% of app CPU samples in WPF render thread; hardware geometry/path costs remain. No Collector in this fixture; do not attribute CPU to league upload or call 4% sufficiently light.
- DirectComposition adapter did not prove adoption benefit; no process split. VR CODE REVIEW ONLY / NOT VALIDATED. No game, install, commit/tag/push/release.
- [Current report](docs/reports/2026-09-12-monitor-renderer-matrix.md); [previous gate report](docs/reports/2026-09-11-overlay-performance-remediation-gates.md).

## 1. 프로젝트 식별

- 프로젝트명: **AMS2KRLeague / AMS2 League Overlay**
- GitHub: `choi3724/AMS2KRLeague`
- 로컬 작업 경로: `E:\AMS2 KRLEAGUE\AMS2KRLeague`
- 애플리케이션: Windows WPF / .NET 8 / x64
- 게임: Automobilista 2
- 게임 데이터 소스: **Project CARS 2 Shared Memory v14**
- 게임 연동 정책: **읽기 전용**
- 현재 공개 Latest: **v0.7.2**
- 0.7.0 안정화 작업 기준 커밋: **48b2881f1f6f4b6f758763ef9556e383db086e70**
- 현재 Assembly/File version 기준: **0.7.2 / 0.7.2.0**
- 안정 보존 기준선: `v0.2.2` 태그는 이동하거나 덮어쓰지 않는다.

이 문서는 현재 저장소의 사실과 구조를 설명한다.
작업별 요구사항은 `docs/TASK.md`를 따른다.
두 문서가 충돌하면 실제 소스와 Git 상태를 먼저 확인하고 임의 해석하지 않는다.

## 2. 저장소 범위

이 저장소가 직접 소유하는 범위:

- AMS2 Player Overlay Client
- WPF Overlay UI
- Shared Memory v14 reader/parser
- 세션/참가자/리그 분류
- Race Event / Race Control
- 주행 HUD
- 레이아웃 저장/편집
- SteamVR 시험 지원
- Activity / Session Witness capture
- Compact Telemetry A2CT encoder/schema
- 로컬 durable archive
- Cafe24 전송 Client
- GitHub 자동 업데이트
- Installer / Portable 패키징
- Client/Activity 테스트

이 저장소의 직접 범위 밖:

- Cafe24 PHP 운영 서버
- MariaDB schema/migration
- 관리자/공개 웹페이지
- 서버의 공식 결과 승인/분류 정책

서버 소스가 별도 작업공간에 존재할 수 있으나 Overlay 저장소의 파일과 섞어 커밋하지 않는다.

## 3. 절대 안전 경계

다음을 하지 않는다.

- AMS2 프로세스 메모리 쓰기
- DLL injection
- DirectX hook
- 게임 네트워크 패킷 변경
- 게임 파일 수정
- 게임 레지스트리 변경
- 방화벽/네트워크 설정 변경
- 입력 가로채기
- 게임 프로세스 강제 종료
- 사용자 승인 없는 OS 재부팅
- 서버/DB 자격증명 Git 기록
- SteamID64 등 민감 식별자를 공개 UI에 노출

Shared Memory는 항상 읽기 전용이다.

## 4. 현재 0.7.1의 주요 기능

### 4.1 Overlay

- Timing Tower
- 전후방 거리
- 현재/섹터 타임
- 세션 정보
- Event Card
- Race Control
- Waiting Overlay
- Telemetry graph
- 독립 Pedal Gauge
- Speed
- Gear
- Racing Dashboard
- Overlay별 표시 토글
- 게임 없이 레이아웃 미리보기/편집
- 저장된 위치/크기 복원
- Timing Tower / Telemetry의 `기본(legacy)` / `개량(racing)` 디자인 선택

### 4.2 주행 HUD 갱신

현재 구조:

```text
AMS2 SHM
 ├─ Full Snapshot read       ≈ 30 Hz
 │    ├─ Session / Participant
 │    ├─ Timing / Event / Race Control
 │    └─ Activity / Witness / Telemetry Capture
 │
 └─ Display-only fast read   각 고유 렌더 프레임에서 최신 SHM 읽기 (60Hz 상한 없음)
      └─ Brake / Throttle / Clutch / Handbrake
         Speed / Gear / RPM / Steering / ABS
```

- 전체 snapshot 계산/순위 계층은 최대 20 Hz.
- 주행 HUD용 빠른 read는 각 고유 WPF 렌더 프레임에서 수행한다. 실제 읽기 성공 Hz는 렌더 빈도·SHM 갱신·일관성 검사와 별개로 측정한다.
- 표시 기록은 최대 4,096개로 제한하며 240Hz에서도 10초 범위를 보존한다. 서버 기록 버퍼가 아니다.
- 화면 animation은 WPF compositor와 분리해 높은 프레임으로 표시하도록 설계.
- display-only fast read는 기록/서버 전송 cadence를 증가시키지 않는다.
- full read와 충돌하면 fast read는 대기하지 않고 해당 표시 tick을 건너뛴다.

## 5. Telemetry / Capture 구조

주요 흐름:

```text
Shared Memory v14
  ↓
SharedMemoryReader / Parser
  ↓
TelemetrySnapshot
  ↓
ActivityCaptureRuntime
  ├─ Player Activity
  ├─ Session Witness
  └─ Future Telemetry
       ↓
       LocalDurableTelemetryArchive
       ↓
       CompactTelemetryChunkStore
       ↓
       A2CT gzip
       ↓
       durable upload queue
       ↓
       Cafe24 HTTPS
```

핫패스 원칙:

- Shared Memory 읽기 경로에서 파일 I/O, gzip, hash, HTTP를 수행하지 않는다.
- 고비용 작업은 background worker가 담당한다.
- 업로드 실패는 원본을 버리지 않고 durable queue에서 재시도한다.

## 6. Compact Telemetry 현재 계약

Protocol:

- Magic: `A2CT`
- Protocol version: `1`
- V1 schema는 불변 계약
- schema field ordinal은 연속적이며 기존 V1에서 재배치하지 않는다.
- Loss Ledger: `0x0050`
- Attempt Finalize: `0x0051`

주요 V1:

- `0x0001` SESSION_STATIC_V1
- `0x0002` SESSION_CHANGE_V1
- `0x0010` RACE_EVENT_V1
- `0x0020` PARTICIPANT_REPLAY_V1
- `0x0021` TRACK_GEOMETRY_V1
- `0x0030` DRIVER_FAST_V1
- `0x0031` DRIVER_MOTION_V1
- `0x0032` DRIVER_SLOW_V1
- `0x0033` DRIVER_CHANGE_V1
- `0x0040` INCIDENT_V1
- `0x0050` LOSS_LEDGER_V1
- `0x0051` ATTEMPT_FINALIZE_V1

### 6.1 0.7.0 장거리 V2

20 km 초과 서킷에서 V1 거리 양자화 범위를 넘는 블록만 additive V2를 사용한다.

- `0x0101` SESSION_STATIC_V2
- `0x0110` RACE_EVENT_V2
- `0x0120` PARTICIPANT_REPLAY_V2
- `0x0121` TRACK_GEOMETRY_V2
- `0x0130` DRIVER_FAST_V2
- `0x0140` INCIDENT_V2

원칙:

- V1 의미를 변경하지 않는다.
- 거리 overflow가 없으면 V1 유지.
- V2 최대 거리 목표: 100 km.
- 서버가 V2를 아직 모르면 `COMPACT_SCHEMA_UNKNOWN`을 영구 폐기하지 않고 retry 가능한 상태로 유지한다.

## 7. 현재 Capture cadence

`TelemetryArchiveOptions` 기본 개념:

- Replay source gate: 5 Hz
- Driver telemetry: 20 Hz
- Incident ring: 20 Hz
- Replay progress wire cadence: 2,000 ms
- Replay world XYZ wire cadence: 500 ms
- Replay extension cadence: 20,000 ms
- close-battle cadence: 500 ms

주의:

- `ActivityCaptureRuntime`의 실제 product 구성에서는 `ChunkDurationMs = 300_000`으로 설정되어 있다.
- 즉 현재 메모리 내 telemetry chunk 단위는 **5분**이다.
- 네트워크 전송 주기와 로컬 durable commit 주기는 동일한 개념이 아니다.

## 8. Replay 데이터 정책

- raw 30 Hz participant telemetry를 Cafe24로 스트리밍하지 않는다.
- 2D Replay용 world position은 Compact에서 기본 500 ms cadence를 사용한다.
- 브라우저 또는 서버 표현 계층은 실제 sample 사이의 presentation interpolation을 사용할 수 있다.
- interpolation은 captured fact가 아니다.
- Race Story, Position History, Replay의 source of truth는 저장된 Compact/Witness evidence다.
- missing sample을 실제 사건으로 합성하지 않는다.

## 9. 사고 / 이벤트 정책

- 사고는 `INCIDENT_CANDIDATE` 등 관측 사실로 기록한다.
- 과실 판정은 하지 않는다.
- Yellow / Double Yellow / FCY를 구분한다.
- FCY는 authoritative `mYellowFlagState`를 사용한다.
- Position Change는 자동으로 Overtake라고 단정하지 않는다.
- Safety Car는 raw evidence에는 남을 수 있으나 League classification 순위/분모에서 제외한다.

## 10. 싱글 / 멀티 판정

현재 authoritative SHM multiplayer flag가 없으므로 AMS2 공식 `online.log`를 보조 근거로 사용한다.

현재 구현 특성:

- 접속 로그로 `MULTIPLAYER / SINGLE_PLAYER / UNKNOWN` 판정.
- 현재 로그 근거가 오래되면 `UNKNOWN`으로 내려갈 수 있다.
- 서버 업로드는 확인된 multiplayer 구간만 허용.
- 판정 실패/미확인 데이터는 로컬 보관하며 임의로 멀티라고 추정하지 않는다.

## 11. Player / Private telemetry 정책

- 공개 Session Witness와 public replay evidence는 익명 설치 단위로 업로드할 수 있다.
- Private Driver Telemetry는 authoritative owner attestation이 해결되지 않은 상태에서는 서버 공개 업로드하지 않는다.
- 사용자/운전자 소유권을 fuzzy name 추정만으로 자동 확정하지 않는다.

## 12. GitHub 자동 업데이트

현재 동작:

- GitHub `releases/latest` 확인.
- 새 버전이 있으면 Installer를 다운로드.
- size + SHA-256 검증.
- helper를 통해 설치 후 프로그램 재실행.
- 실패 시 현재 버전을 유지하고 재시도.

현재 제품 정책상 모든 새 GitHub Release는 Latest로 게시된다.

중요:
- 0.7.1부터 capture 진행 중이거나 durable finalize ACK 전에는 설치를 보류하며, 종료 인계 직전에 다시 확인한다.
- 업데이트 기능을 수정할 때 기록 종료/Finalize 안정성을 우선한다.

## 13. SteamVR

0.7.0:

- SteamVR Overlay API 사용.
- game graphics device를 hook하지 않는다.
- 독립 D3D11 device/texture 사용.
- `SetOverlayRaw` 반복 제출 대신 persistent texture + `SetOverlayTexture`.
- 동일 해상도에서는 texture 재사용.
- Monitor / VR / 동시 표시 지원.
- Quest 3 / Virtual Desktop 실장비 전체 검증은 별도 acceptance가 필요하다.

## 14. 현재 알려진 검증 공백 / 기술 부채

다음 항목은 "해결됨"으로 간주하지 않는다.

1. 실제 AMS2 부하 상태에서 60 Hz Driving HUD의 장시간 frame/CPU/GC 검증.
2. 실제 Quest 3 / Virtual Desktop 환경에서 VR flicker 및 latency 검증.
3. `online.log`가 조용한 실제 multiplayer에서 UNKNOWN 전환이 업로드를 막는지 검증.
4. 경기 중 GitHub 자동 업데이트가 capture/finalize를 끊지 않는지 검증.
5. 5분 telemetry chunk가 비정상 종료 시 durable loss window를 키우는 문제 검토.
6. 과거 진단에서 관측된 비정상 `speed ≈ 690~705 m/s` 원인 미확정.
7. Replay payload size gate를 실제 production packing 기준으로 지속 검증.
8. Long-track V2가 운영 Cafe24 decoder에서 실제 E2E 수신되는지 별도 검증.
9. Private Driver Telemetry ownership/attestation 미완료.
10. UI 처리 예외 시 전체 Overlay를 숨기는 현재 fault boundary의 복원력 검토.
11. 0.7.1에서 telemetry diagnostic log를 bounded background writer로 변경했다. 실제 장시간 디스크 지연의 영향은 별도 관측 대상이다.

## 15. 주요 코드 위치

| 역할 | 경로 |
|---|---|
| SHM layout/read/parse | `src/AMS2LeagueClient.Core/Telemetry/` |
| Fast display read | `src/AMS2LeagueClient.Core/Telemetry/SharedMemoryReader.cs` |
| 세션 / 참가자 / 분류 | `src/AMS2LeagueClient.Core/Session/` |
| Timing / Presentation | `src/AMS2LeagueClient.Core/Presentation/` |
| Event | `src/AMS2LeagueClient.Core/Events/` |
| Race Control | `src/AMS2LeagueClient.Core/RaceControl/` |
| Overlay windows/layout | `src/AMS2LeagueClient/Overlay/` |
| WPF views | `src/AMS2LeagueClient/Presentation/` |
| Main coordinator | `src/AMS2LeagueClient/Runtime/PlayerOverlayCoordinator.cs` |
| Activity capture | `src/AMS2LeagueClient/Runtime/ActivityCaptureRuntime.cs` |
| Upload transport | `src/AMS2LeagueClient/Runtime/Cafe24ActivityUploadTransport.cs` |
| Auto updater | `src/AMS2LeagueClient/Runtime/GitHubAutoUpdater.cs` |
| Compact codec/schema | `src/AMS2LeagueClient.Core/CompactTelemetry/` |
| Durable telemetry | `src/AMS2LeagueClient.Core/FutureTelemetry/` |
| VR | `src/AMS2LeagueClient/Vr/` |
| Client tests | `tests/AMS2LeagueClient.Tests/` |
| Activity/Compact tests | `tests/AMS2LeagueActivity.Tests/` |

## 16. 검증

기본 검증 스크립트가 존재하면 먼저 사용한다.

```powershell
.\scripts\verify.ps1
```

수동 핵심 검증:

```powershell
.\work\dotnet8\dotnet.exe restore .\AMS2KRLeague.sln
.\work\dotnet8\dotnet.exe build .\AMS2KRLeague.sln -c Release --no-restore

.\work\dotnet8\dotnet.exe run `
  --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj `
  -c Release --no-build

.\work\dotnet8\dotnet.exe run `
  --project .\tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj `
  -c Release --no-build
```

현재 0.7.0 릴리스 보고 기준:

- Client tests: 135 PASS
- Activity tests: 110 PASS
- Release build: warnings 0 / errors 0

숫자는 현재 작업 트리에서 다시 실행해 확인해야 하며 문서 숫자를 그대로 완료 증거로 사용하지 않는다.

## 17. Release 정책

사용자가 명시적으로 요청한 경우에만:

- version bump
- commit
- tag
- push
- GitHub Release

를 수행한다.

자동으로 다음 버전을 만들지 않는다.

릴리스 시 최소 확인:

- version 일치
- clean worktree
- Release build
- Client tests
- Activity tests
- Installer
- Portable ZIP
- SHA256SUMS
- release manifest
- 금지 파일/secret 없음
- 실행 smoke test

## 18. 문서 우선순위

작업 시작 시 최소 다음 순서로 확인한다.

1. `AGENTS.md`
2. `PROJECT.md`
3. `docs/TASK.md`
4. 현재 Git `status/log/diff`
5. TASK 관련 실제 소스
6. TASK 관련 최신 검증 보고서

과거 handoff 문서는 참고 자료다.
과거 문서의 버전/정책이 현재 `PROJECT.md`, `docs/TASK.md`, 실제 코드와 충돌하면 과거 문서를 권위로 사용하지 않는다.

## 19. 프로젝트 원칙

이 프로젝트에서 중요한 순서:

1. 실제 경기 기록을 잃지 않는다.
2. 잘못된 사실을 만들지 않는다.
3. 기존 정상 기능을 깨뜨리지 않는다.
4. 화면은 실제 사용자가 쓸 수 있을 정도로 부드럽고 읽기 쉬워야 한다.
5. 서버 전송량과 저장량을 통제한다.
6. 새 기능보다 현재 기능의 실전 안정성을 우선한다.

"테스트가 통과함"과 "실제 Race에서 쓸 수 있음"은 동일하지 않다.
