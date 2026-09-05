# Claude Code 작업 인수인계 분석서

작성일: 2026-09-05 KST  
대상: Claude Code 후속 작업자  
기준 저장소: `outputs/AMS2KRLeague`

## 1. 현재 기준점

```text
Repository: https://github.com/choi3724/AMS2KRLeague
Branch: main
HEAD: 9303e33e4edf916c73214835ef09e58e72042e43
Tag: v0.2.3-beta.3
GitHub Release: https://github.com/choi3724/AMS2KRLeague/releases/tag/v0.2.3-beta.3
GitHub latest: v0.2.3-beta.3
Draft: false
Prerelease: false
Stable baseline: v0.2.2
Client version: 0.2.3-beta.3
Assembly/File version: 0.2.3.0
```

`v0.2.3-beta.3`은 이름은 Closed Beta지만 사용자 정책에 따라 GitHub의 일반 `Latest Release`로 게시됐다. `v0.2.2` 태그는 안정 기준선으로 그대로 유지한다.

이 문서를 만들기 직전 Client 저장소는 `main == origin/main == 9303e33`, 작업 트리 clean 상태였다. 이 문서 파일 자체는 후속 인계를 위해 새로 추가한 로컬 변경이다.

## 2. 운영자가 확인한 실제 상태

2026-09-05 사용자가 `v0.2.3-beta.3`의 **실제 AMS2 인게임 동작이 정상임을 확인**했다. 이는 자동 fixture가 아니라 사용자의 실게임 화면 확인 결과다.

따라서 다음 항목의 현재 UI 판정은 PASS로 인계한다.

```text
IN-GAME OVERLAY: PASS (operator confirmed, 2026-09-05)
WAITING CLIP: PASS
POSITION ANIMATION: PASS
PRACTICE BEST LAP: PASS
QUALIFYING BEST LAP: PASS
RACE FINISH TIMING: PASS
CLASS COLOR MAPPING: PASS
FONT SIZE UPDATE: PASS
ACTIVE PARTICIPANT DIM BUG: PASS
INTENDED INACTIVE STYLE ONLY: PASS
RELATIVE GAP COLOR: PASS
FALSE LAP GAP: PASS
```

별도의 새 증상이 접수되지 않는 한 이 UI hotfix를 다시 설계하거나 되돌리지 않는다.

## 3. beta.2/beta.3에서 완료된 변경

### beta.2 기반 회귀 수정

- 멀티플레이 대기 Overlay 한국어 문구 clipping 수정.
- 20 Hz 값 갱신 중에도 Timing Tower 행 컨테이너를 유지해 기존 340 ms Position Change 애니메이션 복원.
- Practice/Qualifying 주행 완료 참가자는 AMS2 participant Best Lap 표시. 유효 값이 없으면 `--`.
- Race는 참가자별 Finish 상태로 처리. 선두 Finish 후에도 후속 차량 timing은 진행하고, 각 차량 Finish 시 `FIN`으로 즉시 고정.
- `DNF`/`RET`/`DSQ` 이후 숫자 timer 증가 금지.

### beta.3 UI/participant hotfix

- GT3, GT4, GTE, Prototype, Formula 등 고정 클래스 배지 팔레트와 fallback 추가.
- 클래스 폰트 17 px, 타임 폰트 18 px, 행 36 px, 클래스 열 100 px, 타임 열 104 px.
- 상태 변경 때 행 전체 opacity를 0.35로 내리던 애니메이션 제거.
- `IsActive=false`, `RET`, `DNF`, `DSQ`만 dim 처리. Pit, timing 지연, Finished는 밝기 유지.
- Waiting, Position Change, Best Lap, Race Finish 수정의 회귀 테스트 유지.

### Relative Gap hotfix

색상 의미는 항상 다음과 같다.

```text
RED  = 사용자에게 불리
BLUE = 사용자에게 유리
```

```text
AHEAD 50m -> 60m = RED
AHEAD 60m -> 50m = BLUE
BEHIND 50m -> 40m = RED
BEHIND 40m -> 50m = BLUE
```

- 20 Hz SHM 진동 방지를 위해 마지막 안정 거리 대비 2 m deadband/hysteresis 적용.
- 작은 변화는 마지막 안정 trend를 유지한다.
- participant 또는 session generation 변경 시 trend와 Lap Gap 확인 상태 초기화.
- Lap Gap은 `lapsCompleted × trackLength + validLapDistance` 누적 진행거리로 계산.
- 실제 차이가 trackLength 이상이고 동일 후보가 연속 2개 유효 snapshot에서 확인된 경우만 `LAP N` 확정.
- trackLength/lapDistance가 신뢰 불가능하면 `LAP N`을 추정하지 않고 meter/time fallback 사용.

기존 false `LAP 1`의 원인은 Start/Finish를 먼저 지난 차량의 `lapsCompleted`가 먼저 증가한 순간을 단순 lap counter 차이로 판정한 것이었다.

## 4. 핵심 코드 위치

| 역할 | 주요 파일 |
|---|---|
| SHM v14 layout/read/parse | `src/AMS2LeagueClient.Core/Telemetry/SharedMemoryLayout.cs`, `SharedMemoryReader.cs`, `SharedMemoryParser.cs` |
| 세션/로컬 참가자/분류 | `src/AMS2LeagueClient.Core/Session/` |
| Timing Tower ViewModel | `src/AMS2LeagueClient.Core/Presentation/OverlayViewModel.cs` |
| 클래스 팔레트 | `src/AMS2LeagueClient.Core/Presentation/ClassBadgePalette.cs` |
| participant dim 판정 | `src/AMS2LeagueClient.Core/Presentation/ParticipantRowStateResolver.cs` |
| Relative distance/Lap Gap | `src/AMS2LeagueClient.Core/Presentation/TrackProgressDistance.cs`, `GapPresenter.cs` |
| timing/finish 상태 표시 | `src/AMS2LeagueClient.Core/Presentation/StateText.cs`, `OverlayViewModel.cs` |
| WPF Timing Tower/애니메이션 | `src/AMS2LeagueClient/Presentation/OverlayHudView.xaml`, `OverlayHudView.xaml.cs` |
| 앞차/뒷차 WPF | `src/AMS2LeagueClient/Presentation/RelativeDriversView.xaml` |
| 대기 Overlay | `src/AMS2LeagueClient.Core/Presentation/MultiplayerWaitingOverlay.cs`, `src/AMS2LeagueClient/Presentation/MultiplayerWaitingOverlayView.xaml` |
| 독립 창/배치 저장 | `src/AMS2LeagueClient/Overlay/`, `src/AMS2LeagueClient.Core/Presentation/OverlayLayoutProfile.cs` |
| 전체 Client orchestration | `src/AMS2LeagueClient/Runtime/PlayerOverlayCoordinator.cs` |
| 활동/Witness 캡처 | `src/AMS2LeagueClient.Core/ActivityCapture/`, `SessionWitness/` |
| P024 Compact codec | `src/AMS2LeagueClient.Core/CompactTelemetry/` |
| durable archive/finalize/loss ledger | `src/AMS2LeagueClient.Core/FutureTelemetry/` |
| HTTPS/익명 등록/upload | `src/AMS2LeagueClient/Runtime/ActivityCaptureRuntime.cs`, `Cafe24ActivityUploadTransport.cs` |
| UI/SHM/transport tests | `tests/AMS2LeagueClient.Tests/Program.cs` |
| activity/witness/compact tests | `tests/AMS2LeagueActivity.Tests/` |

## 5. 데이터 흐름

```text
AMS2 Shared Memory v14 (read-only)
  -> SharedMemoryReader / SharedMemoryParser
  -> TelemetrySnapshot
  -> Session/Classification/Relative/Presentation
  -> independent click-through WPF Overlay windows

TelemetrySnapshot
  -> ActivityCapture + Session Witness
  -> FutureTelemetryCaptureRuntime
  -> A2CT V1 Compact frames + legacy low-rate metadata
  -> local durable archive / upload sidecar queue
  -> Cafe24ActivityUploadTransport (HTTPS)
  -> Cafe24 PHP/PDO/MariaDB index + canonical .a2ct.gz filesystem storage
```

게임 프로세스에는 쓰지 않는다. DLL injection, DirectX hook, packet interception, 게임 파일/레지스트리 변경을 사용하지 않는다.

## 6. Client/Server 경계

GitHub 저장소와 공개 Release에는 **사용자용 Overlay Client만** 들어간다. 웹서비스, PHP Server, DB migration, 운영 설정, Host credential은 포함하지 않는다.

Server 작업 트리는 별도 비-Git 디렉터리다.

```text
../AMS2League/
../AMS2League/server/cafe24_telemetry014/
```

현재 확인된 Cafe24 Closed Beta 기준:

```text
Application: 1.6.0
DB schema: 15
Cafe24 release: 20260902-001
Canonical Compact storage: .a2ct.gz filesystem
MariaDB binary payload: 저장하지 않음(검색/index metadata만)
```

beta.1 배포 때 backup, migration 014/015 dry-run, rollback 준비, PHP/PDO/MariaDB ingest와 GET hash round trip을 통과했다. beta.3는 UI/participant hotfix이므로 Server/DB/Compact Protocol을 변경하지 않았다.

Cafe24 작업이 새로 필요할 때의 사용자 정책:

- SSH key/SSH를 사용하지 않는다.
- FTP/FileZilla 기반 수동 업로드를 사용한다.
- 운영 데이터 삭제나 DB 초기화 금지.
- migration 전에 DB/운영 파일 backup과 dry-run, 실패 시 rollback.
- 암호, FTP/DB 비밀번호, token을 문서·Git·로그에 기록하지 않는다.

## 7. Compact Telemetry 현재 판정

P024는 Closed Beta 사용에는 승인됐지만 안정판 승격 기준은 아직 YELLOW/HOLD다.

완료된 부분:

- A2CT V1 fixed-schema Compact encoding.
- 161/161 useful SHM field lineage accounting.
- synthetic 60분/32대 기준 wire `465,279 B`, 11/11 offline fidelity PASS.
- public Replay/Race Story/Incident raw 업로드와 Cafe24 canonical 저장/GET 검증.
- 실제 멀티플레이 2회 public chunk `72/72` Client→Cafe24→GET hash/bytes/decode 일치.
- Server raw만으로 14명, Lap, Position History, Race Story, 2D movement 재처리 PASS.
- Loss Ledger와 Attempt Finalize 순서/ACK 계약.

남아 있는 제한:

- 실제 두 multiplayer capture의 upload loss는 0이지만 cadence loss가 session 1 `7,909`, session 2 `6,014`여서 completeness는 `PARTIAL`.
- authoritative local-owner attestation이 없어 private Driver telemetry의 Server upload는 계속 차단. 정책상 차단은 전송 실패가 아니다.
- 실제 장시간/다중 Client/완전한 Incident pre-roll과 성능 고수위 검증은 계속 필요.
- low-rate `SESSION_METADATA`는 legacy JSON/gzip 호환 경로를 유지하므로 archive 전체가 compact-only인 것은 아니다.
- 안정판 `0.2.3` 승격은 별도 승인과 남은 gate 검증 전까지 하지 않는다.

## 8. 마지막 전체 검증

`v0.2.3-beta.3` 릴리스 직전 결과:

```text
Release build: warnings 0 / errors 0
AMS2LeagueClient.Tests: 54/54 PASS
AMS2LeagueActivity.Tests: 96/96 PASS
Total: 150/150 PASS
Public publish directory audit: 466 files / forbidden 0
Portable ZIP audit: 466 files / forbidden 0
Installer audit: 1 file / forbidden 0
Manifest/hash cross-check: PASS
In-game operation: PASS, user confirmed 2026-09-05
```

배포 파일:

| 파일 | bytes | SHA-256 |
|---|---:|---|
| `AMS2-League-Overlay-0.2.3-beta.3-win-x64.zip` | `70,127,355` | `7f96d9c1b38e725afb0dc939f9b1c0ec57afa682de612b2a6674cb2e85c15b7b` |
| `AMS2-League-Overlay-0.2.3-beta.3-Setup.exe` | `51,242,992` | `de5b783664cce63a0748876bb58061e5d34063c4956b1cf65cbb24a13dc0ea11` |

## 9. 빌드와 테스트 명령

PowerShell 실행 정책 때문에 `.ps1` 직접 실행이 차단되면 process 범위에서만 Bypass한다.

```powershell
cd C:\Users\User\Documents\Codex\2026-08-25\files-pasted-by-the-user-2026\outputs\AMS2KRLeague

.\work\dotnet8\dotnet.exe restore .\AMS2KRLeague.sln
.\work\dotnet8\dotnet.exe build .\AMS2KRLeague.sln -c Release --no-restore
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj -c Release --no-build
```

릴리스 후보 전체 검증/패키징:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 `
  -DotnetExecutable .\work\dotnet8\dotnet.exe `
  -Version <version> `
  -DisplayVersion <version>
```

GitHub 게시 전에는 clean worktree와 HEAD tag가 필요하다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-github-release.ps1 `
  -Version <version>
```

릴리스 정책은 beta/rc 접미사와 무관하게 `--latest`, `isPrerelease=false`다.

## 10. 릴리스 파이프라인에서 이미 수정한 함정

- Windows PowerShell 5.1에서 `Get-ChildItem -LiteralPath ... -Include *.pdb`가 모든 파일을 반환해 배포 폴더를 비우던 문제를 `-Filter '*.pdb'`로 수정했다.
- PowerShell 5.1에 없는 `Convert.ToHexString`과 `utf8NoBOM` 인코딩 이름을 호환 구현으로 교체했다.
- 버전별 새 publish 경로에서 증분 복사 누락을 방지하도록 publish에 `-t:Rebuild`를 넣었다.

이 코드를 이전 방식으로 되돌리지 않는다.

## 11. 문서 신뢰 우선순위와 오래된 내용

다음 순서로 읽는다.

1. 이 문서: 현재 release/운영/UI 상태.
2. `V023_BETA3_HOTFIX_REPORT_KO.md`: beta.3 원인, 코드와 fixture 검증 상세.
3. `V023_BETA1_RELEASE_AND_CAFE24_DEPLOYMENT_REPORT.md`: Cafe24 1.6.0/schema 15 실제 배포 증거.
4. `P024_RELEASE_GATE_REPORT.md`: Compact protocol, size, fidelity, remaining stable gates.
5. `COMPACT_PROTOCOL_V1.md`, `COMPACT_SCHEMA_REGISTRY.md`: wire contract.

주의할 오래된 문구:

- `V023_BETA3_HOTFIX_REPORT_KO.md`의 “게시 전 후보” 문구는 현재는 오래됐다. beta.3는 commit/tag/push/Latest 게시 완료 상태다.
- `GPT_NEXT_TASK_HANDOFF_REPORT.md`는 P023 개발 중간 상태 문서다. Production 1.4.2/schema 13, beta release HOLD 등의 현재와 다른 문구는 역사 자료로만 본다.
- `P024_RELEASE_GATE_REPORT.md` 안의 초기 “Production remains unchanged” 문단은 뒤의 beta.1 실제 배포 결과에 의해 대체됐다. 현재 Cafe24 기준은 1.6.0/schema 15다.
- `README.md`의 “앞차/뒷차 모두 멀어지면 파란색, 가까워지면 빨간색” 설명은 beta.3의 유불리 규칙을 완전히 표현하지 못한다. 코드와 이 문서의 AHEAD/BEHIND 규칙이 현재 truth다.

## 12. 다음 작업 권장 순서

새 기능 요구가 없다면 다음 우선순위가 가장 안전하다.

1. beta.3 실게임 정상 확인은 유지하고, 새로 접수된 실제 증상만 재현한다.
2. 실제 multiplayer local archive와 attempt loss ledger에서 cadence miss `7,909/6,014`의 발생 계층을 분리한다.
3. Capture 실패, local commit, queue pending/retry, HTTP, Server reject, hash, DB/index, raw storage, finalize를 각각 구분한다.
4. 실제 장시간 multiplayer와 full pre/post-roll Incident를 확보한다.
5. Server에 저장된 Compact 원본만으로 재처리하고 SHM을 다시 읽지 않는다.
6. client wire bytes, server raw bytes, MariaDB/index 증가량을 별도로 측정한다.
7. private Driver upload 차단과 기존 API/Portal/0.2.2 호환성을 유지한다.
8. 모든 gate가 통과하고 사용자가 명시적으로 요청할 때만 다음 버전 commit/tag/Latest Release를 만든다.

실제 multiplayer가 진행 중일 때는 게임을 조작하지 말고 데이터 전송/저장만 확인한다. AMS2 실행이 필요한 테스트를 사용자가 요청한 경우에는 바탕화면의 한글패치 실행 바로가기를 사용하고 게임 메뉴 조작까지 자동화하되, 현재 진행 중인 사용자 레이스를 방해하지 않는다.

## 13. 사용자 고정 정책

- 모든 새 GitHub Release는 beta 포함 항상 `Latest`.
- GitHub에는 사용자용 Overlay Client만 게시. 웹서비스/Server 제외.
- Evidence 수집은 multiplayer Race일 때만 수행.
- Safety Car는 League Classification 순위와 분모에서 제외.
- 앞차/뒷차는 순위가 아니라 실제 트랙 진행거리 기준.
- 없는 official time/final gap/penalty reason을 추정하지 않는다.
- Compact 원본은 compact 형태로 저장하고 대형 JSON으로 풀어 장기 보존하지 않는다.
- GENERAL/LEAGUE classification과 session result ingestion을 결합하지 않는다.
- Cafe24 운영 데이터 삭제·초기화 금지.
- 다음 작업 종료 때도 후속 AI가 읽을 수 있는 보고서를 남긴다.

## 14. 인계 결론

```text
CURRENT CLIENT: v0.2.3-beta.3
CURRENT HEAD: 9303e33e4edf916c73214835ef09e58e72042e43
GITHUB LATEST: PASS
IN-GAME UI: PASS (operator confirmed)
CLIENT TESTS: 150/150 PASS
CAFE24 CLOSED-BETA CONTRACT: APP 1.6.0 / SCHEMA 15
COMPACT UPLOAD/RAW HASH: PASS
CAPTURE COMPLETENESS: PARTIAL on two real sessions due cadence miss
PRIVATE DRIVER UPLOAD: INTENTIONALLY BLOCKED
STABLE v0.2.3 PROMOTION: HOLD pending separate gates/authorization
NEXT DEFAULT FOCUS: cadence-loss root cause and long multiplayer/incident evidence
```

## 15. 2026-09-05 Claude Code 후속 작업 (미커밋 작업 트리)

- F1 중계 스타일 오버레이 애니메이션과 오버레이별 켜기/끄기 상시 토글: `docs/F1_BROADCAST_OVERLAY_AND_TOGGLES_2026-09-05_KO.md`
- 리플레이 전송 충분성 실측: `docs/REPLAY_TRANSMISSION_SUFFICIENCY_2026-09-05_KO.md` — 전송/저장 손실 0이지만 Compact 변환의 월드 좌표 5,000 ms downsampling 때문에 2D 리플레이에는 불충분. 500 ms 권장, 코드 미변경, 사용자 승인 대기
- 검증: Release build 경고 0/오류 0, AMS2LeagueClient.Tests 64/64, AMS2LeagueActivity.Tests 96/96, `--demo-events` 스모크 예외 0
- commit/tag/Latest Release 미실행. 실게임 확인 미실시
- 리플레이 cadence별 전송량 실측(같은 보고서 5절): Compact 리플레이 상수 4종을 `TelemetryArchiveOptions`로 옵션화, `work/replay-cadence-cost`로 60분/32대 fixture를 제품 변환 코드로 재인코딩. world 500 ms = 리플레이 wire +558,222 B(+184.6 %, gate 합계 1,023,501 B로 512 KiB 목표 FAIL), 실제 리그 세션 +53 %. 1,000 ms +89.8 %/+24 %, 2,000 ms +33.9 %/+9 %
- 주의: `TelemetryArchiveOptions.DefaultReplayWorldIntervalMs`가 2026-09-05 13:29 KST에 Claude Code 세션 밖에서 500으로 변경됨(주석은 "사용자 결정"). 이 세션은 확인하지 못했으며 되돌리지 않음. 유지 시 P024 gate 재승인과 `tools/AMS2CompactProof` WriteReplay 정렬 필요
- 검증 갱신: Release build 0/0, AMS2LeagueActivity.Tests 97/97, AMS2LeagueClient.Tests 64/64
- 갱신: 사용자 지시로 위 작업 트리를 `0.3.0`으로 커밋하고 `v0.3.0` Latest Release로 게시함(world 500 ms 기본값 포함). 후속 작업과 작업 방식은 `AGENTS.md`와 `docs/CODEX_HANDOFF_2026-09-05_KO.md` 참조
