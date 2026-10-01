# Windows presentation mechanics for a transparent always-on-top overlay over a GPU-saturated borderless game

Research date: 2026-10-01. Scope: C#/.NET 8 WPF overlay app, about 10-14 transparent topmost click-through windows (WS_EX_LAYERED + AllowsTransparency=true), and a fullscreen-borderless game running at about 100% GPU.

Legend:
- **[VERIFIED]** means I read it in source code or official documentation. The citation is given.
- **[INFERENCE]** means it is my reasoning from the verified facts, or general knowledge I could not confirm against a primary source during this session.

Source snapshots:
- WPF: `dotnet/wpf` @ `a4f9f07f71c0a612295d4af1c75db48d954b30c9` (2026-09-30). Paths are relative to `src/Microsoft.DotNet.Wpf/src/`. URL form: `https://github.com/dotnet/wpf/blob/a4f9f07f71c0a612295d4af1c75db48d954b30c9/src/Microsoft.DotNet.Wpf/src/<path>#L<n>`.
- Microsoft Docs: learn.microsoft.com was blocked by the sandbox egress proxy. I read the same pages from their GitHub source repos instead:
  - `MicrosoftDocs/win32` @ e103fa4
  - `MicrosoftDocs/sdk-api` @ c12073e
  - `MicrosoftDocs/windows-driver-docs` @ 6de78e0
  - `MicrosoftDocs/windows-driver-docs-ddi` @ 7515063
  
  Each citation gives the canonical learn.microsoft.com URL.
- OBS Studio @ `a8b04ac5e15115591d8a184701e1670fb5dc00af`, PresentMon @ `00db2caba0efb4fa5f6c847fced2d8189edc87e4`, LookingGlass `master` (2026-10-01).
- devblogs.microsoft.com, obsproject.com and lkml mirrors were also blocked. For those I only have search-engine summaries, and they are marked as such.

---

## 0. Executive summary

1. **[VERIFIED] WPF hardware path with AllowsTransparency.** It renders with D3D9(Ex). It does not call D3D Present. It calls `PresentWithGDI`, which calls `CD3DSwapChainWithSwDC::GetDC`, which calls `CD3DSurface::ReadIntoSysMemBuffer`, which calls `IDirect3DDevice9::GetRenderTargetData` (plus a `StretchRect` into a temporary RT for sub-rects). The DIB is then handed to `UpdateLayeredWindowIndirect` straight away.
   - **[INFERENCE, strongly supported by the code]** The readback is a synchronous CPU wait on the GPU. WPF passes the DIB to ULWI right after `GetRenderTargetData` returns, with no `LockRect` on the zero-copy path. So the copy must be finished when the call returns.
   - Under a saturated GPU, that wait includes queueing behind the game's work, because WPF's D3D context runs at normal GPU priority and is not foreground.
2. **[VERIFIED] One render thread serves every WPF window in the process.** `NUM_WORKER_THREADS 1`, created at `THREAD_PRIORITY_NORMAL`. `CRenderTargetManager::Present` loops over all HWND targets serially, so one window's stalled readback delays every other window's present on that frame. The UI-thread frame loop is also gated: it waits for a "Presented" notification before committing the next frame, and the render thread paces with `DwmFlush()`.
3. **[VERIFIED] SoftwareOnly removes the WPF-side GPU work.** The rasterizer writes into a DIB section and calls `UpdateLayeredWindowIndirect` with a dirty rect. No D3D device is created for that render target.
   - **[INFERENCE]** The GPU work left is DWM composing your layered surfaces. DWM work is documented as "high-priority GPU work".
   - `DwmFlush()` pacing still applies, so DWM latency under load still shows up in your frame loop.
4. **[VERIFIED] DComp + `CreateSwapChainForComposition` + `WS_EX_NOREDIRECTIONBITMAP` removes the GDI redirection/readback path entirely.** Your process still submits GPU work at normal priority and still competes with the game.
   - Raising priority needs `D3DKMTSetProcessSchedulingPriorityClass` plus `IDXGIDevice::SetGPUThreadPriority`.
   - **[VERIFIED in engineering code]** OBS and LookingGlass both treat HIGH/REALTIME GPU priority as needing admin or a service. OBS uses HIGH (not REALTIME) when HAGS is on.
5. **[VERIFIED] Under HAGS, Windows has an explicit FOCUS (foreground) priority band.** A NORMAL-band process starved by the focus band gets a target GPU share that defaults to **10%** on desktop. This is the documented reason background overlay GPU work suffers under a foreground GPU-bound game.
   - **[INFERENCE]** A game frame cap opens idle gaps and removes most of the problem.
6. **[VERIFIED] Topmost windows over a flip-model borderless game.** DWM can "seamlessly transition back to composed mode", "reverse compose", or use MPO to keep independent flip. Without a free MPO plane, an overlay forces the game to *Composed: Flip* (PresentMon terminology), which adds latency and disables tearing/VRR in some setups.
7. **[INFERENCE] Recommended direction:**
   - Software rasterization (WPF SoftwareOnly, or D2D software/Skia CPU into a DIB + ULWI with prcDirty).
   - As few and as small layered windows as practical, updated only when content changes.
   - Or one DComp visual tree in one HWND, if you need GPU rendering, but measure it under load.

---

## 1. WPF internals (dotnet/wpf, wpfgfx)

### 1.1 How AllowsTransparency maps to a present path

- **[VERIFIED]** `HwndTarget` passes `MILWindowLayerType.ApplicationManagedLayer` when the window is layered and uses per-pixel opacity (the AllowsTransparency case).
  - `PresentationCore/System/Windows/InterOp/HwndTarget.cs#L2242`
- **[VERIFIED]** The native factory maps `ApplicationManagedLayer` to `MilRTInitialization::PresentUsingUpdateLayeredWindow`. `SystemManagedLayer` (SetLayeredWindowAttributes) maps to `PresentUsingBitBlt`. Otherwise it is `PresentUsingHal` (a real D3D Present).
  - `WpfGfx/core/api/api_factory.cpp#L531-L567`
  - The comment at L538-541 gives the order of precedence: composited first, then UpdateLayeredWindow, then RTL.

### 1.2 Hardware mode (default RenderMode) for layered windows: where the readback happens

1. **[VERIFIED]** `CD3DDeviceLevel1::Present`:
   - if `pMILDC->PresentWithHAL()`, it calls `PresentWithD3D` (D3D9 `Present`);
   - otherwise it calls **`PresentWithGDI`**.
   - `WpfGfx/core/hw/d3ddevice.cpp#L3234-L3254`
2. **[VERIFIED]** `CD3DDeviceLevel1::PresentWithGDI` (`d3ddevice.cpp#L3503-L3651`):
   - L3583 calls `pD3DSwapChain->GetDC(0, rcSource, &hdcBackBuffer)`;
   - for `PresentUsingUpdateLayeredWindow`, L3598 calls `UpdateLayeredWindowEx(hWnd, NULL, pos, size, hdcBackBuffer, ..., blend, flags, prcSource)`.
   - The function header comment ("should only be used with rendertargets that have specified a right to left layout") is stale. Layered windows take this path too.
3. **[VERIFIED]** For non-HAL presents the swap chain is a `CD3DSwapChainWithSwDC`.
   - `WpfGfx/core/hw/d3dswapchain.cpp#L30-L90`: "If a present context is supplied, the swap chain will implement GetDC by copying the backbuffer to a software GDI DIB section."
   - `WpfGfx/core/hw/d3dswapchainwithswdc.cpp#L13-L15`: "implement GetDC using GetRenderTargetData. This approach achieved phenomenal perf wins in WDDM."
   - `CD3DSwapChainWithSwDC::GetDC` (`d3dswapchainwithswdc.cpp#L155-L189`) calls `m_rgBackBuffers[i]->ReadIntoSysMemBuffer(rcDirty, ...)` at **L175** and then returns `m_hdcCopiedBackBuffer` (a DIB-section DC).
4. **[VERIFIED]** `CD3DSurface::ReadIntoSysMemBuffer` (`WpfGfx/core/hw/d3dsurface.cpp#L278-L440`):
   - On WDDM with matching stride and no clip rects, `CreateSysMemUpdateSurface` wraps the output DIB memory directly (zero-copy) and `fNeedToManuallyCopyBits=false`. See L346-L378.
   - If the dirty rect is smaller than the surface, it **creates a new temporary render target each call** (`CreateRenderTargetUntracked`, L393) and `StretchRect`s into it. See L380-L410.
   - It then calls **`Device().GetRenderTargetData(...)`** at **L412**, which goes to `IDirect3DDevice9::GetRenderTargetData` (`d3ddevice.cpp#L1944-L1951`).
5. **Is it a synchronous GPU wait?**
   - **[VERIFIED]** The docs only say it "Copies the render-target data from device memory to system memory". There is no async/Map semantics.
     - https://learn.microsoft.com/windows/win32/api/d3d9/nf-d3d9-idirect3ddevice9-getrendertargetdata
   - **[INFERENCE, high confidence]** It is synchronous. On the zero-copy path WPF never locks the system-memory surface. It hands `m_hdcCopiedBackBuffer` to `UpdateLayeredWindowIndirect` as soon as `ReadIntoSysMemBuffer` returns. That is only correct if the copy has finished, so the D3D9 runtime/driver must block the calling thread (the render thread) until:
     - (a) every queued draw into the back buffer has executed on the GPU, and
     - (b) the GPU-to-system-memory copy has executed.
   
     Under a saturated GPU, (a) and (b) are scheduled behind or between the game's DMA packets (see §4). That matches the ~97 ms p95.
6. **[VERIFIED] WPF uses D3D9Ex where available.**
   - `WpfGfx/core/common/d3dloader.cpp` loads `Direct3DCreate9Ex` (L329) with a fallback to `Direct3DCreate9`.
   - `WpfGfx/core/hw/d3ddevicemanager.cpp#L1541-L1615` QIs `IDirect3D9Ex` and calls `CreateDeviceEx`.
   - Swap chains use `D3DSWAPEFFECT_DISCARD`/`COPY` (blt model, `d3ddevicemanager.cpp#L1459-L1463`), not FLIPEX.
   - **[VERIFIED]** Devices are shared: "Try to find an existing device" via `GetAvailableDevice` (`d3ddevicemanager.cpp#L1011-L1016`). All windows on one adapter share one D3D9Ex device/context.
   - **[VERIFIED]** WPF never calls `SetGPUThreadPriority`. The only hits are in `hw/shaders/ShaderGen` stubs and `av/d3ddevicewrapper.h`. So the WPF D3D context is at default (normal, 0) GPU priority.
7. **[VERIFIED]** The non-layered path (your "DWM glass" variant: no WS_EX_LAYERED + `DwmExtendFrameIntoClientArea`) uses `PresentWithD3D`, a D3D9 blt-model Present into the DWM redirection surface. There is no CPU readback, which matches its better p95 (~55 ms).
   - **[INFERENCE]** It is still a normal-priority GPU copy queued behind the game, plus DWM composition.

### 1.3 Threading: one render thread for all windows?

- **[VERIFIED]** `CPartitionManager` is a singleton ("There is only one instance of CPartitionManager").
  - `WpfGfx/core/uce/partitionmanager.h#L95-L110`
  - `#define NUM_WORKER_THREADS 1` at `partitionmanager.h#L38`, asserted at `partitionmanager.cpp#L1041`.
- **[VERIFIED]** Its priority comes from managed code as `0 // THREAD_PRIORITY_NORMAL` (`PresentationCore/System/Windows/Media/MediaSystem.cs#L54-L56`) and is applied with `SetThreadPriority` (`WpfGfx/core/uce/partitionthread.cpp#L101-L113`).
- **[VERIFIED]** `CRenderTargetManager::Present` (`WpfGfx/core/uce/rendertargetmanager.cpp#L588-L656`) does three things in order:
  1. optional `WaitForGPU()`, which is XPDM-only (disabled on WDDM per `IsGPUThrottlingEnabled`, L240-L265);
  2. `WaitToPresent(...)`;
  3. `for (i < cTargets) pTarget->Present()` (L637-L650). This presents **every HWND target serially on the same thread**.
- **[INFERENCE, direct consequence]**
  - **Yes: one window's blocked present (a GetRenderTargetData stall) blocks all other windows' presents** in that composition pass, even if they are on different Dispatcher threads, because they all share this render thread.
  - With 10-14 layered windows in hardware mode, each changed window costs its own synchronous readback. The total stall per frame is roughly the sum of the readbacks, and each one can wait for a GPU time slice.

### 1.4 Frame pacing (DWM vsync, CompositionTarget.Rendering)

- **[VERIFIED]** `WaitToPresent` calls `WaitForDwm` when there are VBlank-sync listeners and composition is on (`rendertargetmanager.cpp#L670-L715`).
  - `WaitForDwm` calls **`DwmFlush()`** ("Wait until VBlank and until all of our current Dx updates are complete", L1164-L1171).
  - It then calls `DwmGetCompositionTimingInfo` to get `qpcVBlank` and the refresh rate (L1196-L1216).
- **[VERIFIED]** The DwmFlush docs say it "blocks the caller until the next call to a Present method, when all of the Microsoft DirectX surface updates that are currently outstanding have been made".
  - https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmflush
- **[VERIFIED]** UI side, in `MediaContext`:
  - When the "interlock" is enabled, `CommitChannel` requests a Presented notification and enters `WaitingForResponse` (`PresentationCore/System/Windows/Media/MediaContext.cs#L2109-L2147`).
  - Enabling it sends `MilCmdPartitionSetVBlankSyncMode` (L1255-L1277).
  - The `InterlockState` docs are at L2749-L2791.
  - So the UI thread (and `CompositionTarget.Rendering`, layout, animations) produces **at most one frame per render-thread present**, and that present is paced by DwmFlush.
- **[INFERENCE]** Pacing is roughly "DWM frame rate", with no ability to get ahead.
  - If DWM composition or our present is delayed under GPU load, `CompositionTarget.Rendering` cadence drops for every window on that Dispatcher.
  - This applies in **both** hardware and software modes. DwmFlush is used regardless of RT type.

### 1.5 RenderOptions.ProcessRenderMode semantics

- **[VERIFIED]** `RenderOptions.ProcessRenderMode` (`PresentationCore/System/Windows/Media/RenderOptions.cs#L222-L251`):
  - It accepts only `Default` or `SoftwareOnly`.
  - It calls native `RenderOptions_ForceSoftwareRenderingModeForProcess` (`WpfGfx/core/common/renderoptions.cpp#L15`).
  - Docs comment: "specifies a preference … can be trumped by the registry settings".
- **[VERIFIED]** Per-window `HwndTarget.RenderMode` also exists (`HwndTarget.cs#L643-L663`, `InvalidateRenderMode` L576-L590).
- **[VERIFIED]** In the native desktop RT, when `SoftwareOnly` is set, the hardware branch (`if (!(dwFlags & SoftwareOnly))`) is skipped and a `CSwRenderTargetHWND` is created.
  - `WpfGfx/core/meta/desktoprt.cpp#L313`, `#L349-L360`
  - `desktophwndrt.cpp#L331-L355`
  - WPF also forces software when D3D is unavailable or a non-local (RDP) display is present (`desktoprt.cpp#L92-L105`).
- **[INFERENCE]** Set it before the first window is shown. It affects render targets created afterwards. Existing HwndTargets get re-created through `InvalidateRenderMode` only if you set the per-window `RenderMode`.

---

## 2. SoftwareOnly + layered windows

### 2.1 Is there still GPU work in our process?

- **[VERIFIED]** `CSwPresenter32bppGDI` renders into a `CreateDIBSection` buffer (`WpfGfx/core/sw/swlib/swpresentgdi.cpp#L1077-L1110`).
- **[VERIFIED]** For `ApplicationManagedLayer`, `Present` calls `UpdateLayeredWindowEx(hwnd, hdcFront(NULL), pos, size, m_hdcBack, ..., blend, flags, prcSource)` (`swpresentgdi.cpp#L676-L800`, call at **L775**).
- **[VERIFIED]** `UpdateLayeredWindowEx` (`WpfGfx/core/common/oscompat.cpp#L108-L190`) prefers `UpdateLayeredWindowIndirect` with `ulwi.prcDirty = prcDirty`. The dirty rect is passed through, so **partial updates are supported**. It falls back to `UpdateLayeredWindow` (full update) only if ULWI is missing.
- **[VERIFIED]** No D3D device is created for a SoftwareOnly RT (§1.5).
- **[INFERENCE]** WPF may still load d3d9 / create an `IDirect3D9Ex` object for display enumeration (`display.cpp#L989`). That is not GPU work. Your process's GPU work goes to about zero.
- **[INFERENCE, needs ETW to confirm on your machine]** What remains:
  - (a) inside `UpdateLayeredWindowIndirect`, win32k copies the dirty rect from your DIB into the window's DWM-owned redirection surface. This is a CPU copy or a GDI-accelerated/aperture copy. The GDI hardware acceleration feature was added in Windows 7: https://learn.microsoft.com/windows-hardware/drivers/display/gdi-hardware-acceleration
  - (b) DWM composes that surface onto the desktop on its own D3D device, which is GPU work done **in dwm.exe**, not in your process.
  - Use GPUView, or PresentMon/WPA with the DxgKrnl provider, to see where the copy lands.

### 2.2 Cost scaling with window pixel area

- **[VERIFIED]** The `UpdateLayeredWindow` docs:
  - "UpdateLayeredWindow always updates the entire window. To update part of a window, use the traditional WM_PAINT and set the blend value using SetLayeredWindowAttributes."
  - "For best drawing performance by the layered window and any underlying windows, the layered window should be as small as possible."
  - https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-updatelayeredwindow
- **[VERIFIED]** `UPDATELAYEREDWINDOWINFO.prcDirty`: "If it is non-NULL, only the area in this rectangle is updated from the source DC."
  - https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-updatelayeredwindowinfo
- **[INFERENCE]** Approximate per-frame CPU cost for a SoftwareOnly WPF layered window:
  - WPF software rasterization of the dirty region (scales with dirty area × scene complexity; single render thread);
  - plus a ULWI copy of the **dirty bounding rect** (4 B/px).
  
  Example: a 600×200 HUD is 480 KB per full update, which is about 29 MB/s at 60 Hz. That is negligible. A full 2560×1440 transparent window is 14.7 MB per update, about 885 MB/s at 60 Hz, before rasterization. That is where software mode would start to hurt.
- **[INFERENCE]** WPF passes a single bounding rect (`prcSource`). Two small changes at opposite corners of a large window therefore copy the whole span. Separate small windows avoid that.

### 2.3 Does DWM composition of layered windows need GPU, and at what priority?

- **[VERIFIED]** The composition engine lives in dwm.exe:
  - "A single composition process, dwm.exe, supports every application in a session".
  - "produces one frame for each vertical blank".
  - It skips work for fully occluded windows.
  - It "uses a single Direct3D device" per adapter for composition.
  - https://learn.microsoft.com/windows/win32/directcomp/architecture-and-components
- **[VERIFIED]** On priority, the WDK GPU preemption doc says: "If the OS can't successfully preempt long-running packets, then: High-priority GPU work (such as work required by the Desktop Window Manager (DWM)) can be delayed."
  - https://learn.microsoft.com/windows-hardware/drivers/display/gpu-preemption
  - This is the official statement that DWM's GPU work is high-priority.
- **[VERIFIED]** GPU priority bands exist: `DXGK_SCHEDULING_PRIORITY_BAND_{IDLE, NORMAL, FOCUS, REALTIME}`.
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmddi/ne-d3dkmddi-_dxgk_scheduling_priority_band
  - `DXGKARG_SETUPPRIORITYBANDS.gracePeriodForBand`: "For realtime band this will be typically set to 0, because realtime processes need to use the GPU right away."
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmddi/ns-d3dkmddi-_dxgkarg_setupprioritybands
- **[INFERENCE]** DWM is in the REALTIME band (or uses high absolute context priority), above the game's FOCUS band. I could not find a public Microsoft doc that names dwm.exe's exact band. Practical consequences:
  - DWM composing layered windows usually preempts the game at a preemption boundary. That is fine for the overlay.
  - DWM composition is not free for the game. Every frame with a changed overlay forces a composition pass (and, if the game was in independent flip, forces composed mode; see §5).
- **[INFERENCE from search-result summary, not a primary source]** `DwmEnableMMCSS` raises DWM *CPU* thread priority into the MMCSS realtime range. It is deprecated-ish and not needed for the GPU side.

---

## 3. DirectComposition + composition swap chain + WS_EX_NOREDIRECTIONBITMAP

### 3.1 How it avoids redirection surfaces

- **[VERIFIED]** `WS_EX_NOREDIRECTIONBITMAP`: "The window does not render to a redirection surface. This is for windows that do not have visible content or that use mechanisms other than surfaces to provide their visual."
  - https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles
- **[VERIFIED]** `CreateSwapChainForComposition` creates a swap chain that is bound to a DComp visual via `IDCompositionVisual::SetContent`. HWND-specific methods (`GetHwnd`, `SetFullscreenState`, …) fail on it.
  - https://learn.microsoft.com/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgifactory2-createswapchainforcomposition
- **[VERIFIED]** DComp batches are applied by dwm.exe at the next vblank frame ("Batches are placed in a pending queue when the application calls Commit, and the pending queue is flushed atomically at the beginning of the frame").
  - https://learn.microsoft.com/windows/win32/directcomp/architecture-and-components
- **[INFERENCE]** No GDI redirection surface exists. No ULW copy and no readback happen. Your swap-chain buffer *is* the surface DWM samples (flip model: "sharing it directly with the desktop compositor, with minimal copies").
  - https://learn.microsoft.com/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model
  - Premultiplied alpha (`DXGI_ALPHA_MODE_PREMULTIPLIED`, B8G8R8A8) is what DWM blends.
- **[INFERENCE]** Click-through still works with `WS_EX_TRANSPARENT` (+ `WS_EX_LAYERED` if you need hit-test transparency on older behaviour). A layered style on a NOREDIRECTIONBITMAP window does not bring back ULW cost as long as you never call ULW or SetLayeredWindowAttributes. Validate on your target Windows build.

### 3.2 Frame latency waitable object and Present(0/1)

- **[VERIFIED]** `GetFrameLatencyWaitableObject` "Returns a waitable handle that signals when the DXGI adapter has finished presenting a new frame". It requires `DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT`.
  - https://learn.microsoft.com/windows/win32/api/dxgi1_3/nf-dxgi1_3-idxgiswapchain2-getframelatencywaitableobject
- **[VERIFIED]** Default max frame latency is 3 (range 1-16). See `IDXGIDevice1::SetMaximumFrameLatency`.
  - https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-idxgidevice1-setmaximumframelatency
- **[VERIFIED]** Flip model `Present` `SyncInterval=0`: "Cancel the remaining time on the previously presented frame and discard this frame if a newer frame is queued". `SyncInterval=1..4` synchronises after the Nth vblank.
  - https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-present
- **[INFERENCE]** For an overlay:
  - set `SetMaximumFrameLatency(1)` on the swap chain;
  - wait on the waitable object (with a timeout) **before** rendering;
  - render;
  - call `Present(1,0)`, or `Present(0,0)` if you only care about latest-wins.
  
  The thread then never blocks inside Present/Map behind a congested GPU queue. Instead it skips frames (latest data wins), which is what a HUD wants. Do not do CPU readbacks (`Map` on staging) in this path.

### 3.3 Does it still compete for the GPU with the game?

- **[INFERENCE, firm]** Yes. Your D3D11 context submits rendering, and DWM composes your visual.
  - Your context is a normal background process. Under HAGS it is in the NORMAL band versus the game's FOCUS band (see §4), so the render can still be delayed by tens of ms when the game saturates the GPU.
  - The difference from WPF hardware mode is that **no CPU thread blocks** on a GPU-to-CPU readback, and no other windows are serialised behind it.

### 3.4 Raising GPU priority

- **[VERIFIED]** `IDXGIDevice::SetGPUThreadPriority(INT)`:
  - **Relative mode** (bit 30 = 0): value −7..+7. 0 = normal (the default for all contexts), −7 = idle.
  - **Absolute mode** (bit 30 = `D3DKMT_SETCONTEXTSCHEDULINGPRIORITY_ABSOLUTE`, Windows 10+): bits[4:0] 0..31.
    - 0 = Idle;
    - 1 = Normal;
    - 2-15 = reserved;
    - **16-29 = Soft Realtime** ("Preempts lower priorities and periodically yields to lower priorities");
    - **30 = Hard Realtime** ("does not yield");
    - 31 = internal.
  - Warning in the doc: "If used inappropriately … can impede rendering speed."
  - https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-idxgidevice-setgputhreadpriority
- **[VERIFIED]** `D3DKMTSetProcessSchedulingPriorityClass(HANDLE, D3DKMT_SCHEDULINGPRIORITYCLASS)` takes classes IDLE, BELOW_NORMAL, NORMAL, ABOVE_NORMAL, HIGH, REALTIME (values 0-5). The docs list **no privilege requirement**.
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtsetprocessschedulingpriorityclass
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmthk/ne-d3dkmthk-_d3dkmt_schedulingpriorityclass
- **[VERIFIED in credible engineering code] OBS Studio** `libobs-d3d11/d3d11-subsystem.cpp#L436-L459`:
  - It calls `D3DKMTSetProcessSchedulingPriorityClass(GetCurrentProcess(), hags_enabled ? D3DKMT_SCHEDULINGPRIORITYCLASS_HIGH : D3DKMT_SCHEDULINGPRIORITYCLASS_REALTIME)` and then `SetGPUThreadPriority(GPU_PRIORITY_VAL)`.
  - On failure it logs "D3D11 GPU priority setup failed (not admin?)" (L655-L660).
  - It skips Intel GPUs (`VendorId != 0x8086`).
  - `GPU_PRIORITY_VAL` is injected from a CI **secret** (`CMakePresets.json#L163`, `.github/workflows/build-project.yaml#L410`). The exact absolute value OBS uses is not public.
  - OBS also raises `SetMaximumFrameLatency(16)` to "prevent stalls sometimes seen in Present calls" (L417-L433).
  - https://github.com/obsproject/obs-studio/blob/a8b04ac5e15115591d8a184701e1670fb5dc00af/libobs-d3d11/d3d11-subsystem.cpp#L436-L459
- **[VERIFIED in credible engineering code] LookingGlass host** `host/platform/Windows/src/platform.c` `boostPriority()`:
  - It calls `D3DKMTSetProcessSchedulingPriorityClass(..., REALTIME)`.
  - On failure: "Failed to set realtime GPU priority … To fix this, install and run the Looking Glass host as a service."
  - https://github.com/gnif/LookingGlass/blob/master/host/platform/Windows/src/platform.c
- **Privilege, [INFERENCE]**
  - Both projects observe that REALTIME (and in OBS's experience HIGH) fails for a normal, non-elevated user process. Classes ≤ NORMAL (and probably ABOVE_NORMAL) succeed unprivileged.
  - It is plausible that the kernel check mirrors CPU `REALTIME_PRIORITY_CLASS`, which requires `SeIncreaseBasePriorityPrivilege` (held by Administrators). I found **no Microsoft primary source** naming the exact privilege for the GPU class. Treat "HIGH/REALTIME need elevation" as empirical.
- **HAGS interaction**
  - **[VERIFIED in OBS code]** OBS deliberately uses HIGH instead of REALTIME when HAGS is enabled.
  - **[INFERENCE]** Under HAGS, REALTIME maps to the non-yielding realtime band and can starve the game/DWM, or misbehave. HIGH is safer.
  - **[VERIFIED via search-result summary of the DirectX blog, https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/]** Under HAGS, "Windows continues to control prioritization and decide which applications have priority among contexts", while quanta and context switching are offloaded to a GPU scheduling processor.
- **Recommendation, [INFERENCE]**
  - Do not ship an elevated overlay just for GPU priority.
  - Without elevation, `SetGPUThreadPriority(+7)` (relative) is the only lever, and its effect against a FOCUS-band game is limited (see §4).
  - Prefer removing GPU dependence (software raster) for latency-critical HUD text/numbers.

---

## 4. Windows GPU scheduling under a GPU-bound foreground game

- **[VERIFIED]** Priority bands: IDLE / NORMAL / **FOCUS** / REALTIME (`DXGK_SCHEDULING_PRIORITY_BAND`, link in §2.3).
- **[VERIFIED]** `DXGKARG_SETUPPRIORITYBANDS.targetNormalBandPercentage`: "For normal priority band, specifies the target GPU percentage in situations when it's starved by the focus band. Valid values are between 0 and 50, with the default value on desktop systems being 10." There are also per-band `gracePeriodForBand`, `processQuantumForBand` and `processGracePeriodForBand` in 100 ns units.
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmddi/ns-d3dkmddi-_dxgkarg_setupprioritybands
  - This is the HAGS-era DDI (`DxgkDdiSetupPriorityBands`, "after adapter startup and before scheduling the first GPU work item").
- **[INFERENCE]** The foreground (focus) process, which is the game, gets a foreground boost. Your overlay is never foreground (click-through, no activation), so it is in NORMAL. When the game saturates the GPU, NORMAL-band work only progresses at the target ~10% share, granted in quanta. A tiny overlay job (a readback, or a few draws) can therefore wait for the next NORMAL-band slice:
  - several to tens of ms, depending on the driver-chosen quantum and grace period;
  - plus the time for the game's in-flight packet to reach a preemption boundary.
  
  This is consistent with p95 ≈ 55-97 ms.
- **[VERIFIED]** Preemption granularity is per-driver:
  - `D3DKMDT_GRAPHICS_PREEMPTION_{NONE, DMA_BUFFER_BOUNDARY, PRIMITIVE_BOUNDARY, TRIANGLE_BOUNDARY, PIXEL_BOUNDARY, SHADER_BOUNDARY}`.
  - The driver reports the coarsest across engines.
  - The Windows 8+ model forbids drivers from disabling preemption, and recommends mid-DMA-buffer preemption.
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmdt/ne-d3dkmdt-_d3dkmdt_graphics_preemption_granularity
  - https://learn.microsoft.com/windows-hardware/drivers/display/gpu-preemption
  - **[INFERENCE]** Modern NVIDIA/AMD GPUs report pixel/shader-level graphics preemption, so preemption latency itself is sub-ms. The dominant delay is *policy* (band share/quantum), not preemption granularity.
- **[INFERENCE]** Queued game frames:
  - A GPU-bound game with max frame latency 2-3 keeps the hardware queue full.
  - Without HAGS (CPU-side VidSch), NORMAL-priority submissions from other processes are interleaved at quantum boundaries.
  - With HAGS the GPU-side scheduler does it, with the same band policy.
  - Either way, your work does not need to wait for *all* queued game frames, but it does wait for a slice.
- **[INFERENCE] Effect of a game frame cap** (in-game limiter, RTSS, NVIDIA/AMD limiter, or VRR cap below max):
  - The GPU stops being 100% busy and idle gaps appear every frame.
  - NORMAL-band work runs immediately in those gaps.
  - This is the single most effective mitigation, and it is under the user's control.
  - A recommendation to cap at a few fps below the achievable rate (or use the driver's low-latency mode) is reasonable user guidance for your league setup.

---

## 5. Independent flip / MPO with a topmost overlay over a borderless game

- **[VERIFIED]** From "For best performance, use DXGI flip model": once a flip-model swap chain is DirectFlipped, "Independent Flip" lets frames go to the screen "with the same efficiency as fullscreen exclusive". Then: "**If other desktop contents come on top, the DWM can either seamlessly transition back to composed mode, efficiently 'reverse compose' the contents on top of the application before flipping it, or leverage MPO to maintain the independent flip mode.**" `DXGI_SWAP_EFFECT_FLIP_DISCARD` enables the reverse-composition variant.
  - https://learn.microsoft.com/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model
- **[VERIFIED]** PresentMon PresentMode values:
  - `Hardware: Independent Flip`;
  - `Composed: Flip` (sharing surfaces with DWM to be composed);
  - `Hardware Composed: Independent Flip` (flip model, granted a hardware overlay plane);
  - `Composed: Copy with GPU GDI` / `CPU GDI`.
  - `README-ConsoleApplication.md#L135-L145`: https://github.com/GameTechDev/PresentMon/blob/00db2caba0efb4fa5f6c847fced2d8189edc87e4/README-ConsoleApplication.md
- **[VERIFIED]** MPO (WDDM 1.3+) lets display hardware compose planes without DWM GPU blending.
  - https://learn.microsoft.com/windows-hardware/drivers/display/multiplane-overlay-support
- **[Search-result summary of Blur Busters forum threads, not a primary source]** A window on top of a borderless game without MPO gives "Composed: Flip", which adds latency and can break G-Sync/VRR and tearing.
  - https://forums.blurbusters.com/viewtopic.php?t=14523
- **[INFERENCE]** Effect of your 10-14 topmost layered windows:
  - The game likely ends up in **Composed: Flip** most of the time. DWM must compose a full-screen frame every refresh in which anything changed, at least +1 frame of latency versus independent flip, with no tearing.
  - VRR may still work in composed mode on recent Windows/drivers, but behaviour varies.
  - Whether DWM can instead promote to MPO depends on how many overlay planes the GPU/display exposes (often 1-3). Many separate overlay windows make MPO assignment less likely.
  - Verify with `PresentMon --process_name <game>.exe` with overlays on and off.
- **[VERIFIED, from search-result summaries of the DirectX blog "Updates in Graphics and Gaming" and Neowin]** **"Optimizations for windowed games"** (Settings > System > Display > Graphics) upgrades DX10/DX11 *blt-model* games to flip model, which enables independent flip, Auto HDR and VRR in windowed/borderless.
  - https://devblogs.microsoft.com/directx/updates-in-graphics-and-gaming/
  - https://www.neowin.net/news/windows-11-insiders-now-have-optimizations-for-legacy-games-running-in-windowed-mode/
  - **[INFERENCE]** It does not change the overlay rule above. It only makes the game eligible for independent flip in the first place.
  - **[INFERENCE]** AMS2 is DX11. Whether it presents with flip or blt, and whether this toggle applies, should be measured with PresentMon.

---

## 6. Many HWNDs vs one big transparent HWND

| Aspect | Many small layered windows (current) | One large layered window | One HWND + DComp visual tree |
|---|---|---|---|
| Update cost (ULW) | Each changed window copies only its own area. Unchanged windows cost 0 **[INFERENCE]** | ULW copies the whole window. ULWI copies `prcDirty`, but WPF passes a single bounding rect, so scattered changes copy a big span **[VERIFIED for ULW/ULWI docs and WPF ULWI usage; cost INFERENCE]** | No copy. DComp swap chain or surface per element, sized to content **[INFERENCE]** |
| DWM composition | One quad per visible window per composed frame. Occlusion culling per window **[VERIFIED: DWM skips fully occluded windows; per-window cost INFERENCE]** | One large alpha-blended quad covering the screen. Fill cost ∝ full area even where transparent **[INFERENCE]** | Visuals sized to content. One HWND, many visuals, one Commit **[VERIFIED: DComp batches/Commit; cost INFERENCE]** |
| MPO / independent flip eligibility for the game | Several topmost rects. Harder to fit in limited MPO planes **[INFERENCE]** | One full-screen topmost layer always forces composition **[INFERENCE]** | Same as one HWND, but small visuals **[INFERENCE]** |
| WPF render thread | Serial presents. In HW mode one readback per changed window per frame **[VERIFIED serial loop §1.3]** | One present, but large raster/readback **[INFERENCE]** | n/a (not WPF) |
| Hit-test / click-through | Per window `WS_EX_TRANSPARENT` | Same | Same |

- **[VERIFIED]** ULW docs: "the layered window should be as small as possible". `prcDirty` limits ULWI to a rect (links in §2.2).
- **[INFERENCE] Recommendation:**
  - With WPF SoftwareOnly, keep separate windows sized tightly to each HUD, and avoid any full-screen transparent window.
  - Only re-render a window when its data changes. A HUD updating at 10-20 Hz for text costs far less than 60 Hz animation.
  - If the merged count could fall to a few windows, merging adjacent HUDs is fine. Do not merge into one full-screen window.

---

## 7. Software raster alternatives: Direct2D software / WARP, Skia

- **[VERIFIED]** `D2D1_RENDER_TARGET_TYPE_SOFTWARE`: "The render target uses software rendering only."
  - https://learn.microsoft.com/windows/win32/api/d2d1/ne-d2d1-d2d1_render_target_type
- **[VERIFIED]** `ID2D1DCRenderTarget` behaviour:
  - It "renders Direct2D content to an internal bitmap, and then renders the bitmap to the DC with GDI".
  - It must be `DXGI_FORMAT_B8G8R8A8_UNORM` + `D2D1_ALPHA_MODE_PREMULTIPLIED` (or IGNORE).
  - `BindDC` must be called per DC/size change.
  - https://learn.microsoft.com/windows/win32/api/d2d1/nn-d2d1-id2d1dcrendertarget
  - https://learn.microsoft.com/windows/win32/Direct2D/supported-pixel-formats-and-alpha-modes
- **[INFERENCE]** With the DEFAULT/HARDWARE type, a DC render target would perform a GPU-to-CPU readback, the same class of problem as WPF HW layered. With **SOFTWARE** there is no GPU work.
- **[INFERENCE]** A better alternative to a DC RT:
  - Create a 32bpp top-down premultiplied `CreateDIBSection`.
  - Wrap its bits in a WIC bitmap and use `ID2D1Factory::CreateWicBitmapRenderTarget` with type SOFTWARE (or draw with a WARP D3D11 device + D2D device context, then `Map`).
  - Then call `UpdateLayeredWindowIndirect` with `prcDirty` = changed rect and `ULW_ALPHA` + `AC_SRC_ALPHA` blend.
- **[VERIFIED]** WARP is the Direct3D software rasterizer ("a single, general purpose software rasterizer").
  - https://learn.microsoft.com/windows/win32/direct3darticles/directx-warp
  - **[INFERENCE]** WARP is multithreaded and JIT-compiles shaders. For simple 2D HUDs the plain D2D software RT is lighter.
- **[VERIFIED]** SkiaSharp exposes `SKSurface.Create(SKImageInfo info, IntPtr pixels, int rowBytes)` (`binding/SkiaSharp/SKSurface.cs` in mono/SkiaSharp). You can rasterize straight into a DIB section's bits using `SKColorType.Bgra8888` + `SKAlphaType.Premul` and then ULWI.
  - **[INFERENCE]** Skia's CPU raster backend is mature and fast for text, paths and AA, and it gives you full control over threading. You can rasterize HUDs in parallel on worker threads, unlike WPF's single render thread.
- **[INFERENCE] Comparison with WPF SoftwareOnly:**
  - WPF SW keeps your XAML. It costs the single shared render thread, DwmFlush-gated pacing, and WPF retained-mode overhead (layout, visual tree, dirty-region tracking). It already uses ULWI with a dirty rect.
  - D2D-SW / Skia + manual ULWI gives explicit control over update timing (you can present immediately when telemetry arrives, not on the WPF/DWM cadence), per-window threads, and exact dirty rects.
  - Expected delivery-latency floor is then about your rasterization time + ULWI copy + the next DWM composition (~1 refresh), mostly independent of the game's GPU load except for DWM's own (high-priority) composition.

---

## 8. What to measure next (suggested, [INFERENCE])

1. **PresentMon on the game** with overlays off, then on with SoftwareOnly, then on with DComp. Check whether `PresentMode` changes from `Hardware: Independent Flip` to `Composed: Flip`, and how `MsUntilDisplayed` / display latency changes.
2. **GPUView / WPA (DxgKrnl)** to see where overlay delays come from: your process's NORMAL-band packets and DWM's packets during load. Confirm that the remaining SoftwareOnly delay is DWM composition and not a ULWI copy.
3. **Instrument the WPF render-thread loop.** Time from data arrival to `CompositionTarget.Rendering` to the next Rendering. This shows DwmFlush gating directly.
4. **A/B test a game frame cap** (e.g. cap 5-10% below the uncapped average) and HAGS on/off. Expect large p95 improvements for any GPU-path overlay.

---

## Source index

WPF (dotnet/wpf @ a4f9f07). Prefix: `https://github.com/dotnet/wpf/blob/a4f9f07f71c0a612295d4af1c75db48d954b30c9/src/Microsoft.DotNet.Wpf/src/`

| File | Lines | What it shows |
|---|---|---|
| `WpfGfx/core/hw/d3ddevice.cpp` | L3234-3254 | HAL vs GDI present |
| `WpfGfx/core/hw/d3ddevice.cpp` | L3503-3651 | `PresentWithGDI`; GetDC at L3583, ULW at L3598 |
| `WpfGfx/core/hw/d3ddevice.cpp` | L1944-1951 | `GetRenderTargetData` |
| `WpfGfx/core/hw/d3dswapchain.cpp` | L30-90 | SwDC selection |
| `WpfGfx/core/hw/d3dswapchainwithswdc.cpp` | L13-15, L155-189 | GetDC calls ReadIntoSysMemBuffer at L175 |
| `WpfGfx/core/hw/d3dsurface.cpp` | L278-440 | ReadIntoSysMemBuffer: L372 CreateSysMemUpdateSurface, L393 temp RT, L412 GetRenderTargetData |
| `WpfGfx/core/hw/d3ddevicemanager.cpp` | L1011-1016 | Shared device |
| `WpfGfx/core/hw/d3ddevicemanager.cpp` | L1459-1463 | Swap effects |
| `WpfGfx/core/hw/d3ddevicemanager.cpp` | L1541-1615 | CreateDeviceEx |
| `WpfGfx/core/common/d3dloader.cpp` | L329 | Direct3DCreate9Ex |
| `WpfGfx/core/hw/hwdisplayrt.cpp` | L360-455 | Present / dirty rect |
| `WpfGfx/core/api/api_factory.cpp` | L531-567 | Layered present path selection |
| `WpfGfx/core/common/oscompat.cpp` | L108-190 | ULWI with prcDirty, ULW fallback |
| `WpfGfx/core/sw/swlib/swpresentgdi.cpp` | L676-800, L775, L1077-1110 | SW present via ULWI, DIB section |
| `WpfGfx/core/meta/desktoprt.cpp` | L92-105, L236-262, L313, L349-360 | SW/HW RT selection |
| `WpfGfx/core/uce/partitionmanager.h` | L38, L95-110 | Single worker thread |
| `WpfGfx/core/uce/partitionthread.cpp` | L101-113 | Thread creation and priority |
| `WpfGfx/core/uce/rendertargetmanager.cpp` | L240-265 | GPU throttling XPDM only |
| `WpfGfx/core/uce/rendertargetmanager.cpp` | L588-656 | Serial present loop |
| `WpfGfx/core/uce/rendertargetmanager.cpp` | L670-715, L1153-1216 | WaitForDwm, DwmFlush |
| `PresentationCore/System/Windows/Media/MediaSystem.cs` | L54-56 | THREAD_PRIORITY_NORMAL |
| `PresentationCore/System/Windows/Media/MediaContext.cs` | L1255-1277, L2109-2147, L2749-2791 | Interlocked presentation |
| `PresentationCore/System/Windows/Media/RenderOptions.cs` | L222-251 | ProcessRenderMode |
| `PresentationCore/System/Windows/InterOp/HwndTarget.cs` | L576-590, L643-663, L2242 | RenderMode, layer type |

Microsoft docs (read via the MicrosoftDocs GitHub repos):

- Win32 / DXGI / D2D / DComp:
  - https://learn.microsoft.com/windows/win32/api/d3d9/nf-d3d9-idirect3ddevice9-getrendertargetdata
  - https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmflush
  - https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-updatelayeredwindow
  - https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-updatelayeredwindowinfo
  - https://learn.microsoft.com/windows/win32/winmsg/window-features#layered-windows
  - https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles
  - https://learn.microsoft.com/windows/win32/directcomp/architecture-and-components
  - https://learn.microsoft.com/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgifactory2-createswapchainforcomposition
  - https://learn.microsoft.com/windows/win32/api/dxgi1_3/nf-dxgi1_3-idxgiswapchain2-getframelatencywaitableobject
  - https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-idxgidevice1-setmaximumframelatency
  - https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-present
  - https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-idxgidevice-setgputhreadpriority
  - https://learn.microsoft.com/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model
  - https://learn.microsoft.com/windows/win32/api/d2d1/ne-d2d1-d2d1_render_target_type
  - https://learn.microsoft.com/windows/win32/api/d2d1/nn-d2d1-id2d1dcrendertarget
  - https://learn.microsoft.com/windows/win32/Direct2D/supported-pixel-formats-and-alpha-modes
  - https://learn.microsoft.com/windows/win32/direct3darticles/directx-warp
- WDK:
  - https://learn.microsoft.com/windows-hardware/drivers/display/gpu-preemption
  - https://learn.microsoft.com/windows-hardware/drivers/display/gdi-hardware-acceleration
  - https://learn.microsoft.com/windows-hardware/drivers/display/multiplane-overlay-support
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmddi/ne-d3dkmddi-_dxgk_scheduling_priority_band
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmddi/ns-d3dkmddi-_dxgkarg_setupprioritybands
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmddi/nc-d3dkmddi-dxgkddi_setupprioritybands
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtsetprocessschedulingpriorityclass
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmthk/ne-d3dkmthk-_d3dkmt_schedulingpriorityclass
  - https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmdt/ne-d3dkmdt-_d3dkmdt_graphics_preemption_granularity

Engineering code / tools:

- OBS: https://github.com/obsproject/obs-studio/blob/a8b04ac5e15115591d8a184701e1670fb5dc00af/libobs-d3d11/d3d11-subsystem.cpp (L417-459, L640-661)
- LookingGlass: https://github.com/gnif/LookingGlass/blob/master/host/platform/Windows/src/platform.c (`boostPriority`, about L541-558)
- PresentMon: https://github.com/GameTechDev/PresentMon/blob/00db2caba0efb4fa5f6c847fced2d8189edc87e4/README-ConsoleApplication.md (L135-145)
- SkiaSharp: https://github.com/mono/SkiaSharp/blob/main/binding/SkiaSharp/SKSurface.cs

Search-summary-only (pages blocked; not read directly):

- https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/
- https://devblogs.microsoft.com/directx/updates-in-graphics-and-gaming/
- https://www.neowin.net/news/windows-11-insiders-now-have-optimizations-for-legacy-games-running-in-windowed-mode/
- https://forums.blurbusters.com/viewtopic.php?t=14523
