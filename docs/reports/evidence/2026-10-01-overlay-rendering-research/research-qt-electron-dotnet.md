# How open-source sim-racing overlays render transparent topmost HUDs on Windows (Qt, Electron, .NET)

Research date: 2026-10-01. Sources were cloned into `/tmp/claude-0/research/` (`git clone --depth 1`, plus blobless history clones where noted).

Labels used below:
- **[CODE]**: verified by reading source at the cited path and line.
- **[DOC]**: verified in the project's own docs, changelog, PR, or issue text (fetched).
- **[INFER]**: my inference. Not verified in code.

| Repo | Commit read | Stack |
|---|---|---|
| https://github.com/s-victor/TinyPedal | `6ac9ab3` (2026-10-01) | Python + PySide2 5.15 (PySide6 optional) |
| https://github.com/tariknz/irdashies | `88e4c95` (2026-09-26) | Electron + React |
| https://gitlab.com/mr_belowski/CrewChiefV4 (contains vendored `GameOverlay.Net`) | `85ec43781` (2026-09-29) | .NET Framework WinForms + SharpDX Direct2D |
| https://github.com/RiddleTime/Race-Element | `55121bb` (2026-09-20) | .NET (WPF app shell, WinForms/GDI+ HUDs), **supports AMS2** |
| https://github.com/Winzarten/SecondMonitor | `1d191e1` (2024-04-19) | .NET WPF (second-screen app, not an overlay), has a PCars2/AMS2 connector |
| https://github.com/loz-archer/ams2hud | `52dae6b` (2026-01-29) | Electron, AMS2 through CREST2 HTTP |
| Chromium `components/viz` and Qt `qtbase` 5.15 | raw files from GitHub mirrors | To show which present path the Qt and Electron overlays actually use |

---

## 0. Summary table

| Program | Windows | Transparency / present path | Raster | Paint loop | Repaints only on change? | Priority handling |
|---|---|---|---|---|---|---|
| TinyPedal | **1 top-level QWidget per widget** (~80 widget types) | Qt `WA_TranslucentBackground` + frameless → Qt Windows backing store → **`UpdateLayeredWindowIndirect` with a dirty rect** | **CPU (QPainter raster)**. The FAQ says "TinyPedal only utilizes CPU" | Per-widget `QBasicTimer`, default **20 ms** for most widgets (10/50/100/200/500 ms for others). Data sync thread runs every 10 ms | **Yes.** Each sub-element keeps `last` and only calls `update()` when the value changed | None |
| irdashies | **1 BrowserWindow per display** (since v0.0.40; before that, 1 per widget) + settings window | Chromium GPU compositor by default (DirectComposition). Optional `disableHardwareAcceleration` → Chromium software output → **`UpdateLayeredWindow`** | GPU by default, CPU optional | Telemetry poll 25 Hz (60 Hz only while a 60 Hz channel is visible). Per-channel rate-limited IPC at 2–60 Hz | **Yes.** Processors bump `version` only on change, and `publishIfChanged` drops unchanged snapshots | None. Mitigations are **shrink-wrapping the window to the widgets' bounding box** and user Chromium flags |
| CrewChief overlay (GameOverlay.Net) | 2 (chart/console overlay, subtitle overlay) | `WS_EX_LAYERED \| TRANSPARENT \| NOACTIVATE \| TOPMOST` + `SetLayeredWindowAttributes(alpha=255)` + **`DwmExtendFrameIntoClientArea(-1)`** (DWM "glass") + Direct2D `HwndRenderTarget` with premultiplied alpha | GPU (D2D HWND RT, `RenderTargetType.Default`) | Dedicated thread, **continuous 30 FPS**, VSync off (`PresentOptions.Immediately`) | No. It clears and redraws every frame | None |
| Race Element | **1 native window per HUD** (NativeWindow) | `WS_EX_LAYERED \| TRANSPARENT \| TOOLWINDOW \| NOACTIVATE \| TOPMOST` + GDI+ bitmap → **`UpdateLayeredWindow(ULW_ALPHA)`** | **CPU (GDI+)** | **One background MTA thread per HUD**, `RefreshRateHz` (default 30, many HUDs at 1–20 Hz), `timeBeginPeriod(1)` | No (time-driven). Optional `RequestsDrawItself` mode (5 Hz loop, redraw on request) | **Process `PriorityClass = BelowNormal`** |
| ams2hud | 1 HUD BrowserWindow + director + settings | Electron `transparent:true`, default hardware acceleration | GPU (Chromium default) | CREST2 HTTP poll at TickRate 24 Hz; `setFrameRate(60)` (only effective for offscreen rendering) | React-driven | None |
| SecondMonitor | Normal WPF window (not an overlay) | n/a | **Forces WPF `RenderMode.SoftwareOnly`** on the main GUI | Connector polls every 10 ms | — | None |

**Main takeaway [INFER, built on the CODE facts below]:** None of the CPU-based overlays (TinyPedal, Race Element, and Chromium with hardware acceleration disabled) ever read pixels back from the GPU. They raster into a DIB and hand it to DWM through `UpdateLayeredWindow(Indirect)`. The GPU-based ones (CrewChief/GameOverlay.Net, Chromium with hardware acceleration on) let DWM compose a GPU surface directly: a D2D HWND target on a DWM-glass window, or a DirectComposition premultiplied swap chain. **No one uses WPF's hardware layered-window path**, which is the `PresentWithGDI → GetRenderTargetData` GPU→CPU readback in our trace. That readback is what serialises the overlay behind the game's saturated GPU queue. Switching WPF to `SoftwareOnly` puts us on the same architecture as TinyPedal and Race Element.

---

## 1. TinyPedal (Python + Qt)

### 1.1 Stack and Qt binding [CODE]
- `requirements.txt` (UTF-16) pins `PySide2==5.15.2.1` and `psutil==6.1.1`. PySide6 is only an opt-in: `run.py:60-73` adds a `--pyside {2,6}` CLI flag (default 2) for source runs, and `run.py:77-87` swaps the modules (`sys.modules["PySide2"] = PySide6`). **So the default binding is PySide2/Qt 5.15, not PySide6.**
- `tinypedal/main.py:107-128` `init_gui()` makes no OpenGL or RHI setup and sets no `AA_UseOpenGLES`/`AA_UseSoftwareOpenGL`. It uses `QApplication.setStyle("Fusion")` and `QPixmapCache.setCacheLimit(0)` (line 125).
- `main.py:157-183` sets only `QT_QPA_PLATFORM` (PySide6: `windows:darkmode=2:fontengine=freetype`), multimedia, and high-DPI variables.

### 1.2 Window creation: one top-level native window per widget [CODE]
`tinypedal/widget/_base.py`:
```python
44 class Base(QWidget):
47     def __init__(self, config, widget_name):
48         super().__init__()               # no parent -> top-level native window
...
96  def __set_window_attributes(self):
98      self.setWindowOpacity(self.wcfg["opacity"])
99      self.setAttribute(Qt.WA_DeleteOnClose, True)
100     if self.cfg.compatibility["enable_translucent_background"]:
101         self.setAttribute(Qt.WA_TranslucentBackground, True)
...
105 def __set_window_flags(self):
107     self.setWindowFlag(Qt.FramelessWindowHint, True)
108     self.setWindowFlag(Qt.WindowStaysOnTopHint, True)
109     if not self.cfg.overlay["vr_compatibility"]:  # hide taskbar widget
110         self.setWindowFlag(Qt.Tool, True)
111     if self.cfg.compatibility["enable_bypass_window_manager"]:
112         self.setWindowFlag(Qt.X11BypassWindowManagerHint, True)
...
136 def __toggle_lock(self, locked: bool):
138     self.setWindowFlag(Qt.WindowTransparentForInput, locked)   # click-through when locked
```
- `template/setting_global.py:55-56`: `enable_bypass_window_manager = (not WINDOWS)` and `enable_translucent_background = True`.
- `tinypedal/widget/` contains 81 files, and 77 of them define `timerEvent`. Each enabled widget is a separate top-level translucent window.

### 1.3 Present path: CPU raster + `UpdateLayeredWindowIndirect` with a dirty rect [CODE, Qt source]
TinyPedal draws with plain `QPainter` in `paintEvent`. There is no `QOpenGLWidget` or `QQuickWindow` anywhere in `tinypedal/widget`. Example: `widget/_painter.py:371-431` `RawText.paintEvent` → `painter.fillRect(...)` + `painter.drawText(...)`.

On Windows, Qt 5.15's raster backing store flushes frameless translucent windows like this (`qtbase/5.15/src/plugins/platforms/windows/qwindowsbackingstore.cpp:89-110`):
```cpp
const bool hasAlpha = rw->format().hasAlpha();
if ((flags & Qt::FramelessWindowHint) && QWindowsWindow::setWindowLayered(rw->handle(), flags, hasAlpha, rw->opacity()) && hasAlpha) {
    // Windows with alpha: Use blend function to update.
    ...
    RECT dirty = {dirtyRect.x(), ...};
    UPDATELAYEREDWINDOWINFO info = {sizeof(info), nullptr, &ptDst, &size,
                                    m_image->hdc(), &ptSrc, 0, &blend, ULW_ALPHA, &dirty};
    const BOOL result = UpdateLayeredWindowIndirect(rw->handle(), &info);
```
So every TinyPedal widget is a `WS_EX_LAYERED` window whose pixels come from a CPU DIB (`m_image->hdc()`). Only the **dirty rectangle** is pushed (`prcDirty`). Qt `dev` has the same logic at `qwindowsbackingstore.cpp:54-71`. There is no GPU device and no readback.

[DOC] The TinyPedal wiki FAQ (https://github.com/s-victor/TinyPedal/wiki/Frequently-Asked-Questions) says: *"Since currently TinyPedal only utilizes CPU, it may not be compatible with G-Sync, especially when game is running in borderless or windowed mode … it is not possible for TinyPedal to support G-Sync."*

### 1.4 Update loop, data rate vs repaint rate [CODE]
- Per-widget timer: `widget/_base.py:62-66`
  ```python
  self._update_timer = QBasicTimer()
  self._update_interval = max(self.wcfg["update_interval"], self.cfg.application["minimum_update_interval"])
  ```
  `_base.py:150-156` starts/stops it on `overlay_signal.paused`. The timers stop when the game is inactive (`__toggle_timer(not realtime_state.active)`, line 73).
- Defaults in `template/setting_widget.py`: 60 widgets at `update_interval: 20`, 10 at `100`, 3 at `200`, 2 at `50`, 1 at `500`, and 1 at `10`. The global floor is `minimum_update_interval: 10` (`setting_global.py:45`).
- The data reader is separate. `adapter/rf2_sharedmemory.py:279-349` runs a daemon thread that copies the mmap every **10 ms while live** (`update_delay = 0.01`) and every 0.5 s while frozen or inactive. Widgets read the copied snapshot through `api.read.*` from their GUI-thread timers.
- **Repaint only on change.** Example `widget/speedometer.py:120-167`:
  ```python
  def timerEvent(self, event):
      speed = api.read.vehicle.speed()
      ...
      self.update_speed(self.bar_speed_curr, speed)
  def update_speed(self, target, data):
      if target.last != data:
          target.last = data
          target.text = f"..."
          target.update()          # schedules paint of THIS child only
  ```
  `RawText`/`RawImage`/bar painters in `_painter.py` hold a `last` field for exactly this purpose. Because each element is a child QWidget, `update()` dirties only its rectangle, so the backing-store flush pushes a small dirty rect.
- [DOC] `docs/customization.md:579-580`: *"A value of 20 means refreshing every 20ms, which equals 50fps. Since most data from sharedmemory plugin is capped at 50fps … setting value less than 10 has no benefit."*
- [DOC] Changelog 1.10.3: *"Implemented lazy GUI update method that reduces Relative Widget CPU usage by 80%"*. Changelog 2.6.0: *"50-100% less overall CPU usage"*.

### 1.5 Priority / GPU handling [CODE]
There is no `psutil.Process().nice()`, no thread priority, and no GPU switch. A grep for `nice(`/`priority` finds only an unrelated module `state_timer`.

### 1.6 Known stutter issues [DOC]
- #211 "Performance Stuttering When FPS Limit Set to 120 (vs. 60)" (LMU, RX 9070). Stutter disappears when TinyPedal is closed, while TinyPedal's CPU stays under 1%. Closed. The user cites the FAQ's G-Sync note.
- #233 "Stuttering while using tinypedal" (open). Correlated with "REST API RESET" log lines. A pinned comment attributes it to an AMD driver update and says rolling the driver back fixed it.
- #288 "AMD FRTC (fps capping) ignored with Tiny Pedal". Launching TinyPedal while LMU runs makes AMD FRTC stop capping. The user's workaround is in-game 120 Hz + vsync + Game Mode + HAGS.
- FAQ remedies: set a max frame rate in the GPU driver panel, disable Fullscreen Optimizations, and lower post-processing.
- [INFER] These reports are about the *game's* frame pacing (layered topmost windows changing the game's present mode, e.g. losing independent flip / VRR), not the overlay's own refresh rate. TinyPedal does not report "HUD updates only every 0.2–0.5 s".

---

## 2. irdashies (Electron, iRacing)

### 2.1 Window creation [CODE] `src/app/overlayManager.ts`
- **One transparent window per display**, sized to the display. `createWindowForDisplay` is called per display at `overlayManager.ts:215-224`. Displays with no widgets are skipped, and the primary display always gets a window.
```ts
261 const browserWindow = new BrowserWindow({
267   transparent: true,
268   frame: false,
269   skipTaskbar: this.skipTaskbar,
270   focusable: true, // for OpenKneeboard/VR
271   resizable: false, movable: false, roundedCorners: false,
274   hasShadow: false, show: false,
276   alwaysOnTop: this.overlayAlwaysOnTop,
277   backgroundColor: '#00000000',
279   webPreferences: { preload, backgroundThrottling: false, ... autoplayPolicy: 'no-user-gesture-required', webviewTag: true },
...
338 browserWindow.setIgnoreMouseEvents(this.isLocked);
339 browserWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
352 if (this.overlayAlwaysOnTop) {
353   browserWindow.setAlwaysOnTop(true, 'screen-saver', 1);
```
- It does not use offscreen rendering (`webPreferences.offscreen` is absent).
- **Shrink-wrap** (`overlayManager.ts:560-616`): when locked, each display window is resized to the bounding box of its widgets plus `SHRINK_WRAP_PADDING = 20` (line 148). It goes full-display only in edit mode.
- [DOC] README line 583: *"Reduced GPU usage: Overlay windows are sized to fit only the widgets on each display, rather than covering the full screen."* PR #447 (merged 2026-03-28, commit `fc9239d`): *"This avoids excessive GPU usage when only using part of the screen."*

### 2.2 History: per-widget windows → single full-screen container → shrink-wrap [CODE: git history]
Blobless history clone (`irdashies-hist.git`):
- `6dd9539` 2026-02-07 "feat: migrate overlays to single container window (#303)". Before this, every widget had its own BrowserWindow (`-const { x, y, width, height } = layout;`). After it, there is one full-screen transparent window. First tag: **v0.0.40**.
- [DOC] Issue #357 "30fps+ drop with v0.0.40 vs v0.0.37" (closed 2026-03-22). The reporter capped at 116 fps and saw about 85 fps when enabling input trace/track map/radar on a 9800X3D + RTX 5090. Reverting to 0.0.37 fixed it.
- `fc9239d` 2026-03-28 "perf: shrink-wrap overlay container (#447)".
- [INFER] The timing strongly suggests the full-screen transparent surface (large DComp premultiplied surface → more DWM composition work) caused #357, and shrink-wrapping was the fix. The PR text supports the GPU-usage motive but does not quote numbers.

### 2.3 GPU vs CPU, Chromium flags [CODE]
- Hardware acceleration is on by default. `overlayManager.ts:938-943`:
  ```ts
  if (dashboard?.generalSettings?.disableHardwareAcceleration) { app.disableHardwareAcceleration(); }
  ```
  The default is `disableHardwareAcceleration: false` (`src/types/defaultDashboard.ts:1597`). The UI text (`GeneralSettings.tsx` ~700) reads: *"Uses GPU hardware acceleration for rendering. Only disable this if you are experiencing compatibility issues, as it will significantly impact performance."*
- User-configurable Chromium flags (`overlayManager.ts:950-992`, `src/types/chromiumFlags.ts:7-43`, all off by default):
  - `disableNativeWinOcclusion` → `--disable-features=CalculateNativeWinOcclusion`. Comment: *"Common fix for black flickering on transparent always-on-top overlays — Chromium will sometimes wrongly detect the overlay as occluded and stop painting it."*
  - `angleBackend` → `--use-angle=gl|d3d11|d3d9|vulkan`. Comment: *"Switching to 'gl' often resolves NVIDIA RTX-series transparency bugs."*
  - `disableDirectComposition` → `--disable-direct-composition`.
  - Allowlisted custom switches (`src/app/storage/chromiumFlags.ts:26-60`): `disable-gpu`, `disable-gpu-compositing`, `disable-gpu-vsync`, `disable-frame-rate-limit`, `disable-backgrounding-occluded-windows`, `force-high-performance-gpu`, and others.
  - [DOC] PR #534 (2026-05-03) motivation: black flickering on RTX 50-series with transparent topmost overlays. The author notes the fix was *not yet validated* on the target hardware.
- **Chromium software path = `UpdateLayeredWindow` [CODE, Chromium]:**
  - `components/viz/service/display_embedder/software_output_device_win.cc:197-207`: if `NeedsToUseLayerWindow(hwnd)`, it uses `SoftwareOutputDeviceWinProxy` + `LayeredWindowUpdater`.
  - `components/viz/common/display/use_layered_window.cc`: `return GetProp(hwnd, ui::kWindowTranslucent);`.
  - `components/viz/host/layered_window_updater_impl.cc:72`: `UpdateLayeredWindow(hwnd_, nullptr, &position, &size, dib_dc, &zero, RGB(0xFF,0xFF,0xFF), &blend, ULW_ALPHA);`
  - So a transparent Electron window with hardware acceleration disabled behaves like TinyPedal and WPF SoftwareOnly: CPU raster to a DIB, then ULW.

### 2.4 Update/IPC pipeline [CODE]
- `src/app/bridge/iracingSdk/iracingSdkBridge.ts:127-138`: `WAIT_TIMEOUT = 16`, session YAML poll every 500 ms.
- Lines 239-338: loop `sdk.waitForData(16)` → `getTelemetry()` → processors. Then:
  ```ts
  // Demand-driven poll rate: 25 Hz to save system resources, raised to the sim's native 60 Hz only while a visible window is subscribed to a 60 Hz channel
  const targetHz = Math.min(60, Math.max(25, channelBus?.maxActiveRateHz() ?? 25));
  ```
- Change detection: `ProcessorHost.ts:390-405` `publishIfChanged`. If the snapshot `version` token is unchanged, nothing is published. Processors bump `version` only when a field changed (`DriverControlsProcessor.ts:37-65`).
- Rate limiting per renderer subscription: `channelBus.ts:338-379` keeps only the latest payload (`pending`) and delivers at most `rateHz`. It skips windows that are not visible (`isVisible()`). Channel defaults are in `src/types/channels/channel.ts:451+`: e.g. `driver-controls` 25 Hz (max 60), `car-speeds` 10 Hz, `fuel.projection` 5 Hz, plus 2 Hz channels.
- Renderer: React. Canvas 2D is used for the track map (`TrackCanvas.tsx`) and lap graph.
- [DOC] `docs/PERFORMANCE_BENCHMARKS.md` includes a decision table: *"Empty regresses vs observer → renderer/provider bootstrap, transparent composition, window bounds"* and *"Empty-window GPU regression: reduce transparent surface area/window count or change the Chromium composition path."* The harness records iRacing FPS/GPU and per-renderer frames >25/50 ms. It recommends PresentMon/ETW for *"FPS-neutral hitch"* cases.
- [DOC] `docs/PERFORMANCE_TEST_SUMMARY.md`: the dominant "stuttery after a few minutes" cause in irdashies was V8 heap growth/GC from 25 Hz full-telemetry fan-out. The fix was rate-limited typed channels: *"App-wide renderer wake-ups fell 42.4%"*.

### 2.5 Priority [CODE]
There is no `os.setPriority` or process priority change. `priority` only appears in sim auto-detect ordering.

### 2.6 Issues [DOC]
- #612 "…program kills ALL my frames" (2026-07). The user reports >100 fps loss on a 7800X3D + 4090, with or without hardware acceleration. Closed with no visible maintainer analysis.
- #357 is covered above.
- Commit `8c63939` "revert: trackmap contrast text (causes stutter) (#323)". Commit `83ee4e6` "possible stuttering fix around pointer events for overlaycontainer (#343)".

---

## 3. ams2hud (Electron, **AMS2**) [CODE]
`src/main/Windows/HudWindow.js:178-207`:
```js
this.window = new BrowserWindow({ ..., simpleFullscreen: true, frame: false, transparent: true, alwaysOnTop: true, hasShadow: false });
this.window.setAlwaysOnTop(true, 'pop-up-menu', 1);
this.window.setIgnoreMouseEvents(true);
this.window.webContents.setBackgroundThrottling(false);
this.window.webContents.setFrameRate(60);   // max fps, 60 for hud
```
- Hardware acceleration stays on by default (`disableHardwareAcceleration` and `appendSwitch` are absent).
- Data comes from an HTTP poll of CREST2 (`DataController.js:175-179, 243`) at the `TickRate` default of 24 (`SettingsController.js:131`).
- [DOC/INFER] Electron documents `webContents.setFrameRate` as applying to offscreen rendering, and electron#28439's reporter says the same ("only works in offline paint mode"). So the `setFrameRate(60)` line is likely a no-op for this on-screen window.

---

## 4. CrewChief overlay (vendored GameOverlay.Net: WinForms + SharpDX Direct2D)

### 4.1 Window [CODE] `GameOverlay.Net/source/Windows/OverlayWindow.cs:615-638`
```csharp
var extendedWindowStyle = ExtendedWindowStyle.Transparent | ExtendedWindowStyle.Layered | ExtendedWindowStyle.NoActivate;
if (_isTopmost) extendedWindowStyle |= ExtendedWindowStyle.Topmost;
...
var windowStyle = WindowStyle.Popup;
_handle = User32.CreateWindowEx(extendedWindowStyle, _className, _title, windowStyle, ...);
User32.SetLayeredWindowAttributes(_handle, 0, 255, LayeredWindowAttributes.Alpha);
User32.UpdateWindow(_handle);
if (_isVisible) WindowHelper.ExtendFrameIntoClientArea(_handle);
```
`WindowHelper.cs:128-139`: `DwmExtendFrameIntoClientArea(hwnd, margins = -1)`. This is the "sheet of glass" trick. Per-pixel alpha comes from DWM composing the D3D surface's alpha. **There is no `UpdateLayeredWindow` and no readback.**

### 4.2 Render [CODE] `GameOverlay.Net/source/Drawing/Graphics.cs:150-186`
```csharp
_deviceProperties = new HwndRenderTargetProperties() { Hwnd = WindowHandle, PixelSize = ..., PresentOptions = VSync ? PresentOptions.None : PresentOptions.Immediately };
var renderProperties = new RenderTargetProperties(RenderTargetType.Default,
    new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96, RenderTargetUsage.None, FeatureLevel.Level_DEFAULT);
_device = new WindowRenderTarget(_factory, renderProperties, _deviceProperties);
```
`RenderTargetType.Default` means D2D uses the GPU when available and falls back to software otherwise.

### 4.3 Loop [CODE] `GameOverlay.Net/source/Windows/GraphicsWindow.cs:96-108, 170-218`
It runs on a dedicated background thread (`IsBackground = true`). The loop is `OnDrawGraphics` → `Thread.Sleep(1000/FPS - elapsed)`, i.e. **continuous redraw**.

`CrewChiefV4/Overlay/OverlaySettings.cs:36-37`: `windowFPS = 30; vSync = false;`.

`CrewChiefOverlay.cs:111-137` creates `new Graphics{ PerPrimitiveAntiAliasing = true, VSync = settings.vSync }` and `new GraphicsWindow(graphics){ IsTopmost = true, FPS = settings.windowFPS }`. `overlayWindow_DrawGraphics` (`:293-334`) calls `gfx.ClearScene(Color.Transparent)` and redraws every element every frame.

Charts are rendered on the CPU by WinForms `DataVisualization` and serialized to BMP (`Charts.cs:690-697`, `chart.SaveImage(stream, ChartImageFormat.Bmp)`), then drawn as a D2D image.

### 4.4 Windows/count/priority [CODE]
There are 2 overlay windows (`CrewChiefOverlay.cs`, `SubtitleOverlay.cs:125-138`) and no priority handling. The GitHub mirror `mrbelowski/CrewChiefV4` is stale (2019). The live source is on GitLab.

[INFER] This is the same class of path as our WPF "DWM glass" measurement (p95 ~55 ms): a GPU swap chain presented into a GPU-saturated system. Its pacing depends on the GPU scheduler.

---

## 5. Race Element (.NET, **supports AMS2**) — closest analogue to our app

### 5.1 Window [CODE] `Race_Element.HUD/Overlay/Internal/FloatingWindow.cs`
```csharp
14  public class FloatingWindow : NativeWindow, IDisposable
...
274 public int GetExStyle() {
276     int exStyle = User32.WS_EX_LAYERED | User32.WS_EX_TRANSPARENT;
278     if (!WindowMode) { exStyle |= User32.WS_EX_TOOLWINDOW; exStyle |= User32.WS_EX_NOACTIVATE; }
284     if (AlwaysOnTop) exStyle |= User32.WS_EX_TOPMOST;
```
- `CreateWindowOnly` (`:246-272`) uses `Style = WS_POPUP` and calls `UpdateLayeredWindow()` immediately.
- **Each HUD is its own window.** `CommonAbstractOverlay : FloatingWindow` (`CommonAbstractOverlay.cs:17`).

### 5.2 Present: GDI+ CPU raster → `UpdateLayeredWindow` [CODE] `FloatingWindow.cs:65-114`
```csharp
using var bitmap = new Bitmap(Size.Width, Size.Height, PixelFormat.Format32bppPArgb);
using var graphics = Graphics.FromImage(bitmap);
PerformPaint(new PaintEventArgs(graphics, _drawingRect));
IntPtr screenDc = User32.GetDC(IntPtr.Zero);
IntPtr memDc = Gdi32.CreateCompatibleDC(screenDc);
IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
...
User32.UpdateLayeredWindow(base.Handle, screenDc, ref dstPoint, ref size, memDc, ref srcPoint, 0, ref blend, 2); // ULW_ALPHA
```
It allocates a new bitmap and HBITMAP every frame and does a full-window update (no dirty rect).

There is also `OverlayUtil/CachedBitmap.cs:9-77`. It pre-renders static layers into a `Format32bppPArgb` bitmap wrapped in a GDI+ `CachedBitmap` and re-renders them only when needed. Commits `cd6cfdfc` "Create and use cached bitmaps for performance improvement" and `d3ec1c8c` "improve performance of CachedBitmap".

### 5.3 Loop: one background thread per HUD, off the UI thread [CODE] `CommonAbstractOverlay.cs:56-58, 160-242`
```csharp
56  public double RefreshRateHz = 30;
58  public bool RequestsDrawItself = false;
...
184 if (RequestsDrawItself) this.RefreshRateHz = 5;
187 Thread renderThread = new(() => {
189     double tickRefreshRate = Math.Ceiling(1000 / this.RefreshRateHz);
195     while (Draw) {
205         if ((ShouldRender() || IsRepositioning)) {
207             if (RequestsDrawItself) { ... } else this.UpdateLayeredWindow();
...
230         waitTime = interval - time.GetElapsedTime(lastStart);
231         Timers.TimeBeginPeriod(1);
232         if (waitTime.Ticks > 0) Thread.Sleep(waitTime);
234         Timers.TimeEndPeriod(1);
237 renderThread.IsBackground = true;
238 renderThread.SetApartmentState(ApartmentState.MTA);
```
- `ShouldRender()` (`:73-103`) hides the HUD when the engine is off or the game is paused. AMS2 is in the `pauseConditionable` set (line 91).
- Per-HUD `RefreshRateHz` assignments, counted by grep: `1` ×14, `2` ×8, `5` ×7, `10` ×8, `20` ×4, `30` ×4, `3` ×4, `6` ×3, plus user-configurable values.
- `RequestRedraw()` (`:244-253`) does an on-demand ULW for "draw itself" HUDs.
- Data: `Race Element.Data/Games/GameManager.cs:60-65` runs `SimDataProvider.Update()` at `1000 / PollingRate()`. AMS2 is `PollingRate() => 300` Hz (`Games/Automobilista2/DataProvider.cs:24`).

### 5.4 Priority [CODE] `Race_Element/App.xaml.cs:196-197`
```csharp
using Process current = Process.GetCurrentProcess();
current.PriorityClass = ProcessPriorityClass.BelowNormal;
```
Repo-wide there is no `RenderMode` or `ProcessRenderMode` usage. The WPF app shell renders normally. The HUDs are not WPF.

### 5.5 Issues [DOC]
GitHub issue search for fps/stutter/performance/lag/gpu returns only #55 (a VR-related wontfix). Performance work shows up only as CPU-reduction commits, for example `59b7a5d8 refactor(GForceTrace): lower cpu usage`, `0329a804 refactor(InputTrace): lower cpu usage`, and `3927daeb adjust timeperiod`.

---

## 6. SecondMonitor (WPF, has a PCars2/AMS2 connector) [CODE]
- `Applications/Timing/Presentation/View/TimingGUI.xaml.cs:23-32`:
  ```csharp
  var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
  if (hwndSource != null && !_useAcceleration)
      hwndSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
  ```
  It is always constructed as `new TimingGui(false)` (`TimingApplicationController.cs:131`). **The production GUI is forced to WPF software rendering.** The same pattern appears in `Tests/ControlTestingApp/MainWindow.xaml.cs:23`. In `Telemetry/TelemetryPresentation/MainWindow.xaml.cs:34` it is commented out.
- Connector polling: `AbstractGameConnector.cs:44` `TickTime = 10`. `PCars2Connector.cs:75` `await Task.Delay(TickTime)`.
- It is not a transparent overlay. It is relevant only as a WPF sim-racing app that chose `SoftwareOnly` by default. The history clone timed out on `-S` searches, so the original rationale was not found.

---

## 7. Electron/Chromium transparent-window performance (upstream) [DOC]
- **electron#28439** "Full sized transparent 'overlay' window – lagging screen despite high fps in game" (Electron 9, Win10). Quote: *"Transparent window renders seemingly with low fps."* With `app.disableHardwareAcceleration()` and a full-size transparent window, the screen feels like ~20 fps while the game runs at 120. Enabling hardware acceleration removes the lag but raises CPU usage. `backgroundThrottling:false` and `setFrameRate` did not help, since `setFrameRate` only works offscreen. Closed with no fix.
  - [INFER] This is the Chromium software path (full-screen DIB + `UpdateLayeredWindow` every frame). Its cost scales with **window area**, which is why irdashies' shrink-wrap and TinyPedal's dirty-rect ULW matter.
- **electron PR #39895** (merged 2023-09-27, backported to 27-x-y): *"set window contents as opaque to decrease DWM GPU usage."* Transparent content made DirectComposition use `DXGI_ALPHA_MODE_PREMULTIPLIED`, which *"will trigger the DC redraw for every video frame"*. With opaque content, DWM GPU usage fell from 16–18% to <1% at 2160p, and from 6–8% to <1% at 1080p.
  - [INFER] A transparent overlay cannot use the opaque fast path, so the DWM cost of a transparent DComp surface is inherent. Area reduction is the lever.
- **electron#50469** (Electron 41, Win11): `--disable-gpu`, `--disable-gpu-compositing`, `--use-gl=swiftshader`, and `UseSoftwareCompositing` did not give a clean CPU fallback. GPU went to 80–100%. Also, NVIDIA "Background Application Max Frame Rate" throttled the app even in the foreground. Closed as need-repro/not planned.
- irdashies' Chromium-flags feature (§2.3) is the practical workaround set: disable native occlusion, change the ANGLE backend, disable DirectComposition.

---

## 8. Implications for our WPF AMS2 overlay

These are inferences, but each one is grounded in the CODE facts above.

1. **Architecture parity.** Our new default, WPF `RenderMode.SoftwareOnly` + `AllowsTransparency`, gives CPU raster → `UpdateLayeredWindow`. That is the same present model as TinyPedal (Qt raster → `UpdateLayeredWindowIndirect`), Race Element (GDI+ → `UpdateLayeredWindow`), and Chromium with hardware acceleration off. None of the surveyed projects uses a hardware render target **plus** a layered window, which is the WPF HW path with `GetRenderTargetData` readback. That combination is the outlier and explains why we stalled behind a 100%-busy GPU.
2. **What the CPU-path projects do beyond that:**
   - **Repaint only on change**, at element granularity (TinyPedal `last != data` → child `update()` → dirty-rect ULW). irdashies does this at the data layer (`publishIfChanged` + per-channel Hz caps). For WPF, the equivalent is avoiding property churn when values are equal, so WPF does not re-render and re-ULW the window.
   - **Low and differentiated rates.** TinyPedal uses 50 Hz for the fast widgets and 10/5/2 Hz for the others. Race Element mostly uses 1–10 Hz, with 20–30 Hz only for traces/inputs. irdashies uses 25 Hz by default and 60 Hz only for input/trace channels.
   - **Small windows.** irdashies learned (v0.0.40 → #357 → #447) that one big transparent surface hurts game FPS. Per-widget windows sized tightly, as in TinyPedal and Race Element, keep each ULW and DWM blend small.
   - **Render threads off the UI thread.** Race Element runs one background thread per HUD calling ULW directly. [INFER] In WPF, all windows on one Dispatcher share one UI thread and one render thread. One slow present delays all 10–14 HUDs, which matches our "all HUDs update together at 0.2–0.5 s" symptom under the HW path. With `SoftwareOnly`, the ULW happens on WPF's render thread. If per-window stalls remain, splitting HUDs across multiple Dispatcher threads is an option, though not something any surveyed project does in WPF.
   - **Process priority.** Only Race Element changes it, and it sets `BelowNormal`, which deprioritises the overlay *relative to the game*. No project raises overlay priority. Nobody touches GPU scheduling priority (`D3DKMTSetProcessSchedulingPriorityClass`) or MMCSS.
3. **Residual risk on the CPU path [INFER + DOC]:** DWM still composes the layered bitmap on the GPU, so a fully saturated GPU can still delay DWM's own frame. TinyPedal's FAQ and issues (#211, #288) show that layered topmost windows can disturb the *game's* frame pacing (G-Sync/VRR, driver FPS caps) even at <1% CPU. These are game-side pacing effects, not slow overlay updates. They are worth measuring separately with PresentMon on the game process, as irdashies' benchmark doc recommends.

---

## 9. Not verified / gaps
- I could not read GitHub issue comment threads through the API (session not authorized for those repos). Issue content came from WebFetch page summaries, which sometimes omitted comments. Maintainer replies on irdashies #357/#612 and TinyPedal #211 were not visible.
- irdashies PR #447 "before/after" GPU numbers were in screenshots and not extractable.
- The commit dates for Race Element's adoption of `BelowNormal` priority and `UpdateLayeredWindow` were not obtained (the history `-S` search timed out).
- I did not find another open-source **WPF transparent overlay** for sims. SecondMonitor is WPF but not an overlay. CrewChief uses WinForms + D2D. Race Element uses WPF only for its shell.
