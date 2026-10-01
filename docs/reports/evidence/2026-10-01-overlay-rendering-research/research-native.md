# Native (C++) transparent-overlay research: how they avoid stutter under a GPU-bound game

Date: 2026-10-01. All repos cloned with `git clone --depth 1` into `/tmp/claude-0/research/`.
Legend: **[V]** = verified by reading the code/doc at the commit given; **[I]** = my inference, not verified by running anything (no Windows box in this environment; nothing here was measured).

| Repo | Commit read | Local path |
|---|---|---|
| lespalt/iRon | `03f3270f` (2022-03-12, last commit) | `/tmp/claude-0/research/iRon` |
| OpenKneeboard/OpenKneeboard v1.12.10 (last tag with non-VR) | `bef8f702` | `/tmp/claude-0/research/OKB-1.12.10` |
| OpenKneeboard/OpenKneeboard master | `f2d9c9dd` (2026-09-28) | `/tmp/claude-0/research/OpenKneeboard` |
| SemSodermans31/iFL03 (modern iRon derivative) | `b27983f2` (2026-02-06) | `/tmp/claude-0/research/iFL03` |
| andrei-cb/fps_monitor (ImGui + DComp FPS/perf HUD) | `4b9ae7b2` (2026-09-29) | `/tmp/claude-0/research/fps_monitor` |
| mausimus/ShaderBeam (GPU overlay that must hit every vsync next to a game) | `5f9eef6a` (2026-01-31) | `/tmp/claude-0/research/ShaderBeam` |
| obsproject/obs-studio `libobs-d3d11/d3d11-subsystem.cpp` (reference for GPU priority) | master on 2026-10-01 (raw download) | `/tmp/claude-0/research/obs-d3d11-subsystem.cpp` |

Also looked at but not usable: tbattz/iracingOverlay (OpenGL/GLFW), Rexagon/iracing-overlay (Qt Quick), vevikils/iRacing-Overlay (Python), mausimus/ShaderGlass (opaque window, CreateSwapChainForHwnd). GitHub code search for `CreateSwapChainForComposition WS_EX_TRANSPARENT WS_EX_NOREDIRECTIONBITMAP` mostly returns game-cheat ESP overlays, which I left out. I found no open-source native AMS2/PC2/rF2/LMU overlay. (TinyPedal and irdashies were already in the research dir from other work and are not covered here.)

---

## TL;DR for our WPF app

1. **None of the native overlays use `WS_EX_LAYERED` + `UpdateLayeredWindow` or GDI for the HUD content.** iRon, iFL03 and fps_monitor all use the same setup: `WS_EX_NOREDIRECTIONBITMAP`, then D3D11, then `CreateSwapChainForComposition(FLIP_SEQUENTIAL, BufferCount=2, ALPHA_MODE_PREMULTIPLIED)`, then a DirectComposition target/visual, with D2D or ImGui drawing on the GPU. This setup has no redirection surface, no PresentWithGDI and no GetRenderTargetData readback. Those three are the steps that cost us p95 ~97 ms in the WPF hardware layered path. [V]
2. **Click-through on the DComp path:** fps_monitor uses `WS_EX_LAYERED | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` plus `SetLayeredWindowAttributes(hwnd,0,255,LWA_ALPHA)`. The layered bit is there only so that `WS_EX_TRANSPARENT` hit-testing works. All content still goes through DComp (`fps_monitor/src/overlay_window.cpp:21-22,78-81`). iRon and iFL03 do **not** set `WS_EX_TRANSPARENT` at all. [V]
3. **Present mode:**
   - iRon and iFL03 call `Present(1,0)` on each of 5–14 windows, one after another, on one thread. Under GPU load each of those calls can block, so the blocking adds up [I]. iRon issue #11, "Laggy performance", is still open with no answer.
   - OpenKneeboard explicitly uses `Present(0,0)` with `BufferCount=3` and `FLIP_DISCARD`. Its code comment says this is "to avoid stalls" and to "decouple the frame rates" (`OKB-1.12.10/src/app/app-winui3/TabPage.xaml.cpp:502-524,574`). [V]
4. **Frame pacing:**
   - iRon: one main loop, paced by `irsdk.waitForData(16)`. It redraws **every** overlay every tick (~60 Hz) whether or not the data changed.
   - iFL03 adds a per-overlay `target_fps` limiter (default 10–30 fps) and a "static mode".
   - fps_monitor and OpenKneeboard render **only when something changed** (dirty flag / `IsRepaintNeeded`). [V]
5. **GPU priority:** none of the HUD-style overlays (iRon, iFL03, fps_monitor) touch GPU scheduling priority. The two programs that need guaranteed GPU time do:
   - **ShaderBeam:** `IDXGIDevice::SetGPUThreadPriority(29 | 0x40000000)`, `SetMaximumFrameLatency`, a `THREAD_PRIORITY_TIME_CRITICAL` render thread and `timeBeginPeriod(1)`. Its README still says it gets starved by heavy games and recommends a **second GPU/iGPU**.
   - **OBS:** `D3DKMTSetProcessSchedulingPriorityClass(REALTIME, or HIGH when HAGS is on)` + `SetGPUThreadPriority(GPU_PRIORITY_VAL)`. This only applies in builds compiled with `GPU_PRIORITY_VAL`, needs admin, and is skipped on Intel. Separately, OBS calls `SetMaximumFrameLatency(16)` "to prevent stalls sometimes seen in Present calls". [V]
   - iRon only does `SetPriorityClass(HIGH_PRIORITY_CLASS)`, which is CPU priority only. [V]
6. **OpenKneeboard's answer to non-VR overlay stutter was to avoid a separate window entirely.** v1.x injected a DLL into the game and drew its kneeboard into the game's own back buffer inside a `IDXGISwapChain::Present` hook. Cross-process synchronisation was done GPU-side with shared `ID3D11Fence` (`context->Wait(fence)`, no CPU block). That code was **deleted on master** (commit 939e765, 2025-11-17). The maintainer's plan for v2 (#677) is transparent topmost windows that:
   - exactly fill each monitor (one window per monitor),
   - use `WS_EX_NOREDIRECTIONBITMAP`,
   - use a FLIP present model via DirectComposition,
   - can use MPO (multi-plane overlays),

   so that the overlay does not break VRR/G-Sync/FreeSync. [V, from issue text]
7. **Implication for us [I]:**
   - Our current `SoftwareOnly` + `UpdateLayeredWindow` route takes our own GPU work out of the picture. Only DWM's composition of a system-memory bitmap is left, and DWM runs at elevated GPU priority. So it is a reasonable robust default.
   - The "native-grade" alternative is the DComp + flip-composition setup above, used with:
     - (a) one D3D device shared by all HUDs, or fewer windows,
     - (b) `Present(0,0)` or `DXGI_PRESENT_DO_NOT_WAIT`, never `Present(1,0)` serially across many windows,
     - (c) render-on-change,
     - (d) optionally `SetGPUThreadPriority` / `D3DKMTSetProcessSchedulingPriorityClass`, with the admin and HAGS caveats.
   - No native overlay I read shows it **solves** GPU starvation by its own GPU rendering. The ones that need hard guarantees either inject into the game (OKB v1) or recommend a second GPU (ShaderBeam).

---

## 1. lespalt/iRon — https://github.com/lespalt/iRon

### Window creation [V]
`Overlay.cpp:135`
```cpp
m_hwnd = CreateWindowEx( WS_EX_TOPMOST|WS_EX_TOOLWINDOW|WS_EX_NOREDIRECTIONBITMAP, wndclassName, m_name.c_str(),
                         WS_POPUP|WS_VISIBLE, CW_USEDEFAULT, CW_USEDEFAULT, 500, 400, NULL, NULL, NULL, NULL );
```
- **One window per HUD.** `main.cpp:143-151` creates OverlayCover, Relative, Inputs, Standings and DDU (plus Debug in debug builds).
- No `WS_EX_LAYERED`, no `WS_EX_TRANSPARENT`, no `WS_EX_NOACTIVATE` (only `SWP_NOACTIVATE` in `SetWindowPos`, `Overlay.cpp:325`).
- `windowProc` (`Overlay.cpp:34-91`) returns `DefWindowProc` unless UI-edit mode is on. In edit mode it returns `HTCAPTION`/`HTBOTTOMRIGHT` so the window can be dragged.
- [I] Outside edit mode, hit-testing returns HTCLIENT, so the window is **not** click-through. iRon relies on the user not clicking there, and on `giveFocusToIracing()` (`main.cpp:103-108`).

### Transparency mechanism [V]
DirectComposition. `Overlay.cpp:143-201`, with the comment "Create the unsettling amount of stuff that's needed to get a window to properly alpha-blend our Direct2D rendering into the desktop", citing Kenny Kerr's MSDN article "High-Performance Window Layering Using the Windows Composition Engine".
```cpp
D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, D3D11_CREATE_DEVICE_SINGLETHREADED|D3D11_CREATE_DEVICE_BGRA_SUPPORT, ...);   // :154
swapChainDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;   // :172
swapChainDesc.BufferCount = 2;                                 // :173
swapChainDesc.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;       // :175
dxgiFactory->CreateSwapChainForComposition(dxgiDevice.Get(), &swapChainDesc, NULL, &m_swapChain);  // :176
m_d2dFactory->CreateDxgiSurfaceRenderTarget(dxgiSurface.Get(), &targetProperties, &m_renderTarget); // :192 (D2D on swapchain buffer 0)
DCompositionCreateDevice(dxgiDevice.Get(), IID_PPV_ARGS(&m_compositionDevice));                     // :196
m_compositionDevice->CreateTargetForHwnd(m_hwnd, true, &m_compositionTarget);                       // :197 (topmost=true)
m_compositionVisual->SetContent(m_swapChain.Get()); m_compositionTarget->SetRoot(...); Commit();    // :199-201
```
- **Each overlay creates its own D3D11 device, D2D factory, DComp device and DWrite factory.** These are per-window instances, not shared.
- No `SetMaximumFrameLatency`, no waitable object, swap-chain `Flags=0`.

### Rendering API [V]
D2D (`ID2D1RenderTarget` from a DXGI surface) and DirectWrite. GPU (hardware device).

### Present / pacing [V]
`Overlay.cpp:319`: `HRCHECK(m_swapChain->Present( 1, 0 ));`. This is vsync'd and blocking, once per overlay per loop.

`main.cpp:154-207`, main loop:
```cpp
status = ir_tick();                          // iracing.cpp:375-379 -> irsdk.waitForData(16)
...
if( !g_cfg.getBool("General", "performance_mode_30hz", false) )
    for( Overlay* o : overlays ) o->update();   // "Update everything every frame, roughly every 16ms (~60Hz)"
else  /* update half the overlays on even frames, half on odd */
...
while(PeekMessage(&msg, NULL, 0, 0, PM_REMOVE)) { ... }
```
- Single thread for data, render and message pump.
- Paced by the iRacing data-ready event (60 Hz) with a 16 ms timeout.
- **Always redraws, with no dirty check.** The whole background is cleared and refilled each update (`Overlay.cpp:285-296`).
- [I] With N windows each calling `Present(1,0)` in series on a GPU that is saturated, a full present queue makes each call block until DWM consumes a frame. That can stretch one loop iteration to many frames and would look exactly like "HUD updates every 0.2–0.5 s". The 30 Hz mode (`performance_mode_30hz`) halves the number of Presents per tick, which is the only mitigation in the code.

### Priority [V]
`main.cpp:112-113`: `SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS); // Bump priority up so we get time from the sim`. This is CPU priority only. There is no GPU priority call.

### Data vs render rate [V]
Shared memory is read through the irsdk `waitForData(16)` event, and rendering happens in the same iteration, so data rate equals render rate (~60 Hz, or 30 Hz per overlay in perf mode).

### Known issues [V]
- #11 "Laggy performance" (open, 2022-07-20): "the telemetry overlay keeps lagging. I tried setting cpu priority to realtime but that didnt help." There has been no response.
- #29 "BUG Full Screen" (closed).
- [I] The fact that "CPU realtime didn't help" fits a GPU-side or Present-blocking cause rather than CPU starvation.

---

## 2. OpenKneeboard — https://github.com/OpenKneeboard/OpenKneeboard

### 2a. Non-VR in-game overlay (v1.x): injection into the game's swap chain [V]
Files at tag v1.12.10: `src/injectables/NonVRD3D11Kneeboard.cpp`, `IDXGISwapChainHook.cpp`.

- It hooks `IDXGISwapChain::Present` and `ResizeBuffers` (`NonVRD3D11Kneeboard.cpp:24-31`). If the Steam overlay is present, it hooks Steam's hook instead (`docs/internals/injectables.md:106-118`).
- In `OnIDXGISwapChain_Present` (`:87-152`), working **on the game's own device and immediate context, on the game's render thread**:
  ```cpp
  swapchain->GetBuffer(0, IID_PPV_ARGS(destinationTexture.put()));              // :53 game back buffer
  const auto snapshot = mSHM.MaybeGet();                                          // :105 cached unless frame changed
  ...
  D3D11::ScopedDeviceContextStateChange savedState(ctx, &mRenderState);           // :144 save/restore game state
  mResources->mRenderer->RenderLayers(sr, 0, snapshot, {&layer,1}, RenderMode::Overlay); // :147 sprite-blit into back buffer
  return passthrough();                                                           // game's Present
  ```
- There is no separate window, no DWM composition and no second process on the GPU queue. The overlay costs one textured quad inside the game's frame.

**Cross-process GPU sync (shared texture + shared fence, GPU-side waits only):**
- Producer (OpenKneeboard app):
  - `InterprocessRenderer.cpp:91-108`: `CopySubresourceRegion(ipcTexture, canvas)` then `ctx->Signal(sharedFence, fenceOut)`.
  - The fence is created at `:243-246` with `CreateFence(0, D3D11_FENCE_FLAG_SHARED)` and `CreateSharedHandle`.
  - The IPC "swapchain" is 2 textures (`config.in.hpp:26 SHMSwapchainLength = 2`). `SHM.cpp:508-521` picks the index from the frame number and does `InterlockedIncrement64` on that slot's fence value.
- Consumer (inside the game):
  - `lib/SHM/D3D11.cpp:37-71`: `mContext->Wait(fenceIn, value)` (a **GPU** wait, the CPU does not block), then copy into a private cache texture, then `Signal(copyFence)`.
  - The cache texture is marked `SetEvictionPriority(DXGI_RESOURCE_PRIORITY_MAXIMUM)` with the comment "Will be needed within 3 frames, so never allow it to be booted from VRAM to RAM" (`:51-53`).
  - `OpenSharedFence` / `OpenSharedResource1` handles are cached (`:187-196`).
- `CachedReader::MaybeGet` (`SHM.cpp:770-830`) returns the cached snapshot when the render cache key has not changed. Most game frames therefore do no copy at all.

### 2b. Producer pacing [V]
`app-winui3/MainWindow.xaml.cpp:219-246`: `FrameLoop()` runs a fixed-interval coroutine at `FramesPerSecond = 90` (`lib/include/OpenKneeboard/config.in.hpp:29`).

`FrameTick` (`:335-372`) only renders `if (mKneeboard->IsRepaintNeeded())`. It is data-driven: it emits `SetRepaintNeeded()` on content change, and if it cannot get the lock it skips the tick (`try_to_lock`) instead of blocking.

### 2c. OKB's own windowed renderers (not the overlay)
- WinUI3 `TabPage` SwapChainPanel (`TabPage.xaml.cpp:502-524,574`):
  ```cpp
  // BufferCount = 3: triple-buffer to avoid stalls
  // If the previous frame is still being Present()ed and we only have two frames in the buffer,
  // Present()ing the new frame will stall until that has completed.
  // We could avoid this by using frame pacing, but we want to decouple the frame rates ...
  .BufferCount = 3, .SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD, .AlphaMode = DXGI_ALPHA_MODE_IGNORE
  CreateSwapChainForComposition(...);
  ...
  mSwapChain->Present(0, 0);
  ```
- Standalone viewer (master, `src/utilities/viewer.cpp:236-249, 545-558, 726`):
  - `CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP, ...)`
  - `SetTimer(..., 1000/60)`
  - DComp target, `BufferCount=3`, `FLIP_DISCARD`, `ALPHA_MODE_PREMULTIPLIED`, `CreateSwapChainForComposition`, `Present(0,0)`.
  - Comment: "We need DirectComposition in order to support DXGI_ALPHA_MODE_PREMULTIPLIED".
- **The pattern across OKB is FLIP + 3 buffers + `Present(0,0)`: never let Present block the producer thread.**

### 2d. GPU scheduling [V]
- `lib/DXResources.cpp:172-221` only **logs** the HAGS state (D3DKMTQueryAdapterInfo WDDM_2_9/2_7 caps) and MPO support (`IDXGIOutput2::SupportsOverlays`, `:140-168`).
- There is no `SetGPUThreadPriority` or `D3DKMTSetProcessSchedulingPriorityClass` anywhere (grep over v1.12.10 `src/`).

### 2e. Non-VR removed; v2 plan [V]
- Commit `939e765` (2025-11-17) "Remove non-VR support" deletes NonVRD3D11Kneeboard, the IDXGISwapChainHook, etc. Its message: "If this is added, it will be rewritten (#677), based on overlay windows with the DWM PASSIVE flag set, instead of swapchain modification refs #815".
- Issue #677 "master/v2.x: re-add non-VR overlay" (open). Injection "is not compatible with anti-cheat; requires specific code for each graphics API ...". v2 "will use transparent always-on-top windows in a way that does not interfere with variable refresh rate, g-sync, or freesync".
- The pinned maintainer comment, **as extracted by WebFetch from the issue page** (the GitHub API was blocked here, so this is a paraphrase-level extraction): the window must exactly fill a monitor; one window per monitor, not one combined window; `WS_EX_NOREDIRECTIONBITMAP` must be used; one of the FLIP present models must be used (DirectComposition/WinRT composition already are); MPO when hardware, driver and user config allow.
- [I] I could not identify the exact "DWM PASSIVE flag" API from the sources available to me. It is not in the code. Treat it as an unverified pointer.

### Known issues [V]
- #255 "Microstutter when using non-vr D3D11 kneeboard (e.g. via autodetect)" (closed, label "Performance improvements"). No root cause was visible in the fetched page.
- #775 "FPS drop with tinypedal" (closed, not planned / needs more info).
- #733 "Losing 15fps when I open OpenKneeBoard".

---

## 3. SemSodermans31/iFL03 — https://github.com/SemSodermans31/iFL03 (modern iRon derivative, 14 overlays)

The window, swap chain and DComp setup is **identical to iRon**:
- `Overlay.cpp:134` `WS_EX_TOPMOST|WS_EX_TOOLWINDOW|WS_EX_NOREDIRECTIONBITMAP`
- `:155` its own `D3D11CreateDevice(... SINGLETHREADED|BGRA_SUPPORT)` per overlay
- `:171-175` FLIP_SEQUENTIAL / 2 buffers / PREMULTIPLIED / CreateSwapChainForComposition
- `:195` DCompositionCreateDevice
- `:341` `Present(1,0)`

[V] What it adds on top of iRon:
- A **per-overlay frame limiter** (`Overlay.cpp:281-297`):
  ```cpp
  const int cfgFps = std::max( 10, g_cfg.getInt(m_name, "target_fps", m_targetFPS) );
  const DWORD minDelta = (DWORD)std::max(1, 1000 / std::max(10, m_targetFPS));
  if( !m_forceNextUpdate && (now - m_lastUpdateTick) < minDelta ) return;
  if( m_staticMode && !m_forceNextUpdate ) return;
  ```
  Defaults (`OverlayX.h`, `AppControl.cpp:362-589`): Standings, DDU, Relative, Fuel, Cover, Weather, Flags, Radar and Tire at **10 fps**; Delta, Track and Traffic at 15; Pit at 30; Inputs at 30–60.
- `requestRedraw()` / `m_forceNextUpdate` to redraw immediately on session or config change (`:275-279`).
- Gating: overlays do not update until the irsdk header has been stable for 15 frames (`main.cpp:540-555`).
- Still `SetPriorityClass(HIGH_PRIORITY_CLASS)` (`main.cpp:293`) and `irsdk.waitForData(16)` (`iracing.cpp:407`). No GPU priority.

[I] The author's answer to load is "render each HUD at 10–15 fps" and "don't Present unless due". That directly reduces how many blocking `Present(1,0)` calls happen per loop. No GitHub issues about performance exist (search returned 0).

---

## 4. andrei-cb/fps_monitor — https://github.com/andrei-cb/fps_monitor (ImGui DComp perf HUD for games)

[V] `src/overlay_window.cpp`:
```cpp
constexpr DWORD kBaseExStyle    = WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOREDIRECTIONBITMAP; // :21
constexpr DWORD kPassiveExStyle = WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;                                         // :22
hwnd_ = CreateWindowExW(kBaseExStyle | kPassiveExStyle, kWindowClass, L"FPS Monitor", WS_POPUP, 0,0,1,1, ...);  // :78
SetLayeredWindowAttributes(hwnd_, 0, 255, LWA_ALPHA);                                                           // :81
D3D11CreateDevice(HARDWARE ...) ; fallback D3D_DRIVER_TYPE_WARP                                                 // :116-120
desc.BufferCount = 2; desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL; desc.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED; // :136-139
factory->CreateSwapChainForComposition(...); DComp CreateTargetForHwnd(hwnd_, TRUE) ... Commit();               // :140-147
swapChain_->Present(vsync ? 1 : 0, 0);                                                                           // :266
```
- **This shows a click-through DComp window: `WS_EX_LAYERED` (alpha 255) + `WS_EX_TRANSPARENT` + `WS_EX_NOREDIRECTIONBITMAP`.** Click-through is toggled by flipping `WS_EX_TRANSPARENT` (`:190-197`).
- A single window, with ImGui (DX11 backend) rendering on the GPU.
- Pacing (`src/main.cpp:446-490`), in "Passive" mode:
  - "draw what changed, then sleep until new sensor data (or another message)".
  - Renders only when `app.dirty`.
  - Sleeps on `MsgWaitForMultipleObjects` against a high-resolution waitable timer that is only armed when a frame-graph is shown.
  - Sensor sampling runs on a separate thread (`sensor_hub.cpp`) and posts `WM_APP_SENSORS`.
  - Continuous vsync rendering happens only while the settings UI is open.
- No GPU priority calls.

---

## 5. mausimus/ShaderBeam — https://github.com/mausimus/ShaderBeam (a desktop overlay that must present every vsync while a game runs)

This is not a transparent HUD: it is an opaque fullscreen topmost window with WGC capture. It is included because it is the clearest open-source example of fighting **GPU-time starvation from a separate process**. [V]
- `Renderer.cpp:199-204`:
  ```cpp
  dxgiDevice->SetMaximumFrameLatency(m_options.maxQueuedFrames);
  if(m_options.gpuThreadPriority)
      dxgiDevice->SetGPUThreadPriority(m_options.gpuThreadPriority | 0x40000000);   // default 29 (Common.h:148)
  ```
  The same call is made on the capture device (`CaptureWGC.cpp:56`).
  [I] The documented range of `SetGPUThreadPriority` is −7..7. The `0x40000000` bit plus the value 29 is undocumented, and I cannot confirm what it does.
- Swap chain `FLIP_DISCARD`, `BufferCount = maxQueuedFrames+1` or 3 (`ShaderBeam.cpp:130`, `Renderer.cpp:221-229`). The comment there: "on Intel Arc setting DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT makes Present(1) not wait on vsync any more and BSODs Windows".
- Render thread `SetThreadPriority(THREAD_PRIORITY_TIME_CRITICAL)` (`RenderThread.cpp:35`); `timeBeginPeriod(1)` (`ShaderBeam.cpp:54`).
- Click-through by toggling `WS_EX_LAYERED|WS_EX_TRANSPARENT` (`Window.cpp:218-232`).
- README "Single GPU setups" / "I get irregular flashing" says: "caused by ShaderBeam not being given enough GPU time by the OS"; "some games (especially Unreal Engine ones) create unavoidable GPU stalls". It recommends:
  - "Use Process Lasso to max GPU priority";
  - try disabling HAGS;
  - disable MPO;
  - tune Queued Frames;
  - "The best way to avoid this issue is to use a second GPU for ShaderBeam".

  So even maximum GPU thread priority does not fully solve starvation on a single GPU.

---

## 6. Reference: OBS libobs-d3d11 GPU priority (not an overlay, but the canonical "my process must get GPU time while a game is GPU-bound" code)

`obs-d3d11-subsystem.cpp` (master on 2026-10-01). [V]
```cpp
/* prevent stalls sometimes seen in Present calls */
increase_maximum_frame_latency(device)  ->  dxgiDevice->SetMaximumFrameLatency(16);          // ~:420-433, :641-644
#ifdef USE_GPU_PRIORITY
static bool set_priority(ID3D11Device *device, bool hags_enabled) {                          // :436
    D3DKMTSetProcessSchedulingPriorityClass(GetCurrentProcess(),
        hags_enabled ? D3DKMT_SCHEDULINGPRIORITYCLASS_HIGH : D3DKMT_SCHEDULINGPRIORITYCLASS_REALTIME); // :444-446
    dxgiDevice->SetGPUThreadPriority(GPU_PRIORITY_VAL);                                      // :452
}
...
if (desc.VendorId != 0x8086 && !set_priority(device, hags_enabled))                          // :657 skip Intel
    blog(LOG_INFO, "D3D11 GPU priority setup failed (not admin?)");
```
`libobs-d3d11/CMakeLists.txt:31-32`: `USE_GPU_PRIORITY` is only defined when CMake `GPU_PRIORITY_VAL` is set, so this is opt-in at build time. The REALTIME class needs admin. With HAGS on, OBS uses HIGH instead of REALTIME.

---

## Comparison table

| | iRon | iFL03 | OKB v1 non-VR | OKB windows (TabPage/viewer) | fps_monitor | ShaderBeam |
|---|---|---|---|---|---|---|
| Windows | 1 per HUD (5) | 1 per HUD (14) | none (inject) | 1 | 1 | 1 fullscreen |
| Ex styles | TOPMOST, TOOLWINDOW, NOREDIRECTIONBITMAP | same | n/a | NOREDIRECTIONBITMAP | TOPMOST, TOOLWINDOW, LAYERED, NOREDIRECTIONBITMAP, TRANSPARENT, NOACTIVATE | LAYERED+TRANSPARENT toggled |
| Click-through | no | no | n/a | n/a | yes | yes |
| Transparency | DComp + premult flip SC | same | game back buffer | DComp (premult in viewer) | DComp + premult flip SC | opaque |
| Draw API | D2D/DWrite (GPU) | D2D/DWrite (GPU) | D3D11 sprite in game ctx | D2D/D3D11 | ImGui DX11 (WARP fallback) | D3D11 shaders |
| SwapEffect / buffers | FLIP_SEQ / 2 | FLIP_SEQ / 2 | game's | FLIP_DISCARD / 3 | FLIP_SEQ / 2 | FLIP_DISCARD / N+1 |
| Present | (1,0) serial | (1,0) serial | game's | (0,0) | (1,0) or (0,0) | Present1(1) |
| Frame latency | default | default | n/a | default | default | SetMaximumFrameLatency(N) |
| Pacing | irsdk event ~60 Hz, always redraw | same + per-HUD 10–30 fps limiter | game fps, cached unless changed | 90 Hz timer, redraw only if dirty | dirty-only, sleep on MsgWait | every vsync, TIME_CRITICAL thread |
| Device sharing | per window | per window | game device | one | one | one |
| GPU priority | none (CPU HIGH class) | none (CPU HIGH class) | n/a | none (logs HAGS/MPO) | none | SetGPUThreadPriority(29\|0x40000000) |

## Takeaways for our AMS2 WPF overlay (inference [I], to be validated by measurement)

1. **Path choice.** Our measured bottleneck (PresentWithGDI, then GetRenderTargetData readback) only exists in WPF's hardware *layered* path. Every native overlay avoids it with `NOREDIRECTIONBITMAP` + DComp + `CreateSwapChainForComposition(PREMULTIPLIED)`. Our experimental Vortice DComp path is the same architecture as iRon, iFL03 and fps_monitor.
2. **For the DComp path, copy OKB, not iRon:**
   - `Present(0,0)` (or `DXGI_PRESENT_DO_NOT_WAIT` and drop the frame on `DXGI_ERROR_WAS_STILL_DRAWING`);
   - 3 buffers / FLIP_DISCARD;
   - never call `Present(1,0)` in sequence across 10–14 windows on one thread;
   - render only when shared-memory data actually changed (fps_monitor/OKB);
   - cap per-HUD rate (iFL03: 10–15 fps for timing tables);
   - use one D3D11/D2D device for all HUDs instead of one per window (iRon/iFL03 create one per window).
3. **Click-through with DComp:** use fps_monitor's `WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_NOREDIRECTIONBITMAP|WS_EX_NOACTIVATE` + `SetLayeredWindowAttributes(255, LWA_ALPHA)`. The content still comes from DComp, not UpdateLayeredWindow.
4. **GPU priority** is the only code-level lever any of these use against a GPU-bound game:
   - `IDXGIDevice::SetGPUThreadPriority` (no admin; documented −7..7);
   - `D3DKMTSetProcessSchedulingPriorityClass` (REALTIME needs admin; OBS uses HIGH under HAGS).

   ShaderBeam's README shows that even this does not guarantee frames on a single GPU. No HUD overlay I read uses it.
5. **Fewer windows.** OKB's v2 plan is one fullscreen DComp window per monitor (MPO/VRR-friendly). For us that would mean one per-monitor DComp surface with all HUDs as visuals, instead of 10–14 separate top-level windows. It is a bigger change, and it is the direction OKB's maintainer chose.
6. **Our SoftwareOnly + UpdateLayeredWindow default** puts no draw work on the GPU queue from our process. Only DWM's upload and composition remain, and DWM has elevated GPU priority. None of the native projects use this path, so I have no comparison data from them. Measure p95 frame-to-screen under the same AMS2 load before concluding.
