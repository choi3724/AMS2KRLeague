# F1 중계 스타일 오버레이 애니메이션 · 오버레이별 켜기/끄기 작업 보고서

작성일: 2026-09-05 KST
작성: Claude Code
기준: `v0.2.3-beta.3` (HEAD `9303e33`) 위의 작업 트리. commit/tag/release는 하지 않았다.

## 1. 요청과 결과

| 요청 | 결과 |
|---|---|
| F1 레이스 중계 화면 같은 애니메이션 (기존에는 순위 이동 슬라이드와 카드 페이드뿐) | Timing Tower, 전후방, 세션, 랩 타임, 이벤트 카드, Race Control 6개 화면에 중계 그래픽 전환 추가 |
| 각 오버레이 화면을 켜고 끄는 기능 | 상태창 체크박스를 편집 모드와 무관하게 항상 활성화, 변경 즉시 저장, 시작 시 복원, `모두 켜기/끄기` 추가 |
| 리플레이 데이터 전송 충분성 확인 | 별도 보고서 `REPLAY_TRANSMISSION_SUFFICIENCY_2026-09-05_KO.md` |

검증:

```text
Release build: warnings 0 / errors 0
AMS2LeagueClient.Tests: 64/64 PASS (기존 54 + 신규 10)
AMS2LeagueActivity.Tests: 96/96 PASS
데모 모드(--demo-events) 스모크 실행: 아래 5절
실게임 확인: 미실시 (사용자 확인 필요)
```

## 2. 애니메이션 설계 (F1 TV 그래픽 대응)

모든 전환은 "값이 바뀐 순간"에만 시작하며 20 Hz 갱신 자체는 애니메이션하지 않는다. 행 전체 opacity는 어떤 경우에도 애니메이션하지 않는다(beta.3의 "주행 중 참가자 dim 금지" 규칙 유지, 테스트로 고정).

### Timing Tower (`OverlayHudView`)

| F1 중계 요소 | 구현 | 시간 |
|---|---|---|
| 타워 빌드(세션 시작 시 행이 순서대로 들어옴) | 행이 처음 화면에 Loaded될 때 좌측 -182 px에서 슬라이드 인, 80 ms 안에 로드된 행끼리 38 ms 간격 stagger. `ItemsControl ClipToBounds`로 패널 밖은 잘림 | 320 ms + stagger |
| 참가자 진입 | 새 행 단독 슬라이드 인(stagger 0) | 320 ms |
| 순위 이동 | 기존 340 ms Y 슬라이드 유지, 같은 `TranslateTransform` 재사용 | 340 ms |
| 추월/피추월 플래시 | 행 뒤 `FlashLayer`가 획득=민트 `#82F1D0`, 상실=레드 `#FF7777`로 0.5 opacity 160 ms 유지 후 페이드 | 820 ms |
| 순위 숫자 롤 | `PositionText`가 획득 시 아래에서, 상실 시 위에서 롤 인 + 텍스트 opacity 0→1 | 280 ms |
| 세션 최속 랩(퍼플) | Status가 `BEST`로 바뀌면 보라 `#B68CFF` 레이어가 좌→우 스윕(ScaleX 0→1) 후 페이드 | 320 + 880 ms |
| 상태 배지(PIT/DT/SG/RET…) | `StatusText` 1.45→1 BackEase 팝 | 300 ms |

색상 의미: 순위 플래시는 이벤트 카드와 같은 민트(획득)/레드(상실) 팔레트를 쓴다. 전후방 거리의 RED=불리/BLUE=유리 규칙은 그대로다.

### 전후방 (`RelativeDriversView`)

- 앞차/뒷차 참가자 키가 바뀌면 해당 행이 위(앞차)/아래(뒷차)에서 14 px 슬라이드 인 + 페이드 (280 ms).
- 거리 색상(유불리)이 반전되면 거리 값이 1.22→1 팝 (240 ms).

### 세션 카드 (`SessionInfoView`)

- 랩 카운터 변경 시 위로 롤, 순위 값 변경 시 방향에 맞춰 롤 (280 ms). 남은 시간은 1초마다 바뀌므로 애니메이션하지 않음.

### 현재·섹터 타임 (`LapTimingView`)

- 랩 완료(직전 값 변경) 1.18 팝, 개인 최고 갱신 1.32 팝, 섹터가 `—`에서 값으로 바뀔 때 1.2 팝. 현재 랩 타임은 애니메이션하지 않음.

### 이벤트 카드 (`EventCardView`)

- 좌측 액센트 바(Accent 색) ScaleY 0→1, 카드 X -28→0 + 페이드, 본문 -16→0, 우측 값 +12→0 (240~380 ms).
- 이벤트 종료 시 X 0→-24 + 페이드 아웃 220 ms. `SetViewModel`이 종료 애니메이션 길이를 반환하고 `OverlayWindow`가 그 시간 동안 창을 유지(`_eventExitDeadline`)해 20 Hz 틱이 슬라이드 아웃을 끊지 않는다.

### Race Control 배너 (`RaceControlView`)

- 새 메시지: 위에서 -12 px 드롭 + 페이드, 플래그 색(Accent) 스윕 0.42 opacity 360 ms 후 700 ms 페이드.
- 같은 메시지에서 플래그 상태 라벨만 바뀌면 라벨 1.25 팝.
- 해제 시 위로 슬라이드 아웃 200 ms, 같은 deadline 방식으로 창 유지.

### 공통 (`HudMotion`)

`SlideIn/SlideOut/Roll/Pop/Sweep/GrowY`. 모두 지정한 요소의 transform 또는 opacity만 건드리고 휴지값(0, 1, 1)으로 끝난다. DataTemplate 안에서 선언된 transform은 WPF가 frozen 상태로 공유하므로 `EnsureTranslate/EnsureScale`이 애니메이션 가능한 복제본으로 한 번 교체한다(이 처리가 없으면 `InvalidOperationException: 개체가 봉인…`이 난다. 첫 테스트 실행에서 실제로 발생해 수정했다).

## 3. 오버레이별 켜기/끄기

이전: 체크박스는 존재했으나 `레이아웃 편집` 중에만 활성화되고, 편집 저장 시에만 파일에 기록됐다.

변경:

| 파일 | 변경 |
|---|---|
| `ClientStatusWindow.xaml` | `LayoutVisibilityPanel` 항상 활성, 안내 문구 변경, `모두 켜기`/`모두 끄기` 버튼 |
| `ClientStatusWindow.xaml.cs` | `SetLayoutEditState`가 패널을 비활성화하지 않음. `AreComponentTogglesEnabled`, `GetLayoutComponentStates`, `SetAllComponents(bool)` 추가 |
| `OverlayWindow.xaml.cs` | `SetComponentEnabled`가 `overlay-layout.json`을 즉시 저장 |
| `App.xaml.cs` | 시작 시 저장된 enabled 상태를 체크박스에 반영 |

저장 위치와 스키마는 그대로다: `%LOCALAPPDATA%\AMS2KRLeague\overlay-layout.json`의 `enabledComponents`. 전역 단축키는 추가하지 않았다. 상태창의 "입력 가로채기 없음" 정책과 충돌하고 AMS2 키 바인딩을 침범할 수 있기 때문이다.

## 4. 코드 변경 목록

```text
src/AMS2LeagueClient.Core/Presentation/TimingTowerTransitionTracker.cs   순위 숫자·상태 전이(IsNew, PositionGained/Lost, BecameFastestLap, ParsePosition)
src/AMS2LeagueClient/Presentation/OverlayHudView.xaml(.cs)               FlashLayer/PositionText/StatusText, Loaded 진입, 플래시·롤·팝·스윕
src/AMS2LeagueClient/Presentation/HudMotion.cs                            신규 공통 모션
src/AMS2LeagueClient/Presentation/RelativeDriversView.xaml(.cs)          SetViewModel + 슬라이드/팝
src/AMS2LeagueClient/Presentation/SessionInfoView.xaml(.cs)              SetViewModel + 롤
src/AMS2LeagueClient/Presentation/LapTimingView.xaml(.cs)                SetViewModel + 팝
src/AMS2LeagueClient/Presentation/EventCardView.xaml(.cs)                액센트 바, 슬라이드 인/아웃, 종료 길이 반환
src/AMS2LeagueClient/Presentation/RaceControlView.xaml(.cs)              플래그 스윕, 슬라이드 인/아웃, 라벨 팝
src/AMS2LeagueClient/Presentation/ClientStatusWindow.xaml(.cs)           토글 상시 활성, 모두 켜기/끄기
src/AMS2LeagueClient/Overlay/OverlayWindow.xaml.cs                       뷰 SetViewModel 호출, 종료 deadline, 토글 즉시 저장, IsEventCardSurfaceVisible
src/AMS2LeagueClient/App.xaml.cs                                          시작 시 토글 상태 반영
tests/AMS2LeagueClient.Tests/Program.cs                                  신규 10개 테스트 + 헬퍼
CHANGELOG.md                                                              Unreleased 항목
work/replay-cadence-audit/                                                리플레이 실측 도구(gitignored)
```

신규 테스트:

```text
Transition tracker reports position direction and fastest lap
Position change flashes row and rolls number
Fastest lap status sweeps purple without dimming
Tower rows build in when shown            (실제 창 표시 + Loaded 진입 애니메이션)
Component toggle persists without layout edit
Status window toggles are always enabled
Relative participant change animates
Session lap counter rolls
Event card exit keeps surface for animation (실제 창, 종료 deadline 후 숨김 확인)
Lap timing best lap pops
```

테스트는 WPF 애니메이션 값이 다음 TimeManager 틱 전에는 갱신되지 않으므로, 값이 아니라 clock 부착(`HasAnimatedProperties`)과 base value로 검증한다. 행 컨테이너 자체는 `HasAnimatedProperties=false`, `Opacity=1`을 유지해야 한다.

## 5. 데모 스모크 실행

`AMS2LeagueClient.exe --demo-events --auto-exit-seconds 16 --log-dir work/demo-smoke/logs`로 실행해 화면을 캡처했다(`work/demo-smoke/demo-1..3.png`). 결과는 이 문서와 함께 최종 메시지에 기록했다. 데모는 고정 데이터라 순위 이동·최속 랩 플래시는 재현되지 않고, 이벤트 카드 슬라이드 인/아웃과 타워 빌드만 확인된다.

## 6. 남은 확인과 권장 다음 단계

1. 실게임 멀티플레이에서 사용자 확인: 타워 빌드 stagger가 과하지 않은지, 추월 플래시 강도(0.5)와 퍼플 스윕(0.62)이 가독성을 해치지 않는지.
2. 세션 전환(예선→레이스) 첫 프레임에 순위가 그리드 순으로 재배열되며 여러 행이 동시에 플래시할 수 있다. 거슬리면 `TimingTowerTransitionTracker.Reset()`을 `SESSION_TRANSITION` 시점에 호출하도록 `PlayerOverlayCoordinator`에서 연결하면 된다.
3. Waiting 오버레이(멀티 대기 화면)는 애니메이션을 추가하지 않았다.
4. 커밋·태그·릴리스는 사용자 요청 시에만 진행한다.
