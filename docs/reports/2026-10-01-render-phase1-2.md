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

REQ-REDRAW-ON-CHANGE-088 [PARTIAL — 코드·테스트 작성, Windows 실행 없음]
  변경: `Core/Presentation/DisplaySignature.cs`(신규), `OverlayWindow.ApplyViewModel`·`ShowGameplaySurfaces`
  원인:
  - 순위·전후방·랩 타이밍의 표시 키에 SHM sequence가 들어 있었다. 그래서 게임 프레임마다(20Hz 상한) 새 모델로 다시 바인딩됐다.
  - 문자열 색상이 매번 새 Brush로 변환되어 같은 화면을 다시 그렸다.
  수정:
  - 실시간 경로에서 모델의 모든 표시 값(순위 행 포함) 서명이 같으면 세 뷰의 바인딩을 건너뛴다.
  - 편집·미리보기·활성화·불투명도 변경 때는 서명을 초기화해 항상 반영한다.
  나머지 UI 점검 결과: 이미 값을 비교한 뒤 그린다.
  - 속도·기어: 같은 문자열이면 변화 없음
  - 페달 막대: 같은 목표값은 무시
  - 대시보드: 표시 키 비교
  - 세션·이벤트·레이스 컨트롤·대기: 키 비교
  - N 계기판: 값 묶음 비교
  - 그래프 스크롤·바늘 보간·점멸·연출은 움직임이므로 그대로 둔다.
  수용: `DisplaySignatureTracksShownContent`, `TimingViewsRebindOnlyOnChange`

REQ-N-INTRO-AREA-088 [PARTIAL — 코드·테스트 작성, Windows 실행 없음]
  변경:
  - `AvanteClusterView`: RestDesignArea, RestPlacement, DesignViewport, IsIgnitionRunning
  - `OverlayWindow.PlaceAvante/PlaceAvantePanels`
  - threaded 배치에 viewport 전달
  동작:
  - 평상시 창은 계기판 영역만 덮는다(설계 단위로 일반형 908×770, 확장형 2048×770).
  - 전체 캔버스(1138×1090 / 2048×1090)는 시작 연출 중과 편집·미리보기 중에만 쓴다.
  - 연출이 시작되는 프레임 전에 창을 키우고, 끝나면 다음 20Hz 갱신에서 줄인다.
  - 창 영역에 정확히 대응하는 설계 영역을 뷰에 지정하므로, 두 크기에서 계기판 화면 위치가 같다.
  - 저장 배치와 편집 화면은 전체 캔버스 그대로다. DComp N 시험 경로도 전체 캔버스를 유지한다.
  수용: `AvanteRestWindowKeepsDialPosition`(중심·모서리 3점이 ±0.5px 이내), `AvanteWindowUsesFullCanvasOnlyForIntroAndEditing`

REQ-HUD-REFRESH-089 [PARTIAL — 코드·테스트 작성, Windows 실행 없음]
  144Hz의 출처:
  - 0.3.1에서 WPF 애니메이션 `DesiredFrameRate` 힌트로 들어왔다. 당시 문서에 "현재 PC는 두 디스플레이가 144Hz"라고 적혀 있다.
  - 0.7.2에서 같은 PC에 맞춰 움직임 시계를 144Hz로 만들었다. 60Hz 시험은 144Hz 화면과 박자가 맞지 않아 더 나빴다.
  - 즉 개발 PC 모니터에 맞춘 고정값이었다.
  변경:
  - `DrivingHudSettings.HudRefreshRate`, `HudFrameRate`(신규, DWM 합성 주기로 모니터 주사율 판단)
  - `MonitorPresentationClock`(초 단위 deadline, 설정값 사용, 소프트웨어 HUD도 사용)
  - `HudMotion.ConfigureFrameRate(int?)`, App 시작 시 저장 설정 읽기
  - `PlayerOverlayCoordinator.DrivingFrame` 누적 deadline, `ThreadedAvanteHud` 캡처 주기
  - 설정 창의 "HUD 화면 갱신 빈도" 항목과 안내 문구
  수용:
  - `HudRefreshRateSettingIsNormalized`
  - `DrivingFramesDoNotThrottleLocalReads` 갱신: 144Hz 설정이면 144회, 60Hz 설정이면 144Hz 프레임에서 58~62회 읽는다.
  - `DrivingHudRenderingAndSettings`: 설정 창의 갱신 빈도 선택 상자를 확인한다.
  - 누적 deadline 계산을 별도로 모의 계산했다. 60/144→60, 30/144→30, 75/165→75, 144/60→60.

## 요청하지 않은 변경
- 0건.

## 승인 대기
- 사용자 결정에 따라 이번 변경은 미게시 0.8.4에 포함한다.
- 릴리즈 절차: Windows에서 `build-release.ps1`(테스트 포함)을 실행한 뒤 태그와 게시를 진행한다.

## 발견했지만 고치지 않은 문제
- VR 출력 타이머는 HUD 변화와 관계없이 매 프레임 텍스처를 합성한다. 시험 지원 기능이라 이번에는 바꾸지 않았다.
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
