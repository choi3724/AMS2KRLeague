# 다른 오버레이 프로그램의 표시 방식 조사와 적용 계획 (2026-10-01)

사용자 요청: SimHub와 다른 오버레이 프로그램이 어떤 방식으로 HUD를 그리는지 상세히 분석하고(소스가 있으면 소스 분석), 우리 프로그램에 적용할 계획을 세운다.

이 문서는 조사·계획만 다룬다. 코드 변경은 없다. 각 단계 구현은 `docs/TASK.md`에 REQ를 추가하고 사용자 승인을 받은 뒤 진행한다.

## 조사 방법과 증거 수준

| 수준 | 의미 | 대상 |
|---|---|---|
| **코드 확인** | 저장소를 clone해서 해당 파일·줄을 직접 읽음 | iRon, iFL03, OpenKneeboard, fps_monitor, ShaderBeam, OBS `d3d11-subsystem`, TinyPedal, irdashies, Race Element, CrewChief(GameOverlay.Net), SecondMonitor, ams2hud, dotnet/wpf, Chromium 일부, Qt 5.15 일부 |
| **공식 문서·이슈** | 원문을 직접 읽음 | SimHub GitHub wiki·issues, Microsoft Docs 원본 저장소(MicrosoftDocs/win32, sdk-api, windows-driver-docs), Electron 이슈 |
| **검색 요약** | 프록시 차단으로 원문을 열지 못하고 검색 결과 요약만 확인 | simhubdash.com, RaceLab·Kapps·iOverlay 문서, learn.microsoft.com 본문, DirectX 블로그, 포럼. **인용 전 재확인 필요** |
| **추론** | 위 사실들에서 도출한 판단 | 문서에 "추론"으로 표시 |

- 상용 프로그램(SimHub 등)은 디컴파일하지 않았다. 문서·이슈·변경 이력만 사용했다.
- Windows에서 실행·측정한 것은 없다.
- 원본 조사 메모(경로:줄 인용 포함) 네 개는 [evidence/2026-10-01-overlay-rendering-research/](evidence/2026-10-01-overlay-rendering-research/)에 있다.
  - `research-native.md`: C++ 네이티브 오버레이
  - `research-qt-electron-dotnet.md`: Qt·Electron·.NET 오버레이
  - `research-closed.md`: SimHub와 상용 프로그램
  - `research-windows.md`: WPF·DWM·GPU 스케줄링 내부 동작

## 1. 우리 문제의 정확한 위치 (WPF 소스로 확인)

dotnet/wpf 커밋 `a4f9f07` 기준으로 확인한 내용이다.

1. **0.8.3까지의 기본 경로(투명 layered 창 + GPU 렌더링)는 매 프레임 GPU→CPU readback을 한다.**
   - `AllowsTransparency` 창은 `PresentUsingUpdateLayeredWindow`로 분류된다(`api_factory.cpp#L531-567`).
   - GPU 모드의 Present는 `PresentWithGDI` → `CD3DSwapChainWithSwDC::GetDC` → `ReadIntoSysMemBuffer` → `GetRenderTargetData` 순서로 진행된다(`d3ddevice.cpp#L3503-3651`, `d3dswapchainwithswdc.cpp#L175`, `d3dsurface.cpp#L393-412`).
   - 이렇게 읽어 온 결과를 `UpdateLayeredWindowIndirect`로 넘긴다(`oscompat.cpp#L108-190`).
   - (추론) WPF는 `GetRenderTargetData`가 반환되자마자 결과를 사용한다. 따라서 GPU가 그리기를 끝낼 때까지 동기적으로 기다리는 구조다. 게임이 GPU를 다 쓰면 이 대기가 길어진다.
2. **WPF는 프로세스 전체에서 렌더 스레드가 하나다.**
   - `NUM_WORKER_THREADS 1`(`partitionmanager.h#L38`), 우선순위 `THREAD_PRIORITY_NORMAL`(`MediaSystem.cs#L54-56`).
   - `CRenderTargetManager::Present`는 모든 창을 하나씩 차례로 Present한다(`rendertargetmanager.cpp#L637-650`, 직접 확인).
   - 따라서 **창 하나의 readback이 멈추면 HUD 14개가 모두 멈춘다.** 사용자가 본 "모든 게 뚝뚝 끊김"과 맞다.
   - 한 프레임이 끝나야 다음 프레임이 시작된다. 렌더 스레드는 `DwmFlush()`로 화면 갱신 주기를 기다리고(`rendertargetmanager.cpp#L1164-1216`), UI 스레드는 "Presented" 알림을 받은 뒤에 다음 프레임을 커밋한다(`MediaContext.cs#L2109-2147`).
3. **0.8.4의 SoftwareOnly 경로**
   - CPU로 그린 DIB를 dirty rect와 함께 `UpdateLayeredWindowIndirect`에 넘긴다(`swpresentgdi.cpp#L775`). D3D 장치를 만들지 않으므로 우리 프로세스는 GPU 작업을 하지 않는다.
   - 마지막 화면 합성은 DWM이 GPU로 한다. Microsoft 선점(preemption) 문서는 DWM 작업을 "High-priority GPU work"로 분류한다.
   - 남은 약점: 단일 렌더 스레드, 창을 하나씩 순서대로 처리하는 구조, `DwmFlush` 대기는 그대로다.
4. **GPU 스케줄링**
   - HAGS 환경에는 전면 앱용 FOCUS 대역이 있다. 이 대역에 밀린 NORMAL 대역 프로세스의 목표 GPU 몫은 데스크톱 기본값 **10%**다(`DXGKARG_SETUPPRIORITYBANDS.targetNormalBandPercentage`).
   - (추론) 백그라운드인 오버레이가 GPU를 쓰면 이 몫을 기다린다. 측정된 p95 55~97ms가 이 구조와 맞는다.

## 2. 프로그램별 분석

### 2.1 SimHub (상용, WPF) — 공식 wiki·이슈 확인
- **구조**: wiki(DashStudio-performance) 원문은 이렇다. "SimHub overlays and dashboards are not using d3d graphic hack, it's a series of borderless windows shown over or aside the game." Dash Studio는 "powered by WPF"다. 즉 **우리와 같은 WPF 투명 창 방식**이다.
- **같은 증상이 미해결로 남아 있다**: 이슈 #1119(2022), #1330, #1851(2025)는 모두 게임에 포커스가 있을 때 대시가 4~16fps로 떨어지고 포커스가 빠지면 회복된다는 보고다. 모두 코드 수정 없이 닫혔다.
- **공식 대응은 모두 사용자 쪽 설정이다**:
  - 게임만의 GPU 부하를 90~95% 이하로 유지한다(FPS 제한, vsync).
  - "GSync or Freesync can block WPF rendering" → 전체화면 전용으로 바꾸거나 SimHub를 예외로 등록한다.
  - "Reduce the overlay surfaces as much as possible (space taken on the screen, even if totally transparent have an impact)".
  - 모니터 주사율을 통일한다.
  - Windows 게임 모드, Process Lasso 등을 끈다.
- **오버레이 크기 제한**: 오버레이 하나당 최대 800×600(480,000픽셀)이다(wiki Dash-Studio-Overlays). layered 창은 비용이 면적에 비례하기 때문이다.
- **AMS2 실행 인자**: wiki는 `-windowed -borderless`를 권장한다.
- **렌더 선택지**: 확인된 것은 "HTML rendering mode"(Chromium 엔진) 하나뿐이다. 전역 또는 오버레이별로 켤 수 있다. 검색 요약 수준에서는 v9 기준으로 "대부분 HTML, 움직이는 맵·트래커는 WPF"라는 커뮤니티 권장과, 개발자가 "하드웨어 가속을 끌 수 없다"고 한 발언이 확인된다.
- **시사점**: SimHub도 이 문제를 구조적으로 풀지 못했다. **WPF를 GPU 모드 그대로 쓰는 한 우리도 같은 한계에 머문다.**

### 2.2 RaceLab · Kapps · iOverlay (상용) — 검색 요약 수준
- RaceLab은 Chromium 계열로 보인다. 문서에 "Chrome/Edge GPU process" 언급이 있다. Kapps와 iOverlay는 스택을 공개하지 않았다.
- 세 제품 모두 **"Hardware Acceleration" 토글**이 있다. 끊길 때 끄라고 안내한다. 우리 0.8.4의 CPU 기본값과 같은 발상이다.
- 공통 안내:
  - 오버레이 갱신률을 낮추고 오버레이 개수를 줄인다.
  - 게임은 테두리 없는 창으로 실행한다.
  - G-Sync·VRR, Windows 게임 모드, HAGS를 끈다.
  - FPS는 게임 내 제한 기능으로 제한한다.
- iOverlay: "G-Sync might detect iOverlay as the primary application"이라고 안내한다.

### 2.3 Race Element (오픈소스 .NET, **AMS2 지원**) — 코드 확인
우리와 가장 비슷한 오픈소스다.
- **창**: HUD마다 네이티브 창 하나. 스타일은 `WS_EX_LAYERED|TRANSPARENT|TOOLWINDOW|NOACTIVATE|TOPMOST`(`FloatingWindow.cs:274-291`).
- **그리기**: GDI+로 **CPU에서 그린 뒤** 매 프레임 `UpdateLayeredWindow(ULW_ALPHA)`로 창 전체를 갱신한다(`FloatingWindow.cs:65-114`, 직접 확인).
- **스레드**: **HUD마다 전용 백그라운드 스레드**를 두고 `timeBeginPeriod(1)`을 쓴다(`CommonAbstractOverlay.cs:187-239`). HUD 하나가 느려도 다른 HUD는 영향을 받지 않는다.
- **갱신률**: 기본 30Hz이고, 대부분의 HUD는 1~10Hz로 낮춰 둔다. AMS2 데이터는 300Hz로 읽는다(`Automobilista2/DataProvider.cs:24`).
- **프로세스 우선순위**: 오히려 **BelowNormal로 낮춘다**(`App.xaml.cs:196-197`).

### 2.4 TinyPedal (오픈소스, Qt/Python, rF2·LMU) — 코드 확인
- **창**: 위젯마다 최상위 창 하나. `WA_TranslucentBackground`, `FramelessWindowHint`, `Qt.Tool`, 클릭 통과는 `WindowTransparentForInput`(`widget/_base.py:96-138`).
- **그리기**: QPainter로 **CPU 래스터**를 한다. Qt 5.15는 dirty rect를 붙여 `UpdateLayeredWindowIndirect`로 내보낸다(`qwindowsbackingstore.cpp:89-110`). FAQ: "TinyPedal only utilizes CPU".
- **갱신**:
  - 위젯마다 타이머를 둔다. 빠른 위젯은 20ms, 느린 위젯은 100~500ms다.
  - **값이 바뀐 경우에만 다시 그린다**(`if target.last != data: ... update()`, `widget/speedometer.py:162-167`).
  - 공유 메모리는 별도 스레드가 10ms마다 복사한다.
- **우선순위 조정**: 없다.
- 이슈 #211, #233, #288의 끊김은 오버레이가 아니라 게임 프레임 페이싱 문제(G-Sync, AMD FRTC)였다.

### 2.5 SecondMonitor (오픈소스 WPF, AMS2·PCars2)
- 메인 창에 `RenderMode.SoftwareOnly`를 강제한다(`TimingGUI.xaml.cs:23-32`).
- 오버레이가 아니라 두 번째 화면용 프로그램이지만, **AMS2 WPF 도구가 CPU 렌더링을 택한 사례**다.

### 2.6 irdashies (오픈소스, Electron, iRacing) — 코드·이슈 확인
- **창 구조의 변화**:
  - 디스플레이당 투명 창 하나를 쓴다(`overlayManager.ts:261-353`).
  - v0.0.40(PR #303)에서 위젯별 창을 **전체화면 투명 창 하나로 합쳤다.** 그 직후 이슈 #357에서 게임 FPS가 30 이상 떨어졌다는 보고가 나왔다.
  - PR #447에서 창을 위젯 영역 + 20px로 다시 줄였다. 커밋 메시지는 "to avoid excessive GPU usage"(`overlayManager.ts:560-616`).
  - **교훈: 큰 투명 창 하나는 오히려 손해다.**
- **GPU 가속**: 기본으로 켜져 있고 끄는 옵션이 있다. 설정 화면에서 끄면 "will significantly impact performance"라고 경고한다.
- **데이터 갱신**:
  - 텔레메트리는 25Hz로 읽는다. 보이는 창이 필요할 때만 60Hz로 올린다.
  - **snapshot version이 바뀔 때만 전송한다.**
  - 채널마다 2~60Hz 상한을 두고, 숨겨진 창은 건너뛴다(`channelBus.ts:338-379`).

### 2.7 CrewChief 오버레이 (GameOverlay.Net) — 코드 확인
- **방식**: `WS_EX_LAYERED|TRANSPARENT|NOACTIVATE|TOPMOST` 창에 `SetLayeredWindowAttributes(255)`와 `DwmExtendFrameIntoClientArea(-1)`을 적용한다. 그 위에 **Direct2D GPU**로 premultiplied alpha 그리기를 한다(`WindowHelper.cs:128-139`, `Graphics.cs:150-186`).
- **readback이 없다.** 우리가 측정한 "DWM glass" 경로와 같은 계열이다.
- **갱신**: 30fps 고정이고 vsync는 끈다.

### 2.8 iRon / iFL03 (오픈소스 C++, iRacing) — 코드 확인
- **창**: HUD마다 창 하나. 스타일은 `WS_EX_TOPMOST|WS_EX_TOOLWINDOW|WS_EX_NOREDIRECTIONBITMAP`(`iRon/Overlay.cpp:135`, 직접 확인).
- **그리기**:
  - HUD마다 **D3D11 장치를 따로** 만든다.
  - `CreateSwapChainForComposition`(FLIP_SEQUENTIAL, 버퍼 2개, PREMULTIPLIED)에 D2D/DirectWrite로 그리고, DirectComposition으로 띄운다.
- **Present**: `Present(1,0)`을 한 스레드에서 차례로 호출한다(`Overlay.cpp:319`, 직접 확인). (추론) GPU 부하가 걸리면 대기가 창 수만큼 누적된다.
- **이슈 #11 "Laggy performance"**: 미해결이다. 작성자는 CPU 우선순위를 realtime으로 올려도 소용없었다고 적었다. **사용자가 겪은 상황과 같다.**
- **iFL03**: HUD별 `target_fps` 상한을 둔다. 순위표는 10fps, 나머지는 15~30fps, 입력 HUD는 30~60fps다(`AppControl.cpp:362-589`).

### 2.9 OpenKneeboard (오픈소스 C++) — 코드 확인
- **non-VR 방식**: 게임의 `IDXGISwapChain::Present`를 hook해서 게임 화면 안에 직접 그렸다(`NonVRD3D11Kneeboard.cpp:87-152`).
  - 그러다 **master에서 이 기능을 삭제했다**(커밋 939e765). 이유는 anti-cheat 비호환이다(이슈 #677).
  - v2 계획: **모니터당 투명 창 하나**에 `WS_EX_NOREDIRECTIONBITMAP`, FLIP 방식, DirectComposition을 쓰고, MPO를 활용해 VRR에 영향을 주지 않는다.
- **자체 창 설정**:
  - `BufferCount=3`, `FLIP_DISCARD`, **`Present(0,0)`**(`TabPage.xaml.cpp:502-574`, 직접 확인).
  - 주석: "triple-buffer to avoid stalls … decouple the frame rates".
- **갱신**: 90Hz 타이머를 돌리되 **다시 그릴 필요가 있을 때만** 그린다.

### 2.10 그 밖의 참고 사례
- **fps_monitor (C++ ImGui)**
  - `WS_EX_LAYERED|NOREDIRECTIONBITMAP|TRANSPARENT|NOACTIVATE` 창 위에 DirectComposition으로 그린다. 클릭 통과까지 해결한 예다.
  - 값이 바뀔 때만 그리고, 고해상도 waitable timer로 대기한다(`main.cpp:446-490`).
- **ShaderBeam**
  - `SetGPUThreadPriority`와 `SetMaximumFrameLatency`를 쓰고, 렌더 스레드를 TIME_CRITICAL로 둔다.
  - 그래도 README는 "not being given enough GPU time by the OS"라고 인정하고 **두 번째 GPU를 권장**한다.
- **OBS**
  - `D3DKMTSetProcessSchedulingPriorityClass`로 HAGS 환경에서는 HIGH, 그 외에는 REALTIME을 설정한다(`d3d11-subsystem.cpp:436-459`).
  - 관리자 권한이 필요하고 Intel에서는 건너뛴다. 정확한 값은 CI 비밀값으로 관리되어 공개되지 않았다.
- **Steam·Discord·RTSS 같은 주입형 오버레이**
  - 게임의 Present를 hook해서 게임 프레임 안에 그린다.
  - 레이싱 도구들은 anti-cheat(iRacing의 EAC), 다른 hook과의 충돌, 시뮬마다 따로 대응해야 하는 부담 때문에 피한다.

## 3. 비교 요약

| 프로그램 | 기술 | 창 구성 | 투명 방식 | 그리는 곳 | 갱신 방식 | GPU 100% 대응 |
|---|---|---|---|---|---|---|
| **우리 0.8.3** | WPF | HUD별 최대 14개 | layered + GPU readback | GPU | 렌더 프레임마다 + 144Hz 시계 | 없음 → 0.2~0.5초 끊김 |
| **우리 0.8.4** | WPF SoftwareOnly | 동일 | layered ULWI | CPU | 동일(144Hz 시계 미사용) | GPU 대기열 회피(미측정) |
| SimHub | WPF(+HTML) | 오버레이별 | WPF 투명 창 | GPU | — | 사용자 설정 안내뿐, 같은 이슈 미해결 |
| RaceLab/Kapps/iOverlay | Chromium 계열 추정 | — | — | GPU(끄기 가능) | 갱신률 설정 | HW 가속 끄기 안내 |
| Race Element | .NET GDI+ | HUD별 | layered ULW | **CPU** | **HUD별 스레드**, 1~30Hz | 프로세스 우선순위 BelowNormal |
| TinyPedal | Qt QPainter | 위젯별 | layered ULWI(dirty) | **CPU** | 위젯별 타이머, **값 변경 시만** | 없음(CPU만 사용) |
| SecondMonitor | WPF | 메인 창 | (오버레이 아님) | **CPU** | — | — |
| irdashies | Electron | 디스플레이별(영역 축소) | Chromium DComp | GPU | **변경 시 전송**, 채널별 상한 | 큰 창 회귀 후 축소 |
| CrewChief | D2D(GameOverlay.Net) | 2개 | DWM glass | GPU | 30fps 고정 | 없음 |
| iRon/iFL03 | D3D11+D2D+DComp | HUD별 | NOREDIRECTIONBITMAP | GPU | 60Hz 전부 / HUD별 fps 상한 | 없음, 렉 이슈 미해결 |
| OpenKneeboard | D3D11+DComp(구: 주입) | 모니터당 1개(계획) | NOREDIRECTIONBITMAP | GPU | **필요할 때만**, `Present(0,0)`, 버퍼 3개 | 주입 방식은 anti-cheat 때문에 폐기 |

## 4. 결론

1. **0.8.3 기본 경로(WPF layered + GPU readback)를 쓰는 프로그램은 조사한 것 중 하나도 없었다.**
   - CPU로 그리는 쪽(Race Element, TinyPedal, SecondMonitor, Qt·Chromium의 GPU 가속을 끈 경로)이 있다.
   - GPU로 그리는 쪽은 readback 없이 DWM이 직접 합성하는 방식(DirectComposition, DWM glass)이다.
   - 0.8.4의 CPU 전환은 업계 사례와 같은 방향이다.
2. **GPU로 그리는 방식으로 "게임 GPU 100%"를 구조적으로 해결한 사례는 없다.**
   - SimHub, RaceLab, iRon 모두 사용자에게 FPS 제한을 안내하거나 이슈가 미해결로 남아 있다.
   - GPU 우선순위 API는 OBS와 ShaderBeam만 쓴다. 그 둘도 관리자 권한이 필요하거나 효과가 불완전하다고 스스로 밝힌다.
   - 따라서 **CPU 경로를 기본으로 유지하는 판단이 맞다.**
3. **끊기지 않는 프로그램들의 공통점은 렌더러 종류보다 다음 네 가지다.**
   - (a) 값이 바뀔 때만 다시 그린다.
   - (b) HUD별 갱신률 상한을 둔다. 순위표 1~10Hz, 계기판 30~60Hz.
   - (c) 창 면적을 내용 크기에 딱 맞게 유지한다. SimHub는 800×600 상한을 두고, irdashies는 회귀를 겪었다.
   - (d) HUD마다 독립된 스레드에서 그린다(Race Element). 한 HUD가 느려도 다른 HUD가 멈추지 않는다.
4. **우리에게 남은 구조적 약점은 (d)다.** SoftwareOnly여도 WPF는 렌더 스레드 하나가 모든 창을 순서대로 그리고, `DwmFlush` 주기에 묶여 있다. 큰 N 계기판의 CPU 래스터가 길어지면 다른 HUD도 함께 늦어진다.
   - 관련 사실: 0.8.2에서 점화 연출을 위해 N 계기판 캔버스를 750 → 1090 설계 단위로 키웠다(`AvanteClusterView.CanvasTop=-150`, `CanvasHeight=1090`). layered 창 비용은 면적에 비례한다.

## 5. 적용 계획 (단계별, 각 단계는 별도 REQ와 승인 필요)

### 0단계 — 0.8.4 실측 (사용자, 집에서)
- 0.8.4를 기본 상태와 `--monitor-hardware` 상태로 각각 같은 트랙·차량에서 실행해 비교한다.
- 수집할 것:
  - 로그 `MONITOR_PRESENTATION_PATH`와 `PERFORMANCE`(`renderCallbackHz`, `renderGapMaxMs`, `drivingHz`, `drivingReadRejected`, `cpu`).
  - 가능하면 PresentMon으로 AMS2의 PresentMode를 오버레이 끈 상태와 켠 상태에서 비교한다(Independent Flip ↔ Composed).
  - 게임 FPS 상한을 건 경우와 안 건 경우를 비교한다.
- **판정**:
  - CPU 경로에서 끊김이 사라지면 1단계만 진행한다.
  - 남아 있으면 `renderGapMaxMs`가 큰지(렌더 스레드 문제), `drivingHz`가 낮은지(데이터 문제)로 2단계 진행 여부를 정한다.

### 1단계 — WPF 안에서 할 수 있는 저위험 개선 (0.8.5 후보)
1. **HUD별 갱신률 상한과 변경 시에만 다시 그리기.** 근거: TinyPedal, irdashies, iFL03, Race Element.
   - 순위표·전후방·세션 HUD는 값이 바뀔 때만, 최대 10Hz로 갱신한다.
   - N 계기판·페달은 최대 60Hz로 하되, 같은 샘플이면 다시 그리지 않는다.
   - 기존 `RefreshDrivingViews`·`UiTick` 경로를 점검해서 변하지 않은 창의 무효화를 없앤다.
2. **창 면적 축소.** 근거: SimHub 800×600 상한, irdashies PR #447.
   - N 계기판의 점화 연출용 확장 영역(위 150, 아래 190 설계 단위)은 연출 중에만 쓰고, 평상시에는 기존 750 높이로 되돌리는 방안을 검토한다. **디자인 변경에 해당하므로 사용자 결정이 필요하다.**
   - HUD별 실제 물리 픽셀 면적을 로그로 남겨 큰 창을 찾는다.
3. **`UiTick`을 Background 우선순위에서 올리는 것 검토.** 0.8.4 보고서의 "발견했지만 고치지 않은 문제"다. Render 작업에 밀려 20Hz 갱신이 굶는 것을 막는다.
4. **사용자 안내 문서.** SimHub·RaceLab의 공통 권장을 README_KO에 반영한다.
   - AMS2 테두리 없는 창(`-windowed -borderless`), 게임 내 FPS 상한, G-Sync는 전체화면 전용으로 설정, 모니터 주사율 통일.
   - **새 문구 추가이므로 사용자 승인 대상이다.**

### 2단계 — 구조 변경: "HUD별 독립 CPU 렌더러" (Race Element·TinyPedal 방식)
**목표**: WPF의 단일 렌더 스레드, 창별 순차 Present, `DwmFlush` 대기를 우회한다. HUD 하나가 느려도 나머지는 계속 움직이게 한다.

**구성** (추론에 기반한 설계이며 시제품 측정이 필요하다):
- **창**: Win32 창을 직접 만든다. 스타일은 `WS_EX_LAYERED|TRANSPARENT|TOOLWINDOW|NOACTIVATE|TOPMOST`로 현재와 같다. 배치·편집 모드는 기존 `OverlayWindowInterop`의 좌표·스타일 로직을 재사용한다.
- **그리기, 두 가지 선택지**:
  - **A. 기존 WPF 그리기 코드 재사용 (권장 시작점).**
    - HUD마다 전용 STA 스레드와 Dispatcher를 두고, 그 스레드에서 기존 View(DrawingVisual 기반)를 만든다.
    - `RenderTargetBitmap.Render`로 CPU 래스터한 뒤 `CopyPixels`로 premultiplied BGRA DIB에 복사하고, dirty rect를 붙여 `UpdateLayeredWindowIndirect`로 내보낸다.
    - 장점: 계기판 디자인 코드를 다시 짤 필요가 없다. `RenderTargetBitmap`은 공용 렌더 스레드를 거치지 않고 호출 스레드에서 바로 렌더링된다(WPF 소스로 코드 확인).
      - `BitmapVisualManager.Render`는 `AllocateSyncChannel`을 쓴다(`BitmapVisualManager.cs:104`).
      - 이 채널은 `WgxConnection_Create(true /* synchronous transport */)`로 만들어진다(`ChannelManager.cs:109-117`).
      - 그 결과 "immediate execution"을 하는 `CSameThreadComposition`에서 실행된다(`samethreadcomposition.cpp`).
      - 단, 실제 HUD별 처리 비용은 시제품으로 측정해야 한다.
    - 위험: `RenderTargetBitmap` 생성·복사 비용과 할당량. 버퍼 재사용으로 줄인다.
  - **B. SkiaSharp CPU 래스터로 재작성.**
    - `SKSurface.Create(info, pixels, rowBytes)`로 DIB에 직접 그린다. 가장 가볍고 스레드 제약이 없다.
    - 대신 N 계기판 등 모든 디자인을 다시 구현해야 하므로 디자인 회귀 위험이 크다.
    - A로 부족할 때만 HUD 단위로 선택한다.
- **스레드·주기**:
  - HUD마다 렌더 스레드를 두고, 고해상도 waitable timer(0.8.4의 `TelemetryReadThread`와 같은 방식)로 HUD별 상한 주기를 지킨다.
  - 새 샘플이 없으면 다시 그리지 않는다.
  - SHM 표시 읽기는 0.8.4의 `TryReadDriving`을 그대로 쓴다. 리더 내부 잠금이 있어 여러 스레드에서 호출해도 안전하다.
- **이행 순서**:
  1. N 계기판 하나에 `--monitor-threaded-n` 같은 시험 옵션으로 시제품을 만든다.
  2. 기존 하네스(renderer-matrix, monitor-structure)로 GPU 부하 조건에서 기존 경로와 A/B 측정한다.
  3. 효과가 확인되면 페달, 그래프, 나머지 HUD 순서로 옮긴다. 옮길 때마다 기존 WPF 경로는 복구 옵션으로 남긴다.
- **보호할 것**: 디자인 픽셀 일치(기존 캡처 비교 테스트), 클릭 통과, 배치 편집, VR 텍스처 경로, 기록·전송 계약.

### 3단계 — GPU 경로 (2단계로도 부족할 때만, OpenKneeboard 방식)
기존 시험 경로 `--monitor-retained-n`(D3D11+D2D+DComp)을 조사 결과에 맞게 고친다.
- **장치와 창**:
  - 모든 HUD가 **장치 하나를 공유**한다. iRon처럼 HUD마다 장치를 만들지 않는다.
  - 가능하면 **모니터당 창 하나 + DComp visual tree**로 구성한다. OpenKneeboard v2 계획과 같은 구성이다.
  - 단, irdashies 교훈에 따라 창 면적은 HUD 영역으로 제한한다(창 여러 개 + 장치 1개도 대안).
- **Present 설정**: `FLIP_DISCARD`, 버퍼 3개, `Present(0,0)` 또는 `DXGI_PRESENT_DO_NOT_WAIT`, `SetMaximumFrameLatency(1)` + waitable object. 블로킹 `Present(1,0)`은 쓰지 않는다.
- **클릭 통과**: fps_monitor의 `WS_EX_LAYERED|NOREDIRECTIONBITMAP|TRANSPARENT` 조합을 쓴다.
- **GPU 우선순위**: `D3DKMTSetProcessSchedulingPriorityClass(HIGH)`는 측정용 옵션으로만 둔다. 관리자 권한과 효과 모두 확인되지 않았다.
- **한계**: 이 경로도 게임과 GPU를 나눠 쓴다. 조사한 GPU 기반 도구 모두 FPS 제한 안내에 의존하고 있다.

### 하지 않을 것
- **게임 Present 주입(hook)**
  - AMS2는 anti-cheat가 없다는 포럼 요약은 있다(검증 필요).
  - 그래도 클라이언트 구조 변경이고, 게임 업데이트에 취약하며, OpenKneeboard조차 폐기한 방식이다.
  - AGENTS.md상 별도 승인 없이 구현할 수 없다.
- **전체화면 투명 창 하나로 합치기.** irdashies에서 게임 FPS가 30 이상 떨어진 회귀가 있었다.
- **GPU 우선순위 상향을 주 해결책으로 삼기.** ShaderBeam, OBS 사례상 불완전하고 권한 문제가 있다.

## 6. 남은 불확실성
- SimHub "HTML rendering mode"의 정확한 내부 구현과 변경 이력 버전은 원문 접근 실패로 미확인이다.
- RaceLab, Kapps, iOverlay의 스택과 설정 문구는 검색 요약 수준이다.
- `RenderTargetBitmap`이 호출 스레드에서 동기 실행된다는 점은 소스로 확인했다. HUD별 처리 비용과 할당량은 시제품으로 측정해야 한다.
- 모든 성능 판단은 Windows 실측 전이다. Monitor 성능 상태는 RED다.
