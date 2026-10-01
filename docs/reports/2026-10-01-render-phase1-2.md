# 렌더링 개선 1·2단계 구현 (2026-10-01)

근거 문서: [다른 오버레이 조사와 적용 계획](2026-10-01-overlay-rendering-research.md). 사용자는 1·2단계 진행을 승인했고, 실측은 나중에 직접 하기로 했다.

## 기준선
- 브랜치 `claude/jolly-hypatia-euwyty`. 시작 HEAD는 `56940c4`(0.8.4 미게시 + 조사 문서)이고 working tree는 clean이었다.
- `verify.sh`는 Windows 전용이라 **실행하지 못했다**. 대신 Linux에서 솔루션 전체를 교차 빌드했다(`EnableWindowsTargeting`). 결과는 warning 0 / error 0.
- 보호 대상 기능:
  - 기본 CPU 렌더 경로와 `--monitor-hardware` / `--monitor-glass` / `--monitor-retained-n`
  - N 계기판 디자인과 점화 연출 1회 재생
  - 배치 편집, 클릭 통과, VR
  - 30Hz 기록, 기록·전송 계약

## 요구사항
REQ-DISPLAY-DEDUP-087 [PARTIAL — 코드·테스트 작성, Windows 실행 없음]
  변경: `SharedMemoryReader.LastDrivingSequence`, `PlayerOverlayCoordinator.IsUnchangedGameFrame`
  - SHM sequence가 직전 게시 때와 같으면(게임이 새 프레임을 쓰지 않음) 같은 값을 다시 게시하지 않는다.
  - 새로 표시된 HUD가 값을 받을 수 있도록, 값이 같아도 100ms마다는 다시 게시한다.
  - 숨김 상태와 프로세스 분리 때는 기준 샘플을 초기화한다.
  - 로그에 `drivingDuplicateFrames`를 추가했다.
  수용:
  - `DrivingFramesDoNotThrottleLocalReads`를 갱신했다. 매 프레임 게임 쓰기를 흉내 내면 144건이 모두 게시되고, 쓰기가 없는 10프레임은 게시 0건, 120ms 후에는 1건 재게시되는지 확인한다.
  - 영향: 입력 그래프 이력에는 같은 값의 중복 점이 더 이상 들어가지 않는다. 게임이 쓰지 않은 구간은 직전 실제 점과 다음 실제 점이 선으로 바로 이어진다.

REQ-SURFACE-LOG-087 [PARTIAL]
  변경: `OverlayWindow.DescribeVisibleSurfaces`, `PlayerOverlayCoordinator.SamplePerformance`
  - 보이는 HUD 창의 물리 픽셀 크기와 합계가 바뀔 때마다 `OVERLAY_SURFACES` 로그를 남긴다. 10초 성능 주기 안에서만 확인한다.
  수용: 실제 로그는 미확인이다.

REQ-GUIDE-087 [DONE — 문구]
  변경: `release/README_KO.txt` "오버레이가 끊겨 보일 때" 4줄.

REQ-N-THREADED-087 [PARTIAL — 시험 옵션, Windows 실행 없음]
  변경:
  - `Presentation/ThreadedAvanteHud.cs`(신규), `Overlay/OverlayWindow.ThreadedN.cs`(신규)
  - `MonitorPresentationClock`: UI 스레드별 시계, 외부 표시 source 등록
  - `AvanteClusterView`: 정적 geometry 3개 Freeze, `IsAnimating`, 점화 1회 보정 helper
  - `AuxiliaryOverlayWindow.SetExternalPresentation`, `ClientStartupPolicy`(`--monitor-threaded-n`), App 연결
  동작:
  1. N 일반형과 확장형이 각각 전용 STA 스레드를 갖는다.
  2. 그 스레드에서 기존 WPF `AvanteClusterView`를 숨은 HwndSource에 둔다. 따라서 `IsLoaded`·`IsVisible` 조건이 그대로 성립한다.
  3. `RenderTargetBitmap`으로 래스터한다. WPF 소스상 동기 채널(`CSameThreadComposition`)로 실행되어 공용 렌더 스레드와 GPU를 쓰지 않는다.
  4. 결과를 네이티브 layered 클릭 통과 창(`WS_EX_LAYERED|TRANSPARENT|TOOLWINDOW|NOACTIVATE|TOPMOST`)에 `UpdateLayeredWindow`로 표시한다.
  5. 입력이 바뀌었거나 움직임(바늘 보간, 점멸, 점화)이 진행 중일 때만 최대 60Hz로 캡처한다.
  6. WPF 패널은 위치·크기·편집을 계속 맡는다. 첫 프레임이 표시된 뒤에만 WPF 내용을 접는다.
  7. 편집·미리보기·실패 시에는 WPF 표시로 돌아간다. 점화 연출은 표시가 옮겨 가도 게임 창당 한 번만 재생된다.
  수용: 테스트를 추가했다.
  - `ThreadedNIsExplicitAndLayeredOnly`
  - `AvanteStaticResourcesAreFrozen`
  - `ThreadedNPresentsOffTheUiThreadAndFallsBack`: 첫 프레임 표시 → 외부 표시 전환 → 편집 시 해제·복귀
  - Windows에서 실행하지 않았다.

## 요청하지 않은 변경
- 0건.
- 1단계 후보였던 "N 점화 영역 축소"는 디자인 변경이라 사용자 결정 전까지 보류했다.

## 승인 대기
- 버전 상향과 릴리즈는 하지 않았다. CHANGELOG에는 "Unreleased"로 기록했다.
- 0.8.4가 아직 게시되지 않았으므로, 이번 변경을 0.8.4에 포함할지 0.8.5로 낼지 결정이 필요하다.

## 발견했지만 고치지 않은 문제
- 기존 `SetRetainedPresentation`으로 접힌 내용을 `ApplyComponentOpacities`가 다시 Visible로 되돌릴 수 있다. retained N 시험 경로에 해당한다. 새 외부 표시 경로는 이 함수에서 예외 처리했다.
- `UiTick`의 Background 우선순위: CPU 기본 경로에서는 144Hz 시계를 쓰지 않으므로 굶을 가능성이 낮다. 그래서 바꾸지 않았다.

## 최종 게이트
- `verify.sh`: 미실행(Windows 전용). **PARTIAL**.
- Linux 교차 빌드(Client, Core, Activity, 두 테스트 프로젝트): warning 0 / error 0.
- Windows 테스트, 실제 화면, 실게임: **NOT TESTED**.
- 확인 필요:
  - `--monitor-threaded-n` 실행 시 화면이 WPF 표시와 같은지(안티앨리어싱 차이 가능)
  - 점화 연출
  - 창 이동·배율 변경 시 위치
  - CPU 사용량
- Monitor 성능 **RED** 유지.
