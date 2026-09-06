# 세션 표시 / 인터벌 랩 UI 수정 — 2026-09-06

## 변경과 원인

- 전후방 패널: 기존 누적 진행거리 계산은 세션 종류와 무관하게 LAP 후보를 만들었다. 이제 **Race에서만** 후보를 제공한다. 연습·예선·테스트·타임 어택에서는 LAP 표시가 없으며, 기존 시간차와 미터는 유지한다. 같은 generation에서 Race → Practice로 전환해도 이전 LAP 표시를 즉시 지운다. 시간차의 원본이 없거나 순위상 이웃과 실제 앞뒤 차량이 다르면 기존처럼 `—`이며, 시간을 추정하지 않는다.
- 순위 타워: 활동 중/Racing, `lapsCompleted == 0 && currentLap <= 1`인 Race·Practice·Qualify·Test 참가자를 `인터벌 랩`으로 표시한다. 플레이어와 AI 모두 같은 조건을 쓴다. 기존 작은 상태 배지가 아니라 타임 칸을 사용하여 글꼴 비율을 유지한다. 이때 무효 배지와 랩 무효 이벤트는 억제한다. 다음 랩부터 실제 invalid 신호가 있으면 기존 무효/동결 처리를 한다. Finish/RET/DNF/DSQ 표시는 우선 유지하며 Time Attack의 첫 유효 시도 판정은 바꾸지 않는다.
- 대기 화면: `active.Length > 1`을 멀티플레이의 근거로 쓰던 오류를 제거했다. AI가 많은 싱글플레이도 멀티로 표시되던 원인이다. 한 명뿐인 대기 세션도 표시한다.
- 대기 세션명: SHM SessionState 기준으로 Practice → `자유 연습 주행`, Qualify → `예선`, Race → `레이스`, Test → `테스트 주행`. Invalid는 `세션 전환 중`, 알 수 없는 값은 `세션 미확인`으로 표시한다.

## 싱글 / 멀티 표시의 한계와 사용법

현재 사용하는 공식 SHM v14 구조에는 확정 가능한 온라인/멀티플레이 boolean이 없다. 참가자 수·AI 이름·`SessionIsPrivate`는 그 증거가 아니다.

따라서 **싱글/멀티 완전 자동 감지는 구현하지 않았다.** 앱 상태창의 `대기 화면 플레이 모드`에서 `싱글플레이어` 또는 `멀티플레이어`를 선택한다. 선택 전에는 `세션 대기 · 모드 미확인`이며 앱 재실행 시 미확인으로 초기화된다. 앱을 켠 채 다른 모드로 이동하면 선택도 변경해야 한다. 자유 연습/예선 등의 세션 종류는 이 선택과 별개로 게임 데이터에서 자동 반영된다.

이 선택은 UI 표시 전용이다. 업로드 권한, 증거 수집, session fingerprint, 서버 분류 등에 전달되지 않는다. 향후 신뢰 가능한 모드 원본이 확보된 경우에만 자동 판정을 연결한다.

## 검증

| 항목 | 결과 |
|---|---|
| Release 전체 빌드 | 경고 0 / 오류 0 |
| Client 자동 테스트 | 91/91 PASS |
| Activity 자동 테스트 | 102/102 PASS |
| WPF 렌더링 테스트 실행 | 91/91 PASS |
| Race LAP 1/2 → 연습·예선 전환, 시간차·거리 유지 | PASS |
| 첫 랩 invalid 억제 / 다음 랩 무효 / 참가자별 전이 / raw 보존 | PASS |
| 1명·30명, 선택 3종, 세션 6종, privacy 값 영향 없음 | PASS |
| 선택 UI 양방향 binding / 잘못된 enum 값 방어 | PASS |
| 대기·타워·상태창 별도 렌더 시각 검수 | PASS |
| 수정본 실게임 동작 | NOT RUN — 게임/실행 중 오버레이 조작 안 함 |

이미지: `work/session-label-hotfix-layouts/`의 `practice-relative-no-lap.png`, `opening-interval-lap-tower.png`, `waiting-practice-SinglePlayer.png`, `waiting-practice-Multiplayer.png`, `status-mode-selector.png`. 테스트 fixture를 WPF로 렌더한 이미지이며 실게임 캡처가 아니다. 새 선택란이 기존 상태 카드와 겹치지 않도록 상태창 기본/최소 높이를 840/820으로 조정했다.

## 수정본 실행과 재검증

기존 실행본을 종료한 뒤 다음 파일로 시작한다. 중복 실행하거나 진행 중인 녹화를 강제 종료하지 않는다.

`src/AMS2LeagueClient/bin/SessionLabelHotfix/AMS2LeagueClient.exe`

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/SessionLabelHotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/SessionLabelHotfix/AMS2LeagueClient.Tests.dll
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/SessionLabelHotfix/AMS2LeagueActivity.Tests.dll
# 별도 WPF 이미지 생성
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/SessionLabelHotfix/AMS2LeagueClient.Tests.dll --capture-layout work/session-label-hotfix-layouts
```

## 범위 / 인수인계

- 기존 미커밋 수정사항을 유지한 작업 트리에 추가했다. 버전은 0.3.1 그대로이며 이번 작업에서 commit/tag/release/서버 배포는 하지 않았다.
- 기존 `Archive403Hotfix` 실행 프로세스는 교체하지 않았다. 새 변경은 수정본을 다시 실행해야 반영된다.
- Ponytail 원칙으로 기존 `OverlayViewModel`, `InvalidLapDisplayTracker` 공통 판정과 대기 controller, 기존 테스트 도구를 재사용했다. 별도 상태 프레임워크나 의존성은 추가하지 않았다.
- 텔레메트리 원본, Compact Protocol, 수집·업로드·서버 API/DB는 이번 UI 작업에서 변경하지 않았다. 과거 서버 검증의 남은 항목은 `ARCHIVE403_DAYTONA_LIVE_E2E_2026-09-06_KO.md`를 따르며 이번 UI 검증으로 해결된 것으로 보지 않는다.
