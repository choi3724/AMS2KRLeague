# AGENTS.md — AMS2KRLeague Overlay Client 작업 지침

이 파일은 이 저장소에서 작업하는 모든 AI 에이전트(Codex, Claude Code 등)를 위한 공통 지침이다. 상세 인수인계는 `docs/CODEX_HANDOFF_2026-09-05_KO.md`를 먼저 읽는다.

## 1. 저장소 범위

- 사용자용 AMS2 Player Overlay Client(WPF, .NET 8, x64)만 포함한다. 웹서비스/PHP Server/DB migration/Host 자격은 `../AMS2League/server/cafe24_telemetry014/`에 있으며 Git 밖이다.
- AMS2 Shared Memory v14를 읽기 전용으로만 사용한다. DLL injection, DirectX hook, 입력 가로채기, 게임 파일/레지스트리 변경, 전역 단축키를 추가하지 않는다.

## 2. 절대 규칙 (사용자 고정 정책)

1. commit, tag, push, GitHub Release는 사용자가 명시적으로 요청할 때만 한다. 요청 없이 작업 트리에 남긴다.
2. 모든 GitHub Release는 beta 접미사가 있어도 항상 `Latest`(`isPrerelease=false`)다. `scripts/publish-github-release.ps1`이 이를 강제한다.
3. GitHub에는 Overlay Client만 게시한다. Server 코드, 운영 설정, 자격증명은 포함하지 않는다.
4. Cafe24는 SSH를 쓰지 않는다. FileZilla 수동 FTP 업로드만 쓰고, migration 전 backup과 dry-run을 하며, 운영 데이터 삭제·DB 초기화는 금지다.
5. 비밀번호, FTP/DB 비밀번호, token을 문서·Git·로그에 기록하지 않는다.
6. 실게임 멀티플레이가 진행 중이면 게임을 조작하지 않는다. AMS2 실행이 필요한 테스트는 사용자가 요청할 때만 한다.
7. Safety Car는 League Classification 순위와 분모에서 제외한다. 앞차/뒷차는 순위가 아니라 실제 트랙 진행거리 기준이다.
8. 임의 official time/final gap/penalty reason을 추정하지 않는다. `—`나 `--`로 남긴다.
9. 상대 갭 색상: RED = 사용자에게 불리, BLUE = 유리.
10. Timing Tower 행 전체 opacity를 애니메이션하지 않는다. `IsActive=false`, `RET`, `DNF`, `DSQ`만 dim 처리한다.
11. Compact 원본은 compact 형태로 저장하고 JSON으로 풀어 장기 보존하지 않는다. GENERAL/LEAGUE classification과 session result ingestion을 결합하지 않는다.

## 3. 작업 방식 (이렇게 일한다)

- 바꾸기 전에 관련 코드와 문서를 끝까지 읽는다. 추측으로 답하지 않고, 수치가 필요한 주장은 도구를 만들어 측정한다(예: `work/replay-cadence-audit`, `work/replay-cadence-cost`).
- 변경마다 Release 빌드와 두 테스트 스위트를 모두 돌리고 결과를 숫자로 보고한다. 통과하지 못한 항목은 PASS로 쓰지 않고 FAIL 또는 NOT RUN으로 쓴다.
- 동작 변경에는 테스트를 추가한다. WPF 애니메이션은 값이 아니라 `HasAnimatedProperties`/base value로 검증한다(TimeManager 틱 전에는 값이 갱신되지 않는다).
- 정책에 영향을 주는 변경(업로드량, 프로토콜, gate)은 먼저 측정 결과와 옵션을 보고하고 사용자 결정을 받는다. 결정 없이 기본값을 바꾸지 않는다.
- 같은 작업 트리를 다른 에이전트가 동시에 편집할 수 있다. 편집 전 `git status --short`와 파일 mtime을 확인하고, 다른 에이전트의 변경은 되돌리지 말고 보고서에 사실대로 적는다.
- 작업 종료 시 후임이 읽을 보고서를 `docs/`에 남긴다: 무엇을 했는지, 어떻게 검증했는지, 무엇을 하지 않았는지, 다음 단계.
- 문서와 답변은 한국어로 쓴다. 파일·함수명은 필요한 곳에서만 쓴다.

## 4. 빌드·테스트·릴리스

```powershell
cd <repo>
.\work\dotnet8\dotnet.exe restore .\AMS2KRLeague.sln
.\work\dotnet8\dotnet.exe build .\AMS2KRLeague.sln -c Release --no-restore
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj -c Release --no-build
```

릴리스(사용자 요청 시에만):

```powershell
# 1. 버전 갱신: Directory.Build.props, installer/AMS2LeagueOverlay.iss, scripts/build-release.ps1 기본값,
#    README.md, VERSIONING.md, CHANGELOG.md, release/RELEASE_NOTES_KO.md, ClientStatusViewModel 기본 문자열
# 2. 패키지
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -DotnetExecutable .\work\dotnet8\dotnet.exe -Version <ver> -DisplayVersion <ver>
# 3. commit → tag v<ver> → git push origin main v<ver>
# 4. 게시 (clean worktree, HEAD tag 필요)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-github-release.ps1 -Version <ver>
```

`.ps1` 실행이 차단되면 process 범위 `Bypass`만 쓴다. 로컬 dotnet은 `work\dotnet8\dotnet.exe`(8.0.424)다.

## 5. 핵심 코드 위치

| 역할 | 파일 |
|---|---|
| SHM v14 layout/read/parse | `src/AMS2LeagueClient.Core/Telemetry/` |
| 세션/로컬 참가자/분류 | `src/AMS2LeagueClient.Core/Session/` |
| Timing Tower ViewModel, 전이 추적 | `src/AMS2LeagueClient.Core/Presentation/OverlayViewModel.cs`, `TimingTowerTransitionTracker.cs` |
| Timing Tower WPF + 애니메이션 | `src/AMS2LeagueClient/Presentation/OverlayHudView.xaml(.cs)`, `HudMotion.cs` |
| 전후방/세션/랩타임/이벤트/Race Control 뷰 | `src/AMS2LeagueClient/Presentation/*View.xaml(.cs)` |
| 독립 창, 배치 저장, 토글 | `src/AMS2LeagueClient/Overlay/OverlayWindow.xaml.cs`, `Core/Presentation/OverlayLayoutProfile.cs` |
| 상태창(토글 UI) | `src/AMS2LeagueClient/Presentation/ClientStatusWindow.xaml(.cs)` |
| 오케스트레이션 | `src/AMS2LeagueClient/Runtime/PlayerOverlayCoordinator.cs` |
| Compact telemetry codec/schema | `src/AMS2LeagueClient.Core/CompactTelemetry/` |
| durable archive, 리플레이 downsampling, cadence 옵션 | `src/AMS2LeagueClient.Core/FutureTelemetry/CompactTelemetryChunkStore.cs`, `TelemetryArchiveOptions.cs` |
| 업로드 | `src/AMS2LeagueClient/Runtime/ActivityCaptureRuntime.cs`, `Cafe24ActivityUploadTransport.cs` |
| 테스트 | `tests/AMS2LeagueClient.Tests/Program.cs`(UI/SHM/transport), `tests/AMS2LeagueActivity.Tests/`(archive/compact) |

## 6. 문서 읽는 순서

1. `docs/CODEX_HANDOFF_2026-09-05_KO.md` — 현재 상태와 남은 작업
2. `docs/CLAUDE_CODE_HANDOFF_2026-09-05_KO.md` — 릴리스/운영/UI 상태와 사용자 정책
3. `docs/F1_BROADCAST_OVERLAY_AND_TOGGLES_2026-09-05_KO.md` — 애니메이션/토글 설계
4. `docs/REPLAY_TRANSMISSION_SUFFICIENCY_2026-09-05_KO.md` — 리플레이 밀도와 cadence별 전송량 실측
5. `docs/P024_RELEASE_GATE_REPORT.md`, `COMPACT_PROTOCOL_V1.md`, `COMPACT_SCHEMA_REGISTRY.md` — wire 계약
