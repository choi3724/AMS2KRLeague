# Race 출발 랩 무효 알림 억제 — 2026-09-06

## 원인

최근 실행 로그 `client-20260906-113513.log`에서 다음 순서를 확인했다.

- 11:36:13.320: Race 세션의 localRaceState=1(출발 전).
- 11:36:19.771: RaceActive 전환.
- 11:36:19.804: InvalidLap 감지 및 표시 시작, Critical / 4초.

기존 코드가 Racing 상태와 raw invalid 플래그만 확인하고 첫 랩을 예외 처리하지 않았다. 타워 시간은 첫 랩에 `--`로 숨겼지만, 이후 표시 추적기가 `무효`를 추가하는 경로도 따로 존재했다. 실제 출발 때 AMS2가 해당 플래그를 세팅한 게임 내부 이유까지 확정한 것은 아니다.

## 적용 규칙

이전 사용자 요청의 Race 첫 랩 타이밍 숨김 정책과 동일하게 `SessionState.Race && 참가자.LapsCompleted == 0` 구간에서는 다음을 숨긴다.

- `현재 랩 무효` 이벤트.
- 순위 타워의 빨간 `무효` 상태.
- 개인 타이밍의 무효 문구와 무효로 인한 현재 시간 고정.

이는 출발 몇 초만 숨기는 임의 타이머가 아니라 **Race 첫 랩 전체의 표시 정책**이다. 해당 참가자의 첫 랩 완료 후에는 기존 게임 invalid 신호에 따라 무효 표시/시간 고정/알림을 다시 적용한다. 선두가 먼저 랩을 마쳤다고 아직 첫 랩인 후속 참가자의 표시를 활성화하지 않는다.

Practice/Qualifying의 첫 랩 무효 판정, 별도 페널티/Finish/RET/DNF/DSQ 표시는 기존대로다. 게임 데이터와 저장 원본에서 invalid 플래그를 지우거나 랩을 유효하다고 변경하지 않았다.

## 변경 위치

기존 `InvalidLapDisplayTracker`에 공통 표시 조건을 두고 이벤트 엔진, 타워/개인 시간 고정, 개인 패널 상태 문구가 같은 조건을 사용한다. 새 타이머나 캡처 정책을 만들지 않았다.

- `src/AMS2LeagueClient.Core/Presentation/InvalidLapDisplayTracker.cs`
- `src/AMS2LeagueClient.Core/Events/RaceEventEngine.cs`
- `src/AMS2LeagueClient.Core/Presentation/OverlayViewModel.cs`
- `tests/AMS2LeagueClient.Tests/Program.cs`

## 검증 결과

- Release Build: 경고 0 / 오류 0.
- Client: **86/86 PASS**.
- Activity: **97/97 PASS**.
- `git diff --check`: PASS.
- 출발 전 → Racing, 반복 snapshot, pause/resume generation, 랩 카운터 rollback/restart에서 무효 UI 억제: PASS.
- 플레이어/다른 참가자 각각 첫 랩 완료 후 표시 활성화: PASS.
- root-only invalid 및 participant invalid 모두 검사, 원본 바이트/플래그 불변: PASS.
- Practice/Qualifying 첫 랩의 실제 invalid 표시는 유지: PASS.
- 기존 LAP/시간차 위치, 이벤트 플래시, 글꼴 비율, 무효 시간 고정, terminal timing 회귀: PASS.

게임을 조작해 다시 출발시키는 실게임 재검수는 하지 않았다. 위 결과는 production 표시/이벤트 코드에 raw fixture를 적용한 자동 검증이다.

## 실행 및 재현

이전 수정본이 실행 중이므로 해당 파일을 덮어쓰거나 프로세스를 종료하지 않고 별도 출력으로 빌드했다.

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/RaceStartHotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/RaceStartHotfix/AMS2LeagueClient.Tests.dll
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/RaceStartHotfix/AMS2LeagueActivity.Tests.dll
```

실행파일: `src/AMS2LeagueClient/bin/RaceStartHotfix/AMS2LeagueClient.exe`.

이전 GAP 복원/이벤트 플래시 등 미커밋 수정도 포함한다. 버전은 아직 0.3.1이며 commit/tag/push/Release를 하지 않았다. 서버/API/DB/Compact/업로드 구조와 운영 데이터는 변경하지 않았다. 현재 실행 중인 오버레이에는 자동 반영되지 않으므로, 사용자가 종료 후 위 수정본으로 실행해야 한다.
